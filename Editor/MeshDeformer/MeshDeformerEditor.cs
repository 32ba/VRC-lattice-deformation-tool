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
    [ExcludeFromCodeCoverage]
    public sealed class LatticeDeformerEditor : UnityEditor.Editor
    {
        private SerializedProperty _settingsProp;
        private SerializedProperty _groupsProp;
        private SerializedProperty _activeGroupIndexProp;
        private ProfileInspectorSection _profileInspector;
        private ValidationInspectorSection _validationInspector;
        private SupportInspectorSection _supportInspector;
        private MeshRebuildInspectorSection _rebuildInspector;
        // These are resolved per-frame from the active group
        private SerializedProperty _layersProp;
        private SerializedProperty _activeLayerIndexProp;
        private SerializedProperty _skinnedRendererProp;
        private SerializedProperty _meshFilterProp;
        private ClearanceInspectorSection _clearanceInspector;
        private BlendShapeInspectorSection _blendShapeInspector;
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

        private static bool s_showAlignSettings = false;
        private static readonly Dictionary<long, Vector3Int> s_pendingGridSizes = new();
        private const string DetailedInspectorSessionKeyPrefix =
            "Net32Ba.LatticeDeformationTool.DetailedInspector.";

        private VisualElement _guidedInspectorContainer;
        private VisualElement _detailedInspectorContainer;
        private GuidedInspectorSection _guidedInspector;
        internal GuidedInspectorSection GuidedInspector => _guidedInspector;

        private void OnEnable()
        {
            EnsureLinkIcons();
            _settingsProp = serializedObject.FindProperty("_settings");
            _groupsProp = serializedObject.FindProperty("_groups");
            _activeGroupIndexProp = serializedObject.FindProperty("_activeGroupIndex");
            _blendShapeInspector = new BlendShapeInspectorSection(this, NotifyPropertyChanges, OnBlendShapeImported);
            _stackInspector = new DeformerStackInspectorSection(this, _blendShapeInspector.DrawGroup,
                DrawActiveLayerSettings, _blendShapeInspector.DrawImport, OnStackStructureChanged);
            _profileInspector = new ProfileInspectorSection(this, RebuildGroupList);
            _guidedInspector = new GuidedInspectorSection(this, AutoAssignLocalRendererReferences,
                OpenDetailedInspector, OnGuidedEditingStarted, NotifyPropertyChanges);
            _rebuildInspector = new MeshRebuildInspectorSection(serializedObject);
            _supportInspector = new SupportInspectorSection(this);
            _validationInspector = new ValidationInspectorSection(this, NotifyPropertyChanges);
            _skinnedRendererProp = serializedObject.FindProperty("_skinnedMeshRenderer");
            _meshFilterProp = serializedObject.FindProperty("_meshFilter");
            _clearanceInspector = new ClearanceInspectorSection(this, OnClearanceLayersChanged);
            _clearanceInspector.Session.Changed += OnClearanceStateChanged;
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
            ReleaseChecker.OnUpdateCheckCompleted += Repaint;
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
            ReleaseChecker.OnUpdateCheckCompleted -= Repaint;
            _clearanceInspector?.Dispose();
            _clearanceInspector = null;
            _blendShapeInspector?.Dispose();
            _blendShapeInspector = null;
            _stackInspector?.Dispose();
            _stackInspector = null;
            _profileInspector = null;
            _guidedInspector = null;
            _rebuildInspector = null;
            _supportInspector = null;
            _validationInspector = null;

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

            _guidedInspectorContainer = new VisualElement();
            _guidedInspectorContainer.Add(new IMGUIContainer(DrawGuidedInspector));
            root.Add(_guidedInspectorContainer);

            _detailedInspectorContainer = new VisualElement();
            _detailedInspectorContainer.Add(new IMGUIContainer(DrawDetailedInspectorNavigation));

            // Top: Language + Mesh Source (IMGUI)
            _detailedInspectorContainer.Add(new IMGUIContainer(DrawTopSection));

            // Groups > Layers nested structure (UI Toolkit)
            if (targets.Length == 1)
            {
                _groupsContainer = _stackInspector.Root;
                RebuildGroupList();
                _detailedInspectorContainer.Add(_groupsContainer);
            }

            // Build Options + Open Editor (IMGUI)
            _detailedInspectorContainer.Add(new IMGUIContainer(DrawBottomSection));
            root.Add(_detailedInspectorContainer);

            ApplyInspectorDepthVisibility();

            // Track serialized changes
            root.TrackSerializedObjectValue(serializedObject, _ =>
            {
                CheckAndRebuildLayers();
                NotifyPropertyChanges();
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
            if (_guidedInspectorContainer != null)
            {
                _guidedInspectorContainer.style.display = detailed ? DisplayStyle.None : DisplayStyle.Flex;
            }
            if (_detailedInspectorContainer != null)
            {
                _detailedInspectorContainer.style.display = detailed ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void DrawDetailedInspectorNavigation()
        {
            if (GUILayout.Button(LatticeLocalization.Tr(LocKey.ReturnToGuidedInspector)))
            {
                DetailedInspectorEnabled = false;
                ApplyInspectorDepthVisibility();
            }
        }

        private void DrawGuidedInspector() => _guidedInspector?.Draw();

        private void OpenDetailedInspector()
        {
            DetailedInspectorEnabled = true;
            ApplyInspectorDepthVisibility();
        }

        private void OnGuidedEditingStarted()
        {
            ResolveActiveGroupProperties();
            InitializePendingGridSizes();
            RebuildGroupList();
            NotifyPropertyChanges(true);
        }

        // Historical tests use this internal entry; normal UI calls the authoring service.
        internal static int EnsureGuidedLayer(LatticeDeformer deformer, MeshDeformerLayerType requiredType)
            => GuidedAuthoringService.EnsureLayer(deformer, requiredType, LatticeLocalization.Tr(LocKey.MeshDeformer));

        private VisualElement _groupsContainer; // Existing Inspector test seam; the section owns this element.
        private void OnStackStructureChanged()
        {
            ResolveActiveGroupProperties();
            InitializePendingGridSizes();
            NotifyPropertyChanges(true);
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

        private void DrawActiveLayerSettings()
        {
            if (target is not LatticeDeformer d || d == null) return;
            serializedObject.Update();
            ResolveActiveGroupProperties();
            if (GetSerializedActiveLayerType() == MeshDeformerLayerType.Brush) DrawBrushLayerSettings(d);
            else
            {
                DrawResetLatticeBoxControls(); DrawGridSizeControls(d);
                DrawSettingsExcludingGrid(GetActiveSettingsProperty(d), allowStructureEdits: true);
                DrawAlignmentSettings();
            }
            if (LatticeDeformationFeatureFlags.AdvancedBlendShapes) _blendShapeInspector.DrawLayer();
            if (serializedObject.ApplyModifiedProperties()) NotifyPropertyChanges();
        }

        private bool ProfileReadOnly => _stackInspector?.ProfileReadOnly == true;

        private void DrawTopSection()
        {
            // The Inspector can repaint once more after the inspected object was
            // destroyed (e.g. closing a Prefab Stage); bail out before touching it.
            if (target == null) return;

            AutoAssignLocalRendererReferences();
            serializedObject.Update();
            ResolveActiveGroupProperties();

            bool hasSkinnedAssigned = _skinnedRendererProp != null && !_skinnedRendererProp.hasMultipleDifferentValues && _skinnedRendererProp.objectReferenceValue != null;
            bool hasMeshAssigned = _meshFilterProp != null && !_meshFilterProp.hasMultipleDifferentValues && _meshFilterProp.objectReferenceValue != null;
            bool disableSkinnedField = ShouldDisableRendererField<SkinnedMeshRenderer>() || hasMeshAssigned;
            bool disableMeshField = ShouldDisableRendererField<MeshFilter>() || hasSkinnedAssigned;

            DrawLanguageSelector();
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(LatticeLocalization.Tr(LocKey.MeshDeformerOverview), MessageType.Info);
            EditorGUILayout.Space();
            ReleaseNotificationGUI.Draw();

            if (LatticeDeformationFeatureFlags.DeformerProfiles)
            {
                DrawProfileSection();
            }

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

        private void DrawProfileSection() => _profileInspector?.Draw();

        private void DrawBottomSection()
        {
            if (target == null) return;

            serializedObject.Update();
            ResolveActiveGroupProperties();

            _rebuildInspector.Draw();
            if (LatticeDeformationFeatureFlags.ClearanceTools)
            {
                DrawClearanceHeatmapSettings();
            }

            bool modified = serializedObject.ApplyModifiedProperties();
            if (modified)
            {
                NotifyPropertyChanges();
            }

            if (LatticeDeformationFeatureFlags.ValidationDiagnostics)
            {
                _validationInspector.Draw();
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
                    MeshDeformerTool.UseDetailedOverlay();
                    ToolManager.SetActiveTool<MeshDeformerTool>();
                    LatticePreviewUtility.RequestSceneRepaint();
                }
            }

            _supportInspector.Draw();
        }

        internal IReadOnlyList<MeshDeformerDiagnostic> GetCachedValidationDiagnostics(LatticeDeformer deformer)
            => _validationInspector.State.Read(deformer);
        internal static int ComputeValidationStateHash(LatticeDeformer deformer)
            => InspectorValidationState.ComputeValidationStateHash(deformer);

        private void NotifyPropertyChanges()
        {
            NotifyPropertyChanges(false);
        }

        private void NotifyPropertyChanges(bool dataAlreadyInvalidated)
        {
            InvalidateClearanceEvaluation();
            bool assignRuntimeMesh = LatticePreviewUtility.ShouldAssignRuntimeMesh();
            foreach (var instance in EnumerateTargets())
            {
                if (!dataAlreadyInvalidated) instance.InvalidateCache();
                instance.Deform(assignRuntimeMesh);
                if (!dataAlreadyInvalidated) LatticePrefabUtility.MarkModified(instance);
            }

            if (targets.Length == 1 && target is LatticeDeformer activeDeformer)
            {
                SyncActiveToolToLayer(activeDeformer);
                _blendShapeInspector?.Refresh(activeDeformer);
            }

            LatticePreviewUtility.RequestSceneRepaint();
        }

        private void DrawClearanceHeatmapSettings() => _clearanceInspector.Draw();

        internal ClearanceAuthoringSession ClearanceSession => _clearanceInspector.Session;

        private void OnClearanceLayersChanged()
        {
            serializedObject.Update();
            ResolveActiveGroupProperties();
            InitializePendingGridSizes();
            RebuildLayerList();
        }

        private void OnClearanceStateChanged()
        {
            _validationInspector?.State.Invalidate();
            Repaint();
            SceneView.RepaintAll();
        }

        private void InvalidateClearanceEvaluation()
        {
            _clearanceInspector?.Session.Invalidate();
            _validationInspector?.State.Invalidate();
        }

        internal ClearanceHeatmapEvaluation GetClearanceEvaluation(
            LatticeDeformer deformer,
            Renderer reference,
            ClearanceQueryMode queryMode,
            float warningDistance,
            float targetDistance,
            float updateInterval)
            => ClearanceSession.GetClearanceEvaluation(deformer, reference, queryMode, warningDistance, targetDistance, updateInterval);

        internal ClearanceHeatmapRawEvaluation GetFitCorrectionRawEvaluation(
            LatticeDeformer deformer,
            Renderer reference,
            ClearanceQueryMode queryMode,
            float updateInterval)
            => ClearanceSession.GetFitCorrectionRawEvaluation(deformer, reference, queryMode, updateInterval);

        internal FitCorrectionPlan GetCachedFitCorrectionPlan(
            LatticeDeformer deformer,
            ClearanceHeatmapRawEvaluation rawEvaluation,
            Renderer reference,
            ClearanceQueryMode queryMode,
            FitCorrectionScope scope,
            float warningDistance,
            float targetDistance,
            float maximumMove,
            FitCorrectionConstraintOptions constraints)
            => ClearanceSession.GetCachedFitCorrectionPlan(deformer, rawEvaluation, reference, queryMode, scope, warningDistance, targetDistance, maximumMove, constraints);

        internal static int ComputeFitCorrectionPlanKey(
            LatticeDeformer deformer,
            ClearanceHeatmapRawEvaluation rawEvaluation,
            Renderer reference,
            ClearanceQueryMode queryMode,
            FitCorrectionScope scope,
            float warningDistance,
            float targetDistance,
            float maximumMove,
            FitCorrectionConstraintOptions constraints)
            => ClearanceAuthoringSession.ComputeFitCorrectionPlanKey(deformer, rawEvaluation, reference, queryMode, scope, warningDistance, targetDistance, maximumMove, constraints);

        internal static int CalculateAdaptiveHeatmapStride(
            int vertexCount,
            int requestedStride,
            int pointBudget = 4096)
            => ClearanceSceneDrawer.CalculateAdaptiveHeatmapStride(vertexCount, requestedStride, pointBudget);

        internal static Renderer ResolveClearanceTargetRenderer(LatticeDeformer deformer,
            Renderer previewProxy, out bool usedPreviewProxy) =>
            ClearanceAuthoringSession.ResolveClearanceTargetRenderer(deformer, previewProxy, out usedPreviewProxy);

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

            bool showTooltips = LatticeLocalization.ShowTooltips;
            bool newShowTooltips = EditorGUILayout.Toggle(LatticeLocalization.Content(LocKey.ShowTooltips), showTooltips);
            if (newShowTooltips != showTooltips)
            {
                LatticeLocalization.ShowTooltips = newShowTooltips;
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

        private void PerformEditOperation(Func<bool> operation)
        {
            serializedObject.ApplyModifiedProperties();
            bool changed = operation();
            serializedObject.Update();
            if (!changed) return;
            ResolveActiveGroupProperties();
            InitializePendingGridSizes();
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

