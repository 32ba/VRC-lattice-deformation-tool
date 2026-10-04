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
            if (restore && !ReferenceEquals(_output, null))
            {
                if (_filter != null && OwnsAssignment(_filter, _filter.sharedMesh))
                    _filter.sharedMesh = _filterSource != null ? _filterSource : null;
                if (_renderer != null && OwnsAssignment(_renderer, _renderer.sharedMesh))
                    _renderer.sharedMesh = _rendererSource != null ? _rendererSource : null;
            }
            _output = null;
            _filter = null;
            _filterSource = null;
            _renderer = null;
            _rendererSource = null;
        }

        private bool OwnsAssignment(Component target, Mesh displayed)
        {
            if (_output != null) return displayed == _output;
            // Unity's getter returns managed null for a destroyed mesh, but the
            // serialized reference can retain its instance ID. An explicit clear
            // has ID zero and must not be mistaken for our missing assignment.
            return DeformerPlatformServices.AssignedMeshInstanceId != null &&
                   DeformerPlatformServices.AssignedMeshInstanceId(target) == _output.GetInstanceID();
        }
    }
}
