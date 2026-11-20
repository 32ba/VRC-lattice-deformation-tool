#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Net._32Ba.LatticeDeformationTool.Editor;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class VertexProportionalSnapshotTests
    {
        [Test]
        public void RevisionReuseClearAndInvalidInput_DoNotLeaveEmptyInfluencesCached()
        {
            var cache = new VertexProportionalInfluenceCache();
            var selected = new HashSet<int> { 0 };
            var points = new[] { Vector3.zero, Vector3.right };
            void Update(Vector3[] input, int revision) => cache.Update(Matrix4x4.identity,
                input, null, null, selected, 2, VertexSelectionHandler.FalloffType.Linear, 0, 0, revision);
            Update(points, 0);
            Assert.That(cache.GetInfluence(1), Is.EqualTo(.5f));
            points[1] = Vector3.right * 3;
            Update(points, 0);
            Assert.That(cache.GetInfluence(1), Is.EqualTo(.5f), "Same revision borrows the captured result.");
            Update(points, 1);
            Assert.That(cache.GetInfluence(1), Is.Zero);
            points[1] = Vector3.right;
            cache.Clear();
            Update(points, 1);
            Assert.That(cache.GetInfluence(1), Is.EqualTo(.5f));
            Update(null, 1);
            Assert.That(cache.GetInfluence(1), Is.Zero);
            Update(points, 1);
            Assert.That(cache.GetInfluence(1), Is.EqualTo(.5f), "Restored source must rebuild after invalid input.");
        }

        [Test]
        public void FrozenThenPosedThenTransformedPositions_HaveExplicitPrecedence()
        {
            var cache = new VertexProportionalInfluenceCache();
            var selected = new HashSet<int> { 0 };
            var local = new[] { Vector3.zero, Vector3.right };
            var posed = new[] { Vector3.zero, Vector3.right * .25f };
            var frozen = new[] { Vector3.zero, Vector3.right * .5f };
            void Update(Matrix4x4 matrix, Vector3[] world, Vector3[] gesture, int revision) =>
                cache.Update(matrix, local, world, gesture, selected, 2,
                    VertexSelectionHandler.FalloffType.Linear, 0, 0, revision);
            var scale = Matrix4x4.Scale(new Vector3(3, 2, 1));
            Update(scale, posed, frozen, 0);
            Assert.That(cache.GetInfluence(1), Is.EqualTo(.75f));
            Update(scale, posed, null, 1);
            Assert.That(cache.GetInfluence(1), Is.EqualTo(.875f));
            Update(scale, new Vector3[1], null, 2);
            Assert.That(cache.GetInfluence(1), Is.Zero);
            Update(Matrix4x4.identity, null, null, 2);
            Assert.That(cache.GetInfluence(1), Is.EqualTo(.5f), "Transform changes invalidate world distance.");
        }

        [Test]
        public void SelectionAndSettingsInvalidateAndWarmLookupDoesNotAllocate()
        {
            var cache = new VertexProportionalInfluenceCache();
            var selected = new HashSet<int> { 0 };
            var points = new[] { Vector3.zero, Vector3.right };
            void Update(int selectionRevision, int settingsRevision, float radius) =>
                cache.Update(Matrix4x4.identity, points, null, null, selected, radius,
                    VertexSelectionHandler.FalloffType.Linear, selectionRevision, settingsRevision, 0);
            Update(0, 0, 2);
            Update(0, 1, 4);
            Assert.That(cache.GetInfluence(1), Is.EqualTo(.75f));
            selected.Clear(); selected.Add(1);
            Update(1, 1, 4);
            Assert.That(cache.GetInfluence(0), Is.EqualTo(.75f));
            Assert.That(cache.GetInfluence(1), Is.EqualTo(1));
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) Update(1, 1, 4);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }
    }
}
#endif
