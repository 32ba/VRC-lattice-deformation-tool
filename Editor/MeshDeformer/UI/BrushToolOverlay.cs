#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class BrushToolOverlay
    {
        private static bool s_showMirrorSection;
        private static bool s_showAdvancedSection;
        private static bool s_showVisualizationSection;

        internal static void Draw(LatticeDeformer deformer)
        {
            // Brush Mode toolbar (icon + text)
            var modeContent = LatticeDeformationFeatureFlags.VertexMaskEditing
                ? new[]
                {
                    ToolIcons.Content(ToolIcons.Normal, LocKey.Normal),
                    ToolIcons.Content(ToolIcons.Move, LocKey.Move),
                    ToolIcons.Content(ToolIcons.Smooth, LocKey.Smooth),
                    ToolIcons.Content(ToolIcons.Mask, LocKey.Mask),
                }
                : new[]
                {
                    ToolIcons.Content(ToolIcons.Normal, LocKey.Normal),
                    ToolIcons.Content(ToolIcons.Move, LocKey.Move),
                    ToolIcons.Content(ToolIcons.Smooth, LocKey.Smooth),
                };
            int currentModeIndex = Mathf.Min((int)BrushToolHandler.CurrentBrushMode, modeContent.Length - 1);
            int modeIndex = GUILayout.Toolbar(currentModeIndex, modeContent);
            modeIndex = Mathf.Clamp(modeIndex, 0, modeContent.Length - 1);
            BrushToolHandler.CurrentBrushMode = (BrushToolHandler.BrushMode)modeIndex;

            GUILayout.Space(2f);

            // Primary parameters (always visible)
            // Display world-space cm: mesh-local radius * transform scale * 100
            // s_brushRadius is world-space meters — display as cm directly
            float radiusCm = BrushToolHandler.BrushRadius * 100f;
            EditorGUI.BeginChangeCheck();
            radiusCm = EditorGUILayout.Slider(
                new GUIContent(LatticeLocalization.Tr(LocKey.BrushRadius) + " (cm)", LatticeLocalization.Tooltip(LocKey.BrushRadius)),
                radiusCm, 0f, 10f);
            if (EditorGUI.EndChangeCheck())
                BrushToolHandler.BrushRadius = radiusCm / 100f;

            // Display strength as 0-100%, store internally as 0-1
            float strengthPercent = BrushToolHandler.BrushStrength * 100f;
            strengthPercent = EditorGUILayout.Slider(
                new GUIContent(LatticeLocalization.Tr(LocKey.BrushStrength) + " (%)", LatticeLocalization.Tooltip(LocKey.BrushStrength)),
                strengthPercent, 0f, 100f);
            BrushToolHandler.BrushStrength = strengthPercent / 100f;

            if (LatticeDeformationFeatureFlags.RestSpaceEditing &&
                BrushToolHandler.CurrentBrushMode == BrushToolHandler.BrushMode.Move &&
                deformer != null && deformer.GetComponent<SkinnedMeshRenderer>() != null)
            {
                SkinnedVertexHelper.StoreMovesInRestSpace = EditorGUILayout.Toggle(
                    LatticeLocalization.Content(LocKey.StoreMoveInRestSpace),
                    SkinnedVertexHelper.StoreMovesInRestSpace);
            }

            // Falloff type (text only — falloff curves are self-explanatory with names)
            var falloffContent = new GUIContent[]
            {
                LatticeLocalization.Content(LocKey.Smooth),
                LatticeLocalization.Content(LocKey.Linear),
                LatticeLocalization.Content(LocKey.Constant),
                LatticeLocalization.Content(LocKey.Sphere),
                LatticeLocalization.Content(LocKey.Gaussian)
            };
            int falloffIndex = EditorGUILayout.Popup(
                LatticeLocalization.Content(LocKey.BrushFalloff),
                (int)BrushToolHandler.BrushFalloff,
                falloffContent);
            falloffIndex = Mathf.Clamp(falloffIndex, 0, falloffContent.Length - 1);
            BrushToolHandler.BrushFalloff = (BrushFalloffType)falloffIndex;

            GUILayout.Space(2f);

            // Compact toggles (horizontal)
            using (new GUILayout.HorizontalScope())
            {
                BrushToolHandler.InvertBrush = GUILayout.Toggle(
                    BrushToolHandler.InvertBrush,
                    ToolIcons.Content(ToolIcons.Invert, LocKey.InvertBrush));
                BrushToolHandler.BackfaceCulling = GUILayout.Toggle(
                    BrushToolHandler.BackfaceCulling,
                    ToolIcons.Content(ToolIcons.BackfaceCull, LocKey.BackfaceCulling));
            }

            // --- Advanced section (foldout) ---
            s_showAdvancedSection = EditorGUILayout.Foldout(s_showAdvancedSection, LatticeLocalization.Tr(LocKey.Advanced), true);
            if (s_showAdvancedSection)
            {
                EditorGUI.indentLevel++;
                BrushToolHandler.ConnectedOnly = GUILayout.Toggle(
                    BrushToolHandler.ConnectedOnly,
                    ToolIcons.Content(ToolIcons.Connected, LocKey.ConnectedOnly));
                BrushToolHandler.UseSurfaceDistance = GUILayout.Toggle(
                    BrushToolHandler.UseSurfaceDistance,
                    ToolIcons.Content(ToolIcons.SurfaceDistance, LocKey.SurfaceDistance));
                EditorGUI.indentLevel--;
            }

            // --- Mirror section (foldout) ---
            s_showMirrorSection = EditorGUILayout.Foldout(s_showMirrorSection, LatticeLocalization.Tr(LocKey.EnableMirror), true);
            if (s_showMirrorSection)
            {
                EditorGUI.indentLevel++;
                BrushToolHandler.MirrorEditing = GUILayout.Toggle(
                    BrushToolHandler.MirrorEditing,
                    ToolIcons.Content(ToolIcons.Mirror, LocKey.EnableMirror));

                using (new EditorGUI.DisabledScope(!BrushToolHandler.MirrorEditing))
                {
                    GUILayout.Label(LatticeLocalization.Content(LocKey.MirrorAxis), EditorStyles.miniLabel);
                    int axisSelection = GUILayout.Toolbar(
                        (int)BrushToolHandler.CurrentMirrorAxis,
                        BrushToolHandler.AxisOptions);
                    axisSelection = Mathf.Clamp(axisSelection, 0, BrushToolHandler.AxisOptions.Length - 1);
                    BrushToolHandler.CurrentMirrorAxis = (BrushToolHandler.MirrorAxis)axisSelection;
                }
                EditorGUI.indentLevel--;
            }

            // --- Visualization section (foldout) ---
            s_showVisualizationSection = EditorGUILayout.Foldout(s_showVisualizationSection, LatticeLocalization.Tr(LocKey.Visualization), true);
            if (s_showVisualizationSection)
            {
                EditorGUI.indentLevel++;
                BrushToolHandler.ShowWireframe = GUILayout.Toggle(BrushToolHandler.ShowWireframe,
                    ToolIcons.Content(ToolIcons.Eye, LocKey.ShowWireframe));
                BrushToolHandler.ShowAffectedVertices = GUILayout.Toggle(
                    BrushToolHandler.ShowAffectedVertices,
                    ToolIcons.Content(ToolIcons.Eye, LocKey.ShowAffectedVertices));
                BrushToolHandler.ShowDisplacementHeatmap = GUILayout.Toggle(
                    BrushToolHandler.ShowDisplacementHeatmap,
                    ToolIcons.Content(ToolIcons.Eye, LocKey.ShowDisplacementHeatmap));
                BrushToolHandler.VertexDotSize = EditorGUILayout.Slider(
                    LatticeLocalization.Content(LocKey.DotSize),
                    BrushToolHandler.VertexDotSize, 1f, 8f);

                BrushToolHandler.ShowPenetration = GUILayout.Toggle(
                    BrushToolHandler.ShowPenetration,
                    ToolIcons.Content(ToolIcons.Eye, LocKey.ShowPenetration));
                if (BrushToolHandler.ShowPenetration)
                {
                    BrushToolHandler.PenetrationReference = (Renderer)EditorGUILayout.ObjectField(
                        LatticeLocalization.Content(LocKey.ReferenceMesh),
                        BrushToolHandler.PenetrationReference,
                        typeof(Renderer),
                        true);
                }
                EditorGUI.indentLevel--;
            }

            GUILayout.Space(4f);

            // Action buttons
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button(ToolIcons.Content(ToolIcons.Clear, LocKey.ClearAll)))
                {
                    if (deformer != null && deformer.ActiveLayerType == MeshDeformerLayerType.Brush)
                    {
                        BrushToolHandler.ClearAllDisplacements(deformer);
                    }
                }

                if (LatticeDeformationFeatureFlags.VertexMaskEditing &&
                    GUILayout.Button(ToolIcons.Content(ToolIcons.Clear, LocKey.ClearMask)))
                {
                    if (deformer != null && deformer.ActiveLayerType == MeshDeformerLayerType.Brush)
                    {
                        BrushToolHandler.ClearActiveMask(deformer);
                    }
                }
            }

            GUILayout.Space(2f);
        }
    }
}
#endif
