#if UNITY_EDITOR
using System.Collections.Generic;
using nadena.dev.ndmf.preview;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using Net._32Ba.LatticeDeformationTool;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    [CustomEditor(typeof(LatticeDeformer))]
    public sealed class LatticeDeformerEditor : UnityEditor.Editor
    {
        private SerializedProperty _settingsProp;
        private SerializedProperty _layersProp;
        private SerializedProperty _activeLayerIndexProp;
        private SerializedProperty _skinnedRendererProp;
        private SerializedProperty _meshFilterProp;
        private SerializedProperty _recalcNormalsProp;
        private SerializedProperty _recalcTangentsProp;
        private SerializedProperty _recalcBoundsProp;
        private SerializedProperty _recalcBoneWeightsProp;
        private SerializedProperty _weightTransferSettingsProp;
        private SerializedProperty _alignModeProp;
        private SerializedProperty _clampMulXYProp;
        private SerializedProperty _clampMinXYProp;
        private SerializedProperty _clampMulZProp;
        private SerializedProperty _clampMinZProp;
        private SerializedProperty _allowCenterOffsetProp;
        private SerializedProperty _alignAutoInitializedProp;
        private SerializedProperty _manualOffsetProp;
        private SerializedProperty _manualScaleProp;
        private Vector3 _uniformScaleBuffer = Vector3.one;
        private static bool s_linkManualScale = true;
        private static GUIContent s_linkOn;
        private static GUIContent s_linkOff;
        private static readonly GUIContent[] s_xyzLabels = { new GUIContent("X"), new GUIContent("Y"), new GUIContent("Z") };

        private static bool s_showOptions = false;
        private static bool s_showLayerStack = true;
        private static bool s_showAdvancedSettings = false;
        private static bool s_showAlignSettings = false;
        private static bool s_showWeightTransferSettings = false;
        private static readonly Dictionary<long, Vector3Int> s_pendingGridSizes = new();

        private void OnEnable()
        {
            EnsureLinkIcons();
            _settingsProp = serializedObject.FindProperty("_settings");
            _layersProp = serializedObject.FindProperty("_layers");
            _activeLayerIndexProp = serializedObject.FindProperty("_activeLayerIndex");
            _skinnedRendererProp = serializedObject.FindProperty("_skinnedMeshRenderer");
            _meshFilterProp = serializedObject.FindProperty("_meshFilter");
            _recalcNormalsProp = serializedObject.FindProperty("_recalculateNormals");
            _recalcTangentsProp = serializedObject.FindProperty("_recalculateTangents");
            _recalcBoundsProp = serializedObject.FindProperty("_recalculateBounds");
            _recalcBoneWeightsProp = serializedObject.FindProperty("_recalculateBoneWeights");
            _weightTransferSettingsProp = serializedObject.FindProperty("_weightTransferSettings");
            _alignModeProp = serializedObject.FindProperty("_alignMode");
            _clampMulXYProp = serializedObject.FindProperty("_centerClampMulXY");
            _clampMinXYProp = serializedObject.FindProperty("_centerClampMinXY");
            _clampMulZProp = serializedObject.FindProperty("_centerClampMulZ");
            _clampMinZProp = serializedObject.FindProperty("_centerClampMinZ");
            _allowCenterOffsetProp = serializedObject.FindProperty("_allowCenterOffsetWhenBoundsSkipped");
            _alignAutoInitializedProp = serializedObject.FindProperty("_alignAutoInitialized");
            _manualOffsetProp = serializedObject.FindProperty("_manualOffsetProxy");
            _manualScaleProp = serializedObject.FindProperty("_manualScaleProxy");

            AutoAssignLocalRendererReferences();
            InitializePendingGridSizes();

            LatticeLocalization.LanguageChanged += Repaint;
        }

        private void OnDisable()
        {
            LatticeLocalization.LanguageChanged -= Repaint;

            foreach (var deformer in EnumerateTargets())
            {
                RemovePendingGridSizesFor(deformer);
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
            if (targets.Length == 1)
            {
                DrawLayerStackControls((LatticeDeformer)target);
                EditorGUILayout.Space();
            }
            else
            {
                EditorGUILayout.HelpBox(LatticeLocalization.Tr("Layer stack editing is only available when one Mesh Deformer is selected."), MessageType.Info);
            }

            var activeSettingsProp = GetActiveSettingsProperty((LatticeDeformer)target);

            EditorGUILayout.LabelField(LatticeLocalization.Content("Mesh Deformer Settings"), EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            DrawGridSizeControls((LatticeDeformer)target);
            DrawResetLatticeBoxControls();

            s_showAdvancedSettings = EditorGUILayout.Foldout(s_showAdvancedSettings, LatticeLocalization.Tr("Advanced Cage Settings"), true);
            if (s_showAdvancedSettings)
            {
                EditorGUI.indentLevel++;
                bool allowStructureEdits = target is LatticeDeformer selected && selected.IsEditingBaseLayer;
                if (!allowStructureEdits)
                {
                    EditorGUILayout.HelpBox(
                        LatticeLocalization.Tr("Bounds and interpolation are shared by the Lattice layer. Only control-point-related settings are shown for additional layers."),
                        MessageType.Info);
                }
                DrawSettingsExcludingGrid(activeSettingsProp, allowStructureEdits);
                EditorGUI.indentLevel--;
            }

            // Alignment subsection at same level as advanced settings
            DrawAlignmentSettings();

            if (targets.Length == 1 && target is LatticeDeformer single &&
                !single.IsEditingBaseLayer &&
                !single.IsLayerStructurallyCompatible(single.ActiveLayerIndex))
            {
                EditorGUILayout.HelpBox(
                    LatticeLocalization.Tr("The selected layer does not match the Lattice layer structure and is currently ignored during deformation. Use Add/Duplicate from the layer panel or reset the layer."),
                    MessageType.Warning);
            }

            EditorGUI.indentLevel--;

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

            // Bone weight recalculation (only for SkinnedMeshRenderer)
            bool hasSkinnedRenderer = _skinnedRendererProp != null &&
                !_skinnedRendererProp.hasMultipleDifferentValues &&
                _skinnedRendererProp.objectReferenceValue != null;

            using (new EditorGUI.DisabledScope(!hasSkinnedRenderer))
            {
                EditorGUILayout.PropertyField(_recalcBoneWeightsProp, LatticeLocalization.Content("Recalculate Bone Weights"));
            }

            if (!hasSkinnedRenderer && _recalcBoneWeightsProp != null && _recalcBoneWeightsProp.boolValue)
            {
                EditorGUILayout.HelpBox(LatticeLocalization.Tr("Bone weight recalculation requires a SkinnedMeshRenderer."), MessageType.Info);
            }

            // Weight transfer settings (when bone weight recalculation is enabled)
            if (_recalcBoneWeightsProp != null && _recalcBoneWeightsProp.boolValue && hasSkinnedRenderer)
            {
                EditorGUI.indentLevel++;
                s_showWeightTransferSettings = EditorGUILayout.Foldout(s_showWeightTransferSettings, LatticeLocalization.Tr("Weight Transfer Settings"), true);
                if (s_showWeightTransferSettings && _weightTransferSettingsProp != null)
                {
                    EditorGUI.indentLevel++;
                    DrawWeightTransferSettings();
                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
            }

            bool modified = serializedObject.ApplyModifiedProperties();
            if (modified)
            {
                bool assignRuntimeMesh = LatticePreviewUtility.ShouldAssignRuntimeMesh();

                foreach (var instance in EnumerateTargets())
                {
                    instance.SyncLayerStructuresToBase(resetControlPoints: false);
                    instance.InvalidateCache();
                    instance.Deform(assignRuntimeMesh);
                    LatticePrefabUtility.MarkModified(instance);
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

            foreach (var deformer in EnumerateTargets())
            {
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

            bool anyFound = false;
            foreach (var deformer in EnumerateTargets())
            {
                anyFound = true;
                if (deformer.GetComponent<T>() == null)
                {
                    return false;
                }
            }

            if (!anyFound)
            {
                return false;
            }

            if (targets.Length == 1)
            {
                return (target as LatticeDeformer)?.GetComponent<T>() != null;
            }

            return true;
        }

        private IEnumerable<LatticeDeformer> EnumerateTargets()
        {
            if (targets == null)
            {
                yield break;
            }

            foreach (var obj in targets)
            {
                if (obj is LatticeDeformer deformer)
                {
                    yield return deformer;
                }
            }
        }

        private void DrawResetLatticeBoxControls()
        {
            bool canReset = false;

            foreach (var deformer in EnumerateTargets())
            {
                if (!HasResettableBounds(deformer))
                {
                    continue;
                }

                canReset = true;
                break;
            }

            using (new EditorGUI.DisabledScope(!canReset))
            {
                bool resetActiveOnly = targets != null && targets.Length == 1 && target is LatticeDeformer single && !single.IsEditingBaseLayer;
                string resetLabel = resetActiveOnly
                    ? LatticeLocalization.Tr("Reset Active Layer")
                    : LatticeLocalization.Tr("Reset Lattice Cage");

                if (GUILayout.Button(resetLabel))
                {
                    bool anyReset = false;

                    foreach (var deformer in EnumerateTargets())
                    {
                        if (ResetLatticeBox(deformer))
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

            if (deformer.EditingSettings == null)
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

            var settings = deformer.EditingSettings;
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

            if (deformer.IsEditingBaseLayer)
            {
                deformer.InitializeFromSource(true);
            }
            else
            {
                var baseSettings = deformer.Settings;
                if (baseSettings != null)
                {
                    settings.GridSize = baseSettings.GridSize;
                    settings.LocalBounds = baseSettings.LocalBounds;
                    settings.Interpolation = baseSettings.Interpolation;
                }

                settings.ResetControlPoints();
            }

            deformer.InvalidateCache();
            bool assignRuntimeMesh = LatticePreviewUtility.ShouldAssignRuntimeMesh();
            deformer.Deform(assignRuntimeMesh);

            LatticePrefabUtility.MarkModified(deformer);

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

        private SerializedProperty GetActiveSettingsProperty(LatticeDeformer deformer)
        {
            if (deformer == null)
            {
                return _settingsProp;
            }

            if (_settingsProp == null)
            {
                return null;
            }

            int activeLayerIndex = _activeLayerIndexProp != null
                ? Mathf.Clamp(_activeLayerIndexProp.intValue, -1, (_layersProp != null ? _layersProp.arraySize - 1 : -1))
                : -1;

            if (activeLayerIndex < 0 || _layersProp == null || activeLayerIndex >= _layersProp.arraySize)
            {
                return _settingsProp;
            }

            var layerProp = _layersProp.GetArrayElementAtIndex(activeLayerIndex);
            if (layerProp == null)
            {
                return _settingsProp;
            }

            var layerSettingsProp = layerProp.FindPropertyRelative("_settings");
            return layerSettingsProp ?? _settingsProp;
        }

        private void DrawLayerStackControls(LatticeDeformer deformer)
        {
            if (deformer == null || _layersProp == null || _activeLayerIndexProp == null)
            {
                return;
            }

            s_showLayerStack = EditorGUILayout.Foldout(s_showLayerStack, LatticeLocalization.Tr("Deformation Layers"), true);
            if (!s_showLayerStack)
            {
                return;
            }

            EditorGUI.indentLevel++;

            int activeLayerIndex = Mathf.Clamp(_activeLayerIndexProp.intValue, -1, _layersProp.arraySize - 1);
            string[] labels = BuildLayerPopupLabels(_layersProp);
            int activePopup = Mathf.Clamp(activeLayerIndex + 1, 0, labels.Length - 1);

            EditorGUI.BeginChangeCheck();
            int nextPopup = EditorGUILayout.Popup(LatticeLocalization.Content("Editing Layer"), activePopup, labels);
            if (EditorGUI.EndChangeCheck())
            {
                _activeLayerIndexProp.intValue = nextPopup - 1;
            }

            for (int i = 0; i < _layersProp.arraySize; i++)
            {
                var layerProp = _layersProp.GetArrayElementAtIndex(i);
                if (layerProp == null)
                {
                    continue;
                }

                var nameProp = layerProp.FindPropertyRelative("_name");
                var enabledProp = layerProp.FindPropertyRelative("_enabled");
                var weightProp = layerProp.FindPropertyRelative("_weight");

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool isEditing = activeLayerIndex == i;
                        if (GUILayout.Toggle(isEditing, isEditing ? LatticeLocalization.Tr("Editing") : LatticeLocalization.Tr("Edit"), "Button", GUILayout.Width(70f)))
                        {
                            _activeLayerIndexProp.intValue = i;
                        }

                        if (enabledProp != null)
                        {
                            enabledProp.boolValue = GUILayout.Toggle(enabledProp.boolValue, LatticeLocalization.Content("Enabled"), GUILayout.Width(80f));
                        }

                        if (nameProp != null)
                        {
                            nameProp.stringValue = EditorGUILayout.TextField(nameProp.stringValue);
                        }
                    }

                    if (weightProp != null)
                    {
                        weightProp.floatValue = EditorGUILayout.Slider(LatticeLocalization.Content("Weight"), weightProp.floatValue, 0f, 1f);
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(LatticeLocalization.Tr("Add Layer")))
                {
                    PerformSingleLayerOperation(deformer, LatticeLocalization.Tr("Add Layer"), instance =>
                    {
                        instance.AddLayer();
                        return true;
                    });
                }

                using (new EditorGUI.DisabledScope(activeLayerIndex < 0))
                {
                    if (GUILayout.Button(LatticeLocalization.Tr("Duplicate")))
                    {
                        PerformSingleLayerOperation(deformer, LatticeLocalization.Tr("Duplicate Layer"), instance =>
                        {
                            return instance.DuplicateLayer(instance.ActiveLayerIndex) >= 0;
                        });
                    }

                    if (GUILayout.Button(LatticeLocalization.Tr("Remove")))
                    {
                        PerformSingleLayerOperation(deformer, LatticeLocalization.Tr("Remove Layer"), instance =>
                        {
                            return instance.RemoveLayer(instance.ActiveLayerIndex);
                        });
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(activeLayerIndex <= 0))
                {
                    if (GUILayout.Button(LatticeLocalization.Tr("Move Up")))
                    {
                        PerformSingleLayerOperation(deformer, LatticeLocalization.Tr("Move Layer Up"), instance =>
                        {
                            int index = instance.ActiveLayerIndex;
                            return instance.MoveLayer(index, index - 1);
                        });
                    }
                }

                using (new EditorGUI.DisabledScope(activeLayerIndex < 0 || activeLayerIndex >= _layersProp.arraySize - 1))
                {
                    if (GUILayout.Button(LatticeLocalization.Tr("Move Down")))
                    {
                        PerformSingleLayerOperation(deformer, LatticeLocalization.Tr("Move Layer Down"), instance =>
                        {
                            int index = instance.ActiveLayerIndex;
                            return instance.MoveLayer(index, index + 1);
                        });
                    }
                }
            }

            EditorGUI.indentLevel--;
        }

        private static string[] BuildLayerPopupLabels(SerializedProperty layersProp)
        {
            int count = layersProp != null ? layersProp.arraySize : 0;
            var labels = new string[count + 1];
            labels[0] = LatticeLocalization.Tr("Lattice Layer");

            for (int i = 0; i < count; i++)
            {
                var layerProp = layersProp.GetArrayElementAtIndex(i);
                var nameProp = layerProp?.FindPropertyRelative("_name");
                string name = nameProp != null && !string.IsNullOrWhiteSpace(nameProp.stringValue)
                    ? nameProp.stringValue
                    : $"Layer {i + 1}";
                labels[i + 1] = name;
            }

            return labels;
        }

        private void PerformSingleLayerOperation(LatticeDeformer deformer, string undoLabel, System.Func<LatticeDeformer, bool> op)
        {
            if (deformer == null || op == null)
            {
                return;
            }

            serializedObject.ApplyModifiedProperties();

            Undo.RecordObject(deformer, undoLabel);
            bool changed = op(deformer);
            if (!changed)
            {
                serializedObject.Update();
                return;
            }

            EditorUtility.SetDirty(deformer);
            LatticePrefabUtility.MarkModified(deformer);

            serializedObject.Update();
            InitializePendingGridSizes();

            bool assignRuntimeMesh = LatticePreviewUtility.ShouldAssignRuntimeMesh();
            deformer.InvalidateCache();
            deformer.Deform(assignRuntimeMesh);
            LatticePreviewUtility.RequestSceneRepaint();
            SceneView.RepaintAll();
        }

        private void DrawSettingsExcludingGrid(SerializedProperty settingsProp, bool allowStructureEdits)
        {
            if (settingsProp == null)
            {
                return;
            }

            var iterator = settingsProp.Copy();
            var end = iterator.GetEndProperty();

            bool enterChildren = iterator.NextVisible(true);
            while (enterChildren && !SerializedProperty.EqualContents(iterator, end))
            {
                bool isTopLevel = iterator.depth == settingsProp.depth + 1;
                bool isStructureField = iterator.name == "_localBounds" || iterator.name == "_interpolation";
                if (isTopLevel &&
                    iterator.name != "_gridSize" &&
                    iterator.name != "_controlPointsLocal" &&
                    (allowStructureEdits || !isStructureField))
                {
                    EditorGUILayout.PropertyField(iterator, includeChildren: true);
                }

                enterChildren = iterator.NextVisible(false);
            }
        }

        private void InitializePendingGridSizes()
        {
            foreach (var deformer in EnumerateTargets())
            {
                var settings = deformer.IsEditingBaseLayer ? deformer.Settings : deformer.EditingSettings;
                if (settings == null)
                {
                    continue;
                }

                s_pendingGridSizes[GetPendingGridKey(deformer)] = settings.GridSize;
            }
        }

        private static void ApplyGridSizeChange(LatticeDeformer deformer, Vector3Int newSize)
        {
            if (deformer == null)
            {
                return;
            }

            var settings = deformer.EditingSettings;
            if (settings == null)
            {
                return;
            }

            newSize.x = Mathf.Max(2, newSize.x);
            newSize.y = Mathf.Max(2, newSize.y);
            newSize.z = Mathf.Max(2, newSize.z);

            Undo.RecordObject(deformer, LatticeLocalization.Tr("Change Lattice Divisions"));
            settings.ResizeGrid(newSize);
            if (deformer.IsEditingBaseLayer)
            {
                deformer.SyncLayerStructuresToBase(resetControlPoints: true);
            }
            deformer.InvalidateCache();

            bool assignRuntimeMesh = LatticePreviewUtility.ShouldAssignRuntimeMesh();
            deformer.Deform(assignRuntimeMesh);

            LatticePrefabUtility.MarkModified(deformer);

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

            var settings = deformer.EditingSettings;
            if (settings == null)
            {
                EditorGUILayout.HelpBox(LatticeLocalization.Tr("No Lattice Asset assigned to this deformer."), MessageType.Warning);
                return;
            }

            if (!deformer.IsEditingBaseLayer)
            {
                EditorGUILayout.HelpBox(
                    LatticeLocalization.Tr("Grid divisions are shared by the Lattice layer. Switch Editing Layer to Lattice Layer to change structure."),
                    MessageType.Info);
            }

            long pendingKey = GetPendingGridKey(deformer);
            if (!s_pendingGridSizes.TryGetValue(pendingKey, out var pending))
            {
                pending = settings.GridSize;
                s_pendingGridSizes[pendingKey] = pending;
            }

            EditorGUILayout.LabelField(LatticeLocalization.Content("Current Grid Divisions"), new GUIContent(settings.GridSize.ToString()));
            using (new EditorGUI.DisabledScope(!deformer.IsEditingBaseLayer))
            {
                EditorGUI.BeginChangeCheck();
                pending = EditorGUILayout.Vector3IntField(LatticeLocalization.Tr("Pending Grid Divisions"), pending);
                if (EditorGUI.EndChangeCheck())
                {
                    pending.x = Mathf.Max(2, pending.x);
                    pending.y = Mathf.Max(2, pending.y);
                    pending.z = Mathf.Max(2, pending.z);
                    s_pendingGridSizes[pendingKey] = pending;
                }
            }

            bool hasPendingChange = s_pendingGridSizes[pendingKey] != settings.GridSize;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!hasPendingChange || !deformer.IsEditingBaseLayer))
                {
                    if (GUILayout.Button(LatticeLocalization.Tr("Apply"), GUILayout.Width(80f)))
                    {
                        foreach (var selected in EnumerateTargets())
                        {
                            if (!selected.IsEditingBaseLayer)
                            {
                                continue;
                            }

                            long selectedKey = GetPendingGridKey(selected);
                            if (!s_pendingGridSizes.TryGetValue(selectedKey, out var pendingSize))
                            {
                                pendingSize = selected.EditingSettings?.GridSize ?? pending;
                            }

                            ApplyGridSizeChange(selected, pendingSize);
                            s_pendingGridSizes[selectedKey] = selected.EditingSettings?.GridSize ?? pendingSize;
                        }
                    }

                    if (GUILayout.Button(LatticeLocalization.Tr("Revert"), GUILayout.Width(80f)))
                    {
                        foreach (var selected in EnumerateTargets())
                        {
                            if (selected.EditingSettings != null)
                            {
                                s_pendingGridSizes[GetPendingGridKey(selected)] = selected.EditingSettings.GridSize;
                            }
                        }
                    }
                }
            }

            EditorGUILayout.Space();
        }

        private static long GetPendingGridKey(LatticeDeformer deformer)
        {
            if (deformer == null)
            {
                return 0L;
            }

            int instanceId = deformer.GetInstanceID();
            int layerIndex = deformer.ActiveLayerIndex + 1; // map base(-1) -> 0
            return ((long)instanceId << 32) ^ (uint)layerIndex;
        }

        private static void RemovePendingGridSizesFor(LatticeDeformer deformer)
        {
            if (deformer == null || s_pendingGridSizes.Count == 0)
            {
                return;
            }

            int instanceId = deformer.GetInstanceID();
            var toRemove = new List<long>();
            foreach (var key in s_pendingGridSizes.Keys)
            {
                int keyInstance = unchecked((int)(key >> 32));
                if (keyInstance == instanceId)
                {
                    toRemove.Add(key);
                }
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                s_pendingGridSizes.Remove(toRemove[i]);
            }
        }

        private void DrawWeightTransferSettings()
        {
            if (_weightTransferSettingsProp == null)
            {
                return;
            }

            // Stage 1 settings
            EditorGUILayout.LabelField(LatticeLocalization.Tr("Stage 1: Initial Transfer"), EditorStyles.boldLabel);

            var maxDistProp = _weightTransferSettingsProp.FindPropertyRelative("maxTransferDistance");
            if (maxDistProp != null)
            {
                EditorGUILayout.PropertyField(
                    maxDistProp,
                    LatticeLocalization.Content(
                        "Max Transfer Distance",
                        "If weights stick to the wrong surface, try lowering this value or the Normal Angle Threshold for stricter matching."));
            }

            var normalThresholdProp = _weightTransferSettingsProp.FindPropertyRelative("normalAngleThreshold");
            if (normalThresholdProp != null)
            {
                EditorGUILayout.PropertyField(
                    normalThresholdProp,
                    LatticeLocalization.Content(
                        "Normal Angle Threshold",
                        "If weights stick to the wrong surface, try lowering this value or the Max Transfer Distance for stricter matching."));
            }

            EditorGUILayout.Space(4);

            // Stage 2 settings
            EditorGUILayout.LabelField(LatticeLocalization.Tr("Stage 2: Weight Inpainting"), EditorStyles.boldLabel);

            var enableInpaintingProp = _weightTransferSettingsProp.FindPropertyRelative("enableInpainting");
            if (enableInpaintingProp != null)
            {
                EditorGUILayout.PropertyField(enableInpaintingProp, LatticeLocalization.Content("Enable Inpainting"));

                if (enableInpaintingProp.boolValue)
                {
                    EditorGUI.indentLevel++;

                    var maxIterProp = _weightTransferSettingsProp.FindPropertyRelative("maxIterations");
                    if (maxIterProp != null)
                    {
                        EditorGUILayout.PropertyField(maxIterProp, LatticeLocalization.Content("Max Iterations"));
                    }

                    var toleranceProp = _weightTransferSettingsProp.FindPropertyRelative("tolerance");
                    if (toleranceProp != null)
                    {
                        EditorGUILayout.PropertyField(toleranceProp, LatticeLocalization.Content("Tolerance"));
                    }

                    EditorGUI.indentLevel--;
                }
            }
        }

        private void DrawAlignmentSettings()
        {
            s_showAlignSettings = EditorGUILayout.Foldout(s_showAlignSettings, LatticeLocalization.Tr("Lattice Cage Alignment"), true);
            if (!s_showAlignSettings)
            {
                return;
            }

            EditorGUI.indentLevel++;

            EditorGUILayout.HelpBox(
                LatticeLocalization.Tr("Position/scale here only move the editing cage in the Scene view. They do not change the deform target's bounds or final mesh deformation."),
                MessageType.Info);

            if (_manualOffsetProp != null)
            {
                EditorGUILayout.PropertyField(_manualOffsetProp,
                    new GUIContent(LatticeLocalization.Tr("Offset"),
                        LatticeLocalization.Tr("Direct offset applied in proxy local space before alignment")));
            }

            if (_manualScaleProp != null)
            {
                DrawLinkedScaleField(_manualScaleProp, ref s_linkManualScale, LatticeLocalization.Tr("Scale"));
            }

            bool debugAlign = LatticePreviewUtility.DebugAlignLogs;
            bool nextDebug = EditorGUILayout.ToggleLeft(
                new GUIContent(LatticeLocalization.Tr("(Debug) Log Preview Alignment to Console")),
                debugAlign);
            if (nextDebug != debugAlign)
            {
                LatticePreviewUtility.DebugAlignLogs = nextDebug;
                LatticePreviewUtility.LogAlign("Toggle", $"DebugAlignLogs set to {nextDebug}");
            }

            EditorGUI.indentLevel--;
        }

        private static void DrawLinkedScaleField(SerializedProperty prop, ref bool link, string label)
        {
            if (prop == null || prop.propertyType != SerializedPropertyType.Vector3)
            {
                return;
            }

            EnsureLinkIcons();

            var value = prop.vector3Value;
            var rect = EditorGUILayout.GetControlRect();
            var labelContent = new GUIContent(label);

            EditorGUI.BeginProperty(rect, labelContent, prop);
            rect = EditorGUI.PrefixLabel(rect, labelContent);

            const float linkWidth = 20f;
            var linkRect = new Rect(rect.x, rect.y, linkWidth, rect.height);
            var fieldsRect = new Rect(linkRect.xMax + 2f, rect.y, rect.width - linkWidth - 2f, rect.height);

            if (GUI.Button(linkRect, link ? s_linkOn : s_linkOff, GUIStyle.none))
            {
                link = !link;
            }

            float[] vals = { value.x, value.y, value.z };
            EditorGUI.BeginChangeCheck();
            EditorGUI.MultiFloatField(fieldsRect, s_xyzLabels, vals);
            if (EditorGUI.EndChangeCheck())
            {
                if (link)
                {
                    vals[1] = vals[2] = vals[0];
                }

                value = new Vector3(
                    Mathf.Max(0.0001f, vals[0]),
                    Mathf.Max(0.0001f, vals[1]),
                    Mathf.Max(0.0001f, vals[2]));
                prop.vector3Value = value;
            }

            EditorGUI.EndProperty();
        }

        private static Bounds DivBoundsByScale(Bounds b, Vector3 scale)
        {
            var center = new Vector3(
                scale.x != 0f ? b.center.x / scale.x : b.center.x,
                scale.y != 0f ? b.center.y / scale.y : b.center.y,
                scale.z != 0f ? b.center.z / scale.z : b.center.z);

            var size = new Vector3(
                scale.x != 0f ? b.size.x / Mathf.Abs(scale.x) : b.size.x,
                scale.y != 0f ? b.size.y / Mathf.Abs(scale.y) : b.size.y,
                scale.z != 0f ? b.size.z / Mathf.Abs(scale.z) : b.size.z);

            return new Bounds(center, size);
        }

        private static void EnsureLinkIcons()
        {
            if (s_linkOn == null)
            {
                s_linkOn = EditorGUIUtility.IconContent("Linked");
                if (s_linkOn == null || s_linkOn.image == null)
                {
                    s_linkOn = new GUIContent("≡", "Link axes");
                }
            }

            if (s_linkOff == null)
            {
                s_linkOff = EditorGUIUtility.IconContent("Unlinked");
                if (s_linkOff == null || s_linkOff.image == null)
                {
                    s_linkOff = new GUIContent("≠", "Unlink axes");
                }
            }
        }
    }
}
#endif




