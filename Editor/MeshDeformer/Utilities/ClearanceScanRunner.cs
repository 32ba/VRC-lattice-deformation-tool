#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal enum ClearanceScanConditionStatus
    {
        Success = 0,
        InvalidCondition = 1,
        InvalidAnimationClip = 2,
        MissingAvatarRoot = 3,
        MissingTransform = 4,
        InvalidRenderer = 5,
        MissingBlendShape = 6,
        EvaluationFailed = 7,
        Exception = 8
    }

    internal sealed class ClearanceScanConditionResult
    {
        internal readonly int ConditionIndex;
        internal readonly string ConditionName;
        internal readonly ClearanceScanConditionStatus Status;
        internal readonly string ErrorMessage;
        internal readonly float WarningDistance;
        internal readonly float TargetDistance;
        internal readonly ClearanceHeatmapStatistics Statistics;
        internal readonly float[] VertexClearances;
        internal readonly bool UsedNdmfPreviewProxy;
        internal readonly string EvaluatedRendererName;
        internal readonly ClearanceQaCondition ConditionSnapshot;

        internal bool IsSuccess => Status == ClearanceScanConditionStatus.Success;

        internal ClearanceScanConditionResult(
            int conditionIndex,
            string conditionName,
            ClearanceScanConditionStatus status,
            string errorMessage = "",
            float warningDistance = 0f,
            float targetDistance = 0f,
            ClearanceHeatmapStatistics statistics = default,
            float[] vertexClearances = null,
            bool usedNdmfPreviewProxy = false,
            string evaluatedRendererName = "",
            ClearanceScanCondition conditionDefinition = null)
        {
            ConditionIndex = conditionIndex;
            ConditionName = conditionName ?? "";
            Status = status;
            ErrorMessage = errorMessage ?? "";
            WarningDistance = warningDistance;
            TargetDistance = targetDistance;
            Statistics = statistics;
            VertexClearances = vertexClearances ?? Array.Empty<float>();
            UsedNdmfPreviewProxy = usedNdmfPreviewProxy;
            EvaluatedRendererName = evaluatedRendererName ?? "";
            ConditionSnapshot = ClearanceQaReportBuilder.SnapshotConditionDefinition(
                conditionDefinition);
        }
    }

    internal sealed class ClearanceScanResult
    {
        internal readonly List<ClearanceScanConditionResult> Conditions =
            new List<ClearanceScanConditionResult>();
        internal float[] WorstClearances = Array.Empty<float>();
        internal int[] WorstConditionIndices = Array.Empty<int>();
        internal bool WasCancelled;
        internal ClearanceScanSet ScanSet;
        internal string TargetRendererName = "";
        internal string ReferenceRendererName = "";
        internal ClearanceQueryMode QueryMode;
        internal ClearanceQaReport ReportHeaderSnapshot;

        internal int SuccessfulConditionCount
        {
            get
            {
                int count = 0;
                for (int index = 0; index < Conditions.Count; index++)
                    if (Conditions[index].IsSuccess) count++;
                return count;
            }
        }

        internal int WorstConditionIndex
        {
            get
            {
                float worst = float.PositiveInfinity;
                int condition = -1;
                for (int vertex = 0; vertex < WorstClearances.Length; vertex++)
                {
                    if (WorstClearances[vertex] >= worst) continue;
                    worst = WorstClearances[vertex];
                    condition = WorstConditionIndices[vertex];
                }
                return condition;
            }
        }
    }

    internal sealed class ClearanceScanOperation : IDisposable
    {
        private readonly ClearanceScanSet _scanSet;
        private readonly LatticeDeformer _deformer;
        private readonly Renderer _referenceRenderer;
        private readonly Transform _avatarRoot;
        private readonly ClearanceQueryMode _queryMode;
        private readonly float _defaultWarningDistance;
        private readonly float _defaultTargetDistance;
        private readonly SceneStateSnapshot _snapshot;
        private readonly bool _restoreOnComplete;
        private readonly Func<Renderer, Renderer> _previewProxyResolver;
        private readonly Action<int> _afterConditionApplied;
        private readonly Mesh _expectedEvaluationMesh;
        private readonly string _expectedTopologyHash;
        private bool _disposed;
        private bool _undoRedoObserved;
        private bool _preserveExternalEdits;
        private int _baselineUndoGroup;

        internal ClearanceScanResult Result { get; }
        internal int NextConditionIndex { get; private set; }
        internal bool IsCompleted { get; private set; }
        internal float Progress => _scanSet == null || _scanSet.Conditions.Count == 0
            ? 1f
            : Mathf.Clamp01((float)NextConditionIndex / _scanSet.Conditions.Count);
        internal string CurrentConditionName =>
            _scanSet != null && NextConditionIndex < _scanSet.Conditions.Count
                ? _scanSet.Conditions[NextConditionIndex]?.Name ?? ""
                : "";

        internal ClearanceScanOperation(
            ClearanceScanSet scanSet,
            LatticeDeformer deformer,
            Renderer referenceRenderer,
            Transform avatarRoot,
            ClearanceQueryMode queryMode,
            float defaultWarningDistance,
            float defaultTargetDistance,
            bool restoreOnComplete = true,
            Func<Renderer, Renderer> previewProxyResolver = null,
            Action<int> afterConditionApplied = null)
        {
            _scanSet = scanSet;
            _deformer = deformer;
            _referenceRenderer = referenceRenderer;
            _avatarRoot = avatarRoot != null
                ? avatarRoot
                : deformer != null && deformer.TargetRenderer != null
                    ? deformer.TargetRenderer.transform.root
                    : null;
            _queryMode = queryMode;
            _defaultWarningDistance = Mathf.Max(0f, defaultWarningDistance);
            _defaultTargetDistance = Mathf.Max(_defaultWarningDistance, defaultTargetDistance);
            _restoreOnComplete = restoreOnComplete;
            _previewProxyResolver = previewProxyResolver;
            _afterConditionApplied = afterConditionApplied;
            Renderer originalTarget = deformer != null ? deformer.TargetRenderer : null;
            Renderer initialPreviewProxy = null;
            if (originalTarget != null)
            {
                initialPreviewProxy = previewProxyResolver != null
                    ? previewProxyResolver(originalTarget)
                    : ResolveEvaluationTarget(originalTarget, out bool usedInitialProxy);
                if (ReferenceEquals(initialPreviewProxy, originalTarget)) initialPreviewProxy = null;
            }
            Renderer initialEvaluationTarget = initialPreviewProxy != null
                ? initialPreviewProxy
                : originalTarget;
            _expectedEvaluationMesh = GetRendererMesh(initialEvaluationTarget);
            _expectedTopologyHash = ClearanceQaReportBuilder.ComputeTopology(
                _expectedEvaluationMesh).topologyHash;
            _snapshot = SceneStateSnapshot.Capture(
                _avatarRoot,
                originalTarget,
                referenceRenderer,
                scanSet,
                initialPreviewProxy);
            Result = new ClearanceScanResult
            {
                ScanSet = scanSet,
                TargetRendererName = deformer != null && deformer.TargetRenderer != null
                    ? GetHierarchyName(deformer.TargetRenderer.transform)
                    : "",
                ReferenceRendererName = referenceRenderer != null
                    ? GetHierarchyName(referenceRenderer.transform)
                    : "",
                QueryMode = queryMode,
                ReportHeaderSnapshot = ClearanceQaReportBuilder.CreateHeaderSnapshot(
                    originalTarget,
                    referenceRenderer,
                    queryMode,
                    DateTime.UtcNow)
            };
            _baselineUndoGroup = Undo.GetCurrentGroup();
            if (_restoreOnComplete) Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        internal void Step()
        {
            if (IsCompleted || _disposed) return;
            if (_scanSet == null || _deformer == null || _referenceRenderer == null)
            {
                Complete();
                return;
            }
            if (NextConditionIndex >= _scanSet.Conditions.Count)
            {
                Complete();
                return;
            }

            if (_restoreOnComplete && NextConditionIndex > 0 &&
                (_undoRedoObserved || Undo.GetCurrentGroup() != _baselineUndoGroup))
            {
                Result.WasCancelled = true;
                _preserveExternalEdits = true;
                IsCompleted = true;
                return;
            }
            if (NextConditionIndex > 0) _snapshot.Restore();
            int conditionIndex = NextConditionIndex++;
            ClearanceScanCondition condition = _scanSet.Conditions[conditionIndex];
            ClearanceScanConditionResult conditionResult;
            try
            {
                conditionResult = EvaluateCondition(conditionIndex, condition);
            }
            catch (Exception exception)
            {
                conditionResult = Error(
                    conditionIndex,
                    condition,
                    ClearanceScanConditionStatus.Exception,
                    exception.GetType().Name + ": " + exception.Message);
            }
            if (conditionResult.IsSuccess && Result.WorstClearances.Length > 0 &&
                conditionResult.VertexClearances.Length != Result.WorstClearances.Length)
            {
                conditionResult = Error(
                    conditionIndex,
                    condition,
                    ClearanceScanConditionStatus.EvaluationFailed,
                    "Target topology changed between conditions.");
            }
            Result.Conditions.Add(conditionResult);
            if (conditionResult.IsSuccess) AccumulateWorst(conditionResult);
            if (_restoreOnComplete && NextConditionIndex < _scanSet.Conditions.Count)
                _snapshot.Restore();
            _baselineUndoGroup = Undo.GetCurrentGroup();
            if (NextConditionIndex >= _scanSet.Conditions.Count) Complete();
        }

        internal void Cancel()
        {
            if (IsCompleted) return;
            Result.WasCancelled = true;
            Complete();
        }

        internal ClearanceScanResult RunToCompletion(Func<int, bool> cancelBeforeCondition = null)
        {
            while (!IsCompleted)
            {
                if (cancelBeforeCondition != null && cancelBeforeCondition(NextConditionIndex))
                {
                    Cancel();
                    break;
                }
                Step();
            }
            return Result;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_restoreOnComplete) Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            if (!_preserveExternalEdits) _snapshot.Restore();
        }

        private ClearanceScanConditionResult EvaluateCondition(
            int conditionIndex,
            ClearanceScanCondition condition)
        {
            if (condition == null)
                return Error(conditionIndex, null, ClearanceScanConditionStatus.InvalidCondition, "Condition is null.");

            if (condition.UseAnimationClip)
            {
                if (condition.AnimationClip == null || condition.SampleTime > condition.AnimationClip.length + 1e-6f)
                {
                    return Error(conditionIndex, condition,
                        ClearanceScanConditionStatus.InvalidAnimationClip,
                        "AnimationClip or sample time is invalid.");
                }
                if (_avatarRoot == null)
                    return Error(conditionIndex, condition,
                        ClearanceScanConditionStatus.MissingAvatarRoot,
                        "Avatar root is required for animation sampling.");
                Transform animationRoot = string.IsNullOrEmpty(condition.AnimationRootPath)
                    ? _avatarRoot
                    : _avatarRoot.Find(condition.AnimationRootPath);
                if (animationRoot == null)
                    return Error(conditionIndex, condition,
                        ClearanceScanConditionStatus.MissingTransform,
                        "Animation root was not found: " + condition.AnimationRootPath);
                condition.AnimationClip.SampleAnimation(animationRoot.gameObject, condition.SampleTime);
            }

            for (int index = 0; index < condition.TransformOverrides.Count; index++)
            {
                ClearanceTransformPoseOverride pose = condition.TransformOverrides[index];
                if (pose == null) continue;
                if (_avatarRoot == null)
                    return Error(conditionIndex, condition,
                        ClearanceScanConditionStatus.MissingAvatarRoot,
                        "Avatar root is required for transform overrides.");
                Transform target = string.IsNullOrEmpty(pose.RelativePath)
                    ? _avatarRoot
                    : _avatarRoot.Find(pose.RelativePath);
                if (target == null)
                    return Error(conditionIndex, condition,
                        ClearanceScanConditionStatus.MissingTransform,
                        "Transform was not found: " + pose.RelativePath);
                if (pose.OverridePosition) target.localPosition = pose.LocalPosition;
                if (pose.OverrideRotation) target.localEulerAngles = pose.LocalEulerAngles;
                if (pose.OverrideScale) target.localScale = pose.LocalScale;
            }

            Renderer originalTarget = _deformer.TargetRenderer;
            Renderer evaluationTarget;
            bool usedPreviewProxy;
            if (_previewProxyResolver != null)
            {
                evaluationTarget = _previewProxyResolver(originalTarget);
                usedPreviewProxy = evaluationTarget != null && evaluationTarget != originalTarget;
                evaluationTarget ??= originalTarget;
            }
            else
            {
                evaluationTarget = ResolveEvaluationTarget(originalTarget, out usedPreviewProxy);
            }
            if (evaluationTarget != originalTarget &&
                !TrySynchronizePose(originalTarget, evaluationTarget, out string poseError))
            {
                return Error(
                    conditionIndex,
                    condition,
                    ClearanceScanConditionStatus.InvalidRenderer,
                    "Preview proxy: " + poseError);
            }
            if (evaluationTarget != originalTarget &&
                !TrySynchronizeBlendShapeWeights(originalTarget, evaluationTarget, out string syncError))
            {
                return Error(
                    conditionIndex,
                    condition,
                    ClearanceScanConditionStatus.MissingBlendShape,
                    syncError);
            }
            for (int index = 0; index < condition.BlendShapeOverrides.Count; index++)
            {
                ClearanceBlendShapeOverride blendShape = condition.BlendShapeOverrides[index];
                if (blendShape == null) continue;
                Renderer renderer = blendShape.RendererRole == ClearanceScanRendererRole.Target
                    ? originalTarget
                    : _referenceRenderer;
                if (!TrySetBlendShape(renderer, blendShape, out string error))
                    return Error(conditionIndex, condition,
                        error == "Renderer is not a SkinnedMeshRenderer."
                            ? ClearanceScanConditionStatus.InvalidRenderer
                            : ClearanceScanConditionStatus.MissingBlendShape,
                        error);
                if (blendShape.RendererRole == ClearanceScanRendererRole.Target &&
                    evaluationTarget != originalTarget)
                {
                    if (!TrySetBlendShape(evaluationTarget, blendShape, out string proxyError))
                    {
                        return Error(
                            conditionIndex,
                            condition,
                            proxyError == "Renderer is not a SkinnedMeshRenderer."
                                ? ClearanceScanConditionStatus.InvalidRenderer
                                : ClearanceScanConditionStatus.MissingBlendShape,
                            "Preview proxy: " + proxyError);
                    }
                }
            }

            _afterConditionApplied?.Invoke(conditionIndex);

            Mesh currentEvaluationMesh = GetRendererMesh(evaluationTarget);
            string currentTopologyHash = ClearanceQaReportBuilder.ComputeTopology(
                currentEvaluationMesh).topologyHash;
            if (string.IsNullOrEmpty(_expectedTopologyHash) ||
                (!usedPreviewProxy && !ReferenceEquals(currentEvaluationMesh, _expectedEvaluationMesh)) ||
                !string.Equals(currentTopologyHash, _expectedTopologyHash, StringComparison.Ordinal))
            {
                return Error(
                    conditionIndex,
                    condition,
                    ClearanceScanConditionStatus.EvaluationFailed,
                    "Target topology or vertex identity changed during the scan.");
            }

            float warningDistance = condition.OverrideThresholds
                ? condition.WarningDistance
                : _defaultWarningDistance;
            float targetDistance = condition.OverrideThresholds
                ? condition.TargetDistance
                : _defaultTargetDistance;
            ClearanceHeatmapRawEvaluation raw = ClearanceHeatmapEvaluator.Evaluate(
                evaluationTarget,
                _referenceRenderer,
                _queryMode == ClearanceQueryMode.ClosedMesh
                    ? ClearanceSignMode.ClosedMesh
                    : ClearanceSignMode.ReferenceNormal);
            ClearanceHeatmapEvaluation evaluation = ClearanceHeatmapEvaluator.Classify(
                raw,
                warningDistance,
                targetDistance);
            if (evaluation.Status != ClearanceEvaluationStatus.Valid)
                return Error(conditionIndex, condition,
                    ClearanceScanConditionStatus.EvaluationFailed,
                    "Clearance evaluation failed.");

            var clearances = new float[evaluation.QueryResults.Length];
            for (int vertex = 0; vertex < clearances.Length; vertex++)
            {
                clearances[vertex] = evaluation.QueryResults[vertex].IsValid
                    ? evaluation.QueryResults[vertex].SignedClearance
                    : float.PositiveInfinity;
            }
            return new ClearanceScanConditionResult(
                conditionIndex,
                condition.Name,
                ClearanceScanConditionStatus.Success,
                warningDistance: warningDistance,
                targetDistance: targetDistance,
                statistics: evaluation.Statistics,
                vertexClearances: clearances,
                usedNdmfPreviewProxy: usedPreviewProxy,
                evaluatedRendererName: evaluationTarget != null
                    ? GetHierarchyName(evaluationTarget.transform)
                    : "",
                conditionDefinition: condition);
        }

        private void AccumulateWorst(ClearanceScanConditionResult condition)
        {
            if (Result.WorstClearances.Length == 0)
            {
                Result.WorstClearances = new float[condition.VertexClearances.Length];
                Result.WorstConditionIndices = new int[condition.VertexClearances.Length];
                for (int vertex = 0; vertex < Result.WorstClearances.Length; vertex++)
                {
                    Result.WorstClearances[vertex] = float.PositiveInfinity;
                    Result.WorstConditionIndices[vertex] = -1;
                }
            }
            if (condition.VertexClearances.Length != Result.WorstClearances.Length) return;
            for (int vertex = 0; vertex < condition.VertexClearances.Length; vertex++)
            {
                if (condition.VertexClearances[vertex] >= Result.WorstClearances[vertex]) continue;
                Result.WorstClearances[vertex] = condition.VertexClearances[vertex];
                Result.WorstConditionIndices[vertex] = condition.ConditionIndex;
            }
        }

        private void Complete()
        {
            if (IsCompleted) return;
            IsCompleted = true;
            if (_restoreOnComplete) _snapshot.Restore();
        }

        private void OnUndoRedoPerformed()
        {
            _undoRedoObserved = true;
        }

        private ClearanceScanConditionResult Error(
            int conditionIndex,
            ClearanceScanCondition condition,
            ClearanceScanConditionStatus status,
            string message)
        {
            float warningDistance = condition != null && condition.OverrideThresholds
                ? condition.WarningDistance
                : _defaultWarningDistance;
            float targetDistance = condition != null && condition.OverrideThresholds
                ? condition.TargetDistance
                : _defaultTargetDistance;
            return new ClearanceScanConditionResult(
                conditionIndex,
                condition?.Name ?? "",
                status,
                message,
                warningDistance,
                targetDistance,
                conditionDefinition: condition);
        }

        internal static Renderer ResolveEvaluationTarget(Renderer original, out bool usedPreviewProxy)
        {
            usedPreviewProxy = false;
            if (original != null &&
                NDMFPreviewProxyUtility.TryGetProxyRenderer(original, out Renderer proxy) &&
                proxy != null)
            {
                usedPreviewProxy = true;
                return proxy;
            }
            return original;
        }

        private static bool TrySetBlendShape(
            Renderer renderer,
            ClearanceBlendShapeOverride blendShape,
            out string error)
        {
            error = "";
            if (renderer is not SkinnedMeshRenderer skinned || skinned.sharedMesh == null)
            {
                error = "Renderer is not a SkinnedMeshRenderer.";
                return false;
            }
            int blendShapeIndex = skinned.sharedMesh.GetBlendShapeIndex(blendShape.BlendShapeName);
            if (blendShapeIndex < 0)
            {
                error = "BlendShape was not found: " + blendShape.BlendShapeName;
                return false;
            }
            skinned.SetBlendShapeWeight(blendShapeIndex, blendShape.Weight);
            return true;
        }

        private static bool TrySynchronizeBlendShapeWeights(
            Renderer original,
            Renderer proxy,
            out string error)
        {
            error = "";
            if (original is not SkinnedMeshRenderer originalSkinned ||
                originalSkinned.sharedMesh == null)
            {
                return true;
            }
            if (proxy is not SkinnedMeshRenderer proxySkinned || proxySkinned.sharedMesh == null)
            {
                for (int index = 0; index < originalSkinned.sharedMesh.blendShapeCount; index++)
                {
                    if (Mathf.Abs(originalSkinned.GetBlendShapeWeight(index)) > 1e-6f)
                    {
                        error = "Preview proxy cannot represent active target BlendShapes.";
                        return false;
                    }
                }
                return true;
            }

            Mesh originalMesh = originalSkinned.sharedMesh;
            Mesh proxyMesh = proxySkinned.sharedMesh;
            for (int index = 0; index < originalMesh.blendShapeCount; index++)
            {
                string shapeName = originalMesh.GetBlendShapeName(index);
                float weight = originalSkinned.GetBlendShapeWeight(index);
                int proxyIndex = proxyMesh.GetBlendShapeIndex(shapeName);
                if (proxyIndex < 0)
                {
                    if (Mathf.Abs(weight) <= 1e-6f) continue;
                    error = "Preview proxy is missing active BlendShape: " + shapeName;
                    return false;
                }
                proxySkinned.SetBlendShapeWeight(proxyIndex, weight);
            }
            return true;
        }

        private static bool TrySynchronizePose(Renderer original, Renderer proxy, out string error)
        {
            error = "";
            if (original == null || proxy == null)
            {
                error = "Target renderer is missing.";
                return false;
            }
            CopyWorldTransform(original.transform, proxy.transform);
            if (original is not SkinnedMeshRenderer originalSkinned) return true;
            if (proxy is not SkinnedMeshRenderer proxySkinned)
            {
                error = "Skinned target requires a SkinnedMeshRenderer proxy.";
                return false;
            }
            Transform[] originalBones = originalSkinned.bones ?? Array.Empty<Transform>();
            Transform[] proxyBones = proxySkinned.bones ?? Array.Empty<Transform>();
            if (originalBones.Length != proxyBones.Length)
            {
                error = "Proxy bone layout does not match the target renderer.";
                return false;
            }
            for (int index = 0; index < originalBones.Length; index++)
            {
                Transform source = originalBones[index];
                Transform destination = proxyBones[index];
                if (source == null || destination == null)
                {
                    if (source != destination)
                    {
                        error = "Proxy bone layout contains a missing bone.";
                        return false;
                    }
                    continue;
                }
                if (ReferenceEquals(source, destination)) continue;
                destination.localPosition = source.localPosition;
                destination.localRotation = source.localRotation;
                destination.localScale = source.localScale;
            }
            return true;
        }

        private static void CopyWorldTransform(Transform source, Transform destination)
        {
            if (source == null || destination == null || ReferenceEquals(source, destination)) return;
            destination.SetPositionAndRotation(source.position, source.rotation);
            Vector3 parentScale = destination.parent != null
                ? destination.parent.lossyScale
                : Vector3.one;
            Vector3 sourceScale = source.lossyScale;
            destination.localScale = new Vector3(
                Mathf.Abs(parentScale.x) > 1e-8f ? sourceScale.x / parentScale.x : source.localScale.x,
                Mathf.Abs(parentScale.y) > 1e-8f ? sourceScale.y / parentScale.y : source.localScale.y,
                Mathf.Abs(parentScale.z) > 1e-8f ? sourceScale.z / parentScale.z : source.localScale.z);
        }

        private static Mesh GetRendererMesh(Renderer renderer)
        {
            return renderer switch
            {
                SkinnedMeshRenderer skinned => skinned.sharedMesh,
                MeshRenderer meshRenderer => meshRenderer.GetComponent<MeshFilter>()?.sharedMesh,
                _ => null
            };
        }

        private static string GetHierarchyName(Transform transform)
        {
            if (transform == null) return "";
            var names = new Stack<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", names);
        }
    }

    internal sealed class ClearanceScanPreviewState : IDisposable
    {
        private readonly ClearanceScanOperation _operation;
        private readonly ClearanceScanSet _temporarySet;

        private ClearanceScanPreviewState(
            ClearanceScanOperation operation,
            ClearanceScanSet temporarySet)
        {
            _operation = operation;
            _temporarySet = temporarySet;
        }

        internal static bool TryApply(
            ClearanceScanSet scanSet,
            int conditionIndex,
            LatticeDeformer deformer,
            Renderer referenceRenderer,
            Transform avatarRoot,
            ClearanceQueryMode queryMode,
            float warningDistance,
            float targetDistance,
            out ClearanceScanPreviewState previewState,
            out ClearanceScanConditionResult result)
        {
            previewState = null;
            result = null;
            if (scanSet == null || conditionIndex < 0 || conditionIndex >= scanSet.Conditions.Count)
                return false;
            var single = ScriptableObject.CreateInstance<ClearanceScanSet>();
            single.Conditions.Add(scanSet.Conditions[conditionIndex]);
            var operation = new ClearanceScanOperation(
                single, deformer, referenceRenderer, avatarRoot,
                queryMode, warningDistance, targetDistance,
                restoreOnComplete: false);
            operation.Step();
            result = operation.Result.Conditions.Count > 0
                ? operation.Result.Conditions[0]
                : null;
            if (result == null || !result.IsSuccess)
            {
                operation.Dispose();
                UnityEngine.Object.DestroyImmediate(single);
                return false;
            }
            previewState = new ClearanceScanPreviewState(operation, single);
            return true;
        }

        public void Dispose()
        {
            _operation?.Dispose();
            if (_temporarySet != null) UnityEngine.Object.DestroyImmediate(_temporarySet);
        }
    }

    internal sealed class SceneStateSnapshot
    {
        private readonly TransformState[] _transforms;
        private readonly ComponentState[] _components;
        private readonly RendererState[] _renderers;
        private readonly SkinnedRendererState[] _skinnedRenderers;
        private readonly AnimatorState[] _animators;

        private SceneStateSnapshot(
            TransformState[] transforms,
            ComponentState[] components,
            RendererState[] renderers,
            SkinnedRendererState[] skinnedRenderers,
            AnimatorState[] animators)
        {
            _transforms = transforms;
            _components = components;
            _renderers = renderers;
            _skinnedRenderers = skinnedRenderers;
            _animators = animators;
        }

        internal static SceneStateSnapshot Capture(
            Transform root,
            Renderer target,
            Renderer reference,
            ClearanceScanSet scanSet = null,
            params Renderer[] additionalRenderers)
        {
            var transforms = new List<Transform>();
            var components = new List<Component>();
            var renderers = new List<Renderer>();
            var skinned = new List<SkinnedMeshRenderer>();
            var animators = new List<Animator>();
            if (root != null)
            {
                transforms.AddRange(root.GetComponentsInChildren<Transform>(true));
                renderers.AddRange(root.GetComponentsInChildren<Renderer>(true));
                skinned.AddRange(root.GetComponentsInChildren<SkinnedMeshRenderer>(true));
                animators.AddRange(root.GetComponentsInChildren<Animator>(true));
                AddAnimatedComponents(root, scanSet, components);
            }
            AddRenderer(target, renderers, skinned);
            AddRendererTransforms(target, transforms);
            AddRenderer(reference, renderers, skinned);
            AddRendererTransforms(reference, transforms);
            if (additionalRenderers != null)
            {
                for (int index = 0; index < additionalRenderers.Length; index++)
                {
                    AddRenderer(additionalRenderers[index], renderers, skinned);
                    AddRendererTransforms(additionalRenderers[index], transforms);
                }
            }

            var transformStates = new TransformState[transforms.Count];
            for (int index = 0; index < transforms.Count; index++)
                transformStates[index] = new TransformState(transforms[index]);
            var componentStates = new ComponentState[components.Count];
            for (int index = 0; index < components.Count; index++)
                componentStates[index] = new ComponentState(components[index]);
            var rendererStates = new RendererState[renderers.Count];
            for (int index = 0; index < renderers.Count; index++)
                rendererStates[index] = new RendererState(renderers[index]);
            var skinnedStates = new SkinnedRendererState[skinned.Count];
            for (int index = 0; index < skinned.Count; index++)
                skinnedStates[index] = new SkinnedRendererState(skinned[index]);
            var animatorStates = new AnimatorState[animators.Count];
            for (int index = 0; index < animators.Count; index++)
                animatorStates[index] = new AnimatorState(animators[index]);
            return new SceneStateSnapshot(
                transformStates, componentStates, rendererStates, skinnedStates, animatorStates);
        }

        internal void Restore()
        {
            for (int index = 0; index < _components.Length; index++) _components[index].Restore();
            for (int index = 0; index < _animators.Length; index++) _animators[index].Restore();
            for (int index = 0; index < _transforms.Length; index++) _transforms[index].Restore();
            for (int index = 0; index < _renderers.Length; index++) _renderers[index].Restore();
            for (int index = 0; index < _skinnedRenderers.Length; index++) _skinnedRenderers[index].Restore();
        }

        private static void AddAnimatedComponents(
            Transform avatarRoot,
            ClearanceScanSet scanSet,
            List<Component> destination)
        {
            if (avatarRoot == null || scanSet == null) return;
            for (int conditionIndex = 0; conditionIndex < scanSet.Conditions.Count; conditionIndex++)
            {
                ClearanceScanCondition condition = scanSet.Conditions[conditionIndex];
                if (condition == null || !condition.UseAnimationClip || condition.AnimationClip == null) continue;
                Transform animationRoot = string.IsNullOrEmpty(condition.AnimationRootPath)
                    ? avatarRoot
                    : avatarRoot.Find(condition.AnimationRootPath);
                if (animationRoot == null) continue;
                EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(condition.AnimationClip);
                for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
                    AddBoundComponent(animationRoot, bindings[bindingIndex], destination);
                EditorCurveBinding[] objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(
                    condition.AnimationClip);
                for (int bindingIndex = 0; bindingIndex < objectBindings.Length; bindingIndex++)
                    AddBoundComponent(animationRoot, objectBindings[bindingIndex], destination);
            }
        }

        private static void AddRendererTransforms(Renderer renderer, List<Transform> destination)
        {
            if (renderer == null) return;
            AddTransform(renderer.transform, destination);
            if (renderer is not SkinnedMeshRenderer skinned) return;
            AddTransform(skinned.rootBone, destination);
            Transform[] bones = skinned.bones;
            if (bones == null) return;
            for (int index = 0; index < bones.Length; index++) AddTransform(bones[index], destination);
        }

        private static void AddTransform(Transform transform, List<Transform> destination)
        {
            if (transform != null && !destination.Contains(transform)) destination.Add(transform);
        }

        private static void AddBoundComponent(
            Transform animationRoot,
            EditorCurveBinding binding,
            List<Component> destination)
        {
            if (binding.type == null || binding.type == typeof(Transform) ||
                !typeof(Component).IsAssignableFrom(binding.type))
            {
                return;
            }
            Transform target = string.IsNullOrEmpty(binding.path)
                ? animationRoot
                : animationRoot.Find(binding.path);
            Component component = target != null ? target.GetComponent(binding.type) : null;
            if (component != null && !destination.Contains(component)) destination.Add(component);
        }

        private static void AddRenderer(
            Renderer renderer,
            List<Renderer> renderers,
            List<SkinnedMeshRenderer> skinned)
        {
            if (renderer == null) return;
            if (!renderers.Contains(renderer)) renderers.Add(renderer);
            if (renderer is SkinnedMeshRenderer skinnedRenderer && !skinned.Contains(skinnedRenderer))
                skinned.Add(skinnedRenderer);
        }

        private readonly struct TransformState
        {
            private readonly Transform _transform;
            private readonly Vector3 _position;
            private readonly Quaternion _rotation;
            private readonly Vector3 _scale;
            private readonly bool _active;

            internal TransformState(Transform transform)
            {
                _transform = transform;
                _position = transform.localPosition;
                _rotation = transform.localRotation;
                _scale = transform.localScale;
                _active = transform.gameObject.activeSelf;
            }

            internal void Restore()
            {
                if (_transform == null) return;
                _transform.localPosition = _position;
                _transform.localRotation = _rotation;
                _transform.localScale = _scale;
                if (_transform.gameObject.activeSelf != _active)
                    _transform.gameObject.SetActive(_active);
            }
        }

        private readonly struct ComponentState
        {
            private readonly Component _component;
            private readonly string _json;

            internal ComponentState(Component component)
            {
                _component = component;
                try
                {
                    _json = component != null ? EditorJsonUtility.ToJson(component) : null;
                }
                catch (Exception)
                {
                    _json = null;
                }
            }

            internal void Restore()
            {
                if (_component == null || string.IsNullOrEmpty(_json)) return;
                try
                {
                    EditorJsonUtility.FromJsonOverwrite(_json, _component);
                }
                catch (Exception)
                {
                    // Best-effort serialized restoration; specialized state below still restores essentials.
                }
            }
        }

        private readonly struct RendererState
        {
            private readonly Renderer _renderer;
            private readonly bool _enabled;
            private readonly Mesh _sharedMesh;

            internal RendererState(Renderer renderer)
            {
                _renderer = renderer;
                _enabled = renderer != null && renderer.enabled;
                _sharedMesh = renderer switch
                {
                    SkinnedMeshRenderer skinned => skinned.sharedMesh,
                    MeshRenderer meshRenderer => meshRenderer.GetComponent<MeshFilter>()?.sharedMesh,
                    _ => null
                };
            }

            internal void Restore()
            {
                if (_renderer == null) return;
                _renderer.enabled = _enabled;
                if (_renderer is SkinnedMeshRenderer skinned)
                    skinned.sharedMesh = _sharedMesh;
                else if (_renderer is MeshRenderer meshRenderer)
                {
                    MeshFilter filter = meshRenderer.GetComponent<MeshFilter>();
                    if (filter != null) filter.sharedMesh = _sharedMesh;
                }
            }
        }

        private readonly struct SkinnedRendererState
        {
            private readonly SkinnedMeshRenderer _renderer;
            private readonly float[] _weights;

            internal SkinnedRendererState(SkinnedMeshRenderer renderer)
            {
                _renderer = renderer;
                int count = renderer != null && renderer.sharedMesh != null
                    ? renderer.sharedMesh.blendShapeCount
                    : 0;
                _weights = new float[count];
                for (int index = 0; index < count; index++)
                    _weights[index] = renderer.GetBlendShapeWeight(index);
            }

            internal void Restore()
            {
                if (_renderer == null || _renderer.sharedMesh == null) return;
                int count = Mathf.Min(_weights.Length, _renderer.sharedMesh.blendShapeCount);
                for (int index = 0; index < count; index++)
                    _renderer.SetBlendShapeWeight(index, _weights[index]);
            }
        }

        private readonly struct AnimatorState
        {
            private readonly Animator _animator;
            private readonly bool _enabled;
            private readonly float _speed;
            private readonly bool _applyRootMotion;
            private readonly AnimatorUpdateMode _updateMode;
            private readonly AnimatorCullingMode _cullingMode;
            private readonly LayerState[] _layers;
            private readonly ParameterState[] _parameters;

            internal AnimatorState(Animator animator)
            {
                _animator = animator;
                _enabled = animator != null && animator.enabled;
                _speed = animator != null ? animator.speed : 1f;
                _applyRootMotion = animator != null && animator.applyRootMotion;
                _updateMode = animator != null ? animator.updateMode : AnimatorUpdateMode.Normal;
                _cullingMode = animator != null ? animator.cullingMode : AnimatorCullingMode.AlwaysAnimate;
                if (animator == null)
                {
                    _layers = Array.Empty<LayerState>();
                    _parameters = Array.Empty<ParameterState>();
                    return;
                }

                _layers = new LayerState[animator.layerCount];
                for (int layer = 0; layer < _layers.Length; layer++)
                    _layers[layer] = new LayerState(animator, layer);
                AnimatorControllerParameter[] parameters = animator.parameters;
                _parameters = new ParameterState[parameters.Length];
                for (int index = 0; index < parameters.Length; index++)
                    _parameters[index] = new ParameterState(animator, parameters[index]);
            }

            internal void Restore()
            {
                if (_animator == null) return;
                _animator.enabled = _enabled;
                _animator.speed = _speed;
                _animator.applyRootMotion = _applyRootMotion;
                _animator.updateMode = _updateMode;
                _animator.cullingMode = _cullingMode;
                bool canRestoreState = _animator.runtimeAnimatorController != null &&
                                       _animator.gameObject.activeInHierarchy;
                if (canRestoreState)
                {
                    bool restoreEnabled = _animator.enabled;
                    _animator.enabled = true;
                    for (int layer = 0; layer < _layers.Length && layer < _animator.layerCount; layer++)
                        _layers[layer].RestoreState(_animator, layer);
                    _animator.Update(0f);
                    for (int layer = 0; layer < _layers.Length && layer < _animator.layerCount; layer++)
                        _layers[layer].RestoreWeight(_animator, layer);
                    for (int index = 0; index < _parameters.Length; index++)
                        _parameters[index].Restore(_animator);
                    _animator.enabled = restoreEnabled;
                }
                _animator.enabled = _enabled;
            }
        }

        private readonly struct LayerState
        {
            private readonly int _fullPathHash;
            private readonly float _normalizedTime;
            private readonly float _weight;

            internal LayerState(Animator animator, int layer)
            {
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(layer);
                _fullPathHash = state.fullPathHash;
                _normalizedTime = state.normalizedTime;
                _weight = animator.GetLayerWeight(layer);
            }

            internal void RestoreState(Animator animator, int layer)
            {
                if (_fullPathHash != 0) animator.Play(_fullPathHash, layer, _normalizedTime);
            }

            internal void RestoreWeight(Animator animator, int layer)
            {
                animator.SetLayerWeight(layer, _weight);
            }
        }

        private readonly struct ParameterState
        {
            private readonly int _hash;
            private readonly AnimatorControllerParameterType _type;
            private readonly float _floatValue;
            private readonly int _intValue;
            private readonly bool _boolValue;

            internal ParameterState(Animator animator, AnimatorControllerParameter parameter)
            {
                _hash = parameter.nameHash;
                _type = parameter.type;
                _floatValue = _type == AnimatorControllerParameterType.Float ? animator.GetFloat(_hash) : 0f;
                _intValue = _type == AnimatorControllerParameterType.Int ? animator.GetInteger(_hash) : 0;
                _boolValue = (_type == AnimatorControllerParameterType.Bool ||
                              _type == AnimatorControllerParameterType.Trigger) && animator.GetBool(_hash);
            }

            internal void Restore(Animator animator)
            {
                switch (_type)
                {
                    case AnimatorControllerParameterType.Float:
                        animator.SetFloat(_hash, _floatValue);
                        break;
                    case AnimatorControllerParameterType.Int:
                        animator.SetInteger(_hash, _intValue);
                        break;
                    case AnimatorControllerParameterType.Bool:
                        animator.SetBool(_hash, _boolValue);
                        break;
                    case AnimatorControllerParameterType.Trigger:
                        if (_boolValue) animator.SetTrigger(_hash);
                        else animator.ResetTrigger(_hash);
                        break;
                }
            }
        }
    }
}
#endif
