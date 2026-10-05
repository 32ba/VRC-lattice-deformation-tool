#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>Purpose-based navigation and tool activation; authoring writes belong to the service.</summary>
    internal sealed class GuidedInspectorSection
    {
        private readonly UnityEditor.Editor _owner;
        private readonly Action _prepareTarget, _showDetailed, _onStarted, _onPropertyChanged;
        private readonly SerializedProperty _skinnedRenderer, _meshFilter;
        internal bool StartFailed { get; private set; }

        internal GuidedInspectorSection(UnityEditor.Editor owner, Action prepareTarget, Action showDetailed,
            Action onStarted, Action onPropertyChanged)
        {
            _owner = owner;
            _prepareTarget = prepareTarget; _showDetailed = showDetailed;
            _onStarted = onStarted; _onPropertyChanged = onPropertyChanged;
            _skinnedRenderer = owner.serializedObject.FindProperty("_skinnedMeshRenderer");
            _meshFilter = owner.serializedObject.FindProperty("_meshFilter");
        }

        internal void Draw()
        {
            if (_owner == null || _owner.target == null) return;
            _prepareTarget();
            _owner.serializedObject.Update();
            EditorGUILayout.LabelField(LatticeLocalization.Tr(LocKey.GuidedQuestion), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(LatticeLocalization.Tr(LocKey.GuidedIntro), MessageType.Info);
            if (_owner.targets.Length != 1 || _owner.target is not LatticeDeformer deformer)
            {
                EditorGUILayout.HelpBox(LatticeLocalization.Tr(LocKey.GuidedSingleSelectionRequired), MessageType.Warning);
                DrawDetailedButton();
                return;
            }

            var state = GuidedInspectorState.Read(deformer);
            if (state.Source != null)
                EditorGUILayout.LabelField(LatticeLocalization.Tr(LocKey.GuidedTarget), state.Source.name);
            else
            {
                EditorGUILayout.HelpBox(LatticeLocalization.Tr(LocKey.GuidedNoTarget), MessageType.Warning);
                EditorGUILayout.PropertyField(_skinnedRenderer, LatticeLocalization.Content(LocKey.SkinnedMeshSource));
                EditorGUILayout.PropertyField(_meshFilter, LatticeLocalization.Content(LocKey.StaticMeshSource));
            }
            if (state.IsProfile)
            {
                EditorGUILayout.HelpBox(LatticeLocalization.Tr(LocKey.GuidedProfileReadOnly), MessageType.Warning);
                DrawDetailedButton();
                return;
            }
            if (state.ActiveLayerName != null)
                EditorGUILayout.LabelField(string.Format(LatticeLocalization.Tr(LocKey.GuidedCurrentEdit),
                    state.ActiveLayerName), EditorStyles.miniLabel);

            using (new EditorGUI.DisabledScope(state.Source == null))
            {
                DrawAction(LocKey.GuidedAdjustShape, LocKey.GuidedAdjustShapeDescription, GuidedEditingIntent.AdjustShape);
                DrawAction(LocKey.GuidedSculptSurface, LocKey.GuidedSculptSurfaceDescription, GuidedEditingIntent.SculptSurface);
                DrawAction(LocKey.GuidedMoveVertices, LocKey.GuidedMoveVerticesDescription, GuidedEditingIntent.MoveVertices);
            }
            EditorGUILayout.HelpBox(LatticeLocalization.Tr(LocKey.GuidedPreviewNote), MessageType.None);
            if (StartFailed)
                EditorGUILayout.HelpBox(LatticeLocalization.Tr(LocKey.GuidedUnableToStart), MessageType.Warning);
            DrawDetailedButton();
            if (_owner.serializedObject.ApplyModifiedProperties()) _onPropertyChanged();
        }

        private void DrawAction(string label, string descriptionKey, GuidedEditingIntent intent)
        {
            string description = LatticeLocalization.Tr(descriptionKey);
            if (GUILayout.Button(new GUIContent(LatticeLocalization.Tr(label), description), GUILayout.Height(34f)))
                StartEditing(intent);
            GUILayout.Label(description, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4f);
        }

        private void DrawDetailedButton()
        {
            EditorGUILayout.Space(4f);
            if (GUILayout.Button(LatticeLocalization.Tr(LocKey.OpenDetailedInspector))) _showDetailed();
        }

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
