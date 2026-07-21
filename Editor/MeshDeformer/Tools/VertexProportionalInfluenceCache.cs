#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Computes exact world-space proportional-edit influence with a reusable
    /// radius-sized spatial hash. The handler rebuilds it only when selection,
    /// settings, or the source snapshot changes.
    /// </summary>
    internal sealed class VertexProportionalInfluenceCache
    {
        private const int k_DirectSearchThreshold = 32;
        private const int k_MaxDenseBucketCount = 1 << 20;

        private Vector3[] _points = Array.Empty<Vector3>();
        private int[] _pointNext = Array.Empty<int>();
        private int[] _bucketHeads = Array.Empty<int>();
        private int[] _bucketX = Array.Empty<int>();
        private int[] _bucketY = Array.Empty<int>();
        private int[] _bucketZ = Array.Empty<int>();
        private int[] _denseHeads = Array.Empty<int>();
        private float[] _influences = Array.Empty<float>();
        private int _pointCount;
        private int _denseMinX;
        private int _denseMinY;
        private int _denseMinZ;
        private int _denseSizeX;
        private int _denseSizeY;
        private int _denseSizeZ;
        private bool _useDenseGrid;
        private long _lastQueryNodeVisits;

        internal int VertexCount => _influences.Length;
        // Retain the diagnostic name used by existing performance gates. With the
        // spatial hash this counts exact candidate-point distance checks.
        internal long LastQueryNodeVisits => _lastQueryNodeVisits;

        internal void Clear()
        {
            if (_influences.Length > 0)
            {
                Array.Clear(_influences, 0, _influences.Length);
            }

            _pointCount = 0;
            _lastQueryNodeVisits = 0;
        }

        internal void Rebuild(
            Vector3[] worldPositions,
            IReadOnlyCollection<int> selectedVertices,
            float worldRadius,
            VertexSelectionHandler.FalloffType falloff)
        {
            int vertexCount = worldPositions?.Length ?? 0;
            _lastQueryNodeVisits = 0;
            EnsureArray(ref _influences, vertexCount);
            if (vertexCount == 0)
            {
                _pointCount = 0;
                return;
            }

            Array.Clear(_influences, 0, vertexCount);
            if (selectedVertices == null || selectedVertices.Count == 0 || worldRadius <= 0f)
            {
                _pointCount = 0;
                return;
            }

            EnsureArray(ref _points, selectedVertices.Count);
            EnsureArray(ref _pointNext, selectedVertices.Count);

            _pointCount = 0;
            foreach (int vertexIndex in selectedVertices)
            {
                if (vertexIndex < 0 || vertexIndex >= vertexCount) continue;
                _points[_pointCount] = worldPositions[vertexIndex];
                _pointCount++;
            }

            if (_pointCount == 0) return;

            float radiusSquared = worldRadius * worldRadius;
            if (_pointCount <= k_DirectSearchThreshold)
            {
                RebuildWithDirectSearch(worldPositions, worldRadius, radiusSquared, falloff);
                return;
            }

            BuildSpatialHash(worldRadius);
            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                Vector3 position = worldPositions[vertexIndex];
                int cellX = Mathf.FloorToInt(position.x / worldRadius);
                int cellY = Mathf.FloorToInt(position.y / worldRadius);
                int cellZ = Mathf.FloorToInt(position.z / worldRadius);
                float nearestSquared = _useDenseGrid
                    ? FindNearestDenseSquared(position, cellX, cellY, cellZ, radiusSquared)
                    : FindNearestHashedSquared(position, cellX, cellY, cellZ, radiusSquared);

                SetInfluence(vertexIndex, nearestSquared, radiusSquared, worldRadius, falloff);
            }
        }

        internal float GetInfluence(int vertexIndex)
        {
            return vertexIndex >= 0 && vertexIndex < _influences.Length
                ? _influences[vertexIndex]
                : 0f;
        }

        private void RebuildWithDirectSearch(
            Vector3[] worldPositions,
            float worldRadius,
            float radiusSquared,
            VertexSelectionHandler.FalloffType falloff)
        {
            for (int vertexIndex = 0; vertexIndex < worldPositions.Length; vertexIndex++)
            {
                float nearestSquared = radiusSquared;
                Vector3 position = worldPositions[vertexIndex];
                for (int pointIndex = 0; pointIndex < _pointCount; pointIndex++)
                {
                    _lastQueryNodeVisits++;
                    float distanceSquared = (position - _points[pointIndex]).sqrMagnitude;
                    if (distanceSquared < nearestSquared)
                        nearestSquared = distanceSquared;
                }

                SetInfluence(vertexIndex, nearestSquared, radiusSquared, worldRadius, falloff);
            }
        }

        private void BuildSpatialHash(float cellSize)
        {
            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int minZ = int.MaxValue;
            int maxX = int.MinValue;
            int maxY = int.MinValue;
            int maxZ = int.MinValue;
            for (int pointIndex = 0; pointIndex < _pointCount; pointIndex++)
            {
                Vector3 point = _points[pointIndex];
                int x = Mathf.FloorToInt(point.x / cellSize);
                int y = Mathf.FloorToInt(point.y / cellSize);
                int z = Mathf.FloorToInt(point.z / cellSize);
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                minZ = Math.Min(minZ, z);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                maxZ = Math.Max(maxZ, z);
            }

            long sizeX = (long)maxX - minX + 1L;
            long sizeY = (long)maxY - minY + 1L;
            long sizeZ = (long)maxZ - minZ + 1L;
            long denseBucketCount = k_MaxDenseBucketCount + 1L;
            if (sizeX <= k_MaxDenseBucketCount &&
                sizeY <= k_MaxDenseBucketCount &&
                sizeZ <= k_MaxDenseBucketCount)
            {
                long xy = sizeX * sizeY;
                if (xy <= k_MaxDenseBucketCount && sizeZ <= k_MaxDenseBucketCount / xy)
                    denseBucketCount = xy * sizeZ;
            }
            long adaptiveDenseLimit = Math.Max(4096L, _pointCount * 16L);
            _useDenseGrid = denseBucketCount > 0L &&
                            denseBucketCount <= k_MaxDenseBucketCount &&
                            denseBucketCount <= adaptiveDenseLimit;
            if (_useDenseGrid)
            {
                _denseMinX = minX;
                _denseMinY = minY;
                _denseMinZ = minZ;
                _denseSizeX = (int)sizeX;
                _denseSizeY = (int)sizeY;
                _denseSizeZ = (int)sizeZ;
                EnsureArray(ref _denseHeads, (int)denseBucketCount);
                for (int index = 0; index < _denseHeads.Length; index++)
                    _denseHeads[index] = -1;

                for (int pointIndex = 0; pointIndex < _pointCount; pointIndex++)
                {
                    Vector3 point = _points[pointIndex];
                    int x = Mathf.FloorToInt(point.x / cellSize);
                    int y = Mathf.FloorToInt(point.y / cellSize);
                    int z = Mathf.FloorToInt(point.z / cellSize);
                    int bucket = DenseBucketIndex(x, y, z);
                    _pointNext[pointIndex] = _denseHeads[bucket];
                    _denseHeads[bucket] = pointIndex;
                }
                return;
            }

            int bucketCount = 4;
            while (bucketCount < _pointCount * 2) bucketCount <<= 1;
            EnsureArray(ref _bucketHeads, bucketCount);
            EnsureArray(ref _bucketX, bucketCount);
            EnsureArray(ref _bucketY, bucketCount);
            EnsureArray(ref _bucketZ, bucketCount);
            for (int index = 0; index < bucketCount; index++) _bucketHeads[index] = -1;

            for (int pointIndex = 0; pointIndex < _pointCount; pointIndex++)
            {
                Vector3 point = _points[pointIndex];
                int x = Mathf.FloorToInt(point.x / cellSize);
                int y = Mathf.FloorToInt(point.y / cellSize);
                int z = Mathf.FloorToInt(point.z / cellSize);
                int bucket = FindBucketSlot(x, y, z, create: true);
                _pointNext[pointIndex] = _bucketHeads[bucket];
                _bucketHeads[bucket] = pointIndex;
            }
        }

        private int FindBucketHead(int x, int y, int z)
        {
            int slot = FindBucketSlot(x, y, z, create: false);
            return slot >= 0 ? _bucketHeads[slot] : -1;
        }

        private float FindNearestDenseSquared(
            Vector3 position,
            int cellX,
            int cellY,
            int cellZ,
            float nearestSquared)
        {
            long centerX = (long)cellX - _denseMinX;
            long centerY = (long)cellY - _denseMinY;
            long centerZ = (long)cellZ - _denseMinZ;
            if (centerX < -1L || centerX > _denseSizeX ||
                centerY < -1L || centerY > _denseSizeY ||
                centerZ < -1L || centerZ > _denseSizeZ)
            {
                return nearestSquared;
            }
            int startX = (int)Math.Max(0L, centerX - 1L);
            int startY = (int)Math.Max(0L, centerY - 1L);
            int startZ = (int)Math.Max(0L, centerZ - 1L);
            int endX = (int)Math.Min(_denseSizeX - 1L, centerX + 1L);
            int endY = (int)Math.Min(_denseSizeY - 1L, centerY + 1L);
            int endZ = (int)Math.Min(_denseSizeZ - 1L, centerZ + 1L);
            if (startX > endX || startY > endY || startZ > endZ)
                return nearestSquared;

            int planeSize = _denseSizeX * _denseSizeY;
            for (int z = startZ; z <= endZ; z++)
            {
                int planeOffset = z * planeSize;
                for (int y = startY; y <= endY; y++)
                {
                    int bucket = planeOffset + y * _denseSizeX + startX;
                    for (int x = startX; x <= endX; x++, bucket++)
                    {
                        int pointIndex = _denseHeads[bucket];
                        while (pointIndex >= 0)
                        {
                            _lastQueryNodeVisits++;
                            float distanceSquared =
                                (position - _points[pointIndex]).sqrMagnitude;
                            if (distanceSquared < nearestSquared)
                                nearestSquared = distanceSquared;
                            pointIndex = _pointNext[pointIndex];
                        }
                    }
                }
            }
            return nearestSquared;
        }

        private float FindNearestHashedSquared(
            Vector3 position,
            int cellX,
            int cellY,
            int cellZ,
            float nearestSquared)
        {
            for (int zOffset = -1; zOffset <= 1; zOffset++)
            {
                int z = unchecked(cellZ + zOffset);
                for (int yOffset = -1; yOffset <= 1; yOffset++)
                {
                    int y = unchecked(cellY + yOffset);
                    for (int xOffset = -1; xOffset <= 1; xOffset++)
                    {
                        int x = unchecked(cellX + xOffset);
                        int pointIndex = FindBucketHead(x, y, z);
                        while (pointIndex >= 0)
                        {
                            _lastQueryNodeVisits++;
                            float distanceSquared =
                                (position - _points[pointIndex]).sqrMagnitude;
                            if (distanceSquared < nearestSquared)
                                nearestSquared = distanceSquared;
                            pointIndex = _pointNext[pointIndex];
                        }
                    }
                }
            }
            return nearestSquared;
        }

        private int DenseBucketIndex(int x, int y, int z)
        {
            return (x - _denseMinX) +
                   (y - _denseMinY) * _denseSizeX +
                   (z - _denseMinZ) * _denseSizeX * _denseSizeY;
        }

        private int FindBucketSlot(int x, int y, int z, bool create)
        {
            int mask = _bucketHeads.Length - 1;
            int slot = HashCell(x, y, z) & mask;
            while (_bucketHeads[slot] >= 0)
            {
                if (_bucketX[slot] == x && _bucketY[slot] == y && _bucketZ[slot] == z)
                    return slot;
                slot = (slot + 1) & mask;
            }

            if (!create) return -1;
            _bucketX[slot] = x;
            _bucketY[slot] = y;
            _bucketZ[slot] = z;
            return slot;
        }

        private static int HashCell(int x, int y, int z)
        {
            unchecked
            {
                return (x * 73856093) ^ (y * 19349663) ^ (z * 83492791);
            }
        }

        private void SetInfluence(
            int vertexIndex,
            float nearestSquared,
            float radiusSquared,
            float worldRadius,
            VertexSelectionHandler.FalloffType falloff)
        {
            if (nearestSquared >= radiusSquared) return;
            float normalizedDistance = Mathf.Sqrt(nearestSquared) / worldRadius;
            _influences[vertexIndex] = EvaluateFalloff(normalizedDistance, falloff);
        }

        private static float EvaluateFalloff(
            float normalizedDistance,
            VertexSelectionHandler.FalloffType falloff)
        {
            float t = Mathf.Clamp01(normalizedDistance);
            switch (falloff)
            {
                case VertexSelectionHandler.FalloffType.Linear:
                    return 1f - t;
                case VertexSelectionHandler.FalloffType.Smooth:
                    float s = 1f - t;
                    return s * s * (3f - 2f * s);
                case VertexSelectionHandler.FalloffType.Constant:
                    return 1f;
                case VertexSelectionHandler.FalloffType.Sphere:
                    return t < 0.9f ? 1f : Mathf.Clamp01((1f - t) / 0.1f);
                case VertexSelectionHandler.FalloffType.Gaussian:
                    return Mathf.Exp(-3f * t * t);
                default:
                    return 1f - t;
            }
        }

        private static void EnsureArray<T>(ref T[] array, int length)
        {
            if (array == null || array.Length != length)
            {
                array = length == 0 ? Array.Empty<T>() : new T[length];
            }
        }
    }
}
#endif
