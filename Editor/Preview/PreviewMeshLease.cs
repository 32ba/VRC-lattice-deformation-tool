#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>Owns one generated mesh and borrows the meshes displaced on its proxies.</summary>
    internal sealed class PreviewMeshLease : IDisposable
    {
        private readonly struct Binding
        {
            internal readonly Renderer Renderer;
            internal readonly Mesh Upstream;

            internal Binding(Renderer renderer, Mesh upstream)
            {
                Renderer = renderer;
                Upstream = upstream;
            }
        }

        private readonly List<Binding> _bindings = new();
        private readonly Mesh _fallbackUpstream;
        private bool _disposed;
        internal Mesh Mesh { get; }

        internal PreviewMeshLease(Mesh ownedMesh, Mesh upstream)
        {
            if (ownedMesh == null) throw new ArgumentNullException(nameof(ownedMesh));
            if (ReferenceEquals(ownedMesh, upstream) || EditorUtility.IsPersistent(ownedMesh))
                throw new ArgumentException("A preview lease requires an independent generated mesh.", nameof(ownedMesh));
            Mesh = ownedMesh;
            _fallbackUpstream = upstream;
            Mesh.MarkDynamic();
        }

        internal void AssignTo(Renderer renderer)
        {
            if (_disposed || Mesh == null || renderer == null) return;
            for (int i = 0; i < _bindings.Count; i++)
            {
                if (ReferenceEquals(_bindings[i].Renderer, renderer))
                {
                    PreviewRendererMesh.Assign(renderer, Mesh);
                    return;
                }
            }

            Mesh observed = PreviewRendererMesh.Get(renderer);
            _bindings.Add(new Binding(renderer, ReferenceEquals(observed, Mesh) ? _fallbackUpstream : observed));
            PreviewRendererMesh.Assign(renderer, Mesh);
        }

        internal void Publish()
        {
            if (_disposed || Mesh == null) return;
            for (int i = 0; i < _bindings.Count; i++)
                PreviewRendererMesh.Assign(_bindings[i].Renderer, Mesh);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                for (int i = 0; i < _bindings.Count; i++)
                {
                    var binding = _bindings[i];
                    // NDMF generations and downstream stages can overlap. Only an
                    // assignment still pointing to this unique output belongs to us.
                    if (binding.Renderer != null && ReferenceEquals(PreviewRendererMesh.Get(binding.Renderer), Mesh))
                        PreviewRendererMesh.Assign(binding.Renderer, binding.Upstream != null ? binding.Upstream : null);
                }
            }
            finally
            {
                _bindings.Clear();
                if (Mesh != null) UnityEngine.Object.DestroyImmediate(Mesh);
            }
        }
    }

    internal static class PreviewRendererMesh
    {
        internal static Mesh Get(Renderer renderer)
        {
            if (renderer == null) return null;
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
            return renderer is MeshRenderer ? renderer.GetComponent<MeshFilter>()?.sharedMesh : null;
        }

        internal static void Assign(Renderer renderer, Mesh mesh)
        {
            if (renderer == null) return;
            if (renderer is SkinnedMeshRenderer skinned) skinned.sharedMesh = mesh;
            else if (renderer is MeshRenderer)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter != null) filter.sharedMesh = mesh;
            }
        }
    }
}
#endif
