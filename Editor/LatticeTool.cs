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
        private enum MirrorAxis
        {
            X = 0,
            Y = 1,
            Z = 2
        }

        private enum MirrorBehavior
        {
            Identical = 0,
            Mirrored = 1,
            Antisymmetric = 2
        }

        private static readonly string[] s_axisLabels = { "X", "Y", "Z" };
        private static readonly string[] s_behaviorLabels = { "Copy", "Mirror", "Antisymmetric" };

        private static GUIContent s_icon;
        private static bool s_showIndices = false;
        private static readonly HashSet<int> s_selectedControls = new HashSet<int>();
        private static bool s_mirrorEditing = false;
        private static MirrorAxis s_mirrorAxis = MirrorAxis.X;
        private static MirrorBehavior s_mirrorBehavior = MirrorBehavior.Mirrored;
        private static PivotRotation? s_previousPivotRotation;

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
            s_previousPivotRotation = Tools.pivotRotation;
            Tools.pivotRotation = PivotRotation.Local;
            Undo.undoRedoPerformed += OnUndoRedo;
            SceneView.RepaintAll();
        }

        public override void OnWillBeDeactivated()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            ClearSelection();
            if (s_previousPivotRotation.HasValue)
            {
                Tools.pivotRotation = s_previousPivotRotation.Value;
                s_previousPivotRotation = null;
            }
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (Event.current != null && Event.current.commandName == "UndoRedoPerformed")
            {
                return;
            }

            if (Tools.pivotRotation != PivotRotation.Local)
            {
                Tools.pivotRotation = PivotRotation.Local;
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

            if (s_mirrorEditing)
            {
                DrawMirrorPlane(settings, meshTransform);
            }

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
            var mirrorPartnerColor = new Color(1f, 0.5f, 0.2f, 0.9f);

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
                bool isMirrorPartner = false;
                if (!isSelected && s_mirrorEditing && TryGetSymmetryIndex(index, gridSize, s_mirrorBehavior, s_mirrorAxis, out var symmetryOfIndex))
                {
                    isMirrorPartner = s_selectedControls.Contains(symmetryOfIndex);
                }

                Handles.color = isSelected ? Color.yellow : isMirrorPartner ? mirrorPartnerColor : handleColor;

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

                            var deltaLocal = meshTransform != null
                                ? meshTransform.InverseTransformVector(delta)
                                : delta;
                            var bounds = settings.LocalBounds;
                            var processedIndices = new HashSet<int>();

                            foreach (var selectedIndex in s_selectedControls)
                            {
                                if (!processedIndices.Add(selectedIndex))
                                {
                                    continue;
                                }

                                var newWorldPosition = worldPositions[selectedIndex] + delta;
                                var stored = meshTransform != null
                                    ? meshTransform.InverseTransformPoint(newWorldPosition)
                                    : newWorldPosition;
                                settings.SetControlPointLocal(selectedIndex, stored);

                                if (s_mirrorEditing && TryGetSymmetryIndex(selectedIndex, gridSize, s_mirrorBehavior, s_mirrorAxis, out var mirrorIndex))
                                {
                                    if (!processedIndices.Add(mirrorIndex))
                                    {
                                        continue;
                                    }

                                    Vector3 mirrorLocal;

                                    switch (s_mirrorBehavior)
                                    {
                                        case MirrorBehavior.Identical:
                                        {
                                            var original = settings.GetControlPointLocal(mirrorIndex);
                                            mirrorLocal = original + deltaLocal;
                                            break;
                                        }
                                        case MirrorBehavior.Mirrored:
                                            mirrorLocal = MirrorPointAxis(stored, bounds, s_mirrorAxis);
                                            break;
                                        case MirrorBehavior.Antisymmetric:
                                        {
                                            var original = settings.GetControlPointLocal(mirrorIndex);
                                            mirrorLocal = original - deltaLocal;
                                            break;
                                        }
                                        default:
                                            mirrorLocal = stored;
                                            break;
                                    }

                                    settings.SetControlPointLocal(mirrorIndex, mirrorLocal);
                                }
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
            GUILayout.BeginArea(new Rect(12f, 12f, 300f, 210f), GUIContent.none, GUI.skin.window);
            GUILayout.Label("Lattice Tool", EditorStyles.boldLabel);
            s_showIndices = GUILayout.Toggle(s_showIndices, "Show Control Indices");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear Selection", GUILayout.Width(110f)))
            {
                ClearSelection();
            }
            GUILayout.Label(GetSelectionLabel());
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            bool mirrorEnabled = GUILayout.Toggle(s_mirrorEditing, "Mirror Editing");
            if (mirrorEnabled != s_mirrorEditing)
            {
                s_mirrorEditing = mirrorEnabled;
                SceneView.RepaintAll();
            }

            using (new EditorGUI.DisabledScope(!s_mirrorEditing))
            {
                int modeSelection = EditorGUILayout.Popup("Mirror Mode", (int)s_mirrorBehavior, s_behaviorLabels);
                modeSelection = Mathf.Clamp(modeSelection, 0, s_behaviorLabels.Length - 1);
                var newMode = (MirrorBehavior)modeSelection;
                if (newMode != s_mirrorBehavior)
                {
                    s_mirrorBehavior = newMode;
                    SceneView.RepaintAll();
                }

                GUILayout.Label("Mirror Axis", EditorStyles.miniLabel);
                int axisSelection = GUILayout.Toolbar((int)s_mirrorAxis, s_axisLabels);
                axisSelection = Mathf.Clamp(axisSelection, 0, s_axisLabels.Length - 1);
                var newAxis = (MirrorAxis)axisSelection;
                if (newAxis != s_mirrorAxis)
                {
                    s_mirrorAxis = newAxis;
                    SceneView.RepaintAll();
                }
            }

            GUILayout.Label("Hold Shift/Ctrl to toggle selection.", EditorStyles.miniLabel);

            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private static void DrawMirrorPlane(LatticeAsset settings, Transform meshTransform)
        {
            var bounds = settings.LocalBounds;
            var size = bounds.size;
            if (size == Vector3.zero)
            {
                return;
            }

            var centerLocal = bounds.center;
            Vector3 axisA;
            Vector3 axisB;

            switch (s_mirrorAxis)
            {
                case MirrorAxis.X:
                    axisA = Vector3.up * (size.y * 0.5f);
                    axisB = Vector3.forward * (size.z * 0.5f);
                    break;
                case MirrorAxis.Y:
                    axisA = Vector3.right * (size.x * 0.5f);
                    axisB = Vector3.forward * (size.z * 0.5f);
                    break;
                case MirrorAxis.Z:
                default:
                    axisA = Vector3.right * (size.x * 0.5f);
                    axisB = Vector3.up * (size.y * 0.5f);
                    break;
            }

            var localCorners = new Vector3[4];
            localCorners[0] = centerLocal + axisA + axisB;
            localCorners[1] = centerLocal + axisA - axisB;
            localCorners[2] = centerLocal - axisA - axisB;
            localCorners[3] = centerLocal - axisA + axisB;

            if (meshTransform != null)
            {
                for (int i = 0; i < localCorners.Length; i++)
                {
                    localCorners[i] = meshTransform.TransformPoint(localCorners[i]);
                }
            }

            var fillColor = new Color(0.3f, 0.6f, 1f, 0.08f);
            var outlineColor = new Color(0.3f, 0.6f, 1f, 0.6f);
            Handles.DrawSolidRectangleWithOutline(localCorners, fillColor, outlineColor);
        }

        private static bool TryGetSymmetryIndex(int index, Vector3Int gridSize, MirrorBehavior behavior, MirrorAxis axis, out int symmetryIndex)
        {
            int nx = Mathf.Max(1, gridSize.x);
            int ny = Mathf.Max(1, gridSize.y);
            int nz = Mathf.Max(1, gridSize.z);

            symmetryIndex = index;

            if (behavior != MirrorBehavior.Identical &&
                behavior != MirrorBehavior.Mirrored &&
                behavior != MirrorBehavior.Antisymmetric)
            {
                return false;
            }

            if ((axis == MirrorAxis.X && nx <= 1) ||
                (axis == MirrorAxis.Y && ny <= 1) ||
                (axis == MirrorAxis.Z && nz <= 1))
            {
                return false;
            }

            int ix = index % nx;
            int iy = (index / nx) % ny;
            int iz = index / (nx * ny);

            int mirrorX = ix;
            int mirrorY = iy;
            int mirrorZ = iz;

            switch (axis)
            {
                case MirrorAxis.X:
                    mirrorX = nx - 1 - ix;
                    break;
                case MirrorAxis.Y:
                    mirrorY = ny - 1 - iy;
                    break;
                case MirrorAxis.Z:
                    mirrorZ = nz - 1 - iz;
                    break;
            }

            symmetryIndex = mirrorX + mirrorY * nx + mirrorZ * nx * ny;
            return symmetryIndex != index;
        }

        private static Vector3 MirrorPointAxis(Vector3 localPoint, Bounds bounds, MirrorAxis axis)
        {
            var mirrored = localPoint;
            var center = bounds.center;

            switch (axis)
            {
                case MirrorAxis.X:
                    mirrored.x = center.x - (localPoint.x - center.x);
                    break;
                case MirrorAxis.Y:
                    mirrored.y = center.y - (localPoint.y - center.y);
                    break;
                case MirrorAxis.Z:
                    mirrored.z = center.z - (localPoint.z - center.z);
                    break;
            }

            return mirrored;
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
