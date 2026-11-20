#if UNITY_EDITOR
using UnityEngine.UIElements;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class LatticeToolOverlay
    {
        private static bool s_showSymmetrySection;

        /// <summary>Detailed overlay content for lattice layers. Building it never edits the deformer.</summary>
        internal static VisualElement Create(LatticeDeformer deformer, OverlayControls c)
        {
            var root = MdUI.Element("md-overlay__content");
            root.Add(OverlayControls.Group(LatticeLocalization.Tr(LocKey.ControlPointScope),
                c.Segmented(OverlayControls.Tr(LocKey.BoundaryOnly, LocKey.AllControls),
                    () => LatticeToolHandler.IncludeInteriorControls ? 1 : 0,
                    index => LatticeToolHandler.IncludeInteriorControls = index == 1,
                    new[] { "scope-outer", "scope-all" },
                    tooltips: OverlayControls.Tooltips(LocKey.BoundaryOnly, LocKey.AllControls))));

            root.Add(c.Switch(LatticeLocalization.Tr(LocKey.ShowControlIds),
                () => LatticeToolHandler.ShowIndices, value => LatticeToolHandler.ShowIndices = value));
            root.Add(c.Switch(LatticeLocalization.Tr(LocKey.KeepControlPointsVisible),
                () => !LatticeToolHandler.OccludeWithSceneGeometry,
                value => LatticeToolHandler.OccludeWithSceneGeometry = !value));

            var clear = MdUI.Button(LatticeLocalization.Tr(LocKey.ClearSelection), LatticeToolHandler.ClearSelection,
                "md-button--small");
            root.Add(OverlayControls.Row(clear, c.LiveText(LatticeToolHandler.GetSelectionLabel)));

            var list = MdUI.Element("md-foldout-list");
            list.Add(MdUI.Foldout(LatticeLocalization.Tr(LocKey.EnableSymmetryEditing),
                () => s_showSymmetrySection, open => s_showSymmetrySection = open, out var symmetry));
            symmetry.Add(c.Switch(LatticeLocalization.Tr(LocKey.EnableSymmetryEditing),
                () => LatticeToolHandler.MirrorEditing, value => LatticeToolHandler.MirrorEditing = value));
            var options = MdUI.Element(null,
                c.Popup(LatticeLocalization.Tr(LocKey.SymmetryMode), OverlayControls.Texts(LatticeToolHandler.BehaviorOptions),
                    () => (int)LatticeToolHandler.CurrentMirrorBehavior,
                    index => LatticeToolHandler.CurrentMirrorBehavior = (LatticeToolHandler.MirrorBehavior)index,
                    LatticeLocalization.Tooltip(LocKey.SymmetryMode)),
                OverlayControls.Group(LatticeLocalization.Tr(LocKey.SymmetryAxis),
                    c.Segmented(OverlayControls.Texts(LatticeToolHandler.AxisOptions),
                        () => (int)LatticeToolHandler.CurrentMirrorAxis,
                        index => LatticeToolHandler.CurrentMirrorAxis = (LatticeToolHandler.MirrorAxis)index)));
            c.EnabledWhen(options, () => LatticeToolHandler.MirrorEditing);
            symmetry.Add(options);
            root.Add(list);
            root.Add(OverlayControls.Hint(LatticeLocalization.Tr(LocKey.ShiftClickHint)));
            return root;
        }
    }
}
#endif
