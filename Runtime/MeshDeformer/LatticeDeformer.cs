using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    public enum MeshDeformerLayerType
    {
        Lattice = 0,
        Brush = 1
    }

    public enum BlendShapeOutputMode
    {
        Disabled = 0,
        OutputAsBlendShape = 1
    }

    [Serializable]
    public sealed class LatticeLayer
    {
        [SerializeField] private string _name = "Layer";
        [SerializeField] private bool _enabled = true;
        [SerializeField] private float _weight = 1f;
        [SerializeField] private MeshDeformerLayerType _type = MeshDeformerLayerType.Lattice;
        [SerializeField] private LatticeAsset _settings = new LatticeAsset();
        [SerializeField, HideInInspector] private Vector3[] _brushDisplacements = Array.Empty<Vector3>();
        [SerializeField] private BlendShapeOutputMode _blendShapeOutput = BlendShapeOutputMode.Disabled;
        [SerializeField] private string _blendShapeName = "";

        public string Name
        {
            get => string.IsNullOrWhiteSpace(_name) ? "Layer" : _name;
            set => _name = string.IsNullOrWhiteSpace(value) ? "Layer" : value;
        }

        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public float Weight
        {
            get => _weight;
            set => _weight = Mathf.Clamp01(value);
        }

        public MeshDeformerLayerType Type
        {
            get => _type;
        }

        internal void SetType(MeshDeformerLayerType type) => _type = type;

        public LatticeAsset Settings
        {
            get
            {
                if (_settings == null)
                {
                    _settings = new LatticeAsset();
                }

                _settings.EnsureInitialized();
                return _settings;
            }
            set => _settings = value ?? new LatticeAsset();
        }

        public BlendShapeOutputMode BlendShapeOutput
        {
            get => _blendShapeOutput;
            set => _blendShapeOutput = value;
        }

        public string BlendShapeName
        {
            get => _blendShapeName;
            set => _blendShapeName = value ?? "";
        }

        public string EffectiveBlendShapeName => string.IsNullOrWhiteSpace(_blendShapeName) ? Name : _blendShapeName;

        public Vector3[] BrushDisplacements
        {
            get => _brushDisplacements ?? (_brushDisplacements = Array.Empty<Vector3>());
            set => _brushDisplacements = value ?? Array.Empty<Vector3>();
        }

        public int BrushDisplacementCount => _brushDisplacements?.Length ?? 0;

        public void EnsureBrushDisplacementCapacity(int vertexCount)
        {
            vertexCount = Mathf.Max(0, vertexCount);
            if (_brushDisplacements == null || _brushDisplacements.Length != vertexCount)
            {
                var previous = _brushDisplacements;
                _brushDisplacements = new Vector3[vertexCount];
                if (previous != null)
                {
                    Array.Copy(previous, _brushDisplacements, Mathf.Min(previous.Length, vertexCount));
                }
            }
        }

        public bool HasBrushDisplacements()
        {
            if (_brushDisplacements == null || _brushDisplacements.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < _brushDisplacements.Length; i++)
            {
                if (_brushDisplacements[i].sqrMagnitude > 1e-12f)
                {
                    return true;
                }
            }

            return false;
        }

        public void ClearBrushDisplacements()
        {
            if (_brushDisplacements == null)
            {
                return;
            }

            Array.Clear(_brushDisplacements, 0, _brushDisplacements.Length);
        }

        public Vector3 GetBrushDisplacement(int index)
        {
            if (_brushDisplacements == null || index < 0 || index >= _brushDisplacements.Length)
            {
                return Vector3.zero;
            }

            return _brushDisplacements[index];
        }

        public void SetBrushDisplacement(int index, Vector3 displacement)
        {
            if (_brushDisplacements == null || index < 0 || index >= _brushDisplacements.Length)
            {
                return;
            }

            _brushDisplacements[index] = displacement;
        }

        public void AddBrushDisplacement(int index, Vector3 delta)
        {
            if (_brushDisplacements == null || index < 0 || index >= _brushDisplacements.Length)
            {
                return;
            }

            _brushDisplacements[index] += delta;
        }
    }

    [DisallowMultipleComponent]
    [ExecuteAlways]
    [AddComponentMenu("32ba/Mesh Deformer")]
    public class LatticeDeformer : MonoBehaviour
    {
        public static bool SuppressRestoreOnDisable { get; set; } = false;

        public enum LatticeAlignMode
        {
            Mode1_TransformOnly = 0,
            Mode2_TransformPlusCenter = 1,
            Mode3_BoundsRemap = 2
        }

        [SerializeField] private LatticeAsset _settings = new LatticeAsset();
        [SerializeField] private List<LatticeLayer> _layers = new List<LatticeLayer>();
        [SerializeField, HideInInspector] private int _activeLayerIndex = 0;
        [SerializeField, HideInInspector] private int _layerModelVersion = 0;
        [SerializeField] private SkinnedMeshRenderer _skinnedMeshRenderer;
        [SerializeField] private MeshFilter _meshFilter;
        [SerializeField] private bool _recalculateNormals = true;
        [SerializeField] private bool _recalculateTangents = false;
        [SerializeField] private bool _recalculateBounds = true;
        [SerializeField] private bool _recalculateBoneWeights = false;
        [SerializeField] private WeightTransferSettingsData _weightTransferSettings = new WeightTransferSettingsData();
        [SerializeField, HideInInspector] private bool _hasInitializedFromSource = false;
        [SerializeField, HideInInspector] private Mesh _serializedSourceMesh;

        // Preview alignment (per-instance)
        [SerializeField, HideInInspector] private LatticeAlignMode _alignMode = LatticeAlignMode.Mode1_TransformOnly;
        [SerializeField, HideInInspector] private float _centerClampMulXY = 0f;
        [SerializeField, HideInInspector] private float _centerClampMinXY = 0f;
        [SerializeField, HideInInspector] private float _centerClampMulZ = 0f;
        [SerializeField, HideInInspector] private float _centerClampMinZ = 0f;
        [SerializeField, HideInInspector] private bool _allowCenterOffsetWhenBoundsSkipped = false;
        [SerializeField, HideInInspector] private bool _alignAutoInitialized = false;
        [SerializeField, HideInInspector] private Vector3 _manualOffsetProxy = Vector3.zero;
        [SerializeField, HideInInspector] private Vector3 _manualScaleProxy = Vector3.one;
        [NonSerialized] private LatticeDeformerCache _cache = new LatticeDeformerCache();
        [NonSerialized] private Mesh _runtimeMesh;
        [NonSerialized] private Mesh _sourceMesh;
        private const int k_CurrentLayerModelVersion = 2;
        private const string k_PrimaryLayerName = "Lattice Layer";
        private const string k_BrushLayerName = "Brush Layer";

        private Vector3[] _controlBuffer = Array.Empty<Vector3>();

        /// <summary>
        /// Base layer settings (legacy serialized field). Kept for backward compatibility.
        /// </summary>
        public LatticeAsset Settings
        {
            get
            {
                return GetPrimaryLayerSettings();
            }
            set
            {
                EnsureLayers();
                var resolved = value ?? new LatticeAsset();
                resolved.EnsureInitialized();

                if (_layers.Count == 0)
                {
                    _layers.Add(new LatticeLayer
                    {
                        Name = k_PrimaryLayerName,
                        Enabled = true,
                        Weight = 1f,
                        Settings = resolved
                    });
                }
                else
                {
                    if (_layers[0] == null)
                    {
                        _layers[0] = new LatticeLayer();
                    }

                    _layers[0].Name = k_PrimaryLayerName;
                    _layers[0].Enabled = true;
                    _layers[0].Settings = resolved;
                }

                _settings = CloneSettings(resolved);
                _hasInitializedFromSource = false;
                InvalidateCache();
            }
        }

        public IReadOnlyList<LatticeLayer> Layers
        {
            get
            {
                EnsureLayers();
                return _layers;
            }
        }

        /// <summary>
        /// 0..N-1 = Active layer in _layers.
        /// </summary>
        public int ActiveLayerIndex
        {
            get
            {
                EnsureLayers();
                return _activeLayerIndex;
            }
            set
            {
                EnsureLayers();
                int maxIndex = _layers.Count - 1;
                _activeLayerIndex = Mathf.Clamp(value, 0, maxIndex);
            }
        }

        public bool IsEditingBaseLayer => false;

        public LatticeAsset EditingSettings
        {
            get
            {
                EnsureLayers();
                return GetSettingsForLayerIndex(_activeLayerIndex) ?? GetPrimaryLayerSettings();
            }
        }

        public MeshDeformerLayerType ActiveLayerType
        {
            get
            {
                EnsureLayers();
                if (!TryGetLayer(_activeLayerIndex, out var layer))
                {
                    return MeshDeformerLayerType.Lattice;
                }

                return layer.Type;
            }
        }

        public Mesh RuntimeMesh => _runtimeMesh;

        public Mesh SourceMesh => _sourceMesh;

        // Bone weight recalculation settings
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
                {
                    _weightTransferSettings = new WeightTransferSettingsData();
                }
                return _weightTransferSettings;
            }
            set => _weightTransferSettings = value ?? new WeightTransferSettingsData();
        }

        // Brush-layer compatibility surface for BrushDeformerTool.
        public Vector3[] Displacements
        {
            get
            {
                EnsureLayers();
                if (!TryGetLayer(_activeLayerIndex, out var layer) || layer.Type != MeshDeformerLayerType.Brush)
                {
                    return Array.Empty<Vector3>();
                }

                return layer.BrushDisplacements;
            }
        }

        public int DisplacementCount => Displacements.Length;

        public bool HasDisplacements()
        {
            EnsureLayers();
            if (!TryGetLayer(_activeLayerIndex, out var layer) || layer.Type != MeshDeformerLayerType.Brush)
            {
                return false;
            }

            return layer.HasBrushDisplacements();
        }

        public void EnsureDisplacementCapacity()
        {
            EnsureLayers();
            CacheSourceMesh();
            if (_sourceMesh == null)
            {
                return;
            }

            if (TryGetLayer(_activeLayerIndex, out var layer) && layer.Type == MeshDeformerLayerType.Brush)
            {
                layer.EnsureBrushDisplacementCapacity(_sourceMesh.vertexCount);
            }
        }

        public void SetDisplacement(int index, Vector3 displacement)
        {
            EnsureLayers();
            if (TryGetLayer(_activeLayerIndex, out var layer) && layer.Type == MeshDeformerLayerType.Brush)
            {
                layer.SetBrushDisplacement(index, displacement);
            }
        }

        public void AddDisplacement(int index, Vector3 delta)
        {
            EnsureLayers();
            if (TryGetLayer(_activeLayerIndex, out var layer) && layer.Type == MeshDeformerLayerType.Brush)
            {
                layer.AddBrushDisplacement(index, delta);
            }
        }

        public Vector3 GetDisplacement(int index)
        {
            EnsureLayers();
            if (TryGetLayer(_activeLayerIndex, out var layer) && layer.Type == MeshDeformerLayerType.Brush)
            {
                return layer.GetBrushDisplacement(index);
            }

            return Vector3.zero;
        }

        public void ClearDisplacements()
        {
            EnsureLayers();
            if (TryGetLayer(_activeLayerIndex, out var layer) && layer.Type == MeshDeformerLayerType.Brush)
            {
                layer.ClearBrushDisplacements();
            }
        }

        public int AddLayer(string layerName = null, MeshDeformerLayerType layerType = MeshDeformerLayerType.Lattice)
        {
            EnsureLayers();
            var source = EditingSettings ?? GetPrimaryLayerSettings();

            var layer = new LatticeLayer
            {
                Name = string.IsNullOrWhiteSpace(layerName) ? GenerateNextLayerName(layerType) : layerName,
                Enabled = true,
                Weight = 1f,
                Settings = CreateNeutralLayerSettings(source)
            };
            layer.SetType(layerType);

            _layers.Add(layer);
            _activeLayerIndex = _layers.Count - 1;
            if (layerType == MeshDeformerLayerType.Brush)
            {
                EnsureDisplacementCapacity();
            }

            return _activeLayerIndex;
        }

        public int DuplicateLayer(int index)
        {
            EnsureLayers();
            if (!TryGetLayer(index, out var sourceLayer))
            {
                return -1;
            }

            var duplicate = new LatticeLayer
            {
                Name = sourceLayer.Name + " Copy",
                Enabled = sourceLayer.Enabled,
                Weight = sourceLayer.Weight,
                Settings = CloneSettings(sourceLayer.Settings)
            };
            duplicate.SetType(sourceLayer.Type);
            duplicate.BrushDisplacements = (Vector3[])sourceLayer.BrushDisplacements.Clone();

            int insertAt = Mathf.Clamp(index + 1, 0, _layers.Count);
            _layers.Insert(insertAt, duplicate);
            _activeLayerIndex = insertAt;

            return _activeLayerIndex;
        }

        public bool RemoveLayer(int index)
        {
            EnsureLayers();
            if (index < 0 || index >= _layers.Count || _layers.Count <= 1)
            {
                return false;
            }

            _layers.RemoveAt(index);

            if (_layers.Count == 0)
            {
                _activeLayerIndex = 0;
                return true;
            }

            if (_activeLayerIndex == index)
            {
                _activeLayerIndex = Mathf.Min(index, _layers.Count - 1);
            }
            else if (_activeLayerIndex > index)
            {
                _activeLayerIndex--;
            }

            return true;
        }

        public bool MoveLayer(int index, int targetIndex)
        {
            EnsureLayers();
            if (index < 0 || index >= _layers.Count)
            {
                return false;
            }

            targetIndex = Mathf.Clamp(targetIndex, 0, _layers.Count - 1);
            if (targetIndex == index)
            {
                return true;
            }

            var layer = _layers[index];
            _layers.RemoveAt(index);
            _layers.Insert(targetIndex, layer);

            if (_activeLayerIndex == index)
            {
                _activeLayerIndex = targetIndex;
            }
            else if (index < _activeLayerIndex && targetIndex >= _activeLayerIndex)
            {
                _activeLayerIndex--;
            }
            else if (index > _activeLayerIndex && targetIndex <= _activeLayerIndex)
            {
                _activeLayerIndex++;
            }

            return true;
        }

        public int ImportBlendShapeAsLayer(int blendShapeIndex, int frameIndex = 0)
        {
            if (_sourceMesh == null)
            {
                return -1;
            }

            int shapeCount = _sourceMesh.blendShapeCount;
            if (blendShapeIndex < 0 || blendShapeIndex >= shapeCount)
            {
                return -1;
            }

            int frameCount = _sourceMesh.GetBlendShapeFrameCount(blendShapeIndex);
            if (frameIndex < 0 || frameIndex >= frameCount)
            {
                return -1;
            }

            int vertexCount = _sourceMesh.vertexCount;
            if (vertexCount == 0)
            {
                return -1;
            }

            var deltaVertices = new Vector3[vertexCount];
            var deltaNormals = new Vector3[vertexCount];
            var deltaTangents = new Vector3[vertexCount];
            _sourceMesh.GetBlendShapeFrameVertices(blendShapeIndex, frameIndex, deltaVertices, deltaNormals, deltaTangents);

            string shapeName = _sourceMesh.GetBlendShapeName(blendShapeIndex);

            var layer = new LatticeLayer();
            layer.Name = shapeName;
            layer.SetType(MeshDeformerLayerType.Brush);
            layer.Weight = 1f;

            layer.EnsureBrushDisplacementCapacity(vertexCount);
            for (int i = 0; i < vertexCount; i++)
            {
                layer.SetBrushDisplacement(i, deltaVertices[i]);
            }

            if (_layers == null)
            {
                _layers = new List<LatticeLayer>();
            }

            _layers.Add(layer);
            return _layers.Count - 1;
        }

        public string[] GetSourceBlendShapeNames()
        {
            if (_sourceMesh == null)
            {
                return Array.Empty<string>();
            }

            int count = _sourceMesh.blendShapeCount;
            if (count == 0)
            {
                return Array.Empty<string>();
            }

            var names = new string[count];
            for (int i = 0; i < count; i++)
            {
                names[i] = _sourceMesh.GetBlendShapeName(i);
            }

            return names;
        }

        public bool IsLayerStructurallyCompatible(int index)
        {
            EnsureLayers();
            return TryGetLayer(index, out _);
        }

        public void SyncLayerStructuresToBase(bool resetControlPoints)
        {
            // Base layer concept was removed in 1.3.0.
            // Kept as a no-op for backward compatibility.
        }

        public int ComputeLayeredStateHash()
        {
            EnsureLayers();

            int hash = 17;
            hash = HashCode.Combine(hash, _layers.Count);
            hash = HashCode.Combine(hash, _activeLayerIndex);

            foreach (var layer in _layers)
            {
                if (layer == null)
                {
                    hash = HashCode.Combine(hash, 0);
                    continue;
                }

                hash = HashCode.Combine(hash, layer.Enabled);
                hash = HashCode.Combine(hash, layer.Weight);
                hash = HashCode.Combine(hash, (int)layer.Type);
                switch (layer.Type)
                {
                    case MeshDeformerLayerType.Brush:
                        hash = HashCode.Combine(hash, HashDisplacementState(layer.BrushDisplacements));
                        break;
                    default:
                        hash = HashCode.Combine(hash, HashAssetState(layer.Settings));
                        break;
                }
            }

            return hash;
        }

        // Alignment settings accessors
        public LatticeAlignMode AlignMode
        {
            get => _alignMode;
            set => _alignMode = value;
        }

        public float CenterClampMulXY
        {
            get => _centerClampMulXY;
            set => _centerClampMulXY = Mathf.Max(0f, value);
        }

        public float CenterClampMinXY
        {
            get => _centerClampMinXY;
            set => _centerClampMinXY = Mathf.Max(0f, value);
        }

        public float CenterClampMulZ
        {
            get => _centerClampMulZ;
            set => _centerClampMulZ = Mathf.Max(0f, value);
        }

        public float CenterClampMinZ
        {
            get => _centerClampMinZ;
            set => _centerClampMinZ = Mathf.Max(0f, value);
        }

        public bool AllowCenterOffsetWhenBoundsSkipped
        {
            get => _allowCenterOffsetWhenBoundsSkipped;
            set => _allowCenterOffsetWhenBoundsSkipped = value;
        }

        public bool AlignAutoInitialized
        {
            get => _alignAutoInitialized;
            set => _alignAutoInitialized = value;
        }

        public Vector3 ManualOffsetProxy
        {
            get => _manualOffsetProxy;
            set => _manualOffsetProxy = value;
        }

        public Vector3 ManualScaleProxy
        {
            get => _manualScaleProxy;
            set
            {
                _manualScaleProxy.x = Mathf.Max(0.0001f, value.x);
                _manualScaleProxy.y = Mathf.Max(0.0001f, value.y);
                _manualScaleProxy.z = Mathf.Max(0.0001f, value.z);
            }
        }

        public Transform MeshTransform
        {
            get
            {
                if (_skinnedMeshRenderer != null)
                {
                    return _skinnedMeshRenderer.transform;
                }

                if (_meshFilter != null)
                {
                    return _meshFilter.transform;
                }

                return transform;
            }
        }

        public void Reset()
        {
            if (_skinnedMeshRenderer == null)
            {
                _skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>();
            }

            if (_meshFilter == null)
            {
                _meshFilter = GetComponent<MeshFilter>();
            }

            EnsureSettings();

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                CacheSourceMesh();
                if (_sourceMesh != null)
                {
                    InitializeFromSource(true);
                }
            }
#endif
        }
        private void OnEnable()
        {
            EnsureLayerModelReady();
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
            {
                return;
            }

            if (SuppressRestoreOnDisable)
            {
                ReleaseRuntimeMesh();
                return;
            }

            RestoreOriginalMesh();
        }

        public Mesh Deform(bool assignToRenderer = true)
        {
            EnsureLayerModelReady();

            if (_sourceMesh == null)
            {
                return null;
            }

            var mesh = AcquireRuntimeMesh(assignToRenderer);
            if (mesh == null)
            {
                return null;
            }

            var sourceVertices = _sourceMesh.vertices;
            var vertices = sourceVertices != null ? (Vector3[])sourceVertices.Clone() : Array.Empty<Vector3>();
            if (vertices.Length == 0)
            {
                return null;
            }

            EnsureBrushLayerDisplacementCapacity(vertices.Length);

            // --- Pass 1: Direct deformation layers (existing behavior) ---
            for (int i = 0; i < _layers.Count; i++)
            {
                var layer = _layers[i];
                if (layer == null || !layer.Enabled || layer.Weight <= 0f)
                {
                    continue;
                }

                if (layer.BlendShapeOutput != BlendShapeOutputMode.Disabled)
                {
                    continue;
                }

                switch (layer.Type)
                {
                    case MeshDeformerLayerType.Brush:
                        TryApplyBrushLayerContribution(layer, sourceVertices, vertices);
                        break;
                    default:
                        TryApplyLatticeLayerContribution(layer, sourceVertices, vertices);
                        break;
                }
            }

            mesh.vertices = vertices;

            if (_recalculateNormals)
            {
                mesh.RecalculateNormals();
            }

            if (_recalculateTangents)
            {
                mesh.RecalculateTangents();
            }

            if (_recalculateBounds)
            {
                mesh.RecalculateBounds();
            }

            // --- Pass 2: BlendShape output layers ---
            for (int i = 0; i < _layers.Count; i++)
            {
                var layer = _layers[i];
                if (layer == null || !layer.Enabled || layer.Weight <= 0f)
                {
                    continue;
                }

                if (layer.BlendShapeOutput != BlendShapeOutputMode.OutputAsBlendShape)
                {
                    continue;
                }

                TryAddBlendShapeFrame(mesh, layer, sourceVertices, vertices);
            }

            mesh.UploadMeshData(false);

            if (assignToRenderer)
            {
                AssignRuntimeMesh(mesh);
            }

            return mesh;
        }

        private void EnsureLayerModelReady()
        {
            EnsureSettings();
            if (_layers == null)
            {
                _layers = new List<LatticeLayer>();
            }

            TryMigrateLegacyBaseToLayerStructure();
            EnsureLayers();
            CacheSourceMesh();
            TryAutoConfigureSettings();
        }

        private static void TryApplyBrushLayerContribution(LatticeLayer layer, Vector3[] sourceVertices, Vector3[] deformedVertices)
        {
            if (layer == null || sourceVertices == null || deformedVertices == null)
            {
                return;
            }

            var displacements = layer.BrushDisplacements;
            if (displacements == null || displacements.Length != sourceVertices.Length)
            {
                return;
            }

            float weight = layer.Weight;
            for (int vertex = 0; vertex < deformedVertices.Length; vertex++)
            {
                deformedVertices[vertex] += displacements[vertex] * weight;
            }
        }

        private void TryApplyLatticeLayerContribution(LatticeLayer layer, Vector3[] sourceVertices, Vector3[] deformedVertices)
        {
            if (layer == null || sourceVertices == null || deformedVertices == null)
            {
                return;
            }

            var layerSettings = layer.Settings;
            if (layerSettings == null || !EnsureCache(layerSettings))
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
            CollectControlPointsLocal(layerSettings, _controlBuffer.AsSpan());

            var layerVertices = DeformWithJobs(entries, _controlBuffer);
            float weight = layer.Weight;
            for (int vertex = 0; vertex < deformedVertices.Length; vertex++)
            {
                deformedVertices[vertex] += (layerVertices[vertex] - sourceVertices[vertex]) * weight;
            }
        }

        private void TryAddBlendShapeFrame(Mesh mesh, LatticeLayer layer, Vector3[] sourceVertices, Vector3[] baseVertices)
        {
            if (mesh == null || layer == null || sourceVertices == null || baseVertices == null)
            {
                return;
            }

            int vertexCount = sourceVertices.Length;
            var deltaVertices = new Vector3[vertexCount];

            switch (layer.Type)
            {
                case MeshDeformerLayerType.Brush:
                {
                    var displacements = layer.BrushDisplacements;
                    if (displacements == null || displacements.Length != vertexCount)
                    {
                        return;
                    }

                    float weight = layer.Weight;
                    for (int v = 0; v < vertexCount; v++)
                    {
                        deltaVertices[v] = displacements[v] * weight;
                    }

                    break;
                }
                default:
                {
                    var layerSettings = layer.Settings;
                    if (layerSettings == null || !EnsureCache(layerSettings))
                    {
                        return;
                    }

                    var entries = _cache.Entries;
                    if (entries == null || entries.Length != vertexCount)
                    {
                        return;
                    }

                    int cpCount = layerSettings.ControlPointCount;
                    EnsureControlBuffer(cpCount);
                    CollectControlPointsLocal(layerSettings, _controlBuffer.AsSpan());

                    var layerVertices = DeformWithJobs(entries, _controlBuffer);
                    float weight = layer.Weight;
                    for (int v = 0; v < vertexCount; v++)
                    {
                        deltaVertices[v] = (layerVertices[v] - sourceVertices[v]) * weight;
                    }

                    break;
                }
            }

            string shapeName = layer.EffectiveBlendShapeName;
            mesh.AddBlendShapeFrame(shapeName, 100f, deltaVertices, null, null);
        }

        public void RestoreOriginalMesh()
        {
            if (_skinnedMeshRenderer != null && _sourceMesh != null)
            {
                _skinnedMeshRenderer.sharedMesh = _sourceMesh;
            }

            if (_meshFilter != null && _sourceMesh != null)
            {
                _meshFilter.sharedMesh = _sourceMesh;
            }

            ReleaseRuntimeMesh();
        }

        public void InvalidateCache()
        {
            if (_cache == null)
            {
                _cache = new LatticeDeformerCache();
            }

            _cache.Clear();
        }

        public void InitializeFromSource(bool resetControlPoints)
        {
            EnsureSettings();
            EnsureLayers();
            if (_sourceMesh == null)
            {
                return;
            }

            var meshBounds = _sourceMesh.bounds;
            for (int i = 0; i < _layers.Count; i++)
            {
                if (_layers[i] == null)
                {
                    _layers[i] = new LatticeLayer();
                }

                var layerSettings = _layers[i].Settings;
                if (layerSettings == null)
                {
                    continue;
                }

                layerSettings.LocalBounds = meshBounds;
                if (resetControlPoints)
                {
                    layerSettings.ResetControlPoints();
                }

                if (_layers[i].Type == MeshDeformerLayerType.Brush)
                {
                    _layers[i].EnsureBrushDisplacementCapacity(_sourceMesh.vertexCount);
                    if (resetControlPoints)
                    {
                        _layers[i].ClearBrushDisplacements();
                    }
                }
            }

            _settings = CloneSettings(GetPrimaryLayerSettings());

            _hasInitializedFromSource = true;
            InvalidateCache();

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(this);

                if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(this))
                {
                    UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
                }
            }
#endif
        }

        private void EnsureSettings()
        {
            if (_settings == null)
            {
                _settings = new LatticeAsset();
            }

            _settings.EnsureInitialized();
        }

        private void EnsureLayers()
        {
            if (_layerModelVersion < k_CurrentLayerModelVersion)
            {
                TryMigrateLegacyBaseToLayerStructure();
            }

            if (_layers == null)
            {
                _layers = new List<LatticeLayer>();
            }

            for (int i = 0; i < _layers.Count; i++)
            {
                if (_layers[i] == null)
                {
                    _layers[i] = new LatticeLayer();
                }

                var layer = _layers[i];
                _ = layer.Settings;

                if (string.IsNullOrWhiteSpace(layer.Name))
                {
                    layer.Name = layer.Type == MeshDeformerLayerType.Brush ? k_BrushLayerName : k_PrimaryLayerName;
                }
            }

            if (_layers.Count == 0)
            {
                var seed = _settings != null ? CloneSettings(_settings) : new LatticeAsset();
                seed.EnsureInitialized();
                _layers.Add(new LatticeLayer
                {
                    Name = k_PrimaryLayerName,
                    Enabled = true,
                    Weight = 1f,
                    Settings = seed
                });
            }

            if (_layers[0] != null && string.IsNullOrWhiteSpace(_layers[0].Name))
            {
                _layers[0].Name = _layers[0].Type == MeshDeformerLayerType.Brush ? k_BrushLayerName : k_PrimaryLayerName;
            }

            int maxIndex = _layers.Count - 1;
            _activeLayerIndex = Mathf.Clamp(_activeLayerIndex, 0, maxIndex);

            if (_sourceMesh != null)
            {
                EnsureBrushLayerDisplacementCapacity(_sourceMesh.vertexCount);
            }
        }

        private void CacheSourceMesh()
        {
            Mesh nextSource = GetSharedSourceMesh();

            if (_runtimeMesh != null && ReferenceEquals(_runtimeMesh, nextSource))
            {
                return;
            }

            bool meshChanged = !ReferenceEquals(_sourceMesh, nextSource);

            _sourceMesh = nextSource;

            if (!ReferenceEquals(_serializedSourceMesh, nextSource))
            {
                _serializedSourceMesh = nextSource;
                _hasInitializedFromSource = false;
            }

            if (!meshChanged)
            {
                return;
            }

            InvalidateCache();
            ReleaseRuntimeMesh();
            EnsureBrushLayerDisplacementCapacity(_sourceMesh != null ? _sourceMesh.vertexCount : 0);
        }

        private Mesh GetSharedSourceMesh()
        {
            if (_skinnedMeshRenderer != null)
            {
                return _skinnedMeshRenderer.sharedMesh;
            }

            if (_meshFilter != null)
            {
                return _meshFilter.sharedMesh;
            }

            return null;
        }

        private void TryAutoConfigureSettings()
        {
            if (_sourceMesh == null)
            {
                return;
            }

            var settings = GetPrimaryLayerSettings();

            if (!_hasInitializedFromSource && settings != null && settings.HasCustomizedControlPoints())
            {
                _hasInitializedFromSource = true;
            }

            if (_hasInitializedFromSource)
            {
                return;
            }

            InitializeFromSource(true);
        }

        private bool TryMigrateLegacyBaseToLayerStructure()
        {
            EnsureSettings();
            if (_layerModelVersion >= k_CurrentLayerModelVersion)
            {
                return false;
            }

            var existingLayers = _layers ?? new List<LatticeLayer>();
            var migratedLayers = new List<LatticeLayer>();

            bool includeLegacyBase = _settings != null && (_settings.HasCustomizedControlPoints() || existingLayers.Count == 0);
            if (includeLegacyBase)
            {
                migratedLayers.Add(new LatticeLayer
                {
                    Name = k_PrimaryLayerName,
                    Enabled = true,
                    Weight = 1f,
                    Settings = CloneSettings(_settings)
                });
            }

            for (int i = 0; i < existingLayers.Count; i++)
            {
                var existing = existingLayers[i];
                if (existing == null)
                {
                    continue;
                }

                _ = existing.Settings;
                migratedLayers.Add(existing);
            }

            if (migratedLayers.Count == 0)
            {
                migratedLayers.Add(new LatticeLayer
                {
                    Name = k_PrimaryLayerName,
                    Enabled = true,
                    Weight = 1f,
                    Settings = CreateNeutralLayerSettings(_settings)
                });
            }

            int migratedActive = _activeLayerIndex;
            if (includeLegacyBase && migratedActive >= 0)
            {
                migratedActive++;
            }

            _layers = migratedLayers;
            _activeLayerIndex = Mathf.Clamp(migratedActive, 0, _layers.Count - 1);
            _layerModelVersion = k_CurrentLayerModelVersion;
            _settings = CloneSettings(GetPrimaryLayerSettings());

            InvalidateCache();

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(this);

                if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(this))
                {
                    UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
                }
            }
#endif
            return true;
        }

        private LatticeAsset GetPrimaryLayerSettings()
        {
            EnsureSettings();
            EnsureLayers();
            if (_layers != null && _layers.Count > 0)
            {
                var layer = _layers[0] ?? (_layers[0] = new LatticeLayer());
                return layer.Settings;
            }

            return _settings;
        }

        private LatticeAsset GetSettingsForLayerIndex(int layerIndex)
        {
            if (!TryGetLayer(layerIndex, out var layer))
            {
                return GetPrimaryLayerSettings();
            }

            return layer.Settings;
        }

        private bool TryGetLayer(int index, out LatticeLayer layer)
        {
            layer = null;
            if (_layers == null || index < 0 || index >= _layers.Count)
            {
                return false;
            }

            layer = _layers[index];
            return layer != null;
        }

        private string GenerateNextLayerName(MeshDeformerLayerType layerType)
        {
            EnsureLayers();

            string baseName = layerType == MeshDeformerLayerType.Brush ? k_BrushLayerName : k_PrimaryLayerName;

            bool baseNameExists = false;
            for (int i = 0; i < _layers.Count; i++)
            {
                if (_layers[i] != null && string.Equals(_layers[i].Name, baseName, StringComparison.OrdinalIgnoreCase))
                {
                    baseNameExists = true;
                    break;
                }
            }

            if (!baseNameExists)
            {
                return baseName;
            }

            int number = 1;
            while (true)
            {
                string candidate = $"{baseName} {number}";
                bool exists = false;
                for (int i = 0; i < _layers.Count; i++)
                {
                    if (_layers[i] != null && string.Equals(_layers[i].Name, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    return candidate;
                }

                number++;
            }
        }

        private static LatticeAsset CreateNeutralLayerSettings(LatticeAsset source)
        {
            var cloned = CloneSettings(source);
            cloned.ResetControlPoints();
            return cloned;
        }

        private static LatticeAsset CloneSettings(LatticeAsset source)
        {
            var cloned = new LatticeAsset();
            if (source == null)
            {
                cloned.EnsureInitialized();
                return cloned;
            }

            cloned.GridSize = source.GridSize;
            cloned.LocalBounds = source.LocalBounds;
            cloned.Interpolation = source.Interpolation;
            cloned.EnsureInitialized();

            int count = Mathf.Min(cloned.ControlPointCount, source.ControlPointCount);
            for (int i = 0; i < count; i++)
            {
                cloned.SetControlPointLocal(i, source.GetControlPointLocal(i));
            }

            return cloned;
        }

        private static int HashAssetState(LatticeAsset settings)
        {
            if (settings == null)
            {
                return 0;
            }

            int hash = HashCode.Combine(settings.GridSize, settings.LocalBounds.center, settings.LocalBounds.size, (int)settings.Interpolation);
            var points = settings.ControlPointsLocal;
            foreach (var point in points)
            {
                hash = HashCode.Combine(hash, point.x, point.y, point.z);
            }

            hash = HashCode.Combine(hash, points.Length);
            return hash;
        }

        private static int HashDisplacementState(Vector3[] displacements)
        {
            if (displacements == null)
            {
                return 0;
            }

            int hash = 17;
            for (int i = 0; i < displacements.Length; i++)
            {
                var displacement = displacements[i];
                hash = HashCode.Combine(hash, displacement.x, displacement.y, displacement.z);
            }

            hash = HashCode.Combine(hash, displacements.Length);
            return hash;
        }

        private void EnsureBrushLayerDisplacementCapacity(int vertexCount)
        {
            if (_layers == null)
            {
                return;
            }

            for (int i = 0; i < _layers.Count; i++)
            {
                var layer = _layers[i];
                if (layer == null || layer.Type != MeshDeformerLayerType.Brush)
                {
                    continue;
                }

                layer.EnsureBrushDisplacementCapacity(vertexCount);
            }
        }

        private Mesh AcquireRuntimeMesh(bool assignToRenderer)
        {
            if (_runtimeMesh == null)
            {
                if (_sourceMesh == null)
                {
                    return null;
                }

                _runtimeMesh = Instantiate(_sourceMesh);
                _runtimeMesh.name = _sourceMesh.name + " (Mesh Deformer)";
                _runtimeMesh.hideFlags = HideFlags.HideAndDontSave;
            }

            if (assignToRenderer)
            {
                AssignRuntimeMesh(_runtimeMesh);
            }

            return _runtimeMesh;
        }

        private void AssignRuntimeMesh(Mesh mesh)
        {
            if (_skinnedMeshRenderer != null)
            {
                _skinnedMeshRenderer.sharedMesh = mesh;
            }

            if (_meshFilter != null)
            {
                _meshFilter.sharedMesh = mesh;
            }
        }

        private void ReleaseRuntimeMesh()
        {
            if (_runtimeMesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_runtimeMesh);
            }
            else
            {
                DestroyImmediate(_runtimeMesh);
            }

            _runtimeMesh = null;
        }

        private void EnsureControlBuffer(int controlPointCount)
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

        private static void CollectControlPointsLocal(LatticeAsset settings, Span<Vector3> buffer)
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

        private Vector3[] DeformWithJobs(LatticeCacheEntry[] entries, Vector3[] controlPoints)
        {
            if (entries == null || entries.Length == 0)
            {
                throw new ArgumentException("Cache entries are required for deformation.", nameof(entries));
            }

            if (controlPoints == null || controlPoints.Length == 0)
            {
                throw new ArgumentException("Control points are required for deformation.", nameof(controlPoints));
            }

            using var controlNative = LatticeNativeArrayUtility.CreateCopy(controlPoints, Allocator.TempJob);
            using var entriesNative = LatticeNativeArrayUtility.CreateCopy(entries, Allocator.TempJob);
            using var outputNative = LatticeNativeArrayUtility.CreateFloat3Array(entries.Length, Allocator.TempJob);

            var job = new DeformVerticesJob
            {
                ControlPoints = controlNative,
                Entries = entriesNative,
                Result = outputNative
            };

            job.Schedule(entries.Length, 64).Complete();

            var vertices = new Vector3[entries.Length];
            outputNative.CopyToManaged(vertices);

            return vertices;
        }


        private LatticeCacheEntry[] BuildCacheWithJobs(Vector3Int gridSize, Bounds bounds, Vector3[] restVertices)
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


        private bool EnsureCache(LatticeAsset settings)
        {
            if (settings == null)
            {
                return false;
            }

            if (_cache == null)
            {
                _cache = new LatticeDeformerCache();
            }

            var mesh = _sourceMesh;
            if (mesh == null)
            {
                return false;
            }

            if (_cache.IsCompatibleWith(settings, mesh))
            {
                return true;
            }

            return RebuildCache(settings, mesh);
        }

        private bool RebuildCache(LatticeAsset settings, Mesh mesh)
        {
            if (settings == null || mesh == null)
            {
                return false;
            }

            var gridSize = settings.GridSize;
            if (gridSize.x < 2 || gridSize.y < 2 || gridSize.z < 2)
            {
                return false;
            }

            int vertexCount = mesh.vertexCount;
            if (vertexCount <= 0)
            {
                _cache.Clear();
                return false;
            }

            var restVertices = mesh.vertices;
            var bounds = settings.LocalBounds;
            LatticeCacheEntry[] entries;

            entries = BuildCacheWithJobs(gridSize, bounds, restVertices);

            _cache.Populate(gridSize, bounds, settings.Interpolation, vertexCount, entries, restVertices);
            return true;
        }

        private static Vector3 CalculateNormalizedCoordinate(Bounds bounds, Vector3 point)
        {
            var size = bounds.size;
            var min = bounds.min;

            float nx = size.x > Mathf.Epsilon ? (point.x - min.x) / size.x : 0f;
            float ny = size.y > Mathf.Epsilon ? (point.y - min.y) / size.y : 0f;
            float nz = size.z > Mathf.Epsilon ? (point.z - min.z) / size.z : 0f;

            return new Vector3(Mathf.Clamp01(nx), Mathf.Clamp01(ny), Mathf.Clamp01(nz));
        }

        private static LatticeCacheEntry BuildTrilinearEntry(Vector3Int gridSize, Vector3 barycentric)
        {
            var grid = new int3(gridSize.x, gridSize.y, gridSize.z);

            float3 scaled = new float3(
                math.clamp(barycentric.x * (grid.x - 1), 0f, grid.x - 1),
                math.clamp(barycentric.y * (grid.y - 1), 0f, grid.y - 1),
                math.clamp(barycentric.z * (grid.z - 1), 0f, grid.z - 1));

            int ix = math.min((int)math.floor(scaled.x), grid.x - 2);
            int iy = math.min((int)math.floor(scaled.y), grid.y - 2);
            int iz = math.min((int)math.floor(scaled.z), grid.z - 2);

            float tx = math.saturate(scaled.x - ix);
            float ty = math.saturate(scaled.y - iy);
            float tz = math.saturate(scaled.z - iz);

            int nx = grid.x;
            int ny = grid.y;

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
                Barycentric = new float3(tx, ty, tz)
            };
        }

        private static Bounds TransformBounds(Matrix4x4 matrix, Bounds bounds)
        {
            var center = matrix.MultiplyPoint3x4(bounds.center);
            var extents = bounds.extents;

            var axisX = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
            var axisY = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
            var axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));

            var halfSize = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));

            return new Bounds(center, halfSize * 2f);
        }

        [BurstCompile]
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
                    Barycentric = new float3(tx, ty, tz)
                };
            }
        }
    }

    [Serializable]
    internal sealed class LatticeDeformerCache
    {
        [SerializeField] private Vector3Int _gridSize;
        [SerializeField] private Bounds _localBounds;
        [SerializeField] private LatticeInterpolationMode _interpolation;
        [SerializeField] private int _vertexCount;
        [SerializeField] private LatticeCacheEntry[] _entries = Array.Empty<LatticeCacheEntry>();
        [SerializeField] private Vector3[] _restVertices = Array.Empty<Vector3>();

        public LatticeCacheEntry[] Entries => _entries;

        public bool IsCompatibleWith(LatticeAsset asset, Mesh mesh)
        {
            if (asset == null || mesh == null)
            {
                return false;
            }

            if (_entries == null || _entries.Length == 0)
            {
                return false;
            }

            if (_vertexCount != mesh.vertexCount)
            {
                return false;
            }

            if (_gridSize != asset.GridSize)
            {
                return false;
            }

            if (_interpolation != asset.Interpolation)
            {
                return false;
            }

            if (!ApproximatelyEquals(_localBounds, asset.LocalBounds))
            {
                return false;
            }

            return true;
        }

        public void Populate(Vector3Int gridSize, Bounds bounds, LatticeInterpolationMode interpolation, int vertexCount, LatticeCacheEntry[] entries, Vector3[] restVertices)
        {
            _gridSize = gridSize;
            _localBounds = bounds;
            _interpolation = interpolation;
            _vertexCount = vertexCount;
            _entries = entries ?? Array.Empty<LatticeCacheEntry>();
            _restVertices = restVertices ?? Array.Empty<Vector3>();
        }

        public void Clear()
        {
            _entries = Array.Empty<LatticeCacheEntry>();
            _restVertices = Array.Empty<Vector3>();
            _vertexCount = 0;
        }

        private static bool ApproximatelyEquals(Bounds lhs, Bounds rhs)
        {
            const float epsilon = 1e-5f;
            return (lhs.center - rhs.center).sqrMagnitude <= epsilon * epsilon &&
                   (lhs.size - rhs.size).sqrMagnitude <= epsilon * epsilon;
        }
    }

    [Serializable]
    internal struct LatticeCacheEntry
    {
        public int Corner0;
        public int Corner1;
        public int Corner2;
        public int Corner3;
        public int Corner4;
        public int Corner5;
        public int Corner6;
        public int Corner7;
        public float4 Weights0;
        public float4 Weights1;
        public float3 Barycentric;
    }
}
