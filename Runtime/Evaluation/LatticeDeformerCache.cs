using System;
using Unity.Mathematics;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    [Serializable]
    internal sealed class LatticeDeformerCache
    {
        [SerializeField] private Vector3Int _gridSize;
        [SerializeField] private Bounds _localBounds;
        [SerializeField] private LatticeInterpolationMode _interpolation;
        [SerializeField] private int _vertexCount;
        [SerializeField] private int _restVerticesHash;
        [SerializeField] private LatticeCacheEntry[] _entries = Array.Empty<LatticeCacheEntry>();
        [SerializeField] private Vector3[] _restVertices = Array.Empty<Vector3>();
        [SerializeField] private float[] _bernsteinWeights = Array.Empty<float>();

        public LatticeCacheEntry[] Entries => _entries;
        public Vector3Int GridSize => _gridSize;
        public LatticeInterpolationMode Interpolation => _interpolation;
        public float[] BernsteinWeights => _bernsteinWeights;

        public bool IsCompatibleWith(LatticeAsset asset, Mesh mesh, int restVerticesHash)
        {
            return IsCompatibleWith(
                asset,
                mesh,
                restVerticesHash,
                asset?.Interpolation ?? LatticeInterpolationMode.Trilinear);
        }

        public bool IsCompatibleWith(
            LatticeAsset asset,
            Mesh mesh,
            int restVerticesHash,
            LatticeInterpolationMode effectiveInterpolation)
        {
            if (asset == null || mesh == null)
            {
                return false;
            }

            return IsCompatibleWith(
                asset,
                mesh.vertexCount,
                restVerticesHash,
                effectiveInterpolation);
        }

        public bool IsCompatibleWith(
            LatticeAsset asset,
            int vertexCount,
            int restVerticesHash,
            LatticeInterpolationMode effectiveInterpolation)
        {
            if (asset == null || vertexCount < 0)
            {
                return false;
            }

            if (_entries == null || _entries.Length == 0)
            {
                return false;
            }

            if (_vertexCount != vertexCount)
            {
                return false;
            }

            if (_restVerticesHash != restVerticesHash)
            {
                return false;
            }

            if (_gridSize != asset.GridSize)
            {
                return false;
            }

            if (_interpolation != effectiveInterpolation)
            {
                return false;
            }

            if (_interpolation == LatticeInterpolationMode.CubicBernstein &&
                !HasValidBernsteinWeights(vertexCount))
            {
                return false;
            }

            if (!ApproximatelyEquals(_localBounds, asset.LocalBounds))
            {
                return false;
            }

            return true;
        }

        public void Populate(
            Vector3Int gridSize,
            Bounds bounds,
            LatticeInterpolationMode interpolation,
            int vertexCount,
            int restVerticesHash,
            LatticeCacheEntry[] entries,
            Vector3[] restVertices,
            float[] bernsteinWeights = null)
        {
            _gridSize = gridSize;
            _localBounds = bounds;
            _interpolation = interpolation;
            _vertexCount = vertexCount;
            _restVerticesHash = restVerticesHash;
            _entries = entries ?? Array.Empty<LatticeCacheEntry>();
            _restVertices = restVertices ?? Array.Empty<Vector3>();
            _bernsteinWeights = bernsteinWeights ?? Array.Empty<float>();
        }

        public bool HasValidBernsteinWeights(int vertexCount)
        {
            if (_bernsteinWeights == null || vertexCount < 0)
            {
                return false;
            }

            long stride = (long)_gridSize.x + _gridSize.y + _gridSize.z;
            return stride > 0 && _bernsteinWeights.LongLength == stride * vertexCount;
        }

        public void Clear()
        {
            _entries = Array.Empty<LatticeCacheEntry>();
            _restVertices = Array.Empty<Vector3>();
            _bernsteinWeights = Array.Empty<float>();
            _vertexCount = 0;
            _restVerticesHash = 0;
        }

        private static bool ApproximatelyEquals(Bounds lhs, Bounds rhs)
        {
            const float epsilon = 1e-5f;
            return (lhs.center - rhs.center).sqrMagnitude <= epsilon * epsilon &&
                   (lhs.size - rhs.size).sqrMagnitude <= epsilon * epsilon;
        }
    }
}
