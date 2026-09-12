#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
