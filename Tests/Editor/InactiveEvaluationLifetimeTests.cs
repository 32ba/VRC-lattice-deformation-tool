#if UNITY_EDITOR
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class InactiveEvaluationLifetimeTests
    {
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
