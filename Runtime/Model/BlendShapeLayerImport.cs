using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    /// <summary>Prepares independent authoring payloads without touching a component or source mesh.</summary>
    internal static class BlendShapeLayerImport
    {
        internal static LatticeLayer CreateLayer(Mesh source, int blendShapeIndex, int frameIndex)
        {
            if (source == null) return null;
            int shapeCount = source.blendShapeCount;
            if (blendShapeIndex < 0 || blendShapeIndex >= shapeCount) return null;
            int frameCount = source.GetBlendShapeFrameCount(blendShapeIndex);
            if (frameIndex < 0 || frameIndex >= frameCount) return null;
            int vertexCount = source.vertexCount;
            if (vertexCount == 0) return null;

            var deltaVertices = new Vector3[vertexCount];
            var deltaNormals = new Vector3[vertexCount];
            var deltaTangents = new Vector3[vertexCount];
            source.GetBlendShapeFrameVertices(blendShapeIndex, frameIndex, deltaVertices, deltaNormals, deltaTangents);

            string shapeName = source.GetBlendShapeName(blendShapeIndex);
            var layer = new LatticeLayer();
            layer.Name = shapeName;
            layer.SetType(MeshDeformerLayerType.Brush);
            layer.Weight = 1f;
            layer.EnsureBrushDisplacementCapacity(vertexCount);
            for (int i = 0; i < vertexCount; i++)
                layer.SetBrushDisplacement(i, deltaVertices[i]);

            return layer;
        }

        internal static DeformerGroup CreateGroup(Mesh source, int blendShapeIndex)
        {
            if (source == null) return null;
            if (blendShapeIndex < 0 || blendShapeIndex >= source.blendShapeCount) return null;

            int frameCount = source.GetBlendShapeFrameCount(blendShapeIndex);
            int vertexCount = source.vertexCount;
            if (frameCount <= 0 || vertexCount <= 0) return null;

            string shapeName = source.GetBlendShapeName(blendShapeIndex);
            var importedLayers = new List<LatticeLayer>(frameCount);
            float previousWeight = float.NegativeInfinity;

            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                float frameWeight = source.GetBlendShapeFrameWeight(blendShapeIndex, frameIndex);
                if (float.IsNaN(frameWeight) || float.IsInfinity(frameWeight) || frameWeight <= previousWeight)
                    return null;

                var deltaVertices = new Vector3[vertexCount];
                source.GetBlendShapeFrameVertices(
                    blendShapeIndex,
                    frameIndex,
                    deltaVertices,
                    new Vector3[vertexCount],
                    new Vector3[vertexCount]);

                for (int vertex = 0; vertex < vertexCount; vertex++)
                {
                    Vector3 delta = deltaVertices[vertex];
                    if (float.IsNaN(delta.x) || float.IsInfinity(delta.x) ||
                        float.IsNaN(delta.y) || float.IsInfinity(delta.y) ||
                        float.IsNaN(delta.z) || float.IsInfinity(delta.z))
                    {
                        return null;
                    }
                }

                var layer = new LatticeLayer
                {
                    Name = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} [{1:0.###}]",
                        shapeName,
                        frameWeight),
                    Weight = 1f
                };
                layer.SetType(MeshDeformerLayerType.Brush);
                layer.BrushDisplacements = deltaVertices;
                layer.SetImportedBlendShapeFrameWeight(frameWeight);
                importedLayers.Add(layer);
                previousWeight = frameWeight;
            }

            var group = new DeformerGroup
            {
                Name = shapeName + " Imported",
                BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape,
                BlendShapeName = shapeName + " Imported",
                BlendShapeComposition = BlendShapeCompositionMode.Crossfade,
                BlendShapeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f)
            };
            group.LayersList.AddRange(importedLayers);
            group.ActiveLayerIndex = 0;

            return group;
        }
    }
}
