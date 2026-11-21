#if UNITY_EDITOR
using System;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class AuthoringPreflightScopeTests
    {
        [TearDown]
        public void TearDown() => AuthoringPreflightScope.Invalidate();

        [Test]
        public void OutsideScope_EveryQueryReadsCurrentData()
        {
            using var fixture = new Fixture();
            Assert.That(AuthoringPreflightScope.IsActive, Is.False);
            Assert.That(AuthoringPreflightScope.HasValidSerializedAuthoringData(fixture.Owner), Is.True);
            fixture.Corrupt();
            Assert.That(AuthoringPreflightScope.HasValidSerializedAuthoringData(fixture.Owner), Is.False);
            Assert.That(DeformerAuthoringSource.TryRead(fixture.Owner, out _, out _, out _), Is.False);
            Assert.That(LayerSettingsEdit.TryReadActive(fixture.Owner, out _, out _), Is.False);
        }

        [Test]
        public void InsideScope_PreflightIsEvaluatedOnceAndDiscardedWhenTheOutermostScopeEnds()
        {
            using var fixture = new Fixture();
            using (AuthoringPreflightScope.Begin())
            {
                Assert.That(DeformerAuthoringSource.TryRead(fixture.Owner, out _, out _, out _), Is.True);
                fixture.Corrupt();
                Assert.That(fixture.Owner.HasValidSerializedAuthoringData, Is.False);
                // The memo is the point of the scope: nothing may write inside a refresh pass.
                Assert.That(AuthoringPreflightScope.HasValidSerializedAuthoringData(fixture.Owner), Is.True);
                using (AuthoringPreflightScope.Begin())
                    Assert.That(LayerSettingsEdit.TryReadActive(fixture.Owner, out _, out _), Is.True);
                Assert.That(AuthoringPreflightScope.IsActive, Is.True, "An inner scope must not end the outer pass.");
                Assert.That(AuthoringPreflightScope.HasValidSerializedAuthoringData(fixture.Owner), Is.True);
            }

            Assert.That(AuthoringPreflightScope.IsActive, Is.False);
            Assert.That(DeformerAuthoringSource.TryRead(fixture.Owner, out _, out _, out _), Is.False);
            using (AuthoringPreflightScope.Begin())
                Assert.That(LayerSettingsEdit.TryReadActive(fixture.Owner, out _, out _), Is.False);
        }

        [Test]
        public void InsideScope_TopologyHashIsMemoizedOnlyForThePass()
        {
            using var fixture = new Fixture();
            int before = SourceMeshTopology.Calculate(fixture.Source);
            using (AuthoringPreflightScope.Begin())
            {
                Assert.That(AuthoringPreflightScope.TopologyHash(fixture.Source), Is.EqualTo(before));
                fixture.Source.triangles = new[] { 0, 2, 1 };
                Assert.That(AuthoringPreflightScope.TopologyHash(fixture.Source), Is.EqualTo(before));
            }

            int after = SourceMeshTopology.Calculate(fixture.Source);
            Assert.That(after, Is.Not.EqualTo(before));
            Assert.That(AuthoringPreflightScope.TopologyHash(fixture.Source), Is.EqualTo(after));
            Assert.That(AuthoringPreflightScope.TopologyHash(null), Is.EqualTo(0));
        }

        [Test]
        public void EditCommit_DiscardsTheMemoEvenInsideAScope()
        {
            using var fixture = new Fixture();
            using (AuthoringPreflightScope.Begin())
            {
                Assert.That(AuthoringPreflightScope.HasValidSerializedAuthoringData(fixture.Owner), Is.True);
                fixture.Corrupt();
                Assert.That(DeformerEditService.Execute(fixture.Owner, "Preflight Scope Test", _ => true), Is.True);
                Assert.That(AuthoringPreflightScope.IsActive, Is.True);
                Assert.That(AuthoringPreflightScope.HasValidSerializedAuthoringData(fixture.Owner), Is.False);
            }
        }

        [Test]
        public void DisposingADefaultOrRepeatedScope_DoesNotUnderflow()
        {
            using var fixture = new Fixture();
            default(AuthoringPreflightScope).Dispose();
            var scope = AuthoringPreflightScope.Begin();
            scope.Dispose();
            scope.Dispose();
            Assert.That(AuthoringPreflightScope.IsActive, Is.False);
            using (AuthoringPreflightScope.Begin())
            {
                Assert.That(AuthoringPreflightScope.IsActive, Is.True);
                Assert.That(AuthoringPreflightScope.HasValidSerializedAuthoringData(null), Is.False);
            }
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly GameObject Root;
            internal readonly Mesh Source;
            internal readonly MeshFilter Filter;
            internal readonly LatticeDeformer Owner;
            private readonly LatticeLayer _brush;

            internal Fixture()
            {
                Root = new GameObject("__AuthoringPreflightScopeFixture"); Root.SetActive(false);
                Source = new Mesh
                {
                    name = "__AuthoringPreflightScopeSource",
                    vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                    triangles = new[] { 0, 1, 2 }
                };
                Source.RecalculateNormals();
                Filter = Root.AddComponent<MeshFilter>(); Root.AddComponent<MeshRenderer>(); Filter.sharedMesh = Source;
                Owner = Root.AddComponent<LatticeDeformer>(); Owner.Reset();
                Owner.ActiveLayerIndex = Owner.AddLayer("Brush", MeshDeformerLayerType.Brush);
                Owner.EnsureDisplacementCapacity();
                var raw = SerializedDeformerReader.Read(Owner);
                _brush = raw.ActiveLayers[raw.ActiveLayerIndex];
                Assert.That(_brush.Type, Is.EqualTo(MeshDeformerLayerType.Brush));
                Assert.That(Owner.HasValidSerializedAuthoringData, Is.True);
            }

            // A non-finite displacement fails only the full-vertex part of the preflight.
            internal void Corrupt()
            {
                _brush.BrushDisplacements = new[] { new Vector3(float.NaN, 0f, 0f), Vector3.zero, Vector3.zero };
                Assert.That(Owner.HasValidSerializedAuthoringData, Is.False);
            }

            public void Dispose()
            {
                AuthoringPreflightScope.Invalidate();
                if (Owner != null) { Undo.ClearUndo(Owner); Owner.InvalidateCache(); Owner.RestoreOriginalMesh(); }
                if (Filter != null) Undo.ClearUndo(Filter);
                Object.DestroyImmediate(Root);
                if (Source != null) Object.DestroyImmediate(Source);
            }
        }
    }
}
#endif
