#if UNITY_EDITOR
using System;
using System.Diagnostics.CodeAnalysis;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;
using Net._32Ba.LatticeDeformationTool;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    [EditorTool("Mesh Deformer", typeof(LatticeDeformer))]
    [ExcludeFromCodeCoverage]
    public sealed class MeshDeformerTool : EditorTool
    {
        internal enum BrushSubMode
        {
            Brush = 0,
            VertexSelection = 1
        }

        private static BrushSubMode s_brushSubMode = BrushSubMode.Brush;
        private static bool s_simpleOverlayEnabled = true;

        internal static BrushSubMode CurrentBrushSubMode
        {
            get => s_brushSubMode;
            set
            {
                if (s_brushSubMode == value)
                {
                    return;
                }

                s_brushSubMode = value;
                SceneView.RepaintAll();
            }
        }

        internal static bool SimpleOverlayEnabled => s_simpleOverlayEnabled;

        internal static void UseSimpleOverlay(LatticeDeformer deformer, bool resetEditingOptions = true)
        {
            s_simpleOverlayEnabled = true;

            if (resetEditingOptions &&
                deformer != null &&
                deformer.TryGetActiveLayerFast(out var activeLayer))
            {
                if (activeLayer.Type == MeshDeformerLayerType.Lattice)
                {
                    LatticeToolHandler.IncludeInteriorControls = false;
                    LatticeToolHandler.ShowIndices = false;
                    LatticeToolHandler.OccludeWithSceneGeometry = false;
                    LatticeToolHandler.MirrorEditing = false;
                }
                else if (s_brushSubMode == BrushSubMode.VertexSelection)
                {
                    VertexSelectionHandler.CurrentTransformMode = VertexSelectionHandler.TransformMode.Move;
                    VertexSelectionHandler.CurrentHandleOrientation = VertexSelectionHandler.HandleOrientation.Normal;
                    VertexSelectionHandler.CurrentPivotMode = VertexSelectionHandler.PivotMode.Center;
                    VertexSelectionHandler.ProportionalRadius = 0f;
                    VertexSelectionHandler.BackfaceCulling = false;
                }
                else
                {
                    BrushToolHandler.CurrentBrushMode = BrushToolHandler.BrushMode.Normal;
                    BrushToolHandler.InvertBrush = false;
                    BrushToolHandler.MirrorEditing = false;
                    BrushToolHandler.ConnectedOnly = false;
                    BrushToolHandler.UseSurfaceDistance = false;
                    BrushToolHandler.BackfaceCulling = true;
                }
            }

            SceneView.RepaintAll();
        }

        internal static void UseDetailedOverlay()
        {
            s_simpleOverlayEnabled = false;
            SceneView.RepaintAll();
        }

        private readonly BrushToolHandler _brushHandler = new BrushToolHandler();
        private readonly VertexSelectionHandler _vertexHandler = new VertexSelectionHandler();
        private readonly LatticeToolHandler _latticeHandler = new LatticeToolHandler();

        private enum ActiveHandler { None, Lattice, Brush, VertexSelection }
        private ActiveHandler _currentHandler = ActiveHandler.None;
        private LatticeDeformer _currentHandlerTarget;

        internal static bool NeedsHandlerReactivation(
            LatticeDeformer currentTarget,
            LatticeDeformer nextTarget)
        {
            return !ReferenceEquals(currentTarget, nextTarget);
        }

        public override GUIContent toolbarIcon
        {
            get
            {
                var icon = EditorGUIUtility.IconContent("EditCollider");
                if (icon != null)
                    icon.tooltip = LatticeLocalization.Tr(LocKey.MeshDeformer);
                return icon ?? new GUIContent("MD", "Mesh Deformer");
            }
        }

        public override bool IsAvailable()
        {
            var deformer = target as LatticeDeformer;
            if (deformer == null && Selection.activeGameObject != null)
                deformer = Selection.activeGameObject.GetComponent<LatticeDeformer>();
            return deformer != null;
        }

        public override void OnActivated()
        {
            if (target is LatticeDeformer deformer)
                ActivateHandler(DetermineHandler(deformer), deformer);
            SceneView.RepaintAll();
        }

        public override void OnWillBeDeactivated()
        {
            DeactivateCurrentHandler();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (Event.current != null && Event.current.commandName == "UndoRedoPerformed")
                return;

            if (target is not LatticeDeformer deformer)
                return;

            var desired = DetermineHandler(deformer);
            if (desired != _currentHandler ||
                NeedsHandlerReactivation(_currentHandlerTarget, deformer))
            {
                DeactivateCurrentHandler();
                ActivateHandler(desired, deformer);
            }

            switch (_currentHandler)
            {
                case ActiveHandler.Lattice:
                    _latticeHandler.OnToolGUI(window, deformer);
                    break;
                case ActiveHandler.Brush:
                    _brushHandler.OnToolGUI(window, deformer);
                    break;
                case ActiveHandler.VertexSelection:
                    _vertexHandler.OnToolGUI(window, deformer);
                    break;
            }
        }

        private ActiveHandler DetermineHandler(LatticeDeformer deformer)
        {
            if (!deformer.TryGetActiveLayerFast(out var layer))
                return ActiveHandler.None;
            if (layer.Type == MeshDeformerLayerType.Lattice)
                return ActiveHandler.Lattice;
            return s_brushSubMode == BrushSubMode.VertexSelection
                ? ActiveHandler.VertexSelection
                : ActiveHandler.Brush;
        }

        private void ActivateHandler(ActiveHandler handler, LatticeDeformer deformer)
        {
            _currentHandler = handler;
            _currentHandlerTarget = deformer;
            switch (handler)
            {
                case ActiveHandler.Lattice:
                    _latticeHandler.Activate(deformer);
                    break;
                case ActiveHandler.Brush:
                    _brushHandler.Activate(deformer);
                    break;
                case ActiveHandler.VertexSelection:
                    _vertexHandler.Activate(deformer);
                    break;
            }
        }

        private void DeactivateCurrentHandler()
        {
            switch (_currentHandler)
            {
                case ActiveHandler.Lattice:
                    _latticeHandler.Deactivate();
                    break;
                case ActiveHandler.Brush:
                    _brushHandler.Deactivate();
                    break;
                case ActiveHandler.VertexSelection:
                    _vertexHandler.Deactivate();
                    break;
            }
            _currentHandler = ActiveHandler.None;
            _currentHandlerTarget = null;
        }
    }

    [Overlay(typeof(SceneView), k_OverlayId, k_OverlayId, defaultDisplay = true)]
    [ExcludeFromCodeCoverage]
    internal sealed class MeshDeformerToolOverlay : IMGUIOverlay, ITransientOverlay
    {
        private const string k_OverlayId = "Mesh Deformer";

        public bool visible => ToolManager.activeToolType == typeof(MeshDeformerTool);

        public override void OnGUI()
        {
            displayName = LatticeLocalization.Tr(LocKey.MeshDeformer);

            if (ToolManager.activeToolType != typeof(MeshDeformerTool))
            {
                GUILayout.Label(LatticeLocalization.Content(LocKey.MeshDeformer), EditorStyles.miniLabel);
                return;
            }

            var selectedDeformer = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<LatticeDeformer>()
                : null;
            if (selectedDeformer == null)
            {
                EditorGUILayout.HelpBox(
                    LatticeLocalization.Tr(LocKey.SelectMeshDeformer),
                    MessageType.Info);
                return;
            }

            using (new GUILayout.VerticalScope(GUILayout.MinWidth(260f)))
            {
                var layers = selectedDeformer.GetActiveLayersFast();
                if (layers.Count == 0 ||
                    !selectedDeformer.TryGetActiveLayerFast(out var activeLayer))
                {
                    EditorGUILayout.HelpBox(
                        LatticeLocalization.Tr(LocKey.NoDeformationLayers),
                        MessageType.Info);
                    return;
                }

                if (MeshDeformerTool.SimpleOverlayEnabled)
                {
                    DrawSimpleOverlay(selectedDeformer, activeLayer);
                }
                else
                {
                    DrawDetailedOverlay(selectedDeformer, activeLayer);
                }
            }
        }

        private static void DrawSimpleOverlay(LatticeDeformer deformer, LatticeLayer activeLayer)
        {
            if (activeLayer.Type == MeshDeformerLayerType.Lattice)
            {
                GUILayout.Label(
                    LatticeLocalization.Content(LocKey.GuidedAdjustShape),
                    EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    LatticeLocalization.Tr(LocKey.GuidedOverlayAdjustShapeHint),
                    MessageType.Info);

                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label(LatticeToolHandler.GetSelectionLabel(), EditorStyles.miniLabel);
                    if (GUILayout.Button(
                            ToolIcons.Content(ToolIcons.Clear, LocKey.ClearSelection),
                            GUILayout.Width(110f)))
                    {
                        LatticeToolHandler.ClearSelection();
                    }
                }

                DrawSimpleLatticeMirrorControls();
            }
            else if (MeshDeformerTool.CurrentBrushSubMode == MeshDeformerTool.BrushSubMode.VertexSelection)
            {
                DrawSimpleVertexOverlay(deformer);
            }
            else
            {
                DrawSimpleBrushOverlay();
            }

            GUILayout.Space(6f);
            if (GUILayout.Button(LatticeLocalization.Tr(LocKey.OpenDetailedOverlay)))
            {
                MeshDeformerTool.UseDetailedOverlay();
            }

            if (GUILayout.Button(LatticeLocalization.Tr(LocKey.FinishEditing), GUILayout.Height(28f)))
            {
                ToolManager.RestorePreviousTool();
            }
        }

        private static void DrawSimpleBrushOverlay()
        {
            GUILayout.Label(
                LatticeLocalization.Content(LocKey.GuidedSculptSurface),
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                LatticeLocalization.Tr(LocKey.GuidedOverlaySculptSurfaceHint),
                MessageType.Info);

            var modeContent = new[]
            {
                LatticeLocalization.Content(LocKey.GuidedBrushPushPull),
                LatticeLocalization.Content(LocKey.GuidedBrushMove),
                LatticeLocalization.Content(LocKey.GuidedBrushSmooth)
            };
            int currentMode = Mathf.Clamp((int)BrushToolHandler.CurrentBrushMode, 0, modeContent.Length - 1);
            int nextMode = GUILayout.Toolbar(currentMode, modeContent);
            BrushToolHandler.CurrentBrushMode = (BrushToolHandler.BrushMode)Mathf.Clamp(
                nextMode,
                0,
                modeContent.Length - 1);

            if (BrushToolHandler.CurrentBrushMode == BrushToolHandler.BrushMode.Normal)
            {
                var directionContent = new[]
                {
                    LatticeLocalization.Content(LocKey.GuidedBrushRaise),
                    LatticeLocalization.Content(LocKey.GuidedBrushIndent)
                };
                int direction = GUILayout.Toolbar(BrushToolHandler.InvertBrush ? 1 : 0, directionContent);
                BrushToolHandler.InvertBrush = direction == 1;
            }

            float radiusCm = BrushToolHandler.BrushRadius * 100f;
            EditorGUI.BeginChangeCheck();
            radiusCm = EditorGUILayout.Slider(
                new GUIContent(
                    LatticeLocalization.Tr(LocKey.BrushRadius) + " (cm)",
                    LatticeLocalization.Tooltip(LocKey.BrushRadius)),
                radiusCm,
                0f,
                10f);
            if (EditorGUI.EndChangeCheck())
            {
                BrushToolHandler.BrushRadius = radiusCm / 100f;
            }

            float strengthPercent = BrushToolHandler.BrushStrength * 100f;
            strengthPercent = EditorGUILayout.Slider(
                new GUIContent(
                    LatticeLocalization.Tr(LocKey.BrushStrength) + " (%)",
                    LatticeLocalization.Tooltip(LocKey.BrushStrength)),
                strengthPercent,
                0f,
                100f);
            BrushToolHandler.BrushStrength = strengthPercent / 100f;

            DrawSimpleBrushMirrorControls();
        }

        private static void DrawSimpleVertexOverlay(LatticeDeformer deformer)
        {
            GUILayout.Label(
                LatticeLocalization.Content(LocKey.GuidedMoveVertices),
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                LatticeLocalization.Tr(LocKey.GuidedOverlayMoveVerticesHint),
                MessageType.Info);

            var modeContent = new[]
            {
                ToolIcons.Content(ToolIcons.Move, LocKey.Move),
                ToolIcons.Content(ToolIcons.Rotate, LocKey.Rotate),
                ToolIcons.Content(ToolIcons.Scale, LocKey.Scale)
            };
            int mode = GUILayout.Toolbar((int)VertexSelectionHandler.CurrentTransformMode, modeContent);
            VertexSelectionHandler.CurrentTransformMode =
                (VertexSelectionHandler.TransformMode)Mathf.Clamp(mode, 0, modeContent.Length - 1);

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
            }

            DrawSimpleProportionalControls();
            DrawSimpleVertexMirrorControls(deformer);
        }

        private static void DrawSimpleLatticeMirrorControls()
        {
            GUILayout.Space(4f);
            LatticeToolHandler.MirrorEditing = GUILayout.Toggle(
                LatticeToolHandler.MirrorEditing,
                ToolIcons.Content(ToolIcons.Mirror, LocKey.EnableMirror));

            using (new EditorGUI.DisabledScope(!LatticeToolHandler.MirrorEditing))
            {
                GUILayout.Label(LatticeLocalization.Content(LocKey.MirrorAxis), EditorStyles.miniLabel);
                int axis = GUILayout.Toolbar(
                    (int)LatticeToolHandler.CurrentMirrorAxis,
                    LatticeToolHandler.AxisOptions);
                axis = Mathf.Clamp(axis, 0, LatticeToolHandler.AxisOptions.Length - 1);
                LatticeToolHandler.CurrentMirrorAxis = (LatticeToolHandler.MirrorAxis)axis;
            }
        }

        private static void DrawSimpleBrushMirrorControls()
        {
            GUILayout.Space(4f);
            BrushToolHandler.MirrorEditing = GUILayout.Toggle(
                BrushToolHandler.MirrorEditing,
                ToolIcons.Content(ToolIcons.Mirror, LocKey.EnableMirror));

            using (new EditorGUI.DisabledScope(!BrushToolHandler.MirrorEditing))
            {
                GUILayout.Label(LatticeLocalization.Content(LocKey.MirrorAxis), EditorStyles.miniLabel);
                int axis = GUILayout.Toolbar(
                    (int)BrushToolHandler.CurrentMirrorAxis,
                    BrushToolHandler.AxisOptions);
                axis = Mathf.Clamp(axis, 0, BrushToolHandler.AxisOptions.Length - 1);
                BrushToolHandler.CurrentMirrorAxis = (BrushToolHandler.MirrorAxis)axis;
            }
        }

        private static void DrawSimpleProportionalControls()
        {
            GUILayout.Space(4f);
            bool enabled = VertexSelectionHandler.ProportionalRadius > 0f;
            bool nextEnabled = GUILayout.Toggle(
                enabled,
                LatticeLocalization.Content(LocKey.ProportionalEditing));
            if (nextEnabled != enabled)
            {
                VertexSelectionHandler.ProportionalRadius = nextEnabled ? 0.02f : 0f;
            }

            using (new EditorGUI.DisabledScope(!nextEnabled))
            {
                float radiusCm = VertexSelectionHandler.ProportionalRadius * 100f;
                EditorGUI.BeginChangeCheck();
                radiusCm = EditorGUILayout.Slider(
                    new GUIContent(
                        LatticeLocalization.Tr(LocKey.ProportionalRadius) + " (cm)",
                        LatticeLocalization.Tooltip(LocKey.ProportionalRadius)),
                    radiusCm,
                    0.1f,
                    10f);
                if (EditorGUI.EndChangeCheck())
                {
                    VertexSelectionHandler.ProportionalRadius = radiusCm / 100f;
                }
            }
        }

        private static void DrawSimpleVertexMirrorControls(LatticeDeformer deformer)
        {
            GUILayout.Space(4f);
            GUILayout.Label(LatticeLocalization.Content(LocKey.MirrorAxis), EditorStyles.miniLabel);
            int axis = GUILayout.Toolbar(
                (int)BrushToolHandler.CurrentMirrorAxis,
                BrushToolHandler.AxisOptions);
            axis = Mathf.Clamp(axis, 0, BrushToolHandler.AxisOptions.Length - 1);
            BrushToolHandler.CurrentMirrorAxis = (BrushToolHandler.MirrorAxis)axis;

            using (new EditorGUI.DisabledScope(VertexSelectionHandler.SelectedVertexCount == 0))
            {
                if (GUILayout.Button(ToolIcons.Content(ToolIcons.Mirror, LocKey.Mirror)))
                {
                    VertexSelectionHandler.SelectMirrorPartners(deformer, axis);
                }
            }
        }

        private static void DrawDetailedOverlay(LatticeDeformer selectedDeformer, LatticeLayer activeLayer)
        {
            if (GUILayout.Button(LatticeLocalization.Tr(LocKey.ReturnToSimpleOverlay)))
            {
                MeshDeformerTool.UseSimpleOverlay(selectedDeformer, resetEditingOptions: false);
                return;
            }

            // Layer selector
            DrawLayerSelector(selectedDeformer);

            GUILayout.Space(4f);

            // Sub-mode selector for brush layers
            if (activeLayer.Type == MeshDeformerLayerType.Brush)
            {
                var subModeLabels = new GUIContent[]
                {
                    ToolIcons.Content(ToolIcons.Brush, LocKey.Brush),
                    ToolIcons.Content(ToolIcons.VertexSelect, LocKey.VertexSelection)
                };
                int subMode = GUILayout.Toolbar(
                    (int)MeshDeformerTool.CurrentBrushSubMode, subModeLabels);
                subMode = Mathf.Clamp(subMode, 0, subModeLabels.Length - 1);
                MeshDeformerTool.CurrentBrushSubMode = (MeshDeformerTool.BrushSubMode)subMode;
                GUILayout.Space(4f);
            }

            // Delegate to handler-specific GUI
            if (activeLayer.Type == MeshDeformerLayerType.Lattice)
            {
                LatticeToolHandler.DrawOverlayGUI(selectedDeformer);
            }
            else if (MeshDeformerTool.CurrentBrushSubMode == MeshDeformerTool.BrushSubMode.VertexSelection)
            {
                VertexSelectionHandler.DrawOverlayGUI(selectedDeformer);
            }
            else
            {
                BrushToolHandler.DrawOverlayGUI(selectedDeformer);
            }
        }

        private static GUIContent IconContent(string locKey, string iconName)
        {
            var text = LatticeLocalization.Tr(locKey);
            var tooltip = LatticeLocalization.Tooltip(locKey);
            var icon = EditorGUIUtility.IconContent(iconName);
            return icon?.image != null
                ? new GUIContent(text, icon.image, tooltip)
                : new GUIContent(text, tooltip);
        }

        private static void DrawLayerSelector(LatticeDeformer deformer)
        {
            var layers = deformer.GetActiveLayersFast();
            if (layers.Count == 0) return;

            var names = new string[layers.Count];
            for (int i = 0; i < layers.Count; i++)
            {
                string typeSuffix = layers[i].Type == MeshDeformerLayerType.Lattice ? " [L]" : " [B]";
                names[i] = layers[i].Name + typeSuffix;
            }

            int current = deformer.GetActiveLayerIndexFast();
            int next = EditorGUILayout.Popup(
                LatticeLocalization.Content(LocKey.ActiveLayer), current, names);
            if (next != current && next >= 0 && next < layers.Count)
            {
                Undo.RecordObject(deformer, "Change Active Layer");
                deformer.ActiveLayerIndex = next;
                SceneView.RepaintAll();
            }
        }

    }
}
#endif
