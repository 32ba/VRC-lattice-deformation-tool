using System;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    internal static class SourceVertexResolver
    {
        internal static Vector3[] Resolve(Mesh sourceMesh, float[] shapeWeights, SourceVertexWorkspace workspace,
            out Vector3[][] bakedBlendShapeDeltas,
            out float[] bakedBlendShapeWeights,
            out int bakedBlendShapeHash)
        {
            bakedBlendShapeDeltas = null;
            bakedBlendShapeWeights = null;
            bakedBlendShapeHash = 0;

            if (!DeformerPlatformServices.CanReadMesh(sourceMesh))
            {
                return null;
            }

            if (shapeWeights != null && shapeWeights.Length != sourceMesh.blendShapeCount)
                throw new ArgumentException("BlendShape weights must match the source shape count.", nameof(shapeWeights));

            int sourceVertexCount = sourceMesh.vertexCount;
            if (sourceVertexCount <= 0)
            {
                return Array.Empty<Vector3>();
            }

            workspace.EnsureCapacity(sourceVertexCount);
            sourceMesh.GetVertices(workspace.VertexScratch);
            if (workspace.VertexScratch.Count != sourceVertexCount)
            {
                return null;
            }

            workspace.VertexScratch.CopyTo(workspace.Vertices, 0);
            var vertices = workspace.Vertices;

            if (shapeWeights == null || sourceMesh.blendShapeCount == 0)
            {
                return vertices;
            }

            int shapeCount = sourceMesh.blendShapeCount;
            int vertexCount = sourceMesh.vertexCount;
            Vector3[][] deltas = null;
            float[] weights = null;
            bool hasBakedShape = false;
            int hash = 17;

            for (int s = 0; s < shapeCount; s++)
            {
                float weight = shapeWeights[s];
                if (Mathf.Abs(weight) <= 1e-5f)
                {
                    continue;
                }

                var delta = SourceBlendShapeEvaluator.EvaluateDelta(sourceMesh, s, weight);
                deltas ??= new Vector3[shapeCount][];
                weights ??= new float[shapeCount];
                deltas[s] = delta;
                weights[s] = weight;
                hasBakedShape = true;
                hash = HashCode.Combine(hash, s, weight);

                for (int v = 0; v < vertexCount; v++)
                {
                    vertices[v] += delta[v];
                }
            }

            if (!hasBakedShape)
            {
                return vertices;
            }

            bakedBlendShapeDeltas = deltas;
            bakedBlendShapeWeights = weights;
            bakedBlendShapeHash = hash;
            return vertices;
        }
    }
}
