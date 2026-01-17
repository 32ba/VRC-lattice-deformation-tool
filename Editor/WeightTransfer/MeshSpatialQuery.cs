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
    /// Uses a simple grid-based acceleration structure with Burst Jobs for parallel queries.
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

        /// <summary>
        /// Blittable result struct for Burst jobs.
        /// </summary>
        [BurstCompile]
        public struct QueryResultNative
        {
            public bool found;
            public float3 closestPoint;
            public float3 interpolatedNormal;
            public int3 triangleIndices;
            public float3 barycentricCoords;
            public float distance;
            public int triangleIndex;

            public QueryResult ToManaged()
            {
                return new QueryResult
                {
                    found = found,
                    closestPoint = closestPoint,
                    interpolatedNormal = interpolatedNormal,
                    triangleIndices = new Vector3Int(triangleIndices.x, triangleIndices.y, triangleIndices.z),
                    barycentricCoords = barycentricCoords,
                    distance = distance,
                    triangleIndex = triangleIndex
                };
            }
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

        // Native arrays for Burst jobs
        private NativeArray<float3> _nativeVertices;
        private NativeArray<int> _nativeTriangles;
        private NativeArray<float3> _nativeNormals;
        private NativeArray<int> _gridCellStarts;
        private NativeArray<int> _gridCellCounts;
        private NativeArray<int> _gridTriangleIndices;
        private int3 _gridDimensions;
        private bool _nativeDataInitialized;

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

        private void InitializeNativeData()
        {
            if (_nativeDataInitialized) return;

            // Create native arrays for vertices
            _nativeVertices = new NativeArray<float3>(_vertices.Length, Allocator.Persistent);
            for (int i = 0; i < _vertices.Length; i++)
            {
                _nativeVertices[i] = new float3(_vertices[i].x, _vertices[i].y, _vertices[i].z);
            }

            // Create native array for triangles
            _nativeTriangles = new NativeArray<int>(_triangles.Length, Allocator.Persistent);
            _nativeTriangles.CopyFrom(_triangles);

            // Create native array for normals
            if (_normals != null && _normals.Length > 0)
            {
                _nativeNormals = new NativeArray<float3>(_normals.Length, Allocator.Persistent);
                for (int i = 0; i < _normals.Length; i++)
                {
                    _nativeNormals[i] = new float3(_normals[i].x, _normals[i].y, _normals[i].z);
                }
            }
            else
            {
                _nativeNormals = new NativeArray<float3>(0, Allocator.Persistent);
            }

            // Build flattened grid structure for Burst
            BuildNativeGrid();

            _nativeDataInitialized = true;
        }

        private void BuildNativeGrid()
        {
            // Calculate grid dimensions
            var boundsSize = _boundsMax - _boundsMin;
            int dimX = Mathf.Max(1, Mathf.CeilToInt(boundsSize.x / _cellSize) + 1);
            int dimY = Mathf.Max(1, Mathf.CeilToInt(boundsSize.y / _cellSize) + 1);
            int dimZ = Mathf.Max(1, Mathf.CeilToInt(boundsSize.z / _cellSize) + 1);
            _gridDimensions = new int3(dimX, dimY, dimZ);

            int totalCells = dimX * dimY * dimZ;
            _gridCellStarts = new NativeArray<int>(totalCells, Allocator.Persistent);
            _gridCellCounts = new NativeArray<int>(totalCells, Allocator.Persistent);

            // Count triangles per cell
            var tempCounts = new int[totalCells];
            foreach (var kvp in _grid)
            {
                int cellIndex = GetCellIndex(kvp.Key.x, kvp.Key.y, kvp.Key.z);
                if (cellIndex >= 0 && cellIndex < totalCells)
                {
                    tempCounts[cellIndex] = kvp.Value.Count;
                }
            }

            // Compute prefix sums for cell starts
            int totalTriangleRefs = 0;
            for (int i = 0; i < totalCells; i++)
            {
                _gridCellStarts[i] = totalTriangleRefs;
                _gridCellCounts[i] = tempCounts[i];
                totalTriangleRefs += tempCounts[i];
            }

            // Fill triangle indices
            _gridTriangleIndices = new NativeArray<int>(totalTriangleRefs, Allocator.Persistent);
            var currentIndex = new int[totalCells];
            foreach (var kvp in _grid)
            {
                int cellIndex = GetCellIndex(kvp.Key.x, kvp.Key.y, kvp.Key.z);
                if (cellIndex >= 0 && cellIndex < totalCells)
                {
                    int start = _gridCellStarts[cellIndex];
                    foreach (int triIdx in kvp.Value)
                    {
                        _gridTriangleIndices[start + currentIndex[cellIndex]] = triIdx;
                        currentIndex[cellIndex]++;
                    }
                }
            }
        }

        private int GetCellIndex(int x, int y, int z)
        {
            if (x < 0 || x >= _gridDimensions.x ||
                y < 0 || y >= _gridDimensions.y ||
                z < 0 || z >= _gridDimensions.z)
                return -1;
            return x + y * _gridDimensions.x + z * _gridDimensions.x * _gridDimensions.y;
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
        /// Finds closest points for multiple query points using parallel Burst jobs.
        /// </summary>
        public QueryResult[] FindClosestPointsBatch(Vector3[] queryPoints)
        {
            // For small batches, use sequential processing
            if (queryPoints.Length < 100)
            {
                var results = new QueryResult[queryPoints.Length];
                for (int i = 0; i < queryPoints.Length; i++)
                {
                    results[i] = FindClosestPoint(queryPoints[i]);
                }
                return results;
            }

            // Initialize native data if needed
            InitializeNativeData();

            // Create native arrays for job
            var nativeQueryPoints = new NativeArray<float3>(queryPoints.Length, Allocator.TempJob);
            for (int i = 0; i < queryPoints.Length; i++)
            {
                nativeQueryPoints[i] = new float3(queryPoints[i].x, queryPoints[i].y, queryPoints[i].z);
            }

            var nativeResults = new NativeArray<QueryResultNative>(queryPoints.Length, Allocator.TempJob);

            // Schedule and complete job
            var job = new FindClosestPointJob
            {
                queryPoints = nativeQueryPoints,
                vertices = _nativeVertices,
                triangles = _nativeTriangles,
                normals = _nativeNormals,
                gridCellStarts = _gridCellStarts,
                gridCellCounts = _gridCellCounts,
                gridTriangleIndices = _gridTriangleIndices,
                gridDimensions = _gridDimensions,
                boundsMin = new float3(_boundsMin.x, _boundsMin.y, _boundsMin.z),
                cellSize = _cellSize,
                results = nativeResults
            };

            var handle = job.Schedule(queryPoints.Length, 64);
            handle.Complete();

            // Copy results back
            var results2 = new QueryResult[queryPoints.Length];
            for (int i = 0; i < queryPoints.Length; i++)
            {
                results2[i] = nativeResults[i].ToManaged();
            }

            // Cleanup temp arrays
            nativeQueryPoints.Dispose();
            nativeResults.Dispose();

            return results2;
        }

        [BurstCompile]
        private struct FindClosestPointJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> queryPoints;
            [ReadOnly] public NativeArray<float3> vertices;
            [ReadOnly] public NativeArray<int> triangles;
            [ReadOnly] public NativeArray<float3> normals;
            [ReadOnly] public NativeArray<int> gridCellStarts;
            [ReadOnly] public NativeArray<int> gridCellCounts;
            [ReadOnly] public NativeArray<int> gridTriangleIndices;
            public int3 gridDimensions;
            public float3 boundsMin;
            public float cellSize;

            [WriteOnly] public NativeArray<QueryResultNative> results;

            public void Execute(int index)
            {
                float3 queryPoint = queryPoints[index];
                var result = new QueryResultNative
                {
                    found = false,
                    distance = float.MaxValue
                };

                int3 queryCell = WorldToCell(queryPoint);

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
                                    math.abs(dx) != radius &&
                                    math.abs(dy) != radius &&
                                    math.abs(dz) != radius)
                                    continue;

                                int3 cellKey = new int3(
                                    queryCell.x + dx,
                                    queryCell.y + dy,
                                    queryCell.z + dz);

                                int cellIndex = GetCellIndex(cellKey);
                                if (cellIndex < 0 || cellIndex >= gridCellStarts.Length)
                                    continue;

                                int start = gridCellStarts[cellIndex];
                                int count = gridCellCounts[cellIndex];

                                for (int t = 0; t < count; t++)
                                {
                                    int triIdx = gridTriangleIndices[start + t];
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

                    // If we found something and the next radius would be too far, stop
                    if (foundInRadius && result.distance < (radius + 1) * cellSize)
                    {
                        break;
                    }
                }

                results[index] = result;
            }

            private int3 WorldToCell(float3 worldPos)
            {
                return new int3(
                    (int)math.floor((worldPos.x - boundsMin.x) / cellSize),
                    (int)math.floor((worldPos.y - boundsMin.y) / cellSize),
                    (int)math.floor((worldPos.z - boundsMin.z) / cellSize));
            }

            private int GetCellIndex(int3 cell)
            {
                if (cell.x < 0 || cell.x >= gridDimensions.x ||
                    cell.y < 0 || cell.y >= gridDimensions.y ||
                    cell.z < 0 || cell.z >= gridDimensions.z)
                    return -1;
                return cell.x + cell.y * gridDimensions.x + cell.z * gridDimensions.x * gridDimensions.y;
            }

            private QueryResultNative FindClosestPointOnTriangle(float3 queryPoint, int triangleIndex)
            {
                int i0 = triangles[triangleIndex * 3];
                int i1 = triangles[triangleIndex * 3 + 1];
                int i2 = triangles[triangleIndex * 3 + 2];

                float3 v0 = vertices[i0];
                float3 v1 = vertices[i1];
                float3 v2 = vertices[i2];

                // Find closest point on triangle
                float3 closest = ClosestPointOnTriangleBurst(queryPoint, v0, v1, v2, out float3 bary);

                // Interpolate normal if available
                float3 normal;
                if (normals.Length > 0)
                {
                    float3 n0 = normals[i0];
                    float3 n1 = normals[i1];
                    float3 n2 = normals[i2];
                    normal = math.normalize(n0 * bary.x + n1 * bary.y + n2 * bary.z);
                }
                else
                {
                    // Compute face normal
                    normal = math.normalize(math.cross(v1 - v0, v2 - v0));
                }

                return new QueryResultNative
                {
                    found = true,
                    closestPoint = closest,
                    interpolatedNormal = normal,
                    triangleIndices = new int3(i0, i1, i2),
                    barycentricCoords = bary,
                    distance = math.distance(queryPoint, closest),
                    triangleIndex = triangleIndex
                };
            }

            private static float3 ClosestPointOnTriangleBurst(
                float3 p, float3 a, float3 b, float3 c, out float3 barycentricCoords)
            {
                float3 ab = b - a;
                float3 ac = c - a;
                float3 ap = p - a;

                float d1 = math.dot(ab, ap);
                float d2 = math.dot(ac, ap);
                if (d1 <= 0f && d2 <= 0f)
                {
                    barycentricCoords = new float3(1f, 0f, 0f);
                    return a;
                }

                float3 bp = p - b;
                float d3 = math.dot(ab, bp);
                float d4 = math.dot(ac, bp);
                if (d3 >= 0f && d4 <= d3)
                {
                    barycentricCoords = new float3(0f, 1f, 0f);
                    return b;
                }

                float vc = d1 * d4 - d3 * d2;
                if (vc <= 0f && d1 >= 0f && d3 <= 0f)
                {
                    float v = d1 / (d1 - d3);
                    barycentricCoords = new float3(1f - v, v, 0f);
                    return a + v * ab;
                }

                float3 cp = p - c;
                float d5 = math.dot(ab, cp);
                float d6 = math.dot(ac, cp);
                if (d6 >= 0f && d5 <= d6)
                {
                    barycentricCoords = new float3(0f, 0f, 1f);
                    return c;
                }

                float vb = d5 * d2 - d1 * d6;
                if (vb <= 0f && d2 >= 0f && d6 <= 0f)
                {
                    float w = d2 / (d2 - d6);
                    barycentricCoords = new float3(1f - w, 0f, w);
                    return a + w * ac;
                }

                float va = d3 * d6 - d5 * d4;
                if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
                {
                    float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                    barycentricCoords = new float3(0f, 1f - w, w);
                    return b + w * (c - b);
                }

                float denom = 1f / (va + vb + vc);
                float v2 = vb * denom;
                float w2 = vc * denom;
                barycentricCoords = new float3(1f - v2 - w2, v2, w2);
                return a + ab * v2 + ac * w2;
            }
        }

        public void Dispose()
        {
            _grid.Clear();

            if (_nativeDataInitialized)
            {
                if (_nativeVertices.IsCreated) _nativeVertices.Dispose();
                if (_nativeTriangles.IsCreated) _nativeTriangles.Dispose();
                if (_nativeNormals.IsCreated) _nativeNormals.Dispose();
                if (_gridCellStarts.IsCreated) _gridCellStarts.Dispose();
                if (_gridCellCounts.IsCreated) _gridCellCounts.Dispose();
                if (_gridTriangleIndices.IsCreated) _gridTriangleIndices.Dispose();
                _nativeDataInitialized = false;
            }
        }
    }
}
