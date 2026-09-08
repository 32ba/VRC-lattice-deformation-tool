#if UNITY_EDITOR
using System;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class LatticeCageGeometryTests
    {
        [Test]
        public void PointAndDeltaMapping_PreserveTranslationScalingAndCollapsedAxisRules()
        {
            var from = new Bounds(Vector3.zero, new Vector3(2, 4, 0));
            var to = new Bounds(new Vector3(10, 20, 30), new Vector3(6, 8, 10));
            Assert.That(LatticeCageGeometry.MapPointBetweenBounds(new Vector3(1, -2, 99), from, to),
                Is.EqualTo(new Vector3(13, 16, 25)));
            Assert.That(LatticeCageGeometry.MapDeltaBetweenBounds(new Vector3(2, -3, 99), from, to),
                Is.EqualTo(new Vector3(6, -6, 0)));
            var remapped = LatticeCageGeometry.RemapBounds(new Bounds(Vector3.zero, new Vector3(1, 2, 0)), from, to);
            Assert.That(remapped.center, Is.EqualTo(new Vector3(10, 20, 25)));
            Assert.That(remapped.size, Is.EqualTo(new Vector3(3, 4, 0)));
        }

        [Test]
        public void ReferencedBounds_ExcludeUnusedVerticesAndRefreshScratchAcrossMeshes()
        {
            var vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one * 100 };
            var mesh = new Mesh { vertices = vertices, subMeshCount = 2 };
            try
            {
                mesh.SetIndices(new[] { 0, 1 }, MeshTopology.Lines, 0);
                mesh.SetIndices(new[] { 2 }, MeshTopology.Points, 1);
                var originalIndices = mesh.GetIndices(0);
                var geometry = new LatticeCageGeometry();
                var matrix = Matrix4x4.TRS(new Vector3(10, -2, 3), Quaternion.identity, new Vector3(-2, 3, 4));
                Bounds bounds = geometry.CalculateTransformedReferencedBounds(mesh, vertices, matrix);
                Assert.That(bounds.min, Is.EqualTo(new Vector3(8, -2, 3)));
                Assert.That(bounds.max, Is.EqualTo(new Vector3(10, 1, 3)));
                mesh.SetIndices(Array.Empty<int>(), MeshTopology.Lines, 0);
                mesh.SetIndices(Array.Empty<int>(), MeshTopology.Points, 1);
                bounds = geometry.CalculateTransformedReferencedBounds(mesh, vertices, Matrix4x4.identity);
                Assert.That(bounds.min, Is.EqualTo(Vector3.zero));
                Assert.That(bounds.max, Is.EqualTo(Vector3.one * 100));
                Assert.That(mesh.vertices, Is.EqualTo(vertices));
                Assert.That(originalIndices, Is.EqualTo(new[] { 0, 1 }));
                bounds = geometry.CalculateTransformedReferencedBounds(null, new[] { Vector3.one }, Matrix4x4.identity);
                Assert.That(bounds.center, Is.EqualTo(Vector3.one));
                Assert.That(bounds.size, Is.EqualTo(Vector3.zero));
            }
            finally { Object.DestroyImmediate(mesh); }
        }
    }
}
#endif
