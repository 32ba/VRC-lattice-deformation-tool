#if UNITY_EDITOR
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class VertexPickingQueryTests
    {
        private static Vector2 Project(Vector3 point) => new Vector2(point.x, point.y);
        [Test]
        public void Nearest_UsesStrictRadiusFirstTieAndBorrowedWorldPose()
        {
            var local = new[] { Vector3.one * 100, Vector3.zero, Vector3.zero };
            var world = new[] { Vector3.left, Vector3.right };
            var query = new VertexPickingQuery(local, world, null,
                Matrix4x4.Translate(Vector3.up * 10), null, false, Project);
            Assert.That(query.Nearest(3, Vector2.zero, 1), Is.EqualTo(-1));
            Assert.That(query.Nearest(3, Vector2.zero, 2), Is.EqualTo(0));
            Assert.That(query.Nearest(0, Vector2.zero, 100), Is.EqualTo(-1));
            Assert.That(query.Nearest(3, new Vector2(0, 10), 1), Is.EqualTo(2));
            Assert.That(world[0], Is.EqualTo(Vector3.left));
            Assert.That(local[0], Is.EqualTo(Vector3.one * 100));
        }
        [Test]
        public void Rectangle_NormalizesDragAndExcludesUpperEdges()
        {
            var points = new[] { Vector3.zero, Vector3.right, Vector3.up, new Vector3(0.5f, 0.5f, 0) };
            var query = new VertexPickingQuery(points, null, null, Matrix4x4.identity, null, false, Project);
            var rect = VertexPickingQuery.Rectangle(Vector2.one, Vector2.zero);
            Assert.That(query.Inside(4, rect), Is.EqualTo(new[] { 0, 3 }));
            Assert.That(query.Inside(0, rect), Is.Empty);
        }
        [Test]
        public void Backfaces_UseCameraAndPermitVerticesWithoutNormalData()
        {
            var points = new[] { Vector3.zero, Vector3.zero, Vector3.zero };
            var normals = new[] { Vector3.back, Vector3.forward };
            var query = new VertexPickingQuery(points, null, normals, Matrix4x4.identity, Vector3.forward * 5, true, Project);
            Assert.That(query.Nearest(3, Vector2.zero, 1), Is.EqualTo(1));
            Assert.That(query.Inside(3, new Rect(-1, -1, 2, 2)), Is.EqualTo(new[] { 1, 2 }));
            query = new VertexPickingQuery(points, null, normals, Matrix4x4.identity, null, true, Project);
            Assert.That(query.Nearest(3, Vector2.zero, 1), Is.EqualTo(0));
            query = new VertexPickingQuery(points, null, normals, Matrix4x4.identity, Vector3.forward, false, Project);
            Assert.That(query.Nearest(3, Vector2.zero, 1), Is.EqualTo(0));
        }
    }
}
#endif
