using System;
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    public partial class LatticeDeformer
    {
        private static bool TryBuildDeltas(
            Vector3[] sourceVertices,
            Vector3[] deformedVertices,
            out Vector3[] deltas)
        {
            return TryBuildDeltas(sourceVertices, deformedVertices, out deltas, true);
        }

        private static bool TryBuildDeltas(
            Vector3[] sourceVertices,
            Vector3[] deformedVertices,
            out Vector3[] deltas,
            bool requireNonZero)
        {
            deltas = null;
            if (sourceVertices == null || deformedVertices == null || sourceVertices.Length != deformedVertices.Length)
            {
                return false;
            }

            var result = new Vector3[sourceVertices.Length];
            bool hasDelta = false;
            for (int v = 0; v < sourceVertices.Length; v++)
            {
                result[v] = deformedVertices[v] - sourceVertices[v];
                if (!hasDelta && result[v].sqrMagnitude > 1e-10f)
                {
                    hasDelta = true;
                }
            }

            if (requireNonZero && !hasDelta)
            {
                return false;
            }

            deltas = result;
            return true;
        }

        private bool TryBuildPooledDeltas(
            Vector3[] sourceVertices,
            Vector3[] deformedVertices,
            out Vector3[] deltas,
            bool requireNonZero = true)
        {
            deltas = null;
            if (sourceVertices == null || deformedVertices == null ||
                sourceVertices.Length != deformedVertices.Length)
            {
                return false;
            }

            var result = RentBlendShapeDeltaBuffer(sourceVertices.Length);
            bool hasDelta = false;
            for (int vertex = 0; vertex < sourceVertices.Length; vertex++)
            {
                result[vertex] = deformedVertices[vertex] - sourceVertices[vertex];
                if (!hasDelta && result[vertex].sqrMagnitude > 1e-10f)
                {
                    hasDelta = true;
                }
            }

            if (requireNonZero && !hasDelta)
            {
                ReturnBlendShapeDeltaBuffer(result);
                return false;
            }

            deltas = result;
            return true;
        }

        private GeneratedBlendShape CreatePooledGeneratedBlendShape(
            string name,
            AnimationCurve curve,
            Vector3[] deltas)
        {
            var candidates = RentBlendShapeCandidateList();
            candidates.Add(deltas);
            return new GeneratedBlendShape(
                name,
                curve,
                BlendShapeCompositionMode.Single,
                candidates);
        }

        private List<Vector3[]> RentBlendShapeCandidateList()
        {
            _blendShapeCandidateListPool ??= new Stack<List<Vector3[]>>();
            return _blendShapeCandidateListPool.Count > 0
                ? _blendShapeCandidateListPool.Pop()
                : new List<Vector3[]>();
        }

        private void ReturnBlendShapeCandidateList(List<Vector3[]> candidates)
        {
            if (candidates == null)
            {
                return;
            }

            candidates.Clear();
            _blendShapeCandidateListPool ??= new Stack<List<Vector3[]>>();
            _blendShapeCandidateListPool.Push(candidates);
        }

        private List<float> RentBlendShapeWeightList()
        {
            _blendShapeWeightListPool ??= new Stack<List<float>>();
            return _blendShapeWeightListPool.Count > 0
                ? _blendShapeWeightListPool.Pop()
                : new List<float>();
        }

        private void ReturnBlendShapeWeightList(List<float> weights)
        {
            if (weights == null)
            {
                return;
            }

            weights.Clear();
            _blendShapeWeightListPool ??= new Stack<List<float>>();
            _blendShapeWeightListPool.Push(weights);
        }

        private Vector3[] RentBlendShapeDeltaBuffer(int vertexCount)
        {
            _blendShapeDeltaPool ??= new Stack<Vector3[]>();
            if (_blendShapeDeltaPoolVertexCount != vertexCount)
            {
                _blendShapeDeltaPool.Clear();
                _blendShapeDeltaPoolVertexCount = vertexCount;
            }

            return _blendShapeDeltaPool.Count > 0
                ? _blendShapeDeltaPool.Pop()
                : new Vector3[vertexCount];
        }

        private void ReturnBlendShapeDeltaBuffer(Vector3[] buffer)
        {
            if (buffer == null || buffer.Length != _blendShapeDeltaPoolVertexCount)
            {
                return;
            }

            _blendShapeDeltaPool ??= new Stack<Vector3[]>();
            const int maximumRetainedBuffers = 32;
            if (_blendShapeDeltaPool.Count < maximumRetainedBuffers)
            {
                _blendShapeDeltaPool.Push(buffer);
            }
        }

        private void ReleaseGeneratedBlendShapeCandidates(List<GeneratedBlendShape> blendShapes)
        {
            if (blendShapes == null)
            {
                return;
            }

            for (int shape = 0; shape < blendShapes.Count; shape++)
            {
                var candidates = blendShapes[shape].Candidates;
                if (candidates == null)
                {
                    continue;
                }

                for (int candidate = 0; candidate < candidates.Count; candidate++)
                {
                    ReturnBlendShapeDeltaBuffer(candidates[candidate]);
                }

                if (candidates is List<Vector3[]> candidateList)
                {
                    ReturnBlendShapeCandidateList(candidateList);
                }

                if (blendShapes[shape].CandidateWeights is List<float> weightList)
                {
                    ReturnBlendShapeWeightList(weightList);
                }
            }

            blendShapes.Clear();
        }

        private static bool HaveStrictlyIncreasingWeights(IReadOnlyList<float> weights)
        {
            if (weights == null || weights.Count == 0) return false;
            float previous = float.NegativeInfinity;
            for (int i = 0; i < weights.Count; i++)
            {
                float value = weights[i];
                if (float.IsNaN(value) || float.IsInfinity(value) || value <= previous)
                    return false;
                previous = value;
            }
            return true;
        }

        private int ComputeBlendShapeOutputHash(List<GeneratedBlendShape> blendShapes)
        {
            int hash = 17;
            foreach (var generated in blendShapes)
            {
                hash = hash * 31 + (generated.Name ?? "").GetHashCode();
                hash = hash * 31 + HashCurveState(generated.Curve);
                hash = hash * 31 + (int)generated.Composition;

                var candidateWeights = generated.CandidateWeights;
                hash = hash * 31 + (candidateWeights?.Count ?? 0);
                if (candidateWeights != null)
                {
                    for (int weight = 0; weight < candidateWeights.Count; weight++)
                        hash = hash * 31 + candidateWeights[weight].GetHashCode();
                }

                var candidates = generated.Candidates;
                if (candidates == null)
                {
                    hash = hash * 31;
                    continue;
                }

                hash = hash * 31 + candidates.Count;
                foreach (var deltas in candidates)
                {
                    if (deltas == null)
                    {
                        hash = hash * 31;
                        continue;
                    }
                    for (int v = 0; v < deltas.Length; v++)
                        hash = hash * 31 + deltas[v].GetHashCode();
                }
            }
            return hash;
        }

        // Retained for existing internal callers and compatibility regression coverage.
        private void AddGeneratedBlendShapeFrames(
            Mesh mesh,
            string shapeName,
            Vector3[] baseVertices,
            Vector3[] deltas,
            AnimationCurve curve)
        {
            AddGeneratedBlendShapeFrames(
                mesh,
                shapeName,
                baseVertices,
                new GeneratedBlendShape(shapeName, curve, deltas));
        }

        private void AddGeneratedBlendShapeFrames(
            Mesh mesh,
            string shapeName,
            Vector3[] baseVertices,
            GeneratedBlendShape generated)
        {
            var candidates = generated.Candidates;
            if (mesh == null || string.IsNullOrEmpty(shapeName) || baseVertices == null ||
                candidates == null || candidates.Count == 0)
            {
                return;
            }

            int vertexCount = mesh.vertexCount;
            if (baseVertices.Length != vertexCount)
            {
                return;
            }
            for (int candidate = 0; candidate < candidates.Count; candidate++)
            {
                if (candidates[candidate] == null || candidates[candidate].Length != vertexCount)
                    return;
            }

            var curve = generated.Curve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);

            Vector3[][] candidateDeltaNormals = null;
            Vector3[][] candidateDeltaTangents = null;
            bool outputsCandidateWeightsDirectly = generated.CandidateWeights != null &&
                generated.CandidateWeights.Count == candidates.Count;
            bool recomputeComposedSurfaceDeltas = !_legacyPublishedBlendShapeSemantics &&
                generated.Composition != BlendShapeCompositionMode.Single &&
                (_recalculateNormals || _recalculateTangents);
            if ((outputsCandidateWeightsDirectly || !recomputeComposedSurfaceDeltas) &&
                !_legacyPublishedBlendShapeSemantics &&
                (_recalculateNormals || _recalculateTangents))
            {
                candidateDeltaNormals = _recalculateNormals ? new Vector3[candidates.Count][] : null;
                candidateDeltaTangents = _recalculateTangents ? new Vector3[candidates.Count][] : null;
                for (int candidate = 0; candidate < candidates.Count; candidate++)
                {
                    CalculateGeneratedSurfaceDeltasPooled(
                        mesh,
                        baseVertices,
                        candidates[candidate],
                        _recalculateNormals,
                        _recalculateTangents,
                        out var normals,
                        out var tangents);
                    if (candidateDeltaNormals != null) candidateDeltaNormals[candidate] = normals;
                    if (candidateDeltaTangents != null) candidateDeltaTangents[candidate] = tangents;
                }
            }

            if (outputsCandidateWeightsDirectly)
            {
                for (int candidate = 0; candidate < candidates.Count; candidate++)
                {
                    mesh.AddBlendShapeFrame(
                        shapeName,
                        generated.CandidateWeights[candidate],
                        candidates[candidate],
                        candidateDeltaNormals?[candidate],
                        candidateDeltaTangents?[candidate]);
                }
                ReturnSurfaceDeltaCandidates(candidateDeltaNormals);
                ReturnSurfaceDeltaCandidates(candidateDeltaTangents);
                return;
            }

            const int sampleCount = 100;
            for (int f = 0; f < sampleCount; f++)
            {
                float t = (f + 1f) / sampleCount;
                float frameWeight = t * 100f;
                float curveValue = curve.Evaluate(t);
                if (generated.Composition != BlendShapeCompositionMode.Single)
                {
                    curveValue = Mathf.Clamp01(curveValue);
                }

                var frameDeltas = RentBlendShapeDeltaBuffer(vertexCount);
                Vector3[] frameNormals = null;
                Vector3[] frameTangents = null;
                bool pooledFrameNormals = false;
                bool pooledFrameTangents = false;
                try
                {
                    ComposeBlendShapeCandidatesInto(
                        candidates,
                        generated.Composition,
                        curveValue,
                        frameDeltas);
                    if (recomputeComposedSurfaceDeltas)
                    {
                        CalculateGeneratedSurfaceDeltasPooled(
                            mesh,
                            baseVertices,
                            frameDeltas,
                            _recalculateNormals,
                            _recalculateTangents,
                            out frameNormals,
                            out frameTangents);
                        pooledFrameNormals = frameNormals != null;
                        pooledFrameTangents = frameTangents != null;
                    }
                    else
                    {
                        if (candidateDeltaNormals != null)
                        {
                            frameNormals = RentBlendShapeDeltaBuffer(vertexCount);
                            pooledFrameNormals = true;
                            ComposeBlendShapeCandidatesInto(
                                candidateDeltaNormals,
                                generated.Composition,
                                curveValue,
                                frameNormals);
                        }

                        if (candidateDeltaTangents != null)
                        {
                            frameTangents = RentBlendShapeDeltaBuffer(vertexCount);
                            pooledFrameTangents = true;
                            ComposeBlendShapeCandidatesInto(
                                candidateDeltaTangents,
                                generated.Composition,
                                curveValue,
                                frameTangents);
                        }
                    }

                    mesh.AddBlendShapeFrame(
                        shapeName,
                        frameWeight,
                        frameDeltas,
                        frameNormals,
                        frameTangents);
                }
                finally
                {
                    ReturnBlendShapeDeltaBuffer(frameDeltas);
                    if (pooledFrameNormals) ReturnBlendShapeDeltaBuffer(frameNormals);
                    if (pooledFrameTangents) ReturnBlendShapeDeltaBuffer(frameTangents);
                }
            }

            ReturnSurfaceDeltaCandidates(candidateDeltaNormals);
            ReturnSurfaceDeltaCandidates(candidateDeltaTangents);
        }

        private void ReturnSurfaceDeltaCandidates(IReadOnlyList<Vector3[]> candidates)
        {
            if (candidates == null)
            {
                return;
            }

            for (int candidate = 0; candidate < candidates.Count; candidate++)
            {
                ReturnBlendShapeDeltaBuffer(candidates[candidate]);
            }
        }

        private static Vector3[] ComposeBlendShapeCandidates(
            IReadOnlyList<Vector3[]> candidates,
            BlendShapeCompositionMode composition,
            float normalizedProgress,
            int vertexCount)
        {
            var result = new Vector3[vertexCount];
            ComposeBlendShapeCandidatesInto(
                candidates,
                composition,
                normalizedProgress,
                result);
            return result;
        }

        private static void ComposeBlendShapeCandidatesInto(
            IReadOnlyList<Vector3[]> candidates,
            BlendShapeCompositionMode composition,
            float normalizedProgress,
            Vector3[] result)
        {
            if (result == null)
            {
                return;
            }

            Array.Clear(result, 0, result.Length);
            int vertexCount = result.Length;
            if (candidates == null || candidates.Count == 0) return;

            if (composition == BlendShapeCompositionMode.Single || candidates.Count == 1)
            {
                float scale = normalizedProgress;
                var candidate = candidates[0];
                if (candidate == null || candidate.Length != vertexCount) return;
                for (int vertex = 0; vertex < vertexCount; vertex++)
                    result[vertex] = candidate[vertex] * scale;
                return;
            }

            normalizedProgress = Mathf.Clamp01(normalizedProgress);
            float stageProgress = normalizedProgress * candidates.Count;
            if (composition == BlendShapeCompositionMode.Progressive)
            {
                int completedStages = Mathf.Min(Mathf.FloorToInt(stageProgress), candidates.Count);
                for (int stage = 0; stage < completedStages; stage++)
                {
                    var candidate = candidates[stage];
                    if (candidate == null || candidate.Length != vertexCount) continue;
                    for (int vertex = 0; vertex < vertexCount; vertex++)
                        result[vertex] += candidate[vertex];
                }

                if (completedStages < candidates.Count)
                {
                    float fraction = stageProgress - completedStages;
                    var candidate = candidates[completedStages];
                    if (candidate == null || candidate.Length != vertexCount) return;
                    for (int vertex = 0; vertex < vertexCount; vertex++)
                        result[vertex] += candidate[vertex] * fraction;
                }
                return;
            }

            if (stageProgress <= 1f)
            {
                var first = candidates[0];
                if (first == null || first.Length != vertexCount) return;
                for (int vertex = 0; vertex < vertexCount; vertex++)
                    result[vertex] = first[vertex] * stageProgress;
                return;
            }

            int lower = Mathf.Min(Mathf.FloorToInt(stageProgress) - 1, candidates.Count - 1);
            int upper = Mathf.Min(lower + 1, candidates.Count - 1);
            float blend = upper == lower ? 0f : stageProgress - Mathf.Floor(stageProgress);
            if (candidates[lower] == null || candidates[upper] == null ||
                candidates[lower].Length != vertexCount || candidates[upper].Length != vertexCount)
            {
                return;
            }
            for (int vertex = 0; vertex < vertexCount; vertex++)
                result[vertex] = Vector3.LerpUnclamped(candidates[lower][vertex], candidates[upper][vertex], blend);
        }

        private static void CalculateGeneratedSurfaceDeltas(
            Mesh template,
            Vector3[] baseVertices,
            Vector3[] deltas,
            bool includeNormals,
            bool includeTangents,
            out Vector3[] deltaNormals,
            out Vector3[] deltaTangents)
        {
            deltaNormals = null;
            deltaTangents = null;

            if (template == null || baseVertices == null || deltas == null || baseVertices.Length != deltas.Length)
            {
                return;
            }

            int vertexCount = baseVertices.Length;
            var targetVertices = new Vector3[vertexCount];
            var normalBuffer = includeNormals ? new Vector3[vertexCount] : null;
            var tangentBuffer = includeTangents ? new Vector3[vertexCount] : null;
            CalculateGeneratedSurfaceDeltasInto(
                template,
                baseVertices,
                deltas,
                targetVertices,
                normalBuffer,
                tangentBuffer,
                out bool hasNormals,
                out bool hasTangents);
            deltaNormals = hasNormals ? normalBuffer : null;
            deltaTangents = hasTangents ? tangentBuffer : null;
        }

        private void CalculateGeneratedSurfaceDeltasPooled(
            Mesh template,
            Vector3[] baseVertices,
            Vector3[] deltas,
            bool includeNormals,
            bool includeTangents,
            out Vector3[] deltaNormals,
            out Vector3[] deltaTangents)
        {
            deltaNormals = null;
            deltaTangents = null;
            if (template == null || baseVertices == null || deltas == null ||
                baseVertices.Length != deltas.Length)
            {
                return;
            }

            int vertexCount = baseVertices.Length;
            var targetVertices = RentBlendShapeDeltaBuffer(vertexCount);
            var normalBuffer = includeNormals ? RentBlendShapeDeltaBuffer(vertexCount) : null;
            var tangentBuffer = includeTangents ? RentBlendShapeDeltaBuffer(vertexCount) : null;
            try
            {
                CalculateGeneratedSurfaceDeltasInto(
                    template,
                    baseVertices,
                    deltas,
                    targetVertices,
                    normalBuffer,
                    tangentBuffer,
                    out bool hasNormals,
                    out bool hasTangents);
                if (hasNormals)
                {
                    deltaNormals = normalBuffer;
                    normalBuffer = null;
                }

                if (hasTangents)
                {
                    deltaTangents = tangentBuffer;
                    tangentBuffer = null;
                }
            }
            finally
            {
                ReturnBlendShapeDeltaBuffer(targetVertices);
                ReturnBlendShapeDeltaBuffer(normalBuffer);
                ReturnBlendShapeDeltaBuffer(tangentBuffer);
            }
        }

        private static void CalculateGeneratedSurfaceDeltasInto(
            Mesh template,
            Vector3[] baseVertices,
            Vector3[] deltas,
            Vector3[] targetVertices,
            Vector3[] deltaNormals,
            Vector3[] deltaTangents,
            out bool hasNormals,
            out bool hasTangents)
        {
            hasNormals = false;
            hasTangents = false;
            if (template == null || baseVertices == null || deltas == null ||
                targetVertices == null || baseVertices.Length != deltas.Length ||
                targetVertices.Length != baseVertices.Length)
            {
                return;
            }

            Mesh baseMesh = null;
            Mesh targetMesh = null;
            try
            {
                baseMesh = UnityEngine.Object.Instantiate(template);
                targetMesh = UnityEngine.Object.Instantiate(template);

                int vertexCount = baseVertices.Length;
                for (int i = 0; i < vertexCount; i++)
                {
                    targetVertices[i] = baseVertices[i] + deltas[i];
                }

                baseMesh.vertices = baseVertices;
                targetMesh.vertices = targetVertices;

                if (deltaNormals != null && deltaNormals.Length == vertexCount)
                {
                    baseMesh.RecalculateNormals();
                    targetMesh.RecalculateNormals();

                    var baseNormals = baseMesh.normals;
                    var targetNormals = targetMesh.normals;
                    if (baseNormals != null && targetNormals != null &&
                        baseNormals.Length == vertexCount && targetNormals.Length == vertexCount)
                    {
                        for (int i = 0; i < vertexCount; i++)
                        {
                            deltaNormals[i] = targetNormals[i] - baseNormals[i];
                        }
                        hasNormals = true;
                    }
                }

                if (deltaTangents != null && deltaTangents.Length == vertexCount)
                {
                    baseMesh.RecalculateNormals();
                    targetMesh.RecalculateNormals();
                    baseMesh.RecalculateTangents();
                    targetMesh.RecalculateTangents();

                    var baseTangents = baseMesh.tangents;
                    var targetTangents = targetMesh.tangents;
                    if (baseTangents != null && targetTangents != null &&
                        baseTangents.Length == vertexCount && targetTangents.Length == vertexCount)
                    {
                        for (int i = 0; i < vertexCount; i++)
                        {
                            deltaTangents[i] = new Vector3(
                                targetTangents[i].x - baseTangents[i].x,
                                targetTangents[i].y - baseTangents[i].y,
                                targetTangents[i].z - baseTangents[i].z);
                        }
                        hasTangents = true;
                    }
                }
            }
            finally
            {
                DestroyTemporaryMesh(baseMesh);
                DestroyTemporaryMesh(targetMesh);
            }
        }

        private static HashSet<string> CollectBlendShapeNames(Mesh mesh)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (mesh == null)
            {
                return names;
            }

            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                names.Add(mesh.GetBlendShapeName(i));
            }

            return names;
        }

        private static string MakeUniqueBlendShapeName(string requestedName, HashSet<string> usedNames)
        {
            usedNames ??= new HashSet<string>(StringComparer.Ordinal);

            string baseName = string.IsNullOrWhiteSpace(requestedName) ? "BlendShape" : requestedName.Trim();
            string name = baseName;
            int suffix = 1;
            while (usedNames.Contains(name))
            {
                name = $"{baseName} {suffix}";
                suffix++;
            }

            usedNames.Add(name);
            return name;
        }

        private static void DestroyTemporaryMesh(Mesh mesh)
        {
            if (mesh == null)
            {
                return;
            }

            // The release gate is EditMode-only; PlayMode destruction is a Unity branch.
#line hidden
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(mesh);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
#line default
        }

        private static void CopyBlendShapes(
            Mesh source,
            Mesh destination,
            Vector3[][] bakedBlendShapeDeltas = null,
            float[] bakedBlendShapeWeights = null)
        {
            int shapeCount = source.blendShapeCount;
            int vertexCount = source.vertexCount;
            for (int s = 0; s < shapeCount; s++)
            {
                string name = source.GetBlendShapeName(s);
                int frameCount = source.GetBlendShapeFrameCount(s);
                var baked = bakedBlendShapeDeltas != null && s < bakedBlendShapeDeltas.Length
                    ? bakedBlendShapeDeltas[s]
                    : null;
                float bakedWeight = bakedBlendShapeWeights != null && s < bakedBlendShapeWeights.Length
                    ? bakedBlendShapeWeights[s]
                    : 0f;
                bool hasBakedShape = baked != null && baked.Length == vertexCount;

                if (hasBakedShape && frameCount > 0)
                {
                    float firstWeight = source.GetBlendShapeFrameWeight(s, 0);
                    if (bakedWeight < firstWeight - 1e-5f)
                    {
                        destination.AddBlendShapeFrame(
                            name,
                            bakedWeight,
                            new Vector3[vertexCount],
                            new Vector3[vertexCount],
                            new Vector3[vertexCount]);
                    }
                }

                for (int f = 0; f < frameCount; f++)
                {
                    float weight = source.GetBlendShapeFrameWeight(s, f);
                    var dv = new Vector3[vertexCount];
                    var dn = new Vector3[vertexCount];
                    var dt = new Vector3[vertexCount];
                    source.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                    if (hasBakedShape)
                    {
                        for (int v = 0; v < vertexCount; v++)
                        {
                            dv[v] -= baked[v];
                        }
                    }

                    destination.AddBlendShapeFrame(name, weight, dv, dn, dt);
                }
            }
        }

        private Vector3[] BuildCurrentSourceVertices(
            out Vector3[][] bakedBlendShapeDeltas,
            out float[] bakedBlendShapeWeights,
            out int bakedBlendShapeHash)
        {
            bakedBlendShapeDeltas = null;
            bakedBlendShapeWeights = null;
            bakedBlendShapeHash = 0;

            if (_sourceMesh == null || !_sourceMesh.isReadable)
            {
                return null;
            }

            int sourceVertexCount = _sourceMesh.vertexCount;
            if (sourceVertexCount <= 0)
            {
                return Array.Empty<Vector3>();
            }

            EnsureManagedDeformationBuffers(sourceVertexCount);
            _sourceVertexScratch ??= new List<Vector3>(sourceVertexCount);
            if (_sourceVertexScratch.Capacity < sourceVertexCount)
            {
                _sourceVertexScratch.Capacity = sourceVertexCount;
            }

            _sourceMesh.GetVertices(_sourceVertexScratch);
            if (_sourceVertexScratch.Count != sourceVertexCount)
            {
                return null;
            }

            _sourceVertexScratch.CopyTo(_sourceVerticesBuffer, 0);
            var vertices = _sourceVerticesBuffer;

            if (_skinnedMeshRenderer == null || _sourceMesh.blendShapeCount == 0)
            {
                return vertices;
            }

            int shapeCount = _sourceMesh.blendShapeCount;
            int vertexCount = _sourceMesh.vertexCount;
            Vector3[][] deltas = null;
            float[] weights = null;
            bool hasBakedShape = false;
            int hash = 17;

            for (int s = 0; s < shapeCount; s++)
            {
                float weight = _skinnedMeshRenderer.GetBlendShapeWeight(s);
                if (Mathf.Abs(weight) <= 1e-5f)
                {
                    continue;
                }

                var delta = EvaluateBlendShapeVertexDelta(_sourceMesh, s, weight);
                deltas ??= new Vector3[shapeCount][];
                weights ??= new float[shapeCount];
                deltas[s] = delta;
                weights[s] = weight;
                hasBakedShape = true;
                hash = HashCode.Combine(hash, s, weight);

                for (int v = 0; v < vertexCount; v++)
                {
                    vertices[v] += delta[v];
                }
            }

            if (!hasBakedShape)
            {
                return vertices;
            }

            bakedBlendShapeDeltas = deltas;
            bakedBlendShapeWeights = weights;
            bakedBlendShapeHash = hash;
            return vertices;
        }

        private void EnsureManagedDeformationBuffers(int vertexCount)
        {
            if (vertexCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(vertexCount));
            }

            EnsureVectorBuffer(ref _sourceVerticesBuffer, vertexCount);
            EnsureVectorBuffer(ref _directDeltasBuffer, vertexCount);
            EnsureVectorBuffer(ref _groupVerticesBuffer, vertexCount);
            EnsureVectorBuffer(ref _layerVerticesBuffer, vertexCount);
            EnsureVectorBuffer(ref _finalVerticesBuffer, vertexCount);
            EnsureVectorBuffer(ref _latticeOutputBuffer, vertexCount);
            _generatedBlendShapeBuffer ??= new List<GeneratedBlendShape>();
            _blendShapeCandidateListPool ??= new Stack<List<Vector3[]>>();
            _blendShapeWeightListPool ??= new Stack<List<float>>();
            _blendShapeDeltaPool ??= new Stack<Vector3[]>();
        }

        private static void EnsureVectorBuffer(ref Vector3[] buffer, int length)
        {
            if (buffer == null || buffer.Length != length)
            {
                buffer = length == 0 ? Array.Empty<Vector3>() : new Vector3[length];
            }
        }

        private static Vector3[] EvaluateBlendShapeVertexDelta(Mesh mesh, int shapeIndex, float weight)
        {
            int frameCount = mesh.GetBlendShapeFrameCount(shapeIndex);
            int vertexCount = mesh.vertexCount;
            var lower = new Vector3[vertexCount];
            var upper = new Vector3[vertexCount];
            var unusedNormals = new Vector3[vertexCount];
            var unusedTangents = new Vector3[vertexCount];

            if (frameCount == 0)
            {
                return lower;
            }

            float firstWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, 0);
            if (weight <= firstWeight || frameCount == 1)
            {
                mesh.GetBlendShapeFrameVertices(shapeIndex, 0, lower, unusedNormals, unusedTangents);
                float scale = Mathf.Abs(firstWeight) > Mathf.Epsilon ? weight / firstWeight : 0f;
                ScaleDeltas(lower, scale);
                return lower;
            }

            for (int frame = 1; frame < frameCount; frame++)
            {
                float upperWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, frame);
                if (weight <= upperWeight)
                {
                    float lowerWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, frame - 1);
                    mesh.GetBlendShapeFrameVertices(shapeIndex, frame - 1, lower, unusedNormals, unusedTangents);
                    mesh.GetBlendShapeFrameVertices(shapeIndex, frame, upper, unusedNormals, unusedTangents);

                    float t = Mathf.Abs(upperWeight - lowerWeight) > Mathf.Epsilon
                        ? Mathf.InverseLerp(lowerWeight, upperWeight, weight)
                        : 0f;
                    for (int i = 0; i < vertexCount; i++)
                    {
                        lower[i] = Vector3.LerpUnclamped(lower[i], upper[i], t);
                    }

                    return lower;
                }
            }

            int lastFrame = frameCount - 1;
            mesh.GetBlendShapeFrameVertices(shapeIndex, lastFrame, lower, unusedNormals, unusedTangents);
            float lastWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, lastFrame);
            float previousWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, lastFrame - 1);
            float interval = lastWeight - previousWeight;
            if (Mathf.Abs(interval) > Mathf.Epsilon)
            {
                // Preserve the package's historical source-geometry behavior: extrapolate
                // the last frame itself instead of continuing the final two-frame slope.
                float scale = 1f + (weight - lastWeight) / interval;
                ScaleDeltas(lower, scale);
            }
            return lower;
        }

        private static void ScaleDeltas(Vector3[] deltas, float scale)
        {
            if (deltas == null)
            {
                return;
            }

            for (int i = 0; i < deltas.Length; i++)
            {
                deltas[i] *= scale;
            }
        }

    }
}
