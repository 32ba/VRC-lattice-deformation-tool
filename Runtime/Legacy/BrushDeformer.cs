using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    public enum BrushFalloffType
    {
        Smooth = 0,
        Linear = 1,
        Constant = 2
    }

    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class BrushDeformer : MonoBehaviour
    {
        public static bool SuppressRestoreOnDisable { get; set; } = false;

        [SerializeField] private SkinnedMeshRenderer _skinnedMeshRenderer;
        [SerializeField] private MeshFilter _meshFilter;
        [SerializeField] private bool _recalculateNormals = true;
        [SerializeField] private bool _recalculateTangents = false;
        [SerializeField] private bool _recalculateBounds = true;
        [SerializeField] private bool _recalculateBoneWeights = false;
        [SerializeField] private WeightTransferSettingsData _weightTransferSettings = new WeightTransferSettingsData();
        [SerializeField, HideInInspector] private Vector3[] _displacements = Array.Empty<Vector3>();
        [SerializeField, HideInInspector] private Mesh _serializedSourceMesh;

        [NonSerialized] private Mesh _runtimeMesh;
        [NonSerialized] private Mesh _sourceMesh;

        public Mesh RuntimeMesh => _runtimeMesh;
        public Mesh SourceMesh => _sourceMesh;

        public bool RecalculateBoneWeights
        {
            get => _recalculateBoneWeights;
            set => _recalculateBoneWeights = value;
        }

        public WeightTransferSettingsData WeightTransferSettings
        {
            get
            {
                if (_weightTransferSettings == null)
                    _weightTransferSettings = new WeightTransferSettingsData();
                return _weightTransferSettings;
            }
            set => _weightTransferSettings = value ?? new WeightTransferSettingsData();
        }

        public Vector3[] Displacements => _displacements;

        public int DisplacementCount => _displacements?.Length ?? 0;

        public Transform MeshTransform
        {
            get
            {
                if (_skinnedMeshRenderer != null) return _skinnedMeshRenderer.transform;
                if (_meshFilter != null) return _meshFilter.transform;
                return transform;
            }
        }

        public bool HasDisplacements()
        {
            if (_displacements == null || _displacements.Length == 0)
                return false;

            for (int i = 0; i < _displacements.Length; i++)
            {
                if (_displacements[i].sqrMagnitude > 1e-12f)
                    return true;
            }
            return false;
        }

        public void EnsureDisplacementCapacity()
        {
            CacheSourceMesh();
            if (_sourceMesh == null) return;

            int vertexCount = _sourceMesh.vertexCount;
            if (_displacements == null || _displacements.Length != vertexCount)
            {
                var old = _displacements;
                _displacements = new Vector3[vertexCount];
                if (old != null)
                {
                    int copyCount = Math.Min(old.Length, vertexCount);
                    Array.Copy(old, _displacements, copyCount);
                }
            }
        }

        public void SetDisplacement(int index, Vector3 displacement)
        {
            if (_displacements == null || index < 0 || index >= _displacements.Length) return;
            _displacements[index] = displacement;
        }

        public void AddDisplacement(int index, Vector3 delta)
        {
            if (_displacements == null || index < 0 || index >= _displacements.Length) return;
            _displacements[index] += delta;
        }

        public Vector3 GetDisplacement(int index)
        {
            if (_displacements == null || index < 0 || index >= _displacements.Length) return Vector3.zero;
            return _displacements[index];
        }

        public void ClearDisplacements()
        {
            if (_displacements != null)
                Array.Clear(_displacements, 0, _displacements.Length);
        }

        public void Reset()
        {
            if (_skinnedMeshRenderer == null)
                _skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>();
            if (_meshFilter == null)
                _meshFilter = GetComponent<MeshFilter>();

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                CacheSourceMesh();
                EnsureDisplacementCapacity();
            }
#endif
        }

        private void OnEnable()
        {
            CacheSourceMesh();
            EnsureDisplacementCapacity();
        }

        private void OnDisable()
        {
            if (Application.isPlaying) return;
            if (SuppressRestoreOnDisable)
            {
                ReleaseRuntimeMesh();
                return;
            }
            RestoreOriginalMesh();
        }

        public Mesh Deform(bool assignToRenderer = true)
        {
            CacheSourceMesh();
            if (_sourceMesh == null) return null;

            EnsureDisplacementCapacity();
            if (_displacements == null || _displacements.Length == 0) return null;

            var mesh = AcquireRuntimeMesh(assignToRenderer);
            if (mesh == null) return null;

            var srcVertices = _sourceMesh.vertices;
            var vertices = ApplyDisplacementsWithJobs(srcVertices, _displacements);

            mesh.vertices = vertices;

            if (_recalculateNormals) mesh.RecalculateNormals();
            if (_recalculateTangents) mesh.RecalculateTangents();
            if (_recalculateBounds) mesh.RecalculateBounds();
            mesh.UploadMeshData(false);

            if (assignToRenderer) AssignRuntimeMesh(mesh);
            return mesh;
        }

        public void RestoreOriginalMesh()
        {
            if (_skinnedMeshRenderer != null && _sourceMesh != null)
                _skinnedMeshRenderer.sharedMesh = _sourceMesh;
            if (_meshFilter != null && _sourceMesh != null)
                _meshFilter.sharedMesh = _sourceMesh;
            ReleaseRuntimeMesh();
        }

        public void CacheSourceMesh()
        {
            Mesh nextSource = GetSharedSourceMesh();
            if (_runtimeMesh != null && ReferenceEquals(_runtimeMesh, nextSource)) return;

            _sourceMesh = nextSource;
            if (!ReferenceEquals(_serializedSourceMesh, nextSource))
            {
                _serializedSourceMesh = nextSource;
            }
        }

        private Mesh GetSharedSourceMesh()
        {
            if (_skinnedMeshRenderer != null) return _skinnedMeshRenderer.sharedMesh;
            if (_meshFilter != null) return _meshFilter.sharedMesh;
            return null;
        }

        private Mesh AcquireRuntimeMesh(bool assignToRenderer)
        {
            if (_runtimeMesh == null)
            {
                if (_sourceMesh == null) return null;
                _runtimeMesh = Instantiate(_sourceMesh);
                _runtimeMesh.name = _sourceMesh.name + " (Brush)";
                _runtimeMesh.hideFlags = HideFlags.HideAndDontSave;
            }

            if (assignToRenderer) AssignRuntimeMesh(_runtimeMesh);
            return _runtimeMesh;
        }

        private void AssignRuntimeMesh(Mesh mesh)
        {
            if (_skinnedMeshRenderer != null) _skinnedMeshRenderer.sharedMesh = mesh;
            if (_meshFilter != null) _meshFilter.sharedMesh = mesh;
        }

        private void ReleaseRuntimeMesh()
        {
            if (_runtimeMesh == null) return;
            if (Application.isPlaying) Destroy(_runtimeMesh);
            else DestroyImmediate(_runtimeMesh);
            _runtimeMesh = null;
        }

        private static Vector3[] ApplyDisplacementsWithJobs(Vector3[] srcVertices, Vector3[] displacements)
        {
            int count = srcVertices.Length;
            using var srcNative = LatticeNativeArrayUtility.CreateCopy(srcVertices, Allocator.TempJob);
            using var dispNative = LatticeNativeArrayUtility.CreateCopy(displacements, Allocator.TempJob);
            using var outNative = LatticeNativeArrayUtility.CreateFloat3Array(count, Allocator.TempJob);

            new ApplyDisplacementsJob
            {
                Source = srcNative,
                Displacements = dispNative,
                Result = outNative
            }.Schedule(count, 64).Complete();

            var result = new Vector3[count];
            outNative.CopyToManaged(result);
            return result;
        }

        [BurstCompile]
        private struct ApplyDisplacementsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> Source;
            [ReadOnly] public NativeArray<float3> Displacements;
            [WriteOnly] public NativeArray<float3> Result;

            public void Execute(int index)
            {
                Result[index] = Source[index] + Displacements[index];
            }
        }

        public static float EvaluateFalloff(BrushFalloffType type, float t)
        {
            t = math.saturate(t);
            switch (type)
            {
                case BrushFalloffType.Linear:
                    return 1f - t;
                case BrushFalloffType.Smooth:
                    float s = 1f - t;
                    return s * s * (3f - 2f * s);
                case BrushFalloffType.Constant:
                    return 1f;
                default:
                    return 1f - t;
            }
        }
    }
}
