#if UNITY_EDITOR
using System.Collections.Generic;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class LatticeControlSelectionTests
    {
        [Test]
        public void ClickSequence_ReplacesTogglesAndRetainsIndependentSelections()
        {
            var selection = new LatticeControlSelection();
            var other = new LatticeControlSelection();
            other.Select(7, false);
            selection.Select(1, false);
            selection.Select(2, true);
            Assert.That(Indices(selection), Is.EquivalentTo(new[] { 1, 2 }));
            selection.Select(1, true);
            Assert.That(Indices(selection), Is.EqualTo(new[] { 2 }));
            selection.Select(5, false);
            selection.Select(5, false);
            Assert.That(Indices(selection), Is.EqualTo(new[] { 5 }));
            selection.Select(5, true);
            Assert.That(selection.Count, Is.Zero);
            Assert.That(other.Contains(7), Is.True);
            other.Clear();
            other.Clear();
            Assert.That(other.Count, Is.Zero);
        }

        [Test]
        public void GridShrink_RemovesInvalidIndicesWithoutChangingSurvivingSelection()
        {
            var selection = new LatticeControlSelection();
            foreach (int index in new[] { -1, 0, 3, 4, 20 }) selection.Select(index, true);
            selection.TrimToCount(4);
            Assert.That(Indices(selection), Is.EquivalentTo(new[] { 0, 3 }));
            selection.TrimToCount(0);
            Assert.That(selection.Count, Is.Zero);
        }

        [TestCase(3, 3, 3, 26)]
        [TestCase(4, 5, 3, 54)]
        [TestCase(1, 4, 3, 12)]
        [TestCase(2, 4, 3, 24)]
        public void BoundaryScope_RemovesOnlyInteriorAndRemainsIdempotent(int x, int y, int z, int boundaryCount)
        {
            var selection = new LatticeControlSelection();
            for (int index = 0; index < x * y * z; index++) selection.Select(index, true);
            selection.KeepBoundary(new Vector3Int(x, y, z));
            Assert.That(selection.Count, Is.EqualTo(boundaryCount));
            Assert.That(selection.Contains(0), Is.True);
            Assert.That(selection.Contains(x * y * z - 1), Is.True);
            var first = Indices(selection);
            selection.KeepBoundary(new Vector3Int(x, y, z));
            Assert.That(Indices(selection), Is.EqualTo(first));
            if (x > 2 && y > 2 && z > 2)
                Assert.That(selection.Contains(1 + x + x * y), Is.False);
        }

        private static int[] Indices(LatticeControlSelection selection)
        {
            var indices = new List<int>();
            foreach (int index in selection) indices.Add(index);
            return indices.ToArray();
        }
    }
}
#endif
