#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using nadena.dev.ndmf.preview;
using NUnit.Framework;
using Net._32Ba.LatticeDeformationTool.Editor;
using UnityEditor;
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

                Assert.That(encoded, Does.Match("^[A-Za-z0-9_-]+$"));
                Assert.That(encoded, Does.Not.Contain("LDT-SUPPORT"));
                Assert.That(encoded, Does.Not.Contain("json"));
                Assert.That(encoded, Does.Not.Contain("gzip"));
                Assert.That(encoded, Does.Not.Contain("sha256"));
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
                StringAssert.Contains("\"active-self\":\"True\"", report);
                StringAssert.Contains("\"active-in-hierarchy\":\"True\"", report);
                StringAssert.Contains("\"shape[0]\":\"name=Support Shape, current=42, initial=73, frames=1\"", report);
                StringAssert.Contains("\"group-count\":\"1\"", report);
                StringAssert.Contains("grid=(3,3,3)", report);
                StringAssert.Contains("\"components\":{", report);
                StringAssert.Contains("type=UnityEngine.SkinnedMeshRenderer, enabled=True", report);
                StringAssert.Contains("type=Net._32Ba.LatticeDeformationTool.LatticeDeformer, enabled=True", report);
                StringAssert.Contains("\"object-toggles\":{", report);
                StringAssert.Contains("\"editor-state\":{", report);
                StringAssert.Contains("\"selected-object\":", report);
                StringAssert.Contains("\"active-tool\":", report);
                StringAssert.Contains("\"brush-sub-mode\":", report);
                StringAssert.Contains("\"preview-aligned-cage\":", report);
                StringAssert.Contains("\"filter-order-available\":", report);
                StringAssert.Contains("\"filter-count\":", report);
                StringAssert.Contains("\"validation\":{", report);
                Assert.That(report, Does.Not.Contain(Application.dataPath));
                string userPathSegment = Path.DirectorySeparatorChar +
                                         Environment.UserName +
                                         Path.DirectorySeparatorChar;
                Assert.That(report, Does.Not.Contain(userPathSegment));
                Assert.That(report, Does.Not.Contain("Assets/"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(avatar);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void Generate_ReportsModularAvatarObjectToggleEntriesWhenAvailable()
        {
            Type toggleType = FindType("nadena.dev.modular_avatar.core.ModularAvatarObjectToggle");
            if (toggleType == null)
                Assert.Ignore("Modular Avatar is not installed in this test project.");

            var avatar = new GameObject("Toggle Avatar");
            var toggleOwner = new GameObject("Toggle Owner");
            var meshObject = new GameObject("Toggle Target");
            Mesh source = CreateSourceMesh();
            try
            {
                toggleOwner.transform.SetParent(avatar.transform, false);
                meshObject.transform.SetParent(avatar.transform, false);
                var renderer = meshObject.AddComponent<MeshRenderer>();
                var filter = meshObject.AddComponent<MeshFilter>();
                filter.sharedMesh = source;
                var deformer = meshObject.AddComponent<LatticeDeformer>();
                deformer.Reset();

                Component toggle = toggleOwner.AddComponent(toggleType);
                var serialized = new SerializedObject(toggle);
                SerializedProperty objects = serialized.FindProperty("m_objects");
                Assert.That(objects, Is.Not.Null);
                objects.arraySize = 1;
                SerializedProperty entry = objects.GetArrayElementAtIndex(0);
                entry.FindPropertyRelative("Active").boolValue = false;
                SerializedProperty reference = entry.FindPropertyRelative("Object");
                reference.FindPropertyRelative("targetObject").objectReferenceValue = meshObject;
                reference.FindPropertyRelative("referencePath").stringValue = "Toggle Target";
                serialized.ApplyModifiedPropertiesWithoutUndo();

                string report = MeshDeformerSupportReport.Decode(
                    MeshDeformerSupportReport.Generate(deformer));

                StringAssert.Contains("\"object-toggles\":{", report);
                StringAssert.Contains("owner=Toggle Avatar/Toggle Owner", report);
                StringAssert.Contains("target=Toggle Avatar/Toggle Target", report);
                StringAssert.Contains("active=False", report);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(avatar);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void Generate_ReportsNdmfPreviewFiltersInExecutionOrder()
        {
            PreviewSession previous = PreviewSession.Current;
            var session = new PreviewSession();
            var root = new GameObject("Filter Order Target");
            Mesh source = CreateSourceMesh();
            IDisposable registration = null;
            try
            {
                var renderer = root.AddComponent<MeshRenderer>();
                var filter = root.AddComponent<MeshFilter>();
                filter.sharedMesh = source;
                var deformer = root.AddComponent<LatticeDeformer>();
                deformer.Reset();
                registration = session.AddMutator(
                    new SequencePoint { DebugString = "support-report-test" },
                    new SupportReportPreviewFilter());
                PreviewSession.Current = session;

                string report = MeshDeformerSupportReport.Decode(
                    MeshDeformerSupportReport.Generate(deformer));

                StringAssert.Contains("\"filter-order-available\":\"True\"", report);
                StringAssert.Contains("\"filter-count\":\"1\"", report);
                StringAssert.Contains(typeof(SupportReportPreviewFilter).FullName, report);
            }
            finally
            {
                PreviewSession.Current = previous;
                registration?.Dispose();
                session.Dispose();
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void Generate_IgnoresLegacyIndependentPreviewRegistration()
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

                StringAssert.Contains("\"proxy-present\":\"False\"", report);
                Assert.That(report, Does.Not.Contain("\"proxy-hierarchy\":\"Preview Proxy\""),
                    "Support data and Scene tools must use only NDMF's active primary proxy, " +
                    "not the removed independent preview registry.");
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
            const int encodedChecksumIndex = 12;
            char replacement = encoded[encodedChecksumIndex] == 'A' ? 'B' : 'A';
            string modified = encoded.Substring(0, encodedChecksumIndex) + replacement +
                              encoded.Substring(encodedChecksumIndex + 1);

            Assert.Throws<InvalidDataException>(() => MeshDeformerSupportReport.Decode(modified));
        }

        [Test]
        public void Png_RoundTripsAndRejectsModifiedPixels()
        {
            byte[] png = MeshDeformerSupportReport.GeneratePng(null);
            TestContext.Out.WriteLine($"support-png-bytes={png.Length}");
            Assert.That(png.Length, Is.LessThanOrEqualTo(MeshDeformerSupportReport.MaximumAttachmentBytes));
            StringAssert.Contains("\"present\":\"False\"", MeshDeformerSupportReport.DecodePng(png));

            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false, true);
            try
            {
                Assert.That(ImageConversion.LoadImage(texture, png, false), Is.True);
                Color32[] pixels = texture.GetPixels32();
                pixels[8].r ^= 0x20;
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                byte[] modified = texture.EncodeToPNG();
                Assert.Throws<InvalidDataException>(() => MeshDeformerSupportReport.DecodePng(modified));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void Generate_DoesNotCanonicalizeMalformedSerializedStack()
        {
            var root = new GameObject("Raw Support State");
            Mesh source = CreateSourceMesh();
            try
            {
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                var deformer = root.AddComponent<LatticeDeformer>();
                deformer.Reset();
                var serialized = new UnityEditor.SerializedObject(deformer);
                serialized.FindProperty("_groups").arraySize = 0;
                serialized.FindProperty("_activeGroupIndex").intValue = 7;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                string before = EditorJsonUtility.ToJson(deformer);

                MeshDeformerSupportReport.GeneratePng(deformer);

                Assert.That(EditorJsonUtility.ToJson(deformer), Is.EqualTo(before));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(source);
            }
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

        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }

        private sealed class SupportReportPreviewFilter : IRenderFilter
        {
            public ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context) =>
                ImmutableList<RenderGroup>.Empty;

            public Task<IRenderFilterNode> Instantiate(
                RenderGroup group,
                IEnumerable<(Renderer, Renderer)> proxyPairs,
                ComputeContext context) => Task.FromResult<IRenderFilterNode>(null);
        }
    }
}
#endif
