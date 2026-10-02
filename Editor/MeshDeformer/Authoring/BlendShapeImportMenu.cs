#if UNITY_EDITOR
using System;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>A deferred import keeps the selected owner, group and source identity.</summary>
    internal sealed class BlendShapeImportMenu
    {
        private readonly Mesh _source;
        private readonly int _topology, _groupIndex, _layerIndex, _layerCount;
        private readonly DeformerGroup _group;
        private readonly LatticeLayer _layer;
        private readonly string[] _names;
        private readonly float[][] _weights;
        private bool _consumed;
        internal LatticeDeformer Owner { get; }
        internal int Count => _names.Length;
        internal string GetName(int index) => _names[index];

        private BlendShapeImportMenu(LatticeDeformer owner, in SerializedDeformerData raw,
            Mesh source, int topology)
        {
            Owner = owner; _source = source; _topology = topology;
            _groupIndex = raw.ActiveGroupIndex; _group = raw.ActiveGroup;
            _layerIndex = raw.ActiveLayerIndex;
            _layerCount = raw.ActiveLayers.Count;
            _layer = _layerIndex >= 0 && _layerIndex < raw.ActiveLayers.Count
                ? raw.ActiveLayers[_layerIndex] : null;
            _names = new string[source.blendShapeCount];
            _weights = new float[_names.Length][];
            for (int shape = 0; shape < _names.Length; shape++)
            {
                _names[shape] = source.GetBlendShapeName(shape);
                _weights[shape] = new float[source.GetBlendShapeFrameCount(shape)];
                for (int frame = 0; frame < _weights[shape].Length; frame++)
                    _weights[shape][frame] = source.GetBlendShapeFrameWeight(shape, frame);
            }
        }

        internal static BlendShapeImportMenu TryCreate(LatticeDeformer owner)
        {
            if (!DeformerAuthoringSource.TryRead(owner, out var raw, out var source, out int topology) ||
                raw.ActiveGroup == null || raw.ActiveLayers == null || source.blendShapeCount == 0) return null;
            return new BlendShapeImportMenu(owner, raw, source, topology);
        }

        internal bool Import(int shape, bool allFrames, string undoLabel)
        {
            if (_consumed || shape < 0 || shape >= Count ||
                !DeformerAuthoringSource.TryRead(Owner, out var raw, out var source, out int topology) ||
                !ReferenceEquals(source, _source) || topology != _topology ||
                raw.ActiveGroupIndex != _groupIndex || !ReferenceEquals(raw.ActiveGroup, _group) ||
                raw.ActiveLayerIndex != _layerIndex || raw.ActiveLayers == null ||
                raw.ActiveLayers.Count != _layerCount ||
                (_layerIndex >= 0 && _layerIndex < _layerCount &&
                    !ReferenceEquals(raw.ActiveLayers[_layerIndex], _layer)) ||
                !MatchesShape(shape) || !HasValidDeltas(shape, allFrames)) return false;

            // A freshly loaded inactive Prefab has serialized source identity but
            // no runtime source cache. Prepare from the validated source directly.
            var layer = allFrames ? null : BlendShapeLayerImport.CreateLayer(source, shape, 0);
            var group = allFrames ? BlendShapeLayerImport.CreateGroup(source, shape) : null;
            if (layer == null && group == null) return false;
            bool changed = DeformerEditService.Execute(Owner, undoLabel, target => allFrames
                ? target.InsertGroup(group) >= 0
                : target.InsertLayer(layer) >= 0);
            if (changed) _consumed = true;
            return changed;
        }

        private bool MatchesShape(int shape)
        {
            if (_source.blendShapeCount != Count || _source.GetBlendShapeName(shape) != _names[shape] ||
                _source.GetBlendShapeFrameCount(shape) != _weights[shape].Length || _weights[shape].Length == 0)
                return false;
            for (int frame = 0; frame < _weights[shape].Length; frame++)
                if (_source.GetBlendShapeFrameWeight(shape, frame) != _weights[shape][frame]) return false;
            return true;
        }

        private bool HasValidDeltas(int shape, bool allFrames)
        {
            // Validate before Undo or any component mutation. Single-frame import
            // otherwise sanitizes invalid deltas through the public layer setter.
            var deltas = new Vector3[_source.vertexCount];
            int frames = allFrames ? _weights[shape].Length : 1;
            float previous = float.NegativeInfinity;
            for (int frame = 0; frame < frames; frame++)
            {
                float weight = _weights[shape][frame];
                if (!IsFinite(weight) || weight <= previous) return false;
                _source.GetBlendShapeFrameVertices(shape, frame, deltas, null, null);
                foreach (var delta in deltas)
                    if (!IsFinite(delta.x) || !IsFinite(delta.y) || !IsFinite(delta.z)) return false;
                previous = weight;
            }
            return true;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
#endif
