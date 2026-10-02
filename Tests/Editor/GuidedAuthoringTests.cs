#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
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
    public sealed class GuidedAuthoringTests
    {
        [TestCase("future")]
        [TestCase("null-groups")]
        [TestCase("null-group")]
        [TestCase("selection")]
        [TestCase("missing-profile")]
        [TestCase("profile")]
        [TestCase("short-brush")]
        [TestCase("disabled-nonfinite")]
        [TestCase("topology")]
        [TestCase("source-replaced")]
        [TestCase("missing-source")]
        [TestCase("unknown-type")]
        public void RejectedStartAndDisplay_PreservePayloadDirtyUndoAndMeshes(string fault)
        {
            using var f = new Fixture();
            Mesh replacement = null;
            var profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            try
            {
                var type = MeshDeformerLayerType.Brush;
                switch (fault)
                {
                    case "future": Set(f.Target, "_migrationReleaseIndex", int.MaxValue); break;
                    case "null-groups": Set(f.Target, "_groups", null); break;
                    case "null-group": Set(f.Target, "_groups", new List<DeformerGroup> { null }); break;
                    case "selection": Set(f.Target, "_activeGroupIndex", -1); break;
                    case "missing-profile": Set(f.Target, "_dataSource", DeformerDataSource.Profile); break;
                    case "profile":
                        Assert.That(f.Target.SaveToProfile(profile), Is.True);
                        Assert.That(f.Target.UseProfile(profile), Is.True);
                        break;
                    case "short-brush":
                        f.Target.AddLayer(layerType: MeshDeformerLayerType.Brush);
                        f.Target.Layers[1].BrushDisplacements = new Vector3[1];
                        break;
                    case "disabled-nonfinite":
                        f.Target.AddLayer(layerType: MeshDeformerLayerType.Brush);
                        f.Target.Layers[1].BrushDisplacements = new[] { new Vector3(float.NaN, 0, 0), Vector3.zero, Vector3.zero };
                        break;
                    case "topology": f.Mesh.triangles = new[] { 0, 2, 1 }; break;
                    case "source-replaced": replacement = Object.Instantiate(f.Mesh); f.Filter.sharedMesh = replacement; break;
                    case "missing-source": f.Filter.sharedMesh = null; break;
                    case "unknown-type": type = (MeshDeformerLayerType)999; break;
                }
                // Unity JSON serialization materializes a null inline Group. For
                // that corrupt case compare the raw list/slot instead of repairing
                // the input while taking the test's before snapshot.
                string before = fault == "null-group" ? null : EditorJsonUtility.ToJson(f.Target);
                string profileBefore = EditorJsonUtility.ToJson(profile);
                int dirty = EditorUtility.GetDirtyCount(f.Target), undo = Undo.GetCurrentGroup();
                var groups = SerializedDeformerReader.Read(f.Target).EmbeddedGroups;
                Mesh runtime = f.Target.RuntimeMesh, displayed = f.Filter.sharedMesh;
                var vertices = f.Mesh.vertices;
                for (int i = 0; i < 3; i++) GuidedInspectorState.Read(f.Target);
                Assert.That(GuidedAuthoringService.EnsureLayer(f.Target, type, "Rejected guided start"), Is.EqualTo(-1));
                if (before != null) Assert.That(EditorJsonUtility.ToJson(f.Target), Is.EqualTo(before));
                else Assert.That(SerializedDeformerReader.Read(f.Target).EmbeddedGroups[0], Is.Null);
                Assert.That(EditorJsonUtility.ToJson(profile), Is.EqualTo(profileBefore));
                Assert.That(SerializedDeformerReader.Read(f.Target).EmbeddedGroups, Is.SameAs(groups));
                Assert.That(EditorUtility.GetDirtyCount(f.Target), Is.EqualTo(dirty));
                Assert.That(Undo.GetCurrentGroup(), Is.EqualTo(undo));
                Assert.That(f.Target.RuntimeMesh, Is.SameAs(runtime));
                Assert.That(f.Filter.sharedMesh, Is.SameAs(displayed));
                Assert.That(f.Mesh.vertices, Is.EqualTo(vertices));
            }
            finally { Object.DestroyImmediate(replacement); Object.DestroyImmediate(profile); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Start_SelectsOrCreatesWithOneUndoAndRedo(bool existing)
        {
            using var f = new Fixture();
            if (existing)
            {
                f.Target.AddLayer("Existing brush", MeshDeformerLayerType.Brush);
                f.Target.Layers[1].BrushDisplacements = new[] { Vector3.forward, Vector3.zero, Vector3.zero };
                f.Target.ActiveLayerIndex = 0;
            }
            string before = EditorJsonUtility.ToJson(f.Target);
            Assert.That(GuidedAuthoringService.EnsureLayer(f.Target, MeshDeformerLayerType.Brush, "Guided start"), Is.EqualTo(1));
            string after = EditorJsonUtility.ToJson(f.Target);
            Assert.That(f.Target.Layers.Count, Is.EqualTo(2));
            Assert.That(f.Target.ActiveLayerIndex, Is.EqualTo(1));
            Assert.That(f.Target.Layers[1].BrushDisplacements[0], Is.EqualTo(existing ? Vector3.forward : Vector3.zero));
            Undo.PerformUndo();
            Assert.That(EditorJsonUtility.ToJson(f.Target), Is.EqualTo(before));
            Undo.PerformRedo();
            Assert.That(EditorJsonUtility.ToJson(f.Target), Is.EqualTo(after));
            Assert.That(f.Mesh.vertices, Is.EqualTo(Fixture.Vertices));
        }

        [Test]
        public void ReusingActiveLayer_DoesNotWriteOrCreateAnUndoRecord()
        {
            using var f = new Fixture();
            string before = EditorJsonUtility.ToJson(f.Target);
            int undo = Undo.GetCurrentGroup(), dirty = EditorUtility.GetDirtyCount(f.Target);
            Assert.That(GuidedAuthoringService.EnsureLayer(f.Target, MeshDeformerLayerType.Lattice, "Reuse"), Is.Zero);
            Assert.That(EditorJsonUtility.ToJson(f.Target), Is.EqualTo(before));
            Assert.That(Undo.GetCurrentGroup(), Is.EqualTo(undo));
            Assert.That(EditorUtility.GetDirtyCount(f.Target), Is.EqualTo(dirty));
        }

        [Test]
        public void Display_DoesNotRetainBorrowedLayersAndMissingProfileRemainsReadOnly()
        {
            using var f = new Fixture();
            var first = GuidedInspectorState.Read(f.Target);
            f.Target.Layers[0].Name = "New name";
            var second = GuidedInspectorState.Read(f.Target);
            Assert.That(first.ActiveLayerName, Is.EqualTo("Lattice Layer"));
            Assert.That(second.ActiveLayerName, Is.EqualTo("New name"));
            Set(f.Target, "_dataSource", DeformerDataSource.Profile);
            var missing = GuidedInspectorState.Read(f.Target);
            Assert.That(missing.IsProfile, Is.True);
            Assert.That(missing.ActiveLayerName, Is.Null);
        }

        [Test]
        public void DestroyedInspector_DoesNotInvokeCallbacksOrChangeToolState()
        {
            using var f = new Fixture();
            var inspector = UnityEditor.Editor.CreateEditor(f.Target, typeof(LatticeDeformerEditor));
            var section = new GuidedInspectorSection(inspector, Fail, Fail, Fail, Fail);
            Object.DestroyImmediate(inspector);
            var mode = MeshDeformerTool.CurrentBrushSubMode;
            section.Draw();
            Assert.That(section.StartEditing(GuidedEditingIntent.MoveVertices), Is.False);
            Assert.That(MeshDeformerTool.CurrentBrushSubMode, Is.EqualTo(mode));
        }

        [Test]
        public void RejectedUiStart_DoesNotActivateToolOrRefreshPreview()
        {
            using var f = new Fixture();
            var inspector = UnityEditor.Editor.CreateEditor(f.Target, typeof(LatticeDeformerEditor));
            try
            {
                Set(f.Target, "_groups", null);
                string before = EditorJsonUtility.ToJson(f.Target);
                var section = new GuidedInspectorSection(inspector, Fail, Fail, Fail, Fail);
                var mode = MeshDeformerTool.CurrentBrushSubMode;
                var tool = UnityEditor.EditorTools.ToolManager.activeToolType;
                Assert.That(section.StartEditing(GuidedEditingIntent.SculptSurface), Is.False);
                Assert.That(section.StartFailed, Is.True);
                Assert.That(EditorJsonUtility.ToJson(f.Target), Is.EqualTo(before));
                Assert.That(MeshDeformerTool.CurrentBrushSubMode, Is.EqualTo(mode));
                Assert.That(UnityEditor.EditorTools.ToolManager.activeToolType, Is.EqualTo(tool));
            }
            finally { Object.DestroyImmediate(inspector); }
        }

        [UnityTest]
        public IEnumerator GuidedPaint_AllLanguagesPreserveNullPayloadWithoutInitializingIt()
        {
            using var f = new Fixture();
            var inspector = (LatticeDeformerEditor)UnityEditor.Editor.CreateEditor(f.Target, typeof(LatticeDeformerEditor));
            var window = ScriptableObject.CreateInstance<GuidedTestWindow>();
            var language = LatticeLocalization.CurrentLanguage;
            Set(f.Target, "_groups", null);
            string before = EditorJsonUtility.ToJson(f.Target);
            int dirty = EditorUtility.GetDirtyCount(f.Target);
            Exception failure = null; int paints = 0;
            window.Draw = () =>
            {
                try
                {
                    int undo = Undo.GetCurrentGroup();
                    inspector.GuidedInspector.Draw();
                    Assert.That(Undo.GetCurrentGroup(), Is.EqualTo(undo));
                    if (Event.current.type == EventType.Repaint) paints++;
                }
                catch (Exception ex) { failure = ex; }
            };
            try
            {
                window.position = new Rect(120, 160, 600, 550); window.ShowUtility();
                foreach (var current in Enum.GetValues(typeof(LatticeLocalization.Language)).Cast<LatticeLocalization.Language>())
                {
                    LatticeLocalization.CurrentLanguage = current;
                    int start = paints;
                    double deadline = EditorApplication.timeSinceStartup + 5;
                    while (paints < start + 2 && failure == null && EditorApplication.timeSinceStartup < deadline)
                    { window.Repaint(); yield return null; }
                    Assert.That(failure, Is.Null);
                    Assert.That(paints, Is.GreaterThanOrEqualTo(start + 2), current.ToString());
                    Assert.That(EditorJsonUtility.ToJson(f.Target), Is.EqualTo(before));
                    Assert.That(SerializedDeformerReader.Read(f.Target).EmbeddedGroups, Is.Null);
                    Assert.That(EditorUtility.GetDirtyCount(f.Target), Is.EqualTo(dirty));
                }
            }
            finally
            {
                window.Draw = null; window.Close();
                LatticeLocalization.CurrentLanguage = language;
                Object.DestroyImmediate(inspector);
            }
        }

        [Test]
        public void InactivePrefabVariant_StartUndoRedoApplyAndReloadPreserveSourceAndBase()
        {
            using var f = new Fixture();
            string folder = "Assets/__GuidedAuthoring_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            GameObject instance = null, loaded = null;
            try
            {
                AssetDatabase.CreateAsset(f.Mesh, folder + "/Source.asset");
                var prefab = PrefabUtility.SaveAsPrefabAsset(f.Target.gameObject, folder + "/Base.prefab");
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var variant = PrefabUtility.SaveAsPrefabAsset(instance, folder + "/Variant.prefab");
                Object.DestroyImmediate(instance);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(variant);
                var target = instance.GetComponent<LatticeDeformer>();
                target.Deform(false);
                string before = EditorJsonUtility.ToJson(target);
                Assert.That(GuidedAuthoringService.EnsureLayer(target, MeshDeformerLayerType.Brush, "Guided variant"), Is.EqualTo(1));
                Undo.PerformUndo();
                Assert.That(EditorJsonUtility.ToJson(target), Is.EqualTo(before));
                Undo.PerformRedo();
                Assert.That(target.Layers.Count, Is.EqualTo(2));
                Assert.That(target.ActiveLayerIndex, Is.EqualTo(1));
                Assert.That(PrefabUtility.GetPropertyModifications(target).Any(p => p.propertyPath.Contains("_layers")), Is.True);
                PrefabUtility.ApplyPrefabInstance(instance, InteractionMode.AutomatedAction);
                target.InvalidateCache(); target.RestoreOriginalMesh();
                Undo.ClearUndo(target);
                Object.DestroyImmediate(instance); instance = null;
                loaded = PrefabUtility.LoadPrefabContents(folder + "/Variant.prefab");
                var restored = loaded.GetComponent<LatticeDeformer>();
                Assert.That(restored.ActiveLayerIndex, Is.EqualTo(1));
                Assert.That(restored.Layers.Count, Is.EqualTo(2));
                Assert.That(loaded.activeSelf, Is.False);
                Assert.That(prefab.GetComponent<LatticeDeformer>().Layers.Count, Is.EqualTo(1));
                Assert.That(f.Mesh.vertices, Is.EqualTo(Fixture.Vertices));
            }
            finally
            {
                if (loaded != null) PrefabUtility.UnloadPrefabContents(loaded);
                if (instance != null)
                {
                    var target = instance.GetComponent<LatticeDeformer>();
                    target.InvalidateCache(); target.RestoreOriginalMesh(); Undo.ClearUndo(target);
                    Object.DestroyImmediate(instance);
                }
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private static void Fail() => Assert.Fail("Rejected input must not invoke UI, preview or source callbacks.");
        private static void Set(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private sealed class GuidedTestWindow : EditorWindow
        {
            internal Action Draw;
            private void OnGUI() => Draw?.Invoke();
        }
        private sealed class Fixture : IDisposable
        {
            internal static readonly Vector3[] Vertices = { Vector3.zero, Vector3.right, Vector3.up };
            internal readonly Mesh Mesh;
            internal readonly LatticeDeformer Target;
            internal readonly MeshFilter Filter;
            internal Fixture()
            {
                Mesh = new Mesh { name = "Guided source", vertices = Vertices, triangles = new[] { 0, 1, 2 } };
                Mesh.RecalculateNormals(); Mesh.RecalculateBounds();
                var go = new GameObject("Guided authoring fixture"); go.SetActive(false);
                Filter = go.AddComponent<MeshFilter>(); Filter.sharedMesh = Mesh; go.AddComponent<MeshRenderer>();
                Target = go.AddComponent<LatticeDeformer>(); Target.Reset(); Target.enabled = false; Target.Deform(false);
            }
            public void Dispose()
            {
                Undo.ClearUndo(Target); Target.InvalidateCache(); Target.RestoreOriginalMesh();
                Object.DestroyImmediate(Target.gameObject);
                if (Mesh != null && !AssetDatabase.Contains(Mesh)) Object.DestroyImmediate(Mesh);
            }
        }
    }
}
#endif
