#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Unity.Profiling;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    public sealed partial class LatticeDeformerEditor
    {
        private void DrawClearanceHeatmapSettings()
        {
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
                    var evaluation = GetClearanceEvaluation(
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
                LatticeLocalization.Tr(_lastClearanceUsedPreviewProxy
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

            double age = EditorApplication.timeSinceStartup - _lastClearanceEvaluationTime;
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

            if (_clearanceScanOperation != null)
            {
                Rect progressRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                string progressText = string.Format(
                    LatticeLocalization.Tr(LocKey.ClearanceScanProgressFormat),
                    _clearanceScanOperation.NextConditionIndex,
                    (_clearanceScanSetProp.objectReferenceValue as ClearanceScanSet)?.Conditions.Count ?? 0,
                    _clearanceScanOperation.CurrentConditionName);
                EditorGUI.ProgressBar(progressRect, _clearanceScanOperation.Progress, progressText);
                if (GUILayout.Button(LatticeLocalization.Tr(LocKey.ClearanceScanCancel)))
                {
                    _clearanceScanOperation.Cancel();
                    FinishClearanceScan();
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
                        _clearanceScanPreviewState?.Dispose();
                        _clearanceScanPreviewState = null;
                        _clearanceScanOperation = new ClearanceScanOperation(
                            deformer.ClearanceScanSet,
                            deformer,
                            reference,
                            deformer.ClearanceScanAvatarRoot,
                            queryMode,
                            warningDistance,
                            targetDistance);
                        EditorApplication.update -= AdvanceClearanceScan;
                        EditorApplication.update += AdvanceClearanceScan;
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

        private void AdvanceClearanceScan()
        {
            if (_clearanceScanOperation == null)
            {
                EditorApplication.update -= AdvanceClearanceScan;
                return;
            }
            _clearanceScanOperation.Step();
            if (_clearanceScanOperation.IsCompleted) FinishClearanceScan();
            Repaint();
            SceneView.RepaintAll();
        }

        private void FinishClearanceScan()
        {
            EditorApplication.update -= AdvanceClearanceScan;
            if (_clearanceScanOperation == null) return;
            _clearanceScanResult = _clearanceScanOperation.Result;
            _clearanceScanOperation.Dispose();
            _clearanceScanOperation = null;
            InvalidateClearanceEvaluation();
            Repaint();
            SceneView.RepaintAll();
        }

        private void DrawClearanceScanResult(
            LatticeDeformer deformer,
            Renderer reference,
            ClearanceQueryMode queryMode,
            float warningDistance,
            float targetDistance)
        {
            if (_clearanceScanResult == null) return;
            EditorGUILayout.HelpBox(
                string.Format(
                    LatticeLocalization.Tr(LocKey.ClearanceScanSummaryFormat),
                    _clearanceScanResult.SuccessfulConditionCount,
                    _clearanceScanResult.Conditions.Count,
                    _clearanceScanResult.WorstConditionIndex),
                _clearanceScanResult.WasCancelled ? MessageType.Warning : MessageType.Info);

            for (int index = 0; index < _clearanceScanResult.Conditions.Count; index++)
            {
                ClearanceScanConditionResult condition = _clearanceScanResult.Conditions[index];
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
                        _clearanceScanPreviewState?.Dispose();
                        ClearanceScanPreviewState.TryApply(
                            _clearanceScanResult.ScanSet,
                            condition.ConditionIndex,
                            deformer,
                            reference,
                            deformer.ClearanceScanAvatarRoot,
                            queryMode,
                            warningDistance,
                            targetDistance,
                            out _clearanceScanPreviewState,
                            out _);
                        InvalidateClearanceEvaluation();
                        SceneView.RepaintAll();
                    }
                }
            }

            if (_clearanceScanPreviewState != null &&
                GUILayout.Button(LatticeLocalization.Tr(LocKey.ClearanceScanRestoreScene)))
            {
                _clearanceScanPreviewState.Dispose();
                _clearanceScanPreviewState = null;
                InvalidateClearanceEvaluation();
                SceneView.RepaintAll();
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

            ClearanceHeatmapRawEvaluation rawEvaluation = GetFitCorrectionRawEvaluation(
                deformer,
                reference,
                queryMode,
                _clearanceUpdateIntervalProp.floatValue);

            FitCorrectionScope scope = (FitCorrectionScope)_fitCorrectionScopeProp.enumValueIndex;
            var plan = _fitCorrectionRawIsThrottledStale
                ? _throttledStaleFitCorrectionPlan
                : GetCachedFitCorrectionPlan(
                    deformer,
                    rawEvaluation,
                    reference,
                    queryMode,
                    scope,
                    warningDistance,
                    targetDistance,
                    _fitCorrectionMaximumMoveProp.floatValue,
                    constraints);
            _fitCorrectionPreviewPlan = _fitCorrectionPreviewProp.boolValue && plan.CanGenerate
                ? plan
                : null;
            DrawFitCorrectionPlan(plan);

            using (new EditorGUI.DisabledScope(!plan.CanGenerate))
            {
                if (GUILayout.Button(LatticeLocalization.Tr(LocKey.CreateFitCorrectionLayer)))
                {
                    CreateFitCorrectionLayer(
                        deformer,
                        reference,
                        queryMode,
                        (FitCorrectionScope)_fitCorrectionScopeProp.enumValueIndex,
                        warningDistance,
                        targetDistance,
                        _fitCorrectionMaximumMoveProp.floatValue,
                        constraints);
                }
            }

            if (_lastFitCorrectionReport != null &&
                _lastFitCorrectionReport.Status == FitCorrectionStatus.Success)
            {
                EditorGUILayout.HelpBox(
                    string.Format(
                        LatticeLocalization.Tr(LocKey.FitCorrectionResultFormat),
                        _lastFitCorrectionReport.ImprovedVertexCount,
                        _lastFitCorrectionReport.UnresolvedVertexCount),
                    MessageType.Info);
            }
        }

        internal ClearanceHeatmapRawEvaluation GetFitCorrectionRawEvaluation(
            LatticeDeformer deformer,
            Renderer reference,
            ClearanceQueryMode queryMode,
            float updateInterval)
        {
            Renderer targetRenderer = deformer != null ? deformer.TargetRenderer : null;
            int targetId = targetRenderer != null ? targetRenderer.GetInstanceID() : 0;
            int referenceId = reference != null ? reference.GetInstanceID() : 0;
            ClearanceSignMode signMode = queryMode == ClearanceQueryMode.ClosedMesh
                ? ClearanceSignMode.ClosedMesh
                : ClearanceSignMode.ReferenceNormal;
            ClearanceQueryCache.TryGetRendererLightweightStateHash(targetRenderer, out int targetState);
            ClearanceQueryCache.TryGetRendererLightweightStateHash(reference, out int referenceState);
            int lightweightStateHash = HashCode.Combine(targetState, referenceState);
            double now = EditorApplication.timeSinceStartup;
            bool identityChanged = targetId != _lastFitCorrectionTargetId ||
                                   referenceId != _lastFitCorrectionReferenceId;
            bool signModeChanged = signMode != _lastFitCorrectionSignMode;
            bool stateChanged = lightweightStateHash != _lastFitCorrectionLightweightStateHash;
            bool intervalElapsed = now - _lastFitCorrectionEvaluationTime >=
                                   Mathf.Clamp(updateInterval, 0.02f, 2f);

            if (_clearanceRawEvaluation != null &&
                ReferenceEquals(_clearanceRawEvaluation.TargetRenderer, targetRenderer) &&
                ReferenceEquals(_clearanceRawEvaluation.ReferenceRenderer, reference) &&
                signMode == _lastClearanceSignMode &&
                lightweightStateHash == _lastClearanceLightweightStateHash)
            {
                _fitCorrectionRawEvaluation = _clearanceRawEvaluation;
                _lastFitCorrectionEvaluationTime = _lastClearanceEvaluationTime;
                _fitCorrectionRawIsThrottledStale = false;
            }
            else if (_fitCorrectionRawEvaluation == null || identityChanged || signModeChanged ||
                     (stateChanged && intervalElapsed))
            {
                _fitCorrectionRawEvaluation = ClearanceHeatmapEvaluator.Evaluate(
                    targetRenderer,
                    reference,
                    signMode);
                _lastFitCorrectionEvaluationTime = now;
                _fitCorrectionRawIsThrottledStale = false;
            }
            else
            {
                _fitCorrectionRawIsThrottledStale = stateChanged;
            }

            if (!_fitCorrectionRawIsThrottledStale)
            {
                _lastFitCorrectionTargetId = targetId;
                _lastFitCorrectionReferenceId = referenceId;
                _lastFitCorrectionSignMode = signMode;
                _lastFitCorrectionLightweightStateHash = lightweightStateHash;
            }
            return _fitCorrectionRawEvaluation;
        }

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
        {
            int planKey = ComputeFitCorrectionPlanKey(
                deformer,
                rawEvaluation,
                reference,
                queryMode,
                scope,
                warningDistance,
                targetDistance,
                maximumMove,
                constraints);
            if (!_hasCachedFitCorrectionPlan ||
                planKey != _cachedFitCorrectionPlanKey ||
                _cachedFitCorrectionPlan == null)
            {
                _cachedFitCorrectionPlan = FitCorrectionGenerator.Analyze(
                    deformer,
                    rawEvaluation,
                    reference,
                    queryMode,
                    scope,
                    warningDistance,
                    targetDistance,
                    maximumMove,
                    constraints);
                _cachedFitCorrectionPlanKey = planKey;
                _hasCachedFitCorrectionPlan = true;
            }
            return _cachedFitCorrectionPlan;
        }

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
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (deformer != null ? deformer.GetInstanceID() : 0);
                hash = hash * 31 + (deformer != null ? EditorUtility.GetDirtyCount(deformer) : 0);
                Renderer currentTarget = deformer != null ? deformer.TargetRenderer : null;
                ClearanceQueryCache.TryGetRendererLightweightStateHash(
                    currentTarget,
                    out int currentTargetState);
                ClearanceQueryCache.TryGetRendererLightweightStateHash(
                    reference,
                    out int currentReferenceState);
                hash = hash * 31 + currentTargetState;
                hash = hash * 31 + currentReferenceState;
                Mesh source = deformer != null ? deformer.SourceMesh : null;
                hash = hash * 31 + (source != null ? source.GetInstanceID() : 0);
                hash = hash * 31 + (source != null ? EditorUtility.GetDirtyCount(source) : 0);
                hash = hash * 31 + (rawEvaluation != null ? rawEvaluation.TargetStateHash : 0);
                hash = hash * 31 + (rawEvaluation != null ? rawEvaluation.ReferenceStateHash : 0);
                hash = hash * 31 + (reference != null ? reference.GetInstanceID() : 0);
                hash = hash * 31 + (int)queryMode;
                hash = hash * 31 + (int)scope;
                hash = hash * 31 + warningDistance.GetHashCode();
                hash = hash * 31 + targetDistance.GetHashCode();
                hash = hash * 31 + maximumMove.GetHashCode();
                hash = hash * 31 + constraints.UseVertexMask.GetHashCode();
                hash = hash * 31 + constraints.PinOpenBoundaries.GetHashCode();
                hash = hash * 31 + constraints.IsolateConnectedComponents.GetHashCode();
                hash = hash * 31 + constraints.SmoothSurface.GetHashCode();
                hash = hash * 31 + constraints.SmoothingIterations;
                hash = hash * 31 + constraints.SmoothingStrength.GetHashCode();
                hash = hash * 31 + constraints.PreserveSolvedClearance.GetHashCode();
                hash = hash * 31 + constraints.UseSymmetry.GetHashCode();
                hash = hash * 31 + constraints.SymmetryAxis;
                hash = hash * 31 + constraints.SymmetryTolerance.GetHashCode();
                if (constraints.UseVertexMask && deformer != null)
                {
                    IReadOnlyList<LatticeLayer> layers = deformer.Layers;
                    int activeLayerIndex = deformer.ActiveLayerIndex;
                    LatticeLayer activeLayer = activeLayerIndex >= 0 &&
                                               activeLayerIndex < layers.Count
                        ? layers[activeLayerIndex]
                        : null;
                    hash = hash * 31 + (activeLayer != null ? activeLayer.GetHashCode() : 0);
                    float[] mask = activeLayer?.VertexMask;
                    int maskLength = mask?.Length ?? 0;
                    hash = hash * 31 + maskLength;
                    for (int i = 0; i < maskLength; i++)
                        hash = hash * 31 + mask[i].GetHashCode();
                }
                return hash;
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

        private void CreateFitCorrectionLayer(
            LatticeDeformer deformer,
            Renderer reference,
            ClearanceQueryMode queryMode,
            FitCorrectionScope scope,
            float warningDistance,
            float targetDistance,
            float maximumMove,
            FitCorrectionConstraintOptions constraints)
        {
            Renderer targetRenderer = deformer.TargetRenderer;
            var freshEvaluation = ClearanceHeatmapEvaluator.Evaluate(
                targetRenderer,
                reference,
                queryMode == ClearanceQueryMode.ClosedMesh
                    ? ClearanceSignMode.ClosedMesh
                    : ClearanceSignMode.ReferenceNormal);
            var freshPlan = FitCorrectionGenerator.Analyze(
                deformer,
                freshEvaluation,
                reference,
                queryMode,
                scope,
                warningDistance,
                targetDistance,
                maximumMove,
                constraints);
            if (!freshPlan.CanGenerate)
            {
                _lastFitCorrectionReport = new FitCorrectionReport(freshPlan.Status);
                return;
            }

            Undo.RegisterCompleteObjectUndo(
                deformer,
                LatticeLocalization.Tr(LocKey.CreateFitCorrectionLayer));
            _lastFitCorrectionReport = FitCorrectionGenerator.Generate(
                deformer,
                freshPlan,
                reference,
                queryMode,
                scope,
                warningDistance,
                targetDistance,
                maximumMove);
            if (_lastFitCorrectionReport.Status != FitCorrectionStatus.Success) return;

            deformer.InvalidateCache();
            deformer.Deform(LatticePreviewUtility.ShouldAssignRuntimeMesh());
            EditorUtility.SetDirty(deformer);
            LatticePrefabUtility.MarkModified(deformer);
            serializedObject.Update();
            ResolveActiveGroupProperties();
            InitializePendingGridSizes();
            RebuildLayerList();
            InvalidateClearanceEvaluation();
            LatticePreviewUtility.RequestSceneRepaint();
            SceneView.RepaintAll();
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
                    Renderer evaluatedRenderer = ResolveClearanceTargetRenderer(
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
                       _clearanceScanResult == null ||
                       _clearanceScanResult.Conditions.Count == 0))
            {
                if (GUILayout.Button(LatticeLocalization.Tr(LocKey.ClearanceReportExportScan)))
                {
                    ExportClearanceReport(ClearanceQaReportBuilder.FromScanResult(
                        deformer,
                        reference,
                        _clearanceScanResult));
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

        private void DrawClearanceHeatmapInScene(SceneView sceneView)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (targets == null || targets.Length != 1 || target is not LatticeDeformer deformer) return;
            if (!deformer.ShowClearanceHeatmap) return;

            var evaluation = GetClearanceEvaluation(
                deformer,
                deformer.ClearanceReferenceRenderer,
                deformer.ClearanceQueryMode,
                deformer.ClearanceWarningDistance,
                deformer.ClearanceTargetDistance,
                deformer.ClearanceUpdateInterval);
            if (evaluation == null || evaluation.Status != ClearanceEvaluationStatus.Valid) return;

            int stride = CalculateAdaptiveHeatmapStride(
                evaluation.WorldPositions.Length,
                deformer.ClearanceDisplayStride,
                HeatmapDrawPointBudget);
            using (s_heatmapDrawMarker.Auto())
            {
                for (int i = 0; i < evaluation.WorldPositions.Length; i += stride)
                {
                    ClearanceClassification classification = evaluation.Classifications[i];
                    if (!ClearanceHeatmapEvaluator.ShouldDisplay(
                            classification,
                            deformer.ClearanceHeatmapDisplayMode))
                    {
                        continue;
                    }

                    Vector3 position = evaluation.WorldPositions[i];
                    Handles.color = ClearanceHeatmapEvaluator.ColorFor(classification);
                    float size = HandleUtility.GetHandleSize(position) * 0.012f;
                    Handles.DotHandleCap(0, position, Quaternion.identity, size, EventType.Repaint);
                }
            }

            DrawFitCorrectionPreview();
        }

        internal static int CalculateAdaptiveHeatmapStride(
            int vertexCount,
            int requestedStride,
            int pointBudget = HeatmapDrawPointBudget)
        {
            requestedStride = Mathf.Max(1, requestedStride);
            pointBudget = Mathf.Max(1, pointBudget);
            int budgetStride = vertexCount > pointBudget
                ? Mathf.CeilToInt(vertexCount / (float)pointBudget)
                : 1;
            return Mathf.Max(requestedStride, budgetStride);
        }

        private void DrawFitCorrectionPreview()
        {
            FitCorrectionPlan plan = _fitCorrectionPreviewPlan;
            if (plan == null || !plan.CanGenerate || plan.BeforeEvaluation == null) return;
            int count = Mathf.Min(
                plan.BeforeEvaluation.WorldPositions.Length,
                plan.CorrectedWorldPositions.Length);
            Handles.color = new Color(0.1f, 0.9f, 1f, 0.9f);
            for (int vertex = 0; vertex < count; vertex++)
            {
                Vector3 from = plan.BeforeEvaluation.WorldPositions[vertex];
                Vector3 to = plan.CorrectedWorldPositions[vertex];
                if ((to - from).sqrMagnitude <= 1e-16f) continue;
                Handles.DrawLine(from, to, 2f);
                float size = HandleUtility.GetHandleSize(to) * 0.01f;
                Handles.DotHandleCap(0, to, Quaternion.identity, size, EventType.Repaint);
            }
        }

        internal ClearanceHeatmapEvaluation GetClearanceEvaluation(
            LatticeDeformer deformer,
            Renderer reference,
            ClearanceQueryMode queryMode,
            float warningDistance,
            float targetDistance,
            float updateInterval)
        {
            Renderer targetRenderer = ResolveClearanceTargetRenderer(deformer, out bool usedPreviewProxy);
            int targetId = targetRenderer != null ? targetRenderer.GetInstanceID() : 0;
            int referenceId = reference != null ? reference.GetInstanceID() : 0;
            double now = EditorApplication.timeSinceStartup;
            bool identityChanged = targetId != _lastClearanceTargetId ||
                                   referenceId != _lastClearanceReferenceId ||
                                   usedPreviewProxy != _lastClearanceUsedPreviewProxy;
            bool hasTargetState = ClearanceQueryCache.TryGetRendererLightweightStateHash(
                targetRenderer,
                out int targetState);
            bool hasReferenceState = ClearanceQueryCache.TryGetRendererLightweightStateHash(
                reference,
                out int referenceState);
            int lightweightStateHash = HashCode.Combine(targetState, referenceState);
            ClearanceSignMode signMode = queryMode == ClearanceQueryMode.ClosedMesh
                ? ClearanceSignMode.ClosedMesh
                : ClearanceSignMode.ReferenceNormal;
            bool stateChanged = hasTargetState != _lastClearanceHadTargetState ||
                                hasReferenceState != _lastClearanceHadReferenceState ||
                                ((hasTargetState || hasReferenceState) &&
                                 lightweightStateHash != _lastClearanceLightweightStateHash);
            bool signModeChanged = signMode != _lastClearanceSignMode;
            bool intervalElapsed = now - _lastClearanceEvaluationTime >=
                                   Mathf.Clamp(updateInterval, 0.02f, 2f);
            bool fallbackExpired = (!hasTargetState || !hasReferenceState) &&
                                   intervalElapsed;
            if (_clearanceRawEvaluation == null || identityChanged || signModeChanged ||
                (stateChanged && intervalElapsed) || fallbackExpired)
            {
                _clearanceRawEvaluation = ClearanceHeatmapEvaluator.Evaluate(
                    targetRenderer,
                    reference,
                    signMode);
                _lastClearanceEvaluationTime = now;
                _lastClearanceTargetId = targetId;
                _lastClearanceReferenceId = referenceId;
                _lastClearanceUsedPreviewProxy = usedPreviewProxy;
                _lastClearanceLightweightStateHash = lightweightStateHash;
                _lastClearanceSignMode = signMode;
                _lastClearanceHadTargetState = hasTargetState;
                _lastClearanceHadReferenceState = hasReferenceState;
                _hasCachedFitCorrectionPlan = false;
            }

            if (_cachedClearanceEvaluation == null ||
                !ReferenceEquals(_classifiedClearanceRawEvaluation, _clearanceRawEvaluation) ||
                _classifiedWarningDistance != warningDistance ||
                _classifiedTargetDistance != targetDistance)
            {
                _cachedClearanceEvaluation = ClearanceHeatmapEvaluator.Classify(
                    _clearanceRawEvaluation,
                    warningDistance,
                    targetDistance);
                _classifiedClearanceRawEvaluation = _clearanceRawEvaluation;
                _classifiedWarningDistance = warningDistance;
                _classifiedTargetDistance = targetDistance;
            }
            return _cachedClearanceEvaluation;
        }

        private static Renderer ResolveClearanceTargetRenderer(
            LatticeDeformer deformer,
            out bool usedPreviewProxy)
        {
            Renderer original = deformer != null ? deformer.TargetRenderer : null;
            Renderer previewProxy = null;
            if (original != null &&
                NDMFPreviewProxyUtility.TryGetProxyRenderer(original, out Renderer proxy) &&
                proxy != null)
            {
                previewProxy = proxy;
            }

            return ResolveClearanceTargetRenderer(deformer, previewProxy, out usedPreviewProxy);
        }

        internal static Renderer ResolveClearanceTargetRenderer(
            LatticeDeformer deformer,
            Renderer previewProxy,
            out bool usedPreviewProxy)
        {
            Renderer original = deformer != null ? deformer.TargetRenderer : null;
            usedPreviewProxy = original != null && previewProxy != null;
            return usedPreviewProxy ? previewProxy : original;
        }

        private void OnClearanceStateChanged()
        {
            InvalidateClearanceEvaluation();
            Repaint();
            SceneView.RepaintAll();
        }

        private void InvalidateClearanceEvaluation()
        {
            _clearanceRawEvaluation = null;
            _lastClearanceEvaluationTime = double.NegativeInfinity;
            _lastClearanceTargetId = 0;
            _lastClearanceReferenceId = 0;
            _lastClearanceUsedPreviewProxy = false;
            _lastClearanceLightweightStateHash = 0;
            _lastClearanceSignMode = default;
            _lastClearanceHadTargetState = false;
            _lastClearanceHadReferenceState = false;
            _cachedClearanceEvaluation = null;
            _classifiedClearanceRawEvaluation = null;
            _fitCorrectionRawEvaluation = null;
            _lastFitCorrectionLightweightStateHash = 0;
            _lastFitCorrectionTargetId = 0;
            _lastFitCorrectionReferenceId = 0;
            _lastFitCorrectionSignMode = default;
            _lastFitCorrectionEvaluationTime = double.NegativeInfinity;
            _fitCorrectionRawIsThrottledStale = false;
            _fitCorrectionPreviewPlan = null;
            _cachedFitCorrectionPlan = null;
            _hasCachedFitCorrectionPlan = false;
            _cachedValidationDiagnostics = null;
            _hasCachedValidationState = false;
        }

    }
}
#endif
