using System;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    internal static class BlendShapeComposer
    {
        internal static Vector3[] Compose(
            Vector3[][] candidates,
            BlendShapeCompositionMode composition,
            float normalizedProgress,
            Vector3[] result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            int vertexCount = result.Length;
            Array.Clear(result, 0, vertexCount);
            if (candidates == null || candidates.Length == 0) return result;

            if (composition == BlendShapeCompositionMode.Single || candidates.Length == 1)
            {
                float scale = normalizedProgress;
                var candidate = candidates[0];
                if (candidate == null || candidate.Length != vertexCount) return result;
                for (int vertex = 0; vertex < vertexCount; vertex++)
                    result[vertex] = candidate[vertex] * scale;
                return result;
            }

            normalizedProgress = Mathf.Clamp01(normalizedProgress);
            float stageProgress = normalizedProgress * candidates.Length;
            if (composition == BlendShapeCompositionMode.Progressive)
            {
                int completedStages = Mathf.Min(Mathf.FloorToInt(stageProgress), candidates.Length);
                for (int stage = 0; stage < completedStages; stage++)
                {
                    var candidate = candidates[stage];
                    if (candidate == null || candidate.Length != vertexCount) continue;
                    for (int vertex = 0; vertex < vertexCount; vertex++)
                        result[vertex] += candidate[vertex];
                }

                if (completedStages < candidates.Length)
                {
                    float fraction = stageProgress - completedStages;
                    var candidate = candidates[completedStages];
                    if (candidate == null || candidate.Length != vertexCount) return result;
                    for (int vertex = 0; vertex < vertexCount; vertex++)
                        result[vertex] += candidate[vertex] * fraction;
                }
                return result;
            }

            if (stageProgress <= 1f)
            {
                var first = candidates[0];
                if (first == null || first.Length != vertexCount) return result;
                for (int vertex = 0; vertex < vertexCount; vertex++)
                    result[vertex] = first[vertex] * stageProgress;
                return result;
            }

            int lower = Mathf.Min(Mathf.FloorToInt(stageProgress) - 1, candidates.Length - 1);
            int upper = Mathf.Min(lower + 1, candidates.Length - 1);
            float blend = upper == lower ? 0f : stageProgress - Mathf.Floor(stageProgress);
            if (candidates[lower] == null || candidates[upper] == null ||
                candidates[lower].Length != vertexCount || candidates[upper].Length != vertexCount)
            {
                return result;
            }
            for (int vertex = 0; vertex < vertexCount; vertex++)
                result[vertex] = Vector3.LerpUnclamped(candidates[lower][vertex], candidates[upper][vertex], blend);
            return result;
        }
    }
}
