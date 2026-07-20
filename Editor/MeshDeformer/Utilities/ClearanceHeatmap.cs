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
        NoValidSamples = 3,
        InvalidThresholds = 4
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
        internal readonly Renderer TargetRenderer;
        internal readonly Renderer ReferenceRenderer;
        internal readonly int TargetStateHash;
        internal readonly int ReferenceStateHash;

        internal ClearanceHeatmapRawEvaluation(
            Vector3[] worldPositions,
            ClearanceQueryResult[] queryResults,
            ClearanceEvaluationStatus status,
            Renderer targetRenderer = null,
            Renderer referenceRenderer = null,
            int targetStateHash = 0,
            int referenceStateHash = 0)
        {
            WorldPositions = worldPositions ?? Array.Empty<Vector3>();
            QueryResults = queryResults ?? Array.Empty<ClearanceQueryResult>();
            Status = status;
            TargetRenderer = targetRenderer;
            ReferenceRenderer = referenceRenderer;
            TargetStateHash = targetStateHash;
            ReferenceStateHash = referenceStateHash;
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
                !ClearanceQueryCache.TryGetWorldVertices(
                    targetRenderer,
                    out Vector3[] worldPositions,
                    out int targetStateHash))
            {
                return new ClearanceHeatmapRawEvaluation(
                    null, null, ClearanceEvaluationStatus.InvalidTarget);
            }

            ClearanceQueryResult[] results = ClearanceQueryCache.QueryPoints(
                referenceRenderer,
                worldPositions,
                Matrix4x4.identity,
                signMode,
                out int referenceStateHash);
            return new ClearanceHeatmapRawEvaluation(
                worldPositions,
                results,
                results.Length == worldPositions.Length
                    ? ClearanceEvaluationStatus.Valid
                    : ClearanceEvaluationStatus.InvalidReference,
                targetRenderer,
                referenceRenderer,
                targetStateHash,
                referenceStateHash);
        }

        internal static ClearanceHeatmapRawEvaluation EvaluateWorldPositions(
            Vector3[] worldPositions,
            Renderer referenceRenderer,
            ClearanceSignMode signMode)
        {
            if (worldPositions == null || !IsUsableRenderer(referenceRenderer))
            {
                return new ClearanceHeatmapRawEvaluation(
                    null, null, ClearanceEvaluationStatus.InvalidReference);
            }
            ClearanceQueryResult[] results = ClearanceQueryCache.QueryPoints(
                referenceRenderer,
                worldPositions,
                Matrix4x4.identity,
                signMode);
            return new ClearanceHeatmapRawEvaluation(
                (Vector3[])worldPositions.Clone(),
                results,
                results.Length == worldPositions.Length
                    ? ClearanceEvaluationStatus.Valid
                    : ClearanceEvaluationStatus.InvalidReference,
                null,
                referenceRenderer);
        }

        internal static ClearanceHeatmapEvaluation Classify(
            ClearanceHeatmapRawEvaluation raw,
            float warningDistance,
            float targetDistance)
        {
            if (!IsFinite(warningDistance) || !IsFinite(targetDistance))
            {
                return new ClearanceHeatmapEvaluation(
                    raw?.WorldPositions ?? Array.Empty<Vector3>(),
                    raw?.QueryResults ?? Array.Empty<ClearanceQueryResult>(),
                    Array.Empty<ClearanceClassification>(),
                    default,
                    ClearanceEvaluationStatus.InvalidThresholds,
                    ClearanceSignMode.ReferenceNormal,
                    false);
            }

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
                if (!result.IsValid || !IsFinite(result.SignedClearance))
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

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// Per-consumer cache for the classification pass. Raw evaluations are immutable snapshots
    /// for the editor heatmap, so reference identity plus the normalized thresholds completely
    /// identifies the classification result. Display mode and stride are intentionally excluded:
    /// they only filter/draw an already classified result.
    /// </summary>
    internal sealed class ClearanceHeatmapClassificationCache
    {
        private ClearanceHeatmapRawEvaluation _raw;
        private float _warningDistance;
        private float _targetDistance;
        private ClearanceHeatmapEvaluation _evaluation;

        internal ClearanceHeatmapEvaluation Get(
            ClearanceHeatmapRawEvaluation raw,
            float warningDistance,
            float targetDistance)
        {
            float normalizedWarning = NormalizeWarning(warningDistance);
            float normalizedTarget = NormalizeTarget(targetDistance, normalizedWarning);
            if (_evaluation != null &&
                ReferenceEquals(_raw, raw) &&
                SameFloat(_warningDistance, normalizedWarning) &&
                SameFloat(_targetDistance, normalizedTarget))
            {
                return _evaluation;
            }

            _raw = raw;
            _warningDistance = normalizedWarning;
            _targetDistance = normalizedTarget;
            _evaluation = ClearanceHeatmapEvaluator.Classify(raw, warningDistance, targetDistance);
            return _evaluation;
        }

        internal void Clear()
        {
            _raw = null;
            _evaluation = null;
        }

        private static float NormalizeWarning(float value)
        {
            return IsFinite(value) ? Mathf.Max(0f, value) : value;
        }

        private static float NormalizeTarget(float value, float warningDistance)
        {
            return IsFinite(value) && IsFinite(warningDistance)
                ? Mathf.Max(warningDistance, value)
                : value;
        }

        private static bool SameFloat(float left, float right)
        {
            return left.Equals(right);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
#endif
