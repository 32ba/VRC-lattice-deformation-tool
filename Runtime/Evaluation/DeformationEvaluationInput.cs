using System.Collections.Generic;

namespace Net._32Ba.LatticeDeformationTool
{
    internal readonly struct EvaluationSemantics
    {
        internal readonly bool PublishedBlendShapeSemantics;

        internal EvaluationSemantics(bool publishedBlendShapeSemantics)
        {
            PublishedBlendShapeSemantics = publishedBlendShapeSemantics;
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
