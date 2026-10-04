#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using nadena.dev.ndmf.preview;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class PreviewOwnershipTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void SourceSurfaceOnlyWeightChange_PublishesUpdatedNormalsOrTangents(bool tangentChannel)
        {
            using var fixture = new Fixture(true);
            var zero = new Vector3[4];
            var delta = new[] {Vector3.up*.2f, Vector3.up*.2f, Vector3.up*.2f, Vector3.up*.2f};
            foreach (var mesh in new[] {fixture.Source, fixture.Upstream})
            {
                mesh.tangents = new[] {Vector4.one, Vector4.one, Vector4.one, Vector4.one};
                mesh.AddBlendShapeFrame("Surface", 100f, zero, tangentChannel ? zero : delta,
                    tangentChannel ? delta : zero);
            }
            fixture.Deformer.Reset();
            var node = fixture.CreateNode(fixture.Proxy);
            var output = PreviewRendererMesh.Get(fixture.Proxy);
            var vertices = output.vertices; var normals = output.normals; var tangents = output.tangents;
            int revision = LatticePreviewUtility.GetInteractiveRevision(fixture.Deformer).Value;
            ((SkinnedMeshRenderer)fixture.Original).SetBlendShapeWeight(0, 50f);
            node.OnFrameGroup();
            Assert.That(output.vertices, Is.EqualTo(vertices));
            if (tangentChannel) Assert.That(output.tangents, Is.Not.EqualTo(tangents));
            else Assert.That(output.normals, Is.Not.EqualTo(normals));
            Assert.That(LatticePreviewUtility.GetInteractiveRevision(fixture.Deformer).Value, Is.Not.EqualTo(revision));
            ((SkinnedMeshRenderer)fixture.Original).SetBlendShapeWeight(0, 0f);
            node.OnFrameGroup();
            Assert.That(output.normals, Is.EqualTo(normals));
            Assert.That(output.tangents, Is.EqualTo(tangents));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RepeatedOnFrame_RestoresLatestBorrowedUpstream(bool skinned)
        {
            using var fixture = new Fixture(skinned);
            var node = fixture.CreateNode(fixture.Proxy);
            var latest = fixture.CloneMesh(fixture.Upstream);
            PreviewRendererMesh.Assign(fixture.Proxy, latest);
            node.OnFrame(fixture.Original, fixture.Proxy);
            var owned = PreviewRendererMesh.Get(fixture.Proxy);
            Assert.That(owned, Is.Not.SameAs(latest));
            node.OnFrame(fixture.Original, fixture.Proxy); // observing our own output must not replace the borrow
            Object.DestroyImmediate(fixture.Upstream);
            node.Dispose();
            Assert.That(PreviewRendererMesh.Get(fixture.Proxy), Is.SameAs(latest));
            Assert.That(owned == null, Is.True);
            Assert.That(latest != null, Is.True);
        }

        [Test]
        public void SourceWeightChange_RefreshesNonlinearPreviewWithoutComponentEdit()
        {
            using var fixture = new Fixture(true);
            var delta = new[] {new Vector3(0.2f, 0.35f, 0f), Vector3.zero, Vector3.zero, Vector3.zero};
            var zero = new Vector3[4];
            fixture.Source.AddBlendShapeFrame("Source", 100f, delta, zero, zero);
            fixture.Upstream.AddBlendShapeFrame("Source", 100f, delta, zero, zero);
            fixture.Deformer.Reset();
            var lattice = fixture.Deformer.EditingSettings;
            for (int i = 0; i < lattice.ControlPointCount; i++)
            {
                var point = lattice.GetControlPointLocal(i);
                lattice.SetControlPointLocal(i, point + Vector3.forward * point.x * point.y);
            }
            var node = fixture.CreateNode(fixture.Proxy);
            var before = PreviewRendererMesh.Get(fixture.Proxy).vertices;
            int revision = LatticePreviewUtility.GetInteractiveRevision(fixture.Deformer).Value;
            ((SkinnedMeshRenderer)fixture.Original).SetBlendShapeWeight(0, 50f);
            node.OnFrameGroup();
            Assert.That(PreviewRendererMesh.Get(fixture.Proxy).vertices, Is.Not.EqualTo(before));
            Assert.That(LatticePreviewUtility.GetInteractiveRevision(fixture.Deformer).Value, Is.Not.EqualTo(revision));
            ((SkinnedMeshRenderer)fixture.Original).SetBlendShapeWeight(0, 0f);
            node.OnFrameGroup();
            Assert.That(PreviewRendererMesh.Get(fixture.Proxy).vertices, Is.EqualTo(before));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SharedProfileChanges_UpdateLiveNodesAndPublishedRevision(bool saveWithUndo)
        {
            using var writer = new Fixture(false);
            using var first = new Fixture(false);
            using var second = new Fixture(false);
            var profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            try
            {
                profile.Capture(writer.Deformer.Groups, 0, writer.Source);
                Assert.That(first.Deformer.UseProfile(profile), Is.True);
                Assert.That(second.Deformer.UseProfile(profile), Is.True);
                var firstNode = first.CreateNode(first.Proxy);
                var secondNode = second.CreateNode(second.Proxy);
                var firstBefore = PreviewRendererMesh.Get(first.Proxy).vertices;
                var secondBefore = PreviewRendererMesh.Get(second.Proxy).vertices;
                int firstRevision = LatticePreviewUtility.GetInteractiveRevision(first.Deformer).Value;
                int secondRevision = LatticePreviewUtility.GetInteractiveRevision(second.Deformer).Value;
                if (saveWithUndo)
                {
                    writer.Deformer.EditingSettings.SetControlPointLocal(0, Vector3.one * 0.4f);
                    Assert.That(ProfileAuthoringService.SaveCurrent(writer.Deformer, profile, "Update shared profile"), Is.True);
                }
                else profile.Groups[0].Layers[0].Settings.SetControlPointLocal(0, Vector3.one * 0.4f);
                string profileAfter = EditorJsonUtility.ToJson(profile);
                int dirty = EditorUtility.GetDirtyCount(profile);
                firstNode.OnFrameGroup(); secondNode.OnFrameGroup();
                Assert.That(PreviewRendererMesh.Get(first.Proxy).vertices, Is.Not.EqualTo(firstBefore));
                Assert.That(PreviewRendererMesh.Get(second.Proxy).vertices, Is.Not.EqualTo(secondBefore));
                Assert.That(LatticePreviewUtility.GetInteractiveRevision(first.Deformer).Value, Is.Not.EqualTo(firstRevision));
                Assert.That(LatticePreviewUtility.GetInteractiveRevision(second.Deformer).Value, Is.Not.EqualTo(secondRevision));
                Assert.That(EditorJsonUtility.ToJson(profile), Is.EqualTo(profileAfter));
                Assert.That(EditorUtility.GetDirtyCount(profile), Is.EqualTo(dirty));
                if (saveWithUndo)
                {
                    Undo.PerformUndo(); firstNode.OnFrameGroup(); secondNode.OnFrameGroup();
                    Assert.That(PreviewRendererMesh.Get(first.Proxy).vertices, Is.EqualTo(firstBefore));
                    Assert.That(PreviewRendererMesh.Get(second.Proxy).vertices, Is.EqualTo(secondBefore));
                    Undo.PerformRedo(); firstNode.OnFrameGroup(); secondNode.OnFrameGroup();
                    Assert.That(PreviewRendererMesh.Get(first.Proxy).vertices, Is.Not.EqualTo(firstBefore));
                    Assert.That(PreviewRendererMesh.Get(second.Proxy).vertices, Is.Not.EqualTo(secondBefore));
                }
            }
            finally { Undo.ClearAll(); Object.DestroyImmediate(profile); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InspectorCallback_PublishesEveryChangedOwnerAfterEvaluation(bool alreadyInvalidated)
        {
            using var first = new Fixture(false);
            using var second = new Fixture(false);
            var fixtures = new[] {first, second};
            var revisions = new int[2];
            var vertices = new Vector3[2][];
            for (int i = 0; i < fixtures.Length; i++)
            {
                fixtures[i].CreateNode(fixtures[i].Proxy);
                vertices[i] = PreviewRendererMesh.Get(fixtures[i].Proxy).vertices;
                revisions[i] = LatticePreviewUtility.GetInteractiveRevision(fixtures[i].Deformer).Value;
            }
            var editor = UnityEditor.Editor.CreateEditor(new[] {first.Deformer, second.Deformer}, typeof(LatticeDeformerEditor));
            var published = new List<LatticeDeformer>();
            void OnPublished(LatticeDeformer owner)
            {
                if (owner != first.Deformer && owner != second.Deformer) return;
                Assert.That(owner.RuntimeMesh, Is.Not.Null, "Evaluate before publishing.");
                published.Add(owner);
            }
            LatticePreviewUtility.InteractiveDeformationPublished += OnPublished;
            try
            {
                foreach (var fixture in fixtures)
                {
                    fixture.Deformer.EditingSettings.SetControlPointLocal(0, Vector3.one * 0.4f);
                    if (alreadyInvalidated) fixture.Deformer.NotifyDeformationDataChanged();
                }
                typeof(LatticeDeformerEditor).GetMethod("NotifyPropertyChanges",
                    BindingFlags.Instance | BindingFlags.NonPublic, null, new[] {typeof(bool)}, null)
                    .Invoke(editor, new object[] {alreadyInvalidated});

                Assert.That(published, Is.EquivalentTo(new[] {first.Deformer, second.Deformer}));
                for (int i = 0; i < fixtures.Length; i++)
                {
                    var owner = fixtures[i].Deformer;
                    Assert.That(LatticePreviewUtility.GetInteractiveRevision(owner).Value,
                        Is.EqualTo(owner.DeformationDataRevision).And.Not.EqualTo(revisions[i]));
                    Assert.That(PreviewRendererMesh.Get(fixtures[i].Proxy).vertices, Is.Not.EqualTo(vertices[i]));
                }
            }
            finally
            {
                LatticePreviewUtility.InteractiveDeformationPublished -= OnPublished;
                Object.DestroyImmediate(editor);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InteractivePublish_PreservesDownstreamAssignment(bool skinned)
        {
            using var fixture = new Fixture(skinned);
            var node = fixture.CreateNode(fixture.Proxy);
            Mesh output = PreviewRendererMesh.Get(fixture.Proxy);
            var before = output.vertices;
            Mesh downstream = fixture.CloneMesh(fixture.Upstream);
            PreviewRendererMesh.Assign(fixture.Proxy, downstream);
            fixture.Deformer.EditingSettings.SetControlPointLocal(0, Vector3.one * 0.5f);
            fixture.Deformer.NotifyDeformationDataChanged();
            LatticePreviewUtility.PublishInteractiveDeformation(fixture.Deformer);
            Assert.That(output.vertices, Is.Not.EqualTo(before));
            Assert.That(PreviewRendererMesh.Get(fixture.Proxy), Is.SameAs(downstream));
            node.Dispose();
            Assert.That(PreviewRendererMesh.Get(fixture.Proxy), Is.SameAs(downstream));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Dispose_RestoresBorrowedUpstreamAndReleasesOnlyOwnedMesh(bool skinned)
        {
            using var fixture = new Fixture(skinned);
            Vector3[] sourceVertices = fixture.Source.vertices;
            Vector3[] upstreamVertices = fixture.Upstream.vertices;
            var node = fixture.CreateNode(fixture.Proxy);
            Mesh output = LatticeDeformerPreviewFilter.GetRendererMesh(fixture.Proxy);
            Assert.That(output, Is.Not.SameAs(fixture.Upstream));

            node.Dispose();

            Assert.That(LatticeDeformerPreviewFilter.GetRendererMesh(fixture.Proxy),
                Is.SameAs(fixture.Upstream), "Disposal must restore the mesh received from the upstream stage.");
            Assert.That(output == null, Is.True, "The generated mesh must be released.");
            Assert.That(fixture.Source.vertices, Is.EqualTo(sourceVertices));
            Assert.That(fixture.Upstream.vertices, Is.EqualTo(upstreamVertices));
            Assert.That(LatticeDeformerPreviewFilter.GetRendererMesh(fixture.Original), Is.SameAs(fixture.Source));

            node.Dispose();
            node.OnFrameGroup();
            node.OnFrame(fixture.Original, fixture.Proxy);
            Assert.That(LatticeDeformerPreviewFilter.GetRendererMesh(fixture.Proxy), Is.SameAs(fixture.Upstream));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OldGenerationDisposal_DoesNotReplaceNewOutputOrItsUpstream(bool skinned)
        {
            using var fixture = new Fixture(skinned);
            var first = fixture.CreateNode(fixture.Proxy);
            Mesh firstOutput = LatticeDeformerPreviewFilter.GetRendererMesh(fixture.Proxy);
            var nextUpstream = fixture.CloneMesh(fixture.Upstream);
            LatticeDeformerPreviewFilter.AssignRendererMesh(fixture.Proxy, nextUpstream);
            var next = fixture.CreateNode(fixture.Proxy);
            Mesh nextOutput = LatticeDeformerPreviewFilter.GetRendererMesh(fixture.Proxy);

            first.Dispose();
            Assert.That(firstOutput == null, Is.True);
            Assert.That(LatticeDeformerPreviewFilter.GetRendererMesh(fixture.Proxy), Is.SameAs(nextOutput));
            next.Dispose();
            Assert.That(LatticeDeformerPreviewFilter.GetRendererMesh(fixture.Proxy), Is.SameAs(nextUpstream));
            Assert.That(fixture.Upstream != null, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Dispose_PreservesAnAssignmentMadeByAnotherStage(bool skinned)
        {
            using var fixture = new Fixture(skinned);
            var node = fixture.CreateNode(fixture.Proxy);
            Mesh output = LatticeDeformerPreviewFilter.GetRendererMesh(fixture.Proxy);
            Mesh downstream = fixture.CloneMesh(fixture.Upstream);
            LatticeDeformerPreviewFilter.AssignRendererMesh(fixture.Proxy, downstream);
            node.Dispose();
            Assert.That(LatticeDeformerPreviewFilter.GetRendererMesh(fixture.Proxy), Is.SameAs(downstream));
            Assert.That(output == null, Is.True);
            Assert.That(downstream != null, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LateProxyRegistration_RestoresEachBorrowedMeshIndependently(bool skinned)
        {
            using var fixture = new Fixture(skinned);
            var node = fixture.CreateNode(fixture.Proxy);
            Mesh otherUpstream = fixture.CloneMesh(fixture.Upstream);
            var later = fixture.CreateRenderer(skinned, "late proxy", otherUpstream);
            node.OnFrame(fixture.Original, later);
            Assert.That(LatticeDeformerPreviewFilter.GetRendererMesh(later),
                Is.SameAs(LatticeDeformerPreviewFilter.GetRendererMesh(fixture.Proxy)));
            node.Dispose();
            Assert.That(LatticeDeformerPreviewFilter.GetRendererMesh(fixture.Proxy), Is.SameAs(fixture.Upstream));
            Assert.That(LatticeDeformerPreviewFilter.GetRendererMesh(later), Is.SameAs(otherUpstream));
        }

        [Test]
        public void DestroyedObjects_AndRepeatedDisposal_DoNotRetainOwnedMesh()
        {
            using var fixture = new Fixture(false);
            var node = fixture.CreateNode(fixture.Proxy);
            Mesh output = LatticeDeformerPreviewFilter.GetRendererMesh(fixture.Proxy);
            Object.DestroyImmediate(fixture.Original.gameObject);
            Object.DestroyImmediate(fixture.Proxy.gameObject);
            Assert.DoesNotThrow(() => { node.Dispose(); node.Dispose(); node.OnFrameGroup(); });
            Assert.That(output == null, Is.True);
            Assert.That(fixture.Source != null && fixture.Upstream != null, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UpstreamDisposedFirst_DoesNotLeaveAReferenceToADestroyedMesh(bool skinned)
        {
            using var fixture = new Fixture(skinned);
            var node = fixture.CreateNode(fixture.Proxy);
            Mesh output = LatticeDeformerPreviewFilter.GetRendererMesh(fixture.Proxy);
            Object.DestroyImmediate(fixture.Upstream);
            Assert.DoesNotThrow(() => node.Dispose());
            Assert.That(LatticeDeformerPreviewFilter.GetRendererMesh(fixture.Proxy), Is.Null);
            Assert.That(output == null, Is.True);
            Assert.That(fixture.Source != null, Is.True);
        }

        [Test]
        public void RejectedEvaluation_KeepsLastValidOutput_AndCanRetry()
        {
            using var fixture = new Fixture(false);
            var node = fixture.CreateNode(fixture.Proxy);
            Mesh output = LatticeDeformerPreviewFilter.GetRendererMesh(fixture.Proxy);
            Vector3[] before = output.vertices;
            using var serialized = new SerializedObject(fixture.Deformer);
            var version = serialized.FindProperty("_migrationReleaseIndex");
            int previousVersion = version.intValue;
            version.intValue = int.MaxValue;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            fixture.Deformer.NotifyDeformationDataChanged();
            node.OnFrameGroup();
            Assert.That(output.vertices, Is.EqualTo(before));
            Assert.That(LatticeDeformerPreviewFilter.GetRendererMesh(fixture.Proxy), Is.SameAs(output));
            serialized.Update();
            Assert.That(version.intValue, Is.EqualTo(int.MaxValue));

            version.intValue = previousVersion;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            fixture.Deformer.EditingSettings.SetControlPointLocal(0, Vector3.one * 0.5f);
            fixture.Deformer.NotifyDeformationDataChanged();
            node.OnFrameGroup();
            Assert.That(output.vertices, Is.Not.EqualTo(before));
            Assert.That(LatticeDeformerPreviewFilter.GetRendererMesh(fixture.Proxy), Is.SameAs(output));
        }

        [Test]
        public void RepeatedDisposal_DoesNotUnsubscribeAnotherLiveGenerationFromUndo()
        {
            using var fixture = new Fixture(false);
            int index = fixture.Deformer.AddLayer("Undo brush", MeshDeformerLayerType.Brush);
            fixture.Deformer.ActiveLayerIndex = index;
            fixture.Deformer.SetDisplacement(0, Vector3.up * 0.2f);
            fixture.Deformer.NotifyDeformationDataChanged();
            fixture.Deformer.Deform(false);
            var first = fixture.CreateNode(fixture.Proxy);
            var secondProxy = fixture.CreateRenderer(false, "second generation", fixture.Upstream);
            fixture.CreateNode(secondProxy);
            var output = LatticeDeformerPreviewFilter.GetRendererMesh(secondProxy);
            Vector3[] before = output.vertices;
            first.Dispose();
            first.Dispose();

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            try
            {
                Undo.RegisterCompleteObjectUndo(fixture.Deformer, "Preview ownership undo");
                fixture.Deformer.SetDisplacement(0, Vector3.up * 0.7f);
                fixture.Deformer.NotifyDeformationDataChanged();
                fixture.Deformer.Deform(false);
                LatticePreviewUtility.PublishInteractiveDeformation(fixture.Deformer);
                Assert.That(output.vertices, Is.Not.EqualTo(before));

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.That(output.vertices, Is.EqualTo(before),
                    "Disposing another node twice must not unregister this generation's Undo update.");
                Assert.That(LatticeDeformerPreviewFilter.GetRendererMesh(secondProxy), Is.SameAs(output));
            }
            finally
            {
                Undo.RevertAllDownToGroup(undoGroup);
            }
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly Mesh Source;
            internal readonly Mesh Upstream;
            internal readonly Renderer Original;
            internal readonly Renderer Proxy;
            internal readonly LatticeDeformer Deformer;
            private readonly List<IRenderFilterNode> _nodes = new();
            private readonly List<Object> _objects = new();

            internal Fixture(bool skinned)
            {
                Source = new Mesh
                {
                    name = "preview ownership source",
                    vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward },
                    triangles = new[] { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3 }
                };
                Source.RecalculateNormals();
                _objects.Add(Source);
                Upstream = Object.Instantiate(Source);
                Upstream.name = "borrowed upstream";
                _objects.Add(Upstream);
                var vertices = Upstream.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i] += Vector3.right * 0.1f;
                Upstream.vertices = vertices;
                Upstream.RecalculateBounds();
                Original = CreateRenderer(skinned, "ownership original", Source);
                Proxy = CreateRenderer(skinned, "ownership proxy", Upstream);
                Deformer = Original.gameObject.AddComponent<LatticeDeformer>();
                Deformer.Reset();
            }

            internal Renderer CreateRenderer(bool skinned, string name, Mesh mesh)
            {
                var owner = new GameObject(name);
                _objects.Add(owner);
                if (skinned)
                {
                    var renderer = owner.AddComponent<SkinnedMeshRenderer>();
                    renderer.sharedMesh = mesh;
                    return renderer;
                }
                owner.AddComponent<MeshFilter>().sharedMesh = mesh;
                return owner.AddComponent<MeshRenderer>();
            }

            internal Mesh CloneMesh(Mesh source)
            {
                var mesh = Object.Instantiate(source);
                _objects.Add(mesh);
                return mesh;
            }

            internal IRenderFilterNode CreateNode(Renderer proxy)
            {
                var node = new LatticeDeformerPreviewFilter().Instantiate(
                    RenderGroup.For(Original), new[] { (Original, proxy) },
                    new ComputeContext("preview ownership contract")).GetAwaiter().GetResult();
                _nodes.Add(node);
                return node;
            }

            public void Dispose()
            {
                foreach (var node in _nodes) node.Dispose();
                for (int i = _objects.Count - 1; i >= 0; i--)
                    if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
            }
        }
    }
}
#endif
