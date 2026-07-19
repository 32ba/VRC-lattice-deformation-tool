#if UNITY_EDITOR
using Net._32Ba.LatticeDeformationTool;
using NUnit.Framework;
using System.Reflection;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class BrushDeformerCoreTests
    {
        [Test]
        public void BrushDeformer_DefaultsAndSettings_AreStable()
        {
            var go = new GameObject("brush");
            try
            {
                var deformer = go.AddComponent<BrushDeformer>();

                Assert.That(BrushDeformer.SuppressRestoreOnDisable, Is.False);
                BrushDeformer.SuppressRestoreOnDisable = true;
                Assert.That(BrushDeformer.SuppressRestoreOnDisable, Is.True);
                BrushDeformer.SuppressRestoreOnDisable = false;

                Assert.That(deformer.RuntimeMesh, Is.Null);
                Assert.That(deformer.SourceMesh, Is.Null);
                Assert.That(deformer.Displacements, Is.Empty);
                Assert.That(deformer.DisplacementCount, Is.EqualTo(0));
                Assert.That(deformer.HasDisplacements(), Is.False);
                Assert.That(deformer.MeshTransform, Is.SameAs(go.transform));

                deformer.RecalculateBoneWeights = true;
                Assert.That(deformer.RecalculateBoneWeights, Is.True);

                deformer.WeightTransferSettings = null;
                Assert.That(deformer.WeightTransferSettings, Is.Not.Null);

                typeof(BrushDeformer)
                    .GetField("_weightTransferSettings", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, null);
                Assert.That(deformer.WeightTransferSettings, Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void BrushDeformer_DisplacementOperations_ResizeCopyAndClear()
        {
            var go = new GameObject("brush");
            var mesh = CreateTriangleMesh("source");
            try
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var deformer = go.AddComponent<BrushDeformer>();

                deformer.EnsureDisplacementCapacity();
                Assert.That(deformer.DisplacementCount, Is.EqualTo(3));

                deformer.SetDisplacement(0, Vector3.right);
                deformer.AddDisplacement(1, Vector3.up);
                deformer.SetDisplacement(-1, Vector3.one);
                deformer.AddDisplacement(99, Vector3.one);

                Assert.That(deformer.GetDisplacement(0), Is.EqualTo(Vector3.right));
                Assert.That(deformer.GetDisplacement(1), Is.EqualTo(Vector3.up));
                Assert.That(deformer.GetDisplacement(-1), Is.EqualTo(Vector3.zero));
                Assert.That(deformer.HasDisplacements(), Is.True);

                deformer.ClearDisplacements();
                Assert.That(deformer.HasDisplacements(), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void BrushDeformer_Deform_CreatesRuntimeMeshAndCanRestoreOriginal()
        {
            var go = new GameObject("brush");
            var mesh = CreateTriangleMesh("source");
            try
            {
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var deformer = go.AddComponent<BrushDeformer>();

                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(1f, 2f, 3f));

                var runtime = deformer.Deform();

                Assert.That(runtime, Is.Not.Null);
                Assert.That(deformer.RuntimeMesh, Is.SameAs(runtime));
                Assert.That(deformer.SourceMesh, Is.SameAs(mesh));
                Assert.That(filter.sharedMesh, Is.SameAs(runtime));
                Assert.That(runtime.vertices[0], Is.EqualTo(new Vector3(1f, 2f, 3f)));
                Assert.That(runtime.name, Does.Contain("(Brush)"));

                deformer.RestoreOriginalMesh();

                Assert.That(filter.sharedMesh, Is.SameAs(mesh));
                Assert.That(deformer.RuntimeMesh, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void BrushDeformer_DeformAfterSourceMeshSwap_RecreatesRuntimeMesh()
        {
            var go = new GameObject("brush");
            var sourceMesh = CreateTriangleMesh("source-a");
            var replacementMesh = CreateQuadMesh("source-b");
            try
            {
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = sourceMesh;
                var deformer = go.AddComponent<BrushDeformer>();
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, Vector3.right);

                var firstRuntimeMesh = deformer.Deform();
                Assert.That(firstRuntimeMesh, Is.Not.Null);
                Assert.That(firstRuntimeMesh.vertexCount, Is.EqualTo(sourceMesh.vertexCount));

                filter.sharedMesh = replacementMesh;

                Mesh secondRuntimeMesh = null;
                Assert.DoesNotThrow(() => secondRuntimeMesh = deformer.Deform());
                Assert.That(secondRuntimeMesh, Is.Not.Null);
                Assert.That(secondRuntimeMesh, Is.Not.SameAs(firstRuntimeMesh));
                Assert.That(secondRuntimeMesh.vertexCount, Is.EqualTo(replacementMesh.vertexCount));
                Assert.That(deformer.SourceMesh, Is.SameAs(replacementMesh));
                Assert.That(filter.sharedMesh, Is.SameAs(secondRuntimeMesh));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(sourceMesh);
                Object.DestroyImmediate(replacementMesh);
            }
        }

        [Test]
        public void BrushDeformer_OnDisableSuppress_ReleasesRuntimeMeshWithoutRestoringRenderer()
        {
            var go = new GameObject("brush");
            var mesh = CreateTriangleMesh("source");
            try
            {
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var deformer = go.AddComponent<BrushDeformer>();
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, Vector3.right);
                deformer.Deform();

                BrushDeformer.SuppressRestoreOnDisable = true;
                go.SendMessage("OnDisable", SendMessageOptions.DontRequireReceiver);

                Assert.That(deformer.RuntimeMesh, Is.Null);
                Assert.That(filter.sharedMesh == null, Is.True);
            }
            finally
            {
                BrushDeformer.SuppressRestoreOnDisable = false;
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void BrushDeformer_RestoreOriginalMesh_RestoresSkinnedMeshRenderer()
        {
            var go = new GameObject("brush");
            var mesh = CreateTriangleMesh("source");
            try
            {
                var skinned = go.AddComponent<SkinnedMeshRenderer>();
                skinned.sharedMesh = mesh;
                var deformer = go.AddComponent<BrushDeformer>();
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, Vector3.right);
                var runtime = deformer.Deform();

                Assert.That(skinned.sharedMesh, Is.SameAs(runtime));

                deformer.RestoreOriginalMesh();

                Assert.That(skinned.sharedMesh, Is.SameAs(mesh));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void BrushDeformer_Deform_ReturnsNullWithoutSourceOrDisplacements()
        {
            var go = new GameObject("brush");
            try
            {
                var deformer = go.AddComponent<BrushDeformer>();

                Assert.That(deformer.Deform(), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void BrushDeformer_MeshTransform_PrefersSkinnedThenMeshFilter()
        {
            var meshGo = new GameObject("mesh");
            var skinnedGo = new GameObject("skinned");
            try
            {
                meshGo.AddComponent<MeshFilter>();
                var meshDeformer = meshGo.AddComponent<BrushDeformer>();
                Assert.That(meshDeformer.MeshTransform, Is.SameAs(meshGo.transform));

                skinnedGo.AddComponent<SkinnedMeshRenderer>();
                var skinnedDeformer = skinnedGo.AddComponent<BrushDeformer>();
                Assert.That(skinnedDeformer.MeshTransform, Is.SameAs(skinnedGo.transform));
            }
            finally
            {
                Object.DestroyImmediate(meshGo);
                Object.DestroyImmediate(skinnedGo);
            }
        }

        [Test]
        public void BrushDeformer_Reset_CachesAttachedRendererAndMesh()
        {
            var go = new GameObject("brush");
            var mesh = CreateTriangleMesh("source");
            try
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var deformer = go.AddComponent<BrushDeformer>();

                deformer.Reset();

                Assert.That(deformer.SourceMesh, Is.SameAs(mesh));
                Assert.That(deformer.DisplacementCount, Is.EqualTo(3));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(mesh);
            }
        }

        [TestCase(BrushFalloffType.Constant, 0.25f, 1f)]
        [TestCase(BrushFalloffType.Linear, 0.25f, 0.75f)]
        [TestCase(BrushFalloffType.Smooth, 0f, 1f)]
        [TestCase(BrushFalloffType.Sphere, 0.95f, 0.5f)]
        public void BrushDeformer_EvaluateFalloff_ReturnsExpectedValues(BrushFalloffType type, float t, float expected)
        {
            Assert.That(BrushDeformer.EvaluateFalloff(type, t), Is.EqualTo(expected).Within(1e-5f));
        }

        [Test]
        public void BrushDeformer_EvaluateFalloff_HandlesGaussianClampAndUnknownType()
        {
            Assert.That(BrushDeformer.EvaluateFalloff(BrushFalloffType.Gaussian, 0f), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(BrushDeformer.EvaluateFalloff((BrushFalloffType)999, 0.25f), Is.EqualTo(0.75f).Within(1e-5f));
            Assert.That(BrushDeformer.EvaluateFalloff(BrushFalloffType.Linear, -1f), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(BrushDeformer.EvaluateFalloff(BrushFalloffType.Linear, 2f), Is.EqualTo(0f).Within(1e-5f));
        }

        private static Mesh CreateTriangleMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = new[]
            {
                Vector3.zero,
                Vector3.right,
                Vector3.up
            };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateQuadMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = new[]
            {
                new Vector3(-1f, -1f, 0f),
                new Vector3(1f, -1f, 0f),
                new Vector3(1f, 1f, 0f),
                new Vector3(-1f, 1f, 0f)
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
#endif
