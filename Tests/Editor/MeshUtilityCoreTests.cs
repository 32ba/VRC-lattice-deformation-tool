#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using Net._32Ba.LatticeDeformationTool;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class MeshUtilityCoreTests
    {
        [Test]
        public void ProportionalInfluenceCache_UsesExactWorldSpaceDistance()
        {
            var cache = new VertexProportionalInfluenceCache();
            var positions = new[]
            {
                Vector3.zero,
                new Vector3(0.5f, 0f, 0f),
                new Vector3(1.01f, 0f, 0f),
                new Vector3(0f, 0.75f, 0f)
            };

            cache.Rebuild(
                positions,
                new HashSet<int> { 0 },
                1f,
                VertexSelectionHandler.FalloffType.Linear);

            Assert.That(cache.GetInfluence(0), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(cache.GetInfluence(1), Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(cache.GetInfluence(2), Is.Zero);
            Assert.That(cache.GetInfluence(3), Is.EqualTo(0.25f).Within(1e-6f));
        }

        [Test]
        public void ProportionalInfluenceCache_SpatialHashMatchesBruteForce()
        {
            const int vertexCount = 240;
            const float radius = 0.37f;
            var random = new System.Random(32017);
            var positions = new Vector3[vertexCount];
            var selected = new HashSet<int>();
            for (int index = 0; index < vertexCount; index++)
            {
                positions[index] = new Vector3(
                    (float)(random.NextDouble() * 4.0 - 2.0),
                    (float)(random.NextDouble() * 4.0 - 2.0),
                    (float)(random.NextDouble() * 4.0 - 2.0));
                if (index % 5 == 0) selected.Add(index);
            }

            var cache = new VertexProportionalInfluenceCache();
            cache.Rebuild(
                positions,
                selected,
                radius,
                VertexSelectionHandler.FalloffType.Linear);

            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                float nearest = radius;
                foreach (int selectedIndex in selected)
                {
                    nearest = Mathf.Min(
                        nearest,
                        Vector3.Distance(positions[vertexIndex], positions[selectedIndex]));
                }
                float expected = nearest < radius ? 1f - nearest / radius : 0f;
                Assert.That(cache.GetInfluence(vertexIndex), Is.EqualTo(expected).Within(1e-6f),
                    $"vertex {vertexIndex}");
            }
        }

        [Test]
        public void ProportionalInfluenceCache_HandlesEmptyInputsClearAndEveryFalloff()
        {
            var cache = new VertexProportionalInfluenceCache();
            cache.Rebuild(null, null, 1f, VertexSelectionHandler.FalloffType.Linear);
            Assert.That(cache.VertexCount, Is.Zero);

            var positions = new[] { Vector3.zero, new Vector3(0.5f, 0f, 0f), new Vector3(0.95f, 0f, 0f) };
            cache.Rebuild(positions, null, 1f, VertexSelectionHandler.FalloffType.Linear);
            cache.Rebuild(positions, new HashSet<int>(), 1f, VertexSelectionHandler.FalloffType.Linear);
            cache.Rebuild(positions, new HashSet<int> { 99 }, 1f, VertexSelectionHandler.FalloffType.Linear);
            cache.Rebuild(positions, new HashSet<int> { 0 }, 0f, VertexSelectionHandler.FalloffType.Linear);
            Assert.That(cache.GetInfluence(-1), Is.Zero);
            Assert.That(cache.GetInfluence(positions.Length), Is.Zero);

            var selected = new HashSet<int> { 0 };
            cache.Rebuild(positions, selected, 1f, VertexSelectionHandler.FalloffType.Constant);
            Assert.That(cache.GetInfluence(1), Is.EqualTo(1f));
            cache.Rebuild(positions, selected, 1f, VertexSelectionHandler.FalloffType.Sphere);
            Assert.That(cache.GetInfluence(2), Is.EqualTo(0.5f).Within(1e-5f));
            cache.Rebuild(positions, selected, 1f, VertexSelectionHandler.FalloffType.Gaussian);
            Assert.That(cache.GetInfluence(1), Is.EqualTo(Mathf.Exp(-0.75f)).Within(1e-6f));
            cache.Rebuild(positions, selected, 1f, VertexSelectionHandler.FalloffType.Smooth);
            Assert.That(cache.GetInfluence(1), Is.EqualTo(0.5f).Within(1e-6f));
            cache.Rebuild(positions, selected, 1f, (VertexSelectionHandler.FalloffType)999);
            Assert.That(cache.GetInfluence(1), Is.EqualTo(0.5f).Within(1e-6f));

            cache.Clear();
            Assert.That(cache.GetInfluence(0), Is.Zero);
            Assert.That(cache.LastQueryNodeVisits, Is.Zero);
        }

        [Test]
        public void ProportionalInfluenceCache_SparseSelectionUsesExactHashedFallback()
        {
            const int selectedCount = 40;
            var positions = new Vector3[selectedCount * 2];
            var selected = new HashSet<int>();
            for (int index = 0; index < selectedCount; index++)
            {
                positions[index] = new Vector3(index * 1000f, -index * 0.25f, index * 0.125f);
                positions[index + selectedCount] = positions[index] + Vector3.right * 0.5f;
                selected.Add(index);
            }

            var cache = new VertexProportionalInfluenceCache();
            cache.Rebuild(
                positions,
                selected,
                1f,
                VertexSelectionHandler.FalloffType.Linear);

            for (int index = 0; index < selectedCount; index++)
            {
                Assert.That(cache.GetInfluence(index), Is.EqualTo(1f));
                Assert.That(cache.GetInfluence(index + selectedCount),
                    Is.EqualTo(0.5f).Within(1e-6f));
            }
        }

        [Test]
        public void ProportionalInfluenceCache_SeventyThousandVerticesAvoidsPairwiseScanAndWarmGc()
        {
            const int width = 265;
            const int vertexCount = width * width;
            var positions = new Vector3[vertexCount];
            var selected = new HashSet<int>();
            for (int i = 0; i < vertexCount; i++)
            {
                positions[i] = new Vector3((i % width) * 0.001f, (i / width) * 0.001f, 0f);
                if (i % 70 == 0) selected.Add(i);
            }

            var cache = new VertexProportionalInfluenceCache();
            cache.Rebuild(
                positions,
                selected,
                0.004f,
                VertexSelectionHandler.FalloffType.Smooth);

            long pairwiseComparisons = (long)vertexCount * selected.Count;
            Assert.That(cache.LastQueryNodeVisits, Is.LessThan(pairwiseComparisons / 2),
                "The cached nearest-neighbor query must remain sub-quadratic.");

            long before = System.GC.GetAllocatedBytesForCurrentThread();
            cache.Rebuild(
                positions,
                selected,
                0.004f,
                VertexSelectionHandler.FalloffType.Smooth);
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void WireframeRenderer_BuildLineIndices_PreservesTriangleEdgeMultiplicity()
        {
            int[] indices = WireframeRenderer.BuildLineIndices(
                new[]
                {
                    0, 1, 2,
                    2, 1, 3
                },
                4);

            Assert.That(indices, Is.EqualTo(new[]
            {
                0, 1,
                1, 2,
                2, 0,
                2, 1,
                1, 3,
                3, 2
            }));
        }

        [Test]
        public void WireframeRenderer_BuildLineIndices_SkipsInvalidTriangles()
        {
            int[] indices = WireframeRenderer.BuildLineIndices(
                new[]
                {
                    0, 0, 1,
                    1, 2, 9
                },
                3);

            Assert.That(indices, Is.EqualTo(new[]
            {
                0, 0,
                0, 1,
                1, 0
            }));
        }

        [Test]
        public void WireframeRenderer_TriangleContentsMatch_DetectsInPlaceMutation()
        {
            var triangles = new[] { 0, 1, 2 };
            var cached = (int[])triangles.Clone();

            Assert.That(WireframeRenderer.TriangleContentsMatch(triangles, cached), Is.True);

            triangles[2] = 3;

            Assert.That(WireframeRenderer.TriangleContentsMatch(triangles, cached), Is.False);
        }

        [Test]
        public void WireframeRenderer_RecreatedMesh_ResetsTopologyCache()
        {
            var type = typeof(WireframeRenderer);
            var lineMeshField = type.GetField("s_lineMesh", BindingFlags.Static | BindingFlags.NonPublic);
            var triangleField = type.GetField(
                "s_cachedTriangleContents",
                BindingFlags.Static | BindingFlags.NonPublic);
            var vertexCountField = type.GetField(
                "s_cachedVertexCount",
                BindingFlags.Static | BindingFlags.NonPublic);
            var ensureMethod = type.GetMethod("EnsureLineMesh", BindingFlags.Static | BindingFlags.NonPublic);
            var originalLineMesh = (Mesh)lineMeshField.GetValue(null);
            var originalTriangles = (int[])triangleField.GetValue(null);
            var originalVertexCount = (int)vertexCountField.GetValue(null);
            var destroyedMesh = new Mesh();
            Mesh recreatedMesh = null;
            try
            {
                lineMeshField.SetValue(null, destroyedMesh);
                triangleField.SetValue(null, new[] { 0, 1, 2 });
                vertexCountField.SetValue(null, 3);
                Object.DestroyImmediate(destroyedMesh);

                var arguments = new object[] { false };
                recreatedMesh = (Mesh)ensureMethod.Invoke(null, arguments);

                Assert.That((bool)arguments[0], Is.True);
                Assert.That(recreatedMesh, Is.Not.Null);
                Assert.That(triangleField.GetValue(null), Is.Null);
                Assert.That(vertexCountField.GetValue(null), Is.EqualTo(-1));
            }
            finally
            {
                if (recreatedMesh != null) Object.DestroyImmediate(recreatedMesh);
                lineMeshField.SetValue(null, originalLineMesh);
                triangleField.SetValue(null, originalTriangles);
                vertexCountField.SetValue(null, originalVertexCount);
            }
        }

        [TestCase(EventType.Layout, false)]
        [TestCase(EventType.MouseDown, false)]
        [TestCase(EventType.MouseDrag, false)]
        [TestCase(EventType.Repaint, true)]
        public void VertexSelection_ShouldDrawVertices_OnlyDuringRepaint(
            EventType eventType,
            bool expected)
        {
            Assert.That(VertexSelectionHandler.ShouldDrawVertices(eventType), Is.EqualTo(expected));
        }

        [Test]
        public void VertexSelectionCache_UnchangedMeshSkipsRefreshAndInPlaceEditInvalidates()
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 }
            };
            try
            {
                var handler = new VertexSelectionHandler();
                handler.RebuildCacheIfNeeded(mesh);
                Assert.That(handler.RefreshCountForTests, Is.EqualTo(1));

                handler.RebuildCacheIfNeeded(mesh);
                Assert.That(handler.RefreshCountForTests, Is.EqualTo(1));

                var vertices = mesh.vertices;
                vertices[0] = new Vector3(0.25f, 0.25f, 0f);
                mesh.vertices = vertices;
                handler.RebuildCacheIfNeeded(mesh);

                Assert.That(handler.RefreshCountForTests, Is.EqualTo(2));
                Assert.That(
                    handler.DeformedVerticesForTests[0],
                    Is.EqualTo(new Vector3(0.25f, 0.25f, 0f)));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void VertexSelectionCache_WarmUnchangedMeshAllocatesZeroBytes()
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 }
            };
            try
            {
                var handler = new VertexSelectionHandler();
                handler.RebuildCacheIfNeeded(mesh);
                handler.RebuildCacheIfNeeded(mesh);

                long before = System.GC.GetAllocatedBytesForCurrentThread();
                handler.RebuildCacheIfNeeded(mesh);
                long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(allocated, Is.Zero);
                Assert.That(handler.RefreshCountForTests, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void VertexSelectionCache_WarmMeshRendererWithDeformerAllocatesZeroBytes()
        {
            var rendererObject = new GameObject("Renderer");
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 }
            };
            try
            {
                rendererObject.AddComponent<MeshRenderer>();
                rendererObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var deformer = rendererObject.AddComponent<LatticeDeformer>();
                var handler = new VertexSelectionHandler();
                handler.RebuildCacheIfNeeded(mesh, deformer);
                handler.RebuildCacheIfNeeded(mesh, deformer);

                long before = System.GC.GetAllocatedBytesForCurrentThread();
                handler.RebuildCacheIfNeeded(mesh, deformer);
                long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(allocated, Is.Zero);
                Assert.That(handler.RefreshCountForTests, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(rendererObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void VertexSelectionCache_WarmHundredThousandVerticesAllocatesZeroBytes()
        {
            const int vertexCount = 100000;
            const int iterations = 20;
            var vertices = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
                vertices[i] = new Vector3(i * 0.001f, i % 17, i % 31);

            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = vertices;
            try
            {
                var handler = new VertexSelectionHandler();
                handler.RebuildCacheIfNeeded(mesh);
                handler.RebuildCacheIfNeeded(mesh);
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < iterations; i++)
                    handler.RebuildCacheIfNeeded(mesh);
                long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
                stopwatch.Stop();

                double averageMilliseconds = stopwatch.Elapsed.TotalMilliseconds / iterations;
                TestContext.WriteLine(
                    $"100k warm snapshot: {averageMilliseconds:F3} ms/call, {allocated} B/{iterations} calls");
                Assert.That(allocated, Is.Zero);
                Assert.That(handler.RefreshCountForTests, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void SkinnedPoseStateHash_TracksBoneTransformAndBlendShapeWeight()
        {
            var rendererObject = new GameObject("Renderer");
            var boneObject = new GameObject("Bone");
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 }
            };
            mesh.AddBlendShapeFrame(
                "Pose",
                100f,
                new[] { Vector3.forward, Vector3.zero, Vector3.zero },
                new Vector3[3],
                new Vector3[3]);
            try
            {
                var renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.bones = new[] { boneObject.transform };
                var bones = renderer.bones;

                int initial = SkinnedVertexHelper.ComputePoseStateHash(renderer, bones);
                boneObject.transform.position = Vector3.right;
                int afterBoneMove = SkinnedVertexHelper.ComputePoseStateHash(renderer, bones);
                renderer.SetBlendShapeWeight(0, 50f);
                int afterBlendShape = SkinnedVertexHelper.ComputePoseStateHash(renderer, bones);
                var vertices = mesh.vertices;
                vertices[0] = Vector3.forward;
                mesh.vertices = vertices;
                int afterMeshEdit = SkinnedVertexHelper.ComputePoseStateHash(renderer, bones);

                Assert.That(afterBoneMove, Is.Not.EqualTo(initial));
                Assert.That(afterBlendShape, Is.Not.EqualTo(afterBoneMove));
                Assert.That(afterMeshEdit, Is.Not.EqualTo(afterBlendShape));
            }
            finally
            {
                Object.DestroyImmediate(rendererObject);
                Object.DestroyImmediate(boneObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void VertexSelectionCache_UnchangedSkinnedPoseBakesOnceAndPoseChangeRebakes()
        {
            var rendererObject = new GameObject("Renderer");
            var boneObject = new GameObject("Bone");
            var replacementBoneObject = new GameObject("Replacement Bone");
            var mesh = CreateSingleBoneTriangleMesh();
            try
            {
                boneObject.transform.SetParent(rendererObject.transform, false);
                var skinned = rendererObject.AddComponent<SkinnedMeshRenderer>();
                skinned.sharedMesh = mesh;
                skinned.rootBone = boneObject.transform;
                skinned.bones = new[] { boneObject.transform };
                var deformer = rendererObject.AddComponent<LatticeDeformer>();
                var handler = new VertexSelectionHandler();
                SkinnedVertexHelper.WorldPositionBakeCountForTests = 0;

                handler.RebuildCacheIfNeeded(mesh, deformer);
                handler.RebuildCacheIfNeeded(mesh, deformer);
                Assert.That(SkinnedVertexHelper.WorldPositionBakeCountForTests, Is.EqualTo(1));

                long before = System.GC.GetAllocatedBytesForCurrentThread();
                handler.RebuildCacheIfNeeded(mesh, deformer);
                long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.Zero);

                boneObject.transform.localPosition = Vector3.right;
                handler.RebuildCacheIfNeeded(mesh, deformer);
                Assert.That(SkinnedVertexHelper.WorldPositionBakeCountForTests, Is.EqualTo(2));

                replacementBoneObject.transform.SetParent(rendererObject.transform, false);
                skinned.bones = new[] { replacementBoneObject.transform };
                handler.RebuildCacheIfNeeded(mesh, deformer);
                Assert.That(SkinnedVertexHelper.WorldPositionBakeCountForTests, Is.EqualTo(3));

                replacementBoneObject.transform.localPosition = Vector3.up;
                handler.RebuildCacheIfNeeded(mesh, deformer);
                Assert.That(SkinnedVertexHelper.WorldPositionBakeCountForTests, Is.EqualTo(4));
            }
            finally
            {
                SkinnedVertexHelper.WorldPositionBakeCountForTests = 0;
                Object.DestroyImmediate(rendererObject);
                Object.DestroyImmediate(boneObject);
                Object.DestroyImmediate(replacementBoneObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void BrushCache_CapturesWorldPositionsAndRaycastMeshWithOneBake()
        {
            var rendererObject = new GameObject("Renderer");
            var boneObject = new GameObject("Bone");
            var mesh = CreateSingleBoneTriangleMesh();
            try
            {
                boneObject.transform.SetParent(rendererObject.transform, false);
                var renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.rootBone = boneObject.transform;
                renderer.bones = new[] { boneObject.transform };
                var deformer = rendererObject.AddComponent<LatticeDeformer>();
                var handler = new BrushToolHandler();
                SkinnedVertexHelper.WorldPositionBakeCountForTests = 0;

                handler.RebuildCacheIfNeeded(mesh, deformer);
                handler.RebuildCacheIfNeeded(mesh, deformer);

                Assert.That(SkinnedVertexHelper.WorldPositionBakeCountForTests, Is.EqualTo(1));
                Assert.That(typeof(BrushToolHandler).GetField(
                    "_worldPositions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(handler),
                    Is.Not.Null);
                Assert.That(typeof(BrushToolHandler).GetField(
                    "_raycastMesh", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(handler),
                    Is.Not.Null);

                boneObject.transform.localPosition = Vector3.right;
                handler.RebuildCacheIfNeeded(mesh, deformer);
                Assert.That(SkinnedVertexHelper.WorldPositionBakeCountForTests, Is.EqualTo(2),
                    "A changed pose must invalidate the shared brush snapshot.");
            }
            finally
            {
                SkinnedVertexHelper.WorldPositionBakeCountForTests = 0;
                Object.DestroyImmediate(rendererObject);
                Object.DestroyImmediate(boneObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void BrushCache_WarmMeshRendererAllocatesZeroBytes()
        {
            var rendererObject = new GameObject("Renderer");
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 }
            };
            try
            {
                rendererObject.AddComponent<MeshRenderer>();
                rendererObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var deformer = rendererObject.AddComponent<LatticeDeformer>();
                var handler = new BrushToolHandler();
                handler.RebuildCacheIfNeeded(mesh, deformer);
                handler.RebuildCacheIfNeeded(mesh, deformer);

                long before = System.GC.GetAllocatedBytesForCurrentThread();
                handler.RebuildCacheIfNeeded(mesh, deformer);
                long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(allocated, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(rendererObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void BrushCache_InPlaceMeshEditInvalidatesSnapshot()
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 }
            };
            try
            {
                var handler = new BrushToolHandler();
                handler.RebuildCacheIfNeeded(mesh);
                var before = (Vector3[])typeof(BrushToolHandler).GetField(
                    "_meshVertices", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(handler);

                var vertices = mesh.vertices;
                vertices[0] = new Vector3(0.25f, 0.25f, 0f);
                mesh.vertices = vertices;
                handler.RebuildCacheIfNeeded(mesh);
                var after = (Vector3[])typeof(BrushToolHandler).GetField(
                    "_meshVertices", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(handler);

                Assert.That(before[0], Is.EqualTo(Vector3.zero));
                Assert.That(after[0], Is.EqualTo(new Vector3(0.25f, 0.25f, 0f)));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void BrushHotLoops_MismatchedDisplacementPayloadFailsClosed()
        {
            var rendererObject = new GameObject("Renderer");
            BrushToolHandler handler = null;
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 }
            };
            mesh.RecalculateNormals();
            try
            {
                rendererObject.AddComponent<MeshRenderer>();
                rendererObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var deformer = rendererObject.AddComponent<LatticeDeformer>();
                deformer.Reset();
                int layerIndex = deformer.AddLayer("Malformed brush", MeshDeformerLayerType.Brush);
                var layer = deformer.Layers[layerIndex];
                var malformed = new[] { new Vector3(1f, 2f, 3f) };
                layer.BrushDisplacements = malformed;

                handler = new BrushToolHandler();
                handler.Activate(deformer);
                handler.RebuildCacheIfNeeded(mesh, deformer);

                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                Assert.That(
                    typeof(BrushToolHandler).GetMethod("ApplyNormalBrush", flags).Invoke(
                        handler, new object[] { deformer, Vector3.zero, 1f, 1f, 1f }),
                    Is.False);
                Assert.That(
                    typeof(BrushToolHandler).GetMethod("ApplyMoveBrushLocalDelta", flags).Invoke(
                        handler,
                        new object[] { deformer, Vector3.zero, 1f, 1f, Vector3.right, Vector3.forward }),
                    Is.False);
                Assert.That(
                    typeof(BrushToolHandler).GetMethod("ApplySmoothBrush", flags).Invoke(
                        handler, new object[] { deformer, Vector3.zero, 1f, 1f }),
                    Is.False);
                Assert.DoesNotThrow(() =>
                    typeof(BrushToolHandler).GetMethod("ApplyMirror", flags).Invoke(
                        handler, new object[] { deformer, Vector3.zero, 1f, 1f, 1f }));
                Assert.That(layer.BrushDisplacements, Is.SameAs(malformed));
                Assert.That(layer.BrushDisplacements[0], Is.EqualTo(new Vector3(1f, 2f, 3f)));
            }
            finally
            {
                handler?.Deactivate();
                Object.DestroyImmediate(rendererObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void BrushCache_WarmSkinnedRendererAllocatesZeroBytes()
        {
            var rendererObject = new GameObject("Renderer");
            var boneObject = new GameObject("Bone");
            var mesh = CreateSingleBoneTriangleMesh();
            try
            {
                boneObject.transform.SetParent(rendererObject.transform, false);
                var renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.rootBone = boneObject.transform;
                renderer.bones = new[] { boneObject.transform };
                var deformer = rendererObject.AddComponent<LatticeDeformer>();
                var handler = new BrushToolHandler();
                handler.RebuildCacheIfNeeded(mesh, deformer);
                handler.RebuildCacheIfNeeded(mesh, deformer);

                long before = System.GC.GetAllocatedBytesForCurrentThread();
                handler.RebuildCacheIfNeeded(mesh, deformer);
                long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(allocated, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(rendererObject);
                Object.DestroyImmediate(boneObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void RestSpaceConverterCache_ReusesWarmConverterAndTracksPose()
        {
            var rendererObject = new GameObject("Renderer");
            var boneObject = new GameObject("Bone");
            var mesh = CreateSingleBoneTriangleMesh();
            try
            {
                boneObject.transform.SetParent(rendererObject.transform, false);
                var renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.rootBone = boneObject.transform;
                renderer.bones = new[] { boneObject.transform };
                var deformer = rendererObject.AddComponent<LatticeDeformer>();
                var cache = new SkinnedVertexHelper.RestSpaceDeltaConverterCache();
                var first = cache.Get(deformer);
                Assert.That(first, Is.Not.Null);

                long before = System.GC.GetAllocatedBytesForCurrentThread();
                var warm = cache.Get(deformer);
                long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(warm, Is.SameAs(first));
                Assert.That(allocated, Is.Zero);

                boneObject.transform.localPosition = Vector3.right;
                Assert.That(cache.Get(deformer), Is.Not.SameAs(first));
            }
            finally
            {
                Object.DestroyImmediate(rendererObject);
                Object.DestroyImmediate(boneObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void RestSpaceConverterCache_TracksSourceMeshAndProxyReplacement()
        {
            var rendererObject = new GameObject("Renderer");
            var proxyObject = new GameObject("Proxy");
            var boneObject = new GameObject("Bone");
            var mesh = CreateSingleBoneTriangleMesh();
            var replacementMesh = CreateSingleBoneTriangleMesh();
            try
            {
                boneObject.transform.SetParent(rendererObject.transform, false);
                var renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.rootBone = boneObject.transform;
                renderer.bones = new[] { boneObject.transform };
                var deformer = rendererObject.AddComponent<LatticeDeformer>();
                var cache = new SkinnedVertexHelper.RestSpaceDeltaConverterCache();
                var first = cache.Get(deformer);

                typeof(LatticeDeformer).GetField(
                    "_sourceMesh", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, replacementMesh);
                var replacement = cache.Get(deformer);
                Assert.That(replacement, Is.Not.SameAs(first));

                var proxyRenderer = proxyObject.AddComponent<SkinnedMeshRenderer>();
                proxyRenderer.sharedMesh = replacementMesh;
                proxyRenderer.rootBone = boneObject.transform;
                proxyRenderer.bones = new[] { boneObject.transform };
                LatticePreviewUtility.RegisterProxy(renderer, proxyRenderer);
                Assert.That(cache.Get(deformer), Is.Not.SameAs(replacement));
            }
            finally
            {
                LatticePreviewUtility.ClearProxy(rendererObject.GetComponent<Renderer>());
                Object.DestroyImmediate(rendererObject);
                Object.DestroyImmediate(proxyObject);
                Object.DestroyImmediate(boneObject);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(replacementMesh);
            }
        }

        [Test]
        public void BrushTool_IntersectRayMeshDelegate_HitsKnownTriangle()
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 }
            };
            try
            {
                var method = typeof(BrushToolHandler).GetMethod(
                    "IntersectRayMesh",
                    BindingFlags.Static | BindingFlags.NonPublic);
                var arguments = new object[]
                {
                    new Ray(new Vector3(0.25f, 0.25f, 1f), Vector3.back),
                    mesh,
                    Matrix4x4.identity,
                    null
                };

                Assert.That((bool)method.Invoke(null, arguments), Is.True);
                Assert.That(((RaycastHit)arguments[3]).triangleIndex, Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

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
        public void PenetrationDetectionCacheKey_TracksGeometryAndTransformState()
        {
            var targetMatrix = Matrix4x4.TRS(
                new Vector3(1f, 2f, 3f),
                Quaternion.Euler(10f, 20f, 30f),
                new Vector3(1f, 2f, 1f));
            var referenceMatrix = Matrix4x4.TRS(
                new Vector3(-1f, 0.5f, 2f),
                Quaternion.Euler(0f, 45f, 0f),
                Vector3.one).inverse;

            var key = new PenetrationDetectionCacheKey(
                101, 2, 11, 12, 13, 14, 100, 200, targetMatrix, referenceMatrix);
            var same = new PenetrationDetectionCacheKey(
                101, 2, 11, 12, 13, 14, 100, 200, targetMatrix, referenceMatrix);
            var changedLayer = new PenetrationDetectionCacheKey(
                102, 2, 11, 12, 13, 14, 100, 200, targetMatrix, referenceMatrix);
            var changedRuntimeMesh = new PenetrationDetectionCacheKey(
                101, 2, 11, 12, 13, 15, 100, 200, targetMatrix, referenceMatrix);
            var changedTransform = new PenetrationDetectionCacheKey(
                101,
                2,
                11,
                12,
                13,
                14,
                100,
                200,
                Matrix4x4.Translate(Vector3.right) * targetMatrix,
                referenceMatrix);

            Assert.That(same, Is.EqualTo(key));
            Assert.That(same.GetHashCode(), Is.EqualTo(key.GetHashCode()));
            Assert.That(changedLayer, Is.Not.EqualTo(key));
            Assert.That(changedRuntimeMesh, Is.Not.EqualTo(key));
            Assert.That(changedTransform, Is.Not.EqualTo(key));
            Assert.That(key.Equals((object)same), Is.True);
            Assert.That(key.Equals((object)"not a cache key"), Is.False);
        }

        [Test]
        public void MeshNormalUtility_HandlesEmptyStoredMissingAndMalformedGeometry()
        {
            Assert.That(MeshNormalUtility.GetOrCalculateNormals(null, null, null), Is.Empty);
            Assert.That(
                MeshNormalUtility.GetOrCalculateNormals(null, System.Array.Empty<Vector3>(), null),
                Is.Empty);

            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                normals = new[] { Vector3.right, Vector3.right, Vector3.right }
            };
            try
            {
                Assert.That(
                    MeshNormalUtility.GetOrCalculateNormals(mesh, mesh.vertices, null),
                    Is.EqualTo(mesh.normals));

                var withoutTriangles = MeshNormalUtility.GetOrCalculateNormals(
                    null,
                    new[] { Vector3.zero, Vector3.right },
                    null);
                Assert.That(withoutTriangles, Is.EqualTo(new[] { Vector3.zero, Vector3.zero }));

                var malformed = MeshNormalUtility.GetOrCalculateNormals(
                    null,
                    new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one },
                    new[] { 0, 1, 99 });
                Assert.That(malformed, Is.All.EqualTo(Vector3.zero));

                var isolated = MeshNormalUtility.GetOrCalculateNormals(
                    null,
                    new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward },
                    new[] { 0, 1, 2 });
                Assert.That(isolated[3], Is.EqualTo(Vector3.zero));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void BrushPenetrationDetection_UsesFullyComposedDeformation()
        {
            var targetObject = new GameObject("penetration-composed-target");
            var referenceObject = new GameObject("penetration-composed-reference");
            Mesh source = null;
            Mesh reference = null;
            var showField = typeof(BrushToolHandler).GetField(
                "s_showPenetration",
                BindingFlags.Static | BindingFlags.NonPublic);
            var referenceField = typeof(BrushToolHandler).GetField(
                "s_penetrationReference",
                BindingFlags.Static | BindingFlags.NonPublic);
            bool previousShow = (bool)showField.GetValue(null);
            var previousReference = referenceField.GetValue(null);
            try
            {
                source = new Mesh
                {
                    vertices = new[]
                    {
                        new Vector3(0f, 0f, 0.1f),
                        new Vector3(1f, 0f, 0.1f),
                        new Vector3(0f, 1f, 0.1f)
                    },
                    triangles = new[] { 0, 1, 2 }
                };
                source.RecalculateNormals();
                source.RecalculateBounds();
                var targetFilter = targetObject.AddComponent<MeshFilter>();
                targetObject.AddComponent<MeshRenderer>();
                targetFilter.sharedMesh = source;

                var deformer = targetObject.AddComponent<LatticeDeformer>();
                deformer.Reset();
                int penetratingLayerIndex = deformer.AddLayer("Non-active penetration", MeshDeformerLayerType.Brush);
                var penetratingLayer = deformer.Layers[penetratingLayerIndex];
                penetratingLayer.EnsureBrushDisplacementCapacity(source.vertexCount);
                for (int i = 0; i < source.vertexCount; i++)
                {
                    penetratingLayer.SetBrushDisplacement(i, new Vector3(0f, 0f, -0.2f));
                }

                int activeLayerIndex = deformer.AddLayer("Active neutral brush", MeshDeformerLayerType.Brush);
                deformer.Layers[activeLayerIndex].EnsureBrushDisplacementCapacity(source.vertexCount);
                Assert.That(deformer.GetDisplacement(0), Is.EqualTo(Vector3.zero));
                Assert.That(deformer.Deform(false), Is.Not.Null);

                reference = new Mesh
                {
                    vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                    normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward },
                    triangles = new[] { 0, 1, 2 }
                };
                var referenceFilter = referenceObject.AddComponent<MeshFilter>();
                var referenceRenderer = referenceObject.AddComponent<MeshRenderer>();
                referenceFilter.sharedMesh = reference;

                var handler = new BrushToolHandler();
                typeof(BrushToolHandler)
                    .GetField("_meshVertices", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(handler, source.vertices);
                showField.SetValue(null, true);
                referenceField.SetValue(null, referenceRenderer);

                typeof(BrushToolHandler)
                    .GetMethod("UpdatePenetrationDetection", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(handler, new object[] { deformer });

                var penetrating = (HashSet<int>)typeof(BrushToolHandler)
                    .GetField("_penetratingVertices", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(handler);
                Assert.That(penetrating, Is.Not.Null);
                Assert.That(penetrating.Contains(0), Is.True,
                    "Penetration from a non-active layer must be detected in the final composed output.");
                var highlightedVertices = (Vector3[])typeof(BrushToolHandler)
                    .GetField("_penetrationDeformedVertices", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(handler);
                Assert.That(highlightedVertices[0].z, Is.EqualTo(-0.1f).Within(1e-6f),
                    "Highlight positions must use the same fully composed vertices as detection.");
            }
            finally
            {
                showField.SetValue(null, previousShow);
                referenceField.SetValue(null, previousReference);
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(referenceObject);
                if (source != null) Object.DestroyImmediate(source);
                if (reference != null) Object.DestroyImmediate(reference);
            }
        }

        [Test]
        public void MeshNormalUtility_CalculatesVisualizationNormalsWithoutMutatingSourceMesh()
        {
            var source = new Mesh
            {
                name = "Normals Must Remain Unserialized",
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 }
            };
            try
            {
                Assert.That(source.normals, Is.Empty);

                var calculated = MeshNormalUtility.GetOrCalculateNormals(
                    source,
                    source.vertices,
                    source.triangles);

                Assert.That(calculated, Has.Length.EqualTo(3));
                Assert.That(calculated[0], Is.EqualTo(Vector3.forward));
                Assert.That(calculated[1], Is.EqualTo(Vector3.forward));
                Assert.That(calculated[2], Is.EqualTo(Vector3.forward));
                Assert.That(
                    source.normals,
                    Is.Empty,
                    "Editor visualization must not call RecalculateNormals on the shared source asset.");
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void MeshDeformerTool_TargetChangeRequiresHandlerReactivation()
        {
            var first = new GameObject("first-handler-target");
            var second = new GameObject("second-handler-target");
            try
            {
                var firstDeformer = first.AddComponent<LatticeDeformer>();
                var secondDeformer = second.AddComponent<LatticeDeformer>();

                Assert.That(
                    MeshDeformerTool.NeedsHandlerReactivation(firstDeformer, firstDeformer),
                    Is.False);
                Assert.That(
                    MeshDeformerTool.NeedsHandlerReactivation(firstDeformer, secondDeformer),
                    Is.True,
                    "The same handler type still owns target-specific caches and gesture state.");
                Assert.That(
                    MeshDeformerTool.NeedsHandlerReactivation(null, firstDeformer),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void VertexSelectionHandler_TargetSwitchClearsInFlightGestureState()
        {
            var first = new GameObject("first-gesture-target");
            var second = new GameObject("second-gesture-target");
            var handler = new VertexSelectionHandler();
            try
            {
                var firstDeformer = first.AddComponent<LatticeDeformer>();
                var secondDeformer = second.AddComponent<LatticeDeformer>();

                handler.Activate(firstDeformer);
                SetHandlerField(handler, "_isDraggingSelection", true);
                SetHandlerField(handler, "_selectionStartPos", new Vector2(41f, 73f));
                SetHandlerField(handler, "_isTransforming", true);
                SetHandlerField(handler, "_preTransformDisplacements", new[] { Vector3.one });
                SetHandlerField(handler, "_preTransformWorldPositions", new[] { Vector3.right });
                SetHandlerField(handler, "_handleRotation", Quaternion.Euler(10f, 20f, 30f));
                SetHandlerField(handler, "_handleScale", new Vector3(2f, 3f, 4f));

                handler.Deactivate();
                handler.Activate(secondDeformer);

                Assert.That(GetHandlerField<bool>(handler, "_isDraggingSelection"), Is.False);
                Assert.That(GetHandlerField<Vector2>(handler, "_selectionStartPos"), Is.EqualTo(Vector2.zero));
                Assert.That(GetHandlerField<bool>(handler, "_isTransforming"), Is.False);
                Assert.That(GetHandlerField<Vector3[]>(handler, "_preTransformDisplacements"), Is.Null);
                Assert.That(GetHandlerField<Vector3[]>(handler, "_preTransformWorldPositions"), Is.Null);
                Assert.That(GetHandlerField<Quaternion>(handler, "_handleRotation"), Is.EqualTo(Quaternion.identity));
                Assert.That(GetHandlerField<Vector3>(handler, "_handleScale"), Is.EqualTo(Vector3.one));
                Assert.That(
                    GetHandlerField<LatticeDeformer>(handler, "_activeDeformer"),
                    Is.SameAs(secondDeformer));
            }
            finally
            {
                handler.Deactivate();
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
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

                var reused = SkinnedVertexHelper.ComputeWorldPositions(
                    deformer,
                    new[] { Vector3.zero, Vector3.right, Vector3.up },
                    world);
                Assert.That(reused, Is.SameAs(world));
                Assert.That(reused[0].x, Is.EqualTo(1f).Within(1e-4f));
                Assert.That(reused[0].y, Is.EqualTo(2f).Within(1e-4f));
                Assert.That(reused[0].z, Is.EqualTo(3f).Within(1e-4f));
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

        private static void SetHandlerField<T>(VertexSelectionHandler handler, string name, T value)
        {
            var field = typeof(VertexSelectionHandler).GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(handler, value);
        }

        private static T GetHandlerField<T>(VertexSelectionHandler handler, string name)
        {
            var field = typeof(VertexSelectionHandler).GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            return (T)field.GetValue(handler);
        }
    }
}
#endif
