#if UNITY_EDITOR
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class RuntimeMeshAssignmentTests
    {
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void ChangedTarget_CleanupRestoresTheActualAssignment(bool skinned, bool destroy)
        {
            using var f = new Fixture(skinned);
            Mesh output = f.Owner.Deform(true);
            Assert.That(f.Get(false), Is.SameAs(output));
            f.SwitchTarget();
            if (destroy) Object.DestroyImmediate(f.Owner);
            else f.Owner.enabled = false;
            Assert.That(f.Get(false), Is.SameAs(f.Source), "The previous target must not retain a destroyed output.");
            Assert.That(f.Get(true), Is.SameAs(f.Source));
            Assert.That(output == null, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ChangedTarget_ReassignmentRestoresOldTargetAndOwnsOnlyNewTarget(bool skinned)
        {
            using var f = new Fixture(skinned);
            Mesh output = f.Owner.Deform(true);
            f.SwitchTarget();
            Assert.That(f.Owner.Deform(true), Is.SameAs(output));
            Assert.That(f.Get(false), Is.SameAs(f.Source));
            Assert.That(f.Get(true), Is.SameAs(output));
            f.Set(false, f.Foreign);
            f.Owner.enabled = false;
            Assert.That(f.Get(false), Is.SameAs(f.Foreign));
            Assert.That(f.Get(true), Is.SameAs(f.Source));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LostTargetReference_ReleaseStillRestoresActualAssignment(bool skinned)
        {
            using var f = new Fixture(skinned);
            Mesh output = f.Owner.Deform(true);
            f.SwitchTarget(nullTarget: true);
            f.Owner.enabled = false;
            Assert.That(f.Get(false), Is.SameAs(f.Source));
            Assert.That(output == null, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ChangedTarget_ExternalOldAssignmentSurvivesCleanup(bool skinned)
        {
            using var f = new Fixture(skinned);
            Mesh output = f.Owner.Deform(true);
            f.Set(false, f.Foreign);
            f.SwitchTarget();
            f.Owner.enabled = false;
            Assert.That(f.Get(false), Is.SameAs(f.Foreign));
            Assert.That(f.Get(true), Is.SameAs(f.Source));
            Assert.That(output == null, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExplicitRebind_RestoresOldAssignmentToOldSource(bool skinned)
        {
            using var f = new Fixture(skinned);
            Mesh output = f.Owner.Deform(true);
            f.Set(true, f.Foreign);
            f.SwitchTarget();
            f.Owner.Reset();
            Assert.That(f.Get(false), Is.SameAs(f.Source));
            Assert.That(f.Get(true), Is.SameAs(f.Foreign));
            Assert.That(output == null, Is.True);
            var next = f.Owner.Deform(true);
            Assert.That(next, Is.Not.Null);
            f.Owner.enabled = false;
            Assert.That(f.Get(true), Is.SameAs(f.Foreign));
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly Mesh Source = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            internal readonly Mesh Foreign;
            internal readonly GameObject Root = new GameObject("__RuntimeAssignment");
            internal readonly GameObject Other = new GameObject("__RuntimeAssignmentOther");
            internal readonly LatticeDeformer Owner;
            private readonly bool _skinned;
            private readonly MeshFilter[] _filters = new MeshFilter[2];
            private readonly SkinnedMeshRenderer[] _renderers = new SkinnedMeshRenderer[2];

            internal Fixture(bool skinned)
            {
                _skinned = skinned;
                Source.RecalculateNormals();
                Foreign = Object.Instantiate(Source);
                var objects = new[] { Root, Other };
                for (int i = 0; i < objects.Length; i++)
                {
                    if (skinned) _renderers[i] = objects[i].AddComponent<SkinnedMeshRenderer>();
                    else { _filters[i] = objects[i].AddComponent<MeshFilter>(); objects[i].AddComponent<MeshRenderer>(); }
                    Set(i == 1, Source);
                }
                Owner = Root.AddComponent<LatticeDeformer>();
                Owner.Reset();
            }

            internal Mesh Get(bool other) => _skinned ? _renderers[other ? 1 : 0].sharedMesh : _filters[other ? 1 : 0].sharedMesh;
            internal void Set(bool other, Mesh mesh)
            {
                if (_skinned) _renderers[other ? 1 : 0].sharedMesh = mesh;
                else _filters[other ? 1 : 0].sharedMesh = mesh;
            }
            internal void SwitchTarget(bool nullTarget = false)
            {
                typeof(LatticeDeformer).GetField(_skinned ? "_skinnedMeshRenderer" : "_meshFilter", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(Owner, nullTarget ? null : _skinned ? (Object)_renderers[1] : _filters[1]);
            }
            public void Dispose()
            {
                Object.DestroyImmediate(Root);
                Object.DestroyImmediate(Other);
                Object.DestroyImmediate(Foreign);
                Object.DestroyImmediate(Source);
            }
        }
    }
}
#endif
