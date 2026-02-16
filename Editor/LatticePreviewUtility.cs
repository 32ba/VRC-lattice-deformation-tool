#if UNITY_EDITOR
using System.Collections.Generic;
using nadena.dev.ndmf.preview;
using UnityEditor;
using UnityEngine;
using Net._32Ba.LatticeDeformationTool;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class LatticePreviewUtility
    {
        private const string k_PreviewAlignedKey = "Net32Ba.LatticeDeformer.UsePreviewAlignedCage";
        private const string k_DebugAlignKey = "Net32Ba.LatticeDeformer.DebugAlignLogs";
        private static readonly Dictionary<Renderer, Renderer> s_latestProxyMap = new();

        /// <summary>
        /// Determines whether the runtime mesh should be assigned back to the renderer.
        /// When the NDMF preview pipeline is active we leave the original mesh untouched
        /// and rely on proxy renderers instead.
        /// </summary>
        public static bool ShouldAssignRuntimeMesh()
        {
            if (NDMFPreview.DisablePreviewDepth != 0)
            {
                return true;
            }

            if (!NDMFPreviewPrefs.instance.EnablePreview)
            {
                return true;
            }

            return !LatticeDeformerPreviewFilter.PreviewToggleEnabled;
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
        public static Transform GetEditingTransform(LatticeDeformer deformer)
        {
            if (deformer == null)
            {
                return null;
            }

            if (UsePreviewAlignedCage)
            {
                var renderer = deformer.GetComponent<Renderer>();
                if (renderer != null)
                {
                    if (TryGetRegisteredProxy(renderer, out var proxy) && proxy != null)
                    {
                        return proxy.transform;
                    }

                    if (NDMFPreviewProxyUtility.TryGetProxyRenderer(renderer, out proxy) && proxy != null)
                    {
                        return proxy.transform;
                    }
                }
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

            if (!NDMFPreviewProxyUtility.TryGetProxyRenderer(renderer, out var proxy) || proxy == null)
            {
                return sourceBounds;
            }

            var targetTransform = editingTransform != null ? editingTransform : proxy.transform;
            if (targetTransform == null)
            {
                return sourceBounds;
            }

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

        public static void RequestSceneRepaint()
        {
            SceneView.RepaintAll();
        }

        internal static void RegisterProxy(Renderer original, Renderer proxy)
        {
            if (original == null || proxy == null)
            {
                return;
            }

            s_latestProxyMap[original] = proxy;
        }

        internal static void ClearProxy(Renderer original)
        {
            if (original == null)
            {
                return;
            }

            s_latestProxyMap.Remove(original);
        }

        internal static void LogAlign(string tag, string msg)
        {
            if (!DebugAlignLogs) return;
            Debug.Log($"[LatticeAlign] {tag}: {msg}");
        }

        private static bool TryGetRegisteredProxy(Renderer original, out Renderer proxy)
        {
            return s_latestProxyMap.TryGetValue(original, out proxy);
        }

        internal static Bounds GetMeshLocalBounds(Renderer renderer)
        {
            if (renderer == null)
            {
                return new Bounds(Vector3.zero, Vector3.zero);
            }

            switch (renderer)
            {
                case SkinnedMeshRenderer skinned:
                    // localBounds is the skinned renderer's evaluated local AABB (reflects root bone & import settings)
                    return skinned.localBounds;
                case MeshRenderer meshRenderer:
                    var mf = meshRenderer.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        return mf.sharedMesh.bounds;
                    }
                    break;
            }

            return renderer.bounds;
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
