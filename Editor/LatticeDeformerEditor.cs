#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    [CustomEditor(typeof(LatticeDeformer))]
    public sealed class LatticeDeformerEditor : UnityEditor.Editor
    {
        private SerializedProperty _settingsProp;
        private SerializedProperty _skinnedRendererProp;
        private SerializedProperty _meshFilterProp;
        private SerializedProperty _gridSizeProp;
        private SerializedProperty _recalcNormalsProp;
        private SerializedProperty _recalcTangentsProp;
        private SerializedProperty _recalcBoundsProp;

        private static bool s_showOptions = false;
        private static bool s_showAdvancedSettings = false;

        private void OnEnable()
        {
            _settingsProp = serializedObject.FindProperty("_settings");
            _skinnedRendererProp = serializedObject.FindProperty("_skinnedMeshRenderer");
            _meshFilterProp = serializedObject.FindProperty("_meshFilter");
            _gridSizeProp = _settingsProp.FindPropertyRelative("_gridSize");
            _recalcNormalsProp = serializedObject.FindProperty("_recalculateNormals");
            _recalcTangentsProp = serializedObject.FindProperty("_recalculateTangents");
            _recalcBoundsProp = serializedObject.FindProperty("_recalculateBounds");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(_skinnedRendererProp, new GUIContent("Target Skinned Mesh Renderer"));
                EditorGUILayout.PropertyField(_meshFilterProp, new GUIContent("Target Mesh Filter"));
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Lattice Settings", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_gridSizeProp, new GUIContent("Grid Size"));

            s_showAdvancedSettings = EditorGUILayout.Foldout(s_showAdvancedSettings, "Advanced Settings", true);
            if (s_showAdvancedSettings)
            {
                EditorGUI.indentLevel++;
                DrawSettingsExcludingGrid();
                EditorGUI.indentLevel--;
            }
            EditorGUI.indentLevel--;
            EditorGUILayout.Space();

            EditorGUILayout.Space();
            s_showOptions = EditorGUILayout.BeginFoldoutHeaderGroup(s_showOptions, "Mesh Update Options");
            if (s_showOptions)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_recalcNormalsProp);
                EditorGUILayout.PropertyField(_recalcTangentsProp);
                EditorGUILayout.PropertyField(_recalcBoundsProp);

                EditorGUILayout.Space();
                bool previewEnabled = LatticeDeformerPreviewFilter.PreviewToggleEnabled;
                string previewLabel = previewEnabled ? "(NDMF) Disable Lattice Preview" : "(NDMF) Enable Lattice Preview";
                if (GUILayout.Button(previewLabel))
                {
                    TogglePreviewForTargets(!previewEnabled);
                }

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            bool modified = serializedObject.ApplyModifiedProperties();
            if (modified)
            {
                foreach (var obj in targets)
                {
                    if (obj is LatticeDeformer instance)
                    {
                        instance.InvalidateCache();
                        instance.Deform(false);
                        EditorUtility.SetDirty(instance);
                    }
                }

                LatticePreviewUtility.RequestSceneRepaint();
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Activate Lattice Tool"))
            {
                ToolManager.SetActiveTool<LatticeDeformerTool>();
                LatticePreviewUtility.RequestSceneRepaint();
            }
        }

        private void TogglePreviewForTargets(bool enabled)
        {
            LatticeDeformerPreviewFilter.ForcePreviewState(enabled);
        }

        private void DrawSettingsExcludingGrid()
        {
            if (_settingsProp == null)
            {
                return;
            }

            var iterator = _settingsProp.Copy();
            var end = iterator.GetEndProperty();

            bool enterChildren = iterator.NextVisible(true);
            while (enterChildren && !SerializedProperty.EqualContents(iterator, end))
            {
                if (iterator.depth == _settingsProp.depth + 1 && iterator.name != "_gridSize")
                {
                    EditorGUILayout.PropertyField(iterator, includeChildren: true);
                }

                enterChildren = iterator.NextVisible(false);
            }
        }
    }
}
#endif
