#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.Rendering;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Draws mesh wireframe edges in the Scene view using GL lines.
    /// Shared between BrushToolHandler and VertexSelectionHandler.
    /// </summary>
    internal static class WireframeRenderer
    {
        private static Material s_material;
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
        public static void Draw(int[] triangles, Vector3[] worldPositions, Vector3[] localVertices, Matrix4x4 localToWorld)
        {
            if (triangles == null || triangles.Length < 3) return;
            if (worldPositions == null && localVertices == null) return;

            var mat = EnsureMaterial();
            mat.SetPass(0);

            GL.PushMatrix();
            GL.MultMatrix(Matrix4x4.identity);
            GL.Begin(GL.LINES);
            GL.Color(k_wireColor);

            int triCount = triangles.Length / 3;
            for (int t = 0; t < triCount; t++)
            {
                int i0 = triangles[t * 3];
                int i1 = triangles[t * 3 + 1];
                int i2 = triangles[t * 3 + 2];

                Vector3 p0 = GetWorldPos(i0, worldPositions, localVertices, localToWorld);
                Vector3 p1 = GetWorldPos(i1, worldPositions, localVertices, localToWorld);
                Vector3 p2 = GetWorldPos(i2, worldPositions, localVertices, localToWorld);

                // Edge 0-1
                GL.Vertex(p0); GL.Vertex(p1);
                // Edge 1-2
                GL.Vertex(p1); GL.Vertex(p2);
                // Edge 2-0
                GL.Vertex(p2); GL.Vertex(p0);
            }

            GL.End();
            GL.PopMatrix();
        }

        private static Vector3 GetWorldPos(int index, Vector3[] worldPositions, Vector3[] localVertices, Matrix4x4 localToWorld)
        {
            if (worldPositions != null && index >= 0 && index < worldPositions.Length)
                return worldPositions[index];
            if (localVertices != null && index >= 0 && index < localVertices.Length)
                return localToWorld.MultiplyPoint3x4(localVertices[index]);
            return Vector3.zero;
        }
    }
}
#endif
