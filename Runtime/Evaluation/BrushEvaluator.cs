using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    internal static class BrushEvaluator
    {
        internal static void Apply(LatticeLayer layer, Vector3[] sourceVertices, Vector3[] deformedVertices)
        {
            if (layer == null || sourceVertices == null || deformedVertices == null)
            {
                return;
            }

            var displacements = layer.BrushDisplacements;
            if (displacements == null || displacements.Length != sourceVertices.Length)
            {
                return;
            }

            float weight = layer.Weight;
            var mask = layer.VertexMask;
            bool hasMask = mask != null && mask.Length == sourceVertices.Length;
            for (int vertex = 0; vertex < deformedVertices.Length; vertex++)
            {
                float maskValue = hasMask ? mask[vertex] : 1f;
                deformedVertices[vertex] += displacements[vertex] * weight * maskValue;
            }
        }

        // Same contribution as Apply for vertices indices[0..count), written to
        // compactDeformed[i]. Index-addressed payloads are validated against the
        // full vertex count exactly as Apply validates them against its input.
        internal static void ApplySubset(LatticeLayer layer, int vertexCount, int[] indices, int count,
            Vector3[] compactDeformed)
        {
            if (layer == null || indices == null || compactDeformed == null)
            {
                return;
            }

            var displacements = layer.BrushDisplacements;
            if (displacements == null || displacements.Length != vertexCount)
            {
                return;
            }

            float weight = layer.Weight;
            var mask = layer.VertexMask;
            bool hasMask = mask != null && mask.Length == vertexCount;
            for (int i = 0; i < count; i++)
            {
                int vertex = indices[i];
                float maskValue = hasMask ? mask[vertex] : 1f;
                compactDeformed[i] += displacements[vertex] * weight * maskValue;
            }
        }
    }
}
