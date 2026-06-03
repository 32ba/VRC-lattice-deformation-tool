#if UNITY_EDITOR
using Net._32Ba.LatticeDeformationTool;
using Net._32Ba.LatticeDeformationTool.Editor.WeightTransfer;
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class RobustWeightTransferTests
    {
        [Test]
        public void HybridTransfer_PreservesSameIndexWeight_WhenNearestSurfaceWouldUseDifferentIsland()
        {
            var source = CreateParallelQuads();
            var target = Object.Instantiate(source);
            try
            {
                var vertices = target.vertices;
                vertices[0] = new Vector3(vertices[0].x, vertices[0].y, 0.018f);
                target.vertices = vertices;
                target.RecalculateBounds();

                var sourceWeights = CreateParallelQuadWeights();
                var surfaceSettings = new WeightTransferSettings
                {
                    transferMode = WeightTransferMode.SurfaceTransfer,
                    maxTransferDistance = 1f,
                    normalAngleThreshold = 180f,
                    enableInpainting = false
                };
                var hybridSettings = surfaceSettings.Clone();
                hybridSettings.transferMode = WeightTransferMode.Hybrid;

                var surface = RobustWeightTransfer.Transfer(source, sourceWeights, target, surfaceSettings);
                var hybrid = RobustWeightTransfer.Transfer(source, sourceWeights, target, hybridSettings);

                Assert.That(surface.success, Is.True);
                Assert.That(hybrid.success, Is.True);
                Assert.That(DominantBone(surface.weights[0]), Is.EqualTo(1),
                    "Pure surface transfer should follow the closest parallel surface in this fixture.");
                Assert.That(DominantBone(hybrid.weights[0]), Is.EqualTo(0),
                    "Hybrid transfer should keep the reliable same-index source weight.");
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void HybridTransfer_InpaintsOnlyUnreliableSameIndexVertices()
        {
            var source = CreateSingleQuad();
            var target = Object.Instantiate(source);
            try
            {
                var vertices = target.vertices;
                vertices[0] += Vector3.forward * 10f;
                target.vertices = vertices;
                target.RecalculateBounds();

                var sourceWeights = new[]
                {
                    BoneWeightFor(2),
                    BoneWeightFor(0),
                    BoneWeightFor(1),
                    BoneWeightFor(1)
                };
                var settings = new WeightTransferSettings
                {
                    transferMode = WeightTransferMode.Hybrid,
                    maxTransferDistance = 0.001f,
                    normalAngleThreshold = 180f,
                    enableInpainting = true,
                    maxIterations = 200,
                    tolerance = 1e-6f
                };

                var result = RobustWeightTransfer.Transfer(source, sourceWeights, target, settings);

                Assert.That(result.success, Is.True);
                Assert.That(result.inpaintedCount, Is.EqualTo(1));
                Assert.That(TotalWeight(result.weights[0]), Is.EqualTo(1f).Within(1e-4f));
                Assert.That(DominantBone(result.weights[0]), Is.Not.EqualTo(2),
                    "The unreliable moved vertex should be solved from neighbors, not fall back to its original source weight.");
                Assert.That(TotalWeight(result.weights[1]), Is.EqualTo(1f).Within(1e-4f));
                Assert.That(DominantBone(result.weights[1]), Is.EqualTo(0),
                    "Reliable same-index vertices should remain fixed while the moved vertex is solved.");
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(target);
            }
        }

        private static Mesh CreateParallelQuads()
        {
            var mesh = new Mesh { name = "ParallelQuads" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(-0.5f, -0.5f, 0.02f),
                new Vector3(0.5f, -0.5f, 0.02f),
                new Vector3(0.5f, 0.5f, 0.02f),
                new Vector3(-0.5f, 0.5f, 0.02f)
            };
            mesh.normals = new[]
            {
                Vector3.forward,
                Vector3.forward,
                Vector3.forward,
                Vector3.forward,
                Vector3.forward,
                Vector3.forward,
                Vector3.forward,
                Vector3.forward
            };
            mesh.triangles = new[]
            {
                0, 1, 2, 0, 2, 3,
                4, 5, 6, 4, 6, 7
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateSingleQuad()
        {
            var mesh = new Mesh { name = "SingleQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f)
            };
            mesh.normals = new[]
            {
                Vector3.forward,
                Vector3.forward,
                Vector3.forward,
                Vector3.forward
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static BoneWeight[] CreateParallelQuadWeights()
        {
            return new[]
            {
                BoneWeightFor(0),
                BoneWeightFor(0),
                BoneWeightFor(0),
                BoneWeightFor(0),
                BoneWeightFor(1),
                BoneWeightFor(1),
                BoneWeightFor(1),
                BoneWeightFor(1)
            };
        }

        private static BoneWeight BoneWeightFor(int boneIndex)
        {
            return new BoneWeight { boneIndex0 = boneIndex, weight0 = 1f };
        }

        private static int DominantBone(BoneWeight weight)
        {
            int bone = weight.boneIndex0;
            float max = weight.weight0;
            if (weight.weight1 > max)
            {
                bone = weight.boneIndex1;
                max = weight.weight1;
            }
            if (weight.weight2 > max)
            {
                bone = weight.boneIndex2;
                max = weight.weight2;
            }
            if (weight.weight3 > max)
            {
                bone = weight.boneIndex3;
            }
            return bone;
        }

        private static float TotalWeight(BoneWeight weight)
        {
            return weight.weight0 + weight.weight1 + weight.weight2 + weight.weight3;
        }
    }
}
#endif
