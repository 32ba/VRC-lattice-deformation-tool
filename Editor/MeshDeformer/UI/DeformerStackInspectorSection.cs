#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>Owns nested list presentation and selection; detail sections are composed by the Inspector.</summary>
    internal sealed class DeformerStackInspectorSection : IDisposable
    {
        private readonly UnityEditor.Editor _owner;
        private readonly Action<int> _drawGroupSettings;
        private readonly Action _drawLayerSettings, _drawOperations, _onStructureChanged, _onPropertyChanged;
        private readonly Dictionary<VisualElement, Action> _refreshFields = new(), _unbindFields = new();
        private readonly SerializedProperty _groupsProp, _activeGroupIndexProp;
        private SerializedProperty _layersProp, _activeLayerIndexProp;
        private readonly VisualElement _groupsContainer = new VisualElement();
        private ListView _groupListView, _layerListView;
        private readonly List<int> _groupIndices = new(), _layerIndices = new();
        private int _cachedGroupCount, _cachedLayerCount, _cachedActiveIndex;
        private bool _cachedProfileReadOnly, _disposed;
        private int _generation;
        private DeformerStackStructure _structure;
        private static bool s_showLayerSettings;
        private static string s_copiedLayerJson, s_copiedGroupJson;
        private UnityEngine.Object target => _owner != null ? _owner.target : null;
        private SerializedObject serializedObject => _owner.serializedObject;
        internal VisualElement Root => _groupsContainer;
        internal ListView GroupList => _groupListView;
        internal ListView LayerList => _layerListView;
        internal bool ProfileReadOnly => target is LatticeDeformer d &&
            SerializedDeformerReader.Read(d).DataSource == DeformerDataSource.Profile;

        internal DeformerStackInspectorSection(UnityEditor.Editor owner, Action<int> drawGroupSettings,
            Action drawLayerSettings, Action drawOperations, Action onStructureChanged, Action onPropertyChanged)
        {
            _owner = owner; _drawGroupSettings = drawGroupSettings; _drawLayerSettings = drawLayerSettings;
            _drawOperations = drawOperations; _onStructureChanged = onStructureChanged;
            _onPropertyChanged = onPropertyChanged;
            _groupsProp = serializedObject.FindProperty("_groups");
            _activeGroupIndexProp = serializedObject.FindProperty("_activeGroupIndex");
            _groupsContainer.style.marginTop = 4;
        }

        private bool IsCurrent(int generation = -1) => !_disposed && target != null &&
            (generation < 0 || generation == _generation) &&
            (_structure == null || _structure.Matches(target as LatticeDeformer));

        private void ResolveActiveGroupProperties()
        {
            _layersProp = null; _activeLayerIndexProp = null;
            int index = _activeGroupIndexProp.intValue;
            if (index < 0 || index >= _groupsProp.arraySize) return;
            var group = _groupsProp.GetArrayElementAtIndex(index);
            _layersProp = group.FindPropertyRelative("_layers");
            _activeLayerIndexProp = group.FindPropertyRelative("_activeLayerIndex");
        }

        public void Dispose()
        {
            _disposed = true; _generation++; _structure = null;
            ReleaseFields();
            _groupsContainer.Unbind(); _groupsContainer.Clear();
            _groupListView = null; _layerListView = null;
        }

        internal void RebuildGroupList()
        {
            if (target == null || _disposed) return;
            _generation++;
            int generation = _generation;
            _structure = new DeformerStackStructure(target as LatticeDeformer);
            ReleaseFields();
            _groupsContainer.Unbind();
            _groupsContainer.Clear();
            _groupListView = null;
            _layerListView = null;

            _cachedProfileReadOnly = ProfileReadOnly;
            _cachedGroupCount = SerializedDeformerReader.Read((LatticeDeformer)target).EmbeddedGroups?.Count ?? 0;
            if (_cachedProfileReadOnly)
            {
                _groupListView = null;
                _layerListView = null;
                var profileLabel = new Label(LatticeLocalization.Tr(LocKey.ProfileReadOnlyInfo));
                profileLabel.style.whiteSpace = WhiteSpace.Normal;
                profileLabel.style.marginLeft = 3;
                profileLabel.style.marginRight = 3;
                _groupsContainer.Add(profileLabel);
                return;
            }

            if (target is not LatticeDeformer owner || !owner.HasValidSerializedAuthoringData) return;
            serializedObject.Update();
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
            _groupListView.unbindItem = ReleaseRow;
            _groupListView.itemIndexChanged += (from, to) => { if (IsCurrent(generation)) OnGroupReordered(from, to); };
            _groupListView.selectionChanged += _ => { if (IsCurrent(generation)) OnGroupSelectionChanged(); };

            _groupIndices.Clear();
            for (int i = 0; i < groupCount; i++)
                _groupIndices.Add(i);
            _groupListView.itemsSource = _groupIndices;

            int activeGroupIdx = _activeGroupIndexProp != null ? _activeGroupIndexProp.intValue : 0;
            _groupListView.SetSelectionWithoutNotify(activeGroupIdx >= 0 && activeGroupIdx < groupCount
                ? new[] { activeGroupIdx } : Array.Empty<int>());

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
                if (!IsCurrent(generation)) return;
                if (target is LatticeDeformer d)
                {
                    PerformEditOperation(() => DeformerEditService.AddGroup(d, "Add Group"));
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
                if (!IsCurrent(generation)) return;
                if (target is LatticeDeformer d && _cachedGroupCount > 1)
                {
                    PerformEditOperation(() => DeformerEditService.RemoveGroup(d, _activeGroupIndexProp.intValue, "Remove Group"));
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

            // Right-click context menu for group
            root.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                if (root.userData is not int groupIndex) return;
                var d = target as LatticeDeformer;
                if (d == null || !IsCurrent()) return;
                int menuGeneration = _generation;

                evt.menu.AppendAction(LatticeLocalization.Tr(LocKey.DuplicateGroup), _ =>
                {
                    if (!IsCurrent(menuGeneration)) return;
                    DuplicateGroup(d, groupIndex);
                });
                evt.menu.AppendAction(LatticeLocalization.Tr(LocKey.CopyGroup), _ =>
                {
                    if (!IsCurrent(menuGeneration)) return;
                    CopyGroup(d, groupIndex);
                });
                evt.menu.AppendAction(LatticeLocalization.Tr(LocKey.PasteGroup), _ =>
                {
                    if (!IsCurrent(menuGeneration)) return;
                    PasteGroup(d);
                }, string.IsNullOrEmpty(s_copiedGroupJson) ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
                evt.menu.AppendSeparator();
                evt.menu.AppendAction(LatticeLocalization.Tr(LocKey.DeleteGroup), _ =>
                {
                    if (!IsCurrent(menuGeneration)) return;
                    if (_cachedGroupCount <= 1) return;
                    PerformEditOperation(() => DeformerEditService.RemoveGroup(
                        d, groupIndex, LatticeLocalization.Tr(LocKey.DeleteGroup)));
                }, _cachedGroupCount <= 1 ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            }));

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
            if (!IsCurrent()) return;
            ReleaseRow(element, index);
            int generation = _generation;
            serializedObject.Update();
            if (_groupsProp == null || index < 0 || index >= _groupsProp.arraySize) return;

            element.userData = index; // for right-click menu

            var groupProp = _groupsProp.GetArrayElementAtIndex(index);
            var groupNameProp = groupProp.FindPropertyRelative("_name");
            int activeGroupIdx = _activeGroupIndexProp != null ? _activeGroupIndexProp.intValue : 0;
            bool isActive = index == activeGroupIdx;

            // Name field
            var nameField = element.Q<TextField>("group-name");
            if (nameField != null)
            {
                BindField(nameField, groupNameProp, p => p.stringValue, (p, value) => p.stringValue = value);
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
                    if (IsCurrent(generation)) _drawGroupSettings(capturedGroupIndex);
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
                _layerListView.unbindItem = ReleaseRow;
                _layerListView.itemIndexChanged += (from, to) => { if (IsCurrent(generation)) OnLayerReordered(from, to); };
                _layerListView.selectionChanged += _ => { if (IsCurrent(generation)) OnLayerSelectionChanged(); };

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
                    if (!IsCurrent(generation)) return;
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent(LatticeLocalization.Tr(LocKey.AddLatticeLayer)), false, () =>
                    {
                        if (!IsCurrent(generation)) return;
                        AddLayerViaList(MeshDeformerLayerType.Lattice);
                        RebuildGroupList();
                    });
                    menu.AddItem(new GUIContent(LatticeLocalization.Tr(LocKey.AddBrushLayer)), false, () =>
                    {
                        if (!IsCurrent(generation)) return;
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
                    if (!IsCurrent(generation)) return;
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
                layerContainer.Add(new IMGUIContainer(() => { if (IsCurrent(generation)) _drawOperations(); }));
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

        internal void OnGroupReordered(int oldIndex, int newIndex)
        {
            if (target is not LatticeDeformer d) return;
            PerformEditOperation(() => DeformerEditService.MoveGroup(d, oldIndex, newIndex, "Reorder Group"));
        }

        private void OnGroupSelectionChanged()
        {
            if (!IsCurrent() || _groupListView == null || target is not LatticeDeformer d) return;
            int selected = _groupListView.selectedIndex;
            if (selected == _activeGroupIndexProp.intValue) return;
            PerformEditOperation(() => DeformerStackSelection.SelectGroup(d, selected, "Select Group"));
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
            _layerListView.SetSelectionWithoutNotify(_cachedActiveIndex >= 0 && _cachedActiveIndex < _cachedLayerCount
                ? new[] { _cachedActiveIndex } : Array.Empty<int>());
            _layerListView.Rebuild();
        }

        internal void CheckAndRebuildLayers()
        {
            if (target == null || _disposed) return;
            if (_structure == null || !_structure.Matches(target as LatticeDeformer)) RebuildGroupList();
            else
            {
                if (_groupListView == null) return;
                serializedObject.Update();
                foreach (var refresh in _refreshFields.Values) refresh();
            }
        }

        // Resolve a property at the time of an input event, after verifying that
        // the row still identifies the same objects. SerializedObject preserves
        // Unity's text/slider Undo grouping; repaint never assigns stored values.
        private void BindField<T>(BaseField<T> field, SerializedProperty property,
            Func<SerializedProperty, T> read, Action<SerializedProperty, T> write)
        {
            if (property == null) return;
            if (_unbindFields.TryGetValue(field, out var previous)) previous();
            int generation = _generation;
            string path = property.propertyPath;
            Action refresh = () =>
            {
                if (IsCurrent(generation)) field.SetValueWithoutNotify(read(serializedObject.FindProperty(path)));
            };
            EventCallback<ChangeEvent<T>> changed = evt =>
            {
                if (!IsCurrent(generation) || target is not LatticeDeformer d ||
                    !DeformerAuthoringSource.TryRead(d, out _, out _, out _)) return;
                serializedObject.Update();
                write(serializedObject.FindProperty(path), evt.newValue);
                if (serializedObject.ApplyModifiedProperties()) _onPropertyChanged();
            };
            field.RegisterValueChangedCallback(changed);
            _unbindFields[field] = () => field.UnregisterValueChangedCallback(changed);
            _refreshFields[field] = refresh;
            refresh();
        }

        private void ReleaseRow(VisualElement row, int index)
        {
            var removed = new List<VisualElement>();
            foreach (var pair in _unbindFields)
                if (row == pair.Key || row.Contains(pair.Key)) { pair.Value(); removed.Add(pair.Key); }
            foreach (var field in removed) { _unbindFields.Remove(field); _refreshFields.Remove(field); }
        }

        private void ReleaseFields()
        {
            foreach (var unbind in _unbindFields.Values) unbind();
            _unbindFields.Clear(); _refreshFields.Clear();
        }

        private VisualElement MakeLayerItem()
        {
            var root = new VisualElement();
            root.style.paddingTop = 3;
            root.style.paddingBottom = 3;
            root.style.paddingLeft = 4;
            root.style.paddingRight = 4;

            // Right-click context menu for layer (stop propagation to prevent group menu)
            root.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                if (root.userData is not int layerIndex) return;
                var d = target as LatticeDeformer;
                if (d == null || !IsCurrent()) return;
                int menuGeneration = _generation;

                evt.menu.AppendAction(LatticeLocalization.Tr(LocKey.DuplicateLayer), _ =>
                {
                    if (!IsCurrent(menuGeneration)) return;
                    PerformEditOperation(() => DeformerEditService.DuplicateLayer(
                        d, layerIndex, LatticeLocalization.Tr(LocKey.DuplicateLayer)));
                });
                evt.menu.AppendAction(LatticeLocalization.Tr(LocKey.CopyLayer), _ =>
                {
                    if (!IsCurrent(menuGeneration)) return;
                    CopyLayer(d, layerIndex);
                });
                evt.menu.AppendAction(LatticeLocalization.Tr(LocKey.PasteLayer), _ =>
                {
                    if (!IsCurrent(menuGeneration)) return;
                    PasteLayer(d);
                    RebuildGroupList();
                }, string.IsNullOrEmpty(s_copiedLayerJson) ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
                evt.menu.AppendSeparator();
                evt.menu.AppendAction(LatticeLocalization.Tr(LocKey.DeleteLayer), _ =>
                {
                    if (!IsCurrent(menuGeneration)) return;
                    DeleteLayer(d, layerIndex);
                    RebuildGroupList();
                }, _cachedLayerCount <= 1 ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);

                evt.StopPropagation();
            }));

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
            if (!IsCurrent()) return;
            int generation = _generation;
            serializedObject.Update();
            ResolveActiveGroupProperties();
            if (_layersProp == null || index < 0 || index >= _layersProp.arraySize) return;

            element.userData = index; // for right-click menu

            var layerProp = _layersProp.GetArrayElementAtIndex(index);
            var enabledProp = layerProp.FindPropertyRelative("_enabled");
            var nameProp = layerProp.FindPropertyRelative("_name");
            var weightProp = layerProp.FindPropertyRelative("_weight");
            var typeProp = layerProp.FindPropertyRelative("_type");

            bool isBrush = typeProp != null && typeProp.enumValueIndex == (int)MeshDeformerLayerType.Brush;
            bool isActive = _activeLayerIndexProp.intValue == index;

            // Row 1
            var enabledToggle = element.Q<Toggle>("layer-enabled");
            BindField(enabledToggle, enabledProp, p => p.boolValue, (p, value) => p.boolValue = value);

            var nameField = element.Q<TextField>("layer-name");
            BindField(nameField, nameProp, p => p.stringValue, (p, value) => p.stringValue = value);

            element.Q<Label>("layer-type").text = isBrush ? "B" : "L";

            // Row 2
            var weightSlider = element.Q<Slider>("layer-weight");
            BindField(weightSlider, weightProp, p => p.floatValue, (p, value) =>
            {
                if (!float.IsNaN(value) && !float.IsInfinity(value)) p.floatValue = Mathf.Clamp01(value);
            });

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
                    if (IsCurrent(generation)) _drawLayerSettings();
                };
            }
            else
            {
                imgui.onGUIHandler = null;
            }
        }

        private void OnLayerSelectionChanged()
        {
            if (!IsCurrent() || _layerListView == null || target is not LatticeDeformer d) return;
            int selected = _layerListView.selectedIndex;
            if (selected == _activeLayerIndexProp.intValue) return;
            PerformEditOperation(() => DeformerStackSelection.SelectLayer(d, selected, "Select Layer"));
        }

        private void OnLayerReordered(int oldIndex, int newIndex)
        {
            if (target is not LatticeDeformer deformer) return;
            MoveLayer(deformer, oldIndex, newIndex);
        }

        internal void MoveLayer(LatticeDeformer deformer, int fromIndex, int toIndex)
        {
            PerformEditOperation(() => DeformerEditService.MoveLayer(deformer, fromIndex, toIndex, "Reorder Layer"));
        }

        internal void DeleteLayer(LatticeDeformer deformer, int index)
        {
            PerformEditOperation(() => DeformerEditService.RemoveLayer(deformer, index, "Delete Layer"));
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

            PerformEditOperation(() => DeformerEditService.AddLayer(deformer, layerType, undoLabel));
        }

        internal void CopyLayer(LatticeDeformer deformer, int layerIndex)
        {
            if (!DeformerAuthoringSource.TryRead(deformer, out var raw, out _, out _)) return;
            var layers = raw.ActiveLayers;
            if (layers == null || layerIndex < 0 || layerIndex >= layers.Count) return;
            var layer = layers[layerIndex];
            if (layer == null) return;
            s_copiedLayerJson = JsonUtility.ToJson(layer);
        }

        internal void PasteLayer(LatticeDeformer deformer)
        {
            PerformEditOperation(() => DeformerEditService.PasteLayer(
                deformer, s_copiedLayerJson, LatticeLocalization.Tr(LocKey.PasteLayer)));
        }

        internal void DuplicateGroup(LatticeDeformer deformer, int groupIndex)
        {
            PerformEditOperation(() => DeformerEditService.DuplicateGroup(
                deformer, groupIndex, LatticeLocalization.Tr(LocKey.DuplicateGroup)));
        }

        internal void CopyGroup(LatticeDeformer deformer, int groupIndex)
        {
            if (!DeformerAuthoringSource.TryRead(deformer, out var raw, out _, out _)) return;
            var groups = raw.Groups;
            if (groups == null || groupIndex < 0 || groupIndex >= groups.Count) return;
            s_copiedGroupJson = JsonUtility.ToJson(groups[groupIndex]);
        }

        internal void PasteGroup(LatticeDeformer deformer)
        {
            PerformEditOperation(() => DeformerEditService.PasteGroup(
                deformer, s_copiedGroupJson, LatticeLocalization.Tr(LocKey.PasteGroup)));
        }

        private void PerformEditOperation(Func<bool> operation)
        {
            if (_disposed || target is not LatticeDeformer d ||
                !DeformerAuthoringSource.TryRead(d, out _, out _, out _)) return;
            serializedObject.ApplyModifiedProperties();
            bool changed = operation();
            serializedObject.Update();
            RebuildGroupList();
            if (changed) _onStructureChanged();
        }
    }
}
#endif
