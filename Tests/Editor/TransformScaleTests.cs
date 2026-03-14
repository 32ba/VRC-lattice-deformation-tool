#if UNITY_EDITOR
using System.Reflection;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    /// <summary>
    /// Tests for lattice initialization and deformation correctness
    /// when parent transforms have non-identity scale.
    /// Covers bugs triggered by Armature scale or MA Scale Adjuster.
    /// </summary>
    public sealed class TransformScaleTests
    {
        private const float Epsilon = 1e-4f;
        private static readonly BindingFlags s_privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        // ========================================================================
        // Bounds initialization under parent scale
        // ========================================================================

        [Test]
        public void Init_ParentScale001_BoundsMatchMeshLocalBounds()
        {
            // VRChat avatar pattern: Armature at scale 0.01
            var fixture = CreateScaledFixture("Init_ParentScale001",
                parentScale: new Vector3(0.01f, 0.01f, 0.01f));
            try
            {
                var deformer = fixture.Deformer;
                var meshBounds = deformer.SourceMesh.bounds;
                var latticeBounds = deformer.Layers[0].Settings.LocalBounds;

                // Lattice bounds should match mesh local bounds regardless of parent scale
                AssertApproximately(meshBounds.center, latticeBounds.center);
                AssertApproximately(meshBounds.size, latticeBounds.size);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Init_ParentScale100_BoundsMatchMeshLocalBounds()
        {
            var fixture = CreateScaledFixture("Init_ParentScale100",
                parentScale: new Vector3(100f, 100f, 100f));
            try
            {
                var deformer = fixture.Deformer;
                var meshBounds = deformer.SourceMesh.bounds;
                var latticeBounds = deformer.Layers[0].Settings.LocalBounds;

                AssertApproximately(meshBounds.center, latticeBounds.center);
                AssertApproximately(meshBounds.size, latticeBounds.size);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Init_NonUniformParentScale_BoundsMatchMeshLocalBounds()
        {
            // Non-uniform scale (e.g., stretched horizontally)
            var fixture = CreateScaledFixture("Init_NonUniformParentScale",
                parentScale: new Vector3(2f, 0.5f, 1f));
            try
            {
                var deformer = fixture.Deformer;
                var meshBounds = deformer.SourceMesh.bounds;
                var latticeBounds = deformer.Layers[0].Settings.LocalBounds;

                AssertApproximately(meshBounds.center, latticeBounds.center);
                AssertApproximately(meshBounds.size, latticeBounds.size);
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Control points positioning under parent scale
        // ========================================================================

        [Test]
        public void Init_ParentScale001_ControlPointsWithinMeshBounds()
        {
            var fixture = CreateScaledFixture("ControlPoints_Scale001",
                parentScale: new Vector3(0.01f, 0.01f, 0.01f));
            try
            {
                AssertControlPointsWithinBounds(fixture.Deformer);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Init_ParentScale100_ControlPointsWithinMeshBounds()
        {
            var fixture = CreateScaledFixture("ControlPoints_Scale100",
                parentScale: new Vector3(100f, 100f, 100f));
            try
            {
                AssertControlPointsWithinBounds(fixture.Deformer);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Init_NonUniformScale_ControlPointsWithinMeshBounds()
        {
            var fixture = CreateScaledFixture("ControlPoints_NonUniform",
                parentScale: new Vector3(3f, 0.1f, 2f));
            try
            {
                AssertControlPointsWithinBounds(fixture.Deformer);
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Deformation correctness under parent scale
        // ========================================================================

        [Test]
        public void Deform_ParentScale001_SameResultAsUnscaled()
        {
            AssertDeformConsistentWithScale(
                "Deform_ParentScale001",
                new Vector3(0.01f, 0.01f, 0.01f));
        }

        [Test]
        public void Deform_ParentScale100_SameResultAsUnscaled()
        {
            AssertDeformConsistentWithScale(
                "Deform_ParentScale100",
                new Vector3(100f, 100f, 100f));
        }

        [Test]
        public void Deform_NonUniformParentScale_SameResultAsUnscaled()
        {
            AssertDeformConsistentWithScale(
                "Deform_NonUniformScale",
                new Vector3(2f, 0.5f, 1.5f));
        }

        // ========================================================================
        // MA Scale Adjuster pattern: parent=0.01, grandparent=100
        // (or similar nested compensating scales)
        // ========================================================================

        [Test]
        public void Init_MAScaleAdjusterPattern_BoundsCorrect()
        {
            // Simulates: Armature (scale 0.01) → ScaleAdjuster child (scale 100) → mesh
            // lossyScale of mesh object = 0.01 * 100 = 1.0
            var fixture = CreateNestedScaleFixture(
                "Init_MAScaleAdjuster",
                grandparentScale: new Vector3(0.01f, 0.01f, 0.01f),
                parentScale: new Vector3(100f, 100f, 100f));
            try
            {
                var deformer = fixture.Deformer;
                var meshBounds = deformer.SourceMesh.bounds;
                var latticeBounds = deformer.Layers[0].Settings.LocalBounds;

                AssertApproximately(meshBounds.center, latticeBounds.center);
                AssertApproximately(meshBounds.size, latticeBounds.size);
                AssertControlPointsWithinBounds(deformer);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Deform_MAScaleAdjusterPattern_CorrectResult()
        {
            // Armature(0.01) → ScaleAdjuster(100) → mesh
            var refFixture = CreateFixture("Deform_MAScale_Ref");
            var scaledFixture = CreateNestedScaleFixture(
                "Deform_MAScale_Scaled",
                grandparentScale: new Vector3(0.01f, 0.01f, 0.01f),
                parentScale: new Vector3(100f, 100f, 100f));
            try
            {
                // Apply same deformation to both
                ApplyTestDeformation(refFixture.Deformer);
                ApplyTestDeformation(scaledFixture.Deformer);

                ReleaseRuntimeMesh(refFixture.Deformer);
                ReleaseRuntimeMesh(scaledFixture.Deformer);
                var refMesh = refFixture.Deformer.Deform(false);
                var scaledMesh = scaledFixture.Deformer.Deform(false);

                Assert.That(refMesh.vertexCount, Is.EqualTo(scaledMesh.vertexCount));

                var refVerts = refMesh.vertices;
                var scaledVerts = scaledMesh.vertices;
                for (int i = 0; i < refVerts.Length; i++)
                {
                    AssertApproximately(refVerts[i], scaledVerts[i], 2e-3f);
                }
            }
            finally
            {
                refFixture.Dispose();
                scaledFixture.Dispose();
            }
        }

        // ========================================================================
        // Non-uniform nested scale (typical humanoid import issue)
        // ========================================================================

        [Test]
        public void Init_ArmatureScaleXOnly_BoundsCorrect()
        {
            // Some avatars have armature scale (1, 1, 1) but parent at (0.01, 0.01, 0.01)
            // with a child at non-uniform scale
            var fixture = CreateScaledFixture("Init_ArmatureScaleXOnly",
                parentScale: new Vector3(2f, 1f, 1f));
            try
            {
                var deformer = fixture.Deformer;
                var meshBounds = deformer.SourceMesh.bounds;
                var latticeBounds = deformer.Layers[0].Settings.LocalBounds;

                AssertApproximately(meshBounds.center, latticeBounds.center);
                AssertApproximately(meshBounds.size, latticeBounds.size);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Deform_NegativeScale_NoNaN()
        {
            // Some objects have negative scale for mirroring
            var fixture = CreateScaledFixture("Deform_NegativeScale",
                parentScale: new Vector3(-1f, 1f, 1f));
            try
            {
                var deformer = fixture.Deformer;
                ApplyTestDeformation(deformer);

                ReleaseRuntimeMesh(deformer);
                var mesh = deformer.Deform(false);
                Assert.That(mesh, Is.Not.Null);

                foreach (var v in mesh.vertices)
                {
                    Assert.That(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z),
                        Is.False, $"NaN found at {v}");
                }
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Deform_VerySmallScale_NoInfinity()
        {
            var fixture = CreateScaledFixture("Deform_VerySmallScale",
                parentScale: new Vector3(0.001f, 0.001f, 0.001f));
            try
            {
                var deformer = fixture.Deformer;
                ApplyTestDeformation(deformer);

                ReleaseRuntimeMesh(deformer);
                var mesh = deformer.Deform(false);
                Assert.That(mesh, Is.Not.Null);

                foreach (var v in mesh.vertices)
                {
                    Assert.That(float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z),
                        Is.False, $"Infinity found at {v}");
                }
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Brush displacement under scaled parent
        // ========================================================================

        [Test]
        public void BrushDisplacement_ParentScale001_CorrectMeshLocalDisplacement()
        {
            var fixture = CreateScaledFixture("BrushDisp_Scale001",
                parentScale: new Vector3(0.01f, 0.01f, 0.01f));
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;

                int idx = deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();

                var disp = new Vector3(0.1f, 0f, 0f);
                deformer.SetDisplacement(0, disp);

                ReleaseRuntimeMesh(deformer);
                var mesh = deformer.Deform(false);

                // Displacement should be in mesh-local space, unaffected by parent scale
                AssertApproximately(src[0] + disp, mesh.vertices[0], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void BrushDisplacement_NonUniformScale_CorrectMeshLocalDisplacement()
        {
            var fixture = CreateScaledFixture("BrushDisp_NonUniform",
                parentScale: new Vector3(5f, 0.2f, 1f));
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;

                int idx = deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();

                var disp = new Vector3(0.05f, 0.1f, -0.03f);
                deformer.SetDisplacement(0, disp);
                deformer.SetDisplacement(1, disp * 0.5f);

                ReleaseRuntimeMesh(deformer);
                var mesh = deformer.Deform(false);

                AssertApproximately(src[0] + disp, mesh.vertices[0], 2e-3f);
                AssertApproximately(src[1] + disp * 0.5f, mesh.vertices[1], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Lattice deformation under scaled parent
        // ========================================================================

        [Test]
        public void LatticeDeform_ParentScale001_ControlPointMoveProducesCorrectResult()
        {
            var refFixture = CreateFixture("LatticeCP_Ref");
            var scaledFixture = CreateScaledFixture("LatticeCP_Scaled",
                parentScale: new Vector3(0.01f, 0.01f, 0.01f));
            try
            {
                // Move same control point on both
                MoveFirstControlPoint(refFixture.Deformer, new Vector3(0f, 0.1f, 0f));
                MoveFirstControlPoint(scaledFixture.Deformer, new Vector3(0f, 0.1f, 0f));

                ReleaseRuntimeMesh(refFixture.Deformer);
                ReleaseRuntimeMesh(scaledFixture.Deformer);
                var refMesh = refFixture.Deformer.Deform(false);
                var scaledMesh = scaledFixture.Deformer.Deform(false);

                for (int i = 0; i < refMesh.vertexCount; i++)
                {
                    AssertApproximately(refMesh.vertices[i], scaledMesh.vertices[i], 2e-3f);
                }
            }
            finally
            {
                refFixture.Dispose();
                scaledFixture.Dispose();
            }
        }

        // ========================================================================
        // BlendShape output under scaled parent
        // ========================================================================

        [Test]
        public void BlendShapeOutput_ParentScale001_DeltasMatchUnscaled()
        {
            var refFixture = CreateFixture("BS_Ref");
            var scaledFixture = CreateScaledFixture("BS_Scaled",
                parentScale: new Vector3(0.01f, 0.01f, 0.01f));
            try
            {
                // Same BS output setup on both
                SetupBlendShapeOutput(refFixture.Deformer);
                SetupBlendShapeOutput(scaledFixture.Deformer);

                ReleaseRuntimeMesh(refFixture.Deformer);
                ReleaseRuntimeMesh(scaledFixture.Deformer);
                var refMesh = refFixture.Deformer.Deform(false);
                var scaledMesh = scaledFixture.Deformer.Deform(false);

                int vertexCount = refMesh.vertexCount;
                Assert.That(refMesh.blendShapeCount, Is.GreaterThan(0));
                Assert.That(scaledMesh.blendShapeCount, Is.EqualTo(refMesh.blendShapeCount));

                var refDeltas = new Vector3[vertexCount];
                var scaledDeltas = new Vector3[vertexCount];
                refMesh.GetBlendShapeFrameVertices(refMesh.blendShapeCount - 1, 0, refDeltas, null, null);
                scaledMesh.GetBlendShapeFrameVertices(scaledMesh.blendShapeCount - 1, 0, scaledDeltas, null, null);

                for (int i = 0; i < vertexCount; i++)
                {
                    AssertApproximately(refDeltas[i], scaledDeltas[i], 2e-3f);
                }
            }
            finally
            {
                refFixture.Dispose();
                scaledFixture.Dispose();
            }
        }

        // ========================================================================
        // Split/Flip under scaled parent
        // ========================================================================

        [Test]
        public void SplitFlip_ParentScale001_SameAsUnscaled()
        {
            var refFixture = CreateSymmetricFixture("SplitFlip_Ref");
            var scaledFixture = CreateScaledSymmetricFixture("SplitFlip_Scaled",
                parentScale: new Vector3(0.01f, 0.01f, 0.01f));
            try
            {
                // Same split+flip on both
                ApplySplitFlipWorkflow(refFixture.Deformer);
                ApplySplitFlipWorkflow(scaledFixture.Deformer);

                // Compare layer data
                int layerCount = refFixture.Deformer.Layers.Count;
                Assert.That(scaledFixture.Deformer.Layers.Count, Is.EqualTo(layerCount));

                for (int l = 0; l < layerCount; l++)
                {
                    var refLayer = refFixture.Deformer.Layers[l];
                    var scaledLayer = scaledFixture.Deformer.Layers[l];
                    if (refLayer.Type != MeshDeformerLayerType.Brush) continue;

                    int dispCount = refLayer.BrushDisplacementCount;
                    for (int v = 0; v < dispCount; v++)
                    {
                        AssertApproximately(
                            refLayer.GetBrushDisplacement(v),
                            scaledLayer.GetBrushDisplacement(v), 2e-3f);
                    }
                }
            }
            finally
            {
                refFixture.Dispose();
                scaledFixture.Dispose();
            }
        }

        // ========================================================================
        // SkinnedMeshRenderer with scaled armature
        // ========================================================================

        [Test]
        public void SkinnedMesh_ScaledArmature_InitializesCorrectly()
        {
            var root = new GameObject("SkinnedMesh_ScaledArmature");
            var armature = new GameObject("Armature");
            armature.transform.parent = root.transform;
            armature.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);

            var bone = new GameObject("Bone");
            bone.transform.parent = armature.transform;

            var meshObj = new GameObject("Body");
            meshObj.transform.parent = root.transform;
            var smr = meshObj.AddComponent<SkinnedMeshRenderer>();

            var sourceMesh = CreateCubeMesh();
            smr.sharedMesh = sourceMesh;
            smr.bones = new[] { bone.transform };
            smr.rootBone = bone.transform;

            var deformer = meshObj.AddComponent<LatticeDeformer>();
            try
            {
                deformer.Reset();

                Assert.That(deformer.SourceMesh, Is.Not.Null);
                Assert.That(deformer.Layers.Count, Is.GreaterThanOrEqualTo(1));

                var meshBounds = sourceMesh.bounds;
                var latticeBounds = deformer.Layers[0].Settings.LocalBounds;

                AssertApproximately(meshBounds.center, latticeBounds.center);
                AssertApproximately(meshBounds.size, latticeBounds.size);
                AssertControlPointsWithinBounds(deformer);

                // Deform should work
                var mesh = deformer.Deform(false);
                Assert.That(mesh, Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(sourceMesh);
            }
        }

        // ========================================================================
        // Complex: full workflow under scaled hierarchy
        // ========================================================================

        [Test]
        public void Story_FullWorkflow_UnderScaledArmature()
        {
            // Simulate: Armature(0.01) → mesh with lattice + brush + mask + BS output
            var fixture = CreateScaledFixture("Story_ScaledFullWorkflow",
                parentScale: new Vector3(0.01f, 0.01f, 0.01f));
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;
                var srcVerts = deformer.SourceMesh.vertices;

                // Lattice: shift
                var latticeSettings = deformer.Layers[0].Settings;
                latticeSettings.SetControlPointLocal(0,
                    latticeSettings.GetControlPointLocal(0) + new Vector3(0f, 0.05f, 0f));

                // Brush with mask and BS output
                int brushIdx = deformer.AddLayer("Masked BS", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[brushIdx];
                layer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                layer.BlendShapeName = "ScaledEdit";
                layer.EnsureVertexMaskCapacity(vertexCount);

                for (int i = 0; i < vertexCount; i++)
                {
                    deformer.SetDisplacement(i, new Vector3(0.01f, 0f, 0f));
                    layer.SetVertexMask(i, srcVerts[i].y >= 0f ? 1f : 0f);
                }

                // Split L, duplicate, flip R
                deformer.SplitLayerByAxis(brushIdx, 0, false);
                int rightIdx = deformer.DuplicateLayer(brushIdx);
                deformer.FlipLayerByAxis(rightIdx, 0);
                deformer.Layers[rightIdx].BlendShapeName = "ScaledEdit_R";

                ReleaseRuntimeMesh(deformer);
                deformer.InvalidateCache();
                var result = deformer.Deform(false);
                Assert.That(result, Is.Not.Null);
                Assert.That(result.vertexCount, Is.EqualTo(vertexCount));

                // No NaN or Infinity
                foreach (var v in result.vertices)
                {
                    Assert.That(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z),
                        Is.False);
                    Assert.That(float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z),
                        Is.False);
                }

                // BS should exist
                Assert.That(result.blendShapeCount, Is.GreaterThanOrEqualTo(1));
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Helpers
        // ========================================================================

        private static void AssertDeformConsistentWithScale(string name, Vector3 parentScale)
        {
            var refFixture = CreateFixture($"{name}_Ref");
            var scaledFixture = CreateScaledFixture($"{name}_Scaled", parentScale);
            try
            {
                ApplyTestDeformation(refFixture.Deformer);
                ApplyTestDeformation(scaledFixture.Deformer);

                ReleaseRuntimeMesh(refFixture.Deformer);
                ReleaseRuntimeMesh(scaledFixture.Deformer);
                var refMesh = refFixture.Deformer.Deform(false);
                var scaledMesh = scaledFixture.Deformer.Deform(false);

                Assert.That(refMesh.vertexCount, Is.EqualTo(scaledMesh.vertexCount));

                var refVerts = refMesh.vertices;
                var scaledVerts = scaledMesh.vertices;
                for (int i = 0; i < refVerts.Length; i++)
                {
                    AssertApproximately(refVerts[i], scaledVerts[i], 2e-3f);
                }
            }
            finally
            {
                refFixture.Dispose();
                scaledFixture.Dispose();
            }
        }

        private static void ApplyTestDeformation(LatticeDeformer deformer)
        {
            // Lattice shift + brush displacement
            var settings = deformer.Layers[0].Settings;
            settings.SetControlPointLocal(0,
                settings.GetControlPointLocal(0) + new Vector3(0f, 0.05f, 0f));

            int idx = deformer.AddLayer("TestBrush", MeshDeformerLayerType.Brush);
            deformer.ActiveLayerIndex = idx;
            deformer.EnsureDisplacementCapacity();
            deformer.SetDisplacement(0, new Vector3(0.02f, 0f, 0f));
        }

        private static void MoveFirstControlPoint(LatticeDeformer deformer, Vector3 delta)
        {
            var settings = deformer.Layers[0].Settings;
            settings.SetControlPointLocal(0, settings.GetControlPointLocal(0) + delta);
        }

        private static void SetupBlendShapeOutput(LatticeDeformer deformer)
        {
            int idx = deformer.AddLayer("BS Layer", MeshDeformerLayerType.Brush);
            deformer.ActiveLayerIndex = idx;
            deformer.EnsureDisplacementCapacity();
            deformer.SetDisplacement(0, new Vector3(0.1f, 0f, 0f));
            deformer.SetDisplacement(1, new Vector3(0f, 0.2f, 0f));
            deformer.Layers[idx].BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
            deformer.Layers[idx].BlendShapeName = "TestBS";
        }

        private static void ApplySplitFlipWorkflow(LatticeDeformer deformer)
        {
            int idx = deformer.AddLayer("SplitFlip", MeshDeformerLayerType.Brush);
            deformer.ActiveLayerIndex = idx;
            deformer.EnsureDisplacementCapacity();

            int vertexCount = deformer.SourceMesh.vertexCount;
            for (int i = 0; i < vertexCount; i++)
                deformer.SetDisplacement(i, new Vector3(0.01f, 0.02f, 0f));

            deformer.SplitLayerByAxis(idx, 0, false); // keep L
            int dupIdx = deformer.DuplicateLayer(idx);
            deformer.FlipLayerByAxis(dupIdx, 0);
        }

        private static void AssertControlPointsWithinBounds(LatticeDeformer deformer)
        {
            var settings = deformer.Layers[0].Settings;
            var bounds = settings.LocalBounds;

            for (int i = 0; i < settings.ControlPointCount; i++)
            {
                var cp = settings.GetControlPointLocal(i);
                Assert.That(cp.x, Is.GreaterThanOrEqualTo(bounds.min.x - Epsilon)
                    .And.LessThanOrEqualTo(bounds.max.x + Epsilon),
                    $"Control point {i} X={cp.x} outside bounds [{bounds.min.x}, {bounds.max.x}]");
                Assert.That(cp.y, Is.GreaterThanOrEqualTo(bounds.min.y - Epsilon)
                    .And.LessThanOrEqualTo(bounds.max.y + Epsilon),
                    $"Control point {i} Y={cp.y} outside bounds [{bounds.min.y}, {bounds.max.y}]");
                Assert.That(cp.z, Is.GreaterThanOrEqualTo(bounds.min.z - Epsilon)
                    .And.LessThanOrEqualTo(bounds.max.z + Epsilon),
                    $"Control point {i} Z={cp.z} outside bounds [{bounds.min.z}, {bounds.max.z}]");
            }
        }

        private static TestFixture CreateFixture(string name)
        {
            var root = new GameObject(name);
            var filter = root.AddComponent<MeshFilter>();
            root.AddComponent<MeshRenderer>();
            var mesh = CreateCubeMesh();
            filter.sharedMesh = mesh;
            var deformer = root.AddComponent<LatticeDeformer>();
            deformer.Reset();
            deformer.Deform(false);
            return new TestFixture(root, mesh, deformer);
        }

        private static TestFixture CreateScaledFixture(string name, Vector3 parentScale)
        {
            var parent = new GameObject($"{name}_Parent");
            parent.transform.localScale = parentScale;

            var child = new GameObject(name);
            child.transform.parent = parent.transform;
            var filter = child.AddComponent<MeshFilter>();
            child.AddComponent<MeshRenderer>();
            var mesh = CreateCubeMesh();
            filter.sharedMesh = mesh;

            var deformer = child.AddComponent<LatticeDeformer>();
            deformer.Reset();
            deformer.Deform(false);

            return new TestFixture(parent, mesh, deformer);
        }

        private static TestFixture CreateNestedScaleFixture(string name,
            Vector3 grandparentScale, Vector3 parentScale)
        {
            var grandparent = new GameObject($"{name}_Grandparent");
            grandparent.transform.localScale = grandparentScale;

            var parent = new GameObject($"{name}_Parent");
            parent.transform.parent = grandparent.transform;
            parent.transform.localScale = parentScale;

            var child = new GameObject(name);
            child.transform.parent = parent.transform;
            var filter = child.AddComponent<MeshFilter>();
            child.AddComponent<MeshRenderer>();
            var mesh = CreateCubeMesh();
            filter.sharedMesh = mesh;

            var deformer = child.AddComponent<LatticeDeformer>();
            deformer.Reset();
            deformer.Deform(false);

            return new TestFixture(grandparent, mesh, deformer);
        }

        private static TestFixture CreateSymmetricFixture(string name)
        {
            var root = new GameObject(name);
            var filter = root.AddComponent<MeshFilter>();
            root.AddComponent<MeshRenderer>();
            var mesh = CreateSymmetricMesh();
            filter.sharedMesh = mesh;
            var deformer = root.AddComponent<LatticeDeformer>();
            deformer.Reset();
            deformer.Deform(false);
            return new TestFixture(root, mesh, deformer);
        }

        private static TestFixture CreateScaledSymmetricFixture(string name, Vector3 parentScale)
        {
            var parent = new GameObject($"{name}_Parent");
            parent.transform.localScale = parentScale;

            var child = new GameObject(name);
            child.transform.parent = parent.transform;
            var filter = child.AddComponent<MeshFilter>();
            child.AddComponent<MeshRenderer>();
            var mesh = CreateSymmetricMesh();
            filter.sharedMesh = mesh;

            var deformer = child.AddComponent<LatticeDeformer>();
            deformer.Reset();
            deformer.Deform(false);

            return new TestFixture(parent, mesh, deformer);
        }

        private static Mesh CreateCubeMesh()
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var source = temp.GetComponent<MeshFilter>().sharedMesh;
            var mesh = Object.Instantiate(source);
            mesh.name = "ScaleTestMesh";
            Object.DestroyImmediate(temp);
            return mesh;
        }

        private static Mesh CreateSymmetricMesh()
        {
            var mesh = new Mesh { name = "ScaleTestSymmetric" };
            mesh.vertices = new[]
            {
                new Vector3( 1f, 0f, 0f),
                new Vector3(-1f, 0f, 0f),
                new Vector3( 0f, 1f, 0f),
                new Vector3( 0f,-1f, 0f)
            };
            mesh.triangles = new[] { 0, 2, 1, 1, 3, 0 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void ReleaseRuntimeMesh(LatticeDeformer deformer)
        {
            var field = typeof(LatticeDeformer).GetField("_runtimeMesh", s_privateInstance);
            var mesh = field?.GetValue(deformer) as Mesh;
            if (mesh != null) Object.DestroyImmediate(mesh);
            field?.SetValue(deformer, null);
        }

        private static void AssertApproximately(Vector3 expected, Vector3 actual,
            float tolerance = Epsilon)
        {
            Assert.That((expected - actual).sqrMagnitude,
                Is.LessThanOrEqualTo(tolerance * tolerance),
                $"Expected {expected} but got {actual}");
        }

        private sealed class TestFixture
        {
            public GameObject Root { get; }
            public Mesh SourceMesh { get; }
            public LatticeDeformer Deformer { get; }

            public TestFixture(GameObject root, Mesh sourceMesh, LatticeDeformer deformer)
            {
                Root = root;
                SourceMesh = sourceMesh;
                Deformer = deformer;
            }

            public void Dispose()
            {
                if (Root != null) Object.DestroyImmediate(Root);
                if (SourceMesh != null) Object.DestroyImmediate(SourceMesh);
            }
        }
    }
}
#endif
