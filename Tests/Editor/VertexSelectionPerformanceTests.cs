#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class VertexSelectionPerformanceTests
    {
        [Test]
        public void ProportionalInfluenceCache_ReusesStableResultAndTracksEveryInput()
        {
            var gameObject = new GameObject(nameof(ProportionalInfluenceCache_ReusesStableResultAndTracksEveryInput));
            var handler = new VertexSelectionHandler();
            var selected = GetSelectedVertices();
            var originalSelection = new HashSet<int>(selected);
            float originalRadius = VertexSelectionHandler.ProportionalRadius;
            var originalFalloff = VertexSelectionHandler.ProportionalFalloffType;

            try
            {
                var deformer = gameObject.AddComponent<LatticeDeformer>();
                handler.Activate(deformer);
                SetField(handler, "_meshVertices", new[]
                {
                    Vector3.zero,
                    new Vector3(0.75f, 0f, 0f),
                    new Vector3(1.5f, 0f, 0f)
                });
                selected.Clear();
                selected.Add(0);
                VertexSelectionHandler.ProportionalRadius = 1f;
                VertexSelectionHandler.ProportionalFalloffType = VertexSelectionHandler.FalloffType.Linear;

                RebuildInfluences(handler);
                Assert.That(GetBuildCount(handler), Is.EqualTo(1));
                Assert.That(GetInfluence(handler, 1), Is.EqualTo(0.25f).Within(1e-6f));
                Assert.That(GetInfluence(handler, 2), Is.Zero);

                RebuildInfluences(handler);
                Assert.That(GetBuildCount(handler), Is.EqualTo(1),
                    "An unchanged repaint must reuse the cached nearest-selection result.");

                selected.Add(2);
                RebuildInfluences(handler);
                Assert.That(GetBuildCount(handler), Is.EqualTo(2));

                var vertices = GetField<Vector3[]>(handler, "_meshVertices");
                vertices[1] = new Vector3(0.25f, 0f, 0f);
                RebuildInfluences(handler);
                Assert.That(GetBuildCount(handler), Is.EqualTo(3),
                    "In-place position edits must invalidate the cache.");

                VertexSelectionHandler.ProportionalRadius = 2f;
                RebuildInfluences(handler);
                Assert.That(GetBuildCount(handler), Is.EqualTo(4));

                VertexSelectionHandler.ProportionalFalloffType = VertexSelectionHandler.FalloffType.Smooth;
                RebuildInfluences(handler);
                Assert.That(GetBuildCount(handler), Is.EqualTo(5));

                gameObject.transform.localScale = Vector3.one * 2f;
                RebuildInfluences(handler);
                Assert.That(GetBuildCount(handler), Is.EqualTo(6),
                    "World-space radius conversion must follow target scale changes.");
            }
            finally
            {
                handler.Deactivate();
                selected.Clear();
                selected.UnionWith(originalSelection);
                VertexSelectionHandler.ProportionalRadius = originalRadius;
                VertexSelectionHandler.ProportionalFalloffType = originalFalloff;
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void ProportionalInfluenceKdTree_MatchesBruteForceNearestDistance()
        {
            var gameObject = new GameObject(nameof(ProportionalInfluenceKdTree_MatchesBruteForceNearestDistance));
            var handler = new VertexSelectionHandler();
            var selected = GetSelectedVertices();
            var originalSelection = new HashSet<int>(selected);
            float originalRadius = VertexSelectionHandler.ProportionalRadius;
            var originalFalloff = VertexSelectionHandler.ProportionalFalloffType;

            try
            {
                handler.Activate(gameObject.AddComponent<LatticeDeformer>());
                var vertices = new Vector3[200];
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] = new Vector3(
                        Mathf.Sin(i * 1.17f) * 2f,
                        Mathf.Cos(i * 0.73f) * 1.5f,
                        Mathf.Sin(i * 0.31f) * Mathf.Cos(i * 0.47f));
                }

                SetField(handler, "_meshVertices", vertices);
                selected.Clear();
                for (int i = 0; i < vertices.Length; i += 7)
                {
                    selected.Add(i);
                }

                const float radius = 2.5f;
                VertexSelectionHandler.ProportionalRadius = radius;
                VertexSelectionHandler.ProportionalFalloffType = VertexSelectionHandler.FalloffType.Linear;
                RebuildInfluences(handler);

                for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                {
                    float nearest = float.MaxValue;
                    foreach (int selectedIndex in selected)
                    {
                        nearest = Mathf.Min(nearest, Vector3.Distance(vertices[vertexIndex], vertices[selectedIndex]));
                    }

                    float expected = nearest < radius ? 1f - nearest / radius : 0f;
                    Assert.That(GetInfluence(handler, vertexIndex), Is.EqualTo(expected).Within(1e-5f),
                        $"vertex {vertexIndex}");
                }
            }
            finally
            {
                handler.Deactivate();
                selected.Clear();
                selected.UnionWith(originalSelection);
                VertexSelectionHandler.ProportionalRadius = originalRadius;
                VertexSelectionHandler.ProportionalFalloffType = originalFalloff;
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void ProportionalInfluenceKdTree_LargeSelectionAvoidsCartesianDistanceScan()
        {
            var gameObject = new GameObject(nameof(ProportionalInfluenceKdTree_LargeSelectionAvoidsCartesianDistanceScan));
            var handler = new VertexSelectionHandler();
            var selected = GetSelectedVertices();
            var originalSelection = new HashSet<int>(selected);
            float originalRadius = VertexSelectionHandler.ProportionalRadius;

            try
            {
                handler.Activate(gameObject.AddComponent<LatticeDeformer>());
                const int side = 10;
                var vertices = new Vector3[side * side * side];
                int index = 0;
                for (int z = 0; z < side; z++)
                for (int y = 0; y < side; y++)
                for (int x = 0; x < side; x++)
                {
                    vertices[index++] = new Vector3(x, y, z) * 0.1f;
                }

                SetField(handler, "_meshVertices", vertices);
                selected.Clear();
                for (int i = 0; i < vertices.Length; i += 2)
                {
                    selected.Add(i);
                }

                VertexSelectionHandler.ProportionalRadius = 5f;
                RebuildInfluences(handler);

                int distanceEvaluations = GetField<int>(handler, "_proportionalDistanceEvaluationCount");
                long bruteForceEvaluations = (long)(vertices.Length - selected.Count) * selected.Count;
                Assert.That(distanceEvaluations, Is.LessThan(bruteForceEvaluations / 4),
                    "A large rectangular selection must use the spatial index rather than a V×S scan.");
            }
            finally
            {
                handler.Deactivate();
                selected.Clear();
                selected.UnionWith(originalSelection);
                VertexSelectionHandler.ProportionalRadius = originalRadius;
                Object.DestroyImmediate(gameObject);
            }
        }

        private static HashSet<int> GetSelectedVertices()
        {
            var field = typeof(VertexSelectionHandler).GetField(
                "s_selectedVertices",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (HashSet<int>)field.GetValue(null);
        }

        private static void RebuildInfluences(VertexSelectionHandler handler)
        {
            var method = typeof(VertexSelectionHandler).GetMethod(
                "EnsureProportionalInfluenceCache",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(handler, null);
        }

        private static float GetInfluence(VertexSelectionHandler handler, int index)
        {
            var method = typeof(VertexSelectionHandler).GetMethod(
                "GetProportionalInfluence",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (float)method.Invoke(handler, new object[] { index });
        }

        private static int GetBuildCount(VertexSelectionHandler handler)
        {
            return GetField<int>(handler, "_proportionalInfluenceCacheBuildCount");
        }

        private static void SetField<T>(VertexSelectionHandler handler, string name, T value)
        {
            var field = typeof(VertexSelectionHandler).GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(handler, value);
        }

        private static T GetField<T>(VertexSelectionHandler handler, string name)
        {
            var field = typeof(VertexSelectionHandler).GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            return (T)field.GetValue(handler);
        }
    }
}
#endif
