#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class ClearanceHeatmapTests
    {
        private const float Epsilon = 1e-4f;

        [SetUp]
        public void SetUp()
        {
            ClearanceQueryCache.Clear();
        }

        [Test]
        public void Classification_UsesConsistentThresholdBoundariesAndStatistics()
        {
            var raw = CreateRawEvaluation(-0.002f, 0f, 0.005f, 0.0075f, 0.01f, 0.02f);

            var evaluation = ClearanceHeatmapEvaluator.Classify(raw, 0.005f, 0.01f);

            Assert.That(evaluation.Classifications, Is.EqualTo(new[]
            {
                ClearanceClassification.Penetrating,
                ClearanceClassification.Warning,
                ClearanceClassification.Warning,
                ClearanceClassification.BelowTarget,
                ClearanceClassification.Clear,
                ClearanceClassification.Clear
            }));
            Assert.That(evaluation.Statistics.MinimumClearance, Is.EqualTo(-0.002f).Within(Epsilon));
            Assert.That(evaluation.Statistics.MaximumPenetrationDepth, Is.EqualTo(0.002f).Within(Epsilon));
            Assert.That(evaluation.Statistics.ViolationVertexCount, Is.EqualTo(4));
            Assert.That(evaluation.Statistics.EvaluatedVertexCount, Is.EqualTo(6));
        }

        [Test]
        public void ThresholdChange_ReclassifiesCachedRawResultsWithoutNewQuery()
        {
            var raw = CreateRawEvaluation(0.004f, 0.008f, 0.012f);

            var narrow = ClearanceHeatmapEvaluator.Classify(raw, 0.003f, 0.006f);
            var wide = ClearanceHeatmapEvaluator.Classify(raw, 0.005f, 0.01f);

            Assert.That(narrow.Classifications, Is.EqualTo(new[]
            {
                ClearanceClassification.BelowTarget,
                ClearanceClassification.Clear,
                ClearanceClassification.Clear
            }));
            Assert.That(wide.Classifications, Is.EqualTo(new[]
            {
                ClearanceClassification.Warning,
                ClearanceClassification.BelowTarget,
                ClearanceClassification.Clear
            }));
            Assert.That(narrow.QueryResults, Is.SameAs(wide.QueryResults));
        }

        [Test]
        public void ClassificationCache_ReusesSameRawSnapshotAndEffectiveThresholds()
        {
            var raw = CreateRawEvaluation(0.004f, 0.008f, 0.012f);
            var cache = new ClearanceHeatmapClassificationCache();

            ClearanceHeatmapEvaluation first = cache.Get(raw, -0.001f, 0.01f);
            ClearanceHeatmapEvaluation reused = cache.Get(raw, 0f, 0.01f);

            Assert.That(reused, Is.SameAs(first));
            Assert.That(reused.Classifications, Is.EqualTo(new[]
            {
                ClearanceClassification.BelowTarget,
                ClearanceClassification.BelowTarget,
                ClearanceClassification.Clear
            }));
        }

        [Test]
        public void ClassificationCache_InvalidatesForThresholdOrRawSnapshotChange()
        {
            var firstRaw = CreateRawEvaluation(0.004f, 0.008f);
            var secondRaw = CreateRawEvaluation(0.004f, 0.008f);
            var cache = new ClearanceHeatmapClassificationCache();

            ClearanceHeatmapEvaluation first = cache.Get(firstRaw, 0.003f, 0.006f);
            ClearanceHeatmapEvaluation changedThreshold = cache.Get(firstRaw, 0.005f, 0.01f);
            ClearanceHeatmapEvaluation changedRaw = cache.Get(secondRaw, 0.005f, 0.01f);

            Assert.That(changedThreshold, Is.Not.SameAs(first));
            Assert.That(changedRaw, Is.Not.SameAs(changedThreshold));
            Assert.That(changedRaw.QueryResults, Is.SameAs(secondRaw.QueryResults));
        }

        [TestCase(float.NaN, 0.01f)]
        [TestCase(float.PositiveInfinity, 0.01f)]
        [TestCase(0.005f, float.NaN)]
        [TestCase(0.005f, float.PositiveInfinity)]
        public void NonFiniteThresholds_FailClosed(float warningDistance, float targetDistance)
        {
            var raw = CreateRawEvaluation(-0.001f, 0.02f);

            var evaluation = ClearanceHeatmapEvaluator.Classify(
                raw,
                warningDistance,
                targetDistance);

            Assert.That(evaluation.Status, Is.EqualTo(ClearanceEvaluationStatus.InvalidThresholds));
            Assert.That(evaluation.Classifications, Is.Empty);
            Assert.That(evaluation.Statistics.EvaluatedVertexCount, Is.Zero);
        }

        [Test]
        public void NonFiniteQueryClearance_IsNotClassifiedAsClear()
        {
            var raw = CreateRawEvaluation(float.NaN, float.PositiveInfinity, 0.02f);

            var evaluation = ClearanceHeatmapEvaluator.Classify(raw, 0.005f, 0.01f);

            Assert.That(evaluation.Classifications, Is.EqualTo(new[]
            {
                ClearanceClassification.Invalid,
                ClearanceClassification.Invalid,
                ClearanceClassification.Clear
            }));
            Assert.That(evaluation.Statistics.EvaluatedVertexCount, Is.EqualTo(1));
        }

        [Test]
        public void DisplayModes_SelectExpectedSeverityBands()
        {
            Assert.That(ClearanceHeatmapEvaluator.ShouldDisplay(
                ClearanceClassification.Penetrating,
                ClearanceHeatmapDisplayMode.PenetrationOnly), Is.True);
            Assert.That(ClearanceHeatmapEvaluator.ShouldDisplay(
                ClearanceClassification.Warning,
                ClearanceHeatmapDisplayMode.PenetrationOnly), Is.False);
            Assert.That(ClearanceHeatmapEvaluator.ShouldDisplay(
                ClearanceClassification.Warning,
                ClearanceHeatmapDisplayMode.WarningAndPenetration), Is.True);
            Assert.That(ClearanceHeatmapEvaluator.ShouldDisplay(
                ClearanceClassification.BelowTarget,
                ClearanceHeatmapDisplayMode.WarningAndPenetration), Is.True);
            Assert.That(ClearanceHeatmapEvaluator.ShouldDisplay(
                ClearanceClassification.Clear,
                ClearanceHeatmapDisplayMode.FullDistribution), Is.True);
            Assert.That(ClearanceHeatmapEvaluator.ShouldDisplay(
                ClearanceClassification.Invalid,
                ClearanceHeatmapDisplayMode.FullDistribution), Is.False);
        }

        [Test]
        public void Evaluate_DoesNotModifyTargetMesh()
        {
            var referenceMesh = CreatePlane(0f);
            var targetMesh = CreatePointMesh(0.004f, 0.012f);
            var reference = CreateRenderer("Reference", referenceMesh);
            var target = CreateRenderer("Target", targetMesh);
            var originalVertices = targetMesh.vertices;
            int originalBlendShapeCount = targetMesh.blendShapeCount;
            try
            {
                var raw = ClearanceHeatmapEvaluator.Evaluate(
                    target,
                    reference,
                    ClearanceSignMode.ReferenceNormal);
                var evaluation = ClearanceHeatmapEvaluator.Classify(raw, 0.005f, 0.01f);

                Assert.That(evaluation.Status, Is.EqualTo(ClearanceEvaluationStatus.Valid));
                Assert.That(targetMesh.vertices, Is.EqualTo(originalVertices));
                Assert.That(targetMesh.blendShapeCount, Is.EqualTo(originalBlendShapeCount));
            }
            finally
            {
                DestroyRenderer(target, targetMesh);
                DestroyRenderer(reference, referenceMesh);
            }
        }

        [Test]
        public void InvalidOrDisabledReference_ClearsEvaluationSafely()
        {
            var targetMesh = CreatePointMesh(0.01f);
            var target = CreateRenderer("Target", targetMesh);
            var referenceMesh = CreatePlane(0f);
            var reference = CreateRenderer("Reference", referenceMesh);
            try
            {
                reference.enabled = false;
                var disabled = ClearanceHeatmapEvaluator.Evaluate(
                    target, reference, ClearanceSignMode.ReferenceNormal);
                Assert.That(disabled.Status, Is.EqualTo(ClearanceEvaluationStatus.InvalidReference));
                Assert.That(disabled.QueryResults, Is.Empty);

                Object.DestroyImmediate(reference.gameObject);
                var deleted = ClearanceHeatmapEvaluator.Evaluate(
                    target, reference, ClearanceSignMode.ReferenceNormal);
                Assert.That(deleted.Status, Is.EqualTo(ClearanceEvaluationStatus.InvalidReference));
                Assert.That(deleted.WorldPositions, Is.Empty);
            }
            finally
            {
                if (reference != null) Object.DestroyImmediate(reference.gameObject);
                Object.DestroyImmediate(referenceMesh);
                DestroyRenderer(target, targetMesh);
            }
        }

        [Test]
        public void SwitchingReferenceRenderer_DoesNotReuseOldDistances()
        {
            var targetMesh = CreatePointMesh(0.02f);
            var target = CreateRenderer("Target", targetMesh);
            var nearMesh = CreatePlane(0f);
            var farMesh = CreatePlane(-0.1f);
            var near = CreateRenderer("Near", nearMesh);
            var far = CreateRenderer("Far", farMesh);
            try
            {
                var nearRaw = ClearanceHeatmapEvaluator.Evaluate(
                    target, near, ClearanceSignMode.ReferenceNormal);
                var farRaw = ClearanceHeatmapEvaluator.Evaluate(
                    target, far, ClearanceSignMode.ReferenceNormal);

                Assert.That(nearRaw.QueryResults[0].Distance, Is.EqualTo(0.02f).Within(Epsilon));
                Assert.That(farRaw.QueryResults[0].Distance, Is.EqualTo(0.12f).Within(Epsilon));
            }
            finally
            {
                DestroyRenderer(far, farMesh);
                DestroyRenderer(near, nearMesh);
                DestroyRenderer(target, targetMesh);
            }
        }

        [Test]
        public void ReplacingReferenceMesh_UpdatesEvaluationWithoutStaleResults()
        {
            var targetMesh = CreatePointMesh(0.02f);
            var target = CreateRenderer("Target", targetMesh);
            var nearMesh = CreatePlane(0f);
            var farMesh = CreatePlane(-0.2f);
            var reference = CreateRenderer("Reference", nearMesh);
            try
            {
                var near = ClearanceHeatmapEvaluator.Evaluate(
                    target, reference, ClearanceSignMode.ReferenceNormal);
                reference.GetComponent<MeshFilter>().sharedMesh = farMesh;
                var far = ClearanceHeatmapEvaluator.Evaluate(
                    target, reference, ClearanceSignMode.ReferenceNormal);

                Assert.That(near.QueryResults[0].Distance, Is.EqualTo(0.02f).Within(Epsilon));
                Assert.That(far.QueryResults[0].Distance, Is.EqualTo(0.22f).Within(Epsilon));
            }
            finally
            {
                Object.DestroyImmediate(reference.gameObject);
                Object.DestroyImmediate(nearMesh);
                Object.DestroyImmediate(farMesh);
                DestroyRenderer(target, targetMesh);
            }
        }

        [Test]
        public void SkinnedBlendShapeWeightChange_UpdatesEvaluatedPositions()
        {
            var referenceMesh = CreatePlane(0f);
            var reference = CreateRenderer("Reference", referenceMesh);
            var targetMesh = CreatePointMesh(0.01f);
            targetMesh.AddBlendShapeFrame(
                "Move",
                100f,
                new[] { Vector3.forward * 0.02f },
                null,
                null);
            var root = new GameObject("Skinned Target");
            var bone = new GameObject("Bone");
            bone.transform.SetParent(root.transform, false);
            var target = root.AddComponent<SkinnedMeshRenderer>();
            targetMesh.boneWeights = new[] { new BoneWeight { boneIndex0 = 0, weight0 = 1f } };
            targetMesh.bindposes = new[] { bone.transform.worldToLocalMatrix * root.transform.localToWorldMatrix };
            target.sharedMesh = targetMesh;
            target.bones = new[] { bone.transform };
            target.rootBone = bone.transform;
            try
            {
                target.SetBlendShapeWeight(0, 0f);
                var neutral = ClearanceHeatmapEvaluator.Evaluate(
                    target, reference, ClearanceSignMode.ReferenceNormal);
                target.SetBlendShapeWeight(0, 100f);
                var moved = ClearanceHeatmapEvaluator.Evaluate(
                    target, reference, ClearanceSignMode.ReferenceNormal);
                bone.transform.localPosition = Vector3.forward * 0.01f;
                var posed = ClearanceHeatmapEvaluator.Evaluate(
                    target, reference, ClearanceSignMode.ReferenceNormal);

                Assert.That(neutral.QueryResults[0].Distance, Is.EqualTo(0.01f).Within(Epsilon));
                Assert.That(moved.QueryResults[0].Distance, Is.EqualTo(0.03f).Within(Epsilon));
                Assert.That(moved.WorldPositions[0].z - neutral.WorldPositions[0].z,
                    Is.EqualTo(0.02f).Within(Epsilon));
                Assert.That(posed.QueryResults[0].Distance, Is.EqualTo(0.04f).Within(Epsilon));
                Assert.That(moved.TargetStateHash, Is.Not.EqualTo(neutral.TargetStateHash));
                Assert.That(posed.TargetStateHash, Is.Not.EqualTo(moved.TargetStateHash));
                Assert.That(moved.ReferenceStateHash, Is.EqualTo(neutral.ReferenceStateHash));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(targetMesh);
                DestroyRenderer(reference, referenceMesh);
            }
        }

        [Test]
        public void NdmfPreviewProxy_IsSelectedAsClearanceEvaluationTarget()
        {
            var originalMesh = CreatePointMesh(0.01f);
            var proxyMesh = CreatePointMesh(0.03f);
            var original = CreateRenderer("Original", originalMesh);
            var proxy = CreateRenderer("Preview Proxy", proxyMesh);
            var deformer = original.gameObject.AddComponent<LatticeDeformer>();
            var serializedDeformer = new SerializedObject(deformer);
            serializedDeformer.FindProperty("_meshFilter").objectReferenceValue =
                original.GetComponent<MeshFilter>();
            serializedDeformer.ApplyModifiedPropertiesWithoutUndo();
            var session = new FakePreviewSession();
            session.OriginalToProxy[original] = proxy;
            try
            {
                Assert.That(NDMFPreviewProxyUtility.TryGetProxyRenderer(
                    original,
                    session,
                    out Renderer resolvedProxy),
                    Is.True);

                Renderer selected = LatticeDeformerEditor.ResolveClearanceTargetRenderer(
                    deformer,
                    resolvedProxy,
                    out bool usedPreviewProxy);

                Assert.That(usedPreviewProxy, Is.True);
                Assert.That(selected, Is.SameAs(proxy));
                Assert.That(ClearanceQueryCache.TryGetWorldVertices(selected, out Vector3[] positions), Is.True);
                Assert.That(positions[0].z, Is.EqualTo(0.03f).Within(Epsilon));
            }
            finally
            {
                DestroyRenderer(proxy, proxyMesh);
                DestroyRenderer(original, originalMesh);
            }
        }

        [Test]
        public void UndoRedo_InvalidatesEditorClearanceCache()
        {
            var targetMesh = CreatePointMesh(0.01f);
            var target = CreateRenderer("Undo Target", targetMesh);
            var deformer = target.gameObject.AddComponent<LatticeDeformer>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(
                deformer,
                typeof(LatticeDeformerEditor));
            var cacheField = typeof(LatticeDeformerEditor).GetField(
                "_clearanceRawEvaluation",
                BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                Assert.That(cacheField, Is.Not.Null);
                cacheField.SetValue(editor, CreateRawEvaluation(0.01f));
                Assert.That(cacheField.GetValue(editor), Is.Not.Null);

                Undo.RegisterCompleteObjectUndo(deformer, "Clearance cache invalidation");
                deformer.ShowClearanceHeatmap = true;
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();

                Assert.That(cacheField.GetValue(editor), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(editor);
                DestroyRenderer(target, targetMesh);
                Undo.ClearAll();
            }
        }

        [Test]
        public void QueryModeChange_InvalidatesEditorRawAndClassificationCachesImmediately()
        {
            var targetMesh = CreatePointMesh(0.01f);
            var target = CreateRenderer("Mode Target", targetMesh);
            var referenceMesh = CreatePlane(0f);
            var reference = CreateRenderer("Mode Reference", referenceMesh);
            var deformer = target.gameObject.AddComponent<LatticeDeformer>();
            var serializedDeformer = new SerializedObject(deformer);
            serializedDeformer.FindProperty("_meshFilter").objectReferenceValue =
                target.GetComponent<MeshFilter>();
            serializedDeformer.ApplyModifiedPropertiesWithoutUndo();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(
                deformer,
                typeof(LatticeDeformerEditor));
            MethodInfo getEvaluation = typeof(LatticeDeformerEditor).GetMethod(
                "GetClearanceEvaluation",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var rawField = typeof(LatticeDeformerEditor).GetField(
                "_clearanceRawEvaluation",
                BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                Assert.That(getEvaluation, Is.Not.Null);
                Assert.That(rawField, Is.Not.Null);
                getEvaluation.Invoke(editor, new object[]
                {
                    deformer,
                    reference,
                    ClearanceQueryMode.ReferenceNormal,
                    0.005f,
                    0.01f,
                    2f
                });
                object firstRaw = rawField.GetValue(editor);

                getEvaluation.Invoke(editor, new object[]
                {
                    deformer,
                    reference,
                    ClearanceQueryMode.ClosedMesh,
                    0.005f,
                    0.01f,
                    2f
                });
                object secondRaw = rawField.GetValue(editor);

                Assert.That(firstRaw, Is.Not.Null);
                Assert.That(secondRaw, Is.Not.SameAs(firstRaw));
            }
            finally
            {
                Object.DestroyImmediate(editor);
                DestroyRenderer(reference, referenceMesh);
                DestroyRenderer(target, targetMesh);
            }
        }

        [Test]
        public void DeformerClearanceSettings_ClampAndSerialize()
        {
            var gameObject = new GameObject("Settings");
            try
            {
                var deformer = gameObject.AddComponent<LatticeDeformer>();
                deformer.ClearanceWarningDistance = -1f;
                deformer.ClearanceTargetDistance = -1f;
                deformer.ClearanceDisplayStride = 1000;
                deformer.ClearanceUpdateInterval = 0f;
                deformer.ClearanceQueryMode = ClearanceQueryMode.ClosedMesh;
                deformer.ClearanceHeatmapDisplayMode = ClearanceHeatmapDisplayMode.FullDistribution;

                Assert.That(deformer.ClearanceWarningDistance, Is.EqualTo(0f));
                Assert.That(deformer.ClearanceTargetDistance, Is.EqualTo(0f));
                Assert.That(deformer.ClearanceDisplayStride, Is.EqualTo(64));
                Assert.That(deformer.ClearanceUpdateInterval, Is.EqualTo(0.02f).Within(Epsilon));

                deformer.ClearanceWarningDistance = float.NaN;
                deformer.ClearanceTargetDistance = float.PositiveInfinity;
                deformer.ClearanceUpdateInterval = float.NaN;
                Assert.That(deformer.ClearanceWarningDistance, Is.EqualTo(0f));
                Assert.That(deformer.ClearanceTargetDistance, Is.EqualTo(0f));
                Assert.That(deformer.ClearanceUpdateInterval, Is.EqualTo(0.1f).Within(Epsilon));

                string json = JsonUtility.ToJson(deformer);
                var copyObject = new GameObject("Settings Copy");
                try
                {
                    var copy = copyObject.AddComponent<LatticeDeformer>();
                    JsonUtility.FromJsonOverwrite(json, copy);
                    Assert.That(copy.ClearanceQueryMode, Is.EqualTo(ClearanceQueryMode.ClosedMesh));
                    Assert.That(copy.ClearanceHeatmapDisplayMode,
                        Is.EqualTo(ClearanceHeatmapDisplayMode.FullDistribution));
                }
                finally
                {
                    Object.DestroyImmediate(copyObject);
                }
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        private static ClearanceHeatmapRawEvaluation CreateRawEvaluation(params float[] clearances)
        {
            var positions = new Vector3[clearances.Length];
            var results = new ClearanceQueryResult[clearances.Length];
            for (int i = 0; i < clearances.Length; i++)
            {
                results[i] = new ClearanceQueryResult(
                    0,
                    Vector3.zero,
                    new Vector3(1f, 0f, 0f),
                    Vector3.forward,
                    Mathf.Abs(clearances[i]),
                    clearances[i],
                    false,
                    false,
                    ClearanceSignMode.ReferenceNormal,
                    1);
            }
            return new ClearanceHeatmapRawEvaluation(
                positions,
                results,
                ClearanceEvaluationStatus.Valid);
        }

        private static Mesh CreatePlane(float z)
        {
            var mesh = new Mesh { name = "Reference Plane" };
            mesh.vertices = new[]
            {
                new Vector3(-1f, -1f, z), new Vector3(1f, -1f, z),
                new Vector3(1f, 1f, z), new Vector3(-1f, 1f, z)
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreatePointMesh(params float[] zPositions)
        {
            var vertices = new Vector3[zPositions.Length];
            for (int i = 0; i < zPositions.Length; i++) vertices[i] = Vector3.forward * zPositions[i];
            var mesh = new Mesh { name = "Target Points", vertices = vertices };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static MeshRenderer CreateRenderer(string name, Mesh mesh)
        {
            var gameObject = new GameObject(name);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            return gameObject.AddComponent<MeshRenderer>();
        }

        private static void DestroyRenderer(Renderer renderer, Mesh mesh)
        {
            if (renderer != null) Object.DestroyImmediate(renderer.gameObject);
            if (mesh != null) Object.DestroyImmediate(mesh);
        }

        private sealed class FakePreviewSession
        {
            public Dictionary<Renderer, Renderer> OriginalToProxy { get; } =
                new Dictionary<Renderer, Renderer>();
        }
    }
}
#endif
