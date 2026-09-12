#if UNITY_EDITOR
using System;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class ClearanceSceneDrawer
    {
        private const int HeatmapDrawPointBudget = 4096;
        private static readonly ProfilerMarker s_heatmapDrawMarker = new("ClearanceHeatmap.Draw");
        internal static void Draw(ClearanceHeatmapEvaluation evaluation,
            ClearanceHeatmapDisplayMode displayMode, int displayStride, FitCorrectionPlan plan)
        {
            if (evaluation == null || evaluation.Status != ClearanceEvaluationStatus.Valid) return;

            int stride = CalculateAdaptiveHeatmapStride(
                evaluation.WorldPositions.Length,
                displayStride,
                HeatmapDrawPointBudget);
            using (s_heatmapDrawMarker.Auto())
            {
                for (int i = 0; i < evaluation.WorldPositions.Length; i += stride)
                {
                    ClearanceClassification classification = evaluation.Classifications[i];
                    if (!ClearanceHeatmapEvaluator.ShouldDisplay(
                            classification,
                            displayMode))
                    {
                        continue;
                    }

                    Vector3 position = evaluation.WorldPositions[i];
                    Handles.color = ClearanceHeatmapEvaluator.ColorFor(classification);
                    float size = HandleUtility.GetHandleSize(position) * 0.012f;
                    Handles.DotHandleCap(0, position, Quaternion.identity, size, EventType.Repaint);
                }
            }

            DrawFitCorrectionPreview(plan);
        }

        internal static int CalculateAdaptiveHeatmapStride(
            int vertexCount,
            int requestedStride,
            int pointBudget = HeatmapDrawPointBudget)
        {
            requestedStride = Mathf.Max(1, requestedStride);
            pointBudget = Mathf.Max(1, pointBudget);
            int budgetStride = vertexCount > pointBudget
                ? Mathf.CeilToInt(vertexCount / (float)pointBudget)
                : 1;
            return Mathf.Max(requestedStride, budgetStride);
        }

        private static void DrawFitCorrectionPreview(FitCorrectionPlan plan)
        {
            if (plan == null || !plan.CanGenerate || plan.BeforeEvaluation == null) return;
            int count = Mathf.Min(
                plan.BeforeEvaluation.WorldPositions.Length,
                plan.CorrectedWorldPositions.Length);
            Handles.color = new Color(0.1f, 0.9f, 1f, 0.9f);
            for (int vertex = 0; vertex < count; vertex++)
            {
                Vector3 from = plan.BeforeEvaluation.WorldPositions[vertex];
                Vector3 to = plan.CorrectedWorldPositions[vertex];
                if ((to - from).sqrMagnitude <= 1e-16f) continue;
                Handles.DrawLine(from, to, 2f);
                float size = HandleUtility.GetHandleSize(to) * 0.01f;
                Handles.DotHandleCap(0, to, Quaternion.identity, size, EventType.Repaint);
            }
        }
    }
}
#endif
