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
    }
}
