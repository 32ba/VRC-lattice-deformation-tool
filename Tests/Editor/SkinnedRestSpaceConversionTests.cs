#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor.Tests
{
    public sealed class SkinnedRestSpaceConversionTests
    {
        [TearDown]
        public void TearDown()
        {
            SkinnedVertexHelper.StoreMovesInRestSpace = false;
        }

        [Test]
        public void SingleBonePose_ConvertsDisplayedMoveBackToRestSpace()
        {
            var fixture = CreateSkinnedFixture(new BoneWeight
            {
                boneIndex0 = 0,
                weight0 = 1f
            }, 1);
            try
            {
                fixture.Bones[0].localRotation = Quaternion.Euler(0f, 0f, 90f);

                bool converted = SkinnedVertexHelper.TryConvertDisplayedDeltaToRestSpace(
                    fixture.Deformer, 0, Vector3.up, out var restDelta);

                Assert.That(converted, Is.True);
                AssertVector(restDelta, Vector3.right);
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void MultipleBoneWeights_InvertTheBlendedSkinningMatrix()
        {
            var fixture = CreateSkinnedFixture(new BoneWeight
            {
                boneIndex0 = 0,
                weight0 = 0.5f,
                boneIndex1 = 1,
                weight1 = 0.5f
            }, 2);
            try
            {
                fixture.Bones[1].localRotation = Quaternion.Euler(0f, 0f, 90f);
                var displayedDelta = new Vector3(0.5f, 0.5f, 0f);

                bool converted = SkinnedVertexHelper.TryConvertDisplayedDeltaToRestSpace(
                    fixture.Deformer, 0, displayedDelta, out var restDelta);

                Assert.That(converted, Is.True);
                AssertVector(restDelta, Vector3.right);
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void MissingBindPose_FallsBackWithoutChangingDelta()
        {
            var fixture = CreateSkinnedFixture(new BoneWeight
            {
                boneIndex0 = 0,
                weight0 = 1f
            }, 1);
            try
            {
                fixture.Mesh.bindposes = new Matrix4x4[0];
                var input = new Vector3(0.25f, -0.5f, 1f);

                bool converted = SkinnedVertexHelper.TryConvertDisplayedDeltaToRestSpace(
                    fixture.Deformer, 0, input, out var output);

                Assert.That(converted, Is.False);
                Assert.That(output, Is.EqualTo(input));
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void ZeroWeights_FallBackWithoutProducingNonFiniteValues()
        {
            var fixture = CreateSkinnedFixture(new BoneWeight(), 1);
            try
            {
                var input = new Vector3(1f, 2f, 3f);

                bool converted = SkinnedVertexHelper.TryConvertDisplayedDeltaToRestSpace(
                    fixture.Deformer, 0, input, out var output);

                Assert.That(converted, Is.False);
                Assert.That(output, Is.EqualTo(input));
                Assert.That(float.IsNaN(output.x) || float.IsInfinity(output.x), Is.False);
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void StorageOption_UsesSameConversionAndCanBeDisabled()
        {
            var fixture = CreateSkinnedFixture(new BoneWeight
            {
                boneIndex0 = 0,
                weight0 = 1f
            }, 1);
            try
            {
                fixture.Bones[0].localRotation = Quaternion.Euler(0f, 0f, 90f);

                SkinnedVertexHelper.StoreMovesInRestSpace = false;
                Assert.That(SkinnedVertexHelper.ConvertMoveDeltaForStorage(
                    fixture.Deformer, 0, Vector3.up), Is.EqualTo(Vector3.up));

                SkinnedVertexHelper.StoreMovesInRestSpace = true;
                AssertVector(
                    SkinnedVertexHelper.ConvertMoveDeltaForStorage(
                        fixture.Deformer, 0, Vector3.up),
                    Vector3.right);
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void MeshRenderer_IsUnaffectedWhenRestSpaceOptionIsEnabled()
        {
            var mesh = CreateMesh(new BoneWeight());
            var gameObject = new GameObject("Static Mesh");
            gameObject.AddComponent<MeshRenderer>();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var deformer = gameObject.AddComponent<LatticeDeformer>();
            deformer.Reset();
            try
            {
                var input = new Vector3(0.1f, 0.2f, 0.3f);
                SkinnedVertexHelper.StoreMovesInRestSpace = true;

                Assert.That(SkinnedVertexHelper.ConvertMoveDeltaForStorage(
                    deformer, 0, input), Is.EqualTo(input));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        private static Fixture CreateSkinnedFixture(BoneWeight firstVertexWeight, int boneCount)
        {
            var gameObject = new GameObject("Skinned Rest Space Test");
            var renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            var bones = new Transform[boneCount];
            for (int i = 0; i < boneCount; i++)
            {
                var boneObject = new GameObject($"Bone {i}");
                boneObject.transform.SetParent(gameObject.transform, false);
                bones[i] = boneObject.transform;
            }

            var mesh = CreateMesh(firstVertexWeight);
            var bindPoses = new Matrix4x4[boneCount];
            for (int i = 0; i < boneCount; i++)
            {
                bindPoses[i] = bones[i].worldToLocalMatrix * gameObject.transform.localToWorldMatrix;
            }
            mesh.bindposes = bindPoses;
            renderer.sharedMesh = mesh;
            renderer.bones = bones;
            renderer.rootBone = bones[0];

            var deformer = gameObject.AddComponent<LatticeDeformer>();
            deformer.Reset();
            return new Fixture(gameObject, mesh, deformer, bones);
        }

        private static Mesh CreateMesh(BoneWeight firstVertexWeight)
        {
            var mesh = new Mesh { name = "Rest Space Test Mesh" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.boneWeights = new[]
            {
                firstVertexWeight,
                firstVertexWeight,
                firstVertexWeight
            };
            return mesh;
        }

        private static void AssertVector(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(1e-5f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(1e-5f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(1e-5f));
        }

        private sealed class Fixture
        {
            internal readonly GameObject GameObject;
            internal readonly Mesh Mesh;
            internal readonly LatticeDeformer Deformer;
            internal readonly Transform[] Bones;

            internal Fixture(
                GameObject gameObject,
                Mesh mesh,
                LatticeDeformer deformer,
                Transform[] bones)
            {
                GameObject = gameObject;
                Mesh = mesh;
                Deformer = deformer;
                Bones = bones;
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
