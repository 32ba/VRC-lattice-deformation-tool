#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using Net._32Ba.LatticeDeformationTool.Editor;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class DeformerStackInspectorTests
    {
        [TestCase("selection")]
        [TestCase("future")]
        [TestCase("null-groups")]
        [TestCase("null-group")]
        [TestCase("missing-profile")]
        public void Rebuild_DoesNotRepairOrSelectMalformedStorage(string fault)
        {
            using var f = new Fixture();
            switch (fault)
            {
                case "selection": Set(f.Target, "_activeGroupIndex", 100); break;
                case "future": Set(f.Target, "_migrationReleaseIndex", int.MaxValue); break;
                case "null-groups": Set(f.Target, "_groups", null); break;
                case "null-group": Set(f.Target, "_groups", new List<DeformerGroup> { null }); break;
                case "missing-profile": Set(f.Target, "_dataSource", DeformerDataSource.Profile); break;
            }
            var raw = SerializedDeformerReader.Read(f.Target);
            int dirty = EditorUtility.GetDirtyCount(f.Target), undo = Undo.GetCurrentGroup();
            var runtime = f.Target.RuntimeMesh;
            for (int i = 0; i < 3; i++) { f.Section.RebuildGroupList(); f.Section.CheckAndRebuildLayers(); }
            var after = SerializedDeformerReader.Read(f.Target);
            Assert.That(after.EmbeddedGroups, Is.SameAs(raw.EmbeddedGroups));
            if (fault == "null-group") Assert.That(after.EmbeddedGroups[0], Is.Null);
            Assert.That(after.EmbeddedActiveGroupIndex, Is.EqualTo(raw.EmbeddedActiveGroupIndex));
            Assert.That(after.DataSource, Is.EqualTo(raw.DataSource));
            Assert.That(EditorUtility.GetDirtyCount(f.Target), Is.EqualTo(dirty));
            Assert.That(Undo.GetCurrentGroup(), Is.EqualTo(undo));
            Assert.That(f.Target.RuntimeMesh, Is.SameAs(runtime));
            Assert.That(f.Section.GroupList, Is.Null);
        }

        [Test]
        public void RebuildAndRefresh_KeepSelectionPayloadAndUndo()
        {
            using var f = new Fixture();
            f.Target.ActiveGroupIndex = 1; f.Target.ActiveLayerIndex = 1;
            string before = EditorJsonUtility.ToJson(f.Target);
            int undo = Undo.GetCurrentGroup(), dirty = EditorUtility.GetDirtyCount(f.Target);
            for (int i = 0; i < 3; i++)
            {
                f.Section.RebuildGroupList(); f.BindGroup(1);
                Assert.That(f.Section.GroupList.selectedIndex, Is.EqualTo(1));
                Assert.That(f.Section.LayerList.selectedIndex, Is.EqualTo(1));
                f.Section.CheckAndRebuildLayers();
            }
            Assert.That(EditorJsonUtility.ToJson(f.Target), Is.EqualTo(before));
            Assert.That(Undo.GetCurrentGroup(), Is.EqualTo(undo));
            Assert.That(EditorUtility.GetDirtyCount(f.Target), Is.EqualTo(dirty));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ListSelection_IsUndoableAndRefreshesAfterUndoRedo(bool layer)
        {
            using var f = new Fixture();
            f.BindGroup(0);
            string before = EditorJsonUtility.ToJson(f.Target);
            (layer ? f.Section.LayerList : f.Section.GroupList).selectedIndex = 1;
            string after = EditorJsonUtility.ToJson(f.Target);
            Assert.That(layer ? f.Target.ActiveLayerIndex : f.Target.ActiveGroupIndex, Is.EqualTo(1));
            Undo.PerformUndo(); f.Section.CheckAndRebuildLayers();
            Assert.That(EditorJsonUtility.ToJson(f.Target), Is.EqualTo(before));
            Undo.PerformRedo(); f.Section.CheckAndRebuildLayers();
            Assert.That(EditorJsonUtility.ToJson(f.Target), Is.EqualTo(after));
            f.BindGroup(f.Target.ActiveGroupIndex);
            Assert.That(layer ? f.Section.LayerList.selectedIndex : f.Section.GroupList.selectedIndex, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator GroupDrag_KeepsPanelUntilPointerDispatchEnds() => DragDispatch(false, false);

        [UnityTest]
        public IEnumerator LayerDrag_KeepsPanelUntilPointerDispatchEnds() => DragDispatch(true, false);

        [UnityTest]
        public IEnumerator PendingDrag_DiscardsInputAfterExternalStorageChange() => DragDispatch(true, true);

        [UnityTest]
        public IEnumerator PointerCancel_DiscardsPendingSelectionAndReorder() => DragDispatch(true, false, true);

        private static IEnumerator DragDispatch(bool layer, bool replaceStorage, bool cancel = false)
        {
            using var f = new Fixture();
            f.Attach(f.Section.Root);
            yield return null;
            var groupList = f.Section.GroupList;
            var list = layer ? f.Section.LayerList : groupList;
            Assert.That(list.panel, Is.Not.Null);
            int parentPointerDown = 0;
            groupList.RegisterCallback<PointerDownEvent>(_ => parentPointerDown++);
            var originalGroup = f.Target.Groups[0];
            var originalLayer = originalGroup.Layers[1];
            string originalLayerJson = JsonUtility.ToJson(originalLayer);
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0 }))
            {
                down.target = list; list.SendEvent(down);
            }
            if (layer) Assert.That(parentPointerDown, Is.Zero, "A layer drag must not arm its parent group dragger.");
            // The animated dragger clears selection, reselects the dragged row,
            // and asks its controller to move that row before pointer-up returns.
            list.ClearSelection();
            list.SetSelection(1);
            list.viewController.Move(1, 0);
            f.Section.RebuildGroupList();
            Assert.That(f.Section.GroupList, Is.SameAs(groupList));
            Assert.That(list.panel, Is.Not.Null);
            Assert.That(f.Target.ActiveGroupIndex, Is.EqualTo(0));
            Assert.That(f.Target.ActiveLayerIndex, Is.EqualTo(0));
            Assert.That(f.Target.Groups[0], Is.SameAs(originalGroup));
            Assert.That(f.Target.Groups[0].Layers[1], Is.SameAs(originalLayer));
            yield return null; // A held pointer must survive another Editor update.
            Assert.That(f.Section.GroupList, Is.SameAs(groupList));
            if (replaceStorage) f.Target.ActiveGroupIndex = 1;
            if (cancel)
            {
                using var cancelled = PointerCancelEvent.GetPooled();
                cancelled.target = list; list.SendEvent(cancelled);
            }
            else using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0 }))
            {
                up.target = list; list.SendEvent(up);
            }
            Assert.That(list.panel, Is.Not.Null, "The event dispatcher still uses this panel.");
            yield return null;
            yield return null;
            if (replaceStorage || cancel)
            {
                Assert.That(f.Target.ActiveGroupIndex, Is.EqualTo(replaceStorage ? 1 : 0));
                Assert.That(f.Target.Groups[0], Is.SameAs(originalGroup));
                Assert.That(f.Target.Groups[0].Layers[1], Is.SameAs(originalLayer));
            }
            else if (layer)
            {
                Assert.That(f.Target.ActiveGroupIndex, Is.EqualTo(0));
                Assert.That(f.Target.ActiveLayerIndex, Is.EqualTo(0));
                Assert.That(JsonUtility.ToJson(f.Target.Layers[0]), Is.EqualTo(originalLayerJson));
            }
            else
            {
                Assert.That(f.Target.ActiveGroupIndex, Is.EqualTo(0));
                Assert.That(f.Target.Groups[1], Is.SameAs(originalGroup));
            }
            Assert.That(f.Section.GroupList, Is.Not.SameAs(groupList));
            CollectionAssert.AreEqual(f.Vertices, f.Mesh.vertices);
        }

        [Test]
        public void ExternalSameCountGroupSelection_RebuildsNestedRows()
        {
            using var f = new Fixture();
            var oldList = f.Section.GroupList;
            f.Target.ActiveGroupIndex = 1;
            f.Section.CheckAndRebuildLayers();
            Assert.That(f.Section.GroupList, Is.Not.SameAs(oldList));
            Assert.That(f.Section.GroupList.selectedIndex, Is.EqualTo(1));
            f.BindGroup(1);
            Assert.That(f.BindLayer(1).Q<TextField>("layer-name").value, Is.EqualTo("Second brush"));
        }

        [UnityTest]
        public IEnumerator StaleRows_CannotRenameToggleOrWeightReorderedStorage()
        {
            using var f = new Fixture();
            var oldGroup = f.BindGroup(0);
            var oldLayer = f.BindLayer(1);
            yield return null;
            Assert.That(oldLayer.Q<Slider>("layer-weight").binding, Is.Not.Null);
            Assert.That(f.Target.MoveGroup(0, 1), Is.True);
            string before = EditorJsonUtility.ToJson(f.Target);
            int undo = Undo.GetCurrentGroup();
            Change(oldGroup.Q<TextField>("group-name"), "Wrong group");
            Change(oldLayer.Q<TextField>("layer-name"), "Wrong layer");
            Change(oldLayer.Q<Toggle>("layer-enabled"), false);
            Change(oldLayer.Q<Slider>("layer-weight"), 0.1f);
            f.Section.CheckAndRebuildLayers();
            Change(oldGroup.Q<TextField>("group-name"), "Still stale");
            Assert.That(EditorJsonUtility.ToJson(f.Target), Is.EqualTo(before));
            Assert.That(Undo.GetCurrentGroup(), Is.EqualTo(undo));
        }

        [UnityTest]
        public IEnumerator SameCountObjectReplacement_InvalidatesOldRowIdentity()
        {
            using var f = new Fixture();
            var oldGroup = f.BindGroup(0);
            yield return null;
            Assert.That(oldGroup.Q<TextField>("group-name").binding, Is.Not.Null);
            var raw = SerializedDeformerReader.Read(f.Target);
            var copy = JsonUtility.FromJson<DeformerGroup>(JsonUtility.ToJson(raw.EmbeddedGroups[0]));
            Set(f.Target, "_groups", new List<DeformerGroup> { copy, raw.EmbeddedGroups[1] });
            Change(oldGroup.Q<TextField>("group-name"), "Must not apply");
            Assert.That(copy.Name, Is.EqualTo("First group"));
            var oldList = f.Section.GroupList;
            f.Section.CheckAndRebuildLayers();
            Assert.That(f.Section.GroupList, Is.Not.SameAs(oldList));
        }

        [UnityTest]
        public IEnumerator RowProperties_UseSerializedUndoAndRefreshWithoutRebuilding()
        {
            using var f = new Fixture();
            f.BindGroup(0); var row = f.BindLayer(1);
            yield return null;
            Assert.That(row.Q<Slider>("layer-weight").binding, Is.Not.Null);
            var list = f.Section.GroupList;
            Undo.IncrementCurrentGroup();
            Change(row.Q<TextField>("layer-name"), "Edited brush");
            Change(row.Q<Toggle>("layer-enabled"), false);
            Change(row.Q<Slider>("layer-weight"), 0.25f);
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            Assert.That(f.Target.Layers[1].Name, Is.EqualTo("Edited brush"));
            Assert.That(f.Target.Layers[1].Enabled, Is.False);
            Assert.That(f.Target.Layers[1].Weight, Is.EqualTo(0.25f));
            f.Section.CheckAndRebuildLayers();
            Assert.That(f.Section.GroupList, Is.SameAs(list));
            Undo.PerformUndo(); f.Section.CheckAndRebuildLayers();
            f.BindGroup(0); row = f.BindLayer(1);
            Assert.That(row.Q<TextField>("layer-name").value, Is.EqualTo("First brush"));
            Assert.That(row.Q<Toggle>("layer-enabled").value, Is.True);
            Assert.That(row.Q<Slider>("layer-weight").value, Is.EqualTo(1f));
            Assert.That(f.Mesh.vertices, Is.EqualTo(f.Vertices));
        }

        [UnityTest]
        public IEnumerator SourceDrift_RejectsRowsSelectionAndClipboardWithoutUndo()
        {
            using var f = new Fixture();
            f.BindGroup(0); var row = f.BindLayer(1);
            yield return null;
            Assert.That(row.Q<Slider>("layer-weight").binding, Is.Not.Null);
            var clipboard = typeof(DeformerStackInspectorSection).GetField("s_copiedLayerJson", BindingFlags.NonPublic | BindingFlags.Static);
            var previous = clipboard.GetValue(null);
            try
            {
                clipboard.SetValue(null, "retained clipboard");
                f.Mesh.triangles = new[] { 0, 2, 1 };
                string before = EditorJsonUtility.ToJson(f.Target); int undo = Undo.GetCurrentGroup();
                Change(row.Q<Slider>("layer-weight"), 0.4f);
                f.Section.GroupList.selectedIndex = 1;
                Assert.That(f.Section.GroupList.selectedIndex, Is.Zero);
                f.Section.CopyLayer(f.Target, 1);
                Assert.That(clipboard.GetValue(null), Is.EqualTo("retained clipboard"));
                Assert.That(EditorJsonUtility.ToJson(f.Target), Is.EqualTo(before));
                Assert.That(Undo.GetCurrentGroup(), Is.EqualTo(undo));
            }
            finally { clipboard.SetValue(null, previous); }
        }

        [UnityTest]
        public IEnumerator DisposedInspector_DetachesRowsAndIgnoresDelayedUpdates()
        {
            using var f = new Fixture();
            f.BindGroup(0); var row = f.BindLayer(1); var section = f.Section;
            yield return null;
            Assert.That(row.Q<Slider>("layer-weight").binding, Is.Not.Null);
            string before = EditorJsonUtility.ToJson(f.Target);
            Object.DestroyImmediate(f.Editor); f.Editor = null;
            Change(row.Q<Slider>("layer-weight"), 0.2f);
            Assert.DoesNotThrow(() => { section.CheckAndRebuildLayers(); section.RebuildGroupList(); section.Dispose(); });
            Assert.That(EditorJsonUtility.ToJson(f.Target), Is.EqualTo(before));
        }

        private static void Set(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        [UnityTest]
        public IEnumerator PrefabVariant_SelectionAndWeightSurviveApplyReloadWithoutChangingBase()
        {
            using var f = new Fixture();
            string folder = "Assets/__StackVariant_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            GameObject instance = null, loaded = null;
            LatticeDeformerEditor inspector = null;
            StackTestWindow window = null;
            try
            {
                AssetDatabase.CreateAsset(f.Mesh, folder + "/Source.asset");
                var basePrefab = PrefabUtility.SaveAsPrefabAsset(f.Target.gameObject, folder + "/Base.prefab");
                instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
                var variant = PrefabUtility.SaveAsPrefabAsset(instance, folder + "/Variant.prefab");
                DestroyInstance(instance); instance = (GameObject)PrefabUtility.InstantiatePrefab(variant);
                var d = instance.GetComponent<LatticeDeformer>();
                inspector = (LatticeDeformerEditor)UnityEditor.Editor.CreateEditor(d);
                inspector.CreateInspectorGUI(); var section = inspector.StackInspector;
                section.GroupList.selectedIndex = 1;
                var groupRow = section.GroupList.makeItem(); section.GroupList.bindItem(groupRow, 1);
                section.LayerList.selectedIndex = 1;
                groupRow = section.GroupList.makeItem(); section.GroupList.bindItem(groupRow, 1);
                var row = section.LayerList.makeItem(); section.LayerList.bindItem(row, 1);
                window = ScriptableObject.CreateInstance<StackTestWindow>(); window.ShowUtility();
                window.rootVisualElement.Add(row);
                yield return null;
                Assert.That(row.Q<Slider>("layer-weight").binding, Is.Not.Null);
                Undo.IncrementCurrentGroup(); Change(row.Q<Slider>("layer-weight"), 0.37f);
                Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
                Assert.That(d.Groups[1].Layers[1].Weight, Is.EqualTo(0.37f));
                Undo.PerformUndo(); Assert.That(d.Groups[1].Layers[1].Weight, Is.EqualTo(1f));
                Undo.PerformRedo(); Assert.That(d.Groups[1].Layers[1].Weight, Is.EqualTo(0.37f));
                Object.DestroyImmediate(inspector); inspector = null;
                PrefabUtility.ApplyPrefabInstance(instance, InteractionMode.AutomatedAction);
                DestroyInstance(instance); instance = null;
                loaded = PrefabUtility.LoadPrefabContents(folder + "/Variant.prefab");
                d = loaded.GetComponent<LatticeDeformer>();
                Assert.That(d.ActiveGroupIndex, Is.EqualTo(1));
                Assert.That(d.ActiveLayerIndex, Is.EqualTo(1));
                Assert.That(d.Layers[1].Weight, Is.EqualTo(0.37f));
                var original = AssetDatabase.LoadAssetAtPath<GameObject>(folder + "/Base.prefab").GetComponent<LatticeDeformer>();
                Assert.That(original.ActiveGroupIndex, Is.Zero);
                Assert.That(original.Groups[1].Layers[1].Weight, Is.EqualTo(1f));
                Assert.That(f.Mesh.vertices, Is.EqualTo(f.Vertices));
            }
            finally
            {
                if (inspector != null) Object.DestroyImmediate(inspector);
                if (window != null) window.Close();
                if (loaded != null)
                {
                    var d = loaded.GetComponent<LatticeDeformer>(); d.InvalidateCache(); d.RestoreOriginalMesh();
                    PrefabUtility.UnloadPrefabContents(loaded);
                }
                DestroyInstance(instance);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private static void DestroyInstance(GameObject instance)
        {
            if (instance == null) return;
            var d = instance.GetComponent<LatticeDeformer>();
            if (d != null) { Undo.ClearUndo(d); d.InvalidateCache(); d.RestoreOriginalMesh(); }
            Object.DestroyImmediate(instance);
        }

        // Detached rows do not dispatch ChangeEvent when their value setter runs.
        // Send the actual UI Toolkit event to exercise the registered callback.
        private static void Change<T>(BaseField<T> field, T value)
        {
            Assert.That(field.panel, Is.Not.Null, "The row must be attached to an Editor panel to dispatch input.");
            using var evt = ChangeEvent<T>.GetPooled(field.value, value);
            field.SetValueWithoutNotify(value); evt.target = field; field.SendEvent(evt);
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly Vector3[] Vertices = { Vector3.zero, Vector3.right, Vector3.up };
            internal readonly Mesh Mesh;
            internal readonly LatticeDeformer Target;
            internal LatticeDeformerEditor Editor;
            private StackTestWindow _window;
            internal DeformerStackInspectorSection Section => Editor.StackInspector;
            internal Fixture()
            {
                Mesh = new Mesh { name = "Stack Inspector source", vertices = Vertices, triangles = new[] { 0, 1, 2 } };
                Mesh.RecalculateNormals(); Mesh.RecalculateBounds();
                var go = new GameObject("Stack Inspector fixture"); go.SetActive(false);
                go.AddComponent<MeshFilter>().sharedMesh = Mesh; go.AddComponent<MeshRenderer>();
                Target = go.AddComponent<LatticeDeformer>(); Target.Reset(); Target.enabled = false;
                Target.Groups[0].Name = "First group";
                Target.AddLayer("First brush", MeshDeformerLayerType.Brush);
                Target.AddGroup("Second group"); Target.ActiveGroupIndex = 1;
                Target.AddLayer("Second lattice", MeshDeformerLayerType.Lattice);
                Target.AddLayer("Second brush", MeshDeformerLayerType.Brush);
                Target.ActiveLayerIndex = 0; Target.ActiveGroupIndex = 0; Target.ActiveLayerIndex = 0;
                Target.Deform(false);
                Editor = (LatticeDeformerEditor)UnityEditor.Editor.CreateEditor(Target);
                Editor.CreateInspectorGUI();
            }
            internal VisualElement BindGroup(int index)
            {
                var row = Section.GroupList.makeItem(); Section.GroupList.bindItem(row, index); Attach(row); return row;
            }
            internal VisualElement BindLayer(int index)
            {
                var row = Section.LayerList.makeItem(); Section.LayerList.bindItem(row, index); Attach(row); return row;
            }
            internal void Attach(VisualElement row)
            {
                if (_window == null)
                {
                    _window = ScriptableObject.CreateInstance<StackTestWindow>();
                    _window.titleContent = new GUIContent("Stack Inspector test");
                    _window.ShowUtility();
                }
                _window.rootVisualElement.Add(row);
            }
            public void Dispose()
            {
                if (Editor != null) Object.DestroyImmediate(Editor);
                if (_window != null) _window.Close();
                Undo.ClearUndo(Target); Target.InvalidateCache(); Target.RestoreOriginalMesh();
                Object.DestroyImmediate(Target.gameObject);
                if (Mesh != null && !AssetDatabase.Contains(Mesh)) Object.DestroyImmediate(Mesh);
            }
        }

        private sealed class StackTestWindow : EditorWindow { }
    }
}
#endif
