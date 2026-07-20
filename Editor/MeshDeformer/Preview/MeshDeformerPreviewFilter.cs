#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using nadena.dev.ndmf.preview;
using UnityEditor;
using Unity.Profiling;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    [ExcludeFromCodeCoverage]
    internal sealed class LatticeDeformerPreviewFilter : IRenderFilter
    {
        private static readonly ProfilerMarker s_updateMeshMarker =
            new ProfilerMarker("Preview.UpdateMesh");
        private static readonly ProfilerMarker s_copyBlendShapesMarker =
            new ProfilerMarker("Preview.CopyBlendShapes");
        private static readonly ProfilerMarker s_deformMarker =
            new ProfilerMarker("Preview.Deform");
        private static readonly ProfilerMarker s_bakeBlendShapeSurfaceMarker =
            new ProfilerMarker("Preview.BakeBlendShapeSurfaceDeltas");
        internal static int BlendShapeCopyCount { get; set; }
        private readonly Dictionary<Renderer, LatticeDeformer> _rendererToDeformer = new Dictionary<Renderer, LatticeDeformer>();

        private static readonly TogglablePreviewNode s_previewToggle = TogglablePreviewNode.Create(
            () => LatticeLocalization.Tr(LocKey.MeshDeformer),
            typeof(LatticeDeformerPreviewFilter).FullName);

        internal static bool PreviewToggleEnabled => s_previewToggle.IsEnabled.Value;

        internal static int ComputeBlendShapeWeightStateHash(
            SkinnedMeshRenderer renderer,
            Mesh sourceMesh)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (renderer != null ? renderer.GetInstanceID() : 0);
                hash = hash * 31 + (sourceMesh != null ? sourceMesh.GetInstanceID() : 0);

                int sourceBlendShapeCount = sourceMesh != null ? sourceMesh.blendShapeCount : 0;
                hash = hash * 31 + sourceBlendShapeCount;

                var assignedMesh = renderer != null ? renderer.sharedMesh : null;
                hash = hash * 31 + (assignedMesh != null ? assignedMesh.GetInstanceID() : 0);
                int assignedBlendShapeCount = assignedMesh != null ? assignedMesh.blendShapeCount : 0;
                hash = hash * 31 + assignedBlendShapeCount;

                if (renderer == null)
                {
                    return hash;
                }

                int readableWeightCount = Mathf.Min(sourceBlendShapeCount, assignedBlendShapeCount);
                for (int i = 0; i < sourceBlendShapeCount; i++)
                {
                    float weight = i < readableWeightCount ? renderer.GetBlendShapeWeight(i) : 0f;
                    hash = hash * 31 + BitConverter.SingleToInt32Bits(weight);
                }

                return hash;
            }
        }

        internal static Mesh GetRendererMesh(Renderer renderer)
        {
            switch (renderer)
            {
                case SkinnedMeshRenderer skinned:
                    return skinned.sharedMesh;
                case MeshRenderer meshRenderer:
                    return meshRenderer.GetComponent<MeshFilter>()?.sharedMesh;
                default:
                    return null;
            }
        }

        internal static void AssignRendererMesh(Renderer renderer, Mesh mesh)
        {
            switch (renderer)
            {
                case SkinnedMeshRenderer skinned:
                    skinned.sharedMesh = mesh;
                    break;
                case MeshRenderer meshRenderer:
                {
                    var meshFilter = meshRenderer.GetComponent<MeshFilter>();
                    if (meshFilter != null)
                    {
                        meshFilter.sharedMesh = mesh;
                    }

                    break;
                }
            }
        }

        internal static void RestoreProxyMesh(
            Renderer original,
            Renderer proxy,
            Mesh previousProxyMesh,
            long registrationGeneration)
        {
            // A replacement node may already own this original/proxy pair. In that
            // case the older node must neither overwrite the replacement mesh nor
            // remove the replacement's alignment registration.
            bool ownsRegistration = LatticePreviewUtility.IsCurrentProxyRegistration(
                original,
                proxy,
                registrationGeneration);
            if (!ownsRegistration && LatticePreviewUtility.IsProxyRegistered(original, proxy))
            {
                // A newer node is using the same proxy renderer.
                return;
            }

            try
            {
                if (proxy != null)
                {
                    AssignRendererMesh(proxy, previousProxyMesh);
                }
            }
            finally
            {
                LatticePreviewUtility.ClearProxy(original, proxy, registrationGeneration);
            }
        }

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
                .GroupBy(p => (p.original.GetInstanceID(), p.proxy.GetInstanceID()))
                .Select(grouped => grouped.First())
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

            var evaluationPair = pairList.FirstOrDefault(pair =>
                pair.original != null && pair.original.GetComponent<LatticeDeformer>() == deformer);
            Mesh evaluationTarget = GetRendererMesh(evaluationPair.proxy);
            var diagnostics = MeshDeformerValidator.Validate(deformer, evaluationTarget);
            MeshDeformerValidator.Log(diagnostics);
            if (MeshDeformerValidator.HasErrors(diagnostics))
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
            private readonly List<Vector3> _vertexBuffer = new List<Vector3>();
            private readonly List<Vector3> _normalBuffer = new List<Vector3>();
            private readonly List<Vector4> _tangentBuffer = new List<Vector4>();
            private readonly BlendShapeCopyBuffers _blendShapeBuffers = new BlendShapeCopyBuffers();
            private int _lastBlendShapeWeightStateHash;
            private readonly int _sourceBlendShapeCount;
            private bool _suppressProxySourceBlendShapeWeights;

            public PreviewNode(LatticeDeformer deformer, IEnumerable<(Renderer original, Renderer proxy)> proxyPairs, Mesh previewMesh)
            {
                _deformer = deformer;
                _previewMesh = previewMesh;
                _previewMesh.MarkDynamic();
                _sourceBlendShapeCount = _deformer != null && _deformer.SourceMesh != null
                    ? _deformer.SourceMesh.blendShapeCount
                    : 0;
                _lastBlendShapeWeightStateHash = ComputeBlendShapeWeightStateHash(
                    _deformer != null ? _deformer.GetComponent<SkinnedMeshRenderer>() : null,
                    _deformer != null ? _deformer.SourceMesh : null);

                foreach (var (original, proxy) in proxyPairs)
                {
                    if (original == null || proxy == null)
                    {
                        continue;
                    }

                    var observedProxyMesh = GetRendererMesh(proxy);
                    long registrationGeneration = LatticePreviewUtility.RegisterProxy(
                        original,
                        proxy,
                        observedProxyMesh,
                        out var restorationMesh);
                    var target = new Target
                    {
                        OriginalRenderer = original,
                        ProxyRenderer = proxy,
                        PreviousProxyMesh = restorationMesh,
                        PreviousProxyBlendShapeWeights = CaptureBlendShapeWeights(proxy),
                        RegistrationGeneration = registrationGeneration,
                    };

                    ApplyPreviewMesh(target);
                    _targets.Add(target);
                }
            }

            public RenderAspects WhatChanged => RenderAspects.Mesh;

            public void OnFrame(Renderer original, Renderer proxy)
            {
                var target = EnsureTarget(original, proxy);
                ApplyPreviewMesh(target);
            }

            public void OnFrameGroup()
            {
                int currentHash = ComputeBlendShapeWeightStateHash(
                    _deformer != null ? _deformer.GetComponent<SkinnedMeshRenderer>() : null,
                    _deformer != null ? _deformer.SourceMesh : null);
                if (currentHash == _lastBlendShapeWeightStateHash)
                {
                    if (_suppressProxySourceBlendShapeWeights)
                        SuppressProxySourceBlendShapeWeights();
                    return;
                }

                if (UpdatePreviewMesh())
                {
                    _lastBlendShapeWeightStateHash = ComputeBlendShapeWeightStateHash(
                        _deformer != null ? _deformer.GetComponent<SkinnedMeshRenderer>() : null,
                        _deformer != null ? _deformer.SourceMesh : null);
                }
            }

            public void Dispose()
            {
                foreach (var target in _targets)
                {
                    bool restoreWeights = LatticePreviewUtility.IsCurrentProxyRegistration(
                        target.OriginalRenderer,
                        target.ProxyRenderer,
                        target.RegistrationGeneration);
                    RestoreProxyMesh(
                        target.OriginalRenderer,
                        target.ProxyRenderer,
                        target.PreviousProxyMesh,
                        target.RegistrationGeneration);
                    if (restoreWeights)
                        RestoreProxyBlendShapeWeights(
                            target.ProxyRenderer,
                            target.PreviousProxyBlendShapeWeights,
                            _deformer != null ? _deformer.GetComponent<SkinnedMeshRenderer>() : null,
                            _deformer != null ? _deformer.SourceMesh : null);
                }

                if (_previewMesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(_previewMesh);
                }
            }

            private void ApplyPreviewMesh(Target target)
            {
                if (target == null ||
                    !LatticePreviewUtility.IsCurrentProxyRegistration(
                        target.OriginalRenderer,
                        target.ProxyRenderer,
                        target.RegistrationGeneration))
                {
                    return;
                }

                AssignRendererMesh(target.ProxyRenderer, _previewMesh);
                if (_suppressProxySourceBlendShapeWeights)
                    SuppressProxySourceBlendShapeWeights(target.ProxyRenderer);
            }

            private bool UpdatePreviewMesh()
            {
                if (_deformer == null || _previewMesh == null)
                {
                    return false;
                }

                Mesh runtimeMesh;
                using (s_deformMarker.Auto())
                    runtimeMesh = _deformer.Deform(false);
                if (runtimeMesh == null)
                {
                    return false;
                }

                using (s_updateMeshMarker.Auto())
                {
                    runtimeMesh.GetVertices(_vertexBuffer);
                    if (_vertexBuffer.Count > 0)
                        _previewMesh.SetVertices(_vertexBuffer);

                    runtimeMesh.GetNormals(_normalBuffer);
                    if (_normalBuffer.Count == _previewMesh.vertexCount)
                        _previewMesh.SetNormals(_normalBuffer);

                    runtimeMesh.GetTangents(_tangentBuffer);
                    if (_tangentBuffer.Count == _previewMesh.vertexCount)
                        _previewMesh.SetTangents(_tangentBuffer);

                    if (HasSourceOnlyBlendShapeLayout(runtimeMesh))
                    {
                        BakeCurrentSourceBlendShapeSurfaceDeltas(
                            _deformer.SourceMesh,
                            _deformer.GetComponent<SkinnedMeshRenderer>(),
                            _normalBuffer,
                            _tangentBuffer,
                            _blendShapeBuffers);
                        if (_normalBuffer.Count == _previewMesh.vertexCount)
                            _previewMesh.SetNormals(_normalBuffer);
                        if (_tangentBuffer.Count == _previewMesh.vertexCount)
                            _previewMesh.SetTangents(_tangentBuffer);
                        _suppressProxySourceBlendShapeWeights = true;
                    }
                    else
                    {
                        CopyBlendShapes(runtimeMesh, _previewMesh, _blendShapeBuffers);
                        _suppressProxySourceBlendShapeWeights = false;
                    }
                    _previewMesh.bounds = runtimeMesh.bounds;
                    _previewMesh.UploadMeshData(false);
                }

                foreach (var target in _targets)
                {
                    ApplyPreviewMesh(target);
                }
                if (!_suppressProxySourceBlendShapeWeights)
                    SynchronizeProxySourceBlendShapeWeights();

                return true;
            }

            private bool HasSourceOnlyBlendShapeLayout(Mesh runtimeMesh)
            {
                Mesh sourceMesh = _deformer != null ? _deformer.SourceMesh : null;
                return runtimeMesh != null && sourceMesh != null &&
                       runtimeMesh.blendShapeCount == _sourceBlendShapeCount;
            }

            private void SuppressProxySourceBlendShapeWeights()
            {
                for (int targetIndex = 0; targetIndex < _targets.Count; targetIndex++)
                    SuppressProxySourceBlendShapeWeights(_targets[targetIndex]?.ProxyRenderer);
            }

            private void SuppressProxySourceBlendShapeWeights(Renderer renderer)
            {
                if (renderer is not SkinnedMeshRenderer skinned || skinned.sharedMesh == null) return;
                int count = Mathf.Min(_sourceBlendShapeCount, skinned.sharedMesh.blendShapeCount);
                for (int shape = 0; shape < count; shape++)
                    skinned.SetBlendShapeWeight(shape, 0f);
            }

            private void SynchronizeProxySourceBlendShapeWeights()
            {
                var original = _deformer != null
                    ? _deformer.GetComponent<SkinnedMeshRenderer>()
                    : null;
                if (original == null) return;
                for (int targetIndex = 0; targetIndex < _targets.Count; targetIndex++)
                {
                    if (_targets[targetIndex]?.ProxyRenderer is not SkinnedMeshRenderer proxy ||
                        proxy.sharedMesh == null)
                        continue;
                    int count = Mathf.Min(
                        _sourceBlendShapeCount,
                        proxy.sharedMesh.blendShapeCount);
                    for (int shape = 0; shape < count; shape++)
                        proxy.SetBlendShapeWeight(shape, original.GetBlendShapeWeight(shape));
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

                var observedProxyMesh = GetRendererMesh(proxy);
                long registrationGeneration = LatticePreviewUtility.RegisterProxy(
                    original,
                    proxy,
                    observedProxyMesh,
                    out var restorationMesh);
                var target = new Target
                {
                    OriginalRenderer = original,
                    ProxyRenderer = proxy,
                    PreviousProxyMesh = restorationMesh,
                    PreviousProxyBlendShapeWeights = CaptureBlendShapeWeights(proxy),
                    RegistrationGeneration = registrationGeneration,
                };

                ApplyPreviewMesh(target);
                _targets.Add(target);
                return target;
            }
        }

        private sealed class Target
        {
            public Renderer OriginalRenderer;
            public Renderer ProxyRenderer;
            public Mesh PreviousProxyMesh;
            public float[] PreviousProxyBlendShapeWeights;
            public long RegistrationGeneration;
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
            catch
            {
                return null;
            }
        }

        internal static IReadOnlyList<MeshDeformerDiagnostic> ValidateBeforePreview(
            LatticeDeformer deformer,
            Mesh evaluationTarget = null)
        {
            return MeshDeformerValidator.Validate(deformer, evaluationTarget);
        }

        internal sealed class BlendShapeCopyBuffers
        {
            internal Vector3[] DeltaVertices = Array.Empty<Vector3>();
            internal Vector3[] DeltaNormals = Array.Empty<Vector3>();
            internal Vector3[] DeltaTangents = Array.Empty<Vector3>();
            internal Vector3[] UpperVertices = Array.Empty<Vector3>();
            internal Vector3[] UpperNormals = Array.Empty<Vector3>();
            internal Vector3[] UpperTangents = Array.Empty<Vector3>();

            internal void EnsureCapacity(int vertexCount)
            {
                if (DeltaVertices.Length == vertexCount) return;
                DeltaVertices = new Vector3[vertexCount];
                DeltaNormals = new Vector3[vertexCount];
                DeltaTangents = new Vector3[vertexCount];
                UpperVertices = new Vector3[vertexCount];
                UpperNormals = new Vector3[vertexCount];
                UpperTangents = new Vector3[vertexCount];
            }
        }

        private static float[] CaptureBlendShapeWeights(Renderer renderer)
        {
            if (renderer is not SkinnedMeshRenderer skinned || skinned.sharedMesh == null)
                return Array.Empty<float>();
            int count = skinned.sharedMesh.blendShapeCount;
            var weights = new float[count];
            for (int shape = 0; shape < count; shape++)
                weights[shape] = skinned.GetBlendShapeWeight(shape);
            return weights;
        }

        private static void RestoreProxyBlendShapeWeights(
            Renderer renderer,
            float[] capturedWeights,
            SkinnedMeshRenderer original,
            Mesh sourceMesh)
        {
            if (renderer is not SkinnedMeshRenderer skinned || skinned.sharedMesh == null ||
                capturedWeights == null)
                return;
            int count = Mathf.Min(capturedWeights.Length, skinned.sharedMesh.blendShapeCount);
            for (int shape = 0; shape < count; shape++)
                skinned.SetBlendShapeWeight(shape, capturedWeights[shape]);

            if (original == null || sourceMesh == null) return;
            for (int sourceShape = 0; sourceShape < sourceMesh.blendShapeCount; sourceShape++)
            {
                string shapeName = sourceMesh.GetBlendShapeName(sourceShape);
                int restoredShape = skinned.sharedMesh.GetBlendShapeIndex(shapeName);
                if (restoredShape < 0) continue;
                skinned.SetBlendShapeWeight(
                    restoredShape,
                    original.GetBlendShapeWeight(sourceShape));
            }
        }

        internal static void BakeCurrentSourceBlendShapeSurfaceDeltas(
            Mesh source,
            SkinnedMeshRenderer renderer,
            List<Vector3> normals,
            List<Vector4> tangents,
            BlendShapeCopyBuffers buffers)
        {
            if (source == null || renderer == null || buffers == null) return;
            int vertexCount = source.vertexCount;
            bool bakeNormals = normals != null && normals.Count == vertexCount;
            bool bakeTangents = tangents != null && tangents.Count == vertexCount;
            if (!bakeNormals && !bakeTangents) return;

            buffers.EnsureCapacity(vertexCount);
            using var bakeScope = s_bakeBlendShapeSurfaceMarker.Auto();
            int shapeCount = Mathf.Min(source.blendShapeCount, renderer.sharedMesh != null
                ? renderer.sharedMesh.blendShapeCount
                : 0);
            for (int shape = 0; shape < shapeCount; shape++)
            {
                float weight = renderer.GetBlendShapeWeight(shape);
                if (Mathf.Abs(weight) <= 1e-5f) continue;
                int frameCount = source.GetBlendShapeFrameCount(shape);
                if (frameCount <= 0) continue;

                int lowerFrame = 0;
                int upperFrame = 0;
                float scale = 0f;
                float firstWeight = source.GetBlendShapeFrameWeight(shape, 0);
                if (frameCount == 1 || weight <= firstWeight)
                {
                    scale = Mathf.Abs(firstWeight) > Mathf.Epsilon ? weight / firstWeight : 0f;
                }
                else
                {
                    lowerFrame = frameCount - 1;
                    upperFrame = lowerFrame;
                    scale = 1f;
                    for (int frame = 1; frame < frameCount; frame++)
                    {
                        float upperWeight = source.GetBlendShapeFrameWeight(shape, frame);
                        if (weight > upperWeight) continue;
                        lowerFrame = frame - 1;
                        upperFrame = frame;
                        float lowerWeight = source.GetBlendShapeFrameWeight(shape, lowerFrame);
                        scale = Mathf.Abs(upperWeight - lowerWeight) > Mathf.Epsilon
                            ? Mathf.InverseLerp(lowerWeight, upperWeight, weight)
                            : 0f;
                        break;
                    }
                }

                source.GetBlendShapeFrameVertices(
                    shape,
                    lowerFrame,
                    buffers.DeltaVertices,
                    buffers.DeltaNormals,
                    buffers.DeltaTangents);
                if (upperFrame != lowerFrame)
                {
                    source.GetBlendShapeFrameVertices(
                        shape,
                        upperFrame,
                        buffers.UpperVertices,
                        buffers.UpperNormals,
                        buffers.UpperTangents);
                }

                for (int vertex = 0; vertex < vertexCount; vertex++)
                {
                    Vector3 normalDelta = upperFrame == lowerFrame
                        ? buffers.DeltaNormals[vertex] * scale
                        : Vector3.LerpUnclamped(
                            buffers.DeltaNormals[vertex], buffers.UpperNormals[vertex], scale);
                    Vector3 tangentDelta = upperFrame == lowerFrame
                        ? buffers.DeltaTangents[vertex] * scale
                        : Vector3.LerpUnclamped(
                            buffers.DeltaTangents[vertex], buffers.UpperTangents[vertex], scale);
                    if (bakeNormals) normals[vertex] += normalDelta;
                    if (bakeTangents)
                    {
                        Vector4 tangent = tangents[vertex];
                        tangent.x += tangentDelta.x;
                        tangent.y += tangentDelta.y;
                        tangent.z += tangentDelta.z;
                        tangents[vertex] = tangent;
                    }
                }
            }
        }

        private static void CopyBlendShapes(
            Mesh source,
            Mesh destination,
            BlendShapeCopyBuffers buffers)
        {
            if (source == null || destination == null || source.vertexCount != destination.vertexCount)
            {
                return;
            }

            using var copyScope = s_copyBlendShapesMarker.Auto();
            BlendShapeCopyCount++;
            destination.ClearBlendShapes();

            int shapeCount = source.blendShapeCount;
            int vertexCount = source.vertexCount;
            buffers ??= new BlendShapeCopyBuffers();
            buffers.EnsureCapacity(vertexCount);
            for (int shape = 0; shape < shapeCount; shape++)
            {
                string shapeName = source.GetBlendShapeName(shape);
                int frameCount = source.GetBlendShapeFrameCount(shape);
                for (int frame = 0; frame < frameCount; frame++)
                {
                    float frameWeight = source.GetBlendShapeFrameWeight(shape, frame);
                    source.GetBlendShapeFrameVertices(
                        shape,
                        frame,
                        buffers.DeltaVertices,
                        buffers.DeltaNormals,
                        buffers.DeltaTangents);
                    destination.AddBlendShapeFrame(
                        shapeName,
                        frameWeight,
                        buffers.DeltaVertices,
                        buffers.DeltaNormals,
                        buffers.DeltaTangents);
                }
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
                int layeredHash = deformer.ComputeLayeredStateHash();
                int controlPointCount = settings != null ? settings.ControlPointCount : 0;

                var snapshot = new LatticePreviewState(
                    settings?.GridSize ?? Vector3Int.zero,
                    settings?.LocalBounds ?? new Bounds(Vector3.zero, Vector3.zero),
                    settings?.Interpolation ?? LatticeInterpolationMode.Trilinear,
                    settings != null,
                    deformer.SourceMesh != null ? deformer.SourceMesh.GetInstanceID() : 0,
                    layeredHash,
                    controlPointCount);

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
