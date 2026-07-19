using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    public enum UnmatchedSymmetryVertexBehavior
    {
        Skip = 0,
        Self = 1
    }

    /// <summary>
    /// Immutable vertex-to-vertex correspondence across a configurable symmetry plane.
    /// </summary>
    public sealed class SymmetryVertexMap
    {
        private readonly int[] _partners;

        internal SymmetryVertexMap(int[] partners, int unmatchedCount)
        {
            _partners = partners;
            UnmatchedCount = unmatchedCount;
        }

        public int Count => _partners.Length;
        public int UnmatchedCount { get; }
        public int this[int vertexIndex] => _partners[vertexIndex];

        public bool TryGetPartner(int vertexIndex, out int partnerIndex)
        {
            if ((uint)vertexIndex >= (uint)_partners.Length)
            {
                partnerIndex = -1;
                return false;
            }

            partnerIndex = _partners[vertexIndex];
            return partnerIndex >= 0;
        }
    }

    /// <summary>
    /// Builds and caches symmetry maps without repeatedly scanning every vertex pair.
    /// </summary>
    public static class SymmetryVertexMapCache
    {
        public const float DefaultTolerance = 0.001f;

        private static readonly ConditionalWeakTable<Mesh, MeshCache> s_meshCaches =
            new ConditionalWeakTable<Mesh, MeshCache>();

        public static SymmetryVertexMap GetOrCreate(
            Mesh mesh,
            int axis,
            float centerOffset = 0f,
            float tolerance = DefaultTolerance,
            UnmatchedSymmetryVertexBehavior unmatchedBehavior = UnmatchedSymmetryVertexBehavior.Skip)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            ValidateSettings(axis, centerOffset, tolerance);

            var key = new MapKey(axis, centerOffset, tolerance, unmatchedBehavior);
            var cache = s_meshCaches.GetOrCreateValue(mesh);
            if (cache.VertexCount != mesh.vertexCount)
            {
                cache.Maps.Clear();
                cache.VertexCount = mesh.vertexCount;
            }

            if (!cache.Maps.TryGetValue(key, out var map))
            {
                map = Build(mesh.vertices, axis, centerOffset, tolerance, unmatchedBehavior);
                cache.Maps.Add(key, map);
            }

            return map;
        }

        public static SymmetryVertexMap Build(
            Vector3[] vertices,
            int axis,
            float centerOffset = 0f,
            float tolerance = DefaultTolerance,
            UnmatchedSymmetryVertexBehavior unmatchedBehavior = UnmatchedSymmetryVertexBehavior.Skip)
        {
            if (vertices == null) throw new ArgumentNullException(nameof(vertices));
            ValidateSettings(axis, centerOffset, tolerance);
            if (!Enum.IsDefined(typeof(UnmatchedSymmetryVertexBehavior), unmatchedBehavior))
                throw new ArgumentOutOfRangeException(nameof(unmatchedBehavior));

            var buckets = BuildBuckets(vertices, tolerance);
            var partners = new int[vertices.Length];
            for (int i = 0; i < partners.Length; i++) partners[i] = -1;
            float toleranceSq = tolerance * tolerance;
            int unmatchedCount = 0;

            for (int i = 0; i < vertices.Length; i++)
            {
                if (partners[i] >= 0)
                {
                    continue;
                }

                if (!IsFinite(vertices[i]))
                {
                    partners[i] = unmatchedBehavior == UnmatchedSymmetryVertexBehavior.Self ? i : -1;
                    unmatchedCount++;
                    continue;
                }

                float signedDistance = GetAxisCoordinate(vertices[i], axis) - centerOffset;
                if (Mathf.Abs(signedDistance) <= tolerance)
                {
                    partners[i] = i;
                    continue;
                }

                var mirrored = Mirror(vertices[i], axis, centerOffset);
                int bestIndex = FindBestUnpairedCandidate(
                    vertices,
                    buckets,
                    partners,
                    i,
                    mirrored,
                    axis,
                    centerOffset,
                    signedDistance,
                    tolerance,
                    toleranceSq);

                if (bestIndex >= 0)
                {
                    partners[i] = bestIndex;
                    partners[bestIndex] = i;
                }
                else
                {
                    partners[i] = unmatchedBehavior == UnmatchedSymmetryVertexBehavior.Self ? i : -1;
                    unmatchedCount++;
                }
            }

            return new SymmetryVertexMap(partners, unmatchedCount);
        }

        public static void Invalidate(Mesh mesh)
        {
            if (mesh != null)
            {
                s_meshCaches.Remove(mesh);
            }
        }

        public static Vector3 Mirror(Vector3 value, int axis, float centerOffset = 0f)
        {
            ValidateAxis(axis);
            float mirroredCoordinate = centerOffset * 2f - GetAxisCoordinate(value, axis);
            SetAxisCoordinate(ref value, axis, mirroredCoordinate);
            return value;
        }

        public static Vector3 MirrorDirection(Vector3 value, int axis)
        {
            ValidateAxis(axis);
            SetAxisCoordinate(ref value, axis, -GetAxisCoordinate(value, axis));
            return value;
        }

        public static float GetSignedDistance(Vector3 value, int axis, float centerOffset = 0f)
        {
            ValidateAxis(axis);
            return GetAxisCoordinate(value, axis) - centerOffset;
        }

        private static Dictionary<GridKey, List<int>> BuildBuckets(Vector3[] vertices, float cellSize)
        {
            var buckets = new Dictionary<GridKey, List<int>>(vertices.Length);
            for (int i = 0; i < vertices.Length; i++)
            {
                if (!IsFinite(vertices[i]))
                {
                    continue;
                }

                var key = GridKey.FromPosition(vertices[i], cellSize);
                if (!buckets.TryGetValue(key, out var indices))
                {
                    indices = new List<int>(1);
                    buckets.Add(key, indices);
                }

                indices.Add(i);
            }

            return buckets;
        }

        private static int FindBestUnpairedCandidate(
            Vector3[] vertices,
            Dictionary<GridKey, List<int>> buckets,
            int[] partners,
            int sourceIndex,
            Vector3 mirrored,
            int axis,
            float centerOffset,
            float sourceSignedDistance,
            float cellSize,
            float toleranceSq)
        {
            var centerKey = GridKey.FromPosition(mirrored, cellSize);
            int bestIndex = -1;
            float bestDistanceSq = float.MaxValue;

            for (int z = -1; z <= 1; z++)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                var key = new GridKey(centerKey.X + x, centerKey.Y + y, centerKey.Z + z);
                if (!buckets.TryGetValue(key, out var candidates))
                {
                    continue;
                }

                for (int candidateOffset = 0; candidateOffset < candidates.Count; candidateOffset++)
                {
                    int candidateIndex = candidates[candidateOffset];
                    if (candidateIndex <= sourceIndex || partners[candidateIndex] >= 0)
                    {
                        continue;
                    }

                    float candidateSignedDistance =
                        GetAxisCoordinate(vertices[candidateIndex], axis) - centerOffset;
                    if ((sourceSignedDistance > 0f) == (candidateSignedDistance > 0f))
                    {
                        continue;
                    }

                    float distanceSq = (vertices[candidateIndex] - mirrored).sqrMagnitude;
                    if (distanceSq > toleranceSq)
                    {
                        continue;
                    }

                    if (bestIndex < 0 || distanceSq < bestDistanceSq ||
                        (distanceSq == bestDistanceSq && candidateIndex < bestIndex))
                    {
                        bestIndex = candidateIndex;
                        bestDistanceSq = distanceSq;
                    }
                }
            }

            return bestIndex;
        }

        private static float GetAxisCoordinate(Vector3 value, int axis)
        {
            return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static void SetAxisCoordinate(ref Vector3 value, int axis, float coordinate)
        {
            if (axis == 0) value.x = coordinate;
            else if (axis == 1) value.y = coordinate;
            else value.z = coordinate;
        }

        private static void ValidateSettings(int axis, float centerOffset, float tolerance)
        {
            ValidateAxis(axis);
            if (float.IsNaN(centerOffset) || float.IsInfinity(centerOffset))
                throw new ArgumentOutOfRangeException(nameof(centerOffset));
            if (float.IsNaN(tolerance) || float.IsInfinity(tolerance) || tolerance <= 0f)
                throw new ArgumentOutOfRangeException(nameof(tolerance));
        }

        private static void ValidateAxis(int axis)
        {
            if ((uint)axis > 2u) throw new ArgumentOutOfRangeException(nameof(axis));
        }

        private sealed class MeshCache
        {
            internal int VertexCount = -1;
            internal readonly Dictionary<MapKey, SymmetryVertexMap> Maps =
                new Dictionary<MapKey, SymmetryVertexMap>();
        }

        private readonly struct MapKey : IEquatable<MapKey>
        {
            private readonly int _axis;
            private readonly float _centerOffset;
            private readonly float _tolerance;
            private readonly UnmatchedSymmetryVertexBehavior _unmatchedBehavior;

            internal MapKey(int axis, float centerOffset, float tolerance,
                UnmatchedSymmetryVertexBehavior unmatchedBehavior)
            {
                _axis = axis;
                _centerOffset = centerOffset;
                _tolerance = tolerance;
                _unmatchedBehavior = unmatchedBehavior;
            }

            public bool Equals(MapKey other)
            {
                return _axis == other._axis && _centerOffset.Equals(other._centerOffset) &&
                       _tolerance.Equals(other._tolerance) && _unmatchedBehavior == other._unmatchedBehavior;
            }

            public override bool Equals(object obj) => obj is MapKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _axis;
                    hash = hash * 397 ^ _centerOffset.GetHashCode();
                    hash = hash * 397 ^ _tolerance.GetHashCode();
                    hash = hash * 397 ^ (int)_unmatchedBehavior;
                    return hash;
                }
            }
        }

        private readonly struct GridKey : IEquatable<GridKey>
        {
            internal readonly int X;
            internal readonly int Y;
            internal readonly int Z;

            internal GridKey(int x, int y, int z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            internal static GridKey FromPosition(Vector3 position, float cellSize)
            {
                return new GridKey(
                    Mathf.FloorToInt(position.x / cellSize),
                    Mathf.FloorToInt(position.y / cellSize),
                    Mathf.FloorToInt(position.z / cellSize));
            }

            public bool Equals(GridKey other) => X == other.X && Y == other.Y && Z == other.Z;
            public override bool Equals(object obj) => obj is GridKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = X;
                    hash = hash * 397 ^ Y;
                    hash = hash * 397 ^ Z;
                    return hash;
                }
            }
        }
    }
}
