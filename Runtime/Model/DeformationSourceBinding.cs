using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    /// <summary>
    /// The saved source identity is an authoring baseline, not a renderer cache.
    /// Legacy data without a baseline may establish one; an existing binding must
    /// survive missing renderers, missing assets and incidental reads unchanged.
    /// Topology hashes are retained for the existing topology validation boundary.
    /// </summary>
    internal readonly struct DeformationSourceBinding
    {
        private readonly Mesh _source;
        private readonly int _vertexCount;
        private readonly int _topologyHash;

        internal DeformationSourceBinding(Mesh source, int vertexCount, int topologyHash)
        {
            _source = source;
            _vertexCount = vertexCount;
            _topologyHash = topologyHash;
        }

        internal bool HasBaseline => _source != null || _vertexCount != 0 || _topologyHash != 0;

        internal bool CanUse(Mesh current)
        {
            if (!HasBaseline) return true;
            return _source != null && current != null && _source == current &&
                   (_vertexCount == 0 || _vertexCount == current.vertexCount);
        }
    }
}
