using System;
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
                // Above this many changed vertices the whole-mesh evaluation is used.
                int sparseLimit = vertexCount / 2;
                if (inputMesh.blendShapeCount > 0)
                {
                    frames.EnsureCapacity(vertexCount);
                    frames.EnsureChangedCapacity(sparseLimit);
                }
                var deltaVertices = frames.DeltaVertices;
                var deltaNormals = frames.DeltaNormals;
                var deltaTangents = frames.DeltaTangents;
                var outputDelta = frames.OutputDelta;
                for (int shape = 0; shape < inputMesh.blendShapeCount; shape++)
                {
                    string shapeName = inputMesh.GetBlendShapeName(shape);
                    int frameCount = inputMesh.GetBlendShapeFrameCount(shape);
                    for (int frame = 0; frame < frameCount; frame++)
                    {
                        inputMesh.GetBlendShapeFrameVertices(
                            shape, frame, deltaVertices, deltaNormals, deltaTangents);
                        EvaluateFrameDelta(input, inputVertices, outputVertices, frames, workspace, sparseLimit);

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

        // Writes frames.OutputDelta = F(input + frame delta) - F(input) for one
        // upstream frame, bit-identical to evaluating the whole mesh. Every
        // contribution is per-vertex and deterministic, so a vertex whose combined
        // position has the same bits as its input position evaluates to the base
        // output, and x - x is +0 for finite x. Only the remaining vertices (and
        // any with a non-finite base output) are evaluated; when they exceed
        // sparseLimit the whole mesh is evaluated as before.
        private static void EvaluateFrameDelta(in DeformationEvaluationInput input, Vector3[] inputVertices,
            Vector3[] outputVertices, PreviewFrameWorkspace frames, EvaluationWorkspace workspace, int sparseLimit)
        {
            int vertexCount = inputVertices.Length;
            var deltaVertices = frames.DeltaVertices;
            var combined = frames.Combined;
            var indices = frames.ChangedIndices;
            int changed = 0;
            bool dense = false;
            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                var position = inputVertices[vertex] + deltaVertices[vertex];
                combined[vertex] = position;
                if (dense || (HasSameBits(position, inputVertices[vertex]) && IsFinite(outputVertices[vertex])))
                    continue;
                if (changed == sparseLimit) dense = true;
                else indices[changed++] = vertex;
            }

            var outputDelta = frames.OutputDelta;
            if (dense)
            {
                var deformedCombined = frames.DeformedCombined;
                DeformationEvaluator.Evaluate(input, combined, deformedCombined, workspace, null);
                for (int vertex = 0; vertex < vertexCount; vertex++)
                    outputDelta[vertex] = deformedCombined[vertex] - outputVertices[vertex];
                return;
            }

            Array.Clear(outputDelta, 0, vertexCount);
            if (changed == 0) return;
            var changedOutput = frames.ChangedOutput;
            DeformationEvaluator.EvaluateSubset(input, combined, indices, changed, changedOutput, workspace);
            for (int i = 0; i < changed; i++)
            {
                int vertex = indices[i];
                outputDelta[vertex] = changedOutput[i] - outputVertices[vertex];
            }
        }

        private static bool HasSameBits(Vector3 a, Vector3 b) =>
            BitConverter.SingleToInt32Bits(a.x) == BitConverter.SingleToInt32Bits(b.x) &&
            BitConverter.SingleToInt32Bits(a.y) == BitConverter.SingleToInt32Bits(b.y) &&
            BitConverter.SingleToInt32Bits(a.z) == BitConverter.SingleToInt32Bits(b.z);

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

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
