using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    // Runtime output borrows renderer assignments independently of serialized
    // authoring references, which may change before cleanup or explicit Reset.
    internal sealed class RuntimeMeshAssignment
    {
        private Mesh _output;
        private MeshFilter _filter;
        private Mesh _filterSource;
        private SkinnedMeshRenderer _renderer;
        private Mesh _rendererSource;

        internal void Assign(Mesh output, MeshFilter filter, SkinnedMeshRenderer renderer)
        {
            if (_output != output || _filter != filter || _renderer != renderer)
                Clear(restore: true);

            _output = output;
            if (filter != null)
            {
                if (_filter != filter || filter.sharedMesh != output)
                    _filterSource = filter.sharedMesh;
                _filter = filter;
                filter.sharedMesh = output;
            }
            if (renderer != null)
            {
                if (_renderer != renderer || renderer.sharedMesh != output)
                    _rendererSource = renderer.sharedMesh;
                _renderer = renderer;
                renderer.sharedMesh = output;
            }
        }

        internal void Clear(bool restore)
        {
            // Release the borrowed references even if the output was destroyed
            // elsewhere. Never overwrite another processor's assignment.
            if (restore && _output != null)
            {
                if (_filter != null && _filter.sharedMesh == _output)
                    _filter.sharedMesh = _filterSource != null ? _filterSource : null;
                if (_renderer != null && _renderer.sharedMesh == _output)
                    _renderer.sharedMesh = _rendererSource != null ? _rendererSource : null;
            }
            _output = null;
            _filter = null;
            _filterSource = null;
            _renderer = null;
            _rendererSource = null;
        }
    }
}
