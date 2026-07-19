#if UNITY_EDITOR
using System;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal enum FitCorrectionStatus
    {
        Ready = 0,
        Success = 1,
        InvalidReference = 2,
        InvalidTarget = 3,
        StaleEvaluation = 4,
        TopologyMismatch = 5,
        PosedSkinnedMeshUnsupported = 6,
        NoCandidates = 7,
        InvalidSettings = 8,
        InvalidTargetTransform = 9
    }

    internal sealed class FitCorrectionPlan
    {
        internal readonly FitCorrectionStatus Status;
        internal readonly int CandidateVertexCount;
        internal readonly int UnresolvedVertexCount;
        internal readonly float MaximumAppliedMove;
        internal readonly Vector3[] LocalDisplacements;
        internal readonly Vector3[] CorrectedWorldPositions;
        internal readonly ClearanceHeatmapEvaluation BeforeEvaluation;

        internal bool CanGenerate => Status == FitCorrectionStatus.Ready && CandidateVertexCount > 0;

        internal FitCorrectionPlan(
            FitCorrectionStatus status,
            int candidateVertexCount = 0,
            int unresolvedVertexCount = 0,
            float maximumAppliedMove = 0f,
            Vector3[] localDisplacements = null,
            Vector3[] correctedWorldPositions = null,
            ClearanceHeatmapEvaluation beforeEvaluation = null)
        {
            Status = status;
            CandidateVertexCount = candidateVertexCount;
            UnresolvedVertexCount = unresolvedVertexCount;
            MaximumAppliedMove = maximumAppliedMove;
            LocalDisplacements = localDisplacements ?? Array.Empty<Vector3>();
            CorrectedWorldPositions = correctedWorldPositions ?? Array.Empty<Vector3>();
            BeforeEvaluation = beforeEvaluation;
        }
    }

    internal sealed class FitCorrectionReport
    {
        internal readonly FitCorrectionStatus Status;
        internal readonly int LayerIndex;
        internal readonly int MovedVertexCount;
        internal readonly int ImprovedVertexCount;
        internal readonly int UnresolvedVertexCount;
        internal readonly float MaximumAppliedMove;
        internal readonly ClearanceHeatmapStatistics BeforeStatistics;
        internal readonly ClearanceHeatmapStatistics AfterStatistics;

        internal FitCorrectionReport(
            FitCorrectionStatus status,
            int layerIndex = -1,
            int movedVertexCount = 0,
            int improvedVertexCount = 0,
            int unresolvedVertexCount = 0,
            float maximumAppliedMove = 0f,
            ClearanceHeatmapStatistics beforeStatistics = default,
            ClearanceHeatmapStatistics afterStatistics = default)
        {
            Status = status;
            LayerIndex = layerIndex;
            MovedVertexCount = movedVertexCount;
            ImprovedVertexCount = improvedVertexCount;
            UnresolvedVertexCount = unresolvedVertexCount;
            MaximumAppliedMove = maximumAppliedMove;
            BeforeStatistics = beforeStatistics;
            AfterStatistics = afterStatistics;
        }
    }

    internal static class FitCorrectionGenerator
    {
        private const float PoseToleranceSq = 1e-10f;

        internal static FitCorrectionPlan Analyze(
            LatticeDeformer deformer,
            ClearanceHeatmapRawEvaluation rawEvaluation,
            Renderer referenceRenderer,
            ClearanceQueryMode queryMode,
            FitCorrectionScope scope,
            float warningDistance,
            float targetDistance,
            float maximumMove)
        {
            if (!AreSettingsValid(queryMode, scope, warningDistance, targetDistance, maximumMove))
            {
                return new FitCorrectionPlan(FitCorrectionStatus.InvalidSettings);
            }

            warningDistance = Mathf.Max(0f, warningDistance);
            targetDistance = Mathf.Max(warningDistance, targetDistance);
            maximumMove = Mathf.Max(0f, maximumMove);
            if (referenceRenderer == null || rawEvaluation == null ||
                rawEvaluation.Status == ClearanceEvaluationStatus.InvalidReference)
            {
                return new FitCorrectionPlan(FitCorrectionStatus.InvalidReference);
            }

            Renderer targetRenderer = deformer != null ? deformer.TargetRenderer : null;
            if (deformer == null || targetRenderer == null || deformer.SourceMesh == null ||
                rawEvaluation.Status != ClearanceEvaluationStatus.Valid)
            {
                return new FitCorrectionPlan(FitCorrectionStatus.InvalidTarget);
            }

            if (rawEvaluation.TargetRenderer != targetRenderer ||
                rawEvaluation.ReferenceRenderer != referenceRenderer ||
                !ClearanceQueryCache.TryGetRendererStateHash(targetRenderer, out int targetStateHash) ||
                !ClearanceQueryCache.TryGetRendererStateHash(referenceRenderer, out int referenceStateHash) ||
                targetStateHash != rawEvaluation.TargetStateHash ||
                referenceStateHash != rawEvaluation.ReferenceStateHash)
            {
                return new FitCorrectionPlan(FitCorrectionStatus.StaleEvaluation);
            }

            int vertexCount = deformer.SourceMesh.vertexCount;
            if (rawEvaluation.WorldPositions.Length != vertexCount ||
                rawEvaluation.QueryResults.Length != vertexCount)
            {
                return new FitCorrectionPlan(FitCorrectionStatus.TopologyMismatch);
            }

            if (targetRenderer is SkinnedMeshRenderer skinned &&
                !IsSkinnedRestPose(skinned, rawEvaluation.WorldPositions))
            {
                return new FitCorrectionPlan(FitCorrectionStatus.PosedSkinnedMeshUnsupported);
            }

            Matrix4x4 localToWorld = targetRenderer.transform.localToWorldMatrix;
            Matrix4x4 worldToLocal = targetRenderer.transform.worldToLocalMatrix;
            if (!AreInverseTransforms(localToWorld, worldToLocal))
            {
                return new FitCorrectionPlan(FitCorrectionStatus.InvalidTargetTransform);
            }

            var before = ClearanceHeatmapEvaluator.Classify(
                rawEvaluation,
                warningDistance,
                targetDistance);
            var localDisplacements = new Vector3[vertexCount];
            var correctedWorldPositions = (Vector3[])rawEvaluation.WorldPositions.Clone();
            int candidates = 0;
            int unresolved = 0;
            float maximumApplied = 0f;

            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                ClearanceQueryResult result = rawEvaluation.QueryResults[vertex];
                if (!result.IsValid || !IsInScope(result.SignedClearance, scope, warningDistance, targetDistance))
                    continue;

                float requiredMove = Mathf.Max(0f, targetDistance - result.SignedClearance);
                if (requiredMove <= 1e-8f) continue;
                candidates++;

                Vector3 normal = result.NormalWorld;
                if (!IsFinite(normal) || normal.sqrMagnitude <= 1e-12f)
                {
                    unresolved++;
                    continue;
                }
                normal.Normalize();
                float appliedMove = Mathf.Min(requiredMove, maximumMove);
                if (requiredMove - appliedMove > 1e-6f) unresolved++;
                Vector3 worldDisplacement = normal * appliedMove;
                Vector3 localDisplacement = worldToLocal.MultiplyVector(worldDisplacement);
                Vector3 reconstructedWorldDisplacement = localToWorld.MultiplyVector(localDisplacement);
                float roundTripToleranceSq = Mathf.Max(
                    1e-12f,
                    worldDisplacement.sqrMagnitude * 1e-8f);
                if (!IsFinite(localDisplacement) ||
                    !IsFinite(reconstructedWorldDisplacement) ||
                    (reconstructedWorldDisplacement - worldDisplacement).sqrMagnitude > roundTripToleranceSq)
                {
                    return new FitCorrectionPlan(FitCorrectionStatus.InvalidTargetTransform);
                }

                localDisplacements[vertex] = localDisplacement;
                correctedWorldPositions[vertex] += worldDisplacement;
                maximumApplied = Mathf.Max(maximumApplied, appliedMove);
            }

            return new FitCorrectionPlan(
                candidates > 0 ? FitCorrectionStatus.Ready : FitCorrectionStatus.NoCandidates,
                candidates,
                unresolved,
                maximumApplied,
                localDisplacements,
                correctedWorldPositions,
                before);
        }

        internal static FitCorrectionReport Generate(
            LatticeDeformer deformer,
            FitCorrectionPlan plan,
            Renderer referenceRenderer,
            ClearanceQueryMode queryMode,
            FitCorrectionScope scope,
            float warningDistance,
            float targetDistance,
            float maximumMove)
        {
            if (deformer == null || plan == null || !plan.CanGenerate)
                return new FitCorrectionReport(plan?.Status ?? FitCorrectionStatus.InvalidTarget);
            if (!AreSettingsValid(queryMode, scope, warningDistance, targetDistance, maximumMove))
                return new FitCorrectionReport(FitCorrectionStatus.InvalidSettings);

            int layerIndex = deformer.AddLayer("Fit Correction", MeshDeformerLayerType.Brush);
            if (layerIndex < 0)
                return new FitCorrectionReport(FitCorrectionStatus.InvalidTarget);
            var layer = deformer.Layers[layerIndex];
            layer.BrushDisplacements = (Vector3[])plan.LocalDisplacements.Clone();
            layer.ConfigureFitCorrection(
                referenceRenderer,
                queryMode,
                scope,
                warningDistance,
                targetDistance,
                maximumMove);
            ClearanceSignMode signMode = queryMode == ClearanceQueryMode.ClosedMesh
                ? ClearanceSignMode.ClosedMesh
                : ClearanceSignMode.ReferenceNormal;
            var afterRaw = ClearanceHeatmapEvaluator.EvaluateWorldPositions(
                plan.CorrectedWorldPositions,
                referenceRenderer,
                signMode);
            var after = ClearanceHeatmapEvaluator.Classify(afterRaw, warningDistance, targetDistance);
            int improved = 0;
            int unresolved = 0;
            int count = Mathf.Min(
                plan.BeforeEvaluation.QueryResults.Length,
                after.QueryResults.Length);
            for (int vertex = 0; vertex < count; vertex++)
            {
                ClearanceQueryResult beforeResult = plan.BeforeEvaluation.QueryResults[vertex];
                ClearanceQueryResult afterResult = after.QueryResults[vertex];
                if (!beforeResult.IsValid || !afterResult.IsValid) continue;
                if (afterResult.SignedClearance > beforeResult.SignedClearance + 1e-6f) improved++;
                if (IsInScope(beforeResult.SignedClearance, scope, warningDistance, targetDistance) &&
                    afterResult.SignedClearance < targetDistance - 1e-6f)
                {
                    unresolved++;
                }
            }

            return new FitCorrectionReport(
                FitCorrectionStatus.Success,
                layerIndex,
                plan.CandidateVertexCount,
                improved,
                unresolved,
                plan.MaximumAppliedMove,
                plan.BeforeEvaluation.Statistics,
                after.Statistics);
        }

        private static bool IsInScope(
            float clearance,
            FitCorrectionScope scope,
            float warningDistance,
            float targetDistance)
        {
            return scope switch
            {
                FitCorrectionScope.PenetrationOnly => clearance < 0f,
                FitCorrectionScope.WarningThreshold => clearance <= warningDistance,
                FitCorrectionScope.TargetClearance => clearance < targetDistance,
                _ => false
            };
        }

        private static bool AreSettingsValid(
            ClearanceQueryMode queryMode,
            FitCorrectionScope scope,
            float warningDistance,
            float targetDistance,
            float maximumMove)
        {
            return (queryMode == ClearanceQueryMode.ReferenceNormal ||
                    queryMode == ClearanceQueryMode.ClosedMesh) &&
                   (scope == FitCorrectionScope.PenetrationOnly ||
                    scope == FitCorrectionScope.WarningThreshold ||
                    scope == FitCorrectionScope.TargetClearance) &&
                   IsFinite(warningDistance) &&
                   IsFinite(targetDistance) &&
                   IsFinite(maximumMove);
        }

        private static bool AreInverseTransforms(Matrix4x4 localToWorld, Matrix4x4 worldToLocal)
        {
            if (!IsFinite(localToWorld) || !IsFinite(worldToLocal)) return false;
            float determinant = localToWorld.determinant;
            if (!IsFinite(determinant) || Mathf.Abs(determinant) <= 1e-12f) return false;
            return IsApproximatelyIdentity(localToWorld * worldToLocal) &&
                   IsApproximatelyIdentity(worldToLocal * localToWorld);
        }

        private static bool IsApproximatelyIdentity(Matrix4x4 value)
        {
            const float tolerance = 1e-4f;
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    float expected = row == column ? 1f : 0f;
                    if (Mathf.Abs(value[row, column] - expected) > tolerance) return false;
                }
            }
            return true;
        }

        private static bool IsSkinnedRestPose(
            SkinnedMeshRenderer renderer,
            Vector3[] bakedWorldPositions)
        {
            Mesh currentMesh = renderer != null ? renderer.sharedMesh : null;
            if (currentMesh == null || currentMesh.vertexCount != bakedWorldPositions.Length) return false;
            Vector3[] restVertices;
            try
            {
                restVertices = currentMesh.vertices;
            }
            catch (Exception)
            {
                return false;
            }

            Matrix4x4 localToWorld = renderer.transform.localToWorldMatrix;
            for (int vertex = 0; vertex < restVertices.Length; vertex++)
            {
                Vector3 restWorld = localToWorld.MultiplyPoint3x4(restVertices[vertex]);
                if ((restWorld - bakedWorldPositions[vertex]).sqrMagnitude > PoseToleranceSq)
                    return false;
            }
            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Matrix4x4 value)
        {
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    if (!IsFinite(value[row, column])) return false;
                }
            }
            return true;
        }
    }
}
#endif
