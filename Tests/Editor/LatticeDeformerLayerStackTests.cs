#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class LatticeDeformerLayerStackTests
    {
        private const float Epsilon = 1e-4f;

        [Test]
        public void AddLayer_CreatesNeutralLayerWithoutChangingBase()
        {
            var fixture = CreateFixture("AddLayer_CreatesNeutralLayerWithoutChangingBase");
            try
            {
                var deformer = fixture.Deformer;
                var baseSettings = deformer.Settings;
                var baseSnapshot = baseSettings.ControlPointsLocal.ToArray();

                int layerIndex = deformer.AddLayer("Layer A");

                Assert.That(layerIndex, Is.EqualTo(0));
                Assert.That(deformer.Layers.Count, Is.EqualTo(1));

                var layerSettings = deformer.Layers[0].Settings;
                Assert.That(layerSettings, Is.Not.SameAs(baseSettings));
                Assert.That(layerSettings.GridSize, Is.EqualTo(baseSettings.GridSize));
                Assert.That(layerSettings.Interpolation, Is.EqualTo(baseSettings.Interpolation));
                AssertApproximately(baseSettings.LocalBounds.center, layerSettings.LocalBounds.center);
                AssertApproximately(baseSettings.LocalBounds.size, layerSettings.LocalBounds.size);

                for (int i = 0; i < baseSettings.ControlPointCount; i++)
                {
                    Vector3 neutral = GetNeutralControlPoint(baseSettings.LocalBounds, baseSettings.GridSize, i);
                    AssertApproximately(neutral, layerSettings.GetControlPointLocal(i), Epsilon);
                    AssertApproximately(baseSnapshot[i], baseSettings.GetControlPointLocal(i), Epsilon);
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
                var baseSettings = deformer.Settings;

                deformer.AddLayer("Layer A");
                var layer = deformer.Layers[0];

                const int controlPointIndex = 0;
                var delta = new Vector3(0f, 0.2f, 0f);
                var neutral = layer.Settings.GetControlPointLocal(controlPointIndex);
                layer.Settings.SetControlPointLocal(controlPointIndex, neutral + delta);
                layer.Weight = 0.5f;

                var runtimeMesh = deformer.Deform(false);
                Assert.That(runtimeMesh, Is.Not.Null);
                Assert.That(deformer.SourceMesh, Is.Not.Null);

                var sourceVertices = deformer.SourceMesh.vertices;
                var deformedVertices = runtimeMesh.vertices;

                int minCornerIndex = FindCornerVertexIndex(sourceVertices, baseSettings.LocalBounds.min);
                int maxCornerIndex = FindCornerVertexIndex(sourceVertices, baseSettings.LocalBounds.max);

                Assert.That(minCornerIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(maxCornerIndex, Is.GreaterThanOrEqualTo(0));

                var expectedMoved = sourceVertices[minCornerIndex] + delta * layer.Weight;
                AssertApproximately(expectedMoved, deformedVertices[minCornerIndex], 2e-3f);

                // Opposite corner is not affected by CP(0,0,0) in trilinear interpolation.
                AssertApproximately(sourceVertices[maxCornerIndex], deformedVertices[maxCornerIndex], 2e-3f);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void SyncLayerStructuresToBase_ResetsIncompatibleLayerShape()
        {
            var fixture = CreateFixture("SyncLayerStructuresToBase_ResetsIncompatibleLayerShape");
            try
            {
                var deformer = fixture.Deformer;
                deformer.AddLayer("Layer A");

                var baseSettings = deformer.Settings;
                baseSettings.ResizeGrid(new Vector3Int(3, 4, 5));
                deformer.SyncLayerStructuresToBase(resetControlPoints: false);

                var layerSettings = deformer.Layers[0].Settings;
                Assert.That(layerSettings.GridSize, Is.EqualTo(baseSettings.GridSize));
                Assert.That(layerSettings.ControlPointCount, Is.EqualTo(baseSettings.ControlPointCount));
                AssertApproximately(baseSettings.LocalBounds.center, layerSettings.LocalBounds.center);
                AssertApproximately(baseSettings.LocalBounds.size, layerSettings.LocalBounds.size);

                for (int i = 0; i < layerSettings.ControlPointCount; i++)
                {
                    Vector3 neutral = GetNeutralControlPoint(layerSettings.LocalBounds, layerSettings.GridSize, i);
                    AssertApproximately(neutral, layerSettings.GetControlPointLocal(i), Epsilon);
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
                var settings = deformer.Settings;

                const int controlPointIndex = 0;
                var legacyPoint = settings.GetControlPointLocal(controlPointIndex) + new Vector3(0.05f, 0.1f, 0f);
                settings.SetControlPointLocal(controlPointIndex, legacyPoint);

                Assert.That(deformer.Layers.Count, Is.EqualTo(0));

                var beforeMigration = deformer.Deform(false);
                Assert.That(beforeMigration, Is.Not.Null);
                var beforeVertices = beforeMigration.vertices;

                deformer.enabled = false;
                deformer.enabled = true;

                Assert.That(deformer.Layers.Count, Is.EqualTo(1));
                Assert.That(deformer.ActiveLayerIndex, Is.EqualTo(0));
                Assert.That(deformer.Layers[0].Name, Is.EqualTo("Lattice Layer"));

                var baseAfterMigration = deformer.Settings;
                Vector3 neutral = GetNeutralControlPoint(baseAfterMigration.LocalBounds, baseAfterMigration.GridSize, controlPointIndex);
                AssertApproximately(neutral, baseAfterMigration.GetControlPointLocal(controlPointIndex), Epsilon);

                var migratedLayerPoint = deformer.Layers[0].Settings.GetControlPointLocal(controlPointIndex);
                AssertApproximately(legacyPoint, migratedLayerPoint, Epsilon);

                var afterMigration = deformer.Deform(false);
                Assert.That(afterMigration, Is.Not.Null);
                var afterVertices = afterMigration.vertices;
                Assert.That(afterVertices.Length, Is.EqualTo(beforeVertices.Length));
                for (int i = 0; i < beforeVertices.Length; i++)
                {
                    AssertApproximately(beforeVertices[i], afterVertices[i], 2e-3f);
                }
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
