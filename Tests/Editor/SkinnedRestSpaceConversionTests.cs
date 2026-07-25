#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using nadena.dev.ndmf.preview;
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

        [Test]
        public void BrushMoveHandler_StoresConvertedRestSpaceDisplacement()
        {
            var fixture = CreateSkinnedFixture(new BoneWeight { boneIndex0 = 0, weight0 = 1f }, 1);
            var handler = new BrushToolHandler();
            try
            {
                PrepareBrushLayer(fixture.Deformer);
                fixture.Bones[0].localRotation = Quaternion.Euler(0f, 0f, 90f);
                SkinnedVertexHelper.StoreMovesInRestSpace = true;
                handler.Activate(fixture.Deformer);

                typeof(BrushToolHandler).GetField("_meshVertices", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(handler, fixture.Mesh.vertices);
                typeof(BrushToolHandler).GetField("_cachedMesh", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(handler, fixture.Mesh);
                SetStaticField(typeof(BrushToolHandler), "s_connectedOnly", false);
                SetStaticField(typeof(BrushToolHandler), "s_backfaceCulling", false);
                SetStaticField(typeof(BrushToolHandler), "s_useSurfaceDistance", false);

                var method = typeof(BrushToolHandler).GetMethod(
                    "ApplyMoveBrushLocalDelta", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null);
                bool modified = (bool)method.Invoke(handler, new object[]
                {
                    fixture.Deformer, Vector3.zero, 0.1f, 0.1f,
                    Vector3.up, Vector3.forward
                });

                Assert.That(modified, Is.True);
                AssertVector(fixture.Deformer.GetDisplacement(0), Vector3.right);
                Assert.That(fixture.Deformer.GetDisplacement(1), Is.EqualTo(Vector3.zero));
            }
            finally
            {
                handler.Deactivate();
                fixture.Destroy();
            }
        }

        [Test]
        public void BrushMirrorMoveHandler_StoresConvertedRestSpaceDisplacement()
        {
            var fixture = CreateSkinnedFixture(new BoneWeight { boneIndex0 = 0, weight0 = 1f }, 1);
            var handler = new BrushToolHandler();
            try
            {
                PrepareBrushLayer(fixture.Deformer);
                fixture.Bones[0].localRotation = Quaternion.Euler(0f, 0f, 90f);
                SkinnedVertexHelper.StoreMovesInRestSpace = true;
                handler.Activate(fixture.Deformer);

                typeof(BrushToolHandler).GetField("_meshVertices", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(handler, fixture.Mesh.vertices);
                typeof(BrushToolHandler).GetField("_cachedMesh", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(handler, fixture.Mesh);
                typeof(BrushToolHandler).GetField("_hasLastMoveBrushLocalDelta", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(handler, true);
                typeof(BrushToolHandler).GetField("_lastMoveBrushLocalDelta", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(handler, Vector3.up);
                SetStaticField(typeof(BrushToolHandler), "s_brushMode", BrushToolHandler.BrushMode.Move);
                SetStaticField(typeof(BrushToolHandler), "s_mirrorAxis", BrushToolHandler.MirrorAxis.X);
                SetStaticField(typeof(BrushToolHandler), "s_connectedOnly", false);

                var method = typeof(BrushToolHandler).GetMethod(
                    "ApplyMirror", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null);
                method.Invoke(handler, new object[]
                {
                    fixture.Deformer, Vector3.zero, Vector3.zero, 0.1f, 0.1f, 1f
                });

                AssertVector(fixture.Deformer.GetDisplacement(0), Vector3.right);
                Assert.That(fixture.Deformer.GetDisplacement(1), Is.EqualTo(Vector3.zero));
            }
            finally
            {
                handler.Deactivate();
                fixture.Destroy();
            }
        }

        [Test]
        public void VertexSelectionMoveHandler_StoresConvertedRestSpaceDisplacement()
        {
            var fixture = CreateSkinnedFixture(new BoneWeight { boneIndex0 = 0, weight0 = 1f }, 1);
            var handler = new VertexSelectionHandler();
            try
            {
                PrepareBrushLayer(fixture.Deformer);
                fixture.Bones[0].localRotation = Quaternion.Euler(0f, 0f, 90f);
                SkinnedVertexHelper.StoreMovesInRestSpace = true;
                handler.Activate(fixture.Deformer);

                var selectedField = typeof(VertexSelectionHandler).GetField(
                    "s_selectedVertices", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(selectedField, Is.Not.Null);
                var selected = (HashSet<int>)selectedField.GetValue(null);
                selected.Clear();
                selected.Add(0);

                var method = typeof(VertexSelectionHandler).GetMethod(
                    "ApplyMoveDelta", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null);
                method.Invoke(handler, new object[] { fixture.Deformer, Vector3.up });

                AssertVector(fixture.Deformer.GetDisplacement(0), Vector3.right);
                Assert.That(fixture.Deformer.GetDisplacement(1), Is.EqualTo(Vector3.zero));
            }
            finally
            {
                handler.Deactivate();
                fixture.Destroy();
            }
        }

        [Test]
        public void PosedBrushCenter_UsesDisplayedWorldPosition()
        {
            var fixture = CreateSkinnedFixture(new BoneWeight { boneIndex0 = 0, weight0 = 1f }, 1);
            var handler = new BrushToolHandler();
            try
            {
                PrepareBrushLayer(fixture.Deformer);
                fixture.Bones[0].localPosition = Vector3.right;
                handler.Activate(fixture.Deformer);
                handler.RebuildCacheIfNeeded(fixture.Mesh, fixture.Deformer);
                SetStaticField(typeof(BrushToolHandler), "s_connectedOnly", false);
                SetStaticField(typeof(BrushToolHandler), "s_backfaceCulling", false);
                SetStaticField(typeof(BrushToolHandler), "s_useSurfaceDistance", false);

                var method = typeof(BrushToolHandler).GetMethod(
                    "ApplyNormalBrush", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null);
                bool modified = (bool)method.Invoke(handler, new object[]
                {
                    fixture.Deformer, Vector3.right, 0.1f, 0.1f, 1f
                });

                Assert.That(modified, Is.True,
                    "The brush must affect the vertex at its posed visual location.");
                Assert.That(fixture.Deformer.GetDisplacement(0).sqrMagnitude, Is.GreaterThan(0f));
                Assert.That(fixture.Deformer.GetDisplacement(1), Is.EqualTo(Vector3.zero));
            }
            finally
            {
                handler.Deactivate();
                fixture.Destroy();
            }
        }

        [Test]
        public void VertexSelectionRotateAndScale_DoNotBakeCurrentPoseIntoBrushData()
        {
            var fixture = CreateSkinnedFixture(new BoneWeight { boneIndex0 = 0, weight0 = 1f }, 1);
            var handler = new VertexSelectionHandler();
            try
            {
                PrepareBrushLayer(fixture.Deformer);
                fixture.Bones[0].localPosition = Vector3.right;
                SkinnedVertexHelper.StoreMovesInRestSpace = true;
                VertexSelectionHandler.ProportionalEditing = false;
                handler.Activate(fixture.Deformer);
                handler.RebuildCacheIfNeeded(fixture.Mesh, fixture.Deformer);

                var selectedField = typeof(VertexSelectionHandler).GetField(
                    "s_selectedVertices", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(selectedField, Is.Not.Null);
                var selected = (HashSet<int>)selectedField.GetValue(null);
                selected.Clear();
                selected.Add(0);

                var rotate = typeof(VertexSelectionHandler).GetMethod(
                    "ApplyRotationDelta", BindingFlags.Instance | BindingFlags.NonPublic);
                var scale = typeof(VertexSelectionHandler).GetMethod(
                    "ApplyScaleDelta", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(rotate, Is.Not.Null);
                Assert.That(scale, Is.Not.Null);

                rotate.Invoke(handler, new object[]
                {
                    fixture.Deformer, fixture.Deformer.MeshTransform, Vector3.right, Quaternion.identity
                });
                scale.Invoke(handler, new object[]
                {
                    fixture.Deformer, fixture.Deformer.MeshTransform, Vector3.right, Vector3.one
                });

                AssertVector(fixture.Deformer.GetDisplacement(0), Vector3.zero);
            }
            finally
            {
                handler.Deactivate();
                fixture.Destroy();
            }
        }

        [Test]
        public void RestSpaceMove_IsPropagatedThroughNdmfPreviewProxyNode()
        {
            var fixture = CreateSkinnedFixture(new BoneWeight { boneIndex0 = 0, weight0 = 1f }, 1);
            var proxyObject = new GameObject("Rest Space NDMF Proxy");
            IRenderFilterNode node = null;
            Mesh upstreamProxyMesh = null;
            try
            {
                PrepareBrushLayer(fixture.Deformer);
                fixture.Bones[0].localRotation = Quaternion.Euler(0f, 0f, 90f);
                SkinnedVertexHelper.StoreMovesInRestSpace = true;
                fixture.Deformer.AddDisplacement(0,
                    SkinnedVertexHelper.ConvertMoveDeltaForStorage(fixture.Deformer, 0, Vector3.up));

                var generate = typeof(LatticeDeformerPreviewFilter).GetMethod(
                    "GeneratePreviewMesh", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(generate, Is.Not.Null);
                var previewMesh = (Mesh)generate.Invoke(null, new object[] { fixture.Deformer });
                Assert.That(previewMesh, Is.Not.Null);

                upstreamProxyMesh = Object.Instantiate(fixture.Mesh);
                var proxyRenderer = proxyObject.AddComponent<SkinnedMeshRenderer>();
                proxyRenderer.sharedMesh = upstreamProxyMesh;
                proxyRenderer.bones = fixture.Bones;
                proxyRenderer.rootBone = fixture.Bones[0];
                var originalRenderer = fixture.GameObject.GetComponent<SkinnedMeshRenderer>();
                var pairs = new List<(Renderer original, Renderer proxy)>
                {
                    (originalRenderer, proxyRenderer)
                };

                var nodeType = typeof(LatticeDeformerPreviewFilter).GetNestedType(
                    "PreviewNode", BindingFlags.NonPublic);
                Assert.That(nodeType, Is.Not.Null);
                var constructor = nodeType.GetConstructors(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0];
                node = (IRenderFilterNode)constructor.Invoke(
                    new object[] { fixture.Deformer, pairs, previewMesh });

                Assert.That(proxyRenderer.sharedMesh, Is.SameAs(previewMesh));
                AssertVector(proxyRenderer.sharedMesh.vertices[0], Vector3.right);

                node.Dispose();
                node = null;
                Assert.That(proxyRenderer.sharedMesh, Is.SameAs(upstreamProxyMesh));
            }
            finally
            {
                node?.Dispose();
                Object.DestroyImmediate(proxyObject);
                if (upstreamProxyMesh != null) Object.DestroyImmediate(upstreamProxyMesh);
                fixture.Destroy();
            }
        }

        private static void PrepareBrushLayer(LatticeDeformer deformer)
        {
            int layerIndex = deformer.AddLayer("Rest Space Brush", MeshDeformerLayerType.Brush);
            deformer.ActiveLayerIndex = layerIndex;
            deformer.EnsureDisplacementCapacity();
        }

        private static void SetStaticField(System.Type type, string name, object value)
        {
            var field = type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(null, value);
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
