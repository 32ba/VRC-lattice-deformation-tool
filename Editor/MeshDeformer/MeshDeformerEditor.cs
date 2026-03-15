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
        private SerializedProperty _groupsProp;
        private SerializedProperty _activeGroupIndexProp;
        // These are resolved per-frame from the active group
        private SerializedProperty _layersProp;
        private SerializedProperty _activeLayerIndexProp;
        private SerializedProperty _blendShapeOutputProp;
        private SerializedProperty _blendShapeNameProp;
        private SerializedProperty _blendShapeCurveProp;
        private SerializedProperty _skinnedRendererProp;
        private SerializedProperty _meshFilterProp;
        private SerializedProperty _recalcNormalsProp;
        private SerializedProperty _recalcTangentsProp;
        private SerializedProperty _recalcBoundsProp;
        private SerializedProperty _recalcBoneWeightsProp;
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
            _groupsProp = serializedObject.FindProperty("_groups");
            _activeGroupIndexProp = serializedObject.FindProperty("_activeGroupIndex");
            _skinnedRendererProp = serializedObject.FindProperty("_skinnedMeshRenderer");
            _meshFilterProp = serializedObject.FindProperty("_meshFilter");
            _recalcNormalsProp = serializedObject.FindProperty("_recalculateNormals");
            _recalcTangentsProp = serializedObject.FindProperty("_recalculateTangents");
            _recalcBoundsProp = serializedObject.FindProperty("_recalculateBounds");
            _recalcBoneWeightsProp = serializedObject.FindProperty("_recalculateBoneWeights");
            _weightTransferSettingsProp = serializedObject.FindProperty("_weightTransferSettings");
            ResolveActiveGroupProperties();
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

        private void ResolveActiveGroupProperties()
        {
            _layersProp = null;
            _activeLayerIndexProp = null;
            _blendShapeOutputProp = null;
            _blendShapeNameProp = null;
            _blendShapeCurveProp = null;

            if (_groupsProp == null || _activeGroupIndexProp == null) return;
            int groupIndex = _activeGroupIndexProp.intValue;
            if (groupIndex < 0 || groupIndex >= _groupsProp.arraySize) return;

            var groupProp = _groupsProp.GetArrayElementAtIndex(groupIndex);
            if (groupProp == null) return;

            _layersProp = groupProp.FindPropertyRelative("_layers");
            _activeLayerIndexProp = groupProp.FindPropertyRelative("_activeLayerIndex");
            _blendShapeOutputProp = groupProp.FindPropertyRelative("_blendShapeOutput");
            _blendShapeNameProp = groupProp.FindPropertyRelative("_blendShapeName");
            _blendShapeCurveProp = groupProp.FindPropertyRelative("_blendShapeCurve");
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

            // Groups > Layers nested structure (UI Toolkit)
            if (targets.Length == 1)
            {
                _groupsContainer = new VisualElement();
                _groupsContainer.style.marginTop = 4;
                RebuildGroupList();
                root.Add(_groupsContainer);
            }

            // Build Options + Open Editor (IMGUI)
            root.Add(new IMGUIContainer(DrawBottomSection));

            // Track serialized changes
            root.TrackSerializedObjectValue(serializedObject, _ =>
            {
                CheckAndRebuildLayers();
                NotifyPropertyChanges();
            });
            root.Bind(serializedObject);

            return root;
        }

        private VisualElement _groupsContainer;
        private ListView _groupListView;
        private readonly List<int> _groupIndices = new();
        private int _cachedGroupCount = -1;

        private void RebuildGroupList()
        {
            if (_groupsContainer == null) return;
            serializedObject.Update();
            _groupsContainer.Clear();

            if (_groupsProp == null) return;
            int groupCount = _groupsProp.arraySize;
            _cachedGroupCount = groupCount;

            // Groups label
            var groupsLabel = new Label(LatticeLocalization.Tr(LocKey.DeformationGroups));
            groupsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            groupsLabel.style.marginLeft = 3;
            groupsLabel.style.marginBottom = 2;
            _groupsContainer.Add(groupsLabel);

            // Group ListView
            _groupListView = new ListView
            {
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                showBorder = true,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                selectionType = SelectionType.Single,
            };
            _groupListView.makeItem = MakeGroupItem;
            _groupListView.bindItem = BindGroupItem;
            _groupListView.itemIndexChanged += OnGroupReordered;
            _groupListView.selectionChanged += _ => OnGroupSelectionChanged();

            _groupIndices.Clear();
            for (int i = 0; i < groupCount; i++)
                _groupIndices.Add(i);
            _groupListView.itemsSource = _groupIndices;

            int activeGroupIdx = _activeGroupIndexProp != null ? _activeGroupIndexProp.intValue : 0;
            _groupListView.selectedIndex = Mathf.Clamp(activeGroupIdx, 0, Mathf.Max(0, groupCount - 1));

            _groupsContainer.Add(_groupListView);

            // Group footer: [+] [-]
            var groupFooter = new VisualElement();
            groupFooter.style.flexDirection = FlexDirection.Row;
            groupFooter.style.justifyContent = Justify.FlexEnd;
            groupFooter.style.marginTop = -2;
            groupFooter.style.marginRight = 2;
            groupFooter.style.marginBottom = 4;

            var addGroupBtn = new Button(() =>
            {
                if (target is LatticeDeformer d)
                {
                    Undo.RecordObject(d, "Add Group");
                    d.AddGroup();
                    EditorUtility.SetDirty(d);
                    serializedObject.Update();
                    ResolveActiveGroupProperties();
                    RebuildGroupList();
                    NotifyPropertyChanges();
                }
            }) { text = "+" };
            addGroupBtn.style.width = 25;
            addGroupBtn.style.height = 16;
            addGroupBtn.style.fontSize = 14;
            addGroupBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            addGroupBtn.style.paddingTop = 0;
            addGroupBtn.style.paddingBottom = 0;
            groupFooter.Add(addGroupBtn);

            var removeGroupBtn = new Button(() =>
            {
                if (target is LatticeDeformer d && d.GroupCount > 1)
                {
                    Undo.RecordObject(d, "Remove Group");
                    d.RemoveGroup(d.ActiveGroupIndex);
                    EditorUtility.SetDirty(d);
                    serializedObject.Update();
                    ResolveActiveGroupProperties();
                    RebuildGroupList();
                    NotifyPropertyChanges();
                }
            }) { text = "\u2212" };
            removeGroupBtn.style.width = 25;
            removeGroupBtn.style.height = 16;
            removeGroupBtn.style.fontSize = 14;
            removeGroupBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            removeGroupBtn.style.paddingTop = 0;
            removeGroupBtn.style.paddingBottom = 0;
            groupFooter.Add(removeGroupBtn);

            _groupsContainer.Add(groupFooter);
        }

        private VisualElement MakeGroupItem()
        {
            var root = new VisualElement();
            root.style.paddingTop = 2;
            root.style.paddingBottom = 2;
            root.style.paddingLeft = 4;
            root.style.paddingRight = 4;

            // Row 1: Group name
            var nameField = new TextField();
            nameField.name = "group-name";
            nameField.style.flexGrow = 1;
            nameField.style.flexShrink = 1;
            nameField.style.minWidth = 0;
            nameField.style.overflow = Overflow.Hidden;
            root.Add(nameField);

            // BlendShape output settings (per group)
            var blendShapeContainer = new VisualElement();
            blendShapeContainer.name = "blendshape-container";
            blendShapeContainer.style.marginTop = 2;
            root.Add(blendShapeContainer);

            // Nested layer list container (populated in bind for active group)
            var layerContainer = new VisualElement();
            layerContainer.name = "layer-container";
            layerContainer.style.marginTop = 4;
            root.Add(layerContainer);

            return root;
        }

        private void BindGroupItem(VisualElement element, int index)
        {
            serializedObject.Update();
            if (_groupsProp == null || index < 0 || index >= _groupsProp.arraySize) return;

            var groupProp = _groupsProp.GetArrayElementAtIndex(index);
            var groupNameProp = groupProp.FindPropertyRelative("_name");
            int activeGroupIdx = _activeGroupIndexProp != null ? _activeGroupIndexProp.intValue : 0;
            bool isActive = index == activeGroupIdx;

            // Name field
            var nameField = element.Q<TextField>("group-name");
            if (nameField != null)
            {
                nameField.SetValueWithoutNotify(groupNameProp != null ? groupNameProp.stringValue : "Group");
                // Unregister previous callback
                if (nameField.userData is EventCallback<ChangeEvent<string>> oldCb)
                    nameField.UnregisterValueChangedCallback(oldCb);
                EventCallback<ChangeEvent<string>> nameCb = evt =>
                {
                    if (groupNameProp != null)
                    {
                        serializedObject.Update();
                        groupNameProp.stringValue = evt.newValue;
                        serializedObject.ApplyModifiedProperties();
                    }
                };
                nameField.RegisterValueChangedCallback(nameCb);
                nameField.userData = nameCb;
            }

            // Active group highlight
            element.style.borderLeftWidth = isActive ? 2 : 0;
            element.style.borderLeftColor = new Color(0.3f, 0.6f, 1f, 0.8f);

            // BlendShape output settings (per group)
            var bsContainer = element.Q("blendshape-container");
            if (bsContainer != null)
            {
                bsContainer.Clear();
                int capturedGroupIndex = index;
                bsContainer.Add(new IMGUIContainer(() =>
                {
                    DrawGroupBlendShapeSection(capturedGroupIndex);
                }));
            }

            // Layer container
            var layerContainer = element.Q("layer-container");
            if (layerContainer == null) return;
            layerContainer.Clear();

            if (isActive)
            {
                // Layers label
                var layersLabel = new Label(LatticeLocalization.Tr(LocKey.Layers));
                layersLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                layersLabel.style.marginLeft = 2;
                layersLabel.style.marginBottom = 2;
                layerContainer.Add(layersLabel);

                // Layer ListView for active group
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

                ResolveActiveGroupProperties();
                RebuildLayerListInternal();
                layerContainer.Add(_layerListView);

                // Layer footer: [+] [-]
                var layerFooter = new VisualElement();
                layerFooter.style.flexDirection = FlexDirection.Row;
                layerFooter.style.justifyContent = Justify.FlexEnd;
                layerFooter.style.marginTop = -2;
                layerFooter.style.marginRight = 2;
                layerFooter.style.marginBottom = 2;

                var addLayerBtn = new Button(() =>
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent(LatticeLocalization.Tr(LocKey.AddLatticeLayer)), false, () =>
                    {
                        AddLayerViaList(MeshDeformerLayerType.Lattice);
                        RebuildGroupList();
                    });
                    menu.AddItem(new GUIContent(LatticeLocalization.Tr(LocKey.AddBrushLayer)), false, () =>
                    {
                        AddLayerViaList(MeshDeformerLayerType.Brush);
                        RebuildGroupList();
                    });
                    menu.ShowAsContext();
                }) { text = "+" };
                addLayerBtn.style.width = 25;
                addLayerBtn.style.height = 16;
                addLayerBtn.style.fontSize = 14;
                addLayerBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
                addLayerBtn.style.paddingTop = 0;
                addLayerBtn.style.paddingBottom = 0;
                layerFooter.Add(addLayerBtn);

                var removeLayerBtn = new Button(() =>
                {
                    ResolveActiveGroupProperties();
                    if (target is LatticeDeformer d && _layersProp != null && _layersProp.arraySize > 0)
                    {
                        DeleteLayer(d, _activeLayerIndexProp.intValue);
                        RebuildGroupList();
                    }
                }) { text = "\u2212" };
                removeLayerBtn.style.width = 25;
                removeLayerBtn.style.height = 16;
                removeLayerBtn.style.fontSize = 14;
                removeLayerBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
                removeLayerBtn.style.paddingTop = 0;
                removeLayerBtn.style.paddingBottom = 0;
                layerFooter.Add(removeLayerBtn);

                layerContainer.Add(layerFooter);
                layerContainer.Add(new IMGUIContainer(DrawLayerOperationsImgui));
            }
            else
            {
                // Inactive: show layer count
                var groupLayersProp = groupProp.FindPropertyRelative("_layers");
                int layerCount = groupLayersProp != null ? groupLayersProp.arraySize : 0;
                var summary = new Label($"{layerCount} layer(s)");
                summary.style.color = new Color(0.6f, 0.6f, 0.6f);
                summary.style.marginLeft = 4;
                summary.style.marginTop = 2;
                layerContainer.Add(summary);
            }
        }

        private void OnGroupReordered(int oldIndex, int newIndex)
        {
            if (target is not LatticeDeformer d) return;
            Undo.RecordObject(d, "Reorder Group");

            serializedObject.Update();
            _groupsProp.MoveArrayElement(oldIndex, newIndex);
            // Adjust active group index
            int active = _activeGroupIndexProp.intValue;
            if (active == oldIndex)
                _activeGroupIndexProp.intValue = newIndex;
            else if (oldIndex < active && newIndex >= active)
                _activeGroupIndexProp.intValue = active - 1;
            else if (oldIndex > active && newIndex <= active)
                _activeGroupIndexProp.intValue = active + 1;
            serializedObject.ApplyModifiedProperties();
            ResolveActiveGroupProperties();
            NotifyPropertyChanges();
        }

        private void OnGroupSelectionChanged()
        {
            if (_groupListView == null || _activeGroupIndexProp == null) return;
            int selected = _groupListView.selectedIndex;
            if (selected < 0 || selected >= _groupsProp.arraySize) return;
            if (_activeGroupIndexProp.intValue == selected) return;

            serializedObject.Update();
            _activeGroupIndexProp.intValue = selected;
            serializedObject.ApplyModifiedProperties();
            ResolveActiveGroupProperties();
            RebuildGroupList();
            NotifyPropertyChanges();
        }

        private void RebuildLayerListInternal()
        {
            if (_layerListView == null || _layersProp == null) return;

            _cachedLayerCount = _layersProp.arraySize;
            _cachedActiveIndex = _activeLayerIndexProp?.intValue ?? 0;

            _layerIndices.Clear();
            for (int i = 0; i < _layersProp.arraySize; i++)
                _layerIndices.Add(i);

            _layerListView.itemsSource = _layerIndices;
            _layerListView.selectedIndex = _cachedActiveIndex;
            _layerListView.Rebuild();
        }

        private void DrawTopSection()
        {
            AutoAssignLocalRendererReferences();
            serializedObject.Update();
            ResolveActiveGroupProperties();

            bool hasSkinnedAssigned = _skinnedRendererProp != null && !_skinnedRendererProp.hasMultipleDifferentValues && _skinnedRendererProp.objectReferenceValue != null;
            bool hasMeshAssigned = _meshFilterProp != null && !_meshFilterProp.hasMultipleDifferentValues && _meshFilterProp.objectReferenceValue != null;
            bool disableSkinnedField = ShouldDisableRendererField<SkinnedMeshRenderer>() || hasMeshAssigned;
            bool disableMeshField = ShouldDisableRendererField<MeshFilter>() || hasSkinnedAssigned;

            DrawLanguageSelector();
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(disableSkinnedField))
            {
                EditorGUILayout.PropertyField(_skinnedRendererProp, LatticeLocalization.Content(LocKey.SkinnedMeshSource));
            }

            using (new EditorGUI.DisabledScope(disableMeshField))
            {
                EditorGUILayout.PropertyField(_meshFilterProp, LatticeLocalization.Content(LocKey.StaticMeshSource));
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawBottomSection()
        {
            serializedObject.Update();
            ResolveActiveGroupProperties();

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
                    ? LatticeLocalization.Tr(LocKey.OpenBrushEditor)
                    : LatticeLocalization.Tr(LocKey.OpenLatticeEditor)))
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
            serializedObject.Update();
            ResolveActiveGroupProperties();

            // Check if group count changed
            int groupCount = _groupsProp != null ? _groupsProp.arraySize : 0;
            if (groupCount != _cachedGroupCount)
            {
                RebuildGroupList();
                return;
            }

            // Check if active group's layer count/index changed
            if (_layersProp == null || _activeLayerIndexProp == null || _layerListView == null) return;
            int count = _layersProp.arraySize;
            int active = _activeLayerIndexProp.intValue;
            if (count != _cachedLayerCount || active != _cachedActiveIndex)
            {
                RebuildGroupList();
            }
        }

        private void RebuildLayerList()
        {
            RebuildGroupList();
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
            serializedObject.Update();
            ResolveActiveGroupProperties();
            if (_layersProp == null || index < 0 || index >= _layersProp.arraySize) return;
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
                foldout.text = LatticeLocalization.Tr(LocKey.Settings);
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
            ResolveActiveGroupProperties();

            // Dup / Copy / Paste / L-R
            using (new EditorGUI.DisabledScope(_layersProp.arraySize <= 0))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(LatticeLocalization.Tr(LocKey.Duplicate)))
                    {
                        PerformSingleLayerOperation(deformer, LatticeLocalization.Tr(LocKey.DuplicateLayer), instance =>
                        {
                            return instance.DuplicateLayer(instance.ActiveLayerIndex) >= 0;
                        });
                        RebuildLayerList();
                    }
                    if (GUILayout.Button(LatticeLocalization.Tr(LocKey.Copy)))
                    {
                        CopyLayer(deformer, deformer.ActiveLayerIndex);
                    }
                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(s_copiedLayerJson)))
                    {
                        if (GUILayout.Button(LatticeLocalization.Tr(LocKey.Paste)))
                        {
                            PasteLayer(deformer);
                            RebuildLayerList();
                        }
                    }
                    // TODO: L/R Operations temporarily disabled
                    // if (EditorGUILayout.DropdownButton(new GUIContent(LatticeLocalization.Tr(LocKey.LROperations)), FocusType.Keyboard, GUILayout.Width(100)))
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
                string resetLabel = LatticeLocalization.Tr(LocKey.ResetActiveLayer);

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

            Undo.RecordObject(deformer, LatticeLocalization.Tr(LocKey.ResetLatticeCage));

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
            int next = EditorGUILayout.Popup(LatticeLocalization.Content(LocKey.ToolLanguage), current, LatticeLocalization.DisplayNames);
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
            EditorGUILayout.HelpBox(LatticeLocalization.Tr(LocKey.BrushLayerInfo), MessageType.Info);

            int vertexCount = deformer != null && deformer.SourceMesh != null ? deformer.SourceMesh.vertexCount : 0;
            EditorGUILayout.LabelField(LatticeLocalization.Tr(LocKey.VertexCount), vertexCount.ToString());

            if (deformer == null)
            {
                return;
            }

            EditorGUI.BeginDisabledGroup(deformer.ActiveLayerType != MeshDeformerLayerType.Brush);
            if (GUILayout.Button(LatticeLocalization.Tr(LocKey.ClearActiveLayerDisplacements)))
            {
                Undo.RecordObject(deformer, LatticeLocalization.Tr(LocKey.ClearActiveLayerDisplacements));
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
            s_showOptions = EditorGUILayout.BeginFoldoutHeaderGroup(s_showOptions, LatticeLocalization.Tr(LocKey.MeshRebuildOptions));
            if (s_showOptions)
            {
                EditorGUI.indentLevel++;

                // Compact horizontal toggles
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (_recalcNormalsProp != null)
                        _recalcNormalsProp.boolValue = GUILayout.Toggle(_recalcNormalsProp.boolValue, LatticeLocalization.Tr(LocKey.Normals));
                    if (_recalcTangentsProp != null)
                        _recalcTangentsProp.boolValue = GUILayout.Toggle(_recalcTangentsProp.boolValue, LatticeLocalization.Tr(LocKey.Tangents));
                    if (_recalcBoundsProp != null)
                        _recalcBoundsProp.boolValue = GUILayout.Toggle(_recalcBoundsProp.boolValue, LatticeLocalization.Tr(LocKey.Bounds));
                }

                // Bone weight recalculation (only for SkinnedMeshRenderer)
                bool hasSkinnedRenderer = _skinnedRendererProp != null &&
                    !_skinnedRendererProp.hasMultipleDifferentValues &&
                    _skinnedRendererProp.objectReferenceValue != null;

                using (new EditorGUI.DisabledScope(!hasSkinnedRenderer))
                {
                    EditorGUILayout.PropertyField(_recalcBoneWeightsProp, LatticeLocalization.Content(LocKey.RecalculateBoneWeights));
                }

                if (!hasSkinnedRenderer && _recalcBoneWeightsProp != null && _recalcBoneWeightsProp.boolValue)
                {
                    EditorGUILayout.HelpBox(LatticeLocalization.Tr(LocKey.BoneWeightRequiresSMR), MessageType.Info);
                }

                // Weight transfer settings (shown only when bone weight recalculation is enabled)
                if (_recalcBoneWeightsProp != null && _recalcBoneWeightsProp.boolValue && hasSkinnedRenderer)
                {
                    s_showWeightTransferSettings = EditorGUILayout.Foldout(s_showWeightTransferSettings, LatticeLocalization.Tr(LocKey.WeightTransferSettings), true);
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
                    ? LatticeLocalization.Tr(LocKey.NDMFDisableMeshPreview)
                    : LatticeLocalization.Tr(LocKey.NDMFEnableMeshPreview);
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
            menu.AddItem(new GUIContent(LatticeLocalization.Tr(LocKey.SplitL)), false, () =>
                PerformSingleLayerOperation(deformer, LatticeLocalization.Tr(LocKey.SplitLayerLeft), i => { i.SplitLayerByAxis(i.ActiveLayerIndex, 0, false); return true; }));
            menu.AddItem(new GUIContent(LatticeLocalization.Tr(LocKey.SplitR)), false, () =>
                PerformSingleLayerOperation(deformer, LatticeLocalization.Tr(LocKey.SplitLayerRight), i => { i.SplitLayerByAxis(i.ActiveLayerIndex, 0, true); return true; }));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(LatticeLocalization.Tr(LocKey.FlipX)), false, () =>
                PerformSingleLayerOperation(deformer, LatticeLocalization.Tr(LocKey.FlipLayerX), i => { i.FlipLayerByAxis(i.ActiveLayerIndex, 0); return true; }));
            menu.AddItem(new GUIContent(LatticeLocalization.Tr(LocKey.FlipY)), false, () =>
                PerformSingleLayerOperation(deformer, LatticeLocalization.Tr(LocKey.FlipLayerY), i => { i.FlipLayerByAxis(i.ActiveLayerIndex, 1); return true; }));
            menu.AddItem(new GUIContent(LatticeLocalization.Tr(LocKey.FlipZ)), false, () =>
                PerformSingleLayerOperation(deformer, LatticeLocalization.Tr(LocKey.FlipLayerZ), i => { i.FlipLayerByAxis(i.ActiveLayerIndex, 2); return true; }));
            menu.ShowAsContext();
        }

        private void AddLayerViaList(MeshDeformerLayerType layerType)
        {
            if (target is not LatticeDeformer deformer)
            {
                return;
            }

            string undoLabel = layerType == MeshDeformerLayerType.Brush
                ? LatticeLocalization.Tr(LocKey.AddBrushLayer)
                : LatticeLocalization.Tr(LocKey.AddLatticeLayer);

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

            PerformSingleLayerOperation(deformer, LatticeLocalization.Tr(LocKey.PasteLayer), instance =>
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

            Undo.RecordObject(deformer, LatticeLocalization.Tr(LocKey.ChangeLatticeDivisions));
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
                EditorGUILayout.HelpBox(LatticeLocalization.Tr(LocKey.NoLatticeDeformerSelected), MessageType.Info);
                return;
            }

            var settings = deformer.EditingSettings;
            if (settings == null)
            {
                EditorGUILayout.HelpBox(LatticeLocalization.Tr(LocKey.NoLatticeAssetAssigned), MessageType.Warning);
                return;
            }

            long pendingKey = GetPendingGridKey(deformer);
            if (!s_pendingGridSizes.TryGetValue(pendingKey, out var pending))
            {
                pending = settings.GridSize;
                s_pendingGridSizes[pendingKey] = pending;
            }

            EditorGUILayout.LabelField(LatticeLocalization.Content(LocKey.CurrentGridDivisions), new GUIContent(settings.GridSize.ToString()));
            EditorGUI.BeginChangeCheck();
            pending = EditorGUILayout.Vector3IntField(LatticeLocalization.Tr(LocKey.PendingGridDivisions), pending);
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
                    if (GUILayout.Button(LatticeLocalization.Tr(LocKey.Apply), GUILayout.Width(80f)))
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

                    if (GUILayout.Button(LatticeLocalization.Tr(LocKey.Revert), GUILayout.Width(80f)))
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
            EditorGUILayout.LabelField(LatticeLocalization.Tr(LocKey.Stage1InitialTransfer), EditorStyles.boldLabel);

            var maxDistProp = _weightTransferSettingsProp.FindPropertyRelative("maxTransferDistance");
            if (maxDistProp != null)
            {
                EditorGUILayout.PropertyField(
                    maxDistProp,
                    LatticeLocalization.Content(
                        LocKey.MaxTransferDistance,
                        "If weights stick to the wrong surface, try lowering this value or the Normal Angle Threshold for stricter matching."));
            }

            var normalThresholdProp = _weightTransferSettingsProp.FindPropertyRelative("normalAngleThreshold");
            if (normalThresholdProp != null)
            {
                EditorGUILayout.PropertyField(
                    normalThresholdProp,
                    LatticeLocalization.Content(
                        LocKey.NormalAngleThreshold,
                        "If weights stick to the wrong surface, try lowering this value or the Max Transfer Distance for stricter matching."));
            }

            EditorGUILayout.Space(4);

            // Stage 2 settings
            EditorGUILayout.LabelField(LatticeLocalization.Tr(LocKey.Stage2WeightInpainting), EditorStyles.boldLabel);

            var enableInpaintingProp = _weightTransferSettingsProp.FindPropertyRelative("enableInpainting");
            if (enableInpaintingProp != null)
            {
                EditorGUILayout.PropertyField(enableInpaintingProp, LatticeLocalization.Content(LocKey.EnableInpainting));

                if (enableInpaintingProp.boolValue)
                {
                    EditorGUI.indentLevel++;

                    var maxIterProp = _weightTransferSettingsProp.FindPropertyRelative("maxIterations");
                    if (maxIterProp != null)
                    {
                        EditorGUILayout.PropertyField(maxIterProp, LatticeLocalization.Content(LocKey.MaxIterations));
                    }

                    var toleranceProp = _weightTransferSettingsProp.FindPropertyRelative("tolerance");
                    if (toleranceProp != null)
                    {
                        EditorGUILayout.PropertyField(toleranceProp, LatticeLocalization.Content(LocKey.Tolerance));
                    }

                    EditorGUI.indentLevel--;
                }
            }
        }

        private void DrawAlignmentSettings()
        {
            s_showAlignSettings = EditorGUILayout.Foldout(s_showAlignSettings, LatticeLocalization.Tr(LocKey.LatticeCageAlignment), true);
            if (!s_showAlignSettings)
            {
                return;
            }

            EditorGUI.indentLevel++;

            EditorGUILayout.HelpBox(
                LatticeLocalization.Tr(LocKey.AlignmentCageInfo),
                MessageType.Info);

            if (_manualOffsetProp != null)
            {
                EditorGUILayout.PropertyField(_manualOffsetProp,
                    new GUIContent(LatticeLocalization.Tr(LocKey.Offset),
                        LatticeLocalization.Tr(LocKey.OffsetTooltip)));
            }

            if (_manualScaleProp != null)
            {
                DrawLinkedScaleField(_manualScaleProp, ref s_linkManualScale, LatticeLocalization.Tr(LocKey.Scale));
            }

            bool debugAlign = LatticePreviewUtility.DebugAlignLogs;
            bool nextDebug = EditorGUILayout.ToggleLeft(
                new GUIContent(LatticeLocalization.Tr(LocKey.DebugLogAlignment)),
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

        private void DrawGroupBlendShapeSection(int groupIndex)
        {
            serializedObject.Update();
            if (_groupsProp == null || groupIndex < 0 || groupIndex >= _groupsProp.arraySize) return;

            var groupProp = _groupsProp.GetArrayElementAtIndex(groupIndex);
            var outputProp = groupProp.FindPropertyRelative("_blendShapeOutput");
            var nameProp = groupProp.FindPropertyRelative("_blendShapeName");
            var curveProp = groupProp.FindPropertyRelative("_blendShapeCurve");
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

                // Test mode only for active group
                int activeGroupIdx = _activeGroupIndexProp != null ? _activeGroupIndexProp.intValue : 0;
                if (groupIndex == activeGroupIdx)
                    DrawBlendShapeTestMode();
            }

            if (serializedObject.ApplyModifiedProperties())
                NotifyPropertyChanges();
        }

        // Keep for backward compat — no longer called from DrawBottomSection
        private void DrawBlendShapeOutputSection()
        {
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
                if (GUILayout.Button(LatticeLocalization.Tr(LocKey.EnterTestMode)))
                {
                    EnterBlendShapeTestMode(deformer, smr);
                }
            }
            else
            {
                EditorGUILayout.HelpBox(LatticeLocalization.Tr(LocKey.BlendShapeTestMode), MessageType.Info);

                EditorGUI.BeginChangeCheck();
                _blendShapeTestWeight = EditorGUILayout.Slider(
                    new GUIContent(LatticeLocalization.Tr(LocKey.TestWeight)),
                    _blendShapeTestWeight, 0f, 100f);
                if (EditorGUI.EndChangeCheck())
                {
                    ApplyBlendShapeTestWeight(deformer, smr);
                }

                if (GUILayout.Button(LatticeLocalization.Tr(LocKey.ExitTestMode)))
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
            EditorGUILayout.PrefixLabel(LatticeLocalization.Tr(LocKey.ImportBlendShape));

            if (EditorGUILayout.DropdownButton(new GUIContent(LatticeLocalization.Tr(LocKey.Select)), FocusType.Keyboard))
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




