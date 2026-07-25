#if UNITY_EDITOR
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

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
        public void ShouldProcessDeformer_WhenInspectorEnabledCheckboxIsUnchecked_ReturnsFalse()
        {
            var go = new GameObject("InspectorDisabledComponentDeformer");
            try
            {
                var deformer = go.AddComponent<LatticeDeformer>();
                var serializedDeformer = new SerializedObject(deformer);
                serializedDeformer.FindProperty("m_Enabled").boolValue = false;
                serializedDeformer.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(deformer.enabled, Is.False);
                Assert.That(LatticeDeformerBakePass.ShouldProcessDeformer(deformer), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ShouldProcessDeformer_WhenGameObjectIsInactive_ReturnsTrue()
        {
            var go = new GameObject("InactiveObjectDeformer");
            try
            {
                var deformer = go.AddComponent<LatticeDeformer>();
                go.SetActive(false);

                Assert.That(LatticeDeformerBakePass.ShouldProcessDeformer(deformer), Is.True);
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

        [Test]
        public void ValidateAllBeforeBake_WhenLaterDeformerIsInvalid_RejectsEntireSet()
        {
            var validObject = new GameObject("Valid First Deformer");
            var invalidObject = new GameObject("Invalid Second Deformer");
            var mesh = new Mesh();
            try
            {
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one };
                mesh.triangles = new[] { 0, 1, 2, 1, 3, 2 };
                mesh.RecalculateBounds();

                var filter = validObject.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                validObject.AddComponent<MeshRenderer>();
                var valid = validObject.AddComponent<LatticeDeformer>();
                valid.Reset();

                invalidObject.AddComponent<MeshFilter>();
                invalidObject.AddComponent<MeshRenderer>();
                var invalid = invalidObject.AddComponent<LatticeDeformer>();

                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[MDV002\\]"));
                bool result = LatticeDeformerBakePass.ValidateAllBeforeBake(
                    new[] { valid, invalid },
                    out var firstInvalid);

                Assert.That(result, Is.False);
                Assert.That(firstInvalid, Is.SameAs(invalid));
                Assert.That(filter.sharedMesh, Is.SameAs(mesh),
                    "Preflight must not replace an earlier renderer mesh.");
                Assert.That(valid.RuntimeMesh, Is.Null,
                    "Preflight must not instantiate an earlier runtime mesh.");
            }
            finally
            {
                Object.DestroyImmediate(validObject);
                Object.DestroyImmediate(invalidObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void ValidateAllBeforeBake_IgnoresDisabledInvalidDeformer()
        {
            var invalidObject = new GameObject("Disabled Invalid Deformer");
            try
            {
                var invalid = invalidObject.AddComponent<LatticeDeformer>();
                invalid.enabled = false;

                Assert.That(
                    LatticeDeformerBakePass.ValidateAllBeforeBake(
                        new[] { invalid },
                        out var firstInvalid),
                    Is.True);
                Assert.That(firstInvalid, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(invalidObject);
            }
        }
    }
}
#endif
