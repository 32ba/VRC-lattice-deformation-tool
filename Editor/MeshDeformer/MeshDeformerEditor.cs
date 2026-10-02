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
        private LayerSettingsInspectorSection _layerSettingsInspector;
        internal LayerSettingsInspectorSection LayerSettingsInspector => _layerSettingsInspector;
        private const string DetailedInspectorSessionKeyPrefix =
            "Net32Ba.LatticeDeformationTool.DetailedInspector.";

        private VisualElement _guidedInspectorContainer;
        private VisualElement _detailedInspectorContainer;
        private GuidedInspectorSection _guidedInspector;
        internal GuidedInspectorSection GuidedInspector => _guidedInspector;

        private void OnEnable()
        {
            _groupsProp = serializedObject.FindProperty("_groups");
            _activeGroupIndexProp = serializedObject.FindProperty("_activeGroupIndex");
            _blendShapeInspector = new BlendShapeInspectorSection(this, NotifyPropertyChanges, OnBlendShapeImported);
            _layerSettingsInspector = new LayerSettingsInspectorSection(this, NotifyPropertyChanges, _blendShapeInspector.DrawLayer);
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
            AutoAssignLocalRendererReferences();
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

            _layerSettingsInspector?.Dispose();
            _layerSettingsInspector = null;
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

        private void DrawActiveLayerSettings() => _layerSettingsInspector?.Draw();

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
