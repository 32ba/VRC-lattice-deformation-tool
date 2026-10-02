#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>Draws captured brush inputs without selecting layers, evaluating meshes or changing caches.</summary>
    internal static class BrushVertexVisualization
    {
        internal const int MaxAffectedVertexDots = 4096;

        internal static void DrawAffected(VertexDisplayGeometry geometry, Vector3 worldCenter,
            float worldRadius, BrushFalloffType falloffType, float dotSize, float baseSize,
            HashSet<int> connectedVertices, GeodesicDistanceCalculator.Workspace geodesic)
        {
            int vertexCount = geometry.Count;
            float radiusSq = worldRadius * worldRadius;
            var cam = Camera.current;
            if (cam == null) return;
            var camRight = cam.transform.right;
            var camUp = cam.transform.up;

            using (var batch = SceneVertexDots.Shared.Begin(CompareFunction.Always))
            {
                bool useGeodesicCandidates = geodesic != null;
                int candidateCount = useGeodesicCandidates
                    ? geodesic.VisitedCount
                    : vertexCount;
                int sampleStride = GetVisualizationSampleStride(candidateCount);
                int iterationCount = (candidateCount + sampleStride - 1) / sampleStride;
                for (int iteration = 0; iteration < iterationCount; iteration++)
                {
                    int candidateIndex = iteration * sampleStride;
                    int i = useGeodesicCandidates
                        ? geodesic.GetVisitedVertex(candidateIndex)
                        : candidateIndex;
                    if (connectedVertices != null && !connectedVertices.Contains(i))
                        continue;

                    var worldPosition = geometry.WorldPosition(i);

                    float falloff;
                    if (useGeodesicCandidates)
                    {
                        if (!geodesic.TryGetDistance(i, out float geodesicDist))
                            continue;
                        float t = geodesicDist / worldRadius;
                        falloff = BrushDeformer.EvaluateFalloff(falloffType, t);
                    }
                    else
                    {
                        float distSq = (worldPosition - worldCenter).sqrMagnitude;
                        if (distSq > radiusSq) continue;
                        float dist = Mathf.Sqrt(distSq);
                        float t = dist / worldRadius;
                        falloff = BrushDeformer.EvaluateFalloff(falloffType, t);
                    }

                    if (falloff < 0.01f) continue;

                    var worldPos = worldPosition;
                    Color dotColor = HeatmapColor(falloff);
                    dotColor.a = 0.4f + 0.5f * falloff;
                    float scale = Mathf.Lerp(dotSize * 0.6f, dotSize * 1.4f, falloff);
                    batch.Draw(worldPos, dotColor, baseSize * scale, camRight, camUp);
                }
            }
        }

        internal static int GetVisualizationSampleStride(int candidateCount)
        {
            return candidateCount <= MaxAffectedVertexDots
                ? 1
                : (candidateCount + MaxAffectedVertexDots - 1) / MaxAffectedVertexDots;
        }

        internal static void DrawDisplacements(VertexDisplayGeometry geometry, float baseSize)
        {
            var displacements = geometry.Displacements;
            if (geometry.Count == 0 || displacements == null || displacements.Length == 0) return;
            int vertexCount = Mathf.Min(geometry.Count, displacements.Length);
            float maxMag = 0f;
            for (int i = 0; i < vertexCount; i++)
            {
                float mag = displacements[i].sqrMagnitude;
                if (mag > maxMag) maxMag = mag;
            }
            if (maxMag < 1e-12f) return;
            maxMag = Mathf.Sqrt(maxMag);
            var cam = Camera.current;
            if (cam == null) return;
            var camRight = cam.transform.right;
            var camUp = cam.transform.up;

            using (var batch = SceneVertexDots.Shared.Begin(CompareFunction.Always))
            {
                for (int i = 0; i < vertexCount; i++)
                {
                    float mag = displacements[i].magnitude;
                    if (mag < 1e-6f) continue;

                    float normalized = Mathf.Clamp01(mag / maxMag);
                    var worldPos = geometry.WorldPosition(i);

                    Color heatColor = HeatmapColor(normalized);
                    heatColor.a = 0.3f + 0.6f * normalized;

                    float dotRadius = baseSize * (1f + normalized * 2f);
                    batch.Draw(worldPos, heatColor, dotRadius, camRight, camUp);
                }
            }
        }

        internal static Color HeatmapColor(float t)
        {
            // 0.0=blue -> 0.25=cyan -> 0.5=green -> 0.75=yellow -> 1.0=red
            if (t < 0.25f)
                return Color.Lerp(new Color(0f, 0.2f, 1f), new Color(0f, 0.8f, 1f), t * 4f);
            if (t < 0.5f)
                return Color.Lerp(new Color(0f, 0.8f, 1f), new Color(0.2f, 1f, 0.2f), (t - 0.25f) * 4f);
            if (t < 0.75f)
                return Color.Lerp(new Color(0.2f, 1f, 0.2f), new Color(1f, 1f, 0f), (t - 0.5f) * 4f);
            return Color.Lerp(new Color(1f, 1f, 0f), new Color(1f, 0.1f, 0f), (t - 0.75f) * 4f);
        }

        internal static void DrawMask(VertexDisplayGeometry geometry, float[] mask, float baseSize)
        {
            if (geometry.Count == 0 || mask == null || mask.Length == 0) return;
            int vertexCount = Mathf.Min(geometry.Count, mask.Length);
            var cam = Camera.current;
            if (cam == null) return;
            var camRight = cam.transform.right;
            var camUp = cam.transform.up;

            using (var batch = SceneVertexDots.Shared.Begin(CompareFunction.Always))
            {
                for (int i = 0; i < vertexCount; i++)
                {
                    float maskValue = mask[i];
                    if (maskValue > 1f - 1e-6f) continue; // Fully editable, skip

                    var worldPos = geometry.WorldPosition(i);

                    // Red = protected (mask=0), Green = editable (mask=1)
                    float protection = 1f - maskValue;
                    Color dotColor = Color.Lerp(new Color(0.2f, 1f, 0.2f, 0.4f), new Color(1f, 0.2f, 0.2f, 0.8f), protection);
                    float dotRadius = baseSize * (1f + protection * 2f);
                    batch.Draw(worldPos, dotColor, dotRadius, camRight, camUp);
                }
            }
        }
    }
}
#endif
