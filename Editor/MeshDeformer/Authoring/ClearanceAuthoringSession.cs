#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>Owns evaluation caches, an incremental scan and its temporary Scene pose.
    /// UI and drawing borrow results and do not own the captured Scene state.</summary>
    internal sealed class ClearanceAuthoringSession : IDisposable
    {
        private ClearanceHeatmapRawEvaluation _clearanceRawEvaluation;
        private double _lastClearanceEvaluationTime = double.NegativeInfinity;
        private int _lastClearanceTargetId;
        private int _lastClearanceReferenceId;
        private bool _lastClearanceUsedPreviewProxy;
        private int _lastClearanceLightweightStateHash;
        private ClearanceSignMode _lastClearanceSignMode;
        private bool _lastClearanceHadTargetState;
        private bool _lastClearanceHadReferenceState;
        private ClearanceHeatmapEvaluation _cachedClearanceEvaluation;
        private ClearanceHeatmapRawEvaluation _classifiedClearanceRawEvaluation;
        private float _classifiedWarningDistance;
        private float _classifiedTargetDistance;
        private ClearanceScanOperation _clearanceScanOperation;
        private ClearanceScanResult _clearanceScanResult;
        private ClearanceScanPreviewState _clearanceScanPreviewState;
        private ClearanceHeatmapRawEvaluation _fitCorrectionRawEvaluation;
        private int _lastFitCorrectionLightweightStateHash;
        private int _lastFitCorrectionTargetId;
        private int _lastFitCorrectionReferenceId;
        private ClearanceSignMode _lastFitCorrectionSignMode;
        private double _lastFitCorrectionEvaluationTime = double.NegativeInfinity;
        private bool _fitCorrectionRawIsThrottledStale;
        private readonly FitCorrectionPlan _throttledStaleFitCorrectionPlan =
            new FitCorrectionPlan(FitCorrectionStatus.StaleEvaluation);
        private FitCorrectionReport _lastFitCorrectionReport;
        private FitCorrectionPlan _cachedFitCorrectionPlan;
        private int _cachedFitCorrectionPlanKey;
        private bool _hasCachedFitCorrectionPlan;

        private bool _disposed;
        internal event Action Changed;
        internal ClearanceScanOperation ScanOperation => _clearanceScanOperation;
        internal ClearanceScanResult ScanResult => _clearanceScanResult;
        internal bool HasScanPreview => _clearanceScanPreviewState != null;
        internal bool UsedPreviewProxy => _lastClearanceUsedPreviewProxy;
        internal double EvaluationTime => _lastClearanceEvaluationTime;
        internal bool FitCorrectionRawIsThrottledStale => _fitCorrectionRawIsThrottledStale;
        internal FitCorrectionPlan ThrottledStaleFitCorrectionPlan => _throttledStaleFitCorrectionPlan;
        internal FitCorrectionReport LastFitCorrectionReport => _lastFitCorrectionReport;
        internal FitCorrectionPlan PreviewPlan { get; private set; }

        internal ClearanceAuthoringSession()
        {
            Undo.undoRedoPerformed += NotifyChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
        }

        internal void SetFitCorrectionPreview(FitCorrectionPlan plan) => PreviewPlan = _disposed ? null : plan;

        internal void StartScan(LatticeDeformer deformer, Renderer reference,
            ClearanceQueryMode queryMode, float warningDistance, float targetDistance)
        {
            if (_disposed || deformer == null) return;
            CancelScan();
            RestoreScanPreview();
            _clearanceScanOperation = new ClearanceScanOperation(
                deformer.ClearanceScanSet, deformer, reference,
                deformer.ClearanceScanAvatarRoot, queryMode, warningDistance, targetDistance);
            EditorApplication.update += AdvanceClearanceScan;
        }

        internal void CancelScan()
        {
            _clearanceScanOperation?.Cancel();
            FinishClearanceScan();
        }

        internal bool ApplyScanCondition(int conditionIndex, LatticeDeformer deformer,
            Renderer reference, ClearanceQueryMode queryMode, float warningDistance, float targetDistance)
        {
            if (_disposed || _clearanceScanResult == null || deformer == null) return false;
            RestoreScanPreview();
            bool applied = ClearanceScanPreviewState.TryApply(
                _clearanceScanResult.ScanSet, conditionIndex, deformer, reference,
                deformer.ClearanceScanAvatarRoot, queryMode, warningDistance, targetDistance,
                out _clearanceScanPreviewState, out _);
            NotifyChanged();
            return applied;
        }

        internal void RestoreScanPreview()
        {
            _clearanceScanPreviewState?.Dispose();
            _clearanceScanPreviewState = null;
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            if (_disposed) return;
            Invalidate();
            Changed?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Undo.undoRedoPerformed -= NotifyChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            EditorApplication.update -= AdvanceClearanceScan;
            try { _clearanceScanOperation?.Dispose(); }
            finally
            {
                _clearanceScanOperation = null;
                try { _clearanceScanPreviewState?.Dispose(); }
                finally
                {
                    _clearanceScanPreviewState = null;
                    _clearanceScanResult = null;
                    _lastFitCorrectionReport = null;
                    Invalidate();
                    Changed = null;
                }
            }
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
            Changed?.Invoke();
        }

        private void FinishClearanceScan()
        {
            EditorApplication.update -= AdvanceClearanceScan;
            if (_clearanceScanOperation == null) return;
            _clearanceScanResult = _clearanceScanOperation.Result;
            _clearanceScanOperation.Dispose();
            _clearanceScanOperation = null;
            Invalidate();
            Changed?.Invoke();
        }

        internal ClearanceHeatmapRawEvaluation GetFitCorrectionRawEvaluation(
            LatticeDeformer deformer,
            Renderer reference,
            ClearanceQueryMode queryMode,
            float updateInterval)
        {
            if (_disposed) return null;
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
            if (_disposed) return null;
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
                    var data = deformer.ReadResolvedData();
                    var group = data.Groups != null && data.ActiveGroupIndex >= 0 && data.ActiveGroupIndex < data.Groups.Count
                        ? data.Groups[data.ActiveGroupIndex] : null;
                    IReadOnlyList<LatticeLayer> layers = group?.SerializedLayers;
                    int activeLayerIndex = group?.SerializedActiveLayerIndex ?? -1;
                    LatticeLayer activeLayer = activeLayerIndex >= 0 &&
                                               layers != null && activeLayerIndex < layers.Count
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

        internal ClearanceHeatmapEvaluation GetClearanceEvaluation(
            LatticeDeformer deformer,
            Renderer reference,
            ClearanceQueryMode queryMode,
            float warningDistance,
            float targetDistance,
            float updateInterval)
        {
            if (_disposed) return null;
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

        internal static Renderer ResolveClearanceTargetRenderer(
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

        internal void Invalidate()
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
            PreviewPlan = null;
            _cachedFitCorrectionPlan = null;
            _hasCachedFitCorrectionPlan = false;
        }

        internal bool CreateFitCorrectionLayer(LatticeDeformer deformer, Renderer reference,
            ClearanceQueryMode queryMode, FitCorrectionScope scope, float warningDistance,
            float targetDistance, float maximumMove, FitCorrectionConstraintOptions constraints, string undoLabel)
        {
            if (_disposed || deformer == null || !deformer.HasValidSerializedAuthoringData ||
                SerializedDeformerReader.Read(deformer).UsesProfile)
            {
                _lastFitCorrectionReport = new FitCorrectionReport(FitCorrectionStatus.InvalidTarget);
                return false;
            }
            var freshEvaluation = ClearanceHeatmapEvaluator.Evaluate(
                deformer.TargetRenderer, reference, queryMode == ClearanceQueryMode.ClosedMesh
                    ? ClearanceSignMode.ClosedMesh : ClearanceSignMode.ReferenceNormal);
            var plan = FitCorrectionGenerator.Analyze(deformer, freshEvaluation, reference, queryMode,
                scope, warningDistance, targetDistance, maximumMove, constraints);
            if (!plan.CanGenerate)
            {
                _lastFitCorrectionReport = new FitCorrectionReport(plan.Status);
                return false;
            }
            bool committed = DeformerEditService.Execute(deformer, undoLabel, target =>
            {
                _lastFitCorrectionReport = FitCorrectionGenerator.Generate(target, plan, reference,
                    queryMode, scope, warningDistance, targetDistance, maximumMove);
                return _lastFitCorrectionReport.Status == FitCorrectionStatus.Success;
            });
            if (committed) NotifyChanged();
            return committed;
        }
    }
}
#endif
