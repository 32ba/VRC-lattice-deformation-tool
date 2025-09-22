#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEditor.EditorTools;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    [CustomEditor(typeof(LatticeDeformer))]
    public sealed class LatticeDeformerEditor : UnityEditor.Editor
    {
        private SerializedProperty _settingsProp;
        private SerializedProperty _skinnedRendererProp;
        private SerializedProperty _meshFilterProp;
        private SerializedProperty _deformOnEnableProp;
        private SerializedProperty _recalcNormalsProp;
        private SerializedProperty _recalcTangentsProp;
        private SerializedProperty _recalcBoundsProp;
        private SerializedProperty _autoInitializeProp;

        private void OnEnable()
        {
            _settingsProp = serializedObject.FindProperty("_settings");
            _skinnedRendererProp = serializedObject.FindProperty("_skinnedMeshRenderer");
            _meshFilterProp = serializedObject.FindProperty("_meshFilter");
            _deformOnEnableProp = serializedObject.FindProperty("_deformOnEnable");
            _recalcNormalsProp = serializedObject.FindProperty("_recalculateNormals");
            _recalcTangentsProp = serializedObject.FindProperty("_recalculateTangents");
            _recalcBoundsProp = serializedObject.FindProperty("_recalculateBounds");
            _autoInitializeProp = serializedObject.FindProperty("_autoInitializeFromSource");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_settingsProp, new GUIContent("Lattice Settings"), includeChildren: true);
            EditorGUILayout.PropertyField(_skinnedRendererProp);
            EditorGUILayout.PropertyField(_meshFilterProp);
            EditorGUILayout.PropertyField(_deformOnEnableProp);
            EditorGUILayout.PropertyField(_recalcNormalsProp);
            EditorGUILayout.PropertyField(_recalcTangentsProp);
            EditorGUILayout.PropertyField(_recalcBoundsProp);
            EditorGUILayout.PropertyField(_autoInitializeProp);

            serializedObject.ApplyModifiedProperties();

            var deformer = (LatticeDeformer)target;
            var settings = deformer.Settings;

            if (settings != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Grid Size", settings.GridSize.ToString());
                EditorGUILayout.LabelField("Control Points", settings.ControlPointCount.ToString());
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Deform Mesh"))
                {
                    foreach (var obj in targets)
                    {
                        var instance = (LatticeDeformer)obj;
                        instance.Deform();
                        EditorUtility.SetDirty(instance);
                    }

                    SceneView.RepaintAll();
                }

                if (GUILayout.Button("Restore Mesh"))
                {
                    foreach (var obj in targets)
                    {
                        var instance = (LatticeDeformer)obj;
                        instance.RestoreOriginalMesh();
                        instance.InvalidateCache();
                        EditorUtility.SetDirty(instance);
                    }

                    SceneView.RepaintAll();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Activate Lattice Tool"))
                {
                    ToolManager.SetActiveTool<LatticeTool>();
                    SceneView.RepaintAll();
                }

                if (GUILayout.Button("Rebuild Cache"))
                {
                    foreach (var obj in targets)
                    {
                        var instance = (LatticeDeformer)obj;
                        instance.InvalidateCache();
                        instance.Deform();
                        EditorUtility.SetDirty(instance);
                    }

                    SceneView.RepaintAll();
                }
            }
        }
    }
}
#endif
