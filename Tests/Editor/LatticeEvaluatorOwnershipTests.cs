#if UNITY_EDITOR
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class LatticeEvaluatorOwnershipTests
    {
        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(4, true)]
        public void FailedNativeAllocation_ReleasesEarlierBuffersAndAllowsRetry(int failureIndex, bool bernstein)
        {
            var offset = new Vector3(0.1f, -0.2f, 0.3f);
            var settings = CreateOffsetLattice(offset, bernstein);
            string before = JsonUtility.ToJson(settings);
            var source = new[] { Vector3.zero, Vector3.one * 10f, Vector3.left * 0.25f };
            var output = (Vector3[])source.Clone();
            int allocation = 0;
            using var evaluator = new LatticeEvaluator(() =>
            {
                if (++allocation == failureIndex) throw new InvalidOperationException("Injected allocation failure");
            });

            Assert.Throws<InvalidOperationException>(() => evaluator.Apply(settings, 1f,
                new EvaluationSemantics(false), source, output));
            Assert.That(evaluator.HasNativeResources, Is.False, "Earlier successful allocations must be released.");
            Assert.That(output, Is.EqualTo(source), "A failed job setup must not write a partial contribution.");
            var entries = evaluator.Cache.Entries;
            evaluator.Apply(settings, 1f, new EvaluationSemantics(false), source, output);
            Assert.That(evaluator.HasNativeResources, Is.True);
            Assert.That(evaluator.Cache.Entries, Is.SameAs(entries));
            Assert.That(JsonUtility.ToJson(settings), Is.EqualTo(before));
            for (int i = 0; i < source.Length; i++)
                Assert.That(Vector3.Distance(output[i], source[i] + offset), Is.LessThan(1e-5f));
        }

        [Test]
        public void DisposingOneEvaluator_DoesNotReleaseAnotherOwnersBuffers()
        {
            var source = new[] { Vector3.zero, Vector3.one * 0.2f };
            var firstOutput = (Vector3[])source.Clone();
            var secondOutput = (Vector3[])source.Clone();
            var firstSettings = CreateOffsetLattice(Vector3.up, false);
            var secondSettings = CreateOffsetLattice(Vector3.right, true);
            using var first = new LatticeEvaluator();
            using var second = new LatticeEvaluator();
            first.Apply(firstSettings, 1f, new EvaluationSemantics(false), source, firstOutput);
            second.Apply(secondSettings, 1f, new EvaluationSemantics(false), source, secondOutput);
            Assert.That(first.Cache, Is.Not.SameAs(second.Cache));
            first.Dispose();
            first.Dispose();
            Assert.That(first.HasNativeResources, Is.False);
            Assert.That(second.HasNativeResources, Is.True);
            var entries = second.Cache.Entries;
            secondSettings.SetControlPointLocal(0, secondSettings.GetControlPointLocal(0) + Vector3.forward);
            var changed = (Vector3[])source.Clone();
            second.Apply(secondSettings, 1f, new EvaluationSemantics(false), source, changed);
            Assert.That(second.Cache.Entries, Is.SameAs(entries));
            Assert.That(changed, Is.Not.EqualTo(secondOutput));
            Assert.That(firstOutput[0], Is.EqualTo(Vector3.up));
        }

        [Test]
        public void LegacyWorldEvaluation_UsesExplicitCurrentMatrixWithoutMutatingAuthoredPoints()
        {
            var offset = new Vector3(0.08f, 0.04f, -0.02f);
            var settings = CreateOffsetLattice(offset, false);
            var originalOwner = Matrix4x4.TRS(new Vector3(2f, -1f, 3f), Quaternion.Euler(10f, 30f, 5f),
                new Vector3(1.1f, 0.8f, 1.3f));
            for (int i = 0; i < settings.ControlPointCount; i++)
                settings.SetControlPointLocal(i, originalOwner.MultiplyPoint3x4(settings.GetControlPointLocal(i)));
            typeof(LatticeAsset).GetField("_applySpace", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(settings, 1);
            string payload = JsonUtility.ToJson(settings);
            var source = new[] { new Vector3(0.1f, -0.2f, 0.15f), new Vector3(-0.1f, 0.2f, 0.05f) };
            using var evaluator = new LatticeEvaluator();
            foreach (var currentOwner in new[] { originalOwner, Matrix4x4.Translate(Vector3.up * 0.3f) * originalOwner })
            {
                var output = (Vector3[])source.Clone();
                evaluator.Apply(settings, 1f, new EvaluationSemantics(false, true, currentOwner.inverse), source, output);
                for (int i = 0; i < source.Length; i++)
                {
                    var expected = currentOwner.inverse.MultiplyPoint3x4(originalOwner.MultiplyPoint3x4(source[i] + offset));
                    Assert.That(Vector3.Distance(output[i], expected), Is.LessThan(1e-5f));
                }
            }
            Assert.That(JsonUtility.ToJson(settings), Is.EqualTo(payload));
        }

        [Test]
        public void ComponentDisableInvalidationAndDestroy_ReleaseItsNativeWorkspace()
        {
            var root = new GameObject("Owned lattice workspace");
            var mesh = DeformationOutputBaselineFixture.CreateMesh(3);
            try
            {
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                root.AddComponent<MeshRenderer>();
                var deformer = root.AddComponent<LatticeDeformer>();
                deformer.Reset();
                Assert.That(deformer.Deform(false), Is.Not.Null);
                var field = typeof(LatticeDeformer).GetField("_evaluationWorkspace", BindingFlags.Instance | BindingFlags.NonPublic);
                var workspace = (EvaluationWorkspace)field.GetValue(deformer);
                Assert.That(workspace.Lattice.HasNativeResources, Is.True);
                deformer.enabled = false;
                Assert.That(workspace.Lattice.HasNativeResources, Is.False);
                deformer.enabled = true;
                Assert.That(deformer.Deform(false), Is.Not.Null);
                Assert.That(workspace.Lattice.HasNativeResources, Is.True);
                deformer.InvalidateCache();
                Assert.That(workspace.Lattice.HasNativeResources, Is.False);
                Assert.That(deformer.Deform(false), Is.Not.Null);
                Assert.That(workspace.Lattice.HasNativeResources, Is.True);
                Object.DestroyImmediate(root);
                Assert.That(workspace.Lattice.HasNativeResources, Is.False);
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
            }
        }

        private static LatticeAsset CreateOffsetLattice(Vector3 offset, bool bernstein)
        {
            var settings = new LatticeAsset();
            settings.EnsureInitialized();
            settings.Interpolation = bernstein ? LatticeInterpolationMode.CubicBernstein : LatticeInterpolationMode.Trilinear;
            for (int i = 0; i < settings.ControlPointCount; i++)
                settings.SetControlPointLocal(i, settings.GetControlPointLocal(i) + offset);
            return settings;
        }
    }
}
#endif
