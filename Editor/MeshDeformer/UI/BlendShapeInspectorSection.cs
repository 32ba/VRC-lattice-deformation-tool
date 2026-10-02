#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>Owns BlendShape settings, import menus and temporary test display.</summary>
    internal sealed class BlendShapeInspectorSection : IDisposable
    {
        private readonly UnityEditor.Editor _owner;
        private readonly Action _onPropertyChanges, _onImported;
        private readonly SerializedProperty _groupsProp, _activeGroupIndexProp;
        private BlendShapeTestSession _blendShapeTestSession;
        private bool _disposed;
        private UnityEngine.Object target => _owner != null ? _owner.target : null;
        private SerializedObject serializedObject => _owner.serializedObject;

        internal BlendShapeInspectorSection(UnityEditor.Editor owner, Action onPropertyChanges, Action onImported)
        {
            _owner = owner; _onPropertyChanges = onPropertyChanges; _onImported = onImported;
            _groupsProp = serializedObject.FindProperty("_groups");
            _activeGroupIndexProp = serializedObject.FindProperty("_activeGroupIndex");
        }

        private bool CanDrawGroup(int index)
        {
            if (_disposed || target is not LatticeDeformer d || d == null) return false;
            var raw = SerializedDeformerReader.Read(d);
            return raw.DataSource == DeformerDataSource.Embedded && raw.EmbeddedGroups != null &&
                index >= 0 && index < raw.EmbeddedGroups.Count && raw.EmbeddedGroups[index] != null;
        }

        private bool TryGetActiveLayers(out SerializedProperty layers, out SerializedProperty active)
        {
            layers = null; active = null;
            if (_disposed || target is not LatticeDeformer d || d == null) return false;
            var raw = SerializedDeformerReader.Read(d);
            if (!CanDrawGroup(raw.ActiveGroupIndex) || raw.ActiveLayers == null ||
                raw.ActiveLayerIndex < 0 || raw.ActiveLayerIndex >= raw.ActiveLayers.Count ||
                raw.ActiveLayers[raw.ActiveLayerIndex] == null) return false;
            var group = _groupsProp.GetArrayElementAtIndex(raw.ActiveGroupIndex);
            layers = group.FindPropertyRelative("_layers");
            active = group.FindPropertyRelative("_activeLayerIndex");
            return layers != null && active != null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ExitTestMode();
        }

        internal void DrawGroup(int groupIndex)
        {
            // This runs from an IMGUIContainer inside a list item, which can repaint
            // once more after the inspected object was destroyed.
            if (!CanDrawGroup(groupIndex)) return;

            serializedObject.Update();
            if (_groupsProp == null || groupIndex < 0 || groupIndex >= _groupsProp.arraySize) return;

            var groupProp = _groupsProp.GetArrayElementAtIndex(groupIndex);
            var outputProp = groupProp.FindPropertyRelative("_blendShapeOutput");
            var nameProp = groupProp.FindPropertyRelative("_blendShapeName");
            var curveProp = groupProp.FindPropertyRelative("_blendShapeCurve");
            var compositionProp = groupProp.FindPropertyRelative("_blendShapeComposition");
            if (outputProp == null) return;

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(outputProp, new GUIContent(LatticeLocalization.Tr(LocKey.BlendShapeOutput)));
            bool modeJustChanged = EditorGUI.EndChangeCheck();

            if (outputProp.intValue == (int)BlendShapeOutputMode.OutputAsBlendShape)
            {
                if (modeJustChanged && nameProp != null && string.IsNullOrWhiteSpace(nameProp.stringValue))
                {
                    var deformer = target as LatticeDeformer;
                    if (deformer != null)
                        nameProp.stringValue = deformer.gameObject.name;
                }

                if (nameProp != null)
                    EditorGUILayout.PropertyField(nameProp, new GUIContent(LatticeLocalization.Tr(LocKey.BlendShapeName)));

                if (curveProp != null)
                    EditorGUILayout.PropertyField(curveProp, new GUIContent(LatticeLocalization.Tr(LocKey.Curve)));

                if (LatticeDeformationFeatureFlags.AdvancedBlendShapes && compositionProp != null)
                {
                    var compositionOptions = new[]
                    {
                        LatticeLocalization.Content(LocKey.BlendShapeCompositionSingle),
                        LatticeLocalization.Content(LocKey.BlendShapeCompositionProgressive),
                        LatticeLocalization.Content(LocKey.BlendShapeCompositionCrossfade)
                    };
                    EditorGUI.BeginChangeCheck();
                    int composition = EditorGUILayout.Popup(
                        LatticeLocalization.Content(LocKey.BlendShapeComposition),
                        compositionProp.enumValueIndex,
                        compositionOptions);
                    if (EditorGUI.EndChangeCheck()) compositionProp.enumValueIndex = composition;
                }

                // Test mode only for active group
                int activeGroupIdx = _activeGroupIndexProp != null ? _activeGroupIndexProp.intValue : 0;
                if (groupIndex == activeGroupIdx)
                    DrawTestMode();
            }

            if (serializedObject.ApplyModifiedProperties())
                _onPropertyChanges();
        }

        internal void DrawLayer()
        {
            if (!TryGetActiveLayers(out var layersProp, out var activeLayerIndexProp))
            {
                return;
            }

            int activeLayerIndex = activeLayerIndexProp.intValue;
            var layerProp = layersProp.GetArrayElementAtIndex(activeLayerIndex);
            if (layerProp == null)
            {
                return;
            }

            var outputProp = layerProp.FindPropertyRelative("_blendShapeOutput");
            var nameProp = layerProp.FindPropertyRelative("_blendShapeName");
            var curveProp = layerProp.FindPropertyRelative("_blendShapeCurve");
            if (outputProp == null)
            {
                return;
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(LatticeLocalization.Tr(LocKey.BlendShapeOutput), EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(outputProp, LatticeLocalization.Content(LocKey.BlendShapeOutput));

            if (outputProp.intValue == (int)BlendShapeOutputMode.OutputAsBlendShape)
            {
                if (nameProp != null)
                {
                    EditorGUILayout.PropertyField(nameProp, LatticeLocalization.Content(LocKey.BlendShapeName));
                }

                if (curveProp != null)
                {
                    EditorGUILayout.PropertyField(curveProp, LatticeLocalization.Content(LocKey.Curve));
                }
            }
        }

        private void DrawTestMode()
        {
            var deformer = target as LatticeDeformer;
            if (deformer == null) return;

            var smr = deformer.GetComponent<SkinnedMeshRenderer>();
            if (smr == null) return;

            EditorGUILayout.Space(2);

            if (_blendShapeTestSession == null || !_blendShapeTestSession.IsActive)
            {
                if (GUILayout.Button(LatticeLocalization.Tr(LocKey.EnterTestMode)))
                {
                    EnterTestMode(deformer, smr);
                }
            }
            else
            {
                EditorGUILayout.HelpBox(LatticeLocalization.Tr(LocKey.BlendShapeTestMode), MessageType.Info);

                EditorGUI.BeginChangeCheck();
                float weight = EditorGUILayout.Slider(
                    new GUIContent(LatticeLocalization.Tr(LocKey.TestWeight)),
                    _blendShapeTestSession.Weight, 0f, 100f);
                if (EditorGUI.EndChangeCheck())
                {
                    _blendShapeTestSession.SetWeight(weight);
                    SceneView.RepaintAll();
                }

                if (GUILayout.Button(LatticeLocalization.Tr(LocKey.ExitTestMode)))
                {
                    ExitTestMode();
                }
            }
        }

        internal void EnterTestMode(LatticeDeformer deformer, SkinnedMeshRenderer smr)
        {
            if (_disposed || target != deformer) return;
            _blendShapeTestSession?.Dispose();
            _blendShapeTestSession = BlendShapeTestSession.TryBegin(deformer, smr);
            SceneView.RepaintAll();
        }

        internal void ExitTestMode()
        {
            _blendShapeTestSession?.Dispose();
            _blendShapeTestSession = null;
            SceneView.RepaintAll();
        }

        internal void Refresh(LatticeDeformer deformer)
        {
            if (_blendShapeTestSession != null && _blendShapeTestSession.Owns(deformer))
                _blendShapeTestSession.Refresh();
        }

        internal void DrawImport()
        {
            if (_disposed || target is not LatticeDeformer deformer || deformer == null) return;
            var source = SerializedDeformerReader.Read(deformer).SourceMesh;
            if (source == null || source.blendShapeCount == 0) return;
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(LatticeLocalization.Tr(LocKey.ImportBlendShape));
            if (EditorGUILayout.DropdownButton(new GUIContent(LatticeLocalization.Tr(LocKey.Select)), FocusType.Keyboard))
            {
                var snapshot = BlendShapeImportMenu.TryCreate(deformer);
                if (snapshot != null)
                {
                    var menu = new GenericMenu();
                    for (int i = 0; i < snapshot.Count; i++)
                    {
                        string name = snapshot.GetName(i);
                        if (LatticeDeformationFeatureFlags.AdvancedBlendShapes)
                        {
                            menu.AddItem(new GUIContent(LatticeLocalization.Tr(LocKey.ImportBlendShapeSingleFrame) + "/" + name),
                                false, CreateImportAction(snapshot, i, false));
                            menu.AddItem(new GUIContent(LatticeLocalization.Tr(LocKey.ImportBlendShapeAllFrames) + "/" + name),
                                false, CreateImportAction(snapshot, i, true));
                        }
                        else menu.AddItem(new GUIContent(name), false, CreateImportAction(snapshot, i, false));
                    }
                    menu.ShowAsContext();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        internal GenericMenu.MenuFunction CreateImportAction(BlendShapeImportMenu menu, int shape, bool allFrames)
        {
            return () =>
            {
                if (_disposed || menu == null || target == null || target != menu.Owner ||
                    !menu.Import(shape, allFrames, "Import BlendShape")) return;
                serializedObject.Update();
                _onImported();
            };
        }

    }
}
#endif
