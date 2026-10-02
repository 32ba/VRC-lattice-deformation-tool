#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>Draws clearance settings and routes explicit actions to its session.</summary>
    internal sealed class ClearanceInspectorSection : IDisposable
    {
        private readonly UnityEditor.Editor _editor;
        private readonly Action _onLayersChanged;
        private readonly ClearanceAuthoringSession _session = new();
        private bool _disposed;
        private static bool s_showClearanceHeatmapSettings;
        private SerializedObject serializedObject => _editor.serializedObject;
        private UnityEngine.Object[] targets => _editor.targets;
        private UnityEngine.Object target => _editor.target;
        internal ClearanceAuthoringSession Session => _session;
        private SerializedProperty _showClearanceHeatmapProp;
        private SerializedProperty _clearanceReferenceRendererProp;
        private SerializedProperty _clearanceQueryModeProp;
        private SerializedProperty _clearanceHeatmapDisplayModeProp;
        private SerializedProperty _clearanceWarningDistanceProp;
        private SerializedProperty _clearanceTargetDistanceProp;
        private SerializedProperty _clearanceDisplayStrideProp;
        private SerializedProperty _clearanceUpdateIntervalProp;
        private SerializedProperty _clearanceScanSetProp;
        private SerializedProperty _clearanceScanAvatarRootProp;
        private SerializedProperty _fitCorrectionScopeProp;
        private SerializedProperty _fitCorrectionMaximumMoveProp;
        private SerializedProperty _fitCorrectionUseVertexMaskProp;
        private SerializedProperty _fitCorrectionPinOpenBoundariesProp;
        private SerializedProperty _fitCorrectionIsolateComponentsProp;
        private SerializedProperty _fitCorrectionSmoothSurfaceProp;
        private SerializedProperty _fitCorrectionSmoothingIterationsProp;
        private SerializedProperty _fitCorrectionSmoothingStrengthProp;
        private SerializedProperty _fitCorrectionPreserveClearanceProp;
        private SerializedProperty _fitCorrectionUseSymmetryProp;
        private SerializedProperty _fitCorrectionSymmetryAxisProp;
        private SerializedProperty _fitCorrectionSymmetryToleranceProp;
        private SerializedProperty _fitCorrectionPreviewProp;

        internal ClearanceInspectorSection(UnityEditor.Editor editor, Action onLayersChanged)
        {
            _editor = editor;
            _onLayersChanged = onLayersChanged;
            _showClearanceHeatmapProp = serializedObject.FindProperty("_showClearanceHeatmap");
            _clearanceReferenceRendererProp = serializedObject.FindProperty("_clearanceReferenceRenderer");
            _clearanceQueryModeProp = serializedObject.FindProperty("_clearanceQueryMode");
            _clearanceHeatmapDisplayModeProp = serializedObject.FindProperty("_clearanceHeatmapDisplayMode");
            _clearanceWarningDistanceProp = serializedObject.FindProperty("_clearanceWarningDistance");
            _clearanceTargetDistanceProp = serializedObject.FindProperty("_clearanceTargetDistance");
            _clearanceDisplayStrideProp = serializedObject.FindProperty("_clearanceDisplayStride");
            _clearanceUpdateIntervalProp = serializedObject.FindProperty("_clearanceUpdateInterval");
            _clearanceScanSetProp = serializedObject.FindProperty("_clearanceScanSet");
            _clearanceScanAvatarRootProp = serializedObject.FindProperty("_clearanceScanAvatarRoot");
            _fitCorrectionScopeProp = serializedObject.FindProperty("_fitCorrectionScope");
            _fitCorrectionMaximumMoveProp = serializedObject.FindProperty("_fitCorrectionMaximumMove");
            _fitCorrectionUseVertexMaskProp = serializedObject.FindProperty("_fitCorrectionUseVertexMask");
            _fitCorrectionPinOpenBoundariesProp = serializedObject.FindProperty("_fitCorrectionPinOpenBoundaries");
            _fitCorrectionIsolateComponentsProp = serializedObject.FindProperty("_fitCorrectionIsolateComponents");
            _fitCorrectionSmoothSurfaceProp = serializedObject.FindProperty("_fitCorrectionSmoothSurface");
            _fitCorrectionSmoothingIterationsProp = serializedObject.FindProperty("_fitCorrectionSmoothingIterations");
            _fitCorrectionSmoothingStrengthProp = serializedObject.FindProperty("_fitCorrectionSmoothingStrength");
            _fitCorrectionPreserveClearanceProp = serializedObject.FindProperty("_fitCorrectionPreserveClearance");
            _fitCorrectionUseSymmetryProp = serializedObject.FindProperty("_fitCorrectionUseSymmetry");
            _fitCorrectionSymmetryAxisProp = serializedObject.FindProperty("_fitCorrectionSymmetryAxis");
            _fitCorrectionSymmetryToleranceProp = serializedObject.FindProperty("_fitCorrectionSymmetryTolerance");
            _fitCorrectionPreviewProp = serializedObject.FindProperty("_fitCorrectionPreview");
            if (LatticeDeformationFeatureFlags.ClearanceTools)
                SceneView.duringSceneGui += DrawClearanceHeatmapInScene;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SceneView.duringSceneGui -= DrawClearanceHeatmapInScene;
            _session.Dispose();
        }

        private void DrawClearanceHeatmapInScene(SceneView sceneView)
        {
            if (_disposed || _editor == null || Event.current?.type != EventType.Repaint ||
                targets == null || targets.Length != 1 || target is not LatticeDeformer deformer ||
                !deformer.ShowClearanceHeatmap) return;
            var evaluation = _session.GetClearanceEvaluation(deformer, deformer.ClearanceReferenceRenderer,
                deformer.ClearanceQueryMode, deformer.ClearanceWarningDistance,
                deformer.ClearanceTargetDistance, deformer.ClearanceUpdateInterval);
            ClearanceSceneDrawer.Draw(evaluation, deformer.ClearanceHeatmapDisplayMode,
                deformer.ClearanceDisplayStride, _session.PreviewPlan);
        }

        internal void Draw()
        {
            if (_disposed || _editor == null) return;
            if (_showClearanceHeatmapProp == null) return;

            EditorGUILayout.Space();
            s_showClearanceHeatmapSettings = EditorGUILayout.BeginFoldoutHeaderGroup(
                s_showClearanceHeatmapSettings,
                LatticeLocalization.Tr(LocKey.ClearanceHeatmap));
            if (s_showClearanceHeatmapSettings)
            {
                EditorGUILayout.PropertyField(
                    _showClearanceHeatmapProp,
                    LatticeLocalization.Content(LocKey.ShowClearanceHeatmap));
                EditorGUILayout.PropertyField(
                    _clearanceReferenceRendererProp,
                    LatticeLocalization.Content(LocKey.ClearanceReferenceRenderer));
                _clearanceQueryModeProp.enumValueIndex = EditorGUILayout.Popup(
                    LatticeLocalization.Content(LocKey.ClearanceQueryMode),
                    _clearanceQueryModeProp.enumValueIndex,
                    new[]
                    {
                        LatticeLocalization.Content(LocKey.ClearanceReferenceNormal),
                        LatticeLocalization.Content(LocKey.ClearanceClosedMesh)
                    });
                _clearanceHeatmapDisplayModeProp.enumValueIndex = EditorGUILayout.Popup(
                    LatticeLocalization.Content(LocKey.ClearanceDisplayMode),
                    _clearanceHeatmapDisplayModeProp.enumValueIndex,
                    new[]
                    {
                        LatticeLocalization.Content(LocKey.ClearancePenetrationOnly),
                        LatticeLocalization.Content(LocKey.ClearanceIncludeWarning),
                        LatticeLocalization.Content(LocKey.ClearanceFullDistribution)
                    });

                DrawMillimeterField(
                    _clearanceWarningDistanceProp,
                    LocKey.ClearanceWarningThresholdMm,
                    0f);
                DrawMillimeterField(
                    _clearanceTargetDistanceProp,
                    LocKey.ClearanceTargetDistanceMm,
                    _clearanceWarningDistanceProp.floatValue);
                _clearanceDisplayStrideProp.intValue = EditorGUILayout.IntSlider(
                    LatticeLocalization.Content(LocKey.ClearanceDisplayStride),
                    Mathf.Clamp(_clearanceDisplayStrideProp.intValue, 1, 64),
                    1,
                    64);
                float currentUpdateInterval = IsFinite(_clearanceUpdateIntervalProp.floatValue)
                    ? _clearanceUpdateIntervalProp.floatValue
                    : 0.1f;
                _clearanceUpdateIntervalProp.floatValue = EditorGUILayout.Slider(
                    LatticeLocalization.Content(LocKey.ClearanceUpdateInterval),
                    Mathf.Clamp(currentUpdateInterval, 0.02f, 2f),
                    0.02f,
                    2f);

                if (targets.Length == 1 && target is LatticeDeformer deformer &&
                    _showClearanceHeatmapProp.boolValue)
                {
                    Renderer reference = _clearanceReferenceRendererProp.objectReferenceValue as Renderer;
                    var evaluation = _session.GetClearanceEvaluation(
                        deformer,
                        reference,
                        (ClearanceQueryMode)_clearanceQueryModeProp.enumValueIndex,
                        _clearanceWarningDistanceProp.floatValue,
                        _clearanceTargetDistanceProp.floatValue,
                        _clearanceUpdateIntervalProp.floatValue);
                    DrawClearanceStatistics(evaluation);
                    DrawClearanceScanControls(
                        deformer,
                        reference,
                        (ClearanceQueryMode)_clearanceQueryModeProp.enumValueIndex,
                        _clearanceWarningDistanceProp.floatValue,
                        _clearanceTargetDistanceProp.floatValue);
                    DrawClearanceReportControls(
                        deformer,
                        reference,
                        (ClearanceQueryMode)_clearanceQueryModeProp.enumValueIndex,
                        _clearanceWarningDistanceProp.floatValue,
                        _clearanceTargetDistanceProp.floatValue,
                        evaluation);
                    DrawFitCorrectionControls(
                        deformer,
                        reference,
                        (ClearanceQueryMode)_clearanceQueryModeProp.enumValueIndex,
                        _clearanceWarningDistanceProp.floatValue,
                        _clearanceTargetDistanceProp.floatValue);
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private static void DrawMillimeterField(
            SerializedProperty property,
            string labelKey,
            float minimumMeters)
        {
            float currentMeters = IsFinite(property.floatValue)
                ? property.floatValue
                : minimumMeters;
            float millimeters = currentMeters * 1000f;
            float next = EditorGUILayout.FloatField(
                LatticeLocalization.Content(labelKey),
                millimeters);
            property.floatValue = IsFinite(next)
                ? Mathf.Max(minimumMeters, next / 1000f)
                : minimumMeters;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void DrawClearanceStatistics(ClearanceHeatmapEvaluation evaluation)
        {
            if (evaluation == null || evaluation.Status != ClearanceEvaluationStatus.Valid)
            {
                string message = evaluation?.Status == ClearanceEvaluationStatus.InvalidReference
                    ? LatticeLocalization.Tr(LocKey.ClearanceInvalidReference)
                    : LatticeLocalization.Tr(LocKey.ClearanceInvalidTarget);
                EditorGUILayout.HelpBox(message, MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField(
                LatticeLocalization.Tr(LocKey.ClearanceEvaluationTarget),
                LatticeLocalization.Tr(_session.UsedPreviewProxy
                    ? LocKey.ClearanceTargetPreview
                    : LocKey.ClearanceTargetRendered));
            EditorGUILayout.LabelField(
                LatticeLocalization.Tr(LocKey.ClearanceMinimum),
                (evaluation.Statistics.MinimumClearance * 1000f).ToString("0.###") + " mm");
            EditorGUILayout.LabelField(
                LatticeLocalization.Tr(LocKey.ClearanceMaximumPenetration),
                (evaluation.Statistics.MaximumPenetrationDepth * 1000f).ToString("0.###") + " mm");
            EditorGUILayout.LabelField(
                LatticeLocalization.Tr(LocKey.ClearanceViolationVertices),
                evaluation.Statistics.ViolationVertexCount.ToString());
            EditorGUILayout.LabelField(
                LatticeLocalization.Tr(LocKey.ClearanceEvaluatedVertices),
                evaluation.Statistics.EvaluatedVertexCount.ToString());
            EditorGUILayout.LabelField(
                LatticeLocalization.Tr(LocKey.ClearanceActualQueryMode),
                evaluation.SignMode == ClearanceSignMode.ClosedMesh
                    ? LatticeLocalization.Tr(LocKey.ClearanceClosedMesh)
                    : LatticeLocalization.Tr(LocKey.ClearanceReferenceNormal));

            if (_clearanceQueryModeProp.enumValueIndex == (int)ClearanceQueryMode.ClosedMesh &&
                evaluation.SignMode != ClearanceSignMode.ClosedMesh)
            {
                EditorGUILayout.HelpBox(
                    LatticeLocalization.Tr(LocKey.ClearanceSignFallback),
                    MessageType.Warning);
            }

            double age = EditorApplication.timeSinceStartup - _session.EvaluationTime;
            EditorGUILayout.LabelField(
                LatticeLocalization.Tr(LocKey.ClearanceResultAge),
                age.ToString("0.00") + " s");
        }

        private void DrawClearanceScanControls(
            LatticeDeformer deformer,
            Renderer reference,
            ClearanceQueryMode queryMode,
            float warningDistance,
            float targetDistance)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                LatticeLocalization.Tr(LocKey.ClearanceScan),
                EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(
                _clearanceScanSetProp,
                LatticeLocalization.Content(LocKey.ClearanceScanSet));
            EditorGUILayout.PropertyField(
                _clearanceScanAvatarRootProp,
                LatticeLocalization.Content(LocKey.ClearanceScanAvatarRoot));

            if (_session.ScanOperation != null)
            {
                Rect progressRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                string progressText = string.Format(
                    LatticeLocalization.Tr(LocKey.ClearanceScanProgressFormat),
                    _session.ScanOperation.NextConditionIndex,
                    (_clearanceScanSetProp.objectReferenceValue as ClearanceScanSet)?.Conditions.Count ?? 0,
                    _session.ScanOperation.CurrentConditionName);
                EditorGUI.ProgressBar(progressRect, _session.ScanOperation.Progress, progressText);
                if (GUILayout.Button(LatticeLocalization.Tr(LocKey.ClearanceScanCancel)))
                {
                    _session.CancelScan();
                }
            }
            else
            {
                using (new EditorGUI.DisabledScope(
                           _clearanceScanSetProp.objectReferenceValue == null || reference == null))
                {
                    if (GUILayout.Button(LatticeLocalization.Tr(LocKey.ClearanceScanRun)))
                    {
                        serializedObject.ApplyModifiedProperties();
                        _session.StartScan(deformer, reference, queryMode, warningDistance, targetDistance);
                    }
                }
            }

            DrawClearanceScanResult(
                deformer,
                reference,
                queryMode,
                warningDistance,
                targetDistance);
        }

        private void DrawClearanceScanResult(
            LatticeDeformer deformer,
            Renderer reference,
            ClearanceQueryMode queryMode,
            float warningDistance,
            float targetDistance)
        {
            if (_session.ScanResult == null) return;
            EditorGUILayout.HelpBox(
                string.Format(
                    LatticeLocalization.Tr(LocKey.ClearanceScanSummaryFormat),
                    _session.ScanResult.SuccessfulConditionCount,
                    _session.ScanResult.Conditions.Count,
                    _session.ScanResult.WorstConditionIndex),
                _session.ScanResult.WasCancelled ? MessageType.Warning : MessageType.Info);

            for (int index = 0; index < _session.ScanResult.Conditions.Count; index++)
            {
                ClearanceScanConditionResult condition = _session.ScanResult.Conditions[index];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    string label = condition.IsSuccess
                        ? string.Format(
                            LatticeLocalization.Tr(LocKey.ClearanceScanConditionSuccessFormat),
                            condition.ConditionName,
                            condition.Statistics.MinimumClearance * 1000f,
                            condition.Statistics.ViolationVertexCount,
                            condition.UsedNdmfPreviewProxy ? "NDMF Proxy" : "Original")
                        : string.Format(
                            LatticeLocalization.Tr(LocKey.ClearanceScanConditionErrorFormat),
                            condition.ConditionName,
                            condition.ErrorMessage);
                    EditorGUILayout.LabelField(label, EditorStyles.wordWrappedLabel);
                    if (!condition.IsSuccess) continue;
                    if (GUILayout.Button(LatticeLocalization.Tr(LocKey.ClearanceScanApplyCondition)))
                    {
                        _session.ApplyScanCondition(condition.ConditionIndex, deformer, reference,
                            queryMode, warningDistance, targetDistance);
                    }
                }
            }

            if (_session.HasScanPreview &&
                GUILayout.Button(LatticeLocalization.Tr(LocKey.ClearanceScanRestoreScene)))
            {
                _session.RestoreScanPreview();
            }
        }

        private void DrawFitCorrectionControls(
            LatticeDeformer deformer,
            Renderer reference,
            ClearanceQueryMode queryMode,
            float warningDistance,
            float targetDistance)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                LatticeLocalization.Tr(LocKey.FitCorrection),
                EditorStyles.boldLabel);
            _fitCorrectionScopeProp.enumValueIndex = EditorGUILayout.Popup(
                LatticeLocalization.Content(LocKey.FitCorrectionScope),
                _fitCorrectionScopeProp.enumValueIndex,
                new[]
                {
                    LatticeLocalization.Content(LocKey.FitCorrectionPenetrationOnly),
                    LatticeLocalization.Content(LocKey.FitCorrectionWarningThreshold),
                    LatticeLocalization.Content(LocKey.FitCorrectionTargetClearance)
                });
            DrawMillimeterField(
                _fitCorrectionMaximumMoveProp,
                LocKey.FitCorrectionMaximumMoveMm,
                0f);
            EditorGUILayout.LabelField(
                LatticeLocalization.Tr(LocKey.FitCorrectionConstraints),
                EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(
                _fitCorrectionUseVertexMaskProp,
                LatticeLocalization.Content(LocKey.FitCorrectionUseVertexMask));
            EditorGUILayout.PropertyField(
                _fitCorrectionPinOpenBoundariesProp,
                LatticeLocalization.Content(LocKey.FitCorrectionPinOpenBoundaries));
            EditorGUILayout.PropertyField(
                _fitCorrectionIsolateComponentsProp,
                LatticeLocalization.Content(LocKey.FitCorrectionIsolateComponents));
            EditorGUILayout.PropertyField(
                _fitCorrectionSmoothSurfaceProp,
                LatticeLocalization.Content(LocKey.FitCorrectionSmoothSurface));
            if (_fitCorrectionSmoothSurfaceProp.boolValue)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    _fitCorrectionSmoothingIterationsProp.intValue = EditorGUILayout.IntSlider(
                        LatticeLocalization.Content(LocKey.FitCorrectionSmoothingIterations),
                        _fitCorrectionSmoothingIterationsProp.intValue,
                        1,
                        10);
                    _fitCorrectionSmoothingStrengthProp.floatValue = EditorGUILayout.Slider(
                        LatticeLocalization.Content(LocKey.FitCorrectionSmoothingStrength),
                        _fitCorrectionSmoothingStrengthProp.floatValue,
                        0f,
                        1f);
                }
            }
            EditorGUILayout.PropertyField(
                _fitCorrectionPreserveClearanceProp,
                LatticeLocalization.Content(LocKey.FitCorrectionPreserveClearance));
            EditorGUILayout.PropertyField(
                _fitCorrectionUseSymmetryProp,
                LatticeLocalization.Content(LocKey.FitCorrectionUseSymmetry));
            if (_fitCorrectionUseSymmetryProp.boolValue)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    _fitCorrectionSymmetryAxisProp.intValue = EditorGUILayout.Popup(
                        LatticeLocalization.Content(LocKey.FitCorrectionSymmetryAxis),
                        _fitCorrectionSymmetryAxisProp.intValue,
                        new[] { new GUIContent("X"), new GUIContent("Y"), new GUIContent("Z") });
                    DrawMillimeterField(
                        _fitCorrectionSymmetryToleranceProp,
                        LocKey.FitCorrectionSymmetryToleranceMm,
                        0.001f);
                }
            }
            EditorGUILayout.PropertyField(
                _fitCorrectionPreviewProp,
                LatticeLocalization.Content(LocKey.FitCorrectionPreview));

            FitCorrectionConstraintOptions constraints = GetFitCorrectionConstraints();

            ClearanceHeatmapRawEvaluation rawEvaluation = _session.GetFitCorrectionRawEvaluation(
                deformer,
                reference,
                queryMode,
                _clearanceUpdateIntervalProp.floatValue);

            FitCorrectionScope scope = (FitCorrectionScope)_fitCorrectionScopeProp.enumValueIndex;
            var plan = _session.FitCorrectionRawIsThrottledStale
                ? _session.ThrottledStaleFitCorrectionPlan
                : _session.GetCachedFitCorrectionPlan(
                    deformer,
                    rawEvaluation,
                    reference,
                    queryMode,
                    scope,
                    warningDistance,
                    targetDistance,
                    _fitCorrectionMaximumMoveProp.floatValue,
                    constraints);
            _session.SetFitCorrectionPreview(_fitCorrectionPreviewProp.boolValue && plan.CanGenerate
                ? plan : null);
            DrawFitCorrectionPlan(plan);

            using (new EditorGUI.DisabledScope(!plan.CanGenerate))
            {
                if (GUILayout.Button(LatticeLocalization.Tr(LocKey.CreateFitCorrectionLayer)))
                {
                    if (_session.CreateFitCorrectionLayer(
                        deformer,
                        reference,
                        queryMode,
                        (FitCorrectionScope)_fitCorrectionScopeProp.enumValueIndex,
                        warningDistance,
                        targetDistance,
                        _fitCorrectionMaximumMoveProp.floatValue,
                        constraints, LatticeLocalization.Tr(LocKey.CreateFitCorrectionLayer)))
                    {
                        deformer.Deform(LatticePreviewUtility.ShouldAssignRuntimeMesh());
                        _onLayersChanged?.Invoke();
                        LatticePreviewUtility.RequestSceneRepaint();
                    }
                }
            }

            if (_session.LastFitCorrectionReport != null &&
                _session.LastFitCorrectionReport.Status == FitCorrectionStatus.Success)
            {
                EditorGUILayout.HelpBox(
                    string.Format(
                        LatticeLocalization.Tr(LocKey.FitCorrectionResultFormat),
                        _session.LastFitCorrectionReport.ImprovedVertexCount,
                        _session.LastFitCorrectionReport.UnresolvedVertexCount),
                    MessageType.Info);
            }
        }

        private static void DrawFitCorrectionPlan(FitCorrectionPlan plan)
        {
            if (plan == null) return;
            if (plan.Status != FitCorrectionStatus.Ready)
            {
                string key = plan.Status switch
                {
                    FitCorrectionStatus.PosedSkinnedMeshUnsupported => LocKey.FitCorrectionPosedSkinnedBlocked,
                    FitCorrectionStatus.StaleEvaluation => LocKey.FitCorrectionStale,
                    FitCorrectionStatus.TopologyMismatch => LocKey.FitCorrectionTopologyMismatch,
                    FitCorrectionStatus.NoCandidates => LocKey.FitCorrectionNoCandidates,
                    FitCorrectionStatus.InvalidReference => LocKey.ClearanceInvalidReference,
                    _ => LocKey.ClearanceInvalidTarget
                };
                EditorGUILayout.HelpBox(LatticeLocalization.Tr(key), MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField(
                LatticeLocalization.Tr(LocKey.FitCorrectionCandidateVertices),
                plan.CandidateVertexCount.ToString());
            EditorGUILayout.LabelField(
                LatticeLocalization.Tr(LocKey.FitCorrectionMaximumPlannedMove),
                (plan.MaximumAppliedMove * 1000f).ToString("0.###") + " mm");
            EditorGUILayout.LabelField(
                LatticeLocalization.Tr(LocKey.FitCorrectionUnresolvedEstimate),
                plan.UnresolvedVertexCount.ToString());
        }

        private FitCorrectionConstraintOptions GetFitCorrectionConstraints()
        {
            return new FitCorrectionConstraintOptions(
                _fitCorrectionUseVertexMaskProp.boolValue,
                _fitCorrectionPinOpenBoundariesProp.boolValue,
                _fitCorrectionIsolateComponentsProp.boolValue,
                _fitCorrectionSmoothSurfaceProp.boolValue,
                _fitCorrectionSmoothingIterationsProp.intValue,
                _fitCorrectionSmoothingStrengthProp.floatValue,
                _fitCorrectionPreserveClearanceProp.boolValue,
                _fitCorrectionUseSymmetryProp.boolValue,
                _fitCorrectionSymmetryAxisProp.intValue,
                _fitCorrectionSymmetryToleranceProp.floatValue);
        }

        private void DrawClearanceReportControls(
            LatticeDeformer deformer,
            Renderer reference,
            ClearanceQueryMode queryMode,
            float warningDistance,
            float targetDistance,
            ClearanceHeatmapEvaluation currentEvaluation)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                LatticeLocalization.Tr(LocKey.ClearanceReport),
                EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(
                       currentEvaluation == null ||
                       currentEvaluation.Status != ClearanceEvaluationStatus.Valid))
            {
                if (GUILayout.Button(LatticeLocalization.Tr(LocKey.ClearanceReportExportCurrent)))
                {
                    Renderer evaluatedRenderer = ClearanceAuthoringSession.ResolveClearanceTargetRenderer(
                        deformer,
                        out bool usedPreviewProxy);
                    ExportClearanceReport(ClearanceQaReportBuilder.FromCurrentEvaluation(
                        deformer,
                        reference,
                        evaluatedRenderer,
                        currentEvaluation,
                        queryMode,
                        warningDistance,
                        targetDistance,
                        usedPreviewProxy));
                }
            }
            using (new EditorGUI.DisabledScope(
                       _session.ScanResult == null ||
                       _session.ScanResult.Conditions.Count == 0))
            {
                if (GUILayout.Button(LatticeLocalization.Tr(LocKey.ClearanceReportExportScan)))
                {
                    ExportClearanceReport(ClearanceQaReportBuilder.FromScanResult(
                        deformer,
                        reference,
                        _session.ScanResult));
                }
            }
        }

        private static void ExportClearanceReport(ClearanceQaReport report)
        {
            string jsonPath = EditorUtility.SaveFilePanel(
                LatticeLocalization.Tr(LocKey.ClearanceReport),
                "",
                "clearance-qa-report",
                "json");
            if (string.IsNullOrEmpty(jsonPath)) return;
            string markdownPath = Path.ChangeExtension(jsonPath, ".md");
            bool written = ClearanceQaReportWriter.TryWritePair(
                jsonPath,
                markdownPath,
                ClearanceQaReportBuilder.ToJson(report),
                ClearanceQaReportBuilder.ToMarkdown(report),
                out string error);
            EditorUtility.DisplayDialog(
                LatticeLocalization.Tr(LocKey.ClearanceReport),
                written
                    ? LatticeLocalization.Tr(LocKey.ClearanceReportExportSuccess)
                    : string.Format(
                        LatticeLocalization.Tr(LocKey.ClearanceReportExportFailure),
                        error),
                "OK");
        }

    }
}
#endif
