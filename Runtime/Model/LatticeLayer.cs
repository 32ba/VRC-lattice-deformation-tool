using System;
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
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

        internal LatticeLayer CreateFingerprintMetadata(System.IO.BinaryWriter writer)
        {
            ProfileContentFingerprint.Write(writer, _brushDisplacements);
            ProfileContentFingerprint.Write(writer, _vertexMask);
            ProfileContentFingerprint.Write(writer, _fitCorrectionConstraintMask);
            var copy = (LatticeLayer)MemberwiseClone();
            copy._brushDisplacements = Array.Empty<Vector3>();
            copy._vertexMask = Array.Empty<float>();
            copy._fitCorrectionConstraintMask = Array.Empty<float>();
            writer.Write(_settings != null);
            copy._settings = _settings?.CreateFingerprintMetadata(writer);
            return copy;
        }

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

        internal AnimationCurve SerializedBlendShapeCurve => _blendShapeCurve;

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
}
