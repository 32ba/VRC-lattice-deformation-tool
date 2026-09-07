#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using nadena.dev.ndmf.preview;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class ToolPoseSnapshotTests
    {
        [UnityTest]
        [Category("GraphicsE2E")]
        public IEnumerator RealNdmfProxy_PoseAndGenerationChangesRefreshBothToolSnapshots()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Requires an initialized graphics Editor and the real NDMF preview session.");
            bool enabled = PreviewEnabled();
            bool filter = LatticeDeformerPreviewFilter.PreviewToggleEnabled;
            int depth = NDMFPreview.DisablePreviewDepth;
            var selection = Selection.objects;
            using var fixture = new PosedTriangle("Real posed proxy", Vector3.zero);
            var brush = new BrushToolHandler();
            var vertex = new VertexSelectionHandler();
            Mesh referenceBake = new Mesh();
            var shader = Shader.Find("Standard");
            Assert.That(shader != null && shader.isSupported, Is.True);
            Material material = new Material(shader);
            try
            {
                fixture.Renderer.sharedMaterial = material;
                fixture.Source.AddBlendShapeFrame("Pose shape", 100f,
                    new[] { Vector3.forward * 0.2f, Vector3.zero, Vector3.zero }, null, null);
                fixture.Deformer.ActiveLayerIndex = fixture.Deformer.AddLayer("Display deformation", MeshDeformerLayerType.Brush);
                fixture.Deformer.EnsureDisplacementCapacity();
                fixture.Deformer.Displacements[1] = Vector3.up * 0.15f;
                fixture.Deformer.Deform(false);
                var originalVertices = fixture.Source.vertices;
                fixture.Owner.SetActive(true);
                NDMFPreview.DisablePreviewDepth = 0;
                if (!enabled) Assert.That(EditorApplication.ExecuteMenuItem("Tools/NDM Framework/Enable Previews"), Is.True);
                LatticeDeformerPreviewFilter.ForcePreviewState(true);
                Selection.activeGameObject = fixture.Owner;
                yield return WaitFor(() => PreviewSession.Current != null, "NDMF session was not created.");
                PreviewSession.Current.ForceRebuild();
                yield return WaitFor(() => FinalProxy(fixture) != null, "A genuine final skinned proxy was not published.");
                var initialProxy = FinalProxy(fixture);
                AssertCapturesMatchFinalProxy(fixture, brush, vertex, referenceBake);

                fixture.Bone.localPosition = new Vector3(0.1f, 0.2f, -0.1f);
                fixture.Renderer.SetBlendShapeWeight(0, 65f);
                EditorUtility.SetDirty(fixture.Renderer);
                yield return null;
                yield return null;
                AssertCapturesMatchFinalProxy(fixture, brush, vertex, referenceBake);

                // The same handlers retain caches across an actual graph replacement.
                // They are deliberately not reactivated by selection/hierarchy callbacks.
                initialProxy = FinalProxy(fixture);
                int previousId = initialProxy.GetInstanceID();
                PreviewSession.Current.ForceRebuild();
                yield return WaitFor(() => FinalProxy(fixture) != null &&
                    !ReferenceEquals(FinalProxy(fixture), initialProxy), "NDMF did not replace the final proxy generation.");
                AssertCapturesMatchFinalProxy(fixture, brush, vertex, referenceBake);
                Debug.Log($"Posed tools followed final NDMF proxy {previousId} -> {FinalProxy(fixture).GetInstanceID()}.");
                Assert.That(fixture.Source.vertices, Is.EqualTo(originalVertices));
            }
            finally
            {
                brush.Deactivate();
                vertex.Deactivate();
                fixture.Owner.SetActive(false);
                PreviewSession.Current?.ForceRebuild();
                Object.DestroyImmediate(referenceBake);
                Object.DestroyImmediate(material);
                LatticeDeformerPreviewFilter.ForcePreviewState(filter);
                NDMFPreview.DisablePreviewDepth = depth;
                if (PreviewEnabled() != enabled) EditorApplication.ExecuteMenuItem("Tools/NDM Framework/Enable Previews");
                Selection.objects = selection;
            }
            yield return null;
            yield return null;
        }

        private static void AssertCapturesMatchFinalProxy(PosedTriangle fixture,
            BrushToolHandler brush, VertexSelectionHandler vertex, Mesh referenceBake)
        {
            var proxy = FinalProxy(fixture);
            Assert.That(proxy, Is.Not.Null);
            proxy.BakeMesh(referenceBake);
            var expected = referenceBake.vertices.Select(proxy.transform.TransformPoint).ToArray();
            brush.RebuildCacheIfNeeded(fixture.Source, fixture.Deformer);
            vertex.RebuildCacheIfNeeded(fixture.Source, fixture.Deformer);
            Assert.That(Field<SkinnedMeshRenderer>(vertex, "_cachedSkinnedRenderer"), Is.SameAs(proxy));
            foreach (object handler in new object[] { brush, vertex })
            {
                var positions = Field<Vector3[]>(handler, "_worldPositions");
                Assert.That(positions, Has.Length.EqualTo(expected.Length));
                for (int i = 0; i < expected.Length; i++)
                    Assert.That(Vector3.Distance(positions[i], expected[i]), Is.LessThan(1e-5f), handler.GetType().Name + " vertex " + i);
            }
            Assert.That(Field<Mesh>(brush, "_raycastMesh").vertices, Is.EqualTo(referenceBake.vertices));
        }

        private static SkinnedMeshRenderer FinalProxy(PosedTriangle fixture)
        {
            if (!NDMFPreviewProxyUtility.TryGetProxyRenderer(fixture.Renderer, out var proxy) ||
                proxy == null || proxy == fixture.Renderer ||
                NDMFPreview.GetOriginalObjectForProxy(proxy.gameObject) != fixture.Owner)
                return null;
            var skinned = proxy as SkinnedMeshRenderer;
            return skinned != null && skinned.sharedMesh != null && skinned.sharedMesh != fixture.Source &&
                   skinned.sharedMesh.vertexCount == fixture.Source.vertexCount ? skinned : null;
        }

        private static IEnumerator WaitFor(Func<bool> condition, string message)
        {
            double started = EditorApplication.timeSinceStartup;
            while (!condition())
            {
                Assert.That(EditorApplication.timeSinceStartup - started, Is.LessThan(8d), message);
                SceneView.RepaintAll();
                yield return null;
            }
        }

        private static bool PreviewEnabled() => typeof(NDMFPreview).GetProperty("EnablePreviewsUI",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) is bool enabled && enabled;

        [TestCase(false)]
        [TestCase(true)]
        public void AnotherToolCapture_DoesNotOverwriteRetainedBrushRaycast(bool useVertexSelection)
        {
            using var first = new PosedTriangle("First", Vector3.right);
            using var second = new PosedTriangle("Second", Vector3.up * 2f);
            var brush = new BrushToolHandler();
            var otherBrush = new BrushToolHandler();
            var vertex = new VertexSelectionHandler();
            try
            {
                brush.RebuildCacheIfNeeded(first.Source, first.Deformer);
                var retainedMesh = Field<Mesh>(brush, "_raycastMesh");
                var retainedVertices = retainedMesh.vertices;
                var retainedWorld = (Vector3[])Field<Vector3[]>(brush, "_worldPositions").Clone();
                if (useVertexSelection)
                    vertex.RebuildCacheIfNeeded(second.Source, second.Deformer);
                else
                    otherBrush.RebuildCacheIfNeeded(second.Source, second.Deformer);

                Assert.That(retainedMesh.vertices, Is.EqualTo(retainedVertices),
                    "Another capture changed the raycast surface retained by the first brush.");
                Assert.That(Field<Vector3[]>(brush, "_worldPositions"), Is.EqualTo(retainedWorld));
                brush.RebuildCacheIfNeeded(first.Source, first.Deformer);
                Assert.That(Field<Mesh>(brush, "_raycastMesh"), Is.SameAs(retainedMesh));
                Assert.That(retainedMesh.vertices, Is.EqualTo(retainedVertices),
                    "An unchanged pose must keep a coherent world-position/raycast snapshot.");
            }
            finally
            {
                brush.Deactivate();
                otherBrush.Deactivate();
                vertex.Deactivate();
            }
        }

        [Test]
        public void Capture_ReusesOwnedMeshAndWorldArrayWhilePoseAndTransformChange()
        {
            using var fixture = new PosedTriangle("Pose", Vector3.right);
            using var snapshot = new SkinnedPoseSnapshot("Pose reuse test");
            var sourceVertices = fixture.Source.vertices;
            var sourceWeights = fixture.Source.boneWeights;
            var sourceBindPoses = fixture.Source.bindposes;
            string raw = UnityEditor.EditorJsonUtility.ToJson(fixture.Deformer);
            Assert.That(snapshot.TryCapture(fixture.Renderer, 3), Is.True);
            Mesh owned = snapshot.Mesh;
            var first = snapshot.CopyWorldPositions();
            Assert.That(Vector3.Distance(first[0], Vector3.right), Is.LessThan(1e-5f));

            fixture.Bone.localPosition = Vector3.up;
            fixture.Owner.transform.position = new Vector3(2f, 3f, 4f);
            fixture.Owner.transform.rotation = Quaternion.Euler(13f, 27f, 9f);
            fixture.Owner.transform.localScale = new Vector3(1.5f, 0.75f, 2f);
            Assert.That(snapshot.TryCapture(fixture.Renderer, 3), Is.True);
            Assert.That(snapshot.Mesh, Is.SameAs(owned));
            var second = snapshot.CopyWorldPositions(first);
            Assert.That(second, Is.SameAs(first));
            Assert.That(snapshot.LocalToWorld, Is.EqualTo(fixture.Owner.transform.localToWorldMatrix));
            for (int i = 0; i < second.Length; i++)
                Assert.That(Vector3.Distance(second[i], snapshot.LocalToWorld.MultiplyPoint3x4(snapshot.LocalVertices[i])), Is.LessThan(1e-5f));
            Assert.That(fixture.Renderer.sharedMesh, Is.SameAs(fixture.Source));
            Assert.That(fixture.Source.vertices, Is.EqualTo(sourceVertices));
            Assert.That(fixture.Source.boneWeights, Is.EqualTo(sourceWeights));
            Assert.That(fixture.Source.bindposes, Is.EqualTo(sourceBindPoses));
            Assert.That(UnityEditor.EditorJsonUtility.ToJson(fixture.Deformer), Is.EqualTo(raw));
        }

        [TestCase("null-renderer")]
        [TestCase("missing-mesh")]
        [TestCase("count-mismatch")]
        [TestCase("empty-count")]
        [TestCase("destroyed-renderer")]
        public void RejectedCapture_DoesNotExposeStalePoseAndCanRecover(string failure)
        {
            using var fixture = new PosedTriangle("Invalid", Vector3.right);
            using var snapshot = new SkinnedPoseSnapshot("Invalid pose test");
            Assert.That(snapshot.TryCapture(fixture.Renderer, 3), Is.True);
            Mesh owned = snapshot.Mesh;
            int count = 3;
            var renderer = fixture.Renderer;
            switch (failure)
            {
                case "null-renderer": renderer = null; break;
                case "missing-mesh": renderer.sharedMesh = null; break;
                case "count-mismatch": count = 4; break;
                case "empty-count": count = 0; break;
                case "destroyed-renderer": Object.DestroyImmediate(renderer); break;
            }
            Assert.That(snapshot.TryCapture(renderer, count), Is.False);
            Assert.That(snapshot.Mesh, Is.Null);
            Assert.That(snapshot.LocalVertices, Is.Empty);
            Assert.That(snapshot.CopyWorldPositions(), Is.Null);
            Assert.That(snapshot.LocalToWorld, Is.EqualTo(Matrix4x4.identity));
            using var replacement = new PosedTriangle("Recovery", Vector3.up);
            Assert.That(snapshot.TryCapture(replacement.Renderer, 3), Is.True);
            Assert.That(snapshot.Mesh, Is.SameAs(owned));
            Assert.That(Vector3.Distance(snapshot.CopyWorldPositions()[0], Vector3.up), Is.LessThan(1e-5f));
        }

        [Test]
        public void ResetAndDispose_ReleaseOnlyOwnedMeshesAndPermitNextActivation()
        {
            using var fixture = new PosedTriangle("Release", Vector3.right);
            using var first = new SkinnedPoseSnapshot("First release test");
            using var second = new SkinnedPoseSnapshot("Second release test");
            Assert.That(first.TryCapture(fixture.Renderer, 3), Is.True);
            Assert.That(second.TryCapture(fixture.Renderer, 3), Is.True);
            Mesh firstMesh = first.Mesh;
            Mesh secondMesh = second.Mesh;
            first.Reset();
            first.Reset();
            Assert.That(firstMesh == null, Is.True, "Owned native mesh was retained after Reset.");
            Assert.That(first.Mesh, Is.Null);
            Assert.That(first.LocalVertices, Is.Empty);
            Assert.That(secondMesh != null, Is.True);
            Assert.That(fixture.Source != null, Is.True);
            Assert.That(fixture.Renderer.sharedMesh, Is.SameAs(fixture.Source));
            Assert.That(first.TryCapture(fixture.Renderer, 3), Is.True);
            Mesh nextMesh = first.Mesh;
            Assert.That(nextMesh, Is.Not.SameAs(firstMesh));
            first.Dispose();
            Assert.That(nextMesh == null, Is.True);
            Assert.That(second.Mesh, Is.SameAs(secondMesh));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ToolDeactivation_DestroysItsPoseAndReactivationCapturesAgain(bool useVertexSelection)
        {
            using var fixture = new PosedTriangle("Deactivate", Vector3.right);
            var brush = new BrushToolHandler();
            var vertex = new VertexSelectionHandler();
            object handler = useVertexSelection ? (object)vertex : brush;
            try
            {
                if (useVertexSelection) vertex.RebuildCacheIfNeeded(fixture.Source, fixture.Deformer);
                else brush.RebuildCacheIfNeeded(fixture.Source, fixture.Deformer);
                var snapshot = Field<SkinnedPoseSnapshot>(handler, "_poseSnapshot");
                Mesh owned = snapshot.Mesh;
                Assert.That(owned, Is.Not.Null);
                if (useVertexSelection) vertex.Deactivate(); else brush.Deactivate();
                Assert.That(owned == null, Is.True);
                Assert.That(Field<Vector3[]>(handler, "_worldPositions"), Is.Null);
                if (useVertexSelection) vertex.RebuildCacheIfNeeded(fixture.Source, fixture.Deformer);
                else brush.RebuildCacheIfNeeded(fixture.Source, fixture.Deformer);
                Assert.That(snapshot.Mesh, Is.Not.Null);
                Assert.That(snapshot.Mesh, Is.Not.SameAs(owned));
            }
            finally
            {
                brush.Deactivate();
                vertex.Deactivate();
            }
        }

        [Test]
        public void LatticeDeactivation_ReleasesFallbackCaptureAndPreservesSource()
        {
            using var fixture = new PosedTriangle("Cage", Vector3.right);
            var lattice = new LatticeToolHandler();
            try
            {
                var method = typeof(LatticeToolHandler).GetMethod("TryCaptureBakedBoundsInSourceLocal", BindingFlags.Instance | BindingFlags.NonPublic);
                var args = new object[] {fixture.Renderer, fixture.Source, Matrix4x4.identity, null};
                Assert.That((bool)method.Invoke(lattice, args), Is.True);
                var snapshot = Field<SkinnedPoseSnapshot>(lattice, "_poseSnapshot");
                Mesh owned = snapshot.Mesh;
                var bounds = (Bounds)args[3];
                Assert.That(Vector3.Distance(bounds.center, new Vector3(1.5f, 0.5f, 0f)), Is.LessThan(1e-5f));
                lattice.Deactivate();
                Assert.That(owned == null, Is.True);
                Assert.That(fixture.Renderer.sharedMesh, Is.SameAs(fixture.Source));
            }
            finally { lattice.Deactivate(); }
        }

        private static T Field<T>(object owner, string name) =>
            (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);

        private sealed class PosedTriangle : IDisposable
        {
            internal readonly GameObject Owner;
            internal readonly Mesh Source;
            internal readonly SkinnedMeshRenderer Renderer;
            internal readonly LatticeDeformer Deformer;
            internal readonly Transform Bone;

            internal PosedTriangle(string name, Vector3 bonePosition)
            {
                Owner = new GameObject(name);
                Owner.SetActive(false);
                Source = new Mesh
                {
                    name = name + " source",
                    vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                    triangles = new[] { 0, 1, 2 },
                    boneWeights = new[]
                    {
                        new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                        new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                        new BoneWeight { boneIndex0 = 0, weight0 = 1f }
                    },
                    bindposes = new[] { Matrix4x4.identity }
                };
                Source.RecalculateNormals();
                Bone = new GameObject(name + " bone").transform;
                Bone.SetParent(Owner.transform, false);
                Bone.localPosition = bonePosition;
                Renderer = Owner.AddComponent<SkinnedMeshRenderer>();
                Renderer.sharedMesh = Source;
                Renderer.rootBone = Bone;
                Renderer.bones = new[] { Bone };
                Deformer = Owner.AddComponent<LatticeDeformer>();
                Deformer.Reset();
            }

            public void Dispose()
            {
                Object.DestroyImmediate(Owner);
                Object.DestroyImmediate(Source);
            }
        }
    }
}
#endif
