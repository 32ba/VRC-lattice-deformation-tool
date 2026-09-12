#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Net._32Ba.LatticeDeformationTool.Editor;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class PeripheralInspectorTests
    {
        private static string FixturePath(string name) => Path.Combine(
            UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LatticeDeformer).Assembly).resolvedPath,
            "Tests/Editor/Fixtures/SupportCodecV1", name);

        [TestCase(false)]
        [TestCase(true)]
        public void Codec_DecodesOriginalImplementationOutput(bool png)
        {
            string actual = png ? SupportReportCodec.DecodePng(File.ReadAllBytes(FixturePath("image.bytes")))
                : SupportReportCodec.Decode(File.ReadAllText(FixturePath("encoded.txt")));
            Assert.That(actual, Is.EqualTo(File.ReadAllText(FixturePath(png ? "expected-png.txt" : "expected-decoded.txt"))));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Codec_EncodesFixedPlainTextWithOriginalJsonRules(bool png)
        {
            string input = File.ReadAllText(FixturePath("plain.txt"));
            int textures = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
            string actual = png ? SupportReportCodec.DecodePng(SupportReportCodec.GeneratePng(input))
                : SupportReportCodec.Decode(SupportReportCodec.Encode(input));
            Assert.That(actual, Is.EqualTo(File.ReadAllText(FixturePath("expected-json.txt"))));
            Assert.That(Resources.FindObjectsOfTypeAll<Texture2D>().Length, Is.EqualTo(textures));
        }

        [Test]
        public void Codec_RejectsOversizeDecodedReportBeforeCreatingTexture()
        {
            int textures = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
            string input = "Report\nlarge=" + new string('x', 2 * 1024 * 1024);
            Assert.Throws<InvalidDataException>(() => SupportReportCodec.GeneratePng(input));
            Assert.That(Resources.FindObjectsOfTypeAll<Texture2D>().Length, Is.EqualTo(textures));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SupportCollector_ObservesNullGroupWithoutRepairingIt(bool profileMode)
        {
            using var f = new Fixture();
            var profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            try
            {
                if (profileMode) { f.Target.SaveToProfile(profile); f.Target.UseProfile(profile); }
                var payload = new List<DeformerGroup> { null };
                object owner = profileMode ? (object)profile : f.Target;
                owner.GetType().GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, payload);
                int dirty = EditorUtility.GetDirtyCount(f.Target), profileDirty = EditorUtility.GetDirtyCount(profile);
                Mesh runtime = f.Target.RuntimeMesh;
                string plain = SupportReportCollector.Collect(f.Target);
                // Format v1 reports the component's embedded stack. A referenced
                // Profile is described by presence and validation diagnostics.
                Assert.That(plain, Does.Contain(profileMode ? "group-count=0" : "group[0]=null"));
                if (profileMode) Assert.That(plain, Does.Contain(MeshDeformerValidator.NullGroupOrLayer));
                Assert.That(payload[0], Is.Null);
                Assert.That(SerializedDeformerReader.Read(f.Target).Groups, Is.SameAs(payload));
                Assert.That(EditorUtility.GetDirtyCount(f.Target), Is.EqualTo(dirty));
                Assert.That(EditorUtility.GetDirtyCount(profile), Is.EqualTo(profileDirty));
                Assert.That(f.Target.SourceMesh, Is.SameAs(f.Mesh));
                Assert.That(f.Target.RuntimeMesh, Is.SameAs(runtime));
                Assert.That(f.Target.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(f.Mesh));
            }
            finally { Object.DestroyImmediate(profile); }
        }

        [TestCase("replace")]
        [TestCase("readonly")]
        [TestCase("directory")]
        public void SupportFileWrite_IsAtomicAndCleansOnlyItsTemporaryFile(string scenario)
        {
            string folder = Path.Combine(Path.GetTempPath(), "LatticeSupportFiles-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "report.png"), other = Path.Combine(folder, "unrelated.txt");
            byte[] original = { 1, 2, 3 }, replacement = { 5, 6, 7, 8 };
            File.WriteAllBytes(other, original);
            try
            {
                if (scenario == "directory") Directory.CreateDirectory(path);
                else File.WriteAllBytes(path, original);
                if (scenario == "readonly") File.SetAttributes(path, FileAttributes.ReadOnly);
                if (scenario == "replace")
                {
                    SupportReportFiles.Write(path, replacement);
                    Assert.That(File.ReadAllBytes(path), Is.EqualTo(replacement));
                }
                else
                {
                    var error = Assert.Catch(() => SupportReportFiles.Write(path, replacement));
                    Assert.That(error is IOException || error is UnauthorizedAccessException, Is.True);
                    if (scenario == "readonly") Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
                    else Assert.That(Directory.Exists(path), Is.True);
                }
                Assert.That(File.ReadAllBytes(other), Is.EqualTo(original));
                Assert.That(Directory.GetFiles(folder).Length, Is.EqualTo(scenario == "directory" ? 1 : 2));
            }
            finally
            {
                if (File.Exists(path)) { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); }
                if (Directory.Exists(path)) Directory.Delete(path);
                File.Delete(other); Directory.Delete(folder);
            }
        }

        [Test]
        public void ValidationState_KeepsOwnersIndependentAndRefreshesOnExternalMutation()
        {
            using var a = new Fixture(); using var b = new Fixture();
            var first = new InspectorValidationState(); var second = new InspectorValidationState();
            var cachedA = first.Read(a.Target); var cachedB = second.Read(b.Target);
            string beforeA = EditorJsonUtility.ToJson(a.Target), beforeB = EditorJsonUtility.ToJson(b.Target);
            Assert.That(first.Read(a.Target), Is.SameAs(cachedA));
            Assert.That(second.Read(b.Target), Is.SameAs(cachedB));
            b.Target.Layers[1].BrushDisplacements = new Vector3[1];
            Assert.That(second.Read(b.Target).Any(d => d.Code == MeshDeformerValidator.BrushLengthMismatch), Is.True);
            Assert.That(first.Read(a.Target), Is.SameAs(cachedA));
            Assert.That(EditorJsonUtility.ToJson(a.Target), Is.EqualTo(beforeA));
            Assert.That(EditorJsonUtility.ToJson(b.Target), Is.Not.EqualTo(beforeB));
            Assert.That(b.Target.Layers[1].BrushDisplacementCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator RebuildInspector_PaintPreservesMixedValuesAndUnknownEnumWithoutUndoOrDirtyChanges()
        {
            using var a = new Fixture(); using var b = new Fixture();
            using var first = new SerializedObject(a.Target); using var second = new SerializedObject(b.Target);
            foreach (string name in new[] { "_recalculateNormals", "_recalculateTangents", "_recalculateBounds" })
            { first.FindProperty(name).boolValue = true; second.FindProperty(name).boolValue = false; }
            first.FindProperty("_normalsRecalculationMode").intValue = 999;
            first.ApplyModifiedPropertiesWithoutUndo(); second.ApplyModifiedPropertiesWithoutUndo();
            string beforeA = EditorJsonUtility.ToJson(a.Target), beforeB = EditorJsonUtility.ToJson(b.Target);
            int dirtyA = EditorUtility.GetDirtyCount(a.Target), dirtyB = EditorUtility.GetDirtyCount(b.Target);
            using var both = new SerializedObject(new Object[] { a.Target, b.Target });
            var section = new MeshRebuildInspectorSection(both);
            var expanded = typeof(MeshRebuildInspectorSection).GetField("s_showOptions", BindingFlags.Static | BindingFlags.NonPublic);
            bool previous = (bool)expanded.GetValue(null); expanded.SetValue(null, true);
            var window = ScriptableObject.CreateInstance<RebuildTestWindow>();
            Exception failure = null; int paints = 0;
            window.Draw = () =>
            {
                try
                {
                    int undo = Undo.GetCurrentGroup();
                    both.Update(); section.Draw(); both.ApplyModifiedProperties();
                    Assert.That(Undo.GetCurrentGroup(), Is.EqualTo(undo));
                    if (Event.current.type == EventType.Repaint) paints++;
                }
                catch (Exception exception) { failure = exception; }
            };
            try
            {
                window.position = new Rect(120, 160, 650, 440); window.ShowUtility();
                double deadline = EditorApplication.timeSinceStartup + 5;
                while (paints < 3 && failure == null && EditorApplication.timeSinceStartup < deadline)
                { window.Repaint(); yield return null; }
                Assert.That(failure, Is.Null);
                Assert.That(paints, Is.GreaterThanOrEqualTo(3));
                Assert.That(EditorJsonUtility.ToJson(a.Target), Is.EqualTo(beforeA));
                Assert.That(EditorJsonUtility.ToJson(b.Target), Is.EqualTo(beforeB));
                Assert.That(EditorUtility.GetDirtyCount(a.Target), Is.EqualTo(dirtyA));
                Assert.That(EditorUtility.GetDirtyCount(b.Target), Is.EqualTo(dirtyB));
            }
            finally { window.Draw = null; window.Close(); expanded.SetValue(null, previous); }
        }

        private sealed class RebuildTestWindow : EditorWindow
        {
            internal Action Draw;
            private void OnGUI() => Draw?.Invoke();
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly Mesh Mesh;
            internal readonly LatticeDeformer Target;
            internal Fixture()
            {
                Mesh = new Mesh { name = "Peripheral source", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                Mesh.RecalculateNormals(); Mesh.RecalculateBounds();
                var go = new GameObject("Peripheral authoring"); go.SetActive(false);
                go.AddComponent<MeshFilter>().sharedMesh = Mesh; go.AddComponent<MeshRenderer>();
                Target = go.AddComponent<LatticeDeformer>(); Target.Reset(); Target.AddLayer("Brush", MeshDeformerLayerType.Brush); Target.Deform(false);
            }
            public void Dispose()
            {
                // Never-active objects do not receive Unity's OnDestroy callback.
                Undo.ClearUndo(Target);
                Target.InvalidateCache();
                Target.RestoreOriginalMesh();
                Object.DestroyImmediate(Target.gameObject);
                Object.DestroyImmediate(Mesh);
            }
        }
    }
}
#endif
