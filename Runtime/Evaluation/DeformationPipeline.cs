using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    // Coordinates synchronous upstream evaluation and writes a caller-owned clone.
    // No component or Editor state is accessed after the input has been resolved.
    internal static class DeformationPipeline
    {
        internal static Mesh CreatePreviewMeshFromInput(Mesh inputMesh, in DeformationEvaluationInput input,
            in MeshOutputOptions options, EvaluationWorkspace workspace, float[] sourceWeights = null)
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

                bool hasSourceWeight = false;
                if (sourceWeights != null)
                    foreach (float weight in sourceWeights) hasSourceWeight |= Mathf.Abs(weight) > 1e-5f;
                if (hasSourceWeight)
                {
                    // F(base + weighted deltas) is not generally F(base) plus
                    // interpolated F(frame) deltas. Offset the base at the current
                    // pose, preserving the source frames and renderer weights.
                    var currentInput = SourceVertexResolver.Resolve(inputMesh, sourceWeights, frames.SourcePose,
                        out _, out _, out _);
                    DeformationEvaluator.Evaluate(input, currentInput, frames.DeformedCombined, workspace, generated);
                    var renderedFrames = SourceVertexResolver.Resolve(output, sourceWeights, frames.OutputPose,
                        out _, out _, out _);
                    for (int vertex = 0; vertex < vertexCount; vertex++)
                        outputVertices[vertex] = frames.DeformedCombined[vertex] - (renderedFrames[vertex] - outputVertices[vertex]);
                    output.vertices = outputVertices;
                }

                var usedNames = DeformedMeshWriter.CollectBlendShapeNames(output);
                foreach (var generatedShape in generated)
                {
                    string name = DeformedMeshWriter.MakeUniqueBlendShapeName(generatedShape.Name, usedNames);
                    DeformedMeshWriter.AddGeneratedBlendShapeFrames(output, name, outputVertices, generatedShape, options, workspace.MeshOutput);
                }

                // Preserve the published Preview-specific final normal rebuild rule.
                // Surface rebuilds must use the displayed pose, not the residual
                // vertex base that cancels the renderer's source frame offsets.
                if (hasSourceWeight) output.vertices = frames.DeformedCombined;
                DeformedMeshWriter.FinalizeSurface(inputMesh, output, options, workspace.MeshOutput,
                    NormalsRecalculationMode.LegacyUnityRecalculate);
                if (hasSourceWeight)
                {
                    AnchorCurrentSurface(inputMesh, output, sourceWeights, frames);
                    output.vertices = outputVertices;
                    if (options.RecalculateBounds) output.RecalculateBounds();
                    else output.bounds = inputMesh.bounds;
                    output.UploadMeshData(false);
                }
                return output;
            }
            catch
            {
                DeformedMeshWriter.DestroyTemporaryMesh(output);
                throw;
            }
        }

        private static void AnchorCurrentSurface(Mesh input, Mesh output, float[] weights,
            PreviewFrameWorkspace frames)
        {
            var normals = output.normals;
            var tangents = output.tangents;
            for (int shape = 0; shape < input.blendShapeCount; shape++)
            {
                float weight = weights[shape];
                if (Mathf.Abs(weight) <= 1e-5f || input.GetBlendShapeFrameCount(shape) == 0) continue;
                float first = input.GetBlendShapeFrameWeight(shape, 0);
                // CopyBlendShapes inserts a zero surface-delta frame at the baked
                // weight below the first source frame. Preview retains the source
                // frames, so cancel their current contribution in its base instead.
                if (weight >= first - 1e-5f) continue;
                output.GetBlendShapeFrameVertices(shape, 0,
                    frames.DeltaVertices, frames.DeltaNormals, frames.DeltaTangents);
                float scale = Mathf.Abs(first) > Mathf.Epsilon ? weight / first : 0f;
                for (int vertex = 0; vertex < normals.Length; vertex++)
                    normals[vertex] -= frames.DeltaNormals[vertex] * scale;
                for (int vertex = 0; vertex < tangents.Length; vertex++)
                {
                    var delta = frames.DeltaTangents[vertex] * scale;
                    tangents[vertex] -= new Vector4(delta.x, delta.y, delta.z, 0f);
                }
            }
            output.normals = normals;
            output.tangents = tangents;
        }
    }
}
