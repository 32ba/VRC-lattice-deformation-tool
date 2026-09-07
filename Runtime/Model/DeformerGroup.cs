using System;
using UnityEngine;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Net._32Ba.LatticeDeformationTool
{
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

        internal AnimationCurve SerializedBlendShapeCurve => _blendShapeCurve;

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
}
