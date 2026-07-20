#if UNITY_EDITOR
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class ClearanceQueryTests
    {
        private const float Epsilon = 1e-4f;

        [SetUp]
        public void SetUp()
        {
            ClearanceQueryCache.Clear();
        }

        [Test]
        public void PlaneQuery_ReturnsClosestPointBarycentricNormalAndOneSidedDistance()
        {
            var mesh = CreatePlane(false);
            try
            {
                Assert.That(ClearanceQuery.TryCreate(mesh, Matrix4x4.identity, out var query), Is.True);

                var front = query.QueryPoint(new Vector3(0.25f, 0.1f, 2f));
                Assert.That(front.IsValid, Is.True);
                AssertVector(front.ClosestPointWorld, new Vector3(0.25f, 0.1f, 0f));
                AssertVector(front.NormalWorld, Vector3.forward);
                Assert.That(front.Distance, Is.EqualTo(2f).Within(Epsilon));
                Assert.That(front.SignedClearance, Is.EqualTo(2f).Within(Epsilon));
                Assert.That(front.BarycentricCoordinate.x + front.BarycentricCoordinate.y +
                            front.BarycentricCoordinate.z, Is.EqualTo(1f).Within(Epsilon));
                Assert.That(front.TriangleIndex, Is.GreaterThanOrEqualTo(0));

                var back = query.QueryPoint(new Vector3(0.25f, 0.1f, -1f));
                Assert.That(back.SignedClearance, Is.EqualTo(-1f).Within(Epsilon));
                Assert.That(back.SignMode, Is.EqualTo(ClearanceSignMode.ReferenceNormal));
                Assert.That(back.IsClosedSurface, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void PlaneQuery_ClampsClosestPointToEdgeAndVertex()
        {
            var mesh = CreatePlane(false);
            try
            {
                ClearanceQuery.TryCreate(mesh, Matrix4x4.identity, out var query);

                var edge = query.QueryPoint(new Vector3(2f, 0.25f, 0f));
                AssertVector(edge.ClosestPointWorld, new Vector3(1f, 0.25f, 0f));
                Assert.That(edge.Distance, Is.EqualTo(1f).Within(Epsilon));

                var vertex = query.QueryPoint(new Vector3(2f, 2f, 0f));
                AssertVector(vertex.ClosestPointWorld, new Vector3(1f, 1f, 0f));
                Assert.That(vertex.Distance, Is.EqualTo(Mathf.Sqrt(2f)).Within(Epsilon));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void ClosedMeshQuery_ReturnsNegativeInsideAndPositiveOutside()
        {
            var mesh = CreateCube();
            try
            {
                Assert.That(ClearanceQuery.TryCreate(mesh, Matrix4x4.identity, out var query), Is.True);
                Assert.That(query.IsClosedSurface, Is.True);

                var inside = query.QueryPoint(Vector3.zero, ClearanceSignMode.ClosedMesh);
                Assert.That(inside.IsValid, Is.True);
                Assert.That(inside.IsInside, Is.True);
                Assert.That(inside.SignedClearance, Is.EqualTo(-1f).Within(Epsilon));
                Assert.That(inside.SignMode, Is.EqualTo(ClearanceSignMode.ClosedMesh));

                var outside = query.QueryPoint(new Vector3(3f, 0f, 0f), ClearanceSignMode.ClosedMesh);
                Assert.That(outside.IsInside, Is.False);
                Assert.That(outside.SignedClearance, Is.EqualTo(2f).Within(Epsilon));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void BuiltInSphere_ReturnsExpectedAxisDistance()
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            try
            {
                Mesh mesh = sphere.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(ClearanceQuery.TryCreate(mesh, sphere.transform.localToWorldMatrix, out var query), Is.True);

                var result = query.QueryPoint(new Vector3(0f, 0f, 2f));
                Assert.That(result.IsValid, Is.True);
                Assert.That(result.Distance, Is.EqualTo(1.5f).Within(2e-3f));
                AssertVector(result.ClosestPointWorld, new Vector3(0f, 0f, 0.5f));
            }
            finally
            {
                Object.DestroyImmediate(sphere);
            }
        }

        [Test]
        public void OpenMeshClosedRequest_ExplicitlyFallsBackToReferenceNormalMode()
        {
            var mesh = CreatePlane(false);
            try
            {
                ClearanceQuery.TryCreate(mesh, Matrix4x4.identity, out var query);
                var result = query.QueryPoint(new Vector3(0f, 0f, -0.5f), ClearanceSignMode.ClosedMesh);

                Assert.That(result.IsValid, Is.True);
                Assert.That(result.IsClosedSurface, Is.False);
                Assert.That(result.SignMode, Is.EqualTo(ClearanceSignMode.ReferenceNormal));
                Assert.That(result.IsInside, Is.False);
                Assert.That(result.SignedClearance, Is.EqualTo(-0.5f).Within(Epsilon));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void MissingAndInvertedNormals_UseFallbackOrExplicitOrientation()
        {
            var missingNormals = CreatePlane(true);
            var invertedNormals = CreatePlane(false);
            invertedNormals.normals = new[]
            {
                Vector3.back, Vector3.back, Vector3.back, Vector3.back
            };
            try
            {
                ClearanceQuery.TryCreate(missingNormals, Matrix4x4.identity, out var fallbackQuery);
                ClearanceQuery.TryCreate(invertedNormals, Matrix4x4.identity, out var invertedQuery);

                Assert.That(fallbackQuery.QueryPoint(Vector3.forward).SignedClearance,
                    Is.EqualTo(1f).Within(Epsilon));
                Assert.That(invertedQuery.QueryPoint(Vector3.forward).SignedClearance,
                    Is.EqualTo(-1f).Within(Epsilon));
            }
            finally
            {
                Object.DestroyImmediate(missingNormals);
                Object.DestroyImmediate(invertedNormals);
            }
        }

        [Test]
        public void WorldTransform_NonUniformNegativeScaleRotationAndTranslationPreserveDistance()
        {
            var mesh = CreatePlane(false);
            Matrix4x4 transform = Matrix4x4.TRS(
                new Vector3(3f, -2f, 5f),
                Quaternion.Euler(20f, 70f, -15f),
                new Vector3(-2f, 3f, 0.5f));
            try
            {
                ClearanceQuery.TryCreate(mesh, transform, out var query);
                Vector3 surface = transform.MultiplyPoint3x4(new Vector3(0.2f, -0.25f, 0f));
                Vector3 normal = transform.inverse.transpose.MultiplyVector(Vector3.forward).normalized;
                var result = query.QueryPoint(surface + normal * 1.75f);

                AssertVector(result.ClosestPointWorld, surface);
                AssertVector(result.NormalWorld, normal);
                Assert.That(result.Distance, Is.EqualTo(1.75f).Within(2e-4f));
                Assert.That(result.SignedClearance, Is.EqualTo(1.75f).Within(2e-4f));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void SameSurfaceWithDifferentTriangleDensity_ReturnsSameDistance()
        {
            var coarse = CreateGrid(1, 2f);
            var dense = CreateGrid(20, 2f);
            try
            {
                ClearanceQuery.TryCreate(coarse, Matrix4x4.identity, out var coarseQuery);
                ClearanceQuery.TryCreate(dense, Matrix4x4.identity, out var denseQuery);
                Vector3 point = new Vector3(0.37f, -0.42f, 0.8f);

                var coarseResult = coarseQuery.QueryPoint(point);
                var denseResult = denseQuery.QueryPoint(point);
                AssertVector(coarseResult.ClosestPointWorld, denseResult.ClosestPointWorld);
                Assert.That(coarseResult.Distance, Is.EqualTo(denseResult.Distance).Within(Epsilon));
                Assert.That(coarseResult.SignedClearance,
                    Is.EqualTo(denseResult.SignedClearance).Within(Epsilon));
            }
            finally
            {
                Object.DestroyImmediate(coarse);
                Object.DestroyImmediate(dense);
            }
        }

        [Test]
        public void RendererCache_ReusesAndInvalidatesForTransformAndMeshChanges()
        {
            var mesh = CreatePlane(false);
            var reference = CreateMeshRenderer("Reference", mesh);
            try
            {
                Assert.That(ClearanceQueryCache.TryGet(reference, out var first), Is.True);
                Assert.That(ClearanceQueryCache.TryGet(reference, out var reused), Is.True);
                Assert.That(reused, Is.SameAs(first));
                Assert.That(ClearanceQueryCache.BuildCount, Is.EqualTo(1));

                reference.transform.position = Vector3.right;
                Assert.That(ClearanceQueryCache.TryGet(reference, out var transformed), Is.True);
                Assert.That(transformed, Is.Not.SameAs(first));
                Assert.That(ClearanceQueryCache.BuildCount, Is.EqualTo(2));

                var vertices = mesh.vertices;
                vertices[0] += Vector3.forward * 0.1f;
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
                Assert.That(ClearanceQueryCache.TryGet(reference, out _), Is.True);
                Assert.That(ClearanceQueryCache.BuildCount, Is.EqualTo(3));
            }
            finally
            {
                Object.DestroyImmediate(reference.gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void MeshRendererTargetAndReference_AreQueriedInWorldSpace()
        {
            var referenceMesh = CreatePlane(false);
            var targetMesh = new Mesh { name = "Target Points" };
            targetMesh.vertices = new[] { Vector3.zero, Vector3.right };
            var reference = CreateMeshRenderer("Reference", referenceMesh);
            var target = CreateMeshRenderer("Target", targetMesh);
            reference.transform.position = new Vector3(2f, 0f, 1f);
            target.transform.position = new Vector3(2f, 0f, 2.25f);
            try
            {
                var results = ClearanceQueryCache.QueryRenderer(
                    target,
                    reference,
                    ClearanceSignMode.ReferenceNormal);
                Assert.That(results.Length, Is.EqualTo(2));
                Assert.That(results[0].Distance, Is.EqualTo(1.25f).Within(Epsilon));
                Assert.That(results[0].SignedClearance, Is.EqualTo(1.25f).Within(Epsilon));
            }
            finally
            {
                Object.DestroyImmediate(target.gameObject);
                Object.DestroyImmediate(reference.gameObject);
                Object.DestroyImmediate(targetMesh);
                Object.DestroyImmediate(referenceMesh);
            }
        }

        [Test]
        public void SkinnedReference_BakesCurrentPoseAndInvalidatesCache()
        {
            var mesh = CreatePlane(false);
            var root = new GameObject("Skinned Reference");
            var bone = new GameObject("Bone");
            bone.transform.SetParent(root.transform, false);
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            ConfigureSingleBoneSkin(mesh, root.transform, bone.transform);
            renderer.sharedMesh = mesh;
            renderer.bones = new[] { bone.transform };
            renderer.rootBone = bone.transform;
            try
            {
                Assert.That(ClearanceQueryCache.TryGet(renderer, out var rest), Is.True);
                Assert.That(ClearanceQueryCache.TryGet(renderer, out var reusedRest), Is.True);
                Assert.That(reusedRest, Is.SameAs(rest));
                Assert.That(ClearanceQueryCache.BuildCount, Is.EqualTo(1));
                var restResult = rest.QueryPoint(Vector3.forward * 2f);
                Assert.That(restResult.Distance, Is.EqualTo(2f).Within(Epsilon));

                bone.transform.localPosition = Vector3.forward;
                Assert.That(ClearanceQueryCache.TryGet(renderer, out var posed), Is.True);
                var posedResult = posed.QueryPoint(Vector3.forward * 2f);
                Assert.That(posedResult.Distance, Is.EqualTo(1f).Within(Epsilon));
                Assert.That(ClearanceQueryCache.BuildCount, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void SkinnedTarget_BakesCurrentPoseBeforeRendererQuery()
        {
            var referenceMesh = CreatePlane(false);
            var reference = CreateMeshRenderer("Reference", referenceMesh);
            var targetMesh = new Mesh { name = "Skinned Target Point" };
            targetMesh.vertices = new[] { Vector3.zero };
            var root = new GameObject("Skinned Target");
            var bone = new GameObject("Target Bone");
            bone.transform.SetParent(root.transform, false);
            var target = root.AddComponent<SkinnedMeshRenderer>();
            ConfigureSingleBoneSkin(targetMesh, root.transform, bone.transform);
            target.sharedMesh = targetMesh;
            target.bones = new[] { bone.transform };
            target.rootBone = bone.transform;
            bone.transform.localPosition = Vector3.forward * 1.25f;
            try
            {
                var results = ClearanceQueryCache.QueryRenderer(
                    target,
                    reference,
                    ClearanceSignMode.ReferenceNormal);

                Assert.That(results.Length, Is.EqualTo(1));
                Assert.That(results[0].IsValid, Is.True);
                Assert.That(results[0].Distance, Is.EqualTo(1.25f).Within(Epsilon));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(reference.gameObject);
                Object.DestroyImmediate(targetMesh);
                Object.DestroyImmediate(referenceMesh);
            }
        }

        [Test]
        public void BvhQuery_PrunesMostTrianglesOnDenseMesh()
        {
            var mesh = CreateGrid(60, 12f);
            try
            {
                ClearanceQuery.TryCreate(mesh, Matrix4x4.identity, out var query);
                var result = query.QueryPoint(new Vector3(-5.8f, -5.8f, 0.25f));

                Assert.That(result.IsValid, Is.True);
                Assert.That(query.TriangleCount, Is.GreaterThan(5000));
                Assert.That(result.VisitedTriangleCount, Is.LessThan(query.TriangleCount / 10));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void BvhQuery_TraversalStackScalesWithTreeDepthRatherThanNodeCount()
        {
            var mesh = CreateGrid(60, 12f);
            try
            {
                Assert.That(ClearanceQuery.TryCreate(mesh, Matrix4x4.identity, out var query), Is.True);

                Assert.That(query.NodeCount, Is.GreaterThan(1000));
                Assert.That(query.TraversalStackSize, Is.LessThan(query.NodeCount / 10));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void QueryPoint_WarmQueriesDoNotAllocateManagedScratch()
        {
            var mesh = CreateGrid(60, 12f);
            try
            {
                Assert.That(ClearanceQuery.TryCreate(mesh, Matrix4x4.identity, out var query), Is.True);
                query.QueryPoint(new Vector3(-5.8f, -5.8f, 0.25f));

                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 128; i++)
                {
                    float offset = i * 0.0001f;
                    query.QueryPoint(new Vector3(-5.8f + offset, -5.8f, 0.25f));
                }
                long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(allocated, Is.Zero,
                    "Warm nearest-point queries must reuse the BVH traversal workspace.");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void ClosedMeshQuery_WarmQueriesDoNotAllocateManagedScratch()
        {
            var mesh = CreateCube();
            try
            {
                Assert.That(ClearanceQuery.TryCreate(mesh, Matrix4x4.identity, out var query), Is.True);
                query.QueryPoint(Vector3.zero, ClearanceSignMode.ClosedMesh);
                query.QueryPoint(Vector3.right * 3f, ClearanceSignMode.ClosedMesh);

                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 128; i++)
                {
                    Vector3 point = (i & 1) == 0 ? Vector3.zero : Vector3.right * 3f;
                    query.QueryPoint(point, ClearanceSignMode.ClosedMesh);
                }
                long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(allocated, Is.Zero,
                    "Warm closed-mesh queries must reuse both traversal and ray-hit buffers.");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void BatchQuery_WithCallerOwnedResultsHasZeroWarmScratchAllocation()
        {
            var mesh = CreateGrid(60, 12f);
            var targets = new Vector3[256];
            var results = new ClearanceQueryResult[targets.Length];
            for (int i = 0; i < targets.Length; i++)
            {
                float coordinate = -5.8f + i * 0.001f;
                targets[i] = new Vector3(coordinate, -5.8f, 0.25f);
            }

            try
            {
                Assert.That(ClearanceQuery.TryCreate(mesh, Matrix4x4.identity, out var query), Is.True);
                query.QueryPoints(
                    targets,
                    Matrix4x4.identity,
                    ClearanceSignMode.ReferenceNormal,
                    results);

                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 8; i++)
                {
                    query.QueryPoints(
                        targets,
                        Matrix4x4.identity,
                        ClearanceSignMode.ReferenceNormal,
                        results);
                }
                long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(allocated, Is.Zero,
                    "A caller-owned result buffer must make repeated batch queries allocation-free.");
                Assert.That(results[0].IsValid, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void CachedBatch_WithCallerOwnedResultsHasZeroWarmManagedAllocation()
        {
            var mesh = CreateGrid(60, 12f);
            var renderer = CreateMeshRenderer("Dense Reference", mesh);
            var targets = new Vector3[256];
            var results = new ClearanceQueryResult[targets.Length];
            for (int i = 0; i < targets.Length; i++)
                targets[i] = new Vector3(-5.8f + i * 0.001f, -5.8f, 0.25f);

            try
            {
                Assert.That(ClearanceQueryCache.TryQueryPoints(
                    renderer,
                    targets,
                    Matrix4x4.identity,
                    ClearanceSignMode.ReferenceNormal,
                    results), Is.True);

                bool allSucceeded = true;
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 8; i++)
                {
                    allSucceeded &= ClearanceQueryCache.TryQueryPoints(
                        renderer,
                        targets,
                        Matrix4x4.identity,
                        ClearanceSignMode.ReferenceNormal,
                        results);
                }
                long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(allSucceeded, Is.True);
                Assert.That(allocated, Is.Zero,
                    "The production cache/hash/query path must be allocation-free with caller-owned output.");
            }
            finally
            {
                Object.DestroyImmediate(renderer.gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void SkinnedCachedBatch_ReusesBakedMeshWithZeroWarmManagedAllocation()
        {
            var mesh = CreatePlane(false);
            var root = new GameObject("Skinned Cached Reference");
            var bone = new GameObject("Cached Bone");
            bone.transform.SetParent(root.transform, false);
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            ConfigureSingleBoneSkin(mesh, root.transform, bone.transform);
            renderer.sharedMesh = mesh;
            renderer.bones = new[] { bone.transform };
            renderer.rootBone = bone.transform;
            var targets = new Vector3[256];
            var results = new ClearanceQueryResult[targets.Length];
            for (int i = 0; i < targets.Length; i++)
                targets[i] = new Vector3((i % 16) * 0.01f, (i / 16) * 0.01f, 0.25f);

            try
            {
                Assert.That(ClearanceQueryCache.TryQueryPoints(
                    renderer,
                    targets,
                    Matrix4x4.identity,
                    ClearanceSignMode.ReferenceNormal,
                    results), Is.True);

                bool allSucceeded = true;
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 8; i++)
                {
                    allSucceeded &= ClearanceQueryCache.TryQueryPoints(
                        renderer,
                        targets,
                        Matrix4x4.identity,
                        ClearanceSignMode.ReferenceNormal,
                        results);
                }
                long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(allSucceeded, Is.True);
                Assert.That(allocated, Is.Zero,
                    "A stable skinned reference must reuse its cache-owned BakeMesh.");
                Assert.That(ClearanceQueryCache.BuildCount, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void AllocatingBatch_OutputAllocationDoesNotGrowWithReferenceDensity()
        {
            var coarseMesh = CreateGrid(1, 12f);
            var denseMesh = CreateGrid(60, 12f);
            var coarse = CreateMeshRenderer("Coarse Reference", coarseMesh);
            var dense = CreateMeshRenderer("Dense Reference", denseMesh);
            var targets = new Vector3[256];
            for (int i = 0; i < targets.Length; i++)
                targets[i] = new Vector3(-5.8f + i * 0.001f, -5.8f, 0.25f);

            try
            {
                ClearanceQueryCache.QueryPoints(
                    coarse, targets, Matrix4x4.identity, ClearanceSignMode.ReferenceNormal);
                ClearanceQueryCache.QueryPoints(
                    dense, targets, Matrix4x4.identity, ClearanceSignMode.ReferenceNormal);

                long beforeCoarse = System.GC.GetAllocatedBytesForCurrentThread();
                ClearanceQueryResult[] coarseResults = ClearanceQueryCache.QueryPoints(
                    coarse, targets, Matrix4x4.identity, ClearanceSignMode.ReferenceNormal);
                long coarseAllocation = System.GC.GetAllocatedBytesForCurrentThread() - beforeCoarse;

                long beforeDense = System.GC.GetAllocatedBytesForCurrentThread();
                ClearanceQueryResult[] denseResults = ClearanceQueryCache.QueryPoints(
                    dense, targets, Matrix4x4.identity, ClearanceSignMode.ReferenceNormal);
                long denseAllocation = System.GC.GetAllocatedBytesForCurrentThread() - beforeDense;

                Assert.That(coarseResults.Length, Is.EqualTo(targets.Length));
                Assert.That(denseResults.Length, Is.EqualTo(targets.Length));
                Assert.That(denseAllocation, Is.EqualTo(coarseAllocation),
                    "Only the owned result snapshot may allocate; reference BVH density must not affect GC.");
            }
            finally
            {
                Object.DestroyImmediate(dense.gameObject);
                Object.DestroyImmediate(coarse.gameObject);
                Object.DestroyImmediate(denseMesh);
                Object.DestroyImmediate(coarseMesh);
            }
        }

        [Test]
        public void BatchQuery_MatchesScalarResultsForTransformAndClosedSign()
        {
            var mesh = CreateCube();
            var points = new[]
            {
                Vector3.zero,
                Vector3.right * 3f,
                new Vector3(0.25f, -0.4f, 0.6f)
            };
            var results = new ClearanceQueryResult[points.Length];
            Matrix4x4 transform = Matrix4x4.TRS(
                new Vector3(0.1f, -0.2f, 0.3f),
                Quaternion.Euler(10f, 20f, 30f),
                Vector3.one);
            try
            {
                Assert.That(ClearanceQuery.TryCreate(mesh, Matrix4x4.identity, out var query), Is.True);
                query.QueryPoints(points, transform, ClearanceSignMode.ClosedMesh, results);

                for (int i = 0; i < points.Length; i++)
                {
                    ClearanceQueryResult scalar = query.QueryPoint(
                        transform.MultiplyPoint3x4(points[i]),
                        ClearanceSignMode.ClosedMesh);
                    Assert.That(results[i].IsValid, Is.EqualTo(scalar.IsValid));
                    Assert.That(results[i].TriangleIndex, Is.EqualTo(scalar.TriangleIndex));
                    AssertVector(results[i].ClosestPointWorld, scalar.ClosestPointWorld);
                    AssertVector(results[i].BarycentricCoordinate, scalar.BarycentricCoordinate);
                    AssertVector(results[i].NormalWorld, scalar.NormalWorld);
                    Assert.That(results[i].Distance, Is.EqualTo(scalar.Distance).Within(Epsilon));
                    Assert.That(results[i].SignedClearance,
                        Is.EqualTo(scalar.SignedClearance).Within(Epsilon));
                    Assert.That(results[i].IsInside, Is.EqualTo(scalar.IsInside));
                    Assert.That(results[i].IsClosedSurface, Is.EqualTo(scalar.IsClosedSurface));
                    Assert.That(results[i].SignMode, Is.EqualTo(scalar.SignMode));
                    Assert.That(results[i].VisitedTriangleCount,
                        Is.EqualTo(scalar.VisitedTriangleCount));
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void RendererCache_PrunesDestroyedRendererAndItsQuery()
        {
            var mesh = CreatePlane(false);
            var renderer = CreateMeshRenderer("Temporary Reference", mesh);
            try
            {
                Assert.That(ClearanceQueryCache.TryGet(renderer, out _), Is.True);
                Assert.That(ClearanceQueryCache.EntryCount, Is.EqualTo(1));

                Object.DestroyImmediate(renderer.gameObject);
                ClearanceQueryCache.PruneDeadEntries();

                Assert.That(ClearanceQueryCache.EntryCount, Is.Zero);
            }
            finally
            {
                if (renderer != null) Object.DestroyImmediate(renderer.gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void RendererCache_SceneCloseHandlerClearsQueries()
        {
            var mesh = CreatePlane(false);
            var renderer = CreateMeshRenderer("Scene Reference", mesh);
            try
            {
                Assert.That(ClearanceQueryCache.TryGet(renderer, out _), Is.True);
                Assert.That(ClearanceQueryCache.EntryCount, Is.EqualTo(1));

                ClearanceQueryCache.HandleSceneClosed(default);

                Assert.That(ClearanceQueryCache.EntryCount, Is.Zero);
            }
            finally
            {
                if (renderer != null) Object.DestroyImmediate(renderer.gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void AllocatingBatch_InvalidReferenceDoesNotAllocateTargetSizedResults()
        {
            var referenceObject = new GameObject("Invalid Reference");
            var reference = referenceObject.AddComponent<MeshRenderer>();
            var targets = new Vector3[4096];
            try
            {
                Assert.That(ClearanceQueryCache.QueryPoints(
                    reference,
                    targets,
                    Matrix4x4.identity,
                    ClearanceSignMode.ReferenceNormal), Is.Empty);

                long before = System.GC.GetAllocatedBytesForCurrentThread();
                ClearanceQueryResult[] results = ClearanceQueryCache.QueryPoints(
                    reference,
                    targets,
                    Matrix4x4.identity,
                    ClearanceSignMode.ReferenceNormal);
                long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(results, Is.Empty);
                Assert.That(allocated, Is.Zero,
                    "An invalid reference must be rejected before allocating the target-sized snapshot.");
            }
            finally
            {
                Object.DestroyImmediate(referenceObject);
            }
        }

        [Test]
        public void FailedCaptureDisposal_PreservesCacheOwnedMeshAndDestroysTemporaryMesh()
        {
            var cacheOwned = new Mesh { name = "Cache Owned Bake Mesh" };
            var temporary = new Mesh { name = "Temporary Bake Mesh" };
            try
            {
                ClearanceQueryCache.DisposeCapturedMeshAfterFailure(cacheOwned, false);
                ClearanceQueryCache.DisposeCapturedMeshAfterFailure(temporary, true);

                Assert.That(cacheOwned == null, Is.False);
                Assert.That(temporary == null, Is.True);
            }
            finally
            {
                if (cacheOwned != null) Object.DestroyImmediate(cacheOwned);
                if (temporary != null) Object.DestroyImmediate(temporary);
            }
        }

        [Test]
        public void InvalidMeshesReturnExplicitInvalidStateWithoutThrowing()
        {
            Assert.That(ClearanceQuery.TryCreate(null, Matrix4x4.identity, out _), Is.False);
            var empty = new Mesh();
            var verticesOnly = new Mesh { vertices = new[] { Vector3.zero } };
            try
            {
                Assert.That(ClearanceQuery.TryCreate(empty, Matrix4x4.identity, out _), Is.False);
                Assert.That(ClearanceQuery.TryCreate(verticesOnly, Matrix4x4.identity, out _), Is.False);
                Assert.That(ClearanceQueryCache.QueryPoints(
                    null,
                    new[] { Vector3.zero },
                    Matrix4x4.identity,
                    ClearanceSignMode.ReferenceNormal), Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(empty);
                Object.DestroyImmediate(verticesOnly);
            }
        }

        private static Mesh CreatePlane(bool omitNormals)
        {
            var mesh = new Mesh { name = "Plane" };
            mesh.vertices = new[]
            {
                new Vector3(-1f, -1f, 0f),
                new Vector3(1f, -1f, 0f),
                new Vector3(1f, 1f, 0f),
                new Vector3(-1f, 1f, 0f)
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            if (!omitNormals)
                mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateCube()
        {
            var mesh = new Mesh { name = "Closed Cube" };
            mesh.vertices = new[]
            {
                new Vector3(-1f, -1f, -1f), new Vector3(1f, -1f, -1f),
                new Vector3(1f, 1f, -1f), new Vector3(-1f, 1f, -1f),
                new Vector3(-1f, -1f, 1f), new Vector3(1f, -1f, 1f),
                new Vector3(1f, 1f, 1f), new Vector3(-1f, 1f, 1f)
            };
            mesh.triangles = new[]
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                3, 7, 6, 3, 6, 2,
                0, 4, 7, 0, 7, 3,
                1, 2, 6, 1, 6, 5
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateGrid(int divisions, float size)
        {
            int side = divisions + 1;
            var vertices = new Vector3[side * side];
            for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                float px = ((float)x / divisions - 0.5f) * size;
                float py = ((float)y / divisions - 0.5f) * size;
                vertices[y * side + x] = new Vector3(px, py, 0f);
            }

            var triangles = new int[divisions * divisions * 6];
            int cursor = 0;
            for (int y = 0; y < divisions; y++)
            for (int x = 0; x < divisions; x++)
            {
                int a = y * side + x;
                int b = a + 1;
                int d = (y + 1) * side + x;
                int c = d + 1;
                triangles[cursor++] = a;
                triangles[cursor++] = b;
                triangles[cursor++] = c;
                triangles[cursor++] = a;
                triangles[cursor++] = c;
                triangles[cursor++] = d;
            }

            var mesh = new Mesh { name = "Grid " + divisions };
            if (vertices.Length > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static MeshRenderer CreateMeshRenderer(string name, Mesh mesh)
        {
            var gameObject = new GameObject(name);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            return gameObject.AddComponent<MeshRenderer>();
        }

        private static void ConfigureSingleBoneSkin(Mesh mesh, Transform root, Transform bone)
        {
            var weights = new BoneWeight[mesh.vertexCount];
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            }
            mesh.boneWeights = weights;
            mesh.bindposes = new[] { bone.worldToLocalMatrix * root.localToWorldMatrix };
        }

        private static void AssertVector(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Epsilon));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Epsilon));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(Epsilon));
        }
    }
}
#endif
