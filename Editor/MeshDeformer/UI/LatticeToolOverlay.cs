#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class LatticeToolOverlay
    {
        private static bool s_showSymmetrySection;

        internal static void Draw(LatticeDeformer deformer)
        {
            GUILayout.Label(LatticeLocalization.Content(LocKey.ControlPointScope), EditorStyles.miniLabel);
            int scopeSelection = GUILayout.Toolbar(
                LatticeToolHandler.IncludeInteriorControls ? 1 : 0,
                new[]
                {
                    LatticeLocalization.Content(LocKey.BoundaryOnly),
                    LatticeLocalization.Content(LocKey.AllControls)
                });
            bool includeInterior = scopeSelection == 1;
            LatticeToolHandler.IncludeInteriorControls = includeInterior;
            GUILayout.Space(2f);

            // Compact toggles (horizontal)
            using (new GUILayout.HorizontalScope())
            {
                LatticeToolHandler.ShowIndices = GUILayout.Toggle(LatticeToolHandler.ShowIndices, ToolIcons.Content(ToolIcons.Eye, LocKey.ShowControlIds));

                bool keepControlsVisible = GUILayout.Toggle(
                    !LatticeToolHandler.OccludeWithSceneGeometry,
                    LatticeLocalization.Content(LocKey.KeepControlPointsVisible));
                LatticeToolHandler.OccludeWithSceneGeometry = !keepControlsVisible;
            }

            GUILayout.Space(2f);

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button(ToolIcons.Content(ToolIcons.Clear, LocKey.ClearSelection), GUILayout.Width(110f)))
                {
                    LatticeToolHandler.ClearSelection();
                }

                GUILayout.Label(LatticeToolHandler.GetSelectionLabel());
            }

            // --- Symmetry section (foldout) ---
            s_showSymmetrySection = EditorGUILayout.Foldout(s_showSymmetrySection, LatticeLocalization.Tr(LocKey.EnableSymmetryEditing), true);
            if (s_showSymmetrySection)
            {
                EditorGUI.indentLevel++;
                LatticeToolHandler.MirrorEditing = GUILayout.Toggle(LatticeToolHandler.MirrorEditing, ToolIcons.Content(ToolIcons.Mirror, LocKey.EnableSymmetryEditing));

                using (new EditorGUI.DisabledScope(!LatticeToolHandler.MirrorEditing))
                {
                    int modeSelection = EditorGUILayout.Popup(
                        LatticeLocalization.Content(LocKey.SymmetryMode),
                        (int)LatticeToolHandler.CurrentMirrorBehavior,
                        LatticeToolHandler.BehaviorOptions);
                    modeSelection = Mathf.Clamp(modeSelection, 0, LatticeToolHandler.BehaviorOptions.Length - 1);
                    LatticeToolHandler.CurrentMirrorBehavior = (LatticeToolHandler.MirrorBehavior)modeSelection;

                    GUILayout.Label(LatticeLocalization.Content(LocKey.SymmetryAxis), EditorStyles.miniLabel);
                    int axisSelection = GUILayout.Toolbar((int)LatticeToolHandler.CurrentMirrorAxis, LatticeToolHandler.AxisOptions);
                    axisSelection = Mathf.Clamp(axisSelection, 0, LatticeToolHandler.AxisOptions.Length - 1);
                    LatticeToolHandler.CurrentMirrorAxis = (LatticeToolHandler.MirrorAxis)axisSelection;
                }
                EditorGUI.indentLevel--;
            }

            GUILayout.Label(LatticeLocalization.Content(LocKey.ShiftClickHint), EditorStyles.miniLabel);
        }
    }
}
#endif
