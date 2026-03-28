#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;
using Net._32Ba.LatticeDeformationTool;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    [EditorTool("Mesh Deformer", typeof(LatticeDeformer))]
    public sealed class MeshDeformerTool : EditorTool
    {
        internal enum BrushSubMode
        {
            Brush = 0,
            VertexSelection = 1
        }

        private static BrushSubMode s_brushSubMode = BrushSubMode.Brush;

        internal static BrushSubMode CurrentBrushSubMode
        {
            get => s_brushSubMode;
            set { s_brushSubMode = value; SceneView.RepaintAll(); }
        }

        private readonly BrushToolHandler _brushHandler = new BrushToolHandler();
        private readonly VertexSelectionHandler _vertexHandler = new VertexSelectionHandler();
        private readonly LatticeToolHandler _latticeHandler = new LatticeToolHandler();

        private enum ActiveHandler { None, Lattice, Brush, VertexSelection }
        private ActiveHandler _currentHandler = ActiveHandler.None;

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
            if (desired != _currentHandler)
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
            if (deformer.Layers.Count == 0)
                return ActiveHandler.None;
            if (deformer.ActiveLayerType == MeshDeformerLayerType.Lattice)
                return ActiveHandler.Lattice;
            return s_brushSubMode == BrushSubMode.VertexSelection
                ? ActiveHandler.VertexSelection
                : ActiveHandler.Brush;
        }

        private void ActivateHandler(ActiveHandler handler, LatticeDeformer deformer)
        {
            _currentHandler = handler;
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
        }
    }

    [Overlay(typeof(SceneView), k_OverlayId, k_OverlayId, defaultDisplay = true)]
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
                // Layer selector
                DrawLayerSelector(selectedDeformer);

                if (selectedDeformer.Layers.Count == 0)
                {
                    EditorGUILayout.HelpBox(
                        LatticeLocalization.Tr(LocKey.NoDeformationLayers),
                        MessageType.Info);
                    return;
                }

                GUILayout.Space(4f);

                // Sub-mode selector for brush layers
                if (selectedDeformer.ActiveLayerType == MeshDeformerLayerType.Brush)
                {
                    var subModeLabels = new GUIContent[]
                    {
                        IconContent(LocKey.Brush, "TerrainInspector.TerrainToolSplat"),
                        IconContent(LocKey.VertexSelection, "EditCollider")
                    };
                    int subMode = GUILayout.Toolbar(
                        (int)MeshDeformerTool.CurrentBrushSubMode, subModeLabels);
                    subMode = Mathf.Clamp(subMode, 0, subModeLabels.Length - 1);
                    MeshDeformerTool.CurrentBrushSubMode = (MeshDeformerTool.BrushSubMode)subMode;
                    GUILayout.Space(4f);
                }

                // Delegate to handler-specific GUI
                if (selectedDeformer.ActiveLayerType == MeshDeformerLayerType.Lattice)
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
            var layers = deformer.Layers;
            if (layers.Count == 0) return;

            var names = new string[layers.Count];
            for (int i = 0; i < layers.Count; i++)
            {
                string typeSuffix = layers[i].Type == MeshDeformerLayerType.Lattice ? " [L]" : " [B]";
                names[i] = layers[i].Name + typeSuffix;
            }

            int current = deformer.ActiveLayerIndex;
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
