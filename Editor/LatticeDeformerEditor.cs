#if UNITY_EDITOR
using System.Collections.Generic;
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
        private SerializedProperty _recalcNormalsProp;
        private SerializedProperty _recalcTangentsProp;
        private SerializedProperty _recalcBoundsProp;

        private static bool s_showOptions = false;
        private static bool s_showAdvancedSettings = false;
        private static readonly Dictionary<int, Vector3Int> s_pendingGridSizes = new();

        private void OnEnable()
        {
            _settingsProp = serializedObject.FindProperty("_settings");
            _skinnedRendererProp = serializedObject.FindProperty("_skinnedMeshRenderer");
            _meshFilterProp = serializedObject.FindProperty("_meshFilter");
            _recalcNormalsProp = serializedObject.FindProperty("_recalculateNormals");
            _recalcTangentsProp = serializedObject.FindProperty("_recalculateTangents");
            _recalcBoundsProp = serializedObject.FindProperty("_recalculateBounds");

            InitializePendingGridSizes();
        }

        private void OnDisable()
        {
            foreach (var obj in targets)
            {
                if (obj is LatticeDeformer deformer)
                {
                    s_pendingGridSizes.Remove(deformer.GetInstanceID());
                }
            }
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
            DrawGridSizeControls((LatticeDeformer)target);

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

        private void InitializePendingGridSizes()
        {
            foreach (var obj in targets)
            {
                if (obj is not LatticeDeformer deformer)
                {
                    continue;
                }

                var settings = deformer.Settings;
                if (settings == null)
                {
                    continue;
                }

                s_pendingGridSizes[deformer.GetInstanceID()] = settings.GridSize;
            }
        }

        private static void ApplyGridSizeChange(LatticeDeformer deformer, Vector3Int newSize)
        {
            if (deformer == null)
            {
                return;
            }

            var settings = deformer.Settings;
            if (settings == null)
            {
                return;
            }

            newSize.x = Mathf.Max(2, newSize.x);
            newSize.y = Mathf.Max(2, newSize.y);
            newSize.z = Mathf.Max(2, newSize.z);

            Undo.RecordObject(deformer, "Change Lattice Grid Size");
            settings.ResizeGrid(newSize);
            deformer.InvalidateCache();
            deformer.Deform(false);
            EditorUtility.SetDirty(deformer);

            LatticePreviewUtility.RequestSceneRepaint();
            SceneView.RepaintAll();
        }

        private void DrawGridSizeControls(LatticeDeformer deformer)
        {
            if (deformer == null)
            {
                EditorGUILayout.HelpBox("No Lattice Deformer selected.", MessageType.Info);
                return;
            }

            var settings = deformer.Settings;
            if (settings == null)
            {
                EditorGUILayout.HelpBox("Missing Lattice Asset on this deformer.", MessageType.Warning);
                return;
            }

            int id = deformer.GetInstanceID();
            if (!s_pendingGridSizes.TryGetValue(id, out var pending))
            {
                pending = settings.GridSize;
                s_pendingGridSizes[id] = pending;
            }

            EditorGUILayout.LabelField("Current Grid Size", settings.GridSize.ToString());
            EditorGUI.BeginChangeCheck();
            pending = EditorGUILayout.Vector3IntField("Pending Grid Size", pending);
            if (EditorGUI.EndChangeCheck())
            {
                pending.x = Mathf.Max(2, pending.x);
                pending.y = Mathf.Max(2, pending.y);
                pending.z = Mathf.Max(2, pending.z);
                s_pendingGridSizes[id] = pending;
            }

            bool hasPendingChange = s_pendingGridSizes[id] != settings.GridSize;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!hasPendingChange))
                {
                    if (GUILayout.Button("Apply", GUILayout.Width(80f)))
                    {
                        foreach (var obj in targets)
                        {
                            if (obj is LatticeDeformer selected)
                            {
                                if (!s_pendingGridSizes.TryGetValue(selected.GetInstanceID(), out var pendingSize))
                                {
                                    pendingSize = selected.Settings?.GridSize ?? pending;
                                }

                                ApplyGridSizeChange(selected, pendingSize);
                                s_pendingGridSizes[selected.GetInstanceID()] = selected.Settings?.GridSize ?? pendingSize;
                            }
                        }
                    }

                    if (GUILayout.Button("Revert", GUILayout.Width(80f)))
                    {
                        foreach (var obj in targets)
                        {
                            if (obj is LatticeDeformer selected && selected.Settings != null)
                            {
                                s_pendingGridSizes[selected.GetInstanceID()] = selected.Settings.GridSize;
                            }
                        }
                    }
                }
            }

            EditorGUILayout.Space();
        }
    }
}
#endif
