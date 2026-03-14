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
                layer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                layer.BlendShapeName = "TestShape";
                layer.Weight = weight;

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
                runtimeMesh.GetBlendShapeFrameVertices(shapeIndex, 0, frameDeltas, frameNormals, frameTangents);

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
                layer.BlendShapeOutput = BlendShapeOutputMode.Disabled;
                layer.Weight = weight;

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

                // Output as BlendShape
                importedLayer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                importedLayer.BlendShapeName = "ModifiedShape";

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
                runtimeMesh.GetBlendShapeFrameVertices(outputIndex, 0, outputDeltas, null, null);

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
        public void MultipleBlendShapeOutputLayers_ProduceSeparateFrames()
        {
            var fixture = CreateFixture("MultipleBlendShapeOutputLayers_ProduceSeparateFrames");
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
                deformer.Layers[layerA].BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;

                // Layer B
                int layerB = deformer.AddLayer("ShapeB", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = layerB;
                deformer.EnsureDisplacementCapacity();
                var deltaB = new Vector3(0f, 0.2f, 0f);
                deformer.SetDisplacement(0, deltaB);
                deformer.Layers[layerB].BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);

                int sourceCount = deformer.SourceMesh.blendShapeCount;
                Assert.That(runtimeMesh.blendShapeCount, Is.EqualTo(sourceCount + 2),
                    "Should have 2 additional BlendShapes");

                // Verify each BlendShape has independent deltas
                var frameA = new Vector3[vertexCount];
                var frameB = new Vector3[vertexCount];
                runtimeMesh.GetBlendShapeFrameVertices(sourceCount, 0, frameA, null, null);
                runtimeMesh.GetBlendShapeFrameVertices(sourceCount + 1, 0, frameB, null, null);

                AssertApproximately(deltaA, frameA[0], 2e-3f);
                AssertApproximately(Vector3.zero, frameA[1], 2e-3f);
                AssertApproximately(deltaB, frameB[0], 2e-3f);
                AssertApproximately(Vector3.zero, frameB[1], 2e-3f);

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
        public void BlendShapeName_FallsBackToLayerName()
        {
            var fixture = CreateFixture("BlendShapeName_FallsBackToLayerName");
            try
            {
                var deformer = fixture.Deformer;

                int layerIndex = deformer.AddLayer("MyLayerName", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = layerIndex;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.1f, 0f, 0f));

                var layer = deformer.Layers[layerIndex];
                layer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                layer.BlendShapeName = ""; // Empty — should fall back to layer Name

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);

                int lastIndex = runtimeMesh.blendShapeCount - 1;
                Assert.That(runtimeMesh.GetBlendShapeName(lastIndex), Is.EqualTo("MyLayerName"));
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
                layer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;

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
                runtimeMesh.GetBlendShapeFrameVertices(shapeIndex, 0, frameDeltas, null, null);

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

                latticeLayer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                latticeLayer.BlendShapeName = "LatticeShape";

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
                runtimeMesh.GetBlendShapeFrameVertices(shapeIndex, 0, frameDeltas, null, null);

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
                layer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);

                int shapeIndex = runtimeMesh.blendShapeCount - 1;
                var frameDeltas = new Vector3[vertexCount];
                runtimeMesh.GetBlendShapeFrameVertices(shapeIndex, 0, frameDeltas, null, null);

                // Delta should be displacement * weight
                AssertApproximately(displacement * weight, frameDeltas[0], 2e-3f);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void DirectAndBlendShapeOutputLayers_ComposeCorrectly()
        {
            var fixture = CreateFixture("DirectAndBlendShapeOutputLayers_ComposeCorrectly");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;

                // Direct layer
                int directLayer = deformer.AddLayer("Direct", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = directLayer;
                deformer.EnsureDisplacementCapacity();
                var directDisp = new Vector3(0.1f, 0f, 0f);
                deformer.SetDisplacement(0, directDisp);

                // BlendShape output layer
                int bsLayer = deformer.AddLayer("BS Output", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = bsLayer;
                deformer.EnsureDisplacementCapacity();
                var bsDisp = new Vector3(0f, 0.2f, 0f);
                deformer.SetDisplacement(0, bsDisp);
                deformer.Layers[bsLayer].BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);

                var sourceVertices = deformer.SourceMesh.vertices;
                var deformedVertices = runtimeMesh.vertices;

                // Direct layer should modify vertices
                var expectedVertex = sourceVertices[0] + directDisp;
                AssertApproximately(expectedVertex, deformedVertices[0], 2e-3f);

                // BS layer should only appear as BlendShape, NOT in vertex data
                int shapeIndex = runtimeMesh.blendShapeCount - 1;
                var frameDeltas = new Vector3[vertexCount];
                runtimeMesh.GetBlendShapeFrameVertices(shapeIndex, 0, frameDeltas, null, null);
                AssertApproximately(bsDisp, frameDeltas[0], 2e-3f);
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
        public void LatticePlusBrushPlusBlendShapeOutput_ThreeLayerComposition()
        {
            var fixture = CreateFixture("LatticePlusBrushPlusBlendShapeOutput_ThreeLayerComposition");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;

                // Layer 0: Lattice (direct)
                var latticeSettings = deformer.Layers[0].Settings;
                var latticeDelta = new Vector3(0f, 0.15f, 0f);
                latticeSettings.SetControlPointLocal(0,
                    latticeSettings.GetControlPointLocal(0) + latticeDelta);

                // Layer 1: Brush (direct)
                int brushDirect = deformer.AddLayer("Brush Direct", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushDirect;
                deformer.EnsureDisplacementCapacity();
                var brushDisp = new Vector3(0.05f, 0f, 0f);
                deformer.SetDisplacement(0, brushDisp);

                // Layer 2: Brush (BlendShape output)
                int brushBS = deformer.AddLayer("Brush BS", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushBS;
                deformer.EnsureDisplacementCapacity();
                var bsDisp = new Vector3(0f, 0f, 0.1f);
                deformer.SetDisplacement(0, bsDisp);
                deformer.Layers[brushBS].BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);

                // Vertex should have lattice + direct brush, but NOT BS brush
                var sourceVertices = deformer.SourceMesh.vertices;
                var deformedVertices = runtimeMesh.vertices;

                // BS layer displacement should NOT be in vertex data
                // (only lattice delta and direct brush should contribute)
                float zDiff = Mathf.Abs(deformedVertices[0].z - sourceVertices[0].z);
                Assert.That(zDiff, Is.LessThan(0.05f),
                    "BlendShape output layer should not modify vertices directly");

                // BS layer should appear as BlendShape frame
                int bsShapeIndex = runtimeMesh.blendShapeCount - 1;
                var frameDeltas = new Vector3[vertexCount];
                runtimeMesh.GetBlendShapeFrameVertices(bsShapeIndex, 0, frameDeltas, null, null);
                AssertApproximately(bsDisp, frameDeltas[0], 2e-3f);
            }
            finally
            {
                fixture.Dispose();
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
