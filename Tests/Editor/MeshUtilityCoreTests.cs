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
                SetHandlerField(handler, "_preTransformPositions", new[] { Vector3.right });
                SetHandlerField(handler, "_handleRotation", Quaternion.Euler(10f, 20f, 30f));
                SetHandlerField(handler, "_handleScale", new Vector3(2f, 3f, 4f));

                handler.Deactivate();
                handler.Activate(secondDeformer);

                Assert.That(GetHandlerField<bool>(handler, "_isDraggingSelection"), Is.False);
                Assert.That(GetHandlerField<Vector2>(handler, "_selectionStartPos"), Is.EqualTo(Vector2.zero));
                Assert.That(GetHandlerField<bool>(handler, "_isTransforming"), Is.False);
                Assert.That(GetHandlerField<Vector3[]>(handler, "_preTransformDisplacements"), Is.Null);
                Assert.That(GetHandlerField<Vector3[]>(handler, "_preTransformPositions"), Is.Null);
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
