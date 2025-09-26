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

            AutoAssignLocalRendererReferences();
            InitializePendingGridSizes();

            LatticeLocalization.LanguageChanged += Repaint;
        }

        private void OnDisable()
        {
            LatticeLocalization.LanguageChanged -= Repaint;

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
            AutoAssignLocalRendererReferences();
            serializedObject.Update();

            bool hasSkinnedAssigned = _skinnedRendererProp != null && !_skinnedRendererProp.hasMultipleDifferentValues && _skinnedRendererProp.objectReferenceValue != null;
            bool hasMeshAssigned = _meshFilterProp != null && !_meshFilterProp.hasMultipleDifferentValues && _meshFilterProp.objectReferenceValue != null;

            bool disableSkinnedField = ShouldDisableRendererField<SkinnedMeshRenderer>() || hasMeshAssigned;
            bool disableMeshField = ShouldDisableRendererField<MeshFilter>() || hasSkinnedAssigned;

            DrawLanguageSelector();
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(disableSkinnedField))
            {
                EditorGUILayout.PropertyField(_skinnedRendererProp, LatticeLocalization.Content("Skinned Mesh Source"));
            }

            using (new EditorGUI.DisabledScope(disableMeshField))
            {
                EditorGUILayout.PropertyField(_meshFilterProp, LatticeLocalization.Content("Static Mesh Source"));
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(LatticeLocalization.Content("Lattice Cage Settings"), EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            DrawGridSizeControls((LatticeDeformer)target);
            DrawResetLatticeBoxControls();

            s_showAdvancedSettings = EditorGUILayout.Foldout(s_showAdvancedSettings, LatticeLocalization.Tr("Advanced Cage Settings"), true);
            if (s_showAdvancedSettings)
            {
                EditorGUI.indentLevel++;
                DrawSettingsExcludingGrid();
                EditorGUI.indentLevel--;
            }
            EditorGUI.indentLevel--;
            EditorGUILayout.Space();

            EditorGUILayout.Space();
            s_showOptions = EditorGUILayout.BeginFoldoutHeaderGroup(s_showOptions, LatticeLocalization.Tr("Mesh Rebuild Options"));
            if (s_showOptions)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_recalcNormalsProp);
                EditorGUILayout.PropertyField(_recalcTangentsProp);
                EditorGUILayout.PropertyField(_recalcBoundsProp);

                EditorGUILayout.Space();
                bool previewEnabled = LatticeDeformerPreviewFilter.PreviewToggleEnabled;
                string previewLabel = previewEnabled
                    ? LatticeLocalization.Tr("(NDMF) Disable Lattice Preview")
                    : LatticeLocalization.Tr("(NDMF) Enable Lattice Preview");
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
                bool assignRuntimeMesh = LatticePreviewUtility.ShouldAssignRuntimeMesh();

                foreach (var obj in targets)
                {
                    if (obj is LatticeDeformer instance)
                    {
                        instance.InvalidateCache();
                        instance.Deform(assignRuntimeMesh);
                        EditorUtility.SetDirty(instance);
                    }
                }

                LatticePreviewUtility.RequestSceneRepaint();
            }

            EditorGUILayout.Space();
            if (GUILayout.Button(LatticeLocalization.Tr("Open Lattice Editor")))
            {
                ToolManager.SetActiveTool<LatticeDeformerTool>();
                LatticePreviewUtility.RequestSceneRepaint();
            }
        }

        private void AutoAssignLocalRendererReferences()
        {
            if (targets == null || targets.Length == 0)
            {
                return;
            }

            foreach (var obj in targets)
            {
                if (obj is not LatticeDeformer deformer)
                {
                    continue;
                }

                var localSkinned = deformer.GetComponent<SkinnedMeshRenderer>();
                var localMesh = deformer.GetComponent<MeshFilter>();

                if (localSkinned == null && localMesh == null)
                {
                    continue;
                }

                var serialized = new SerializedObject(deformer);
                serialized.UpdateIfRequiredOrScript();

                var skinnedProp = serialized.FindProperty("_skinnedMeshRenderer");
                var meshProp = serialized.FindProperty("_meshFilter");

                bool changed = false;

                if (localSkinned != null)
                {
                    if (skinnedProp != null && skinnedProp.objectReferenceValue != localSkinned)
                    {
                        skinnedProp.objectReferenceValue = localSkinned;
                        changed = true;
                    }

                    if (meshProp != null && meshProp.objectReferenceValue != null)
                    {
                        meshProp.objectReferenceValue = null;
                        changed = true;
                    }
                }
                else if (localMesh != null)
                {
                    if (meshProp != null && meshProp.objectReferenceValue != localMesh)
                    {
                        meshProp.objectReferenceValue = localMesh;
                        changed = true;
                    }

                    if (skinnedProp != null && skinnedProp.objectReferenceValue != null)
                    {
                        skinnedProp.objectReferenceValue = null;
                        changed = true;
                    }
                }

                if (changed)
                {
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        private bool ShouldDisableRendererField<T>() where T : Component
        {
            if (targets == null || targets.Length == 0)
            {
                return false;
            }

            if (targets.Length == 1)
            {
                if (target is not LatticeDeformer single)
                {
                    return false;
                }

                return single.GetComponent<T>() != null;
            }

            foreach (var obj in targets)
            {
                if (obj is not LatticeDeformer deformer || deformer.GetComponent<T>() == null)
                {
                    return false;
                }
            }

            return true;
        }

        private void DrawResetLatticeBoxControls()
        {
            bool canReset = false;

            foreach (var obj in targets)
            {
                if (obj is LatticeDeformer deformer && HasResettableBounds(deformer))
                {
                    canReset = true;
                    break;
                }
            }

            using (new EditorGUI.DisabledScope(!canReset))
            {
                if (GUILayout.Button(LatticeLocalization.Tr("Reset Lattice Cage")))
                {
                    bool anyReset = false;

                    foreach (var obj in targets)
                    {
                        if (obj is LatticeDeformer deformer && ResetLatticeBox(deformer))
                        {
                            anyReset = true;
                        }
                    }

                    if (anyReset)
                    {
                        serializedObject.Update();
                        LatticePreviewUtility.RequestSceneRepaint();
                        SceneView.RepaintAll();
                    }
                }
            }
        }

        private static bool HasResettableBounds(LatticeDeformer deformer)
        {
            if (deformer == null)
            {
                return false;
            }

            if (deformer.Settings == null)
            {
                return false;
            }

            if (deformer.SourceMesh != null)
            {
                return true;
            }

            return deformer.GetComponent<SkinnedMeshRenderer>() != null || deformer.GetComponent<MeshFilter>() != null;
        }

        private static bool ResetLatticeBox(LatticeDeformer deformer)
        {
            if (!HasResettableBounds(deformer))
            {
                return false;
            }

            var settings = deformer.Settings;
            if (settings == null)
            {
                return false;
            }

            Undo.RecordObject(deformer, LatticeLocalization.Tr("Reset Lattice Cage"));

            deformer.Deform(false);
            if (deformer.SourceMesh == null)
            {
                return false;
            }

            deformer.InitializeFromSource(true);
            deformer.InvalidateCache();
            bool assignRuntimeMesh = LatticePreviewUtility.ShouldAssignRuntimeMesh();
            deformer.Deform(assignRuntimeMesh);

            EditorUtility.SetDirty(deformer);

            return true;
        }

        private void DrawLanguageSelector()
        {
            int current = (int)LatticeLocalization.CurrentLanguage;
            int next = EditorGUILayout.Popup(LatticeLocalization.Content("Tool Language"), current, LatticeLocalization.DisplayNames);
            if (next != current)
            {
                next = Mathf.Clamp(next, 0, LatticeLocalization.DisplayNames.Length - 1);
                LatticeLocalization.CurrentLanguage = (LatticeLocalization.Language)next;
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
                if (iterator.depth == _settingsProp.depth + 1 && iterator.name != "_gridSize" && iterator.name != "_controlPointsLocal")
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

            Undo.RecordObject(deformer, LatticeLocalization.Tr("Change Lattice Divisions"));
            settings.ResizeGrid(newSize);
            deformer.InvalidateCache();

            bool assignRuntimeMesh = LatticePreviewUtility.ShouldAssignRuntimeMesh();
            deformer.Deform(assignRuntimeMesh);

            EditorUtility.SetDirty(deformer);

            LatticePreviewUtility.RequestSceneRepaint();
            SceneView.RepaintAll();
        }

        private void DrawGridSizeControls(LatticeDeformer deformer)
        {
            if (deformer == null)
            {
                EditorGUILayout.HelpBox(LatticeLocalization.Tr("No Lattice Deformer selected."), MessageType.Info);
                return;
            }

            var settings = deformer.Settings;
            if (settings == null)
            {
                EditorGUILayout.HelpBox(LatticeLocalization.Tr("No Lattice Asset assigned to this deformer."), MessageType.Warning);
                return;
            }

            int id = deformer.GetInstanceID();
            if (!s_pendingGridSizes.TryGetValue(id, out var pending))
            {
                pending = settings.GridSize;
                s_pendingGridSizes[id] = pending;
            }

            EditorGUILayout.LabelField(LatticeLocalization.Content("Current Grid Divisions"), new GUIContent(settings.GridSize.ToString()));
            EditorGUI.BeginChangeCheck();
            pending = EditorGUILayout.Vector3IntField(LatticeLocalization.Tr("Pending Grid Divisions"), pending);
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
                    if (GUILayout.Button(LatticeLocalization.Tr("Apply"), GUILayout.Width(80f)))
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

                    if (GUILayout.Button(LatticeLocalization.Tr("Revert"), GUILayout.Width(80f)))
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




