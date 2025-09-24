using System;
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


        [SerializeField]
        private bool _useJobsAndBurst = false;

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

        public bool UseJobsAndBurst
        {
            get => _useJobsAndBurst;
            set => _useJobsAndBurst = value;
        }

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

            if (!hasExistingPoints)
            {
                EnsureControlPointCapacity();
                return;
            }

            var oldPointsCopy = new Vector3[oldPoints.Length];
            System.Array.Copy(oldPoints, oldPointsCopy, oldPoints.Length);

            int newCount = ControlPointCount;
            if (_controlPointsLocal == null || _controlPointsLocal.Length != newCount)
            {
                _controlPointsLocal = new Vector3[newCount];
            }

            // TODO(CUST-123): Burstify control-point resampling to avoid large single-thread stalls.
            for (int z = 0; z < newGridSize.z; z++)
            {
                float w = newGridSize.z > 1 ? (float)z / (newGridSize.z - 1) : 0f;
                for (int y = 0; y < newGridSize.y; y++)
                {
                    float v = newGridSize.y > 1 ? (float)y / (newGridSize.y - 1) : 0f;
                    for (int x = 0; x < newGridSize.x; x++)
                    {
                        float u = newGridSize.x > 1 ? (float)x / (newGridSize.x - 1) : 0f;
                        int newIndex = Index(newGridSize, x, y, z);
                        _controlPointsLocal[newIndex] = SampleControlPoints(oldPointsCopy, oldGrid, u, v, w);
                    }
                }
            }
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

        public void EnsureInitialized()
        {
            GridSize = _gridSize;
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

        private void EnsureControlPointCapacity()
        {
            int expected = ControlPointCount;
            if (expected <= 0)
            {
                _controlPointsLocal = System.Array.Empty<Vector3>();
                return;
            }

            bool sizeChanged = _controlPointsLocal == null || _controlPointsLocal.Length != expected;
            if (sizeChanged)
            {
                System.Array.Resize(ref _controlPointsLocal, expected);
            }

            if (_controlPointsLocal == null)
            {
                return;
            }

            if (sizeChanged)
            {
                PopulateControlPoints();
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

            var min = _localBounds.min;
            var size = _localBounds.size;
            int nx = _gridSize.x;
            int ny = _gridSize.y;
            int nz = _gridSize.z;

            int index = 0;
            for (int z = 0; z < nz; z++)
            {
                float wz = nz > 1 ? (float)z / (nz - 1) : 0f;
                for (int y = 0; y < ny; y++)
                {
                    float wy = ny > 1 ? (float)y / (ny - 1) : 0f;
                    for (int x = 0; x < nx; x++)
                    {
                        float wx = nx > 1 ? (float)x / (nx - 1) : 0f;
                        _controlPointsLocal[index++] = min + Vector3.Scale(size, new Vector3(wx, wy, wz));
                    }
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
