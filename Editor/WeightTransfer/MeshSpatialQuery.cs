using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;

namespace Net._32Ba.LatticeDeformationTool.Editor.WeightTransfer
{
    /// <summary>
    /// Spatial query structure for finding closest points on a mesh surface.
    /// Uses a simple grid-based acceleration structure.
    /// </summary>
    public class MeshSpatialQuery : IDisposable
    {
        /// <summary>
        /// Result of a closest point query.
        /// </summary>
        public struct QueryResult
        {
            public bool found;
            public Vector3 closestPoint;
            public Vector3 interpolatedNormal;
            public Vector3Int triangleIndices; // x, y, z = vertex indices of the triangle
            public Vector3 barycentricCoords;  // barycentric coordinates within the triangle
            public float distance;
            public int triangleIndex;
        }

        private readonly Vector3[] _vertices;
        private readonly int[] _triangles;
        private readonly Vector3[] _normals;
        private readonly int _triangleCount;

        // Grid-based acceleration structure
        private readonly Dictionary<Vector3Int, List<int>> _grid;
        private readonly float _cellSize;
        private readonly Vector3 _boundsMin;
        private readonly Vector3 _boundsMax;

        /// <summary>
        /// Creates a new spatial query structure from mesh data.
        /// </summary>
        public MeshSpatialQuery(Vector3[] vertices, int[] triangles, Vector3[] normals)
        {
            _vertices = vertices ?? throw new ArgumentNullException(nameof(vertices));
            _triangles = triangles ?? throw new ArgumentNullException(nameof(triangles));
            _normals = normals;
            _triangleCount = triangles.Length / 3;

            // Compute bounds
            _boundsMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            _boundsMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            foreach (var v in vertices)
            {
                _boundsMin = Vector3.Min(_boundsMin, v);
                _boundsMax = Vector3.Max(_boundsMax, v);
            }

            // Compute cell size based on mesh bounds
            var boundsSize = _boundsMax - _boundsMin;
            float maxSize = Mathf.Max(boundsSize.x, Mathf.Max(boundsSize.y, boundsSize.z));
            _cellSize = maxSize / 32f; // 32 cells along longest axis
            if (_cellSize < 0.001f) _cellSize = 0.001f;

            // Build grid
            _grid = new Dictionary<Vector3Int, List<int>>();
            BuildGrid();
        }

        private void BuildGrid()
        {
            for (int t = 0; t < _triangleCount; t++)
            {
                int i0 = _triangles[t * 3];
                int i1 = _triangles[t * 3 + 1];
                int i2 = _triangles[t * 3 + 2];

                var v0 = _vertices[i0];
                var v1 = _vertices[i1];
                var v2 = _vertices[i2];

                // Compute triangle bounds
                var triMin = Vector3.Min(v0, Vector3.Min(v1, v2));
                var triMax = Vector3.Max(v0, Vector3.Max(v1, v2));

                // Get cell range
                var cellMin = WorldToCell(triMin);
                var cellMax = WorldToCell(triMax);

                // Add triangle to all cells it overlaps
                for (int x = cellMin.x; x <= cellMax.x; x++)
                {
                    for (int y = cellMin.y; y <= cellMax.y; y++)
                    {
                        for (int z = cellMin.z; z <= cellMax.z; z++)
                        {
                            var cellKey = new Vector3Int(x, y, z);
                            if (!_grid.TryGetValue(cellKey, out var list))
                            {
                                list = new List<int>();
                                _grid[cellKey] = list;
                            }
                            list.Add(t);
                        }
                    }
                }
            }
        }

        private Vector3Int WorldToCell(Vector3 worldPos)
        {
            return new Vector3Int(
                Mathf.FloorToInt((worldPos.x - _boundsMin.x) / _cellSize),
                Mathf.FloorToInt((worldPos.y - _boundsMin.y) / _cellSize),
                Mathf.FloorToInt((worldPos.z - _boundsMin.z) / _cellSize));
        }

        /// <summary>
        /// Finds the closest point on the mesh surface to the given position.
        /// </summary>
        public QueryResult FindClosestPoint(Vector3 queryPoint)
        {
            var result = new QueryResult
            {
                found = false,
                distance = float.MaxValue
            };

            var queryCell = WorldToCell(queryPoint);

            // Search in expanding radius
            for (int radius = 0; radius <= 8; radius++)
            {
                bool foundInRadius = false;

                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        for (int dz = -radius; dz <= radius; dz++)
                        {
                            // Only check cells on the surface of the current radius cube
                            if (radius > 0 &&
                                Mathf.Abs(dx) != radius &&
                                Mathf.Abs(dy) != radius &&
                                Mathf.Abs(dz) != radius)
                                continue;

                            var cellKey = new Vector3Int(
                                queryCell.x + dx,
                                queryCell.y + dy,
                                queryCell.z + dz);

                            if (_grid.TryGetValue(cellKey, out var triangles))
                            {
                                foreach (int triIdx in triangles)
                                {
                                    var triResult = FindClosestPointOnTriangle(queryPoint, triIdx);
                                    if (triResult.distance < result.distance)
                                    {
                                        result = triResult;
                                        foundInRadius = true;
                                    }
                                }
                            }
                        }
                    }
                }

                // If we found something and the next radius would be too far, stop
                if (foundInRadius && result.distance < (radius + 1) * _cellSize)
                {
                    break;
                }
            }

            return result;
        }

        private QueryResult FindClosestPointOnTriangle(Vector3 queryPoint, int triangleIndex)
        {
            int i0 = _triangles[triangleIndex * 3];
            int i1 = _triangles[triangleIndex * 3 + 1];
            int i2 = _triangles[triangleIndex * 3 + 2];

            var v0 = _vertices[i0];
            var v1 = _vertices[i1];
            var v2 = _vertices[i2];

            // Find closest point on triangle
            var closest = ClosestPointOnTriangle(queryPoint, v0, v1, v2, out var bary);

            // Interpolate normal if available
            Vector3 normal;
            if (_normals != null && _normals.Length > 0)
            {
                var n0 = _normals[i0];
                var n1 = _normals[i1];
                var n2 = _normals[i2];
                normal = (n0 * bary.x + n1 * bary.y + n2 * bary.z).normalized;
            }
            else
            {
                // Compute face normal
                normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
            }

            return new QueryResult
            {
                found = true,
                closestPoint = closest,
                interpolatedNormal = normal,
                triangleIndices = new Vector3Int(i0, i1, i2),
                barycentricCoords = bary,
                distance = Vector3.Distance(queryPoint, closest),
                triangleIndex = triangleIndex
            };
        }

        /// <summary>
        /// Finds the closest point on a triangle to a given point.
        /// Returns barycentric coordinates in the output parameter.
        /// </summary>
        private static Vector3 ClosestPointOnTriangle(
            Vector3 p, Vector3 a, Vector3 b, Vector3 c, out Vector3 barycentricCoords)
        {
            // Check if P is in vertex region outside A
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = p - a;

            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f)
            {
                barycentricCoords = new Vector3(1f, 0f, 0f);
                return a;
            }

            // Check if P is in vertex region outside B
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3)
            {
                barycentricCoords = new Vector3(0f, 1f, 0f);
                return b;
            }

            // Check if P is in edge region of AB
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                barycentricCoords = new Vector3(1f - v, v, 0f);
                return a + v * ab;
            }

            // Check if P is in vertex region outside C
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6)
            {
                barycentricCoords = new Vector3(0f, 0f, 1f);
                return c;
            }

            // Check if P is in edge region of AC
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                barycentricCoords = new Vector3(1f - w, 0f, w);
                return a + w * ac;
            }

            // Check if P is in edge region of BC
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                barycentricCoords = new Vector3(0f, 1f - w, w);
                return b + w * (c - b);
            }

            // P is inside the triangle
            float denom = 1f / (va + vb + vc);
            float v2 = vb * denom;
            float w2 = vc * denom;
            barycentricCoords = new Vector3(1f - v2 - w2, v2, w2);
            return a + ab * v2 + ac * w2;
        }

        /// <summary>
        /// Finds closest points for multiple query points using parallel jobs.
        /// </summary>
        public QueryResult[] FindClosestPointsBatch(Vector3[] queryPoints)
        {
            var results = new QueryResult[queryPoints.Length];

            // For now, use simple sequential processing
            // TODO: Implement parallel job version for better performance
            for (int i = 0; i < queryPoints.Length; i++)
            {
                results[i] = FindClosestPoint(queryPoints[i]);
            }

            return results;
        }

        public void Dispose()
        {
            _grid.Clear();
        }
    }
}
