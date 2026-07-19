#if UNITY_EDITOR
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class MultiFrameBlendShapeImportTests
    {
        private const float Epsilon = 2e-3f;

        [Test]
        public void ImportAllFrames_CreatesEditableGroupWithOriginalOrderWeightsAndDeltas()
        {
            var fixture = CreateFixture();
            try
            {
                int groupCount = fixture.Deformer.GroupCount;
                int importedGroup = fixture.Deformer.ImportBlendShapeAllFramesAsGroup(0);

                Assert.That(importedGroup, Is.EqualTo(groupCount));
                Assert.That(fixture.Deformer.ActiveGroupIndex, Is.EqualTo(importedGroup));
                Assert.That(fixture.Deformer.GroupCount, Is.EqualTo(groupCount + 1));

                var group = fixture.Deformer.ActiveGroup;
                Assert.That(group.BlendShapeOutput, Is.EqualTo(BlendShapeOutputMode.OutputAsBlendShape));
                Assert.That(group.BlendShapeComposition, Is.EqualTo(BlendShapeCompositionMode.Crossfade));
                Assert.That(group.Layers.Count, Is.EqualTo(fixture.Weights.Length));

                for (int frame = 0; frame < fixture.Weights.Length; frame++)
                {
                    var layer = group.Layers[frame];
                    Assert.That(layer.Type, Is.EqualTo(MeshDeformerLayerType.Brush));
                    Assert.That(layer.HasImportedBlendShapeFrameWeight, Is.True);
                    Assert.That(layer.ImportedBlendShapeFrameWeight,
                        Is.EqualTo(fixture.Weights[frame]).Within(Epsilon));
                    AssertVector(layer.GetBrushDisplacement(0), fixture.Deltas[frame][0]);
                }
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void ImportAllFrames_ReoutputsOriginalFrameCountWeightsAndVertexDeltas()
        {
            var fixture = CreateFixture();
            try
            {
                Assert.That(fixture.Deformer.ImportBlendShapeAllFramesAsGroup(0), Is.GreaterThanOrEqualTo(0));

                var output = fixture.Deformer.Deform(false);
                int shape = output.GetBlendShapeIndex("Smile Imported");
                Assert.That(shape, Is.GreaterThanOrEqualTo(0));
                Assert.That(output.GetBlendShapeFrameCount(shape), Is.EqualTo(fixture.Weights.Length));

                for (int frame = 0; frame < fixture.Weights.Length; frame++)
                {
                    Assert.That(output.GetBlendShapeFrameWeight(shape, frame),
                        Is.EqualTo(fixture.Weights[frame]).Within(Epsilon));
                    AssertFrame(output, shape, frame, fixture.Deltas[frame]);
                }
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void ImportAllFrames_ReoutputsSurfaceDeltasFromEachCandidateGeometry()
        {
            var fixture = CreateFixture();
            Mesh expectedMesh = null;
            try
            {
                Assert.That(fixture.Deformer.ImportBlendShapeAllFramesAsGroup(0), Is.GreaterThanOrEqualTo(0));
                typeof(LatticeDeformer).GetField(
                    "_recalculateTangents", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(fixture.Deformer, true);

                var output = fixture.Deformer.Deform(false);
                int shape = output.GetBlendShapeIndex("Smile Imported");
                var sourceVertices = fixture.Mesh.vertices;
                var sourceNormals = fixture.Mesh.normals;
                var sourceTangents = fixture.Mesh.tangents;

                for (int frame = 0; frame < fixture.Weights.Length; frame++)
                {
                    var actualNormals = new Vector3[output.vertexCount];
                    var actualTangents = new Vector3[output.vertexCount];
                    output.GetBlendShapeFrameVertices(
                        shape, frame, new Vector3[output.vertexCount], actualNormals, actualTangents);

                    expectedMesh = Object.Instantiate(fixture.Mesh);
                    var expectedVertices = (Vector3[])sourceVertices.Clone();
                    for (int vertex = 0; vertex < expectedVertices.Length; vertex++)
                        expectedVertices[vertex] += fixture.Deltas[frame][vertex];
                    expectedMesh.vertices = expectedVertices;
                    expectedMesh.RecalculateNormals();
                    expectedMesh.RecalculateTangents();

                    for (int vertex = 0; vertex < output.vertexCount; vertex++)
                    {
                        AssertVector(actualNormals[vertex], expectedMesh.normals[vertex] - sourceNormals[vertex]);
                        AssertVector(actualTangents[vertex],
                            (Vector3)expectedMesh.tangents[vertex] - (Vector3)sourceTangents[vertex]);
                    }
                    Object.DestroyImmediate(expectedMesh);
                    expectedMesh = null;
                }
            }
            finally
            {
                if (expectedMesh != null) Object.DestroyImmediate(expectedMesh);
                fixture.Destroy();
            }
        }

        [Test]
        public void EditingOneImportedFrame_DoesNotChangeOtherFrames()
        {
            var fixture = CreateFixture();
            try
            {
                fixture.Deformer.ImportBlendShapeAllFramesAsGroup(0);
                var editedLayer = fixture.Deformer.ActiveGroup.Layers[1];
                Vector3 edit = new Vector3(0.12f, -0.08f, 0.04f);
                editedLayer.SetBrushDisplacement(0, editedLayer.GetBrushDisplacement(0) + edit);

                var output = fixture.Deformer.Deform(false);
                int shape = output.GetBlendShapeIndex("Smile Imported");
                var expectedEdited = (Vector3[])fixture.Deltas[1].Clone();
                expectedEdited[0] += edit;

                AssertFrame(output, shape, 0, fixture.Deltas[0]);
                AssertFrame(output, shape, 1, expectedEdited);
                AssertFrame(output, shape, 2, fixture.Deltas[2]);
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void ImportAllFrames_PreservesZeroDeltaFrame()
        {
            var fixture = CreateFixture();
            try
            {
                fixture.Deformer.ImportBlendShapeAllFramesAsGroup(0);
                var output = fixture.Deformer.Deform(false);
                int shape = output.GetBlendShapeIndex("Smile Imported");

                Assert.That(output.GetBlendShapeFrameCount(shape), Is.EqualTo(3));
                Assert.That(output.GetBlendShapeFrameWeight(shape, 0), Is.EqualTo(0f).Within(Epsilon));
                AssertFrame(output, shape, 0, fixture.Deltas[0]);
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void ImportAllFrames_InvalidIndexDoesNotMutateGroups()
        {
            var fixture = CreateFixture();
            try
            {
                int groupCount = fixture.Deformer.GroupCount;
                int activeGroup = fixture.Deformer.ActiveGroupIndex;

                Assert.That(fixture.Deformer.ImportBlendShapeAllFramesAsGroup(-1), Is.EqualTo(-1));
                Assert.That(fixture.Deformer.ImportBlendShapeAllFramesAsGroup(99), Is.EqualTo(-1));
                Assert.That(fixture.Deformer.GroupCount, Is.EqualTo(groupCount));
                Assert.That(fixture.Deformer.ActiveGroupIndex, Is.EqualTo(activeGroup));
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void ImportedFrameMetadata_SurvivesJsonRoundTrip()
        {
            var fixture = CreateFixture();
            try
            {
                fixture.Deformer.ImportBlendShapeAllFramesAsGroup(0);
                var source = fixture.Deformer.ActiveGroup.Layers[1];
                var restored = JsonUtility.FromJson<LatticeLayer>(JsonUtility.ToJson(source));

                Assert.That(restored.HasImportedBlendShapeFrameWeight, Is.True);
                Assert.That(restored.ImportedBlendShapeFrameWeight, Is.EqualTo(50f).Within(Epsilon));
                AssertVector(restored.GetBrushDisplacement(0), fixture.Deltas[1][0]);
            }
            finally
            {
                fixture.Destroy();
            }
        }

        private static void AssertFrame(Mesh mesh, int shape, int frame, Vector3[] expected)
        {
            var actual = new Vector3[mesh.vertexCount];
            mesh.GetBlendShapeFrameVertices(shape, frame, actual, null, null);
            for (int vertex = 0; vertex < actual.Length; vertex++)
                AssertVector(actual[vertex], expected[vertex]);
        }

        private static void AssertVector(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Epsilon));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Epsilon));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(Epsilon));
        }

        private static Fixture CreateFixture()
        {
            var mesh = new Mesh { name = "Multi Frame Source" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            var weights = new[] { 0f, 50f, 100f };
            var deltas = new[]
            {
                new[] { Vector3.zero, Vector3.zero, Vector3.zero },
                new[]
                {
                    new Vector3(0.1f, 0f, 0f),
                    new Vector3(0f, 0.15f, 0f),
                    new Vector3(0f, 0f, 0.05f)
                },
                new[]
                {
                    new Vector3(0.25f, 0.1f, 0f),
                    new Vector3(0f, 0.3f, 0.1f),
                    new Vector3(0.05f, 0f, 0.2f)
                }
            };
            for (int frame = 0; frame < weights.Length; frame++)
                mesh.AddBlendShapeFrame("Smile", weights[frame], deltas[frame], null, null);

            var gameObject = new GameObject("Multi Frame Import");
            gameObject.AddComponent<MeshRenderer>();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var deformer = gameObject.AddComponent<LatticeDeformer>();
            deformer.Reset();
            return new Fixture(gameObject, mesh, deformer, weights, deltas);
        }

        private sealed class Fixture
        {
            internal readonly GameObject GameObject;
            internal readonly Mesh Mesh;
            internal readonly LatticeDeformer Deformer;
            internal readonly float[] Weights;
            internal readonly Vector3[][] Deltas;

            internal Fixture(
                GameObject gameObject,
                Mesh mesh,
                LatticeDeformer deformer,
                float[] weights,
                Vector3[][] deltas)
            {
                GameObject = gameObject;
                Mesh = mesh;
                Deformer = deformer;
                Weights = weights;
                Deltas = deltas;
            }

            internal void Destroy()
            {
                var runtime = Deformer != null ? Deformer.RuntimeMesh : null;
                if (runtime != null) Object.DestroyImmediate(runtime);
                Object.DestroyImmediate(GameObject);
                Object.DestroyImmediate(Mesh);
            }
        }
    }
}
#endif
