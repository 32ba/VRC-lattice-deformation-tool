using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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

    public enum ClearanceHeatmapDisplayMode
    {
        PenetrationOnly = 0,
        WarningAndPenetration = 1,
        FullDistribution = 2
    }

    public enum ClearanceQueryMode
    {
        ReferenceNormal = 0,
        ClosedMesh = 1
    }

    public enum FitCorrectionScope
    {
        PenetrationOnly = 0,
        WarningThreshold = 1,
        TargetClearance = 2
    }

    public enum BlendShapeCompositionMode
    {
        Single = 0,
        Progressive = 1,
        Crossfade = 2
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
        [SerializeField, HideInInspector] private bool _isFitCorrection;
        [SerializeField, HideInInspector] private Renderer _fitCorrectionReferenceRenderer;
        [SerializeField, HideInInspector] private ClearanceQueryMode _fitCorrectionQueryMode;
        [SerializeField, HideInInspector] private FitCorrectionScope _fitCorrectionScope;
        [SerializeField, HideInInspector] private float _fitCorrectionWarningDistance;
        [SerializeField, HideInInspector] private float _fitCorrectionTargetDistance;
        [SerializeField, HideInInspector] private float _fitCorrectionMaximumMove;
        [SerializeField, HideInInspector] private bool _fitCorrectionUsedVertexMask;
        [SerializeField, HideInInspector] private float[] _fitCorrectionConstraintMask = Array.Empty<float>();
        [SerializeField, HideInInspector] private bool _fitCorrectionPinnedOpenBoundaries;
        [SerializeField, HideInInspector] private bool _fitCorrectionIsolatedComponents;
        [SerializeField, HideInInspector] private bool _fitCorrectionSmoothedSurface;
        [SerializeField, HideInInspector] private int _fitCorrectionSmoothingIterations;
        [SerializeField, HideInInspector] private float _fitCorrectionSmoothingStrength;
        [SerializeField, HideInInspector] private bool _fitCorrectionPreservedClearance;
        [SerializeField, HideInInspector] private bool _fitCorrectionUsedSymmetry;
        [SerializeField, HideInInspector] private int _fitCorrectionSymmetryAxis;
        [SerializeField, HideInInspector] private float _fitCorrectionSymmetryTolerance;
        [SerializeField] private AnimationCurve _blendShapeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField, HideInInspector] private bool _hasImportedBlendShapeFrameWeight;
        [SerializeField, HideInInspector] private float _importedBlendShapeFrameWeight;

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

        public bool IsFitCorrection => _isFitCorrection;
        public Renderer FitCorrectionReferenceRenderer => _fitCorrectionReferenceRenderer;
        public ClearanceQueryMode FitCorrectionQueryMode => _fitCorrectionQueryMode;
        public FitCorrectionScope FitCorrectionScope => _fitCorrectionScope;
        public float FitCorrectionWarningDistance => _fitCorrectionWarningDistance;
        public float FitCorrectionTargetDistance => _fitCorrectionTargetDistance;
        public float FitCorrectionMaximumMove => _fitCorrectionMaximumMove;
        public bool FitCorrectionUsedVertexMask => _fitCorrectionUsedVertexMask;
        public IReadOnlyList<float> FitCorrectionConstraintMask =>
            _fitCorrectionConstraintMask ?? (_fitCorrectionConstraintMask = Array.Empty<float>());
        public bool FitCorrectionPinnedOpenBoundaries => _fitCorrectionPinnedOpenBoundaries;
        public bool FitCorrectionIsolatedComponents => _fitCorrectionIsolatedComponents;
        public bool FitCorrectionSmoothedSurface => _fitCorrectionSmoothedSurface;
        public int FitCorrectionSmoothingIterations => _fitCorrectionSmoothingIterations;
        public float FitCorrectionSmoothingStrength => _fitCorrectionSmoothingStrength;
        public bool FitCorrectionPreservedClearance => _fitCorrectionPreservedClearance;
        public bool FitCorrectionUsedSymmetry => _fitCorrectionUsedSymmetry;
        public int FitCorrectionSymmetryAxis => _fitCorrectionSymmetryAxis;
        public float FitCorrectionSymmetryTolerance => _fitCorrectionSymmetryTolerance;

        public void ConfigureFitCorrection(
            Renderer referenceRenderer,
            ClearanceQueryMode queryMode,
            FitCorrectionScope scope,
            float warningDistance,
            float targetDistance,
            float maximumMove)
        {
            _isFitCorrection = true;
            _fitCorrectionReferenceRenderer = referenceRenderer;
            _fitCorrectionQueryMode = queryMode;
            _fitCorrectionScope = scope;
            _fitCorrectionWarningDistance = IsFinite(warningDistance) ? Mathf.Max(0f, warningDistance) : 0f;
            _fitCorrectionTargetDistance = IsFinite(targetDistance)
                ? Mathf.Max(_fitCorrectionWarningDistance, targetDistance)
                : _fitCorrectionWarningDistance;
            _fitCorrectionMaximumMove = IsFinite(maximumMove) ? Mathf.Max(0f, maximumMove) : 0f;
        }

        public void ConfigureFitCorrectionConstraints(
            bool useVertexMask,
            float[] constraintMask,
            bool pinOpenBoundaries,
            bool isolateComponents,
            bool smoothSurface,
            int smoothingIterations,
            float smoothingStrength,
            bool preserveClearance,
            bool useSymmetry,
            int symmetryAxis,
            float symmetryTolerance)
        {
            _fitCorrectionUsedVertexMask = useVertexMask;
            if (constraintMask == null)
            {
                _fitCorrectionConstraintMask = Array.Empty<float>();
            }
            else
            {
                _fitCorrectionConstraintMask = new float[constraintMask.Length];
                for (int vertex = 0; vertex < constraintMask.Length; vertex++)
                {
                    float value = constraintMask[vertex];
                    _fitCorrectionConstraintMask[vertex] = IsFinite(value) ? Mathf.Clamp01(value) : 0f;
                }
            }
            _fitCorrectionPinnedOpenBoundaries = pinOpenBoundaries;
            _fitCorrectionIsolatedComponents = isolateComponents;
            _fitCorrectionSmoothedSurface = smoothSurface;
            _fitCorrectionSmoothingIterations = Mathf.Max(0, smoothingIterations);
            _fitCorrectionSmoothingStrength = IsFinite(smoothingStrength)
                ? Mathf.Clamp01(smoothingStrength)
                : 0f;
            _fitCorrectionPreservedClearance = preserveClearance;
            _fitCorrectionUsedSymmetry = useSymmetry;
            _fitCorrectionSymmetryAxis = Mathf.Clamp(symmetryAxis, 0, 2);
            _fitCorrectionSymmetryTolerance = IsFinite(symmetryTolerance)
                ? Mathf.Max(1e-6f, symmetryTolerance)
                : 1e-4f;
        }

        internal void CopyFitCorrectionMetadataFrom(LatticeLayer source)
        {
            if (source == null || !source._isFitCorrection) return;
            ConfigureFitCorrection(
                source._fitCorrectionReferenceRenderer,
                source._fitCorrectionQueryMode,
                source._fitCorrectionScope,
                source._fitCorrectionWarningDistance,
                source._fitCorrectionTargetDistance,
                source._fitCorrectionMaximumMove);
            ConfigureFitCorrectionConstraints(
                source._fitCorrectionUsedVertexMask,
                source._fitCorrectionConstraintMask,
                source._fitCorrectionPinnedOpenBoundaries,
                source._fitCorrectionIsolatedComponents,
                source._fitCorrectionSmoothedSurface,
                source._fitCorrectionSmoothingIterations,
                source._fitCorrectionSmoothingStrength,
                source._fitCorrectionPreservedClearance,
                source._fitCorrectionUsedSymmetry,
                source._fitCorrectionSymmetryAxis,
                source._fitCorrectionSymmetryTolerance);
        }

        public bool HasImportedBlendShapeFrameWeight => _hasImportedBlendShapeFrameWeight;

        public float ImportedBlendShapeFrameWeight => _importedBlendShapeFrameWeight;

        internal void SetImportedBlendShapeFrameWeight(float frameWeight)
        {
            _hasImportedBlendShapeFrameWeight = true;
            _importedBlendShapeFrameWeight = frameWeight;
        }

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
            float.IsNaN(_weight) || float.IsInfinity(_weight) ||
            (_hasImportedBlendShapeFrameWeight &&
             (float.IsNaN(_importedBlendShapeFrameWeight) || float.IsInfinity(_importedBlendShapeFrameWeight))) ||
            (_isFitCorrection &&
             ((_fitCorrectionQueryMode != ClearanceQueryMode.ReferenceNormal &&
               _fitCorrectionQueryMode != ClearanceQueryMode.ClosedMesh) ||
              (_fitCorrectionScope != FitCorrectionScope.PenetrationOnly &&
               _fitCorrectionScope != FitCorrectionScope.WarningThreshold &&
               _fitCorrectionScope != FitCorrectionScope.TargetClearance) ||
              !IsFinite(_fitCorrectionWarningDistance) ||
              !IsFinite(_fitCorrectionTargetDistance) ||
              !IsFinite(_fitCorrectionMaximumMove) ||
              _fitCorrectionWarningDistance < 0f ||
              _fitCorrectionTargetDistance < _fitCorrectionWarningDistance ||
              _fitCorrectionMaximumMove < 0f ||
              HasMalformedFitCorrectionConstraints()));

        private bool HasMalformedFitCorrectionConstraints()
        {
            if (_fitCorrectionSmoothedSurface &&
                (_fitCorrectionSmoothingIterations < 0 ||
                 !IsFinite(_fitCorrectionSmoothingStrength) ||
                 _fitCorrectionSmoothingStrength < 0f ||
                 _fitCorrectionSmoothingStrength > 1f))
            {
                return true;
            }

            if (_fitCorrectionUsedSymmetry &&
                ((_fitCorrectionSymmetryAxis < 0 || _fitCorrectionSymmetryAxis > 2) ||
                 !IsFinite(_fitCorrectionSymmetryTolerance) ||
                 _fitCorrectionSymmetryTolerance < 1e-6f))
            {
                return true;
            }

            if (!_fitCorrectionUsedVertexMask) return false;
            if (_fitCorrectionConstraintMask == null ||
                _fitCorrectionConstraintMask.Length != SerializedBrushDisplacementCount)
            {
                return true;
            }

            for (int vertex = 0; vertex < _fitCorrectionConstraintMask.Length; vertex++)
            {
                float value = _fitCorrectionConstraintMask[vertex];
                if (!IsFinite(value) || value < 0f || value > 1f) return true;
            }
            return false;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        internal bool HasNonFiniteSerializedBrushDisplacements
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

                return false;
            }
        }

        internal bool HasInvalidSerializedVertexMask
        {
            get
            {
                if (_vertexMask != null)
                {
                    for (int i = 0; i < _vertexMask.Length; i++)
                    {
                        float value = _vertexMask[i];
                        if (!IsFinite(value) || value < 0f || value > 1f)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        internal bool HasNonFiniteSerializedVertexData =>
            HasNonFiniteSerializedBrushDisplacements || HasInvalidSerializedVertexMask;

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

            if (!IsFinite(displacement.x) || !IsFinite(displacement.y) || !IsFinite(displacement.z))
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

            if (!IsFinite(delta.x) || !IsFinite(delta.y) || !IsFinite(delta.z))
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

            if (!IsFinite(value))
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
        [SerializeField] private BlendShapeCompositionMode _blendShapeComposition = BlendShapeCompositionMode.Single;
        [NonSerialized] private List<LatticeLayer> _readOnlyLayerSource;
        [NonSerialized] private ReadOnlyCollection<LatticeLayer> _readOnlyLayers;

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

        /// <summary>
        /// Legacy mutable collection retained for source compatibility. Prefer
        /// <see cref="Layers"/> and the mutation methods on <see cref="LatticeDeformer"/>
        /// so cache invalidation and active-index maintenance cannot be bypassed.
        /// </summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public List<LatticeLayer> LayersList
        {
            get
            {
                if (_layers == null) _layers = new List<LatticeLayer>();
                return _layers;
            }
        }

        public IReadOnlyList<LatticeLayer> Layers
        {
            get
            {
                var layers = MutableLayers;
                if (_readOnlyLayers == null || !ReferenceEquals(_readOnlyLayerSource, layers))
                {
                    _readOnlyLayerSource = layers;
                    _readOnlyLayers = layers.AsReadOnly();
                }
                return _readOnlyLayers;
            }
        }

        internal List<LatticeLayer> MutableLayers
        {
            get
            {
                if (_layers == null) _layers = new List<LatticeLayer>();
                return _layers;
            }
        }

        internal List<LatticeLayer> SerializedLayers => _layers;

        internal int SerializedActiveLayerIndex => _activeLayerIndex;

        internal void SetSerializedActiveLayerIndex(int value) => _activeLayerIndex = value;

        internal bool HasMalformedSerializedMetadata =>
            (_blendShapeOutput != BlendShapeOutputMode.Disabled &&
             _blendShapeOutput != BlendShapeOutputMode.OutputAsBlendShape) ||
            (_blendShapeComposition != BlendShapeCompositionMode.Single &&
             _blendShapeComposition != BlendShapeCompositionMode.Progressive &&
             _blendShapeComposition != BlendShapeCompositionMode.Crossfade);

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

        public BlendShapeCompositionMode BlendShapeComposition
        {
            get => _blendShapeComposition;
            set => _blendShapeComposition = value;
        }

        public string EffectiveBlendShapeName(string fallback)
        {
            return string.IsNullOrWhiteSpace(_blendShapeName) ? fallback : _blendShapeName;
        }
    }

    [DisallowMultipleComponent]
    [ExecuteAlways]
    [AddComponentMenu("32ba/Mesh Deformer")]
    public partial class LatticeDeformer : MonoBehaviour
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
        [SerializeField] private DeformerDataSource _dataSource = DeformerDataSource.Embedded;
        [SerializeField] private MeshDeformerProfile _profile;

        [SerializeField] private SkinnedMeshRenderer _skinnedMeshRenderer;
        [SerializeField] private MeshFilter _meshFilter;
        [SerializeField] private bool _recalculateNormals = true;
        [SerializeField] private bool _recalculateTangents = false;
        [SerializeField] private bool _recalculateBounds = true;
        [SerializeField] private bool _recalculateBoneWeights = false;
        [SerializeField] private WeightTransferSettingsData _weightTransferSettings = new WeightTransferSettingsData();
        [SerializeField] private bool _showClearanceHeatmap;
        [SerializeField] private Renderer _clearanceReferenceRenderer;
        [SerializeField] private ClearanceQueryMode _clearanceQueryMode = ClearanceQueryMode.ReferenceNormal;
        [SerializeField] private ClearanceHeatmapDisplayMode _clearanceHeatmapDisplayMode = ClearanceHeatmapDisplayMode.WarningAndPenetration;
        [SerializeField] private float _clearanceWarningDistance = 0.005f;
        [SerializeField] private float _clearanceTargetDistance = 0.01f;
        [SerializeField] private int _clearanceDisplayStride = 1;
        [SerializeField] private float _clearanceUpdateInterval = 0.1f;
        [SerializeField] private ClearanceScanSet _clearanceScanSet;
        [SerializeField] private Transform _clearanceScanAvatarRoot;
        [SerializeField] private FitCorrectionScope _fitCorrectionScope = FitCorrectionScope.TargetClearance;
        [SerializeField] private float _fitCorrectionMaximumMove = 0.02f;
        [SerializeField] private bool _fitCorrectionUseVertexMask = true;
        [SerializeField] private bool _fitCorrectionPinOpenBoundaries = true;
        [SerializeField] private bool _fitCorrectionIsolateComponents = true;
        [SerializeField] private bool _fitCorrectionSmoothSurface = true;
        [SerializeField] private int _fitCorrectionSmoothingIterations = 2;
        [SerializeField] private float _fitCorrectionSmoothingStrength = 0.5f;
        [SerializeField] private bool _fitCorrectionPreserveClearance = true;
        [SerializeField] private bool _fitCorrectionUseSymmetry;
        [SerializeField] private int _fitCorrectionSymmetryAxis;
        [SerializeField] private float _fitCorrectionSymmetryTolerance = SymmetryVertexMapCache.DefaultTolerance;
        [SerializeField] private bool _fitCorrectionPreview = true;
        [SerializeField, HideInInspector] private bool _hasInitializedFromSource = false;
        [SerializeField, HideInInspector] private Mesh _serializedSourceMesh;
        [SerializeField, HideInInspector] private int _serializedSourceVertexCount;
        [SerializeField, HideInInspector] private int _serializedSourceTopologyHash;

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
        // Keep the currently selected slot in _cache for the deformation hot path.
        // Additional slots prevent layers with different lattice configurations from
        // evicting each other's interpolation data on every Deform call.
        [NonSerialized] private LatticeDeformerCache _cache = new LatticeDeformerCache();
        [NonSerialized] private List<LatticeDeformerCache> _cacheSlots = new List<LatticeDeformerCache>();
        [NonSerialized] private Mesh _runtimeMesh;
        [NonSerialized] private Mesh _sourceMesh;
        [NonSerialized] private int _lastBlendShapeHash;
        [NonSerialized] private int _lastBakedBlendShapeHash;
        [NonSerialized] private List<DeformerGroup> _profileGroups;
        [NonSerialized] private List<DeformerGroup> _blockedProfileGroups;
        [NonSerialized] private List<DeformerGroup> _readOnlyGroupSource;
        [NonSerialized] private ReadOnlyCollection<DeformerGroup> _readOnlyGroups;
        [NonSerialized] private string _profileFingerprint;
        [NonSerialized] private bool _blendShapeOutputDirty = true;
        [NonSerialized] private int _runtimeMeshRevision;
        [NonSerialized] private int _deformationDataRevision;
        [NonSerialized] private bool _isEnsuringLayerModelReady;
        [NonSerialized] private bool _hasIncompatibleBrushData;
        [NonSerialized] private List<Vector3> _sourceVertexScratch = new List<Vector3>();
        [NonSerialized] private List<Vector3> _sourceNormalScratch = new List<Vector3>();
        [NonSerialized] private List<Vector4> _sourceTangentScratch = new List<Vector4>();
        [NonSerialized] private Vector3[] _sourceVerticesBuffer = Array.Empty<Vector3>();
        [NonSerialized] private Vector3[] _directDeltasBuffer = Array.Empty<Vector3>();
        [NonSerialized] private Vector3[] _groupVerticesBuffer = Array.Empty<Vector3>();
        [NonSerialized] private Vector3[] _layerVerticesBuffer = Array.Empty<Vector3>();
        [NonSerialized] private Vector3[] _finalVerticesBuffer = Array.Empty<Vector3>();
        [NonSerialized] private Vector3[] _latticeOutputBuffer = Array.Empty<Vector3>();
        [NonSerialized] private List<GeneratedBlendShape> _generatedBlendShapeBuffer =
            new List<GeneratedBlendShape>();
        [NonSerialized] private Stack<List<Vector3[]>> _blendShapeCandidateListPool =
            new Stack<List<Vector3[]>>();
        [NonSerialized] private Stack<List<float>> _blendShapeWeightListPool =
            new Stack<List<float>>();
        [NonSerialized] private Stack<Vector3[]> _blendShapeDeltaPool = new Stack<Vector3[]>();
        [NonSerialized] private int _blendShapeDeltaPoolVertexCount = -1;
        [NonSerialized] private NativeArray<float3> _deformControlNative;
        [NonSerialized] private NativeArray<LatticeCacheEntry> _deformEntriesNative;
        [NonSerialized] private NativeArray<float3> _deformOutputNative;
        [NonSerialized] private NativeArray<float> _deformBernsteinWeightsNative;
        [NonSerialized] private LatticeCacheEntry[] _deformEntriesSource;
        [NonSerialized] private float[] _deformBernsteinWeightsSource;
        [NonSerialized] private DeformationDataMigrationStatus _migrationStatus =
            DeformationDataMigrationStatus.Uninitialized;
        private const int k_InterpolationCacheSlotCount = 4;
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
            public readonly BlendShapeCompositionMode Composition;
            public readonly IReadOnlyList<Vector3[]> Candidates;
            public readonly IReadOnlyList<float> CandidateWeights;

            public GeneratedBlendShape(string name, AnimationCurve curve, Vector3[] deltas)
                : this(name, curve, BlendShapeCompositionMode.Single, new[] { deltas }, null)
            {
            }

            public GeneratedBlendShape(
                string name,
                AnimationCurve curve,
                BlendShapeCompositionMode composition,
                IReadOnlyList<Vector3[]> candidates,
                IReadOnlyList<float> candidateWeights = null)
            {
                Name = name;
                Curve = curve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);
                Composition = composition;
                Candidates = candidates;
                CandidateWeights = candidateWeights;
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

        public DeformerDataSource DataSource
        {
            get => _dataSource;
            set
            {
                if (_dataSource == value) return;
                if (value == DeformerDataSource.Profile && _profile != null &&
                    EvaluateProfileCompatibility(_profile) == ProfileCompatibilityStatus.TopologyMismatch)
                {
                    return;
                }
                _dataSource = value;
                if (_dataSource == DeformerDataSource.Profile && _profile != null)
                {
                    _groups?.Clear();
                }
                _profileFingerprint = null;
                EnsureGroups();
                InvalidateCache();
            }
        }

        public MeshDeformerProfile Profile
        {
            get => _profile;
            set
            {
                if (_profile == value) return;
                if (_dataSource == DeformerDataSource.Profile && value != null &&
                    EvaluateProfileCompatibility(value) == ProfileCompatibilityStatus.TopologyMismatch)
                {
                    return;
                }
                _profile = value;
                if (_dataSource == DeformerDataSource.Profile && _profile != null)
                {
                    _groups?.Clear();
                }
                _profileFingerprint = null;
                EnsureGroups();
                InvalidateCache();
            }
        }

        public bool UseProfile(MeshDeformerProfile profile)
        {
            if (profile == null) return false;
            if (EvaluateProfileCompatibility(profile) == ProfileCompatibilityStatus.TopologyMismatch) return false;
            _profile = profile;
            _dataSource = DeformerDataSource.Profile;
            _groups?.Clear();
            _profileGroups = null;
            _blockedProfileGroups = null;
            _profileFingerprint = null;
            EnsureGroups();
            InvalidateCache();
            return true;
        }

        public bool CopyProfileToEmbedded()
        {
            if (_profile == null) return false;
            if (EvaluateProfileCompatibility(_profile) == ProfileCompatibilityStatus.TopologyMismatch)
                return false;
            var payload = _profile.CreateIndependentPayload();
            _groups = payload.Groups;
            _activeGroupIndex = payload.ActiveGroupIndex;
            _dataSource = DeformerDataSource.Embedded;
            _profileGroups = null;
            _blockedProfileGroups = null;
            _profileFingerprint = null;
            EnsureGroups();
            InvalidateCache();
            return true;
        }

        public bool SaveToProfile(MeshDeformerProfile destination)
        {
            if (destination == null) return false;
            CacheSourceMesh();
            if (_dataSource == DeformerDataSource.Profile && _profile != null &&
                EvaluateProfileCompatibility(_profile) == ProfileCompatibilityStatus.TopologyMismatch)
            {
                return false;
            }
            EnsureGroups();
            destination.Capture(GetGroupStorage(), _activeGroupIndex, _sourceMesh);
            if (_profile == destination)
            {
                _profileFingerprint = null;
            }
            return true;
        }

        public ProfileCompatibilityStatus EvaluateProfileCompatibility(
            MeshDeformerProfile profile = null,
            string assetGuid = "",
            long assetLocalId = 0)
        {
            var targetProfile = profile != null ? profile : _profile;
            if (targetProfile == null) return ProfileCompatibilityStatus.InsufficientMetadata;
            Mesh compatibilitySource = GetCompatibilitySourceMesh();
            // A Mesh can be mutated in place without changing its reference or vertex count.
            // Re-evaluate its exact vertex/index fingerprint so stale compatibility results
            // can never authorize vertex-indexed profile data for a changed topology.
            return targetProfile.EvaluateCompatibility(
                compatibilitySource,
                assetGuid ?? "",
                assetLocalId);
        }

        public IReadOnlyList<DeformerGroup> Groups
        {
            get
            {
                if (!EnsureGroups()) return Array.Empty<DeformerGroup>();
                var groups = GetGroupStorage();
                if (_readOnlyGroups == null || !ReferenceEquals(_readOnlyGroupSource, groups))
                {
                    _readOnlyGroupSource = groups;
                    _readOnlyGroups = groups.AsReadOnly();
                }
                return _readOnlyGroups;
            }
        }

        public int GroupCount
        {
            get
            {
                return EnsureGroups() ? GetGroupStorage().Count : 0;
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
                var groups = GetGroupStorage();
                _activeGroupIndex = groups.Count > 0 ? Mathf.Clamp(value, 0, groups.Count - 1) : 0;
            }
        }

        public DeformerGroup ActiveGroup
        {
            get
            {
                if (!EnsureGroups()) return null;
                var groups = GetGroupStorage();
                if (groups.Count == 0) return null;
                return groups[Mathf.Clamp(_activeGroupIndex, 0, groups.Count - 1)];
            }
        }

        public int AddGroup(string groupName = null)
        {
            if (!EnsureGroups()) return -1;
            var groups = GetGroupStorage();
            var group = new DeformerGroup();
            group.Name = string.IsNullOrWhiteSpace(groupName) ? GenerateNextGroupName() : groupName;
            groups.Add(group);
            _activeGroupIndex = groups.Count - 1;
            return _activeGroupIndex;
        }

        public bool RemoveGroup(int index)
        {
            if (!EnsureGroups()) return false;
            var groups = GetGroupStorage();
            if (index < 0 || index >= groups.Count || groups.Count <= 1)
                return false;

            groups.RemoveAt(index);
            if (_activeGroupIndex == index)
                _activeGroupIndex = Mathf.Min(index, groups.Count - 1);
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

        /// <summary>
        /// Returns the already-validated embedded active layer without repeating the
        /// release-migration payload preflight. Editor tools call this once per GUI
        /// event after OnEnable/activation has validated the component. Unknown,
        /// migrated, invalid, and Profile-backed states retain the full fail-closed
        /// public accessor path.
        /// </summary>
        internal bool TryGetActiveLayerFast(out LatticeLayer layer)
        {
            layer = null;
            if (!TryGetValidatedEmbeddedActiveGroup(out var group))
            {
                return TryGetActiveLayer(out layer);
            }

            var layers = group.SerializedLayers;
            int index = group.SerializedActiveLayerIndex;
            if (layers == null || index < 0 || index >= layers.Count)
            {
                return false;
            }

            layer = layers[index];
            return layer != null;
        }

        internal IReadOnlyList<LatticeLayer> GetActiveLayersFast()
        {
            return TryGetValidatedEmbeddedActiveGroup(out var group)
                ? group.SerializedLayers
                : Layers;
        }

        internal int GetActiveLayerIndexFast()
        {
            return TryGetValidatedEmbeddedActiveGroup(out var group)
                ? group.SerializedActiveLayerIndex
                : ActiveLayerIndex;
        }

        private bool TryGetValidatedEmbeddedActiveGroup(out DeformerGroup group)
        {
            group = null;
            if (_migrationStatus != DeformationDataMigrationStatus.Ready ||
                _deformationDataVersion != DeformationDataVersion.CurrentDevelopment ||
                _layerModelVersion != k_CurrentLayerModelVersion ||
                _dataSource != DeformerDataSource.Embedded ||
                _groups == null ||
                _activeGroupIndex < 0 ||
                _activeGroupIndex >= _groups.Count)
            {
                return false;
            }

            group = _groups[_activeGroupIndex];
            return group != null;
        }

        public Mesh RuntimeMesh => _runtimeMesh;

        internal int RuntimeMeshRevision => _runtimeMeshRevision;
        internal int DeformationDataRevision => _deformationDataRevision;

        public Mesh SourceMesh => _sourceMesh;

        public Renderer TargetRenderer
        {
            get
            {
                if (_skinnedMeshRenderer != null) return _skinnedMeshRenderer;
                return _meshFilter != null ? _meshFilter.GetComponent<MeshRenderer>() : null;
            }
        }

        public bool ShowClearanceHeatmap
        {
            get => _showClearanceHeatmap;
            set => _showClearanceHeatmap = value;
        }

        public Renderer ClearanceReferenceRenderer
        {
            get => _clearanceReferenceRenderer;
            set => _clearanceReferenceRenderer = value;
        }

        public ClearanceQueryMode ClearanceQueryMode
        {
            get => _clearanceQueryMode;
            set => _clearanceQueryMode = value;
        }

        public ClearanceHeatmapDisplayMode ClearanceHeatmapDisplayMode
        {
            get => _clearanceHeatmapDisplayMode;
            set => _clearanceHeatmapDisplayMode = value;
        }

        public float ClearanceWarningDistance
        {
            get => IsFinite(_clearanceWarningDistance) ? Mathf.Max(0f, _clearanceWarningDistance) : 0f;
            set => _clearanceWarningDistance = IsFinite(value) ? Mathf.Max(0f, value) : 0f;
        }

        public float ClearanceTargetDistance
        {
            get => IsFinite(_clearanceTargetDistance)
                ? Mathf.Max(ClearanceWarningDistance, _clearanceTargetDistance)
                : ClearanceWarningDistance;
            set => _clearanceTargetDistance = IsFinite(value)
                ? Mathf.Max(ClearanceWarningDistance, value)
                : ClearanceWarningDistance;
        }

        public int ClearanceDisplayStride
        {
            get => Mathf.Clamp(_clearanceDisplayStride, 1, 64);
            set => _clearanceDisplayStride = Mathf.Clamp(value, 1, 64);
        }

        public float ClearanceUpdateInterval
        {
            get => IsFinite(_clearanceUpdateInterval)
                ? Mathf.Clamp(_clearanceUpdateInterval, 0.02f, 2f)
                : 0.1f;
            set => _clearanceUpdateInterval = IsFinite(value)
                ? Mathf.Clamp(value, 0.02f, 2f)
                : 0.1f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public FitCorrectionScope FitCorrectionScope
        {
            get => _fitCorrectionScope;
            set => _fitCorrectionScope = value;
        }

        public float FitCorrectionMaximumMove
        {
            get => IsFinite(_fitCorrectionMaximumMove) ? Mathf.Max(0f, _fitCorrectionMaximumMove) : 0f;
            set => _fitCorrectionMaximumMove = IsFinite(value) ? Mathf.Max(0f, value) : 0f;
        }

        public ClearanceScanSet ClearanceScanSet
        {
            get => _clearanceScanSet;
            set => _clearanceScanSet = value;
        }

        public Transform ClearanceScanAvatarRoot
        {
            get => _clearanceScanAvatarRoot;
            set => _clearanceScanAvatarRoot = value;
        }
        public bool FitCorrectionUseVertexMask { get => _fitCorrectionUseVertexMask; set => _fitCorrectionUseVertexMask = value; }
        public bool FitCorrectionPinOpenBoundaries { get => _fitCorrectionPinOpenBoundaries; set => _fitCorrectionPinOpenBoundaries = value; }
        public bool FitCorrectionIsolateComponents { get => _fitCorrectionIsolateComponents; set => _fitCorrectionIsolateComponents = value; }
        public bool FitCorrectionSmoothSurface { get => _fitCorrectionSmoothSurface; set => _fitCorrectionSmoothSurface = value; }
        public int FitCorrectionSmoothingIterations { get => Mathf.Max(0, _fitCorrectionSmoothingIterations); set => _fitCorrectionSmoothingIterations = Mathf.Max(0, value); }
        public float FitCorrectionSmoothingStrength { get => IsFinite(_fitCorrectionSmoothingStrength) ? Mathf.Clamp01(_fitCorrectionSmoothingStrength) : 0f; set => _fitCorrectionSmoothingStrength = IsFinite(value) ? Mathf.Clamp01(value) : 0f; }
        public bool FitCorrectionPreserveClearance { get => _fitCorrectionPreserveClearance; set => _fitCorrectionPreserveClearance = value; }
        public bool FitCorrectionUseSymmetry { get => _fitCorrectionUseSymmetry; set => _fitCorrectionUseSymmetry = value; }
        public int FitCorrectionSymmetryAxis { get => Mathf.Clamp(_fitCorrectionSymmetryAxis, 0, 2); set => _fitCorrectionSymmetryAxis = Mathf.Clamp(value, 0, 2); }
        public float FitCorrectionSymmetryTolerance { get => IsFinite(_fitCorrectionSymmetryTolerance) ? Mathf.Max(1e-6f, _fitCorrectionSymmetryTolerance) : 1e-4f; set => _fitCorrectionSymmetryTolerance = IsFinite(value) ? Mathf.Max(1e-6f, value) : 1e-4f; }
        public bool FitCorrectionPreview { get => _fitCorrectionPreview; set => _fitCorrectionPreview = value; }

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

        public BlendShapeCompositionMode BlendShapeComposition
        {
            get
            {
                var group = ActiveGroup;
                return group?.BlendShapeComposition ?? BlendShapeCompositionMode.Single;
            }
            set
            {
                var group = ActiveGroup;
                if (group != null) group.BlendShapeComposition = value;
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
            duplicate.CopyFitCorrectionMetadataFrom(sourceLayer);
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

        public int ImportBlendShapeAllFramesAsGroup(int blendShapeIndex)
        {
            if (_sourceMesh == null) return -1;
            if (blendShapeIndex < 0 || blendShapeIndex >= _sourceMesh.blendShapeCount) return -1;

            int frameCount = _sourceMesh.GetBlendShapeFrameCount(blendShapeIndex);
            int vertexCount = _sourceMesh.vertexCount;
            if (frameCount <= 0 || vertexCount <= 0) return -1;

            string shapeName = _sourceMesh.GetBlendShapeName(blendShapeIndex);
            var importedLayers = new List<LatticeLayer>(frameCount);
            float previousWeight = float.NegativeInfinity;

            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                float frameWeight = _sourceMesh.GetBlendShapeFrameWeight(blendShapeIndex, frameIndex);
                if (float.IsNaN(frameWeight) || float.IsInfinity(frameWeight) || frameWeight <= previousWeight)
                    return -1;

                var deltaVertices = new Vector3[vertexCount];
                _sourceMesh.GetBlendShapeFrameVertices(
                    blendShapeIndex,
                    frameIndex,
                    deltaVertices,
                    new Vector3[vertexCount],
                    new Vector3[vertexCount]);

                for (int vertex = 0; vertex < vertexCount; vertex++)
                {
                    Vector3 delta = deltaVertices[vertex];
                    if (float.IsNaN(delta.x) || float.IsInfinity(delta.x) ||
                        float.IsNaN(delta.y) || float.IsInfinity(delta.y) ||
                        float.IsNaN(delta.z) || float.IsInfinity(delta.z))
                    {
                        return -1;
                    }
                }

                var layer = new LatticeLayer
                {
                    Name = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} [{1:0.###}]",
                        shapeName,
                        frameWeight),
                    Weight = 1f
                };
                layer.SetType(MeshDeformerLayerType.Brush);
                layer.BrushDisplacements = deltaVertices;
                layer.SetImportedBlendShapeFrameWeight(frameWeight);
                importedLayers.Add(layer);
                previousWeight = frameWeight;
            }

            if (!EnsureGroups()) return -1;

            var group = new DeformerGroup
            {
                Name = shapeName + " Imported",
                BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape,
                BlendShapeName = shapeName + " Imported",
                BlendShapeComposition = BlendShapeCompositionMode.Crossfade,
                BlendShapeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f)
            };
            group.LayersList.AddRange(importedLayers);
            group.ActiveLayerIndex = 0;

            _groups.Add(group);
            _activeGroupIndex = _groups.Count - 1;
            return _activeGroupIndex;
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
                    float coord = SymmetryVertexMapCache.GetSignedDistance(vertices[i], axis);
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
                var mirrorMap = SymmetryVertexMapCache.GetOrCreate(
                    _sourceMesh,
                    axis,
                    unmatchedBehavior: UnmatchedSymmetryVertexBehavior.Self);
                var newDisplacements = new Vector3[vertexCount];
                var masks = layer.VertexMask;
                bool hasMask = masks.Length == vertexCount;
                var newMasks = hasMask ? new float[vertexCount] : null;

                for (int i = 0; i < vertexCount; i++)
                {
                    var displacement = SymmetryVertexMapCache.MirrorDirection(
                        displacements[mirrorMap[i]], axis);

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
            var groups = GetGroupStorage();

            int hash = 17;
            hash = HashCode.Combine(hash, _legacyAbsoluteLatticeEvaluation);
            hash = HashCode.Combine(hash, _legacyPublishedBlendShapeSemantics);
            hash = HashCode.Combine(hash, (int)_deformationDataVersion);
            hash = HashCode.Combine(hash, groups.Count);
            hash = HashCode.Combine(hash, _activeGroupIndex);
            hash = HashCode.Combine(hash, (gameObject.name ?? "").GetHashCode());
            hash = HashCode.Combine(hash, _recalculateNormals);
            hash = HashCode.Combine(hash, _recalculateTangents);
            hash = HashCode.Combine(hash, _recalculateBounds);

            foreach (var group in groups)
            {
                if (group == null) { hash = HashCode.Combine(hash, 0); continue; }
                hash = HashCode.Combine(hash, (group.Name ?? "").GetHashCode());
                hash = HashCode.Combine(hash, group.Enabled);
                hash = HashCode.Combine(hash, (int)group.BlendShapeOutput);
                hash = HashCode.Combine(hash, (int)group.BlendShapeComposition);
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
                    hash = HashCode.Combine(hash, layer.HasImportedBlendShapeFrameWeight);
                    if (layer.HasImportedBlendShapeFrameWeight)
                        hash = HashCode.Combine(hash, layer.ImportedBlendShapeFrameWeight);
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
            ReleaseDeformationNativeBuffers();
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
            ReleaseDeformationNativeBuffers();
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

            if (_sourceMesh == null || !_sourceMesh.isReadable)
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
            int sourceVerticesHash = HashVertices(sourceVertices);
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
            EnsureManagedDeformationBuffers(vertexCount);
            var directDeltas = _directDeltasBuffer;
            Array.Clear(directDeltas, 0, vertexCount);
            // Collect generated BlendShapes from groups and individual layers.
            var generatedBlendShapes = _generatedBlendShapeBuffer;
            generatedBlendShapes.Clear();
            var groups = GetGroupStorage();

            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                if (group == null || !group.Enabled) continue;

                var groupVertices = _groupVerticesBuffer;
                Array.Copy(sourceVertices, groupVertices, vertexCount);
                var layers = group.LayersList;
                bool stagedGroupOutput =
                    group.BlendShapeOutput == BlendShapeOutputMode.OutputAsBlendShape &&
                    group.BlendShapeComposition != BlendShapeCompositionMode.Single;
                List<Vector3[]> stageCandidates = null;
                List<float> stageCandidateWeights = null;
                if (stagedGroupOutput)
                {
                    stageCandidates = RentBlendShapeCandidateList();
                    stageCandidateWeights = RentBlendShapeWeightList();
                }
                bool preserveCandidateWeights = stagedGroupOutput;

                for (int i = 0; i < layers.Count; i++)
                {
                    var layer = layers[i];
                    if (layer == null || !layer.Enabled || layer.Weight <= 0f) continue;

                    if (!_legacyPublishedBlendShapeSemantics &&
                        layer.BlendShapeOutput == BlendShapeOutputMode.OutputAsBlendShape)
                    {
                        var layerVertices = _layerVerticesBuffer;
                        Array.Copy(sourceVertices, layerVertices, vertexCount);
                        TryApplyLayerContribution(
                            layer,
                            sourceVertices,
                            sourceVerticesHash,
                            layerVertices);
                        if (TryBuildPooledDeltas(sourceVertices, layerVertices, out var layerDeltas))
                        {
                            generatedBlendShapes.Add(CreatePooledGeneratedBlendShape(
                                layer.EffectiveBlendShapeName,
                                layer.BlendShapeCurve,
                                layerDeltas));
                        }

                        continue;
                    }

                    if (stagedGroupOutput)
                    {
                        var layerVertices = _layerVerticesBuffer;
                        Array.Copy(sourceVertices, layerVertices, vertexCount);
                        TryApplyLayerContribution(
                            layer,
                            sourceVertices,
                            sourceVerticesHash,
                            layerVertices);
                        if (TryBuildPooledDeltas(
                                sourceVertices,
                                layerVertices,
                                out var stageDeltas,
                                !layer.HasImportedBlendShapeFrameWeight))
                        {
                            stageCandidates.Add(stageDeltas);
                            if (layer.HasImportedBlendShapeFrameWeight)
                                stageCandidateWeights.Add(layer.ImportedBlendShapeFrameWeight);
                            else
                                preserveCandidateWeights = false;
                        }
                    }
                    else
                    {
                        TryApplyLayerContribution(
                            layer,
                            sourceVertices,
                            sourceVerticesHash,
                            groupVertices);
                    }
                }

                if (group.BlendShapeOutput == BlendShapeOutputMode.OutputAsBlendShape)
                {
                    if (stagedGroupOutput && stageCandidates.Count > 0)
                    {
                        IReadOnlyList<float> candidateWeights =
                            group.BlendShapeComposition == BlendShapeCompositionMode.Crossfade &&
                            preserveCandidateWeights &&
                                                   HaveStrictlyIncreasingWeights(stageCandidateWeights)
                            ? stageCandidateWeights
                            : null;
                        generatedBlendShapes.Add(new GeneratedBlendShape(
                            group.EffectiveBlendShapeName(gameObject.name),
                            group.BlendShapeCurve,
                            group.BlendShapeComposition,
                            stageCandidates,
                            candidateWeights));
                        if (candidateWeights == null)
                        {
                            ReturnBlendShapeWeightList(stageCandidateWeights);
                        }
                        stageCandidates = null;
                        stageCandidateWeights = null;
                    }
                    else if (!stagedGroupOutput &&
                             TryBuildPooledDeltas(sourceVertices, groupVertices, out var groupDeltas))
                    {
                        generatedBlendShapes.Add(CreatePooledGeneratedBlendShape(
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

                ReturnBlendShapeCandidateList(stageCandidates);
                ReturnBlendShapeWeightList(stageCandidateWeights);
            }

            // Apply direct deltas
            var finalVertices = _finalVerticesBuffer;
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
                        AddGeneratedBlendShapeFrames(mesh, shapeName, finalVertices, generated);
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

            unchecked
            {
                _runtimeMeshRevision++;
            }

            ReleaseGeneratedBlendShapeCandidates(generatedBlendShapes);
            UnityEngine.Profiling.Profiler.EndSample();
            return mesh;
        }

        private void RestoreSourceNormals(Mesh mesh)
        {
            if (mesh == null || _sourceMesh == null)
            {
                return;
            }

            _sourceNormalScratch ??= new List<Vector3>(mesh.vertexCount);
            _sourceMesh.GetNormals(_sourceNormalScratch);
            if (_sourceNormalScratch.Count == mesh.vertexCount)
            {
                mesh.SetNormals(_sourceNormalScratch);
            }
            else
            {
                mesh.normals = Array.Empty<Vector3>();
            }
        }

        private void RestoreSourceTangents(Mesh mesh)
        {
            if (mesh == null || _sourceMesh == null)
            {
                return;
            }

            _sourceTangentScratch ??= new List<Vector4>(mesh.vertexCount);
            _sourceMesh.GetTangents(_sourceTangentScratch);
            if (_sourceTangentScratch.Count == mesh.vertexCount)
            {
                mesh.SetTangents(_sourceTangentScratch);
            }
            else
            {
                mesh.tangents = Array.Empty<Vector4>();
            }
        }

        private void TryApplyLayerContribution(
            LatticeLayer layer,
            Vector3[] sourceVertices,
            Vector3[] deformedVertices)
        {
            TryApplyLayerContribution(
                layer,
                sourceVertices,
                HashVertices(sourceVertices),
                deformedVertices);
        }

        private void TryApplyLayerContribution(
            LatticeLayer layer,
            Vector3[] sourceVertices,
            int sourceVerticesHash,
            Vector3[] deformedVertices)
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
                    TryApplyLatticeLayerContribution(
                        layer,
                        sourceVertices,
                        sourceVerticesHash,
                        deformedVertices);
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
            TryApplyLatticeLayerContribution(
                layer,
                sourceVertices,
                HashVertices(sourceVertices),
                deformedVertices);
        }

        private void TryApplyLatticeLayerContribution(
            LatticeLayer layer,
            Vector3[] sourceVertices,
            int sourceVerticesHash,
            Vector3[] deformedVertices)
        {
            if (layer == null || sourceVertices == null || deformedVertices == null)
            {
                return;
            }

            var layerSettings = layer.Settings;
            if (layerSettings == null ||
                !EnsureCache(layerSettings, sourceVertices, sourceVerticesHash))
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
            NotifyDeformationDataChanged();

            if (_cache == null)
            {
                _cache = new LatticeDeformerCache();
            }

            _cache.Clear();
            if (_cacheSlots != null)
            {
                for (int i = 0; i < _cacheSlots.Count; i++)
                {
                    var slot = _cacheSlots[i];
                    if (slot != null && !ReferenceEquals(slot, _cache))
                    {
                        slot.Clear();
                    }
                }

                _cacheSlots.Clear();
            }
            ReleaseDeformationNativeBuffers();
            _lastBlendShapeHash = 0;
            _blendShapeOutputDirty = true;
        }

        internal void NotifyDeformationDataChanged()
        {
            unchecked
            {
                _deformationDataRevision++;
            }
        }

        public void InitializeFromSource(bool resetControlPoints)
        {
            if (!EnsureGroups()) return;
            EnsureSettings();
            if (_sourceMesh == null) return;

            // Adding/enabling the component must remain safe for imported meshes whose
            // Read/Write flag is disabled. Deformation fails closed until it is enabled;
            // initialization can use the serialized Mesh bounds without touching CPU
            // vertex/index buffers.
            var sourceVertices = _sourceMesh.isReadable
                ? BuildCurrentSourceVertices(out _, out _, out _)
                : null;
            var meshBounds = CalculateReferencedBounds(_sourceMesh, sourceVertices, _sourceMesh.bounds);
            foreach (var group in GetGroupStorage())
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
            // Keep automatic initialization pending when only the coarse serialized
            // bounds were available. A later mesh reimport with Read/Write enabled can
            // then rebuild referenced-vertex and active-BlendShape bounds.
            _hasInitializedFromSource = _sourceMesh.isReadable;
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
            var groups = GetGroupStorage();

            // Create default group if none exist
            if (groups.Count == 0)
            {
                var defaultGroup = new DeformerGroup();
                defaultGroup.Name = "Group";
                defaultGroup.LayersList.Add(new LatticeLayer
                {
                    Name = k_PrimaryLayerName,
                    Enabled = true,
                    Weight = 1f,
                });
                groups.Add(defaultGroup);
                _activeGroupIndex = 0;
            }

            // Ensure each group's layers are valid
            foreach (var group in groups)
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

            _activeGroupIndex = groups.Count > 0 ? Mathf.Clamp(_activeGroupIndex, 0, groups.Count - 1) : 0;

            if (_sourceMesh != null)
                EnsureAllBrushLayerDisplacementCapacity(_sourceMesh.vertexCount);
        }

        private List<DeformerGroup> GetGroupStorage()
        {
            if (_groups == null) _groups = new List<DeformerGroup>();
            if (_dataSource != DeformerDataSource.Profile || _profile == null)
            {
                return _groups;
            }

            if (EvaluateProfileCompatibility(_profile) == ProfileCompatibilityStatus.TopologyMismatch)
            {
                _profileGroups = null;
                _profileFingerprint = null;
                if (_blockedProfileGroups == null)
                {
                    _blockedProfileGroups = new List<DeformerGroup>
                    {
                        new DeformerGroup
                        {
                            Name = "Incompatible Profile",
                            Enabled = false
                        }
                    };
                }
                return _blockedProfileGroups;
            }

            if (_groups.Count > 0)
            {
                _groups.Clear();
            }

            string fingerprint = _profile.GetContentFingerprint();
            if (_profileGroups == null || !string.Equals(_profileFingerprint, fingerprint, StringComparison.Ordinal))
            {
                var payload = _profile.CreateIndependentPayload();
                _profileGroups = payload.Groups;
                _blockedProfileGroups = null;
                _activeGroupIndex = payload.ActiveGroupIndex;
                _profileFingerprint = fingerprint;
                InvalidateCache();
            }

            return _profileGroups;
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
                _serializedSourceVertexCount = nextSource != null ? nextSource.vertexCount : 0;
                _serializedSourceTopologyHash = CalculateSourceTopologyHash(nextSource);
                _hasInitializedFromSource = false;
            }
            else if (nextSource != null && _serializedSourceVertexCount == 0 && _serializedSourceTopologyHash == 0)
            {
                // Establish a baseline for assets saved before validation metadata existed.
                _serializedSourceVertexCount = nextSource.vertexCount;
                _serializedSourceTopologyHash = CalculateSourceTopologyHash(nextSource);
            }

            if (!meshChanged)
            {
                return;
            }

            InvalidateCache();
            ReleaseRuntimeMesh();
            EnsureAllBrushLayerDisplacementCapacity(_sourceMesh != null ? _sourceMesh.vertexCount : 0);
        }

        private static int CalculateSourceTopologyHash(Mesh mesh)
        {
            if (mesh == null) return 0;
            try
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + mesh.vertexCount;
                    hash = hash * 31 + mesh.subMeshCount;
                    using Mesh.MeshDataArray meshDataArray = Mesh.AcquireReadOnlyMeshData(mesh);
                    Mesh.MeshData data = meshDataArray[0];
                    bool use16Bit = mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt16;
                    NativeArray<ushort> indices16 = use16Bit
                        ? data.GetIndexData<ushort>()
                        : default;
                    NativeArray<uint> indices32 = !use16Bit
                        ? data.GetIndexData<uint>()
                        : default;
                    for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                    {
                        UnityEngine.Rendering.SubMeshDescriptor descriptor = data.GetSubMesh(subMesh);
                        hash = hash * 31 + (int)descriptor.topology;
                        hash = hash * 31 + descriptor.indexCount;
                        int end = descriptor.indexStart + descriptor.indexCount;
                        for (int index = descriptor.indexStart; index < end; index++)
                        {
                            int value = use16Bit
                                ? indices16[index] + descriptor.baseVertex
                                : unchecked((int)indices32[index]) + descriptor.baseVertex;
                            hash = hash * 31 + value;
                        }
                    }
                    return hash;
                }
            }
            catch
            {
                return 0;
            }
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

        private Mesh GetCompatibilitySourceMesh()
        {
            Mesh sharedMesh = GetSharedSourceMesh();
            // Deform(true) assigns the generated runtime mesh to the renderer. Profile
            // compatibility is defined against the original source in that situation.
            if (_runtimeMesh != null && ReferenceEquals(sharedMesh, _runtimeMesh))
            {
                return _sourceMesh;
            }

            // This method is intentionally read-only. A rejected compatibility check must
            // not synchronize serialized source state or release an existing preview mesh.
            return sharedMesh != null ? sharedMesh : (_sourceMesh != null ? _sourceMesh : _serializedSourceMesh);
        }

        private void TryAutoConfigureSettings()
        {
            // Reset performs the one safe coarse-bounds initialization for an unreadable
            // mesh. Do not repeat it from accessors/validation while Read/Write is disabled;
            // keep the pending flag so a later readable reimport can perform the full pass.
            if (_sourceMesh == null || !_sourceMesh.isReadable)
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
            var groups = GetGroupStorage();
            string baseName = "Group";
            bool baseExists = false;
            for (int i = 0; i < groups.Count; i++)
                if (groups[i] != null && string.Equals(groups[i].Name, baseName, StringComparison.OrdinalIgnoreCase))
                { baseExists = true; break; }
            if (!baseExists) return baseName;

            int number = 1;
            while (true)
            {
                string candidate = $"{baseName} {number}";
                bool exists = false;
                for (int i = 0; i < groups.Count; i++)
                    if (groups[i] != null && string.Equals(groups[i].Name, candidate, StringComparison.OrdinalIgnoreCase))
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
            foreach (var group in GetGroupStorage())
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

        // Compatibility/testing entry point. Production hot paths pass a reusable result
        // buffer to the overload below and therefore avoid this allocation.
        private Vector3[] DeformWithJobs(
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

        private Vector3[] DeformWithJobs(
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

        private void EnsureDeformationNativeBuffers(
            LatticeCacheEntry[] entries,
            int controlPointCount,
            bool useBernstein)
        {
            if (!_deformControlNative.IsCreated || _deformControlNative.Length != controlPointCount)
            {
                if (_deformControlNative.IsCreated) _deformControlNative.Dispose();
                _deformControlNative = LatticeNativeArrayUtility.CreateFloat3Array(
                    controlPointCount,
                    Allocator.Persistent);
            }

            if (!_deformOutputNative.IsCreated || _deformOutputNative.Length != entries.Length)
            {
                if (_deformOutputNative.IsCreated) _deformOutputNative.Dispose();
                _deformOutputNative = LatticeNativeArrayUtility.CreateFloat3Array(
                    entries.Length,
                    Allocator.Persistent);
            }

            if (!_deformEntriesNative.IsCreated ||
                _deformEntriesNative.Length != entries.Length ||
                !ReferenceEquals(_deformEntriesSource, entries))
            {
                if (_deformEntriesNative.IsCreated) _deformEntriesNative.Dispose();
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
                _deformBernsteinWeightsNative = LatticeNativeArrayUtility.CreateCopy(
                    weights,
                    Allocator.Persistent);
                _deformBernsteinWeightsSource = weights;
            }
        }

        private void ReleaseDeformationNativeBuffers()
        {
            if (_deformControlNative.IsCreated) _deformControlNative.Dispose();
            if (_deformEntriesNative.IsCreated) _deformEntriesNative.Dispose();
            if (_deformOutputNative.IsCreated) _deformOutputNative.Dispose();
            if (_deformBernsteinWeightsNative.IsCreated) _deformBernsteinWeightsNative.Dispose();
            _deformEntriesSource = null;
            _deformBernsteinWeightsSource = null;
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
            return EnsureCache(settings, restVertices, HashVertices(restVertices));
        }

        private bool EnsureCache(
            LatticeAsset settings,
            Vector3[] restVertices,
            int restVerticesHash)
        {
            if (settings == null)
            {
                return false;
            }

            var mesh = _sourceMesh;
            if (mesh == null)
            {
                return false;
            }

            LatticeInterpolationMode effectiveInterpolation = GetEffectiveInterpolation(settings);
            EnsureCacheSlots();
            if (_cache.IsCompatibleWith(settings, mesh, restVerticesHash, effectiveInterpolation))
            {
                TouchCacheSlot(_cache);
                return true;
            }

            for (int i = 0; i < _cacheSlots.Count; i++)
            {
                var slot = _cacheSlots[i];
                if (slot == null || ReferenceEquals(slot, _cache) ||
                    !slot.IsCompatibleWith(
                        settings,
                        mesh,
                        restVerticesHash,
                        effectiveInterpolation))
                {
                    continue;
                }

                _cache = slot;
                TouchCacheSlot(slot);
                return true;
            }

            _cache = AcquireCacheSlot();

            return RebuildCache(
                settings,
                mesh,
                restVertices,
                restVerticesHash,
                effectiveInterpolation);
        }

        private void EnsureCacheSlots()
        {
            if (_cacheSlots == null)
            {
                _cacheSlots = new List<LatticeDeformerCache>(k_InterpolationCacheSlotCount);
            }

            if (_cache == null)
            {
                _cache = new LatticeDeformerCache();
            }

            if (!_cacheSlots.Contains(_cache))
            {
                if (_cacheSlots.Count >= k_InterpolationCacheSlotCount)
                {
                    _cacheSlots.RemoveAt(_cacheSlots.Count - 1);
                }

                _cacheSlots.Insert(0, _cache);
            }
        }

        private LatticeDeformerCache AcquireCacheSlot()
        {
            if (_cache != null && (_cache.Entries == null || _cache.Entries.Length == 0))
            {
                TouchCacheSlot(_cache);
                return _cache;
            }

            if (_cacheSlots.Count < k_InterpolationCacheSlotCount)
            {
                var slot = new LatticeDeformerCache();
                _cacheSlots.Insert(0, slot);
                return slot;
            }

            int leastRecentlyUsedIndex = _cacheSlots.Count - 1;
            var reused = _cacheSlots[leastRecentlyUsedIndex] ?? new LatticeDeformerCache();
            _cacheSlots.RemoveAt(leastRecentlyUsedIndex);
            reused.Clear();
            _cacheSlots.Insert(0, reused);
            return reused;
        }

        private void TouchCacheSlot(LatticeDeformerCache slot)
        {
            int index = _cacheSlots.IndexOf(slot);
            if (index <= 0)
            {
                return;
            }

            _cacheSlots.RemoveAt(index);
            _cacheSlots.Insert(0, slot);
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
            UnityEngine.Profiling.Profiler.BeginSample(
                "LatticeDeformer.RebuildInterpolationCache");
            try
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
            finally
            {
                UnityEngine.Profiling.Profiler.EndSample();
            }
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
