#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class TopologySeamUtility
    {
        private readonly struct PositionKey : IEquatable<PositionKey>
        {
            private readonly int _x;
            private readonly int _y;
            private readonly int _z;

            internal PositionKey(Vector3 value)
            {
                _x = FloatBits(value.x);
                _y = FloatBits(value.y);
                _z = FloatBits(value.z);
            }

            private static int FloatBits(float value)
            {
                if (value == 0f) return 0;
                return BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
            }

            public bool Equals(PositionKey other) =>
                _x == other._x && _y == other._y && _z == other._z;
            public override bool Equals(object obj) => obj is PositionKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(_x, _y, _z);
        }

        private readonly struct TriangleKey : IEquatable<TriangleKey>
        {
            private readonly int _a;
            private readonly int _b;
            private readonly int _c;

            internal TriangleKey(int a, int b, int c)
            {
                if (a > b) Swap(ref a, ref b);
                if (b > c) Swap(ref b, ref c);
                if (a > b) Swap(ref a, ref b);
                _a = a;
                _b = b;
                _c = c;
            }

            private static void Swap(ref int left, ref int right)
            {
                int temporary = left;
                left = right;
                right = temporary;
            }

            public bool Equals(TriangleKey other) =>
                _a == other._a && _b == other._b && _c == other._c;
            public override bool Equals(object obj) => obj is TriangleKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(_a, _b, _c);
        }

        private sealed class EdgeStats
        {
            internal int Count;
            internal int Forward;
            internal int Reverse;
            internal int A;
            internal int B;
        }

        private sealed class Topology
        {
            internal Dictionary<int, List<int>> Members;
            internal Dictionary<ulong, EdgeStats> Edges;
            internal bool HasDuplicateTriangle;
        }

        internal static bool IsClosedSurface(Vector3[] vertices, IReadOnlyList<int> indices)
        {
            Topology topology;
            if (!TryBuild(vertices, indices, out topology) ||
                topology.HasDuplicateTriangle || topology.Edges.Count == 0)
            {
                return false;
            }

            foreach (EdgeStats edge in topology.Edges.Values)
            {
                if (edge.Count != 2 || edge.Forward != 1 || edge.Reverse != 1)
                    return false;
            }
            return true;
        }

        internal static void MarkOpenBoundaryVertices(
            Vector3[] vertices,
            IReadOnlyList<int> indices,
            bool[] boundaryVertices)
        {
            Topology topology;
            if (boundaryVertices == null || vertices == null ||
                boundaryVertices.Length < vertices.Length ||
                !TryBuild(vertices, indices, out topology))
            {
                return;
            }

            if (topology.HasDuplicateTriangle)
            {
                MarkRawOpenBoundaryVertices(indices, boundaryVertices, vertices.Length);
                return;
            }

            foreach (EdgeStats edge in topology.Edges.Values)
            {
                if (edge.Count != 1) continue;
                MarkMembers(topology.Members, edge.A, boundaryVertices);
                MarkMembers(topology.Members, edge.B, boundaryVertices);
            }
        }

        private static bool TryBuild(Vector3[] vertices, IReadOnlyList<int> indices, out Topology topology)
        {
            topology = null;
            if (vertices == null || indices == null || vertices.Length == 0 || indices.Count < 3)
                return false;

            var logicalIds = new int[vertices.Length];
            var positionIds = new Dictionary<PositionKey, int>();
            var members = new Dictionary<int, List<int>>();
            int nextId = 0;
            for (int vertex = 0; vertex < vertices.Length; vertex++)
            {
                var key = new PositionKey(vertices[vertex]);
                int id;
                if (!positionIds.TryGetValue(key, out id))
                {
                    id = nextId++;
                    positionIds.Add(key, id);
                    members.Add(id, new List<int>());
                }
                logicalIds[vertex] = id;
                members[id].Add(vertex);
            }

            var edges = new Dictionary<ulong, EdgeStats>();
            var triangles = new HashSet<TriangleKey>();
            bool duplicateTriangle = false;
            for (int index = 0; index + 2 < indices.Count; index += 3)
            {
                int i0 = indices[index];
                int i1 = indices[index + 1];
                int i2 = indices[index + 2];
                if ((uint)i0 >= (uint)vertices.Length ||
                    (uint)i1 >= (uint)vertices.Length ||
                    (uint)i2 >= (uint)vertices.Length)
                    continue;

                int a = logicalIds[i0];
                int b = logicalIds[i1];
                int c = logicalIds[i2];
                if (a == b || b == c || c == a) continue;
                if (!triangles.Add(new TriangleKey(a, b, c))) duplicateTriangle = true;
                AddEdge(edges, a, b);
                AddEdge(edges, b, c);
                AddEdge(edges, c, a);
            }

            topology = new Topology
            {
                Members = members,
                Edges = edges,
                HasDuplicateTriangle = duplicateTriangle
            };
            return edges.Count > 0;
        }

        private static void AddEdge(Dictionary<ulong, EdgeStats> edges, int from, int to)
        {
            uint min = (uint)Math.Min(from, to);
            uint max = (uint)Math.Max(from, to);
            ulong key = ((ulong)min << 32) | max;
            EdgeStats stats;
            if (!edges.TryGetValue(key, out stats))
            {
                stats = new EdgeStats { A = (int)min, B = (int)max };
                edges.Add(key, stats);
            }
            stats.Count++;
            if (from < to) stats.Forward++; else stats.Reverse++;
        }

        private static void MarkMembers(
            Dictionary<int, List<int>> members,
            int logicalId,
            bool[] boundaryVertices)
        {
            List<int> vertices;
            if (!members.TryGetValue(logicalId, out vertices)) return;
            for (int i = 0; i < vertices.Count; i++) boundaryVertices[vertices[i]] = true;
        }

        private static void MarkRawOpenBoundaryVertices(
            IReadOnlyList<int> indices,
            bool[] boundaryVertices,
            int vertexCount)
        {
            var counts = new Dictionary<ulong, int>();
            for (int index = 0; index + 2 < indices.Count; index += 3)
            {
                int a = indices[index];
                int b = indices[index + 1];
                int c = indices[index + 2];
                if ((uint)a >= (uint)vertexCount || (uint)b >= (uint)vertexCount || (uint)c >= (uint)vertexCount)
                    continue;
                CountRawEdge(counts, a, b);
                CountRawEdge(counts, b, c);
                CountRawEdge(counts, c, a);
            }

            foreach (KeyValuePair<ulong, int> edge in counts)
            {
                if (edge.Value != 1) continue;
                boundaryVertices[(int)(edge.Key >> 32)] = true;
                boundaryVertices[(int)(edge.Key & uint.MaxValue)] = true;
            }
        }

        private static void CountRawEdge(Dictionary<ulong, int> counts, int a, int b)
        {
            uint min = (uint)Math.Min(a, b);
            uint max = (uint)Math.Max(a, b);
            ulong key = ((ulong)min << 32) | max;
            int count;
            counts.TryGetValue(key, out count);
            counts[key] = count + 1;
        }
    }
}
#endif
