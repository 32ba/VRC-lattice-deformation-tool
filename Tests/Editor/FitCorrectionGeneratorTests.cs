#if UNITY_EDITOR
using System;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class FitCorrectionGeneratorTests
    {
        private const float Epsilon = 2e-4f;

        [SetUp]
        public void SetUp()
        {
            ClearanceQueryCache.Clear();
            Undo.ClearAll();
        }

        [Test]
        public void Generate_CreatesNewBrushLayerAndMovesCandidatesToTargetClearance()
        {
            var fixture = CreateFixture(-0.002f, 0.003f, 0.008f, 0.02f);
            try
            {
                Vector3[] sourceBefore = fixture.TargetMesh.vertices;
                int layerCountBefore = fixture.Deformer.Layers.Count;
                var existingLayer = fixture.Deformer.Layers[0];
                var raw = Evaluate(fixture);
                var plan = Analyze(fixture, raw, FitCorrectionScope.TargetClearance, 0.1f);

                var report = FitCorrectionGenerator.Generate(
                    fixture.Deformer,
                    plan,
                    fixture.Reference,
                    ClearanceQueryMode.ReferenceNormal,
                    FitCorrectionScope.TargetClearance,
                    0.005f,
                    0.01f,
                    0.1f);

                Assert.That(report.Status, Is.EqualTo(FitCorrectionStatus.Success));
                Assert.That(report.MovedVertexCount, Is.EqualTo(3));
                Assert.That(report.ImprovedVertexCount, Is.EqualTo(3));
                Assert.That(report.UnresolvedVertexCount, Is.EqualTo(0));
                Assert.That(fixture.Deformer.Layers.Count, Is.EqualTo(layerCountBefore + 1));
                Assert.That(fixture.Deformer.Layers[0], Is.SameAs(existingLayer));

                var layer = fixture.Deformer.Layers[report.LayerIndex];
                Assert.That(layer.Type, Is.EqualTo(MeshDeformerLayerType.Brush));
                Assert.That(layer.Weight, Is.EqualTo(1f));
                Assert.That(layer.Enabled, Is.True);
                Assert.That(layer.GetBrushDisplacement(0).z, Is.EqualTo(0.012f).Within(Epsilon));
                Assert.That(layer.GetBrushDisplacement(1).z, Is.EqualTo(0.007f).Within(Epsilon));
                Assert.That(layer.GetBrushDisplacement(2).z, Is.EqualTo(0.002f).Within(Epsilon));
                Assert.That(layer.GetBrushDisplacement(3), Is.EqualTo(Vector3.zero));
                Assert.That(fixture.TargetMesh.vertices, Is.EqualTo(sourceBefore));

                layer.Weight = 0.5f;
                layer.EnsureVertexMaskCapacity(fixture.TargetMesh.vertexCount);
                layer.SetVertexMask(0, 0f);
                Assert.That(layer.Weight, Is.EqualTo(0.5f));
                Assert.That(layer.GetVertexMask(0), Is.EqualTo(0f));
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void MaximumMove_ClampsWorldDisplacementAndReportsUnresolvedVertices()
        {
            var fixture = CreateFixture(-0.02f, 0.002f, 0.02f);
            try
            {
                var plan = Analyze(fixture, Evaluate(fixture), FitCorrectionScope.TargetClearance, 0.005f);
                var report = FitCorrectionGenerator.Generate(
                    fixture.Deformer, plan, fixture.Reference,
                    ClearanceQueryMode.ReferenceNormal, FitCorrectionScope.TargetClearance,
                    0.005f, 0.01f, 0.005f);
                var layer = fixture.Deformer.Layers[report.LayerIndex];

                Assert.That(plan.MaximumAppliedMove, Is.EqualTo(0.005f).Within(Epsilon));
                Assert.That(plan.UnresolvedVertexCount, Is.EqualTo(2));
                Assert.That(report.UnresolvedVertexCount, Is.EqualTo(2));
                for (int vertex = 0; vertex < layer.BrushDisplacementCount; vertex++)
                {
                    Vector3 worldMove = fixture.Target.transform.localToWorldMatrix.MultiplyVector(
                        layer.GetBrushDisplacement(vertex));
                    Assert.That(worldMove.magnitude, Is.LessThanOrEqualTo(0.005f + Epsilon));
                }
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void NonFiniteSettings_FailClosedWithoutCreatingLayer()
        {
            var fixture = CreateFixture(-0.002f, 0.003f);
            try
            {
                int originalLayerCount = fixture.Deformer.Layers.Count;
                var raw = Evaluate(fixture);

                var invalidPlan = Analyze(
                    fixture,
                    raw,
                    FitCorrectionScope.TargetClearance,
                    float.NaN);
                Assert.That(invalidPlan.Status, Is.EqualTo(FitCorrectionStatus.InvalidSettings));
                Assert.That(invalidPlan.CanGenerate, Is.False);

                var validPlan = Analyze(
                    fixture,
                    raw,
                    FitCorrectionScope.TargetClearance,
                    0.1f);
                var report = FitCorrectionGenerator.Generate(
                    fixture.Deformer,
                    validPlan,
                    fixture.Reference,
                    ClearanceQueryMode.ReferenceNormal,
                    FitCorrectionScope.TargetClearance,
                    0.005f,
                    0.01f,
                    float.PositiveInfinity);

                Assert.That(report.Status, Is.EqualTo(FitCorrectionStatus.InvalidSettings));
                Assert.That(fixture.Deformer.Layers.Count, Is.EqualTo(originalLayerCount));

                fixture.Deformer.FitCorrectionMaximumMove = float.NaN;
                Assert.That(fixture.Deformer.FitCorrectionMaximumMove, Is.EqualTo(0f));
                var layer = fixture.Deformer.Layers[0];
                layer.ConfigureFitCorrection(
                    fixture.Reference,
                    ClearanceQueryMode.ReferenceNormal,
                    FitCorrectionScope.TargetClearance,
                    float.NaN,
                    float.PositiveInfinity,
                    float.NaN);
                Assert.That(layer.FitCorrectionWarningDistance, Is.EqualTo(0f));
                Assert.That(layer.FitCorrectionTargetDistance, Is.EqualTo(0f));
                Assert.That(layer.FitCorrectionMaximumMove, Is.EqualTo(0f));

                var invalidConstraints = new FitCorrectionConstraintOptions(
                    false, false, false, true, 1, float.NaN,
                    false, true, 0, float.PositiveInfinity);
                var invalidConstraintPlan = FitCorrectionGenerator.Analyze(
                    fixture.Deformer,
                    raw,
                    fixture.Reference,
                    ClearanceQueryMode.ReferenceNormal,
                    FitCorrectionScope.TargetClearance,
                    0.005f,
                    0.01f,
                    0.1f,
                    invalidConstraints);
                Assert.That(invalidConstraintPlan.Status, Is.EqualTo(FitCorrectionStatus.InvalidSettings));

                layer.ConfigureFitCorrectionConstraints(
                    true,
                    new[] { float.NaN, float.PositiveInfinity },
                    false,
                    false,
                    true,
                    1,
                    float.NaN,
                    false,
                    true,
                    0,
                    float.PositiveInfinity);
                Assert.That(layer.FitCorrectionConstraintMask, Is.EqualTo(new[] { 0f, 0f }));
                Assert.That(layer.FitCorrectionSmoothingStrength, Is.EqualTo(0f));
                Assert.That(layer.FitCorrectionSymmetryTolerance, Is.EqualTo(1e-4f).Within(Epsilon));
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void SingularTargetTransform_FailsClosedWithoutCreatingLayer()
        {
            var fixture = CreateFixture(-0.002f, 0.003f);
            try
            {
                fixture.Target.transform.localScale = new Vector3(0f, 1f, 1f);
                var raw = Evaluate(fixture);
                int originalLayerCount = fixture.Deformer.Layers.Count;

                var plan = Analyze(
                    fixture,
                    raw,
                    FitCorrectionScope.TargetClearance,
                    0.1f);
                var report = FitCorrectionGenerator.Generate(
                    fixture.Deformer,
                    plan,
                    fixture.Reference,
                    ClearanceQueryMode.ReferenceNormal,
                    FitCorrectionScope.TargetClearance,
                    0.005f,
                    0.01f,
                    0.1f);

                Assert.That(plan.Status, Is.EqualTo(FitCorrectionStatus.InvalidTargetTransform));
                Assert.That(report.Status, Is.EqualTo(FitCorrectionStatus.InvalidTargetTransform));
                Assert.That(fixture.Deformer.Layers.Count, Is.EqualTo(originalLayerCount));
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void CorrectionScopes_SelectDifferentVertexSets()
        {
            var fixture = CreateFixture(-0.002f, 0.003f, 0.008f, 0.02f);
            try
            {
                var raw = Evaluate(fixture);
                Assert.That(Analyze(fixture, raw, FitCorrectionScope.PenetrationOnly, 0.1f)
                    .CandidateVertexCount, Is.EqualTo(1));
                Assert.That(Analyze(fixture, raw, FitCorrectionScope.WarningThreshold, 0.1f)
                    .CandidateVertexCount, Is.EqualTo(2));
                Assert.That(Analyze(fixture, raw, FitCorrectionScope.TargetClearance, 0.1f)
                    .CandidateVertexCount, Is.EqualTo(3));
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void GeneratedLayer_RecordsReproducibleSettingsAndPreservesBlendShapes()
        {
            var fixture = CreateFixture(-0.002f, 0.003f, 0.02f);
            var blendShapeDeltas = new[] { Vector3.right, Vector3.right, Vector3.right };
            fixture.TargetMesh.AddBlendShapeFrame("Original", 100f, blendShapeDeltas, null, null);
            try
            {
                var plan = Analyze(fixture, Evaluate(fixture), FitCorrectionScope.WarningThreshold, 0.006f);
                var report = FitCorrectionGenerator.Generate(
                    fixture.Deformer, plan, fixture.Reference,
                    ClearanceQueryMode.ReferenceNormal, FitCorrectionScope.WarningThreshold,
                    0.005f, 0.01f, 0.006f);
                var layer = fixture.Deformer.Layers[report.LayerIndex];

                Assert.That(layer.IsFitCorrection, Is.True);
                Assert.That(layer.FitCorrectionReferenceRenderer, Is.SameAs(fixture.Reference));
                Assert.That(layer.FitCorrectionQueryMode, Is.EqualTo(ClearanceQueryMode.ReferenceNormal));
                Assert.That(layer.FitCorrectionScope, Is.EqualTo(FitCorrectionScope.WarningThreshold));
                Assert.That(layer.FitCorrectionWarningDistance, Is.EqualTo(0.005f).Within(Epsilon));
                Assert.That(layer.FitCorrectionTargetDistance, Is.EqualTo(0.01f).Within(Epsilon));
                Assert.That(layer.FitCorrectionMaximumMove, Is.EqualTo(0.006f).Within(Epsilon));
                Assert.That(fixture.TargetMesh.blendShapeCount, Is.EqualTo(1));
                var actualDeltas = new Vector3[fixture.TargetMesh.vertexCount];
                fixture.TargetMesh.GetBlendShapeFrameVertices(0, 0, actualDeltas, null, null);
                Assert.That(actualDeltas, Is.EqualTo(blendShapeDeltas));
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void GeneratedLayer_SettingsAndDisplacementsSurvivePrefabReload()
        {
            const string prefabPath = "Assets/__FitCorrectionGeneratorTests.prefab";
            var fixture = CreateFixture(-0.002f, 0.02f, 0.02f);
            var root = new GameObject("Fit Correction Prefab");
            try
            {
                fixture.Target.transform.SetParent(root.transform);
                fixture.Reference.transform.SetParent(root.transform);
                fixture.Deformer.Layers[fixture.Deformer.ActiveLayerIndex].VertexMask =
                    new[] { 1f, 0.5f, 0f };
                var plan = FitCorrectionGenerator.Analyze(
                    fixture.Deformer, Evaluate(fixture), fixture.Reference,
                    ClearanceQueryMode.ReferenceNormal, FitCorrectionScope.TargetClearance,
                    0.005f, 0.01f, 0.006f,
                    Constraints(useMask: true, isolate: true, preserve: true));
                var report = FitCorrectionGenerator.Generate(
                    fixture.Deformer, plan, fixture.Reference,
                    ClearanceQueryMode.ReferenceNormal, FitCorrectionScope.TargetClearance,
                    0.005f, 0.01f, 0.006f);
                Vector3 expectedDisplacement = fixture.Deformer.Layers[report.LayerIndex]
                    .GetBrushDisplacement(0);

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Object.DestroyImmediate(root);
                root = null;

                var loadedRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    var loadedDeformer = loadedRoot.GetComponentInChildren<LatticeDeformer>();
                    var loadedReference = loadedRoot.transform.Find("Reference")
                        .GetComponent<MeshRenderer>();
                    var loadedLayer = loadedDeformer.Layers[loadedDeformer.Layers.Count - 1];

                    Assert.That(loadedLayer.IsFitCorrection, Is.True);
                    Assert.That(loadedLayer.FitCorrectionReferenceRenderer, Is.SameAs(loadedReference));
                    Assert.That(loadedLayer.FitCorrectionScope, Is.EqualTo(FitCorrectionScope.TargetClearance));
                    Assert.That(loadedLayer.FitCorrectionTargetDistance, Is.EqualTo(0.01f).Within(Epsilon));
                    Assert.That(loadedLayer.FitCorrectionMaximumMove, Is.EqualTo(0.006f).Within(Epsilon));
                    Assert.That(loadedLayer.FitCorrectionUsedVertexMask, Is.True);
                    Assert.That(loadedLayer.FitCorrectionConstraintMask,
                        Is.EqualTo(new[] { 1f, 0.5f, 0f }));
                    Assert.That(loadedLayer.FitCorrectionIsolatedComponents, Is.True);
                    Assert.That(loadedLayer.FitCorrectionPreservedClearance, Is.True);
                    Assert.That(loadedLayer.GetBrushDisplacement(0), Is.EqualTo(expectedDisplacement));
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(loadedRoot);
                }
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                AssetDatabase.DeleteAsset(prefabPath);
                fixture.Destroy();
            }
        }

        [Test]
        public void CompleteObjectUndo_RemovesAndRestoresGeneratedLayer()
        {
            var fixture = CreateFixture(-0.002f, 0.02f, 0.02f);
            try
            {
                int before = fixture.Deformer.Layers.Count;
                var plan = Analyze(fixture, Evaluate(fixture), FitCorrectionScope.TargetClearance, 0.1f);
                Undo.RegisterCompleteObjectUndo(fixture.Deformer, "Create Fit Correction Layer");
                FitCorrectionGenerator.Generate(
                    fixture.Deformer, plan, fixture.Reference,
                    ClearanceQueryMode.ReferenceNormal, FitCorrectionScope.TargetClearance,
                    0.005f, 0.01f, 0.1f);
                Undo.FlushUndoRecordObjects();
                Assert.That(fixture.Deformer.Layers.Count, Is.EqualTo(before + 1));

                Undo.PerformUndo();
                Assert.That(fixture.Deformer.Layers.Count, Is.EqualTo(before));
                Undo.PerformRedo();
                Assert.That(fixture.Deformer.Layers.Count, Is.EqualTo(before + 1));
                Assert.That(fixture.Deformer.Layers[fixture.Deformer.Layers.Count - 1].IsFitCorrection, Is.True);
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void StaleEvaluation_DoesNotGenerateAfterTransformChanges()
        {
            var fixture = CreateFixture(-0.002f, 0.02f, 0.02f);
            try
            {
                var raw = Evaluate(fixture);
                int layersBefore = fixture.Deformer.Layers.Count;
                fixture.Target.transform.position += Vector3.right;

                var plan = Analyze(fixture, raw, FitCorrectionScope.TargetClearance, 0.1f);
                var report = FitCorrectionGenerator.Generate(
                    fixture.Deformer, plan, fixture.Reference,
                    ClearanceQueryMode.ReferenceNormal, FitCorrectionScope.TargetClearance,
                    0.005f, 0.01f, 0.1f);

                Assert.That(plan.Status, Is.EqualTo(FitCorrectionStatus.StaleEvaluation));
                Assert.That(report.Status, Is.EqualTo(FitCorrectionStatus.StaleEvaluation));
                Assert.That(fixture.Deformer.Layers.Count, Is.EqualTo(layersBefore));
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void TopologyMismatch_DoesNotGenerateLayer()
        {
            var fixture = CreateFixture(-0.002f, 0.02f, 0.02f);
            try
            {
                var evaluated = Evaluate(fixture);
                var raw = new ClearanceHeatmapRawEvaluation(
                    new[] { evaluated.WorldPositions[0], evaluated.WorldPositions[1] },
                    new[] { evaluated.QueryResults[0], evaluated.QueryResults[1] },
                    ClearanceEvaluationStatus.Valid,
                    evaluated.TargetRenderer,
                    evaluated.ReferenceRenderer,
                    evaluated.TargetStateHash,
                    evaluated.ReferenceStateHash);
                int layersBefore = fixture.Deformer.Layers.Count;
                var plan = Analyze(fixture, raw, FitCorrectionScope.TargetClearance, 0.1f);

                Assert.That(plan.Status, Is.EqualTo(FitCorrectionStatus.TopologyMismatch));
                Assert.That(fixture.Deformer.Layers.Count, Is.EqualTo(layersBefore));
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void PosedSkinnedMesh_IsBlockedWhileRestPoseIsAccepted()
        {
            var targetMesh = CreateTargetMesh(-0.002f, 0.02f, 0.02f);
            var referenceMesh = CreateReferencePlane();
            var reference = CreateRenderer("Reference", referenceMesh);
            var root = new GameObject("Skinned Target");
            var bone = new GameObject("Bone");
            bone.transform.SetParent(root.transform, false);
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            var weights = new BoneWeight[targetMesh.vertexCount];
            for (int i = 0; i < weights.Length; i++)
                weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            targetMesh.boneWeights = weights;
            targetMesh.bindposes = new[] { bone.transform.worldToLocalMatrix * root.transform.localToWorldMatrix };
            renderer.sharedMesh = targetMesh;
            renderer.bones = new[] { bone.transform };
            renderer.rootBone = bone.transform;
            var deformer = root.AddComponent<LatticeDeformer>();
            deformer.Reset();
            try
            {
                var restRaw = ClearanceHeatmapEvaluator.Evaluate(
                    renderer, reference, ClearanceSignMode.ReferenceNormal);
                var restPlan = FitCorrectionGenerator.Analyze(
                    deformer, restRaw, reference, ClearanceQueryMode.ReferenceNormal,
                    FitCorrectionScope.TargetClearance, 0.005f, 0.01f, 0.1f);
                Assert.That(restPlan.Status, Is.EqualTo(FitCorrectionStatus.Ready));

                bone.transform.localPosition = Vector3.forward * 0.01f;
                var posedRaw = ClearanceHeatmapEvaluator.Evaluate(
                    renderer, reference, ClearanceSignMode.ReferenceNormal);
                var posedPlan = FitCorrectionGenerator.Analyze(
                    deformer, posedRaw, reference, ClearanceQueryMode.ReferenceNormal,
                    FitCorrectionScope.TargetClearance, 0.005f, 0.01f, 0.1f);
                Assert.That(posedPlan.Status, Is.EqualTo(FitCorrectionStatus.PosedSkinnedMeshUnsupported));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(targetMesh);
                Object.DestroyImmediate(reference.gameObject);
                Object.DestroyImmediate(referenceMesh);
            }
        }

        [Test]
        public void NonUniformScale_ClampsMovementInWorldSpace()
        {
            var fixture = CreateFixture(-0.02f, 0.02f, 0.02f);
            fixture.Target.transform.localScale = new Vector3(2f, 3f, 0.5f);
            try
            {
                var plan = Analyze(fixture, Evaluate(fixture), FitCorrectionScope.TargetClearance, 0.004f);
                var report = FitCorrectionGenerator.Generate(
                    fixture.Deformer, plan, fixture.Reference,
                    ClearanceQueryMode.ReferenceNormal, FitCorrectionScope.TargetClearance,
                    0.005f, 0.01f, 0.004f);
                Vector3 local = fixture.Deformer.Layers[report.LayerIndex].GetBrushDisplacement(0);
                Vector3 world = fixture.Target.transform.localToWorldMatrix.MultiplyVector(local);

                Assert.That(world.magnitude, Is.EqualTo(0.004f).Within(Epsilon));
                Assert.That(local.z, Is.EqualTo(0.008f).Within(Epsilon));
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void DisabledConstraints_MatchIssue31BasicResult()
        {
            var fixture = CreateFixture(-0.002f, 0.003f, 0.008f);
            try
            {
                var raw = Evaluate(fixture);
                var basic = Analyze(fixture, raw, FitCorrectionScope.TargetClearance, 0.1f);
                var disabled = FitCorrectionGenerator.Analyze(
                    fixture.Deformer, raw, fixture.Reference,
                    ClearanceQueryMode.ReferenceNormal, FitCorrectionScope.TargetClearance,
                    0.005f, 0.01f, 0.1f, DisabledConstraints());

                Assert.That(disabled.LocalDisplacements, Is.EqualTo(basic.LocalDisplacements));
                Assert.That(disabled.CorrectedWorldPositions, Is.EqualTo(basic.CorrectedWorldPositions));
                Assert.That(disabled.UnresolvedVertexCount, Is.EqualTo(basic.UnresolvedVertexCount));
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void VertexMask_ProducesContinuousGradientAndPinsZeroWeight()
        {
            var fixture = CreateFixture(0f, 0f, 0f);
            fixture.Deformer.Layers[fixture.Deformer.ActiveLayerIndex].VertexMask = new[] { 0f, 0.5f, 1f };
            try
            {
                var plan = FitCorrectionGenerator.Analyze(
                    fixture.Deformer, Evaluate(fixture), fixture.Reference,
                    ClearanceQueryMode.ReferenceNormal, FitCorrectionScope.TargetClearance,
                    0.005f, 0.01f, 0.1f,
                    Constraints(useMask: true, preserve: true));

                Assert.That(plan.LocalDisplacements[0], Is.EqualTo(Vector3.zero));
                Assert.That(plan.LocalDisplacements[1].z, Is.EqualTo(0.005f).Within(Epsilon));
                Assert.That(plan.LocalDisplacements[2].z, Is.EqualTo(0.01f).Within(Epsilon));
                Assert.That(plan.PinnedVertices[0], Is.True);
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void PinOpenBoundaries_MovesOnlyInteriorGridVertex()
        {
            var vertices = new Vector3[9];
            for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                vertices[y * 3 + x] = new Vector3(x - 1f, y - 1f, 0f) * 0.4f;
            int[] triangles =
            {
                0, 1, 3, 1, 4, 3,
                1, 2, 4, 2, 5, 4,
                3, 4, 6, 4, 7, 6,
                4, 5, 7, 5, 8, 7
            };
            var fixture = CreateCustomFixture(vertices, triangles);
            try
            {
                var plan = FitCorrectionGenerator.Analyze(
                    fixture.Deformer, Evaluate(fixture), fixture.Reference,
                    ClearanceQueryMode.ReferenceNormal, FitCorrectionScope.TargetClearance,
                    0.005f, 0.01f, 0.1f,
                    Constraints(pinBoundaries: true));

                for (int vertex = 0; vertex < vertices.Length; vertex++)
                {
                    if (vertex == 4)
                        Assert.That(plan.LocalDisplacements[vertex].z, Is.EqualTo(0.01f).Within(Epsilon));
                    else
                        Assert.That(plan.LocalDisplacements[vertex], Is.EqualTo(Vector3.zero));
                }
                Assert.That(plan.UnresolvedVertexCount, Is.EqualTo(8));
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void SurfaceSmoothing_DoesNotCrossDisconnectedIslands()
        {
            var fixture = CreateCustomFixture(
                new[]
                {
                    new Vector3(-0.8f, -0.2f, 0f), new Vector3(-0.4f, -0.2f, 0.02f),
                    new Vector3(-0.6f, 0.2f, 0.02f),
                    new Vector3(0.4f, -0.2f, 0.02f), new Vector3(0.8f, -0.2f, 0.02f),
                    new Vector3(0.6f, 0.2f, 0.02f)
                },
                new[] { 0, 1, 2, 3, 4, 5 });
            try
            {
                var plan = FitCorrectionGenerator.Analyze(
                    fixture.Deformer, Evaluate(fixture), fixture.Reference,
                    ClearanceQueryMode.ReferenceNormal, FitCorrectionScope.TargetClearance,
                    0.005f, 0.01f, 0.1f,
                    Constraints(isolate: true, smooth: true, iterations: 1, strength: 1f));

                Assert.That(plan.LocalDisplacements[1].sqrMagnitude, Is.GreaterThan(0f));
                Assert.That(plan.LocalDisplacements[2].sqrMagnitude, Is.GreaterThan(0f));
                Assert.That(plan.LocalDisplacements[3], Is.EqualTo(Vector3.zero));
                Assert.That(plan.LocalDisplacements[4], Is.EqualTo(Vector3.zero));
                Assert.That(plan.LocalDisplacements[5], Is.EqualTo(Vector3.zero));
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void PreserveSolvedClearance_ReprojectsAfterSmoothing()
        {
            var fixture = CreateCustomFixture(
                new[]
                {
                    new Vector3(-0.4f, -0.4f, 0f),
                    new Vector3(0.4f, -0.4f, 0.02f),
                    new Vector3(0f, 0.4f, 0.02f)
                },
                new[] { 0, 1, 2 });
            try
            {
                var raw = Evaluate(fixture);
                var smoothed = FitCorrectionGenerator.Analyze(
                    fixture.Deformer, raw, fixture.Reference,
                    ClearanceQueryMode.ReferenceNormal, FitCorrectionScope.TargetClearance,
                    0.005f, 0.01f, 0.02f,
                    Constraints(smooth: true, iterations: 1, strength: 1f));
                var preserved = FitCorrectionGenerator.Analyze(
                    fixture.Deformer, raw, fixture.Reference,
                    ClearanceQueryMode.ReferenceNormal, FitCorrectionScope.TargetClearance,
                    0.005f, 0.01f, 0.02f,
                    Constraints(smooth: true, iterations: 1, strength: 1f, preserve: true));

                Assert.That(smoothed.UnresolvedVertexCount, Is.EqualTo(1));
                Assert.That(preserved.UnresolvedVertexCount, Is.EqualTo(0));
                Assert.That(preserved.LocalDisplacements[0].magnitude, Is.LessThanOrEqualTo(0.02f + Epsilon));
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void OptionalSymmetry_MirrorsCorrectionAndSkipsUnmatchedVertex()
        {
            var targetVertices = new[]
            {
                new Vector3(-0.5f, 0f, 0f),
                new Vector3(0.5f, 0f, 0f),
                new Vector3(0.2f, 0.4f, 0.02f)
            };
            var referenceMesh = new Mesh { name = "Sloped Reference" };
            referenceMesh.vertices = new[]
            {
                new Vector3(-1f, -1f, -0.02f), new Vector3(1f, -1f, 0.02f),
                new Vector3(1f, 1f, 0.02f), new Vector3(-1f, 1f, -0.02f)
            };
            referenceMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            referenceMesh.RecalculateNormals();
            var fixture = CreateCustomFixture(targetVertices, Array.Empty<int>(), referenceMesh);
            try
            {
                var plan = FitCorrectionGenerator.Analyze(
                    fixture.Deformer, Evaluate(fixture), fixture.Reference,
                    ClearanceQueryMode.ReferenceNormal, FitCorrectionScope.PenetrationOnly,
                    0.005f, 0.01f, 0.05f,
                    Constraints(symmetry: true));
                Vector3 left = plan.LocalDisplacements[0];
                Vector3 right = plan.LocalDisplacements[1];

                Vector3 mirroredRight = SymmetryVertexMapCache.MirrorDirection(right, 0);
                Assert.That(left.x, Is.EqualTo(mirroredRight.x).Within(Epsilon));
                Assert.That(left.y, Is.EqualTo(mirroredRight.y).Within(Epsilon));
                Assert.That(left.z, Is.EqualTo(mirroredRight.z).Within(Epsilon));
                Assert.That(left.sqrMagnitude, Is.GreaterThan(0f));
                Assert.That(plan.LocalDisplacements[2], Is.EqualTo(Vector3.zero));
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void GeneratedLayer_PersistsConstraintMetadata()
        {
            var fixture = CreateFixture(0f, 0f, 0f);
            fixture.Deformer.Layers[fixture.Deformer.ActiveLayerIndex].VertexMask = new[] { 0f, 0.5f, 1f };
            try
            {
                var options = Constraints(
                    useMask: true, pinBoundaries: true, isolate: true,
                    smooth: true, iterations: 3, strength: 0.25f,
                    preserve: true, symmetry: true);
                var plan = FitCorrectionGenerator.Analyze(
                    fixture.Deformer, Evaluate(fixture), fixture.Reference,
                    ClearanceQueryMode.ReferenceNormal, FitCorrectionScope.TargetClearance,
                    0.005f, 0.01f, 0.1f, options);
                var report = FitCorrectionGenerator.Generate(
                    fixture.Deformer, plan, fixture.Reference,
                    ClearanceQueryMode.ReferenceNormal, FitCorrectionScope.TargetClearance,
                    0.005f, 0.01f, 0.1f);
                var layer = fixture.Deformer.Layers[report.LayerIndex];

                Assert.That(layer.FitCorrectionUsedVertexMask, Is.True);
                Assert.That(layer.FitCorrectionConstraintMask, Is.EqualTo(new[] { 0f, 0.5f, 1f }));
                Assert.That(layer.FitCorrectionPinnedOpenBoundaries, Is.True);
                Assert.That(layer.FitCorrectionIsolatedComponents, Is.True);
                Assert.That(layer.FitCorrectionSmoothedSurface, Is.True);
                Assert.That(layer.FitCorrectionSmoothingIterations, Is.EqualTo(3));
                Assert.That(layer.FitCorrectionSmoothingStrength, Is.EqualTo(0.25f).Within(Epsilon));
                Assert.That(layer.FitCorrectionPreservedClearance, Is.True);
                Assert.That(layer.FitCorrectionUsedSymmetry, Is.True);
            }
            finally
            {
                fixture.Destroy();
            }
        }

        private static ClearanceHeatmapRawEvaluation Evaluate(Fixture fixture)
        {
            return ClearanceHeatmapEvaluator.Evaluate(
                fixture.Target,
                fixture.Reference,
                ClearanceSignMode.ReferenceNormal);
        }

        private static FitCorrectionPlan Analyze(
            Fixture fixture,
            ClearanceHeatmapRawEvaluation raw,
            FitCorrectionScope scope,
            float maximumMove)
        {
            return FitCorrectionGenerator.Analyze(
                fixture.Deformer,
                raw,
                fixture.Reference,
                ClearanceQueryMode.ReferenceNormal,
                scope,
                0.005f,
                0.01f,
                maximumMove);
        }

        private static Fixture CreateFixture(params float[] clearances)
        {
            var targetMesh = CreateTargetMesh(clearances);
            var referenceMesh = CreateReferencePlane();
            var target = CreateRenderer("Target", targetMesh);
            var reference = CreateRenderer("Reference", referenceMesh);
            var deformer = target.gameObject.AddComponent<LatticeDeformer>();
            deformer.Reset();
            return new Fixture(target, reference, targetMesh, referenceMesh, deformer);
        }

        private static Fixture CreateCustomFixture(
            Vector3[] vertices,
            int[] triangles,
            Mesh referenceMesh = null)
        {
            var targetMesh = new Mesh { name = "Constraint Target", vertices = vertices };
            targetMesh.triangles = triangles;
            if (triangles.Length > 0) targetMesh.RecalculateNormals();
            targetMesh.RecalculateBounds();
            referenceMesh ??= CreateReferencePlane();
            var target = CreateRenderer("Target", targetMesh);
            var reference = CreateRenderer("Reference", referenceMesh);
            var deformer = target.gameObject.AddComponent<LatticeDeformer>();
            deformer.Reset();
            return new Fixture(target, reference, targetMesh, referenceMesh, deformer);
        }

        private static FitCorrectionConstraintOptions DisabledConstraints() =>
            new FitCorrectionConstraintOptions(false, false, false, false, 0, 0f, false, false, 0, 0.001f);

        private static FitCorrectionConstraintOptions Constraints(
            bool useMask = false,
            bool pinBoundaries = false,
            bool isolate = false,
            bool smooth = false,
            int iterations = 0,
            float strength = 0f,
            bool preserve = false,
            bool symmetry = false)
        {
            return new FitCorrectionConstraintOptions(
                useMask, pinBoundaries, isolate, smooth, iterations, strength,
                preserve, symmetry, 0, 0.001f);
        }

        private static Mesh CreateTargetMesh(params float[] clearances)
        {
            var vertices = new Vector3[clearances.Length];
            for (int i = 0; i < clearances.Length; i++)
            {
                float x = (i & 1) == 0 ? -0.4f : 0.4f;
                float y = i < 2 ? -0.4f : 0.4f;
                vertices[i] = new Vector3(x, y, clearances[i]);
            }
            var mesh = new Mesh { name = "Correction Target", vertices = vertices };
            if (vertices.Length >= 3)
            {
                mesh.triangles = vertices.Length >= 4
                    ? new[] { 0, 1, 2, 1, 3, 2 }
                    : new[] { 0, 1, 2 };
                mesh.RecalculateNormals();
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateReferencePlane()
        {
            var mesh = new Mesh { name = "Correction Reference" };
            mesh.vertices = new[]
            {
                new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f),
                new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f)
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static MeshRenderer CreateRenderer(string name, Mesh mesh)
        {
            var gameObject = new GameObject(name);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            return gameObject.AddComponent<MeshRenderer>();
        }

        private sealed class Fixture
        {
            internal readonly MeshRenderer Target;
            internal readonly MeshRenderer Reference;
            internal readonly Mesh TargetMesh;
            internal readonly Mesh ReferenceMesh;
            internal readonly LatticeDeformer Deformer;

            internal Fixture(
                MeshRenderer target,
                MeshRenderer reference,
                Mesh targetMesh,
                Mesh referenceMesh,
                LatticeDeformer deformer)
            {
                Target = target;
                Reference = reference;
                TargetMesh = targetMesh;
                ReferenceMesh = referenceMesh;
                Deformer = deformer;
            }

            internal void Destroy()
            {
                var runtime = Deformer != null ? Deformer.RuntimeMesh : null;
                if (runtime != null) Object.DestroyImmediate(runtime);
                if (Target != null) Object.DestroyImmediate(Target.gameObject);
                if (Reference != null) Object.DestroyImmediate(Reference.gameObject);
                if (TargetMesh != null) Object.DestroyImmediate(TargetMesh);
                if (ReferenceMesh != null) Object.DestroyImmediate(ReferenceMesh);
            }
        }
    }
}
#endif
