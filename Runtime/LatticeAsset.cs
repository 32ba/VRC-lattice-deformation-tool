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
            set
            {
                value.x = Mathf.Max(k_MinAxisResolution, value.x);
                value.y = Mathf.Max(k_MinAxisResolution, value.y);
                value.z = Mathf.Max(k_MinAxisResolution, value.z);
                if (_gridSize == value)
                {
                    return;
                }

                _gridSize = value;
                EnsureControlPointCapacity();
            }
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
