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
        internal readonly int GroupIndex;

        public GeneratedBlendShapeOutput(string name, AnimationCurve curve, Vector3[] deltas, int groupIndex = -1)
            : this(name, curve, BlendShapeCompositionMode.Single, new[] { deltas }, null, groupIndex)
        {
        }

        public GeneratedBlendShapeOutput(
            string name,
            AnimationCurve curve,
            BlendShapeCompositionMode composition,
            Vector3[][] candidates,
            float[] candidateWeights = null,
            int groupIndex = -1)
        {
            Name = name;
            Curve = curve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);
            Composition = composition;
            Candidates = candidates;
            CandidateWeights = candidateWeights;
            GroupIndex = groupIndex;
        }
    }

}
