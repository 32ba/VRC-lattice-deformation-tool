using System;
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    internal sealed class SourceVertexWorkspace
    {
        internal readonly List<Vector3> VertexScratch = new List<Vector3>();
        internal Vector3[] Vertices = Array.Empty<Vector3>();
        internal float[] WeightSnapshot = Array.Empty<float>();

        internal void EnsureCapacity(int vertexCount)
        {
            if (Vertices.Length != vertexCount) Vertices = new Vector3[vertexCount];
            if (VertexScratch.Capacity < vertexCount) VertexScratch.Capacity = vertexCount;
        }

        // Unity object reads finish here; the resolver consumes only Mesh + values.
        internal float[] CaptureWeights(SkinnedMeshRenderer renderer, int count)
        {
            if (renderer == null || count == 0) return null;
            if (WeightSnapshot.Length != count) WeightSnapshot = new float[count];
            for (int i = 0; i < count; i++) WeightSnapshot[i] = renderer.GetBlendShapeWeight(i);
            return WeightSnapshot;
        }
    }
}
