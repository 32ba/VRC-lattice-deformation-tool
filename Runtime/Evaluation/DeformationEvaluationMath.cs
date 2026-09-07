using System;
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    internal static class DeformationEvaluationMath
    {
        internal static bool TryBuildDeltas(
            Vector3[] sourceVertices,
            Vector3[] deformedVertices,
            out Vector3[] deltas)
        {
            return TryBuildDeltas(sourceVertices, deformedVertices, out deltas, true);
        }

        internal static bool TryBuildDeltas(
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

        internal static bool HaveStrictlyIncreasingWeights(List<float> weights)
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

        internal static int ComputeBlendShapeOutputHash(IReadOnlyList<GeneratedBlendShapeOutput> blendShapes)
        {
            int hash = 17;
            for (int shape = 0; shape < blendShapes.Count; shape++)
            {
                var generated = blendShapes[shape];
                hash = hash * 31 + (generated.Name ?? "").GetHashCode();
                hash = hash * 31 + HashCurveState(generated.Curve);
                hash = hash * 31 + (int)generated.Composition;

                var candidateWeights = generated.CandidateWeights;
                hash = hash * 31 + (candidateWeights?.Length ?? 0);
                if (candidateWeights != null)
                {
                    for (int weight = 0; weight < candidateWeights.Length; weight++)
                        hash = hash * 31 + candidateWeights[weight].GetHashCode();
                }

                var candidates = generated.Candidates;
                if (candidates == null)
                {
                    hash = hash * 31;
                    continue;
                }

                hash = hash * 31 + candidates.Length;
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

        internal static int HashCurveState(AnimationCurve curve)
        {
            if (curve == null)
            {
                return 0;
            }

            int hash = HashCode.Combine(curve.preWrapMode, curve.postWrapMode, curve.length);
            var keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                hash = HashCode.Combine(
                    hash,
                    key.time,
                    key.value,
                    key.inTangent,
                    key.outTangent,
                    key.inWeight,
                    key.outWeight,
                    key.weightedMode);
            }

            return hash;
        }

    }
}
