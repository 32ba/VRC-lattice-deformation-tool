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
        internal enum Placement
        {
            Any,
            BeforeTopologyChanges,
            AfterTopologyChanges,
        }

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
        private readonly Placement _placement;

        internal LatticeDeformerPreviewFilter(Placement placement = Placement.Any)
        {
            _placement = placement;
        }

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
            if (!ObservePreviewEnabled(context))
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

                // Keep observing hierarchy activity so NDMF rebuilds when an outfit is
                // toggled, but do not remove an enabled deformer from the graph merely
                // because MA or another upstream operation currently owns an inactive
                // source object. Such operations can produce an active downstream proxy.
                _ = context.ActiveAndEnabled(deformer);
                if (!deformer.enabled)
                {
                    continue;
                }

                if (!MatchesPlacement(deformer))
                {
                    continue;
                }

                var interactiveRevision = LatticePreviewUtility.GetInteractiveRevision(deformer);
                if (interactiveRevision != null)
                {
                    _ = context.Observe(interactiveRevision);
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
                return Task.FromResult<IRenderFilterNode>(new NoOpNode());
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
                return Task.FromResult<IRenderFilterNode>(new NoOpNode());
            }

            if (!MatchesPlacement(deformer))
            {
                return Task.FromResult<IRenderFilterNode>(new NoOpNode());
            }

            var interactiveRevision = LatticePreviewUtility.GetInteractiveRevision(deformer);
            if (interactiveRevision != null)
            {
                _ = context.Observe(interactiveRevision);
            }

            var evaluationPair = pairList.FirstOrDefault(pair =>
                pair.original != null && pair.original.GetComponent<LatticeDeformer>() == deformer);
            Mesh evaluationTarget = GetRendererMesh(evaluationPair.proxy);
            var diagnostics = ValidateBeforePreview(
                deformer,
                evaluationTarget,
                _placement == Placement.AfterTopologyChanges &&
                deformer.CanPreviewAfterTopologyChanges());
            MeshDeformerValidator.Log(diagnostics);
            if (MeshDeformerValidator.HasErrors(diagnostics))
            {
                return Task.FromResult<IRenderFilterNode>(new NoOpNode());
            }

            // Only structural changes should replace the node. Interactive layer
            // changes are applied to the existing preview mesh in OnFrameGroup so
            // the proxy is never restored to its upstream mesh between drag frames.
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

            var previewMesh = GeneratePreviewMeshFromInput(deformer, evaluationTarget);
            if (previewMesh == null)
            {
                return Task.FromResult<IRenderFilterNode>(new NoOpNode());
            }

            var node = new PreviewNode(deformer, pairList, previewMesh, evaluationTarget);
            return Task.FromResult<IRenderFilterNode>(node);
        }

        private bool MatchesPlacement(LatticeDeformer deformer)
        {
            if (deformer == null) return false;
            bool canRunAfterTopologyChanges = deformer.CanPreviewAfterTopologyChanges();
            return _placement switch
            {
                Placement.BeforeTopologyChanges => !canRunAfterTopologyChanges,
                Placement.AfterTopologyChanges => canRunAfterTopologyChanges,
                _ => true,
            };
        }

        internal static bool ObservePreviewEnabled(ComputeContext context)
        {
            return context.Observe(s_previewToggle.IsEnabled);
        }

        internal static bool RequiresDownstreamMeshRefresh(Renderer renderer)
        {
            if (renderer == null)
            {
                return false;
            }

            foreach (var component in renderer.GetComponents<Component>())
            {
                if (component == null)
                {
                    continue;
                }

                Type type = component.GetType();
                if (IsAvatarOptimizerMeshRemovalType(type.Namespace, type.Name))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsAvatarOptimizerMeshRemovalType(
            string typeNamespace,
            string typeName)
        {
            return string.Equals(
                       typeNamespace,
                       "Anatawa12.AvatarOptimizer",
                       StringComparison.Ordinal) &&
                   typeName != null &&
                   typeName.StartsWith("RemoveMesh", StringComparison.Ordinal);
        }

        private sealed class NoOpNode : IRenderFilterNode
        {
            public RenderAspects WhatChanged => 0;

            public void OnFrameGroup()
            {
            }

            public void OnFrame(Renderer original, Renderer proxy)
            {
            }

            public void Dispose()
            {
            }
        }

        private sealed class PreviewNode : IRenderFilterNode
        {
            private readonly LatticeDeformer _deformer;
            private readonly List<Target> _targets = new List<Target>();
            private readonly Mesh _previewMesh;
            private readonly Mesh _upstreamMesh;
            private int _lastDeformationDataRevision;
            private int _lastRuntimeMeshRevision;

            public PreviewNode(
                LatticeDeformer deformer,
                IEnumerable<(Renderer original, Renderer proxy)> proxyPairs,
                Mesh previewMesh,
                Mesh upstreamMesh = null)
            {
                _deformer = deformer;
                _previewMesh = previewMesh;
                _upstreamMesh = upstreamMesh ?? proxyPairs
                    .Select(pair => GetRendererMesh(pair.proxy))
                    .FirstOrDefault(mesh => mesh != null);
                _previewMesh.MarkDynamic();
                _lastDeformationDataRevision = _deformer != null
                    ? _deformer.DeformationDataRevision
                    : 0;
                _lastRuntimeMeshRevision = _deformer != null
                    ? _deformer.RuntimeMeshRevision
                    : 0;

                foreach (var (original, proxy) in proxyPairs)
                {
                    if (original == null || proxy == null)
                    {
                        continue;
                    }

                    var target = new Target
                    {
                        ProxyRenderer = proxy,
                    };

                    ApplyPreviewMesh(target);
                    _targets.Add(target);
                }

                LatticePreviewUtility.RegisterPreviewUndoTarget(_deformer);
                LatticePreviewUtility.InteractiveDeformationPublished +=
                    OnInteractiveDeformationPublished;
            }

            public RenderAspects WhatChanged => RenderAspects.Mesh;

            public void OnFrame(Renderer original, Renderer proxy)
            {
                var target = EnsureTarget(original, proxy);
                ApplyPreviewMesh(target);
            }

            public void OnFrameGroup()
            {
                int currentDeformationDataRevision = _deformer != null
                    ? _deformer.DeformationDataRevision
                    : 0;
                int currentRuntimeMeshRevision = _deformer != null
                    ? _deformer.RuntimeMeshRevision
                    : 0;
                bool deformationChanged =
                    currentDeformationDataRevision != _lastDeformationDataRevision ||
                    currentRuntimeMeshRevision != _lastRuntimeMeshRevision;
                if (!deformationChanged) return;

                // Keep the same Mesh instance assigned to every proxy. Replacing the
                // node here would briefly restore the upstream mesh and visibly drop
                // active source BlendShapes for one rendered frame.
                //
                UpdateAndPublishPreviewMesh();
            }

            public void Dispose()
            {
                LatticePreviewUtility.InteractiveDeformationPublished -=
                    OnInteractiveDeformationPublished;
                LatticePreviewUtility.UnregisterPreviewUndoTarget(_deformer);

                if (_previewMesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(_previewMesh);
                }
            }

            private void OnInteractiveDeformationPublished(LatticeDeformer deformer)
            {
                if (!ReferenceEquals(deformer, _deformer))
                {
                    return;
                }

                UpdateAndPublishPreviewMesh();
            }

            private bool UpdateAndPublishPreviewMesh()
            {
                if (!UpdatePreviewMesh())
                {
                    return false;
                }

                _lastDeformationDataRevision = _deformer != null
                    ? _deformer.DeformationDataRevision
                    : 0;
                _lastRuntimeMeshRevision = _deformer != null
                    ? _deformer.RuntimeMeshRevision
                    : 0;
                return true;
            }

            private void ApplyPreviewMesh(Target target)
            {
                if (target == null || target.ProxyRenderer == null)
                {
                    return;
                }

                AssignRendererMesh(target.ProxyRenderer, _previewMesh);
            }

            private bool UpdatePreviewMesh()
            {
                if (_deformer == null || _previewMesh == null)
                {
                    return false;
                }

                Mesh runtimeMesh;
                using (s_deformMarker.Auto())
                    runtimeMesh = _deformer.CreatePreviewMeshFromInput(_upstreamMesh);
                if (runtimeMesh == null)
                {
                    return false;
                }

                try
                {
                    using (s_updateMeshMarker.Auto())
                    {
                        EditorUtility.CopySerialized(runtimeMesh, _previewMesh);
                        _previewMesh.hideFlags = HideFlags.HideAndDontSave;
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(runtimeMesh);
                }

                foreach (var target in _targets)
                {
                    ApplyPreviewMesh(target);
                }
                return true;
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
                    ProxyRenderer = proxy,
                };

                ApplyPreviewMesh(target);
                _targets.Add(target);
                return target;
            }
        }

        private sealed class NoOpNode : IRenderFilterNode
        {
            public RenderAspects WhatChanged => 0;

            public void OnFrame(Renderer original, Renderer proxy)
            {
            }

            public void OnFrameGroup()
            {
            }

            public void Dispose()
            {
            }
        }

        private sealed class Target
        {
            public Renderer ProxyRenderer;
        }

        private static Mesh GeneratePreviewMesh(LatticeDeformer deformer)
        {
            return GeneratePreviewMeshFromInput(deformer, deformer != null ? deformer.SourceMesh : null);
        }

        private static Mesh GeneratePreviewMeshFromInput(LatticeDeformer deformer, Mesh inputMesh)
        {
            try
            {
                var runtimeMesh = deformer.CreatePreviewMeshFromInput(inputMesh ?? deformer.SourceMesh);
                if (runtimeMesh == null)
                {
                    return null;
                }
                runtimeMesh.hideFlags = HideFlags.HideAndDontSave;
                return runtimeMesh;
            }
            catch
            {
                return null;
            }
        }

        internal static IReadOnlyList<MeshDeformerDiagnostic> ValidateBeforePreview(
            LatticeDeformer deformer,
            Mesh evaluationTarget = null,
            bool intentionalTopologyChangedInput = false)
        {
            var diagnostics = MeshDeformerValidator.Validate(deformer, evaluationTarget);
            if (!intentionalTopologyChangedInput)
            {
                return diagnostics;
            }

            // The late lattice-only preview deliberately evaluates the completed
            // upstream NDMF mesh, whose topology may differ from the authored
            // renderer. MDV018 protects callers which accidentally mix Preview and
            // Bake targets; it is not actionable for this explicitly-routed spatial
            // deformation path. Preserve every other diagnostic unchanged.
            return diagnostics
                .Where(diagnostic =>
                    diagnostic.Code != MeshDeformerValidator.PreviewBakeTargetMismatch)
                .ToArray();
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

        internal static void CopyBlendShapes(
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

        internal static void AppendMissingBlendShapes(
            Mesh source,
            Mesh destination,
            BlendShapeCopyBuffers buffers)
        {
            if (source == null || destination == null ||
                source.vertexCount != destination.vertexCount)
                return;

            var existingNames = new HashSet<string>(StringComparer.Ordinal);
            for (int shape = 0; shape < destination.blendShapeCount; shape++)
                existingNames.Add(destination.GetBlendShapeName(shape));

            buffers ??= new BlendShapeCopyBuffers();
            buffers.EnsureCapacity(source.vertexCount);
            for (int shape = 0; shape < source.blendShapeCount; shape++)
            {
                string name = source.GetBlendShapeName(shape);
                if (!existingNames.Add(name)) continue;

                int frameCount = source.GetBlendShapeFrameCount(shape);
                for (int frame = 0; frame < frameCount; frame++)
                {
                    source.GetBlendShapeFrameVertices(
                        shape,
                        frame,
                        buffers.DeltaVertices,
                        buffers.DeltaNormals,
                        buffers.DeltaTangents);
                    destination.AddBlendShapeFrame(
                        name,
                        source.GetBlendShapeFrameWeight(shape, frame),
                        buffers.DeltaVertices,
                        buffers.DeltaNormals,
                        buffers.DeltaTangents);
                }
            }
        }

        private readonly struct LatticePreviewState : IEquatable<LatticePreviewState>
        {
            private readonly int _sourceMeshId;
            private readonly int _sourceVertexCount;
            private readonly int _sourceSubMeshCount;

            private LatticePreviewState(
                int sourceMeshId,
                int sourceVertexCount,
                int sourceSubMeshCount)
            {
                _sourceMeshId = sourceMeshId;
                _sourceVertexCount = sourceVertexCount;
                _sourceSubMeshCount = sourceSubMeshCount;
            }

            public static LatticePreviewState Create(LatticeDeformer deformer)
            {
                if (deformer == null)
                {
                    return default;
                }

                Mesh sourceMesh = deformer.SourceMesh;
                return new LatticePreviewState(
                    sourceMesh != null ? sourceMesh.GetInstanceID() : 0,
                    sourceMesh != null ? sourceMesh.vertexCount : 0,
                    sourceMesh != null ? sourceMesh.subMeshCount : 0);
            }

            public static bool Equals(LatticePreviewState lhs, LatticePreviewState rhs)
            {
                return lhs.Equals(rhs);
            }

            public bool Equals(LatticePreviewState other)
            {
                return _sourceMeshId == other._sourceMeshId &&
                       _sourceVertexCount == other._sourceVertexCount &&
                       _sourceSubMeshCount == other._sourceSubMeshCount;
            }

            public override bool Equals(object obj)
            {
                return obj is LatticePreviewState other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(
                    _sourceMeshId,
                    _sourceVertexCount,
                    _sourceSubMeshCount);
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
