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
        [SerializeField, HideInInspector] private float[] _vertexMask = Array.Empty<float>();
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

        public float[] VertexMask
        {
            get => _vertexMask ?? (_vertexMask = Array.Empty<float>());
            set => _vertexMask = value ?? Array.Empty<float>();
        }

        public void EnsureVertexMaskCapacity(int vertexCount)
        {
            vertexCount = Mathf.Max(0, vertexCount);
            if (_vertexMask == null || _vertexMask.Length != vertexCount)
            {
                var previous = _vertexMask;
                _vertexMask = new float[vertexCount];
                // Initialize to 1.0 (fully editable)
                for (int i = 0; i < vertexCount; i++)
                {
                    _vertexMask[i] = 1f;
                }

                if (previous != null)
                {
                    int copyLen = Mathf.Min(previous.Length, vertexCount);
                    Array.Copy(previous, _vertexMask, copyLen);
                }
            }
        }

        public float GetVertexMask(int index)
        {
            if (_vertexMask == null || index < 0 || index >= _vertexMask.Length)
            {
                return 1f; // Default: fully editable
            }

            return _vertexMask[index];
        }

        public void SetVertexMask(int index, float value)
        {
            if (_vertexMask == null || index < 0 || index >= _vertexMask.Length)
            {
                return;
            }

            _vertexMask[index] = Mathf.Clamp01(value);
        }

        public void ClearVertexMask()
        {
            if (_vertexMask == null)
            {
                return;
            }

            for (int i = 0; i < _vertexMask.Length; i++)
            {
                _vertexMask[i] = 1f;
            }
        }

        public bool HasVertexMask()
        {
            if (_vertexMask == null || _vertexMask.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < _vertexMask.Length; i++)
            {
                if (_vertexMask[i] < 1f - 1e-6f)
                {
                    return true;
                }
            }

            return false;
        }
    }

    [Serializable]
    public sealed class DeformerGroup
    {
        [SerializeField] private string _name = "Group";
        [SerializeField] private bool _enabled = true;
        [SerializeField] private List<LatticeLayer> _layers = new List<LatticeLayer>();
        [SerializeField] private int _activeLayerIndex = 0;
        [SerializeField] private BlendShapeOutputMode _blendShapeOutput = BlendShapeOutputMode.Disabled;
        [SerializeField] private string _blendShapeName = "";
        [SerializeField] private AnimationCurve _blendShapeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        public string Name
        {
            get => string.IsNullOrWhiteSpace(_name) ? "Group" : _name;
            set => _name = string.IsNullOrWhiteSpace(value) ? "Group" : value;
        }

        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public List<LatticeLayer> LayersList
        {
            get
            {
                if (_layers == null) _layers = new List<LatticeLayer>();
                return _layers;
            }
        }

        public IReadOnlyList<LatticeLayer> Layers => LayersList;

        public int ActiveLayerIndex
        {
            get
            {
                if (_layers == null || _layers.Count == 0) return 0;
                return Mathf.Clamp(_activeLayerIndex, 0, _layers.Count - 1);
            }
            set
            {
                if (_layers == null || _layers.Count == 0) { _activeLayerIndex = 0; return; }
                _activeLayerIndex = Mathf.Clamp(value, 0, _layers.Count - 1);
            }
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

        public AnimationCurve BlendShapeCurve
        {
            get => _blendShapeCurve ?? (_blendShapeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f));
            set => _blendShapeCurve = value ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);
        }

        public string EffectiveBlendShapeName(string fallback)
        {
            return string.IsNullOrWhiteSpace(_blendShapeName) ? fallback : _blendShapeName;
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

        // Legacy fields kept for deserialization / migration
        [SerializeField] private LatticeAsset _settings = new LatticeAsset();
        [SerializeField] private List<LatticeLayer> _layers = new List<LatticeLayer>();
        [SerializeField, HideInInspector] private int _activeLayerIndex = 0;
        [SerializeField, HideInInspector] private int _layerModelVersion = 0;
        [SerializeField] private BlendShapeOutputMode _blendShapeOutput = BlendShapeOutputMode.Disabled;
        [SerializeField] private string _blendShapeName = "";
        [SerializeField] private AnimationCurve _blendShapeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        // New group-based structure
        [SerializeField] private List<DeformerGroup> _groups = new List<DeformerGroup>();
        [SerializeField, HideInInspector] private int _activeGroupIndex = 0;

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
        [NonSerialized] private int _lastBlendShapeHash;
        private const int k_CurrentLayerModelVersion = 3;
        private const string k_PrimaryLayerName = "Lattice Layer";
        private const string k_BrushLayerName = "Brush Layer";

        private Vector3[] _controlBuffer = Array.Empty<Vector3>();

        /// <summary>
        /// Base layer settings (legacy). Delegates to the first layer of the active group.
        /// </summary>
        public LatticeAsset Settings
        {
            get => GetPrimaryLayerSettings();
            set
            {
                EnsureGroups();
                var resolved = value ?? new LatticeAsset();
                resolved.EnsureInitialized();

                var group = ActiveGroup;
                if (group != null)
                {
                    var layers = group.LayersList;
                    if (layers.Count == 0)
                    {
                        layers.Add(new LatticeLayer
                        {
                            Name = k_PrimaryLayerName,
                            Enabled = true,
                            Weight = 1f,
                            Settings = resolved
                        });
                    }
                    else
                    {
                        if (layers[0] == null) layers[0] = new LatticeLayer();
                        layers[0].Name = k_PrimaryLayerName;
                        layers[0].Enabled = true;
                        layers[0].Settings = resolved;
                    }
                }

                _settings = CloneSettings(resolved);
                _hasInitializedFromSource = false;
                InvalidateCache();
            }
        }

        // ── Group-level API ──────────────────────────────────────────

        public IReadOnlyList<DeformerGroup> Groups
        {
            get
            {
                EnsureGroups();
                return _groups;
            }
        }

        public int GroupCount
        {
            get
            {
                EnsureGroups();
                return _groups.Count;
            }
        }

        public int ActiveGroupIndex
        {
            get
            {
                EnsureGroups();
                return _activeGroupIndex;
            }
            set
            {
                EnsureGroups();
                _activeGroupIndex = _groups.Count > 0 ? Mathf.Clamp(value, 0, _groups.Count - 1) : 0;
            }
        }

        public DeformerGroup ActiveGroup
        {
            get
            {
                EnsureGroups();
                if (_groups.Count == 0) return null;
                return _groups[Mathf.Clamp(_activeGroupIndex, 0, _groups.Count - 1)];
            }
        }

        public int AddGroup(string groupName = null)
        {
            EnsureGroups();
            var group = new DeformerGroup();
            group.Name = string.IsNullOrWhiteSpace(groupName) ? GenerateNextGroupName() : groupName;
            _groups.Add(group);
            _activeGroupIndex = _groups.Count - 1;
            return _activeGroupIndex;
        }

        public bool RemoveGroup(int index)
        {
            EnsureGroups();
            if (index < 0 || index >= _groups.Count || _groups.Count <= 1)
                return false;

            _groups.RemoveAt(index);
            if (_activeGroupIndex == index)
                _activeGroupIndex = Mathf.Min(index, _groups.Count - 1);
            else if (_activeGroupIndex > index)
                _activeGroupIndex--;
            return true;
        }

        // ── Facade: delegates to ActiveGroup ────────────────────────

        public IReadOnlyList<LatticeLayer> Layers
        {
            get
            {
                EnsureGroups();
                var group = ActiveGroup;
                return group != null ? group.Layers : (IReadOnlyList<LatticeLayer>)Array.Empty<LatticeLayer>();
            }
        }

        public int ActiveLayerIndex
        {
            get
            {
                EnsureGroups();
                var group = ActiveGroup;
                return group?.ActiveLayerIndex ?? 0;
            }
            set
            {
                EnsureGroups();
                var group = ActiveGroup;
                if (group != null) group.ActiveLayerIndex = value;
            }
        }

        public bool IsEditingBaseLayer => false;

        public LatticeAsset EditingSettings
        {
            get
            {
                EnsureGroups();
                var group = ActiveGroup;
                if (group == null) return GetPrimaryLayerSettings();
                var layers = group.LayersList;
                int idx = group.ActiveLayerIndex;
                if (idx >= 0 && idx < layers.Count && layers[idx] != null)
                    return layers[idx].Settings;
                return GetPrimaryLayerSettings();
            }
        }

        public MeshDeformerLayerType ActiveLayerType
        {
            get
            {
                EnsureGroups();
                var group = ActiveGroup;
                if (group == null) return MeshDeformerLayerType.Lattice;
                var layers = group.LayersList;
                int idx = group.ActiveLayerIndex;
                if (idx >= 0 && idx < layers.Count && layers[idx] != null)
                    return layers[idx].Type;
                return MeshDeformerLayerType.Lattice;
            }
        }

        public Mesh RuntimeMesh => _runtimeMesh;

        public Mesh SourceMesh => _sourceMesh;

        public bool RecalculateBoneWeights
        {
            get => _recalculateBoneWeights;
            set => _recalculateBoneWeights = value;
        }

        public BlendShapeOutputMode BlendShapeOutput
        {
            get
            {
                var group = ActiveGroup;
                return group?.BlendShapeOutput ?? BlendShapeOutputMode.Disabled;
            }
            set
            {
                var group = ActiveGroup;
                if (group != null) group.BlendShapeOutput = value;
            }
        }

        public string BlendShapeName
        {
            get
            {
                var group = ActiveGroup;
                return group?.BlendShapeName ?? "";
            }
            set
            {
                var group = ActiveGroup;
                if (group != null) group.BlendShapeName = value ?? "";
            }
        }

        public string EffectiveBlendShapeName
        {
            get
            {
                var group = ActiveGroup;
                return group?.EffectiveBlendShapeName(gameObject.name) ?? gameObject.name;
            }
        }

        public AnimationCurve BlendShapeCurve
        {
            get
            {
                var group = ActiveGroup;
                return group?.BlendShapeCurve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);
            }
            set
            {
                var group = ActiveGroup;
                if (group != null) group.BlendShapeCurve = value ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);
            }
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

        // Brush-layer compatibility surface for BrushToolHandler.
        // All delegate to ActiveGroup's active layer.
        public Vector3[] Displacements
        {
            get
            {
                EnsureGroups();
                if (!TryGetActiveLayer(out var layer) || layer.Type != MeshDeformerLayerType.Brush)
                    return Array.Empty<Vector3>();
                return layer.BrushDisplacements;
            }
        }

        public int DisplacementCount => Displacements.Length;

        public bool HasDisplacements()
        {
            EnsureGroups();
            if (!TryGetActiveLayer(out var layer) || layer.Type != MeshDeformerLayerType.Brush)
                return false;
            return layer.HasBrushDisplacements();
        }

        public void EnsureDisplacementCapacity()
        {
            EnsureGroups();
            CacheSourceMesh();
            if (_sourceMesh == null) return;
            if (TryGetActiveLayer(out var layer) && layer.Type == MeshDeformerLayerType.Brush)
                layer.EnsureBrushDisplacementCapacity(_sourceMesh.vertexCount);
        }

        public void SetDisplacement(int index, Vector3 displacement)
        {
            EnsureGroups();
            if (TryGetActiveLayer(out var layer) && layer.Type == MeshDeformerLayerType.Brush)
                layer.SetBrushDisplacement(index, displacement);
        }

        public void AddDisplacement(int index, Vector3 delta)
        {
            EnsureGroups();
            if (TryGetActiveLayer(out var layer) && layer.Type == MeshDeformerLayerType.Brush)
                layer.AddBrushDisplacement(index, delta);
        }

        public Vector3 GetDisplacement(int index)
        {
            EnsureGroups();
            if (TryGetActiveLayer(out var layer) && layer.Type == MeshDeformerLayerType.Brush)
                return layer.GetBrushDisplacement(index);
            return Vector3.zero;
        }

        public void ClearDisplacements()
        {
            EnsureGroups();
            if (TryGetActiveLayer(out var layer) && layer.Type == MeshDeformerLayerType.Brush)
                layer.ClearBrushDisplacements();
        }

        // ── Layer management (operates on ActiveGroup) ──────────────

        public int AddLayer(string layerName = null, MeshDeformerLayerType layerType = MeshDeformerLayerType.Lattice)
        {
            EnsureGroups();
            var group = ActiveGroup;
            if (group == null) return -1;
            var layers = group.LayersList;

            var source = EditingSettings ?? GetPrimaryLayerSettings();
            var newLayer = new LatticeLayer
            {
                Name = string.IsNullOrWhiteSpace(layerName) ? GenerateNextLayerName(layerType) : layerName,
                Enabled = true,
                Weight = 1f,
                Settings = CreateNeutralLayerSettings(source)
            };
            newLayer.SetType(layerType);

            layers.Add(newLayer);
            group.ActiveLayerIndex = layers.Count - 1;
            if (layerType == MeshDeformerLayerType.Brush)
                EnsureDisplacementCapacity();

            return group.ActiveLayerIndex;
        }

        public int DuplicateLayer(int index)
        {
            EnsureGroups();
            var group = ActiveGroup;
            if (group == null) return -1;
            var layers = group.LayersList;

            if (index < 0 || index >= layers.Count || layers[index] == null)
                return -1;

            var sourceLayer = layers[index];
            var duplicate = new LatticeLayer
            {
                Name = sourceLayer.Name + " Copy",
                Enabled = sourceLayer.Enabled,
                Weight = sourceLayer.Weight,
                Settings = CloneSettings(sourceLayer.Settings)
            };
            duplicate.SetType(sourceLayer.Type);
            duplicate.BrushDisplacements = (Vector3[])sourceLayer.BrushDisplacements.Clone();
            if (sourceLayer.VertexMask.Length > 0)
                duplicate.VertexMask = (float[])sourceLayer.VertexMask.Clone();

            int insertAt = Mathf.Clamp(index + 1, 0, layers.Count);
            layers.Insert(insertAt, duplicate);
            group.ActiveLayerIndex = insertAt;
            return group.ActiveLayerIndex;
        }

        public int InsertLayer(LatticeLayer layer)
        {
            if (layer == null) return -1;
            EnsureGroups();
            var group = ActiveGroup;
            if (group == null) return -1;
            var layers = group.LayersList;
            layers.Add(layer);
            group.ActiveLayerIndex = layers.Count - 1;
            return group.ActiveLayerIndex;
        }

        public bool RemoveLayer(int index)
        {
            EnsureGroups();
            var group = ActiveGroup;
            if (group == null) return false;
            var layers = group.LayersList;
            if (index < 0 || index >= layers.Count || layers.Count <= 1)
                return false;

            layers.RemoveAt(index);
            int active = group.ActiveLayerIndex;
            if (active == index)
                group.ActiveLayerIndex = Mathf.Min(index, layers.Count - 1);
            else if (active > index)
                group.ActiveLayerIndex = active - 1;
            return true;
        }

        public bool MoveLayer(int index, int targetIndex)
        {
            EnsureGroups();
            var group = ActiveGroup;
            if (group == null) return false;
            var layers = group.LayersList;
            if (index < 0 || index >= layers.Count) return false;

            targetIndex = Mathf.Clamp(targetIndex, 0, layers.Count - 1);
            if (targetIndex == index) return true;

            var layer = layers[index];
            layers.RemoveAt(index);
            layers.Insert(targetIndex, layer);

            int active = group.ActiveLayerIndex;
            if (active == index)
                group.ActiveLayerIndex = targetIndex;
            else if (index < active && targetIndex >= active)
                group.ActiveLayerIndex = active - 1;
            else if (index > active && targetIndex <= active)
                group.ActiveLayerIndex = active + 1;
            return true;
        }

        public int ImportBlendShapeAsLayer(int blendShapeIndex, int frameIndex = 0)
        {
            if (_sourceMesh == null) return -1;
            int shapeCount = _sourceMesh.blendShapeCount;
            if (blendShapeIndex < 0 || blendShapeIndex >= shapeCount) return -1;
            int frameCount = _sourceMesh.GetBlendShapeFrameCount(blendShapeIndex);
            if (frameIndex < 0 || frameIndex >= frameCount) return -1;
            int vertexCount = _sourceMesh.vertexCount;
            if (vertexCount == 0) return -1;

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
                layer.SetBrushDisplacement(i, deltaVertices[i]);

            EnsureGroups();
            var group = ActiveGroup;
            if (group == null) return -1;
            group.LayersList.Add(layer);
            return group.LayersList.Count - 1;
        }

        /// <summary>
        /// Splits a layer's deformation data by zeroing out one side of the given axis.
        /// For brush layers, vertices on the zeroed side have their displacements cleared.
        /// For lattice layers, control points on the zeroed side are reset to their default positions.
        /// </summary>
        /// <param name="layerIndex">Index of the layer to split</param>
        /// <param name="axis">0=X, 1=Y, 2=Z</param>
        /// <param name="keepPositiveSide">true keeps the positive side, false keeps the negative side</param>
        public void SplitLayerByAxis(int layerIndex, int axis, bool keepPositiveSide)
        {
            EnsureGroups();
            if (!TryGetLayerInActiveGroup(layerIndex, out var layer))
            {
                return;
            }

            CacheSourceMesh();

            if (layer.Type == MeshDeformerLayerType.Brush)
            {
                if (_sourceMesh == null)
                {
                    return;
                }

                var displacements = layer.BrushDisplacements;
                var vertices = _sourceMesh.vertices;
                if (displacements == null || vertices == null || displacements.Length != vertices.Length)
                {
                    return;
                }

                for (int i = 0; i < vertices.Length; i++)
                {
                    float coord = axis == 0 ? vertices[i].x : axis == 1 ? vertices[i].y : vertices[i].z;
                    bool isPositive = coord >= 0f;
                    if (isPositive != keepPositiveSide)
                    {
                        layer.SetBrushDisplacement(i, Vector3.zero);
                    }
                }
            }
            else // Lattice
            {
                var settings = layer.Settings;
                if (settings == null)
                {
                    return;
                }

                var gridSize = settings.GridSize;
                int axisSize = axis == 0 ? gridSize.x : axis == 1 ? gridSize.y : gridSize.z;
                int mid = axisSize / 2;

                var boundsMin = settings.LocalBounds.min;
                var boundsSize = settings.LocalBounds.size;

                for (int z = 0; z < gridSize.z; z++)
                {
                    for (int y = 0; y < gridSize.y; y++)
                    {
                        for (int x = 0; x < gridSize.x; x++)
                        {
                            int axisCoord = axis == 0 ? x : axis == 1 ? y : z;
                            bool isPositive = axisCoord >= mid;
                            if (isPositive != keepPositiveSide)
                            {
                                // Compute neutral/default position from bounds
                                float wx = gridSize.x > 1 ? (float)x / (gridSize.x - 1) : 0f;
                                float wy = gridSize.y > 1 ? (float)y / (gridSize.y - 1) : 0f;
                                float wz = gridSize.z > 1 ? (float)z / (gridSize.z - 1) : 0f;
                                var neutralPos = boundsMin + Vector3.Scale(boundsSize, new Vector3(wx, wy, wz));

                                int index = x + y * gridSize.x + z * gridSize.x * gridSize.y;
                                settings.SetControlPointLocal(index, neutralPos);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Flips a layer's deformation data across the given axis.
        /// For brush layers, swaps displacements between mirrored vertex pairs and negates the axis component.
        /// For lattice layers, mirrors control point offsets across the axis.
        /// </summary>
        /// <param name="layerIndex">Index of the layer to flip</param>
        /// <param name="axis">0=X, 1=Y, 2=Z</param>
        public void FlipLayerByAxis(int layerIndex, int axis)
        {
            EnsureGroups();
            if (!TryGetLayerInActiveGroup(layerIndex, out var layer))
            {
                return;
            }

            CacheSourceMesh();

            if (layer.Type == MeshDeformerLayerType.Brush)
            {
                if (_sourceMesh == null)
                {
                    return;
                }

                var displacements = layer.BrushDisplacements;
                var vertices = _sourceMesh.vertices;
                if (displacements == null || vertices == null || displacements.Length != vertices.Length)
                {
                    return;
                }

                int vertexCount = vertices.Length;
                var newDisplacements = new Vector3[vertexCount];
                var matched = new bool[vertexCount];

                // Build mirror map: for each vertex, find its mirror counterpart
                for (int i = 0; i < vertexCount; i++)
                {
                    if (matched[i])
                    {
                        continue;
                    }

                    var pos = vertices[i];
                    var mirrorPos = pos;
                    if (axis == 0) mirrorPos.x = -mirrorPos.x;
                    else if (axis == 1) mirrorPos.y = -mirrorPos.y;
                    else mirrorPos.z = -mirrorPos.z;

                    // Find nearest mirror vertex
                    int mirrorIndex = -1;
                    float bestDistSq = float.MaxValue;
                    for (int j = 0; j < vertexCount; j++)
                    {
                        float distSq = (vertices[j] - mirrorPos).sqrMagnitude;
                        if (distSq < bestDistSq)
                        {
                            bestDistSq = distSq;
                            mirrorIndex = j;
                        }
                    }

                    // Tolerance check (1mm)
                    if (mirrorIndex < 0 || bestDistSq > 0.001f * 0.001f)
                    {
                        // No mirror found, negate axis component in place
                        var d = displacements[i];
                        if (axis == 0) d.x = -d.x;
                        else if (axis == 1) d.y = -d.y;
                        else d.z = -d.z;
                        newDisplacements[i] = d;
                        matched[i] = true;
                        continue;
                    }

                    // Swap and negate axis component
                    var di = displacements[i];
                    var dj = displacements[mirrorIndex];

                    if (axis == 0) { di.x = -di.x; dj.x = -dj.x; }
                    else if (axis == 1) { di.y = -di.y; dj.y = -dj.y; }
                    else { di.z = -di.z; dj.z = -dj.z; }

                    newDisplacements[i] = dj;
                    newDisplacements[mirrorIndex] = di;
                    matched[i] = true;
                    matched[mirrorIndex] = true;
                }

                // Apply
                for (int i = 0; i < vertexCount; i++)
                {
                    layer.SetBrushDisplacement(i, newDisplacements[i]);
                }
            }
            else // Lattice
            {
                var settings = layer.Settings;
                if (settings == null)
                {
                    return;
                }

                var gridSize = settings.GridSize;
                var boundsMin = settings.LocalBounds.min;
                var boundsSize = settings.LocalBounds.size;

                // Collect all control point offsets (delta from default)
                var offsets = new Vector3[gridSize.x, gridSize.y, gridSize.z];
                for (int z = 0; z < gridSize.z; z++)
                {
                    for (int y = 0; y < gridSize.y; y++)
                    {
                        for (int x = 0; x < gridSize.x; x++)
                        {
                            int index = x + y * gridSize.x + z * gridSize.x * gridSize.y;
                            var current = settings.GetControlPointLocal(index);

                            float wx = gridSize.x > 1 ? (float)x / (gridSize.x - 1) : 0f;
                            float wy = gridSize.y > 1 ? (float)y / (gridSize.y - 1) : 0f;
                            float wz = gridSize.z > 1 ? (float)z / (gridSize.z - 1) : 0f;
                            var neutral = boundsMin + Vector3.Scale(boundsSize, new Vector3(wx, wy, wz));

                            offsets[x, y, z] = current - neutral;
                        }
                    }
                }

                // Apply flipped
                for (int z = 0; z < gridSize.z; z++)
                {
                    for (int y = 0; y < gridSize.y; y++)
                    {
                        for (int x = 0; x < gridSize.x; x++)
                        {
                            int mx = axis == 0 ? gridSize.x - 1 - x : x;
                            int my = axis == 1 ? gridSize.y - 1 - y : y;
                            int mz = axis == 2 ? gridSize.z - 1 - z : z;

                            var offset = offsets[mx, my, mz];
                            if (axis == 0) offset.x = -offset.x;
                            else if (axis == 1) offset.y = -offset.y;
                            else offset.z = -offset.z;

                            float wx = gridSize.x > 1 ? (float)x / (gridSize.x - 1) : 0f;
                            float wy = gridSize.y > 1 ? (float)y / (gridSize.y - 1) : 0f;
                            float wz = gridSize.z > 1 ? (float)z / (gridSize.z - 1) : 0f;
                            var neutral = boundsMin + Vector3.Scale(boundsSize, new Vector3(wx, wy, wz));

                            int index = x + y * gridSize.x + z * gridSize.x * gridSize.y;
                            settings.SetControlPointLocal(index, neutral + offset);
                        }
                    }
                }
            }
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
            EnsureGroups();
            return TryGetLayerInActiveGroup(index, out _);
        }

        public void SyncLayerStructuresToBase(bool resetControlPoints)
        {
            // Base layer concept was removed in 1.3.0.
            // Kept as a no-op for backward compatibility.
        }

        public int ComputeLayeredStateHash()
        {
            EnsureGroups();

            int hash = 17;
            hash = HashCode.Combine(hash, _groups.Count);
            hash = HashCode.Combine(hash, _activeGroupIndex);

            foreach (var group in _groups)
            {
                if (group == null) { hash = HashCode.Combine(hash, 0); continue; }
                hash = HashCode.Combine(hash, group.Enabled);
                hash = HashCode.Combine(hash, (int)group.BlendShapeOutput);
                hash = HashCode.Combine(hash, (group.BlendShapeName ?? "").GetHashCode());

                var layers = group.LayersList;
                hash = HashCode.Combine(hash, layers.Count);
                hash = HashCode.Combine(hash, group.ActiveLayerIndex);

                foreach (var layer in layers)
                {
                    if (layer == null) { hash = HashCode.Combine(hash, 0); continue; }
                    hash = HashCode.Combine(hash, layer.Enabled);
                    hash = HashCode.Combine(hash, layer.Weight);
                    hash = HashCode.Combine(hash, (int)layer.Type);
                    switch (layer.Type)
                    {
                        case MeshDeformerLayerType.Brush:
                            hash = HashCode.Combine(hash, HashDisplacementState(layer.BrushDisplacements));
                            hash = HashCode.Combine(hash, HashMaskState(layer.VertexMask));
                            break;
                        default:
                            hash = HashCode.Combine(hash, HashAssetState(layer.Settings));
                            break;
                    }
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
            UnityEngine.Profiling.Profiler.BeginSample("LatticeDeformer.Deform");
            EnsureLayerModelReady();

            if (_sourceMesh == null)
            {
                UnityEngine.Profiling.Profiler.EndSample();
                return null;
            }

            var mesh = AcquireRuntimeMesh(assignToRenderer);
            if (mesh == null)
            {
                return null;
            }

            var sourceVertices = _sourceMesh.vertices;
            if (sourceVertices == null || sourceVertices.Length == 0)
            {
                return null;
            }

            int vertexCount = sourceVertices.Length;
            EnsureAllBrushLayerDisplacementCapacity(vertexCount);

            // Accumulate direct-deform deltas across all groups
            var directDeltas = new Vector3[vertexCount];
            // Collect BlendShape groups
            var blendShapeGroups = new List<(DeformerGroup group, Vector3[] deltas)>();

            for (int g = 0; g < _groups.Count; g++)
            {
                var group = _groups[g];
                if (group == null || !group.Enabled) continue;

                var groupVertices = (Vector3[])sourceVertices.Clone();
                var layers = group.LayersList;

                for (int i = 0; i < layers.Count; i++)
                {
                    var layer = layers[i];
                    if (layer == null || !layer.Enabled || layer.Weight <= 0f) continue;

                    switch (layer.Type)
                    {
                        case MeshDeformerLayerType.Brush:
                            TryApplyBrushLayerContribution(layer, sourceVertices, groupVertices);
                            break;
                        default:
                            TryApplyLatticeLayerContribution(layer, sourceVertices, groupVertices);
                            break;
                    }
                }

                if (group.BlendShapeOutput == BlendShapeOutputMode.OutputAsBlendShape)
                {
                    var deltas = new Vector3[vertexCount];
                    bool hasDelta = false;
                    for (int v = 0; v < vertexCount; v++)
                    {
                        deltas[v] = groupVertices[v] - sourceVertices[v];
                        if (!hasDelta && deltas[v].sqrMagnitude > 1e-10f)
                            hasDelta = true;
                    }
                    if (hasDelta)
                        blendShapeGroups.Add((group, deltas));
                }
                else
                {
                    for (int v = 0; v < vertexCount; v++)
                        directDeltas[v] += groupVertices[v] - sourceVertices[v];
                }
            }

            // Apply direct deltas
            var finalVertices = new Vector3[vertexCount];
            for (int v = 0; v < vertexCount; v++)
                finalVertices[v] = sourceVertices[v] + directDeltas[v];

            // Handle BlendShape output
            if (blendShapeGroups.Count > 0)
            {
                int blendShapeHash = ComputeBlendShapeOutputHash(blendShapeGroups);
                if (blendShapeHash != _lastBlendShapeHash)
                {
                    UnityEngine.Profiling.Profiler.BeginSample("LatticeDeformer.RebuildBlendShapes");
                    _lastBlendShapeHash = blendShapeHash;

                    mesh.ClearBlendShapes();
                    CopyBlendShapes(_sourceMesh, mesh);

                    const int sampleCount = 100;
                    foreach (var (group, deltas) in blendShapeGroups)
                    {
                        string shapeName = group.EffectiveBlendShapeName(gameObject.name);
                        var curve = group.BlendShapeCurve;

                        for (int f = 0; f < sampleCount; f++)
                        {
                            float t = (f + 1f) / sampleCount;
                            float frameWeight = t * 100f;
                            float curveValue = curve.Evaluate(t);

                            var frameDeltas = new Vector3[vertexCount];
                            for (int v = 0; v < vertexCount; v++)
                                frameDeltas[v] = deltas[v] * curveValue;
                            mesh.AddBlendShapeFrame(shapeName, frameWeight, frameDeltas, null, null);
                        }
                    }
                    UnityEngine.Profiling.Profiler.EndSample();
                }
            }
            else
            {
                // No BlendShape groups — clear any previously generated BlendShapes
                if (_lastBlendShapeHash != 0)
                {
                    mesh.ClearBlendShapes();
                    CopyBlendShapes(_sourceMesh, mesh);
                    _lastBlendShapeHash = 0;
                }
            }

            mesh.vertices = finalVertices;

            if (_recalculateNormals) mesh.RecalculateNormals();
            if (_recalculateTangents) mesh.RecalculateTangents();
            if (_recalculateBounds) mesh.RecalculateBounds();

            mesh.UploadMeshData(false);

            if (assignToRenderer)
                AssignRuntimeMesh(mesh);

            UnityEngine.Profiling.Profiler.EndSample();
            return mesh;
        }

        private void EnsureLayerModelReady()
        {
            EnsureSettings();
            if (_layers == null) _layers = new List<LatticeLayer>();
            if (_groups == null) _groups = new List<DeformerGroup>();

            TryMigrateLegacyBaseToLayerStructure();
            TryMigrateLayersToGroupStructure();
            EnsureGroups();
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
            var mask = layer.VertexMask;
            bool hasMask = mask != null && mask.Length == sourceVertices.Length;
            for (int vertex = 0; vertex < deformedVertices.Length; vertex++)
            {
                float maskValue = hasMask ? mask[vertex] : 1f;
                deformedVertices[vertex] += displacements[vertex] * weight * maskValue;
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

        private int ComputeBlendShapeOutputHash(List<(DeformerGroup group, Vector3[] deltas)> blendShapeGroups)
        {
            int hash = 17;
            foreach (var (group, deltas) in blendShapeGroups)
            {
                hash = hash * 31 + (group.EffectiveBlendShapeName(gameObject.name) ?? "").GetHashCode();
                var curve = group.BlendShapeCurve;
                for (int i = 0; i <= 4; i++)
                    hash = hash * 31 + curve.Evaluate(i * 0.25f).GetHashCode();

                int step = Mathf.Max(1, deltas.Length / 32);
                for (int v = 0; v < deltas.Length; v += step)
                    hash = hash * 31 + deltas[v].GetHashCode();
            }
            return hash;
        }

        private static void CopyBlendShapes(Mesh source, Mesh destination)
        {
            int shapeCount = source.blendShapeCount;
            int vertexCount = source.vertexCount;
            for (int s = 0; s < shapeCount; s++)
            {
                string name = source.GetBlendShapeName(s);
                int frameCount = source.GetBlendShapeFrameCount(s);
                for (int f = 0; f < frameCount; f++)
                {
                    float weight = source.GetBlendShapeFrameWeight(s, f);
                    var dv = new Vector3[vertexCount];
                    var dn = new Vector3[vertexCount];
                    var dt = new Vector3[vertexCount];
                    source.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                    destination.AddBlendShapeFrame(name, weight, dv, dn, dt);
                }
            }
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
            _lastBlendShapeHash = 0;
        }

        public void InitializeFromSource(bool resetControlPoints)
        {
            EnsureSettings();
            EnsureGroups();
            if (_sourceMesh == null) return;

            var meshBounds = _sourceMesh.bounds;
            foreach (var group in _groups)
            {
                if (group == null) continue;
                var layers = group.LayersList;
                for (int i = 0; i < layers.Count; i++)
                {
                    if (layers[i] == null) layers[i] = new LatticeLayer();
                    var layerSettings = layers[i].Settings;
                    if (layerSettings == null) continue;

                    layerSettings.LocalBounds = meshBounds;
                    if (resetControlPoints) layerSettings.ResetControlPoints();

                    if (layers[i].Type == MeshDeformerLayerType.Brush)
                    {
                        layers[i].EnsureBrushDisplacementCapacity(_sourceMesh.vertexCount);
                        if (resetControlPoints) layers[i].ClearBrushDisplacements();
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
                    UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
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

        /// <summary>
        /// v2→v3 migration: moves flat _layers + component-level BlendShape settings into a single group.
        /// </summary>
        private bool TryMigrateLayersToGroupStructure()
        {
            if (_groups == null) _groups = new List<DeformerGroup>();
            if (_groups.Count > 0) return false; // Already migrated
            if (_layers == null || _layers.Count == 0)
            {
                if (_layerModelVersion >= 3) return false;
                // New component or empty — will be handled by EnsureGroups default creation
                _layerModelVersion = k_CurrentLayerModelVersion;
                return false;
            }

            // Migrate: wrap existing _layers into a single group
            var group = new DeformerGroup();
            group.Name = "Group";
            foreach (var layer in _layers)
            {
                if (layer != null) group.LayersList.Add(layer);
            }
            group.ActiveLayerIndex = _activeLayerIndex;
            group.BlendShapeOutput = _blendShapeOutput;
            group.BlendShapeName = _blendShapeName ?? "";
            group.BlendShapeCurve = _blendShapeCurve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);

            _groups.Add(group);
            _activeGroupIndex = 0;
            _layers.Clear(); // Keep the field but empty it
            _layerModelVersion = k_CurrentLayerModelVersion;

            InvalidateCache();

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(this);
                if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(this))
                    UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
            }
#endif
            return true;
        }

        private void EnsureGroups()
        {
            if (_groups == null) _groups = new List<DeformerGroup>();

            // Create default group if none exist
            if (_groups.Count == 0)
            {
                var defaultGroup = new DeformerGroup();
                defaultGroup.Name = "Group";
                defaultGroup.LayersList.Add(new LatticeLayer
                {
                    Name = k_PrimaryLayerName,
                    Enabled = true,
                    Weight = 1f,
                });
                _groups.Add(defaultGroup);
                _activeGroupIndex = 0;
            }

            // Ensure each group's layers are valid
            foreach (var group in _groups)
            {
                if (group == null) continue;
                var layers = group.LayersList;
                for (int i = 0; i < layers.Count; i++)
                {
                    if (layers[i] == null) layers[i] = new LatticeLayer();
                    var layer = layers[i];
                    _ = layer.Settings;
                    if (string.IsNullOrWhiteSpace(layer.Name))
                        layer.Name = layer.Type == MeshDeformerLayerType.Brush ? k_BrushLayerName : k_PrimaryLayerName;
                }
            }

            _activeGroupIndex = _groups.Count > 0 ? Mathf.Clamp(_activeGroupIndex, 0, _groups.Count - 1) : 0;

            if (_sourceMesh != null)
                EnsureAllBrushLayerDisplacementCapacity(_sourceMesh.vertexCount);
        }

        // Legacy compat — still called from EnsureLayerModelReady before group migration
        private void EnsureLayers()
        {
            EnsureGroups();
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
            EnsureAllBrushLayerDisplacementCapacity(_sourceMesh != null ? _sourceMesh.vertexCount : 0);
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
            // This handles v0→v2 (flat _settings → _layers). Skip if already at v2+.
            if (_layerModelVersion >= 2)
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
            _layerModelVersion = 2; // v0→v2 done; TryMigrateLayersToGroupStructure handles v2→v3

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
            var group = ActiveGroup;
            if (group != null)
            {
                var layers = group.LayersList;
                if (layers.Count > 0)
                {
                    var layer = layers[0] ?? (layers[0] = new LatticeLayer());
                    return layer.Settings;
                }
            }
            return _settings;
        }

        private bool TryGetActiveLayer(out LatticeLayer layer)
        {
            layer = null;
            var group = ActiveGroup;
            if (group == null) return false;
            var layers = group.LayersList;
            int idx = group.ActiveLayerIndex;
            if (idx < 0 || idx >= layers.Count) return false;
            layer = layers[idx];
            return layer != null;
        }

        private bool TryGetLayerInActiveGroup(int index, out LatticeLayer layer)
        {
            layer = null;
            var group = ActiveGroup;
            if (group == null) return false;
            var layers = group.LayersList;
            if (index < 0 || index >= layers.Count) return false;
            layer = layers[index];
            return layer != null;
        }

        private string GenerateNextLayerName(MeshDeformerLayerType layerType)
        {
            var group = ActiveGroup;
            var layers = group?.LayersList ?? new List<LatticeLayer>();
            string baseName = layerType == MeshDeformerLayerType.Brush ? k_BrushLayerName : k_PrimaryLayerName;

            bool baseNameExists = false;
            for (int i = 0; i < layers.Count; i++)
                if (layers[i] != null && string.Equals(layers[i].Name, baseName, StringComparison.OrdinalIgnoreCase))
                { baseNameExists = true; break; }

            if (!baseNameExists) return baseName;

            int number = 1;
            while (true)
            {
                string candidate = $"{baseName} {number}";
                bool exists = false;
                for (int i = 0; i < layers.Count; i++)
                    if (layers[i] != null && string.Equals(layers[i].Name, candidate, StringComparison.OrdinalIgnoreCase))
                    { exists = true; break; }
                if (!exists) return candidate;
                number++;
            }
        }

        private string GenerateNextGroupName()
        {
            string baseName = "Group";
            bool baseExists = false;
            for (int i = 0; i < _groups.Count; i++)
                if (_groups[i] != null && string.Equals(_groups[i].Name, baseName, StringComparison.OrdinalIgnoreCase))
                { baseExists = true; break; }
            if (!baseExists) return baseName;

            int number = 1;
            while (true)
            {
                string candidate = $"{baseName} {number}";
                bool exists = false;
                for (int i = 0; i < _groups.Count; i++)
                    if (_groups[i] != null && string.Equals(_groups[i].Name, candidate, StringComparison.OrdinalIgnoreCase))
                    { exists = true; break; }
                if (!exists) return candidate;
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

        private static int HashMaskState(float[] mask)
        {
            if (mask == null || mask.Length == 0)
            {
                return 0;
            }

            int hash = 31;
            for (int i = 0; i < mask.Length; i++)
            {
                hash = HashCode.Combine(hash, mask[i]);
            }

            hash = HashCode.Combine(hash, mask.Length);
            return hash;
        }

        private void EnsureAllBrushLayerDisplacementCapacity(int vertexCount)
        {
            if (_groups == null) return;
            foreach (var group in _groups)
            {
                if (group == null) continue;
                foreach (var layer in group.LayersList)
                {
                    if (layer != null && layer.Type == MeshDeformerLayerType.Brush)
                        layer.EnsureBrushDisplacementCapacity(vertexCount);
                }
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
