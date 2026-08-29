#if UNITY_EDITOR
using NUnit.Framework;
using UnityEditor;
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

        [Test]
        public void Update_TopologyChangedProxyUsesItsRemappedWeightsAndBonesTogether()
        {
            var root = new GameObject("Root");
            var bone0 = new GameObject("Bone0");
            var bone1 = new GameObject("Bone1");
            var sourceMesh = CreateTwoBoneTriangle();
            var proxyMesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one },
                triangles = new[] { 0, 1, 2 },
                boneWeights = new[]
                {
                    new BoneWeight { boneIndex0 = 1, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 1, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 1, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 1, weight0 = 1f }
                },
                bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity }
            };
            try
            {
                bone0.transform.SetParent(root.transform, false);
                bone1.transform.SetParent(root.transform, false);
                bone1.transform.localPosition = new Vector3(5f, 0f, 0f);
                bone1.transform.localScale = new Vector3(0.02f, 1f, 1f);

                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = proxyMesh;
                renderer.bones = new[] { bone1.transform, bone0.transform };
                renderer.rootBone = bone0.transform;

                var cache = new LatticeControlPointSkinning();
                Assert.That(
                    cache.Update(
                        renderer,
                        sourceMesh,
                        proxyMesh,
                        sourceMesh.bounds,
                        new Vector3Int(2, 2, 1),
                        root.transform.worldToLocalMatrix),
                    Is.True);
                Assert.That(cache.IsValid, Is.True);

                Vector3 neutral = sourceMesh.bounds.min;
                Assert.That(cache.TryTransformPoint(0, neutral, out Vector3 displayed), Is.True);
                AssertVector(displayed, neutral);

                var displayedDelta = new Vector3(0.1f, 0f, 0f);
                Assert.That(cache.TryInverseTransformVector(0, displayedDelta, out Vector3 storedDelta), Is.True);
                AssertVector(storedDelta, displayedDelta);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(bone0);
                Object.DestroyImmediate(bone1);
                Object.DestroyImmediate(sourceMesh);
                Object.DestroyImmediate(proxyMesh);
            }
        }

        [Test]
        public void Update_BlendShapeWeightChangeKeepsExistingControlPointBindings()
        {
            var root = new GameObject("Root");
            var bone0 = new GameObject("Bone0");
            var bone1 = new GameObject("Bone1");
            var mesh = CreateTwoBoneTriangle();
            mesh.AddBlendShapeFrame(
                "MoveBindingAcrossBoneBoundary",
                100f,
                new[] { Vector3.left * 10f, Vector3.left, Vector3.zero },
                null,
                null);
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
                int bindingRefreshes = cache.BindingRefreshCountForTests;
                Assert.That(cache.TryTransformPoint(0, Vector3.zero, out Vector3 before), Is.True);

                renderer.SetBlendShapeWeight(0, 100f);
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
                Assert.That(cache.TryTransformPoint(0, Vector3.zero, out Vector3 after), Is.True);
                AssertVector(after, before);
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
        public void Update_ChangingOneOfSeveralShapesReadsOnlyChangedShapeFrames()
        {
            var root = new GameObject("Incremental Shapes");
            var bone = new GameObject("Bone");
            var mesh = CreateBlendShapeCube();
            try
            {
                var delta = new Vector3[mesh.vertexCount];
                for (int i = 0; i < delta.Length; i++) delta[i] = Vector3.right;
                mesh.AddBlendShapeFrame("Shape B", 100f, delta, null, null);
                for (int i = 0; i < delta.Length; i++) delta[i] = Vector3.forward;
                mesh.AddBlendShapeFrame("Shape C", 100f, delta, null, null);
                bone.transform.SetParent(root.transform, false);
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.bones = new[] { bone.transform };
                renderer.rootBone = bone.transform;
                renderer.SetBlendShapeWeight(0, 20f);
                renderer.SetBlendShapeWeight(1, 30f);
                renderer.SetBlendShapeWeight(2, 40f);
                var cache = new LatticeControlPointSkinning();
                Assert.That(cache.Update(renderer, mesh, mesh, mesh.bounds,
                    new Vector3Int(2, 2, 2), root.transform.worldToLocalMatrix,
                    renderer, new[] { 0f, 0f, 0f }), Is.True);
                Assert.That(cache.Update(renderer, mesh, mesh, mesh.bounds,
                    new Vector3Int(2, 2, 2), root.transform.worldToLocalMatrix,
                    renderer, new[] { 0f, 0f, 0f }), Is.True);
                int before = cache.BlendShapeFrameReadCountForTests;

                renderer.SetBlendShapeWeight(0, 55f);
                Assert.That(cache.Update(renderer, mesh, mesh, mesh.bounds,
                    new Vector3Int(2, 2, 2), root.transform.worldToLocalMatrix,
                    renderer, new[] { 0f, 0f, 0f }), Is.True);

                Assert.That(
                    cache.BlendShapeFrameReadCountForTests - before,
                    Is.EqualTo(3),
                    "Only the changed multi-frame Shape should be removed and resampled.");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(bone);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Update_SameVertexCountRewiredProxyUsesProxyPositionsWithProxyWeights()
        {
            var root = new GameObject("Rewired Proxy Root");
            var bone0 = new GameObject("Bone 0");
            var bone1 = new GameObject("Bone 1");
            var source = CreateTwoBoneTriangle();
            var proxy = new Mesh
            {
                vertices = new[] { Vector3.right, Vector3.zero, Vector3.up },
                triangles = new[] { 1, 0, 2 },
                boneWeights = new[]
                {
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 1, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                },
                bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity },
            };
            try
            {
                bone0.transform.SetParent(root.transform, false);
                bone1.transform.SetParent(root.transform, false);
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = proxy;
                renderer.bones = new[] { bone0.transform, bone1.transform };
                renderer.rootBone = bone0.transform;
                var cache = new LatticeControlPointSkinning();

                Assert.That(cache.Update(renderer, source, proxy, source.bounds,
                    new Vector3Int(2, 2, 1), root.transform.worldToLocalMatrix), Is.True);
                Assert.That(cache.TryGetBindingForTests(0, out int[] bones, out float[] weights), Is.True);
                Assert.That(bones[0], Is.EqualTo(1));
                Assert.That(weights[0], Is.EqualTo(1f).Within(1e-6f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(bone0);
                Object.DestroyImmediate(bone1);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(proxy);
            }
        }

        [TestCase(12.5f)]
        [TestCase(25f)]
        [TestCase(42.5f)]
        [TestCase(60f)]
        [TestCase(80f)]
        [TestCase(100f)]
        public void Update_MultiFrameBlendShapeMatchesUnityBakedVertices(float currentWeight)
        {
            var root = new GameObject("MultiFrame BlendShape Root");
            var bone = new GameObject("Bone");
            var source = CreateBlendShapeCube();
            var baked = new Mesh();
            try
            {
                bone.transform.SetParent(root.transform, false);
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                renderer.bones = new[] { bone.transform };
                renderer.rootBone = bone.transform;
                renderer.SetBlendShapeWeight(0, currentWeight);

                var cache = new LatticeControlPointSkinning();
                Assert.That(
                    cache.Update(
                        renderer,
                        source,
                        source,
                        source.bounds,
                        new Vector3Int(2, 2, 2),
                        root.transform.worldToLocalMatrix,
                        renderer,
                        new[] { 0f }),
                    Is.True);

                renderer.BakeMesh(baked);
                Vector3[] expected = baked.vertices;
                Vector3[] neutral = source.vertices;
                for (int control = 0; control < 8; control++)
                {
                    Assert.That(
                        cache.TryTransformPoint(control, neutral[control], out Vector3 actual),
                        Is.True);
                    AssertVector(actual, expected[control]);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(bone);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(baked);
            }
        }

        [TestCase(0f, 80f)]
        [TestCase(40f, 40f)]
        [TestCase(40f, 100f)]
        [TestCase(80f, 25f)]
        public void Update_NonZeroInitializationUsesRelativeBlendShapeDelta(
            float initialWeight,
            float currentWeight)
        {
            var root = new GameObject("Initial BlendShape Root");
            var bone = new GameObject("Bone");
            var source = CreateBlendShapeCube();
            var initialBaked = new Mesh();
            var currentBaked = new Mesh();
            try
            {
                bone.transform.SetParent(root.transform, false);
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                renderer.bones = new[] { bone.transform };
                renderer.rootBone = bone.transform;

                renderer.SetBlendShapeWeight(0, initialWeight);
                renderer.BakeMesh(initialBaked);
                renderer.SetBlendShapeWeight(0, currentWeight);
                renderer.BakeMesh(currentBaked);

                var cache = new LatticeControlPointSkinning();
                Assert.That(
                    cache.Update(
                        renderer,
                        source,
                        source,
                        source.bounds,
                        new Vector3Int(2, 2, 2),
                        root.transform.worldToLocalMatrix,
                        renderer,
                        new[] { initialWeight }),
                    Is.True);

                Vector3[] neutral = source.vertices;
                Vector3[] initial = initialBaked.vertices;
                Vector3[] current = currentBaked.vertices;
                for (int control = 0; control < 8; control++)
                {
                    Vector3 expected = neutral[control] + current[control] - initial[control];
                    Assert.That(
                        cache.TryTransformPoint(control, neutral[control], out Vector3 actual),
                        Is.True);
                    AssertVector(actual, expected);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(bone);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(initialBaked);
                Object.DestroyImmediate(currentBaked);
            }
        }

        [Test]
        public void Update_LargeMeshRetainsUnsampledExtremeBlendShapeVertex()
        {
            var root = new GameObject("Large BlendShape Root");
            var bone = new GameObject("Bone");
            var source = CreateLargeBlendShapeCube();
            var baked = new Mesh();
            try
            {
                bone.transform.SetParent(root.transform, false);
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                renderer.bones = new[] { bone.transform };
                renderer.rootBone = bone.transform;
                renderer.SetBlendShapeWeight(0, 100f);

                var cache = new LatticeControlPointSkinning();
                Assert.That(
                    cache.Update(
                        renderer,
                        source,
                        source,
                        source.bounds,
                        new Vector3Int(2, 2, 2),
                        root.transform.worldToLocalMatrix,
                        renderer,
                        new[] { 0f }),
                    Is.True);
                renderer.BakeMesh(baked);

                Assert.That(
                    cache.TryTransformPoint(1, source.vertices[1], out Vector3 actual),
                    Is.True);
                AssertVector(actual, baked.vertices[1]);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(bone);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(baked);
            }
        }

        [TestCase(false, TestName = "Update_WeightSourceBlendShapesAreMatchedByName_WhenReordered")]
        [TestCase(true, TestName = "Update_WeightSourceBlendShapesAreMatchedByName_WhenOneIsMissing")]
        public void Update_WeightSourceBlendShapesAreMatchedByName(bool omitShapeA)
        {
            var root = new GameObject("Named BlendShape Root");
            var bone = new GameObject("Bone");
            var weightObject = new GameObject("Weight Source");
            var source = CreateNamedBlendShapeCube(new[] { "A", "B" });
            var weightMesh = CreateNamedBlendShapeCube(
                omitShapeA ? new[] { "B" } : new[] { "B", "A" });
            var baked = new Mesh();
            try
            {
                bone.transform.SetParent(root.transform, false);
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                renderer.bones = new[] { bone.transform };
                renderer.rootBone = bone.transform;

                var weightSource = weightObject.AddComponent<SkinnedMeshRenderer>();
                weightSource.sharedMesh = weightMesh;
                weightSource.SetBlendShapeWeight(0, 70f); // B
                if (!omitShapeA)
                weightSource.SetBlendShapeWeight(1, 20f); // A

                renderer.SetBlendShapeWeight(0, omitShapeA ? 0f : 20f); // A
                renderer.SetBlendShapeWeight(1, 70f); // B
                renderer.BakeMesh(baked);

                var cache = new LatticeControlPointSkinning();
                Assert.That(
                    cache.Update(
                        renderer,
                        source,
                        source,
                        source.bounds,
                        new Vector3Int(2, 2, 2),
                        root.transform.worldToLocalMatrix,
                        weightSource,
                        new[] { 0f, 0f }),
                    Is.True);

                Vector3[] neutral = source.vertices;
                Vector3[] expected = baked.vertices;
                for (int control = 0; control < 8; control++)
                {
                    Assert.That(
                        cache.TryTransformPoint(control, neutral[control], out Vector3 actual),
                        Is.True);
                    AssertVector(actual, expected[control]);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(bone);
                Object.DestroyImmediate(weightObject);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(weightMesh);
                Object.DestroyImmediate(baked);
            }
        }

        [Test]
        public void Update_NegativeAndZeroFrameWeightsMatchFrameInterpolationAcrossSequence()
        {
            var root = new GameObject("Signed Frame Root");
            var bone = new GameObject("Bone");
            var source = CreateSignedFrameBlendShapeCube();
            try
            {
                bone.transform.SetParent(root.transform, false);
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                renderer.bones = new[] { bone.transform };
                renderer.rootBone = bone.transform;
                renderer.SetBlendShapeWeight(0, 0f);
                Vector3[] initial = EvaluateBlendShapeVertices(source, new[] { 0f });

                var cache = new LatticeControlPointSkinning();
                int initialBindingRefreshes = -1;
                float[] weights = { -100f, -70f, -40f, -10f, 0f, 20f, 35f };
                for (int step = 0; step < weights.Length; step++)
                {
                    renderer.SetBlendShapeWeight(0, weights[step]);
                    Vector3[] current = EvaluateBlendShapeVertices(source, new[] { weights[step] });
                    Assert.That(
                        cache.Update(
                            renderer,
                            source,
                            source,
                            source.bounds,
                            new Vector3Int(2, 2, 2),
                            root.transform.worldToLocalMatrix,
                            renderer,
                            new[] { 0f }),
                        Is.True,
                        $"weight={weights[step]}");
                    if (step == 0)
                        initialBindingRefreshes = cache.BindingRefreshCountForTests;
                    else
                        Assert.That(cache.BindingRefreshCountForTests, Is.EqualTo(initialBindingRefreshes));

                    Vector3[] neutral = source.vertices;
                    for (int control = 0; control < 8; control++)
                    {
                        Vector3 expected = neutral[control] + current[control] - initial[control];
                        Assert.That(
                            cache.TryTransformPoint(control, neutral[control], out Vector3 actual),
                            Is.True);
                        Assert.That(
                            Vector3.Distance(actual, expected),
                            Is.LessThanOrEqualTo(1e-5f),
                            $"weight={weights[step]}, control={control}, " +
                            $"expected={expected}, actual={actual}");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(bone);
                Object.DestroyImmediate(source);
            }
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void Update_NonFiniteBlendShapeWeightKeepsFiniteNeutralCage(float weight)
        {
            var root = new GameObject("Non-finite Shape Root");
            var bone = new GameObject("Bone");
            var source = CreateBlendShapeCube();
            try
            {
                bone.transform.SetParent(root.transform, false);
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                renderer.bones = new[] { bone.transform };
                renderer.rootBone = bone.transform;
                renderer.SetBlendShapeWeight(0, weight);

                var cache = new LatticeControlPointSkinning();
                Assert.That(
                    cache.Update(
                        renderer,
                        source,
                        source,
                        source.bounds,
                        new Vector3Int(2, 2, 2),
                        root.transform.worldToLocalMatrix,
                        renderer,
                        new[] { 0f }),
                    Is.True);
                Assert.That(
                    cache.TryTransformPoint(0, source.vertices[0], out Vector3 actual),
                    Is.True);
                AssertVector(actual, source.vertices[0]);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(bone);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        [Category("CageShapeExploration")]
        public void SeededMultiShapeStateSpace_AlwaysMatchesExpectedBlendShapeVertices()
        {
            int seed = ReadInteger("LATTICE_CAGE_SHAPE_SEED", 14501);
            int steps = Mathf.Clamp(ReadInteger("LATTICE_CAGE_SHAPE_STEPS", 192), 1, 1024);
            var random = new System.Random(seed);
            var root = new GameObject("Seeded Multi Shape Root");
            var bone = new GameObject("Bone");
            var source = CreateExplorationBlendShapeCube();
            try
            {
                bone.transform.SetParent(root.transform, false);
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                renderer.bones = new[] { bone.transform };
                renderer.rootBone = bone.transform;
                var cache = new LatticeControlPointSkinning();
                int bindingRefreshes = -1;
                var initialWeights = new float[source.blendShapeCount];
                var currentWeights = new float[source.blendShapeCount];

                for (int step = 0; step < steps; step++)
                {
                    for (int shape = 0; shape < source.blendShapeCount; shape++)
                    {
                        initialWeights[shape] = RandomRange(random, -60f, 150f);
                        currentWeights[shape] = RandomRange(random, -100f, 180f);
                        renderer.SetBlendShapeWeight(shape, initialWeights[shape]);
                    }
                    Vector3[] initial = EvaluateBlendShapeVertices(source, initialWeights);
                    for (int shape = 0; shape < source.blendShapeCount; shape++)
                        renderer.SetBlendShapeWeight(shape, currentWeights[shape]);
                    Vector3[] current = EvaluateBlendShapeVertices(source, currentWeights);

                    Assert.That(
                        cache.Update(
                            renderer,
                            source,
                            source,
                            source.bounds,
                            new Vector3Int(2, 2, 2),
                            root.transform.worldToLocalMatrix,
                            renderer,
                            initialWeights),
                        Is.True,
                        $"seed={seed}, step={step}");
                    if (step == 0)
                        bindingRefreshes = cache.BindingRefreshCountForTests;
                    else
                        Assert.That(
                            cache.BindingRefreshCountForTests,
                            Is.EqualTo(bindingRefreshes),
                            $"Shape-only changes must not rebind. seed={seed}, step={step}");

                    Vector3[] neutral = source.vertices;
                    for (int control = 0; control < 8; control++)
                    {
                        Vector3 expected = neutral[control] + current[control] - initial[control];
                        Assert.That(
                            cache.TryTransformPoint(control, neutral[control], out Vector3 actual),
                            Is.True);
                        Assert.That(
                            Vector3.Distance(actual, expected),
                            Is.LessThanOrEqualTo(1e-5f),
                            $"seed={seed}, step={step}, control={control}, " +
                            $"initial=[{string.Join(",", initialWeights)}], " +
                            $"current=[{string.Join(",", currentWeights)}], " +
                            $"expected={expected}, actual={actual}");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(bone);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void Update_TopologyChangedProxyStillAppliesSourceBlendShape()
        {
            var root = new GameObject("Topology Changed Shape Root");
            var bone = new GameObject("Bone");
            var sourceObject = new GameObject("Source Weight Renderer");
            var source = CreateBlendShapeCube();
            var proxy = CreateTopologyChangedProxy(source);
            var baked = new Mesh();
            try
            {
                bone.transform.SetParent(root.transform, false);
                var proxyRenderer = root.AddComponent<SkinnedMeshRenderer>();
                proxyRenderer.sharedMesh = proxy;
                proxyRenderer.bones = new[] { bone.transform };
                proxyRenderer.rootBone = bone.transform;
                var weightSource = sourceObject.AddComponent<SkinnedMeshRenderer>();
                weightSource.sharedMesh = source;
                weightSource.bones = new[] { bone.transform };
                weightSource.rootBone = bone.transform;
                weightSource.SetBlendShapeWeight(0, 85f);
                weightSource.BakeMesh(baked);

                var cache = new LatticeControlPointSkinning();
                Assert.That(
                    cache.Update(
                        proxyRenderer,
                        source,
                        proxy,
                        source.bounds,
                        new Vector3Int(2, 2, 2),
                        root.transform.worldToLocalMatrix,
                        weightSource,
                        new[] { 0f }),
                    Is.True);
                Vector3[] neutral = source.vertices;
                Vector3[] expected = baked.vertices;
                for (int control = 0; control < 8; control++)
                {
                    Assert.That(
                        cache.TryTransformPoint(control, neutral[control], out Vector3 actual),
                        Is.True);
                    AssertVector(actual, expected[control]);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(bone);
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(proxy);
                Object.DestroyImmediate(baked);
            }
        }

        [Test]
        public void Update_DegenerateBoundsAxisKeepsFiniteShapeAlignedCage()
        {
            var root = new GameObject("Planar Shape Root");
            var bone = new GameObject("Bone");
            var source = CreatePlanarBlendShapeMesh();
            var baked = new Mesh();
            try
            {
                bone.transform.SetParent(root.transform, false);
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                renderer.bones = new[] { bone.transform };
                renderer.rootBone = bone.transform;
                renderer.SetBlendShapeWeight(0, 75f);
                renderer.BakeMesh(baked);

                var cache = new LatticeControlPointSkinning();
                Assert.That(
                    cache.Update(
                        renderer,
                        source,
                        source,
                        source.bounds,
                        new Vector3Int(2, 2, 1),
                        root.transform.worldToLocalMatrix,
                        renderer,
                        new[] { 0f }),
                    Is.True);
                Vector3[] neutral = source.vertices;
                Vector3[] expected = baked.vertices;
                for (int control = 0; control < 4; control++)
                {
                    Assert.That(
                        cache.TryTransformPoint(control, neutral[control], out Vector3 actual),
                        Is.True);
                    AssertVector(actual, expected[control]);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(bone);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(baked);
            }
        }

        [Test]
        public void LatticeDeformer_ResetAndReinitializeMaintainExplicitShapeBaseline()
        {
            var root = new GameObject("Shape Baseline Root");
            var bone = new GameObject("Bone");
            var source = CreateBlendShapeCube();
            try
            {
                bone.transform.SetParent(root.transform, false);
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                renderer.bones = new[] { bone.transform };
                renderer.rootBone = bone.transform;
                renderer.SetBlendShapeWeight(0, 40f);
                var deformer = root.AddComponent<LatticeDeformer>();
                deformer.Reset();
                Assert.That(deformer.InitialBlendShapeWeightsForEditor, Is.EqualTo(new[] { 40f }));

                renderer.SetBlendShapeWeight(0, 90f);
                deformer.InitializeFromSource(false);
                Assert.That(
                    deformer.InitialBlendShapeWeightsForEditor,
                    Is.EqualTo(new[] { 40f }),
                    "A non-reset refresh must not redefine the editing baseline.");

                deformer.InitializeFromSource(true);
                Assert.That(
                    deformer.InitialBlendShapeWeightsForEditor,
                    Is.EqualTo(new[] { 90f }),
                    "An explicit control-point reset must adopt the currently visible Shape.");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(bone);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void LatticeDeformer_SerializedRoundTripPreservesShapeBaseline()
        {
            var root = new GameObject("Serialized Shape Baseline Root");
            var bone = new GameObject("Bone");
            var source = CreateBlendShapeCube();
            try
            {
                bone.transform.SetParent(root.transform, false);
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                renderer.bones = new[] { bone.transform };
                renderer.rootBone = bone.transform;
                renderer.SetBlendShapeWeight(0, 37f);
                var deformer = root.AddComponent<LatticeDeformer>();
                deformer.Reset();
                string json = EditorJsonUtility.ToJson(deformer);

                renderer.SetBlendShapeWeight(0, 91f);
                deformer.Reset();
                Assert.That(deformer.InitialBlendShapeWeightsForEditor, Is.EqualTo(new[] { 91f }));
                EditorJsonUtility.FromJsonOverwrite(json, deformer);
                Assert.That(
                    deformer.InitialBlendShapeWeightsForEditor,
                    Is.EqualTo(new[] { 37f }),
                    "Saving and reopening a component must retain its original Shape baseline.");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(bone);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void LatticeDeformer_PreBaselineComponentAdoptsVisibleShapeWithoutResettingCage()
        {
            var root = new GameObject("Legacy Shape Baseline Root");
            var bone = new GameObject("Bone");
            var source = CreateBlendShapeCube();
            try
            {
                bone.transform.SetParent(root.transform, false);
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                renderer.bones = new[] { bone.transform };
                renderer.rootBone = bone.transform;
                renderer.SetBlendShapeWeight(0, 64f);
                var deformer = root.AddComponent<LatticeDeformer>();
                deformer.Reset();
                Vector3 edited = deformer.Groups[0].Layers[0].Settings.GetControlPointLocal(0) + Vector3.right;
                deformer.Groups[0].Layers[0].Settings.SetControlPointLocal(0, edited);

                var serialized = new SerializedObject(deformer);
                serialized.FindProperty("_initialBlendShapeWeights").arraySize = 0;
                serialized.FindProperty("_hasInitialBlendShapeWeightBaseline").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                deformer.enabled = false;
                deformer.enabled = true;

                Assert.That(deformer.InitialBlendShapeWeightsForEditor, Is.EqualTo(new[] { 64f }));
                AssertVector(deformer.Groups[0].Layers[0].Settings.GetControlPointLocal(0), edited);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(bone);
                Object.DestroyImmediate(source);
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

        private static Mesh CreateBlendShapeCube()
        {
            Vector3[] vertices = CreateCubeVertices();
            var mesh = new Mesh
            {
                name = "Multi-frame BlendShape Cube",
                vertices = vertices,
                triangles = CreateCubeTriangles(),
                boneWeights = CreateSingleBoneWeights(vertices.Length),
                bindposes = new[] { Matrix4x4.identity },
            };
            var frame25 = new Vector3[vertices.Length];
            var frame60 = new Vector3[vertices.Length];
            var frame100 = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                frame25[i] = new Vector3(vertices[i].y * 0.08f, 0.03f, vertices[i].x * 0.05f);
                frame60[i] = new Vector3(vertices[i].y * -0.12f, 0.11f + vertices[i].x * 0.04f, 0.02f);
                frame100[i] = new Vector3(0.2f, vertices[i].x * vertices[i].y, -vertices[i].z * 0.3f);
            }
            mesh.AddBlendShapeFrame("Shape", 25f, frame25, null, null);
            mesh.AddBlendShapeFrame("Shape", 60f, frame60, null, null);
            mesh.AddBlendShapeFrame("Shape", 100f, frame100, null, null);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateLargeBlendShapeCube()
        {
            const int vertexCount = 2050;
            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] cube = CreateCubeVertices();
            for (int i = 0; i < cube.Length; i++)
                vertices[i] = cube[i];
            for (int i = cube.Length; i < vertices.Length; i++)
            {
                float t = (float)(i - cube.Length) / (vertices.Length - cube.Length - 1);
                vertices[i] = new Vector3(
                    Mathf.Lerp(-0.45f, 0.45f, t),
                    Mathf.Sin(i * 0.17f) * 0.2f,
                    Mathf.Cos(i * 0.11f) * 0.08f);
            }
            var mesh = new Mesh
            {
                name = "Large BlendShape Cube",
                vertices = vertices,
                triangles = CreateCubeTriangles(),
                boneWeights = CreateSingleBoneWeights(vertices.Length),
                bindposes = new[] { Matrix4x4.identity },
            };
            var deltas = new Vector3[vertices.Length];
            deltas[1] = new Vector3(0f, 0.4f, 0.15f);
            mesh.AddBlendShapeFrame("Extreme", 100f, deltas, null, null);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateNamedBlendShapeCube(string[] shapeOrder)
        {
            Vector3[] vertices = CreateCubeVertices();
            var mesh = new Mesh
            {
                name = "Named BlendShape Cube",
                vertices = vertices,
                triangles = CreateCubeTriangles(),
                boneWeights = CreateSingleBoneWeights(vertices.Length),
                bindposes = new[] { Matrix4x4.identity },
            };
            foreach (string shapeName in shapeOrder)
            {
                var deltas = new Vector3[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    deltas[i] = shapeName == "A"
                        ? new Vector3(0.15f + vertices[i].y * 0.1f, 0f, 0.02f)
                        : new Vector3(0f, -0.2f + vertices[i].x * 0.08f, 0.05f);
                }
                mesh.AddBlendShapeFrame(shapeName, 100f, deltas, null, null);
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateSignedFrameBlendShapeCube()
        {
            Vector3[] vertices = CreateCubeVertices();
            var mesh = new Mesh
            {
                name = "Signed Frame BlendShape Cube",
                vertices = vertices,
                triangles = CreateCubeTriangles(),
                boneWeights = CreateSingleBoneWeights(vertices.Length),
                bindposes = new[] { Matrix4x4.identity },
            };
            float[] frameWeights = { -100f, -40f, 0f, 35f };
            for (int frame = 0; frame < frameWeights.Length; frame++)
            {
                var deltas = new Vector3[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    float factor = frame + 1f;
                    deltas[i] = new Vector3(
                        vertices[i].y * 0.03f * factor,
                        (0.02f + vertices[i].x * 0.01f) * factor,
                        vertices[i].z * -0.05f * factor);
                }
                mesh.AddBlendShapeFrame("Signed", frameWeights[frame], deltas, null, null);
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateExplorationBlendShapeCube()
        {
            Vector3[] vertices = CreateCubeVertices();
            var mesh = new Mesh
            {
                name = "Exploration BlendShape Cube",
                vertices = vertices,
                triangles = CreateCubeTriangles(),
                boneWeights = CreateSingleBoneWeights(vertices.Length),
                bindposes = new[] { Matrix4x4.identity },
            };
            float[][] frameWeights =
            {
                new[] { 100f },
                new[] { 20f, 55f, 100f },
                new[] { 25f, 80f },
            };
            for (int shape = 0; shape < frameWeights.Length; shape++)
            {
                for (int frame = 0; frame < frameWeights[shape].Length; frame++)
                {
                    var deltas = new Vector3[vertices.Length];
                    float factor = (shape + 1f) * (frame + 1f);
                    for (int vertex = 0; vertex < vertices.Length; vertex++)
                    {
                        deltas[vertex] = new Vector3(
                            (0.01f + vertices[vertex].y * 0.04f) * factor,
                            (-0.015f + vertices[vertex].x * 0.03f) * factor,
                            vertices[vertex].x * vertices[vertex].y * 0.05f * factor);
                    }
                    mesh.AddBlendShapeFrame(
                        $"Shape{shape}",
                        frameWeights[shape][frame],
                        deltas,
                        null,
                        null);
                }
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateTopologyChangedProxy(Mesh source)
        {
            Vector3[] sourceVertices = source.vertices;
            var vertices = new Vector3[sourceVertices.Length + 1];
            System.Array.Copy(sourceVertices, vertices, sourceVertices.Length);
            vertices[vertices.Length - 1] = Vector3.zero;
            return new Mesh
            {
                name = "Topology Changed Shape Proxy",
                vertices = vertices,
                triangles = CreateCubeTriangles(),
                boneWeights = CreateSingleBoneWeights(vertices.Length),
                bindposes = new[] { Matrix4x4.identity },
            };
        }

        private static Mesh CreatePlanarBlendShapeMesh()
        {
            var vertices = new[]
            {
                new Vector3(-0.5f, -0.25f, 0f),
                new Vector3(0.5f, -0.25f, 0f),
                new Vector3(-0.5f, 0.25f, 0f),
                new Vector3(0.5f, 0.25f, 0f),
            };
            var mesh = new Mesh
            {
                name = "Planar BlendShape Mesh",
                vertices = vertices,
                triangles = new[] { 0, 2, 1, 1, 2, 3 },
                boneWeights = CreateSingleBoneWeights(vertices.Length),
                bindposes = new[] { Matrix4x4.identity },
            };
            var deltas = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
                deltas[i] = new Vector3(vertices[i].y * 0.1f, 0.08f, vertices[i].x * 0.05f);
            mesh.AddBlendShapeFrame("Planar", 100f, deltas, null, null);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static int ReadInteger(string name, int fallback)
        {
            string raw = System.Environment.GetEnvironmentVariable(name);
            return int.TryParse(raw, out int value) ? value : fallback;
        }

        private static Vector3[] EvaluateBlendShapeVertices(Mesh mesh, float[] weights)
        {
            Vector3[] result = mesh.vertices;
            int vertexCount = mesh.vertexCount;
            var lower = new Vector3[vertexCount];
            var upper = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var tangents = new Vector3[vertexCount];

            for (int shape = 0; shape < mesh.blendShapeCount; shape++)
            {
                float weight = shape < weights.Length ? weights[shape] : 0f;
                int frameCount = mesh.GetBlendShapeFrameCount(shape);
                if (frameCount <= 0 || float.IsNaN(weight) || float.IsInfinity(weight))
                    continue;

                int lowerFrame = 0;
                int upperFrame = 0;
                float interpolation = 0f;
                float firstWeight = mesh.GetBlendShapeFrameWeight(shape, 0);
                if (frameCount == 1 || weight <= firstWeight)
                {
                    interpolation = Mathf.Abs(firstWeight) > Mathf.Epsilon
                        ? weight / firstWeight
                        : 0f;
                }
                else
                {
                    int lastFrame = frameCount - 1;
                    float lastWeight = mesh.GetBlendShapeFrameWeight(shape, lastFrame);
                    if (weight >= lastWeight)
                    {
                        lowerFrame = lastFrame;
                        upperFrame = lastFrame;
                        float previousWeight = mesh.GetBlendShapeFrameWeight(shape, lastFrame - 1);
                        interpolation = Mathf.Abs(lastWeight - previousWeight) > Mathf.Epsilon
                            ? (weight - previousWeight) / (lastWeight - previousWeight)
                            : 1f;
                    }
                    else
                    {
                        for (int frame = 1; frame < frameCount; frame++)
                        {
                            float upperWeight = mesh.GetBlendShapeFrameWeight(shape, frame);
                            if (weight > upperWeight)
                                continue;

                            lowerFrame = frame - 1;
                            upperFrame = frame;
                            float lowerWeight = mesh.GetBlendShapeFrameWeight(shape, lowerFrame);
                            interpolation = Mathf.InverseLerp(lowerWeight, upperWeight, weight);
                            break;
                        }
                    }
                }

                mesh.GetBlendShapeFrameVertices(shape, lowerFrame, lower, normals, tangents);
                if (upperFrame != lowerFrame)
                    mesh.GetBlendShapeFrameVertices(shape, upperFrame, upper, normals, tangents);

                for (int vertex = 0; vertex < vertexCount; vertex++)
                {
                    result[vertex] += upperFrame == lowerFrame
                        ? lower[vertex] * interpolation
                        : Vector3.LerpUnclamped(lower[vertex], upper[vertex], interpolation);
                }
            }

            return result;
        }

        private static float RandomRange(System.Random random, float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }

        private static Vector3[] CreateCubeVertices()
        {
            return new[]
            {
                new Vector3(-0.5f, -0.25f, -0.1f),
                new Vector3(0.5f, -0.25f, -0.1f),
                new Vector3(-0.5f, 0.25f, -0.1f),
                new Vector3(0.5f, 0.25f, -0.1f),
                new Vector3(-0.5f, -0.25f, 0.1f),
                new Vector3(0.5f, -0.25f, 0.1f),
                new Vector3(-0.5f, 0.25f, 0.1f),
                new Vector3(0.5f, 0.25f, 0.1f),
            };
        }

        private static int[] CreateCubeTriangles()
        {
            return new[]
            {
                0, 2, 1, 1, 2, 3,
                4, 5, 6, 5, 7, 6,
                0, 1, 4, 1, 5, 4,
                2, 6, 3, 3, 6, 7,
                0, 4, 2, 2, 4, 6,
                1, 3, 5, 3, 7, 5,
            };
        }

        private static BoneWeight[] CreateSingleBoneWeights(int count)
        {
            var weights = new BoneWeight[count];
            for (int i = 0; i < count; i++)
                weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            return weights;
        }

        private static void AssertVector(Vector3 actual, Vector3 expected)
        {
            string message = $"expected={expected}, actual={actual}";
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(1e-5f), message);
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(1e-5f), message);
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(1e-5f), message);
        }
    }
}
#endif
