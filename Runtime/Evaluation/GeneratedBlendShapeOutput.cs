using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    internal readonly struct GeneratedBlendShapeOutput
    {
        public readonly string Name;
        public readonly AnimationCurve Curve;
        public readonly BlendShapeCompositionMode Composition;
        public readonly Vector3[][] Candidates;
        public readonly float[] CandidateWeights;

        public GeneratedBlendShapeOutput(string name, AnimationCurve curve, Vector3[] deltas)
            : this(name, curve, BlendShapeCompositionMode.Single, new[] { deltas }, null)
        {
        }

        public GeneratedBlendShapeOutput(
            string name,
            AnimationCurve curve,
            BlendShapeCompositionMode composition,
            Vector3[][] candidates,
            float[] candidateWeights = null)
        {
            Name = name;
            Curve = curve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);
            Composition = composition;
            Candidates = candidates;
            CandidateWeights = candidateWeights;
        }
    }

}
