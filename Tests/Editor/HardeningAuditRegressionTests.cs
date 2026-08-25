#if UNITY_EDITOR
using System;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class HardeningAuditRegressionTests
    {
        [Test]
        public void ResizeGrid_Overflow_IsRejectedWithoutChangingState()
        {
            var asset = new LatticeAsset();
            asset.EnsureInitialized();
            Vector3Int oldGrid = asset.GridSize;
            Vector3[] oldPoints = asset.ControlPointsLocal.ToArray();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                asset.ResizeGrid(new Vector3Int(1024, 1024, 2048)));

            Assert.That(asset.GridSize, Is.EqualTo(oldGrid));
            Assert.That(asset.ControlPointsLocal.ToArray(), Is.EqualTo(oldPoints));
        }

        [Test]
        public void LatticeAsset_PublicSettersRejectNonFiniteValues()
        {
            var asset = new LatticeAsset();
            asset.EnsureInitialized();
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                asset.LocalBounds = new Bounds(new Vector3(float.NaN, 0f, 0f), Vector3.one));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                asset.SetControlPointLocal(0, new Vector3(float.PositiveInfinity, 0f, 0f)));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                asset.Interpolation = (LatticeInterpolationMode)12345);
        }

        [Test]
        public void BrushDisplacement_FiniteAdditionCannotOverflowToInfinity()
        {
            var layer = new LatticeLayer();
            layer.SetType(MeshDeformerLayerType.Brush);
            layer.EnsureBrushDisplacementCapacity(1);
            layer.SetBrushDisplacement(0, new Vector3(float.MaxValue, 0f, 0f));
            layer.AddBrushDisplacement(0, new Vector3(float.MaxValue, 0f, 0f));
            Assert.That(layer.GetBrushDisplacement(0).x, Is.EqualTo(float.MaxValue));
            Assert.That(float.IsInfinity(layer.GetBrushDisplacement(0).x), Is.False);
        }

        [Test]
        public void PublicBrushFacadeMutation_AdvancesPreviewRevision()
        {
            var go = new GameObject("revision-test");
            var mesh = new Mesh();
            try
            {
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                mesh.triangles = new[] { 0, 1, 2 };
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                go.AddComponent<MeshRenderer>();
                var deformer = go.AddComponent<LatticeDeformer>();
                deformer.Reset();
                int layer = deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = layer;
                deformer.EnsureDisplacementCapacity();
                int before = deformer.DeformationDataRevision;
                deformer.SetDisplacement(0, Vector3.right);
                Assert.That(deformer.DeformationDataRevision, Is.Not.EqualTo(before));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void ClosedMesh_TangentVertexRayOutsidePointRemainsOutside()
        {
            var mesh = CreateIndexedCube();
            try
            {
                Assert.That(ClearanceQuery.TryCreate(mesh, Matrix4x4.identity, out var query), Is.True);
                var result = query.QueryPoint(
                    new Vector3(0.58010054f, -1.1559467f, 0.7778175f),
                    ClearanceSignMode.ClosedMesh);
                Assert.That(result.IsClosedSurface, Is.True);
                Assert.That(result.IsInside, Is.False);
                Assert.That(result.SignedClearance, Is.GreaterThan(0f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void ClosedMesh_SplitAttributeVerticesStillCountAsClosed()
        {
            var mesh = CreateSplitVertexCube();
            try
            {
                Assert.That(ClearanceQuery.TryCreate(mesh, Matrix4x4.identity, out var query), Is.True);
                Assert.That(query.IsClosedSurface, Is.True);
                Assert.That(query.QueryPoint(Vector3.zero, ClearanceSignMode.ClosedMesh).IsInside, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static Mesh CreateIndexedCube()
        {
            var mesh = new Mesh();
            mesh.vertices = new[]
            {
                new Vector3(-1,-1,-1), new Vector3(1,-1,-1),
                new Vector3(1,1,-1), new Vector3(-1,1,-1),
                new Vector3(-1,-1,1), new Vector3(1,-1,1),
                new Vector3(1,1,1), new Vector3(-1,1,1)
            };
            mesh.triangles = new[]
            {
                0,2,1, 0,3,2, 4,5,6, 4,6,7,
                0,1,5, 0,5,4, 3,7,6, 3,6,2,
                0,4,7, 0,7,3, 1,2,6, 1,6,5
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateSplitVertexCube()
        {
            Vector3[][] faces =
            {
                new[] { new Vector3(-1,-1,-1), new Vector3(-1,1,-1), new Vector3(1,1,-1), new Vector3(1,-1,-1) },
                new[] { new Vector3(-1,-1,1), new Vector3(1,-1,1), new Vector3(1,1,1), new Vector3(-1,1,1) },
                new[] { new Vector3(-1,-1,-1), new Vector3(1,-1,-1), new Vector3(1,-1,1), new Vector3(-1,-1,1) },
                new[] { new Vector3(-1,1,-1), new Vector3(-1,1,1), new Vector3(1,1,1), new Vector3(1,1,-1) },
                new[] { new Vector3(-1,-1,-1), new Vector3(-1,-1,1), new Vector3(-1,1,1), new Vector3(-1,1,-1) },
                new[] { new Vector3(1,-1,-1), new Vector3(1,1,-1), new Vector3(1,1,1), new Vector3(1,-1,1) }
            };
            var vertices = new Vector3[24];
            var triangles = new int[36];
            for (int face = 0; face < 6; face++)
            {
                Array.Copy(faces[face], 0, vertices, face * 4, 4);
                int v = face * 4;
                int t = face * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v;
                triangles[t + 4] = v + 2;
                triangles[t + 5] = v + 3;
            }
            var mesh = new Mesh { vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
#endif
