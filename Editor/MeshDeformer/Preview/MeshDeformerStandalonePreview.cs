#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Scene View fallback used when the global NDMF preview pipeline is disabled.
    /// It keeps the authored renderer mesh untouched and displays the generated mesh
    /// through an editor-only renderer instead.
    /// </summary>
    [InitializeOnLoad]
    internal static class MeshDeformerStandalonePreview
    {
        private sealed class Entry
        {
            internal LatticeDeformer Deformer;
            internal Renderer Source;
            internal Renderer Proxy;
            internal GameObject ProxyObject;
            internal long RegistrationGeneration;
            internal bool SourceForceRenderingOff;
        }

        private static readonly Dictionary<LatticeDeformer, Entry> s_entries = new();

        static MeshDeformerStandalonePreview()
        {
            EditorApplication.update += CleanupInactiveEntries;
            Undo.undoRedoPerformed += OnUndoRedo;
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseAll;
            EditorApplication.playModeStateChanged += _ => ReleaseAll();
        }

        internal static bool Update(LatticeDeformer deformer)
        {
            if (deformer == null || deformer.RuntimeMesh == null)
            {
                return false;
            }

            Renderer source = deformer.TargetRenderer;
            if (source == null)
            {
                return false;
            }

            if (LatticePreviewUtility.TryGetPreviewProxy(source, out Renderer registered) &&
                registered != null &&
                (!s_entries.TryGetValue(deformer, out Entry existing) || registered != existing.Proxy))
            {
                Release(deformer);
                return false;
            }

            if (!s_entries.TryGetValue(deformer, out Entry entry) || entry.Proxy == null)
            {
                Release(deformer);
                entry = Create(deformer, source);
                if (entry == null)
                {
                    return false;
                }

                s_entries[deformer] = entry;
            }

            SyncTransform(entry.Source.transform, entry.ProxyObject.transform);
            AssignMesh(entry.Proxy, deformer.RuntimeMesh);
            return true;
        }

        internal static void OnExternalProxyRegistered(Renderer original, Renderer proxy)
        {
            if (original == null || proxy == null)
            {
                return;
            }

            var release = new List<LatticeDeformer>();
            foreach (var pair in s_entries)
            {
                Entry entry = pair.Value;
                if (entry.Source == original && entry.Proxy != proxy)
                {
                    // NDMF may have cloned the transient forceRenderingOff state.
                    // The real preview proxy must inherit the authored renderer state.
                    proxy.forceRenderingOff = entry.SourceForceRenderingOff;
                    release.Add(pair.Key);
                }
            }

            foreach (LatticeDeformer deformer in release)
            {
                Release(deformer);
            }
        }

        internal static bool TryGetProxy(LatticeDeformer deformer, out Renderer proxy)
        {
            proxy = null;
            if (deformer == null || !s_entries.TryGetValue(deformer, out Entry entry) || entry.Proxy == null)
            {
                return false;
            }

            proxy = entry.Proxy;
            return true;
        }

        internal static void Release(LatticeDeformer deformer)
        {
            if (ReferenceEquals(deformer, null) || !s_entries.TryGetValue(deformer, out Entry entry))
            {
                return;
            }

            s_entries.Remove(deformer);
            LatticePreviewUtility.ClearProxy(entry.Source, entry.Proxy, entry.RegistrationGeneration);
            if (entry.Source != null)
            {
                entry.Source.forceRenderingOff = entry.SourceForceRenderingOff;
            }

            if (entry.ProxyObject != null)
            {
                Object.DestroyImmediate(entry.ProxyObject);
            }
        }

        internal static void ReleaseAll()
        {
            var deformers = new List<LatticeDeformer>(s_entries.Keys);
            foreach (LatticeDeformer deformer in deformers)
            {
                Release(deformer);
            }
        }

        private static Entry Create(LatticeDeformer deformer, Renderer source)
        {
            var proxyObject = new GameObject(source.gameObject.name + " (Mesh Deformer Preview)")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            SyncTransform(source.transform, proxyObject.transform);

            Renderer proxy;
            if (source is SkinnedMeshRenderer sourceSkinned)
            {
                var proxySkinned = proxyObject.AddComponent<SkinnedMeshRenderer>();
                proxySkinned.rootBone = sourceSkinned.rootBone;
                proxySkinned.bones = sourceSkinned.bones;
                proxySkinned.localBounds = sourceSkinned.localBounds;
                proxySkinned.quality = sourceSkinned.quality;
                proxySkinned.updateWhenOffscreen = sourceSkinned.updateWhenOffscreen;
                proxySkinned.skinnedMotionVectors = sourceSkinned.skinnedMotionVectors;
                proxy = proxySkinned;
            }
            else if (source is MeshRenderer)
            {
                proxyObject.AddComponent<MeshFilter>().sharedMesh = deformer.RuntimeMesh;
                proxy = proxyObject.AddComponent<MeshRenderer>();
            }
            else
            {
                Object.DestroyImmediate(proxyObject);
                return null;
            }

            CopyRendererSettings(source, proxy);
            AssignMesh(proxy, deformer.RuntimeMesh);

            bool forceRenderingOff = source.forceRenderingOff;
            source.forceRenderingOff = true;

            long generation = LatticePreviewUtility.RegisterProxy(source, proxy);
            return new Entry
            {
                Deformer = deformer,
                Source = source,
                Proxy = proxy,
                ProxyObject = proxyObject,
                RegistrationGeneration = generation,
                SourceForceRenderingOff = forceRenderingOff
            };
        }

        private static void CopyRendererSettings(Renderer source, Renderer proxy)
        {
            proxy.enabled = source.enabled;
            proxy.sharedMaterials = source.sharedMaterials;
            proxy.shadowCastingMode = source.shadowCastingMode;
            proxy.receiveShadows = source.receiveShadows;
            proxy.lightProbeUsage = source.lightProbeUsage;
            proxy.reflectionProbeUsage = source.reflectionProbeUsage;
            proxy.probeAnchor = source.probeAnchor;
            proxy.motionVectorGenerationMode = source.motionVectorGenerationMode;
            proxy.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
            proxy.renderingLayerMask = source.renderingLayerMask;
        }

        private static void AssignMesh(Renderer renderer, Mesh mesh)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                skinned.sharedMesh = mesh;
            }
            else if (renderer is MeshRenderer)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter != null)
                {
                    filter.sharedMesh = mesh;
                }
            }
        }

        private static void SyncTransform(Transform source, Transform proxy)
        {
            proxy.SetParent(source.parent, false);
            proxy.localPosition = source.localPosition;
            proxy.localRotation = source.localRotation;
            proxy.localScale = source.localScale;
        }

        internal static void CleanupInactiveEntries()
        {
            var release = new List<LatticeDeformer>();
            foreach (var pair in s_entries)
            {
                LatticeDeformer deformer = pair.Key;
                Entry entry = pair.Value;
                if (deformer == null || !deformer.isActiveAndEnabled ||
                    entry.Source == null || entry.Proxy == null)
                {
                    release.Add(deformer);
                }
            }

            foreach (LatticeDeformer deformer in release)
            {
                Release(deformer);
            }
        }

        internal static void OnUndoRedo()
        {
            CleanupInactiveEntries();
            var deformers = new List<LatticeDeformer>(s_entries.Keys);
            foreach (LatticeDeformer deformer in deformers)
            {
                if (deformer == null || !deformer.isActiveAndEnabled)
                {
                    Release(deformer);
                    continue;
                }

                deformer.Deform(false);
                Update(deformer);
            }

            SceneView.RepaintAll();
        }
    }
}
#endif
