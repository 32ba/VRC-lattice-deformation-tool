#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;
using Net._32Ba.LatticeDeformationTool.Editor;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public class LatticeControlPointSkinningTests
    {
        [Test]
        public void Update_UsesInterpolatedBoneWeightsAndRoundTripsControlPoint()
        {
            var root = new GameObject("Root");
            var bone0 = new GameObject("Bone0");
            var bone1 = new GameObject("Bone1");
            var mesh = CreateTwoBoneTriangle();
            try
            {
                bone0.transform.SetParent(root.transform, false);
                bone1.transform.SetParent(root.transform, false);
                bone1.transform.localPosition = new Vector3(2f, 0f, 0f);

                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.bones = new[] { bone0.transform, bone1.transform };
                renderer.rootBone = bone0.transform;

                var cache = new LatticeControlPointSkinning();
                Assert.That(
                    cache.Update(
                        renderer,
                        mesh,
                        mesh,
                        mesh.bounds,
                        new Vector3Int(2, 2, 1),
                        root.transform.worldToLocalMatrix),
                    Is.True);

                Assert.That(
                    cache.TryTransformPoint(0, Vector3.zero, out Vector3 fixedPoint),
                    Is.True);
                AssertVector(fixedPoint, Vector3.zero);

                Assert.That(
                    cache.TryTransformPoint(1, Vector3.right, out Vector3 movedPoint),
                    Is.True);
                AssertVector(movedPoint, new Vector3(3f, 0f, 0f));

                Vector3 editedPoint = movedPoint + new Vector3(0.25f, -0.1f, 0.05f);
                Assert.That(
                    cache.TryInverseTransformPoint(1, editedPoint, out Vector3 storedPoint),
                    Is.True);
                Assert.That(
                    cache.TryTransformPoint(1, storedPoint, out Vector3 roundTrip),
                    Is.True);
                AssertVector(roundTrip, editedPoint);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(bone0);
                Object.DestroyImmediate(bone1);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Update_ReusesSurfaceBindingAcrossPoseChanges()
        {
            var root = new GameObject("Root");
            var bone0 = new GameObject("Bone0");
            var bone1 = new GameObject("Bone1");
            var mesh = CreateTwoBoneTriangle();
            try
            {
                bone0.transform.SetParent(root.transform, false);
                bone1.transform.SetParent(root.transform, false);
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.bones = new[] { bone0.transform, bone1.transform };
                renderer.rootBone = bone0.transform;

                var cache = new LatticeControlPointSkinning();
                Assert.That(
                    cache.Update(
                        renderer,
                        mesh,
                        mesh,
                        mesh.bounds,
                        new Vector3Int(2, 2, 1),
                        root.transform.worldToLocalMatrix),
                    Is.True);
                int bindingRefreshes = cache.BindingRefreshCountForTests;

                bone1.transform.localPosition = Vector3.right;
                Assert.That(
                    cache.Update(
                        renderer,
                        mesh,
                        mesh,
                        mesh.bounds,
                        new Vector3Int(2, 2, 1),
                        root.transform.worldToLocalMatrix),
                    Is.True);

                Assert.That(cache.BindingRefreshCountForTests, Is.EqualTo(bindingRefreshes));
                Assert.That(cache.PoseRefreshCountForTests, Is.EqualTo(2));
                Assert.That(cache.HasPoseBounds, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(bone0);
                Object.DestroyImmediate(bone1);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Update_InvalidWeightsFailsClosedForBoundsFallback()
        {
            var root = new GameObject("Root");
            var bone = new GameObject("Bone");
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
                bindposes = new[] { Matrix4x4.identity }
            };
            try
            {
                bone.transform.SetParent(root.transform, false);
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.bones = new[] { bone.transform };
                renderer.rootBone = bone.transform;

                var cache = new LatticeControlPointSkinning();
                Assert.That(
                    cache.Update(
                        renderer,
                        mesh,
                        mesh,
                        mesh.bounds,
                        new Vector3Int(2, 2, 1),
                        root.transform.worldToLocalMatrix),
                    Is.False);
                Assert.That(cache.IsValid, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(bone);
                Object.DestroyImmediate(mesh);
            }
        }

        private static Mesh CreateTwoBoneTriangle()
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
                boneWeights = new[]
                {
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 1, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f }
                },
                bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity }
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AssertVector(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(1e-5f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(1e-5f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(1e-5f));
        }
    }
}
#endif
