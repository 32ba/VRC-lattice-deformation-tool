#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using nadena.dev.ndmf;
using nadena.dev.ndmf.preview;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class MeshDeformerValidatorTests
    {
        [TearDown]
        public void TearDown()
        {
            SkinnedVertexHelper.StoreMovesInRestSpace = false;
            Undo.ClearAll();
        }

        [Test]
        public void HealthyDeformer_HasNoErrors_AndBakeIsAllowed()
        {
            using var fixture = CreateFixture("Healthy");

            var diagnostics = MeshDeformerValidator.Validate(fixture.Deformer);

            Assert.That(diagnostics.Any(d => d.Severity == MeshDeformerDiagnosticSeverity.Error), Is.False);
            Assert.That(LatticeDeformerBakePass.ValidateBeforeBake(fixture.Deformer), Is.True);
        }

        [Test]
        public void NonReadableSourceMesh_TopologyValidationDoesNotUseReadableOnlyApis()
        {
            var gameObject = new GameObject("Non-readable Source");
            var mesh = CreateMesh("Non-readable Mesh");
            mesh.UploadMeshData(true);
            try
            {
                gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                gameObject.AddComponent<MeshRenderer>();
                var deformer = gameObject.AddComponent<LatticeDeformer>();

                Assert.DoesNotThrow(deformer.Reset);
                IReadOnlyList<MeshDeformerDiagnostic> diagnostics = null;
                Assert.DoesNotThrow(() => diagnostics = MeshDeformerValidator.Validate(deformer));
                Assert.That(diagnostics.Any(d => d.Code == MeshDeformerValidator.SourceMeshChanged),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void InspectorPreviewAndBake_ReturnSameCodesForSameTarget()
        {
            using var fixture = CreateFixture("Shared Context");
            CorruptBrushLength(fixture.Deformer, 1);

            var inspector = MeshDeformerValidator.Validate(fixture.Deformer, fixture.Mesh).Select(d => d.Code).ToArray();
            var preview = LatticeDeformerPreviewFilter.ValidateBeforePreview(fixture.Deformer, fixture.Mesh).Select(d => d.Code).ToArray();
            var bake = LatticeDeformerBakePass.ValidateBeforeBakeDiagnostics(fixture.Deformer).Select(d => d.Code).ToArray();

            Assert.That(preview, Is.EqualTo(inspector));
            Assert.That(bake, Is.EqualTo(inspector));
            Assert.That(inspector, Does.Contain(MeshDeformerValidator.BrushLengthMismatch));
        }

        [Test]
        public void NdmfPreviewInstantiate_ErrorReturnsNoNodeAndDoesNotMutateProxy()
        {
            using var fixture = CreateFixture("NDMF Preview E2E");
            CorruptBrushLength(fixture.Deformer, 1);
            var proxyObject = new GameObject("NDMF Preview Proxy");
            Mesh proxyMesh = Object.Instantiate(fixture.Mesh);
            try
            {
                proxyObject.AddComponent<MeshFilter>().sharedMesh = proxyMesh;
                var proxyRenderer = proxyObject.AddComponent<MeshRenderer>();
                var filter = new LatticeDeformerPreviewFilter();
                bool previousIgnore = LogAssert.ignoreFailingMessages;
                IRenderFilterNode node;
                try
                {
                    LogAssert.ignoreFailingMessages = true;
                    node = filter.Instantiate(
                            default,
                            new[] { ((Renderer)fixture.Renderer, (Renderer)proxyRenderer) },
                            null)
                        .GetAwaiter().GetResult();
                }
                finally
                {
                    LogAssert.ignoreFailingMessages = previousIgnore;
                }

                Assert.That(node, Is.Null);
                Assert.That(proxyRenderer.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(proxyMesh));
            }
            finally
            {
                Object.DestroyImmediate(proxyObject);
                Object.DestroyImmediate(proxyMesh);
            }
        }

        [Test]
        public void NdmfPreviewInstantiate_ProxyTopologyMismatchWarnsAndRestoresProxy()
        {
            using var fixture = CreateFixture("NDMF Preview Topology");
            var proxyObject = new GameObject("NDMF Preview Mismatched Proxy");
            Mesh proxyMesh = CreateMesh("Mismatched Proxy Mesh");
            proxyMesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            IRenderFilterNode node = null;
            try
            {
                proxyObject.AddComponent<MeshFilter>().sharedMesh = proxyMesh;
                var proxyRenderer = proxyObject.AddComponent<MeshRenderer>();
                var filter = new LatticeDeformerPreviewFilter();
                LogAssert.Expect(LogType.Warning,
                    new System.Text.RegularExpressions.Regex(MeshDeformerValidator.PreviewBakeTargetMismatch));

                node = filter.Instantiate(
                        default,
                        new[] { ((Renderer)fixture.Renderer, (Renderer)proxyRenderer) },
                        ComputeContext.NullContext)
                    .GetAwaiter().GetResult();

                Assert.That(node, Is.Not.Null);
                Assert.That(proxyRenderer.GetComponent<MeshFilter>().sharedMesh, Is.Not.SameAs(proxyMesh));
                node.Dispose();
                node = null;
                Assert.That(proxyRenderer.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(proxyMesh));
            }
            finally
            {
                node?.Dispose();
                Object.DestroyImmediate(proxyObject);
                Object.DestroyImmediate(proxyMesh);
            }
        }

        [Test]
        public void NdmfAvatarProcessor_ErrorStopsBakeBeforeMeshReplacement()
        {
            using var fixture = CreateFixture("NDMF Bake E2E");
            CorruptBrushLength(fixture.Deformer, 1);
            Mesh source = fixture.Filter.sharedMesh;
            bool previousIgnore = LogAssert.ignoreFailingMessages;
            try
            {
                LogAssert.ignoreFailingMessages = true;

                BuildContext context = AvatarProcessor.ProcessAvatar(
                    fixture.GameObject,
                    nadena.dev.ndmf.platform.AmbientPlatform.CurrentPlatform);
                Assert.That(context.Successful, Is.False);
                Assert.That(fixture.Filter.sharedMesh, Is.SameAs(source));
                Assert.That(fixture.GameObject.GetComponent<LatticeDeformer>(), Is.SameAs(fixture.Deformer));
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnore;
            }
        }

        [Test]
        public void MissingRendererAndMesh_AreStableErrors()
        {
            var empty = new GameObject("No Renderer");
            var noMesh = new GameObject("No Mesh");
            try
            {
                var missingRenderer = empty.AddComponent<LatticeDeformer>();
                var filter = noMesh.AddComponent<MeshFilter>();
                noMesh.AddComponent<MeshRenderer>();
                var missingMesh = noMesh.AddComponent<LatticeDeformer>();
                missingMesh.Reset();

                AssertCode(missingRenderer, MeshDeformerValidator.MissingRenderer);
                Assert.That(filter.sharedMesh, Is.Null);
                AssertCode(missingMesh, MeshDeformerValidator.MissingSourceMesh);
            }
            finally
            {
                Object.DestroyImmediate(empty);
                Object.DestroyImmediate(noMesh);
            }
        }

        [Test]
        public void SourceMeshReplacement_IsNotSilentlyAccepted()
        {
            using var fixture = CreateFixture("Source Replacement");
            var replacement = CreateMesh("Replacement");
            try
            {
                fixture.Filter.sharedMesh = replacement;

                AssertCode(fixture.Deformer, MeshDeformerValidator.SourceMeshChanged);
                Assert.That(LatticeDeformerBakePass.ValidateBeforeBake(fixture.Deformer), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(replacement);
            }
        }

        [Test]
        public void InPlaceTopologyChange_IsDetectedFromSerializedBaseline()
        {
            using var fixture = CreateFixture("Topology Drift");
            fixture.Mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };

            AssertCode(fixture.Deformer, MeshDeformerValidator.SourceMeshChanged);
        }

        [Test]
        public void BrushAndMaskLengthMismatch_ReportLayerAndUndoableSingleObjectFix()
        {
            using var fixture = CreateFixture("Array Fix");
            int layerIndex = CorruptBrushLength(fixture.Deformer, 1);
            CorruptMaskLength(fixture.Deformer, layerIndex, 2);
            Undo.ClearAll();

            var diagnostics = MeshDeformerValidator.Validate(fixture.Deformer);
            var brush = diagnostics.Single(d => d.Code == MeshDeformerValidator.BrushLengthMismatch);
            var mask = diagnostics.Single(d => d.Code == MeshDeformerValidator.MaskLengthMismatch);
            Assert.That(brush.Target, Is.SameAs(fixture.Deformer));
            Assert.That(brush.GroupIndex, Is.EqualTo(0));
            Assert.That(brush.LayerIndex, Is.EqualTo(layerIndex));
            Assert.That(brush.Fix, Is.Not.Null);
            Assert.That(mask.Fix, Is.Not.Null);

            int undoGroup = Undo.GetCurrentGroup();
            brush.Fix();
            var afterFix = new SerializedObject(fixture.Deformer).FindProperty("_groups")
                .GetArrayElementAtIndex(0).FindPropertyRelative("_layers")
                .GetArrayElementAtIndex(layerIndex).FindPropertyRelative("_brushDisplacements");
            Assert.That(afterFix.arraySize, Is.EqualTo(fixture.Mesh.vertexCount));
            var untouchedMask = new SerializedObject(fixture.Deformer).FindProperty("_groups")
                .GetArrayElementAtIndex(0).FindPropertyRelative("_layers")
                .GetArrayElementAtIndex(layerIndex).FindPropertyRelative("_vertexMask");
            Assert.That(untouchedMask.arraySize, Is.EqualTo(2), "Fix must not mutate another property.");
            Assert.That(Undo.GetCurrentGroup(), Is.EqualTo(undoGroup));
            Assert.That(Undo.GetCurrentGroupName(), Is.EqualTo("Resize Brush Displacements"));
        }

        [Test]
        public void InvalidLatticeGridControlPointsAndBounds_AreReported()
        {
            using var fixture = CreateFixture("Invalid Lattice");
            var lattice = fixture.Deformer.Groups[0].Layers[0].Settings;
            var serialized = new SerializedObject(fixture.Deformer);
            var settings = serialized.FindProperty("_groups").GetArrayElementAtIndex(0)
                .FindPropertyRelative("_layers").GetArrayElementAtIndex(0)
                .FindPropertyRelative("_settings");
            settings.FindPropertyRelative("_gridSize").vector3IntValue = new Vector3Int(1, 3, 3);
            settings.FindPropertyRelative("_controlPointsLocal").arraySize = 2;
            settings.FindPropertyRelative("_localBounds").boundsValue = new Bounds(Vector3.zero, Vector3.zero);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            typeof(LatticeAsset).GetField("_controlPointsLocal", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(lattice, new Vector3[2]);

            var codes = MeshDeformerValidator.Validate(fixture.Deformer).Select(d => d.Code).ToArray();
            Assert.That(codes, Does.Contain(MeshDeformerValidator.InvalidGridSize));
            Assert.That(codes, Does.Contain(MeshDeformerValidator.ControlPointCountMismatch));
            Assert.That(codes, Does.Contain(MeshDeformerValidator.InvalidBounds));
        }

        [Test]
        public void InvalidActiveIndicesAndEmptyEnabledGroup_AreErrors()
        {
            using var fixture = CreateFixture("Raw Structure");
            var serialized = new SerializedObject(fixture.Deformer);
            serialized.FindProperty("_activeGroupIndex").intValue = 8;
            var group = serialized.FindProperty("_groups").GetArrayElementAtIndex(0);
            group.FindPropertyRelative("_layers").ClearArray();
            group.FindPropertyRelative("_activeLayerIndex").intValue = 4;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var codes = MeshDeformerValidator.Validate(fixture.Deformer).Select(d => d.Code).ToArray();
            Assert.That(codes, Does.Contain(MeshDeformerValidator.InvalidGroupStructure));
            Assert.That(codes, Does.Contain(MeshDeformerValidator.InvalidLayerStructure));
        }

        [Test]
        public void DuplicateEmptyAndExistingBlendShapeNames_AreDistinguished()
        {
            using var fixture = CreateFixture("BlendShape Names", addSourceBlendShape: true);
            var first = fixture.Deformer.Groups[0];
            first.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
            first.BlendShapeName = "";
            var secondIndex = fixture.Deformer.AddGroup("Second");
            var second = fixture.Deformer.Groups[secondIndex];
            second.LayersList.Add(new LatticeLayer());
            second.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
            second.BlendShapeName = "Smile";
            first.BlendShapeName = "Smile";

            var diagnostics = MeshDeformerValidator.Validate(fixture.Deformer);
            Assert.That(diagnostics.Count(d => d.Code == MeshDeformerValidator.DuplicateBlendShapeName), Is.EqualTo(1));
            Assert.That(diagnostics.Count(d => d.Code == MeshDeformerValidator.ExistingBlendShapeCollision), Is.EqualTo(2));

            first.BlendShapeName = "";
            AssertCode(fixture.Deformer, MeshDeformerValidator.EmptyBlendShapeName);
        }

        [Test]
        public void DisabledComponentGroupAndLayer_DoNotProduceFatalDataErrors()
        {
            using var fixture = CreateFixture("Disabled");
            DeformerGroup group = fixture.Deformer.Groups[0];
            int layer = CorruptBrushLength(fixture.Deformer, 1);
            LatticeLayer corruptedLayer = group.Layers[layer];
            corruptedLayer.Enabled = false;
            Assert.That(MeshDeformerValidator.Validate(fixture.Deformer)
                .Any(d => d.Code == MeshDeformerValidator.BrushLengthMismatch), Is.False);

            group.Enabled = false;
            Assert.That(MeshDeformerValidator.HasErrors(MeshDeformerValidator.Validate(fixture.Deformer)), Is.False);

            fixture.Deformer.enabled = false;
            Assert.That(MeshDeformerValidator.Validate(fixture.Deformer), Is.Empty);
        }

        [Test]
        public void ClearanceReferences_ReportMissingSelfAndInactiveAsWarningOnly()
        {
            using var fixture = CreateFixture("Clearance");
            fixture.Deformer.ShowClearanceHeatmap = true;
            AssertWarning(fixture.Deformer, MeshDeformerValidator.InvalidClearanceReference);

            fixture.Deformer.ClearanceReferenceRenderer = fixture.Renderer;
            AssertWarning(fixture.Deformer, MeshDeformerValidator.InvalidClearanceReference);

            var reference = new GameObject("Inactive Reference");
            var referenceMesh = CreateMesh("Reference");
            try
            {
                reference.AddComponent<MeshFilter>().sharedMesh = referenceMesh;
                var referenceRenderer = reference.AddComponent<MeshRenderer>();
                reference.SetActive(false);
                fixture.Deformer.ClearanceReferenceRenderer = referenceRenderer;
                AssertWarning(fixture.Deformer, MeshDeformerValidator.InvalidClearanceReference);
                Assert.That(LatticeDeformerBakePass.ValidateBeforeBake(fixture.Deformer), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(reference);
                Object.DestroyImmediate(referenceMesh);
            }
        }

        [Test]
        public void PreviewTargetMismatch_IsWarningAndBakeCanContinue()
        {
            using var fixture = CreateFixture("Preview Target");
            var other = CreateMesh("Proxy Topology");
            other.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            try
            {
                var diagnostic = LatticeDeformerPreviewFilter.ValidateBeforePreview(fixture.Deformer, other)
                    .Single(d => d.Code == MeshDeformerValidator.PreviewBakeTargetMismatch);
                Assert.That(diagnostic.Severity, Is.EqualTo(MeshDeformerDiagnosticSeverity.Warning));
                Assert.That(MeshDeformerValidator.HasErrors(new[] { diagnostic }), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void PreviewTargetCloneWithSameTopology_DoesNotWarn()
        {
            using var fixture = CreateFixture("Preview Target Clone");
            var clone = Object.Instantiate(fixture.Mesh);
            try
            {
                Assert.That(LatticeDeformerPreviewFilter.ValidateBeforePreview(fixture.Deformer, clone)
                    .Any(d => d.Code == MeshDeformerValidator.PreviewBakeTargetMismatch), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(clone);
            }
        }

        [Test]
        public void ProfileExactLegacyAndMismatch_UseErrorAndWarningSeverities()
        {
            using var fixture = CreateFixture("Profile Compatibility");
            var exact = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            var legacy = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            var mismatch = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            var incompatibleMesh = CreateMesh("Incompatible");
            incompatibleMesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            try
            {
                exact.Capture(new List<DeformerGroup>(fixture.Deformer.Groups), 0, fixture.Mesh);
                Assert.That(fixture.Deformer.UseProfile(exact), Is.True);
                var exactDiagnostics = MeshDeformerValidator.Validate(fixture.Deformer);
                Assert.That(MeshDeformerValidator.HasErrors(exactDiagnostics), Is.False);
                Assert.That(exactDiagnostics
                    .Any(d => d.Code == MeshDeformerValidator.ProfileTopologyMismatch ||
                              d.Code == MeshDeformerValidator.ProfileCompatibilityUnknown), Is.False);

                legacy.Capture(new List<DeformerGroup>(), 0);
                SetProfileState(fixture.Deformer, legacy);
                AssertWarning(fixture.Deformer, MeshDeformerValidator.ProfileCompatibilityUnknown);

                mismatch.Capture(new List<DeformerGroup>(), 0, incompatibleMesh);
                SetProfileState(fixture.Deformer, mismatch);
                var mismatchDiagnostic = MeshDeformerValidator.Validate(fixture.Deformer)
                    .Single(d => d.Code == MeshDeformerValidator.ProfileTopologyMismatch);
                Assert.That(mismatchDiagnostic.Severity, Is.EqualTo(MeshDeformerDiagnosticSeverity.Error));
            }
            finally
            {
                Object.DestroyImmediate(exact);
                Object.DestroyImmediate(legacy);
                Object.DestroyImmediate(mismatch);
                Object.DestroyImmediate(incompatibleMesh);
            }
        }

        [Test]
        public void UnsafeRestSpaceConversion_IsWarningOnly()
        {
            var gameObject = new GameObject("Unsafe Rest Space");
            var renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            var mesh = CreateMesh("Unskinned");
            renderer.sharedMesh = mesh;
            var deformer = gameObject.AddComponent<LatticeDeformer>();
            deformer.Reset();
            try
            {
                SkinnedVertexHelper.StoreMovesInRestSpace = true;
                AssertWarning(deformer, MeshDeformerValidator.RestSpaceConversionUnsafe);
                Assert.That(LatticeDeformerBakePass.ValidateBeforeBake(deformer), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void CorrectingState_RemovesStaleDiagnostic()
        {
            using var fixture = CreateFixture("Revalidation");
            int layer = CorruptBrushLength(fixture.Deformer, 1);
            var diagnostic = MeshDeformerValidator.Validate(fixture.Deformer)
                .Single(d => d.Code == MeshDeformerValidator.BrushLengthMismatch);

            diagnostic.Fix();

            Assert.That(MeshDeformerValidator.Validate(fixture.Deformer)
                .Any(d => d.Code == MeshDeformerValidator.BrushLengthMismatch), Is.False);
            Assert.That(fixture.Deformer.Groups[0].Layers[layer].BrushDisplacementCount,
                Is.EqualTo(fixture.Mesh.vertexCount));
        }

        private static void AssertCode(LatticeDeformer deformer, string code)
        {
            Assert.That(MeshDeformerValidator.Validate(deformer).Any(d => d.Code == code), Is.True);
        }

        private static void AssertWarning(LatticeDeformer deformer, string code)
        {
            var diagnostic = MeshDeformerValidator.Validate(deformer).Single(d => d.Code == code);
            Assert.That(diagnostic.Severity, Is.EqualTo(MeshDeformerDiagnosticSeverity.Warning));
        }

        private static int CorruptBrushLength(LatticeDeformer deformer, int size)
        {
            int layerIndex = deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
            var serialized = new SerializedObject(deformer);
            var layer = serialized.FindProperty("_groups").GetArrayElementAtIndex(0)
                .FindPropertyRelative("_layers").GetArrayElementAtIndex(layerIndex);
            layer.FindPropertyRelative("_brushDisplacements").arraySize = size;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return layerIndex;
        }

        private static void CorruptMaskLength(LatticeDeformer deformer, int layerIndex, int size)
        {
            var serialized = new SerializedObject(deformer);
            var layer = serialized.FindProperty("_groups").GetArrayElementAtIndex(0)
                .FindPropertyRelative("_layers").GetArrayElementAtIndex(layerIndex);
            layer.FindPropertyRelative("_vertexMask").arraySize = size;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetProfileState(LatticeDeformer deformer, MeshDeformerProfile profile)
        {
            var serialized = new SerializedObject(deformer);
            serialized.FindProperty("_profile").objectReferenceValue = profile;
            serialized.FindProperty("_dataSource").enumValueIndex = (int)DeformerDataSource.Profile;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Fixture CreateFixture(string name, bool addSourceBlendShape = false)
        {
            var gameObject = new GameObject(name);
            var filter = gameObject.AddComponent<MeshFilter>();
            var renderer = gameObject.AddComponent<MeshRenderer>();
            var mesh = CreateMesh(name + " Mesh");
            if (addSourceBlendShape)
            {
                mesh.AddBlendShapeFrame("Smile", 100f, new Vector3[mesh.vertexCount], null, null);
            }
            filter.sharedMesh = mesh;
            var deformer = gameObject.AddComponent<LatticeDeformer>();
            deformer.Reset();
            return new Fixture(gameObject, mesh, filter, renderer, deformer);
        }

        private static Mesh CreateMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one };
            mesh.triangles = new[] { 0, 1, 2, 1, 3, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private sealed class Fixture : System.IDisposable
        {
            internal GameObject GameObject { get; }
            internal Mesh Mesh { get; }
            internal MeshFilter Filter { get; }
            internal MeshRenderer Renderer { get; }
            internal LatticeDeformer Deformer { get; }

            internal Fixture(GameObject gameObject, Mesh mesh, MeshFilter filter,
                MeshRenderer renderer, LatticeDeformer deformer)
            {
                GameObject = gameObject;
                Mesh = mesh;
                Filter = filter;
                Renderer = renderer;
                Deformer = deformer;
            }

            public void Dispose()
            {
                Object.DestroyImmediate(GameObject);
                Object.DestroyImmediate(Mesh, AssetDatabase.Contains(Mesh));
            }
        }
    }
}
#endif
