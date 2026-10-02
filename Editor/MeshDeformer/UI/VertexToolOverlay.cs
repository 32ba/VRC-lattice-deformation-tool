#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class VertexToolOverlay
    {
        private static bool s_showVisualizationSection;

        internal static void Draw(LatticeDeformer deformer)
        {
            // Transform mode selector (icon + text)
            var modeContent = new GUIContent[]
            {
                ToolIcons.Content(ToolIcons.Move, LocKey.Move),
                ToolIcons.Content(ToolIcons.Rotate, LocKey.Rotate),
                ToolIcons.Content(ToolIcons.Scale, LocKey.Scale)
            };
            GUILayout.Label(LatticeLocalization.Content(LocKey.TransformMode), EditorStyles.miniLabel);
            int modeIndex = GUILayout.Toolbar((int)VertexSelectionHandler.CurrentTransformMode, modeContent);
            modeIndex = Mathf.Clamp(modeIndex, 0, modeContent.Length - 1);
            VertexSelectionHandler.CurrentTransformMode = (VertexSelectionHandler.TransformMode)modeIndex;

            if (LatticeDeformationFeatureFlags.RestSpaceEditing &&
                VertexSelectionHandler.CurrentTransformMode == VertexSelectionHandler.TransformMode.Move &&
                deformer != null && deformer.GetComponent<SkinnedMeshRenderer>() != null)
            {
                SkinnedVertexHelper.StoreMovesInRestSpace = EditorGUILayout.Toggle(
                    LatticeLocalization.Content(LocKey.StoreMoveInRestSpace),
                    SkinnedVertexHelper.StoreMovesInRestSpace);
            }

            // Handle orientation selector
            var orientContent = new GUIContent[]
            {
                ToolIcons.Content(ToolIcons.Local, LocKey.Local),
                ToolIcons.Content(ToolIcons.Global, LocKey.Global),
                ToolIcons.Content(ToolIcons.Normal, LocKey.Normal)
            };
            GUILayout.Label(LatticeLocalization.Content(LocKey.HandleOrientation), EditorStyles.miniLabel);
            int orientIndex = GUILayout.Toolbar((int)VertexSelectionHandler.CurrentHandleOrientation, orientContent);
            orientIndex = Mathf.Clamp(orientIndex, 0, orientContent.Length - 1);
            VertexSelectionHandler.CurrentHandleOrientation = (VertexSelectionHandler.HandleOrientation)orientIndex;

            // Pivot mode selector
            var pivotContent = new GUIContent[]
            {
                ToolIcons.Content(ToolIcons.Pivot, LocKey.Center),
                LatticeLocalization.Content(LocKey.LastSelected)
            };
            GUILayout.Label(LatticeLocalization.Content(LocKey.Pivot), EditorStyles.miniLabel);
            int pivotIndex = GUILayout.Toolbar((int)VertexSelectionHandler.CurrentPivotMode, pivotContent);
            pivotIndex = Mathf.Clamp(pivotIndex, 0, pivotContent.Length - 1);
            VertexSelectionHandler.CurrentPivotMode = (VertexSelectionHandler.PivotMode)pivotIndex;

            GUILayout.Space(4f);

            // --- Proportional Editing (radius 0 = disabled, no separate toggle) ---
            // Display in cm, store internally in meters (mesh-local)
            float propCm = VertexSelectionHandler.ProportionalRadius * 100f;
            EditorGUI.BeginChangeCheck();
            propCm = EditorGUILayout.Slider(
                new GUIContent(LatticeLocalization.Tr(LocKey.ProportionalRadius) + " (cm)", LatticeLocalization.Tooltip(LocKey.ProportionalRadius)),
                propCm, 0f, 10f);
            if (EditorGUI.EndChangeCheck())
                VertexSelectionHandler.ProportionalRadius = propCm / 100f;

            var falloffContent = new GUIContent[]
            {
                LatticeLocalization.Content(LocKey.Smooth),
                LatticeLocalization.Content(LocKey.Linear),
                LatticeLocalization.Content(LocKey.Constant),
                LatticeLocalization.Content(LocKey.Sphere),
                LatticeLocalization.Content(LocKey.Gaussian)
            };
            int falloffIndex = EditorGUILayout.Popup(
                LatticeLocalization.Content(LocKey.Falloff),
                (int)VertexSelectionHandler.ProportionalFalloffType,
                falloffContent);
            falloffIndex = Mathf.Clamp(falloffIndex, 0, falloffContent.Length - 1);
            VertexSelectionHandler.ProportionalFalloffType = (VertexSelectionHandler.FalloffType)falloffIndex;

            // --- Visualization section (foldout) ---
            s_showVisualizationSection = EditorGUILayout.Foldout(s_showVisualizationSection, LatticeLocalization.Tr(LocKey.Visualization), true);
            if (s_showVisualizationSection)
            {
                EditorGUI.indentLevel++;
                VertexSelectionHandler.ShowWireframe = GUILayout.Toggle(VertexSelectionHandler.ShowWireframe,
                    ToolIcons.Content(ToolIcons.Eye, LocKey.ShowWireframe));
                VertexSelectionHandler.VertexDotSize = EditorGUILayout.Slider(
                    LatticeLocalization.Content(LocKey.DotSize),
                    VertexSelectionHandler.VertexDotSize, 1f, 8f);
                VertexSelectionHandler.BackfaceCulling = GUILayout.Toggle(
                    VertexSelectionHandler.BackfaceCulling,
                    ToolIcons.Content(ToolIcons.BackfaceCull, LocKey.BackfaceCulling));
                EditorGUI.indentLevel--;
            }

            GUILayout.Space(2f);

            // Selection info and actions
            GUILayout.Label(VertexSelectionHandler.GetSelectionLabel(), EditorStyles.miniLabel);

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button(LatticeLocalization.Content(LocKey.SelectAll)))
                {
                    VertexSelectionHandler.SelectAll(deformer);
                }

                if (GUILayout.Button(LatticeLocalization.Content(LocKey.SelectNone)))
                {
                    VertexSelectionHandler.ClearSelection();
                }

                if (GUILayout.Button(ToolIcons.Content(ToolIcons.Invert, LocKey.Invert)))
                {
                    VertexSelectionHandler.InvertSelection(deformer);
                }
            }

            if (LatticeDeformationFeatureFlags.SymmetricVertexSelection)
            {
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label(LatticeLocalization.Content(LocKey.MirrorAxis), GUILayout.Width(75f));
                    int mirrorAxis = GUILayout.Toolbar(
                        (int)BrushToolHandler.CurrentMirrorAxis,
                        BrushToolHandler.AxisOptions);
                    mirrorAxis = Mathf.Clamp(mirrorAxis, 0, BrushToolHandler.AxisOptions.Length - 1);
                    BrushToolHandler.CurrentMirrorAxis = (BrushToolHandler.MirrorAxis)mirrorAxis;

                    using (new EditorGUI.DisabledScope(VertexSelectionHandler.SelectedVertexCount == 0))
                    {
                        if (GUILayout.Button(ToolIcons.Content(ToolIcons.Mirror, LocKey.Mirror)))
                        {
                            VertexSelectionHandler.SelectMirrorPartners(deformer, mirrorAxis);
                        }
                    }
                }
            }

            using (new GUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(VertexSelectionHandler.SelectedVertexCount == 0))
                {
                    if (GUILayout.Button(ToolIcons.Content(ToolIcons.Reset, LocKey.ResetSelectedVertices)))
                    {
                        VertexSelectionHandler.ResetSelectedVertices(deformer);
                    }
                }

                if (GUILayout.Button(ToolIcons.Content(ToolIcons.Reset, LocKey.ResetAllVertices)))
                {
                    VertexSelectionHandler.ResetAllVertices(deformer);
                }
            }

            GUILayout.Space(2f);
            GUILayout.Label(LatticeLocalization.Tr(LocKey.ShiftClickHint), EditorStyles.miniLabel);
        }
    }
}
#endif
