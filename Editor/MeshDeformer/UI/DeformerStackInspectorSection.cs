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
        private readonly Func<int, VisualElement> _makeGroupSettings;
        private readonly Func<VisualElement> _makeImport;
        private readonly Action _onStructureChanged;
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
        private readonly List<Action> _pendingListChanges = new();
        private bool _pointerDown, _listUpdateScheduled, _rebuildPending;
        private int _pendingGeneration;
        private static string s_copiedLayerJson, s_copiedGroupJson;
        private static DeformerClipboardSource s_copiedLayerSource, s_copiedGroupSource;
        private UnityEngine.Object target => _owner != null ? _owner.target : null;
        private SerializedObject serializedObject => _owner.serializedObject;
        internal VisualElement Root => _groupsContainer;
        internal ListView GroupList => _groupListView;
        internal ListView LayerList => _layerListView;
        internal bool ProfileReadOnly => target is LatticeDeformer d &&
            SerializedDeformerReader.Read(d).DataSource == DeformerDataSource.Profile;

        internal DeformerStackInspectorSection(UnityEditor.Editor owner, Func<int, VisualElement> makeGroupSettings,
            Func<VisualElement> makeImport, Action onStructureChanged)
        {
            _owner = owner; _makeGroupSettings = makeGroupSettings; _makeImport = makeImport;
            _onStructureChanged = onStructureChanged;
            _groupsProp = serializedObject.FindProperty("_groups");
            _activeGroupIndexProp = serializedObject.FindProperty("_activeGroupIndex");
            _groupsContainer.AddToClassList("md-stack");
        }

        private void ConfigureListInput(ListView list, bool nested = false)
        {
            list.RegisterCallback<PointerDownEvent>(evt =>
            {
                // Detail controls (popups, object pickers) can consume mouse-up in a
                // separate native window. They do not start an animated list drag.
                if (evt.button == 0 && !IsDetailInput(list, evt))
                {
                    _pointerDown = true;
                    if (nested && ReferenceEquals(evt.target, list) && IsCurrent())
                        SelectRetargetedLayerRow(list, evt.position);
                }
                if (nested) evt.StopPropagation();
            });
            list.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button == 0) FinishPointerInput();
            }, TrickleDown.TrickleDown);
            list.RegisterCallback<PointerCancelEvent>(_ =>
            {
                _pendingListChanges.Clear();
                _rebuildPending = true;
                FinishPointerInput();
            }, TrickleDown.TrickleDown);
        }

        private static bool IsDetailInput(ListView list, PointerDownEvent evt)
        {
            for (var hit = evt.target as VisualElement; hit != null && hit != list; hit = hit.parent)
                if (hit.ClassListContains(DetailClass)) return true;
            // The animated dragger may already have retargeted this event to the
            // ListView. Use panel coordinates to identify the original detail hit.
            foreach (var detail in list.Query<VisualElement>(className: DetailClass).ToList())
                if (detail.visible && detail.worldBound.Contains(evt.position)) return true;
            // Field manipulators capture their own release. They edit values,
            // not list order, and must not suspend later structural refreshes.
            foreach (var field in list.Query<VisualElement>().ToList())
                if ((field.name == "layer-weight" || field.name == "layer-name" ||
                     field.name == "layer-enabled" || field.name == "group-name") &&
                    field.visible && field.worldBound.Contains(evt.position)) return true;
            return false;
        }

        /// <summary>Marks detail controls inside list rows; their input never reorders or selects rows.</summary>
        internal const string DetailClass = "md-detail";

        private static void SelectRetargetedLayerRow(ListView list, Vector2 position)
        {
            // Unity 2022's animated dragger can target the ListView itself. Its
            // normal selection callback lives on the scroll content and is then
            // absent from the event route. Preserve field input and select only
            // the actual row hit; the existing pointer-up queue commits the edit.
            for (var hit = list.panel?.Pick(position); hit != null && hit != list; hit = hit.parent)
            {
                if (hit.name == "layer-name" || hit.name == "layer-weight" || hit.name == "layer-enabled") return;
                if (hit.name != "deformer-layer-row" || hit.userData is not int index) continue;
                list.SetSelection(index);
                return;
            }
        }

        // Animated ListView clears and reselects during StartDrag, then continues
        // using the same panel through OnDrop. Commit after pointer dispatch so
        // neither selection nor reordering can detach its live drag controller.
        private void QueueListChange(Action change)
        {
            if (_groupsContainer.panel == null) { change(); return; }
            if (_pendingListChanges.Count == 0) _pendingGeneration = _generation;
            _pendingListChanges.Add(change);
            if (!_pointerDown) ScheduleListUpdate();
        }

        private void FinishPointerInput()
        {
            _pointerDown = false;
            if (_pendingListChanges.Count > 0 || _rebuildPending) ScheduleListUpdate();
        }

        private void ScheduleListUpdate()
        {
            if (_listUpdateScheduled || _disposed) return;
            _listUpdateScheduled = true;
            _groupsContainer.schedule.Execute(() =>
            {
                if (_disposed || _pointerDown) { _listUpdateScheduled = false; return; }
                var changes = _pendingListChanges.ToArray();
                _pendingListChanges.Clear();
                bool canApply = IsCurrent(_pendingGeneration);
                try
                {
                    if (canApply) foreach (var change in changes) change();
                }
                finally
                {
                    _listUpdateScheduled = false;
                    bool rebuild = _rebuildPending || changes.Length > 0;
                    _rebuildPending = false;
                    if (rebuild) RebuildGroupList();
                }
            });
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
            _pendingListChanges.Clear();
            ReleaseFields();
            _groupsContainer.Unbind(); _groupsContainer.Clear();
            _groupListView = null; _layerListView = null;
        }

        internal void RebuildGroupList()
        {
            if (target == null || _disposed) return;
            if (_pointerDown || _listUpdateScheduled) { _rebuildPending = true; return; }
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
                _groupsContainer.Add(MdUI.HelpBox(LatticeLocalization.Tr(LocKey.ProfileReadOnlyInfo)));
                return;
            }

            if (target is not LatticeDeformer owner || !owner.HasValidSerializedAuthoringData) return;
            serializedObject.Update();
            if (_groupsProp == null) return;
            int groupCount = _groupsProp.arraySize;
            _cachedGroupCount = groupCount;

            // Groups label
            _groupsContainer.Add(MdUI.Text(LatticeLocalization.Tr(LocKey.DeformationGroups), "md-stack__label"));

            // Group ListView
            _groupListView = new ListView
            {
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                showBorder = true,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                selectionType = SelectionType.Single,
            };
            _groupListView.AddToClassList("md-stack-list");
            _groupListView.AddToClassList("md-stack-list--groups");
            ConfigureListInput(_groupListView);
            _groupListView.makeItem = MakeGroupItem;
            _groupListView.bindItem = BindGroupItem;
            _groupListView.unbindItem = ReleaseRow;
            _groupListView.itemIndexChanged += (from, to) => { if (IsCurrent(generation)) QueueListChange(() => OnGroupReordered(from, to)); };
            _groupListView.selectionChanged += _ => { if (IsCurrent(generation)) OnGroupSelectionChanged(); };

            _groupIndices.Clear();
            for (int i = 0; i < groupCount; i++)
                _groupIndices.Add(i);
            _groupListView.itemsSource = _groupIndices;

            int activeGroupIdx = _activeGroupIndexProp != null ? _activeGroupIndexProp.intValue : 0;
            _groupListView.SetSelectionWithoutNotify(activeGroupIdx >= 0 && activeGroupIdx < groupCount
                ? new[] { activeGroupIdx } : Array.Empty<int>());

            _groupsContainer.Add(_groupListView);

            var groupFooter = MdUI.Element("md-stack__footer");
            var addGroupBtn = MdUI.Button(LatticeLocalization.Tr(LocKey.AddGroup), () =>
            {
                if (!IsCurrent(generation)) return;
                if (target is LatticeDeformer d)
                {
                    PerformEditOperation(() => DeformerEditService.AddGroup(d, "Add Group"));
                }
            }, "md-button--small", "plus");
            addGroupBtn.name = "add-group";
            groupFooter.Add(addGroupBtn);

            var removeGroupBtn = MdUI.Button(LatticeLocalization.Tr(LocKey.DeleteGroup), () =>
            {
                if (!IsCurrent(generation)) return;
                if (target is LatticeDeformer d && _cachedGroupCount > 1)
                {
                    PerformEditOperation(() => DeformerEditService.RemoveGroup(d, _activeGroupIndexProp.intValue, "Remove Group"));
                }
            }, "md-button--small md-button--danger");
            removeGroupBtn.name = "remove-group";
            removeGroupBtn.SetEnabled(groupCount > 1);
            groupFooter.Add(removeGroupBtn);

            _groupsContainer.Add(groupFooter);
        }

        private VisualElement MakeGroupItem()
        {
            var root = MdUI.Element("md-group-row");

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
            var nameField = new TextField { name = "group-name" };
            nameField.AddToClassList("md-group-row__name");
            root.Add(nameField);

            // BlendShape output settings (per group)
            var blendShapeContainer = new VisualElement { name = "blendshape-container" };
            root.Add(blendShapeContainer);

            // Nested layer list container (populated in bind for active group)
            var layerContainer = new VisualElement { name = "layer-container" };
            layerContainer.AddToClassList("md-group-row__layers");
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
                BindField(nameField, groupNameProp, p => p.stringValue);
            }

            element.EnableInClassList("md-group-row--active", isActive);

            // BlendShape output settings (per group)
            var bsContainer = element.Q("blendshape-container");
            if (bsContainer != null)
            {
                bsContainer.Clear();
                int capturedGroupIndex = index;
                var settings = _makeGroupSettings?.Invoke(capturedGroupIndex);
                if (settings != null) bsContainer.Add(settings);
            }

            // Layer container
            var layerContainer = element.Q("layer-container");
            if (layerContainer == null) return;
            layerContainer.Clear();

            if (isActive)
            {
                // Layers label
                layerContainer.Add(MdUI.Text(LatticeLocalization.Tr(LocKey.Layers), "md-stack__label"));

                // Layer ListView for active group
                _layerListView = new ListView
                {
                    reorderable = true,
                    reorderMode = ListViewReorderMode.Animated,
                    showBorder = true,
                    virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                    selectionType = SelectionType.Single,
                };
                _layerListView.AddToClassList("md-stack-list");
                _layerListView.AddToClassList("md-layer-list");
                ConfigureListInput(_layerListView, nested: true);
                _layerListView.makeItem = MakeLayerItem;
                _layerListView.bindItem = BindLayerItem;
                _layerListView.unbindItem = ReleaseRow;
                _layerListView.itemIndexChanged += (from, to) => { if (IsCurrent(generation)) QueueListChange(() => OnLayerReordered(from, to)); };
                _layerListView.selectionChanged += _ => { if (IsCurrent(generation)) OnLayerSelectionChanged(); };

                ResolveActiveGroupProperties();
                RebuildLayerListInternal();
                layerContainer.Add(_layerListView);

                var layerFooter = MdUI.Element("md-stack__footer");
                Button addLayerBtn = null;
                addLayerBtn = MdUI.Button(LatticeLocalization.Tr(LocKey.AddLayer), () =>
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
                    menu.DropDown(addLayerBtn.worldBound);
                }, "md-button--small", "plus");
                addLayerBtn.name = "add-layer";
                addLayerBtn.Add(MdUI.Icon("caret-down", "md-icon--small"));
                layerFooter.Add(addLayerBtn);

                var removeLayerBtn = MdUI.Button(LatticeLocalization.Tr(LocKey.DeleteLayer), () =>
                {
                    if (!IsCurrent(generation)) return;
                    ResolveActiveGroupProperties();
                    if (target is LatticeDeformer d && _layersProp != null && _layersProp.arraySize > 0)
                    {
                        DeleteLayer(d, _activeLayerIndexProp.intValue);
                        RebuildGroupList();
                    }
                }, "md-button--small md-button--danger");
                removeLayerBtn.name = "remove-layer";
                removeLayerBtn.SetEnabled(_layersProp != null && _layersProp.arraySize > 0);
                layerFooter.Add(removeLayerBtn);

                layerContainer.Add(layerFooter);
                var import = _makeImport?.Invoke();
                if (import != null) layerContainer.Add(import);
            }
            else
            {
                // Inactive: show layer count
                var groupLayersProp = groupProp.FindPropertyRelative("_layers");
                int layerCount = groupLayersProp != null ? groupLayersProp.arraySize : 0;
                layerContainer.Add(MdUI.Text(string.Format(LatticeLocalization.Tr(LocKey.GroupLayerCount), layerCount),
                    "md-text-small md-group-row__summary"));
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
            if (selected < 0 || selected == _activeGroupIndexProp.intValue) return;
            QueueListChange(() => PerformEditOperation(() => DeformerStackSelection.SelectGroup(d, selected, "Select Group")));
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

        // Reject stale input before Unity's binding handler runs. Keep the native
        // binding for Undo grouping, Prefab override styling and property menus.
        // Refreshing presentation resolves a current property and never writes it.
        private void BindField<T>(BaseField<T> field, SerializedProperty property,
            Func<SerializedProperty, T> read)
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
                    !DeformerAuthoringSource.TryRead(d, out _, out _, out _) ||
                    (evt.newValue is float number && (float.IsNaN(number) || float.IsInfinity(number))))
                {
                    field.SetValueWithoutNotify(evt.previousValue);
                    evt.StopImmediatePropagation();
                }
            };
            field.SetValueWithoutNotify(read(property));
            field.BindProperty(property);
            field.RegisterCallback(changed, TrickleDown.TrickleDown);
            _unbindFields[field] = () =>
            {
                field.UnregisterCallback(changed, TrickleDown.TrickleDown);
                field.Unbind();
            };
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
            var root = new VisualElement { name = "deformer-layer-row" };
            root.AddToClassList("md-layer-row");

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

            // Row 1: [visible] [type] [name] [state]
            var row1 = MdUI.Element("md-layer-row__line");

            var enabledToggle = new Toggle { name = "layer-enabled", tooltip = LatticeLocalization.Tr(LocKey.LayerVisible) };
            enabledToggle.AddToClassList("md-eye-toggle");
            row1.Add(enabledToggle);

            var typeLabel = new Label { name = "layer-type" };
            typeLabel.AddToClassList("md-badge");
            row1.Add(typeLabel);

            var nameField = new TextField { name = "layer-name" };
            nameField.AddToClassList("md-layer-row__name-field");
            row1.Add(nameField);

            var state = MdUI.Text(LatticeLocalization.Tr(LocKey.LayerEditing), "md-layer-row__state");
            state.name = "layer-state";
            row1.Add(state);
            root.Add(row1);

            // Row 2: Weight slider
            var weightSlider = new Slider(0f, 1f) { name = "layer-weight", showInputField = true,
                tooltip = LatticeLocalization.Tr(LocKey.LayerInfluence) };
            weightSlider.AddToClassList("md-layer-row__weight-slider");
            root.Add(weightSlider);

            return root;
        }

        private void BindLayerItem(VisualElement element, int index)
        {
            if (!IsCurrent()) return;
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
            BindField(enabledToggle, enabledProp, p => p.boolValue);

            var nameField = element.Q<TextField>("layer-name");
            BindField(nameField, nameProp, p => p.stringValue);

            var typeLabel = element.Q<Label>("layer-type");
            typeLabel.text = LatticeLocalization.Tr(isBrush ? LocKey.LayerBadgeBrush : LocKey.LayerBadgeLattice);
            typeLabel.EnableInClassList("md-badge--brush", isBrush);
            typeLabel.EnableInClassList("md-badge--lattice", !isBrush);
            element.EnableInClassList("md-layer-row--selected", isActive);
            element.EnableInClassList("md-layer-row--hidden", enabledProp != null && !enabledProp.boolValue);
            MdUI.SetVisible(element.Q("layer-state"), isActive);

            // Row 2
            var weightSlider = element.Q<Slider>("layer-weight");
            BindField(weightSlider, weightProp, p => p.floatValue);

        }

        private void OnLayerSelectionChanged()
        {
            if (!IsCurrent() || _layerListView == null || target is not LatticeDeformer d) return;
            int selected = _layerListView.selectedIndex;
            if (selected < 0 || selected == _activeLayerIndexProp.intValue) return;
            QueueListChange(() => PerformEditOperation(() => DeformerStackSelection.SelectLayer(d, selected, "Select Layer")));
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
            s_copiedLayerSource = DeformerClipboardSource.Capture(deformer, s_copiedLayerJson);
        }

        internal void PasteLayer(LatticeDeformer deformer)
        {
            PerformEditOperation(() => DeformerEditService.PasteLayer(
                deformer, s_copiedLayerJson, LatticeLocalization.Tr(LocKey.PasteLayer), s_copiedLayerSource));
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
            s_copiedGroupSource = DeformerClipboardSource.Capture(deformer, s_copiedGroupJson);
        }

        internal void PasteGroup(LatticeDeformer deformer)
        {
            PerformEditOperation(() => DeformerEditService.PasteGroup(
                deformer, s_copiedGroupJson, LatticeLocalization.Tr(LocKey.PasteGroup), s_copiedGroupSource));
        }

        private void PerformEditOperation(Func<bool> operation)
        {
            if (_disposed || target is not LatticeDeformer d) return;
            if (!DeformerAuthoringSource.TryRead(d, out _, out _, out _))
            {
                RebuildGroupList();
                return;
            }
            serializedObject.ApplyModifiedProperties();
            bool changed = operation();
            serializedObject.Update();
            RebuildGroupList();
            if (changed) _onStructureChanged();
        }
    }
}
#endif
