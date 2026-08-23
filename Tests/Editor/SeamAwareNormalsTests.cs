#if UNITY_EDITOR
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class SeamAwareNormalsTests
    {
        private const float Epsilon = 1e-4f;
        private static readonly BindingFlags s_privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void PreserveSourceSmoothing_SharesNormalsAcrossMatchingDuplicateVertices()
        {
            var fixture = CreateFixture("SeamAwareNormals_SharesMatchingSeam", hardEdge: false);
            try
            {
                fixture.Deformer.NormalsMode = NormalsRecalculationMode.PreserveSourceSmoothing;
                DisplaceSecondTriangle(fixture.Deformer);

                var output = fixture.Deformer.Deform(false);
                var expected = new Vector3(0f, 1f, 2f).normalized;
                AssertVector(output.normals[0], expected);
                AssertVector(output.normals[3], expected);
                Assert.That((output.normals[0] - output.normals[3]).sqrMagnitude, Is.LessThanOrEqualTo(Epsilon * Epsilon));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void PreserveSourceSmoothing_LeavesUniqueVerticesAtUnityNormals()
        {
            var fixture = CreateFixture("SeamAwareNormals_PreservesUniqueVertices", hardEdge: false);
            try
            {
                fixture.Deformer.NormalsMode = NormalsRecalculationMode.PreserveSourceSmoothing;
                DisplaceSecondTriangle(fixture.Deformer);
                var output = fixture.Deformer.Deform(false);

                var expected = Object.Instantiate(fixture.SourceMesh);
                try
                {
                    expected.vertices = output.vertices;
                    expected.RecalculateNormals();
                    AssertVector(output.normals[2], expected.normals[2]);
                    AssertVector(output.normals[5], expected.normals[5]);
                }
                finally
                {
                    Object.DestroyImmediate(expected);
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void PreserveSourceSmoothing_DoesNotMergeSourceHardEdges()
        {
            var fixture = CreateFixture("SeamAwareNormals_SeparatesHardEdge", hardEdge: true);
            try
            {
                fixture.Deformer.NormalsMode = NormalsRecalculationMode.PreserveSourceSmoothing;
                DisplaceSecondTriangle(fixture.Deformer);

                var output = fixture.Deformer.Deform(false);
                AssertVector(output.normals[0], Vector3.forward);
                AssertVector(output.normals[3], new Vector3(0f, 1f, 1f).normalized);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void PreserveSourceSmoothing_SeparatesOneDegreeSourceCrease()
        {
            var fixture = CreateFixture("SeamAwareNormals_SeparatesSmallCrease", hardEdge: false);
            try
            {
                var normals = fixture.SourceMesh.normals;
                Vector3 oneDegree = Quaternion.AngleAxis(1f, Vector3.right) * Vector3.forward;
                normals[3] = normals[4] = normals[5] = oneDegree;
                fixture.SourceMesh.normals = normals;

                fixture.Deformer.NormalsMode = NormalsRecalculationMode.PreserveSourceSmoothing;
                DisplaceSecondTriangle(fixture.Deformer);
                var output = fixture.Deformer.Deform(false);

                AssertVector(output.normals[0], Vector3.forward);
                AssertVector(output.normals[3], new Vector3(0f, 1f, 1f).normalized);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void LegacyMode_MatchesUnityRecalculateNormals()
        {
            var fixture = CreateFixture("SeamAwareNormals_LegacyMatchesUnity", hardEdge: false);
            try
            {
                fixture.Deformer.NormalsMode = NormalsRecalculationMode.LegacyUnityRecalculate;
                DisplaceSecondTriangle(fixture.Deformer);

                var output = fixture.Deformer.Deform(false);
                var expected = Object.Instantiate(fixture.SourceMesh);
                try
                {
                    expected.vertices = output.vertices;
                    expected.RecalculateNormals();
                    Assert.That(output.normals.Length, Is.EqualTo(expected.normals.Length));
                    for (int i = 0; i < output.normals.Length; i++)
                    {
                        AssertVector(output.normals[i], expected.normals[i]);
                    }
                }
                finally
                {
                    Object.DestroyImmediate(expected);
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void DisabledNormalRecalculation_PreservesSourceNormals()
        {
            var fixture = CreateFixture("SeamAwareNormals_DisabledPreservesSource", hardEdge: true);
            try
            {
                fixture.Deformer.NormalsMode = NormalsRecalculationMode.PreserveSourceSmoothing;
                SetPrivateField(fixture.Deformer, "_recalculateNormals", false);
                DisplaceSecondTriangle(fixture.Deformer);

                var output = fixture.Deformer.Deform(false);
                var sourceNormals = fixture.SourceMesh.normals;
                Assert.That(output.normals.Length, Is.EqualTo(sourceNormals.Length));
                for (int i = 0; i < sourceNormals.Length; i++)
                {
                    AssertVector(output.normals[i], sourceNormals[i]);
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void NormalsMode_ChangesLayeredStateHash()
        {
            var fixture = CreateFixture("SeamAwareNormals_ModeStateHash", hardEdge: false);
            try
            {
                fixture.Deformer.NormalsMode = NormalsRecalculationMode.LegacyUnityRecalculate;
                int legacyHash = fixture.Deformer.ComputeLayeredStateHash();
                fixture.Deformer.NormalsMode = NormalsRecalculationMode.PreserveSourceSmoothing;
                int smoothingHash = fixture.Deformer.ComputeLayeredStateHash();
                Assert.That(smoothingHash, Is.Not.EqualTo(legacyHash));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void UnknownNormalsMode_FallsBackToLegacyUnityCalculation()
        {
            var fixture = CreateFixture("SeamAwareNormals_UnknownMode", hardEdge: false);
            try
            {
                DisplaceSecondTriangle(fixture.Deformer);
                SetPrivateField(fixture.Deformer, "_normalsRecalculationMode", (NormalsRecalculationMode)99);
                var output = fixture.Deformer.Deform(false);

                var expected = Object.Instantiate(fixture.SourceMesh);
                try
                {
                    expected.vertices = output.vertices;
                    expected.RecalculateNormals();
                    for (int i = 0; i < output.normals.Length; i++)
                    {
                        AssertVector(output.normals[i], expected.normals[i]);
                    }
                }
                finally
                {
                    Object.DestroyImmediate(expected);
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void BlendShapeNormalDelta_UsesSourceSmoothingForBaseAndTarget()
        {
            var fixture = CreateFixture("SeamAwareNormals_BlendShapeDelta", hardEdge: false);
            try
            {
                fixture.Deformer.NormalsMode = NormalsRecalculationMode.PreserveSourceSmoothing;
                DisplaceSecondTriangle(fixture.Deformer);
                fixture.Deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                fixture.Deformer.BlendShapeName = "SeamAwareShape";

                ReleaseRuntimeMesh(fixture.Deformer);
                var output = fixture.Deformer.Deform(false);
                int shape = output.GetBlendShapeIndex("SeamAwareShape");
                Assert.That(shape, Is.GreaterThanOrEqualTo(0));

                var deltaNormals = new Vector3[output.vertexCount];
                output.GetBlendShapeFrameVertices(
                    shape,
                    output.GetBlendShapeFrameCount(shape) - 1,
                    new Vector3[output.vertexCount],
                    deltaNormals,
                    null);

                Assert.That(deltaNormals[0].sqrMagnitude, Is.GreaterThan(1e-8f));
                Assert.That(deltaNormals[3].sqrMagnitude, Is.GreaterThan(1e-8f));
                Assert.That((deltaNormals[0] - deltaNormals[3]).sqrMagnitude,
                    Is.LessThanOrEqualTo(Epsilon * Epsilon));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        private static TestFixture CreateFixture(string name, bool hardEdge)
        {
            var root = new GameObject(name);
            var filter = root.AddComponent<MeshFilter>();
            root.AddComponent<MeshRenderer>();

            var source = new Mesh { name = name + "Mesh" };
            source.vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f),
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, -1f, 0f)
            };
            source.triangles = new[] { 0, 1, 2, 3, 5, 4 };
            source.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(0.5f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f)
            };
            var normals = new Vector3[6];
            normals[0] = normals[1] = normals[2] = Vector3.forward;
            normals[3] = normals[4] = normals[5] = hardEdge ? Vector3.up : Vector3.forward;
            source.normals = normals;
            source.RecalculateBounds();
            filter.sharedMesh = source;

            var deformer = root.AddComponent<LatticeDeformer>();
            deformer.Reset();
            Assert.That(deformer.Deform(false), Is.Not.Null);
            return new TestFixture(root, source, deformer);
        }

        private static void DisplaceSecondTriangle(LatticeDeformer deformer)
        {
            int layer = deformer.AddLayer("SeamAware Brush", MeshDeformerLayerType.Brush);
            deformer.ActiveLayerIndex = layer;
            deformer.EnsureDisplacementCapacity();
            deformer.SetDisplacement(5, new Vector3(0f, 0f, 1f));
        }

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            var field = instance.GetType().GetField(fieldName, s_privateInstance);
            Assert.That(field, Is.Not.Null, "Private field not found: " + fieldName);
            field.SetValue(instance, value);
        }

        private static void ReleaseRuntimeMesh(LatticeDeformer deformer)
        {
            var field = typeof(LatticeDeformer).GetField("_runtimeMesh", s_privateInstance);
            Assert.That(field, Is.Not.Null);
            var mesh = field.GetValue(deformer) as Mesh;
            if (mesh != null) Object.DestroyImmediate(mesh);
            field.SetValue(deformer, null);
        }

        private static void AssertVector(Vector3 actual, Vector3 expected)
        {
            Assert.That((actual - expected).sqrMagnitude, Is.LessThanOrEqualTo(Epsilon * Epsilon),
                $"Expected {expected}, got {actual}");
        }

        private sealed class TestFixture
        {
            internal readonly GameObject Root;
            internal readonly Mesh SourceMesh;
            internal readonly LatticeDeformer Deformer;

            internal TestFixture(GameObject root, Mesh sourceMesh, LatticeDeformer deformer)
            {
                Root = root;
                SourceMesh = sourceMesh;
                Deformer = deformer;
            }

            internal void Dispose()
            {
                Object.DestroyImmediate(Root);
                Object.DestroyImmediate(SourceMesh);
            }
        }
    }
}
#endif
