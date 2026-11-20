#if UNITY_EDITOR
using UnityEngine.UIElements;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class VertexToolOverlay
    {
        private static bool s_showVisualizationSection;

        /// <summary>Detailed overlay content for vertex selection. Building it never edits the deformer.</summary>
        internal static VisualElement Create(LatticeDeformer deformer, OverlayControls c)
        {
            var root = MdUI.Element("md-overlay__content");
            root.Add(OverlayControls.Group(LatticeLocalization.Tr(LocKey.TransformMode),
                c.Segmented(OverlayControls.Tr(LocKey.Move, LocKey.Rotate, LocKey.Scale),
                    () => (int)VertexSelectionHandler.CurrentTransformMode,
                    index => VertexSelectionHandler.CurrentTransformMode = (VertexSelectionHandler.TransformMode)index,
                    new[] { "move", "rotate", "scale" }, strong: true,
                    tooltips: OverlayControls.Tooltips(LocKey.Move, LocKey.Rotate, LocKey.Scale))));
            root.Add(OverlayControls.Group(LatticeLocalization.Tr(LocKey.HandleOrientation),
                c.Segmented(OverlayControls.Tr(LocKey.Local, LocKey.Global, LocKey.Normal),
                    () => (int)VertexSelectionHandler.CurrentHandleOrientation,
                    index => VertexSelectionHandler.CurrentHandleOrientation = (VertexSelectionHandler.HandleOrientation)index,
                    tooltips: OverlayControls.Tooltips(LocKey.Local, LocKey.Global, LocKey.Normal))));
            root.Add(OverlayControls.Group(LatticeLocalization.Tr(LocKey.Pivot),
                c.Segmented(OverlayControls.Tr(LocKey.Center, LocKey.LastSelected),
                    () => (int)VertexSelectionHandler.CurrentPivotMode,
                    index => VertexSelectionHandler.CurrentPivotMode = (VertexSelectionHandler.PivotMode)index,
                    tooltips: OverlayControls.Tooltips(LocKey.Center, LocKey.LastSelected))));

            // Proportional editing: radius 0 disables it. Shown in cm, stored in mesh-local meters.
            root.Add(c.Slider(LatticeLocalization.Tr(LocKey.ProportionalRadius) + " (cm)", 0f, 10f,
                () => VertexSelectionHandler.ProportionalRadius * 100f,
                value => VertexSelectionHandler.ProportionalRadius = value / 100f,
                LatticeLocalization.Tooltip(LocKey.ProportionalRadius)));
            root.Add(c.Popup(LatticeLocalization.Tr(LocKey.Falloff),
                OverlayControls.Tr(LocKey.Smooth, LocKey.Linear, LocKey.Constant, LocKey.Sphere, LocKey.Gaussian),
                () => (int)VertexSelectionHandler.ProportionalFalloffType,
                index => VertexSelectionHandler.ProportionalFalloffType = (VertexSelectionHandler.FalloffType)index,
                LatticeLocalization.Tooltip(LocKey.Falloff)));

            var list = MdUI.Element("md-foldout-list");
            list.Add(MdUI.Foldout(LatticeLocalization.Tr(LocKey.Visualization),
                () => s_showVisualizationSection, open => s_showVisualizationSection = open, out var visualization));
            visualization.Add(c.Switch(LatticeLocalization.Tr(LocKey.ShowWireframe),
                () => VertexSelectionHandler.ShowWireframe, value => VertexSelectionHandler.ShowWireframe = value));
            visualization.Add(c.Slider(LatticeLocalization.Tr(LocKey.DotSize), 1f, 8f,
                () => VertexSelectionHandler.VertexDotSize, value => VertexSelectionHandler.VertexDotSize = value,
                LatticeLocalization.Tooltip(LocKey.DotSize)));
            visualization.Add(c.Switch(LatticeLocalization.Tr(LocKey.BackfaceCulling),
                () => VertexSelectionHandler.BackfaceCulling, value => VertexSelectionHandler.BackfaceCulling = value));
            root.Add(list);

            root.Add(c.LiveText(VertexSelectionHandler.GetSelectionLabel));
            root.Add(OverlayControls.Row(
                MdUI.Button(LatticeLocalization.Tr(LocKey.SelectAll), () => VertexSelectionHandler.SelectAll(deformer), "md-button--small"),
                MdUI.Button(LatticeLocalization.Tr(LocKey.SelectNone), VertexSelectionHandler.ClearSelection, "md-button--small"),
                MdUI.Button(LatticeLocalization.Tr(LocKey.Invert), () => VertexSelectionHandler.InvertSelection(deformer), "md-button--small")));
            var resetSelected = MdUI.Button(LatticeLocalization.Tr(LocKey.ResetSelectedVertices),
                () => VertexSelectionHandler.ResetSelectedVertices(deformer), "md-button--small md-button--danger");
            c.EnabledWhen(resetSelected, () => VertexSelectionHandler.SelectedVertexCount > 0);
            root.Add(OverlayControls.Row(resetSelected,
                MdUI.Button(LatticeLocalization.Tr(LocKey.ResetAllVertices),
                    () => VertexSelectionHandler.ResetAllVertices(deformer), "md-button--small md-button--danger")));
            root.Add(OverlayControls.Hint(LatticeLocalization.Tr(LocKey.ShiftClickHint)));
            return root;
        }
    }
}
#endif
