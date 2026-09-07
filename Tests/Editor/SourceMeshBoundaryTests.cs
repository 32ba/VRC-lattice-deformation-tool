#if UNITY_EDITOR
using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class SourceMeshBoundaryTests
    {
        [Test]
        public void RuntimeAssembly_DoesNotReferenceEditorOrNdmfAssemblies()
        {
            var references = typeof(LatticeDeformer).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
            Assert.That(references.Where(name => name.StartsWith("UnityEditor", StringComparison.Ordinal) ||
                name.StartsWith("nadena.dev.ndmf", StringComparison.Ordinal)), Is.Empty);
            Assert.That(DeformerPlatformServices.EditorMeshDataReader, Is.Not.Null);
            Assert.That(DeformerPlatformServices.RecordLegacyMigration, Is.Not.Null);
        }

        [Test]
        public void ReadableLease_BorrowsWithoutDestroyingSource()
        {
            var source = DeformationOutputBaselineFixture.CreateMesh(2);
            try
            {
                var lease = SourceMeshAccess.Acquire(source);
                Assert.That(lease.Mesh, Is.SameAs(source));
                Assert.That(lease.OwnsMesh, Is.False);
                lease.Dispose();
                lease.Dispose();
                Assert.That(source != null, Is.True);
            }
            finally { Object.DestroyImmediate(source); }
        }

        [Test]
        public void UnreadableLease_CopiesEveryChannelAndDisposesOnlyItsCopy()
        {
            var source = DeformationOutputBaselineFixture.CreateMesh(3);
            try
            {
                var before = DeformationOutputBaselineFixture.CaptureMesh(source);
                int topology = SourceMeshTopology.Calculate(source);
                source.UploadMeshData(true);
                Assert.That(source.isReadable, Is.False);
                using var lease = SourceMeshAccess.Acquire(source);
                Assert.That(lease.OwnsMesh, Is.True, "Editor adapter must be registered before evaluation.");
                Assert.That(lease.Mesh, Is.Not.SameAs(source));
                Assert.That(lease.Mesh.isReadable, Is.True);
                DeformationOutputCompatibilityTests.CompareMesh(before,
                    DeformationOutputBaselineFixture.CaptureMesh(lease.Mesh), "readable copy");
                Assert.That(SourceMeshTopology.Calculate(source), Is.EqualTo(topology));
                Assert.That(SourceMeshTopology.Calculate(lease.Mesh), Is.EqualTo(topology));
                var owned = lease.Mesh;
                lease.Dispose();
                lease.Dispose();
                Assert.That(owned == null, Is.True);
                Assert.That(source != null, Is.True);
                Assert.That(source.isReadable, Is.False);
                using var repeated = SourceMeshAccess.Acquire(source);
                DeformationOutputCompatibilityTests.CompareMesh(before,
                    DeformationOutputBaselineFixture.CaptureMesh(repeated.Mesh), "unchanged unreadable source");
            }
            finally { Object.DestroyImmediate(source); }
        }

        [Test]
        public void ReadFailure_DoesNotLeaveAnAllocatedMesh()
        {
            var source = DeformationOutputBaselineFixture.CreateMesh(2);
            try
            {
                string before = JsonUtility.ToJson(DeformationOutputBaselineFixture.CaptureMesh(source));
                var meshes = Resources.FindObjectsOfTypeAll<Mesh>().Select(m => m.GetInstanceID()).OrderBy(id => id).ToArray();
                Assert.Throws<InvalidOperationException>(() => SourceMeshAccess.CreateReadableCopy(source,
                    _ => throw new InvalidOperationException("Injected MeshData acquisition failure")));
                Assert.That(Resources.FindObjectsOfTypeAll<Mesh>().Select(m => m.GetInstanceID()).OrderBy(id => id).ToArray(),
                    Is.EqualTo(meshes));
                Assert.That(JsonUtility.ToJson(DeformationOutputBaselineFixture.CaptureMesh(source)), Is.EqualTo(before));
            }
            finally { Object.DestroyImmediate(source); }
        }

        [Test]
        public void WeightCaptureAndBakedPayload_AreIndependentOfLaterRendererChanges()
        {
            var root = new GameObject("Source weight boundary");
            var source = DeformationOutputBaselineFixture.CreateMesh(2);
            try
            {
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                renderer.SetBlendShapeWeight(0, 60f);
                var workspace = new SourceVertexWorkspace();
                var captured = workspace.CaptureWeights(renderer, source.blendShapeCount);
                renderer.SetBlendShapeWeight(0, 100f);
                Assert.That(captured[0], Is.EqualTo(60f));
                var first = SourceVertexResolver.Resolve(source, captured, workspace, out var deltas, out var weights, out int hash);
                var firstVertices = (Vector3[])first.Clone();
                var retainedDeltas = (Vector3[])deltas[0].Clone();
                var nextCapture = workspace.CaptureWeights(renderer, source.blendShapeCount);
                var second = SourceVertexResolver.Resolve(source, nextCapture, workspace,
                    out _, out var nextWeights, out int nextHash);
                Assert.That(first, Is.SameAs(second), "Only the synchronous vertex result is borrowed.");
                Assert.That(second, Is.Not.EqualTo(firstVertices));
                Assert.That(deltas[0], Is.EqualTo(retainedDeltas));
                Assert.That(weights[0], Is.EqualTo(60f));
                Assert.That(nextWeights[0], Is.EqualTo(100f));
                Assert.That(nextHash, Is.Not.EqualTo(hash));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void InvalidWeightCount_IsRejectedBeforeWritingBorrowedVertices()
        {
            var source = DeformationOutputBaselineFixture.CreateMesh(2);
            try
            {
                var workspace = new SourceVertexWorkspace();
                var vertices = SourceVertexResolver.Resolve(source, null, workspace, out _, out _, out _);
                Array.Fill(vertices, Vector3.one * 5f);
                var before = (Vector3[])vertices.Clone();
                Assert.Throws<ArgumentException>(() => SourceVertexResolver.Resolve(source,
                    new float[source.blendShapeCount + 1], workspace, out _, out _, out _));
                Assert.That(vertices, Is.EqualTo(before));
            }
            finally { Object.DestroyImmediate(source); }
        }

        [TestCase(-25f, -1f, -1f)]
        [TestCase(100f, 3f, 3f)]
        [TestCase(150f, 5f, 3f)]
        public void LastFramePolicy_PreservesEvaluationAndBoundsContracts(float weight, float evaluation, float bounds)
        {
            var source = new Mesh { vertices = new[] { Vector3.zero } };
            try
            {
                source.AddBlendShapeFrame("Frame policy", 25f, new[] { Vector3.right }, null, null);
                source.AddBlendShapeFrame("Frame policy", 100f, new[] { Vector3.right * 3f }, null, null);
                var normal = SourceBlendShapeEvaluator.EvaluateDelta(source, 0, weight);
                var clamped = SourceBlendShapeEvaluator.EvaluateDelta(source, 0, weight,
                    SourceBlendShapeExtrapolation.ClampLastFrame);
                Assert.That(normal[0].x, Is.EqualTo(evaluation).Within(1e-5f));
                Assert.That(clamped[0].x, Is.EqualTo(bounds).Within(1e-5f));
            }
            finally { Object.DestroyImmediate(source); }
        }
    }
}
#endif
