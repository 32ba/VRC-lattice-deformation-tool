#if UNITY_EDITOR
using System;
using System.IO;
using NUnit.Framework;
using Net._32Ba.LatticeDeformationTool.Editor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class MeshDeformerSupportReportTests
    {
        [Test]
        public void Generate_IncludesActionableStateWithoutLocalFilesystemIdentity()
        {
            var avatar = new GameObject("Support Avatar");
            var outfit = new GameObject("Support Outfit");
            var meshObject = new GameObject("Support Mesh");
            var bone = new GameObject("Support Bone");
            Mesh source = CreateSourceMesh();
            try
            {
                outfit.transform.SetParent(avatar.transform, false);
                meshObject.transform.SetParent(outfit.transform, false);
                bone.transform.SetParent(avatar.transform, false);
                meshObject.transform.localPosition = new Vector3(0.1f, 0.2f, -0.3f);
                outfit.transform.localScale = new Vector3(0.8f, 1.2f, 0.9f);
                var renderer = meshObject.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                renderer.bones = new[] { bone.transform };
                renderer.rootBone = bone.transform;
                renderer.SetBlendShapeWeight(0, 73f);
                var deformer = meshObject.AddComponent<LatticeDeformer>();
                deformer.Reset();
                renderer.SetBlendShapeWeight(0, 42f);

                string encoded = MeshDeformerSupportReport.Generate(deformer);
                string report = MeshDeformerSupportReport.Decode(encoded);

                StringAssert.StartsWith(MeshDeformerSupportReport.EnvelopePrefix, encoded);
                Assert.That(encoded, Does.Not.Contain("Support Avatar"));
                Assert.That(encoded, Does.Not.Contain("Support Shape"));
                Assert.That(encoded.Length, Is.LessThan(report.Length));
                StringAssert.StartsWith("{\"report\":\"Mesh Deformer Support Report\"", report);
                StringAssert.Contains("\"format-version\":\"1\"", report);
                StringAssert.Contains("\"unity-version\":\"" + Application.unityVersion + "\"", report);
                StringAssert.Contains("\"net.32ba.lattice-deformation-tool\":", report);
                StringAssert.Contains("\"hierarchy\":\"Support Avatar/Support Outfit/Support Mesh\"", report);
                StringAssert.Contains("\"source-mesh.vertices\":\"4\"", report);
                StringAssert.Contains("\"source-mesh.blend-shapes\":\"1\"", report);
                StringAssert.Contains("\"shape[0]\":\"name=Support Shape, current=42, initial=73, frames=1\"", report);
                StringAssert.Contains("\"group-count\":\"1\"", report);
                StringAssert.Contains("grid=(3,3,3)", report);
                StringAssert.Contains("\"preview-aligned-cage\":", report);
                StringAssert.Contains("\"validation\":{", report);
                Assert.That(report, Does.Not.Contain(Application.dataPath));
                Assert.That(report, Does.Not.Contain(Environment.UserName));
                Assert.That(report, Does.Not.Contain("Assets/"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(avatar);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void Generate_IncludesRegisteredPreviewProxyTopology()
        {
            var originalObject = new GameObject("Original Renderer");
            var proxyObject = new GameObject("Preview Proxy");
            Mesh source = CreateSourceMesh();
            Mesh proxyMesh = CreateProxyMesh();
            try
            {
                var original = originalObject.AddComponent<SkinnedMeshRenderer>();
                original.sharedMesh = source;
                var deformer = originalObject.AddComponent<LatticeDeformer>();
                deformer.Reset();
                var proxy = proxyObject.AddComponent<SkinnedMeshRenderer>();
                proxy.sharedMesh = proxyMesh;
                LatticePreviewUtility.RegisterProxy(original, proxy);
                LatticePreviewUtility.RegisterPreviewMesh(original, proxyMesh);

                string report = MeshDeformerSupportReport.Decode(
                    MeshDeformerSupportReport.Generate(deformer));

                StringAssert.Contains("\"proxy-present\":\"True\"", report);
                StringAssert.Contains("\"proxy-hierarchy\":\"Preview Proxy\"", report);
                StringAssert.Contains("\"proxy-mesh.vertices\":\"5\"", report);
                StringAssert.Contains("\"registered-preview-mesh\":\"True\"", report);
            }
            finally
            {
                LatticePreviewUtility.ClearProxy(originalObject.GetComponent<Renderer>());
                UnityEngine.Object.DestroyImmediate(originalObject);
                UnityEngine.Object.DestroyImmediate(proxyObject);
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(proxyMesh);
            }
        }

        [Test]
        public void Generate_NullComponentReturnsPasteablePartialReport()
        {
            string encoded = null;
            Assert.DoesNotThrow(() => encoded = MeshDeformerSupportReport.Generate(null));
            string report = MeshDeformerSupportReport.Decode(encoded);
            StringAssert.Contains("\"packages\":{", report);
            StringAssert.Contains("\"component\":{", report);
            StringAssert.Contains("\"present\":\"False\"", report);
        }

        [Test]
        public void Decode_RejectsModifiedPayload()
        {
            string encoded = MeshDeformerSupportReport.Generate(null);
            int checksumIndex = MeshDeformerSupportReport.EnvelopePrefix.Length;
            char replacement = encoded[checksumIndex] == '0' ? '1' : '0';
            string modified = encoded.Substring(0, checksumIndex) + replacement +
                              encoded.Substring(checksumIndex + 1);

            Assert.Throws<InvalidDataException>(() => MeshDeformerSupportReport.Decode(modified));
        }

        private static Mesh CreateSourceMesh()
        {
            var mesh = new Mesh
            {
                name = "Support Source Mesh",
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f),
                    new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(-0.5f, 0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0f),
                },
                triangles = new[] { 0, 2, 1, 1, 2, 3 },
                boneWeights = new[]
                {
                    OneBone(), OneBone(), OneBone(), OneBone(),
                },
                bindposes = new[] { Matrix4x4.identity },
            };
            mesh.AddBlendShapeFrame(
                "Support Shape",
                100f,
                new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up },
                null,
                null);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateProxyMesh()
        {
            var mesh = new Mesh
            {
                name = "Support Proxy Mesh",
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f),
                    new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(-0.5f, 0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0f),
                    Vector3.zero,
                },
                triangles = new[] { 0, 2, 1, 1, 2, 3 },
                boneWeights = new[]
                {
                    OneBone(), OneBone(), OneBone(), OneBone(), OneBone(),
                },
                bindposes = new[] { Matrix4x4.identity },
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static BoneWeight OneBone() =>
            new BoneWeight { boneIndex0 = 0, weight0 = 1f };
    }
}
#endif
