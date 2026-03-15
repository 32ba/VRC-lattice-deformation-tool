#if UNITY_EDITOR
using System.Collections.Generic;
using nadena.dev.ndmf.preview;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
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
        private SerializedProperty _blendShapeOutputProp;
        private SerializedProperty _blendShapeNameProp;
        private SerializedProperty _blendShapeCurveProp;
        private bool _blendShapeTestMode = false;
        private float _blendShapeTestWeight = 0f;
        private Mesh _preTestMesh = null;
        private bool _preTestMeshWasOverridden = false;
        private bool _preTestWeightsWereOverridden = false;
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
        private static bool s_showAlignSettings = false;
        private static bool s_showWeightTransferSettings = false;
        private static bool s_showBlendShapeOutput = false;
        private static bool s_showLayerSettings = false;
        private static readonly Dictionary<long, Vector3Int> s_pendingGridSizes = new();
        private static string s_copiedLayerJson = null;
        private static MeshDeformerLayerType s_copiedLayerType;

        // UI Toolkit layer list
        private ListView _layerListView;
        private readonly List<int> _layerIndices = new();
        private int _cachedLayerCount = -1;
        private int _cachedActiveIndex = -1;

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
            _blendShapeOutputProp = serializedObject.FindProperty("_blendShapeOutput");
            _blendShapeNameProp = serializedObject.FindProperty("_blendShapeName");
            _blendShapeCurveProp = serializedObject.FindProperty("_blendShapeCurve");
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
            LatticeLocalization.LanguageChanged += OnLanguageChanged;
        }

        private void OnDisable()
        {
            LatticeLocalization.LanguageChanged -= OnLanguageChanged;
            ExitBlendShapeTestMode();

            foreach (var deformer in EnumerateTargets())
            {
                RemovePendingGridSizesFor(deformer);
            }
        }

        private void OnLanguageChanged()
        {
            Repaint();
            RebuildLayerList();
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();

            // Top: Language + Mesh Source (IMGUI)
            root.Add(new IMGUIContainer(DrawTopSection));

            // Layer list section (UI Toolkit ListView)
            if (targets.Length == 1)
            {
                var layerFoldout = new Foldout
                {
                    text = LatticeLocalization.Tr("Deformation Layers"),
                    value = s_showLayerStack
                };
                layerFoldout.RegisterValueChangedCallback(evt =>
                {
                    s_showLayerStack = evt.newValue;
                    evt.StopPropagation();
                });

                _layerListView = new ListView
                {
                    reorderable = true,
                    reorderMode = ListViewReorderMode.Animated,
                    showBorder = true,
                    showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                    virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                    selectionType = SelectionType.Single,
                };
                _layerListView.makeItem = MakeLayerItem;
                _layerListView.bindItem = BindLayerItem;
                _layerListView.itemIndexChanged += OnLayerReordered;
                _layerListView.selectionChanged += _ => OnLayerSelectionChanged();

                RebuildLayerList();
                layerFoldout.Add(_layerListView);

                // Footer: [+] [-] like ReorderableList
                var footer = new VisualElement();
                footer.style.flexDirection = FlexDirection.Row;
                footer.style.justifyContent = Justify.FlexEnd;
                footer.style.marginTop = -2;
                footer.style.marginRight = 2;
                footer.style.marginBottom = 4;

                var addBtn = new Button(() =>
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent(LatticeLocalization.Tr("Add Lattice Layer")), false, () =>
                    {
                        AddLayerViaList(MeshDeformerLayerType.Lattice);
                        RebuildLayerList();
                    });
                    menu.AddItem(new GUIContent(LatticeLocalization.Tr("Add Brush Layer")), false, () =>
                    {
                        AddLayerViaList(MeshDeformerLayerType.Brush);
                        RebuildLayerList();
                    });
                    menu.ShowAsContext();
                }) { text = "+" };
                addBtn.style.width = 25;
                addBtn.style.height = 16;
                addBtn.style.fontSize = 14;
                addBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
                addBtn.style.paddingTop = 0;
                addBtn.style.paddingBottom = 0;
                footer.Add(addBtn);

                var removeBtn = new Button(() =>
                {
                    if (target is LatticeDeformer d && _layersProp.arraySize > 0)
                    {
                        DeleteLayer(d, _activeLayerIndexProp.intValue);
                        RebuildLayerList();
                    }
                }) { text = "\u2212" }; // −
                removeBtn.style.width = 25;
                removeBtn.style.height = 16;
                removeBtn.style.fontSize = 14;
                removeBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
                removeBtn.style.paddingTop = 0;
                removeBtn.style.paddingBottom = 0;
                footer.Add(removeBtn);

                layerFoldout.Add(footer);
                layerFoldout.Add(new IMGUIContainer(DrawLayerOperationsImgui));
                root.Add(layerFoldout);
            }

            // Build Options + Open Editor (IMGUI)
            root.Add(new IMGUIContainer(DrawBottomSection));

            // Track serialized changes for layer list rebuild
            root.TrackSerializedObjectValue(serializedObject, _ =>
            {
                CheckAndRebuildLayers();
                // Also notify property changes for deformation preview
                NotifyPropertyChanges();
            });
            root.Bind(serializedObject);

            return root;
        }

        private void DrawTopSection()
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

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawBottomSection()
        {
            serializedObject.Update();

            DrawBlendShapeOutputSection();
            DrawBuildOptions();

            bool modified = serializedObject.ApplyModifiedProperties();
            if (modified)
            {
                NotifyPropertyChanges();
            }

            EditorGUILayout.Space();

            bool hasLayers = _layersProp != null && _layersProp.arraySize > 0;
            using (new EditorGUI.DisabledScope(!hasLayers))
            {
                bool openBrushTool = hasLayers && GetSerializedActiveLayerType() == MeshDeformerLayerType.Brush;
                if (GUILayout.Button(openBrushTool
                    ? LatticeLocalization.Tr("Open Brush Editor")
                    : LatticeLocalization.Tr("Open Lattice Editor")))
                {
                    ToolManager.SetActiveTool<MeshDeformerTool>();
                    LatticePreviewUtility.RequestSceneRepaint();
                }
            }
        }

        private void NotifyPropertyChanges()
        {
            bool assignRuntimeMesh = LatticePreviewUtility.ShouldAssignRuntimeMesh();
            foreach (var instance in EnumerateTargets())
            {
                instance.InvalidateCache();
                instance.Deform(assignRuntimeMesh);
                LatticePrefabUtility.MarkModified(instance);
            }

            if (targets.Length == 1 && target is LatticeDeformer activeDeformer)
            {
                SyncActiveToolToLayer(activeDeformer);
                ReapplyBlendShapeTestWeight(activeDeformer);
            }

            LatticePreviewUtility.RequestSceneRepaint();
        }

        private void ReapplyBlendShapeTestWeight(LatticeDeformer deformer)
        {
            if (!_blendShapeTestMode) return;
            if (deformer.BlendShapeOutput != BlendShapeOutputMode.OutputAsBlendShape) return;

            var smr = deformer.GetComponent<SkinnedMeshRenderer>();
            if (smr == null) return;

            // Re-assign runtime mesh after Deform() rebuild
            var runtimeMesh = deformer.RuntimeMesh;
            if (runtimeMesh != null && smr.sharedMesh != runtimeMesh)
            {
                smr.sharedMesh = runtimeMesh;
            }

            ApplyBlendShapeTestWeight(deformer, smr);
        }

        private void CheckAndRebuildLayers()
        {
            if (_layersProp == null || _activeLayerIndexProp == null || _layerListView == null) return;
            serializedObject.Update();
            int count = _layersProp.arraySize;
            int active = _activeLayerIndexProp.intValue;
            if (count != _cachedLayerCount || active != _cachedActiveIndex)
            {
                _cachedLayerCount = count;
                _cachedActiveIndex = active;
                RebuildLayerList();
            }
        }

        private void RebuildLayerList()
        {
            if (_layerListView == null || _layersProp == null) return;
            serializedObject.Update();

            _cachedLayerCount = _layersProp.arraySize;
            _cachedActiveIndex = _activeLayerIndexProp?.intValue ?? 0;

            _layerIndices.Clear();
            for (int i = 0; i < _layersProp.arraySize; i++)
                _layerIndices.Add(i);

            _layerListView.itemsSource = _layerIndices;
            _layerListView.selectedIndex = _cachedActiveIndex;
            _layerListView.Rebuild();
        }

        private VisualElement MakeLayerItem()
        {
            var root = new VisualElement();
            root.style.paddingTop = 3;
            root.style.paddingBottom = 3;
            root.style.paddingLeft = 4;
            root.style.paddingRight = 4;

            // Row 1: [Enabled] [Name] [Type]
            var row1 = new VisualElement();
            row1.style.flexDirection = FlexDirection.Row;
            row1.style.alignItems = Align.Center;
            row1.style.overflow = Overflow.Hidden;

            var enabledToggle = new Toggle { name = "layer-enabled" };
            enabledToggle.style.width = 18;
            enabledToggle.style.marginRight = 2;
            row1.Add(enabledToggle);

            var nameField = new TextField { name = "layer-name" };
            nameField.style.flexGrow = 1;
            nameField.style.flexShrink = 1;
            nameField.style.minWidth = 0;
            nameField.style.marginRight = 4;
            nameField.style.overflow = Overflow.Hidden;
            row1.Add(nameField);

            var typeLabel = new Label { name = "layer-type" };
            typeLabel.style.width = 16;
            typeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            typeLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            row1.Add(typeLabel);

            root.Add(row1);

            // Row 2: Weight slider
            var weightSlider = new Slider(0f, 1f) { name = "layer-weight", showInputField = true };
            weightSlider.style.marginTop = 1;
            root.Add(weightSlider);

            // Settings foldout (shown only for active layer)
            var foldout = new Foldout { name = "layer-settings" };
            foldout.style.marginTop = 2;
            foldout.style.display = DisplayStyle.None;
            foldout.Add(new IMGUIContainer { name = "layer-settings-imgui" });
            root.Add(foldout);

            return root;
        }

        private void BindLayerItem(VisualElement element, int index)
        {
            if (_layersProp == null || index < 0 || index >= _layersProp.arraySize) return;

            serializedObject.Update();
            var layerProp = _layersProp.GetArrayElementAtIndex(index);
            var enabledProp = layerProp.FindPropertyRelative("_enabled");
            var nameProp = layerProp.FindPropertyRelative("_name");
            var weightProp = layerProp.FindPropertyRelative("_weight");
            var typeProp = layerProp.FindPropertyRelative("_type");

            bool isBrush = typeProp != null && typeProp.enumValueIndex == (int)MeshDeformerLayerType.Brush;
            bool isActive = _activeLayerIndexProp.intValue == index;

            // Row 1
            var enabledToggle = element.Q<Toggle>("layer-enabled");
            enabledToggle.Unbind();
            enabledToggle.BindProperty(enabledProp);

            var nameField = element.Q<TextField>("layer-name");
            nameField.Unbind();
            nameField.BindProperty(nameProp);

            element.Q<Label>("layer-type").text = isBrush ? "B" : "L";

            // Row 2
            var weightSlider = element.Q<Slider>("layer-weight");
            weightSlider.Unbind();
            weightSlider.BindProperty(weightProp);

            // Settings foldout
            var foldout = element.Q<Foldout>("layer-settings");

            // Clean up previous foldout callback
            if (foldout.userData is EventCallback<ChangeEvent<bool>> oldCb)
            {
                foldout.UnregisterValueChangedCallback(oldCb);
                foldout.userData = null;
            }

            foldout.style.display = isActive ? DisplayStyle.Flex : DisplayStyle.None;

            var imgui = element.Q<IMGUIContainer>("layer-settings-imgui");

            if (isActive)
            {
                foldout.text = LatticeLocalization.Tr("Settings");
                foldout.SetValueWithoutNotify(s_showLayerSettings);

                EventCallback<ChangeEvent<bool>> cb = evt =>
                {
                    s_showLayerSettings = evt.newValue;
                    evt.StopPropagation();
                    _layerListView?.schedule.Execute(() => _layerListView?.RefreshItems());
                };
                foldout.RegisterValueChangedCallback(cb);
                foldout.userData = cb;

                imgui.onGUIHandler = () =>
                {
                    if (target is not LatticeDeformer deformer) return;
                    serializedObject.Update();

                    if (isBrush)
                    {
                        DrawBrushLayerSettings(deformer);
                    }
                    else
                    {
                        var settingsProp = GetActiveSettingsProperty(deformer);
                        DrawResetLatticeBoxControls();
                        DrawGridSizeControls(deformer);
                        DrawSettingsExcludingGrid(settingsProp, allowStructureEdits: true);
                        DrawAlignmentSettings();
                    }
                    if (serializedObject.ApplyModifiedProperties())
                        NotifyPropertyChanges();
                };
            }
            else
            {
                imgui.onGUIHandler = null;
            }
        }

        private void OnLayerSelectionChanged()
        {
            if (_layerListView == null || _activeLayerIndexProp == null) return;
            int selected = _layerListView.selectedIndex;
            if (selected < 0 || selected >= _layersProp.arraySize) return;
            if (_activeLayerIndexProp.intValue == selected) return;

            serializedObject.Update();
            _activeLayerIndexProp.intValue = selected;
            serializedObject.ApplyModifiedProperties();
            _cachedActiveIndex = selected;
            _layerListView.RefreshItems();
            NotifyPropertyChanges();
        }

        private void OnLayerReordered(int oldIndex, int newIndex)
        {
            if (target is not LatticeDeformer deformer) return;

            serializedObject.Update();
            _layersProp.MoveArrayElement(oldIndex, newIndex);
            UpdateActiveLayerIndexAfterReorder(oldIndex, newIndex);
            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();
            InitializePendingGridSizes();

            RebuildLayerList();
            NotifyPropertyChanges();
        }

        private void DrawLayerOperationsImgui()
        {
            if (targets.Length != 1) return;
            var deformer = target as LatticeDeformer;
            if (deformer == null) return;

            serializedObject.Update();

            // Dup / Copy / Paste / L-R
            using (new EditorGUI.DisabledScope(_layersProp.arraySize <= 0))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(LatticeLocalization.Tr("Duplicate")))
                    {
                        PerformSingleLayerOperation(deformer, LatticeLocalization.Tr("Duplicate Layer"), instance =>
                        {
                            return instance.DuplicateLayer(instance.ActiveLayerIndex) >= 0;
                        });
                        RebuildLayerList();
                    }
                    if (GUILayout.Button(LatticeLocalization.Tr("Copy")))
                    {
                        CopyLayer(deformer, deformer.ActiveLayerIndex);
                    }
                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(s_copiedLayerJson)))
                    {
                        if (GUILayout.Button(LatticeLocalization.Tr("Paste")))
                        {
                            PasteLayer(deformer);
                            RebuildLayerList();
                        }
                    }
                    // TODO: L/R Operations temporarily disabled
                    // if (EditorGUILayout.DropdownButton(new GUIContent(LatticeLocalization.Tr("L/R Operations")), FocusType.Keyboard, GUILayout.Width(100)))
                    // {
                    //     ShowLROperationsMenu(deformer);
                    // }
                }
            }

            DrawImportBlendShapeUI(deformer);

            serializedObject.ApplyModifiedProperties();
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

        private void DrawBuildOptions()
        {
            s_showOptions = EditorGUILayout.BeginFoldoutHeaderGroup(s_showOptions, LatticeLocalization.Tr("Mesh Rebuild Options"));
            if (s_showOptions)
            {
                EditorGUI.indentLevel++;

                // Compact horizontal toggles
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (_recalcNormalsProp != null)
                        _recalcNormalsProp.boolValue = GUILayout.Toggle(_recalcNormalsProp.boolValue, LatticeLocalization.Tr("Normals"));
                    if (_recalcTangentsProp != null)
                        _recalcTangentsProp.boolValue = GUILayout.Toggle(_recalcTangentsProp.boolValue, LatticeLocalization.Tr("Tangents"));
                    if (_recalcBoundsProp != null)
                        _recalcBoundsProp.boolValue = GUILayout.Toggle(_recalcBoundsProp.boolValue, LatticeLocalization.Tr("Bounds"));
                }

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

                // Weight transfer settings (shown only when bone weight recalculation is enabled)
                if (_recalcBoneWeightsProp != null && _recalcBoneWeightsProp.boolValue && hasSkinnedRenderer)
                {
                    s_showWeightTransferSettings = EditorGUILayout.Foldout(s_showWeightTransferSettings, LatticeLocalization.Tr("Weight Transfer Settings"), true);
                    if (s_showWeightTransferSettings && _weightTransferSettingsProp != null)
                    {
                        EditorGUI.indentLevel++;
                        DrawWeightTransferSettings();
                        EditorGUI.indentLevel--;
                    }
                }

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
        }

        private void MoveLayer(LatticeDeformer deformer, int fromIndex, int toIndex)
        {
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(deformer, "Reorder Layer");
            _layersProp.MoveArrayElement(fromIndex, toIndex);
            UpdateActiveLayerIndexAfterReorder(fromIndex, toIndex);
            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();
            InitializePendingGridSizes();
        }

        private void DeleteLayer(LatticeDeformer deformer, int index)
        {
            if (_layersProp.arraySize <= 0) return;
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(deformer, "Delete Layer");
            _layersProp.DeleteArrayElementAtIndex(index);
            ClampActiveLayerIndexProperty();
            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();
            InitializePendingGridSizes();

            deformer.InvalidateCache();
            deformer.Deform(LatticePreviewUtility.ShouldAssignRuntimeMesh());
            LatticePrefabUtility.MarkModified(deformer);
            LatticePreviewUtility.RequestSceneRepaint();
            SceneView.RepaintAll();
        }

        private void ShowLROperationsMenu(LatticeDeformer deformer)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(LatticeLocalization.Tr("Split L")), false, () =>
                PerformSingleLayerOperation(deformer, LatticeLocalization.Tr("Split Layer Left"), i => { i.SplitLayerByAxis(i.ActiveLayerIndex, 0, false); return true; }));
            menu.AddItem(new GUIContent(LatticeLocalization.Tr("Split R")), false, () =>
                PerformSingleLayerOperation(deformer, LatticeLocalization.Tr("Split Layer Right"), i => { i.SplitLayerByAxis(i.ActiveLayerIndex, 0, true); return true; }));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(LatticeLocalization.Tr("Flip X")), false, () =>
                PerformSingleLayerOperation(deformer, LatticeLocalization.Tr("Flip Layer X"), i => { i.FlipLayerByAxis(i.ActiveLayerIndex, 0); return true; }));
            menu.AddItem(new GUIContent(LatticeLocalization.Tr("Flip Y")), false, () =>
                PerformSingleLayerOperation(deformer, LatticeLocalization.Tr("Flip Layer Y"), i => { i.FlipLayerByAxis(i.ActiveLayerIndex, 1); return true; }));
            menu.AddItem(new GUIContent(LatticeLocalization.Tr("Flip Z")), false, () =>
                PerformSingleLayerOperation(deformer, LatticeLocalization.Tr("Flip Layer Z"), i => { i.FlipLayerByAxis(i.ActiveLayerIndex, 2); return true; }));
            menu.ShowAsContext();
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

        private void CopyLayer(LatticeDeformer deformer, int layerIndex)
        {
            var layers = deformer.Layers;
            if (layerIndex < 0 || layerIndex >= layers.Count) return;
            var layer = layers[layerIndex];
            if (layer == null) return;
            s_copiedLayerJson = JsonUtility.ToJson(layer);
            s_copiedLayerType = layer.Type;
        }

        private void PasteLayer(LatticeDeformer deformer)
        {
            if (string.IsNullOrEmpty(s_copiedLayerJson)) return;

            var newLayer = new LatticeLayer();
            JsonUtility.FromJsonOverwrite(s_copiedLayerJson, newLayer);

            PerformSingleLayerOperation(deformer, LatticeLocalization.Tr("Paste Layer"), instance =>
            {
                return instance.InsertLayer(newLayer) >= 0;
            });
        }

        private static void SyncActiveToolToLayer(LatticeDeformer deformer)
        {
            if (deformer == null) return;
            if (ToolManager.activeToolType == typeof(MeshDeformerTool))
            {
                SceneView.RepaintAll();
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

        private void DrawBlendShapeOutputSection()
        {
            if (_blendShapeOutputProp == null) return;

            s_showBlendShapeOutput = EditorGUILayout.BeginFoldoutHeaderGroup(s_showBlendShapeOutput, LatticeLocalization.Tr("BlendShape Output"));
            if (s_showBlendShapeOutput)
            {
                EditorGUI.indentLevel++;

                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(_blendShapeOutputProp, new GUIContent(LatticeLocalization.Tr("BlendShape Output")));
                bool modeJustChanged = EditorGUI.EndChangeCheck();

                if (_blendShapeOutputProp.intValue == (int)BlendShapeOutputMode.OutputAsBlendShape)
                {
                    // Auto-fill name when first enabling
                    if (modeJustChanged && _blendShapeNameProp != null
                        && string.IsNullOrWhiteSpace(_blendShapeNameProp.stringValue))
                    {
                        var deformer = target as LatticeDeformer;
                        if (deformer != null)
                        {
                            _blendShapeNameProp.stringValue = deformer.gameObject.name;
                        }
                    }

                    if (_blendShapeNameProp != null)
                    {
                        EditorGUILayout.PropertyField(_blendShapeNameProp, new GUIContent(LatticeLocalization.Tr("BlendShape Name")));
                    }

                    // Curve
                    if (_blendShapeCurveProp != null)
                    {
                        EditorGUILayout.PropertyField(_blendShapeCurveProp, new GUIContent(LatticeLocalization.Tr("Curve")));
                    }

                    // Test mode
                    DrawBlendShapeTestMode();
                }

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawBlendShapeTestMode()
        {
            var deformer = target as LatticeDeformer;
            if (deformer == null) return;

            var smr = deformer.GetComponent<SkinnedMeshRenderer>();
            if (smr == null) return;

            EditorGUILayout.Space(2);

            if (!_blendShapeTestMode)
            {
                if (GUILayout.Button(LatticeLocalization.Tr("Enter Test Mode")))
                {
                    EnterBlendShapeTestMode(deformer, smr);
                }
            }
            else
            {
                EditorGUILayout.HelpBox(LatticeLocalization.Tr("BlendShape Test Mode"), MessageType.Info);

                EditorGUI.BeginChangeCheck();
                _blendShapeTestWeight = EditorGUILayout.Slider(
                    new GUIContent(LatticeLocalization.Tr("Test Weight")),
                    _blendShapeTestWeight, 0f, 100f);
                if (EditorGUI.EndChangeCheck())
                {
                    ApplyBlendShapeTestWeight(deformer, smr);
                }

                if (GUILayout.Button(LatticeLocalization.Tr("Exit Test Mode")))
                {
                    ExitBlendShapeTestMode();
                }
            }
        }

        private void EnterBlendShapeTestMode(LatticeDeformer deformer, SkinnedMeshRenderer smr)
        {
            _preTestMesh = smr.sharedMesh;

            // Record which properties were already overridden before test mode
            if (PrefabUtility.IsPartOfPrefabInstance(smr))
            {
                var so = new SerializedObject(smr);
                var meshProp = so.FindProperty("m_Mesh");
                var weightsProp = so.FindProperty("m_BlendShapeWeights");
                _preTestMeshWasOverridden = meshProp != null && meshProp.prefabOverride;
                _preTestWeightsWereOverridden = weightsProp != null && weightsProp.prefabOverride;
            }

            _blendShapeTestMode = true;
            _blendShapeTestWeight = 0f;

            // Force Deform with assignment so the BlendShape is on the SMR
            deformer.InvalidateCache();
            deformer.Deform(true);
            SceneView.RepaintAll();
        }

        private void ExitBlendShapeTestMode()
        {
            if (!_blendShapeTestMode) return;
            _blendShapeTestMode = false;
            _blendShapeTestWeight = 0f;

            if (target is LatticeDeformer deformer)
            {
                var smr = deformer.GetComponent<SkinnedMeshRenderer>();
                if (smr != null)
                {
                    // Restore only what test mode changed: sharedMesh
                    if (_preTestMesh != null)
                    {
                        smr.sharedMesh = _preTestMesh;
                    }

                    // Revert only the prefab overrides that test mode created
                    if (PrefabUtility.IsPartOfPrefabInstance(smr))
                    {
                        var so = new SerializedObject(smr);
                        if (!_preTestMeshWasOverridden)
                        {
                            var meshProp = so.FindProperty("m_Mesh");
                            if (meshProp != null && meshProp.prefabOverride)
                                PrefabUtility.RevertPropertyOverride(meshProp, InteractionMode.AutomatedAction);
                        }
                        if (!_preTestWeightsWereOverridden)
                        {
                            var weightsProp = so.FindProperty("m_BlendShapeWeights");
                            if (weightsProp != null && weightsProp.prefabOverride)
                                PrefabUtility.RevertPropertyOverride(weightsProp, InteractionMode.AutomatedAction);
                        }
                    }
                }
            }

            _preTestMesh = null;
            _preTestMeshWasOverridden = false;
            _preTestWeightsWereOverridden = false;
            SceneView.RepaintAll();
        }

        private void ApplyBlendShapeTestWeight(LatticeDeformer deformer, SkinnedMeshRenderer smr)
        {
            var runtimeMesh = deformer.RuntimeMesh;
            if (runtimeMesh == null) return;

            string shapeName = deformer.EffectiveBlendShapeName;
            int shapeIndex = runtimeMesh.GetBlendShapeIndex(shapeName);
            if (shapeIndex < 0) return;

            smr.SetBlendShapeWeight(shapeIndex, _blendShapeTestWeight);
            SceneView.RepaintAll();
        }

        private void DrawImportBlendShapeUI(LatticeDeformer deformer)
        {
            if (deformer == null)
            {
                return;
            }

            var blendShapeNames = deformer.GetSourceBlendShapeNames();
            if (blendShapeNames == null || blendShapeNames.Length == 0)
            {
                return;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(LatticeLocalization.Tr("Import BlendShape"));

            if (EditorGUILayout.DropdownButton(new GUIContent(LatticeLocalization.Tr("Select...")), FocusType.Keyboard))
            {
                var menu = new GenericMenu();
                for (int i = 0; i < blendShapeNames.Length; i++)
                {
                    int index = i;
                    menu.AddItem(new GUIContent(blendShapeNames[i]), false, () =>
                    {
                        Undo.RecordObject(deformer, "Import BlendShape");
                        int newLayerIndex = deformer.ImportBlendShapeAsLayer(index);
                        if (newLayerIndex >= 0)
                        {
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
                    });
                }
                menu.ShowAsContext();
            }

            EditorGUILayout.EndHorizontal();
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




