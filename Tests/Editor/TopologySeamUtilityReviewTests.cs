#if UNITY_EDITOR
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class TopologySeamUtilityReviewTests
    {
        [Test]
        public void ClosedMesh_CoincidentSheetsWithDifferentDiagonalsRemainOpen()
        {
            var mesh = CreateDifferentlyTriangulatedCoincidentSheets();
            try
            {
                Assert.That(
                    ClearanceQuery.TryCreate(mesh, Matrix4x4.identity, out var query),
                    Is.True);
                Assert.That(query.IsClosedSurface, Is.False);

                var result = query.QueryPoint(
                    new Vector3(0.5f, 0.5f, 0.1f),
                    ClearanceSignMode.ClosedMesh);
                Assert.That(result.SignMode, Is.EqualTo(ClearanceSignMode.ReferenceNormal));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void FitTopology_CoincidentSheetsWithDifferentDiagonalsKeepBoundaries()
        {
            var mesh = CreateDifferentlyTriangulatedCoincidentSheets();
            try
            {
                Vector3[] vertices = mesh.vertices;
                int[] indices = mesh.triangles;
                var boundaries = new bool[vertices.Length];

                TopologySeamUtility.MarkOpenBoundaryVertices(
                    vertices,
                    indices,
                    boundaries);

                Assert.That(boundaries, Is.All.True);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        private static Mesh CreateDifferentlyTriangulatedCoincidentSheets()
        {
            var mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(0f, 0f, 0f),
                    new Vector3(1f, 0f, 0f),
                    new Vector3(1f, 1f, 0f),
                    new Vector3(0f, 1f, 0f),
                    new Vector3(0f, 0f, 0f),
                    new Vector3(1f, 0f, 0f),
                    new Vector3(1f, 1f, 0f),
                    new Vector3(0f, 1f, 0f)
                },
                // Front uses diagonal 0-2. Back uses diagonal 5-7 with the
                // opposite winding, so there are no duplicate triangle keys.
                triangles = new[]
                {
                    0, 1, 2,
                    0, 2, 3,
                    4, 7, 5,
                    5, 7, 6
                }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
#endif
