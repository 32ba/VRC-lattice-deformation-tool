#if UNITY_EDITOR
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class LatticeDeformerBakePassTests
    {
        [Test]
        public void ShouldProcessDeformer_WhenComponentCheckboxIsOff_ReturnsFalse()
        {
            var go = new GameObject("DisabledComponentDeformer");
            try
            {
                var deformer = go.AddComponent<LatticeDeformer>();
                deformer.enabled = false;

                Assert.That(LatticeDeformerBakePass.ShouldProcessDeformer(deformer), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ShouldProcessDeformer_WhenGameObjectIsInactive_ReturnsFalse()
        {
            var go = new GameObject("InactiveObjectDeformer");
            try
            {
                var deformer = go.AddComponent<LatticeDeformer>();
                go.SetActive(false);

                Assert.That(LatticeDeformerBakePass.ShouldProcessDeformer(deformer), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ShouldProcessDeformer_WhenActiveAndEnabled_ReturnsTrue()
        {
            var go = new GameObject("ActiveDeformer");
            try
            {
                var deformer = go.AddComponent<LatticeDeformer>();

                Assert.That(LatticeDeformerBakePass.ShouldProcessDeformer(deformer), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
#endif
