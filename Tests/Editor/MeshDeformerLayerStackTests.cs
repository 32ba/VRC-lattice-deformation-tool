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
