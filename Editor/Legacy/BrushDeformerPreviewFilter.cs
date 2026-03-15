#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using nadena.dev.ndmf.preview;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal sealed class BrushDeformerPreviewFilter : IRenderFilter
    {
        private readonly Dictionary<Renderer, BrushDeformer> _rendererToDeformer = new Dictionary<Renderer, BrushDeformer>();

        private static readonly TogglablePreviewNode s_previewToggle = TogglablePreviewNode.Create(
            () => LatticeLocalization.Tr(LocKey.BrushDeformer),
            typeof(BrushDeformerPreviewFilter).FullName);

        internal static bool PreviewToggleEnabled => s_previewToggle.IsEnabled.Value;

        public IEnumerable<TogglablePreviewNode> GetPreviewControlNodes()
        {
            yield return s_previewToggle;
        }

        public bool CanEnableRenderers => false;

        internal static void ForcePreviewState(bool enabled)
        {
            s_previewToggle.IsEnabled.Value = enabled;
        }

        public ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context)
        {
            if (!context.Observe(s_previewToggle.IsEnabled))
            {
                return ImmutableList<RenderGroup>.Empty;
            }

            _rendererToDeformer.Clear();

            var builder = ImmutableList.CreateBuilder<RenderGroup>();
            var deformers = context.GetComponentsByType<BrushDeformer>();

            foreach (var deformer in deformers)
            {
                if (deformer == null)
                {
                    continue;
                }

                var renderer = context.GetComponent<Renderer>(deformer.gameObject);
                if (renderer == null)
                {
                    continue;
                }

                if (!context.ActiveAndEnabled(deformer))
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
            var pairList = proxyPairs
                .Select(pair => (original: pair.Item1, proxy: pair.Item2))
                .Where(p => p.original != null && p.proxy != null)
                .ToList();

            if (pairList.Count == 0)
            {
                return Task.FromResult<IRenderFilterNode>(null);
            }

            var deformer = pairList
                .Select(p => p.original)
                .Select(renderer =>
                {
                    if (renderer == null)
                    {
                        return null;
                    }

                    if (_rendererToDeformer.TryGetValue(renderer, out var cached) && cached != null)
                    {
                        return cached;
                    }

                    var found = renderer.GetComponent<BrushDeformer>();
                    if (found != null)
                    {
                        _rendererToDeformer[renderer] = found;
                    }

                    return found;
                })
                .FirstOrDefault(instance => instance != null);

            if (deformer == null)
            {
                return Task.FromResult<IRenderFilterNode>(null);
            }

            _ = context.Observe(
                deformer,
                BrushPreviewState.Create,
                BrushPreviewState.Equals);

            var meshTransform = deformer.MeshTransform;
            if (meshTransform != null)
            {
                _ = context.Observe(meshTransform, TransformSnapshot.Create, TransformSnapshot.Equals);
            }

            var sourceMesh = deformer.SourceMesh;
            if (sourceMesh != null)
            {
                _ = context.Observe(sourceMesh);
            }

            var previewMesh = GeneratePreviewMesh(deformer);
            if (previewMesh == null)
            {
                return Task.FromResult<IRenderFilterNode>(null);
            }

            var node = new PreviewNode(deformer, pairList, previewMesh);
            return Task.FromResult<IRenderFilterNode>(node);
        }

        private sealed class PreviewNode : IRenderFilterNode
        {
            private readonly BrushDeformer _deformer;
            private readonly List<Target> _targets = new List<Target>();
            private readonly Mesh _previewMesh;

            public PreviewNode(BrushDeformer deformer, IEnumerable<(Renderer original, Renderer proxy)> proxyPairs, Mesh previewMesh)
            {
                _deformer = deformer;
                _previewMesh = previewMesh;
                _previewMesh.MarkDynamic();

                foreach (var (original, proxy) in proxyPairs)
                {
                    if (original == null || proxy == null)
                    {
                        continue;
                    }

                    var target = new Target
                    {
                        OriginalRenderer = original,
                        ProxyRenderer = proxy,
                        OriginalMeshFilter = original.GetComponent<MeshFilter>(),
                        ProxyMeshFilter = proxy.GetComponent<MeshFilter>(),
                        OriginalSkinned = original as SkinnedMeshRenderer,
                        ProxySkinned = proxy as SkinnedMeshRenderer,
                    };

                    if (target.OriginalMeshFilter != null)
                    {
                        target.OriginalMesh = target.OriginalMeshFilter.sharedMesh;
                    }
                    else if (target.OriginalSkinned != null)
                    {
                        target.OriginalMesh = target.OriginalSkinned.sharedMesh;
                    }

                    ApplyPreviewMesh(target);
                    _targets.Add(target);
                }

                UpdatePreviewMesh();
            }

            public RenderAspects WhatChanged => RenderAspects.Mesh;

            public void OnFrame(Renderer original, Renderer proxy)
            {
                var target = EnsureTarget(original, proxy);
                ApplyPreviewMesh(target);
            }

            public void OnFrameGroup()
            {
                UpdatePreviewMesh();
            }

            public void Dispose()
            {
                foreach (var target in _targets)
                {
                    if (target.OriginalMesh == null)
                    {
                        continue;
                    }

                    if (target.ProxyMeshFilter != null)
                    {
                        target.ProxyMeshFilter.sharedMesh = target.OriginalMesh;
                    }
                    else if (target.ProxySkinned != null)
                    {
                        target.ProxySkinned.sharedMesh = target.OriginalMesh;
                    }
                }

                if (_previewMesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(_previewMesh);
                }
            }

            private void ApplyPreviewMesh(Target target)
            {
                if (target == null)
                {
                    return;
                }

                if (target.ProxyMeshFilter != null)
                {
                    target.ProxyMeshFilter.sharedMesh = _previewMesh;
                }
                else if (target.ProxySkinned != null)
                {
                    target.ProxySkinned.sharedMesh = _previewMesh;
                }
            }

            private void UpdatePreviewMesh()
            {
                if (_deformer == null || _previewMesh == null)
                {
                    return;
                }

                _deformer.CacheSourceMesh();
                var runtimeMesh = _deformer.Deform(false);
                if (runtimeMesh == null)
                {
                    return;
                }

                var vertices = runtimeMesh.vertices;
                if (vertices != null && vertices.Length > 0)
                {
                    _previewMesh.vertices = vertices;
                }

                var normals = runtimeMesh.normals;
                if (normals != null && normals.Length == _previewMesh.vertexCount)
                {
                    _previewMesh.normals = normals;
                }

                var tangents = runtimeMesh.tangents;
                if (tangents != null && tangents.Length == _previewMesh.vertexCount)
                {
                    _previewMesh.tangents = tangents;
                }

                _previewMesh.bounds = runtimeMesh.bounds;
                _previewMesh.UploadMeshData(false);

                foreach (var target in _targets)
                {
                    ApplyPreviewMesh(target);
                }
            }

            private Target EnsureTarget(Renderer original, Renderer proxy)
            {
                var existing = _targets.FirstOrDefault(t => t.ProxyRenderer == proxy);
                if (existing != null)
                {
                    return existing;
                }

                if (original == null || proxy == null)
                {
                    return null;
                }

                var target = new Target
                {
                    OriginalRenderer = original,
                    ProxyRenderer = proxy,
                    OriginalMeshFilter = original.GetComponent<MeshFilter>(),
                    ProxyMeshFilter = proxy.GetComponent<MeshFilter>(),
                    OriginalSkinned = original as SkinnedMeshRenderer,
                    ProxySkinned = proxy as SkinnedMeshRenderer,
                };

                if (target.OriginalMeshFilter != null)
                {
                    target.OriginalMesh = target.OriginalMeshFilter.sharedMesh;
                }
                else if (target.OriginalSkinned != null)
                {
                    target.OriginalMesh = target.OriginalSkinned.sharedMesh;
                }

                ApplyPreviewMesh(target);
                _targets.Add(target);
                return target;
            }
        }

        private sealed class Target
        {
            public Renderer OriginalRenderer;
            public Renderer ProxyRenderer;
            public Mesh OriginalMesh;
            public MeshFilter OriginalMeshFilter;
            public MeshFilter ProxyMeshFilter;
            public SkinnedMeshRenderer OriginalSkinned;
            public SkinnedMeshRenderer ProxySkinned;
        }

        private static Mesh GeneratePreviewMesh(BrushDeformer deformer)
        {
            try
            {
                deformer.CacheSourceMesh();
                var runtimeMesh = deformer.Deform(false);
                if (runtimeMesh == null)
                {
                    return null;
                }

                var previewMesh = UnityEngine.Object.Instantiate(runtimeMesh);
                previewMesh.name = runtimeMesh.name + " (Preview)";
                previewMesh.hideFlags = HideFlags.HideAndDontSave;
                previewMesh.UploadMeshData(false);
                return previewMesh;
            }
            catch
            {
                return null;
            }
        }

        private readonly struct BrushPreviewState : IEquatable<BrushPreviewState>
        {
            private readonly int _sourceMeshId;
            private readonly int _displacementHash;
            private readonly int _displacementCount;

            private BrushPreviewState(int sourceMeshId, int displacementHash, int displacementCount)
            {
                _sourceMeshId = sourceMeshId;
                _displacementHash = displacementHash;
                _displacementCount = displacementCount;
            }

            public static BrushPreviewState Create(BrushDeformer deformer)
            {
                if (deformer == null)
                {
                    return default;
                }

                var displacements = deformer.Displacements;
                int hash = 17;
                if (displacements != null)
                {
                    for (int i = 0; i < displacements.Length; i++)
                    {
                        var d = displacements[i];
                        hash = HashCode.Combine(hash, d.x, d.y, d.z);
                    }
                }

                return new BrushPreviewState(
                    deformer.SourceMesh != null ? deformer.SourceMesh.GetInstanceID() : 0,
                    hash,
                    deformer.DisplacementCount);
            }

            public static bool Equals(BrushPreviewState lhs, BrushPreviewState rhs)
            {
                return lhs.Equals(rhs);
            }

            public bool Equals(BrushPreviewState other)
            {
                return _sourceMeshId == other._sourceMeshId &&
                       _displacementHash == other._displacementHash &&
                       _displacementCount == other._displacementCount;
            }

            public override bool Equals(object obj)
            {
                return obj is BrushPreviewState other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(_sourceMeshId, _displacementHash, _displacementCount);
            }
        }

        private readonly struct TransformSnapshot : IEquatable<TransformSnapshot>
        {
            private readonly Vector3 _position;
            private readonly Quaternion _rotation;
            private readonly Vector3 _scale;

            private TransformSnapshot(Vector3 position, Quaternion rotation, Vector3 scale)
            {
                _position = position;
                _rotation = rotation;
                _scale = scale;
            }

            public static TransformSnapshot Create(Transform transform)
            {
                if (transform == null)
                {
                    return default;
                }

                return new TransformSnapshot(transform.position, transform.rotation, transform.lossyScale);
            }

            public static bool Equals(TransformSnapshot lhs, TransformSnapshot rhs)
            {
                return lhs.Equals(rhs);
            }

            public bool Equals(TransformSnapshot other)
            {
                return _position == other._position &&
                       _rotation == other._rotation &&
                       _scale == other._scale;
            }

            public override bool Equals(object obj)
            {
                return obj is TransformSnapshot other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(_position, _rotation, _scale);
            }
        }
    }
}
#endif
