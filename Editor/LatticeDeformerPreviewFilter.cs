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
    internal sealed class LatticeDeformerPreviewFilter : IRenderFilter
    {
        private readonly Dictionary<Renderer, LatticeDeformer> _rendererToDeformer = new Dictionary<Renderer, LatticeDeformer>();

        private static readonly TogglablePreviewNode s_previewToggle = TogglablePreviewNode.Create(
            () => LatticeLocalization.Tr("Lattice Deformer"),
            typeof(LatticeDeformerPreviewFilter).FullName);

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
            var deformers = context.GetComponentsByType<LatticeDeformer>();

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

                    var found = renderer.GetComponent<LatticeDeformer>();
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
                LatticePreviewState.Create,
                LatticePreviewState.Equals);

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
            private readonly LatticeDeformer _deformer;
            private readonly List<Target> _targets = new List<Target>();
            private readonly Mesh _previewMesh;

            public PreviewNode(LatticeDeformer deformer, IEnumerable<(Renderer original, Renderer proxy)> proxyPairs, Mesh previewMesh)
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

                    LatticePreviewUtility.RegisterProxy(original, proxy);

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

                    LatticePreviewUtility.ClearProxy(target.OriginalRenderer);

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

                LatticePreviewUtility.RegisterProxy(original, proxy);

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

        private static Mesh GeneratePreviewMesh(LatticeDeformer deformer)
        {
            try
            {
                deformer.InvalidateCache();
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
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[LatticeDeformer] Failed to generate preview mesh: {ex.Message}");
                return null;
            }
        }

        private readonly struct LatticePreviewState : IEquatable<LatticePreviewState>
        {
            private readonly Vector3Int _gridSize;
            private readonly Vector3 _boundsCenter;
            private readonly Vector3 _boundsSize;
            private readonly LatticeInterpolationMode _interpolation;
            private readonly bool _useJobs;
            private readonly int _controlPointHash;
            private readonly int _controlPointCount;
            private readonly int _sourceMeshId;

            private LatticePreviewState(
                Vector3Int gridSize,
                Bounds bounds,
                LatticeInterpolationMode interpolation,
                bool useJobs,
                int sourceMeshId,
                int controlHash,
                int controlCount)
            {
                _gridSize = gridSize;
                _boundsCenter = bounds.center;
                _boundsSize = bounds.size;
                _interpolation = interpolation;
                _useJobs = useJobs;
                _sourceMeshId = sourceMeshId;
                _controlPointHash = controlHash;
                _controlPointCount = controlCount;
            }

            public static LatticePreviewState Create(LatticeDeformer deformer)
            {
                if (deformer == null)
                {
                    return default;
                }

                var settings = deformer.Settings;
                settings?.EnsureInitialized();

                ReadOnlySpan<Vector3> controlPoints = ReadOnlySpan<Vector3>.Empty;
                if (settings != null)
                {
                    controlPoints = settings.ControlPointsLocal;
                }
                int hash = 17;
                foreach (var point in controlPoints)
                {
                    hash = HashCode.Combine(hash, point.x, point.y, point.z);
                }

                var snapshot = new LatticePreviewState(
                    settings?.GridSize ?? Vector3Int.zero,
                    settings?.LocalBounds ?? new Bounds(Vector3.zero, Vector3.zero),
                    settings?.Interpolation ?? LatticeInterpolationMode.Trilinear,
                    settings != null,
                    deformer.SourceMesh != null ? deformer.SourceMesh.GetInstanceID() : 0,
                    hash,
                    controlPoints.Length);

                return snapshot;
            }

            public static bool Equals(LatticePreviewState lhs, LatticePreviewState rhs)
            {
                return lhs.Equals(rhs);
            }

            public bool Equals(LatticePreviewState other)
            {
                return _gridSize == other._gridSize &&
                       _boundsCenter == other._boundsCenter &&
                       _boundsSize == other._boundsSize &&
                       _interpolation == other._interpolation &&
                       _useJobs == other._useJobs &&
                       _sourceMeshId == other._sourceMeshId &&
                       _controlPointHash == other._controlPointHash &&
                       _controlPointCount == other._controlPointCount;
            }

            public override bool Equals(object obj)
            {
                return obj is LatticePreviewState other && Equals(other);
            }

            public override int GetHashCode()
            {
                var hash = HashCode.Combine(
                    _gridSize,
                    _boundsCenter,
                    _boundsSize,
                    (int)_interpolation,
                    _useJobs,
                    _sourceMeshId);

                hash = HashCode.Combine(hash, _controlPointHash);
                hash = HashCode.Combine(hash, _controlPointCount);

                return hash;
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
