#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
    [CanEditMultipleObjects]
    [ExcludeFromCodeCoverage]
    public sealed class LatticeDeformerEditor : UnityEditor.Editor
    {
        private SerializedProperty _groupsProp;
        private SerializedProperty _activeGroupIndexProp;
        private SupportInspectorSection _supportInspector;
        private MeshRebuildInspectorSection _rebuildInspector;
        // These are resolved per-frame from the active group
        private SerializedProperty _layersProp;
        private SerializedProperty _activeLayerIndexProp;
        private SerializedProperty _skinnedRendererProp;
        private SerializedProperty _meshFilterProp;
        private BlendShapeInspectorSection _blendShapeInspector;
        private LayerSettingsInspectorSection _layerSettingsInspector;
        internal LayerSettingsInspectorSection LayerSettingsInspector => _layerSettingsInspector;
        private const string DetailedInspectorSessionKeyPrefix =
            "Net32Ba.LatticeDeformationTool.DetailedInspector.";

        private VisualElement _root;
        private VisualElement _guidedInspectorContainer;
        private VisualElement _detailedInspectorContainer;
        private VisualElement _depthTabs;
        private VisualElement _releaseNotification;
        private PropertyField _skinnedSourceField, _staticSourceField;
        private Button _openEditorButton;
        private IVisualElementScheduledItem _refreshTicker;
        private bool _refreshRequested = true;
        private double _nextFallbackRefresh;
        // State that can change outside this Inspector (preview toggle, release check) is re-read
        // at this interval even without a serialized change.
        private const double FallbackRefreshInterval = 1.0;
        private GuidedInspectorSection _guidedInspector;
        internal GuidedInspectorSection GuidedInspector => _guidedInspector;

        private void OnEnable()
        {
            _groupsProp = serializedObject.FindProperty("_groups");
            _activeGroupIndexProp = serializedObject.FindProperty("_activeGroupIndex");
            _blendShapeInspector = new BlendShapeInspectorSection(this, NotifyPropertyChanges, OnBlendShapeImported);
            _layerSettingsInspector = new LayerSettingsInspectorSection(this, NotifyPropertyChanges);
            _stackInspector = new DeformerStackInspectorSection(this, _blendShapeInspector.CreateGroupView,
                _blendShapeInspector.CreateImportView, OnStackStructureChanged);
            _guidedInspector = new GuidedInspectorSection(this, AutoAssignLocalRendererReferences,
                OpenDetailedInspector, OnGuidedEditingStarted, NotifyPropertyChanges);
            _rebuildInspector = new MeshRebuildInspectorSection(serializedObject);
            _supportInspector = new SupportInspectorSection(this);
            _skinnedRendererProp = serializedObject.FindProperty("_skinnedMeshRenderer");
            _meshFilterProp = serializedObject.FindProperty("_meshFilter");
            ResolveActiveGroupProperties();
            AutoAssignLocalRendererReferences();
            LatticeLocalization.LanguageChanged += OnLanguageChanged;
            ReleaseChecker.OnUpdateCheckCompleted += OnReleaseCheckCompleted;
        }

        private void ResolveActiveGroupProperties()
        {
            _layersProp = null;
            _activeLayerIndexProp = null;

            if (_groupsProp == null || _activeGroupIndexProp == null) return;
            int groupIndex = _activeGroupIndexProp.intValue;
            if (groupIndex < 0 || groupIndex >= _groupsProp.arraySize) return;

            var groupProp = _groupsProp.GetArrayElementAtIndex(groupIndex);
            if (groupProp == null) return;

            _layersProp = groupProp.FindPropertyRelative("_layers");
            _activeLayerIndexProp = groupProp.FindPropertyRelative("_activeLayerIndex");
        }

        private void OnDisable()
        {
            LatticeLocalization.LanguageChanged -= OnLanguageChanged;
            ReleaseChecker.OnUpdateCheckCompleted -= OnReleaseCheckCompleted;
            _refreshTicker?.Pause();
            _refreshTicker = null;
            _blendShapeInspector?.Dispose();
            _blendShapeInspector = null;
            _stackInspector?.Dispose();
            _stackInspector = null;
            _guidedInspector = null;
            _rebuildInspector = null;
            _supportInspector = null;

            _layerSettingsInspector?.Dispose();
            _layerSettingsInspector = null;
        }

        private void OnLanguageChanged()
        {
            // Labels are baked into the elements, so rebuild the content in the new language.
            if (_root != null) MeshDeformerStyles.Apply(_root);
            if (_root != null && target != null) BuildContent();
            RebuildLayerList();
        }

        private void OnReleaseCheckCompleted()
        {
            if (_releaseNotification != null) RefreshReleaseNotification();
            Repaint();
        }

        public override VisualElement CreateInspectorGUI()
        {
            _root = new VisualElement { name = "mesh-deformer-inspector" };
            MeshDeformerStyles.Apply(_root);
            _root.AddToClassList("md-inspector");
            BuildContent();
            _refreshTicker = _root.schedule.Execute(OnRefreshTick).Every(250);
            _root.RegisterCallback<DetachFromPanelEvent>(_ => _refreshTicker?.Pause());
            _root.RegisterCallback<AttachToPanelEvent>(_ => _refreshTicker?.Resume());
            return BindInspector(_root);
        }

        private void BuildContent()
        {
            _root.Clear();
            _guidedInspectorContainer = null; _detailedInspectorContainer = null; _depthTabs = null;
            _releaseNotification = null; _skinnedSourceField = null; _staticSourceField = null; _openEditorButton = null;
            if (targets.Length > 1)
            {
                // Group navigation and Guided actions are single-owner workflows.
                // The selected-layer section owns atomic commands for all owners.
                var multi = MdUI.Section(LatticeLocalization.Tr(LocKey.Settings), out var multiBody);
                multi.name = "lattice-multi-layer-settings";
                multiBody.Add(_layerSettingsInspector.CreateView());
                _root.Add(multi);
                RefreshMultiObjectLayerSettings();
                return;
            }

            _depthTabs = MdUI.Segmented(new[]
            {
                LatticeLocalization.Tr(LocKey.GuidedInspectorTab),
                LatticeLocalization.Tr(LocKey.DetailedInspectorTab)
            }, DetailedInspectorEnabled ? 1 : 0, index =>
            {
                DetailedInspectorEnabled = index == 1;
                ApplyInspectorDepthVisibility();
            }, "md-inspector__tabs");
            _depthTabs.name = "inspector-depth";
            _root.Add(_depthTabs);

            _guidedInspectorContainer = MdUI.Element("md-inspector__page");
            _guidedInspectorContainer.name = "guided-inspector";
            _guidedInspectorContainer.Add(_guidedInspector.Root);
            _root.Add(_guidedInspectorContainer);

            _detailedInspectorContainer = MdUI.Element("md-inspector__page");
            _detailedInspectorContainer.name = "detailed-inspector";
            BuildDetailedInspector(_detailedInspectorContainer);
            _root.Add(_detailedInspectorContainer);

            ApplyInspectorDepthVisibility();
            RefreshInspector();
        }

        private void BuildDetailedInspector(VisualElement page)
        {
            _releaseNotification = MdUI.Element("md-release");
            page.Add(_releaseNotification);
            RefreshReleaseNotification();

            var source = MdUI.Section(LatticeLocalization.Tr(LocKey.MeshSource), out var sourceBody);
            source.name = "mesh-source";
            _skinnedSourceField = new PropertyField(_skinnedRendererProp, LatticeLocalization.Tr(LocKey.SkinnedMeshSource))
                { tooltip = LatticeLocalization.Tooltip(LocKey.SkinnedMeshSource) };
            _staticSourceField = new PropertyField(_meshFilterProp, LatticeLocalization.Tr(LocKey.StaticMeshSource))
                { tooltip = LatticeLocalization.Tooltip(LocKey.StaticMeshSource) };
            sourceBody.Add(_skinnedSourceField);
            sourceBody.Add(_staticSourceField);
            page.Add(source);

            // Groups > Layers nested structure
            _groupsContainer = _stackInspector.Root;
            var stack = MdUI.Section(LatticeLocalization.Tr(LocKey.DeformationLayers), out var stackBody);
            stack.name = "deformer-stack";
            stackBody.Add(_groupsContainer);
            page.Add(stack);
            RebuildGroupList();

            var settings = MdUI.Section(LatticeLocalization.Tr(LocKey.SelectedLayerSettings), out var settingsBody);
            settings.name = "selected-layer-settings";
            settingsBody.Add(_layerSettingsInspector.CreateView());
            page.Add(settings);

            _openEditorButton = MdUI.Button(string.Empty, () =>
            {
                MeshDeformerTool.UseDetailedOverlay();
                ToolManager.SetActiveTool<MeshDeformerTool>();
                LatticePreviewUtility.RequestSceneRepaint();
            }, "md-button--primary md-inspector__open-editor");
            _openEditorButton.name = "open-editor";
            page.Add(_openEditorButton);

            var more = MdUI.Element("md-foldout-list md-inspector__more");
            more.Add(_rebuildInspector.CreateView());
            more.Add(BuildPreferences());
            more.Add(_supportInspector.CreateView());
            page.Add(more);
        }

        private static bool s_showPreferences;

        private VisualElement BuildPreferences()
        {
            var root = MdUI.Foldout(LatticeLocalization.Tr(LocKey.ToolPreferences),
                () => s_showPreferences, open => s_showPreferences = open, out var content);
            root.name = "tool-preferences";
            var language = new PopupField<string>(LatticeLocalization.Tr(LocKey.ToolLanguage),
                new List<string>(LatticeLocalization.DisplayNames), (int)LatticeLocalization.CurrentLanguage)
                { name = "tool-language", tooltip = LatticeLocalization.Tooltip(LocKey.ToolLanguage) };
            language.RegisterValueChangedCallback(_ =>
            {
                int next = Mathf.Clamp(language.index, 0, LatticeLocalization.DisplayNames.Length - 1);
                // Defer: changing the language rebuilds this element.
                language.schedule.Execute(() => LatticeLocalization.CurrentLanguage = (LatticeLocalization.Language)next);
            });
            content.Add(language);
            content.Add(MdUI.Switch(LatticeLocalization.Tr(LocKey.ShowTooltips), null,
                () => LatticeLocalization.ShowTooltips, value => LatticeLocalization.ShowTooltips = value, out _));
            content.Add(MdUI.Text(LatticeLocalization.Tr(LocKey.MeshDeformerOverview), "md-text-small"));
            return root;
        }

        private void RefreshReleaseNotification()
        {
            _releaseNotification.Clear();
            bool show = ReleaseChecker.HasNewVersion && !string.IsNullOrEmpty(ReleaseChecker.LatestVersion);
            MdUI.SetVisible(_releaseNotification, show);
            if (!show) return;
            var box = MdUI.HelpBox(string.Format(LatticeLocalization.Tr(LocKey.UpdateCurrentToLatest),
                VersionUtility.FormatVersion(ReleaseChecker.GetCurrentVersion()),
                VersionUtility.FormatVersion(ReleaseChecker.LatestVersion)));
            box.Q<Label>().text = LatticeLocalization.Tr(LocKey.UpdateAvailable) + "\n" + box.Q<Label>().text
                + "\n" + LatticeLocalization.Tr(LocKey.UpdateViaVpm);
            _releaseNotification.Add(box);
            _releaseNotification.Add(MdUI.Button(LatticeLocalization.Tr(LocKey.UpdateOpenGuide),
                ReleaseChecker.OpenUpdateGuide, "md-button--small"));
        }

        private void RequestRefresh() => _refreshRequested = true;

        private void OnRefreshTick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (!_refreshRequested && now < _nextFallbackRefresh) return;
            _refreshRequested = false;
            _nextFallbackRefresh = now + FallbackRefreshInterval;
            RefreshInspector();
        }

        /// <summary>
        /// Resyncs state-dependent parts with the target. Every part only reads here; the sections
        /// rebuild themselves only when what they show has changed.
        /// </summary>
        private void RefreshInspector()
        {
            if (_root == null || target == null) return;
            if (targets.Length > 1) { RefreshMultiObjectLayerSettings(); return; }
            _guidedInspector?.Refresh();
            if (!DetailedInspectorEnabled) return;
            AutoAssignLocalRendererReferences();
            serializedObject.Update();
            ResolveActiveGroupProperties();
            _layerSettingsInspector?.Refresh();
            _rebuildInspector?.Refresh();

            bool hasSkinnedAssigned = _skinnedRendererProp != null && !_skinnedRendererProp.hasMultipleDifferentValues && _skinnedRendererProp.objectReferenceValue != null;
            bool hasMeshAssigned = _meshFilterProp != null && !_meshFilterProp.hasMultipleDifferentValues && _meshFilterProp.objectReferenceValue != null;
            _skinnedSourceField?.SetEnabled(!(ShouldDisableRendererField<SkinnedMeshRenderer>() || hasMeshAssigned));
            _staticSourceField?.SetEnabled(!(ShouldDisableRendererField<MeshFilter>() || hasSkinnedAssigned));

            if (_openEditorButton != null)
            {
                bool hasLayers = _layersProp != null && _layersProp.arraySize > 0;
                bool openBrushTool = hasLayers && GetSerializedActiveLayerType() == MeshDeformerLayerType.Brush;
                MdUI.SetButtonText(_openEditorButton, openBrushTool
                    ? LatticeLocalization.Tr(LocKey.OpenBrushEditor)
                    : LatticeLocalization.Tr(LocKey.OpenLatticeEditor));
                _openEditorButton.SetEnabled(hasLayers);
            }
        }

        private void RefreshMultiObjectLayerSettings()
        {
            if (target == null) return;
            bool editable = true;
            foreach (var owner in EnumerateTargets()) editable &= LayerSettingsEdit.Capture(owner) != null;
            _layerSettingsInspector?.Refresh();
            _layerSettingsInspector?.Root.SetEnabled(editable);
        }

        private VisualElement BindInspector(VisualElement root)
        {
            // Track serialized changes
            root.TrackSerializedObjectValue(serializedObject, _ =>
            {
                CheckAndRebuildLayers();
                NotifyPropertyChanges();
                // Brush strokes change the target every frame; refresh on the next tick only.
                RequestRefresh();
            });
            root.Bind(serializedObject);

            return root;
        }

        private bool DetailedInspectorEnabled
        {
            get => SessionState.GetBool(GetDetailedInspectorSessionKey(), false);
            set => SessionState.SetBool(GetDetailedInspectorSessionKey(), value);
        }

        private string GetDetailedInspectorSessionKey()
        {
            int instanceId = target != null ? target.GetInstanceID() : 0;
            return DetailedInspectorSessionKeyPrefix + instanceId;
        }

        private void ApplyInspectorDepthVisibility()
        {
            bool detailed = DetailedInspectorEnabled;
            MdUI.SetVisible(_guidedInspectorContainer, !detailed);
            MdUI.SetVisible(_detailedInspectorContainer, detailed);
            if (_depthTabs != null) MdUI.SelectSegment(_depthTabs, detailed ? 1 : 0);
            RefreshInspector();
        }

        private void OpenDetailedInspector()
        {
            DetailedInspectorEnabled = true;
            ApplyInspectorDepthVisibility();
        }

        private void OnGuidedEditingStarted()
        {
            ResolveActiveGroupProperties();
            RebuildGroupList();
            NotifyPropertyChanges(true);
            RequestRefresh();
        }

        // Historical tests use this internal entry; normal UI calls the authoring service.
        internal static int EnsureGuidedLayer(LatticeDeformer deformer, MeshDeformerLayerType requiredType)
            => GuidedAuthoringService.EnsureLayer(deformer, requiredType, LatticeLocalization.Tr(LocKey.MeshDeformer));

        private VisualElement _groupsContainer; // Existing Inspector test seam; the section owns this element.
        private void OnStackStructureChanged()
        {
            ResolveActiveGroupProperties();
            NotifyPropertyChanges(true);
            RequestRefresh();
        }
        private void OnBlendShapeImported()
        {
            OnStackStructureChanged();
            RebuildGroupList();
        }
        private DeformerStackInspectorSection _stackInspector;
        internal DeformerStackInspectorSection StackInspector => _stackInspector;
        private void RebuildGroupList() => _stackInspector?.RebuildGroupList();
        private void CheckAndRebuildLayers() => _stackInspector?.CheckAndRebuildLayers();
        private void OnGroupReordered(int from, int to) => _stackInspector?.OnGroupReordered(from, to);
        private void OnLayerReordered(int from, int to)
        {
            if (target is LatticeDeformer d) _stackInspector?.MoveLayer(d, from, to);
        }
        private void MoveLayer(LatticeDeformer d, int from, int to) => _stackInspector?.MoveLayer(d, from, to);
        private void DeleteLayer(LatticeDeformer d, int index) => _stackInspector?.DeleteLayer(d, index);
        private void CopyLayer(LatticeDeformer d, int index) => _stackInspector?.CopyLayer(d, index);
        private void PasteLayer(LatticeDeformer d) => _stackInspector?.PasteLayer(d);
        private void DuplicateGroup(LatticeDeformer d, int index) => _stackInspector?.DuplicateGroup(d, index);
        private void CopyGroup(LatticeDeformer d, int index) => _stackInspector?.CopyGroup(d, index);
        private void PasteGroup(LatticeDeformer d) => _stackInspector?.PasteGroup(d);

        // Historical Inspector tests use these private entry points.
        private void EnterBlendShapeTestMode(LatticeDeformer deformer, SkinnedMeshRenderer renderer)
            => _blendShapeInspector?.EnterTestMode(deformer, renderer);
        private void ExitBlendShapeTestMode() => _blendShapeInspector?.ExitTestMode();
        internal BlendShapeInspectorSection BlendShapeInspector => _blendShapeInspector;

        private bool ProfileReadOnly => _stackInspector?.ProfileReadOnly == true;

        private void NotifyPropertyChanges()
        {
            NotifyPropertyChanges(false);
        }

        private void NotifyPropertyChanges(bool dataAlreadyInvalidated)
        {
            bool assignRuntimeMesh = LatticePreviewUtility.ShouldAssignRuntimeMesh();
            foreach (var instance in EnumerateTargets())
            {
                if (!dataAlreadyInvalidated) instance.InvalidateCache();
                instance.Deform(assignRuntimeMesh);
                if (!dataAlreadyInvalidated) LatticePrefabUtility.MarkModified(instance);
                LatticePreviewUtility.PublishInteractiveDeformation(instance);
            }

            if (targets.Length == 1 && target is LatticeDeformer activeDeformer)
            {
                SyncActiveToolToLayer(activeDeformer);
                _blendShapeInspector?.Refresh(activeDeformer);
            }

            LatticePreviewUtility.RequestSceneRepaint();
        }

        private void RebuildLayerList()
        {
            RebuildGroupList();
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
                // A type pattern alone does not apply Unity's fake-null check, so a
                // destroyed target (e.g. closed Prefab Stage while selected) must be
                // filtered explicitly before any component access.
                if (obj is LatticeDeformer deformer && deformer != null)
                {
                    yield return deformer;
                }
            }
        }

        private void TogglePreviewForTargets(bool enabled)
        {
            LatticeDeformerPreviewFilter.ForcePreviewState(enabled);
        }

        private MeshDeformerLayerType GetSerializedActiveLayerType() =>
            LayerSettingsEdit.TryReadActive(target as LatticeDeformer, out _, out var layer) ? layer.Type : MeshDeformerLayerType.Lattice;

        private void PerformEditOperation(Func<bool> operation)
        {
            serializedObject.ApplyModifiedProperties();
            bool changed = operation();
            serializedObject.Update();
            if (!changed) return;
            ResolveActiveGroupProperties();
            RebuildGroupList();
            NotifyPropertyChanges(true);
        }

        private void PerformSingleLayerOperation(LatticeDeformer deformer, string undoLabel, System.Func<LatticeDeformer, bool> op)
        {
            PerformEditOperation(() => DeformerEditService.Execute(deformer, undoLabel, op));
        }

        private static void SyncActiveToolToLayer(LatticeDeformer deformer)
        {
            if (deformer == null) return;
            if (ToolManager.activeToolType == typeof(MeshDeformerTool))
            {
                SceneView.RepaintAll();
            }
        }

    }
}
#endif
