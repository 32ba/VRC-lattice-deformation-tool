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
        private static long s_nextProxyRegistrationGeneration;
        private static int s_proxyMappingRevision;

        private readonly struct ProxyRegistration
        {
            internal readonly Renderer Proxy;
            internal readonly long Generation;
            internal readonly Mesh RestorationMesh;

            internal ProxyRegistration(Renderer proxy, long generation, Mesh restorationMesh)
            {
                Proxy = proxy;
                Generation = generation;
                RestorationMesh = restorationMesh;
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

            proxy = registration.Proxy;
            if (proxy != null)
            {
                return true;
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
