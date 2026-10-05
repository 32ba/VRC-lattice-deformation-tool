#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>Selected layer settings and Inspector-local grid drafts.</summary>
    internal sealed class LayerSettingsInspectorSection : IDisposable
    {
        private readonly UnityEditor.Editor _owner;
        private readonly Action<bool> _onChanged;
        private readonly SerializedProperty _manualOffsetProp, _manualScaleProp;
        internal PendingLatticeGrid PendingGrid { get; } = new();
        private bool _disposed;
        private static bool s_showAlignSettings, s_linkManualScale = true;
        private static GUIContent s_linkOn, s_linkOff;
        private static readonly GUIContent[] s_xyzLabels = { new("X"), new("Y"), new("Z") };
        private LatticeDeformer Target => _owner != null ? _owner.target as LatticeDeformer : null;
        private SerializedObject Serialized => _owner.serializedObject;

        internal LayerSettingsInspectorSection(UnityEditor.Editor owner, Action<bool> onChanged)
        {
            _owner = owner; _onChanged = onChanged;
            _manualOffsetProp = Serialized.FindProperty("_manualOffsetProxy");
            _manualScaleProp = Serialized.FindProperty("_manualScaleProxy");
        }

        internal void Draw()
        {
            if (_disposed || !LayerSettingsEdit.TryReadActive(Target, out var raw, out var layer)) return;
            Serialized.Update();
            if (layer.Type == MeshDeformerLayerType.Brush) DrawBrush(raw);
            else
            {
                if (GUILayout.Button(LatticeLocalization.Tr(LocKey.ResetActiveLayer)))
                    Run(LayerSettingsOperation.Reset, LocKey.ResetLatticeCage);
                DrawGrid(Target, layer.SerializedSettings);
                DrawSettingsProperties(DrawSettingsExcludingGrid);
                if (_owner.targets.Length == 1) DrawAlignmentSettings();
            }
            if (GUILayout.Button(LatticeLocalization.Tr(LocKey.LROperations))) ShowLROperationsMenu();
            ApplyProperties();
        }

        private LatticeDeformer[] Targets() => _owner.targets.OfType<LatticeDeformer>().ToArray();

        private void ApplyProperties()
        {
            if (Serialized.ApplyModifiedProperties()) _onChanged?.Invoke(false);
        }

        private void Changed()
        {
            Serialized.Update(); _onChanged?.Invoke(true);
        }

        private void Run(LayerSettingsOperation operation, string label)
        {
            ApplyProperties();
            var commands = Targets().Select(LayerSettingsEdit.Capture).ToArray();
            if (LayerSettingsEdit.ExecuteBatch(commands, operation, LatticeLocalization.Tr(label))) Changed();
        }

        private void DrawBrush(in SerializedDeformerData raw)
        {
            EditorGUILayout.HelpBox(LatticeLocalization.Tr(LocKey.BrushLayerInfo), MessageType.Info);
            EditorGUILayout.LabelField(LatticeLocalization.Tr(LocKey.VertexCount),
                (raw.SourceMesh != null ? raw.SourceMesh.vertexCount : 0).ToString());
            if (GUILayout.Button(LatticeLocalization.Tr(LocKey.ClearActiveLayerDisplacements)))
                Run(LayerSettingsOperation.Clear, LocKey.ClearActiveLayerDisplacements);
        }

        private void DrawGrid(LatticeDeformer deformer, LatticeAsset settings)
        {
            if (settings == null || !PendingGrid.TryGet(deformer, out var pending)) return;
            EditorGUILayout.LabelField(LatticeLocalization.Content(LocKey.CurrentGridDivisions), new GUIContent(settings.GridSize.ToString()));
            EditorGUI.BeginChangeCheck();
            pending = EditorGUILayout.Vector3IntField(LatticeLocalization.Tr(LocKey.PendingGridDivisions), pending);
            if (EditorGUI.EndChangeCheck()) SetPendingGridForSelection(pending);
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(!HasPendingGridChanges))
            {
                if (GUILayout.Button(LatticeLocalization.Tr(LocKey.Apply), GUILayout.Width(80f)))
                {
                    ApplyProperties();
                    if (PendingGrid.Apply(Targets(), LatticeLocalization.Tr(LocKey.ChangeLatticeDivisions))) Changed();
                }
                if (GUILayout.Button(LatticeLocalization.Tr(LocKey.Revert), GUILayout.Width(80f))) PendingGrid.Revert(Targets());
            }
            EditorGUILayout.Space();
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

        private void ShowLROperationsMenu()
        {
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
            menu.ShowAsContext();
            void Add(string item, string undo, LayerSettingsOperation operation)
            {
                var action = CreateOperationAction(command, operation, LatticeLocalization.Tr(undo));
                menu.AddItem(LatticeLocalization.Content(item), false, () => action());
            }
        }

        internal void DrawSettingsProperties(Action<SerializedProperty> draw)
        {
            if (!LayerSettingsEdit.TryReadActive(Target, out var raw, out _)) return;
            var property = Serialized.FindProperty($"_groups.Array.data[{raw.ActiveGroupIndex}]._layers.Array.data[{raw.ActiveLayerIndex}]._settings");
            bool compatible = !_disposed && Targets().All(owner =>
                LayerSettingsEdit.TryReadActive(owner, out var selected, out var layer) &&
                layer.Type == MeshDeformerLayerType.Lattice && layer.SerializedSettings != null &&
                selected.ActiveGroupIndex == raw.ActiveGroupIndex && selected.ActiveLayerIndex == raw.ActiveLayerIndex &&
                DeformerAuthoringSource.TryRead(owner, out _, out _, out _));
            // A shared SerializedObject applies one indexed path to every owner.
            // Different active selections must not edit unrelated secondary layers.
            using (new EditorGUI.DisabledScope(!compatible)) draw(property);
        }

        private static void DrawSettingsExcludingGrid(SerializedProperty settings)
        {
            if (settings == null) return;
            var iterator = settings.Copy(); var end = iterator.GetEndProperty();
            bool hasNext = iterator.NextVisible(true);
            while (hasNext && !SerializedProperty.EqualContents(iterator, end))
            {
                if (iterator.depth == settings.depth + 1 && iterator.name != "_gridSize" && iterator.name != "_controlPointsLocal")
                    EditorGUILayout.PropertyField(iterator, includeChildren: true);
                hasNext = iterator.NextVisible(false);
            }
        }

        public void Dispose() { _disposed = true; PendingGrid.Dispose(); }
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
