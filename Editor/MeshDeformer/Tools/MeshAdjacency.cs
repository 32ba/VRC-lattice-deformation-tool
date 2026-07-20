#if UNITY_EDITOR
using System;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>Compact immutable vertex adjacency in compressed sparse row form.</summary>
    internal sealed class MeshAdjacency
    {
        private readonly int[] _offsets;
        private readonly int[] _neighbors;

        internal int VertexCount => _offsets.Length - 1;
        internal int NeighborCount => _neighbors.Length;

        private MeshAdjacency(int[] offsets, int[] neighbors)
        {
            _offsets = offsets;
            _neighbors = neighbors;
        }

        internal int GetNeighborStart(int vertex) => _offsets[vertex];
        internal int GetNeighborEnd(int vertex) => _offsets[vertex + 1];
        internal int GetNeighbor(int index) => _neighbors[index];

        internal static MeshAdjacency Build(int vertexCount, int[] triangles)
        {
            vertexCount = Mathf.Max(0, vertexCount);
            if (vertexCount == 0 || triangles == null || triangles.Length < 3)
                return new MeshAdjacency(new int[vertexCount + 1], Array.Empty<int>());

            // At most six directed edges are emitted per triangle. Sorting one flat
            // buffer avoids a HashSet object (and buckets) for every mesh vertex.
            var edges = new long[(triangles.Length / 3) * 6];
            int edgeCount = 0;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                AddPair(a, b, vertexCount, edges, ref edgeCount);
                AddPair(b, c, vertexCount, edges, ref edgeCount);
                AddPair(a, c, vertexCount, edges, ref edgeCount);
            }

            Array.Sort(edges, 0, edgeCount);
            int uniqueCount = 0;
            long previous = -1;
            for (int i = 0; i < edgeCount; i++)
            {
                long edge = edges[i];
                if (edge == previous) continue;
                edges[uniqueCount++] = edge;
                previous = edge;
            }

            var offsets = new int[vertexCount + 1];
            for (int i = 0; i < uniqueCount; i++)
                offsets[(int)(edges[i] >> 32) + 1]++;
            for (int i = 1; i < offsets.Length; i++)
                offsets[i] += offsets[i - 1];

            var neighbors = new int[uniqueCount];
            for (int i = 0; i < uniqueCount; i++)
                neighbors[i] = unchecked((int)edges[i]);
            return new MeshAdjacency(offsets, neighbors);
        }

        private static void AddPair(
            int a,
            int b,
            int vertexCount,
            long[] edges,
            ref int edgeCount)
        {
            if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount) return;
            edges[edgeCount++] = ((long)a << 32) | (uint)b;
            edges[edgeCount++] = ((long)b << 32) | (uint)a;
        }
    }
}
#endif
