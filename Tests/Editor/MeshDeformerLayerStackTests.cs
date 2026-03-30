#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class MeshDeformerLayerStackTests
    {
        private const float Epsilon = 1e-4f;
        private static readonly BindingFlags s_privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void DefaultLayer_IsCreatedAsLatticeLayer()
        {
            var fixture = CreateFixture("DefaultLayer_IsCreatedAsLatticeLayer");
            try
            {
                var deformer = fixture.Deformer;
                Assert.That(deformer.Layers.Count, Is.GreaterThanOrEqualTo(1));
                Assert.That(deformer.ActiveLayerIndex, Is.EqualTo(0));
                Assert.That(deformer.Layers[0].Name, Is.EqualTo("Lattice Layer"));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void AddLayer_CreatesNeutralLayerWithoutChangingActiveLayerSettings()
        {
            var fixture = CreateFixture("AddLayer_CreatesNeutralLayerWithoutChangingActiveLayerSettings");
            try
            {
                var deformer = fixture.Deformer;
                var primary = deformer.Layers[0].Settings;
                var primarySnapshot = primary.ControlPointsLocal.ToArray();

                int layerIndex = deformer.AddLayer("Layer A");

                Assert.That(layerIndex, Is.EqualTo(1));
                Assert.That(deformer.Layers.Count, Is.EqualTo(2));

                var layerSettings = deformer.Layers[1].Settings;
                Assert.That(layerSettings, Is.Not.SameAs(primary));
                Assert.That(layerSettings.GridSize, Is.EqualTo(primary.GridSize));
                Assert.That(layerSettings.Interpolation, Is.EqualTo(primary.Interpolation));
                AssertApproximately(primary.LocalBounds.center, layerSettings.LocalBounds.center);
                AssertApproximately(primary.LocalBounds.size, layerSettings.LocalBounds.size);

                for (int i = 0; i < primary.ControlPointCount; i++)
                {
                    Vector3 neutral = GetNeutralControlPoint(primary.LocalBounds, primary.GridSize, i);
                    AssertApproximately(neutral, layerSettings.GetControlPointLocal(i), Epsilon);
                    AssertApproximately(primarySnapshot[i], primary.GetControlPointLocal(i), Epsilon);
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void LayerWeight_OffsetsVerticesFromNeutralDelta()
        {
            var fixture = CreateFixture("LayerWeight_OffsetsVerticesFromNeutralDelta");
            try
            {
                var deformer = fixture.Deformer;
                var layerSettings = deformer.Layers[0].Settings;
                var layer = deformer.Layers[0];

                const int controlPointIndex = 0;
                var delta = new Vector3(0f, 0.2f, 0f);
                var original = layerSettings.GetControlPointLocal(controlPointIndex);
                layerSettings.SetControlPointLocal(controlPointIndex, original + delta);
                layer.Weight = 0.5f;

                var runtimeMesh = deformer.Deform(false);
                Assert.That(runtimeMesh, Is.Not.Null);
                Assert.That(deformer.SourceMesh, Is.Not.Null);

                var sourceVertices = deformer.SourceMesh.vertices;
                var deformedVertices = runtimeMesh.vertices;

                int minCornerIndex = FindCornerVertexIndex(sourceVertices, layerSettings.LocalBounds.min);
                int maxCornerIndex = FindCornerVertexIndex(sourceVertices, layerSettings.LocalBounds.max);

                Assert.That(minCornerIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(maxCornerIndex, Is.GreaterThanOrEqualTo(0));

                var expectedMoved = sourceVertices[minCornerIndex] + delta * layer.Weight;
                AssertApproximately(expectedMoved, deformedVertices[minCornerIndex], 2e-3f);
                AssertApproximately(sourceVertices[maxCornerIndex], deformedVertices[maxCornerIndex], 2e-3f);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void Layers_CanUseIndependentGridSettings()
        {
            var fixture = CreateFixture("Layers_CanUseIndependentGridSettings");
            try
            {
                var deformer = fixture.Deformer;
                deformer.AddLayer("Layer A");

                var layer0 = deformer.Layers[0].Settings;
                var layer1 = deformer.Layers[1].Settings;

                var layer0Grid = layer0.GridSize;
                var layer1Grid = new Vector3Int(4, 3, 2);
                layer1.ResizeGrid(layer1Grid);

                Assert.That(layer0.GridSize, Is.EqualTo(layer0Grid));
                Assert.That(layer1.GridSize, Is.EqualTo(layer1Grid));

                var runtimeMesh = deformer.Deform(false);
                Assert.That(runtimeMesh, Is.Not.Null);
                Assert.That(runtimeMesh.vertexCount, Is.EqualTo(deformer.SourceMesh.vertexCount));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void BrushLayer_AppliesWeightedVertexDisplacement()
        {
            var fixture = CreateFixture("BrushLayer_AppliesWeightedVertexDisplacement");
            try
            {
                var deformer = fixture.Deformer;
                int brushLayerIndex = deformer.AddLayer("Brush Layer", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushLayerIndex;
                deformer.EnsureDisplacementCapacity();

                Assert.That(deformer.DisplacementCount, Is.EqualTo(deformer.SourceMesh.vertexCount));

                const int vertexIndex = 0;
                var brushDelta = new Vector3(0.12f, -0.04f, 0.03f);
                deformer.SetDisplacement(vertexIndex, brushDelta);
                deformer.Layers[brushLayerIndex].Weight = 0.25f;

                var runtimeMesh = deformer.Deform(false);
                Assert.That(runtimeMesh, Is.Not.Null);

                var sourceVertices = deformer.SourceMesh.vertices;
                var deformedVertices = runtimeMesh.vertices;
                var expected = sourceVertices[vertexIndex] + brushDelta * 0.25f;
                AssertApproximately(expected, deformedVertices[vertexIndex], 2e-3f);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void LatticeAndBrushLayers_AreComposed()
        {
            var fixture = CreateFixture("LatticeAndBrushLayers_AreComposed");
            try
            {
                var deformer = fixture.Deformer;
                var latticeLayer = deformer.Layers[0];
                var latticeSettings = latticeLayer.Settings;

                const int latticeControlIndex = 0;
                var latticeDelta = new Vector3(0f, 0.18f, 0f);
                latticeSettings.SetControlPointLocal(
                    latticeControlIndex,
                    latticeSettings.GetControlPointLocal(latticeControlIndex) + latticeDelta);

                int brushLayerIndex = deformer.AddLayer("Brush Layer", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushLayerIndex;
                deformer.EnsureDisplacementCapacity();
                deformer.Layers[brushLayerIndex].Weight = 0.5f;

                var sourceVertices = deformer.SourceMesh.vertices;
                int minCornerIndex = FindCornerVertexIndex(sourceVertices, latticeSettings.LocalBounds.min);
                Assert.That(minCornerIndex, Is.GreaterThanOrEqualTo(0));

                var brushDelta = new Vector3(0.1f, 0f, 0f);
                deformer.SetDisplacement(minCornerIndex, brushDelta);

                var runtimeMesh = deformer.Deform(false);
                Assert.That(runtimeMesh, Is.Not.Null);

                var deformedVertices = runtimeMesh.vertices;
                var expected = sourceVertices[minCornerIndex] + latticeDelta + brushDelta * 0.5f;
                AssertApproximately(expected, deformedVertices[minCornerIndex], 2e-3f);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void BlendShapeOutput_ProducesCorrectDeltaFrames()
        {
            var fixture = CreateFixture("BlendShapeOutput_ProducesCorrectDeltaFrames");
            try
            {
                var deformer = fixture.Deformer;

                int brushLayerIndex = deformer.AddLayer("Brush Layer", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushLayerIndex;
                deformer.EnsureDisplacementCapacity();

                var sourceVertices = deformer.SourceMesh.vertices;
                int vertexCount = sourceVertices.Length;
                var brushDelta = new Vector3(0.1f, 0.2f, -0.05f);
                const float weight = 0.75f;

                deformer.SetDisplacement(0, brushDelta);
                deformer.SetDisplacement(1, brushDelta * 2f);

                var layer = deformer.Layers[brushLayerIndex];
                layer.Weight = weight;

                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "TestShape";

                // Release cached runtime mesh so Deform creates a fresh one
                ReleaseRuntimeMesh(deformer);

                var runtimeMesh = deformer.Deform(false);
                Assert.That(runtimeMesh, Is.Not.Null);

                int sourceBlendShapeCount = deformer.SourceMesh.blendShapeCount;
                Assert.That(runtimeMesh.blendShapeCount, Is.EqualTo(sourceBlendShapeCount + 1));

                int shapeIndex = runtimeMesh.blendShapeCount - 1;
                Assert.That(runtimeMesh.GetBlendShapeName(shapeIndex), Is.EqualTo("TestShape"));

                var frameDeltas = new Vector3[vertexCount];
                var frameNormals = new Vector3[vertexCount];
                var frameTangents = new Vector3[vertexCount];
                int frameCount = runtimeMesh.GetBlendShapeFrameCount(shapeIndex);
                runtimeMesh.GetBlendShapeFrameVertices(shapeIndex, frameCount - 1, frameDeltas, frameNormals, frameTangents);

                AssertApproximately(brushDelta * weight, frameDeltas[0], 2e-3f);
                AssertApproximately(brushDelta * 2f * weight, frameDeltas[1], 2e-3f);
                AssertApproximately(Vector3.zero, frameDeltas[2], 2e-3f);

                // Vertices should NOT be directly modified by a BlendShape output layer
                var deformedVertices = runtimeMesh.vertices;
                AssertApproximately(sourceVertices[0], deformedVertices[0], 2e-3f);
                AssertApproximately(sourceVertices[1], deformedVertices[1], 2e-3f);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void BlendShapeOutput_DisabledMode_AppliesDirectly()
        {
            var fixture = CreateFixture("BlendShapeOutput_DisabledMode_AppliesDirectly");
            try
            {
                var deformer = fixture.Deformer;

                int brushLayerIndex = deformer.AddLayer("Brush Layer", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushLayerIndex;
                deformer.EnsureDisplacementCapacity();

                var brushDelta = new Vector3(0.15f, -0.08f, 0.04f);
                const float weight = 0.5f;
                deformer.SetDisplacement(0, brushDelta);

                var layer = deformer.Layers[brushLayerIndex];
                layer.Weight = weight;

                deformer.BlendShapeOutput = BlendShapeOutputMode.Disabled;

                // Release cached runtime mesh so Deform creates a fresh one
                ReleaseRuntimeMesh(deformer);

                var runtimeMesh = deformer.Deform(false);
                Assert.That(runtimeMesh, Is.Not.Null);

                int sourceBlendShapeCount = deformer.SourceMesh.blendShapeCount;
                Assert.That(runtimeMesh.blendShapeCount, Is.EqualTo(sourceBlendShapeCount),
                    "BlendShape count should not increase when output mode is Disabled");

                var sourceVertices = deformer.SourceMesh.vertices;
                var deformedVertices = runtimeMesh.vertices;
                var expected = sourceVertices[0] + brushDelta * weight;
                AssertApproximately(expected, deformedVertices[0], 2e-3f);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void ImportBlendShapeAsLayer_CreatesMatchingBrushLayer()
        {
            var fixture = CreateFixtureWithBlendShapes(
                "ImportBlendShapeAsLayer_CreatesMatchingBrushLayer",
                new[] { "ShapeA" });
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;

                // Retrieve the original deltas from the source mesh
                var expectedDeltas = new Vector3[vertexCount];
                var tempNormals = new Vector3[vertexCount];
                var tempTangents = new Vector3[vertexCount];
                deformer.SourceMesh.GetBlendShapeFrameVertices(0, 0, expectedDeltas, tempNormals, tempTangents);

                int layerCountBefore = deformer.Layers.Count;
                int newLayerIndex = deformer.ImportBlendShapeAsLayer(0);

                Assert.That(newLayerIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(deformer.Layers.Count, Is.EqualTo(layerCountBefore + 1));

                var newLayer = deformer.Layers[newLayerIndex];
                Assert.That(newLayer.Name, Is.EqualTo("ShapeA"));
                Assert.That(newLayer.Type, Is.EqualTo(MeshDeformerLayerType.Brush));
                Assert.That(newLayer.Weight, Is.EqualTo(1f));
                Assert.That(newLayer.BrushDisplacementCount, Is.EqualTo(vertexCount));

                for (int i = 0; i < vertexCount; i++)
                {
                    AssertApproximately(expectedDeltas[i], newLayer.GetBrushDisplacement(i), Epsilon);
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void GetSourceBlendShapeNames_ReturnsCorrectNames()
        {
            var shapeNames = new[] { "Smile", "Blink", "Pout" };
            var fixture = CreateFixtureWithBlendShapes(
                "GetSourceBlendShapeNames_ReturnsCorrectNames",
                shapeNames);
            try
            {
                var deformer = fixture.Deformer;
                var result = deformer.GetSourceBlendShapeNames();

                Assert.That(result, Is.Not.Null);
                Assert.That(result.Length, Is.EqualTo(shapeNames.Length));
                for (int i = 0; i < shapeNames.Length; i++)
                {
                    Assert.That(result[i], Is.EqualTo(shapeNames[i]));
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void LegacyBaseData_IsMigratedToLatticeLayer_OnEnable()
        {
            var fixture = CreateFixture("LegacyBaseData_IsMigratedToLatticeLayer_OnEnable");
            try
            {
                var deformer = fixture.Deformer;
                var legacySettings = CloneSettings(deformer.Settings);
                const int controlPointIndex = 0;
                var legacyPoint = legacySettings.GetControlPointLocal(controlPointIndex) + new Vector3(0.05f, 0.1f, 0f);
                legacySettings.SetControlPointLocal(controlPointIndex, legacyPoint);

                SetPrivateField(deformer, "_settings", legacySettings);
                SetPrivateField(deformer, "_layers", new List<LatticeLayer>());
                SetPrivateField(deformer, "_groups", new List<DeformerGroup>());
                SetPrivateField(deformer, "_activeLayerIndex", -1);
                SetPrivateField(deformer, "_layerModelVersion", 0);

                deformer.enabled = false;
                deformer.enabled = true;

                Assert.That(deformer.Layers.Count, Is.EqualTo(1));
                Assert.That(deformer.ActiveLayerIndex, Is.EqualTo(0));
                Assert.That(deformer.Layers[0].Name, Is.EqualTo("Lattice Layer"));
                AssertApproximately(legacyPoint, deformer.Layers[0].Settings.GetControlPointLocal(controlPointIndex), Epsilon);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void SplitLayerByAxis_ZerosCorrectSide()
        {
            var fixture = CreateFixture("SplitLayerByAxis_ZerosCorrectSide");
            try
            {
                var deformer = fixture.Deformer;
                int brushLayerIndex = deformer.AddLayer("Brush Layer", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushLayerIndex;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[brushLayerIndex];
                var vertices = deformer.SourceMesh.vertices;
                int vertexCount = vertices.Length;

                // Set displacement on every vertex
                var displacement = new Vector3(0.1f, 0.2f, -0.05f);
                for (int i = 0; i < vertexCount; i++)
                {
                    deformer.SetDisplacement(i, displacement);
                }

                // Split: keep positive X side
                deformer.SplitLayerByAxis(brushLayerIndex, 0, true);

                for (int i = 0; i < vertexCount; i++)
                {
                    var d = layer.GetBrushDisplacement(i);
                    if (vertices[i].x >= 0f)
                    {
                        // Positive side should retain displacement
                        AssertApproximately(displacement, d, Epsilon);
                    }
                    else
                    {
                        // Negative side should be zeroed
                        AssertApproximately(Vector3.zero, d, Epsilon);
                    }
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void FlipLayerByAxis_SwapsMirroredVertices()
        {
            var fixture = CreateFixtureWithSymmetricMesh("FlipLayerByAxis_SwapsMirroredVertices");
            try
            {
                var deformer = fixture.Deformer;
                int brushLayerIndex = deformer.AddLayer("Brush Layer", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushLayerIndex;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[brushLayerIndex];
                var vertices = deformer.SourceMesh.vertices;

                // The symmetric mesh has:
                //   vertex 0 at ( 1, 0, 0)
                //   vertex 1 at (-1, 0, 0)
                //   vertex 2 at ( 0, 1, 0)
                //   vertex 3 at ( 0,-1, 0)
                // Set displacement only on vertex 0 (positive X side)
                var displacementA = new Vector3(0.3f, 0.1f, -0.05f);
                layer.SetBrushDisplacement(0, displacementA);

                // Flip X axis
                deformer.FlipLayerByAxis(brushLayerIndex, 0);

                // After flip, vertex 0's displacement should have moved to vertex 1
                // with X component negated
                var expectedAtMirror = new Vector3(-displacementA.x, displacementA.y, displacementA.z);
                AssertApproximately(expectedAtMirror, layer.GetBrushDisplacement(1), Epsilon);

                // Vertex 0 originally had displacement, but its mirror (vertex 1) had zero,
                // so vertex 0 should now have the negated-X version of zero = zero
                AssertApproximately(Vector3.zero, layer.GetBrushDisplacement(0), Epsilon);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void VertexMask_BlocksBrushApplication()
        {
            var fixture = CreateFixture("VertexMask_BlocksBrushApplication");
            try
            {
                var deformer = fixture.Deformer;
                int brushLayerIndex = deformer.AddLayer("Brush Layer", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushLayerIndex;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[brushLayerIndex];
                var sourceVertices = deformer.SourceMesh.vertices;
                int vertexCount = sourceVertices.Length;

                // Set uniform displacement on all vertices
                var displacement = new Vector3(0f, 0.5f, 0f);
                for (int i = 0; i < vertexCount; i++)
                {
                    deformer.SetDisplacement(i, displacement);
                }

                // Set up vertex mask: even indices protected (0), odd indices editable (1)
                const float weight = 0.8f;
                layer.Weight = weight;
                layer.EnsureVertexMaskCapacity(vertexCount);
                for (int i = 0; i < vertexCount; i++)
                {
                    layer.SetVertexMask(i, i % 2 == 0 ? 0f : 1f);
                }

                var runtimeMesh = deformer.Deform(false);
                Assert.That(runtimeMesh, Is.Not.Null);

                var deformedVertices = runtimeMesh.vertices;
                for (int i = 0; i < vertexCount; i++)
                {
                    if (i % 2 == 0)
                    {
                        // Protected vertices (mask=0) should NOT be displaced
                        AssertApproximately(sourceVertices[i], deformedVertices[i], 2e-3f);
                    }
                    else
                    {
                        // Editable vertices (mask=1) should be displaced by displacement * weight
                        var expected = sourceVertices[i] + displacement * weight;
                        AssertApproximately(expected, deformedVertices[i], 2e-3f);
                    }
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void VertexMask_PartialMask_ScalesDisplacement()
        {
            var fixture = CreateFixture("VertexMask_PartialMask_ScalesDisplacement");
            try
            {
                var deformer = fixture.Deformer;
                int brushLayerIndex = deformer.AddLayer("Brush Layer", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushLayerIndex;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[brushLayerIndex];
                var sourceVertices = deformer.SourceMesh.vertices;
                int vertexCount = sourceVertices.Length;

                // Set displacement on all vertices
                var displacement = new Vector3(0.2f, 0.4f, -0.1f);
                for (int i = 0; i < vertexCount; i++)
                {
                    deformer.SetDisplacement(i, displacement);
                }

                // Set vertex mask to 0.5 (half protection) on all vertices
                const float weight = 1f;
                const float maskValue = 0.5f;
                layer.Weight = weight;
                layer.EnsureVertexMaskCapacity(vertexCount);
                for (int i = 0; i < vertexCount; i++)
                {
                    layer.SetVertexMask(i, maskValue);
                }

                var runtimeMesh = deformer.Deform(false);
                Assert.That(runtimeMesh, Is.Not.Null);

                var deformedVertices = runtimeMesh.vertices;
                for (int i = 0; i < vertexCount; i++)
                {
                    // Half-masked vertices should be displaced by displacement * weight * 0.5
                    var expected = sourceVertices[i] + displacement * weight * maskValue;
                    AssertApproximately(expected, deformedVertices[i], 2e-3f);
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void GeodesicDistanceCalculator_ComputesCorrectDistances()
        {
            // Linear chain: v0 -- v1 -- v2 -- v3 -- v4
            // Each edge has length 1 (vertices spaced 1 unit apart on X axis)
            var vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(2f, 0f, 0f),
                new Vector3(3f, 0f, 0f),
                new Vector3(4f, 0f, 0f)
            };

            var adjacency = new List<HashSet<int>>
            {
                new HashSet<int> { 1 },          // v0 connects to v1
                new HashSet<int> { 0, 2 },       // v1 connects to v0, v2
                new HashSet<int> { 1, 3 },       // v2 connects to v1, v3
                new HashSet<int> { 2, 4 },       // v3 connects to v2, v4
                new HashSet<int> { 3 }            // v4 connects to v3
            };

            var distances = GeodesicDistanceCalculator.ComputeDistances(0, 3.5f, adjacency, vertices);

            // v0..v3 should be reachable (distances 0, 1, 2, 3)
            Assert.That(distances.ContainsKey(0), Is.True, "Start vertex should be in results");
            Assert.That(distances[0], Is.EqualTo(0f).Within(Epsilon));
            Assert.That(distances.ContainsKey(1), Is.True, "v1 should be reachable");
            Assert.That(distances[1], Is.EqualTo(1f).Within(Epsilon));
            Assert.That(distances.ContainsKey(2), Is.True, "v2 should be reachable");
            Assert.That(distances[2], Is.EqualTo(2f).Within(Epsilon));
            Assert.That(distances.ContainsKey(3), Is.True, "v3 should be reachable");
            Assert.That(distances[3], Is.EqualTo(3f).Within(Epsilon));

            // v4 is at distance 4, which exceeds maxDistance 3.5 -- should NOT be included
            Assert.That(distances.ContainsKey(4), Is.False,
                "v4 at distance 4 should not be included (maxDistance = 3.5)");
        }

        // ========================================================================
        // Layer Robustness
        // ========================================================================

        [Test]
        public void RemoveLayer_RemainingLayersStillDeformCorrectly()
        {
            var fixture = CreateFixture("RemoveLayer_RemainingLayersStillDeformCorrectly");
            try
            {
                var deformer = fixture.Deformer;

                // Add two brush layers with different displacements
                int layerA = deformer.AddLayer("Layer A", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = layerA;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.1f, 0f, 0f));

                int layerB = deformer.AddLayer("Layer B", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = layerB;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0f, 0.2f, 0f));

                // Remove Layer A
                deformer.RemoveLayer(layerA);

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);
                var sourceVertices = deformer.SourceMesh.vertices;
                var deformedVertices = runtimeMesh.vertices;

                // Only Layer B's displacement should remain
                var expected = sourceVertices[0] + new Vector3(0f, 0.2f, 0f);
                AssertApproximately(expected, deformedVertices[0], 2e-3f);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void MoveLayer_DoesNotChangeAdditiveResult()
        {
            var fixture = CreateFixture("MoveLayer_DoesNotChangeAdditiveResult");
            try
            {
                var deformer = fixture.Deformer;

                int layerA = deformer.AddLayer("Layer A", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = layerA;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.1f, 0f, 0f));

                int layerB = deformer.AddLayer("Layer B", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = layerB;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0f, 0.3f, 0f));

                ReleaseRuntimeMesh(deformer);
                var meshBefore = deformer.Deform(false);
                var verticesBefore = meshBefore.vertices[0];

                // Swap layer order
                deformer.MoveLayer(2, 1);

                ReleaseRuntimeMesh(deformer);
                var meshAfter = deformer.Deform(false);
                var verticesAfter = meshAfter.vertices[0];

                AssertApproximately(verticesBefore, verticesAfter, 2e-3f);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void ZeroWeightLayer_ContributesNothing()
        {
            var fixture = CreateFixture("ZeroWeightLayer_ContributesNothing");
            try
            {
                var deformer = fixture.Deformer;

                int brushLayerIndex = deformer.AddLayer("Zero Weight", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushLayerIndex;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(99f, 99f, 99f));
                deformer.Layers[brushLayerIndex].Weight = 0f;

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);
                var sourceVertices = deformer.SourceMesh.vertices;
                var deformedVertices = runtimeMesh.vertices;

                AssertApproximately(sourceVertices[0], deformedVertices[0], 2e-3f);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void AllLayersDisabled_MeshUnchanged()
        {
            var fixture = CreateFixture("AllLayersDisabled_MeshUnchanged");
            try
            {
                var deformer = fixture.Deformer;

                // Add a brush layer with big displacement
                int brushLayerIndex = deformer.AddLayer("Disabled", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushLayerIndex;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(1f, 1f, 1f));
                deformer.Layers[brushLayerIndex].Enabled = false;

                // Also disable the default lattice layer
                deformer.Layers[0].Enabled = false;

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);
                var sourceVertices = deformer.SourceMesh.vertices;
                var deformedVertices = runtimeMesh.vertices;

                for (int i = 0; i < sourceVertices.Length; i++)
                {
                    AssertApproximately(sourceVertices[i], deformedVertices[i], 2e-3f);
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        // ========================================================================
        // BlendShape Round-Trip
        // ========================================================================

        [Test]
        public void ImportThenOutput_BlendShapeRoundTrip()
        {
            var fixture = CreateFixtureWithBlendShapes(
                "ImportThenOutput_BlendShapeRoundTrip",
                new[] { "OriginalShape" });
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;

                // Get the original deltas
                var originalDeltas = new Vector3[vertexCount];
                deformer.SourceMesh.GetBlendShapeFrameVertices(0, 0, originalDeltas, null, null);

                // Import as brush layer
                int importedIndex = deformer.ImportBlendShapeAsLayer(0);
                var importedLayer = deformer.Layers[importedIndex];

                // Add extra displacement on top
                var extraDelta = new Vector3(0.05f, 0f, 0f);
                importedLayer.AddBrushDisplacement(0, extraDelta);

                // Output as BlendShape (component-level)
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "ModifiedShape";

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);

                // Find the output BlendShape
                int outputIndex = -1;
                for (int i = 0; i < runtimeMesh.blendShapeCount; i++)
                {
                    if (runtimeMesh.GetBlendShapeName(i) == "ModifiedShape")
                    {
                        outputIndex = i;
                        break;
                    }
                }

                Assert.That(outputIndex, Is.GreaterThanOrEqualTo(0), "Output BlendShape not found");

                var outputDeltas = new Vector3[vertexCount];
                int outputFrameCount = runtimeMesh.GetBlendShapeFrameCount(outputIndex);
                runtimeMesh.GetBlendShapeFrameVertices(outputIndex, outputFrameCount - 1, outputDeltas, null, null);

                // Vertex 0: original delta + extra delta (weight=1)
                AssertApproximately(originalDeltas[0] + extraDelta, outputDeltas[0], 2e-3f);
                // Vertex 1: original delta only
                AssertApproximately(originalDeltas[1], outputDeltas[1], 2e-3f);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void MultipleBlendShapeLayers_ProduceCombinedFrame()
        {
            var fixture = CreateFixture("MultipleBlendShapeLayers_ProduceCombinedFrame");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;

                // Layer A
                int layerA = deformer.AddLayer("ShapeA", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = layerA;
                deformer.EnsureDisplacementCapacity();
                var deltaA = new Vector3(0.1f, 0f, 0f);
                deformer.SetDisplacement(0, deltaA);

                // Layer B
                int layerB = deformer.AddLayer("ShapeB", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = layerB;
                deformer.EnsureDisplacementCapacity();
                var deltaB = new Vector3(0f, 0.2f, 0f);
                deformer.SetDisplacement(0, deltaB);

                // Component-level BlendShape output — all layers combine into one
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "CombinedShape";

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);

                int sourceCount = deformer.SourceMesh.blendShapeCount;
                Assert.That(runtimeMesh.blendShapeCount, Is.EqualTo(sourceCount + 1),
                    "Should have 1 combined BlendShape");

                // Verify the combined BlendShape has the sum of both layers' deltas
                var frameCombined = new Vector3[vertexCount];
                int combinedFrameCount = runtimeMesh.GetBlendShapeFrameCount(sourceCount);
                runtimeMesh.GetBlendShapeFrameVertices(sourceCount, combinedFrameCount - 1, frameCombined, null, null);

                AssertApproximately(deltaA + deltaB, frameCombined[0], 2e-3f);
                AssertApproximately(Vector3.zero, frameCombined[1], 2e-3f);

                // Vertices should NOT be directly modified
                var deformedVertices = runtimeMesh.vertices;
                var sourceVertices = deformer.SourceMesh.vertices;
                AssertApproximately(sourceVertices[0], deformedVertices[0], 2e-3f);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void BlendShapeName_FallsBackToGameObjectName()
        {
            var fixture = CreateFixture("BlendShapeName_FallsBackToGameObjectName");
            try
            {
                var deformer = fixture.Deformer;

                int layerIndex = deformer.AddLayer("MyLayerName", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = layerIndex;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.1f, 0f, 0f));

                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = ""; // Empty — should fall back to gameObject.name

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);

                int lastIndex = runtimeMesh.blendShapeCount - 1;
                Assert.That(runtimeMesh.GetBlendShapeName(lastIndex),
                    Is.EqualTo(deformer.gameObject.name));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void ExistingBlendShapes_PreservedAfterDeform()
        {
            var shapeNames = new[] { "Smile", "Blink" };
            var fixture = CreateFixtureWithBlendShapes(
                "ExistingBlendShapes_PreservedAfterDeform", shapeNames);
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;

                // Save original BlendShape deltas
                var originalSmile = new Vector3[vertexCount];
                var originalBlink = new Vector3[vertexCount];
                deformer.SourceMesh.GetBlendShapeFrameVertices(0, 0, originalSmile, null, null);
                deformer.SourceMesh.GetBlendShapeFrameVertices(1, 0, originalBlink, null, null);

                // Add a brush layer with some displacement
                int brushLayerIndex = deformer.AddLayer("Extra", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushLayerIndex;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.05f, 0f, 0f));

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);

                // Existing BlendShapes should still be present
                Assert.That(runtimeMesh.blendShapeCount, Is.GreaterThanOrEqualTo(2));
                Assert.That(runtimeMesh.GetBlendShapeName(0), Is.EqualTo("Smile"));
                Assert.That(runtimeMesh.GetBlendShapeName(1), Is.EqualTo("Blink"));

                // Verify deltas are preserved
                var runtimeSmile = new Vector3[vertexCount];
                var runtimeBlink = new Vector3[vertexCount];
                runtimeMesh.GetBlendShapeFrameVertices(0, 0, runtimeSmile, null, null);
                runtimeMesh.GetBlendShapeFrameVertices(1, 0, runtimeBlink, null, null);

                for (int i = 0; i < vertexCount; i++)
                {
                    AssertApproximately(originalSmile[i], runtimeSmile[i], Epsilon);
                    AssertApproximately(originalBlink[i], runtimeBlink[i], Epsilon);
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void Mask_ZerosBlendShapeDelta()
        {
            var fixture = CreateFixture("Mask_ZerosBlendShapeDelta");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;

                int brushLayerIndex = deformer.AddLayer("Masked BS", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushLayerIndex;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[brushLayerIndex];
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;

                // Set displacement on vertices 0 and 1
                var displacement = new Vector3(0.3f, 0.2f, 0.1f);
                deformer.SetDisplacement(0, displacement);
                deformer.SetDisplacement(1, displacement);

                // Mask vertex 0 (protected), leave vertex 1 editable
                layer.EnsureVertexMaskCapacity(vertexCount);
                layer.SetVertexMask(0, 0f);
                layer.SetVertexMask(1, 1f);

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);

                int shapeIndex = runtimeMesh.blendShapeCount - 1;
                var frameDeltas = new Vector3[vertexCount];
                int frameCount = runtimeMesh.GetBlendShapeFrameCount(shapeIndex);
                runtimeMesh.GetBlendShapeFrameVertices(shapeIndex, frameCount - 1, frameDeltas, null, null);

                // Vertex 0 (masked=0): delta should be zero
                AssertApproximately(Vector3.zero, frameDeltas[0], 2e-3f);
                // Vertex 1 (masked=1): delta should be full displacement
                AssertApproximately(displacement, frameDeltas[1], 2e-3f);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        // ========================================================================
        // Split / Flip Compound Operations
        // ========================================================================

        [Test]
        public void SplitL_ThenFlipX_PopulatesRightSide()
        {
            var fixture = CreateFixtureWithSymmetricMesh("SplitL_ThenFlipX_PopulatesRightSide");
            try
            {
                var deformer = fixture.Deformer;
                int brushLayerIndex = deformer.AddLayer("SplitFlip", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushLayerIndex;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[brushLayerIndex];

                // Set displacement on all vertices
                var disp = new Vector3(0.2f, 0.1f, 0f);
                for (int i = 0; i < deformer.SourceMesh.vertexCount; i++)
                {
                    layer.SetBrushDisplacement(i, disp);
                }

                // Split: keep negative X side (L side)
                deformer.SplitLayerByAxis(brushLayerIndex, 0, false);

                var vertices = deformer.SourceMesh.vertices;
                // Verify vertex 0 (x=1, positive) is zeroed, vertex 1 (x=-1, negative) retains
                AssertApproximately(Vector3.zero, layer.GetBrushDisplacement(0), Epsilon);
                AssertApproximately(disp, layer.GetBrushDisplacement(1), Epsilon);

                // Flip X: should move vertex 1's displacement to vertex 0 (mirrored)
                deformer.FlipLayerByAxis(brushLayerIndex, 0);

                var expectedFlipped = new Vector3(-disp.x, disp.y, disp.z);
                AssertApproximately(expectedFlipped, layer.GetBrushDisplacement(0), Epsilon);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void SplitLayerByAxis_LatticeLayer_ZerosCorrectSide()
        {
            var fixture = CreateFixture("SplitLayerByAxis_LatticeLayer_ZerosCorrectSide");
            try
            {
                var deformer = fixture.Deformer;
                var latticeLayer = deformer.Layers[0];
                var settings = latticeLayer.Settings;

                // Move all control points upward
                var delta = new Vector3(0f, 0.1f, 0f);
                for (int i = 0; i < settings.ControlPointCount; i++)
                {
                    settings.SetControlPointLocal(i, settings.GetControlPointLocal(i) + delta);
                }

                // Split: keep positive X side
                deformer.SplitLayerByAxis(0, 0, true);

                // Verify control points on negative X side are reset to neutral
                var grid = settings.GridSize;
                for (int i = 0; i < settings.ControlPointCount; i++)
                {
                    var neutral = GetNeutralControlPoint(settings.LocalBounds, grid, i);
                    var actual = settings.GetControlPointLocal(i);

                    if (neutral.x < settings.LocalBounds.center.x - Epsilon)
                    {
                        // Negative side: should be at neutral position
                        AssertApproximately(neutral, actual, 2e-3f);
                    }
                    else if (neutral.x > settings.LocalBounds.center.x + Epsilon)
                    {
                        // Positive side: should retain the delta
                        AssertApproximately(neutral + delta, actual, 2e-3f);
                    }
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void FlipLayerByAxis_LatticeLayer_MirrorsControlPoints()
        {
            var fixture = CreateFixture("FlipLayerByAxis_LatticeLayer_MirrorsControlPoints");
            try
            {
                var deformer = fixture.Deformer;
                var latticeLayer = deformer.Layers[0];
                var settings = latticeLayer.Settings;

                // Move only control points on the positive X side
                var grid = settings.GridSize;
                var delta = new Vector3(0f, 0.15f, 0f);
                var neutralPositions = new Vector3[settings.ControlPointCount];
                for (int i = 0; i < settings.ControlPointCount; i++)
                {
                    neutralPositions[i] = GetNeutralControlPoint(settings.LocalBounds, grid, i);
                    if (neutralPositions[i].x > settings.LocalBounds.center.x + Epsilon)
                    {
                        settings.SetControlPointLocal(i, settings.GetControlPointLocal(i) + delta);
                    }
                }

                // Flip X
                deformer.FlipLayerByAxis(0, 0);

                // After flip, positive side should be back to neutral, and negative side
                // should have an offset with X negated
                // (The exact behavior depends on implementation; verify that a swap happened)
                bool anyNegativeSideMoved = false;
                for (int i = 0; i < settings.ControlPointCount; i++)
                {
                    var neutral = neutralPositions[i];
                    var actual = settings.GetControlPointLocal(i);
                    var offset = actual - neutral;
                    if (neutral.x < settings.LocalBounds.center.x - Epsilon && offset.sqrMagnitude > Epsilon)
                    {
                        anyNegativeSideMoved = true;
                    }
                }

                Assert.That(anyNegativeSideMoved, Is.True,
                    "Negative X control points should have received displacement from positive side");
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void SplitLayerByAxis_YAxis_ZerosCorrectSide()
        {
            var fixture = CreateFixture("SplitLayerByAxis_YAxis_ZerosCorrectSide");
            try
            {
                var deformer = fixture.Deformer;
                int brushLayerIndex = deformer.AddLayer("YSplit", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushLayerIndex;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[brushLayerIndex];
                var vertices = deformer.SourceMesh.vertices;
                var displacement = new Vector3(0.1f, 0.1f, 0.1f);
                for (int i = 0; i < vertices.Length; i++)
                {
                    deformer.SetDisplacement(i, displacement);
                }

                // Split: keep positive Y side
                deformer.SplitLayerByAxis(brushLayerIndex, 1, true);

                for (int i = 0; i < vertices.Length; i++)
                {
                    var d = layer.GetBrushDisplacement(i);
                    if (vertices[i].y >= 0f)
                    {
                        AssertApproximately(displacement, d, Epsilon);
                    }
                    else
                    {
                        AssertApproximately(Vector3.zero, d, Epsilon);
                    }
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        // ========================================================================
        // Mask Interactions
        // ========================================================================

        [Test]
        public void Mask_DoesNotAffectLatticeLayer()
        {
            var fixture = CreateFixture("Mask_DoesNotAffectLatticeLayer");
            try
            {
                var deformer = fixture.Deformer;
                var latticeLayer = deformer.Layers[0];
                var settings = latticeLayer.Settings;

                // Move a control point
                var delta = new Vector3(0f, 0.2f, 0f);
                settings.SetControlPointLocal(0, settings.GetControlPointLocal(0) + delta);

                // Set mask to fully protected on all vertices
                int vertexCount = deformer.SourceMesh.vertexCount;
                latticeLayer.EnsureVertexMaskCapacity(vertexCount);
                for (int i = 0; i < vertexCount; i++)
                {
                    latticeLayer.SetVertexMask(i, 0f);
                }

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);
                var sourceVertices = deformer.SourceMesh.vertices;
                var deformedVertices = runtimeMesh.vertices;

                // Lattice should still deform despite mask=0
                // (mask only affects brush layers per the implementation)
                int minCornerIndex = FindCornerVertexIndex(sourceVertices, settings.LocalBounds.min);
                if (minCornerIndex >= 0)
                {
                    float dist = (deformedVertices[minCornerIndex] - sourceVertices[minCornerIndex]).magnitude;
                    Assert.That(dist, Is.GreaterThan(0.01f),
                        "Lattice deformation should NOT be blocked by vertex mask");
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void Mask_MixedLayerComposition()
        {
            var fixture = CreateFixture("Mask_MixedLayerComposition");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;

                // Brush layer A: masked
                int layerA = deformer.AddLayer("Masked", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = layerA;
                deformer.EnsureDisplacementCapacity();
                var dispA = new Vector3(0.1f, 0f, 0f);
                deformer.SetDisplacement(0, dispA);

                var maskedLayer = deformer.Layers[layerA];
                maskedLayer.EnsureVertexMaskCapacity(vertexCount);
                maskedLayer.SetVertexMask(0, 0.5f); // Half-masked

                // Brush layer B: unmasked
                int layerB = deformer.AddLayer("Unmasked", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = layerB;
                deformer.EnsureDisplacementCapacity();
                var dispB = new Vector3(0f, 0.2f, 0f);
                deformer.SetDisplacement(0, dispB);

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);
                var sourceVertices = deformer.SourceMesh.vertices;
                var deformedVertices = runtimeMesh.vertices;

                // Vertex 0: dispA * 0.5 (mask) + dispB * 1.0 (no mask)
                var expected = sourceVertices[0] + dispA * 0.5f + dispB;
                AssertApproximately(expected, deformedVertices[0], 2e-3f);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void ClearVertexMask_RestoresFullDeformation()
        {
            var fixture = CreateFixture("ClearVertexMask_RestoresFullDeformation");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;

                int brushLayerIndex = deformer.AddLayer("ClearMask", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushLayerIndex;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[brushLayerIndex];
                var displacement = new Vector3(0.1f, 0.2f, 0f);
                deformer.SetDisplacement(0, displacement);

                // Set mask to zero (protected)
                layer.EnsureVertexMaskCapacity(vertexCount);
                layer.SetVertexMask(0, 0f);

                ReleaseRuntimeMesh(deformer);
                var maskedMesh = deformer.Deform(false);
                var sourceVertices = deformer.SourceMesh.vertices;
                AssertApproximately(sourceVertices[0], maskedMesh.vertices[0], 2e-3f);

                // Clear mask
                layer.ClearVertexMask();

                ReleaseRuntimeMesh(deformer);
                var clearedMesh = deformer.Deform(false);
                var expected = sourceVertices[0] + displacement;
                AssertApproximately(expected, clearedMesh.vertices[0], 2e-3f);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        // ========================================================================
        // Serialization (Copy / Paste)
        // ========================================================================

        [Test]
        public void BrushLayer_JsonRoundTrip_PreservesData()
        {
            var fixture = CreateFixture("BrushLayer_JsonRoundTrip_PreservesData");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;

                int layerIndex = deformer.AddLayer("Serialized", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = layerIndex;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[layerIndex];
                layer.Weight = 0.75f;
                layer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                layer.BlendShapeName = "TestBS";

                var disp0 = new Vector3(0.1f, -0.2f, 0.3f);
                var disp1 = new Vector3(-0.05f, 0.15f, 0f);
                layer.SetBrushDisplacement(0, disp0);
                layer.SetBrushDisplacement(1, disp1);

                layer.EnsureVertexMaskCapacity(vertexCount);
                layer.SetVertexMask(0, 0.3f);
                layer.SetVertexMask(1, 0.7f);

                // Serialize
                string json = JsonUtility.ToJson(layer);
                Assert.That(string.IsNullOrEmpty(json), Is.False);

                // Deserialize into new layer
                var restored = new LatticeLayer();
                JsonUtility.FromJsonOverwrite(json, restored);

                Assert.That(restored.Name, Is.EqualTo("Serialized"));
                Assert.That(restored.Weight, Is.EqualTo(0.75f).Within(Epsilon));
                Assert.That(restored.BlendShapeOutput, Is.EqualTo(BlendShapeOutputMode.OutputAsBlendShape));
                Assert.That(restored.BlendShapeName, Is.EqualTo("TestBS"));
                Assert.That(restored.BrushDisplacementCount, Is.EqualTo(vertexCount));

                AssertApproximately(disp0, restored.GetBrushDisplacement(0), Epsilon);
                AssertApproximately(disp1, restored.GetBrushDisplacement(1), Epsilon);
                Assert.That(restored.GetVertexMask(0), Is.EqualTo(0.3f).Within(Epsilon));
                Assert.That(restored.GetVertexMask(1), Is.EqualTo(0.7f).Within(Epsilon));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void LatticeLayer_JsonRoundTrip_PreservesControlPoints()
        {
            var fixture = CreateFixture("LatticeLayer_JsonRoundTrip_PreservesControlPoints");
            try
            {
                var deformer = fixture.Deformer;
                var layer = deformer.Layers[0];
                var settings = layer.Settings;

                layer.Weight = 0.6f;
                layer.BlendShapeName = "LatticeBS";

                // Modify a control point
                var delta = new Vector3(0.1f, 0.2f, -0.05f);
                settings.SetControlPointLocal(0, settings.GetControlPointLocal(0) + delta);

                var originalGrid = settings.GridSize;
                var originalBounds = settings.LocalBounds;
                var originalCP0 = settings.GetControlPointLocal(0);

                // Serialize
                string json = JsonUtility.ToJson(layer);
                var restored = new LatticeLayer();
                JsonUtility.FromJsonOverwrite(json, restored);

                Assert.That(restored.Name, Is.EqualTo("Lattice Layer"));
                Assert.That(restored.Weight, Is.EqualTo(0.6f).Within(Epsilon));
                Assert.That(restored.Settings, Is.Not.Null);
                Assert.That(restored.Settings.GridSize, Is.EqualTo(originalGrid));
                AssertApproximately(originalBounds.center, restored.Settings.LocalBounds.center);
                AssertApproximately(originalBounds.size, restored.Settings.LocalBounds.size);
                AssertApproximately(originalCP0, restored.Settings.GetControlPointLocal(0), Epsilon);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void PasteToDifferentMesh_DoesNotCrash()
        {
            var fixture1 = CreateFixture("PasteToDifferentMesh_Source");
            var fixture2 = CreateFixtureWithSymmetricMesh("PasteToDifferentMesh_Target");
            try
            {
                var deformer1 = fixture1.Deformer;

                int layerIndex = deformer1.AddLayer("CrossPaste", MeshDeformerLayerType.Brush);
                deformer1.ActiveLayerIndex = layerIndex;
                deformer1.EnsureDisplacementCapacity();
                deformer1.SetDisplacement(0, new Vector3(0.5f, 0f, 0f));

                var sourceLayer = deformer1.Layers[layerIndex];
                string json = JsonUtility.ToJson(sourceLayer);

                // Paste into second deformer with different mesh
                var deformer2 = fixture2.Deformer;
                var restored = new LatticeLayer();
                JsonUtility.FromJsonOverwrite(json, restored);

                int insertedIndex = deformer2.InsertLayer(restored);
                Assert.That(insertedIndex, Is.GreaterThanOrEqualTo(0));

                // Deform should not crash even with mismatched vertex counts
                ReleaseRuntimeMesh(deformer2);
                Assert.DoesNotThrow(() => deformer2.Deform(false));
            }
            finally
            {
                fixture1.Dispose();
                fixture2.Dispose();
            }
        }

        // ========================================================================
        // Deform Idempotency & Stability
        // ========================================================================

        [Test]
        public void Deform_CalledTwice_ProducesSameResult()
        {
            var fixture = CreateFixture("Deform_CalledTwice_ProducesSameResult");
            try
            {
                var deformer = fixture.Deformer;

                int brushLayerIndex = deformer.AddLayer("Idempotent", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushLayerIndex;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.15f, -0.1f, 0.05f));

                ReleaseRuntimeMesh(deformer);
                var mesh1 = deformer.Deform(false);
                var verts1 = mesh1.vertices.Clone() as Vector3[];

                ReleaseRuntimeMesh(deformer);
                var mesh2 = deformer.Deform(false);
                var verts2 = mesh2.vertices;

                Assert.That(verts1.Length, Is.EqualTo(verts2.Length));
                for (int i = 0; i < verts1.Length; i++)
                {
                    AssertApproximately(verts1[i], verts2[i], Epsilon);
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void LargeDisplacement_NoNaNOrInfinity()
        {
            var fixture = CreateFixture("LargeDisplacement_NoNaNOrInfinity");
            try
            {
                var deformer = fixture.Deformer;

                int brushLayerIndex = deformer.AddLayer("Large", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushLayerIndex;
                deformer.EnsureDisplacementCapacity();

                // Set huge displacement
                deformer.SetDisplacement(0, new Vector3(1000f, -500f, 2000f));

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);
                Assert.That(runtimeMesh, Is.Not.Null);

                var deformedVertices = runtimeMesh.vertices;
                for (int i = 0; i < deformedVertices.Length; i++)
                {
                    Assert.That(float.IsNaN(deformedVertices[i].x), Is.False, $"NaN at vertex {i}.x");
                    Assert.That(float.IsNaN(deformedVertices[i].y), Is.False, $"NaN at vertex {i}.y");
                    Assert.That(float.IsNaN(deformedVertices[i].z), Is.False, $"NaN at vertex {i}.z");
                    Assert.That(float.IsInfinity(deformedVertices[i].x), Is.False, $"Inf at vertex {i}.x");
                    Assert.That(float.IsInfinity(deformedVertices[i].y), Is.False, $"Inf at vertex {i}.y");
                    Assert.That(float.IsInfinity(deformedVertices[i].z), Is.False, $"Inf at vertex {i}.z");
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void EmptyBrushLayer_DoesNotAffectDeform()
        {
            var fixture = CreateFixture("EmptyBrushLayer_DoesNotAffectDeform");
            try
            {
                var deformer = fixture.Deformer;
                var sourceVertices = deformer.SourceMesh.vertices;

                // Add brush layer but don't set any displacements
                deformer.AddLayer("Empty Brush", MeshDeformerLayerType.Brush);

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);
                var deformedVertices = runtimeMesh.vertices;

                for (int i = 0; i < sourceVertices.Length; i++)
                {
                    AssertApproximately(sourceVertices[i], deformedVertices[i], 2e-3f);
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        // ========================================================================
        // Geodesic Distance (Practical Cases)
        // ========================================================================

        [Test]
        public void GeodesicDistance_DisconnectedIslands_DoNotPropagate()
        {
            // Two separate triangles (islands) that are spatially close but not connected
            // Island A: v0, v1, v2
            // Island B: v3, v4, v5  (close to island A but no shared edges)
            var vertices = new[]
            {
                new Vector3(0f, 0f, 0f),   // Island A
                new Vector3(1f, 0f, 0f),
                new Vector3(0.5f, 1f, 0f),
                new Vector3(0.1f, 0f, 0f), // Island B (very close to island A)
                new Vector3(1.1f, 0f, 0f),
                new Vector3(0.6f, 1f, 0f)
            };

            var adjacency = new List<HashSet<int>>
            {
                new HashSet<int> { 1, 2 },       // v0 connects to v1, v2
                new HashSet<int> { 0, 2 },       // v1 connects to v0, v2
                new HashSet<int> { 0, 1 },       // v2 connects to v0, v1
                new HashSet<int> { 4, 5 },       // v3 connects to v4, v5 (separate island)
                new HashSet<int> { 3, 5 },       // v4 connects to v3, v5
                new HashSet<int> { 3, 4 }        // v5 connects to v3, v4
            };

            var distances = GeodesicDistanceCalculator.ComputeDistances(0, 10f, adjacency, vertices);

            // Island A vertices should be reachable
            Assert.That(distances.ContainsKey(0), Is.True);
            Assert.That(distances.ContainsKey(1), Is.True);
            Assert.That(distances.ContainsKey(2), Is.True);

            // Island B vertices should NOT be reachable despite being spatially close
            Assert.That(distances.ContainsKey(3), Is.False,
                "v3 is on a separate island and should not be reachable");
            Assert.That(distances.ContainsKey(4), Is.False,
                "v4 is on a separate island and should not be reachable");
            Assert.That(distances.ContainsKey(5), Is.False,
                "v5 is on a separate island and should not be reachable");
        }

        [Test]
        public void GeodesicDistance_TJunction_ChoosesShortestPath()
        {
            // T-junction topology:
            //   v0 --1-- v1 --1-- v2
            //                |
            //                1
            //                |
            //               v3 --1-- v4
            // From v0 to v4 the shortest path is v0→v1→v3→v4 = 3 (not going through v2)
            var vertices = new[]
            {
                new Vector3(0f, 0f, 0f),   // v0
                new Vector3(1f, 0f, 0f),   // v1
                new Vector3(2f, 0f, 0f),   // v2
                new Vector3(1f, -1f, 0f),  // v3
                new Vector3(2f, -1f, 0f)   // v4
            };

            var adjacency = new List<HashSet<int>>
            {
                new HashSet<int> { 1 },          // v0: connects to v1
                new HashSet<int> { 0, 2, 3 },    // v1: T-junction
                new HashSet<int> { 1 },          // v2: end
                new HashSet<int> { 1, 4 },       // v3: connects to v1, v4
                new HashSet<int> { 3 }           // v4: end
            };

            var distances = GeodesicDistanceCalculator.ComputeDistances(0, 5f, adjacency, vertices);

            Assert.That(distances[0], Is.EqualTo(0f).Within(Epsilon));
            Assert.That(distances[1], Is.EqualTo(1f).Within(Epsilon));
            Assert.That(distances[2], Is.EqualTo(2f).Within(Epsilon));

            // v3 is at distance 1 (v1) + 1 (edge to v3) = 2
            Assert.That(distances[3], Is.EqualTo(2f).Within(Epsilon));
            // v4 is at distance 2 (v3) + 1 (edge to v4) = 3
            Assert.That(distances[4], Is.EqualTo(3f).Within(Epsilon));
        }

        // ========================================================================
        // DuplicateLayer / Layer Management Edge Cases
        // ========================================================================

        [Test]
        public void DuplicateLayer_ProducesIndependentCopy()
        {
            var fixture = CreateFixture("DuplicateLayer_ProducesIndependentCopy");
            try
            {
                var deformer = fixture.Deformer;

                int brushLayerIndex = deformer.AddLayer("Original", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushLayerIndex;
                deformer.EnsureDisplacementCapacity();

                var originalDisp = new Vector3(0.1f, 0.2f, 0.3f);
                deformer.SetDisplacement(0, originalDisp);

                int dupIndex = deformer.DuplicateLayer(brushLayerIndex);
                Assert.That(dupIndex, Is.GreaterThan(brushLayerIndex));

                var dupLayer = deformer.Layers[dupIndex];
                AssertApproximately(originalDisp, dupLayer.GetBrushDisplacement(0), Epsilon);

                // Modify duplicate — original should be unaffected
                dupLayer.SetBrushDisplacement(0, Vector3.zero);

                var origLayer = deformer.Layers[brushLayerIndex];
                AssertApproximately(originalDisp, origLayer.GetBrushDisplacement(0), Epsilon);
                AssertApproximately(Vector3.zero, dupLayer.GetBrushDisplacement(0), Epsilon);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void RemoveLastLayer_Fails()
        {
            var fixture = CreateFixture("RemoveLastLayer_Fails");
            try
            {
                var deformer = fixture.Deformer;
                Assert.That(deformer.Layers.Count, Is.EqualTo(1));

                bool removed = deformer.RemoveLayer(0);
                Assert.That(removed, Is.False, "Should not be able to remove the only layer");
                Assert.That(deformer.Layers.Count, Is.EqualTo(1));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void ActiveLayerIndex_AdjustsAfterRemoveBefore()
        {
            var fixture = CreateFixture("ActiveLayerIndex_AdjustsAfterRemoveBefore");
            try
            {
                var deformer = fixture.Deformer;

                deformer.AddLayer("Layer A", MeshDeformerLayerType.Brush);
                deformer.AddLayer("Layer B", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = 2; // Layer B

                string activeName = deformer.Layers[deformer.ActiveLayerIndex].Name;
                Assert.That(activeName, Is.EqualTo("Layer B"));

                // Remove Layer A (index 1) — active should shift from 2 to 1
                deformer.RemoveLayer(1);

                Assert.That(deformer.ActiveLayerIndex, Is.LessThanOrEqualTo(deformer.Layers.Count - 1));
                Assert.That(deformer.Layers[deformer.ActiveLayerIndex].Name, Is.EqualTo("Layer B"));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void InsertLayer_AddsAtEnd()
        {
            var fixture = CreateFixture("InsertLayer_AddsAtEnd");
            try
            {
                var deformer = fixture.Deformer;
                int countBefore = deformer.Layers.Count;

                var newLayer = new LatticeLayer();
                newLayer.Name = "Inserted";
                int insertedIndex = deformer.InsertLayer(newLayer);

                Assert.That(insertedIndex, Is.EqualTo(countBefore));
                Assert.That(deformer.Layers.Count, Is.EqualTo(countBefore + 1));
                Assert.That(deformer.Layers[insertedIndex].Name, Is.EqualTo("Inserted"));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void ClearBrushDisplacements_ResetsToZero()
        {
            var fixture = CreateFixture("ClearBrushDisplacements_ResetsToZero");
            try
            {
                var deformer = fixture.Deformer;

                int brushLayerIndex = deformer.AddLayer("ToClear", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushLayerIndex;
                deformer.EnsureDisplacementCapacity();

                deformer.SetDisplacement(0, new Vector3(1f, 2f, 3f));
                deformer.SetDisplacement(1, new Vector3(4f, 5f, 6f));

                deformer.ClearDisplacements();

                AssertApproximately(Vector3.zero, deformer.GetDisplacement(0), Epsilon);
                AssertApproximately(Vector3.zero, deformer.GetDisplacement(1), Epsilon);

                // Deform should produce unmodified mesh
                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);
                var sourceVertices = deformer.SourceMesh.vertices;
                AssertApproximately(sourceVertices[0], runtimeMesh.vertices[0], 2e-3f);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        // ========================================================================
        // BlendShape Advanced Cases
        // ========================================================================

        [Test]
        public void BlendShapeOutput_LatticeLayer_ProducesCorrectDelta()
        {
            var fixture = CreateFixture("BlendShapeOutput_LatticeLayer_ProducesCorrectDelta");
            try
            {
                var deformer = fixture.Deformer;
                var latticeLayer = deformer.Layers[0];
                var settings = latticeLayer.Settings;

                // Move a control point
                var delta = new Vector3(0f, 0.2f, 0f);
                settings.SetControlPointLocal(0, settings.GetControlPointLocal(0) + delta);

                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "LatticeShape";

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);

                // Should have produced a BlendShape
                int shapeIndex = -1;
                for (int i = 0; i < runtimeMesh.blendShapeCount; i++)
                {
                    if (runtimeMesh.GetBlendShapeName(i) == "LatticeShape")
                    {
                        shapeIndex = i;
                        break;
                    }
                }

                Assert.That(shapeIndex, Is.GreaterThanOrEqualTo(0), "Lattice BlendShape not found");

                int vertexCount = deformer.SourceMesh.vertexCount;
                var frameDeltas = new Vector3[vertexCount];
                int frameCount = runtimeMesh.GetBlendShapeFrameCount(shapeIndex);
                runtimeMesh.GetBlendShapeFrameVertices(shapeIndex, frameCount - 1, frameDeltas, null, null);

                // At least one vertex near the moved control point should have non-zero delta
                bool anyNonZero = false;
                for (int i = 0; i < vertexCount; i++)
                {
                    if (frameDeltas[i].sqrMagnitude > Epsilon * Epsilon)
                    {
                        anyNonZero = true;
                        break;
                    }
                }

                Assert.That(anyNonZero, Is.True, "Lattice BlendShape should have non-zero deltas");

                // Vertices should not be directly modified (BlendShape output mode)
                var sourceVertices = deformer.SourceMesh.vertices;
                var deformedVertices = runtimeMesh.vertices;
                for (int i = 0; i < vertexCount; i++)
                {
                    AssertApproximately(sourceVertices[i], deformedVertices[i], 2e-3f);
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void BlendShapeOutput_WeightScalesDelta()
        {
            var fixture = CreateFixture("BlendShapeOutput_WeightScalesDelta");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;

                int brushLayerIndex = deformer.AddLayer("Weighted BS", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushLayerIndex;
                deformer.EnsureDisplacementCapacity();

                var displacement = new Vector3(0.4f, 0f, 0f);
                const float weight = 0.3f;
                deformer.SetDisplacement(0, displacement);

                var layer = deformer.Layers[brushLayerIndex];
                layer.Weight = weight;
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);

                int shapeIndex = runtimeMesh.blendShapeCount - 1;
                var frameDeltas = new Vector3[vertexCount];
                int frameCount = runtimeMesh.GetBlendShapeFrameCount(shapeIndex);
                runtimeMesh.GetBlendShapeFrameVertices(shapeIndex, frameCount - 1, frameDeltas, null, null);

                // Delta should be displacement * weight
                AssertApproximately(displacement * weight, frameDeltas[0], 2e-3f);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void BlendShapeOutput_AllLayersCombineIntoSingleFrame()
        {
            var fixture = CreateFixture("BlendShapeOutput_AllLayersCombineIntoSingleFrame");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;

                // Layer A
                int layerA = deformer.AddLayer("Layer A", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = layerA;
                deformer.EnsureDisplacementCapacity();
                var dispA = new Vector3(0.1f, 0f, 0f);
                deformer.SetDisplacement(0, dispA);

                // Layer B
                int layerB = deformer.AddLayer("Layer B", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = layerB;
                deformer.EnsureDisplacementCapacity();
                var dispB = new Vector3(0f, 0.2f, 0f);
                deformer.SetDisplacement(0, dispB);

                // Component-level BlendShape output — all layers combine
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "Combined";

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);

                var sourceVertices = deformer.SourceMesh.vertices;
                var deformedVertices = runtimeMesh.vertices;

                // Vertices should NOT be directly modified (BlendShape output mode)
                AssertApproximately(sourceVertices[0], deformedVertices[0], 2e-3f);

                // All layers should combine into a single BlendShape frame
                int shapeIndex = runtimeMesh.blendShapeCount - 1;
                var frameDeltas = new Vector3[vertexCount];
                int frameCount = runtimeMesh.GetBlendShapeFrameCount(shapeIndex);
                runtimeMesh.GetBlendShapeFrameVertices(shapeIndex, frameCount - 1, frameDeltas, null, null);
                AssertApproximately(dispA + dispB, frameDeltas[0], 2e-3f);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void MultipleImports_CreateSeparateLayers()
        {
            var shapeNames = new[] { "ShapeA", "ShapeB", "ShapeC" };
            var fixture = CreateFixtureWithBlendShapes(
                "MultipleImports_CreateSeparateLayers", shapeNames);
            try
            {
                var deformer = fixture.Deformer;
                int layerCountBefore = deformer.Layers.Count;

                int indexA = deformer.ImportBlendShapeAsLayer(0);
                int indexB = deformer.ImportBlendShapeAsLayer(1);
                int indexC = deformer.ImportBlendShapeAsLayer(2);

                Assert.That(deformer.Layers.Count, Is.EqualTo(layerCountBefore + 3));
                Assert.That(deformer.Layers[indexA].Name, Is.EqualTo("ShapeA"));
                Assert.That(deformer.Layers[indexB].Name, Is.EqualTo("ShapeB"));
                Assert.That(deformer.Layers[indexC].Name, Is.EqualTo("ShapeC"));

                // Each layer should have independent displacements
                var dispA = deformer.Layers[indexA].GetBrushDisplacement(0);
                var dispB = deformer.Layers[indexB].GetBrushDisplacement(0);
                Assert.That((dispA - dispB).sqrMagnitude, Is.GreaterThan(Epsilon),
                    "Different BlendShapes should produce different displacements");
            }
            finally
            {
                fixture.Dispose();
            }
        }

        // ========================================================================
        // Flip / Split Edge Cases
        // ========================================================================

        [Test]
        public void FlipTwice_RestoresOriginal()
        {
            var fixture = CreateFixtureWithSymmetricMesh("FlipTwice_RestoresOriginal");
            try
            {
                var deformer = fixture.Deformer;
                int brushLayerIndex = deformer.AddLayer("DoubleFlip", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushLayerIndex;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[brushLayerIndex];
                int vertexCount = deformer.SourceMesh.vertexCount;

                // Set varied displacements
                var displacements = new Vector3[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    displacements[i] = new Vector3(0.1f * (i + 1), 0.05f * i, -0.02f * i);
                    layer.SetBrushDisplacement(i, displacements[i]);
                }

                // Flip X twice
                deformer.FlipLayerByAxis(brushLayerIndex, 0);
                deformer.FlipLayerByAxis(brushLayerIndex, 0);

                // Should restore original
                for (int i = 0; i < vertexCount; i++)
                {
                    AssertApproximately(displacements[i], layer.GetBrushDisplacement(i), 2e-3f);
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void SplitBothSides_ZerosEverything()
        {
            var fixture = CreateFixture("SplitBothSides_ZerosEverything");
            try
            {
                var deformer = fixture.Deformer;
                int brushLayerIndex = deformer.AddLayer("BothSplit", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushLayerIndex;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[brushLayerIndex];
                var vertices = deformer.SourceMesh.vertices;
                var displacement = new Vector3(0.2f, 0.3f, 0.1f);
                for (int i = 0; i < vertices.Length; i++)
                {
                    deformer.SetDisplacement(i, displacement);
                }

                // Split keep positive, then split keep negative — should zero everything
                deformer.SplitLayerByAxis(brushLayerIndex, 0, true);  // zeros negative side
                deformer.SplitLayerByAxis(brushLayerIndex, 0, false); // zeros positive side

                for (int i = 0; i < vertices.Length; i++)
                {
                    AssertApproximately(Vector3.zero, layer.GetBrushDisplacement(i), Epsilon);
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        // ========================================================================
        // Multi-Layer Composition Stress Tests
        // ========================================================================

        [Test]
        public void ManyLayers_AccumulateCorrectly()
        {
            var fixture = CreateFixture("ManyLayers_AccumulateCorrectly");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;

                const int layerCount = 10;
                var expectedTotal = Vector3.zero;

                for (int n = 0; n < layerCount; n++)
                {
                    int idx = deformer.AddLayer($"Layer{n}", MeshDeformerLayerType.Brush);
                    deformer.ActiveLayerIndex = idx;
                    deformer.EnsureDisplacementCapacity();

                    var disp = new Vector3(0.01f * (n + 1), 0f, 0f);
                    deformer.SetDisplacement(0, disp);
                    expectedTotal += disp; // all weights default to 1
                }

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);
                var sourceVertices = deformer.SourceMesh.vertices;
                var expected = sourceVertices[0] + expectedTotal;

                AssertApproximately(expected, runtimeMesh.vertices[0], 2e-3f);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void LatticePlusBrushLayers_BlendShapeOutput_CombinesAll()
        {
            var fixture = CreateFixture("LatticePlusBrushLayers_BlendShapeOutput_CombinesAll");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;

                // Layer 0: Lattice
                var latticeSettings = deformer.Layers[0].Settings;
                var latticeDelta = new Vector3(0f, 0.15f, 0f);
                latticeSettings.SetControlPointLocal(0,
                    latticeSettings.GetControlPointLocal(0) + latticeDelta);

                // Layer 1: Brush
                int brushLayer = deformer.AddLayer("Brush Layer", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushLayer;
                deformer.EnsureDisplacementCapacity();
                var brushDisp = new Vector3(0.05f, 0f, 0f);
                deformer.SetDisplacement(0, brushDisp);

                // Component-level BlendShape output — all layers combine
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "CombinedAll";

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);

                var sourceVertices = deformer.SourceMesh.vertices;
                var deformedVertices = runtimeMesh.vertices;

                // Vertices should NOT be directly modified (BlendShape output mode)
                for (int i = 0; i < vertexCount; i++)
                {
                    AssertApproximately(sourceVertices[i], deformedVertices[i], 2e-3f);
                }

                // All layers should combine into a single BlendShape frame
                int bsShapeIndex = runtimeMesh.blendShapeCount - 1;
                var frameDeltas = new Vector3[vertexCount];
                int bsFrameCount = runtimeMesh.GetBlendShapeFrameCount(bsShapeIndex);
                runtimeMesh.GetBlendShapeFrameVertices(bsShapeIndex, bsFrameCount - 1, frameDeltas, null, null);

                // At least vertex 0 should have non-zero delta from the combined lattice + brush
                bool anyNonZero = false;
                for (int i = 0; i < vertexCount; i++)
                {
                    if (frameDeltas[i].sqrMagnitude > Epsilon * Epsilon)
                    {
                        anyNonZero = true;
                        break;
                    }
                }

                Assert.That(anyNonZero, Is.True,
                    "Combined BlendShape should have non-zero deltas from lattice and brush layers");
            }
            finally
            {
                fixture.Dispose();
            }
        }

        // ========================================================================
        // BlendShape Test Mode
        // ========================================================================

        [Test]
        public void BlendShapeTestMode_DeformTrue_AssignsMeshToSMR()
        {
            var fixture = CreateSkinnedFixture("BlendShapeTestMode_DeformTrue_AssignsMeshToSMR");
            try
            {
                var deformer = fixture.Deformer;
                var smr = deformer.GetComponent<SkinnedMeshRenderer>();
                var originalMesh = smr.sharedMesh;

                // Add brush deformation
                int brushIdx = deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.1f, 0f, 0f));

                // Enable BlendShape output
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "TestBS";

                // Simulate entering test mode: Deform(true)
                deformer.InvalidateCache();
                deformer.Deform(true);

                // SMR should now have the runtime mesh with BlendShape
                Assert.That(smr.sharedMesh, Is.Not.SameAs(originalMesh));
                Assert.That(smr.sharedMesh.GetBlendShapeIndex("TestBS"), Is.GreaterThanOrEqualTo(0));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void BlendShapeTestMode_SetWeight_AffectsRenderer()
        {
            var fixture = CreateSkinnedFixture("BlendShapeTestMode_SetWeight_AffectsRenderer");
            try
            {
                var deformer = fixture.Deformer;
                var smr = deformer.GetComponent<SkinnedMeshRenderer>();

                // Add brush deformation
                int brushIdx = deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.2f, 0f, 0f));

                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "WeightTest";

                deformer.InvalidateCache();
                deformer.Deform(true);

                int shapeIndex = smr.sharedMesh.GetBlendShapeIndex("WeightTest");
                Assert.That(shapeIndex, Is.GreaterThanOrEqualTo(0));

                // Set weight and verify it sticks
                smr.SetBlendShapeWeight(shapeIndex, 50f);
                Assert.That(smr.GetBlendShapeWeight(shapeIndex), Is.EqualTo(50f).Within(0.01f));

                smr.SetBlendShapeWeight(shapeIndex, 100f);
                Assert.That(smr.GetBlendShapeWeight(shapeIndex), Is.EqualTo(100f).Within(0.01f));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void BlendShapeTestMode_ExitRestoresMesh()
        {
            var fixture = CreateSkinnedFixture("BlendShapeTestMode_ExitRestoresMesh");
            try
            {
                var deformer = fixture.Deformer;
                var smr = deformer.GetComponent<SkinnedMeshRenderer>();
                var originalMesh = smr.sharedMesh;

                int brushIdx = deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.1f, 0f, 0f));

                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "RestoreTest";

                // Enter test mode
                deformer.InvalidateCache();
                deformer.Deform(true);
                Assert.That(smr.sharedMesh, Is.Not.SameAs(originalMesh));

                // Exit: restore original mesh
                smr.sharedMesh = originalMesh;
                Assert.That(smr.sharedMesh, Is.SameAs(originalMesh));
                Assert.That(smr.sharedMesh.GetBlendShapeIndex("RestoreTest"), Is.EqualTo(-1));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void BlendShapeTestMode_CurveAffectsFrames()
        {
            var fixture = CreateSkinnedFixture("BlendShapeTestMode_CurveAffectsFrames");
            try
            {
                var deformer = fixture.Deformer;

                int brushIdx = deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();
                var disp = new Vector3(1f, 0f, 0f);
                deformer.SetDisplacement(0, disp);

                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "CurveTest";

                // Ease-in curve: slow start, fast end
                deformer.BlendShapeCurve = new AnimationCurve(
                    new Keyframe(0f, 0f, 0f, 0f),
                    new Keyframe(1f, 1f, 2f, 2f));

                ReleaseRuntimeMesh(deformer);
                var mesh = deformer.Deform(false);
                Assert.That(mesh, Is.Not.Null);

                int shapeIndex = mesh.GetBlendShapeIndex("CurveTest");
                Assert.That(shapeIndex, Is.GreaterThanOrEqualTo(0));

                // Should have 100 frames
                Assert.That(mesh.GetBlendShapeFrameCount(shapeIndex), Is.EqualTo(100));

                // Midpoint frame (frame 49, weight=50%) with ease-in should have
                // a delta smaller than linear 0.5
                int vertexCount = mesh.vertexCount;
                var midDeltas = new Vector3[vertexCount];
                mesh.GetBlendShapeFrameVertices(shapeIndex, 49, midDeltas, new Vector3[vertexCount], new Vector3[vertexCount]);

                // Ease-in at t=0.5 should produce a value < 0.5
                float midMagnitude = midDeltas[0].magnitude;
                float fullMagnitude = disp.magnitude;
                Assert.That(midMagnitude, Is.LessThan(fullMagnitude * 0.5f),
                    "Ease-in curve at midpoint should produce less than 50% deformation");

                // Last frame should have ~full delta
                var lastDeltas = new Vector3[vertexCount];
                mesh.GetBlendShapeFrameVertices(shapeIndex, 99, lastDeltas, new Vector3[vertexCount], new Vector3[vertexCount]);
                AssertApproximately(disp, lastDeltas[0], 0.01f);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void BlendShapeTestMode_LinearCurve_ProducesLinearFrames()
        {
            var fixture = CreateSkinnedFixture("BlendShapeTestMode_LinearCurve_ProducesLinearFrames");
            try
            {
                var deformer = fixture.Deformer;

                int brushIdx = deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();
                var disp = new Vector3(1f, 0f, 0f);
                deformer.SetDisplacement(0, disp);

                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "LinearTest";
                deformer.BlendShapeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

                ReleaseRuntimeMesh(deformer);
                var mesh = deformer.Deform(false);
                Assert.That(mesh, Is.Not.Null);

                int shapeIndex = mesh.GetBlendShapeIndex("LinearTest");

                // Check that midpoint frame has ~50% of full delta (linear)
                int vertexCount = mesh.vertexCount;
                var midDeltas = new Vector3[vertexCount];
                mesh.GetBlendShapeFrameVertices(shapeIndex, 49, midDeltas, new Vector3[vertexCount], new Vector3[vertexCount]);

                var expected = disp * 0.5f;
                AssertApproximately(expected, midDeltas[0], 0.01f);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void BlendShapeTestMode_ExitPreservesPreExistingWeights()
        {
            // Source mesh with a pre-existing BlendShape
            var root = new GameObject("BlendShapeTestMode_ExitPreservesPreExistingWeights");
            try
            {
                var smr = root.AddComponent<SkinnedMeshRenderer>();
                var sourceMesh = CreateRuntimeCubeMesh();
                sourceMesh.bindposes = new[] { Matrix4x4.identity };
                var bw = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                var boneWeights = new BoneWeight[sourceMesh.vertexCount];
                for (int i = 0; i < boneWeights.Length; i++) boneWeights[i] = bw;
                sourceMesh.boneWeights = boneWeights;

                // Add a pre-existing BlendShape to the source mesh
                var preExistingDelta = new Vector3[sourceMesh.vertexCount];
                preExistingDelta[0] = new Vector3(0f, 1f, 0f);
                sourceMesh.AddBlendShapeFrame("Smile", 100f, preExistingDelta, null, null);

                smr.sharedMesh = sourceMesh;
                smr.bones = new[] { root.transform };

                // Set a weight on the pre-existing BlendShape
                int smileIndex = sourceMesh.GetBlendShapeIndex("Smile");
                smr.SetBlendShapeWeight(smileIndex, 75f);

                var deformer = root.AddComponent<LatticeDeformer>();
                deformer.Reset();
                deformer.Deform(false);

                // Add brush deformation + enable BS output
                int brushIdx = deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.1f, 0f, 0f));
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "TestBS";

                // --- Enter test mode ---
                var preTestMesh = smr.sharedMesh;
                deformer.InvalidateCache();
                deformer.Deform(true);

                // Runtime mesh should have both "Smile" and "TestBS"
                var runtimeMesh = smr.sharedMesh;
                Assert.That(runtimeMesh, Is.Not.SameAs(preTestMesh));
                int testShapeIdx = runtimeMesh.GetBlendShapeIndex("TestBS");
                Assert.That(testShapeIdx, Is.GreaterThanOrEqualTo(0));
                smr.SetBlendShapeWeight(testShapeIdx, 50f);

                // --- Exit test mode: restore only the mesh ---
                smr.sharedMesh = preTestMesh;

                // Pre-existing BlendShape should still be there
                Assert.That(smr.sharedMesh.GetBlendShapeIndex("Smile"), Is.GreaterThanOrEqualTo(0));
                // Test BlendShape should be gone (only exists on runtime mesh)
                Assert.That(smr.sharedMesh.GetBlendShapeIndex("TestBS"), Is.EqualTo(-1));
                // Pre-existing weight should be preserved
                Assert.That(smr.GetBlendShapeWeight(smileIndex), Is.EqualTo(75f).Within(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BlendShapeTestMode_ExitPreservesDeformerChanges()
        {
            var fixture = CreateSkinnedFixture("BlendShapeTestMode_ExitPreservesDeformerChanges");
            try
            {
                var deformer = fixture.Deformer;
                var smr = deformer.GetComponent<SkinnedMeshRenderer>();

                int brushIdx = deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.1f, 0f, 0f));
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "Original";

                // --- Enter test mode ---
                var preTestMesh = smr.sharedMesh;
                deformer.InvalidateCache();
                deformer.Deform(true);

                // During test mode, change deformer settings
                deformer.BlendShapeName = "Renamed";
                deformer.BlendShapeCurve = new AnimationCurve(
                    new Keyframe(0f, 0f), new Keyframe(1f, 0.5f));
                deformer.SetDisplacement(1, new Vector3(0f, 0.2f, 0f));

                // --- Exit test mode: restore mesh only ---
                smr.sharedMesh = preTestMesh;

                // Deformer changes should be preserved
                Assert.That(deformer.BlendShapeName, Is.EqualTo("Renamed"));
                Assert.That(deformer.BlendShapeCurve.Evaluate(1f), Is.EqualTo(0.5f).Within(0.01f));
                Assert.That(deformer.Layers[brushIdx].GetBrushDisplacement(1),
                    Is.EqualTo(new Vector3(0f, 0.2f, 0f)));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void BlendShapeTestMode_OnlyMeshIsReverted()
        {
            var fixture = CreateSkinnedFixture("BlendShapeTestMode_OnlyMeshIsReverted");
            try
            {
                var deformer = fixture.Deformer;
                var smr = deformer.GetComponent<SkinnedMeshRenderer>();

                int brushIdx = deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.5f, 0f, 0f));
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "RevertTest";

                // Record initial state
                var originalMesh = smr.sharedMesh;
                bool originalEnabled = smr.enabled;
                var originalBounds = smr.localBounds;

                // --- Enter test mode ---
                deformer.InvalidateCache();
                deformer.Deform(true);
                Assert.That(smr.sharedMesh, Is.Not.SameAs(originalMesh), "Mesh should change during test mode");

                // --- Exit test mode ---
                smr.sharedMesh = originalMesh;

                // Only mesh should be reverted
                Assert.That(smr.sharedMesh, Is.SameAs(originalMesh), "Mesh should be restored");
                Assert.That(smr.enabled, Is.EqualTo(originalEnabled), "Enabled state should not change");
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void BlendShapeTestMode_MultipleEnterExitCycles_NoStateLeak()
        {
            var fixture = CreateSkinnedFixture("BlendShapeTestMode_MultipleEnterExitCycles_NoStateLeak");
            try
            {
                var deformer = fixture.Deformer;
                var smr = deformer.GetComponent<SkinnedMeshRenderer>();
                var originalMesh = smr.sharedMesh;

                int brushIdx = deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.1f, 0f, 0f));
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "CycleTest";

                for (int cycle = 0; cycle < 5; cycle++)
                {
                    // Enter
                    deformer.InvalidateCache();
                    deformer.Deform(true);
                    var runtimeMesh = smr.sharedMesh;
                    Assert.That(runtimeMesh, Is.Not.SameAs(originalMesh),
                        $"Cycle {cycle}: mesh should change on enter");
                    int idx = runtimeMesh.GetBlendShapeIndex("CycleTest");
                    Assert.That(idx, Is.GreaterThanOrEqualTo(0),
                        $"Cycle {cycle}: BlendShape should exist");
                    smr.SetBlendShapeWeight(idx, 80f);

                    // Exit
                    smr.sharedMesh = originalMesh;
                    Assert.That(smr.sharedMesh, Is.SameAs(originalMesh),
                        $"Cycle {cycle}: mesh should restore on exit");
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void BlendShapeTestMode_MultiplePreExistingBlendShapes_AllWeightsPreserved()
        {
            var root = new GameObject("BlendShapeTestMode_MultiplePreExistingBlendShapes");
            try
            {
                var smr = root.AddComponent<SkinnedMeshRenderer>();
                var sourceMesh = CreateRuntimeCubeMesh();
                sourceMesh.bindposes = new[] { Matrix4x4.identity };
                var bw = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                var boneWeights = new BoneWeight[sourceMesh.vertexCount];
                for (int i = 0; i < boneWeights.Length; i++) boneWeights[i] = bw;
                sourceMesh.boneWeights = boneWeights;

                // Add 5 pre-existing BlendShapes with different weights
                int vertexCount = sourceMesh.vertexCount;
                string[] names = { "Smile", "Blink", "Angry", "Sad", "Surprised" };
                float[] weights = { 100f, 50f, 25f, 75f, 10f };
                for (int s = 0; s < names.Length; s++)
                {
                    var delta = new Vector3[vertexCount];
                    delta[0] = new Vector3(0.01f * (s + 1), 0f, 0f);
                    sourceMesh.AddBlendShapeFrame(names[s], 100f, delta, null, null);
                }

                smr.sharedMesh = sourceMesh;
                smr.bones = new[] { root.transform };
                for (int s = 0; s < names.Length; s++)
                {
                    smr.SetBlendShapeWeight(s, weights[s]);
                }

                var deformer = root.AddComponent<LatticeDeformer>();
                deformer.Reset();
                deformer.Deform(false);

                int brushIdx = deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.5f, 0f, 0f));
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "TestBS";

                // --- Enter test mode ---
                var preTestMesh = smr.sharedMesh;
                deformer.InvalidateCache();
                deformer.Deform(true);

                // Set test weight
                var runtimeMesh = smr.sharedMesh;
                int testIdx = runtimeMesh.GetBlendShapeIndex("TestBS");
                smr.SetBlendShapeWeight(testIdx, 60f);

                // --- Exit test mode ---
                smr.sharedMesh = preTestMesh;

                // ALL pre-existing weights should be preserved
                for (int s = 0; s < names.Length; s++)
                {
                    Assert.That(smr.GetBlendShapeWeight(s), Is.EqualTo(weights[s]).Within(0.01f),
                        $"Weight for '{names[s]}' should be preserved");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BlendShapeTestMode_AddLayerDuringTest_SurvivesExit()
        {
            var fixture = CreateSkinnedFixture("BlendShapeTestMode_AddLayerDuringTest_SurvivesExit");
            try
            {
                var deformer = fixture.Deformer;
                var smr = deformer.GetComponent<SkinnedMeshRenderer>();

                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "AddLayerTest";

                int initialLayerCount = deformer.Layers.Count;

                // --- Enter test mode ---
                var preTestMesh = smr.sharedMesh;
                deformer.InvalidateCache();
                deformer.Deform(true);

                // During test mode, add a new layer and edit it
                int newBrush = deformer.AddLayer("AddedDuringTest", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = newBrush;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0f, 0.3f, 0f));
                deformer.Layers[newBrush].Weight = 0.6f;

                // --- Exit test mode ---
                smr.sharedMesh = preTestMesh;

                // Layer added during test mode should survive
                Assert.That(deformer.Layers.Count, Is.EqualTo(initialLayerCount + 1));
                Assert.That(deformer.Layers[newBrush].Name, Is.EqualTo("AddedDuringTest"));
                Assert.That(deformer.Layers[newBrush].Weight, Is.EqualTo(0.6f).Within(Epsilon));
                AssertApproximately(new Vector3(0f, 0.3f, 0f),
                    deformer.Layers[newBrush].GetBrushDisplacement(0), Epsilon);

                // Deform should still work with the new layer
                ReleaseRuntimeMesh(deformer);
                deformer.BlendShapeOutput = BlendShapeOutputMode.Disabled;
                var result = deformer.Deform(false);
                Assert.That(result, Is.Not.Null);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void BlendShapeTestMode_ChangeCurveDuringTest_ThenReDeform_Correct()
        {
            var fixture = CreateSkinnedFixture("BlendShapeTestMode_ChangeCurveDuringTest_ThenReDeform_Correct");
            try
            {
                var deformer = fixture.Deformer;
                var smr = deformer.GetComponent<SkinnedMeshRenderer>();

                int brushIdx = deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();
                var disp = new Vector3(1f, 0f, 0f);
                deformer.SetDisplacement(0, disp);
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "CurveChange";

                // --- Enter with linear curve ---
                deformer.BlendShapeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
                deformer.InvalidateCache();
                deformer.Deform(true);

                var runtimeMesh = smr.sharedMesh;
                int shapeIdx = runtimeMesh.GetBlendShapeIndex("CurveChange");
                int vertexCount = runtimeMesh.vertexCount;

                // Read midpoint: linear should be ~0.5
                var midDeltas1 = new Vector3[vertexCount];
                runtimeMesh.GetBlendShapeFrameVertices(shapeIdx, 49, midDeltas1,
                    new Vector3[vertexCount], new Vector3[vertexCount]);
                float linearMid = midDeltas1[0].x;
                Assert.That(linearMid, Is.EqualTo(0.5f).Within(0.02f));

                // --- Change curve to ease-in (quadratic) during test mode ---
                deformer.BlendShapeCurve = new AnimationCurve(
                    new Keyframe(0f, 0f, 0f, 0f),
                    new Keyframe(1f, 1f, 2f, 0f));
                deformer.InvalidateCache();
                deformer.Deform(true);

                // Re-read midpoint: ease-in at t=0.5 should be < 0.5
                runtimeMesh = smr.sharedMesh;
                shapeIdx = runtimeMesh.GetBlendShapeIndex("CurveChange");
                var midDeltas2 = new Vector3[vertexCount];
                runtimeMesh.GetBlendShapeFrameVertices(shapeIdx, 49, midDeltas2,
                    new Vector3[vertexCount], new Vector3[vertexCount]);
                float easeInMid = midDeltas2[0].x;
                Assert.That(easeInMid, Is.LessThan(linearMid),
                    "Ease-in curve at midpoint should produce less than linear");

                // Last frame should still be full delta regardless of curve
                int lastFrame = runtimeMesh.GetBlendShapeFrameCount(shapeIdx) - 1;
                var lastDeltas = new Vector3[vertexCount];
                runtimeMesh.GetBlendShapeFrameVertices(shapeIdx, lastFrame, lastDeltas,
                    new Vector3[vertexCount], new Vector3[vertexCount]);
                AssertApproximately(disp, lastDeltas[0], 0.01f);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void BlendShapeTestMode_ToggleOutputModeDuringTest_WorksCorrectly()
        {
            var fixture = CreateSkinnedFixture("BlendShapeTestMode_ToggleOutputModeDuringTest");
            try
            {
                var deformer = fixture.Deformer;
                var smr = deformer.GetComponent<SkinnedMeshRenderer>();

                int brushIdx = deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();
                var disp = new Vector3(0.2f, 0f, 0f);
                deformer.SetDisplacement(0, disp);

                // Start with BS output enabled
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "ToggleTest";

                var preTestMesh = smr.sharedMesh;
                deformer.InvalidateCache();
                deformer.Deform(true);

                // Vertices should be at source (BS mode)
                var sourceVerts = deformer.SourceMesh.vertices;
                var bsVerts = smr.sharedMesh.vertices;
                AssertApproximately(sourceVerts[0], bsVerts[0], 1e-3f);

                // Switch to Disabled during test
                deformer.BlendShapeOutput = BlendShapeOutputMode.Disabled;
                deformer.InvalidateCache();
                deformer.Deform(true);

                // Vertices should now be deformed directly
                var directVerts = smr.sharedMesh.vertices;
                var expectedDirect = sourceVerts[0] + disp;
                AssertApproximately(expectedDirect, directVerts[0], 2e-3f);

                // Switch back to BS output
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.InvalidateCache();
                deformer.Deform(true);

                var bsVerts2 = smr.sharedMesh.vertices;
                AssertApproximately(sourceVerts[0], bsVerts2[0], 1e-3f);

                // Exit: restore
                smr.sharedMesh = preTestMesh;
                Assert.That(smr.sharedMesh, Is.SameAs(preTestMesh));
                // Output mode change during test should be preserved
                Assert.That(deformer.BlendShapeOutput, Is.EqualTo(BlendShapeOutputMode.OutputAsBlendShape));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void BlendShapeTestMode_MixedLayerStack_CombinedDeltaCorrect()
        {
            var root = new GameObject("BlendShapeTestMode_MixedLayerStack_CombinedDeltaCorrect");
            try
            {
                var smr = root.AddComponent<SkinnedMeshRenderer>();
                var sourceMesh = CreateRuntimeCubeMesh();
                sourceMesh.bindposes = new[] { Matrix4x4.identity };
                var bw = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                var boneWeights = new BoneWeight[sourceMesh.vertexCount];
                for (int i = 0; i < boneWeights.Length; i++) boneWeights[i] = bw;
                sourceMesh.boneWeights = boneWeights;

                // Pre-existing BlendShape
                var smileDelta = new Vector3[sourceMesh.vertexCount];
                smileDelta[0] = new Vector3(0f, 0.5f, 0f);
                sourceMesh.AddBlendShapeFrame("Smile", 100f, smileDelta, null, null);

                smr.sharedMesh = sourceMesh;
                smr.bones = new[] { root.transform };
                smr.SetBlendShapeWeight(0, 40f); // Smile at 40%

                var deformer = root.AddComponent<LatticeDeformer>();
                deformer.Reset();
                deformer.Deform(false);

                // Layer 1: Brush with mask
                int brush1 = deformer.AddLayer("BrushMasked", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brush1;
                deformer.EnsureDisplacementCapacity();
                var disp1 = new Vector3(0.2f, 0f, 0f);
                deformer.SetDisplacement(0, disp1);
                deformer.Layers[brush1].Weight = 0.8f;
                deformer.Layers[brush1].EnsureVertexMaskCapacity(sourceMesh.vertexCount);
                deformer.Layers[brush1].SetVertexMask(0, 0.5f); // half masked

                // Layer 2: Brush full weight
                int brush2 = deformer.AddLayer("BrushFull", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brush2;
                deformer.EnsureDisplacementCapacity();
                var disp2 = new Vector3(0f, 0f, 0.3f);
                deformer.SetDisplacement(0, disp2);

                // Enable BS output
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "MixedStack";

                // --- Enter test mode ---
                var preTestMesh = smr.sharedMesh;
                deformer.InvalidateCache();
                deformer.Deform(true);

                var runtimeMesh = smr.sharedMesh;
                int mixedIdx = runtimeMesh.GetBlendShapeIndex("MixedStack");
                Assert.That(mixedIdx, Is.GreaterThanOrEqualTo(0));

                // Check the last frame has the correct combined delta
                int frameCount = runtimeMesh.GetBlendShapeFrameCount(mixedIdx);
                var lastDeltas = new Vector3[runtimeMesh.vertexCount];
                runtimeMesh.GetBlendShapeFrameVertices(mixedIdx, frameCount - 1, lastDeltas,
                    new Vector3[runtimeMesh.vertexCount], new Vector3[runtimeMesh.vertexCount]);

                // Expected: brush1 contribution = disp1 * weight(0.8) * mask(0.5) = (0.08, 0, 0)
                //           brush2 contribution = disp2 * weight(1.0) * mask(1.0) = (0, 0, 0.3)
                //           combined = (0.08, 0, 0.3)
                var expectedDelta = disp1 * 0.8f * 0.5f + disp2;
                AssertApproximately(expectedDelta, lastDeltas[0], 2e-3f);

                // Pre-existing "Smile" should also be preserved on runtime mesh
                int smileIdx = runtimeMesh.GetBlendShapeIndex("Smile");
                Assert.That(smileIdx, Is.GreaterThanOrEqualTo(0));

                // --- Exit ---
                smr.sharedMesh = preTestMesh;

                // Pre-existing Smile weight should survive
                Assert.That(smr.GetBlendShapeWeight(0), Is.EqualTo(40f).Within(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void DisabledLayerBetweenEnabledLayers_SkippedCorrectly()
        {
            var fixture = CreateFixture("DisabledLayerBetweenEnabledLayers_SkippedCorrectly");
            try
            {
                var deformer = fixture.Deformer;

                // Layer A: enabled
                int layerA = deformer.AddLayer("A", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = layerA;
                deformer.EnsureDisplacementCapacity();
                var dispA = new Vector3(0.1f, 0f, 0f);
                deformer.SetDisplacement(0, dispA);

                // Layer B: disabled (should be skipped)
                int layerB = deformer.AddLayer("B", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = layerB;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0f, 99f, 0f));
                deformer.Layers[layerB].Enabled = false;

                // Layer C: enabled
                int layerC = deformer.AddLayer("C", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = layerC;
                deformer.EnsureDisplacementCapacity();
                var dispC = new Vector3(0f, 0f, 0.2f);
                deformer.SetDisplacement(0, dispC);

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);
                var sourceVertices = deformer.SourceMesh.vertices;

                // Only A + C should contribute
                var expected = sourceVertices[0] + dispA + dispC;
                AssertApproximately(expected, runtimeMesh.vertices[0], 2e-3f);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        private static TestFixture CreateFixture(string name)
        {
            var root = new GameObject(name);
            var filter = root.AddComponent<MeshFilter>();
            root.AddComponent<MeshRenderer>();

            var sourceMesh = CreateRuntimeCubeMesh();
            filter.sharedMesh = sourceMesh;

            var deformer = root.AddComponent<LatticeDeformer>();
            deformer.Reset();
            var warmupMesh = deformer.Deform(false);
            Assert.That(warmupMesh, Is.Not.Null);

            return new TestFixture(root, sourceMesh, deformer);
        }

        private static TestFixture CreateSkinnedFixture(string name)
        {
            var root = new GameObject(name);
            var smr = root.AddComponent<SkinnedMeshRenderer>();

            var sourceMesh = CreateRuntimeCubeMesh();
            // SkinnedMeshRenderer needs bindposes and boneWeights for basic setup
            sourceMesh.bindposes = new[] { Matrix4x4.identity };
            var bw = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            var boneWeights = new BoneWeight[sourceMesh.vertexCount];
            for (int i = 0; i < boneWeights.Length; i++) boneWeights[i] = bw;
            sourceMesh.boneWeights = boneWeights;

            smr.sharedMesh = sourceMesh;
            smr.bones = new[] { root.transform };

            var deformer = root.AddComponent<LatticeDeformer>();
            deformer.Reset();
            var warmupMesh = deformer.Deform(false);
            Assert.That(warmupMesh, Is.Not.Null);

            return new TestFixture(root, sourceMesh, deformer);
        }

        private static TestFixture CreateFixtureWithBlendShapes(string name, string[] blendShapeNames)
        {
            var root = new GameObject(name);
            var filter = root.AddComponent<MeshFilter>();
            root.AddComponent<MeshRenderer>();

            var sourceMesh = CreateRuntimeCubeMesh();

            int vertexCount = sourceMesh.vertexCount;
            for (int s = 0; s < blendShapeNames.Length; s++)
            {
                var deltas = new Vector3[vertexCount];
                for (int v = 0; v < vertexCount; v++)
                {
                    deltas[v] = new Vector3(0.01f * (s + 1) * (v + 1), 0.02f * (s + 1), -0.005f * (v + 1));
                }

                sourceMesh.AddBlendShapeFrame(blendShapeNames[s], 100f, deltas, null, null);
            }

            filter.sharedMesh = sourceMesh;

            var deformer = root.AddComponent<LatticeDeformer>();
            deformer.Reset();
            var warmupMesh = deformer.Deform(false);
            Assert.That(warmupMesh, Is.Not.Null);

            return new TestFixture(root, sourceMesh, deformer);
        }

        private static TestFixture CreateFixtureWithSymmetricMesh(string name)
        {
            var root = new GameObject(name);
            var filter = root.AddComponent<MeshFilter>();
            root.AddComponent<MeshRenderer>();

            // Create a simple mesh with symmetric vertex positions across X axis
            var sourceMesh = new Mesh { name = "SymmetricTestMesh" };
            sourceMesh.vertices = new[]
            {
                new Vector3( 1f, 0f, 0f), // 0: positive X
                new Vector3(-1f, 0f, 0f), // 1: negative X (mirror of 0)
                new Vector3( 0f, 1f, 0f), // 2: on axis (self-mirror)
                new Vector3( 0f,-1f, 0f)  // 3: on axis (self-mirror)
            };
            sourceMesh.triangles = new[] { 0, 2, 1, 1, 3, 0 };
            sourceMesh.RecalculateNormals();
            sourceMesh.RecalculateBounds();

            filter.sharedMesh = sourceMesh;

            var deformer = root.AddComponent<LatticeDeformer>();
            deformer.Reset();
            var warmupMesh = deformer.Deform(false);
            Assert.That(warmupMesh, Is.Not.Null);

            return new TestFixture(root, sourceMesh, deformer);
        }

        private static void ReleaseRuntimeMesh(LatticeDeformer deformer)
        {
            var field = typeof(LatticeDeformer).GetField("_runtimeMesh", s_privateInstance);
            Assert.That(field, Is.Not.Null, "Private field not found: _runtimeMesh");
            var mesh = field.GetValue(deformer) as Mesh;
            if (mesh != null)
            {
                Object.DestroyImmediate(mesh);
            }

            field.SetValue(deformer, null);
        }

        private static Mesh CreateRuntimeCubeMesh()
        {
            var tempPrimitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var source = tempPrimitive.GetComponent<MeshFilter>().sharedMesh;
            var mesh = Object.Instantiate(source);
            mesh.name = "LatticeLayerStackTestMesh";
            Object.DestroyImmediate(tempPrimitive);
            return mesh;
        }

        private static LatticeAsset CloneSettings(LatticeAsset source)
        {
            var cloned = new LatticeAsset();
            if (source == null)
            {
                cloned.EnsureInitialized();
                return cloned;
            }

            cloned.GridSize = source.GridSize;
            cloned.LocalBounds = source.LocalBounds;
            cloned.Interpolation = source.Interpolation;
            cloned.EnsureInitialized();

            int count = Mathf.Min(cloned.ControlPointCount, source.ControlPointCount);
            for (int i = 0; i < count; i++)
            {
                cloned.SetControlPointLocal(i, source.GetControlPointLocal(i));
            }

            return cloned;
        }

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            var field = instance.GetType().GetField(fieldName, s_privateInstance);
            Assert.That(field, Is.Not.Null, $"Private field not found: {fieldName}");
            field.SetValue(instance, value);
        }

        private static int FindCornerVertexIndex(Vector3[] vertices, Vector3 corner)
        {
            if (vertices == null)
            {
                return -1;
            }

            const float cornerEpsilonSq = 1e-6f;
            for (int i = 0; i < vertices.Length; i++)
            {
                if ((vertices[i] - corner).sqrMagnitude <= cornerEpsilonSq)
                {
                    return i;
                }
            }

            return -1;
        }

        private static Vector3 GetNeutralControlPoint(Bounds bounds, Vector3Int grid, int index)
        {
            int nx = Mathf.Max(1, grid.x);
            int ny = Mathf.Max(1, grid.y);
            int nz = Mathf.Max(1, grid.z);

            int plane = nx * ny;
            int z = index / plane;
            int y = (index / nx) % ny;
            int x = index % nx;

            float wx = nx > 1 ? (float)x / (nx - 1) : 0f;
            float wy = ny > 1 ? (float)y / (ny - 1) : 0f;
            float wz = nz > 1 ? (float)z / (nz - 1) : 0f;

            return bounds.min + Vector3.Scale(bounds.size, new Vector3(wx, wy, wz));
        }

        private static void AssertApproximately(Vector3 expected, Vector3 actual, float tolerance = Epsilon)
        {
            float toleranceSq = tolerance * tolerance;
            Assert.That((expected - actual).sqrMagnitude, Is.LessThanOrEqualTo(toleranceSq),
                $"Expected {expected} but got {actual}");
        }

        // ── DeformerGroup Tests ──────────────────────────────────

        [Test]
        public void DefaultComponent_HasOneGroup()
        {
            var fixture = CreateFixture("DefaultComponent_HasOneGroup");
            try
            {
                var deformer = fixture.Deformer;
                Assert.That(deformer.Groups.Count, Is.EqualTo(1));
                Assert.That(deformer.ActiveGroupIndex, Is.EqualTo(0));
                Assert.That(deformer.ActiveGroup, Is.Not.Null);
                Assert.That(deformer.ActiveGroup.Layers.Count, Is.GreaterThanOrEqualTo(1));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void AddGroup_CreatesNewEmptyGroup()
        {
            var fixture = CreateFixture("AddGroup_CreatesNewEmptyGroup");
            try
            {
                var deformer = fixture.Deformer;
                int idx = deformer.AddGroup("Group B");
                Assert.That(deformer.Groups.Count, Is.EqualTo(2));
                Assert.That(deformer.ActiveGroupIndex, Is.EqualTo(idx));
                Assert.That(deformer.ActiveGroup.Name, Is.EqualTo("Group B"));
                Assert.That(deformer.ActiveGroup.Layers.Count, Is.EqualTo(0));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void RemoveGroup_CannotRemoveLastGroup()
        {
            var fixture = CreateFixture("RemoveGroup_CannotRemoveLastGroup");
            try
            {
                var deformer = fixture.Deformer;
                Assert.That(deformer.Groups.Count, Is.EqualTo(1));
                bool removed = deformer.RemoveGroup(0);
                Assert.That(removed, Is.False);
                Assert.That(deformer.Groups.Count, Is.EqualTo(1));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void RemoveGroup_RemovesCorrectGroup()
        {
            var fixture = CreateFixture("RemoveGroup_RemovesCorrectGroup");
            try
            {
                var deformer = fixture.Deformer;
                deformer.AddGroup("Group B");
                deformer.AddGroup("Group C");
                Assert.That(deformer.Groups.Count, Is.EqualTo(3));

                deformer.ActiveGroupIndex = 0;
                bool removed = deformer.RemoveGroup(1); // Remove "Group B"
                Assert.That(removed, Is.True);
                Assert.That(deformer.Groups.Count, Is.EqualTo(2));
                Assert.That(deformer.Groups[1].Name, Is.EqualTo("Group C"));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void FacadeAPI_DelegatesToActiveGroup()
        {
            var fixture = CreateFixture("FacadeAPI_DelegatesToActiveGroup");
            try
            {
                var deformer = fixture.Deformer;
                // Group 0 has default layers
                int group0LayerCount = deformer.Layers.Count;

                // Add group 1 with a brush layer
                deformer.AddGroup("Group B");
                deformer.AddLayer("Brush B", MeshDeformerLayerType.Brush);
                Assert.That(deformer.Layers.Count, Is.EqualTo(1)); // Group B has 1 layer
                Assert.That(deformer.ActiveLayerType, Is.EqualTo(MeshDeformerLayerType.Brush));

                // Switch back to group 0
                deformer.ActiveGroupIndex = 0;
                Assert.That(deformer.Layers.Count, Is.EqualTo(group0LayerCount));
                Assert.That(deformer.ActiveLayerType, Is.EqualTo(MeshDeformerLayerType.Lattice));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void MultipleGroups_DirectDeform_AreAdditive()
        {
            var fixture = CreateFixture("MultipleGroups_DirectDeform_AreAdditive");
            try
            {
                var deformer = fixture.Deformer;
                var sourceVertices = fixture.SourceMesh.vertices;

                // Group 0: brush layer with displacement on vertex 0
                deformer.ActiveGroupIndex = 0;
                int b0 = deformer.AddLayer("Brush0", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = b0;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.1f, 0f, 0f));

                // Group 1: brush layer with displacement on vertex 0
                deformer.AddGroup("Group B");
                int b1 = deformer.AddLayer("Brush1", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = b1;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0f, 0.2f, 0f));

                var mesh = deformer.Deform(false);
                var deformed = mesh.vertices;

                // Both groups contribute additively
                var expected = sourceVertices[0] + new Vector3(0.1f, 0.2f, 0f);
                AssertApproximately(expected, deformed[0], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void MultipleGroups_BlendShapeOutput_ProducesMultipleBlendShapes()
        {
            var fixture = CreateFixture("MultipleGroups_BlendShapeOutput_MultipleBS");
            try
            {
                var deformer = fixture.Deformer;

                // Group 0: BlendShape "ShapeA"
                deformer.ActiveGroupIndex = 0;
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "ShapeA";
                int b0 = deformer.AddLayer("Brush0", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = b0;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.1f, 0f, 0f));

                // Group 1: BlendShape "ShapeB"
                deformer.AddGroup("Group B");
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "ShapeB";
                int b1 = deformer.AddLayer("Brush1", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = b1;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0f, 0.2f, 0f));

                var mesh = deformer.Deform(false);
                int idxA = mesh.GetBlendShapeIndex("ShapeA");
                int idxB = mesh.GetBlendShapeIndex("ShapeB");
                Assert.That(idxA, Is.GreaterThanOrEqualTo(0), "ShapeA BlendShape should exist");
                Assert.That(idxB, Is.GreaterThanOrEqualTo(0), "ShapeB BlendShape should exist");
                Assert.That(idxA, Is.Not.EqualTo(idxB));

                // Verify deltas: last frame (index 99) has full delta
                int vertexCount = mesh.vertexCount;
                var deltasA = new Vector3[vertexCount];
                mesh.GetBlendShapeFrameVertices(idxA, 99, deltasA, null, null);
                AssertApproximately(new Vector3(0.1f, 0f, 0f), deltasA[0], 2e-3f);

                var deltasB = new Vector3[vertexCount];
                mesh.GetBlendShapeFrameVertices(idxB, 99, deltasB, null, null);
                AssertApproximately(new Vector3(0f, 0.2f, 0f), deltasB[0], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void MixedGroups_DirectDeformAndBlendShape_WorkTogether()
        {
            var fixture = CreateFixture("MixedGroups_DirectAndBS");
            try
            {
                var deformer = fixture.Deformer;
                var sourceVertices = fixture.SourceMesh.vertices;

                // Group 0: Direct deform
                deformer.ActiveGroupIndex = 0;
                deformer.BlendShapeOutput = BlendShapeOutputMode.Disabled;
                int b0 = deformer.AddLayer("Direct", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = b0;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.1f, 0f, 0f));

                // Group 1: BlendShape
                deformer.AddGroup("BS Group");
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "Mixed";
                int b1 = deformer.AddLayer("BSBrush", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = b1;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0f, 0.2f, 0f));

                var mesh = deformer.Deform(false);
                var deformed = mesh.vertices;

                // Direct deform group contributes to vertices
                var expectedDirect = sourceVertices[0] + new Vector3(0.1f, 0f, 0f);
                AssertApproximately(expectedDirect, deformed[0], 2e-3f);

                // BlendShape group produces a BlendShape, not vertex change
                int bsIdx = mesh.GetBlendShapeIndex("Mixed");
                Assert.That(bsIdx, Is.GreaterThanOrEqualTo(0));
                var bsDeltas = new Vector3[mesh.vertexCount];
                mesh.GetBlendShapeFrameVertices(bsIdx, 99, bsDeltas, null, null);
                AssertApproximately(new Vector3(0f, 0.2f, 0f), bsDeltas[0], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void DisabledGroup_DoesNotContribute()
        {
            var fixture = CreateFixture("DisabledGroup_DoesNotContribute");
            try
            {
                var deformer = fixture.Deformer;
                var sourceVertices = fixture.SourceMesh.vertices;

                // Group 0: brush
                deformer.ActiveGroupIndex = 0;
                int b0 = deformer.AddLayer("Brush0", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = b0;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.5f, 0f, 0f));

                // Disable the group
                deformer.ActiveGroup.Enabled = false;

                var mesh = deformer.Deform(false);
                var deformed = mesh.vertices;

                // Vertex should be unchanged since group is disabled
                AssertApproximately(sourceVertices[0], deformed[0], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void LegacyMigration_V0ToV3_PreservesData()
        {
            var fixture = CreateFixture("LegacyMigration_V0ToV3");
            try
            {
                var deformer = fixture.Deformer;
                var legacySettings = CloneSettings(deformer.Settings);
                const int cpIdx = 0;
                var movedPoint = legacySettings.GetControlPointLocal(cpIdx) + new Vector3(0.05f, 0.1f, 0f);
                legacySettings.SetControlPointLocal(cpIdx, movedPoint);

                // Simulate v0 state
                SetPrivateField(deformer, "_settings", legacySettings);
                SetPrivateField(deformer, "_layers", new List<LatticeLayer>());
                SetPrivateField(deformer, "_groups", new List<DeformerGroup>());
                SetPrivateField(deformer, "_activeLayerIndex", -1);
                SetPrivateField(deformer, "_layerModelVersion", 0);

                deformer.enabled = false;
                deformer.enabled = true;

                // Should have migrated v0→v2→v3: one group, one layer
                Assert.That(deformer.Groups.Count, Is.EqualTo(1));
                Assert.That(deformer.Layers.Count, Is.EqualTo(1));
                AssertApproximately(movedPoint, deformer.Layers[0].Settings.GetControlPointLocal(cpIdx), Epsilon);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void LegacyMigration_V2ToV3_PreservesBlendShapeSettings()
        {
            var fixture = CreateFixture("LegacyMigration_V2ToV3_BS");
            try
            {
                var deformer = fixture.Deformer;

                // Simulate v2 state: layers exist, BlendShape at component level
                var layers = new List<LatticeLayer>();
                var brushLayer = new LatticeLayer();
                brushLayer.Name = "TestBrush";
                SetPrivateField(brushLayer, "_type", MeshDeformerLayerType.Brush);
                layers.Add(brushLayer);

                SetPrivateField(deformer, "_layers", layers);
                SetPrivateField(deformer, "_groups", new List<DeformerGroup>());
                SetPrivateField(deformer, "_activeLayerIndex", 0);
                SetPrivateField(deformer, "_layerModelVersion", 2);
                SetPrivateField(deformer, "_blendShapeOutput", BlendShapeOutputMode.OutputAsBlendShape);
                SetPrivateField(deformer, "_blendShapeName", "MigratedShape");

                deformer.enabled = false;
                deformer.enabled = true;

                // Should have migrated v2→v3
                Assert.That(deformer.Groups.Count, Is.EqualTo(1));
                Assert.That(deformer.ActiveGroup.BlendShapeOutput, Is.EqualTo(BlendShapeOutputMode.OutputAsBlendShape));
                Assert.That(deformer.ActiveGroup.BlendShapeName, Is.EqualTo("MigratedShape"));
                Assert.That(deformer.Layers.Count, Is.EqualTo(1));
                Assert.That(deformer.Layers[0].Name, Is.EqualTo("TestBrush"));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void GroupBlendShapeOutput_PerGroupSettings()
        {
            var fixture = CreateFixture("GroupBlendShapeOutput_PerGroup");
            try
            {
                var deformer = fixture.Deformer;

                // Group 0: Disabled
                deformer.ActiveGroupIndex = 0;
                deformer.BlendShapeOutput = BlendShapeOutputMode.Disabled;

                // Group 1: OutputAsBlendShape
                deformer.AddGroup("BS Group");
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "GroupBS";

                // Verify settings are per-group
                deformer.ActiveGroupIndex = 0;
                Assert.That(deformer.BlendShapeOutput, Is.EqualTo(BlendShapeOutputMode.Disabled));

                deformer.ActiveGroupIndex = 1;
                Assert.That(deformer.BlendShapeOutput, Is.EqualTo(BlendShapeOutputMode.OutputAsBlendShape));
                Assert.That(deformer.BlendShapeName, Is.EqualTo("GroupBS"));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void GroupStateHash_ChangesWhenGroupModified()
        {
            var fixture = CreateFixture("GroupStateHash_Changes");
            try
            {
                var deformer = fixture.Deformer;
                int hash1 = deformer.ComputeLayeredStateHash();

                deformer.AddGroup("New Group");
                int hash2 = deformer.ComputeLayeredStateHash();
                Assert.That(hash2, Is.Not.EqualTo(hash1), "Hash should change when group added");

                deformer.ActiveGroup.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                int hash3 = deformer.ComputeLayeredStateHash();
                Assert.That(hash3, Is.Not.EqualTo(hash2), "Hash should change when group BS mode changed");
            }
            finally { fixture.Dispose(); }
        }

        // ── Group Copy / Paste / Duplicate Tests ──────────────

        [Test]
        public void DuplicateGroup_CreatesIndependentCopy()
        {
            var fixture = CreateFixture("DuplicateGroup_IndependentCopy");
            try
            {
                var deformer = fixture.Deformer;

                // Setup group 0 with a brush layer displacement
                deformer.ActiveGroupIndex = 0;
                int b = deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = b;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.5f, 0f, 0f));
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "OrigShape";

                // Duplicate via JSON (same as editor DuplicateGroup)
                var srcGroup = deformer.Groups[0];
                string json = JsonUtility.ToJson(srcGroup);
                var newGroup = new DeformerGroup();
                JsonUtility.FromJsonOverwrite(json, newGroup);
                newGroup.Name = "Group Copy";

                var groupsField = typeof(LatticeDeformer).GetField("_groups", s_privateInstance);
                var groupsList = groupsField.GetValue(deformer) as List<DeformerGroup>;
                groupsList.Add(newGroup);
                deformer.ActiveGroupIndex = 1;

                // Verify copy has same data
                Assert.That(deformer.Groups.Count, Is.EqualTo(2));
                Assert.That(deformer.Groups[1].Name, Is.EqualTo("Group Copy"));
                Assert.That(deformer.Groups[1].BlendShapeOutput, Is.EqualTo(BlendShapeOutputMode.OutputAsBlendShape));
                Assert.That(deformer.Groups[1].BlendShapeName, Is.EqualTo("OrigShape"));
                Assert.That(deformer.Layers.Count, Is.GreaterThanOrEqualTo(2)); // original layers duplicated

                // Modify original — copy should NOT change
                deformer.ActiveGroupIndex = 0;
                deformer.ActiveLayerIndex = b;
                deformer.SetDisplacement(0, new Vector3(9f, 9f, 9f));

                deformer.ActiveGroupIndex = 1;
                // Find the brush layer in the copy
                int copyBrushIdx = -1;
                for (int i = 0; i < deformer.Layers.Count; i++)
                    if (deformer.Layers[i].Type == MeshDeformerLayerType.Brush) { copyBrushIdx = i; break; }
                Assert.That(copyBrushIdx, Is.GreaterThanOrEqualTo(0));
                var copyDisp = deformer.Layers[copyBrushIdx].GetBrushDisplacement(0);
                AssertApproximately(new Vector3(0.5f, 0f, 0f), copyDisp, Epsilon);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void CopyPasteGroup_RoundTrip()
        {
            var fixture = CreateFixture("CopyPasteGroup_RoundTrip");
            try
            {
                var deformer = fixture.Deformer;

                // Setup group 0
                deformer.ActiveGroupIndex = 0;
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "CopiedBS";
                int b = deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = b;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.3f, 0.4f, 0f));

                // Copy group 0
                string copiedJson = JsonUtility.ToJson(deformer.Groups[0]);
                Assert.That(string.IsNullOrEmpty(copiedJson), Is.False);

                // Paste as new group
                var pastedGroup = new DeformerGroup();
                JsonUtility.FromJsonOverwrite(copiedJson, pastedGroup);

                var groupsField = typeof(LatticeDeformer).GetField("_groups", s_privateInstance);
                var groupsList = groupsField.GetValue(deformer) as List<DeformerGroup>;
                groupsList.Add(pastedGroup);
                deformer.ActiveGroupIndex = 1;

                // Verify pasted group
                Assert.That(deformer.Groups.Count, Is.EqualTo(2));
                Assert.That(deformer.BlendShapeOutput, Is.EqualTo(BlendShapeOutputMode.OutputAsBlendShape));
                Assert.That(deformer.BlendShapeName, Is.EqualTo("CopiedBS"));

                int pastedBrush = -1;
                for (int i = 0; i < deformer.Layers.Count; i++)
                    if (deformer.Layers[i].Type == MeshDeformerLayerType.Brush) { pastedBrush = i; break; }
                Assert.That(pastedBrush, Is.GreaterThanOrEqualTo(0));
                var disp = deformer.Layers[pastedBrush].GetBrushDisplacement(0);
                AssertApproximately(new Vector3(0.3f, 0.4f, 0f), disp, Epsilon);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void CopyPasteGroup_ProducesIndependentBlendShapes()
        {
            var fixture = CreateFixture("CopyPasteGroup_IndependentBS");
            try
            {
                var deformer = fixture.Deformer;

                // Group 0: BlendShape "A"
                deformer.ActiveGroupIndex = 0;
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "ShapeA";
                int b0 = deformer.AddLayer("B0", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = b0;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.1f, 0f, 0f));

                // Copy and paste group, rename BlendShape
                string json = JsonUtility.ToJson(deformer.Groups[0]);
                var pastedGroup = new DeformerGroup();
                JsonUtility.FromJsonOverwrite(json, pastedGroup);
                pastedGroup.BlendShapeName = "ShapeB";

                var groupsField = typeof(LatticeDeformer).GetField("_groups", s_privateInstance);
                (groupsField.GetValue(deformer) as List<DeformerGroup>).Add(pastedGroup);

                // Deform and verify two separate BlendShapes
                var mesh = deformer.Deform(false);
                int idxA = mesh.GetBlendShapeIndex("ShapeA");
                int idxB = mesh.GetBlendShapeIndex("ShapeB");
                Assert.That(idxA, Is.GreaterThanOrEqualTo(0));
                Assert.That(idxB, Is.GreaterThanOrEqualTo(0));
                Assert.That(idxA, Is.Not.EqualTo(idxB));
            }
            finally { fixture.Dispose(); }
        }

        private sealed class TestFixture
        {
            public GameObject Root { get; }
            public Mesh SourceMesh { get; }
            public LatticeDeformer Deformer { get; }

            public TestFixture(GameObject root, Mesh sourceMesh, LatticeDeformer deformer)
            {
                Root = root;
                SourceMesh = sourceMesh;
                Deformer = deformer;
            }

            public void Dispose()
            {
                if (Root != null)
                {
                    Object.DestroyImmediate(Root);
                }

                if (SourceMesh != null)
                {
                    Object.DestroyImmediate(SourceMesh);
                }
            }
        }
    }
}
#endif
