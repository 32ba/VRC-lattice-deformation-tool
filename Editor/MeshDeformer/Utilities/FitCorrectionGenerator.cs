#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal enum FitCorrectionStatus
    {
        Ready = 0,
        Success = 1,
        InvalidReference = 2,
        InvalidTarget = 3,
        StaleEvaluation = 4,
        TopologyMismatch = 5,
        PosedSkinnedMeshUnsupported = 6,
        NoCandidates = 7,
        InvalidSettings = 8,
        InvalidTargetTransform = 9
    }

    internal readonly struct FitCorrectionConstraintOptions
    {
        internal readonly bool UseVertexMask;
        internal readonly bool PinOpenBoundaries;
        internal readonly bool IsolateConnectedComponents;
        internal readonly bool SmoothSurface;
        internal readonly int SmoothingIterations;
        internal readonly float SmoothingStrength;
        internal readonly bool PreserveSolvedClearance;
        internal readonly bool UseSymmetry;
        internal readonly int SymmetryAxis;
        internal readonly float SymmetryTolerance;

        internal FitCorrectionConstraintOptions(
            bool useVertexMask,
            bool pinOpenBoundaries,
            bool isolateConnectedComponents,
            bool smoothSurface,
            int smoothingIterations,
            float smoothingStrength,
            bool preserveSolvedClearance,
            bool useSymmetry,
            int symmetryAxis,
            float symmetryTolerance)
        {
            UseVertexMask = useVertexMask;
            PinOpenBoundaries = pinOpenBoundaries;
            IsolateConnectedComponents = isolateConnectedComponents;
            SmoothSurface = smoothSurface;
            SmoothingIterations = Mathf.Max(0, smoothingIterations);
            SmoothingStrength = Mathf.Clamp01(smoothingStrength);
            PreserveSolvedClearance = preserveSolvedClearance;
            UseSymmetry = useSymmetry;
            SymmetryAxis = Mathf.Clamp(symmetryAxis, 0, 2);
            SymmetryTolerance = Mathf.Max(1e-6f, symmetryTolerance);
        }
    }

    internal sealed class FitCorrectionPlan
    {
        internal readonly FitCorrectionStatus Status;
        internal readonly int CandidateVertexCount;
        internal readonly int UnresolvedVertexCount;
        internal readonly float MaximumAppliedMove;
        internal readonly Vector3[] LocalDisplacements;
        internal readonly Vector3[] CorrectedWorldPositions;
        internal readonly ClearanceHeatmapEvaluation BeforeEvaluation;
        internal readonly FitCorrectionConstraintOptions Constraints;
        internal readonly float[] ConstraintMask;
        internal readonly bool[] PinnedVertices;

        internal bool CanGenerate => Status == FitCorrectionStatus.Ready && CandidateVertexCount > 0;

        internal FitCorrectionPlan(
            FitCorrectionStatus status,
            int candidateVertexCount = 0,
            int unresolvedVertexCount = 0,
            float maximumAppliedMove = 0f,
            Vector3[] localDisplacements = null,
            Vector3[] correctedWorldPositions = null,
            ClearanceHeatmapEvaluation beforeEvaluation = null,
            FitCorrectionConstraintOptions constraints = default,
            float[] constraintMask = null,
            bool[] pinnedVertices = null)
        {
            Status = status;
            CandidateVertexCount = candidateVertexCount;
            UnresolvedVertexCount = unresolvedVertexCount;
            MaximumAppliedMove = maximumAppliedMove;
            LocalDisplacements = localDisplacements ?? Array.Empty<Vector3>();
            CorrectedWorldPositions = correctedWorldPositions ?? Array.Empty<Vector3>();
            BeforeEvaluation = beforeEvaluation;
            Constraints = constraints;
            ConstraintMask = constraintMask ?? Array.Empty<float>();
            PinnedVertices = pinnedVertices ?? Array.Empty<bool>();
        }
    }

    internal sealed class FitCorrectionReport
    {
        internal readonly FitCorrectionStatus Status;
        internal readonly int LayerIndex;
        internal readonly int MovedVertexCount;
        internal readonly int ImprovedVertexCount;
        internal readonly int UnresolvedVertexCount;
        internal readonly float MaximumAppliedMove;
        internal readonly ClearanceHeatmapStatistics BeforeStatistics;
        internal readonly ClearanceHeatmapStatistics AfterStatistics;

        internal FitCorrectionReport(
            FitCorrectionStatus status,
            int layerIndex = -1,
            int movedVertexCount = 0,
            int improvedVertexCount = 0,
            int unresolvedVertexCount = 0,
            float maximumAppliedMove = 0f,
            ClearanceHeatmapStatistics beforeStatistics = default,
            ClearanceHeatmapStatistics afterStatistics = default)
        {
            Status = status;
            LayerIndex = layerIndex;
            MovedVertexCount = movedVertexCount;
            ImprovedVertexCount = improvedVertexCount;
            UnresolvedVertexCount = unresolvedVertexCount;
            MaximumAppliedMove = maximumAppliedMove;
            BeforeStatistics = beforeStatistics;
            AfterStatistics = afterStatistics;
        }
    }

    internal static class FitCorrectionGenerator
    {
        private static readonly ProfilerMarker s_analyzeMarker =
            new ProfilerMarker("FitCorrection.Analyze");
        internal static int AnalyzeCount { get; set; }
        private const float PoseToleranceSq = 1e-10f;

        internal static FitCorrectionPlan Analyze(
            LatticeDeformer deformer,
            ClearanceHeatmapRawEvaluation rawEvaluation,
            Renderer referenceRenderer,
            ClearanceQueryMode queryMode,
            FitCorrectionScope scope,
            float warningDistance,
            float targetDistance,
            float maximumMove,
            FitCorrectionConstraintOptions constraints = default)
        {
            using var analyzeScope = s_analyzeMarker.Auto();
            AnalyzeCount++;
            if (!AreSettingsValid(queryMode, scope, warningDistance, targetDistance, maximumMove) ||
                !AreConstraintsValid(constraints))
            {
                return new FitCorrectionPlan(FitCorrectionStatus.InvalidSettings);
            }

            warningDistance = Mathf.Max(0f, warningDistance);
            targetDistance = Mathf.Max(warningDistance, targetDistance);
            maximumMove = Mathf.Max(0f, maximumMove);
            if (referenceRenderer == null || rawEvaluation == null ||
                rawEvaluation.Status == ClearanceEvaluationStatus.InvalidReference)
            {
                return new FitCorrectionPlan(FitCorrectionStatus.InvalidReference);
            }

            Renderer targetRenderer = deformer != null ? deformer.TargetRenderer : null;
            if (deformer == null || targetRenderer == null || deformer.SourceMesh == null ||
                rawEvaluation.Status != ClearanceEvaluationStatus.Valid)
            {
                return new FitCorrectionPlan(FitCorrectionStatus.InvalidTarget);
            }

            if (rawEvaluation.TargetRenderer != targetRenderer ||
                rawEvaluation.ReferenceRenderer != referenceRenderer ||
                !ClearanceQueryCache.TryGetRendererStateHash(targetRenderer, out int targetStateHash) ||
                !ClearanceQueryCache.TryGetRendererStateHash(referenceRenderer, out int referenceStateHash) ||
                targetStateHash != rawEvaluation.TargetStateHash ||
                referenceStateHash != rawEvaluation.ReferenceStateHash)
            {
                return new FitCorrectionPlan(FitCorrectionStatus.StaleEvaluation);
            }

            int vertexCount = deformer.SourceMesh.vertexCount;
            if (rawEvaluation.WorldPositions.Length != vertexCount ||
                rawEvaluation.QueryResults.Length != vertexCount)
            {
                return new FitCorrectionPlan(FitCorrectionStatus.TopologyMismatch);
            }

            float[] constraintMask = ResolveConstraintMask(deformer, vertexCount, constraints.UseVertexMask);
            if (constraintMask == null)
                return new FitCorrectionPlan(FitCorrectionStatus.TopologyMismatch);

            if (targetRenderer is SkinnedMeshRenderer skinned &&
                !IsSkinnedRestPose(skinned, rawEvaluation.WorldPositions))
            {
                return new FitCorrectionPlan(FitCorrectionStatus.PosedSkinnedMeshUnsupported);
            }

            Matrix4x4 localToWorld = targetRenderer.transform.localToWorldMatrix;
            Matrix4x4 worldToLocal = targetRenderer.transform.worldToLocalMatrix;
            if (!AreInverseTransforms(localToWorld, worldToLocal))
            {
                return new FitCorrectionPlan(FitCorrectionStatus.InvalidTargetTransform);
            }

            var before = ClearanceHeatmapEvaluator.Classify(
                rawEvaluation,
                warningDistance,
                targetDistance);
            var worldDisplacements = new Vector3[vertexCount];
            var movementLimits = new float[vertexCount];
            for (int vertex = 0; vertex < vertexCount; vertex++)
                movementLimits[vertex] = maximumMove * constraintMask[vertex];
            int candidates = 0;
            var candidateVertices = new bool[vertexCount];

            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                ClearanceQueryResult result = rawEvaluation.QueryResults[vertex];
                if (!result.IsValid || !IsInScope(result.SignedClearance, scope, warningDistance, targetDistance))
                    continue;

                float requiredMove = Mathf.Max(0f, targetDistance - result.SignedClearance);
                if (requiredMove <= 1e-8f) continue;
                candidates++;
                candidateVertices[vertex] = true;

                Vector3 normal = result.NormalWorld;
                if (!IsFinite(normal) || normal.sqrMagnitude <= 1e-12f)
                    continue;
                normal.Normalize();
                float appliedMove = Mathf.Min(requiredMove, maximumMove);
                worldDisplacements[vertex] = normal * appliedMove * constraintMask[vertex];
                movementLimits[vertex] = appliedMove * constraintMask[vertex];
            }


            if (candidates == 0)
                return new FitCorrectionPlan(FitCorrectionStatus.NoCandidates);

            BuildTopology(deformer.SourceMesh, out List<int>[] adjacency, out bool[] boundaryVertices);
            var pinnedVertices = new bool[vertexCount];
            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                pinnedVertices[vertex] = constraintMask[vertex] <= 0f ||
                    (constraints.PinOpenBoundaries && boundaryVertices[vertex]);
                if (pinnedVertices[vertex]) worldDisplacements[vertex] = Vector3.zero;
            }

            if (constraints.SmoothSurface && constraints.SmoothingIterations > 0 &&
                constraints.SmoothingStrength > 0f)
            {
                SmoothDisplacements(
                    worldDisplacements,
                    adjacency,
                    pinnedVertices,
                    candidateVertices,
                    constraints,
                    movementLimits);
            }

            if (constraints.UseSymmetry)
            {
                ApplySymmetry(
                    deformer.SourceMesh,
                    worldDisplacements,
                    pinnedVertices,
                    localToWorld,
                    worldToLocal,
                    movementLimits,
                    constraints);
            }

            ClampDisplacements(worldDisplacements, pinnedVertices, movementLimits);
            if (constraints.PreserveSolvedClearance)
            {
                PreserveClearance(
                    rawEvaluation.WorldPositions,
                    worldDisplacements,
                    candidateVertices,
                    pinnedVertices,
                    movementLimits,
                    referenceRenderer,
                    queryMode,
                    targetDistance);
            }

            var localDisplacements = new Vector3[vertexCount];
            var correctedWorldPositions = new Vector3[vertexCount];
            float maximumApplied = 0f;
            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                Vector3 worldDisplacement = worldDisplacements[vertex];
                Vector3 localDisplacement = worldToLocal.MultiplyVector(worldDisplacement);
                Vector3 reconstructedWorldDisplacement = localToWorld.MultiplyVector(localDisplacement);
                float roundTripToleranceSq = Mathf.Max(
                    1e-12f,
                    worldDisplacement.sqrMagnitude * 1e-8f);
                if (!IsFinite(localDisplacement) ||
                    !IsFinite(reconstructedWorldDisplacement) ||
                    (reconstructedWorldDisplacement - worldDisplacement).sqrMagnitude > roundTripToleranceSq)
                {
                    return new FitCorrectionPlan(FitCorrectionStatus.InvalidTargetTransform);
                }

                localDisplacements[vertex] = localDisplacement;
                correctedWorldPositions[vertex] = rawEvaluation.WorldPositions[vertex] + worldDisplacement;
                maximumApplied = Mathf.Max(maximumApplied, worldDisplacement.magnitude);
            }

            int unresolved = CountUnresolved(
                correctedWorldPositions,
                candidateVertices,
                referenceRenderer,
                queryMode,
                targetDistance);

            return new FitCorrectionPlan(
                FitCorrectionStatus.Ready,
                candidates,
                unresolved,
                maximumApplied,
                localDisplacements,
                correctedWorldPositions,
                before,
                constraints,
                constraintMask,
                pinnedVertices);
        }

        internal static FitCorrectionReport Generate(
            LatticeDeformer deformer,
            FitCorrectionPlan plan,
            Renderer referenceRenderer,
            ClearanceQueryMode queryMode,
            FitCorrectionScope scope,
            float warningDistance,
            float targetDistance,
            float maximumMove)
        {
            if (deformer == null || plan == null || !plan.CanGenerate)
                return new FitCorrectionReport(plan?.Status ?? FitCorrectionStatus.InvalidTarget);
            if (!AreSettingsValid(queryMode, scope, warningDistance, targetDistance, maximumMove))
                return new FitCorrectionReport(FitCorrectionStatus.InvalidSettings);

            int layerIndex = deformer.AddLayer("Fit Correction", MeshDeformerLayerType.Brush);
            if (layerIndex < 0)
                return new FitCorrectionReport(FitCorrectionStatus.InvalidTarget);
            var layer = deformer.Layers[layerIndex];
            layer.BrushDisplacements = (Vector3[])plan.LocalDisplacements.Clone();
            layer.ConfigureFitCorrection(
                referenceRenderer,
                queryMode,
                scope,
                warningDistance,
                targetDistance,
                maximumMove);
            layer.ConfigureFitCorrectionConstraints(
                plan.Constraints.UseVertexMask,
                plan.ConstraintMask,
                plan.Constraints.PinOpenBoundaries,
                plan.Constraints.IsolateConnectedComponents,
                plan.Constraints.SmoothSurface,
                plan.Constraints.SmoothingIterations,
                plan.Constraints.SmoothingStrength,
                plan.Constraints.PreserveSolvedClearance,
                plan.Constraints.UseSymmetry,
                plan.Constraints.SymmetryAxis,
                plan.Constraints.SymmetryTolerance);
            ClearanceSignMode signMode = queryMode == ClearanceQueryMode.ClosedMesh
                ? ClearanceSignMode.ClosedMesh
                : ClearanceSignMode.ReferenceNormal;
            var afterRaw = ClearanceHeatmapEvaluator.EvaluateWorldPositions(
                plan.CorrectedWorldPositions,
                referenceRenderer,
                signMode);
            var after = ClearanceHeatmapEvaluator.Classify(afterRaw, warningDistance, targetDistance);
            int improved = 0;
            int unresolved = 0;
            int count = Mathf.Min(
                plan.BeforeEvaluation.QueryResults.Length,
                after.QueryResults.Length);
            for (int vertex = 0; vertex < count; vertex++)
            {
                ClearanceQueryResult beforeResult = plan.BeforeEvaluation.QueryResults[vertex];
                ClearanceQueryResult afterResult = after.QueryResults[vertex];
                if (!beforeResult.IsValid || !afterResult.IsValid) continue;
                if (afterResult.SignedClearance > beforeResult.SignedClearance + 1e-6f) improved++;
                if (IsInScope(beforeResult.SignedClearance, scope, warningDistance, targetDistance) &&
                    afterResult.SignedClearance < targetDistance - 1e-6f)
                {
                    unresolved++;
                }
            }

            return new FitCorrectionReport(
                FitCorrectionStatus.Success,
                layerIndex,
                plan.CandidateVertexCount,
                improved,
                unresolved,
                plan.MaximumAppliedMove,
                plan.BeforeEvaluation.Statistics,
                after.Statistics);
        }

        private static float[] ResolveConstraintMask(
            LatticeDeformer deformer,
            int vertexCount,
            bool useVertexMask)
        {
            var result = new float[vertexCount];
            for (int vertex = 0; vertex < vertexCount; vertex++) result[vertex] = 1f;
            if (!useVertexMask) return result;

            IReadOnlyList<LatticeLayer> layers = deformer.Layers;
            int activeLayerIndex = deformer.ActiveLayerIndex;
            float[] sourceMask = activeLayerIndex >= 0 && activeLayerIndex < layers.Count
                ? layers[activeLayerIndex]?.VertexMask
                : null;
            if (sourceMask == null || sourceMask.Length == 0) return result;
            if (sourceMask.Length != vertexCount) return null;
            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                float value = sourceMask[vertex];
                result[vertex] = float.IsNaN(value) || float.IsInfinity(value)
                    ? 0f
                    : Mathf.Clamp01(value);
            }
            return result;
        }

        private static void BuildTopology(
            Mesh mesh,
            out List<int>[] adjacency,
            out bool[] boundaryVertices)
        {
            int vertexCount = mesh != null ? mesh.vertexCount : 0;
            adjacency = new List<int>[vertexCount];
            boundaryVertices = new bool[vertexCount];
            for (int vertex = 0; vertex < vertexCount; vertex++)
                adjacency[vertex] = new List<int>();

            int[] triangles = mesh != null ? mesh.triangles : Array.Empty<int>();
            var edgeCounts = new Dictionary<ulong, int>();
            for (int triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
            {
                int a = triangles[triangle];
                int b = triangles[triangle + 1];
                int c = triangles[triangle + 2];
                if ((uint)a >= (uint)vertexCount || (uint)b >= (uint)vertexCount ||
                    (uint)c >= (uint)vertexCount)
                    continue;
                AddNeighbor(adjacency, a, b);
                AddNeighbor(adjacency, b, c);
                AddNeighbor(adjacency, c, a);
                CountEdge(edgeCounts, a, b);
                CountEdge(edgeCounts, b, c);
                CountEdge(edgeCounts, c, a);
            }

            foreach (var edge in edgeCounts)
            {
                if (edge.Value != 1) continue;
                int a = (int)(edge.Key >> 32);
                int b = (int)(edge.Key & uint.MaxValue);
                boundaryVertices[a] = true;
                boundaryVertices[b] = true;
            }
        }

        private static void AddNeighbor(List<int>[] adjacency, int a, int b)
        {
            if (a == b) return;
            if (!adjacency[a].Contains(b)) adjacency[a].Add(b);
            if (!adjacency[b].Contains(a)) adjacency[b].Add(a);
        }

        private static void CountEdge(Dictionary<ulong, int> counts, int a, int b)
        {
            uint min = (uint)Mathf.Min(a, b);
            uint max = (uint)Mathf.Max(a, b);
            ulong key = ((ulong)min << 32) | max;
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
        }

        private static void SmoothDisplacements(
            Vector3[] displacements,
            List<int>[] adjacency,
            bool[] pinned,
            bool[] candidates,
            FitCorrectionConstraintOptions constraints,
            float[] movementLimits)
        {
            bool[] allowed = constraints.IsolateConnectedComponents
                ? FindCandidateComponents(adjacency, candidates)
                : null;
            var next = new Vector3[displacements.Length];
            for (int iteration = 0; iteration < constraints.SmoothingIterations; iteration++)
            {
                Array.Copy(displacements, next, displacements.Length);
                for (int vertex = 0; vertex < displacements.Length; vertex++)
                {
                    if (pinned[vertex] || (allowed != null && !allowed[vertex]))
                    {
                        next[vertex] = Vector3.zero;
                        continue;
                    }
                    List<int> neighbors = adjacency[vertex];
                    if (neighbors == null || neighbors.Count == 0) continue;
                    Vector3 average = Vector3.zero;
                    for (int index = 0; index < neighbors.Count; index++)
                        average += displacements[neighbors[index]];
                    average /= neighbors.Count;
                    next[vertex] = Vector3.Lerp(
                        displacements[vertex],
                        average,
                        constraints.SmoothingStrength);
                }
                Array.Copy(next, displacements, displacements.Length);
                ClampDisplacements(displacements, pinned, movementLimits);
            }
        }

        private static bool[] FindCandidateComponents(List<int>[] adjacency, bool[] candidates)
        {
            var allowed = new bool[candidates.Length];
            var visited = new bool[candidates.Length];
            var queue = new Queue<int>();
            for (int start = 0; start < candidates.Length; start++)
            {
                if (visited[start]) continue;
                var component = new List<int>();
                bool hasCandidate = false;
                visited[start] = true;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    component.Add(current);
                    hasCandidate |= candidates[current];
                    List<int> neighbors = adjacency[current];
                    for (int index = 0; index < neighbors.Count; index++)
                    {
                        int neighbor = neighbors[index];
                        if (visited[neighbor]) continue;
                        visited[neighbor] = true;
                        queue.Enqueue(neighbor);
                    }
                }
                if (!hasCandidate) continue;
                for (int index = 0; index < component.Count; index++)
                    allowed[component[index]] = true;
            }
            return allowed;
        }

        private static void ApplySymmetry(
            Mesh mesh,
            Vector3[] worldDisplacements,
            bool[] pinned,
            Matrix4x4 localToWorld,
            Matrix4x4 worldToLocal,
            float[] movementLimits,
            FitCorrectionConstraintOptions constraints)
        {
            SymmetryVertexMap map = SymmetryVertexMapCache.GetOrCreate(
                mesh,
                constraints.SymmetryAxis,
                0f,
                constraints.SymmetryTolerance,
                UnmatchedSymmetryVertexBehavior.Skip);
            var local = new Vector3[worldDisplacements.Length];
            for (int vertex = 0; vertex < local.Length; vertex++)
                local[vertex] = worldToLocal.MultiplyVector(worldDisplacements[vertex]);

            for (int vertex = 0; vertex < local.Length; vertex++)
            {
                if (!map.TryGetPartner(vertex, out int partner) || partner < vertex) continue;
                if (partner == vertex)
                {
                    if (!pinned[vertex])
                        local[vertex] = RemoveAxisComponent(local[vertex], constraints.SymmetryAxis);
                    continue;
                }
                if (pinned[vertex] && pinned[partner]) continue;
                if (pinned[vertex])
                {
                    local[vertex] = Vector3.zero;
                    continue;
                }
                if (pinned[partner])
                {
                    local[partner] = Vector3.zero;
                    continue;
                }

                Vector3 fromPartner = SymmetryVertexMapCache.MirrorDirection(
                    local[partner], constraints.SymmetryAxis);
                Vector3 symmetric = local[vertex].sqrMagnitude <= 1e-16f
                    ? fromPartner
                    : fromPartner.sqrMagnitude <= 1e-16f
                        ? local[vertex]
                        : (local[vertex] + fromPartner) * 0.5f;
                local[vertex] = symmetric;
                local[partner] = SymmetryVertexMapCache.MirrorDirection(
                    symmetric, constraints.SymmetryAxis);
            }

            for (int vertex = 0; vertex < local.Length; vertex++)
                worldDisplacements[vertex] = localToWorld.MultiplyVector(local[vertex]);
            ClampDisplacements(worldDisplacements, pinned, movementLimits);
        }

        private static Vector3 RemoveAxisComponent(Vector3 value, int axis)
        {
            if (axis == 0) value.x = 0f;
            else if (axis == 1) value.y = 0f;
            else value.z = 0f;
            return value;
        }

        private static void PreserveClearance(
            Vector3[] sourceWorldPositions,
            Vector3[] displacements,
            bool[] candidates,
            bool[] pinned,
            float[] movementLimits,
            Renderer referenceRenderer,
            ClearanceQueryMode queryMode,
            float targetDistance)
        {
            var corrected = new Vector3[sourceWorldPositions.Length];
            for (int vertex = 0; vertex < corrected.Length; vertex++)
                corrected[vertex] = sourceWorldPositions[vertex] + displacements[vertex];
            ClearanceQueryResult[] results = ClearanceQueryCache.QueryPoints(
                referenceRenderer,
                corrected,
                Matrix4x4.identity,
                queryMode == ClearanceQueryMode.ClosedMesh
                    ? ClearanceSignMode.ClosedMesh
                    : ClearanceSignMode.ReferenceNormal);
            int count = Mathf.Min(results.Length, displacements.Length);
            for (int vertex = 0; vertex < count; vertex++)
            {
                if (!candidates[vertex] || pinned[vertex] || !results[vertex].IsValid ||
                    results[vertex].SignedClearance >= targetDistance) continue;
                Vector3 normal = results[vertex].NormalWorld;
                if (!IsFinite(normal) || normal.sqrMagnitude <= 1e-12f) continue;
                normal.Normalize();
                float permitted = movementLimits[vertex];
                float available = Mathf.Max(0f, permitted - displacements[vertex].magnitude);
                float addition = Mathf.Min(targetDistance - results[vertex].SignedClearance, available);
                displacements[vertex] += normal * addition;
            }
            ClampDisplacements(displacements, pinned, movementLimits);
        }

        private static void ClampDisplacements(
            Vector3[] displacements,
            bool[] pinned,
            float[] movementLimits)
        {
            for (int vertex = 0; vertex < displacements.Length; vertex++)
            {
                if (pinned[vertex])
                {
                    displacements[vertex] = Vector3.zero;
                    continue;
                }
                float limit = movementLimits[vertex];
                if (displacements[vertex].sqrMagnitude > limit * limit)
                    displacements[vertex] = displacements[vertex].normalized * limit;
            }
        }

        private static int CountUnresolved(
            Vector3[] correctedWorldPositions,
            bool[] candidates,
            Renderer referenceRenderer,
            ClearanceQueryMode queryMode,
            float targetDistance)
        {
            ClearanceQueryResult[] after = ClearanceQueryCache.QueryPoints(
                referenceRenderer,
                correctedWorldPositions,
                Matrix4x4.identity,
                queryMode == ClearanceQueryMode.ClosedMesh
                    ? ClearanceSignMode.ClosedMesh
                    : ClearanceSignMode.ReferenceNormal);
            int unresolved = 0;
            int count = Mathf.Min(after.Length, candidates.Length);
            for (int vertex = 0; vertex < count; vertex++)
            {
                if (!candidates[vertex]) continue;
                if (!after[vertex].IsValid || after[vertex].SignedClearance < targetDistance - 1e-6f)
                    unresolved++;
            }
            return unresolved;
        }

        private static bool IsInScope(
            float clearance,
            FitCorrectionScope scope,
            float warningDistance,
            float targetDistance)
        {
            return scope switch
            {
                FitCorrectionScope.PenetrationOnly => clearance < 0f,
                FitCorrectionScope.WarningThreshold => clearance <= warningDistance,
                FitCorrectionScope.TargetClearance => clearance < targetDistance,
                _ => false
            };
        }

        private static bool AreSettingsValid(
            ClearanceQueryMode queryMode,
            FitCorrectionScope scope,
            float warningDistance,
            float targetDistance,
            float maximumMove)
        {
            return (queryMode == ClearanceQueryMode.ReferenceNormal ||
                    queryMode == ClearanceQueryMode.ClosedMesh) &&
                   (scope == FitCorrectionScope.PenetrationOnly ||
                    scope == FitCorrectionScope.WarningThreshold ||
                    scope == FitCorrectionScope.TargetClearance) &&
                   IsFinite(warningDistance) &&
                   IsFinite(targetDistance) &&
                   IsFinite(maximumMove);
        }

        private static bool AreConstraintsValid(FitCorrectionConstraintOptions constraints)
        {
            return (!constraints.SmoothSurface ||
                    (constraints.SmoothingIterations >= 0 &&
                     IsFinite(constraints.SmoothingStrength) &&
                     constraints.SmoothingStrength >= 0f &&
                     constraints.SmoothingStrength <= 1f)) &&
                   (!constraints.UseSymmetry ||
                    (constraints.SymmetryAxis >= 0 && constraints.SymmetryAxis <= 2 &&
                     IsFinite(constraints.SymmetryTolerance) &&
                     constraints.SymmetryTolerance >= 1e-6f));
        }

        private static bool AreInverseTransforms(Matrix4x4 localToWorld, Matrix4x4 worldToLocal)
        {
            if (!IsFinite(localToWorld) || !IsFinite(worldToLocal)) return false;
            float determinant = localToWorld.determinant;
            if (!IsFinite(determinant) || Mathf.Abs(determinant) <= 1e-12f) return false;
            return IsApproximatelyIdentity(localToWorld * worldToLocal) &&
                   IsApproximatelyIdentity(worldToLocal * localToWorld);
        }

        private static bool IsApproximatelyIdentity(Matrix4x4 value)
        {
            const float tolerance = 1e-4f;
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    float expected = row == column ? 1f : 0f;
                    if (Mathf.Abs(value[row, column] - expected) > tolerance) return false;
                }
            }
            return true;
        }

        private static bool IsSkinnedRestPose(
            SkinnedMeshRenderer renderer,
            Vector3[] bakedWorldPositions)
        {
            Mesh currentMesh = renderer != null ? renderer.sharedMesh : null;
            if (currentMesh == null || currentMesh.vertexCount != bakedWorldPositions.Length) return false;
            Vector3[] restVertices;
            try
            {
                restVertices = currentMesh.vertices;
            }
            catch (Exception)
            {
                return false;
            }

            Matrix4x4 localToWorld = renderer.transform.localToWorldMatrix;
            for (int vertex = 0; vertex < restVertices.Length; vertex++)
            {
                Vector3 restWorld = localToWorld.MultiplyPoint3x4(restVertices[vertex]);
                if ((restWorld - bakedWorldPositions[vertex]).sqrMagnitude > PoseToleranceSq)
                    return false;
            }
            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Matrix4x4 value)
        {
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    if (!IsFinite(value[row, column])) return false;
                }
            }
            return true;
        }
    }
}
#endif
