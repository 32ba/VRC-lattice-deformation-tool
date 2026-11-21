#if UNITY_EDITOR
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>Selected layer settings and Inspector-local grid drafts.</summary>
    internal sealed class LayerSettingsInspectorSection : IDisposable
    {
        private readonly UnityEditor.Editor _owner;
        private readonly Action<bool> _onChanged;
        private readonly SerializedProperty _manualOffsetProp, _manualScaleProp;
        private readonly VisualElement _root = MdUI.Element("md-layer-settings");
        internal PendingLatticeGrid PendingGrid { get; } = new();
        private bool _disposed;
        private string _signature;
        private static bool s_showAlignSettings, s_linkManualScale = true;

        // Live parts updated in place, so typing into them is never interrupted by a rebuild.
        private Label _currentGrid;
        private Vector3IntField _pendingGrid;
        private Button _applyGrid, _revertGrid;
        private Vector3Field _scaleField;
        private LatticeDeformer Target => _owner != null ? _owner.target as LatticeDeformer : null;
        private SerializedObject Serialized => _owner.serializedObject;
        internal VisualElement Root => _root;

        internal LayerSettingsInspectorSection(UnityEditor.Editor owner, Action<bool> onChanged)
        {
            _owner = owner; _onChanged = onChanged;
            _manualOffsetProp = Serialized.FindProperty("_manualOffsetProxy");
            _manualScaleProp = Serialized.FindProperty("_manualScaleProxy");
        }

        /// <summary>Creates an element that shows this section; the section keeps one shared root.</summary>
        internal VisualElement CreateView()
        {
            _signature = null;
            Refresh();
            return _root;
        }

        internal void Refresh()
        {
            if (_disposed || Target == null) return;
            // Read-only pass: the full-vertex preflight runs once per owner instead of per query.
            using var preflight = AuthoringPreflightScope.Begin();
            if (!LayerSettingsEdit.TryReadActive(Target, out var raw, out var layer))
            {
                if (_signature != string.Empty) { _root.Clear(); _signature = string.Empty; }
                return;
            }
            // Only cheap reads here: this runs on every Inspector refresh, including during strokes.
            // The edit guard hashes source topology, so it is evaluated only when this key changes.
            string signature = string.Join("|", LatticeLocalization.CurrentLanguage, _owner.targets.Length,
                raw.ActiveGroupIndex, raw.ActiveLayerIndex, layer.Type,
                layer.SerializedSettings != null ? RuntimeHelpers.GetHashCode(layer.SerializedSettings) : 0,
                PendingGrid.TryGet(Target, out _), OwnersKey());
            if (signature != _signature)
            {
                _signature = signature;
                var property = SettingsProperty(out bool editable);
                Build(raw, layer, property, editable);
            }
            RefreshLive(layer);
        }

        private void Build(in SerializedDeformerData raw, LatticeLayer layer, SerializedProperty settings, bool editable)
        {
            _root.Clear();
            _currentGrid = null; _pendingGrid = null; _applyGrid = null; _revertGrid = null; _scaleField = null;
            Serialized.Update();
            if (layer.Type == MeshDeformerLayerType.Brush)
            {
                _root.Add(MdUI.HelpBox(LatticeLocalization.Tr(LocKey.BrushLayerInfo)));
                _root.Add(MdUI.FieldRow(LatticeLocalization.Tr(LocKey.VertexCount),
                    MdUI.Text((raw.SourceMesh != null ? raw.SourceMesh.vertexCount : 0).ToString("N0"))));
                _root.Add(MdUI.Button(LatticeLocalization.Tr(LocKey.ClearActiveLayerDisplacements),
                    () => Run(LayerSettingsOperation.Clear, LocKey.ClearActiveLayerDisplacements), "md-button--danger"));
            }
            else
            {
                BuildGrid(layer.SerializedSettings);
                var fields = MdUI.Element("md-layer-settings__fields");
                AddSettingsFields(fields, settings);
                fields.SetEnabled(editable);
                _root.Add(fields);
                if (_owner.targets.Length == 1) _root.Add(BuildAlignment());
                _root.Add(MdUI.Button(LatticeLocalization.Tr(LocKey.ResetActiveLayer),
                    () => Run(LayerSettingsOperation.Reset, LocKey.ResetLatticeCage), "md-button--danger", "undo"));
            }
            var operations = MdUI.Button(LatticeLocalization.Tr(LocKey.LROperations), null, null);
            operations.Add(MdUI.Icon("caret-down", "md-icon--small"));
            operations.clicked += () => ShowLROperationsMenu(operations);
            _root.Add(operations);
        }

        private void RefreshLive(LatticeLayer layer)
        {
            if (_currentGrid != null && layer.SerializedSettings != null)
                _currentGrid.text = layer.SerializedSettings.GridSize.ToString();
            if (_pendingGrid != null && PendingGrid.TryGet(Target, out var pending) &&
                !IsWithin(_pendingGrid.focusController?.focusedElement as VisualElement, _pendingGrid))
                _pendingGrid.SetValueWithoutNotify(pending);
            bool changed = HasPendingGridChanges;
            _applyGrid?.SetEnabled(changed);
            _revertGrid?.SetEnabled(changed);
            if (_scaleField != null && _manualScaleProp != null &&
                !IsWithin(_scaleField.focusController?.focusedElement as VisualElement, _scaleField))
                _scaleField.SetValueWithoutNotify(_manualScaleProp.vector3Value);
        }

        private string OwnersKey()
        {
            var key = new StringBuilder();
            foreach (var owner in Targets())
            {
                if (!LayerSettingsEdit.TryReadActive(owner, out var data, out var active)) { key.Append("x;"); continue; }
                var source = data.SourceMesh;
                var displayed = data.SkinnedRenderer != null ? data.SkinnedRenderer.sharedMesh
                    : data.MeshFilter != null ? data.MeshFilter.sharedMesh : null;
                key.Append(owner.GetInstanceID()).Append(',').Append(data.DataSource).Append(',')
                    .Append(displayed != null ? displayed.GetInstanceID() : 0).Append(',')
                    .Append(source != null ? source.GetInstanceID() : 0).Append(',')
                    .Append(source != null ? source.vertexCount : 0).Append(',')
                    .Append(data.ActiveGroupIndex).Append(',').Append(data.ActiveLayerIndex).Append(',')
                    .Append(active.Type).Append(',').Append(active.SerializedSettings != null).Append(';');
            }
            return key.ToString();
        }

        private static bool IsWithin(VisualElement element, VisualElement container)
        {
            for (var e = element; e != null; e = e.parent) if (e == container) return true;
            return false;
        }

        private LatticeDeformer[] Targets() => _owner.targets.OfType<LatticeDeformer>().ToArray();

        private void ApplyProperties()
        {
            if (Serialized.ApplyModifiedProperties()) _onChanged?.Invoke(false);
        }

        private void Changed()
        {
            Serialized.Update(); _onChanged?.Invoke(true);
            Refresh();
        }

        private void Run(LayerSettingsOperation operation, string label)
        {
            if (_disposed) return;
            ApplyProperties();
            var commands = Targets().Select(LayerSettingsEdit.Capture).ToArray();
            if (LayerSettingsEdit.ExecuteBatch(commands, operation, LatticeLocalization.Tr(label))) Changed();
        }

        private void BuildGrid(LatticeAsset settings)
        {
            if (settings == null || !PendingGrid.TryGet(Target, out var pending)) return;
            _currentGrid = MdUI.Text(settings.GridSize.ToString());
            _root.Add(MdUI.FieldRow(LatticeLocalization.Tr(LocKey.CurrentGridDivisions), _currentGrid));
            _pendingGrid = new Vector3IntField { name = "pending-grid" };
            _pendingGrid.SetValueWithoutNotify(pending);
            _pendingGrid.RegisterValueChangedCallback(evt =>
            {
                SetPendingGridForSelection(evt.newValue);
                Refresh();
            });
            _root.Add(MdUI.FieldRow(LatticeLocalization.Tr(LocKey.PendingGridDivisions), _pendingGrid));
            _applyGrid = MdUI.Button(LatticeLocalization.Tr(LocKey.Apply), () =>
            {
                if (_disposed) return;
                ApplyProperties();
                if (PendingGrid.Apply(Targets(), LatticeLocalization.Tr(LocKey.ChangeLatticeDivisions))) Changed();
            }, "md-button--primary md-button--small");
            _revertGrid = MdUI.Button(LatticeLocalization.Tr(LocKey.Revert), () =>
            {
                if (_disposed) return;
                PendingGrid.Revert(Targets());
                Refresh();
            }, "md-button--small");
            _root.Add(MdUI.Element("md-button-row", _applyGrid, _revertGrid));
        }

        internal bool SetPendingGridForSelection(Vector3Int value)
        {
            if (_disposed || Target == null) return false;
            var owners = Targets();
            if (owners.Length == 0 || owners.Any(owner => !PendingGrid.TryGet(owner, out _))) return false;
            foreach (var owner in owners) PendingGrid.Set(owner, value);
            return true;
        }

        internal bool HasPendingGridChanges
        {
            get
            {
                if (_disposed || Target == null) return false;
                bool changed = false;
                foreach (var owner in Targets())
                {
                    if (!LayerSettingsEdit.TryReadActive(owner, out _, out var layer) ||
                        !PendingGrid.TryGet(owner, out var pending)) return false;
                    changed |= pending != layer.SerializedSettings.GridSize;
                }
                return changed;
            }
        }

        internal Action CreateOperationAction(LayerSettingsEdit command, LayerSettingsOperation operation, string label)
        {
            var capturedTargets = Targets();
            var commands = capturedTargets.Select(owner => ReferenceEquals(owner, command?.Owner)
                ? command : LayerSettingsEdit.Capture(owner)).ToArray();
            bool used = false;
            return () =>
            {
                if (used || _disposed || command == null || Target == null || !ReferenceEquals(Target, command.Owner)) return;
                used = true;
                var currentTargets = Targets();
                if (currentTargets.Length != capturedTargets.Length ||
                    currentTargets.Where((owner, i) => !ReferenceEquals(owner, capturedTargets[i])).Any()) return;
                if (LayerSettingsEdit.ExecuteBatch(commands, operation, label)) Changed();
            };
        }

        private void ShowLROperationsMenu(VisualElement anchor)
        {
            if (_disposed) return;
            ApplyProperties();
            var command = LayerSettingsEdit.Capture(Target);
            if (command == null) return;
            var menu = new GenericMenu();
            Add(LocKey.SplitL, LocKey.SplitLayerLeft, LayerSettingsOperation.SplitLeft);
            Add(LocKey.SplitR, LocKey.SplitLayerRight, LayerSettingsOperation.SplitRight);
            menu.AddSeparator("");
            Add(LocKey.FlipX, LocKey.FlipLayerX, LayerSettingsOperation.FlipX);
            Add(LocKey.FlipY, LocKey.FlipLayerY, LayerSettingsOperation.FlipY);
            Add(LocKey.FlipZ, LocKey.FlipLayerZ, LayerSettingsOperation.FlipZ);
            menu.DropDown(anchor.worldBound);
            void Add(string item, string undo, LayerSettingsOperation operation)
            {
                var action = CreateOperationAction(command, operation, LatticeLocalization.Tr(undo));
                menu.AddItem(LatticeLocalization.Content(item), false, () => action());
            }
        }

        /// <summary>
        /// The active layer's settings property, and whether it may be edited. A shared
        /// SerializedObject applies one indexed path to every owner, so different active
        /// selections must not edit unrelated secondary layers.
        /// </summary>
        internal SerializedProperty SettingsProperty(out bool editable)
        {
            editable = false;
            if (!LayerSettingsEdit.TryReadActive(Target, out var raw, out _)) return null;
            var property = Serialized.FindProperty($"_groups.Array.data[{raw.ActiveGroupIndex}]._layers.Array.data[{raw.ActiveLayerIndex}]._settings");
            editable = !_disposed && Targets().All(owner =>
                LayerSettingsEdit.TryReadActive(owner, out var selected, out var layer) &&
                layer.Type == MeshDeformerLayerType.Lattice && layer.SerializedSettings != null &&
                selected.ActiveGroupIndex == raw.ActiveGroupIndex && selected.ActiveLayerIndex == raw.ActiveLayerIndex &&
                DeformerAuthoringSource.TryRead(owner, out _, out _, out _));
            return property;
        }

        private void AddSettingsFields(VisualElement parent, SerializedProperty settings)
        {
            if (settings == null) return;
            var iterator = settings.Copy(); var end = iterator.GetEndProperty();
            bool hasNext = iterator.NextVisible(true);
            while (hasNext && !SerializedProperty.EqualContents(iterator, end))
            {
                if (iterator.depth == settings.depth + 1 && iterator.name != "_gridSize" && iterator.name != "_controlPointsLocal")
                {
                    var field = new PropertyField(iterator.Copy());
                    field.Bind(Serialized);
                    parent.Add(field);
                }
                hasNext = iterator.NextVisible(false);
            }
        }

        public void Dispose()
        {
            _disposed = true;
            PendingGrid.Dispose();
            _root.Unbind();
            _root.Clear();
        }

        private VisualElement BuildAlignment()
        {
            var list = MdUI.Element("md-foldout-list");
            list.Add(MdUI.Foldout(LatticeLocalization.Tr(LocKey.LatticeCageAlignment),
                () => s_showAlignSettings, open => s_showAlignSettings = open, out var content));
            content.Add(MdUI.HelpBox(LatticeLocalization.Tr(LocKey.AlignmentCageInfo)));
            if (_manualOffsetProp != null)
            {
                var offset = new PropertyField(_manualOffsetProp, LatticeLocalization.Tr(LocKey.Offset))
                    { tooltip = LatticeLocalization.Tr(LocKey.OffsetTooltip) };
                offset.Bind(Serialized);
                content.Add(offset);
            }
            if (_manualScaleProp != null && _manualScaleProp.propertyType == SerializedPropertyType.Vector3)
                content.Add(BuildLinkedScale());
            var debug = new Toggle(LatticeLocalization.Tr(LocKey.DebugLogAlignment))
                { value = LatticePreviewUtility.DebugAlignLogs };
            debug.RegisterValueChangedCallback(evt =>
            {
                LatticePreviewUtility.DebugAlignLogs = evt.newValue;
                LatticePreviewUtility.LogAlign("Toggle", $"DebugAlignLogs set to {evt.newValue}");
            });
            content.Add(debug);
            return list;
        }

        private VisualElement BuildLinkedScale()
        {
            _scaleField = new Vector3Field { name = "manual-scale" };
            _scaleField.SetValueWithoutNotify(_manualScaleProp.vector3Value);
            Button link = null;
            link = MdUI.IconButton(s_linkManualScale ? "check" : "plus", () =>
            {
                s_linkManualScale = !s_linkManualScale;
                SyncLink();
            }, null);
            void SyncLink()
            {
                link.tooltip = LatticeLocalization.Tr(s_linkManualScale ? LocKey.ScaleLinked : LocKey.ScaleUnlinked);
                link.EnableInClassList("md-icon-button--active", s_linkManualScale);
            }
            SyncLink();
            _scaleField.RegisterValueChangedCallback(evt =>
            {
                var value = evt.newValue;
                if (s_linkManualScale)
                {
                    float uniform = value.x != evt.previousValue.x ? value.x
                        : value.y != evt.previousValue.y ? value.y : value.z;
                    value = new Vector3(uniform, uniform, uniform);
                }
                value = new Vector3(Mathf.Max(0.0001f, value.x), Mathf.Max(0.0001f, value.y), Mathf.Max(0.0001f, value.z));
                Serialized.Update();
                _manualScaleProp.vector3Value = value;
                ApplyProperties();
                _scaleField.SetValueWithoutNotify(value);
            });
            var control = MdUI.Element("md-linked-field", link, _scaleField);
            return MdUI.FieldRow(LatticeLocalization.Tr(LocKey.Scale), control);
        }
    }
}
#endif
