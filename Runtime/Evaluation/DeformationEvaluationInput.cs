using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    internal readonly struct EvaluationSemantics
    {
        internal readonly bool PublishedBlendShapeSemantics;
        internal readonly bool AbsoluteLatticeEvaluation;
        internal readonly Matrix4x4 OwnerWorldToLocal;

        internal EvaluationSemantics(bool publishedBlendShapeSemantics)
            : this(publishedBlendShapeSemantics, false, Matrix4x4.identity)
        {
        }

        internal EvaluationSemantics(bool publishedBlendShapeSemantics, bool absoluteLatticeEvaluation,
            Matrix4x4 ownerWorldToLocal)
        {
            PublishedBlendShapeSemantics = publishedBlendShapeSemantics;
            AbsoluteLatticeEvaluation = absoluteLatticeEvaluation;
            OwnerWorldToLocal = ownerWorldToLocal;
        }
    }

    // A validated, synchronous borrowed view; never hand it to asynchronous work.
    // The component resolves Embedded/Profile and validates before construction.
    // No selection, Renderer, Scene View, or migration state enters composition.
    internal readonly struct DeformationEvaluationInput
    {
        internal readonly IReadOnlyList<DeformerGroup> Groups;
        internal readonly string DefaultOutputName;
        internal readonly EvaluationSemantics Semantics;

        internal DeformationEvaluationInput(IReadOnlyList<DeformerGroup> groups, string defaultOutputName,
            EvaluationSemantics semantics)
        {
            Groups = groups;
            DefaultOutputName = defaultOutputName;
            Semantics = semantics;
        }
    }
}
