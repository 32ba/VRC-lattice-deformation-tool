#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityEngine.Rendering;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    [EditorTool("Lattice Tool", typeof(LatticeDeformer))]
    public sealed class LatticeDeformerTool : EditorTool
    {
        private static GUIContent s_icon;
        private static bool s_showIndices = false;
        private static int s_selectedControl = -1;

        public override GUIContent toolbarIcon
        {
            get
            {
                if (s_icon == null)
                {
                    s_icon = EditorGUIUtility.IconContent("EditCollider");
                }

                return s_icon;
            }
        }

        public override void OnActivated()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            SceneView.RepaintAll();
        }

        public override void OnWillBeDeactivated()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            SceneView.RepaintAll();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (Event.current != null && Event.current.commandName == "UndoRedoPerformed")
            {
                return;
            }

            if (target is not LatticeDeformer deformer)
            {
                return;
            }

            var settings = deformer.Settings;
            if (settings == null)
            {
                return;
            }

            int controlCount = settings.ControlPointCount;
            if (controlCount == 0)
            {
                return;
            }

            DrawOverlayUI();
            DrawControlHandles(deformer, settings, controlCount);
        }

        private void DrawControlHandles(LatticeDeformer deformer, LatticeAsset settings, int controlCount)
        {
            var meshTransform = deformer.MeshTransform;
            var applySpace = settings.ApplySpace;
            var gridSize = settings.GridSize;
            int nx = Mathf.Max(1, gridSize.x);
            int ny = Mathf.Max(1, gridSize.y);
            int nz = Mathf.Max(1, gridSize.z);

            int Index(int x, int y, int z) => x + y * nx + z * nx * ny;

            var worldPositions = new Vector3[controlCount];
            for (int z = 0; z < nz; z++)
            {
                for (int y = 0; y < ny; y++)
                {
                    for (int x = 0; x < nx; x++)
                    {
                        int index = Index(x, y, z);
                        var local = settings.GetControlPointLocal(index);
                        worldPositions[index] = applySpace == LatticeApplySpace.World
                            ? local
                            : meshTransform.TransformPoint(local);
                    }
                }
            }

            var previousZTest = Handles.zTest;
            Handles.zTest = CompareFunction.LessEqual;

            var cageColor = new Color(1f, 1f, 1f, 0.8f);
            Handles.color = cageColor;

            for (int z = 0; z < nz; z++)
            {
                for (int y = 0; y < ny; y++)
                {
                    for (int x = 0; x < nx; x++)
                    {
                        int index = Index(x, y, z);
                        var from = worldPositions[index];

                        if (x + 1 < nx)
                        {
                            Handles.DrawAAPolyLine(3f, from, worldPositions[Index(x + 1, y, z)]);
                        }

                        if (y + 1 < ny)
                        {
                            Handles.DrawAAPolyLine(3f, from, worldPositions[Index(x, y + 1, z)]);
                        }

                        if (z + 1 < nz)
                        {
                            Handles.DrawAAPolyLine(3f, from, worldPositions[Index(x, y, z + 1)]);
                        }
                    }
                }
            }

            var handleColor = new Color(0.2f, 0.8f, 1f, 0.9f);

            if (s_selectedControl >= controlCount)
            {
                s_selectedControl = -1;
            }

            for (int index = 0; index < controlCount; index++)
            {
                int ix = index % nx;
                int iy = (index / nx) % ny;
                int iz = index / (nx * ny);

                bool onBoundary = ix == 0 || ix == nx - 1 || iy == 0 || iy == ny - 1 || iz == 0 || iz == nz - 1;
                if (!onBoundary)
                {
                    continue;
                }

                var worldPosition = worldPositions[index];
                float handleSize = HandleUtility.GetHandleSize(worldPosition) * 0.08f;

                bool isSelected = index == s_selectedControl;
                Handles.color = isSelected ? Color.yellow : handleColor;

                if (!isSelected)
                {
                    if (Handles.Button(worldPosition, Quaternion.identity, handleSize, handleSize, Handles.CubeHandleCap))
                    {
                        s_selectedControl = index;
                        SceneView.RepaintAll();
                    }

                    if (s_showIndices)
                    {
                        Handles.Label(worldPosition, $" {index}");
                    }

                    continue;
                }

                EditorGUI.BeginChangeCheck();
                var fmh_147_21_638941814807696584 = Quaternion.identity;
                var newPosition = Handles.PositionHandle(worldPosition, Quaternion.identity);
                if (!EditorGUI.EndChangeCheck())
                {
                    continue;
                }

                Undo.RecordObject(deformer, "Move Lattice Control");

                var stored = applySpace == LatticeApplySpace.World
                    ? newPosition
                    : meshTransform.InverseTransformPoint(newPosition);

                settings.SetControlPointLocal(index, stored);

                deformer.InvalidateCache();
                deformer.Deform(false);
                EditorUtility.SetDirty(deformer);
                LatticePreviewUtility.RequestSceneRepaint();
            }

            Handles.zTest = previousZTest;
        }

        private void OnUndoRedo()
        {
            if (target is not LatticeDeformer deformer)
            {
                return;
            }

            deformer.Deform(false);
            LatticePreviewUtility.RequestSceneRepaint();
        }

        private static void DrawOverlayUI()
        {
            var sceneView = SceneView.currentDrawingSceneView;
            if (sceneView == null)
            {
                return;
            }

            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(12f, 12f, 220f, 90f), GUIContent.none, GUI.skin.window);
            GUILayout.Label("Lattice Tool", EditorStyles.boldLabel);
            s_showIndices = GUILayout.Toggle(s_showIndices, "Show Control Indices");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear Selection", GUILayout.Width(110f)))
            {
                s_selectedControl = -1;
                SceneView.RepaintAll();
            }
            GUILayout.Label(s_selectedControl >= 0 ? $"Selected: {s_selectedControl}" : "Selected: None");
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
            Handles.EndGUI();
        }
    }
}
#endif
