#if UNITY_EDITOR
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class BrushToolOverlay
    {
        private static bool s_showMirrorSection;
        private static bool s_showAdvancedSection;
        private static bool s_showVisualizationSection;

        internal static readonly string[] ModeIcons = { "op-reshape", "op-shift", "op-smooth" };

        /// <summary>Detailed overlay content for brush layers. Building it never edits the deformer.</summary>
        internal static VisualElement Create(LatticeDeformer deformer, OverlayControls c)
        {
            var root = MdUI.Element("md-overlay__content");
            root.Add(c.Segmented(OverlayControls.Tr(LocKey.Normal, LocKey.Move, LocKey.Smooth),
                () => Mathf.Min((int)BrushToolHandler.CurrentBrushMode, 2),
                index => BrushToolHandler.CurrentBrushMode = (BrushToolHandler.BrushMode)index,
                ModeIcons, strong: true, tooltips: OverlayControls.Tooltips(LocKey.Normal, LocKey.Move, LocKey.Smooth)));

            // Radius is world-space meters, shown in cm. Strength is 0-1, shown in %.
            root.Add(c.Slider(LatticeLocalization.Tr(LocKey.BrushRadius) + " (cm)", 0f, 10f,
                () => BrushToolHandler.BrushRadius * 100f, value => BrushToolHandler.BrushRadius = value / 100f,
                LatticeLocalization.Tooltip(LocKey.BrushRadius)));
            root.Add(c.Slider(LatticeLocalization.Tr(LocKey.BrushStrength) + " (%)", 0f, 100f,
                () => BrushToolHandler.BrushStrength * 100f, value => BrushToolHandler.BrushStrength = value / 100f,
                LatticeLocalization.Tooltip(LocKey.BrushStrength)));
            root.Add(c.Popup(LatticeLocalization.Tr(LocKey.BrushFalloff),
                OverlayControls.Tr(LocKey.Smooth, LocKey.Linear, LocKey.Constant, LocKey.Sphere, LocKey.Gaussian),
                () => (int)BrushToolHandler.BrushFalloff,
                index => BrushToolHandler.BrushFalloff = (BrushFalloffType)index,
                LatticeLocalization.Tooltip(LocKey.BrushFalloff)));
            root.Add(c.Switch(LatticeLocalization.Tr(LocKey.InvertBrush),
                () => BrushToolHandler.InvertBrush, value => BrushToolHandler.InvertBrush = value));
            root.Add(c.Switch(LatticeLocalization.Tr(LocKey.BackfaceCulling),
                () => BrushToolHandler.BackfaceCulling, value => BrushToolHandler.BackfaceCulling = value));

            var list = MdUI.Element("md-foldout-list");
            list.Add(MdUI.Foldout(LatticeLocalization.Tr(LocKey.Advanced),
                () => s_showAdvancedSection, open => s_showAdvancedSection = open, out var advanced));
            advanced.Add(c.Switch(LatticeLocalization.Tr(LocKey.ConnectedOnly),
                () => BrushToolHandler.ConnectedOnly, value => BrushToolHandler.ConnectedOnly = value));
            advanced.Add(c.Switch(LatticeLocalization.Tr(LocKey.SurfaceDistance),
                () => BrushToolHandler.UseSurfaceDistance, value => BrushToolHandler.UseSurfaceDistance = value));

            list.Add(MdUI.Foldout(LatticeLocalization.Tr(LocKey.EnableMirror),
                () => s_showMirrorSection, open => s_showMirrorSection = open, out var mirror));
            mirror.Add(c.Switch(LatticeLocalization.Tr(LocKey.EnableMirror),
                () => BrushToolHandler.MirrorEditing, value => BrushToolHandler.MirrorEditing = value));
            var axis = OverlayControls.Group(LatticeLocalization.Tr(LocKey.MirrorAxis),
                c.Segmented(OverlayControls.Texts(BrushToolHandler.AxisOptions),
                    () => (int)BrushToolHandler.CurrentMirrorAxis,
                    index => BrushToolHandler.CurrentMirrorAxis = (BrushToolHandler.MirrorAxis)index));
            c.EnabledWhen(axis, () => BrushToolHandler.MirrorEditing);
            mirror.Add(axis);

            list.Add(MdUI.Foldout(LatticeLocalization.Tr(LocKey.Visualization),
                () => s_showVisualizationSection, open => s_showVisualizationSection = open, out var visualization));
            visualization.Add(c.Switch(LatticeLocalization.Tr(LocKey.ShowWireframe),
                () => BrushToolHandler.ShowWireframe, value => BrushToolHandler.ShowWireframe = value));
            visualization.Add(c.Switch(LatticeLocalization.Tr(LocKey.ShowAffectedVertices),
                () => BrushToolHandler.ShowAffectedVertices, value => BrushToolHandler.ShowAffectedVertices = value));
            visualization.Add(c.Switch(LatticeLocalization.Tr(LocKey.ShowDisplacementHeatmap),
                () => BrushToolHandler.ShowDisplacementHeatmap, value => BrushToolHandler.ShowDisplacementHeatmap = value));
            visualization.Add(c.Slider(LatticeLocalization.Tr(LocKey.DotSize), 1f, 8f,
                () => BrushToolHandler.VertexDotSize, value => BrushToolHandler.VertexDotSize = value,
                LatticeLocalization.Tooltip(LocKey.DotSize)));
            visualization.Add(c.Switch(LatticeLocalization.Tr(LocKey.ShowPenetration),
                () => BrushToolHandler.ShowPenetration, value => BrushToolHandler.ShowPenetration = value));
            var reference = new ObjectField(LatticeLocalization.Tr(LocKey.ReferenceMesh))
                { objectType = typeof(Renderer), allowSceneObjects = true, name = "penetration-reference",
                  tooltip = LatticeLocalization.Tooltip(LocKey.ReferenceMesh) };
            reference.SetValueWithoutNotify(BrushToolHandler.PenetrationReference);
            reference.RegisterValueChangedCallback(evt => BrushToolHandler.PenetrationReference = evt.newValue as Renderer);
            c.Track(() =>
            {
                if (reference.value != BrushToolHandler.PenetrationReference)
                    reference.SetValueWithoutNotify(BrushToolHandler.PenetrationReference);
            });
            c.VisibleWhen(reference, () => BrushToolHandler.ShowPenetration);
            visualization.Add(reference);
            root.Add(list);

            root.Add(MdUI.Button(LatticeLocalization.Tr(LocKey.ClearAll), () =>
            {
                if (deformer != null && deformer.ActiveLayerType == MeshDeformerLayerType.Brush)
                    BrushToolHandler.ClearAllDisplacements(deformer);
            }, "md-button--small md-button--danger"));
            return root;
        }
    }
}
#endif
