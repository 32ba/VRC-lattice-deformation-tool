#if UNITY_EDITOR
using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class MeshOutputOwnershipTests
    {
        private static readonly MeshOutputOptions PreserveChannels = new MeshOutputOptions(
            false, false, false, NormalsRecalculationMode.LegacyUnityRecalculate, false);

        [TestCase(BlendShapeCompositionMode.Single)]
        [TestCase(BlendShapeCompositionMode.Progressive)]
        [TestCase(BlendShapeCompositionMode.Crossfade)]
        public void GeneratedFrames_OwnTheirDataAcrossBufferReuse(BlendShapeCompositionMode composition)
        {
            var mesh = DeformationOutputBaselineFixture.CreateMesh(2);
            try
            {
                mesh.ClearBlendShapes();
                var first = new[] { Vector3.up, Vector3.right, Vector3.forward, Vector3.one };
                var second = new[] { Vector3.left, Vector3.back, Vector3.down, Vector3.zero };
                var originalFirst = (Vector3[])first.Clone();
                var originalSecond = (Vector3[])second.Clone();
                var workspace = new MeshOutputWorkspace();
                var generated = new GeneratedBlendShapeOutput("First", null, composition, new[] { first, second });
                DeformedMeshWriter.AddGeneratedBlendShapeFrames(mesh, "First", mesh.vertices,
                    generated, PreserveChannels, workspace);
                var before = DeformationOutputBaselineFixture.CaptureMesh(mesh);
                var buffer = workspace.FrameVertices;
                Assert.That(mesh.GetBlendShapeFrameCount(0), Is.EqualTo(100));
                Assert.That(before.frames[0].vertices, Is.Not.EqualTo(before.frames[99].vertices));

                DeformedMeshWriter.AddGeneratedBlendShapeFrames(mesh, "Zero", mesh.vertices,
                    new GeneratedBlendShapeOutput("Zero", null, new Vector3[mesh.vertexCount]),
                    PreserveChannels, workspace);
                Assert.That(workspace.FrameVertices, Is.SameAs(buffer), "Reuse the exact-sized write buffer.");
                workspace.ClearFrameBuffers();
                Array.Fill(buffer, Vector3.one * 900f);
                var after = DeformationOutputBaselineFixture.CaptureMesh(mesh);
                for (int i = 0; i < 100; i++)
                {
                    Assert.That(JsonUtility.ToJson(after.frames[i]), Is.EqualTo(JsonUtility.ToJson(before.frames[i])));
                    Assert.That(after.frames[100 + i].vertices, Is.EqualTo(new Vector3[mesh.vertexCount]));
                    Assert.That(after.frames[100 + i].normals, Is.EqualTo(new Vector3[mesh.vertexCount]));
                    Assert.That(after.frames[100 + i].tangents, Is.EqualTo(new Vector3[mesh.vertexCount]));
                }
                Assert.That(first, Is.EqualTo(originalFirst));
                Assert.That(second, Is.EqualTo(originalSecond));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void CopyFrames_ClearsMissingChannelsAndSurvivesDifferentMeshSizes()
        {
            var workspace = new MeshOutputWorkspace();
            foreach (int side in new[] { 3, 2, 3 })
            {
                var source = DeformationOutputBaselineFixture.CreateMesh(side);
                Mesh output = null;
                try
                {
                    source.AddBlendShapeFrame("Empty channels", 100f, new Vector3[source.vertexCount], null, null);
                    var before = DeformationOutputBaselineFixture.CaptureMesh(source);
                    output = Object.Instantiate(source);
                    output.ClearBlendShapes();
                    DeformedMeshWriter.CopyBlendShapes(source, output, workspace);
                    workspace.ClearFrameBuffers();
                    DeformationOutputCompatibilityTests.CompareMesh(before,
                        DeformationOutputBaselineFixture.CaptureMesh(output), "copied frame channels");
                    Assert.That(JsonUtility.ToJson(DeformationOutputBaselineFixture.CaptureMesh(source)),
                        Is.EqualTo(JsonUtility.ToJson(before)));
                }
                finally
                {
                    Object.DestroyImmediate(output);
                    Object.DestroyImmediate(source);
                }
            }
        }

        [Test]
        public void BakedZeroFrame_DoesNotInheritPreviousWriteBuffers()
        {
            var source = DeformationOutputBaselineFixture.CreateMesh(2);
            Mesh output = null;
            try
            {
                output = Object.Instantiate(source);
                output.ClearBlendShapes();
                var workspace = new MeshOutputWorkspace();
                workspace.EnsureFrameCapacity(source.vertexCount, true, true);
                Array.Fill(workspace.FrameVertices, Vector3.one);
                Array.Fill(workspace.FrameNormals, Vector3.one);
                Array.Fill(workspace.FrameTangents, Vector3.one);
                var baked = new[] { Vector3.up, Vector3.right, Vector3.forward, Vector3.one };
                DeformedMeshWriter.CopyBlendShapes(source, output, workspace, new[] { baked }, new[] { 10f });
                var dv = new Vector3[source.vertexCount];
                var dn = new Vector3[source.vertexCount];
                var dt = new Vector3[source.vertexCount];
                output.GetBlendShapeFrameVertices(0, 0, dv, dn, dt);
                Assert.That(output.GetBlendShapeFrameWeight(0, 0), Is.EqualTo(10f));
                Assert.That(dv, Is.EqualTo(new Vector3[source.vertexCount]));
                Assert.That(dn, Is.EqualTo(new Vector3[source.vertexCount]));
                Assert.That(dt, Is.EqualTo(new Vector3[source.vertexCount]));
                source.GetBlendShapeFrameVertices(0, 0, dv, dn, dt);
                var actual = new Vector3[source.vertexCount];
                output.GetBlendShapeFrameVertices(0, 1, actual, null, null);
                for (int i = 0; i < actual.Length; i++) Assert.That(actual[i], Is.EqualTo(dv[i] - baked[i]));
            }
            finally
            {
                Object.DestroyImmediate(output);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void SelfCopy_IsRejectedWithoutChangingSource()
        {
            var source = DeformationOutputBaselineFixture.CreateMesh(2);
            try
            {
                string before = JsonUtility.ToJson(DeformationOutputBaselineFixture.CaptureMesh(source));
                Assert.Throws<ArgumentException>(() =>
                    DeformedMeshWriter.CopyBlendShapes(source, source, new MeshOutputWorkspace()));
                Assert.That(JsonUtility.ToJson(DeformationOutputBaselineFixture.CaptureMesh(source)), Is.EqualTo(before));
            }
            finally { Object.DestroyImmediate(source); }
        }

        [Test]
        public void UpstreamClone_RemainsIndependentAfterWorkspaceReuseAndDisposal()
        {
            var source = DeformationOutputBaselineFixture.CreateMesh(3);
            Mesh first = null, second = null;
            using var workspace = new EvaluationWorkspace();
            try
            {
                var input = new DeformationEvaluationInput(Array.Empty<DeformerGroup>(), null, new EvaluationSemantics(false));
                var before = DeformationOutputBaselineFixture.CaptureMesh(source);
                first = DeformationPipeline.CreatePreviewMeshFromInput(source, input, PreserveChannels, workspace);
                var retained = DeformationOutputBaselineFixture.CaptureMesh(first);
                second = DeformationPipeline.CreatePreviewMeshFromInput(source, input, PreserveChannels, workspace);
                Array.Fill(workspace.FinalVertices, Vector3.one * 900f);
                workspace.Dispose();
                DeformationOutputCompatibilityTests.CompareMesh(before, retained, "identity upstream evaluation");
                Assert.That(JsonUtility.ToJson(DeformationOutputBaselineFixture.CaptureMesh(first)),
                    Is.EqualTo(JsonUtility.ToJson(retained)));
                Assert.That(JsonUtility.ToJson(DeformationOutputBaselineFixture.CaptureMesh(source)),
                    Is.EqualTo(JsonUtility.ToJson(before)));
                Assert.That(first, Is.Not.SameAs(second));
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(source);
            }
        }
    }
}
#endif
