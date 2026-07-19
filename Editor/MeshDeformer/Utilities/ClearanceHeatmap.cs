#if UNITY_EDITOR
using System;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal enum ClearanceClassification
    {
        Invalid = 0,
        Penetrating = 1,
        Warning = 2,
        BelowTarget = 3,
        Clear = 4
    }

    internal enum ClearanceEvaluationStatus
    {
        Valid = 0,
        InvalidReference = 1,
        InvalidTarget = 2,
        NoValidSamples = 3
    }

    internal readonly struct ClearanceHeatmapStatistics
    {
        internal readonly float MinimumClearance;
        internal readonly float MaximumPenetrationDepth;
        internal readonly int ViolationVertexCount;
        internal readonly int EvaluatedVertexCount;

        internal ClearanceHeatmapStatistics(
            float minimumClearance,
            float maximumPenetrationDepth,
            int violationVertexCount,
            int evaluatedVertexCount)
        {
            MinimumClearance = minimumClearance;
            MaximumPenetrationDepth = maximumPenetrationDepth;
            ViolationVertexCount = violationVertexCount;
            EvaluatedVertexCount = evaluatedVertexCount;
        }
    }

    internal sealed class ClearanceHeatmapRawEvaluation
    {
        internal readonly Vector3[] WorldPositions;
        internal readonly ClearanceQueryResult[] QueryResults;
        internal readonly ClearanceEvaluationStatus Status;

        internal ClearanceHeatmapRawEvaluation(
            Vector3[] worldPositions,
            ClearanceQueryResult[] queryResults,
            ClearanceEvaluationStatus status)
        {
            WorldPositions = worldPositions ?? Array.Empty<Vector3>();
            QueryResults = queryResults ?? Array.Empty<ClearanceQueryResult>();
            Status = status;
        }
    }

    internal sealed class ClearanceHeatmapEvaluation
    {
        internal readonly Vector3[] WorldPositions;
        internal readonly ClearanceQueryResult[] QueryResults;
        internal readonly ClearanceClassification[] Classifications;
        internal readonly ClearanceHeatmapStatistics Statistics;
        internal readonly ClearanceEvaluationStatus Status;
        internal readonly ClearanceSignMode SignMode;
        internal readonly bool IsClosedSurface;

        internal ClearanceHeatmapEvaluation(
            Vector3[] worldPositions,
            ClearanceQueryResult[] queryResults,
            ClearanceClassification[] classifications,
            ClearanceHeatmapStatistics statistics,
            ClearanceEvaluationStatus status,
            ClearanceSignMode signMode,
            bool isClosedSurface)
        {
            WorldPositions = worldPositions;
            QueryResults = queryResults;
            Classifications = classifications;
            Statistics = statistics;
            Status = status;
            SignMode = signMode;
            IsClosedSurface = isClosedSurface;
        }
    }

    internal static class ClearanceHeatmapEvaluator
    {
        internal static ClearanceHeatmapRawEvaluation Evaluate(
            Renderer targetRenderer,
            Renderer referenceRenderer,
            ClearanceSignMode signMode)
        {
            if (!IsUsableRenderer(referenceRenderer))
            {
                return new ClearanceHeatmapRawEvaluation(
                    null, null, ClearanceEvaluationStatus.InvalidReference);
            }

            if (!IsUsableRenderer(targetRenderer) ||
                !ClearanceQueryCache.TryGetWorldVertices(targetRenderer, out Vector3[] worldPositions))
            {
                return new ClearanceHeatmapRawEvaluation(
                    null, null, ClearanceEvaluationStatus.InvalidTarget);
            }

            ClearanceQueryResult[] results = ClearanceQueryCache.QueryPoints(
                referenceRenderer,
                worldPositions,
                Matrix4x4.identity,
                signMode);
            return new ClearanceHeatmapRawEvaluation(
                worldPositions,
                results,
                results.Length == worldPositions.Length
                    ? ClearanceEvaluationStatus.Valid
                    : ClearanceEvaluationStatus.InvalidReference);
        }

        internal static ClearanceHeatmapEvaluation Classify(
            ClearanceHeatmapRawEvaluation raw,
            float warningDistance,
            float targetDistance)
        {
            warningDistance = Mathf.Max(0f, warningDistance);
            targetDistance = Mathf.Max(warningDistance, targetDistance);
            if (raw == null || raw.Status != ClearanceEvaluationStatus.Valid)
            {
                return new ClearanceHeatmapEvaluation(
                    raw?.WorldPositions ?? Array.Empty<Vector3>(),
                    raw?.QueryResults ?? Array.Empty<ClearanceQueryResult>(),
                    Array.Empty<ClearanceClassification>(),
                    default,
                    raw?.Status ?? ClearanceEvaluationStatus.InvalidTarget,
                    ClearanceSignMode.ReferenceNormal,
                    false);
            }

            var classifications = new ClearanceClassification[raw.QueryResults.Length];
            float minimumClearance = float.PositiveInfinity;
            float maximumPenetration = 0f;
            int violations = 0;
            int evaluated = 0;
            ClearanceSignMode actualSignMode = ClearanceSignMode.ReferenceNormal;
            bool isClosedSurface = false;

            for (int i = 0; i < raw.QueryResults.Length; i++)
            {
                ClearanceQueryResult result = raw.QueryResults[i];
                if (!result.IsValid)
                {
                    classifications[i] = ClearanceClassification.Invalid;
                    continue;
                }

                evaluated++;
                actualSignMode = result.SignMode;
                isClosedSurface = result.IsClosedSurface;
                float clearance = result.SignedClearance;
                minimumClearance = Mathf.Min(minimumClearance, clearance);
                if (clearance < 0f)
                {
                    classifications[i] = ClearanceClassification.Penetrating;
                    maximumPenetration = Mathf.Max(maximumPenetration, -clearance);
                }
                else if (clearance <= warningDistance)
                {
                    classifications[i] = ClearanceClassification.Warning;
                }
                else if (clearance < targetDistance)
                {
                    classifications[i] = ClearanceClassification.BelowTarget;
                }
                else
                {
                    classifications[i] = ClearanceClassification.Clear;
                }

                if (clearance < targetDistance) violations++;
            }

            ClearanceEvaluationStatus status = evaluated > 0
                ? ClearanceEvaluationStatus.Valid
                : ClearanceEvaluationStatus.NoValidSamples;
            if (evaluated == 0) minimumClearance = 0f;
            return new ClearanceHeatmapEvaluation(
                raw.WorldPositions,
                raw.QueryResults,
                classifications,
                new ClearanceHeatmapStatistics(
                    minimumClearance,
                    maximumPenetration,
                    violations,
                    evaluated),
                status,
                actualSignMode,
                isClosedSurface);
        }

        internal static bool ShouldDisplay(
            ClearanceClassification classification,
            ClearanceHeatmapDisplayMode displayMode)
        {
            return displayMode switch
            {
                ClearanceHeatmapDisplayMode.PenetrationOnly =>
                    classification == ClearanceClassification.Penetrating,
                ClearanceHeatmapDisplayMode.WarningAndPenetration =>
                    classification == ClearanceClassification.Penetrating ||
                    classification == ClearanceClassification.Warning ||
                    classification == ClearanceClassification.BelowTarget,
                ClearanceHeatmapDisplayMode.FullDistribution =>
                    classification != ClearanceClassification.Invalid,
                _ => false
            };
        }

        internal static Color ColorFor(ClearanceClassification classification)
        {
            return classification switch
            {
                ClearanceClassification.Penetrating => new Color(1f, 0.05f, 0.05f, 0.95f),
                ClearanceClassification.Warning => new Color(1f, 0.35f, 0.05f, 0.9f),
                ClearanceClassification.BelowTarget => new Color(1f, 0.9f, 0.05f, 0.85f),
                ClearanceClassification.Clear => new Color(0.1f, 0.85f, 0.25f, 0.65f),
                _ => Color.clear
            };
        }

        private static bool IsUsableRenderer(Renderer renderer)
        {
            return renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy;
        }
    }
}
#endif
