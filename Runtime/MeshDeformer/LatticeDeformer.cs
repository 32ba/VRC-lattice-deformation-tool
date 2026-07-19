using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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

    /// <summary>
    /// Published deformation-data schemas in release order. Every value is retained in
    /// the migration dispatcher even when that release did not change serialized data,
    /// so an upgrade can be audited and resumed one published release at a time.
    /// </summary>
    public enum DeformationDataVersion
    {
        Unversioned = 0,
        V0_0_1 = 1,
        V0_0_2 = 2,
        V0_0_3 = 3,
        V0_0_4 = 4,
        V0_0_5 = 5,
        V0_0_6 = 6,
        V1_0_0 = 7,
        V1_0_1 = 8,
        V1_1_0 = 9,
        V1_2_0 = 10,
        V1_2_1 = 11,
        V1_3_0 = 12,
        V1_3_1 = 13,
        V1_4_0 = 14,
        CurrentDevelopment = 15
    }

    internal enum DeformationDataMigrationStatus
    {
        Uninitialized = 0,
        Ready = 1,
        InProgress = 2,
        PendingOwnerTransform = 3,
        InvalidData = 4,
        UnsupportedFutureVersion = 5
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
        [SerializeField] private AnimationCurve _blendShapeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

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

        public AnimationCurve BlendShapeCurve
        {
            get => _blendShapeCurve ?? (_blendShapeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f));
            set => _blendShapeCurve = value ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);
        }

        public string EffectiveBlendShapeName => string.IsNullOrWhiteSpace(_blendShapeName) ? Name : _blendShapeName;

        public Vector3[] BrushDisplacements
        {
            get => _brushDisplacements ?? (_brushDisplacements = Array.Empty<Vector3>());
            set => _brushDisplacements = value ?? Array.Empty<Vector3>();
        }

        public int BrushDisplacementCount => _brushDisplacements?.Length ?? 0;

        internal LatticeAsset SerializedSettings => _settings;

        internal int SerializedBrushDisplacementCount => _brushDisplacements?.Length ?? 0;

        internal int SerializedVertexMaskCount => _vertexMask?.Length ?? 0;

        internal bool HasMalformedSerializedMetadata =>
            (_type != MeshDeformerLayerType.Lattice && _type != MeshDeformerLayerType.Brush) ||
            (_blendShapeOutput != BlendShapeOutputMode.Disabled &&
             _blendShapeOutput != BlendShapeOutputMode.OutputAsBlendShape) ||
            float.IsNaN(_weight) || float.IsInfinity(_weight);

        internal bool HasNonFiniteSerializedVertexData
        {
            get
            {
                if (_brushDisplacements != null)
                {
                    for (int i = 0; i < _brushDisplacements.Length; i++)
                    {
                        Vector3 value = _brushDisplacements[i];
                        if (float.IsNaN(value.x) || float.IsInfinity(value.x) ||
                            float.IsNaN(value.y) || float.IsInfinity(value.y) ||
                            float.IsNaN(value.z) || float.IsInfinity(value.z))
                        {
                            return true;
                        }
                    }
                }

                if (_vertexMask != null)
                {
                    for (int i = 0; i < _vertexMask.Length; i++)
                    {
                        if (float.IsNaN(_vertexMask[i]) || float.IsInfinity(_vertexMask[i]))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }

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

        internal bool TryEnsureBrushDataCapacityPreservingExisting(int vertexCount)
        {
            vertexCount = Mathf.Max(0, vertexCount);

            // Validate every existing payload before allocating either one. Failure is
            // intentionally mutation-free so historical data can still be recovered.
            if (_vertexMask != null && _vertexMask.Length != 0 && _vertexMask.Length != vertexCount)
            {
                return false;
            }

            if (_brushDisplacements == null || _brushDisplacements.Length == 0)
            {
                _brushDisplacements = new Vector3[vertexCount];
            }
            else if (_brushDisplacements.Length != vertexCount)
            {
                return false;
            }

            // An empty mask means fully editable and does not require allocation.
            return true;
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

        internal List<LatticeLayer> SerializedLayers => _layers;

        internal int SerializedActiveLayerIndex => _activeLayerIndex;

        internal void SetSerializedActiveLayerIndex(int value) => _activeLayerIndex = value;

        internal bool HasMalformedSerializedMetadata =>
            _blendShapeOutput != BlendShapeOutputMode.Disabled &&
            _blendShapeOutput != BlendShapeOutputMode.OutputAsBlendShape;

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

        [SerializeField, HideInInspector]
        private DeformationDataVersion _deformationDataVersion = DeformationDataVersion.Unversioned;

        [SerializeField, HideInInspector]
        private DeformationDataVersion _deformationDataSourceVersion = DeformationDataVersion.Unversioned;

        // Historical releases evaluated interpolated absolute control points. Current
        // data evaluates a neutral-relative offset field. Existing data keeps the former
        // behavior so Bounds-external vertices remain byte-for-byte compatible.
        [SerializeField, HideInInspector]
        private bool _legacyAbsoluteLatticeEvaluation;

        // Published group releases ignored layer-level output fields and wrote generated
        // group frames without normal/tangent deltas. Preserve that output contract
        // without discarding latent metadata; newly-authored current data stays current.
        [SerializeField, HideInInspector]
        private bool _legacyPublishedBlendShapeSemantics;

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
        [NonSerialized] private int _lastBakedBlendShapeHash;
        [NonSerialized] private bool _blendShapeOutputDirty = true;
        [NonSerialized] private bool _isEnsuringLayerModelReady;
        [NonSerialized] private bool _hasIncompatibleBrushData;
        [NonSerialized] private DeformationDataMigrationStatus _migrationStatus =
            DeformationDataMigrationStatus.Uninitialized;
        private const int k_CurrentLayerModelVersion = 3;
        private const string k_PrimaryLayerName = "Lattice Layer";
        private const string k_BrushLayerName = "Brush Layer";
        private const string k_RecoveredLegacyFlatLayersGroupName = "Recovered Legacy Flat Layers";

        private Vector3[] _controlBuffer = Array.Empty<Vector3>();

        internal static DeformationDataVersion CurrentDeformationDataVersion =>
            DeformationDataVersion.CurrentDevelopment;

        internal DeformationDataVersion SerializedDeformationDataVersion => _deformationDataVersion;

        internal DeformationDataVersion SourceDeformationDataVersion =>
            _deformationDataSourceVersion == DeformationDataVersion.Unversioned
                ? _deformationDataVersion
                : _deformationDataSourceVersion;

        internal DeformationDataMigrationStatus MigrationStatus => _migrationStatus;

        internal bool UsesLegacyAbsoluteLatticeEvaluation => _legacyAbsoluteLatticeEvaluation;

        private readonly struct GeneratedBlendShape
        {
            public readonly string Name;
            public readonly AnimationCurve Curve;
            public readonly Vector3[] Deltas;

            public GeneratedBlendShape(string name, AnimationCurve curve, Vector3[] deltas)
            {
                Name = name;
                Curve = curve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);
                Deltas = deltas;
            }
        }

        private readonly struct GroupSelectionSnapshot
        {
            public readonly DeformerGroup Group;
            public readonly int ActiveLayerIndex;

            public GroupSelectionSnapshot(DeformerGroup group, int activeLayerIndex)
            {
                Group = group;
                ActiveLayerIndex = activeLayerIndex;
            }
        }

        private readonly struct LatticeInterpolationCompatibilitySnapshot
        {
            public readonly LatticeAsset Asset;
            public readonly bool UsedLegacyTrilinearInterpolation;

            public LatticeInterpolationCompatibilitySnapshot(
                LatticeAsset asset,
                bool usedLegacyTrilinearInterpolation)
            {
                Asset = asset;
                UsedLegacyTrilinearInterpolation = usedLegacyTrilinearInterpolation;
            }
        }

        /// <summary>
        /// Base layer settings (legacy). Delegates to the first layer of the active group.
        /// </summary>
        public LatticeAsset Settings
        {
            get => EnsureGroups() ? GetPrimaryLayerSettings() : null;
            set
            {
                if (!EnsureGroups()) return;
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
                if (!EnsureGroups()) return Array.Empty<DeformerGroup>();
                return _groups;
            }
        }

        public int GroupCount
        {
            get
            {
                return EnsureGroups() && _groups != null ? _groups.Count : 0;
            }
        }

        public int ActiveGroupIndex
        {
            get
            {
                return EnsureGroups() ? _activeGroupIndex : 0;
            }
            set
            {
                if (!EnsureGroups()) return;
                _activeGroupIndex = _groups.Count > 0 ? Mathf.Clamp(value, 0, _groups.Count - 1) : 0;
            }
        }

        public DeformerGroup ActiveGroup
        {
            get
            {
                if (!EnsureGroups() || _groups == null || _groups.Count == 0) return null;
                return _groups[Mathf.Clamp(_activeGroupIndex, 0, _groups.Count - 1)];
            }
        }

        public int AddGroup(string groupName = null)
        {
            if (!EnsureGroups()) return -1;
            var group = new DeformerGroup();
            group.Name = string.IsNullOrWhiteSpace(groupName) ? GenerateNextGroupName() : groupName;
            _groups.Add(group);
            _activeGroupIndex = _groups.Count - 1;
            return _activeGroupIndex;
        }

        public bool RemoveGroup(int index)
        {
            if (!EnsureGroups()) return false;
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
                if (!EnsureGroups())
                {
                    // Keep authoritative invalid payloads inspectable so an explicit
                    // editor action can repair them. This is a recovery view only:
                    // Deform and all mutating facade operations still fail closed.
                    if (_migrationStatus == DeformationDataMigrationStatus.InvalidData &&
                        HasNonNullLayers(_layers))
                    {
                        return _layers;
                    }
                    if (_migrationStatus == DeformationDataMigrationStatus.InvalidData &&
                        _groups != null &&
                        _activeGroupIndex >= 0 &&
                        _activeGroupIndex < _groups.Count)
                    {
                        var recoveryGroup = _groups[_activeGroupIndex];
                        if (recoveryGroup?.SerializedLayers != null)
                            return recoveryGroup.SerializedLayers;
#line hidden
                    }
#line default

                    return Array.Empty<LatticeLayer>();
                }
                var group = ActiveGroup;
                return group != null ? group.Layers : (IReadOnlyList<LatticeLayer>)Array.Empty<LatticeLayer>();
            }
        }

        public int ActiveLayerIndex
        {
            get
            {
                if (!EnsureGroups()) return 0;
                var group = ActiveGroup;
                return group?.ActiveLayerIndex ?? 0;
            }
            set
            {
                if (!EnsureGroups()) return;
                var group = ActiveGroup;
                if (group != null) group.ActiveLayerIndex = value;
            }
        }

        public bool IsEditingBaseLayer => false;

        public LatticeAsset EditingSettings
        {
            get
            {
                if (!EnsureGroups()) return null;
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
                if (!EnsureGroups()) return MeshDeformerLayerType.Lattice;
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
                if (!EnsureGroups()) return Array.Empty<Vector3>();
                if (!TryGetActiveLayer(out var layer) || layer.Type != MeshDeformerLayerType.Brush)
                    return Array.Empty<Vector3>();
                return layer.BrushDisplacements;
            }
        }

        public int DisplacementCount => Displacements.Length;

        public bool HasDisplacements()
        {
            if (!EnsureGroups()) return false;
            if (!TryGetActiveLayer(out var layer) || layer.Type != MeshDeformerLayerType.Brush)
                return false;
            return layer.HasBrushDisplacements();
        }

        public void EnsureDisplacementCapacity()
        {
            if (!EnsureGroups()) return;
            CacheSourceMesh();
            if (_sourceMesh == null) return;
            if (TryGetActiveLayer(out var layer) && layer.Type == MeshDeformerLayerType.Brush)
            {
                _hasIncompatibleBrushData =
                    !layer.TryEnsureBrushDataCapacityPreservingExisting(_sourceMesh.vertexCount);
                // EnsureLayerModelReady rejects this payload before public mutation APIs run.
#line hidden
                if (_hasIncompatibleBrushData)
                {
                    _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                }
#line default
            }
        }

        public void SetDisplacement(int index, Vector3 displacement)
        {
            if (!EnsureGroups()) return;
            if (TryGetActiveLayer(out var layer) && layer.Type == MeshDeformerLayerType.Brush)
                layer.SetBrushDisplacement(index, displacement);
        }

        public void AddDisplacement(int index, Vector3 delta)
        {
            if (!EnsureGroups()) return;
            if (TryGetActiveLayer(out var layer) && layer.Type == MeshDeformerLayerType.Brush)
                layer.AddBrushDisplacement(index, delta);
        }

        public Vector3 GetDisplacement(int index)
        {
            if (!EnsureGroups()) return Vector3.zero;
            if (TryGetActiveLayer(out var layer) && layer.Type == MeshDeformerLayerType.Brush)
                return layer.GetBrushDisplacement(index);
            return Vector3.zero;
        }

        public void ClearDisplacements()
        {
            if (!EnsureGroups()) return;
            if (TryGetActiveLayer(out var layer) && layer.Type == MeshDeformerLayerType.Brush)
                layer.ClearBrushDisplacements();
        }

        // ── Layer management (operates on ActiveGroup) ──────────────

        public int AddLayer(string layerName = null, MeshDeformerLayerType layerType = MeshDeformerLayerType.Lattice)
        {
            if (!EnsureGroups()) return -1;
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
            if (!EnsureGroups()) return -1;
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
                Settings = CloneSettings(sourceLayer.Settings),
                BlendShapeOutput = sourceLayer.BlendShapeOutput,
                BlendShapeName = sourceLayer.BlendShapeName,
                BlendShapeCurve = CloneCurve(sourceLayer.BlendShapeCurve)
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
            if (!EnsureGroups()) return -1;
            var group = ActiveGroup;
            if (group == null) return -1;
            var layers = group.LayersList;
            layers.Add(layer);
            group.ActiveLayerIndex = layers.Count - 1;
            return group.ActiveLayerIndex;
        }

        public bool RemoveLayer(int index)
        {
            if (!EnsureGroups()) return false;
            var group = ActiveGroup;
            if (group == null) return false;
            var layers = group.LayersList;
            if (index < 0 || index >= layers.Count || layers.Count <= 1)
                return false;

            // Capture the raw selection before shrinking the list. The public getter
            // clamps against the current count, so reading it after RemoveAt would hide
            // a just-removed last index and leave the serialized value dangling.
            int active = group.SerializedActiveLayerIndex;
            layers.RemoveAt(index);
            if (active == index)
                group.ActiveLayerIndex = Mathf.Min(index, layers.Count - 1);
            else if (active > index)
                group.ActiveLayerIndex = active - 1;
            return true;
        }

        public bool MoveLayer(int index, int targetIndex)
        {
            if (!EnsureGroups()) return false;
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

            if (!EnsureGroups()) return -1;
            var group = ActiveGroup;
            if (group == null) return -1;
            group.LayersList.Add(layer);
            int addedIndex = group.LayersList.Count - 1;
            group.ActiveLayerIndex = addedIndex;
            return addedIndex;
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
            if (!EnsureGroups()) return;
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

                var vertices = _sourceMesh.vertices;
                // Central serialized-payload validation rejects this state first.
#line hidden
                if (!layer.TryEnsureBrushDataCapacityPreservingExisting(vertices.Length))
                {
                    _hasIncompatibleBrushData = true;
                    _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                    return;
                }
#line default
                var displacements = layer.BrushDisplacements;

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
            if (!EnsureGroups()) return;
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

                var vertices = _sourceMesh.vertices;
#line hidden
                if (!layer.TryEnsureBrushDataCapacityPreservingExisting(vertices.Length))
                {
                    _hasIncompatibleBrushData = true;
                    _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                    return;
                }
#line default
                var displacements = layer.BrushDisplacements;

                int vertexCount = vertices.Length;
                var mirrorMap = BuildBrushMirrorMap(vertices, axis);
                var newDisplacements = new Vector3[vertexCount];
                var masks = layer.VertexMask;
                bool hasMask = masks.Length == vertexCount;
                var newMasks = hasMask ? new float[vertexCount] : null;

                for (int i = 0; i < vertexCount; i++)
                {
                    var displacement = displacements[mirrorMap[i]];
                    if (axis == 0) displacement.x = -displacement.x;
                    else if (axis == 1) displacement.y = -displacement.y;
                    else displacement.z = -displacement.z;

                    newDisplacements[i] = displacement;
                    if (hasMask)
                    {
                        newMasks[i] = masks[mirrorMap[i]];
                    }
                }

                layer.BrushDisplacements = newDisplacements;
                if (hasMask)
                {
                    layer.VertexMask = newMasks;
                }
            }
            else // Lattice
            {
                var settings = layer.Settings;
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

        private static int[] BuildBrushMirrorMap(Vector3[] vertices, int axis)
        {
            int vertexCount = vertices?.Length ?? 0;
            var mirrorMap = new int[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                mirrorMap[i] = -1;
            }

            const float tolerance = 0.001f;
            const float toleranceSq = tolerance * tolerance;

            for (int i = 0; i < vertexCount; i++)
            {
                if (mirrorMap[i] >= 0)
                {
                    continue;
                }

                var position = vertices[i];
                float axisPosition = axis == 0 ? position.x : axis == 1 ? position.y : position.z;
                if (Mathf.Abs(axisPosition) <= tolerance)
                {
                    mirrorMap[i] = i;
                    continue;
                }

                var mirroredPosition = position;
                if (axis == 0) mirroredPosition.x = -mirroredPosition.x;
                else if (axis == 1) mirroredPosition.y = -mirroredPosition.y;
                else mirroredPosition.z = -mirroredPosition.z;

                int bestIndex = -1;
                float bestDistanceSq = float.MaxValue;
                for (int j = i + 1; j < vertexCount; j++)
                {
                    if (mirrorMap[j] >= 0)
                    {
                        continue;
                    }

                    var candidate = vertices[j];
                    float candidateAxisPosition = axis == 0 ? candidate.x : axis == 1 ? candidate.y : candidate.z;
                    if (Mathf.Abs(candidateAxisPosition) <= tolerance ||
                        (axisPosition > 0f) == (candidateAxisPosition > 0f))
                    {
                        continue;
                    }

                    float distanceSq = (candidate - mirroredPosition).sqrMagnitude;
                    if (distanceSq > toleranceSq)
                    {
                        continue;
                    }

                    if (bestIndex < 0 || distanceSq < bestDistanceSq ||
                        (distanceSq == bestDistanceSq && j < bestIndex))
                    {
                        bestIndex = j;
                        bestDistanceSq = distanceSq;
                    }
                }

                if (bestIndex < 0)
                {
                    mirrorMap[i] = i;
                    continue;
                }

                mirrorMap[i] = bestIndex;
                mirrorMap[bestIndex] = i;
            }

            return mirrorMap;
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
            if (!EnsureGroups()) return false;
            return TryGetLayerInActiveGroup(index, out _);
        }

        public void SyncLayerStructuresToBase(bool resetControlPoints)
        {
            // Base layer concept was removed in 1.3.0.
            // Kept as a no-op for backward compatibility.
        }

        public int ComputeLayeredStateHash()
        {
            if (!EnsureGroups()) return 0;

            int hash = 17;
            hash = HashCode.Combine(hash, _legacyAbsoluteLatticeEvaluation);
            hash = HashCode.Combine(hash, _legacyPublishedBlendShapeSemantics);
            hash = HashCode.Combine(hash, (int)_deformationDataVersion);
            hash = HashCode.Combine(hash, _groups.Count);
            hash = HashCode.Combine(hash, _activeGroupIndex);
            hash = HashCode.Combine(hash, (gameObject.name ?? "").GetHashCode());
            hash = HashCode.Combine(hash, _recalculateNormals);
            hash = HashCode.Combine(hash, _recalculateTangents);
            hash = HashCode.Combine(hash, _recalculateBounds);

            foreach (var group in _groups)
            {
                if (group == null) { hash = HashCode.Combine(hash, 0); continue; }
                hash = HashCode.Combine(hash, (group.Name ?? "").GetHashCode());
                hash = HashCode.Combine(hash, group.Enabled);
                hash = HashCode.Combine(hash, (int)group.BlendShapeOutput);
                hash = HashCode.Combine(hash, (group.BlendShapeName ?? "").GetHashCode());
                hash = HashCode.Combine(hash, HashCurveState(group.BlendShapeCurve));

                var layers = group.LayersList;
                hash = HashCode.Combine(hash, layers.Count);
                hash = HashCode.Combine(hash, group.ActiveLayerIndex);

                foreach (var layer in layers)
                {
                    if (layer == null) { hash = HashCode.Combine(hash, 0); continue; }
                    hash = HashCode.Combine(hash, (layer.Name ?? "").GetHashCode());
                    hash = HashCode.Combine(hash, layer.Enabled);
                    hash = HashCode.Combine(hash, layer.Weight);
                    hash = HashCode.Combine(hash, (int)layer.Type);
                    hash = HashCode.Combine(hash, (int)layer.BlendShapeOutput);
                    hash = HashCode.Combine(hash, (layer.BlendShapeName ?? "").GetHashCode());
                    hash = HashCode.Combine(hash, HashCurveState(layer.BlendShapeCurve));
                    switch (layer.Type)
                    {
                        case MeshDeformerLayerType.Brush:
                            hash = HashCode.Combine(hash, HashDisplacementState(layer.BrushDisplacements));
                            hash = HashCode.Combine(hash, HashMaskState(layer.VertexMask));
                            break;
                        default:
                            var layerSettings = layer.Settings;
                            hash = HashCode.Combine(hash, HashAssetState(layerSettings));
                            if (layerSettings.HasPendingLegacyWorldSpace)
                            {
                                Transform owner = MeshTransform;
                                hash = HashCode.Combine(
                                    hash,
                                    owner != null ? HashMatrix(owner.worldToLocalMatrix) : 0);
                            }
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
            int rawVersion = (int)_deformationDataVersion;
            if (rawVersion > (int)DeformationDataVersion.CurrentDevelopment)
            {
                _migrationStatus = DeformationDataMigrationStatus.UnsupportedFutureVersion;
                return;
            }

            if (_layerModelVersion > k_CurrentLayerModelVersion)
            {
                _migrationStatus = DeformationDataMigrationStatus.UnsupportedFutureVersion;
                return;
            }

            if (rawVersion < (int)DeformationDataVersion.Unversioned)
            {
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return;
            }

            if (_skinnedMeshRenderer == null)
            {
                _skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>();
            }

            if (_meshFilter == null)
            {
                _meshFilter = GetComponent<MeshFilter>();
            }

            if (!EnsureLayerModelReady())
            {
                return;
            }

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

        [ExcludeFromCodeCoverage]
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

        private void OnDestroy()
        {
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
            if (!EnsureLayerModelReady())
            {
                UnityEngine.Profiling.Profiler.EndSample();
                return null;
            }

            if (_sourceMesh == null)
            {
                UnityEngine.Profiling.Profiler.EndSample();
                return null;
            }

            // EnsureLayerModelReady has already performed the same fail-closed check.
#line hidden
            if (!EnsureAllBrushLayerDisplacementCapacity(_sourceMesh.vertexCount))
            {
                UnityEngine.Profiling.Profiler.EndSample();
                return null;
            }
#line default

            var sourceVertices = BuildCurrentSourceVertices(
                out var bakedBlendShapeDeltas,
                out var bakedBlendShapeWeights,
                out var bakedBlendShapeHash);
            if (sourceVertices == null || sourceVertices.Length == 0)
            {
                UnityEngine.Profiling.Profiler.EndSample();
                return null;
            }

            int vertexCount = sourceVertices.Length;
            // BuildCurrentSourceVertices preserves the source vertex count.
#line hidden
            if (!EnsureAllBrushLayerDisplacementCapacity(vertexCount))
            {
                UnityEngine.Profiling.Profiler.EndSample();
                return null;
            }
#line default

            // Do not instantiate or assign a runtime mesh until every serialized
            // vertex-indexed payload has passed compatibility validation.
            var mesh = AcquireRuntimeMesh(assignToRenderer);
            // A validated non-null source always yields an instantiated runtime mesh.
#line hidden
            if (mesh == null)
            {
                UnityEngine.Profiling.Profiler.EndSample();
                return null;
            }
#line default

            // Accumulate direct-deform deltas across all groups
            var directDeltas = new Vector3[vertexCount];
            // Collect generated BlendShapes from groups and individual layers.
            var generatedBlendShapes = new List<GeneratedBlendShape>();

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

                    if (!_legacyPublishedBlendShapeSemantics &&
                        layer.BlendShapeOutput == BlendShapeOutputMode.OutputAsBlendShape)
                    {
                        var layerVertices = (Vector3[])sourceVertices.Clone();
                        TryApplyLayerContribution(layer, sourceVertices, layerVertices);
                        if (TryBuildDeltas(sourceVertices, layerVertices, out var layerDeltas))
                        {
                            generatedBlendShapes.Add(new GeneratedBlendShape(
                                layer.EffectiveBlendShapeName,
                                layer.BlendShapeCurve,
                                layerDeltas));
                        }

                        continue;
                    }

                    TryApplyLayerContribution(layer, sourceVertices, groupVertices);
                }

                if (group.BlendShapeOutput == BlendShapeOutputMode.OutputAsBlendShape)
                {
                    if (TryBuildDeltas(sourceVertices, groupVertices, out var groupDeltas))
                    {
                        generatedBlendShapes.Add(new GeneratedBlendShape(
                            group.EffectiveBlendShapeName(gameObject.name),
                            group.BlendShapeCurve,
                            groupDeltas));
                    }
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
            if (generatedBlendShapes.Count > 0)
            {
                int blendShapeHash = HashCode.Combine(
                    ComputeBlendShapeOutputHash(generatedBlendShapes),
                    HashVertices(finalVertices),
                    bakedBlendShapeHash,
                    _recalculateNormals,
                    _recalculateTangents,
                    _legacyPublishedBlendShapeSemantics);
                if (_blendShapeOutputDirty || blendShapeHash != _lastBlendShapeHash)
                {
                    UnityEngine.Profiling.Profiler.BeginSample("LatticeDeformer.RebuildBlendShapes");
                    _lastBlendShapeHash = blendShapeHash;

                    mesh.ClearBlendShapes();
                    CopyBlendShapes(_sourceMesh, mesh, bakedBlendShapeDeltas, bakedBlendShapeWeights);

                    var usedNames = CollectBlendShapeNames(mesh);
                    foreach (var generated in generatedBlendShapes)
                    {
                        string shapeName = MakeUniqueBlendShapeName(generated.Name, usedNames);
                        AddGeneratedBlendShapeFrames(mesh, shapeName, finalVertices, generated.Deltas, generated.Curve);
                    }
                    _blendShapeOutputDirty = false;
                    UnityEngine.Profiling.Profiler.EndSample();
                }
            }
            else
            {
                // No BlendShape groups — clear any previously generated BlendShapes
                if (_blendShapeOutputDirty || _lastBlendShapeHash != 0)
                {
                    mesh.ClearBlendShapes();
                    CopyBlendShapes(_sourceMesh, mesh, bakedBlendShapeDeltas, bakedBlendShapeWeights);
                    _lastBlendShapeHash = 0;
                    _blendShapeOutputDirty = false;
                }
                else if (bakedBlendShapeHash != _lastBakedBlendShapeHash)
                {
                    mesh.ClearBlendShapes();
                    CopyBlendShapes(_sourceMesh, mesh, bakedBlendShapeDeltas, bakedBlendShapeWeights);
                }
            }

            _lastBakedBlendShapeHash = bakedBlendShapeHash;

            mesh.vertices = finalVertices;

            if (_recalculateNormals)
            {
                mesh.RecalculateNormals();
            }
            else
            {
                RestoreSourceNormals(mesh);
            }

            if (_recalculateTangents)
            {
                mesh.RecalculateTangents();
            }
            else
            {
                RestoreSourceTangents(mesh);
            }

            if (_recalculateBounds)
            {
                mesh.RecalculateBounds();
            }
            else
            {
                mesh.bounds = _sourceMesh.bounds;
            }

            mesh.UploadMeshData(false);

            if (assignToRenderer)
                AssignRuntimeMesh(mesh);

            UnityEngine.Profiling.Profiler.EndSample();
            return mesh;
        }

        private void RestoreSourceNormals(Mesh mesh)
        {
            if (mesh == null || _sourceMesh == null)
            {
                return;
            }

            var normals = _sourceMesh.normals;
            mesh.normals = normals != null && normals.Length == mesh.vertexCount
                ? normals
                : Array.Empty<Vector3>();
        }

        private void RestoreSourceTangents(Mesh mesh)
        {
            if (mesh == null || _sourceMesh == null)
            {
                return;
            }

            var tangents = _sourceMesh.tangents;
            mesh.tangents = tangents != null && tangents.Length == mesh.vertexCount
                ? tangents
                : Array.Empty<Vector4>();
        }

        private bool EnsureLayerModelReady()
        {
            if (_isEnsuringLayerModelReady)
            {
                return _deformationDataVersion == DeformationDataVersion.CurrentDevelopment &&
                       !_hasIncompatibleBrushData;
            }

            int rawVersion = (int)_deformationDataVersion;
            if (rawVersion > (int)DeformationDataVersion.CurrentDevelopment)
            {
                _migrationStatus = DeformationDataMigrationStatus.UnsupportedFutureVersion;
                return false;
            }

            if (_layerModelVersion > k_CurrentLayerModelVersion)
            {
                _migrationStatus = DeformationDataMigrationStatus.UnsupportedFutureVersion;
                return false;
            }

            if (rawVersion < (int)DeformationDataVersion.Unversioned)
            {
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return false;
            }

            if (HasUnsupportedFutureLatticeAsset())
            {
                _migrationStatus = DeformationDataMigrationStatus.UnsupportedFutureVersion;
                return false;
            }

            if (HasMalformedLatticeAsset())
            {
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return false;
            }

            if (HasIncompatibleSerializedVertexIndexedData())
            {
                _hasIncompatibleBrushData = true;
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return false;
            }

            _isEnsuringLayerModelReady = true;
            try
            {
                RecoverStaleCurrentStructureVersionIfNeeded();

                while (_deformationDataVersion != DeformationDataVersion.CurrentDevelopment)
                {
                    if (!TryUpgradeDeformationDataOneRelease())
                    {
                        return false;
                    }
                }

                EnsureSettings();
                if (_layers == null) _layers = new List<LatticeLayer>();
                if (_groups == null) _groups = new List<DeformerGroup>();

                EnsureGroupsCore();
                CacheSourceMesh();
                TryAutoConfigureSettings();

                _migrationStatus = _hasIncompatibleBrushData
                    ? DeformationDataMigrationStatus.InvalidData
                    : DeformationDataMigrationStatus.Ready;
                return !_hasIncompatibleBrushData;
            }
            finally
            {
                _isEnsuringLayerModelReady = false;
            }
        }

        private void RecoverStaleCurrentStructureVersionIfNeeded()
        {
            if (_deformationDataVersion != DeformationDataVersion.CurrentDevelopment ||
                _layerModelVersion >= k_CurrentLayerModelVersion ||
                HasNonNullGroups(_groups) ||
                (!HasNonNullLayers(_layers) && !HasMeaningfulBaseSettings()))
            {
                return;
            }

            // A current release marker paired with only an older serialized shape can
            // result from an interrupted save or an Inspector-first partial migration.
            // Recover the older shape instead of creating a default group over it.
            _deformationDataVersion = _settings != null && _settings.HasPendingLegacyWorldSpace
                ? DeformationDataVersion.V0_0_1
                : DeformationDataVersion.V1_2_0;
            _deformationDataSourceVersion = _deformationDataVersion;
            _migrationStatus = DeformationDataMigrationStatus.InProgress;
            MarkMigrationCommitted();
        }

        /// <summary>
        /// Advances exactly one published release boundary. Unversioned data is first
        /// classified by its oldest unambiguous serialized shape; no release-specific
        /// mutation occurs until the following call. A failed step never advances the
        /// version and must leave its source payload intact.
        /// </summary>
        internal bool TryUpgradeDeformationDataOneRelease()
        {
            int rawVersion = (int)_deformationDataVersion;
            if (rawVersion > (int)DeformationDataVersion.CurrentDevelopment)
            {
                _migrationStatus = DeformationDataMigrationStatus.UnsupportedFutureVersion;
                return false;
            }

            if (_layerModelVersion > k_CurrentLayerModelVersion)
            {
                _migrationStatus = DeformationDataMigrationStatus.UnsupportedFutureVersion;
                return false;
            }

            if (rawVersion < (int)DeformationDataVersion.Unversioned)
            {
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return false;
            }

            if (HasUnsupportedFutureLatticeAsset())
            {
                _migrationStatus = DeformationDataMigrationStatus.UnsupportedFutureVersion;
                return false;
            }

            if (HasMalformedLatticeAsset())
            {
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return false;
            }

            if (HasIncompatibleSerializedVertexIndexedData())
            {
                _hasIncompatibleBrushData = true;
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return false;
            }

            if (_deformationDataVersion == DeformationDataVersion.CurrentDevelopment)
            {
                _migrationStatus = _hasIncompatibleBrushData
                    ? DeformationDataMigrationStatus.InvalidData
                    : DeformationDataMigrationStatus.Ready;
                return false;
            }

            _migrationStatus = DeformationDataMigrationStatus.InProgress;
            switch (_deformationDataVersion)
            {
                case DeformationDataVersion.Unversioned:
                    return ClassifyUnversionedDeformationData();

                case DeformationDataVersion.V0_0_1:
                    return TryUpgradeV0_0_1ToV0_0_2();

                // These releases did not alter the serialized deformation payload.
                // They remain explicit so interrupted upgrades resume deterministically.
                case DeformationDataVersion.V0_0_2:
                    return CommitReleaseVersion(DeformationDataVersion.V0_0_3);
                case DeformationDataVersion.V0_0_3:
                    return CommitReleaseVersion(DeformationDataVersion.V0_0_4);
                case DeformationDataVersion.V0_0_4:
                    return CommitReleaseVersion(DeformationDataVersion.V0_0_5);
                case DeformationDataVersion.V0_0_5:
                    return CommitReleaseVersion(DeformationDataVersion.V0_0_6);
                case DeformationDataVersion.V0_0_6:
                    return CommitReleaseVersion(DeformationDataVersion.V1_0_0);
                case DeformationDataVersion.V1_0_0:
                    return CommitReleaseVersion(DeformationDataVersion.V1_0_1);
                case DeformationDataVersion.V1_0_1:
                    return CommitReleaseVersion(DeformationDataVersion.V1_1_0);
                case DeformationDataVersion.V1_1_0:
                    return CommitReleaseVersion(DeformationDataVersion.V1_2_0);

                case DeformationDataVersion.V1_2_0:
                    return TryUpgradeV1_2_0ToV1_2_1();

                case DeformationDataVersion.V1_2_1:
                    return TryUpgradeV1_2_1ToV1_3_0();
                case DeformationDataVersion.V1_3_0:
                    return TryNormalizePublishedGroupSelectionAndCommit(
                        DeformationDataVersion.V1_3_1);
                case DeformationDataVersion.V1_3_1:
                    return TryNormalizePublishedGroupSelectionAndCommit(
                        DeformationDataVersion.V1_4_0);

                case DeformationDataVersion.V1_4_0:
                    return TryUpgradeV1_4_0ToCurrent();

                // The serialized enum is contiguous; range guards reject every unknown value.
#line hidden
                default:
                    _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                    return false;
#line default
            }
        }

        private void NormalizeAuthoritativeGroupShapeVersion()
        {
            if (HasNonNullGroups(_groups) && !HasNonNullLayers(_layers) &&
                _layerModelVersion < k_CurrentLayerModelVersion)
            {
                _layerModelVersion = k_CurrentLayerModelVersion;
            }
        }

        private bool ClassifyUnversionedDeformationData()
        {
            DeformationDataVersion detected;
            bool hasGroups = HasNonNullGroups(_groups);
            bool hasFlatLayers = HasNonNullLayers(_layers);
            bool hasBaseSettings = HasMeaningfulBaseSettings();

            if (!hasGroups && !hasFlatLayers && !hasBaseSettings)
            {
                _layerModelVersion = k_CurrentLayerModelVersion;
                _legacyAbsoluteLatticeEvaluation = false;
                _deformationDataSourceVersion = DeformationDataVersion.CurrentDevelopment;
                return CommitReleaseVersion(DeformationDataVersion.CurrentDevelopment);
            }

            if (hasGroups)
            {
                // Serialized groups first shipped in 1.2.1. The published releases can
                // also contain an eagerly-created group beside a stale flat-layer copy
                // and conceptual-v2 marker; those are still 1.2.1 evidence.
                detected = DeformationDataVersion.V1_2_1;
            }
            else if (hasFlatLayers || _layerModelVersion > 0)
            {
                // Internal conceptual-v1/v2 builds are treated as the immediately
                // preceding public release and normalized in the 1.2.0→1.2.1 step.
                detected = DeformationDataVersion.V1_2_0;
            }
            else
            {
                // Single-settings payloads are intentionally classified at the oldest
                // compatible release. Only an intact _applySpace=1 marker identifies
                // 0.0.1 World data; marker-less 0.0.2+ data is never guessed as World.
                detected = DeformationDataVersion.V0_0_1;
            }

            _deformationDataSourceVersion = detected;
            _deformationDataVersion = detected;
            MarkMigrationCommitted();
            return true;
        }

        private bool TryUpgradeV0_0_1ToV0_0_2()
        {
            if (_settings == null)
            {
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return false;
            }

            if (_settings.HasInvalidLegacyApplySpace)
            {
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return false;
            }

            if (_settings.HasPendingLegacyWorldSpace)
            {
                Transform owner = MeshTransform;
                // A live MonoBehaviour always owns a Transform.
#line hidden
                if (owner == null)
                {
                    _migrationStatus = DeformationDataMigrationStatus.PendingOwnerTransform;
                    return false;
                }
#line default

                if (_settings.ControlPointsLocal.Length != _settings.ControlPointCount)
                {
                    _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                    return false;
                }

                // 0.0.1 evaluated World control points against the owner's transform
                // on every deformation. Validate now, but retain both raw points and
                // marker so later transform changes keep those exact semantics.
                if (!_settings.CanEvaluateLegacyWorldSpace(owner.worldToLocalMatrix))
                {
                    _migrationStatus = DeformationDataMigrationStatus.PendingOwnerTransform;
                    return false;
                }
            }

            return CommitReleaseVersion(DeformationDataVersion.V0_0_2);
        }

        private bool TryUpgradeV1_2_0ToV1_2_1()
        {
            // The structural helpers below use copy-on-write for the containing lists,
            // so retaining the original references is a complete rollback snapshot.
            var originalLayers = _layers;
            var originalGroups = _groups;
            int originalLayerVersion = _layerModelVersion;
            int originalActiveLayer = _activeLayerIndex;
            int originalActiveGroup = _activeGroupIndex;

            try
            {
                bool hasGroups = HasNonNullGroups(_groups);
                bool hasFlatLayers = HasNonNullLayers(_layers);

                if (hasGroups && !hasFlatLayers)
                {
                    // A partial save already contains the newest meaningful shape. Do
                    // not manufacture a duplicate layer from the facade _settings copy.
                    _layerModelVersion = k_CurrentLayerModelVersion;
                }
                else
                {
                    if (_layerModelVersion < 2)
                    {
                        TryMigrateLegacyBaseToLayerStructure();
                    }

                    TryMigrateLayersToGroupStructure();
                }

                if (_layerModelVersion != k_CurrentLayerModelVersion || !HasNonNullGroups(_groups))
                {
                    throw new InvalidOperationException("Layer/group migration did not produce the v3 structure.");
                }
            }
            catch (Exception)
            {
                _layers = originalLayers;
                _groups = originalGroups;
                _layerModelVersion = originalLayerVersion;
                _activeLayerIndex = originalActiveLayer;
                _activeGroupIndex = originalActiveGroup;
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return false;
            }

            return CommitReleaseVersion(DeformationDataVersion.V1_2_1);
        }

        private bool TryUpgradeV1_2_1ToV1_3_0()
        {
            // 1.2.1–1.4.0 could serialize authoritative groups together with a stale
            // flat-layer facade and _layerModelVersion=2. The old runtime ignored that
            // flat copy. Preserve it in a disabled recovery group so the payload remains
            // inspectable without changing deformation or BlendShape output.
            var originalLayers = _layers;
            var originalGroups = _groups;
            int originalLayerVersion = _layerModelVersion;
            int originalActiveLayer = _activeLayerIndex;
            int originalActiveGroup = _activeGroupIndex;
            DeformationDataVersion originalVersion = _deformationDataVersion;
            DeformationDataVersion originalSourceVersion = _deformationDataSourceVersion;
            bool originalPublishedBlendShapeSemantics = _legacyPublishedBlendShapeSemantics;
            List<GroupSelectionSnapshot> selectionSnapshots = null;

            try
            {
                if (!HasNonNullGroups(_groups))
                {
                    throw new InvalidOperationException("The 1.2.1 group payload is missing.");
                }
                bool preservePublishedBlendShapeSemantics =
                    ShouldPreserveHistoricalGroupBlendShapeSemantics();

                var migratedGroups = new List<DeformerGroup>(_groups);
                if (HasNonNullLayers(_layers))
                {
                    var migratedLayers = FilterLayersAndRemapActive(
                        _layers,
                        _activeLayerIndex,
                        out int migratedActiveLayer);
                    // HasNonNullLayers guarantees the filter retains at least one layer.
#line hidden
                    if (migratedLayers.Count == 0)
                    {
                        throw new InvalidOperationException("The legacy flat-layer payload could not be recovered.");
                    }
#line default

                    var recoveryGroup = new DeformerGroup
                    {
                        Name = k_RecoveredLegacyFlatLayersGroupName,
                        Enabled = false,
                        ActiveLayerIndex = migratedActiveLayer,
                        BlendShapeOutput = _blendShapeOutput,
                        BlendShapeName = _blendShapeName ?? "",
                        BlendShapeCurve = CloneCurve(_blendShapeCurve)
                    };
                    foreach (var layer in migratedLayers)
                    {
                        recoveryGroup.LayersList.Add(layer);
                    }
                    // ActiveLayerIndex clamps against the destination list, so restore
                    // it after the layers have been copied.
                    recoveryGroup.ActiveLayerIndex = migratedActiveLayer;
                    migratedGroups.Add(recoveryGroup);
                }

                _groups = migratedGroups;
                _layers = new List<LatticeLayer>();
                // The recovery group owns the preserved flat selection from this point.
                _activeLayerIndex = 0;
                _layerModelVersion = k_CurrentLayerModelVersion;
                // Existing groups are authoritative; keep the user's selected group.
                _activeGroupIndex = originalActiveGroup;
                if (preservePublishedBlendShapeSemantics)
                {
                    _legacyPublishedBlendShapeSemantics = true;
                }
                if (_activeGroupIndex < 0 || _activeGroupIndex >= _groups.Count ||
                    _groups[_activeGroupIndex] == null)
                {
                    throw new InvalidOperationException("The active 1.2.1 group index is invalid.");
                }

                selectionSnapshots = CanonicalizePublishedRemoveLastSelections();

                if (!CommitReleaseVersion(DeformationDataVersion.V1_3_0))
                {
                    throw new InvalidOperationException("Could not commit the 1.2.1→1.3.0 migration boundary.");
                }

                return true;
            }
            catch (Exception)
            {
                _layers = originalLayers;
                _groups = originalGroups;
                _layerModelVersion = originalLayerVersion;
                _activeLayerIndex = originalActiveLayer;
                _activeGroupIndex = originalActiveGroup;
                _deformationDataVersion = originalVersion;
                _deformationDataSourceVersion = originalSourceVersion;
                _legacyPublishedBlendShapeSemantics = originalPublishedBlendShapeSemantics;
                RestoreGroupSelections(selectionSnapshots);
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return false;
            }
        }

        private bool TryNormalizePublishedGroupSelectionAndCommit(DeformationDataVersion next)
        {
            DeformationDataVersion originalVersion = _deformationDataVersion;
            DeformationDataVersion originalSourceVersion = _deformationDataSourceVersion;
            List<GroupSelectionSnapshot> selectionSnapshots = null;
            try
            {
                selectionSnapshots = CanonicalizePublishedRemoveLastSelections();
                if (!CommitReleaseVersion(next))
                {
                    RestoreGroupSelections(selectionSnapshots);
                    return false;
                }

                return true;
            }
            // Canonicalization and commit are non-throwing for validated state.
#line hidden
            catch (Exception)
            {
                RestoreGroupSelections(selectionSnapshots);
                _deformationDataVersion = originalVersion;
                _deformationDataSourceVersion = originalSourceVersion;
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return false;
            }
        }

        private bool TryUpgradeV1_4_0ToCurrent()
        {
            DeformationDataVersion originalVersion = _deformationDataVersion;
            DeformationDataVersion originalSourceVersion = _deformationDataSourceVersion;
            int originalLayerModelVersion = _layerModelVersion;
            bool originalPublishedSemantics = _legacyPublishedBlendShapeSemantics;
            bool originalAbsoluteEvaluation = _legacyAbsoluteLatticeEvaluation;
            List<GroupSelectionSnapshot> selectionSnapshots = null;
            List<LatticeInterpolationCompatibilitySnapshot> interpolationSnapshots = null;
            try
            {
                NormalizeAuthoritativeGroupShapeVersion();
                if (ShouldPreserveHistoricalGroupBlendShapeSemantics())
                {
                    _legacyPublishedBlendShapeSemantics = true;
                }
                _legacyAbsoluteLatticeEvaluation = HasMeaningfulSerializedLatticeData();
                interpolationSnapshots = PreservePublishedCubicInterpolationSemantics();
                selectionSnapshots = CanonicalizePublishedRemoveLastSelections();
                if (!CommitReleaseVersion(DeformationDataVersion.CurrentDevelopment))
                {
                    throw new InvalidOperationException("Could not commit the 1.4.0→current migration boundary.");
                }

                return true;
            }
            catch (Exception)
            {
                RestoreGroupSelections(selectionSnapshots);
                _deformationDataVersion = originalVersion;
                _deformationDataSourceVersion = originalSourceVersion;
                _layerModelVersion = originalLayerModelVersion;
                _legacyPublishedBlendShapeSemantics = originalPublishedSemantics;
                _legacyAbsoluteLatticeEvaluation = originalAbsoluteEvaluation;
                RestoreLatticeInterpolationCompatibility(interpolationSnapshots);
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return false;
            }
#line default
        }

        private List<LatticeInterpolationCompatibilitySnapshot> PreservePublishedCubicInterpolationSemantics()
        {
            var snapshots = new List<LatticeInterpolationCompatibilitySnapshot>();
            var visited = new HashSet<LatticeAsset>();

            void Preserve(LatticeAsset asset)
            {
                if (asset == null || !visited.Add(asset) ||
                    asset.Interpolation != LatticeInterpolationMode.CubicBernstein)
                {
                    return;
                }

                snapshots.Add(new LatticeInterpolationCompatibilitySnapshot(
                    asset,
                    asset.UsesLegacyTrilinearInterpolation));
                asset.SetLegacyTrilinearInterpolation(true);
            }

            Preserve(_settings);
            if (_layers != null)
            {
                foreach (var layer in _layers)
                {
                    if (layer != null && layer.Type == MeshDeformerLayerType.Lattice)
                    {
                        Preserve(layer.SerializedSettings);
                    }
                }
            }

            if (_groups != null)
            {
                foreach (var group in _groups)
                {
                    var layers = group?.SerializedLayers;
                    if (layers == null) continue;
                    foreach (var layer in layers)
                    {
                        if (layer != null && layer.Type == MeshDeformerLayerType.Lattice)
                        {
                            Preserve(layer.SerializedSettings);
                        }
                    }
                }
            }

            return snapshots;
        }

        private static void RestoreLatticeInterpolationCompatibility(
            List<LatticeInterpolationCompatibilitySnapshot> snapshots)
        {
            if (snapshots == null) return;
            for (int index = snapshots.Count - 1; index >= 0; index--)
            {
                var snapshot = snapshots[index];
                snapshot.Asset?.SetLegacyTrilinearInterpolation(
                    snapshot.UsedLegacyTrilinearInterpolation);
            }
        }

        /// <summary>
        /// Releases 1.2.1 through 1.4.0 read ActiveLayerIndex only after removing a
        /// layer. Removing the selected last layer therefore serialized exactly one past
        /// the new Count. That exact, tag-proven pattern is recoverable without guessing;
        /// every other out-of-range value remains invalid.
        /// </summary>
        private List<GroupSelectionSnapshot> CanonicalizePublishedRemoveLastSelections()
        {
            var snapshots = new List<GroupSelectionSnapshot>();
            if (!CanContainPublishedRemoveLastSelectionBug() || _groups == null)
            {
                return snapshots;
            }

            for (int groupIndex = 0; groupIndex < _groups.Count; groupIndex++)
            {
                var group = _groups[groupIndex];
                var layers = group?.SerializedLayers;
                if (layers == null || layers.Count == 0 ||
                    group.SerializedActiveLayerIndex != layers.Count)
                {
                    continue;
                }

                snapshots.Add(new GroupSelectionSnapshot(group, group.SerializedActiveLayerIndex));
            }

            for (int index = 0; index < snapshots.Count; index++)
            {
                var snapshot = snapshots[index];
                snapshot.Group.SetSerializedActiveLayerIndex(snapshot.ActiveLayerIndex - 1);
            }

            return snapshots;
        }

        private static void RestoreGroupSelections(List<GroupSelectionSnapshot> snapshots)
        {
            if (snapshots == null) return;
            for (int index = snapshots.Count - 1; index >= 0; index--)
            {
                var snapshot = snapshots[index];
                snapshot.Group?.SetSerializedActiveLayerIndex(snapshot.ActiveLayerIndex);
            }
        }

        private bool CanContainPublishedRemoveLastSelectionBug()
        {
            if (_deformationDataVersion == DeformationDataVersion.Unversioned)
            {
                return HasNonNullGroups(_groups);
            }

            return _deformationDataVersion >= DeformationDataVersion.V1_2_1 &&
                   _deformationDataVersion <= DeformationDataVersion.V1_4_0;
        }

        private bool ShouldPreserveHistoricalGroupBlendShapeSemantics()
        {
            DeformationDataVersion source = SourceDeformationDataVersion;
            return source >= DeformationDataVersion.V1_2_1 &&
                   source <= DeformationDataVersion.V1_4_0 &&
                   HasEnabledPublishedBlendShapeMetadata();
        }

        private bool HasEnabledPublishedBlendShapeMetadata()
        {
            if (_groups != null)
            {
                foreach (var group in _groups)
                {
                    // Published Deform skipped disabled groups before inspecting any
                    // output metadata. Such dormant fields must not lock unrelated,
                    // enabled groups into component-wide compatibility semantics.
                    if (group == null || !group.Enabled) continue;
                    if (group.BlendShapeOutput == BlendShapeOutputMode.OutputAsBlendShape)
                    {
                        return true;
                    }

                    var layers = group.SerializedLayers;
                    if (layers == null) continue;
                    foreach (var layer in layers)
                    {
                        if (layer != null && layer.Enabled && layer.Weight > 0f &&
                            layer.BlendShapeOutput == BlendShapeOutputMode.OutputAsBlendShape)
                        {
                            return true;
                        }
                    }
                }
            }

            // Once published groups existed, the old runtime never evaluated the
            // component's stale flat-layer facade. Metadata found only in that backup
            // must therefore not switch the authoritative groups into component-wide
            // compatibility mode. The backup is retained in a disabled recovery group.
            return false;
        }

        private bool CommitReleaseVersion(DeformationDataVersion next)
        {
            if ((int)next <= (int)_deformationDataVersion ||
                (int)next > (int)DeformationDataVersion.CurrentDevelopment)
            {
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return false;
            }

            if (_deformationDataSourceVersion == DeformationDataVersion.Unversioned)
            {
                _deformationDataSourceVersion = _deformationDataVersion;
            }

            _deformationDataVersion = next;
            _migrationStatus = next == DeformationDataVersion.CurrentDevelopment
                ? DeformationDataMigrationStatus.Ready
                : DeformationDataMigrationStatus.InProgress;
            MarkMigrationCommitted();
            return true;
        }

        private void MarkMigrationCommitted()
        {
            InvalidateCache();
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                MarkDirtyInEditor(this);
            }
#endif
        }

        private bool HasMeaningfulBaseSettings()
        {
            if (_settings == null)
            {
                return false;
            }

            if (_settings.HasPendingLegacyWorldSpace || _settings.HasInvalidLegacyApplySpace ||
                _hasInitializedFromSource || _serializedSourceMesh != null)
            {
                return true;
            }

            // Unity may run the nested serialization callback while a brand-new
            // component is being constructed, which creates a neutral point array.
            // Neutral points without any source-initialization evidence are fresh, not
            // historical deformation data.
            return _settings.HasNonDefaultSerializedConfiguration ||
                   (_settings.HasSerializedControlPointData && _settings.HasCustomizedControlPoints());
        }

        private bool HasMeaningfulSerializedLatticeData()
        {
            if (_groups != null)
            {
                foreach (var group in _groups)
                {
                    if (group == null) continue;
                    var serializedLayers = group.SerializedLayers;
                    if (serializedLayers == null) continue;
                    foreach (var layer in serializedLayers)
                    {
                        if (layer != null && layer.Type == MeshDeformerLayerType.Lattice &&
                            layer.SerializedSettings != null &&
                            layer.SerializedSettings.HasSerializedControlPointData)
                        {
                            return true;
                        }
                    }
                }
            }

            if (_layers != null)
            {
                foreach (var layer in _layers)
                {
                    if (layer != null && layer.Type == MeshDeformerLayerType.Lattice &&
                        layer.SerializedSettings != null &&
                        layer.SerializedSettings.HasSerializedControlPointData)
                    {
                        return true;
                    }
                }
            }

            return HasMeaningfulBaseSettings();
        }

        private static bool HasNonNullGroups(List<DeformerGroup> groups)
        {
            if (groups == null) return false;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i] != null) return true;
            }

            return false;
        }

        private static bool HasNonNullLayers(List<LatticeLayer> layers)
        {
            if (layers == null) return false;
            for (int i = 0; i < layers.Count; i++)
            {
                if (layers[i] != null) return true;
            }

            return false;
        }

        private bool HasUnsupportedFutureLatticeAsset()
        {
            if (_settings != null && _settings.HasUnsupportedFutureSerializationVersion)
            {
                return true;
            }

            if (_layers != null)
            {
                foreach (var layer in _layers)
                {
                    if (layer?.SerializedSettings != null &&
                        layer.SerializedSettings.HasUnsupportedFutureSerializationVersion)
                    {
                        return true;
                    }
                }
            }

            if (_groups != null)
            {
                foreach (var group in _groups)
                {
                    var layers = group?.SerializedLayers;
                    if (layers == null) continue;
                    foreach (var layer in layers)
                    {
                        if (layer?.SerializedSettings != null &&
                            layer.SerializedSettings.HasUnsupportedFutureSerializationVersion)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private bool HasMalformedLatticeAsset()
        {
            if (HasMalformedSerializedSelection())
            {
                return true;
            }

            if (_blendShapeOutput != BlendShapeOutputMode.Disabled &&
                _blendShapeOutput != BlendShapeOutputMode.OutputAsBlendShape)
            {
                return true;
            }

            if (_settings != null && _settings.HasMalformedSerializedShape)
            {
                return true;
            }

            if (_layers != null)
            {
                foreach (var layer in _layers)
                {
                    if (layer != null &&
                        (layer.HasMalformedSerializedMetadata ||
                         (layer.SerializedSettings != null &&
                          layer.SerializedSettings.HasMalformedSerializedShape)))
                    {
                        return true;
                    }
                }
            }

            if (_groups != null)
            {
                foreach (var group in _groups)
                {
                    if (group != null && group.HasMalformedSerializedMetadata)
                    {
                        return true;
                    }

                    var layers = group?.SerializedLayers;
                    if (layers == null) continue;
                    foreach (var layer in layers)
                    {
                        if (layer != null &&
                            (layer.HasMalformedSerializedMetadata ||
                             (layer.SerializedSettings != null &&
                              layer.SerializedSettings.HasMalformedSerializedShape)))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Validates raw selection indices before any migration or model-normalization
        /// code can clamp them. Active selection is serialized user data: silently
        /// choosing another group/layer would make a corrupt payload appear to migrate
        /// successfully while changing which deformation the Inspector edits.
        /// </summary>
        private bool HasMalformedSerializedSelection()
        {
            // Missing fields from old YAML retain these field-initializer lists. A
            // runtime null therefore represents an explicit/corrupt payload, and the
            // normalization paths below must not replace it with a guessed empty list.
            if (_groups == null || _layers == null)
            {
                return true;
            }

            if (_groups.Count == 0)
            {
                if (_activeGroupIndex != 0)
                {
                    return true;
                }
            }
            else
            {
                if (_activeGroupIndex < 0 || _activeGroupIndex >= _groups.Count)
                {
                    return true;
                }

                for (int groupIndex = 0; groupIndex < _groups.Count; groupIndex++)
                {
                    var group = _groups[groupIndex];
                    // Group-schema releases never assigned semantics to a null inline
                    // entry. Dropping it or replacing it with a default group would be
                    // a guessed repair, even when that entry is not currently selected.
                    if (group == null)
                    {
                        return true;
                    }

                    var layers = group.SerializedLayers;
                    int activeLayer = group.SerializedActiveLayerIndex;
                    if (layers == null)
                    {
                        return true;
                    }

                    if (layers.Count == 0)
                    {
                        if (activeLayer != 0)
                        {
                            return true;
                        }
                    }
                    else
                    {
                        bool knownPublishedRemoveLastPattern =
                            CanContainPublishedRemoveLastSelectionBug() &&
                            activeLayer == layers.Count;
                        if (activeLayer < 0 ||
                            (activeLayer >= layers.Count && !knownPublishedRemoveLastPattern))
                        {
                            return true;
                        }

                        // As with groups, every inline layer slot must carry an actual
                        // payload. EnsureGroupsCore must not silently manufacture a
                        // neutral layer in place of corrupted serialized data.
                        for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
                        {
                            if (layers[layerIndex] == null)
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            if (_layers.Count == 0)
            {
                // Published group initialization could leave the obsolete component
                // facade index behind after moving its selected flat layer into a
                // DeformerGroup. It has no target once the flat list is empty; preserve
                // it through classification, then canonicalize it at the structural
                // 1.2.1→1.3.0 boundary. Later/current payloads must already be canonical.
                bool awaitingPublishedGroupNormalization = _groups.Count > 0 &&
                    (_deformationDataVersion == DeformationDataVersion.Unversioned ||
                     _deformationDataVersion == DeformationDataVersion.V1_2_0 ||
                     _deformationDataVersion == DeformationDataVersion.V1_2_1);
                if (awaitingPublishedGroupNormalization)
                {
                    return false;
                }

                // The single-settings schema used both the default zero and -1 as the
                // base-lattice selection sentinel before a flat list existed.
                return _activeLayerIndex < -1 || _activeLayerIndex > 0;
            }

            // A conceptual-v2 flat payload could historically contain null holes; the
            // immutable staged migration contract deterministically filters those while
            // remapping a non-null active layer. Once authoritative groups exist, the
            // same null is corruption in the stale backup and must fail closed.
            if (_groups != null && _groups.Count > 0)
            {
                for (int layerIndex = 0; layerIndex < _layers.Count; layerIndex++)
                {
                    if (_layers[layerIndex] == null)
                    {
                        return true;
                    }
                }
            }

            return _activeLayerIndex < 0 || _activeLayerIndex >= _layers.Count ||
                   _layers[_activeLayerIndex] == null;
        }

        /// <summary>
        /// Validates non-empty vertex-indexed payloads without allocating, resizing, or
        /// caching anything. This preflight runs before every release step so a brush or
        /// mask mismatch cannot be committed through later release markers first.
        /// </summary>
        private bool HasIncompatibleSerializedVertexIndexedData()
        {
            Mesh validationMesh = null;
            if (_skinnedMeshRenderer != null)
            {
                validationMesh = _skinnedMeshRenderer.sharedMesh;
            }
            if (validationMesh == null && _meshFilter != null)
            {
                validationMesh = _meshFilter.sharedMesh;
            }

            if (validationMesh == null)
            {
                var serializedSkinnedRenderer = GetComponent<SkinnedMeshRenderer>();
                if (serializedSkinnedRenderer != null)
                {
                    validationMesh = serializedSkinnedRenderer.sharedMesh;
                }
                if (validationMesh == null)
                {
                    var serializedMeshFilter = GetComponent<MeshFilter>();
                    if (serializedMeshFilter != null)
                    {
                        validationMesh = serializedMeshFilter.sharedMesh;
                    }
                }
            }

            if (validationMesh == null)
            {
                validationMesh = _serializedSourceMesh != null ? _serializedSourceMesh : _sourceMesh;
            }

            int expectedVertexCount = validationMesh != null ? validationMesh.vertexCount : -1;

            bool IsIncompatible(LatticeLayer layer)
            {
                if (layer == null) return false;
                if (layer.HasNonFiniteSerializedVertexData) return true;

                int displacementCount = layer.SerializedBrushDisplacementCount;
                int maskCount = layer.SerializedVertexMaskCount;
                if (displacementCount == 0 && maskCount == 0)
                {
                    return false;
                }

                if (expectedVertexCount < 0)
                {
                    // Vertex identity cannot be established without the source mesh.
                    // Preserve the payload and allow shape-only migration; it will be
                    // validated as soon as a source becomes known.
                    return false;
                }

                return (displacementCount != 0 && displacementCount != expectedVertexCount) ||
                       (maskCount != 0 && maskCount != expectedVertexCount);
            }

            if (_layers != null)
            {
                foreach (var layer in _layers)
                {
                    if (IsIncompatible(layer)) return true;
                }
            }

            if (_groups != null)
            {
                foreach (var group in _groups)
                {
                    var layers = group?.SerializedLayers;
                    if (layers == null) continue;
                    foreach (var layer in layers)
                    {
                        if (IsIncompatible(layer)) return true;
                    }
                }
            }

            return false;
        }

        private void TryApplyLayerContribution(LatticeLayer layer, Vector3[] sourceVertices, Vector3[] deformedVertices)
        {
            if (layer == null)
            {
                return;
            }

            switch (layer.Type)
            {
                case MeshDeformerLayerType.Brush:
                    TryApplyBrushLayerContribution(layer, sourceVertices, deformedVertices);
                    break;
                default:
                    TryApplyLatticeLayerContribution(layer, sourceVertices, deformedVertices);
                    break;
            }
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
            if (layerSettings == null || !EnsureCache(layerSettings, sourceVertices))
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
            float weight = layer.Weight;

            if (_legacyAbsoluteLatticeEvaluation)
            {
                Matrix4x4 worldToLocal = Matrix4x4.identity;
                if (layerSettings.HasPendingLegacyWorldSpace)
                {
                    Transform owner = MeshTransform;
                    // MeshTransform falls back to this component's Transform.
#line hidden
                    if (owner == null)
                    {
                        return;
                    }
#line default

                    worldToLocal = owner.worldToLocalMatrix;
                }

                if (!layerSettings.TryCopyLegacyEvaluationControlPoints(
                        worldToLocal,
                        _controlBuffer.AsSpan()))
                {
                    return;
                }

                var layerVertices = DeformWithJobs(entries, _controlBuffer);
                for (int vertex = 0; vertex < deformedVertices.Length; vertex++)
                {
                    deformedVertices[vertex] +=
                        (layerVertices[vertex] - sourceVertices[vertex]) * weight;
                }
            }
            else
            {
                CollectControlPointOffsetsLocal(layerSettings, _controlBuffer.AsSpan());
                var layerOffsets = DeformWithJobs(entries, _controlBuffer);
                for (int vertex = 0; vertex < deformedVertices.Length; vertex++)
                {
                    deformedVertices[vertex] += layerOffsets[vertex] * weight;
                }
            }
        }

        private static bool TryBuildDeltas(Vector3[] sourceVertices, Vector3[] deformedVertices, out Vector3[] deltas)
        {
            deltas = null;
            if (sourceVertices == null || deformedVertices == null || sourceVertices.Length != deformedVertices.Length)
            {
                return false;
            }

            var result = new Vector3[sourceVertices.Length];
            bool hasDelta = false;
            for (int v = 0; v < sourceVertices.Length; v++)
            {
                result[v] = deformedVertices[v] - sourceVertices[v];
                if (!hasDelta && result[v].sqrMagnitude > 1e-10f)
                {
                    hasDelta = true;
                }
            }

            if (!hasDelta)
            {
                return false;
            }

            deltas = result;
            return true;
        }

        private int ComputeBlendShapeOutputHash(List<GeneratedBlendShape> blendShapes)
        {
            int hash = 17;
            foreach (var generated in blendShapes)
            {
                hash = hash * 31 + (generated.Name ?? "").GetHashCode();
                hash = hash * 31 + HashCurveState(generated.Curve);

                var deltas = generated.Deltas;
                if (deltas == null)
                {
                    hash = hash * 31;
                    continue;
                }

                for (int v = 0; v < deltas.Length; v++)
                    hash = hash * 31 + deltas[v].GetHashCode();
            }
            return hash;
        }

        private void AddGeneratedBlendShapeFrames(
            Mesh mesh,
            string shapeName,
            Vector3[] baseVertices,
            Vector3[] deltas,
            AnimationCurve curve)
        {
            if (mesh == null || string.IsNullOrEmpty(shapeName) || baseVertices == null || deltas == null)
            {
                return;
            }

            int vertexCount = mesh.vertexCount;
            if (baseVertices.Length != vertexCount || deltas.Length != vertexCount)
            {
                return;
            }

            curve ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);

            Vector3[] fullDeltaNormals = null;
            Vector3[] fullDeltaTangents = null;
            if (!_legacyPublishedBlendShapeSemantics &&
                (_recalculateNormals || _recalculateTangents))
            {
                CalculateGeneratedSurfaceDeltas(
                    mesh,
                    baseVertices,
                    deltas,
                    _recalculateNormals,
                    _recalculateTangents,
                    out fullDeltaNormals,
                    out fullDeltaTangents);
            }

            const int sampleCount = 100;
            for (int f = 0; f < sampleCount; f++)
            {
                float t = (f + 1f) / sampleCount;
                float frameWeight = t * 100f;
                float curveValue = curve.Evaluate(t);

                var frameDeltas = new Vector3[vertexCount];
                Vector3[] frameNormals = fullDeltaNormals != null ? new Vector3[vertexCount] : null;
                Vector3[] frameTangents = fullDeltaTangents != null ? new Vector3[vertexCount] : null;

                for (int v = 0; v < vertexCount; v++)
                {
                    frameDeltas[v] = deltas[v] * curveValue;
                    if (frameNormals != null)
                    {
                        frameNormals[v] = fullDeltaNormals[v] * curveValue;
                    }

                    if (frameTangents != null)
                    {
                        frameTangents[v] = fullDeltaTangents[v] * curveValue;
                    }
                }

                mesh.AddBlendShapeFrame(shapeName, frameWeight, frameDeltas, frameNormals, frameTangents);
            }
        }

        private static void CalculateGeneratedSurfaceDeltas(
            Mesh template,
            Vector3[] baseVertices,
            Vector3[] deltas,
            bool includeNormals,
            bool includeTangents,
            out Vector3[] deltaNormals,
            out Vector3[] deltaTangents)
        {
            deltaNormals = null;
            deltaTangents = null;

            if (template == null || baseVertices == null || deltas == null || baseVertices.Length != deltas.Length)
            {
                return;
            }

            Mesh baseMesh = null;
            Mesh targetMesh = null;
            try
            {
                baseMesh = UnityEngine.Object.Instantiate(template);
                targetMesh = UnityEngine.Object.Instantiate(template);

                int vertexCount = baseVertices.Length;
                var targetVertices = new Vector3[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    targetVertices[i] = baseVertices[i] + deltas[i];
                }

                baseMesh.vertices = baseVertices;
                targetMesh.vertices = targetVertices;

                if (includeNormals)
                {
                    baseMesh.RecalculateNormals();
                    targetMesh.RecalculateNormals();

                    var baseNormals = baseMesh.normals;
                    var targetNormals = targetMesh.normals;
                    if (baseNormals != null && targetNormals != null &&
                        baseNormals.Length == vertexCount && targetNormals.Length == vertexCount)
                    {
                        deltaNormals = new Vector3[vertexCount];
                        for (int i = 0; i < vertexCount; i++)
                        {
                            deltaNormals[i] = targetNormals[i] - baseNormals[i];
                        }
                    }
                }

                if (includeTangents)
                {
                    baseMesh.RecalculateNormals();
                    targetMesh.RecalculateNormals();
                    baseMesh.RecalculateTangents();
                    targetMesh.RecalculateTangents();

                    var baseTangents = baseMesh.tangents;
                    var targetTangents = targetMesh.tangents;
                    if (baseTangents != null && targetTangents != null &&
                        baseTangents.Length == vertexCount && targetTangents.Length == vertexCount)
                    {
                        deltaTangents = new Vector3[vertexCount];
                        for (int i = 0; i < vertexCount; i++)
                        {
                            deltaTangents[i] = new Vector3(
                                targetTangents[i].x - baseTangents[i].x,
                                targetTangents[i].y - baseTangents[i].y,
                                targetTangents[i].z - baseTangents[i].z);
                        }
                    }
                }
            }
            finally
            {
                DestroyTemporaryMesh(baseMesh);
                DestroyTemporaryMesh(targetMesh);
            }
        }

        private static HashSet<string> CollectBlendShapeNames(Mesh mesh)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (mesh == null)
            {
                return names;
            }

            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                names.Add(mesh.GetBlendShapeName(i));
            }

            return names;
        }

        private static string MakeUniqueBlendShapeName(string requestedName, HashSet<string> usedNames)
        {
            usedNames ??= new HashSet<string>(StringComparer.Ordinal);

            string baseName = string.IsNullOrWhiteSpace(requestedName) ? "BlendShape" : requestedName.Trim();
            string name = baseName;
            int suffix = 1;
            while (usedNames.Contains(name))
            {
                name = $"{baseName} {suffix}";
                suffix++;
            }

            usedNames.Add(name);
            return name;
        }

        private static void DestroyTemporaryMesh(Mesh mesh)
        {
            if (mesh == null)
            {
                return;
            }

            // The release gate is EditMode-only; PlayMode destruction is a Unity branch.
#line hidden
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(mesh);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
#line default
        }

        private static void CopyBlendShapes(
            Mesh source,
            Mesh destination,
            Vector3[][] bakedBlendShapeDeltas = null,
            float[] bakedBlendShapeWeights = null)
        {
            int shapeCount = source.blendShapeCount;
            int vertexCount = source.vertexCount;
            for (int s = 0; s < shapeCount; s++)
            {
                string name = source.GetBlendShapeName(s);
                int frameCount = source.GetBlendShapeFrameCount(s);
                var baked = bakedBlendShapeDeltas != null && s < bakedBlendShapeDeltas.Length
                    ? bakedBlendShapeDeltas[s]
                    : null;
                float bakedWeight = bakedBlendShapeWeights != null && s < bakedBlendShapeWeights.Length
                    ? bakedBlendShapeWeights[s]
                    : 0f;
                bool hasBakedShape = baked != null && baked.Length == vertexCount;

                if (hasBakedShape && frameCount > 0)
                {
                    float firstWeight = source.GetBlendShapeFrameWeight(s, 0);
                    if (bakedWeight < firstWeight - 1e-5f)
                    {
                        destination.AddBlendShapeFrame(
                            name,
                            bakedWeight,
                            new Vector3[vertexCount],
                            new Vector3[vertexCount],
                            new Vector3[vertexCount]);
                    }
                }

                for (int f = 0; f < frameCount; f++)
                {
                    float weight = source.GetBlendShapeFrameWeight(s, f);
                    var dv = new Vector3[vertexCount];
                    var dn = new Vector3[vertexCount];
                    var dt = new Vector3[vertexCount];
                    source.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                    if (hasBakedShape)
                    {
                        for (int v = 0; v < vertexCount; v++)
                        {
                            dv[v] -= baked[v];
                        }
                    }

                    destination.AddBlendShapeFrame(name, weight, dv, dn, dt);
                }
            }
        }

        private Vector3[] BuildCurrentSourceVertices(
            out Vector3[][] bakedBlendShapeDeltas,
            out float[] bakedBlendShapeWeights,
            out int bakedBlendShapeHash)
        {
            bakedBlendShapeDeltas = null;
            bakedBlendShapeWeights = null;
            bakedBlendShapeHash = 0;

            if (_sourceMesh == null)
            {
                return null;
            }

            var vertices = _sourceMesh.vertices;
            if (vertices == null || vertices.Length == 0)
            {
                return vertices;
            }

            if (_skinnedMeshRenderer == null || _sourceMesh.blendShapeCount == 0)
            {
                return vertices;
            }

            int shapeCount = _sourceMesh.blendShapeCount;
            int vertexCount = _sourceMesh.vertexCount;
            Vector3[][] deltas = null;
            float[] weights = null;
            bool hasBakedShape = false;
            int hash = 17;

            for (int s = 0; s < shapeCount; s++)
            {
                float weight = _skinnedMeshRenderer.GetBlendShapeWeight(s);
                if (Mathf.Abs(weight) <= 1e-5f)
                {
                    continue;
                }

                var delta = EvaluateBlendShapeVertexDelta(_sourceMesh, s, weight);
                deltas ??= new Vector3[shapeCount][];
                weights ??= new float[shapeCount];
                deltas[s] = delta;
                weights[s] = weight;
                hasBakedShape = true;
                hash = HashCode.Combine(hash, s, weight);

                for (int v = 0; v < vertexCount; v++)
                {
                    vertices[v] += delta[v];
                }
            }

            if (!hasBakedShape)
            {
                return vertices;
            }

            bakedBlendShapeDeltas = deltas;
            bakedBlendShapeWeights = weights;
            bakedBlendShapeHash = hash;
            return vertices;
        }

        private static Vector3[] EvaluateBlendShapeVertexDelta(Mesh mesh, int shapeIndex, float weight)
        {
            int frameCount = mesh.GetBlendShapeFrameCount(shapeIndex);
            int vertexCount = mesh.vertexCount;
            var lower = new Vector3[vertexCount];
            var upper = new Vector3[vertexCount];
            var unusedNormals = new Vector3[vertexCount];
            var unusedTangents = new Vector3[vertexCount];

            float firstWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, 0);
            if (weight <= firstWeight || frameCount == 1)
            {
                mesh.GetBlendShapeFrameVertices(shapeIndex, 0, lower, unusedNormals, unusedTangents);
                float scale = Mathf.Abs(firstWeight) > Mathf.Epsilon ? weight / firstWeight : 0f;
                ScaleDeltas(lower, scale);
                return lower;
            }

            for (int frame = 1; frame < frameCount; frame++)
            {
                float upperWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, frame);
                if (weight <= upperWeight)
                {
                    float lowerWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, frame - 1);
                    mesh.GetBlendShapeFrameVertices(shapeIndex, frame - 1, lower, unusedNormals, unusedTangents);
                    mesh.GetBlendShapeFrameVertices(shapeIndex, frame, upper, unusedNormals, unusedTangents);

                    float t = Mathf.Abs(upperWeight - lowerWeight) > Mathf.Epsilon
                        ? Mathf.InverseLerp(lowerWeight, upperWeight, weight)
                        : 0f;
                    for (int i = 0; i < vertexCount; i++)
                    {
                        lower[i] = Vector3.LerpUnclamped(lower[i], upper[i], t);
                    }

                    return lower;
                }
            }

            mesh.GetBlendShapeFrameVertices(shapeIndex, frameCount - 1, lower, unusedNormals, unusedTangents);
            return lower;
        }

        private static void ScaleDeltas(Vector3[] deltas, float scale)
        {
            if (deltas == null)
            {
                return;
            }

            for (int i = 0; i < deltas.Length; i++)
            {
                deltas[i] *= scale;
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
            _blendShapeOutputDirty = true;
        }

        public void InitializeFromSource(bool resetControlPoints)
        {
            if (!EnsureGroups()) return;
            EnsureSettings();
            if (_sourceMesh == null) return;

            var sourceVertices = BuildCurrentSourceVertices(out _, out _, out _);
            var meshBounds = CalculateReferencedBounds(_sourceMesh, sourceVertices, _sourceMesh.bounds);
            foreach (var group in _groups)
            {
                if (group == null) continue;
                var layers = group.LayersList;
                for (int i = 0; i < layers.Count; i++)
                {
                    if (layers[i] == null) layers[i] = new LatticeLayer();
                    var layerSettings = layers[i].Settings;

                    layerSettings.LocalBounds = meshBounds;
                    if (resetControlPoints) layerSettings.ResetControlPoints();

                    if (layers[i].Type == MeshDeformerLayerType.Brush)
                    {
                        // EnsureGroupsCore performs the same compatibility preflight before
                        // source initialization can reach this defensive fallback.
#line hidden
                        if (!layers[i].TryEnsureBrushDataCapacityPreservingExisting(_sourceMesh.vertexCount))
                        {
                            _hasIncompatibleBrushData = true;
                            _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                            continue;
                        }
#line default
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
                MarkDirtyInEditor(this);
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
            if (_layerModelVersion > k_CurrentLayerModelVersion ||
                (int)_deformationDataVersion > (int)DeformationDataVersion.CurrentDevelopment)
            {
                return false;
            }

            var sourceLayers = _layers ?? new List<LatticeLayer>();
            var migratedLayers = FilterLayersAndRemapActive(
                sourceLayers,
                _activeLayerIndex,
                out int migratedActiveLayer);

            bool hasGroups = HasNonNullGroups(_groups);
            if (hasGroups && migratedLayers.Count == 0)
            {
                if (_layerModelVersion >= k_CurrentLayerModelVersion) return false;
                _layerModelVersion = k_CurrentLayerModelVersion;
                return false;
            }

            if (!hasGroups && migratedLayers.Count == 0)
            {
                if (_layerModelVersion >= k_CurrentLayerModelVersion) return false;
                _layerModelVersion = k_CurrentLayerModelVersion;
                return false;
            }

            // Wrap the flat payload. If groups already exist due to a partial save or
            // Inspector-first access, append a recovery group instead of discarding
            // either representation.
            var group = new DeformerGroup();
            group.Name = hasGroups ? "Recovered Layers" : "Group";
            foreach (var layer in migratedLayers)
            {
                group.LayersList.Add(layer);
            }
            group.ActiveLayerIndex = migratedActiveLayer;
            group.BlendShapeOutput = _blendShapeOutput;
            group.BlendShapeName = _blendShapeName ?? "";
            group.BlendShapeCurve = _blendShapeCurve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);

            var migratedGroups = _groups == null
                ? new List<DeformerGroup>()
                : new List<DeformerGroup>(_groups);
            migratedGroups.Add(group);
            _groups = migratedGroups;
            _activeGroupIndex = migratedGroups.Count - 1;
            _layers = new List<LatticeLayer>();
            // The selected flat layer now lives in the migrated group. Keep the raw
            // facade index canonical so subsequent fail-closed preflights do not treat
            // an otherwise successful migration as a dangling selection.
            _activeLayerIndex = 0;
            _layerModelVersion = k_CurrentLayerModelVersion;

            InvalidateCache();

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                MarkDirtyInEditor(this);
            }
#endif
            return true;
        }

        private bool EnsureGroups()
        {
            if (_isEnsuringLayerModelReady)
            {
                EnsureGroupsCore();
                return true;
            }

            if (!EnsureLayerModelReady())
            {
                return false;
            }

            EnsureGroupsCore();
            return true;
        }

        private static List<LatticeLayer> FilterLayersAndRemapActive(
            List<LatticeLayer> source,
            int sourceActive,
            out int active)
        {
            var filtered = new List<LatticeLayer>();
            LatticeLayer selected = sourceActive >= 0 && sourceActive < source.Count
                ? source[sourceActive]
                : null;
            active = 0;

            for (int i = 0; i < source.Count; i++)
            {
                var layer = source[i];
                if (layer == null) continue;
                if (ReferenceEquals(layer, selected)) active = filtered.Count;
                filtered.Add(layer);
            }

            if (selected == null && filtered.Count > 0)
            {
                int nonNullBeforeOrAt = 0;
                int limit = Mathf.Clamp(sourceActive, 0, source.Count - 1);
                for (int i = 0; i <= limit; i++)
                {
                    if (source[i] != null) nonNullBeforeOrAt++;
                }

                active = Mathf.Clamp(nonNullBeforeOrAt - 1, 0, filtered.Count - 1);
            }

            return filtered;
        }

        private void EnsureGroupsCore()
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
            LatticeLayer selectedLayer = _activeLayerIndex >= 0 && _activeLayerIndex < existingLayers.Count
                ? existingLayers[_activeLayerIndex]
                : null;

            bool includeLegacyBase = _settings != null &&
                                     (_settings.HasCustomizedControlPoints() ||
                                      !HasNonNullLayers(existingLayers) ||
                                      _activeLayerIndex < 0);
            int migratedActive = 0;
            if (includeLegacyBase)
            {
                migratedLayers.Add(new LatticeLayer
                {
                    Name = k_PrimaryLayerName,
                    Enabled = true,
                    Weight = 1f,
                    Settings = CloneSettings(_settings)
                });

                if (_activeLayerIndex < 0)
                {
                    migratedActive = 0;
                }
            }

            for (int i = 0; i < existingLayers.Count; i++)
            {
                var existing = existingLayers[i];
                if (existing == null)
                {
                    continue;
                }

                if (ReferenceEquals(existing, selectedLayer))
                {
                    migratedActive = migratedLayers.Count;
                }
                migratedLayers.Add(existing);
            }

            if (selectedLayer == null && _activeLayerIndex >= 0 && migratedLayers.Count > 0)
            {
                int nonNullBeforeOrAt = 0;
                int limit = Mathf.Clamp(_activeLayerIndex, 0, Math.Max(0, existingLayers.Count - 1));
                for (int i = 0; i <= limit && i < existingLayers.Count; i++)
                {
                    if (existingLayers[i] != null) nonNullBeforeOrAt++;
                }

                migratedActive = (includeLegacyBase ? 1 : 0) + nonNullBeforeOrAt - 1;
            }

            _layers = migratedLayers;
            _activeLayerIndex = _layers.Count == 0
                ? 0
                : Mathf.Clamp(migratedActive, 0, _layers.Count - 1);
            _layerModelVersion = 2; // v0→v2 done; TryMigrateLayersToGroupStructure handles v2→v3

            InvalidateCache();

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                MarkDirtyInEditor(this);
            }
#endif
            return true;
        }

#if UNITY_EDITOR
        [ExcludeFromCodeCoverage]
        private static void MarkDirtyInEditor(UnityEngine.Object target)
        {
            UnityEditor.EditorUtility.SetDirty(target);
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(target))
            {
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            }

            if (target is Component component)
            {
                var scene = component.gameObject.scene;
                if (scene.IsValid() && scene.isLoaded)
                {
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                }
            }
        }
#endif

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
            cloned.ClearLegacyWorldSpaceState();
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

            cloned.CopyLegacySerializationStateFrom(source);

            return cloned;
        }

        private static AnimationCurve CloneCurve(AnimationCurve source)
        {
            if (source == null)
            {
                return AnimationCurve.Linear(0f, 0f, 1f, 1f);
            }

            var clone = new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode
            };
            return clone;
        }

        private static int HashAssetState(LatticeAsset settings)
        {
            if (settings == null)
            {
                return 0;
            }

            int hash = HashCode.Combine(settings.GridSize, settings.LocalBounds.center, settings.LocalBounds.size, (int)settings.Interpolation);
            hash = HashCode.Combine(hash, settings.LegacyApplySpaceValue);
            hash = HashCode.Combine(hash, settings.UsesLegacyTrilinearInterpolation);
            var points = settings.ControlPointsLocal;
            foreach (var point in points)
            {
                hash = HashCode.Combine(hash, point.x, point.y, point.z);
            }

            hash = HashCode.Combine(hash, points.Length);
            return hash;
        }

        private static int HashMatrix(Matrix4x4 matrix)
        {
            int hash = 17;
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    hash = HashCode.Combine(hash, matrix[row, column]);
                }
            }

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

        private static int HashCurveState(AnimationCurve curve)
        {
            if (curve == null)
            {
                return 0;
            }

            int hash = HashCode.Combine(curve.preWrapMode, curve.postWrapMode, curve.length);
            var keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                hash = HashCode.Combine(
                    hash,
                    key.time,
                    key.value,
                    key.inTangent,
                    key.outTangent,
                    key.inWeight,
                    key.outWeight,
                    key.weightedMode);
            }

            return hash;
        }

        private bool EnsureAllBrushLayerDisplacementCapacity(int vertexCount)
        {
            if (_groups == null)
            {
                _hasIncompatibleBrushData = false;
                return true;
            }

            bool compatible = true;
            foreach (var group in _groups)
            {
                if (group == null) continue;
                foreach (var layer in group.LayersList)
                {
                    if (layer != null && layer.Type == MeshDeformerLayerType.Brush)
                    {
                        compatible &= layer.TryEnsureBrushDataCapacityPreservingExisting(vertexCount);
                    }
                }
            }

            _hasIncompatibleBrushData = !compatible;
            if (!compatible)
            {
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
            }

            return compatible;
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
                _lastBlendShapeHash = 0;
                _lastBakedBlendShapeHash = int.MinValue;
                _blendShapeOutputDirty = true;
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

        [ExcludeFromCodeCoverage]
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
            _lastBlendShapeHash = 0;
            _lastBakedBlendShapeHash = int.MinValue;
            _blendShapeOutputDirty = true;
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

            bool useBernstein = _cache != null &&
                                _cache.Interpolation == LatticeInterpolationMode.CubicBernstein &&
                                _cache.HasValidBernsteinWeights(entries.Length);
            if (useBernstein)
            {
                using var weightsNative = LatticeNativeArrayUtility.CreateCopy(
                    _cache.BernsteinWeights,
                    Allocator.TempJob);
                var bernsteinJob = new DeformBernsteinVerticesJob
                {
                    ControlPoints = controlNative,
                    Weights = weightsNative,
                    Grid = new int3(_cache.GridSize.x, _cache.GridSize.y, _cache.GridSize.z),
                    Result = outputNative
                };

                bernsteinJob.Schedule(entries.Length, 64).Complete();
            }
            else
            {
                var job = new DeformVerticesJob
                {
                    ControlPoints = controlNative,
                    Entries = entriesNative,
                    Result = outputNative
                };

                job.Schedule(entries.Length, 64).Complete();
            }

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

        private static float[] BuildBernsteinWeightsWithJobs(
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


        private bool EnsureCache(LatticeAsset settings, Vector3[] restVertices)
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

            int restVerticesHash = HashVertices(restVertices);
            LatticeInterpolationMode effectiveInterpolation = GetEffectiveInterpolation(settings);
            if (_cache.IsCompatibleWith(settings, mesh, restVerticesHash, effectiveInterpolation))
            {
                return true;
            }

            return RebuildCache(
                settings,
                mesh,
                restVertices,
                restVerticesHash,
                effectiveInterpolation);
        }

        private bool RebuildCache(
            LatticeAsset settings,
            Mesh mesh,
            Vector3[] restVertices,
            int restVerticesHash)
        {
            return RebuildCache(
                settings,
                mesh,
                restVertices,
                restVerticesHash,
                GetEffectiveInterpolation(settings));
        }

        private bool RebuildCache(
            LatticeAsset settings,
            Mesh mesh,
            Vector3[] restVertices,
            int restVerticesHash,
            LatticeInterpolationMode effectiveInterpolation)
        {
            if (settings == null || mesh == null || restVertices == null)
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

        private static LatticeInterpolationMode GetEffectiveInterpolation(LatticeAsset settings)
        {
            if (settings != null &&
                settings.Interpolation == LatticeInterpolationMode.CubicBernstein &&
                settings.UsesLegacyTrilinearInterpolation)
            {
                return LatticeInterpolationMode.Trilinear;
            }

            return settings?.Interpolation ?? LatticeInterpolationMode.Trilinear;
        }

        private static Bounds CalculateReferencedBounds(Mesh mesh, Vector3[] vertices, Bounds fallback)
        {
            if (mesh == null || vertices == null || vertices.Length == 0)
            {
                return fallback;
            }

            var bounds = new Bounds();
            bool hasPoint = false;

            int subMeshCount = mesh.subMeshCount;
            for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
            {
                var indices = mesh.GetIndices(subMesh);
                for (int i = 0; i < indices.Length; i++)
                {
                    int vertexIndex = indices[i];
                    if (vertexIndex < 0 || vertexIndex >= vertices.Length)
                    {
                        continue;
                    }

                    if (!hasPoint)
                    {
                        bounds = new Bounds(vertices[vertexIndex], Vector3.zero);
                        hasPoint = true;
                    }
                    else
                    {
                        bounds.Encapsulate(vertices[vertexIndex]);
                    }
                }
            }

            if (hasPoint)
            {
                return bounds;
            }

            bounds = new Bounds(vertices[0], Vector3.zero);
            for (int i = 1; i < vertices.Length; i++)
            {
                bounds.Encapsulate(vertices[i]);
            }

            return bounds;
        }

        private static int HashVertices(Vector3[] vertices)
        {
            if (vertices == null || vertices.Length == 0)
            {
                return 0;
            }

            int hash = vertices.Length;
            for (int i = 0; i < vertices.Length; i++)
            {
                var v = vertices[i];
                hash = HashCode.Combine(hash, v.x, v.y, v.z);
            }

            return hash;
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
                Barycentric = new float3(tx, ty, tz),
                NormalizedCoordinate = new float3(barycentric.x, barycentric.y, barycentric.z)
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

            if (_entries == null || _entries.Length == 0)
            {
                return false;
            }

            if (_vertexCount != mesh.vertexCount)
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
                !HasValidBernsteinWeights(mesh.vertexCount))
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
        public float3 NormalizedCoordinate;
    }
}
