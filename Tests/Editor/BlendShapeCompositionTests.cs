#if UNITY_EDITOR
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class BlendShapeCompositionTests
    {
        private const float k_Epsilon = 2e-3f;

        [Test]
        public void Progressive_AccumulatesLayerCandidatesAtInBetweenFrames()
        {
            var fixture = CreateFixture("Progressive");
            try
            {
                ConfigureTwoBrushLayers(fixture.Deformer, out var first, out var second);
                var group = fixture.Deformer.ActiveGroup;
                group.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                group.BlendShapeName = "Progressive";
                group.BlendShapeComposition = BlendShapeCompositionMode.Progressive;

                var mesh = fixture.Deformer.Deform(false);

                AssertFrame(mesh, "Progressive", 49, first);
                AssertFrame(mesh, "Progressive", 74, first + second * 0.5f);
                AssertFrame(mesh, "Progressive", 99, first + second);
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void Crossfade_InterpolatesAdjacentCandidatesWithoutDoubleAddition()
        {
            var fixture = CreateFixture("Crossfade");
            try
            {
                ConfigureTwoBrushLayers(fixture.Deformer, out var first, out var second);
                var group = fixture.Deformer.ActiveGroup;
                group.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                group.BlendShapeName = "Crossfade";
                group.BlendShapeComposition = BlendShapeCompositionMode.Crossfade;

                var mesh = fixture.Deformer.Deform(false);

                AssertFrame(mesh, "Crossfade", 49, first);
                AssertFrame(mesh, "Crossfade", 74, Vector3.Lerp(first, second, 0.5f));
                AssertFrame(mesh, "Crossfade", 99, second);
                Assert.That(ReadFrameDelta(mesh, "Crossfade", 99), Is.Not.EqualTo(first + second));
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [TestCase(BlendShapeCompositionMode.Progressive)]
        [TestCase(BlendShapeCompositionMode.Crossfade)]
        public void StagedComposition_SurfaceDeltasMatchComposedStageGeometry(
            BlendShapeCompositionMode composition)
        {
            string shapeName = composition + "Surface";
            var fixture = CreateFixture(shapeName);
            Mesh expectedMesh = null;
            try
            {
                int firstIndex = fixture.Deformer.AddLayer("Lift A", MeshDeformerLayerType.Brush);
                fixture.Deformer.ActiveLayerIndex = firstIndex;
                fixture.Deformer.EnsureDisplacementCapacity();
                fixture.Deformer.SetDisplacement(0, new Vector3(0f, 0f, 0.4f));

                int secondIndex = fixture.Deformer.AddLayer("Lift B", MeshDeformerLayerType.Brush);
                fixture.Deformer.ActiveLayerIndex = secondIndex;
                fixture.Deformer.EnsureDisplacementCapacity();
                fixture.Deformer.SetDisplacement(1, new Vector3(0f, 0f, -0.3f));

                var group = fixture.Deformer.ActiveGroup;
                group.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                group.BlendShapeName = shapeName;
                group.BlendShapeComposition = composition;
                typeof(LatticeDeformer).GetField(
                    "_recalculateTangents", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(fixture.Deformer, true);

                var mesh = fixture.Deformer.Deform(false);
                int shape = mesh.GetBlendShapeIndex(shapeName);
                var deltaVertices = new Vector3[mesh.vertexCount];
                var deltaNormals = new Vector3[mesh.vertexCount];
                var deltaTangents = new Vector3[mesh.vertexCount];
                mesh.GetBlendShapeFrameVertices(shape, 74, deltaVertices, deltaNormals, deltaTangents);

                expectedMesh = Object.Instantiate(fixture.Mesh);
                var expectedVertices = fixture.Mesh.vertices;
                for (int i = 0; i < expectedVertices.Length; i++) expectedVertices[i] += deltaVertices[i];
                expectedMesh.vertices = expectedVertices;
                expectedMesh.RecalculateNormals();
                expectedMesh.RecalculateTangents();

                var sourceNormals = fixture.Mesh.normals;
                var expectedNormals = expectedMesh.normals;
                var sourceTangents = fixture.Mesh.tangents;
                var expectedTangents = expectedMesh.tangents;
                for (int i = 0; i < mesh.vertexCount; i++)
                {
                    AssertVector(deltaNormals[i], expectedNormals[i] - sourceNormals[i]);
                    AssertVector(deltaTangents[i],
                        (Vector3)expectedTangents[i] - (Vector3)sourceTangents[i]);
                }
            }
            finally
            {
                if (expectedMesh != null) Object.DestroyImmediate(expectedMesh);
                fixture.Destroy();
            }
        }

        [Test]
        public void Composition_ReusesGroupCurveAsStageProgress()
        {
            var fixture = CreateFixture("Curve");
            try
            {
                ConfigureTwoBrushLayers(fixture.Deformer, out var first, out _);
                var group = fixture.Deformer.ActiveGroup;
                group.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                group.BlendShapeName = "Curve";
                group.BlendShapeComposition = BlendShapeCompositionMode.Progressive;
                group.BlendShapeCurve = new AnimationCurve(
                    new Keyframe(0f, 0f),
                    new Keyframe(1f, 0.5f));

                var mesh = fixture.Deformer.Deform(false);

                AssertFrame(mesh, "Curve", 99, first,
                    "A curve value of 0.5 advances a two-candidate output to the first stage.");
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void IndependentLayerOutput_IsExcludedFromProgressiveGroupCandidates()
        {
            var fixture = CreateFixture("Independent");
            try
            {
                ConfigureTwoBrushLayers(fixture.Deformer, out var first, out var second,
                    out var firstLayer, out _);
                firstLayer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                firstLayer.BlendShapeName = "IndependentLayer";

                var group = fixture.Deformer.ActiveGroup;
                group.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                group.BlendShapeName = "ProgressiveGroup";
                group.BlendShapeComposition = BlendShapeCompositionMode.Progressive;

                var mesh = fixture.Deformer.Deform(false);

                AssertFrame(mesh, "IndependentLayer", 99, first);
                AssertFrame(mesh, "ProgressiveGroup", 99, second);
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void SingleComposition_PreservesExistingCompositeOutput()
        {
            var fixture = CreateFixture("Single");
            try
            {
                ConfigureTwoBrushLayers(fixture.Deformer, out var first, out var second);
                var group = fixture.Deformer.ActiveGroup;
                group.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                group.BlendShapeName = "Single";
                group.BlendShapeComposition = BlendShapeCompositionMode.Single;

                var mesh = fixture.Deformer.Deform(false);

                AssertFrame(mesh, "Single", 99, first + second);
                Assert.That(mesh.GetBlendShapeFrameCount(mesh.GetBlendShapeIndex("Single")), Is.EqualTo(100));
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void DisabledOutput_IgnoresCompositionAndAppliesLayersDirectly()
        {
            var fixture = CreateFixture("Disabled");
            try
            {
                ConfigureTwoBrushLayers(fixture.Deformer, out var first, out var second);
                var group = fixture.Deformer.ActiveGroup;
                group.BlendShapeOutput = BlendShapeOutputMode.Disabled;
                group.BlendShapeComposition = BlendShapeCompositionMode.Crossfade;
                var source = fixture.Mesh.vertices;

                var mesh = fixture.Deformer.Deform(false);

                AssertVector(mesh.vertices[0], source[0] + first + second);
                Assert.That(mesh.blendShapeCount, Is.EqualTo(fixture.Mesh.blendShapeCount));
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void SingleComposition_PreservesCurveOvershootBehavior()
        {
            var fixture = CreateFixture("SingleOvershoot");
            try
            {
                ConfigureTwoBrushLayers(fixture.Deformer, out var first, out var second);
                var group = fixture.Deformer.ActiveGroup;
                group.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                group.BlendShapeName = "SingleOvershoot";
                group.BlendShapeComposition = BlendShapeCompositionMode.Single;
                group.BlendShapeCurve = AnimationCurve.Linear(0f, 0f, 1f, 2f);

                var mesh = fixture.Deformer.Deform(false);

                AssertFrame(mesh, "SingleOvershoot", 99, (first + second) * 2f);
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void JsonRoundTrip_PreservesCompositionMode()
        {
            var source = new DeformerGroup
            {
                BlendShapeComposition = BlendShapeCompositionMode.Crossfade
            };
            var restored = JsonUtility.FromJson<DeformerGroup>(JsonUtility.ToJson(source));

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.BlendShapeComposition, Is.EqualTo(BlendShapeCompositionMode.Crossfade));
        }

        private static void ConfigureTwoBrushLayers(
            LatticeDeformer deformer,
            out Vector3 first,
            out Vector3 second)
        {
            ConfigureTwoBrushLayers(deformer, out first, out second, out _, out _);
        }

        private static void ConfigureTwoBrushLayers(
            LatticeDeformer deformer,
            out Vector3 first,
            out Vector3 second,
            out LatticeLayer firstLayer,
            out LatticeLayer secondLayer)
        {
            first = new Vector3(0.2f, 0f, 0f);
            second = new Vector3(0f, 0.3f, 0f);

            int firstIndex = deformer.AddLayer("First", MeshDeformerLayerType.Brush);
            deformer.ActiveLayerIndex = firstIndex;
            deformer.EnsureDisplacementCapacity();
            deformer.SetDisplacement(0, first);
            firstLayer = deformer.Layers[firstIndex];

            int secondIndex = deformer.AddLayer("Second", MeshDeformerLayerType.Brush);
            deformer.ActiveLayerIndex = secondIndex;
            deformer.EnsureDisplacementCapacity();
            deformer.SetDisplacement(0, second);
            secondLayer = deformer.Layers[secondIndex];
        }

        private static void AssertFrame(
            Mesh mesh,
            string shapeName,
            int frameIndex,
            Vector3 expected,
            string message = null)
        {
            AssertVector(ReadFrameDelta(mesh, shapeName, frameIndex), expected, message);
        }

        private static Vector3 ReadFrameDelta(Mesh mesh, string shapeName, int frameIndex)
        {
            int shapeIndex = mesh.GetBlendShapeIndex(shapeName);
            Assert.That(shapeIndex, Is.GreaterThanOrEqualTo(0), $"Missing BlendShape '{shapeName}'.");
            Assert.That(frameIndex, Is.LessThan(mesh.GetBlendShapeFrameCount(shapeIndex)));
            var deltas = new Vector3[mesh.vertexCount];
            mesh.GetBlendShapeFrameVertices(
                shapeIndex, frameIndex, deltas, new Vector3[mesh.vertexCount], new Vector3[mesh.vertexCount]);
            return deltas[0];
        }

        private static void AssertVector(Vector3 actual, Vector3 expected, string message = null)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(k_Epsilon), message);
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(k_Epsilon), message);
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(k_Epsilon), message);
        }

        private static Fixture CreateFixture(string name)
        {
            var mesh = new Mesh { name = name + " Mesh" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            var gameObject = new GameObject(name);
            gameObject.AddComponent<MeshRenderer>();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var deformer = gameObject.AddComponent<LatticeDeformer>();
            deformer.Reset();
            return new Fixture(gameObject, mesh, deformer);
        }

        private sealed class Fixture
        {
            internal readonly GameObject GameObject;
            internal readonly Mesh Mesh;
            internal readonly LatticeDeformer Deformer;

            internal Fixture(GameObject gameObject, Mesh mesh, LatticeDeformer deformer)
            {
                GameObject = gameObject;
                Mesh = mesh;
                Deformer = deformer;
            }

            internal void Destroy()
            {
                var runtimeMesh = Deformer != null ? Deformer.RuntimeMesh : null;
                if (runtimeMesh != null) Object.DestroyImmediate(runtimeMesh);
                Object.DestroyImmediate(GameObject);
                Object.DestroyImmediate(Mesh);
            }
        }
    }
}
#endif
