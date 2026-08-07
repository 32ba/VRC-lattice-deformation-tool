#if UNITY_EDITOR
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using nadena.dev.ndmf.preview;
using UnityEditor;
using UnityEngine;
using Net._32Ba.LatticeDeformationTool;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    [ExcludeFromCodeCoverage]
    internal static class LatticePreviewUtility
    {
        private const string k_PreviewAlignedKey = "Net32Ba.LatticeDeformer.UsePreviewAlignedCage";
        private const string k_DebugAlignKey = "Net32Ba.LatticeDeformer.DebugAlignLogs";
        private static readonly Dictionary<Renderer, ProxyRegistration> s_latestProxyMap = new();
        private static readonly Dictionary<Renderer, PreviewMeshRegistration> s_previewMeshMap = new();
        private static long s_nextProxyRegistrationGeneration;
        private static int s_proxyMappingRevision;

        private readonly struct ProxyRegistration
        {
            internal readonly Renderer Proxy;
            internal readonly long Generation;
            internal readonly Mesh RestorationMesh;
            internal readonly Renderer CageProxy;
            internal readonly long CageGeneration;

            internal ProxyRegistration(Renderer proxy, long generation, Mesh restorationMesh)
                : this(proxy, generation, restorationMesh, proxy, generation)
            {
            }

            internal ProxyRegistration(
                Renderer proxy,
                long generation,
                Mesh restorationMesh,
                Renderer cageProxy,
                long cageGeneration)
            {
                Proxy = proxy;
                Generation = generation;
                RestorationMesh = restorationMesh;
                CageProxy = cageProxy;
                CageGeneration = cageGeneration;
            }
        }

        private readonly struct PreviewMeshRegistration
        {
            internal readonly Mesh Mesh;
            internal readonly long Generation;
            internal readonly long ContentRevision;

            internal PreviewMeshRegistration(Mesh mesh, long generation, long contentRevision)
            {
                Mesh = mesh;
                Generation = generation;
                ContentRevision = contentRevision;
            }
        }

        /// <summary>
        /// A downstream preview node may temporarily replace the proxy selected by an
        /// upstream node. Keeping the previous registration in the token lets the
        /// downstream node restore it without invalidating the upstream node's owner
        /// generation.
        /// </summary>
        internal readonly struct ProxyOverrideToken
        {
            internal readonly Renderer Original;
            internal readonly Renderer Proxy;
            internal readonly long Generation;
            internal readonly bool HasPrevious;
            internal readonly Renderer PreviousProxy;
            internal readonly long PreviousGeneration;
            internal readonly Mesh PreviousRestorationMesh;
            internal readonly Renderer PreviousCageProxy;
            internal readonly long PreviousCageGeneration;

            internal ProxyOverrideToken(
                Renderer original,
                Renderer proxy,
                long generation,
                bool hasPrevious,
                Renderer previousProxy,
                long previousGeneration,
                Mesh previousRestorationMesh,
                Renderer previousCageProxy,
                long previousCageGeneration)
            {
                Original = original;
                Proxy = proxy;
                Generation = generation;
                HasPrevious = hasPrevious;
                PreviousProxy = previousProxy;
                PreviousGeneration = previousGeneration;
                PreviousRestorationMesh = previousRestorationMesh;
                PreviousCageProxy = previousCageProxy;
                PreviousCageGeneration = previousCageGeneration;
            }
        }

        /// <summary>
        /// Determines whether the runtime mesh should be assigned back to the renderer.
        /// When the NDMF preview pipeline is active we leave the original mesh untouched
        /// and rely on proxy renderers instead.
        /// </summary>
        public static bool ShouldAssignRuntimeMesh()
        {
            return ShouldAssignRuntimeMesh(
                NDMFPreview.DisablePreviewDepth,
                NDMFPreviewPrefs.instance.EnablePreview,
                LatticeDeformerPreviewFilter.PreviewToggleEnabled);
        }

        internal static bool ShouldAssignRuntimeMesh(int disablePreviewDepth, bool enablePreview, bool previewToggleEnabled)
        {
            if (disablePreviewDepth != 0)
            {
                return true;
            }

            if (!enablePreview)
            {
                return true;
            }

            return !previewToggleEnabled;
        }

        /// <summary>
        /// Whether to align lattice editing handles to the NDMF preview proxy transform (if any).
        /// Stored in EditorPrefs so it is per-user.
        /// </summary>
        public static bool UsePreviewAlignedCage
        {
            get => EditorPrefs.GetBool(k_PreviewAlignedKey, false);
            set => EditorPrefs.SetBool(k_PreviewAlignedKey, value);
        }

        /// <summary>
        /// Whether to apply bounds-based remapping when aligning to proxy. Off by default to avoid doubleスケール.
        /// </summary>

        public static bool DebugAlignLogs
        {
            get => EditorPrefs.GetBool(k_DebugAlignKey, false);
            set => EditorPrefs.SetBool(k_DebugAlignKey, value);
        }

        // Per-instance getters
        public static LatticeDeformer.LatticeAlignMode GetAlignMode(LatticeDeformer deformer) =>
            deformer != null ? deformer.AlignMode : LatticeDeformer.LatticeAlignMode.Mode3_BoundsRemap;

        public static float GetCenterClampMulXY(LatticeDeformer deformer) =>
            deformer != null ? deformer.CenterClampMulXY : 0f;

        public static float GetCenterClampMinXY(LatticeDeformer deformer) =>
            deformer != null ? deformer.CenterClampMinXY : 0f;

        public static float GetCenterClampMulZ(LatticeDeformer deformer) =>
            deformer != null ? deformer.CenterClampMulZ : 0f;

        public static float GetCenterClampMinZ(LatticeDeformer deformer) =>
            deformer != null ? deformer.CenterClampMinZ : 0f;

        public static bool GetAllowCenterOffsetWhenSkipped(LatticeDeformer deformer) =>
            deformer != null && deformer.AllowCenterOffsetWhenBoundsSkipped;

        public static Vector3 GetManualOffsetProxy(LatticeDeformer deformer) =>
            deformer != null ? deformer.ManualOffsetProxy : Vector3.zero;

        public static Vector3 GetManualScaleProxy(LatticeDeformer deformer) =>
            deformer != null ? deformer.ManualScaleProxy : Vector3.one;

        /// <summary>
        /// Returns the transform used for lattice editing. If preview alignment is enabled and a proxy
        /// renderer exists, its transform is used; otherwise the deformer.MeshTransform is returned.
        /// </summary>
        [ExcludeFromCodeCoverage]
        public static Transform GetEditingTransform(LatticeDeformer deformer)
        {
            if (deformer == null)
            {
                return null;
            }

            if (UsePreviewAlignedCage)
            {
                var renderer = deformer.GetComponent<Renderer>();
                if (renderer != null && TryGetPreviewProxy(renderer, out var proxy) && proxy != null) return proxy.transform;
            }

            return deformer.MeshTransform;
        }

        /// <summary>
        /// Returns the bounds to use for editing handles. When preview alignment is enabled and a proxy
        /// renderer exists, this returns the proxy's bounds converted into the editing transform's local space;
        /// otherwise it returns the source bounds unchanged.
        /// </summary>
        public static Bounds GetEditingBounds(LatticeDeformer deformer, Bounds sourceBounds, Transform editingTransform)
        {
            if (!UsePreviewAlignedCage || deformer == null)
            {
                return sourceBounds;
            }

            var renderer = deformer.GetComponent<Renderer>();
            if (renderer == null)
            {
                return sourceBounds;
            }

            if (!TryGetPreviewProxy(renderer, out var proxy) || proxy == null)
            {
                return sourceBounds;
            }

            var targetTransform = editingTransform != null ? editingTransform : proxy.transform;
            var worldBounds = GetRendererWorldBounds(proxy);
            return ToLocalBounds(targetTransform, worldBounds);
        }

        private static Bounds GetRendererWorldBounds(Renderer proxy)
        {
            // Prefer mesh local bounds to avoid inflated SkinnedMeshRenderer.bounds
            if (proxy is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            {
                return TransformMeshBounds(skinned.sharedMesh.bounds, proxy.transform);
            }

            if (proxy is MeshRenderer meshRenderer)
            {
                var mf = meshRenderer.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    return TransformMeshBounds(mf.sharedMesh.bounds, proxy.transform);
                }
            }

            return proxy.bounds;
        }

        private static Bounds TransformMeshBounds(Bounds localBounds, Transform transform)
        {
            var center = transform.TransformPoint(localBounds.center);
            var extents = localBounds.extents;
            var right = transform.TransformVector(extents.x, 0f, 0f);
            var up = transform.TransformVector(0f, extents.y, 0f);
            var fwd = transform.TransformVector(0f, 0f, extents.z);

            var worldExtents = new Vector3(
                Mathf.Abs(right.x) + Mathf.Abs(up.x) + Mathf.Abs(fwd.x),
                Mathf.Abs(right.y) + Mathf.Abs(up.y) + Mathf.Abs(fwd.y),
                Mathf.Abs(right.z) + Mathf.Abs(up.z) + Mathf.Abs(fwd.z));

            return new Bounds(center, worldExtents * 2f);
        }

        private static Bounds ToLocalBounds(Transform target, Bounds worldBounds)
        {
            if (target == null)
            {
                return worldBounds;
            }

            var center = target.InverseTransformPoint(worldBounds.center);

            // Transform extents by inverse rotation/scale using absolute axes
            var extents = worldBounds.extents;
            var right = target.InverseTransformVector(new Vector3(extents.x, 0f, 0f));
            var up = target.InverseTransformVector(new Vector3(0f, extents.y, 0f));
            var forward = target.InverseTransformVector(new Vector3(0f, 0f, extents.z));

            var localExtents = new Vector3(
                Mathf.Abs(right.x) + Mathf.Abs(up.x) + Mathf.Abs(forward.x),
                Mathf.Abs(right.y) + Mathf.Abs(up.y) + Mathf.Abs(forward.y),
                Mathf.Abs(right.z) + Mathf.Abs(up.z) + Mathf.Abs(forward.z));

            return new Bounds(center, localExtents * 2f);
        }

        [ExcludeFromCodeCoverage]
        public static void RequestSceneRepaint()
        {
            SceneView.RepaintAll();
        }

        internal static long RegisterProxy(Renderer original, Renderer proxy)
        {
            return RegisterProxy(original, proxy, null, out _);
        }

        internal static long RegisterPreviewMesh(Renderer original, Mesh previewMesh)
        {
            if (original == null || previewMesh == null)
            {
                return 0;
            }

            long generation;
            unchecked
            {
                generation = ++s_nextProxyRegistrationGeneration;
                if (generation == 0)
                {
                    generation = ++s_nextProxyRegistrationGeneration;
                }
            }

            s_previewMeshMap[original] = new PreviewMeshRegistration(previewMesh, generation, 0);
            return generation;
        }

        internal static bool TryGetPreviewMesh(Renderer original, out Mesh previewMesh)
        {
            return TryGetPreviewMesh(original, out previewMesh, out _);
        }

        internal static bool TryGetPreviewMesh(
            Renderer original,
            out Mesh previewMesh,
            out long contentRevision)
        {
            previewMesh = null;
            contentRevision = 0;
            if (object.ReferenceEquals(original, null) ||
                !s_previewMeshMap.TryGetValue(original, out var registration))
            {
                return false;
            }

            previewMesh = registration.Mesh;
            if (previewMesh != null)
            {
                contentRevision = registration.ContentRevision;
                return true;
            }

            s_previewMeshMap.Remove(original);
            previewMesh = null;
            return false;
        }

        internal static bool MarkPreviewMeshUpdated(
            Renderer original,
            Mesh previewMesh,
            long generation)
        {
            if (object.ReferenceEquals(original, null) || generation == 0 ||
                !s_previewMeshMap.TryGetValue(original, out var registration) ||
                registration.Generation != generation ||
                !object.ReferenceEquals(registration.Mesh, previewMesh))
            {
                return false;
            }

            long nextRevision;
            unchecked
            {
                nextRevision = registration.ContentRevision + 1;
            }

            s_previewMeshMap[original] = new PreviewMeshRegistration(
                registration.Mesh,
                registration.Generation,
                nextRevision);
            return true;
        }

        internal static bool ClearPreviewMesh(
            Renderer original,
            Mesh previewMesh,
            long generation)
        {
            if (object.ReferenceEquals(original, null) || generation == 0 ||
                !s_previewMeshMap.TryGetValue(original, out var registration) ||
                registration.Generation != generation ||
                !object.ReferenceEquals(registration.Mesh, previewMesh))
            {
                return false;
            }

            return s_previewMeshMap.Remove(original);
        }

        internal static long RegisterProxy(
            Renderer original,
            Renderer proxy,
            Mesh observedProxyMesh,
            out Mesh restorationMesh)
        {
            restorationMesh = observedProxyMesh;
            if (original == null || proxy == null)
            {
                return 0;
            }

            if (s_latestProxyMap.TryGetValue(original, out var previous) &&
                object.ReferenceEquals(previous.Proxy, proxy))
            {
                // NDMF may instantiate the replacement node before disposing the old
                // one, reusing the same proxy renderer. Preserve the first upstream
                // mesh instead of treating the old preview mesh as upstream input.
                restorationMesh = previous.RestorationMesh;
            }

            long generation;
            unchecked
            {
                generation = ++s_nextProxyRegistrationGeneration;
                if (generation == 0)
                {
                    generation = ++s_nextProxyRegistrationGeneration;
                }
            }

            s_latestProxyMap[original] = new ProxyRegistration(proxy, generation, restorationMesh);
            unchecked { s_proxyMappingRevision++; }
            return generation;
        }

        /// <summary>
        /// Registers a downstream proxy as a candidate while preserving the cage proxy
        /// committed by the upstream preview node. The candidate becomes visible to
        /// editor tools only after <see cref="CommitProxyOverride"/> confirms that its
        /// output has actually been assigned.
        /// </summary>
        internal static bool RegisterProxyOverride(
            Renderer original,
            Renderer proxy,
            Mesh observedProxyMesh,
            out Mesh restorationMesh,
            out ProxyOverrideToken token)
        {
            restorationMesh = observedProxyMesh;
            token = default;
            if (original == null || proxy == null)
            {
                return false;
            }

            bool hasPrevious = s_latestProxyMap.TryGetValue(original, out var previous);
            if (hasPrevious && previous.Proxy == null)
            {
                if (previous.CageProxy != null)
                {
                    // A candidate can be destroyed before OnFrame while the previously
                    // committed cage proxy is still valid. Preserve that committed
                    // frame as the predecessor of the replacement candidate.
                    previous = new ProxyRegistration(
                        previous.CageProxy,
                        previous.CageGeneration,
                        previous.RestorationMesh);
                }
                else
                {
                    s_latestProxyMap.Remove(original);
                    unchecked { s_proxyMappingRevision++; }
                    hasPrevious = false;
                }
            }

            if (hasPrevious && object.ReferenceEquals(previous.Proxy, proxy))
            {
                restorationMesh = previous.RestorationMesh;
                return false;
            }

            long generation;
            unchecked
            {
                generation = ++s_nextProxyRegistrationGeneration;
                if (generation == 0)
                {
                    generation = ++s_nextProxyRegistrationGeneration;
                }
            }

            s_latestProxyMap[original] = new ProxyRegistration(
                proxy,
                generation,
                restorationMesh,
                hasPrevious ? previous.CageProxy : null,
                hasPrevious ? previous.CageGeneration : 0);
            token = new ProxyOverrideToken(
                original,
                proxy,
                generation,
                hasPrevious,
                hasPrevious ? previous.Proxy : null,
                hasPrevious ? previous.Generation : 0,
                hasPrevious ? previous.RestorationMesh : null,
                hasPrevious ? previous.CageProxy : null,
                hasPrevious ? previous.CageGeneration : 0);
            return true;
        }

        /// <summary>
        /// Atomically promotes the latest downstream candidate after its output mesh is
        /// assigned to the renderer. Repeated or stale commits are ignored.
        /// </summary>
        internal static bool CommitProxyOverride(ProxyOverrideToken token)
        {
            if (object.ReferenceEquals(token.Original, null) ||
                token.Generation == 0 ||
                !s_latestProxyMap.TryGetValue(token.Original, out var current) ||
                current.Generation != token.Generation ||
                !object.ReferenceEquals(current.Proxy, token.Proxy))
            {
                return false;
            }

            if (current.CageGeneration == token.Generation &&
                object.ReferenceEquals(current.CageProxy, token.Proxy))
            {
                return true;
            }

            s_latestProxyMap[token.Original] = new ProxyRegistration(
                current.Proxy,
                current.Generation,
                current.RestorationMesh,
                current.Proxy,
                current.Generation);
            unchecked { s_proxyMappingRevision++; }
            return true;
        }

        /// <summary>
        /// Restores the registration replaced by <see cref="RegisterProxyOverride"/>
        /// only when the override is still the current owner.
        /// </summary>
        internal static bool ClearProxyOverride(ProxyOverrideToken token)
        {
            if (object.ReferenceEquals(token.Original, null) ||
                token.Generation == 0 ||
                !s_latestProxyMap.TryGetValue(token.Original, out var current) ||
                current.Generation != token.Generation ||
                !object.ReferenceEquals(current.Proxy, token.Proxy))
            {
                return false;
            }

            bool cageChanged = current.CageGeneration != token.PreviousCageGeneration ||
                               !object.ReferenceEquals(
                                   current.CageProxy,
                                   token.PreviousCageProxy);

            if (token.HasPrevious &&
                (token.PreviousProxy != null || token.PreviousCageProxy != null))
            {
                Renderer restoredProxy = token.PreviousProxy != null
                    ? token.PreviousProxy
                    : token.PreviousCageProxy;
                long restoredGeneration = token.PreviousProxy != null
                    ? token.PreviousGeneration
                    : token.PreviousCageGeneration;
                s_latestProxyMap[token.Original] = new ProxyRegistration(
                    restoredProxy,
                    restoredGeneration,
                    token.PreviousRestorationMesh,
                    token.PreviousCageProxy,
                    token.PreviousCageGeneration);
            }
            else
            {
                s_latestProxyMap.Remove(token.Original);
            }

            if (cageChanged)
                unchecked { s_proxyMappingRevision++; }
            return true;
        }

        internal static void ClearProxy(Renderer original)
        {
            // A destroyed UnityEngine.Object compares equal to null, but its managed
            // reference is still the dictionary key that must be removed.
            if (object.ReferenceEquals(original, null))
            {
                return;
            }

            if (s_latestProxyMap.Remove(original))
                unchecked { s_proxyMappingRevision++; }
            s_previewMeshMap.Remove(original);
        }

        /// <summary>
        /// Removes a proxy registration only when it is still owned by the caller that
        /// created <paramref name="generation"/>. Preview nodes can overlap briefly while
        /// NDMF replaces them; an older node must not clear the newer node's registration.
        /// </summary>
        internal static bool ClearProxy(Renderer original, Renderer proxy, long generation)
        {
            if (object.ReferenceEquals(original, null) || generation == 0 ||
                !s_latestProxyMap.TryGetValue(original, out var registration) ||
                registration.Generation != generation ||
                !object.ReferenceEquals(registration.Proxy, proxy))
            {
                return false;
            }

            bool removed = s_latestProxyMap.Remove(original);
            if (removed) unchecked { s_proxyMappingRevision++; }
            return removed;
        }

        internal static bool IsCurrentProxyRegistration(
            Renderer original,
            Renderer proxy,
            long generation)
        {
            return !object.ReferenceEquals(original, null) &&
                   generation != 0 &&
                   s_latestProxyMap.TryGetValue(original, out var registration) &&
                   registration.Generation == generation &&
                   object.ReferenceEquals(registration.Proxy, proxy);
        }

        internal static bool IsProxyRegistered(Renderer original, Renderer proxy)
        {
            return !object.ReferenceEquals(original, null) &&
                   s_latestProxyMap.TryGetValue(original, out var registration) &&
                   object.ReferenceEquals(registration.Proxy, proxy);
        }

        internal static void LogAlign(string tag, string msg)
        {
            if (!DebugAlignLogs) return;
            Debug.Log($"[LatticeAlign] {tag}: {msg}");
        }

        private static bool TryGetRegisteredProxy(Renderer original, out Renderer proxy)
        {
            proxy = null;
            if (object.ReferenceEquals(original, null) ||
                !s_latestProxyMap.TryGetValue(original, out var registration))
            {
                return false;
            }

            proxy = registration.CageProxy;
            if (proxy != null)
            {
                return true;
            }

            // A live downstream candidate may intentionally have no committed cage
            // proxy yet. Keep its ownership entry so it can be committed by OnFrame.
            if (registration.Proxy != null)
            {
                proxy = null;
                return false;
            }

            // Do not retain entries whose proxy was destroyed without a normal node
            // disposal callback.
            if (s_latestProxyMap.Remove(original))
                unchecked { s_proxyMappingRevision++; }
            proxy = null;
            return false;
        }

        internal static bool HasRegisteredProxy(Renderer original)
        {
            return !object.ReferenceEquals(original, null) && s_latestProxyMap.ContainsKey(original);
        }

        internal static int ProxyMappingRevision => s_proxyMappingRevision;

        internal static bool TryGetPreviewProxy(Renderer original, out Renderer proxy)
        {
            if (TryGetRegisteredProxy(original, out proxy) && proxy != null)
            {
                return true;
            }

            // A registered downstream candidate deliberately suppresses the NDMF
            // fallback until it is committed; otherwise a fresh lookup could expose
            // the candidate before the cached revision changes.
            if (HasRegisteredProxy(original))
            {
                proxy = null;
                return false;
            }

            return NDMFPreviewProxyUtility.TryGetProxyRenderer(original, out proxy);
        }

        internal static Bounds GetMeshLocalBounds(Renderer renderer)
        {
            UnityEngine.Profiling.Profiler.BeginSample("LatticeTool.CaptureMesh");
            try
            {
                if (renderer == null)
                {
                    return new Bounds(Vector3.zero, Vector3.zero);
                }

                switch (renderer)
                {
                    case SkinnedMeshRenderer skinned:
                        return CalculateSkinnedMeshLocalBounds(skinned);
                    case MeshRenderer meshRenderer:
                        var mf = meshRenderer.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null)
                        {
                            return CalculateReferencedMeshBounds(mf.sharedMesh, mf.sharedMesh.vertices, mf.sharedMesh.bounds);
                        }
                        break;
                }

                return renderer.bounds;
            }
            finally
            {
                UnityEngine.Profiling.Profiler.EndSample();
            }
        }

        private static Bounds CalculateSkinnedMeshLocalBounds(SkinnedMeshRenderer skinned)
        {
            var mesh = skinned != null ? skinned.sharedMesh : null;
            if (mesh == null)
            {
                return skinned != null ? skinned.localBounds : new Bounds(Vector3.zero, Vector3.zero);
            }

            var vertices = mesh.vertices;
            if (vertices == null || vertices.Length == 0)
            {
                return mesh.bounds;
            }

            if (mesh.blendShapeCount > 0)
            {
                UnityEngine.Profiling.Profiler.BeginSample("LatticeTool.CaptureBlendShapes");
                try
                {
                    int shapeCount = mesh.blendShapeCount;
                    for (int shape = 0; shape < shapeCount; shape++)
                    {
                        float weight = skinned.GetBlendShapeWeight(shape);
                        if (Mathf.Abs(weight) <= 1e-5f)
                        {
                            continue;
                        }

                        var delta = EvaluateBlendShapeVertexDelta(mesh, shape, weight);
                        if (delta == null || delta.Length != vertices.Length)
                        {
                            continue;
                        }

                        for (int i = 0; i < vertices.Length; i++)
                        {
                            vertices[i] += delta[i];
                        }
                    }
                }
                finally
                {
                    UnityEngine.Profiling.Profiler.EndSample();
                }
            }

            return CalculateReferencedMeshBounds(mesh, vertices, mesh.bounds);
        }

        private static Vector3[] EvaluateBlendShapeVertexDelta(Mesh mesh, int shapeIndex, float weight)
        {
            int frameCount = mesh.GetBlendShapeFrameCount(shapeIndex);
            if (frameCount <= 0)
            {
                return null;
            }

            int vertexCount = mesh.vertexCount;
            var lower = new Vector3[vertexCount];
            var upper = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var tangents = new Vector3[vertexCount];

            float firstWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, 0);
            if (weight <= firstWeight || frameCount == 1)
            {
                mesh.GetBlendShapeFrameVertices(shapeIndex, 0, lower, normals, tangents);
                float scale = Mathf.Abs(firstWeight) > Mathf.Epsilon ? weight / firstWeight : 0f;
                for (int i = 0; i < lower.Length; i++)
                {
                    lower[i] *= scale;
                }

                return lower;
            }

            for (int frame = 1; frame < frameCount; frame++)
            {
                float upperWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, frame);
                if (weight <= upperWeight)
                {
                    float lowerWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, frame - 1);
                    mesh.GetBlendShapeFrameVertices(shapeIndex, frame - 1, lower, normals, tangents);
                    mesh.GetBlendShapeFrameVertices(shapeIndex, frame, upper, normals, tangents);

                    float t = Mathf.Abs(upperWeight - lowerWeight) > Mathf.Epsilon
                        ? Mathf.InverseLerp(lowerWeight, upperWeight, weight)
                        : 0f;
                    for (int i = 0; i < lower.Length; i++)
                    {
                        lower[i] = Vector3.LerpUnclamped(lower[i], upper[i], t);
                    }

                    return lower;
                }
            }

            mesh.GetBlendShapeFrameVertices(shapeIndex, frameCount - 1, lower, normals, tangents);
            return lower;
        }

        private static Bounds CalculateReferencedMeshBounds(Mesh mesh, Vector3[] vertices, Bounds fallback)
        {
            if (mesh == null || vertices == null || vertices.Length == 0)
            {
                return fallback;
            }

            var bounds = new Bounds();
            bool hasPoint = false;

            int subMeshCount = Mathf.Max(1, mesh.subMeshCount);
            for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
            {
                var indices = mesh.GetIndices(subMesh);
                if (indices == null)
                {
                    continue;
                }

                for (int i = 0; i < indices.Length; i++)
                {
                    int vertexIndex = indices[i];
                    if (vertexIndex < 0 || vertexIndex >= vertices.Length)
                    {
                        continue;
                    }

                    if (!hasPoint)
                    {
                        bounds = new Bounds(vertices[vertexIndex], Vector3.zero);
                        hasPoint = true;
                    }
                    else
                    {
                        bounds.Encapsulate(vertices[vertexIndex]);
                    }
                }
            }

            if (hasPoint)
            {
                return bounds;
            }

            bounds = new Bounds(vertices[0], Vector3.zero);
            for (int i = 1; i < vertices.Length; i++)
            {
                bounds.Encapsulate(vertices[i]);
            }

            return bounds;
        }

        internal static Bounds GetRendererLocalBounds(Renderer renderer)
        {
            if (renderer == null)
            {
                return new Bounds(Vector3.zero, Vector3.zero);
            }

            var world = renderer.bounds;
            var t = renderer.transform;
            var center = t.InverseTransformPoint(world.center);
            var ext = world.extents;
            var right = t.InverseTransformVector(new Vector3(ext.x, 0f, 0f));
            var up = t.InverseTransformVector(new Vector3(0f, ext.y, 0f));
            var fwd = t.InverseTransformVector(new Vector3(0f, 0f, ext.z));
            var localExt = new Vector3(
                Mathf.Abs(right.x) + Mathf.Abs(up.x) + Mathf.Abs(fwd.x),
                Mathf.Abs(right.y) + Mathf.Abs(up.y) + Mathf.Abs(fwd.y),
                Mathf.Abs(right.z) + Mathf.Abs(up.z) + Mathf.Abs(fwd.z));

            return new Bounds(center, localExt * 2f);
        }
    }
}
#endif
