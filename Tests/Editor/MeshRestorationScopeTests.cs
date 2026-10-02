#if UNITY_EDITOR
using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class MeshRestorationScopeTests
    {
        [Test]
        public void Scope_AffectsOnlyItsOwner_AndStillReleasesRuntimeMesh()
        {
            using var first = new Fixture();
            using var second = new Fixture();
            Mesh runtime = first.Deformer.Deform(true);
            second.Deformer.Deform(true);
            using (first.Deformer.SuppressMeshRestoration())
            {
                first.Filter.sharedMesh = first.Export;
                first.Deformer.enabled = false;
                second.Deformer.enabled = false;
                Assert.That(first.Filter.sharedMesh, Is.SameAs(first.Export));
                Assert.That(runtime == null, Is.True);
                Assert.That(second.Filter.sharedMesh, Is.SameAs(second.Source));
                Assert.That(LatticeDeformer.SuppressRestoreOnDisable, Is.False);
            }
        }

        [Test]
        public void NestedScopes_CanCloseOutOfOrder_AndDisposeTwice()
        {
            using var fixture = new Fixture();
            var outer = fixture.Deformer.SuppressMeshRestoration();
            var inner = fixture.Deformer.SuppressMeshRestoration();
            try
            {
                outer.Dispose();
                outer.Dispose();
                fixture.Filter.sharedMesh = fixture.Export;
                fixture.Deformer.enabled = false;
                Assert.That(fixture.Filter.sharedMesh, Is.SameAs(fixture.Export));
                // Re-enable against the original binding before checking that
                // normal cleanup of an owned output resumes after both scopes end.
                fixture.Filter.sharedMesh = fixture.Source;
                fixture.Deformer.enabled = true;
                inner.Dispose();
                inner.Dispose();
                fixture.Deformer.Deform(true);
                fixture.Deformer.enabled = false;
                Assert.That(fixture.Filter.sharedMesh, Is.SameAs(fixture.Source));
            }
            finally
            {
                inner.Dispose();
                outer.Dispose();
            }
        }

        [Test]
        public void Exception_UnwindsScope_AndRestoresNormalDisableBehavior()
        {
            using var fixture = new Fixture();
            Assert.Throws<InvalidOperationException>(() =>
            {
                using (fixture.Deformer.SuppressMeshRestoration())
                    throw new InvalidOperationException("simulated build failure");
            });
            fixture.Deformer.Deform(true);
            fixture.Deformer.enabled = false;
            Assert.That(fixture.Filter.sharedMesh, Is.SameAs(fixture.Source));
        }

        [Test]
        public void DestroyOwner_LeavesExportAssigned_AndScopeCanStillClose()
        {
            using var fixture = new Fixture();
            Mesh runtime = fixture.Deformer.Deform(true);
            var scope = fixture.Deformer.SuppressMeshRestoration();
            fixture.Filter.sharedMesh = fixture.Export;
            Object.DestroyImmediate(fixture.Deformer);
            Assert.That(fixture.Filter.sharedMesh, Is.SameAs(fixture.Export));
            Assert.That(runtime == null, Is.True);
            Assert.DoesNotThrow(() => { scope.Dispose(); scope.Dispose(); });
            Assert.That(LatticeDeformer.SuppressRestoreOnDisable, Is.False);
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly GameObject Owner;
            internal readonly Mesh Source;
            internal readonly Mesh Export;
            internal readonly MeshFilter Filter;
            internal readonly LatticeDeformer Deformer;

            internal Fixture()
            {
                Assert.That(LatticeDeformer.SuppressRestoreOnDisable, Is.False);
                Owner = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Filter = Owner.GetComponent<MeshFilter>();
                Source = Filter.sharedMesh;
                Export = Object.Instantiate(Source);
                Deformer = Owner.AddComponent<LatticeDeformer>();
                Deformer.Reset();
            }

            public void Dispose()
            {
                Object.DestroyImmediate(Owner);
                Object.DestroyImmediate(Export);
            }
        }
    }
}
#endif
