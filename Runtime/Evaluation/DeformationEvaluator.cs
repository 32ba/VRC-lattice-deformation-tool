using System;
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    // Shared Group/Layer composition for source evaluation and upstream frames.
    // Geometry reads the resolved semantics and the caller-owned workspace.
    internal static class DeformationEvaluator
    {
        internal static void Evaluate(in DeformationEvaluationInput input, Vector3[] sourceVertices,
            Vector3[] finalVertices, EvaluationWorkspace workspace,
            List<GeneratedBlendShapeOutput> generatedBlendShapes)
        {
            if (sourceVertices == null || finalVertices == null || sourceVertices.Length != finalVertices.Length)
                throw new ArgumentException("Source and output vertex counts must match.");
            // Every Lattice layer below reads the same unchanged source vertices;
            // finalVertices is written only after the last layer. A source that
            // aliases a per-layer scratch buffer keeps per-layer hashing.
            bool sourceIsScratch = ReferenceEquals(sourceVertices, workspace.GroupVertices) ||
                                   ReferenceEquals(sourceVertices, workspace.LayerVertices) ||
                                   ReferenceEquals(sourceVertices, workspace.DirectDeltas);
            workspace.Lattice.BeginSourceScope(sourceIsScratch ? null : sourceVertices);
            try
            {
                EvaluateLayers(input, sourceVertices, finalVertices, workspace, generatedBlendShapes);
            }
            finally
            {
                workspace.Lattice.EndSourceScope();
            }
        }

        private static void EvaluateLayers(in DeformationEvaluationInput input, Vector3[] sourceVertices,
            Vector3[] finalVertices, EvaluationWorkspace workspace,
            List<GeneratedBlendShapeOutput> generatedBlendShapes)
        {
            int vertexCount = sourceVertices.Length;
            workspace.EnsureCapacity(vertexCount);
            // Accumulate direct-deform deltas across all groups
            var directDeltas = workspace.DirectDeltas;
            Array.Clear(directDeltas, 0, vertexCount);
            // Collect generated BlendShapes from groups and individual layers.
            generatedBlendShapes?.Clear();
            var groups = input.Groups;

            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                if (group == null || !group.Enabled) continue;
                if (generatedBlendShapes == null &&
                    group.BlendShapeOutput == BlendShapeOutputMode.OutputAsBlendShape) continue;

                var groupVertices = workspace.GroupVertices;
                Array.Copy(sourceVertices, groupVertices, vertexCount);
                var layers = group.SerializedLayers;
                bool stagedGroupOutput =
                    group.BlendShapeOutput == BlendShapeOutputMode.OutputAsBlendShape &&
                    group.BlendShapeComposition != BlendShapeCompositionMode.Single;
                var stageCandidates = stagedGroupOutput ? new List<Vector3[]>() : null;
                var stageCandidateWeights = stagedGroupOutput ? new List<float>() : null;
                bool preserveCandidateWeights = stagedGroupOutput;

                for (int i = 0; i < layers.Count; i++)
                {
                    var layer = layers[i];
                    if (layer == null || !layer.Enabled || layer.Weight <= 0f) continue;

                    if (!input.Semantics.PublishedBlendShapeSemantics &&
                        layer.BlendShapeOutput == BlendShapeOutputMode.OutputAsBlendShape)
                    {
                        if (generatedBlendShapes == null) continue;
                        var layerVertices = workspace.LayerVertices;
                        Array.Copy(sourceVertices, layerVertices, vertexCount);
                        ApplyLayer(layer, sourceVertices, layerVertices, input.Semantics, workspace);
                        if (DeformationEvaluationMath.TryBuildDeltas(sourceVertices, layerVertices, out var layerDeltas))
                        {
                            generatedBlendShapes.Add(new GeneratedBlendShapeOutput(
                                layer.EffectiveBlendShapeName,
                                layer.SerializedBlendShapeCurve,
                                layerDeltas));
                        }

                        continue;
                    }

                    if (stagedGroupOutput)
                    {
                        var layerVertices = workspace.LayerVertices;
                        Array.Copy(sourceVertices, layerVertices, vertexCount);
                        ApplyLayer(layer, sourceVertices, layerVertices, input.Semantics, workspace);
                        if (DeformationEvaluationMath.TryBuildDeltas(
                                sourceVertices,
                                layerVertices,
                                out var stageDeltas,
                                !layer.HasImportedBlendShapeFrameWeight))
                        {
                            stageCandidates.Add(stageDeltas);
                            if (layer.HasImportedBlendShapeFrameWeight)
                                stageCandidateWeights.Add(layer.ImportedBlendShapeFrameWeight);
                            else
                                preserveCandidateWeights = false;
                        }
                    }
                    else
                    {
                        ApplyLayer(layer, sourceVertices, groupVertices, input.Semantics, workspace);
                    }
                }

                if (group.BlendShapeOutput == BlendShapeOutputMode.OutputAsBlendShape)
                {
                    if (stagedGroupOutput && stageCandidates.Count > 0)
                    {
                        float[] candidateWeights =
                            group.BlendShapeComposition == BlendShapeCompositionMode.Crossfade &&
                            preserveCandidateWeights &&
                            DeformationEvaluationMath.HaveStrictlyIncreasingWeights(stageCandidateWeights)
                            ? stageCandidateWeights.ToArray()
                            : null;
                        generatedBlendShapes.Add(new GeneratedBlendShapeOutput(
                            group.EffectiveBlendShapeName(input.DefaultOutputName),
                            group.SerializedBlendShapeCurve,
                            group.BlendShapeComposition,
                            stageCandidates.ToArray(),
                            candidateWeights, g));
                    }
                    else if (!stagedGroupOutput &&
                             DeformationEvaluationMath.TryBuildDeltas(sourceVertices, groupVertices, out var groupDeltas))
                    {
                        generatedBlendShapes.Add(new GeneratedBlendShapeOutput(
                            group.EffectiveBlendShapeName(input.DefaultOutputName),
                            group.SerializedBlendShapeCurve,
                            groupDeltas, g));
                    }
                }
                else
                {
                    for (int v = 0; v < vertexCount; v++)
                        directDeltas[v] += groupVertices[v] - sourceVertices[v];
                }
            }

            // Apply direct deltas
            for (int v = 0; v < vertexCount; v++)
                finalVertices[v] = sourceVertices[v] + directDeltas[v];

        }

        // Produces, for vertices indices[0..count), exactly the values that
        // Evaluate(input, positions, final, workspace, null) writes to final at
        // those indices: compactOutput[i] == final[indices[i]] bit for bit. Every
        // contribution is per-vertex (Brush/Mask by index, Lattice by position),
        // and each arithmetic step below repeats Evaluate's order per vertex.
        // Output groups and individually output layers are skipped as Evaluate
        // skips them without a generated list. compactOutput is caller-owned.
        internal static void EvaluateSubset(in DeformationEvaluationInput input, Vector3[] positions,
            int[] indices, int count, Vector3[] compactOutput, EvaluationWorkspace workspace)
        {
            if (positions == null || indices == null || compactOutput == null)
                throw new ArgumentNullException(positions == null ? nameof(positions) :
                    indices == null ? nameof(indices) : nameof(compactOutput));
            if (count < 0 || count > indices.Length || count > compactOutput.Length)
                throw new ArgumentOutOfRangeException(nameof(count));
            int vertexCount = positions.Length;
            workspace.EnsureSubsetCapacity(count);
            var source = workspace.SubsetSource;
            for (int i = 0; i < count; i++) source[i] = positions[indices[i]];
            var directDeltas = workspace.SubsetDirect;
            Array.Clear(directDeltas, 0, count);
            workspace.Lattice.BeginSubset();
            var groups = input.Groups;

            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                if (group == null || !group.Enabled) continue;
                if (group.BlendShapeOutput == BlendShapeOutputMode.OutputAsBlendShape) continue;

                var groupVertices = workspace.SubsetGroup;
                Array.Copy(source, groupVertices, count);
                var layers = group.SerializedLayers;
                for (int l = 0; l < layers.Count; l++)
                {
                    var layer = layers[l];
                    if (layer == null || !layer.Enabled || layer.Weight <= 0f) continue;
                    if (!input.Semantics.PublishedBlendShapeSemantics &&
                        layer.BlendShapeOutput == BlendShapeOutputMode.OutputAsBlendShape) continue;

                    if (layer.Type == MeshDeformerLayerType.Brush)
                        BrushEvaluator.ApplySubset(layer, vertexCount, indices, count, groupVertices);
                    else
                        workspace.Lattice.ApplySubset(layer.SerializedSettings, layer.Weight, input.Semantics,
                            source, count, groupVertices);
                }

                for (int i = 0; i < count; i++)
                    directDeltas[i] += groupVertices[i] - source[i];
            }

            for (int i = 0; i < count; i++)
                compactOutput[i] = source[i] + directDeltas[i];
        }

        private static void ApplyLayer(LatticeLayer layer, Vector3[] sourceVertices, Vector3[] outputVertices,
            in EvaluationSemantics semantics, EvaluationWorkspace workspace)
        {
            if (layer.Type == MeshDeformerLayerType.Brush)
                BrushEvaluator.Apply(layer, sourceVertices, outputVertices);
            else
                workspace.Lattice.Apply(layer.SerializedSettings, layer.Weight, semantics, sourceVertices, outputVertices);
        }
    }
}
