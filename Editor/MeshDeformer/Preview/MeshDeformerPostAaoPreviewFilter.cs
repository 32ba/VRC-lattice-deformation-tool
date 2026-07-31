#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using nadena.dev.ndmf.preview;
using UnityEditor;
using Unity.Profiling;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Preserves AAO's removed-index topology while synchronizing the vertex channels
    /// from the interactive Mesh Deformer preview. This filter intentionally runs after
    /// AAO so editing does not rebuild AAO's mesh-removal nodes on every input event.
    /// </summary>
    [ExcludeFromCodeCoverage]
    internal sealed class LatticeDeformerPostAaoPreviewFilter : IRenderFilter
    {
        private readonly Dictionary<Renderer, LatticeDeformer> _rendererToDeformer = new();

        public ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context)
        {
            if (!LatticeDeformerPreviewFilter.ObservePreviewEnabled(context))
            {
                return ImmutableList<RenderGroup>.Empty;
            }

            _rendererToDeformer.Clear();
            var builder = ImmutableList.CreateBuilder<RenderGroup>();
            foreach (var deformer in context.GetComponentsByType<LatticeDeformer>())
            {
                if (deformer == null || !context.ActiveAndEnabled(deformer))
                {
                    continue;
                }

                var renderer = context.GetComponent<Renderer>(deformer.gameObject);
                if (renderer == null ||
                    !LatticeDeformerPreviewFilter.RequiresDownstreamMeshRefresh(renderer))
                {
                    continue;
                }

                _rendererToDeformer[renderer] = deformer;
                builder.Add(RenderGroup.For(renderer));
            }

            return builder.ToImmutable();
        }

        public Task<IRenderFilterNode> Instantiate(
            RenderGroup group,
            IEnumerable<(Renderer, Renderer)> proxyPairs,
            ComputeContext context)
        {
            foreach (var (original, proxy) in proxyPairs)
            {
                if (original == null || proxy == null)
                {
                    continue;
                }

                if (!_rendererToDeformer.TryGetValue(original, out var deformer) ||
                    deformer == null)
                {
                    deformer = original.GetComponent<LatticeDeformer>();
                }

                Mesh upstreamMesh = LatticeDeformerPreviewFilter.GetRendererMesh(proxy);
                if (deformer == null || upstreamMesh == null ||
                    !LatticePreviewUtility.TryGetPreviewMesh(original, out var latticePreviewMesh) ||
                    latticePreviewMesh.vertexCount != upstreamMesh.vertexCount ||
                    !HasTopologyDifference(latticePreviewMesh, upstreamMesh))
                {
                    continue;
                }

                return Task.FromResult<IRenderFilterNode>(new PreviewNode(
                    deformer,
                    original,
                    proxy,
                    upstreamMesh,
                    context));
            }

            return Task.FromResult<IRenderFilterNode>(null);
        }

        internal static bool HasTopologyDifference(Mesh latticePreviewMesh, Mesh downstreamMesh)
        {
            if (latticePreviewMesh == null || downstreamMesh == null ||
                latticePreviewMesh.vertexCount != downstreamMesh.vertexCount ||
                latticePreviewMesh.subMeshCount != downstreamMesh.subMeshCount)
            {
                return true;
            }

            for (int subMesh = 0; subMesh < latticePreviewMesh.subMeshCount; subMesh++)
            {
                if (latticePreviewMesh.GetIndexCount(subMesh) !=
                    downstreamMesh.GetIndexCount(subMesh))
                {
                    return true;
                }
            }

            return false;
        }

        internal sealed class PreviewNode : IRenderFilterNode
        {
            private const double k_DownstreamRebuildDelaySeconds = 0.25;
            private static readonly ProfilerMarker s_syncMarker =
                new ProfilerMarker("Preview.PostAAOSync");

            private readonly LatticeDeformer _deformer;
            private readonly Renderer _original;
            private readonly Mesh _outputMesh;
            private readonly ComputeContext _context;
            private readonly List<Vector3> _vertices = new();
            private readonly List<Vector3> _normals = new();
            private readonly List<Vector4> _tangents = new();
            private readonly LatticeDeformerPreviewFilter.BlendShapeCopyBuffers
                _blendShapeBuffers = new();
            private int _lastBlendShapeWeightStateHash;
            private long _lastPreviewMeshContentRevision;
            private double _scheduledRebuildAt = -1d;
            private bool _editorUpdateSubscribed;

            internal PreviewNode(
                LatticeDeformer deformer,
                Renderer original,
                Renderer proxy,
                Mesh upstreamMesh,
                ComputeContext context)
            {
                _deformer = deformer;
                _original = original;
                _context = context;
                // AAO's preview node assigns its duplicated mesh back to the proxy on
                // every OnFrame call. A separate downstream mesh is therefore replaced
                // again depending on NDMF's frame callback order. Update AAO's duplicated
                // mesh in place instead; its removed-index topology remains untouched.
                _outputMesh = upstreamMesh;
                _outputMesh.MarkDynamic();

                _lastBlendShapeWeightStateHash = CurrentBlendShapeWeightStateHash();
                LatticePreviewUtility.TryGetPreviewMesh(
                    original,
                    out _,
                    out _lastPreviewMeshContentRevision);

                LatticeDeformerPreviewFilter.AssignRendererMesh(proxy, _outputMesh);
            }

            internal Mesh OutputMeshForTests => _outputMesh;

            public RenderAspects WhatChanged => RenderAspects.Mesh;

            public void OnFrameGroup()
            {
                int currentWeightHash = CurrentBlendShapeWeightStateHash();
                if (!LatticePreviewUtility.TryGetPreviewMesh(
                        _original,
                        out var latticePreviewMesh,
                        out long currentPreviewMeshContentRevision))
                {
                    return;
                }

                bool previewMeshChanged = currentPreviewMeshContentRevision !=
                                          _lastPreviewMeshContentRevision;
                if (!previewMeshChanged &&
                    currentWeightHash == _lastBlendShapeWeightStateHash)
                {
                    return;
                }

                if (!SyncDeformedChannels(
                        latticePreviewMesh,
                        _outputMesh,
                        ShouldCopyBlendShapes(latticePreviewMesh)))
                {
                    return;
                }

                _lastBlendShapeWeightStateHash = currentWeightHash;
                _lastPreviewMeshContentRevision = currentPreviewMeshContentRevision;

                if (previewMeshChanged)
                {
                    ScheduleDownstreamRebuild();
                }
            }

            public void OnFrame(Renderer original, Renderer proxy)
            {
                if (proxy != null && _outputMesh != null)
                {
                    LatticeDeformerPreviewFilter.AssignRendererMesh(proxy, _outputMesh);
                }
            }

            public void Dispose()
            {
                UnsubscribeEditorUpdate();
            }

            private int CurrentBlendShapeWeightStateHash()
            {
                return LatticeDeformerPreviewFilter.ComputeBlendShapeWeightStateHash(
                    _deformer != null
                        ? _deformer.GetComponent<SkinnedMeshRenderer>()
                        : null,
                    _deformer != null ? _deformer.SourceMesh : null);
            }

            private bool ShouldCopyBlendShapes(Mesh latticePreviewMesh)
            {
                Mesh sourceMesh = _deformer != null ? _deformer.SourceMesh : null;
                return sourceMesh == null ||
                       latticePreviewMesh.blendShapeCount != sourceMesh.blendShapeCount;
            }

            private bool SyncDeformedChannels(
                Mesh source,
                Mesh destination,
                bool copyBlendShapes)
            {
                if (source == null || destination == null ||
                    source.vertexCount != destination.vertexCount)
                {
                    return false;
                }

                using (s_syncMarker.Auto())
                {
                    source.GetVertices(_vertices);
                    destination.SetVertices(_vertices);

                    source.GetNormals(_normals);
                    if (_normals.Count == destination.vertexCount)
                    {
                        destination.SetNormals(_normals);
                    }

                    source.GetTangents(_tangents);
                    if (_tangents.Count == destination.vertexCount)
                    {
                        destination.SetTangents(_tangents);
                    }

                    if (copyBlendShapes)
                    {
                        LatticeDeformerPreviewFilter.CopyBlendShapes(
                            source,
                            destination,
                            _blendShapeBuffers);
                    }

                    destination.bounds = source.bounds;
                    destination.UploadMeshData(false);
                }

                return true;
            }

            private void ScheduleDownstreamRebuild()
            {
                if (_context == null)
                {
                    return;
                }

                _scheduledRebuildAt =
                    EditorApplication.timeSinceStartup + k_DownstreamRebuildDelaySeconds;
                if (_editorUpdateSubscribed)
                {
                    return;
                }

                EditorApplication.update += OnEditorUpdate;
                _editorUpdateSubscribed = true;
            }

            private void OnEditorUpdate()
            {
                if (_scheduledRebuildAt < 0d ||
                    EditorApplication.timeSinceStartup < _scheduledRebuildAt)
                {
                    return;
                }

                UnsubscribeEditorUpdate();
                _scheduledRebuildAt = -1d;
                _context?.Invalidate();
            }

            private void UnsubscribeEditorUpdate()
            {
                if (!_editorUpdateSubscribed)
                {
                    return;
                }

                EditorApplication.update -= OnEditorUpdate;
                _editorUpdateSubscribed = false;
            }
        }
    }
}
#endif
