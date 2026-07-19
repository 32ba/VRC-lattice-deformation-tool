#if UNITY_EDITOR
using System;
using System.Linq;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class ClearanceScanRunnerTests
    {
        private const float Epsilon = 2e-4f;

        [SetUp]
        public void SetUp()
        {
            ClearanceQueryCache.Clear();
        }

        [Test]
        public void MultipleConditions_RecordPerVertexWorstConditionAndRestoreScene()
        {
            var fixture = Fixture.CreateMeshRenderers();
            fixture.Target.transform.localPosition = new Vector3(0f, 0f, 0.003f);
            Vector3 initial = fixture.Target.transform.localPosition;
            var scanSet = NewScanSet(
                PoseCondition("Safe", 0.02f),
                PoseCondition("Penetrating", -0.01f));
            try
            {
                ClearanceScanResult result = Run(fixture, scanSet);

                Assert.That(result.Conditions.Count, Is.EqualTo(2));
                Assert.That(result.SuccessfulConditionCount, Is.EqualTo(2));
                Assert.That(result.WorstConditionIndex, Is.EqualTo(1));
                Assert.That(result.WorstConditionIndices, Is.All.EqualTo(1));
                Assert.That(result.Conditions[1].Statistics.MinimumClearance,
                    Is.LessThan(result.Conditions[0].Statistics.MinimumClearance));
                Assert.That(fixture.Target.transform.localPosition, Is.EqualTo(initial));
            }
            finally
            {
                Object.DestroyImmediate(scanSet);
                fixture.Dispose();
            }
        }

        [Test]
        public void AnimationSamples_ProduceDifferentWorstCondition()
        {
            var fixture = Fixture.CreateMeshRenderers();
            var clip = new AnimationClip { legacy = true, name = "Scan Pose" };
            clip.SetCurve(
                "Target",
                typeof(Transform),
                "m_LocalPosition.z",
                new AnimationCurve(new Keyframe(0f, 0.02f), new Keyframe(1f, -0.01f)));
            var first = new ClearanceScanCondition
            {
                Name = "Clip 0",
                UseAnimationClip = true,
                AnimationClip = clip,
                SampleTime = 0f
            };
            var second = new ClearanceScanCondition
            {
                Name = "Clip 1",
                UseAnimationClip = true,
                AnimationClip = clip,
                SampleTime = 1f
            };
            var scanSet = NewScanSet(first, second);
            try
            {
                ClearanceScanResult result = Run(fixture, scanSet);

                Assert.That(result.SuccessfulConditionCount, Is.EqualTo(2));
                Assert.That(result.WorstConditionIndex, Is.EqualTo(1));
                Assert.That(fixture.Target.transform.localPosition, Is.EqualTo(Vector3.zero));
            }
            finally
            {
                Object.DestroyImmediate(scanSet);
                Object.DestroyImmediate(clip);
                fixture.Dispose();
            }
        }

        [Test]
        public void TargetAndReferenceBlendShapes_AreAppliedTogetherAndRestored()
        {
            var fixture = Fixture.CreateSkinnedRenderers();
            fixture.TargetSkinned.SetBlendShapeWeight(0, 10f);
            fixture.ReferenceSkinned.SetBlendShapeWeight(0, 20f);
            var condition = new ClearanceScanCondition { Name = "Both Shapes" };
            condition.BlendShapeOverrides.Add(new ClearanceBlendShapeOverride
            {
                RendererRole = ClearanceScanRendererRole.Target,
                BlendShapeName = "Scan",
                Weight = 80f
            });
            condition.BlendShapeOverrides.Add(new ClearanceBlendShapeOverride
            {
                RendererRole = ClearanceScanRendererRole.Reference,
                BlendShapeName = "Scan",
                Weight = 70f
            });
            var scanSet = NewScanSet(condition);
            try
            {
                ClearanceScanResult result = Run(fixture, scanSet);

                Assert.That(result.Conditions.Single().Status,
                    Is.EqualTo(ClearanceScanConditionStatus.Success));
                Assert.That(fixture.TargetSkinned.GetBlendShapeWeight(0), Is.EqualTo(10f));
                Assert.That(fixture.ReferenceSkinned.GetBlendShapeWeight(0), Is.EqualTo(20f));
            }
            finally
            {
                Object.DestroyImmediate(scanSet);
                fixture.Dispose();
            }
        }

        [Test]
        public void InvalidConditions_AreReportedIndividuallyAndDoNotStopScan()
        {
            var fixture = Fixture.CreateMeshRenderers();
            var invalidClip = new ClearanceScanCondition
            {
                Name = "Missing Clip",
                UseAnimationClip = true
            };
            var missingTransform = new ClearanceScanCondition { Name = "Missing Transform" };
            missingTransform.TransformOverrides.Add(new ClearanceTransformPoseOverride
            {
                RelativePath = "Does/Not/Exist"
            });
            var missingShape = new ClearanceScanCondition { Name = "Missing Shape" };
            missingShape.BlendShapeOverrides.Add(new ClearanceBlendShapeOverride
            {
                RendererRole = ClearanceScanRendererRole.Target,
                BlendShapeName = "Nope",
                Weight = 100f
            });
            var valid = PoseCondition("Valid", 0.02f);
            var scanSet = NewScanSet(null, invalidClip, missingTransform, missingShape, valid);
            try
            {
                ClearanceScanResult result = Run(fixture, scanSet);

                Assert.That(result.Conditions.Select(item => item.Status), Is.EqualTo(new[]
                {
                    ClearanceScanConditionStatus.InvalidCondition,
                    ClearanceScanConditionStatus.InvalidAnimationClip,
                    ClearanceScanConditionStatus.MissingTransform,
                    ClearanceScanConditionStatus.InvalidRenderer,
                    ClearanceScanConditionStatus.Success
                }));
                Assert.That(result.SuccessfulConditionCount, Is.EqualTo(1));
                Assert.That(result.Conditions[0].ErrorMessage, Is.Not.Empty);
                Assert.That(result.Conditions[3].ErrorMessage, Does.Contain("SkinnedMeshRenderer"));
            }
            finally
            {
                Object.DestroyImmediate(scanSet);
                fixture.Dispose();
            }
        }

        [Test]
        public void ExceptionAfterConditionApplication_IsIsolatedAndSceneIsRestored()
        {
            var fixture = Fixture.CreateMeshRenderers();
            fixture.Target.transform.localPosition = new Vector3(0f, 0f, 0.003f);
            Vector3 initial = fixture.Target.transform.localPosition;
            var scanSet = NewScanSet(
                PoseCondition("Throws", -0.01f),
                PoseCondition("Continues", 0.02f));
            using var operation = new ClearanceScanOperation(
                scanSet,
                fixture.Deformer,
                fixture.Reference,
                fixture.Root.transform,
                ClearanceQueryMode.ReferenceNormal,
                0.005f,
                0.01f,
                afterConditionApplied: index =>
                {
                    if (index == 0) throw new InvalidOperationException("Injected failure");
                });
            try
            {
                ClearanceScanResult result = operation.RunToCompletion();

                Assert.That(result.Conditions[0].Status, Is.EqualTo(ClearanceScanConditionStatus.Exception));
                Assert.That(result.Conditions[0].ErrorMessage, Does.Contain("Injected failure"));
                Assert.That(result.Conditions[1].Status, Is.EqualTo(ClearanceScanConditionStatus.Success));
                Assert.That(fixture.Target.transform.localPosition, Is.EqualTo(initial));
            }
            finally
            {
                Object.DestroyImmediate(scanSet);
                fixture.Dispose();
            }
        }

        [Test]
        public void TopologyChange_IsConditionErrorAndOriginalMeshIsRestored()
        {
            var fixture = Fixture.CreateMeshRenderers();
            var alternate = new Mesh { name = "Alternate Topology" };
            alternate.vertices = new[]
            {
                new Vector3(-0.4f, -0.4f, 0f), new Vector3(0.4f, -0.4f, 0f),
                new Vector3(0.4f, 0.4f, 0f), new Vector3(-0.4f, 0.4f, 0f)
            };
            alternate.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            var scanSet = NewScanSet(
                new ClearanceScanCondition { Name = "Original" },
                new ClearanceScanCondition { Name = "Changed" });
            MeshFilter targetFilter = fixture.Target.GetComponent<MeshFilter>();
            using var operation = new ClearanceScanOperation(
                scanSet,
                fixture.Deformer,
                fixture.Reference,
                fixture.Root.transform,
                ClearanceQueryMode.ReferenceNormal,
                0.005f,
                0.01f,
                afterConditionApplied: index =>
                {
                    if (index == 1) targetFilter.sharedMesh = alternate;
                });
            try
            {
                ClearanceScanResult result = operation.RunToCompletion();

                Assert.That(result.Conditions[0].Status, Is.EqualTo(ClearanceScanConditionStatus.Success));
                Assert.That(result.Conditions[1].Status, Is.EqualTo(ClearanceScanConditionStatus.EvaluationFailed));
                Assert.That(result.Conditions[1].ErrorMessage, Does.Contain("topology"));
                Assert.That(targetFilter.sharedMesh, Is.SameAs(fixture.TargetMesh));
            }
            finally
            {
                Object.DestroyImmediate(scanSet);
                Object.DestroyImmediate(alternate);
                fixture.Dispose();
            }
        }

        [Test]
        public void Cancel_RestoresTransformAndReportsPartialProgress()
        {
            var fixture = Fixture.CreateMeshRenderers();
            fixture.Target.transform.localPosition = new Vector3(0f, 0f, 0.003f);
            Vector3 initial = fixture.Target.transform.localPosition;
            var scanSet = NewScanSet(
                PoseCondition("First", -0.01f),
                PoseCondition("Second", 0.02f));
            using var operation = NewOperation(fixture, scanSet);
            try
            {
                operation.Step();
                Assert.That(operation.IsCompleted, Is.False);
                Assert.That(operation.Progress, Is.EqualTo(0.5f));
                Assert.That(fixture.Target.transform.localPosition.z, Is.EqualTo(-0.01f).Within(Epsilon));

                operation.Cancel();

                Assert.That(operation.Result.WasCancelled, Is.True);
                Assert.That(operation.Result.Conditions.Count, Is.EqualTo(1));
                Assert.That(fixture.Target.transform.localPosition, Is.EqualTo(initial));
            }
            finally
            {
                Object.DestroyImmediate(scanSet);
                fixture.Dispose();
            }
        }

        [Test]
        public void RepeatedRun_IsDeterministic()
        {
            var fixture = Fixture.CreateMeshRenderers();
            var scanSet = NewScanSet(
                PoseCondition("A", 0.015f),
                PoseCondition("B", -0.004f));
            try
            {
                ClearanceScanResult first = Run(fixture, scanSet);
                ClearanceScanResult second = Run(fixture, scanSet);

                Assert.That(second.WorstClearances, Is.EqualTo(first.WorstClearances));
                Assert.That(second.WorstConditionIndices, Is.EqualTo(first.WorstConditionIndices));
                Assert.That(second.Conditions.Select(item => item.Statistics.MinimumClearance),
                    Is.EqualTo(first.Conditions.Select(item => item.Statistics.MinimumClearance)));
            }
            finally
            {
                Object.DestroyImmediate(scanSet);
                fixture.Dispose();
            }
        }

        [Test]
        public void ConditionThresholdOverride_IsRecordedInResult()
        {
            var fixture = Fixture.CreateMeshRenderers();
            var condition = PoseCondition("Threshold", 0.004f);
            condition.OverrideThresholds = true;
            condition.WarningDistance = 0.002f;
            condition.TargetDistance = 0.007f;
            var scanSet = NewScanSet(condition);
            try
            {
                ClearanceScanConditionResult result = Run(fixture, scanSet).Conditions.Single();

                Assert.That(result.WarningDistance, Is.EqualTo(0.002f).Within(Epsilon));
                Assert.That(result.TargetDistance, Is.EqualTo(0.007f).Within(Epsilon));
                Assert.That(result.UsedNdmfPreviewProxy, Is.False);
                Assert.That(result.EvaluatedRendererName, Does.EndWith("Root/Target"));
            }
            finally
            {
                Object.DestroyImmediate(scanSet);
                fixture.Dispose();
            }
        }

        [Test]
        public void MeshRendererAndSkinnedMeshRenderer_CanBeScannedTogether()
        {
            var fixture = Fixture.CreateMixedRenderers();
            var scanSet = NewScanSet(new ClearanceScanCondition { Name = "Mixed" });
            try
            {
                ClearanceScanConditionResult result = Run(fixture, scanSet).Conditions.Single();

                Assert.That(result.Status, Is.EqualTo(ClearanceScanConditionStatus.Success));
                Assert.That(result.VertexClearances.Length, Is.EqualTo(fixture.TargetMesh.vertexCount));
            }
            finally
            {
                Object.DestroyImmediate(scanSet);
                fixture.Dispose();
            }
        }

        [Test]
        public void InjectedNdmfPreviewProxy_IsRecordedAsEvaluationTarget()
        {
            var fixture = Fixture.CreateMeshRenderers();
            var proxyObject = new GameObject("Preview Proxy");
            proxyObject.transform.SetParent(fixture.Root.transform);
            proxyObject.AddComponent<MeshFilter>().sharedMesh = fixture.TargetMesh;
            var proxy = proxyObject.AddComponent<MeshRenderer>();
            var scanSet = NewScanSet(new ClearanceScanCondition { Name = "Proxy" });
            using var operation = new ClearanceScanOperation(
                scanSet,
                fixture.Deformer,
                fixture.Reference,
                fixture.Root.transform,
                ClearanceQueryMode.ReferenceNormal,
                0.005f,
                0.01f,
                previewProxyResolver: _ => proxy);
            try
            {
                ClearanceScanConditionResult result = operation.RunToCompletion().Conditions.Single();

                Assert.That(result.UsedNdmfPreviewProxy, Is.True);
                Assert.That(result.EvaluatedRendererName, Does.EndWith("Root/Preview Proxy"));
            }
            finally
            {
                Object.DestroyImmediate(scanSet);
                fixture.Dispose();
            }
        }

        [Test]
        public void ApplyConditionPreview_LeavesStateUntilDisposedThenRestores()
        {
            var fixture = Fixture.CreateMeshRenderers();
            fixture.Target.transform.localPosition = new Vector3(0f, 0f, 0.003f);
            Vector3 initial = fixture.Target.transform.localPosition;
            var scanSet = NewScanSet(PoseCondition("Preview", -0.01f));
            try
            {
                bool applied = ClearanceScanPreviewState.TryApply(
                    scanSet,
                    0,
                    fixture.Deformer,
                    fixture.Reference,
                    fixture.Root.transform,
                    ClearanceQueryMode.ReferenceNormal,
                    0.005f,
                    0.01f,
                    out ClearanceScanPreviewState preview,
                    out ClearanceScanConditionResult result);

                Assert.That(applied, Is.True);
                Assert.That(result.IsSuccess, Is.True);
                Assert.That(fixture.Target.transform.localPosition.z, Is.EqualTo(-0.01f).Within(Epsilon));
                preview.Dispose();
                Assert.That(fixture.Target.transform.localPosition, Is.EqualTo(initial));
            }
            finally
            {
                Object.DestroyImmediate(scanSet);
                fixture.Dispose();
            }
        }

        [Test]
        public void ScanSet_SerializesConditionOrderAndOverrides()
        {
            var source = NewScanSet(PoseCondition("一 番", 0.01f), PoseCondition("Second", -0.01f));
            var destination = ScriptableObject.CreateInstance<ClearanceScanSet>();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(source), destination);

                Assert.That(destination.Conditions.Select(condition => condition.Name),
                    Is.EqualTo(new[] { "一 番", "Second" }));
                Assert.That(destination.Conditions[1].TransformOverrides[0].LocalPosition.z,
                    Is.EqualTo(-0.01f).Within(Epsilon));
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(destination);
            }
        }

        private static ClearanceScanResult Run(Fixture fixture, ClearanceScanSet scanSet)
        {
            using var operation = NewOperation(fixture, scanSet);
            return operation.RunToCompletion();
        }

        private static ClearanceScanOperation NewOperation(Fixture fixture, ClearanceScanSet scanSet)
        {
            return new ClearanceScanOperation(
                scanSet,
                fixture.Deformer,
                fixture.Reference,
                fixture.Root.transform,
                ClearanceQueryMode.ReferenceNormal,
                0.005f,
                0.01f);
        }

        private static ClearanceScanSet NewScanSet(params ClearanceScanCondition[] conditions)
        {
            var scanSet = ScriptableObject.CreateInstance<ClearanceScanSet>();
            scanSet.Conditions.AddRange(conditions);
            return scanSet;
        }

        private static ClearanceScanCondition PoseCondition(string name, float targetLocalZ)
        {
            var condition = new ClearanceScanCondition { Name = name };
            condition.TransformOverrides.Add(new ClearanceTransformPoseOverride
            {
                RelativePath = "Target",
                OverridePosition = true,
                LocalPosition = new Vector3(0f, 0f, targetLocalZ),
                OverrideRotation = false,
                OverrideScale = false
            });
            return condition;
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly GameObject Root;
            internal readonly Renderer Target;
            internal readonly Renderer Reference;
            internal readonly LatticeDeformer Deformer;
            internal readonly Mesh TargetMesh;
            internal readonly Mesh ReferenceMesh;
            internal SkinnedMeshRenderer TargetSkinned => Target as SkinnedMeshRenderer;
            internal SkinnedMeshRenderer ReferenceSkinned => Reference as SkinnedMeshRenderer;

            private Fixture(
                GameObject root,
                Renderer target,
                Renderer reference,
                LatticeDeformer deformer,
                Mesh targetMesh,
                Mesh referenceMesh)
            {
                Root = root;
                Target = target;
                Reference = reference;
                Deformer = deformer;
                TargetMesh = targetMesh;
                ReferenceMesh = referenceMesh;
            }

            internal static Fixture CreateMeshRenderers()
            {
                var root = new GameObject("Root");
                Mesh targetMesh = CreateTargetMesh(false);
                Mesh referenceMesh = CreateReferenceMesh(false);
                var targetObject = new GameObject("Target");
                targetObject.transform.SetParent(root.transform);
                targetObject.AddComponent<MeshFilter>().sharedMesh = targetMesh;
                var target = targetObject.AddComponent<MeshRenderer>();
                var referenceObject = new GameObject("Reference");
                referenceObject.transform.SetParent(root.transform);
                referenceObject.AddComponent<MeshFilter>().sharedMesh = referenceMesh;
                var reference = referenceObject.AddComponent<MeshRenderer>();
                var deformer = targetObject.AddComponent<LatticeDeformer>();
                deformer.Reset();
                return new Fixture(root, target, reference, deformer, targetMesh, referenceMesh);
            }

            internal static Fixture CreateSkinnedRenderers()
            {
                var root = new GameObject("Root");
                Mesh targetMesh = CreateTargetMesh(true);
                Mesh referenceMesh = CreateReferenceMesh(true);
                var targetObject = new GameObject("Target");
                targetObject.transform.SetParent(root.transform);
                var target = targetObject.AddComponent<SkinnedMeshRenderer>();
                target.sharedMesh = targetMesh;
                var referenceObject = new GameObject("Reference");
                referenceObject.transform.SetParent(root.transform);
                var reference = referenceObject.AddComponent<SkinnedMeshRenderer>();
                reference.sharedMesh = referenceMesh;
                var deformer = targetObject.AddComponent<LatticeDeformer>();
                deformer.Reset();
                return new Fixture(root, target, reference, deformer, targetMesh, referenceMesh);
            }

            internal static Fixture CreateMixedRenderers()
            {
                var root = new GameObject("Root");
                Mesh targetMesh = CreateTargetMesh(false);
                Mesh referenceMesh = CreateReferenceMesh(true);
                var targetObject = new GameObject("Target");
                targetObject.transform.SetParent(root.transform);
                targetObject.AddComponent<MeshFilter>().sharedMesh = targetMesh;
                var target = targetObject.AddComponent<MeshRenderer>();
                var referenceObject = new GameObject("Reference");
                referenceObject.transform.SetParent(root.transform);
                var reference = referenceObject.AddComponent<SkinnedMeshRenderer>();
                reference.sharedMesh = referenceMesh;
                var deformer = targetObject.AddComponent<LatticeDeformer>();
                deformer.Reset();
                return new Fixture(root, target, reference, deformer, targetMesh, referenceMesh);
            }

            public void Dispose()
            {
                Mesh runtime = Deformer != null ? Deformer.RuntimeMesh : null;
                if (runtime != null) Object.DestroyImmediate(runtime);
                if (Root != null) Object.DestroyImmediate(Root);
                if (TargetMesh != null) Object.DestroyImmediate(TargetMesh);
                if (ReferenceMesh != null) Object.DestroyImmediate(ReferenceMesh);
            }

            private static Mesh CreateTargetMesh(bool blendShape)
            {
                var mesh = new Mesh { name = "Scan Target" };
                mesh.vertices = new[]
                {
                    new Vector3(-0.4f, -0.4f, 0f),
                    new Vector3(0.4f, -0.4f, 0f),
                    new Vector3(0f, 0.4f, 0f)
                };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                if (blendShape)
                    mesh.AddBlendShapeFrame("Scan", 100f,
                        new[] { Vector3.forward * 0.01f, Vector3.forward * 0.01f, Vector3.forward * 0.01f },
                        null, null);
                return mesh;
            }

            private static Mesh CreateReferenceMesh(bool blendShape)
            {
                var mesh = new Mesh { name = "Scan Reference" };
                mesh.vertices = new[]
                {
                    new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f),
                    new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f)
                };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
                mesh.RecalculateBounds();
                if (blendShape)
                    mesh.AddBlendShapeFrame("Scan", 100f,
                        new[]
                        {
                            Vector3.forward * 0.002f, Vector3.forward * 0.002f,
                            Vector3.forward * 0.002f, Vector3.forward * 0.002f
                        },
                        null, null);
                return mesh;
            }
        }
    }
}
#endif
