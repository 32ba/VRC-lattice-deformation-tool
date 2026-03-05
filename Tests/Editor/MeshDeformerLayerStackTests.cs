#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
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
