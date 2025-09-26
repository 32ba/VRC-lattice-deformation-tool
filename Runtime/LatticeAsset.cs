using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    public enum LatticeInterpolationMode
    {
        Trilinear = 0,
        CubicBernstein = 1
    }

    [System.Serializable]
    public class LatticeAsset : ISerializationCallbackReceiver
    {
        private const int k_MinAxisResolution = 2;

        [SerializeField]
        private Vector3Int _gridSize = new Vector3Int(3, 3, 3);

        [SerializeField]
        private Bounds _localBounds = new Bounds(Vector3.zero, Vector3.one);

        [SerializeField]
        private Vector3[] _controlPointsLocal = System.Array.Empty<Vector3>();

        [SerializeField]
        private LatticeInterpolationMode _interpolation = LatticeInterpolationMode.Trilinear;




        public Vector3Int GridSize
        {
            get => _gridSize;
            set => ResizeGrid(value);
        }

        public Bounds LocalBounds
        {
            get => _localBounds;
            set => _localBounds = value;
        }

        public LatticeInterpolationMode Interpolation
        {
            get => _interpolation;
            set => _interpolation = value;
        }

        public bool UseJobsAndBurst => true;

        public int ControlPointCount => _gridSize.x * _gridSize.y * _gridSize.z;

        public System.ReadOnlySpan<Vector3> ControlPointsLocal => _controlPointsLocal ?? System.Array.Empty<Vector3>();

        public void ResizeGrid(Vector3Int newGridSize)
        {
            newGridSize.x = Mathf.Max(k_MinAxisResolution, newGridSize.x);
            newGridSize.y = Mathf.Max(k_MinAxisResolution, newGridSize.y);
            newGridSize.z = Mathf.Max(k_MinAxisResolution, newGridSize.z);

            if (_gridSize == newGridSize)
            {
                return;
            }

            var oldGrid = _gridSize;
            var oldPoints = _controlPointsLocal;
            bool hasExistingPoints = oldPoints != null && oldPoints.Length == oldGrid.x * oldGrid.y * oldGrid.z;

            _gridSize = newGridSize;

            int newCount = ControlPointCount;
            if (newCount <= 0)
            {
                _controlPointsLocal = Array.Empty<Vector3>();
                return;
            }

            var newPoints = new Vector3[newCount];

            if (hasExistingPoints && oldGrid.x > 0 && oldGrid.y > 0 && oldGrid.z > 0)
            {
                ResampleControlPointsWithJobs(oldPoints, oldGrid, _gridSize, newPoints);
            }
            else
            {
                PopulateControlPointsWithJobs(newPoints);
            }

            _controlPointsLocal = newPoints;
        }

        public Vector3 GetControlPointLocal(int index)
        {
            if (_controlPointsLocal == null || index < 0 || index >= _controlPointsLocal.Length)
            {
                return Vector3.zero;
            }

            return _controlPointsLocal[index];
        }

        public void SetControlPointLocal(int index, Vector3 value)
        {
            if (_controlPointsLocal == null || index < 0 || index >= _controlPointsLocal.Length)
            {
                return;
            }

            _controlPointsLocal[index] = value;
        }

        public void ResetControlPoints()
        {
            EnsureControlPointCapacity();
            PopulateControlPoints();
        }

        public void RelaxInteriorControlPoints(int iterations = 2)
        {
            if (_controlPointsLocal == null)
            {
                return;
            }

            int count = ControlPointCount;
            if (count == 0 || iterations <= 0)
            {
                return;
            }

            int nx = _gridSize.x;
            int ny = _gridSize.y;
            int nz = _gridSize.z;

            if (nx <= 2 || ny <= 2 || nz <= 2)
            {
                return;
            }

            iterations = Mathf.Min(iterations, 16);

            var working = _controlPointsLocal;
            var buffer = new Vector3[count];

            int xStride = 1;
            int yStride = nx;
            int zStride = nx * ny;

            for (int iter = 0; iter < iterations; iter++)
            {
                Array.Copy(working, buffer, count);

                for (int z = 1; z < nz - 1; z++)
                {
                    int zOffset = z * zStride;
                    for (int y = 1; y < ny - 1; y++)
                    {
                        int yOffset = zOffset + y * yStride;
                        for (int x = 1; x < nx - 1; x++)
                        {
                            int index = yOffset + x;

                            Vector3 sum = working[index - xStride] +
                                          working[index + xStride] +
                                          working[index - yStride] +
                                          working[index + yStride] +
                                          working[index - zStride] +
                                          working[index + zStride];

                            buffer[index] = sum / 6f;
                        }
                    }
                }

                var swap = working;
                working = buffer;
                buffer = swap;
            }

            if (!ReferenceEquals(working, _controlPointsLocal))
            {
                Array.Copy(working, _controlPointsLocal, count);
            }
        }

        public void EnsureInitialized()
        {
            EnsureControlPointCapacity();
        }

        private static int Index(Vector3Int grid, int x, int y, int z)
        {
            return x + y * grid.x + z * grid.x * grid.y;
        }

        private static Vector3 SampleControlPoints(Vector3[] points, Vector3Int grid, float u, float v, float w)
        {
            int nx = Mathf.Max(1, grid.x);
            int ny = Mathf.Max(1, grid.y);
            int nz = Mathf.Max(1, grid.z);

            float fx = nx > 1 ? u * (nx - 1) : 0f;
            float fy = ny > 1 ? v * (ny - 1) : 0f;
            float fz = nz > 1 ? w * (nz - 1) : 0f;

            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, nx - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, ny - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(fz), 0, nz - 1);

            int x1 = Mathf.Min(x0 + 1, nx - 1);
            int y1 = Mathf.Min(y0 + 1, ny - 1);
            int z1 = Mathf.Min(z0 + 1, nz - 1);

            float tx = nx > 1 ? fx - x0 : 0f;
            float ty = ny > 1 ? fy - y0 : 0f;
            float tz = nz > 1 ? fz - z0 : 0f;

            Vector3 c000 = points[Index(grid, x0, y0, z0)];
            Vector3 c100 = points[Index(grid, x1, y0, z0)];
            Vector3 c010 = points[Index(grid, x0, y1, z0)];
            Vector3 c110 = points[Index(grid, x1, y1, z0)];
            Vector3 c001 = points[Index(grid, x0, y0, z1)];
            Vector3 c101 = points[Index(grid, x1, y0, z1)];
            Vector3 c011 = points[Index(grid, x0, y1, z1)];
            Vector3 c111 = points[Index(grid, x1, y1, z1)];

            Vector3 c00 = Vector3.Lerp(c000, c100, tx);
            Vector3 c10 = Vector3.Lerp(c010, c110, tx);
            Vector3 c01 = Vector3.Lerp(c001, c101, tx);
            Vector3 c11 = Vector3.Lerp(c011, c111, tx);

            Vector3 c0 = Vector3.Lerp(c00, c10, ty);
            Vector3 c1 = Vector3.Lerp(c01, c11, ty);

            return Vector3.Lerp(c0, c1, tz);
        }

        private void ResampleControlPointsWithJobs(Vector3[] sourcePoints, Vector3Int sourceGrid, Vector3Int targetGrid, Vector3[] target)
        {
            if (sourcePoints == null)
            {
                throw new ArgumentNullException(nameof(sourcePoints));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            int expectedSourceCount = sourceGrid.x * sourceGrid.y * sourceGrid.z;
            int expectedTargetCount = targetGrid.x * targetGrid.y * targetGrid.z;

            if (expectedSourceCount != sourcePoints.Length)
            {
                throw new ArgumentException("Source control point array does not match source grid dimensions.", nameof(sourcePoints));
            }

            if (expectedTargetCount != target.Length)
            {
                throw new ArgumentException("Target control point array does not match target grid dimensions.", nameof(target));
            }

            if (expectedSourceCount == 0 || expectedTargetCount == 0)
            {
                return;
            }

            var sourceNative = new NativeArray<float3>(sourcePoints.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var targetNative = new NativeArray<float3>(target.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            try
            {
                for (int i = 0; i < sourcePoints.Length; i++)
                {
                    var value = sourcePoints[i];
                    sourceNative[i] = new float3(value.x, value.y, value.z);
                }

                var job = new ResampleControlPointsJob
                {
                    OldPoints = sourceNative,
                    Result = targetNative,
                    OldGrid = new int3(math.max(2, sourceGrid.x), math.max(2, sourceGrid.y), math.max(2, sourceGrid.z)),
                    NewGrid = new int3(math.max(2, targetGrid.x), math.max(2, targetGrid.y), math.max(2, targetGrid.z))
                };

                job.Schedule(target.Length, math.max(1, targetGrid.x)).Complete();

                for (int i = 0; i < target.Length; i++)
                {
                    var value = targetNative[i];
                    target[i] = new Vector3(value.x, value.y, value.z);
                }
            }
            finally
            {
                if (sourceNative.IsCreated)
                {
                    sourceNative.Dispose();
                }

                if (targetNative.IsCreated)
                {
                    targetNative.Dispose();
                }
            }
        }



        [BurstCompile]
        private struct ResampleControlPointsJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float3> OldPoints;

            public int3 OldGrid;
            public int3 NewGrid;

            [WriteOnly]
            public NativeArray<float3> Result;

            public void Execute(int index)
            {
                int3 clampedNewGrid = math.max(NewGrid, new int3(1, 1, 1));
                int nx = clampedNewGrid.x;
                int ny = clampedNewGrid.y;
                int nz = clampedNewGrid.z;

                int plane = nx * ny;
                int z = index / plane;
                int y = (index / nx) % ny;
                int x = index % nx;

                float u = nx > 1 ? (float)x / (nx - 1) : 0f;
                float v = ny > 1 ? (float)y / (ny - 1) : 0f;
                float w = nz > 1 ? (float)z / (nz - 1) : 0f;

                Result[index] = SampleOldGrid(u, v, w);
            }

            private float3 SampleOldGrid(float u, float v, float w)
            {
                int3 clampedOldGrid = math.max(OldGrid, new int3(1, 1, 1));

                float fx = clampedOldGrid.x > 1 ? u * (clampedOldGrid.x - 1) : 0f;
                float fy = clampedOldGrid.y > 1 ? v * (clampedOldGrid.y - 1) : 0f;
                float fz = clampedOldGrid.z > 1 ? w * (clampedOldGrid.z - 1) : 0f;

                int x0 = math.clamp((int)math.floor(fx), 0, clampedOldGrid.x - 1);
                int y0 = math.clamp((int)math.floor(fy), 0, clampedOldGrid.y - 1);
                int z0 = math.clamp((int)math.floor(fz), 0, clampedOldGrid.z - 1);

                int x1 = math.min(x0 + 1, clampedOldGrid.x - 1);
                int y1 = math.min(y0 + 1, clampedOldGrid.y - 1);
                int z1 = math.min(z0 + 1, clampedOldGrid.z - 1);

                float tx = clampedOldGrid.x > 1 ? fx - x0 : 0f;
                float ty = clampedOldGrid.y > 1 ? fy - y0 : 0f;
                float tz = clampedOldGrid.z > 1 ? fz - z0 : 0f;

                float3 c000 = OldPoints[Index(clampedOldGrid, x0, y0, z0)];
                float3 c100 = OldPoints[Index(clampedOldGrid, x1, y0, z0)];
                float3 c010 = OldPoints[Index(clampedOldGrid, x0, y1, z0)];
                float3 c110 = OldPoints[Index(clampedOldGrid, x1, y1, z0)];
                float3 c001 = OldPoints[Index(clampedOldGrid, x0, y0, z1)];
                float3 c101 = OldPoints[Index(clampedOldGrid, x1, y0, z1)];
                float3 c011 = OldPoints[Index(clampedOldGrid, x0, y1, z1)];
                float3 c111 = OldPoints[Index(clampedOldGrid, x1, y1, z1)];

                float3 c00 = math.lerp(c000, c100, tx);
                float3 c10 = math.lerp(c010, c110, tx);
                float3 c01 = math.lerp(c001, c101, tx);
                float3 c11 = math.lerp(c011, c111, tx);

                float3 c0 = math.lerp(c00, c10, ty);
                float3 c1 = math.lerp(c01, c11, ty);

                return math.lerp(c0, c1, tz);
            }

            private static int Index(int3 grid, int x, int y, int z)
            {
                return x + y * grid.x + z * grid.x * grid.y;
            }
        }

        [BurstCompile]
        private struct PopulateControlPointsJob : IJobParallelFor
        {
            [WriteOnly]
            public NativeArray<float3> Result;

            public int3 Grid;
            public float3 BoundsMin;
            public float3 BoundsSize;

            public void Execute(int index)
            {
                int nx = Grid.x;
                int ny = Grid.y;

                int plane = nx * ny;
                int z = index / plane;
                int y = (index / nx) % ny;
                int x = index % nx;

                float wx = nx > 1 ? (float)x / (nx - 1) : 0f;
                float wy = ny > 1 ? (float)y / (ny - 1) : 0f;
                float wz = Grid.z > 1 ? (float)z / (Grid.z - 1) : 0f;

                float3 normalized = new float3(wx, wy, wz);
                float3 position = BoundsMin + BoundsSize * normalized;

                Result[index] = position;
            }
        }

        private void EnsureControlPointCapacity()
        {
            int expected = ControlPointCount;
            if (expected <= 0)
            {
                _controlPointsLocal = Array.Empty<Vector3>();
                return;
            }

            bool sizeChanged = _controlPointsLocal == null || _controlPointsLocal.Length != expected;
            if (sizeChanged)
            {
                _controlPointsLocal = new Vector3[expected];
                PopulateControlPoints();
                return;
            }

            if (_controlPointsLocal == null)
            {
                return;
            }

            for (int i = 0; i < _controlPointsLocal.Length; i++)
            {
                if (_controlPointsLocal[i] != Vector3.zero)
                {
                    return;
                }
            }

            PopulateControlPoints();
        }

        private void PopulateControlPoints()
        {
            int expected = ControlPointCount;
            if (expected <= 0 || _controlPointsLocal == null)
            {
                return;
            }

            PopulateControlPointsWithJobs(_localBounds.min, _localBounds.size, _gridSize, _controlPointsLocal);
        }

        private void PopulateControlPointsWithJobs(Vector3[] target)
        {
            if (target == null || target.Length == 0)
            {
                return;
            }

            PopulateControlPointsWithJobs(_localBounds.min, _localBounds.size, _gridSize, target);
        }

        private void PopulateControlPointsWithJobs(Vector3 boundsMin, Vector3 boundsSize, Vector3Int grid, Vector3[] target)
        {
            int expected = grid.x * grid.y * grid.z;
            if (target == null || target.Length != expected || expected == 0)
            {
                throw new ArgumentException("Target array does not match grid dimensions.", nameof(target));
            }

            var targetNative = new NativeArray<float3>(target.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            try
            {
                var job = new PopulateControlPointsJob
                {
                    Result = targetNative,
                    Grid = new int3(math.max(1, grid.x), math.max(1, grid.y), math.max(1, grid.z)),
                    BoundsMin = new float3(boundsMin.x, boundsMin.y, boundsMin.z),
                    BoundsSize = new float3(boundsSize.x, boundsSize.y, boundsSize.z)
                };

                job.Schedule(target.Length, math.max(1, grid.x)).Complete();

                for (int i = 0; i < target.Length; i++)
                {
                    var value = targetNative[i];
                    target[i] = new Vector3(value.x, value.y, value.z);
                }
            }
            finally
            {
                if (targetNative.IsCreated)
                {
                    targetNative.Dispose();
                }
            }
        }

        
        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            EnsureInitialized();
        }
    }
}

