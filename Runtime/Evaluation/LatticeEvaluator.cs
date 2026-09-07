using System;
using System.Diagnostics.CodeAnalysis;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    // Owns interpolation cache, managed scratch and native job buffers. No Editor,
    // component, Renderer or Transform lookup is allowed in this evaluator.
    // Dispose releases native storage; a later evaluation can lazily reacquire it.
    internal sealed class LatticeEvaluator : IDisposable
    {
        private NativeArray<float3> _deformControlNative;
        private NativeArray<LatticeCacheEntry> _deformEntriesNative;
        private NativeArray<float3> _deformOutputNative;
        private NativeArray<float> _deformBernsteinWeightsNative;
        private LatticeCacheEntry[] _deformEntriesSource;
        private float[] _deformBernsteinWeightsSource;
        private Vector3[] _controlBuffer = Array.Empty<Vector3>();
        private Vector3[] _latticeOutputBuffer = Array.Empty<Vector3>();
        private LatticeDeformerCache _cache = new LatticeDeformerCache();
        private readonly Action _beforeNativeAllocation;

        // Optional fault injection verifies cleanup without requesting an OOM.
        internal LatticeEvaluator(Action beforeNativeAllocation = null)
        {
            _beforeNativeAllocation = beforeNativeAllocation;
        }

        internal LatticeDeformerCache Cache => _cache;
        internal bool HasNativeResources => _deformControlNative.IsCreated || _deformEntriesNative.IsCreated ||
            _deformOutputNative.IsCreated || _deformBernsteinWeightsNative.IsCreated;

        // The component keeps an alias solely for existing private compatibility seams.
        internal void BindCache(LatticeDeformerCache cache) => _cache = cache ?? throw new ArgumentNullException(nameof(cache));

        internal void Apply(LatticeAsset layerSettings, float weight, in EvaluationSemantics semantics,
            Vector3[] sourceVertices, Vector3[] deformedVertices)
        {
            if (layerSettings == null || sourceVertices == null || deformedVertices == null)
            {
                return;
            }

            if (!EnsureCache(layerSettings, sourceVertices))
            {
                return;
            }

            var entries = _cache.Entries;
            if (entries == null || entries.Length != sourceVertices.Length)
            {
                return;
            }

            int cpCount = layerSettings.ControlPointCount;
            EnsureControlBuffer(cpCount);
            if (_latticeOutputBuffer.Length != sourceVertices.Length)
                _latticeOutputBuffer = new Vector3[sourceVertices.Length];

            if (semantics.AbsoluteLatticeEvaluation)
            {
                Matrix4x4 worldToLocal = semantics.OwnerWorldToLocal;

                if (!layerSettings.TryCopyLegacyEvaluationControlPoints(
                        worldToLocal,
                        _controlBuffer.AsSpan()))
                {
                    return;
                }

                var layerVertices = DeformWithJobs(entries, _controlBuffer, _latticeOutputBuffer);
                for (int vertex = 0; vertex < deformedVertices.Length; vertex++)
                {
                    deformedVertices[vertex] +=
                        (layerVertices[vertex] - sourceVertices[vertex]) * weight;
                }
            }
            else
            {
                CollectControlPointOffsetsLocal(layerSettings, _controlBuffer.AsSpan());
                var layerOffsets = DeformWithJobs(entries, _controlBuffer, _latticeOutputBuffer);
                for (int vertex = 0; vertex < deformedVertices.Length; vertex++)
                {
                    deformedVertices[vertex] += layerOffsets[vertex] * weight;
                }
            }
        }

        internal void EnsureControlBuffer(int controlPointCount)
        {
            if (controlPointCount <= 0)
            {
                _controlBuffer = Array.Empty<Vector3>();
                return;
            }

            if (_controlBuffer == null || _controlBuffer.Length != controlPointCount)
            {
                _controlBuffer = new Vector3[controlPointCount];
            }
        }

        internal static void CollectControlPointsLocal(LatticeAsset settings, Span<Vector3> buffer)
        {
            if (settings == null || buffer.IsEmpty)
            {
                return;
            }

            var source = settings.ControlPointsLocal;
            if (source.Length != buffer.Length)
            {
                throw new InvalidOperationException("Control point buffer length does not match the lattice asset data.");
            }

            source.CopyTo(buffer);
        }

        internal static void CollectControlPointOffsetsLocal(LatticeAsset settings, Span<Vector3> buffer)
        {
            if (settings == null || buffer.IsEmpty)
            {
                return;
            }

            var source = settings.ControlPointsLocal;
            if (source.Length != buffer.Length)
            {
                throw new InvalidOperationException("Control point buffer length does not match the lattice asset data.");
            }

            var grid = settings.GridSize;
            var bounds = settings.LocalBounds;
            var boundsMin = bounds.min;
            var boundsSize = bounds.size;
            int index = 0;
            for (int z = 0; z < grid.z; z++)
            {
                float wz = grid.z > 1 ? (float)z / (grid.z - 1) : 0f;
                for (int y = 0; y < grid.y; y++)
                {
                    float wy = grid.y > 1 ? (float)y / (grid.y - 1) : 0f;
                    for (int x = 0; x < grid.x; x++, index++)
                    {
                        float wx = grid.x > 1 ? (float)x / (grid.x - 1) : 0f;
                        var neutral = boundsMin + Vector3.Scale(boundsSize, new Vector3(wx, wy, wz));
                        buffer[index] = source[index] - neutral;
                    }
                }
            }
        }

        internal Vector3[] DeformWithJobs(
            LatticeCacheEntry[] entries,
            Vector3[] controlPoints)
        {
            if (entries == null || entries.Length == 0)
            {
                throw new ArgumentException("Cache entries are required for deformation.", nameof(entries));
            }
            if (controlPoints == null || controlPoints.Length == 0)
            {
                throw new ArgumentException("Control points are required for deformation.", nameof(controlPoints));
            }
            return DeformWithJobs(entries, controlPoints, new Vector3[entries.Length]);
        }

        internal Vector3[] DeformWithJobs(
            LatticeCacheEntry[] entries,
            Vector3[] controlPoints,
            Vector3[] result)
        {
            if (entries == null || entries.Length == 0)
            {
                throw new ArgumentException("Cache entries are required for deformation.", nameof(entries));
            }

            if (controlPoints == null || controlPoints.Length == 0)
            {
                throw new ArgumentException("Control points are required for deformation.", nameof(controlPoints));
            }

            if (result == null || result.Length != entries.Length)
            {
                throw new ArgumentException(
                    "The caller-owned result buffer must match the cache entry count.",
                    nameof(result));
            }

            try
            {
                bool useBernstein = _cache != null &&
                                    _cache.Interpolation == LatticeInterpolationMode.CubicBernstein &&
                                    _cache.HasValidBernsteinWeights(entries.Length);
                EnsureDeformationNativeBuffers(entries, controlPoints.Length, useBernstein);
                _deformControlNative.CopyFromManaged(controlPoints);
                if (useBernstein)
                {
                    var bernsteinJob = new DeformBernsteinVerticesJob
                    {
                        ControlPoints = _deformControlNative,
                        Weights = _deformBernsteinWeightsNative,
                        Grid = new int3(_cache.GridSize.x, _cache.GridSize.y, _cache.GridSize.z),
                        Result = _deformOutputNative
                    };
    
                    bernsteinJob.Schedule(entries.Length, 64).Complete();
                }
                else
                {
                    var job = new DeformVerticesJob
                    {
                        ControlPoints = _deformControlNative,
                        Entries = _deformEntriesNative,
                        Result = _deformOutputNative
                    };
    
                    job.Schedule(entries.Length, 64).Complete();
                }
    
                _deformOutputNative.CopyToManaged(result);
                return result;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void EnsureDeformationNativeBuffers(
            LatticeCacheEntry[] entries,
            int controlPointCount,
            bool useBernstein)
        {
            if (!_deformControlNative.IsCreated || _deformControlNative.Length != controlPointCount)
            {
                if (_deformControlNative.IsCreated) _deformControlNative.Dispose();
                _beforeNativeAllocation?.Invoke();
                _deformControlNative = LatticeNativeArrayUtility.CreateFloat3Array(
                    controlPointCount,
                    Allocator.Persistent);
            }

            if (!_deformOutputNative.IsCreated || _deformOutputNative.Length != entries.Length)
            {
                if (_deformOutputNative.IsCreated) _deformOutputNative.Dispose();
                _beforeNativeAllocation?.Invoke();
                _deformOutputNative = LatticeNativeArrayUtility.CreateFloat3Array(
                    entries.Length,
                    Allocator.Persistent);
            }

            if (!_deformEntriesNative.IsCreated ||
                _deformEntriesNative.Length != entries.Length ||
                !ReferenceEquals(_deformEntriesSource, entries))
            {
                if (_deformEntriesNative.IsCreated) _deformEntriesNative.Dispose();
                _beforeNativeAllocation?.Invoke();
                _deformEntriesNative = LatticeNativeArrayUtility.CreateCopy(entries, Allocator.Persistent);
                _deformEntriesSource = entries;
            }

            if (!useBernstein) return;

            float[] weights = _cache.BernsteinWeights;
            if (!_deformBernsteinWeightsNative.IsCreated ||
                _deformBernsteinWeightsNative.Length != weights.Length ||
                !ReferenceEquals(_deformBernsteinWeightsSource, weights))
            {
                if (_deformBernsteinWeightsNative.IsCreated) _deformBernsteinWeightsNative.Dispose();
                _beforeNativeAllocation?.Invoke();
                _deformBernsteinWeightsNative = LatticeNativeArrayUtility.CreateCopy(
                    weights,
                    Allocator.Persistent);
                _deformBernsteinWeightsSource = weights;
            }
        }

        public void Dispose()
        {
            if (_deformControlNative.IsCreated) _deformControlNative.Dispose();
            if (_deformEntriesNative.IsCreated) _deformEntriesNative.Dispose();
            if (_deformOutputNative.IsCreated) _deformOutputNative.Dispose();
            if (_deformBernsteinWeightsNative.IsCreated) _deformBernsteinWeightsNative.Dispose();
            _deformEntriesSource = null;
            _deformBernsteinWeightsSource = null;
        }

        internal static LatticeCacheEntry[] BuildCacheWithJobs(Vector3Int gridSize, Bounds bounds, Vector3[] restVertices)
        {
            if (restVertices == null || restVertices.Length == 0)
            {
                throw new ArgumentException("Rest vertices are required to build the cache.", nameof(restVertices));
            }

            using var restNative = LatticeNativeArrayUtility.CreateCopy(restVertices, Allocator.TempJob);
            using var entriesNative = new NativeArray<LatticeCacheEntry>(restVertices.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            var job = new BuildCacheEntriesJob
            {
                Grid = new int3(gridSize.x, gridSize.y, gridSize.z),
                BoundsMin = new float3(bounds.min.x, bounds.min.y, bounds.min.z),
                BoundsSize = new float3(bounds.size.x, bounds.size.y, bounds.size.z),
                RestVertices = restNative,
                Entries = entriesNative
            };

            job.Schedule(restVertices.Length, 64).Complete();

            var entries = new LatticeCacheEntry[entriesNative.Length];
            entriesNative.CopyToManaged(entries);
            return entries;
        }

        internal static float[] BuildBernsteinWeightsWithJobs(
            Vector3Int gridSize,
            LatticeCacheEntry[] entries)
        {
            if (entries == null || entries.Length == 0)
            {
                return Array.Empty<float>();
            }

            int stride = checked(gridSize.x + gridSize.y + gridSize.z);
            int weightCount = checked(entries.Length * stride);

            using var entriesNative = LatticeNativeArrayUtility.CreateCopy(entries, Allocator.TempJob);
            using var weightsNative = new NativeArray<float>(
                weightCount,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);

            var job = new BuildBernsteinWeightsJob
            {
                Entries = entriesNative,
                Grid = new int3(gridSize.x, gridSize.y, gridSize.z),
                Weights = weightsNative
            };

            job.Schedule(entries.Length, 64).Complete();

            var weights = new float[weightCount];
            weightsNative.CopyToManaged(weights);
            return weights;
        }

        internal bool EnsureCache(LatticeAsset settings, Vector3[] restVertices)
        {
            if (settings == null)
            {
                return false;
            }

            if (_cache == null)
            {
                _cache = new LatticeDeformerCache();
            }

            int restVerticesHash = DeformationEvaluationMath.HashVertices(restVertices);
            LatticeInterpolationMode effectiveInterpolation = GetEffectiveInterpolation(settings);
            if (_cache.IsCompatibleWith(
                    settings,
                    restVertices.Length,
                    restVerticesHash,
                    effectiveInterpolation))
            {
                return true;
            }

            return RebuildCache(
                settings,
                restVertices,
                restVerticesHash,
                effectiveInterpolation);
        }

        internal bool RebuildCache(
            LatticeAsset settings,
            Vector3[] restVertices,
            int restVerticesHash,
            LatticeInterpolationMode effectiveInterpolation)
        {
            UnityEngine.Profiling.Profiler.BeginSample(
                "LatticeDeformer.RebuildInterpolationCache");
            try
            {
                if (settings == null || restVertices == null)
                {
                    return false;
                }

                var gridSize = settings.GridSize;
                if (gridSize.x < 2 || gridSize.y < 2 || gridSize.z < 2)
                {
                    return false;
                }

                int vertexCount = restVertices.Length;
                if (vertexCount <= 0)
                {
                    _cache.Clear();
                    return false;
                }

                var bounds = settings.LocalBounds;
                LatticeCacheEntry[] entries;

                entries = BuildCacheWithJobs(gridSize, bounds, restVertices);
                float[] bernsteinWeights = effectiveInterpolation == LatticeInterpolationMode.CubicBernstein
                    ? BuildBernsteinWeightsWithJobs(gridSize, entries)
                    : Array.Empty<float>();

                _cache.Populate(
                    gridSize,
                    bounds,
                    effectiveInterpolation,
                    vertexCount,
                    restVerticesHash,
                    entries,
                    restVertices,
                    bernsteinWeights);
                return true;
            }
            finally
            {
                UnityEngine.Profiling.Profiler.EndSample();
            }
        }

        internal static LatticeInterpolationMode GetEffectiveInterpolation(LatticeAsset settings)
        {
            if (settings != null &&
                settings.Interpolation == LatticeInterpolationMode.CubicBernstein &&
                settings.UsesLegacyTrilinearInterpolation)
            {
                return LatticeInterpolationMode.Trilinear;
            }

            return settings?.Interpolation ?? LatticeInterpolationMode.Trilinear;
        }

        [BurstCompile]
        [ExcludeFromCodeCoverage]
        private struct DeformVerticesJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<LatticeCacheEntry> Entries;

            [ReadOnly]
            public NativeArray<float3> ControlPoints;

            [WriteOnly]
            public NativeArray<float3> Result;

            public void Execute(int index)
            {
                var entry = Entries[index];
                float4 w0 = entry.Weights0;
                float4 w1 = entry.Weights1;

                float3 value =
                    w0.x * ControlPoints[entry.Corner0] +
                    w0.y * ControlPoints[entry.Corner1] +
                    w0.z * ControlPoints[entry.Corner2] +
                    w0.w * ControlPoints[entry.Corner3] +
                    w1.x * ControlPoints[entry.Corner4] +
                    w1.y * ControlPoints[entry.Corner5] +
                    w1.z * ControlPoints[entry.Corner6] +
                    w1.w * ControlPoints[entry.Corner7];

                Result[index] = value;
            }
        }

        [BurstCompile]
        [ExcludeFromCodeCoverage]
        private struct DeformBernsteinVerticesJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float3> ControlPoints;

            [ReadOnly]
            public NativeArray<float> Weights;

            public int3 Grid;

            [WriteOnly]
            public NativeArray<float3> Result;

            public void Execute(int index)
            {
                int stride = Grid.x + Grid.y + Grid.z;
                int weightBase = index * stride;
                int yWeightBase = weightBase + Grid.x;
                int zWeightBase = yWeightBase + Grid.y;
                int xyStride = Grid.x * Grid.y;
                float3 value = float3.zero;

                for (int z = 0; z < Grid.z; z++)
                {
                    float wz = Weights[zWeightBase + z];
                    int zOffset = z * xyStride;
                    for (int y = 0; y < Grid.y; y++)
                    {
                        float wyz = Weights[yWeightBase + y] * wz;
                        int rowOffset = zOffset + y * Grid.x;
                        for (int x = 0; x < Grid.x; x++)
                        {
                            float weight = Weights[weightBase + x] * wyz;
                            value += ControlPoints[rowOffset + x] * weight;
                        }
                    }
                }

                Result[index] = value;
            }
        }

        [BurstCompile]
        [ExcludeFromCodeCoverage]
        private struct BuildBernsteinWeightsJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<LatticeCacheEntry> Entries;

            public int3 Grid;

            // Each job index owns one disjoint, fixed-stride segment containing
            // that vertex's X/Y/Z basis weights.
            [NativeDisableParallelForRestriction]
            public NativeArray<float> Weights;

            public void Execute(int index)
            {
                int stride = Grid.x + Grid.y + Grid.z;
                int weightBase = index * stride;
                float3 coordinate = math.saturate(Entries[index].NormalizedCoordinate);

                BuildAxisWeights(weightBase, Grid.x, coordinate.x);
                BuildAxisWeights(weightBase + Grid.x, Grid.y, coordinate.y);
                BuildAxisWeights(weightBase + Grid.x + Grid.y, Grid.z, coordinate.z);
            }

            private void BuildAxisWeights(int offset, int count, float coordinate)
            {
                Weights[offset] = 1f;
                for (int degree = 1; degree < count; degree++)
                {
                    Weights[offset + degree] = 0f;
                    for (int basis = degree; basis > 0; basis--)
                    {
                        Weights[offset + basis] =
                            Weights[offset + basis - 1] * coordinate +
                            Weights[offset + basis] * (1f - coordinate);
                    }

                    Weights[offset] *= 1f - coordinate;
                }
            }
        }

        [BurstCompile]
        [ExcludeFromCodeCoverage]
        private struct BuildCacheEntriesJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float3> RestVertices;

            public int3 Grid;
            public float3 BoundsMin;
            public float3 BoundsSize;

            [WriteOnly]
            public NativeArray<LatticeCacheEntry> Entries;

            public void Execute(int index)
            {
                float3 local = RestVertices[index];

                const float epsilon = 1e-6f;
                float3 invSize = new float3(
                    math.abs(BoundsSize.x) > epsilon ? 1f / BoundsSize.x : 0f,
                    math.abs(BoundsSize.y) > epsilon ? 1f / BoundsSize.y : 0f,
                    math.abs(BoundsSize.z) > epsilon ? 1f / BoundsSize.z : 0f);

                float3 barycentric = math.saturate((local - BoundsMin) * invSize);

                Entries[index] = BuildEntry(Grid, barycentric);
            }

            private static LatticeCacheEntry BuildEntry(int3 grid, float3 barycentric)
            {
                int3 clampedGrid = new int3(math.max(2, grid.x), math.max(2, grid.y), math.max(2, grid.z));

                float3 maxIndex = new float3(clampedGrid.x - 1, clampedGrid.y - 1, clampedGrid.z - 1);
                float3 scaled = math.clamp(barycentric * maxIndex, 0f, maxIndex);

                int ix = math.min((int)math.floor(scaled.x), clampedGrid.x - 2);
                int iy = math.min((int)math.floor(scaled.y), clampedGrid.y - 2);
                int iz = math.min((int)math.floor(scaled.z), clampedGrid.z - 2);

                float tx = math.saturate(scaled.x - ix);
                float ty = math.saturate(scaled.y - iy);
                float tz = math.saturate(scaled.z - iz);

                int nx = clampedGrid.x;
                int ny = clampedGrid.y;

                int Index(int x, int y, int z) => x + y * nx + z * nx * ny;

                int c000 = Index(ix, iy, iz);
                int c100 = Index(ix + 1, iy, iz);
                int c010 = Index(ix, iy + 1, iz);
                int c110 = Index(ix + 1, iy + 1, iz);
                int c001 = Index(ix, iy, iz + 1);
                int c101 = Index(ix + 1, iy, iz + 1);
                int c011 = Index(ix, iy + 1, iz + 1);
                int c111 = Index(ix + 1, iy + 1, iz + 1);

                float tx1 = 1f - tx;
                float ty1 = 1f - ty;
                float tz1 = 1f - tz;

                float w000 = tx1 * ty1 * tz1;
                float w100 = tx * ty1 * tz1;
                float w010 = tx1 * ty * tz1;
                float w110 = tx * ty * tz1;
                float w001 = tx1 * ty1 * tz;
                float w101 = tx * ty1 * tz;
                float w011 = tx1 * ty * tz;
                float w111 = tx * ty * tz;

                return new LatticeCacheEntry
                {
                    Corner0 = c000,
                    Corner1 = c100,
                    Corner2 = c010,
                    Corner3 = c110,
                    Corner4 = c001,
                    Corner5 = c101,
                    Corner6 = c011,
                    Corner7 = c111,
                    Weights0 = new float4(w000, w100, w010, w110),
                    Weights1 = new float4(w001, w101, w011, w111),
                    Barycentric = new float3(tx, ty, tz),
                    NormalizedCoordinate = barycentric
                };
            }
        }
    }
}
