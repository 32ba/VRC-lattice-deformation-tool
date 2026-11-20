#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>Purpose-based navigation and tool activation; authoring writes belong to the service.</summary>
    internal sealed class GuidedInspectorSection
    {
        private readonly UnityEditor.Editor _owner;
        private readonly Action _prepareTarget, _showDetailed, _onStarted, _onPropertyChanged;
        private readonly VisualElement _root = MdUI.Element("md-guided");
        private string _signature;
        internal bool StartFailed { get; private set; }
        internal VisualElement Root => _root;

        internal GuidedInspectorSection(UnityEditor.Editor owner, Action prepareTarget, Action showDetailed,
            Action onStarted, Action onPropertyChanged)
        {
            _owner = owner;
            _prepareTarget = prepareTarget; _showDetailed = showDetailed;
            _onStarted = onStarted; _onPropertyChanged = onPropertyChanged;
        }

        /// <summary>
        /// Rebuilds the content when what it shows has changed. It reads the target only, so it is
        /// safe to call on every Inspector refresh.
        /// </summary>
        internal void Refresh()
        {
            if (_owner == null || _owner.target == null) return;
            _prepareTarget();
            var deformer = _owner.targets.Length == 1 ? _owner.target as LatticeDeformer : null;
            var state = deformer != null ? GuidedInspectorState.Read(deformer) : default;
            string signature = string.Join("|", LatticeLocalization.CurrentLanguage, _owner.targets.Length,
                deformer != null, state.Source != null ? state.Source.GetInstanceID() : 0,
                state.Source != null ? state.Source.name : null, state.IsProfile, state.ActiveLayerName, StartFailed);
            if (signature == _signature) return;
            _signature = signature;
            Build(deformer, state);
        }

        private void Build(LatticeDeformer deformer, GuidedInspectorState state)
        {
            _root.Clear();
            _root.Add(MdUI.Text(LatticeLocalization.Tr(LocKey.GuidedQuestion), "md-text-title"));
            _root.Add(MdUI.Text(LatticeLocalization.Tr(LocKey.GuidedIntro), "md-guided__intro md-text-muted"));
            if (deformer == null)
            {
                _root.Add(MdUI.HelpBox(LatticeLocalization.Tr(LocKey.GuidedSingleSelectionRequired), MdUI.HelpKind.Warning));
                AddDetailedButton();
                return;
            }

            if (state.Source != null) _root.Add(TargetRow(state.Source));
            else
            {
                _root.Add(MdUI.HelpBox(LatticeLocalization.Tr(LocKey.GuidedNoTarget), MdUI.HelpKind.Warning));
                _root.Add(SourceField("_skinnedMeshRenderer", LocKey.SkinnedMeshSource));
                _root.Add(SourceField("_meshFilter", LocKey.StaticMeshSource));
            }

            if (state.IsProfile)
            {
                _root.Add(MdUI.HelpBox(LatticeLocalization.Tr(LocKey.GuidedProfileReadOnly), MdUI.HelpKind.Warning));
                AddDetailedButton();
                return;
            }

            var cards = MdUI.Element("md-guided__cards");
            cards.Add(Card("lattice", "lattice", LocKey.GuidedAdjustShape, LocKey.GuidedAdjustShapeDescription,
                LocKey.GuidedAdjustShapeExample, GuidedEditingIntent.AdjustShape));
            cards.Add(Card("brush", "brush", LocKey.GuidedSculptSurface, LocKey.GuidedSculptSurfaceDescription,
                LocKey.GuidedSculptSurfaceExample, GuidedEditingIntent.SculptSurface));
            cards.Add(Card("vertex", "vertex", LocKey.GuidedMoveVertices, LocKey.GuidedMoveVerticesDescription,
                LocKey.GuidedMoveVerticesExample, GuidedEditingIntent.MoveVertices));
            cards.SetEnabled(state.Source != null);
            _root.Add(cards);

            if (state.ActiveLayerName != null)
                _root.Add(MdUI.Text(string.Format(LatticeLocalization.Tr(LocKey.GuidedCurrentEdit), state.ActiveLayerName),
                    "md-text-small"));
            if (StartFailed)
                _root.Add(MdUI.HelpBox(LatticeLocalization.Tr(LocKey.GuidedUnableToStart), MdUI.HelpKind.Warning));
        }

        private VisualElement TargetRow(Mesh source)
        {
            var value = new Button(() => EditorGUIUtility.PingObject(source)) { text = string.Empty };
            value.AddToClassList("md-object-field");
            value.AddToClassList("md-guided__target");
            value.Add(MdUI.Icon("cube", "md-icon--small md-icon--muted"));
            value.Add(new Label(source.name) { pickingMode = PickingMode.Ignore });
            return MdUI.FieldRow(LatticeLocalization.Tr(LocKey.GuidedTarget), value);
        }

        private VisualElement SourceField(string propertyName, string labelKey)
        {
            var property = _owner.serializedObject.FindProperty(propertyName);
            var field = new PropertyField(property, LatticeLocalization.Tr(labelKey));
            field.Bind(_owner.serializedObject);
            field.RegisterValueChangeCallback(_ => _onPropertyChanged());
            return field;
        }

        private Button Card(string mode, string icon, string titleKey, string descriptionKey, string exampleKey,
            GuidedEditingIntent intent)
        {
            var card = MdUI.ActionCard(mode, icon, LatticeLocalization.Tr(titleKey),
                LatticeLocalization.Tr(descriptionKey), LatticeLocalization.Tr(exampleKey), () =>
                {
                    StartEditing(intent);
                    Refresh();
                });
            card.name = "guided-" + mode;
            return card;
        }

        private void AddDetailedButton() =>
            _root.Add(MdUI.Button(LatticeLocalization.Tr(LocKey.OpenDetailedInspector), _showDetailed));

        internal bool StartEditing(GuidedEditingIntent intent)
        {
            if (_owner == null || _owner.target == null || _owner.targets.Length != 1 ||
                _owner.target is not LatticeDeformer deformer ||
                (intent != GuidedEditingIntent.AdjustShape && intent != GuidedEditingIntent.SculptSurface &&
                 intent != GuidedEditingIntent.MoveVertices))
            { StartFailed = true; return false; }

            _owner.serializedObject.ApplyModifiedProperties();
            MeshDeformerLayerType type = intent == GuidedEditingIntent.AdjustShape
                ? MeshDeformerLayerType.Lattice : MeshDeformerLayerType.Brush;
            int index = GuidedAuthoringService.EnsureLayer(deformer, type, LatticeLocalization.Tr(LocKey.MeshDeformer));
            _owner.serializedObject.Update();
            if (index < 0) { StartFailed = true; return false; }

            StartFailed = false;
            MeshDeformerTool.CurrentBrushSubMode = intent == GuidedEditingIntent.MoveVertices
                ? MeshDeformerTool.BrushSubMode.VertexSelection : MeshDeformerTool.BrushSubMode.Brush;
            MeshDeformerTool.UseSimpleOverlay(deformer);
            LatticeDeformerPreviewFilter.ForcePreviewState(true);
            _onStarted();
            ToolManager.SetActiveTool<MeshDeformerTool>();
            LatticePreviewUtility.RequestSceneRepaint();
            SceneView.RepaintAll();
            return true;
        }
    }
}
#endif
