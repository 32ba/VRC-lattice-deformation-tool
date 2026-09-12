#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Owns one tool's baked mesh and matching local vertices/transform. Views are
    /// valid until this owner captures again or resets; another owner cannot alter them.
    /// Source and preview meshes are borrowed only for BakeMesh and are never destroyed.
    /// Reset releases native resources; the same owner can capture again on activation.
    /// </summary>
    internal sealed class SkinnedPoseSnapshot : IDisposable
    {
        private readonly string _name;
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private Mesh _ownedMesh;
        private bool _valid;
        private bool _subscribed;

        internal static int BakeCountForTests { get; set; }
        internal Mesh Mesh => _valid ? _ownedMesh : null;
        internal IReadOnlyList<Vector3> LocalVertices => _vertices;
        internal Matrix4x4 LocalToWorld { get; private set; } = Matrix4x4.identity;

        internal SkinnedPoseSnapshot(string name)
        {
            _name = name;
        }

        internal bool TryCapture(SkinnedMeshRenderer renderer, int expectedVertexCount)
        {
            Invalidate();
            if (renderer == null || renderer.sharedMesh == null || expectedVertexCount <= 0 ||
                renderer.sharedMesh.vertexCount != expectedVertexCount)
                return false;

            if (_ownedMesh == null)
            {
                _ownedMesh = new Mesh { name = _name, hideFlags = HideFlags.HideAndDontSave };
                if (!_subscribed)
                {
                    AssemblyReloadEvents.beforeAssemblyReload += Reset;
                    _subscribed = true;
                }
            }

            // Publish only after mesh, vertices, and transform belong to the same capture.
            // A failed capture exposes no previous pose; retain only reusable storage.
            try
            {
                BakeCountForTests++;
                renderer.BakeMesh(_ownedMesh);
                if (_ownedMesh.vertexCount != expectedVertexCount)
                    return false;
                _ownedMesh.GetVertices(_vertices);
                if (_vertices.Count != expectedVertexCount)
                {
                    Invalidate();
                    return false;
                }
                LocalToWorld = renderer.transform.localToWorldMatrix;
                _valid = true;
                return true;
            }
            catch
            {
                Invalidate();
                throw;
            }
        }

        internal Vector3[] CopyWorldPositions(Vector3[] reusable = null)
        {
            if (!_valid) return null;
            var positions = reusable != null && reusable.Length == _vertices.Count
                ? reusable
                : new Vector3[_vertices.Count];
            for (int i = 0; i < positions.Length; i++)
                positions[i] = LocalToWorld.MultiplyPoint3x4(_vertices[i]);
            return positions;
        }

        private void Invalidate()
        {
            _valid = false;
            _vertices.Clear();
            LocalToWorld = Matrix4x4.identity;
        }

        internal void Reset()
        {
            if (_subscribed)
            {
                AssemblyReloadEvents.beforeAssemblyReload -= Reset;
                _subscribed = false;
            }
            Invalidate();
            if (_ownedMesh != null)
                UnityEngine.Object.DestroyImmediate(_ownedMesh);
            _ownedMesh = null;
        }

        public void Dispose() => Reset();
    }
}
#endif
