#if UNITY_EDITOR
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class LatticeDragGeometryTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void PosedPointAndDelta_ReturnToRestSpaceAfterOptionalBoundsNormalization(bool normalize)
        {
            var root = new GameObject("Drag pose");
            var bone = new GameObject("Drag bone");
            bone.transform.SetParent(root.transform, false);
            bone.transform.localPosition = Vector3.right * 4;
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
                bindposes = new[] { Matrix4x4.identity },
                boneWeights = new[]
                {
                    new BoneWeight { boneIndex0 = 0, weight0 = 1 },
                    new BoneWeight { boneIndex0 = 0, weight0 = 1 },
                    new BoneWeight { boneIndex0 = 0, weight0 = 1 }
                }
            };
            try
            {
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.bones = new[] { bone.transform };
                renderer.rootBone = bone.transform;
                var skinning = new LatticeControlPointSkinning();
                var bounds = new Bounds(Vector3.zero, Vector3.one * 2);
                Assert.That(skinning.Update(renderer, mesh, mesh, bounds,
                    new Vector3Int(2, 2, 2), Matrix4x4.identity), Is.True);
                var geometry = new LatticeDragGeometry(Matrix4x4.identity, Matrix4x4.identity,
                    bounds, bounds, Vector3.one, Vector3.zero, Vector3.zero,
                    false, false, false, skinning, normalize,
                    new Bounds(new Vector3(8, 0, 0), Vector3.one * 4));
                Vector3 point = normalize ? new Vector3(10, 2, 2) : new Vector3(5, 1, 1);
                Vector3 delta = new Vector3(1, 2, 3) * (normalize ? 2 : 1);
                Assert.That(Vector3.Distance(geometry.StorePoint(0, point), Vector3.one), Is.LessThan(1e-5f));
                Assert.That(Vector3.Distance(geometry.StoreMirrorDelta(0, delta), new Vector3(1, 2, 3)), Is.LessThan(1e-5f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
            }
        }

        [TestCase(false, false, false, 18f, 12f, 4f, 4f, 8f, 12f)]
        [TestCase(true, false, false, 15f, 6f, -6f, 4f, 8f, 12f)]
        [TestCase(false, true, false, 2f, 3f, 1f, 1f, 2f, 3f)]
        [TestCase(false, true, true, 2f, 3f, 1f, 1f, 2f, 3f)]
        [TestCase(true, true, false, 1f, 1f, -2.5f, 1f, 2f, 3f)]
        [TestCase(true, true, true, 7.5f, 3f, -3f, 2f, 4f, 6f)]
        public void Mapping_OrdersScaleOffsetsBoundsAndFallbackWithoutChangingMirrorDeltaContract(
            bool proxy, bool map, bool fallback, float px, float py, float pz, float dx, float dy, float dz)
        {
            var geometry = new LatticeDragGeometry(
                Matrix4x4.Translate(new Vector3(-2, 0, 0)),
                Matrix4x4.TRS(new Vector3(10, 0, 0), Quaternion.identity, Vector3.one * 2),
                new Bounds(Vector3.zero, Vector3.one * 2),
                new Bounds(Vector3.zero, Vector3.one * 4),
                new Vector3(2, 0, 4), new Vector3(1, 2, 3), new Vector3(0.5f, 1, 2),
                proxy, map, fallback, null, false, default);
            Assert.That(geometry.StorePoint(0, new Vector3(10, 6, 8)), Is.EqualTo(new Vector3(px, py, pz)));
            Assert.That(geometry.StoreMirrorDelta(0, new Vector3(2, 4, 6)), Is.EqualTo(new Vector3(dx, dy, dz)));
            Assert.That(geometry.StoreMirrorDelta(0, Vector3.zero), Is.EqualTo(Vector3.zero));
        }
    }
}
#endif
