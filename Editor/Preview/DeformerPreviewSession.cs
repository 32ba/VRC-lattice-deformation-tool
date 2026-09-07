#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>Coordinates revision updates and cleanup for one NDMF node lifetime.</summary>
    internal sealed class DeformerPreviewSession : IDisposable
    {
        private static readonly ProfilerMarker s_updateMeshMarker = new("Preview.UpdateMesh");
        private static readonly ProfilerMarker s_deformMarker = new("Preview.Deform");
        private readonly LatticeDeformer _deformer;
        private readonly Mesh _upstreamMesh;
        private readonly PreviewMeshLease _mesh;
        private int _lastDeformationDataRevision;
        private int _lastRuntimeMeshRevision;
        private bool _registered;
        private bool _disposed;

        internal DeformerPreviewSession(
            LatticeDeformer deformer,
            IEnumerable<(Renderer original, Renderer proxy)> proxyPairs,
            Mesh previewMesh,
            Mesh upstreamMesh)
        {
            _deformer = deformer;
            var pairs = proxyPairs.ToArray();
            _upstreamMesh = upstreamMesh;
            if (_upstreamMesh == null)
            {
                for (int i = 0; i < pairs.Length; i++)
                {
                    _upstreamMesh = PreviewRendererMesh.Get(pairs[i].proxy);
                    if (_upstreamMesh != null) break;
                }
            }

            _mesh = new PreviewMeshLease(previewMesh, _upstreamMesh);
            try
            {
                CaptureRevision();
                foreach (var (original, proxy) in pairs) OnFrame(original, proxy);
                LatticePreviewUtility.RegisterPreviewUndoTarget(_deformer);
                _registered = true;
                LatticePreviewUtility.InteractiveDeformationPublished += OnInteractiveDeformationPublished;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void OnFrame(Renderer original, Renderer proxy)
        {
            if (_disposed || original == null || proxy == null) return;
            _mesh.AssignTo(proxy);
        }

        internal void UpdateIfChanged()
        {
            if (_disposed) return;
            int dataRevision = _deformer != null ? _deformer.DeformationDataRevision : 0;
            int meshRevision = _deformer != null ? _deformer.RuntimeMeshRevision : 0;
            if (dataRevision != _lastDeformationDataRevision || meshRevision != _lastRuntimeMeshRevision)
                UpdateAndPublish();
        }

        private void OnInteractiveDeformationPublished(LatticeDeformer deformer)
        {
            if (!_disposed && ReferenceEquals(deformer, _deformer)) UpdateAndPublish();
        }

        private void UpdateAndPublish()
        {
            if (_deformer == null || _mesh.Mesh == null) return;
            Mesh evaluated;
            using (s_deformMarker.Auto()) evaluated = _deformer.CreatePreviewMeshFromInput(_upstreamMesh);
            if (evaluated == null) return;
            try
            {
                // Preserve the assigned Mesh identity throughout an interactive edit.
                using (s_updateMeshMarker.Auto())
                {
                    EditorUtility.CopySerialized(evaluated, _mesh.Mesh);
                    _mesh.Mesh.hideFlags = HideFlags.HideAndDontSave;
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(evaluated);
            }
            _mesh.Publish();
            CaptureRevision();
        }

        private void CaptureRevision()
        {
            _lastDeformationDataRevision = _deformer != null ? _deformer.DeformationDataRevision : 0;
            _lastRuntimeMeshRevision = _deformer != null ? _deformer.RuntimeMeshRevision : 0;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            LatticePreviewUtility.InteractiveDeformationPublished -= OnInteractiveDeformationPublished;
            if (_registered) LatticePreviewUtility.UnregisterPreviewUndoTarget(_deformer);
            _registered = false;
            _mesh.Dispose();
        }
    }
}
#endif
