#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class TopologySeamUtility
    {
        private const float k_DegenerateNormalSquared = 1e-12f;
        private const float k_OppositeFaceNormalDot = -0.9999f;

        private readonly struct PositionKey : IEquatable<PositionKey>, IComparable<PositionKey>
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
                // Treat signed zero as the same geometric coordinate.
                return value == 0f ? 0 : BitConverter.SingleToInt32Bits(value);
            }

            public int CompareTo(PositionKey other)
            {
                int comparison = _x.CompareTo(other._x);
                if (comparison != 0) return comparison;
                comparison = _y.CompareTo(other._y);
                return comparison != 0 ? comparison : _z.CompareTo(other._z);
            }

            public bool Equals(PositionKey other) =>
                _x == other._x && _y == other._y && _z == other._z;

            public override bool Equals(object obj) =>
                obj is PositionKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(_x, _y, _z);
        }

        private readonly struct GeometricEdgeKey : IEquatable<GeometricEdgeKey>
        {
            private readonly PositionKey _a;
            private readonly PositionKey _b;

            internal GeometricEdgeKey(PositionKey from, PositionKey to)
            {
                if (from.CompareTo(to) <= 0)
                {
                    _a = from;
                    _b = to;
                }
                else
                {
                    _a = to;
                    _b = from;
                }
            }

            public bool Equals(GeometricEdgeKey other) =>
                _a.Equals(other._a) && _b.Equals(other._b);

            public override bool Equals(object obj) =>
                obj is GeometricEdgeKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(_a, _b);
        }

        private readonly struct HalfEdge
        {
            internal readonly int FromIndex;
            internal readonly int ToIndex;
            internal readonly PositionKey FromPosition;
            internal readonly PositionKey ToPosition;
            internal readonly Vector3 FaceNormal;

            internal HalfEdge(
                int fromIndex,
                int toIndex,
                Vector3 fromPosition,
                Vector3 toPosition,
                Vector3 faceNormal)
            {
                FromIndex = fromIndex;
                ToIndex = toIndex;
                FromPosition = new PositionKey(fromPosition);
                ToPosition = new PositionKey(toPosition);
                FaceNormal = faceNormal;
            }

            internal bool IsReverseIndexEdge(HalfEdge other) =>
                FromIndex == other.ToIndex && ToIndex == other.FromIndex;

            internal bool IsReverseGeometricEdge(HalfEdge other) =>
                FromPosition.Equals(other.ToPosition) &&
                ToPosition.Equals(other.FromPosition);
        }

        private sealed class Topology
        {
            internal readonly int TriangleCount;
            internal readonly List<HalfEdge> UnmatchedEdges;

            internal Topology(int triangleCount, List<HalfEdge> unmatchedEdges)
            {
                TriangleCount = triangleCount;
                UnmatchedEdges = unmatchedEdges;
            }
        }

        internal static bool IsClosedSurface(Vector3[] vertices, IReadOnlyList<int> indices)
        {
            return TryBuild(vertices, indices, out Topology topology) &&
                   topology.TriangleCount > 0 &&
                   topology.UnmatchedEdges.Count == 0;
        }

        internal static void MarkOpenBoundaryVertices(
            Vector3[] vertices,
            IReadOnlyList<int> indices,
            bool[] boundaryVertices)
        {
            if (boundaryVertices == null || vertices == null ||
                boundaryVertices.Length < vertices.Length ||
                !TryBuild(vertices, indices, out Topology topology))
            {
                return;
            }

            for (int edgeIndex = 0; edgeIndex < topology.UnmatchedEdges.Count; edgeIndex++)
            {
                HalfEdge edge = topology.UnmatchedEdges[edgeIndex];
                boundaryVertices[edge.FromIndex] = true;
                boundaryVertices[edge.ToIndex] = true;
            }
        }

        private static bool TryBuild(
            Vector3[] vertices,
            IReadOnlyList<int> indices,
            out Topology topology)
        {
            topology = null;
            if (vertices == null || indices == null ||
                vertices.Length == 0 || indices.Count < 3)
            {
                return false;
            }

            // First preserve the source index topology. Only raw boundary half-edges are
            // eligible for attribute-seam reconciliation; globally welding equal positions
            // would incorrectly connect overlapping but independent surfaces.
            var rawEdges = new Dictionary<ulong, List<HalfEdge>>();
            int triangleCount = 0;
            for (int index = 0; index + 2 < indices.Count; index += 3)
            {
                int i0 = indices[index];
                int i1 = indices[index + 1];
                int i2 = indices[index + 2];
                if ((uint)i0 >= (uint)vertices.Length ||
                    (uint)i1 >= (uint)vertices.Length ||
                    (uint)i2 >= (uint)vertices.Length)
                {
                    continue;
                }

                Vector3 a = vertices[i0];
                Vector3 b = vertices[i1];
                Vector3 c = vertices[i2];
                if (!IsFinite(a) || !IsFinite(b) || !IsFinite(c))
                {
                    continue;
                }

                Vector3 faceNormal = Vector3.Cross(b - a, c - a);
                if (!IsFinite(faceNormal) ||
                    faceNormal.sqrMagnitude <= k_DegenerateNormalSquared)
                {
                    continue;
                }

                faceNormal.Normalize();
                triangleCount++;
                AddRawEdge(rawEdges, new HalfEdge(i0, i1, a, b, faceNormal));
                AddRawEdge(rawEdges, new HalfEdge(i1, i2, b, c, faceNormal));
                AddRawEdge(rawEdges, new HalfEdge(i2, i0, c, a, faceNormal));
            }

            if (triangleCount == 0)
            {
                return false;
            }

            var seamCandidates =
                new Dictionary<GeometricEdgeKey, List<HalfEdge>>();
            var unmatchedEdges = new List<HalfEdge>();

            foreach (List<HalfEdge> rawGroup in rawEdges.Values)
            {
                if (rawGroup.Count == 2 &&
                    rawGroup[0].IsReverseIndexEdge(rawGroup[1]) &&
                    CanJoinFaces(rawGroup[0], rawGroup[1]))
                {
                    continue;
                }

                if (rawGroup.Count == 1)
                {
                    AddSeamCandidate(seamCandidates, rawGroup[0]);
                    continue;
                }

                // Same-direction pairs and non-manifold raw edges are not valid
                // manifold adjacency. Keep every endpoint visible as a boundary.
                unmatchedEdges.AddRange(rawGroup);
            }

            foreach (List<HalfEdge> seamGroup in seamCandidates.Values)
            {
                if (seamGroup.Count == 2 &&
                    seamGroup[0].IsReverseGeometricEdge(seamGroup[1]) &&
                    CanJoinFaces(seamGroup[0], seamGroup[1]))
                {
                    continue;
                }

                unmatchedEdges.AddRange(seamGroup);
            }

            topology = new Topology(triangleCount, unmatchedEdges);
            return true;
        }

        private static bool CanJoinFaces(HalfEdge first, HalfEdge second)
        {
            float normalDot = Vector3.Dot(first.FaceNormal, second.FaceNormal);
            return !float.IsNaN(normalDot) &&
                   !float.IsInfinity(normalDot) &&
                   normalDot > k_OppositeFaceNormalDot;
        }

        private static void AddRawEdge(
            Dictionary<ulong, List<HalfEdge>> edges,
            HalfEdge edge)
        {
            uint min = (uint)Math.Min(edge.FromIndex, edge.ToIndex);
            uint max = (uint)Math.Max(edge.FromIndex, edge.ToIndex);
            ulong key = ((ulong)min << 32) | max;
            if (!edges.TryGetValue(key, out List<HalfEdge> group))
            {
                group = new List<HalfEdge>(2);
                edges.Add(key, group);
            }

            group.Add(edge);
        }

        private static void AddSeamCandidate(
            Dictionary<GeometricEdgeKey, List<HalfEdge>> candidates,
            HalfEdge edge)
        {
            var key = new GeometricEdgeKey(edge.FromPosition, edge.ToPosition);
            if (!candidates.TryGetValue(key, out List<HalfEdge> group))
            {
                group = new List<HalfEdge>(2);
                candidates.Add(key, group);
            }

            group.Add(edge);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
#endif
