#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>Owns the reusable GL resources; each batch restores its matrix even on failure.</summary>
    internal sealed class SceneVertexDots : IDisposable
    {
        private Material _material;
        private Texture2D _texture;
        private bool _subscribed;

        internal static readonly SceneVertexDots Shared = new SceneVertexDots();

        internal Material Prepare(CompareFunction depthTest)
        {
            if (!_subscribed)
            {
                AssemblyReloadEvents.beforeAssemblyReload += Dispose;
                _subscribed = true;
            }
            if (_texture == null)
            {
                const int size = 32;
                _texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "Deformer Vertex Dot Texture",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear
                };
                float center = (size - 1) * 0.5f;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dx = x - center, dy = y - center;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy) / center;
                        float alpha = Mathf.Clamp01(1f - Mathf.Clamp01((dist - 0.7f) / 0.3f));
                        _texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    }
                _texture.Apply();
            }
            if (_material == null)
            {
                _material = new Material(Shader.Find("Hidden/Internal-Colored"))
                {
                    name = "Deformer Vertex Dot Material",
                    hideFlags = HideFlags.HideAndDontSave
                };
                _material.SetInt("_ZWrite", 0);
                _material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                _material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                _material.SetInt("_Cull", (int)CullMode.Off);
                // Preserve the published shader and texture setup, including its texture handling.
                _material.mainTexture = _texture;
            }
            _material.SetInt("_ZTest", (int)depthTest);
            return _material;
        }

        internal Texture2D Texture => _texture;

        internal Batch Begin(CompareFunction depthTest)
        {
            return new Batch(Prepare(depthTest));
        }

        internal readonly struct Batch : IDisposable
        {
            private readonly bool _active;

            internal Batch(Material material)
            {
                _active = false;
                material.SetPass(0);
                GL.PushMatrix();
                try
                {
                    GL.MultMatrix(Matrix4x4.identity);
                    GL.Begin(GL.QUADS);
                    _active = true;
                }
                catch
                {
                    GL.PopMatrix();
                    throw;
                }
            }

            internal void Draw(Vector3 worldPosition, Color color, float radius,
                Vector3 cameraRight, Vector3 cameraUp)
            {
                var right = cameraRight * radius;
                var up = cameraUp * radius;
                GL.Color(color);
                GL.TexCoord2(0f, 0f); GL.Vertex(worldPosition - right - up);
                GL.TexCoord2(1f, 0f); GL.Vertex(worldPosition + right - up);
                GL.TexCoord2(1f, 1f); GL.Vertex(worldPosition + right + up);
                GL.TexCoord2(0f, 1f); GL.Vertex(worldPosition - right + up);
            }

            public void Dispose()
            {
                if (!_active) return;
                try { GL.End(); }
                finally { GL.PopMatrix(); }
            }
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
                _subscribed = false;
            }
            if (_material != null) UnityEngine.Object.DestroyImmediate(_material);
            if (_texture != null) UnityEngine.Object.DestroyImmediate(_texture);
            _material = null;
            _texture = null;
        }
    }

    /// <summary>Borrowed display inputs, valid for this draw only; no capture or evaluation occurs here.</summary>
    internal readonly struct VertexDisplayGeometry
    {
        internal readonly Vector3[] LocalVertices;
        internal readonly Vector3[] WorldPositions;
        internal readonly Vector3[] Displacements;
        internal readonly Matrix4x4 LocalToWorld;

        internal VertexDisplayGeometry(Vector3[] localVertices, Vector3[] worldPositions,
            Vector3[] displacements, Matrix4x4 localToWorld)
        {
            LocalVertices = localVertices;
            WorldPositions = worldPositions;
            Displacements = displacements;
            LocalToWorld = localToWorld;
        }

        internal int Count => LocalVertices?.Length ?? 0;

        internal Vector3 WorldPosition(int index)
        {
            var local = LocalVertices[index];
            if (Displacements != null) local += Displacements[index];
            return SkinnedVertexHelper.LocalToWorld(index, WorldPositions, local, LocalToWorld);
        }
    }
}
#endif
