#if UNITY_EDITOR
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    // Applies a prepared influence query to borrowed deformation buffers.
    // Caller owns validation, Undo, snapshots, caches, and preview publication.
    internal static class BrushDisplacementApplication
    {
        internal static bool Normal(Vector3[] vertices, Vector3[] normals, Vector3[] displacements, float[] vertexMask,
            BrushInfluenceQuery query, float strength, float direction, SymmetryVertexMap mirrorMap = null, int mirrorAxis = 0)
        {
            bool modified = false;
            int vertexCount = vertices.Length;
            int iterationCount = query.CandidateCount(vertexCount);
            for (int iteration = 0; iteration < iterationCount; iteration++)
            {
                int i = query.CandidateAt(iteration);
                if (mirrorMap != null && !mirrorMap.TryGetPartner(i, out _)) continue;
                var vertex = vertices[i] + displacements[i];
                if (!query.TryGetFalloff(i, vertex, out float falloff)) continue;

                var normal = normals[i].normalized;
                if (normal.sqrMagnitude < 0.001f) normal = Vector3.up;

                if (mirrorMap != null) normal = SymmetryVertexMapCache.MirrorDirection(normal, mirrorAxis);
                var delta = normal * (strength * falloff * direction);
                float maskValue = MaskValue(vertexMask, i);
                if (maskValue < 1e-6f) continue;
                delta *= maskValue;
                displacements[i] += delta;
                modified = true;
            }

            return modified;
        }

        internal static bool Move(Vector3[] vertices, Vector3[] displacements, float[] vertexMask,
            BrushInfluenceQuery query, float strength, Vector3 localDelta,
            SkinnedVertexHelper.RestSpaceDeltaConverter restSpaceConverter)
        {
            bool modified = false;
            int vertexCount = vertices.Length;
            int iterationCount = query.CandidateCount(vertexCount);
            for (int iteration = 0; iteration < iterationCount; iteration++)
            {
                int i = query.CandidateAt(iteration);
                var vertex = vertices[i] + displacements[i];
                if (!query.TryGetFalloff(i, vertex, out float falloff)) continue;

                var storedDelta = restSpaceConverter != null
                    ? restSpaceConverter.ConvertOrFallback(i, localDelta)
                    : localDelta;
                var delta = storedDelta * (strength * falloff * 10f);
                float maskValue = MaskValue(vertexMask, i);
                if (maskValue < 1e-6f) continue;
                delta *= maskValue;
                displacements[i] += delta;
                modified = true;
            }

            return modified;
        }

        internal static bool Smooth(Vector3[] vertices, Vector3[] displacements, Vector3[] currentDisplacements, float[] vertexMask,
            MeshAdjacency adjacency, BrushInfluenceQuery query, float smoothFactor, SymmetryVertexMap mirrorMap = null)
        {
            bool modified = false;
            int vertexCount = vertices.Length;
            int iterationCount = query.CandidateCount(vertexCount);
            for (int iteration = 0; iteration < iterationCount; iteration++)
            {
                int i = query.CandidateAt(iteration);
                if (mirrorMap != null && !mirrorMap.TryGetPartner(i, out _)) continue;
                var vertex = vertices[i] + currentDisplacements[i];
                if (!query.TryGetFalloff(i, vertex, out float falloff)) continue;

                // Compute average displacement of neighbors
                int neighborStart = adjacency.GetNeighborStart(i);
                int neighborEnd = adjacency.GetNeighborEnd(i);
                if (neighborStart == neighborEnd) continue;
                var averageDisp = Vector3.zero;
                for (int edge = neighborStart; edge < neighborEnd; edge++)
                {
                    int neighbor = adjacency.GetNeighbor(edge);
                    averageDisp += currentDisplacements[neighbor];
                }
                averageDisp /= neighborEnd - neighborStart;

                // Blend toward neighbor average
                var currentDisp = currentDisplacements[i];
                float maskValue = MaskValue(vertexMask, i);
                if (maskValue < 1e-6f) continue;
                var targetDisp = Vector3.Lerp(currentDisp, averageDisp, smoothFactor * falloff * maskValue);
                displacements[i] = targetDisp;
                modified = true;
            }

            return modified;
        }

        internal static bool Mask(Vector3[] vertices, Vector3[] displacements, LatticeLayer layer,
            BrushInfluenceQuery query, float targetValue, float strength, SymmetryVertexMap mirrorMap = null)
        {
            bool modified = false;
            int iterationCount = query.CandidateCount(vertices.Length);
            for (int iteration = 0; iteration < iterationCount; iteration++)
            {
                int i = query.CandidateAt(iteration);
                if (mirrorMap != null && !mirrorMap.TryGetPartner(i, out _)) continue;
                var vertex = vertices[i] + displacements[i];
                if (!query.TryGetFalloff(i, vertex, out float falloff)) continue;
                float current = layer.GetVertexMask(i);
                float blend = Mathf.Lerp(current, targetValue, falloff * strength);
                layer.SetVertexMask(i, blend);
                modified = true;
            }
            return modified;
        }

        internal static float MaskValue(float[] mask, int vertexIndex)
        {
            return mask != null && vertexIndex >= 0 && vertexIndex < mask.Length
                ? mask[vertexIndex]
                : 1f;
        }

    }
}
#endif
