#if UNITY_EDITOR
using System;
using System.Diagnostics.CodeAnalysis;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine.UIElements;
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
                // IconContent is cached by Unity and shared with other tools.
                // Own the GUIContent before changing its localized tooltip.
                var shared = EditorGUIUtility.IconContent("EditCollider");
                var icon = shared != null ? new GUIContent(shared) : new GUIContent("MD");
                icon.tooltip = LatticeLocalization.Tr(LocKey.MeshDeformer);
                return icon;
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
    internal sealed class MeshDeformerToolOverlay : Overlay, ITransientOverlay
    {
        private const string k_OverlayId = "Mesh Deformer";
        private VisualElement _root;
        private OverlayControls _controls;
        private string _signature;

        public bool visible => ToolManager.activeToolType == typeof(MeshDeformerTool);

        public override void OnCreated()
        {
            displayName = LatticeLocalization.Tr(LocKey.MeshDeformer);
            LatticeLocalization.LanguageChanged += OnLanguageChanged;
        }

        public override void OnWillBeDestroyed()
        {
            LatticeLocalization.LanguageChanged -= OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            displayName = LatticeLocalization.Tr(LocKey.MeshDeformer);
            if (_root != null) MeshDeformerFonts.Apply(_root);
            _signature = null;
        }

        public override VisualElement CreatePanelContent()
        {
            displayName = LatticeLocalization.Tr(LocKey.MeshDeformer);
            _root = new VisualElement { name = "mesh-deformer-overlay" };
            MeshDeformerStyles.Apply(_root);
            _root.AddToClassList("md-overlay");
            _signature = null;
            Refresh();
            _root.schedule.Execute(Refresh).Every(100);
            return _root;
        }

        /// <summary>
        /// Rebuilds the panel when the edited object or mode changes; otherwise re-reads the tool
        /// settings into the existing controls. Neither path writes to the deformer.
        /// </summary>
        internal void Refresh()
        {
            if (_root == null) return;
            var state = OverlayState.Read();
            string signature = state.Signature;
            if (signature != _signature)
            {
                _signature = signature;
                _controls = new OverlayControls();
                _root.Clear();
                Build(_root, _controls, state);
            }
            else
            {
                _controls?.Sync();
            }
        }

        internal readonly struct OverlayState
        {
            internal readonly bool ToolActive;
            internal readonly LatticeDeformer Deformer;
            internal readonly LatticeLayer ActiveLayer;
            internal readonly int LayerCount, ActiveIndex;
            internal readonly string Signature;

            private OverlayState(bool toolActive, LatticeDeformer deformer, LatticeLayer layer, int count, int index)
            {
                ToolActive = toolActive; Deformer = deformer; ActiveLayer = layer; LayerCount = count; ActiveIndex = index;
                Signature = string.Join("|", LatticeLocalization.CurrentLanguage, toolActive,
                    deformer != null ? deformer.GetInstanceID() : 0, count, index, layer?.Type, layer?.Name,
                    MeshDeformerTool.SimpleOverlayEnabled, MeshDeformerTool.CurrentBrushSubMode);
            }

            internal static OverlayState Read()
            {
                bool active = ToolManager.activeToolType == typeof(MeshDeformerTool);
                var deformer = active && Selection.activeGameObject != null
                    ? Selection.activeGameObject.GetComponent<LatticeDeformer>()
                    : null;
                if (deformer == null) return new OverlayState(active, null, null, 0, -1);
                var layers = deformer.GetActiveLayersFast();
                deformer.TryGetActiveLayerFast(out var layer);
                return new OverlayState(active, deformer, layers.Count == 0 ? null : layer, layers.Count,
                    deformer.GetActiveLayerIndexFast());
            }
        }

        internal static void Build(VisualElement root, OverlayControls c, OverlayState state)
        {
            if (!state.ToolActive)
            {
                root.Add(MdUI.Text(LatticeLocalization.Tr(LocKey.MeshDeformer), "md-text-small"));
                return;
            }
            if (state.Deformer == null)
            {
                root.Add(Body(MdUI.HelpBox(LatticeLocalization.Tr(LocKey.SelectMeshDeformer))));
                return;
            }
            if (state.ActiveLayer == null)
            {
                root.Add(Body(MdUI.HelpBox(LatticeLocalization.Tr(LocKey.NoDeformationLayers))));
                return;
            }
            if (MeshDeformerTool.SimpleOverlayEnabled) BuildSimple(root, c, state.Deformer, state.ActiveLayer);
            else BuildDetailed(root, c, state.Deformer, state.ActiveLayer);
        }

        private static VisualElement Body(params VisualElement[] children) => MdUI.Element("md-overlay__body", children);

        private static VisualElement Header(string mode, string icon, string title, VisualElement accessory = null)
        {
            var tile = MdUI.Element("md-overlay__icon md-mode-" + mode, MdUI.Icon(icon));
            return MdUI.Element("md-overlay__header", tile, MdUI.Text(title, "md-overlay__title"), accessory);
        }

        private static void BuildSimple(VisualElement root, OverlayControls c, LatticeDeformer deformer, LatticeLayer layer)
        {
            var body = Body();
            if (layer.Type == MeshDeformerLayerType.Lattice)
            {
                root.Add(Header("lattice", "lattice", LatticeLocalization.Tr(LocKey.GuidedAdjustShape)));
                body.Add(OverlayControls.Hint(LatticeLocalization.Tr(LocKey.GuidedOverlayAdjustShapeHint)));
                body.Add(MdUI.Button(LatticeLocalization.Tr(LocKey.ClearSelection), LatticeToolHandler.ClearSelection,
                    "md-button--small"));
                body.Add(c.Switch(LatticeLocalization.Tr(LocKey.EnableSymmetryEditing),
                    () => LatticeToolHandler.MirrorEditing, value => LatticeToolHandler.MirrorEditing = value));
                var axis = OverlayControls.Group(LatticeLocalization.Tr(LocKey.SymmetryAxis),
                    c.Segmented(OverlayControls.Texts(LatticeToolHandler.AxisOptions),
                        () => (int)LatticeToolHandler.CurrentMirrorAxis,
                        index => LatticeToolHandler.CurrentMirrorAxis = (LatticeToolHandler.MirrorAxis)index));
                c.EnabledWhen(axis, () => LatticeToolHandler.MirrorEditing);
                body.Add(axis);
            }
            else if (MeshDeformerTool.CurrentBrushSubMode == MeshDeformerTool.BrushSubMode.VertexSelection)
            {
                root.Add(Header("vertex", "vertex", LatticeLocalization.Tr(LocKey.GuidedMoveVertices)));
                BuildSimpleVertex(body, c, deformer);
            }
            else
            {
                root.Add(Header("brush", "brush", LatticeLocalization.Tr(LocKey.GuidedSculptSurface)));
                BuildSimpleBrush(body, c);
            }
            root.Add(body);
            root.Add(MdUI.Element("md-overlay__footer",
                MdUI.Button(LatticeLocalization.Tr(LocKey.OpenDetailedOverlay), MeshDeformerTool.UseDetailedOverlay),
                MdUI.Button(LatticeLocalization.Tr(LocKey.FinishEditing), ToolManager.RestorePreviousTool,
                    "md-button--primary", "check")));
        }

        private static void BuildSimpleBrush(VisualElement body, OverlayControls c)
        {
            body.Add(OverlayControls.Hint(LatticeLocalization.Tr(LocKey.GuidedOverlaySculptSurfaceHint)));
            body.Add(c.Segmented(
                OverlayControls.Tr(LocKey.GuidedBrushPushPull, LocKey.GuidedBrushMove, LocKey.GuidedBrushSmooth),
                () => Mathf.Clamp((int)BrushToolHandler.CurrentBrushMode, 0, 2),
                index => BrushToolHandler.CurrentBrushMode = (BrushToolHandler.BrushMode)index,
                BrushToolOverlay.ModeIcons, strong: true,
                tooltips: OverlayControls.Tooltips(LocKey.GuidedBrushPushPull, LocKey.GuidedBrushMove, LocKey.GuidedBrushSmooth)));
            var direction = c.Segmented(OverlayControls.Tr(LocKey.GuidedBrushRaise, LocKey.GuidedBrushIndent),
                () => BrushToolHandler.InvertBrush ? 1 : 0, index => BrushToolHandler.InvertBrush = index == 1,
                new[] { "op-raise", "op-indent" });
            c.VisibleWhen(direction, () => BrushToolHandler.CurrentBrushMode == BrushToolHandler.BrushMode.Normal);
            body.Add(direction);
            body.Add(c.Slider(LatticeLocalization.Tr(LocKey.BrushRadius) + " (cm)", 0f, 10f,
                () => BrushToolHandler.BrushRadius * 100f, value => BrushToolHandler.BrushRadius = value / 100f,
                LatticeLocalization.Tooltip(LocKey.BrushRadius)));
            body.Add(c.Slider(LatticeLocalization.Tr(LocKey.BrushStrength) + " (%)", 0f, 100f,
                () => BrushToolHandler.BrushStrength * 100f, value => BrushToolHandler.BrushStrength = value / 100f,
                LatticeLocalization.Tooltip(LocKey.BrushStrength)));
            body.Add(c.Switch(LatticeLocalization.Tr(LocKey.EnableMirror),
                () => BrushToolHandler.MirrorEditing, value => BrushToolHandler.MirrorEditing = value));
            var axis = OverlayControls.Group(LatticeLocalization.Tr(LocKey.MirrorAxis),
                c.Segmented(OverlayControls.Texts(BrushToolHandler.AxisOptions),
                    () => (int)BrushToolHandler.CurrentMirrorAxis,
                    index => BrushToolHandler.CurrentMirrorAxis = (BrushToolHandler.MirrorAxis)index));
            c.EnabledWhen(axis, () => BrushToolHandler.MirrorEditing);
            body.Add(axis);
        }

        private static void BuildSimpleVertex(VisualElement body, OverlayControls c, LatticeDeformer deformer)
        {
            body.Add(OverlayControls.Hint(LatticeLocalization.Tr(LocKey.GuidedOverlayMoveVerticesHint)));
            body.Add(c.Segmented(OverlayControls.Tr(LocKey.Move, LocKey.Rotate, LocKey.Scale),
                () => (int)VertexSelectionHandler.CurrentTransformMode,
                index => VertexSelectionHandler.CurrentTransformMode = (VertexSelectionHandler.TransformMode)index,
                new[] { "move", "rotate", "scale" }, strong: true,
                tooltips: OverlayControls.Tooltips(LocKey.Move, LocKey.Rotate, LocKey.Scale)));
            body.Add(OverlayControls.Row(
                MdUI.Button(LatticeLocalization.Tr(LocKey.SelectAll), () => VertexSelectionHandler.SelectAll(deformer), "md-button--small"),
                MdUI.Button(LatticeLocalization.Tr(LocKey.SelectNone), VertexSelectionHandler.ClearSelection, "md-button--small")));
            body.Add(c.Switch(LatticeLocalization.Tr(LocKey.ProportionalEditing),
                () => VertexSelectionHandler.ProportionalRadius > 0f,
                value => VertexSelectionHandler.ProportionalRadius = value ? 0.02f : 0f,
                LatticeLocalization.Tooltip(LocKey.ProportionalEditing)));
            var radius = c.Slider(LatticeLocalization.Tr(LocKey.ProportionalRadius) + " (cm)", 0.1f, 10f,
                () => VertexSelectionHandler.ProportionalRadius * 100f,
                value => VertexSelectionHandler.ProportionalRadius = value / 100f,
                LatticeLocalization.Tooltip(LocKey.ProportionalRadius));
            c.EnabledWhen(radius, () => VertexSelectionHandler.ProportionalRadius > 0f);
            body.Add(radius);
            var mirror = MdUI.Button(LatticeLocalization.Tr(LocKey.SelectMirrorPartners),
                () => VertexSelectionHandler.SelectMirrorPartners(deformer, (int)BrushToolHandler.CurrentMirrorAxis),
                "md-button--small");
            mirror.tooltip = LatticeLocalization.Tooltip(LocKey.SelectMirrorPartners);
            c.EnabledWhen(mirror, () => VertexSelectionHandler.SelectedVertexCount > 0);
            body.Add(OverlayControls.Group(LatticeLocalization.Tr(LocKey.MirrorAxis),
                c.Segmented(OverlayControls.Texts(BrushToolHandler.AxisOptions),
                    () => (int)BrushToolHandler.CurrentMirrorAxis,
                    index => BrushToolHandler.CurrentMirrorAxis = (BrushToolHandler.MirrorAxis)index),
                mirror));
        }

        private static void BuildDetailed(VisualElement root, OverlayControls c, LatticeDeformer deformer, LatticeLayer layer)
        {
            bool brush = layer.Type == MeshDeformerLayerType.Brush;
            bool vertex = brush && MeshDeformerTool.CurrentBrushSubMode == MeshDeformerTool.BrushSubMode.VertexSelection;
            var back = MdUI.Button(LatticeLocalization.Tr(LocKey.ReturnToSimpleOverlay),
                () => MeshDeformerTool.UseSimpleOverlay(deformer, resetEditingOptions: false), "md-button--small");
            root.Add(Header(vertex ? "vertex" : brush ? "brush" : "lattice", vertex ? "vertex" : brush ? "brush" : "lattice",
                string.IsNullOrEmpty(layer.Name) ? LatticeLocalization.Tr(LocKey.MeshDeformer) : layer.Name, back));

            var body = Body();
            var layers = deformer.GetActiveLayersFast();
            var names = new string[layers.Count];
            for (int i = 0; i < layers.Count; i++)
                names[i] = layers[i].Name + " (" + LatticeLocalization.Tr(layers[i].Type == MeshDeformerLayerType.Lattice
                    ? LocKey.LayerBadgeLattice : LocKey.LayerBadgeBrush) + ")";
            body.Add(c.Popup(LatticeLocalization.Tr(LocKey.ActiveLayer), names, deformer.GetActiveLayerIndexFast, next =>
            {
                if (deformer == null || next == deformer.GetActiveLayerIndexFast() || next < 0 ||
                    next >= deformer.GetActiveLayersFast().Count) return;
                Undo.RecordObject(deformer, "Change Active Layer");
                deformer.ActiveLayerIndex = next;
                SceneView.RepaintAll();
            }, LatticeLocalization.Tooltip(LocKey.ActiveLayer)));

            if (brush)
            {
                body.Add(c.Segmented(OverlayControls.Tr(LocKey.Brush, LocKey.VertexSelection),
                    () => (int)MeshDeformerTool.CurrentBrushSubMode,
                    index => MeshDeformerTool.CurrentBrushSubMode = (MeshDeformerTool.BrushSubMode)index,
                    new[] { "brush", "vertex" }, tooltips: OverlayControls.Tooltips(LocKey.Brush, LocKey.VertexSelection)));
            }

            if (!brush) body.Add(LatticeToolHandler.CreateOverlayGUI(deformer, c));
            else if (vertex) body.Add(VertexSelectionHandler.CreateOverlayGUI(deformer, c));
            else body.Add(BrushToolHandler.CreateOverlayGUI(deformer, c));
            root.Add(body);
        }
    }
}
#endif
