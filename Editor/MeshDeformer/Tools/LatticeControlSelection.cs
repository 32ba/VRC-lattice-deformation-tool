#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    // Session-local indices only. Does not own a target or cause repaint/Undo.
    internal sealed class LatticeControlSelection
    {
        private readonly HashSet<int> _indices = new HashSet<int>();
        internal int Count => _indices.Count;
        internal bool Contains(int index) => _indices.Contains(index);
        internal void Clear() => _indices.Clear();
        public HashSet<int>.Enumerator GetEnumerator() => _indices.GetEnumerator();

        internal void Select(int index, bool additive)
        {
            if (additive)
            {
                if (!_indices.Add(index)) _indices.Remove(index);
            }
            else
            {
                _indices.Clear();
                _indices.Add(index);
            }
        }

        internal void TrimToCount(int count) => _indices.RemoveWhere(index => index < 0 || index >= count);

        internal static bool IsBoundaryIndex(int ix, int iy, int iz, int nx, int ny, int nz) =>
            ix == 0 || ix == nx - 1 || iy == 0 || iy == ny - 1 || iz == 0 || iz == nz - 1;

        internal void KeepBoundary(Vector3Int gridSize)
        {
            if (_indices.Count == 0) return;
            int nx = Mathf.Max(1, gridSize.x);
            int ny = Mathf.Max(1, gridSize.y);
            int nz = Mathf.Max(1, gridSize.z);
            _indices.RemoveWhere(index =>
            {
                int ix = index % nx;
                int iy = (index / nx) % ny;
                int iz = index / (nx * ny);
                return !IsBoundaryIndex(ix, iy, iz, nx, ny, nz);
            });
        }
    }
}
#endif
