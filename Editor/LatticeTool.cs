#if UNITY_EDITOR
using System.Collections.Generic;
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
        private static readonly HashSet<int> s_selectedControls = new HashSet<int>();

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
            ClearSelection();
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
                        worldPositions[index] = meshTransform != null ? meshTransform.TransformPoint(local) : local;
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

            s_selectedControls.RemoveWhere(idx => idx < 0 || idx >= controlCount);

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

                bool isSelected = s_selectedControls.Contains(index);
                Handles.color = isSelected ? Color.yellow : handleColor;

                bool additive = false;
                var currentEvent = Event.current;
                if (currentEvent != null)
                {
                    additive = currentEvent.shift || currentEvent.control || currentEvent.command;
                }

                if (Handles.Button(worldPosition, Quaternion.identity, handleSize, handleSize, Handles.CubeHandleCap))
                {
                    if (additive)
                    {
                        if (!s_selectedControls.Add(index))
                        {
                            s_selectedControls.Remove(index);
                        }
                    }
                    else
                    {
                        s_selectedControls.Clear();
                        s_selectedControls.Add(index);
                    }

                    SceneView.RepaintAll();
                }

                if (s_showIndices)
                {
                    Handles.Label(worldPosition, $" {index}");
                }
            }

            if (s_selectedControls.Count > 0)
            {
                Vector3 pivot = Vector3.zero;
                foreach (var selectedIndex in s_selectedControls)
                {
                    pivot += worldPositions[selectedIndex];
                }

                pivot /= s_selectedControls.Count;

                if (Tools.pivotRotation == PivotRotation.Global)
                {
                    Handles.Label(pivot, " Global-space editing disabled");
                }
                else
                {
                    EditorGUI.BeginChangeCheck();
                    var handleRotation = meshTransform != null ? meshTransform.rotation : Quaternion.identity;
                    var newPivot = Handles.PositionHandle(pivot, handleRotation);
                    if (EditorGUI.EndChangeCheck())
                    {
                        var delta = newPivot - pivot;
                        if (delta != Vector3.zero)
                        {
                            Undo.RecordObject(deformer, "Move Lattice Controls");

                            foreach (var selectedIndex in s_selectedControls)
                            {
                                var newWorldPosition = worldPositions[selectedIndex] + delta;
                                var stored = meshTransform != null
                                    ? meshTransform.InverseTransformPoint(newWorldPosition)
                                    : newWorldPosition;
                                settings.SetControlPointLocal(selectedIndex, stored);
                            }

                            deformer.InvalidateCache();
                            deformer.Deform(false);
                            EditorUtility.SetDirty(deformer);
                            LatticePreviewUtility.RequestSceneRepaint();
                        }
                    }
                }
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
                ClearSelection();
            }
            GUILayout.Label(GetSelectionLabel());
            GUILayout.EndHorizontal();

            GUILayout.Label("Hold Shift/Ctrl to toggle selection.", EditorStyles.miniLabel);

            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private static void ClearSelection()
        {
            if (s_selectedControls.Count == 0)
            {
                return;
            }

            s_selectedControls.Clear();
            SceneView.RepaintAll();
        }

        private static string GetSelectionLabel()
        {
            if (s_selectedControls.Count == 0)
            {
                return "Selected: None";
            }

            if (s_selectedControls.Count == 1)
            {
                foreach (var index in s_selectedControls)
                {
                    return $"Selected: {index}";
                }
            }

            return $"Selected: {s_selectedControls.Count} controls";
        }
    }
}
#endif
