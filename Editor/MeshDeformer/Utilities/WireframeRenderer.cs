#if UNITY_EDITOR
using System;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using UnityEngine.Rendering;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Draws mesh wireframe edges in the Scene view using GL lines.
    /// Shared between BrushToolHandler and VertexSelectionHandler.
    /// </summary>
    [ExcludeFromCodeCoverage]
    internal static class WireframeRenderer
    {
        private static Material s_material;
        private static Mesh s_lineMesh;
        private static int[] s_cachedTriangleContents;
        private static int s_cachedVertexCount = -1;
        private static int s_cachedTopologyRevision = int.MinValue;
        private static Color32[] s_vertexColors;
        private static readonly Color k_wireColor = new Color(1f, 1f, 1f, 0.15f);

        private static Material EnsureMaterial()
        {
            if (s_material != null) return s_material;
            s_material = new Material(Shader.Find("Hidden/Internal-Colored"))
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            s_material.SetInt("_ZWrite", 0);
            s_material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            s_material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            s_material.SetInt("_Cull", (int)CullMode.Off);
            s_material.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            return s_material;
        }

        /// <summary>
        /// Draws mesh edges as semi-transparent white lines.
        /// </summary>
        /// <param name="triangles">Mesh triangle indices</param>
        /// <param name="worldPositions">Pre-computed world-space vertex positions (for SkinnedMeshRenderer), or null</param>
        /// <param name="localVertices">Deformed local-space vertices (with displacements applied)</param>
        /// <param name="localToWorld">Transform matrix for local-to-world conversion (used when worldPositions is null)</param>
        public static void Draw(
            int[] triangles,
            Vector3[] worldPositions,
            Vector3[] localVertices,
            Matrix4x4 localToWorld,
            int topologyRevision = int.MinValue)
        {
            if (triangles == null || triangles.Length < 3) return;
            if (worldPositions == null && localVertices == null) return;

            Vector3[] positions = worldPositions ?? localVertices;
            if (positions == null || positions.Length == 0) return;

            var mat = EnsureMaterial();
            mat.SetPass(0);

            Mesh lineMesh = EnsureLineMesh(out bool meshCreated);
            bool hasRevision = topologyRevision != int.MinValue;
            bool topologyChanged = meshCreated ||
                                   s_cachedVertexCount != positions.Length ||
                                   (hasRevision
                                       ? topologyRevision != s_cachedTopologyRevision
                                       : !TriangleContentsMatch(triangles, s_cachedTriangleContents));
            if (topologyChanged)
            {
                lineMesh.Clear(false);
                lineMesh.indexFormat = positions.Length > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16;
            }

            lineMesh.SetVertices(positions);
            if (topologyChanged)
            {
                if (s_vertexColors == null || s_vertexColors.Length != positions.Length)
                {
                    s_vertexColors = new Color32[positions.Length];
                    Color32 color = k_wireColor;
                    for (int index = 0; index < s_vertexColors.Length; index++)
                        s_vertexColors[index] = color;
                }
                lineMesh.SetColors(s_vertexColors);
                lineMesh.SetIndices(
                    BuildLineIndices(triangles, positions.Length),
                    MeshTopology.Lines,
                    0,
                    true);
                if (s_cachedTriangleContents == null ||
                    s_cachedTriangleContents.Length != triangles.Length)
                {
                    s_cachedTriangleContents = new int[triangles.Length];
                }
                Array.Copy(triangles, s_cachedTriangleContents, triangles.Length);
                s_cachedVertexCount = positions.Length;
                s_cachedTopologyRevision = topologyRevision;
            }

            Graphics.DrawMeshNow(lineMesh, worldPositions != null ? Matrix4x4.identity : localToWorld);
        }

        internal static int[] BuildLineIndices(int[] triangles, int vertexCount)
        {
            if (triangles == null || triangles.Length < 3 || vertexCount <= 0)
                return Array.Empty<int>();

            int triangleCount = triangles.Length / 3;
            var indices = new int[triangleCount * 6];
            int writeIndex = 0;
            for (int triangle = 0; triangle < triangleCount; triangle++)
            {
                int offset = triangle * 3;
                int first = triangles[offset];
                int second = triangles[offset + 1];
                int third = triangles[offset + 2];
                if ((uint)first >= (uint)vertexCount ||
                    (uint)second >= (uint)vertexCount ||
                    (uint)third >= (uint)vertexCount)
                {
                    continue;
                }

                indices[writeIndex++] = first;
                indices[writeIndex++] = second;
                indices[writeIndex++] = second;
                indices[writeIndex++] = third;
                indices[writeIndex++] = third;
                indices[writeIndex++] = first;
            }

            if (writeIndex != indices.Length)
                Array.Resize(ref indices, writeIndex);
            return indices;
        }

        internal static bool TriangleContentsMatch(int[] triangles, int[] cachedTriangles)
        {
            if (triangles == null || cachedTriangles == null ||
                triangles.Length != cachedTriangles.Length)
            {
                return false;
            }

            for (int index = 0; index < triangles.Length; index++)
            {
                if (triangles[index] != cachedTriangles[index])
                    return false;
            }
            return true;
        }

        private static Mesh EnsureLineMesh(out bool created)
        {
            created = s_lineMesh == null;
            if (!created) return s_lineMesh;
            s_lineMesh = new Mesh
            {
                name = "Lattice Deformer Wireframe",
                hideFlags = HideFlags.HideAndDontSave
            };
            s_lineMesh.MarkDynamic();
            s_cachedTriangleContents = null;
            s_cachedVertexCount = -1;
            s_cachedTopologyRevision = int.MinValue;
            return s_lineMesh;
        }

    }
}
#endif
