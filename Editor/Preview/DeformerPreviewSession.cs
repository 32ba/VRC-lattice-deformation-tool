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
        private MeshDeformerProfile _lastProfile;
        private string _lastProfileContent, _lastProfileCompatibility;
        private readonly SourceVertexWorkspace _weightWorkspace = new SourceVertexWorkspace();
        private float[] _lastSourceWeights = Array.Empty<float>();
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
                // NDMF node disposal can be deferred past the end of this domain.
                AssemblyReloadEvents.beforeAssemblyReload += Dispose;
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
            var profile = CurrentProfile;
            string content = profile != null ? profile.GetContentFingerprint() : null;
            string compatibility = profile != null ? profile.GetCompatibilityFingerprint() : null;
            var weights = ReadSourceWeights();
            bool weightsChanged = weights.Length != _lastSourceWeights.Length || !weights.SequenceEqual(_lastSourceWeights);
            bool profileChanged = profile != _lastProfile || content != _lastProfileContent || compatibility != _lastProfileCompatibility;
            _lastProfile = profile;
            _lastProfileContent = content;
            _lastProfileCompatibility = compatibility;
            CaptureSourceWeights(weights);
            // Renderer weights already pose unchanged source frames. Rebuilding
            // their graph would discard stable cage bindings unnecessarily. Only
            // publish a weight change when our nonlinear output actually changes.
            bool outputChanged = weightsChanged && !profileChanged && UpdateAndPublish(detectWeightOutputChange: true);
            if (_deformer != null && (profileChanged || outputChanged))
            {
                _deformer.NotifyDeformationDataChanged();
                LatticePreviewUtility.PublishInteractiveDeformation(_deformer);
            }
            int dataRevision = _deformer != null ? _deformer.DeformationDataRevision : 0;
            int meshRevision = _deformer != null ? _deformer.RuntimeMeshRevision : 0;
            if (dataRevision != _lastDeformationDataRevision || meshRevision != _lastRuntimeMeshRevision)
                UpdateAndPublish();
        }

        private void OnInteractiveDeformationPublished(LatticeDeformer deformer)
        {
            if (!_disposed && ReferenceEquals(deformer, _deformer)) UpdateAndPublish();
        }

        private bool UpdateAndPublish(bool detectWeightOutputChange = false)
        {
            if (_deformer == null || _mesh.Mesh == null) return false;
            Mesh evaluated;
            using (s_deformMarker.Auto()) evaluated = _deformer.CreatePreviewMeshFromInput(_upstreamMesh);
            if (evaluated == null) return false;
            bool outputChanged = detectWeightOutputChange &&
                                 (!_mesh.Mesh.vertices.SequenceEqual(evaluated.vertices) ||
                                  !_mesh.Mesh.normals.SequenceEqual(evaluated.normals) ||
                                  !_mesh.Mesh.tangents.SequenceEqual(evaluated.tangents) ||
                                  evaluated.blendShapeCount > (_upstreamMesh != null ? _upstreamMesh.blendShapeCount : 0));
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
            CaptureRevision();
            return outputChanged;
        }

        private MeshDeformerProfile CurrentProfile => _deformer != null &&
            _deformer.DataSource == DeformerDataSource.Profile ? _deformer.Profile : null;

        private float[] ReadSourceWeights() => _weightWorkspace.CaptureMappedWeights(
            _deformer != null ? _deformer.GetComponent<SkinnedMeshRenderer>() : null, _upstreamMesh) ?? Array.Empty<float>();

        private void CaptureSourceWeights(float[] weights)
        {
            if (_lastSourceWeights.Length != weights.Length) _lastSourceWeights = new float[weights.Length];
            Array.Copy(weights, _lastSourceWeights, weights.Length);
        }

        private void CaptureRevision()
        {
            CaptureSourceWeights(ReadSourceWeights());
            _lastProfile = CurrentProfile;
            _lastProfileContent = _lastProfile != null ? _lastProfile.GetContentFingerprint() : null;
            _lastProfileCompatibility = _lastProfile != null ? _lastProfile.GetCompatibilityFingerprint() : null;
            _lastDeformationDataRevision = _deformer != null ? _deformer.DeformationDataRevision : 0;
            _lastRuntimeMeshRevision = _deformer != null ? _deformer.RuntimeMeshRevision : 0;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            LatticePreviewUtility.InteractiveDeformationPublished -= OnInteractiveDeformationPublished;
            if (_registered) LatticePreviewUtility.UnregisterPreviewUndoTarget(_deformer);
            _registered = false;
            _mesh.Dispose();
        }
    }
}
#endif
