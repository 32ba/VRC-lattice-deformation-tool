#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class SelectedVertexVisualization
    {
        internal static void DrawSelectionRectangle(Rect rect)
        {
            Handles.BeginGUI();
            try
            {
                var fillColor = new Color(0.3f, 0.6f, 1f, 0.15f);
                var outlineColor = new Color(0.3f, 0.6f, 1f, 0.6f);

                EditorGUI.DrawRect(rect, fillColor);

                // Draw outline
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), outlineColor);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), outlineColor);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), outlineColor);
                EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), outlineColor);
            }
            finally
            {
                Handles.EndGUI();
            }
        }

        private static readonly Color k_UnselectedVertexColor = new Color(0.2f, 0.8f, 1f, 0.6f);
        private static readonly Color k_SelectedVertexColor = new Color(1f, 1f, 0f, 1f);

        internal static void Draw(VertexDisplayGeometry geometry, HashSet<int> selectedVertices,
            VertexProportionalInfluenceCache influences, bool showInfluence, float dotSizeScale,
            float baseRadius, Vector3 cameraRight, Vector3 cameraUp, CompareFunction depthTest)
        {
            int vertexCount = geometry.Count;
            using (var batch = SceneVertexDots.Shared.Begin(depthTest))
            {
                for (int i = 0; i < vertexCount; i++)
                {
                    var worldPos = geometry.WorldPosition(i);
                    bool isSelected = selectedVertices.Contains(i);

                    Color color;
                    float dotSize;
                    if (isSelected)
                    {
                        color = k_SelectedVertexColor;
                        dotSize = dotSizeScale * 1.5f;
                    }
                    else if (showInfluence)
                    {
                        float influence = influences.GetInfluence(i);
                        if (influence > 0f)
                        {
                            color = InfluenceToColor(influence);
                            dotSize = Mathf.Lerp(
                                dotSizeScale * 0.6f,
                                dotSizeScale * 1.4f,
                                influence);
                        }
                        else
                        {
                            color = k_UnselectedVertexColor;
                            dotSize = dotSizeScale;
                        }
                    }
                    else
                    {
                        color = k_UnselectedVertexColor;
                        dotSize = dotSizeScale;
                    }

                    float radius = baseRadius * dotSize;
                    batch.Draw(worldPos, color, radius, cameraRight, cameraUp);
                }
            }
        }

        internal static Color InfluenceToColor(float t)
        {
            // 0.0 = blue, 0.25 = cyan, 0.5 = green, 0.75 = yellow, 1.0 = red
            t = Mathf.Clamp01(t);
            if (t < 0.25f)
            {
                float s = t / 0.25f;
                return new Color(0f, s, 1f, 0.9f);
            }
            if (t < 0.5f)
            {
                float s = (t - 0.25f) / 0.25f;
                return new Color(0f, 1f, 1f - s, 0.9f);
            }
            if (t < 0.75f)
            {
                float s = (t - 0.5f) / 0.25f;
                return new Color(s, 1f, 0f, 0.9f);
            }
            {
                float s = (t - 0.75f) / 0.25f;
                return new Color(1f, 1f - s, 0f, 0.9f);
            }
        }
    }
}
#endif
