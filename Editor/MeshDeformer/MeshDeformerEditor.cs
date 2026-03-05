#if UNITY_EDITOR
using System.Collections.Generic;
using nadena.dev.ndmf.preview;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditorInternal;
using UnityEngine;
using Net._32Ba.LatticeDeformationTool;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    [CustomEditor(typeof(LatticeDeformer), true)]
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
        private ReorderableList _layerReorderableList;
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
            InitializeLayerReorderableList();

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

            var activeDeformer = target as LatticeDeformer;
            var activeSettingsProp = GetActiveSettingsProperty(activeDeformer);
            bool isBrushLayer = targets.Length == 1 && GetSerializedActiveLayerType() == MeshDeformerLayerType.Brush;

            EditorGUILayout.LabelField(LatticeLocalization.Content("Mesh Deformer Settings"), EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            DrawResetLatticeBoxControls();

            s_showAdvancedSettings = EditorGUILayout.Foldout(s_showAdvancedSettings, LatticeLocalization.Tr("Advanced Layer Settings"), true);
            if (s_showAdvancedSettings)
            {
                EditorGUI.indentLevel++;
                if (isBrushLayer)
                {
                    DrawBrushLayerSettings(activeDeformer);
                }
                else
                {
                    DrawGridSizeControls(activeDeformer);
                    DrawSettingsExcludingGrid(activeSettingsProp, allowStructureEdits: true);
                }
                EditorGUI.indentLevel--;
            }

            // Alignment subsection at same level as advanced settings
            DrawAlignmentSettings();

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
                    ? LatticeLocalization.Tr("(NDMF) Disable Mesh Preview")
                    : LatticeLocalization.Tr("(NDMF) Enable Mesh Preview");
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
                    instance.InvalidateCache();
                    instance.Deform(assignRuntimeMesh);
                    LatticePrefabUtility.MarkModified(instance);
                }

                if (targets.Length == 1 && activeDeformer != null)
                {
                    SyncActiveToolToLayer(activeDeformer);
                }

                LatticePreviewUtility.RequestSceneRepaint();
            }

            EditorGUILayout.Space();
            bool openBrushTool = targets.Length == 1 && GetSerializedActiveLayerType() == MeshDeformerLayerType.Brush;
            if (GUILayout.Button(openBrushTool
                ? LatticeLocalization.Tr("Open Brush Editor")
                : LatticeLocalization.Tr("Open Lattice Editor")))
            {
                if (openBrushTool)
                {
                    ToolManager.SetActiveTool<BrushDeformerTool>();
                }
                else
                {
                    ToolManager.SetActiveTool<LatticeDeformerTool>();
                }

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
                string resetLabel = LatticeLocalization.Tr("Reset Active Layer");

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

            if (deformer.ActiveLayerType == MeshDeformerLayerType.Brush)
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

            settings.LocalBounds = deformer.SourceMesh.bounds;
            settings.ResetControlPoints();

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

        private MeshDeformerLayerType GetSerializedActiveLayerType()
        {
            if (_layersProp == null || _activeLayerIndexProp == null || _layersProp.arraySize == 0)
            {
                return MeshDeformerLayerType.Lattice;
            }

            int activeLayerIndex = Mathf.Clamp(_activeLayerIndexProp.intValue, 0, _layersProp.arraySize - 1);
            var layerProp = _layersProp.GetArrayElementAtIndex(activeLayerIndex);
            var typeProp = layerProp?.FindPropertyRelative("_type");
            if (typeProp == null)
            {
                return MeshDeformerLayerType.Lattice;
            }

            return (MeshDeformerLayerType)Mathf.Clamp(typeProp.enumValueIndex, 0, 1);
        }

        private static void DrawBrushLayerSettings(LatticeDeformer deformer)
        {
            EditorGUILayout.HelpBox(LatticeLocalization.Tr("Brush layer stores per-vertex displacement data. Use Brush Tool to edit this layer."), MessageType.Info);

            int vertexCount = deformer != null && deformer.SourceMesh != null ? deformer.SourceMesh.vertexCount : 0;
            EditorGUILayout.LabelField(LatticeLocalization.Tr("Vertex Count"), vertexCount.ToString());

            if (deformer == null)
            {
                return;
            }

            EditorGUI.BeginDisabledGroup(deformer.ActiveLayerType != MeshDeformerLayerType.Brush);
            if (GUILayout.Button(LatticeLocalization.Tr("Clear Active Layer Displacements")))
            {
                Undo.RecordObject(deformer, LatticeLocalization.Tr("Clear Active Layer Displacements"));
                deformer.ClearDisplacements();
                deformer.InvalidateCache();
                deformer.Deform(LatticePreviewUtility.ShouldAssignRuntimeMesh());
                LatticePrefabUtility.MarkModified(deformer);
                LatticePreviewUtility.RequestSceneRepaint();
                SceneView.RepaintAll();
            }
            EditorGUI.EndDisabledGroup();
        }

        private void DrawLayerStackControls(LatticeDeformer deformer)
        {
            if (deformer == null || _layersProp == null || _activeLayerIndexProp == null)
            {
                return;
            }

            if (_layerReorderableList == null)
            {
                InitializeLayerReorderableList();
            }

            s_showLayerStack = EditorGUILayout.Foldout(s_showLayerStack, LatticeLocalization.Tr("Deformation Layers"), true);
            if (!s_showLayerStack)
            {
                return;
            }

            if (_layerReorderableList == null)
            {
                EditorGUILayout.HelpBox(LatticeLocalization.Tr("No deformation layers are available."), MessageType.Warning);
                return;
            }

            ClampActiveLayerIndexProperty();
            _layerReorderableList.index = _activeLayerIndexProp.intValue;

            EditorGUI.indentLevel++;
            _layerReorderableList.DoLayoutList();
            EditorGUI.indentLevel--;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_layersProp.arraySize <= 0))
                {
                    if (GUILayout.Button(LatticeLocalization.Tr("Duplicate")))
                    {
                        PerformSingleLayerOperation(deformer, LatticeLocalization.Tr("Duplicate Layer"), instance =>
                        {
                            return instance.DuplicateLayer(instance.ActiveLayerIndex) >= 0;
                        });
                    }
                }
            }
        }

        private void InitializeLayerReorderableList()
        {
            if (_layersProp == null)
            {
                _layerReorderableList = null;
                return;
            }

            _layerReorderableList = new ReorderableList(serializedObject, _layersProp, draggable: true, displayHeader: true, displayAddButton: true, displayRemoveButton: true);
            _layerReorderableList.elementHeight = EditorGUIUtility.singleLineHeight * 2f + 8f;
            _layerReorderableList.drawHeaderCallback = rect =>
            {
                EditorGUI.LabelField(rect, LatticeLocalization.Tr("Deformation Layers"));
            };
            _layerReorderableList.drawElementCallback = DrawLayerElement;
            _layerReorderableList.onSelectCallback = list =>
            {
                if (_activeLayerIndexProp == null || _layersProp == null || _layersProp.arraySize == 0)
                {
                    return;
                }

                _activeLayerIndexProp.intValue = Mathf.Clamp(list.index, 0, _layersProp.arraySize - 1);
            };
            _layerReorderableList.onReorderCallbackWithDetails = (_, oldIndex, newIndex) =>
            {
                UpdateActiveLayerIndexAfterReorder(oldIndex, newIndex);
            };
            _layerReorderableList.onCanRemoveCallback = _ => _layersProp != null && _layersProp.arraySize > 1;
            _layerReorderableList.onRemoveCallback = list =>
            {
                if (_layersProp == null || _layersProp.arraySize <= 1)
                {
                    return;
                }

                ReorderableList.defaultBehaviours.DoRemoveButton(list);
                ClampActiveLayerIndexProperty();
            };
            _layerReorderableList.onAddDropdownCallback = (buttonRect, _) =>
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent(LatticeLocalization.Tr("Add Lattice Layer")), false, () => AddLayerViaList(MeshDeformerLayerType.Lattice));
                menu.AddItem(new GUIContent(LatticeLocalization.Tr("Add Brush Layer")), false, () => AddLayerViaList(MeshDeformerLayerType.Brush));
                menu.DropDown(buttonRect);
            };
        }

        private void DrawLayerElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            if (_layersProp == null || index < 0 || index >= _layersProp.arraySize)
            {
                return;
            }

            var layerProp = _layersProp.GetArrayElementAtIndex(index);
            if (layerProp == null)
            {
                return;
            }

            var nameProp = layerProp.FindPropertyRelative("_name");
            var enabledProp = layerProp.FindPropertyRelative("_enabled");
            var weightProp = layerProp.FindPropertyRelative("_weight");
            var typeProp = layerProp.FindPropertyRelative("_type");

            bool isEditing = _activeLayerIndexProp != null && _activeLayerIndexProp.intValue == index;
            if (isEditing)
            {
                var activeColor = EditorGUIUtility.isProSkin
                    ? new Color(0.22f, 0.38f, 0.62f, 0.22f)
                    : new Color(0.24f, 0.46f, 0.82f, 0.18f);
                EditorGUI.DrawRect(rect, activeColor);
            }

            var row1 = new Rect(rect.x, rect.y + 2f, rect.width, EditorGUIUtility.singleLineHeight);
            var row2 = new Rect(rect.x, row1.yMax + 2f, rect.width, EditorGUIUtility.singleLineHeight);

            float editWidth = 60f;
            float enabledWidth = 22f;
            float typeWidth = 72f;

            var editRect = new Rect(row1.x, row1.y, editWidth, row1.height);
            var enabledRect = new Rect(editRect.xMax + 4f, row1.y, enabledWidth, row1.height);
            var typeRect = new Rect(row1.xMax - typeWidth, row1.y, typeWidth, row1.height);
            var nameRect = new Rect(enabledRect.xMax + 2f, row1.y, Mathf.Max(20f, typeRect.x - enabledRect.xMax - 6f), row1.height);

            if (isEditing)
            {
                GUI.Toggle(editRect, true, LatticeLocalization.Tr("Editing"), "Button");
            }
            else if (GUI.Button(editRect, LatticeLocalization.Tr("Edit")))
            {
                _activeLayerIndexProp.intValue = index;
                if (_layerReorderableList != null)
                {
                    _layerReorderableList.index = index;
                }
            }

            if (enabledProp != null)
            {
                enabledProp.boolValue = EditorGUI.Toggle(enabledRect, enabledProp.boolValue);
            }

            if (nameProp != null)
            {
                nameProp.stringValue = EditorGUI.TextField(nameRect, nameProp.stringValue);
            }

            if (typeProp != null)
            {
                string typeLabel = typeProp.enumValueIndex == (int)MeshDeformerLayerType.Brush
                    ? LatticeLocalization.Tr("Brush")
                    : LatticeLocalization.Tr("Lattice");
                EditorGUI.LabelField(typeRect, typeLabel, EditorStyles.miniLabel);
            }

            if (weightProp != null)
            {
                weightProp.floatValue = EditorGUI.Slider(row2, LatticeLocalization.Tr("Weight"), weightProp.floatValue, 0f, 1f);
            }
        }

        private void AddLayerViaList(MeshDeformerLayerType layerType)
        {
            if (target is not LatticeDeformer deformer)
            {
                return;
            }

            string undoLabel = layerType == MeshDeformerLayerType.Brush
                ? LatticeLocalization.Tr("Add Brush Layer")
                : LatticeLocalization.Tr("Add Lattice Layer");

            PerformSingleLayerOperation(deformer, undoLabel, instance =>
            {
                instance.AddLayer(layerType: layerType);
                return true;
            });
        }

        private void ClampActiveLayerIndexProperty()
        {
            if (_layersProp == null || _activeLayerIndexProp == null)
            {
                return;
            }

            int maxIndex = Mathf.Max(0, _layersProp.arraySize - 1);
            _activeLayerIndexProp.intValue = Mathf.Clamp(_activeLayerIndexProp.intValue, 0, maxIndex);
        }

        private void UpdateActiveLayerIndexAfterReorder(int oldIndex, int newIndex)
        {
            if (_activeLayerIndexProp == null || _layersProp == null || _layersProp.arraySize == 0)
            {
                return;
            }

            int active = Mathf.Clamp(_activeLayerIndexProp.intValue, 0, _layersProp.arraySize - 1);
            if (active == oldIndex)
            {
                active = newIndex;
            }
            else if (oldIndex < active && newIndex >= active)
            {
                active--;
            }
            else if (oldIndex > active && newIndex <= active)
            {
                active++;
            }

            _activeLayerIndexProp.intValue = Mathf.Clamp(active, 0, _layersProp.arraySize - 1);
            if (_layerReorderableList != null)
            {
                _layerReorderableList.index = _activeLayerIndexProp.intValue;
            }
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
            SyncActiveToolToLayer(deformer);
            LatticePreviewUtility.RequestSceneRepaint();
            SceneView.RepaintAll();
        }

        private static void SyncActiveToolToLayer(LatticeDeformer deformer)
        {
            if (deformer == null)
            {
                return;
            }

            var activeToolType = ToolManager.activeToolType;
            bool usingLatticeTool = activeToolType == typeof(LatticeDeformerTool);
            bool usingBrushTool = activeToolType == typeof(BrushDeformerTool);
            if (!usingLatticeTool && !usingBrushTool)
            {
                return;
            }

            if (deformer.ActiveLayerType == MeshDeformerLayerType.Brush && usingLatticeTool)
            {
                ToolManager.SetActiveTool<BrushDeformerTool>();
            }
            else if (deformer.ActiveLayerType == MeshDeformerLayerType.Lattice && usingBrushTool)
            {
                ToolManager.SetActiveTool<LatticeDeformerTool>();
            }
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
                var settings = deformer.EditingSettings;
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

            long pendingKey = GetPendingGridKey(deformer);
            if (!s_pendingGridSizes.TryGetValue(pendingKey, out var pending))
            {
                pending = settings.GridSize;
                s_pendingGridSizes[pendingKey] = pending;
            }

            EditorGUILayout.LabelField(LatticeLocalization.Content("Current Grid Divisions"), new GUIContent(settings.GridSize.ToString()));
            EditorGUI.BeginChangeCheck();
            pending = EditorGUILayout.Vector3IntField(LatticeLocalization.Tr("Pending Grid Divisions"), pending);
            if (EditorGUI.EndChangeCheck())
            {
                pending.x = Mathf.Max(2, pending.x);
                pending.y = Mathf.Max(2, pending.y);
                pending.z = Mathf.Max(2, pending.z);
                s_pendingGridSizes[pendingKey] = pending;
            }

            bool hasPendingChange = s_pendingGridSizes[pendingKey] != settings.GridSize;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!hasPendingChange))
                {
                    if (GUILayout.Button(LatticeLocalization.Tr("Apply"), GUILayout.Width(80f)))
                    {
                        foreach (var selected in EnumerateTargets())
                        {
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
            int layerIndex = Mathf.Max(0, deformer.ActiveLayerIndex);
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




