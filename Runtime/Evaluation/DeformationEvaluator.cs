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
                            candidateWeights));
                    }
                    else if (!stagedGroupOutput &&
                             DeformationEvaluationMath.TryBuildDeltas(sourceVertices, groupVertices, out var groupDeltas))
                    {
                        generatedBlendShapes.Add(new GeneratedBlendShapeOutput(
                            group.EffectiveBlendShapeName(input.DefaultOutputName),
                            group.SerializedBlendShapeCurve,
                            groupDeltas));
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
