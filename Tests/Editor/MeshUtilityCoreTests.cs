#if UNITY_EDITOR
using Net._32Ba.LatticeDeformationTool;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class MeshUtilityCoreTests
    {
        [Test]
        public void PenetrationDetector_HandlesNullAndEmptyInputs()
        {
            Assert.That(PenetrationDetector.DetectPenetration(null, null, Matrix4x4.identity), Is.Empty);

            var mesh = new Mesh();
            try
            {
                Assert.That(
                    PenetrationDetector.DetectPenetration(new[] { Vector3.zero }, mesh, Matrix4x4.identity),
                    Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void PenetrationDetector_HandlesReferenceMeshWithoutVertices()
        {
            var reference = new Mesh
            {
                vertices = System.Array.Empty<Vector3>(),
                normals = System.Array.Empty<Vector3>(),
                triangles = System.Array.Empty<int>()
            };
            try
            {
                Assert.That(
                    PenetrationDetector.DetectPenetration(new[] { Vector3.zero }, reference, Matrix4x4.identity),
                    Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(reference);
            }
        }

        [Test]
        public void PenetrationDetector_HandlesReferenceMeshWithoutNormals()
        {
            var reference = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 }
            };
            try
            {
                Assert.That(
                    PenetrationDetector.DetectPenetration(
                        new[] { new Vector3(0.1f, 0.1f, -1f) }, reference, Matrix4x4.identity),
                    Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(reference);
            }
        }

        [Test]
        public void PenetrationDetector_DetectsVerticesBehindClosestNormal()
        {
            var reference = new Mesh
            {
                vertices = new[]
                {
                    Vector3.zero,
                    Vector3.right,
                    Vector3.up
                },
                normals = new[]
                {
                    Vector3.forward,
                    Vector3.forward,
                    Vector3.forward
                },
                triangles = new[] { 0, 1, 2 }
            };
            try
            {
                var penetrating = PenetrationDetector.DetectPenetration(
                    new[]
                    {
                        new Vector3(0.05f, 0.05f, -0.01f),
                        new Vector3(0.05f, 0.05f, 0.01f)
                    },
                    reference,
                    Matrix4x4.identity);

                Assert.That(penetrating.Contains(0), Is.True);
                Assert.That(penetrating.Contains(1), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(reference);
            }
        }

        [Test]
        public void SkinnedVertexHelper_ReturnsNullForInvalidOrUnskinnedInputs()
        {
            Assert.That(SkinnedVertexHelper.ComputeWorldPositions(null, new[] { Vector3.zero }), Is.Null);

            var go = new GameObject("mesh-renderer");
            try
            {
                var deformer = go.AddComponent<LatticeDeformer>();
                Assert.That(SkinnedVertexHelper.ComputeWorldPositions(deformer, null), Is.Null);
                Assert.That(SkinnedVertexHelper.ComputeWorldPositions(deformer, System.Array.Empty<Vector3>()), Is.Null);
                Assert.That(SkinnedVertexHelper.TryGetBakedMeshForRaycast(null, out _, out _), Is.False);

                go.AddComponent<MeshRenderer>();
                Assert.That(SkinnedVertexHelper.ComputeWorldPositions(deformer, new[] { Vector3.zero }), Is.Null);
                Assert.That(SkinnedVertexHelper.TryGetBakedMeshForRaycast(deformer, out _, out _), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SkinnedVertexHelper_ReturnsFalseWhenDeformerHasNoRenderer()
        {
            var go = new GameObject("deformer-only");
            try
            {
                var deformer = go.AddComponent<LatticeDeformer>();

                Assert.That(SkinnedVertexHelper.ComputeWorldPositions(deformer, new[] { Vector3.zero }), Is.Null);
                Assert.That(SkinnedVertexHelper.TryGetBakedMeshForRaycast(deformer, out _, out _), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SkinnedVertexHelper_ReturnsNullWhenBakeVertexCountDiffers()
        {
            var go = new GameObject("skinned");
            var bone = new GameObject("bone");
            var mesh = CreateSingleBoneTriangleMesh();
            try
            {
                bone.transform.SetParent(go.transform, false);
                var deformer = go.AddComponent<LatticeDeformer>();
                var skinned = go.AddComponent<SkinnedMeshRenderer>();
                skinned.sharedMesh = mesh;
                skinned.rootBone = bone.transform;
                skinned.bones = new[] { bone.transform };

                Assert.That(SkinnedVertexHelper.ComputeWorldPositions(deformer, new[] { Vector3.zero }), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(bone);
            }
        }

        [Test]
        public void SkinnedVertexHelper_BakesSingleBoneSkinnedMesh()
        {
            var go = new GameObject("skinned");
            var bone = new GameObject("bone");
            var mesh = CreateSingleBoneTriangleMesh();
            try
            {
                bone.transform.SetParent(go.transform, false);
                var deformer = go.AddComponent<LatticeDeformer>();
                var skinned = go.AddComponent<SkinnedMeshRenderer>();
                skinned.sharedMesh = mesh;
                skinned.rootBone = bone.transform;
                skinned.bones = new[] { bone.transform };
                go.transform.position = new Vector3(1f, 2f, 3f);

                var world = SkinnedVertexHelper.ComputeWorldPositions(
                    deformer,
                    new[] { Vector3.zero, Vector3.right, Vector3.up });

                Assert.That(world, Is.Not.Null);
                Assert.That(world, Has.Length.EqualTo(3));
                Assert.That(world[0].x, Is.EqualTo(1f).Within(1e-4f));
                Assert.That(world[0].y, Is.EqualTo(2f).Within(1e-4f));
                Assert.That(world[0].z, Is.EqualTo(3f).Within(1e-4f));
                Assert.That(SkinnedVertexHelper.TryGetBakedMeshForRaycast(deformer, out var baked, out var matrix), Is.True);
                Assert.That(baked.vertexCount, Is.EqualTo(3));
                Assert.That(matrix, Is.EqualTo(go.transform.localToWorldMatrix));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(bone);
            }
        }

        [Test]
        public void SkinnedVertexHelper_LocalToWorld_UsesPrecomputedOrMatrixFallback()
        {
            var worldPositions = new[] { new Vector3(9f, 8f, 7f) };
            var localVertices = new[] { Vector3.one };
            var matrix = Matrix4x4.Translate(new Vector3(1f, 2f, 3f));

            Assert.That(SkinnedVertexHelper.LocalToWorld(0, worldPositions, localVertices, matrix), Is.EqualTo(worldPositions[0]));
            Assert.That(SkinnedVertexHelper.LocalToWorld(0, null, localVertices, matrix), Is.EqualTo(new Vector3(2f, 3f, 4f)));
            Assert.That(SkinnedVertexHelper.LocalToWorld(5, null, localVertices, matrix), Is.EqualTo(Vector3.zero));
            Assert.That(SkinnedVertexHelper.LocalToWorld(0, worldPositions, Vector3.one, matrix), Is.EqualTo(worldPositions[0]));
            Assert.That(SkinnedVertexHelper.LocalToWorld(5, worldPositions, Vector3.one, matrix), Is.EqualTo(new Vector3(2f, 3f, 4f)));
        }

        private static Mesh CreateSingleBoneTriangleMesh()
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
                boneWeights = new[]
                {
                    Bone(),
                    Bone(),
                    Bone()
                },
                bindposes = new[] { Matrix4x4.identity }
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static BoneWeight Bone()
        {
            return new BoneWeight
            {
                boneIndex0 = 0,
                weight0 = 1f
            };
        }
    }
}
#endif
