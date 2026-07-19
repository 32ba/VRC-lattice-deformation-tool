#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class SymmetryVertexMapTests
    {
        [Test]
        public void Build_PerfectSymmetryAndCenterVertices_ProducesStablePairs()
        {
            var vertices = new[]
            {
                new Vector3(-1f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 2f, 0f),
                new Vector3(-2f, 1f, 0f),
                new Vector3(2f, 1f, 0f)
            };

            var map = SymmetryVertexMapCache.Build(vertices, 0);

            Assert.That(map.Count, Is.EqualTo(vertices.Length));
            Assert.That(map.UnmatchedCount, Is.Zero);
            Assert.That(map[0], Is.EqualTo(1));
            Assert.That(map[1], Is.EqualTo(0));
            Assert.That(map[2], Is.EqualTo(2));
            Assert.That(map[3], Is.EqualTo(4));
            Assert.That(map[4], Is.EqualTo(3));
        }

        [Test]
        public void Build_ConfigurableAxisCenterAndTolerance_AreApplied()
        {
            var yMap = SymmetryVertexMapCache.Build(
                new[] { new Vector3(0f, 1.9996f, 0f), new Vector3(0f, 4.0004f, 0f) },
                axis: 1,
                centerOffset: 3f,
                tolerance: 0.001f);
            var zMap = SymmetryVertexMapCache.Build(
                new[] { new Vector3(0f, 0f, -2f), new Vector3(0f, 0f, 2f) },
                axis: 2);

            Assert.That(yMap[0], Is.EqualTo(1));
            Assert.That(yMap[1], Is.EqualTo(0));
            Assert.That(zMap[0], Is.EqualTo(1));
            Assert.That(zMap[1], Is.EqualTo(0));
            Assert.That(SymmetryVertexMapCache.GetSignedDistance(new Vector3(0f, 4f, 0f), 1, 3f), Is.EqualTo(1f));
            Assert.That(SymmetryVertexMapCache.Mirror(new Vector3(1f, 2f, 3f), 1, 3f),
                Is.EqualTo(new Vector3(1f, 4f, 3f)));
            Assert.That(SymmetryVertexMapCache.MirrorDirection(new Vector3(1f, 2f, 3f), 2),
                Is.EqualTo(new Vector3(1f, 2f, -3f)));
        }

        [Test]
        public void Build_AsymmetricAndNonFiniteVertices_UseExplicitUnmatchedBehavior()
        {
            var vertices = new[]
            {
                new Vector3(-1f, 0f, 0f),
                new Vector3(2f, 0f, 0f),
                new Vector3(float.NaN, 0f, 0f)
            };

            var skipped = SymmetryVertexMapCache.Build(vertices, 0);
            var self = SymmetryVertexMapCache.Build(
                vertices,
                0,
                unmatchedBehavior: UnmatchedSymmetryVertexBehavior.Self);

            Assert.That(skipped.UnmatchedCount, Is.EqualTo(3));
            Assert.That(skipped.TryGetPartner(0, out var skippedPartner), Is.False);
            Assert.That(skippedPartner, Is.EqualTo(-1));
            Assert.That(skipped.TryGetPartner(-1, out _), Is.False);
            Assert.That(self[0], Is.EqualTo(0));
            Assert.That(self[1], Is.EqualTo(1));
            Assert.That(self[2], Is.EqualTo(2));
        }

        [Test]
        public void Build_InvalidInputs_AreRejected()
        {
            Assert.That(() => SymmetryVertexMapCache.Build(null, 0), Throws.ArgumentNullException);
            Assert.That(() => SymmetryVertexMapCache.Build(Array.Empty<Vector3>(), -1), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => SymmetryVertexMapCache.Build(Array.Empty<Vector3>(), 3), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => SymmetryVertexMapCache.Build(Array.Empty<Vector3>(), 0, float.NaN), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => SymmetryVertexMapCache.Build(Array.Empty<Vector3>(), 0, tolerance: 0f), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => SymmetryVertexMapCache.Build(Array.Empty<Vector3>(), 0,
                    unmatchedBehavior: (UnmatchedSymmetryVertexBehavior)99),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => SymmetryVertexMapCache.GetOrCreate(null, 0), Throws.ArgumentNullException);
            Assert.That(() => SymmetryVertexMapCache.Mirror(Vector3.zero, 4), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void GetOrCreate_ReusesMapUntilMeshIsInvalidatedOrVertexDataChanges()
        {
            var mesh = CreateMesh(new Vector3(-1f, 0f, 0f), new Vector3(1f, 0f, 0f));
            try
            {
                var first = SymmetryVertexMapCache.GetOrCreate(mesh, 0);
                var reused = SymmetryVertexMapCache.GetOrCreate(mesh, 0);
                var differentAxis = SymmetryVertexMapCache.GetOrCreate(mesh, 1);

                Assert.That(reused, Is.SameAs(first));
                Assert.That(differentAxis, Is.Not.SameAs(first));

                mesh.vertices = new[] { new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f) };
                var repositioned = SymmetryVertexMapCache.GetOrCreate(mesh, 0);
                Assert.That(repositioned, Is.Not.SameAs(first),
                    "Changing positions without changing vertex count must invalidate the map.");

                mesh.vertices = new[] { Vector3.left, Vector3.right, Vector3.zero };
                var resized = SymmetryVertexMapCache.GetOrCreate(mesh, 0);
                Assert.That(resized, Is.Not.SameAs(repositioned));
                Assert.That(resized.Count, Is.EqualTo(3));

                SymmetryVertexMapCache.Invalidate(mesh);
                var invalidated = SymmetryVertexMapCache.GetOrCreate(mesh, 0);
                Assert.That(invalidated, Is.Not.SameAs(resized));
                SymmetryVertexMapCache.Invalidate(null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test, Timeout(5000)]
        public void Build_HighVertexCountMesh_CompletesAndPairsDeterministically()
        {
            const int pairCount = 10000;
            var vertices = new Vector3[pairCount * 2 + 1];
            for (int i = 0; i < pairCount; i++)
            {
                float y = i * 0.002f;
                vertices[i * 2] = new Vector3(-1f, y, 0f);
                vertices[i * 2 + 1] = new Vector3(1f, y, 0f);
            }
            vertices[vertices.Length - 1] = new Vector3(0f, -1f, 0f);

            var map = SymmetryVertexMapCache.Build(vertices, 0);

            Assert.That(map.UnmatchedCount, Is.Zero);
            Assert.That(map[0], Is.EqualTo(1));
            Assert.That(map[19998], Is.EqualTo(19999));
            Assert.That(map[vertices.Length - 1], Is.EqualTo(vertices.Length - 1));
        }

        [Test]
        public void FlipLayerByAxis_PerfectAndUnmatchedVertices_MatchesLegacyResult()
        {
            var mesh = CreateMesh(
                new Vector3(-1f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                Vector3.zero,
                new Vector3(3f, 1f, 0f));
            var gameObject = new GameObject(nameof(FlipLayerByAxis_PerfectAndUnmatchedVertices_MatchesLegacyResult));
            try
            {
                gameObject.AddComponent<MeshRenderer>();
                gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var deformer = gameObject.AddComponent<LatticeDeformer>();
                int layerIndex = deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = layerIndex;
                deformer.EnsureDisplacementCapacity();

                var values = new[]
                {
                    new Vector3(1f, 2f, 3f),
                    new Vector3(4f, 5f, 6f),
                    new Vector3(7f, 8f, 9f),
                    new Vector3(10f, 11f, 12f)
                };
                for (int i = 0; i < values.Length; i++) deformer.SetDisplacement(i, values[i]);

                deformer.FlipLayerByAxis(layerIndex, 0);

                Assert.That(deformer.GetDisplacement(0), Is.EqualTo(new Vector3(-4f, 5f, 6f)));
                Assert.That(deformer.GetDisplacement(1), Is.EqualTo(new Vector3(-1f, 2f, 3f)));
                Assert.That(deformer.GetDisplacement(2), Is.EqualTo(new Vector3(-7f, 8f, 9f)));
                Assert.That(deformer.GetDisplacement(3), Is.EqualTo(new Vector3(-10f, 11f, 12f)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void VertexSelectionMirror_AddsMappedPartnerAndSkipsUnmatchedVertex()
        {
            var mesh = CreateMesh(Vector3.left, Vector3.right, new Vector3(3f, 0f, 0f));
            var gameObject = new GameObject(nameof(VertexSelectionMirror_AddsMappedPartnerAndSkipsUnmatchedVertex));
            var selectionField = typeof(VertexSelectionHandler).GetField(
                "s_selectedVertices",
                BindingFlags.Static | BindingFlags.NonPublic);
            var selection = (HashSet<int>)selectionField.GetValue(null);
            try
            {
                gameObject.AddComponent<MeshRenderer>();
                gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var deformer = gameObject.AddComponent<LatticeDeformer>();

                selection.Clear();
                selection.Add(0);
                selection.Add(2);
                VertexSelectionHandler.SelectMirrorPartners(deformer, 0);

                Assert.That(selection, Is.EquivalentTo(new[] { 0, 1, 2 }));
            }
            finally
            {
                selection.Clear();
                UnityEngine.Object.DestroyImmediate(gameObject);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static Mesh CreateMesh(params Vector3[] vertices)
        {
            var mesh = new Mesh { name = "SymmetryVertexMapTestMesh" };
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
#endif
