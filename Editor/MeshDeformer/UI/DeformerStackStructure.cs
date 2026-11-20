#if UNITY_EDITOR
using System;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Owns only an identity/selection snapshot for list invalidation. Stored object
    /// references are compared by identity; their mutable payload is never read later.
    /// </summary>
    internal sealed class DeformerStackStructure
    {
        private readonly DeformerDataSource _source;
        private readonly bool _canStart;
        private readonly int _activeGroup;
        private readonly DeformerGroup[] _groups;
        private readonly LatticeLayer[][] _layers;
        private readonly MeshDeformerLayerType[][] _types;
        private readonly int[] _activeLayers;

        internal DeformerStackStructure(LatticeDeformer owner)
        {
            if (owner == null) return;
            var raw = SerializedDeformerReader.Read(owner);
            _source = raw.DataSource;
            _canStart = owner.CanStartAuthoringEdit;
            _activeGroup = raw.EmbeddedActiveGroupIndex;
            if (raw.EmbeddedGroups == null) return;
            int count = raw.EmbeddedGroups.Count;
            _groups = new DeformerGroup[count];
            _layers = new LatticeLayer[count][];
            _types = new MeshDeformerLayerType[count][];
            _activeLayers = new int[count];
            for (int g = 0; g < count; g++)
            {
                var group = raw.EmbeddedGroups[g];
                _groups[g] = group;
                _activeLayers[g] = group?.SerializedActiveLayerIndex ?? -1;
                var layers = group?.SerializedLayers;
                if (layers == null) continue;
                _layers[g] = new LatticeLayer[layers.Count];
                _types[g] = new MeshDeformerLayerType[layers.Count];
                for (int l = 0; l < layers.Count; l++)
                {
                    _layers[g][l] = layers[l];
                    _types[g][l] = layers[l]?.Type ?? default;
                }
            }
        }

        internal bool Matches(LatticeDeformer owner)
        {
            if (owner == null) return false;
            var raw = SerializedDeformerReader.Read(owner);
            if (_source != raw.DataSource || _activeGroup != raw.EmbeddedActiveGroupIndex ||
                _canStart != owner.CanStartAuthoringEdit) return false;
            if (_groups == null || raw.EmbeddedGroups == null) return _groups == null && raw.EmbeddedGroups == null;
            if (_groups.Length != raw.EmbeddedGroups.Count) return false;
            for (int g = 0; g < _groups.Length; g++)
            {
                var group = raw.EmbeddedGroups[g];
                if (!ReferenceEquals(_groups[g], group) || _activeLayers[g] != (group?.SerializedActiveLayerIndex ?? -1)) return false;
                var layers = group?.SerializedLayers;
                if (_layers[g] == null || layers == null)
                {
                    if (_layers[g] != null || layers != null) return false;
                    continue;
                }
                if (_layers[g].Length != layers.Count) return false;
                for (int l = 0; l < layers.Count; l++)
                    if (!ReferenceEquals(_layers[g][l], layers[l]) || _types[g][l] != (layers[l]?.Type ?? default)) return false;
            }
            return true;
        }
    }
}
#endif
