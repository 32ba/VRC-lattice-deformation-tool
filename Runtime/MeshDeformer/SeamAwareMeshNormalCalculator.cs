using System;
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    /// <summary>
    /// Recalculates normals while preserving the smoothing boundaries encoded by
    /// the source mesh. Vertices duplicated at a UV seam are shared only when their
    /// source normals agree; intentional hard edges therefore remain separate.
    /// </summary>
    internal static class SeamAwareMeshNormalCalculator
    {
        private const float PositionTolerance = 1e-5f;
        // Keep intentional small creases separate. A one-degree difference has a
        // cosine of about 0.99985, so it must not be treated as the same smoothing
        // group.
        private const float SourceNormalCosine = 0.99999f;

        internal static Vector3[] Calculate(Mesh deformedMesh, Mesh sourceMesh)
        {
            if (deformedMesh == null)
            {
                return Array.Empty<Vector3>();
            }

            var vertices = deformedMesh.vertices;
            deformedMesh.RecalculateNormals();
            var unityNormals = deformedMesh.normals;
            var result = new Vector3[vertices.Length];
            if (unityNormals != null && unityNormals.Length == result.Length)
            {
                Array.Copy(unityNormals, result, result.Length);
            }

            var faceSums = new Vector3[vertices.Length];
            AccumulateFaceNormals(deformedMesh.triangles, vertices, faceSums);

            if (sourceMesh == null || sourceMesh.vertexCount != vertices.Length)
            {
                return result;
            }

            var sourceVertices = sourceMesh.vertices;
            var sourceNormals = sourceMesh.normals;
            if (sourceVertices.Length != vertices.Length || sourceNormals.Length != vertices.Length)
            {
                return result;
            }

            var buckets = new Dictionary<PositionKey, List<int>>();
            for (int i = 0; i < sourceVertices.Length; i++)
            {
                var key = PositionKey.From(sourceVertices[i]);
                if (!buckets.TryGetValue(key, out var bucket))
                {
                    bucket = new List<int>();
                    buckets.Add(key, bucket);
                }

                bucket.Add(i);
            }

            foreach (var bucket in buckets.Values)
            {
                var groups = new List<NormalGroup>();
                for (int i = 0; i < bucket.Count; i++)
                {
                    int vertex = bucket[i];
                    Vector3 sourceNormal = sourceNormals[vertex];
                    float sourceNormalLength = sourceNormal.sqrMagnitude;
                    if (sourceNormalLength <= 1e-20f ||
                        float.IsNaN(sourceNormalLength) || float.IsInfinity(sourceNormalLength))
                    {
                        continue;
                    }

                    sourceNormal *= 1f / Mathf.Sqrt(sourceNormalLength);
                    NormalGroup matching = null;
                    float bestDot = SourceNormalCosine;
                    for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
                    {
                        float dot = Vector3.Dot(sourceNormal, groups[groupIndex].Representative);
                        if (dot >= bestDot)
                        {
                            bestDot = dot;
                            matching = groups[groupIndex];
                        }
                    }

                    if (matching == null)
                    {
                        matching = new NormalGroup(sourceNormal);
                        groups.Add(matching);
                    }

                    matching.Indices.Add(vertex);
                    matching.FaceSum += faceSums[vertex];
                }

                for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
                {
                    var group = groups[groupIndex];
                    if (group.Indices.Count < 2)
                    {
                        continue;
                    }

                    Vector3 normal = Normalize(group.FaceSum);
                    for (int index = 0; index < group.Indices.Count; index++)
                    {
                        result[group.Indices[index]] = normal;
                    }
                }
            }

            return result;
        }

        private static void AccumulateFaceNormals(int[] triangles, Vector3[] vertices, Vector3[] sums)
        {
            if (triangles == null)
            {
                return;
            }

            for (int triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
            {
                int i0 = triangles[triangle];
                int i1 = triangles[triangle + 1];
                int i2 = triangles[triangle + 2];
                if ((uint)i0 >= (uint)vertices.Length ||
                    (uint)i1 >= (uint)vertices.Length ||
                    (uint)i2 >= (uint)vertices.Length)
                {
                    continue;
                }

                Vector3 face = Vector3.Cross(vertices[i1] - vertices[i0], vertices[i2] - vertices[i0]);
                sums[i0] += face;
                sums[i1] += face;
                sums[i2] += face;
            }
        }

        private static Vector3 Normalize(Vector3 value)
        {
            float length = value.sqrMagnitude;
            return length > 1e-20f && !float.IsNaN(length) && !float.IsInfinity(length)
                ? value * (1f / Mathf.Sqrt(length))
                : Vector3.zero;
        }

        private sealed class NormalGroup
        {
            internal readonly Vector3 Representative;
            internal readonly List<int> Indices = new List<int>();
            internal Vector3 FaceSum;

            internal NormalGroup(Vector3 representative)
            {
                Representative = representative;
            }
        }

        private readonly struct PositionKey : IEquatable<PositionKey>
        {
            private readonly int _x;
            private readonly int _y;
            private readonly int _z;

            private PositionKey(int x, int y, int z)
            {
                _x = x;
                _y = y;
                _z = z;
            }

            internal static PositionKey From(Vector3 position)
            {
                return new PositionKey(
                    Mathf.RoundToInt(position.x / PositionTolerance),
                    Mathf.RoundToInt(position.y / PositionTolerance),
                    Mathf.RoundToInt(position.z / PositionTolerance));
            }

            public bool Equals(PositionKey other)
            {
                return _x == other._x && _y == other._y && _z == other._z;
            }

            public override bool Equals(object obj)
            {
                return obj is PositionKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((_x * 397) ^ _y) * 397 ^ _z;
                }
            }
        }
    }
}
