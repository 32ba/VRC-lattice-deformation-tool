using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    // Coordinates synchronous upstream evaluation and writes a caller-owned clone.
    // No component or Editor state is accessed after the input has been resolved.
    internal static class DeformationPipeline
    {
        internal static Mesh CreatePreviewMeshFromInput(Mesh inputMesh, in DeformationEvaluationInput input,
            in MeshOutputOptions options, EvaluationWorkspace workspace)
        {
            int vertexCount = inputMesh.vertexCount;
            workspace.EnsureCapacity(vertexCount);
            var inputVertices = inputMesh.vertices;
            var outputVertices = workspace.FinalVertices;
            var generated = workspace.GeneratedShapes;
            DeformationEvaluator.Evaluate(input, inputVertices, outputVertices, workspace, generated);

            Mesh output = null;
            try
            {
                output = DeformedMeshWriter.CloneInput(inputMesh, outputVertices);

                var frames = workspace.PreviewFrames;
                if (inputMesh.blendShapeCount > 0) frames.EnsureCapacity(vertexCount);
                var deltaVertices = frames.DeltaVertices;
                var deltaNormals = frames.DeltaNormals;
                var deltaTangents = frames.DeltaTangents;
                var combined = frames.Combined;
                var deformedCombined = frames.DeformedCombined;
                var outputDelta = frames.OutputDelta;
                for (int shape = 0; shape < inputMesh.blendShapeCount; shape++)
                {
                    string shapeName = inputMesh.GetBlendShapeName(shape);
                    int frameCount = inputMesh.GetBlendShapeFrameCount(shape);
                    for (int frame = 0; frame < frameCount; frame++)
                    {
                        inputMesh.GetBlendShapeFrameVertices(
                            shape, frame, deltaVertices, deltaNormals, deltaTangents);
                        for (int vertex = 0; vertex < vertexCount; vertex++)
                            combined[vertex] = inputVertices[vertex] + deltaVertices[vertex];

                        DeformationEvaluator.Evaluate(input, combined, deformedCombined, workspace, null);
                        for (int vertex = 0; vertex < vertexCount; vertex++)
                            outputDelta[vertex] = deformedCombined[vertex] - outputVertices[vertex];

                        output.AddBlendShapeFrame(
                            shapeName,
                            inputMesh.GetBlendShapeFrameWeight(shape, frame),
                            outputDelta,
                            deltaNormals,
                            deltaTangents);
                    }
                }

                var usedNames = DeformedMeshWriter.CollectBlendShapeNames(output);
                foreach (var generatedShape in generated)
                {
                    string name = DeformedMeshWriter.MakeUniqueBlendShapeName(generatedShape.Name, usedNames);
                    DeformedMeshWriter.AddGeneratedBlendShapeFrames(output, name, outputVertices, generatedShape, options, workspace.MeshOutput);
                }

                // Preserve the published Preview-specific final normal rebuild rule.
                DeformedMeshWriter.FinalizeSurface(inputMesh, output, options, workspace.MeshOutput,
                    NormalsRecalculationMode.LegacyUnityRecalculate);
                return output;
            }
            catch
            {
                DeformedMeshWriter.DestroyTemporaryMesh(output);
                throw;
            }
        }
    }
}
