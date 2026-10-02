#if UNITY_EDITOR
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class InactiveEvaluationLifetimeTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void DestroyActiveOwner_ReleasesNativeScratchWithoutExplicitWorkspaceDisposal(bool preview)
        {
            var root = new GameObject("Destroyed active evaluation owner");
            root.SetActive(false);
            var source = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 }
            };
            Mesh output = null;
            EvaluationWorkspace workspace = null;
            try
            {
                source.RecalculateNormals();
                root.AddComponent<MeshFilter>().sharedMesh = source;
                root.AddComponent<MeshRenderer>();
                var owner = root.AddComponent<LatticeDeformer>();
                owner.Reset();
                root.SetActive(true);
                output = preview ? owner.CreatePreviewMeshFromInput(source) : owner.Deform(false);
                workspace = (EvaluationWorkspace)typeof(LatticeDeformer)
                    .GetField("_evaluationWorkspace", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
                Assert.That(workspace.Lattice.HasNativeResources, Is.True,
                    "Exercise a live native cache before testing destruction.");

                Object.DestroyImmediate(root);

                Assert.That(workspace.Lattice.HasNativeResources, Is.False,
                    "Unity lifecycle callbacks must release the cache before explicit test cleanup.");
                Assert.That(source != null, Is.True, "The borrowed source must survive its owner.");
                Assert.That(source.vertices, Is.EqualTo(new[] { Vector3.zero, Vector3.right, Vector3.up }));
                if (preview)
                    Assert.That(output != null, Is.True, "The caller owns a returned preview mesh.");
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                // Cleanup follows the lifecycle assertions, so it cannot mask their failure.
                workspace?.Dispose();
                if (output != null) Object.DestroyImmediate(output);
                Object.DestroyImmediate(source);
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void InactiveEvaluation_ReleasesNativeScratchAndCanEvaluateAgain(bool activeObject, bool preview)
        {
            var root = new GameObject("Inactive evaluation lifetime");
            root.SetActive(false);
            var source = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 }
            };
            Mesh output = null;
            LatticeDeformer owner = null;
            try
            {
                source.RecalculateNormals();
                root.AddComponent<MeshFilter>().sharedMesh = source;
                root.AddComponent<MeshRenderer>();
                owner = root.AddComponent<LatticeDeformer>();
                owner.Reset();
                owner.enabled = false;
                root.SetActive(activeObject);
                var workspace = (EvaluationWorkspace)typeof(LatticeDeformer)
                    .GetField("_evaluationWorkspace", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
                for (int i = 0; i < 2; i++)
                {
                    output = preview ? owner.CreatePreviewMeshFromInput(source) : owner.Deform(false);
                    Assert.That(output, Is.Not.Null);
                    Assert.That(output.vertices, Is.EqualTo(source.vertices));
                    Assert.That(workspace.Lattice.HasNativeResources, Is.False,
                        "Inactive evaluation must release native scratch before returning.");
                    if (preview) { Object.DestroyImmediate(output); output = null; }
                }
                root.SetActive(true);
                owner.enabled = true;
                if (preview) output = owner.CreatePreviewMeshFromInput(source);
                else output = owner.Deform(false);
                Assert.That(workspace.Lattice.HasNativeResources, Is.True,
                    "Active components retain their reusable native cache.");
                owner.enabled = false;
                Assert.That(workspace.Lattice.HasNativeResources, Is.False);
            }
            finally
            {
                if (output != null) Object.DestroyImmediate(output);
                if (owner != null)
                    ((EvaluationWorkspace)typeof(LatticeDeformer).GetField("_evaluationWorkspace",
                        BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner)).Dispose();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(source);
            }
        }
    }
}
#endif
