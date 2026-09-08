#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class LayerSettingsInspectorTests
    {
        [TestCase("resize")]
        [TestCase("reset")]
        [TestCase("clear")]
        [TestCase("lattice-split-left")]
        [TestCase("lattice-split-right")]
        [TestCase("lattice-flip-x")]
        [TestCase("lattice-flip-y")]
        [TestCase("lattice-flip-z")]
        [TestCase("brush-split-left")]
        [TestCase("brush-split-right")]
        [TestCase("brush-flip-x")]
        [TestCase("brush-flip-y")]
        [TestCase("brush-flip-z")]
        public void Operations_MatchOldImplementationAndRoundTripUndo(string operation)
        {
            using var fixture = new Fixture(operation == "clear" || operation.StartsWith("brush-"));
            fixture.Owner.Deform(false);
            string before = EditorJsonUtility.ToJson(fixture.Owner);
            var beforeVertices = fixture.Owner.Deform(false).vertices;
            var expected = JsonUtility.FromJson<Baseline>(File.ReadAllText(
                "Packages/net.32ba.lattice-deformation-tool/Tests/Editor/Fixtures/LayerSettingsBaseline/expected.json")).cases
                .Single(c => c.operation == operation);
            Assert.That(LayerSettingsEdit.Capture(fixture.Owner).Execute(Operation(operation), operation, new Vector3Int(4, 3, 2)), Is.True);
            var output = fixture.Owner.Deform(false);
            Assert.That(JsonUtility.ToJson(fixture.Owner.ActiveGroup), Is.EqualTo((string)expected.groupJson));
            Assert.That(fixture.Owner.ActiveLayerIndex, Is.EqualTo((int)expected.activeLayer));
            AssertVertices(output.vertices, expected.vertices);
            AssertVertices(fixture.Source.vertices, expected.sourceVertices);
            string after = EditorJsonUtility.ToJson(fixture.Owner);
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Assert.That(EditorJsonUtility.ToJson(fixture.Owner), Is.EqualTo(before));
            Assert.That(fixture.Owner.Deform(false).vertices, Is.EqualTo(beforeVertices));
            Undo.PerformRedo();
            Assert.That(EditorJsonUtility.ToJson(fixture.Owner), Is.EqualTo(after));
            AssertVertices(fixture.Owner.Deform(false).vertices, expected.vertices);
        }

        [Test]
        public void PendingGrid_SeparatesGroupsAndInspectorsWithoutChangingSerializedData()
        {
            using var fixture = new Fixture();
            var owner = fixture.Owner;
            owner.AddGroup("second"); owner.AddLayer(); owner.ActiveGroupIndex = 0;
            using var drafts = new PendingLatticeGrid(); using var other = new PendingLatticeGrid();
            string before = EditorJsonUtility.ToJson(owner);
            int dirty = EditorUtility.GetDirtyCount(owner), undo = Undo.GetCurrentGroup();
            Assert.That(drafts.Set(owner, new Vector3Int(4, 5, 6)), Is.True);
            Assert.That(other.TryGet(owner, out var independent), Is.True);
            Assert.That(independent, Is.EqualTo(owner.EditingSettings.GridSize));
            Assert.That(EditorJsonUtility.ToJson(owner), Is.EqualTo(before));
            Assert.That(EditorUtility.GetDirtyCount(owner), Is.EqualTo(dirty));
            Assert.That(Undo.GetCurrentGroup(), Is.EqualTo(undo));
            owner.ActiveGroupIndex = 1;
            drafts.TryGet(owner, out var second);
            Assert.That(second, Is.EqualTo(owner.EditingSettings.GridSize));
            drafts.Set(owner, new Vector3Int(6, 4, 3)); owner.ActiveGroupIndex = 0;
            drafts.TryGet(owner, out var first); Assert.That(first, Is.EqualTo(new Vector3Int(4, 5, 6)));
            Assert.That(drafts.Apply(new[] {owner}, "Apply draft"), Is.True);
            Assert.That(owner.EditingSettings.GridSize, Is.EqualTo(first));
            owner.ActiveGroupIndex = 1; drafts.TryGet(owner, out second);
            Assert.That(second, Is.EqualTo(new Vector3Int(6, 4, 3)));
        }

        [TestCase("group")]
        [TestCase("layer")]
        [TestCase("settings")]
        [TestCase("external-resize")]
        public void PendingGrid_DiscardsDraftWhenStoredIdentityOrGridChanges(string change)
        {
            using var fixture = new Fixture(); using var drafts = new PendingLatticeGrid();
            drafts.Set(fixture.Owner, new Vector3Int(7, 8, 9));
            Replace(fixture.Owner, change);
            drafts.TryGet(fixture.Owner, out var pending);
            Assert.That(pending, Is.EqualTo(fixture.Owner.EditingSettings.GridSize));
            drafts.Dispose();
            Assert.That(drafts.Set(fixture.Owner, Vector3Int.one), Is.False);
            Assert.That(drafts.Apply(new[] {fixture.Owner}, "disposed"), Is.False);
        }

        [Test]
        public void PendingApply_BatchesTargetsAsOneUndoAndRejectsInvalidTargetBeforeAnyWrite()
        {
            using var first = new Fixture(); using var second = new Fixture(); using var drafts = new PendingLatticeGrid();
            var owners = new[] {first.Owner, second.Owner};
            var before = owners.Select(o => EditorJsonUtility.ToJson(o)).ToArray();
            drafts.Set(first.Owner, new Vector3Int(4, 3, 2)); drafts.Set(second.Owner, new Vector3Int(2, 5, 3));
            SetField(second.Owner, "_migrationReleaseIndex", int.MaxValue);
            AssertRejected(first.Owner, () => drafts.Apply(owners, "invalid batch"));
            SetField(second.Owner, "_migrationReleaseIndex", DeformationReleaseManifest.Current);
            Assert.That(drafts.Apply(owners, "resize batch"), Is.True);
            Assert.That(first.Owner.EditingSettings.GridSize, Is.EqualTo(new Vector3Int(4, 3, 2)));
            Assert.That(second.Owner.EditingSettings.GridSize, Is.EqualTo(new Vector3Int(2, 5, 3)));
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Assert.That(owners.Select(o => EditorJsonUtility.ToJson(o)), Is.EqualTo(before));
            drafts.TryGet(first.Owner, out var reset);
            Assert.That(reset, Is.EqualTo(first.Owner.EditingSettings.GridSize));
        }

        [TestCase("profile")]
        [TestCase("future")]
        [TestCase("selection")]
        [TestCase("brush-count")]
        public void ReadAndCommandCapture_RejectInvalidDataWithoutRepair(string change)
        {
            using var fixture = new Fixture(change == "brush-count"); using var drafts = new PendingLatticeGrid();
            if (change == "profile") SetField(fixture.Owner, "_dataSource", DeformerDataSource.Profile);
            if (change == "future") SetField(fixture.Owner, "_migrationReleaseIndex", int.MaxValue);
            if (change == "selection") SetField(fixture.Owner, "_activeGroupIndex", 99);
            if (change == "brush-count") SetField(fixture.Owner.Layers[1], "_brushDisplacements", new Vector3[2]);
            AssertRejected(fixture.Owner, () => LayerSettingsEdit.Capture(fixture.Owner) != null);
            AssertRejected(fixture.Owner, () => drafts.TryGet(fixture.Owner, out _));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReadAndCommandCapture_DoNotMaterializeNullSlots(bool nullLayer)
        {
            using var fixture = new Fixture(); using var drafts = new PendingLatticeGrid();
            var groups = (List<DeformerGroup>)SerializedDeformerReader.Read(fixture.Owner).EmbeddedGroups;
            var layers = (List<LatticeLayer>)SerializedDeformerReader.Read(fixture.Owner).ActiveLayers;
            if (nullLayer) layers[0] = null; else groups[0] = null;
            int dirty = EditorUtility.GetDirtyCount(fixture.Owner), undo = Undo.GetCurrentGroup();
            Assert.That(LayerSettingsEdit.Capture(fixture.Owner), Is.Null);
            Assert.That(drafts.TryGet(fixture.Owner, out _), Is.False);
            Assert.That(nullLayer ? (object)layers[0] : groups[0], Is.Null);
            Assert.That(EditorUtility.GetDirtyCount(fixture.Owner), Is.EqualTo(dirty));
            Assert.That(Undo.GetCurrentGroup(), Is.EqualTo(undo));
        }

        [TestCase("group")]
        [TestCase("layer")]
        [TestCase("settings")]
        [TestCase("selection")]
        [TestCase("renderer")]
        [TestCase("topology")]
        public void DeferredOperation_RejectsChangedDestinationOrSource(string change)
        {
            using var fixture = new Fixture();
            var command = LayerSettingsEdit.Capture(fixture.Owner);
            Mesh other = null;
            try
            {
                if (change == "renderer") { other = Object.Instantiate(fixture.Source); fixture.Root.GetComponent<MeshFilter>().sharedMesh = other; }
                else if (change == "topology") fixture.Source.triangles = new[] {0, 2, 1};
                else if (change == "selection") fixture.Owner.AddLayer();
                else Replace(fixture.Owner, change);
                AssertRejected(fixture.Owner, () => command.Execute(LayerSettingsOperation.FlipX, "stale action"));
            }
            finally { if (other != null) Object.DestroyImmediate(other); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DeferredOperation_IsSingleUseAndStopsWhenInspectorIsDisposed(bool dispose)
        {
            using var fixture = new Fixture(true);
            var editor = UnityEditor.Editor.CreateEditor(fixture.Owner);
            int callbacks = 0;
            var section = new LayerSettingsInspectorSection(editor, _ => callbacks++, null);
            try
            {
                var action = section.CreateOperationAction(LayerSettingsEdit.Capture(fixture.Owner), LayerSettingsOperation.FlipX, "Flip");
                if (dispose) section.Dispose();
                string before = EditorJsonUtility.ToJson(fixture.Owner);
                action(); string after = EditorJsonUtility.ToJson(fixture.Owner); action();
                Assert.That(EditorJsonUtility.ToJson(fixture.Owner), Is.EqualTo(after));
                Assert.That(callbacks, Is.EqualTo(dispose ? 0 : 1));
                Assert.That(after == before, Is.EqualTo(dispose));
            }
            finally { section.Dispose(); Object.DestroyImmediate(editor); }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void InvalidGridOrOperation_IsRejectedBeforeUndo(int kind)
        {
            using var fixture = new Fixture();
            var command = LayerSettingsEdit.Capture(fixture.Owner);
            AssertRejected(fixture.Owner, () => command.Execute(kind == 2 ? LayerSettingsOperation.Clear : LayerSettingsOperation.Resize,
                "invalid resize", kind == 0 ? new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue) : fixture.Owner.EditingSettings.GridSize));
        }

        [TestCase("resize")]
        [TestCase("reset")]
        [TestCase("clear")]
        public void InactivePrefabVariant_RecordsAndSavesEditsWithoutChangingBase(string operation)
        {
            using var fixture = new Fixture(operation == "clear");
            string folder = "Assets/__LayerSettings_" + Guid.NewGuid().ToString("N"); GameObject instance = null;
            try
            {
                AssetDatabase.CreateFolder("Assets", folder.Substring(7));
                AssetDatabase.CreateAsset(fixture.Source, folder + "/source.asset");
                var original = PrefabUtility.SaveAsPrefabAsset(fixture.Root, folder + "/base.prefab");
                string baseBefore = EditorJsonUtility.ToJson(original.GetComponent<LatticeDeformer>());
                instance = (GameObject)PrefabUtility.InstantiatePrefab(original);
                var variant = PrefabUtility.SaveAsPrefabAsset(instance, folder + "/variant.prefab");
                Object.DestroyImmediate(instance); instance = (GameObject)PrefabUtility.InstantiatePrefab(variant);
                var owner = instance.GetComponent<LatticeDeformer>();
                Assert.That(owner.SourceMesh, Is.Null, "An inactive prefab must not need the execution cache.");
                Assert.That(LayerSettingsEdit.Capture(owner).Execute(Operation(operation), operation, new Vector3Int(4, 3, 2)), Is.True);
                string changed = JsonUtility.ToJson(owner.ActiveGroup);
                Assert.That(PrefabUtility.HasPrefabInstanceAnyOverrides(instance, false), Is.True);
                PrefabUtility.ApplyPrefabInstance(instance, InteractionMode.AutomatedAction);
                Undo.ClearUndo(owner); Object.DestroyImmediate(instance); instance = null;
                var loaded = PrefabUtility.LoadPrefabContents(folder + "/variant.prefab");
                try { Assert.That(JsonUtility.ToJson(loaded.GetComponent<LatticeDeformer>().ActiveGroup), Is.EqualTo(changed)); }
                finally { PrefabUtility.UnloadPrefabContents(loaded); }
                Assert.That(EditorJsonUtility.ToJson(original.GetComponent<LatticeDeformer>()), Is.EqualTo(baseBefore));
            }
            finally { if (instance != null) Object.DestroyImmediate(instance); AssetDatabase.DeleteAsset(folder); }
        }

        [Serializable] private sealed class Baseline { public Case[] cases; }
        [Serializable] private sealed class Case
        {
            public string operation, groupJson; public int activeLayer; public Vector3[] vertices, sourceVertices;
        }

        private static LayerSettingsOperation Operation(string name) => name switch
        {
            "resize" => LayerSettingsOperation.Resize, "reset" => LayerSettingsOperation.Reset, "clear" => LayerSettingsOperation.Clear,
            _ when name.EndsWith("split-left") => LayerSettingsOperation.SplitLeft,
            _ when name.EndsWith("split-right") => LayerSettingsOperation.SplitRight,
            _ when name.EndsWith("-x") => LayerSettingsOperation.FlipX,
            _ when name.EndsWith("-y") => LayerSettingsOperation.FlipY,
            _ => LayerSettingsOperation.FlipZ
        };

        private static void Replace(LatticeDeformer owner, string change)
        {
            var raw = SerializedDeformerReader.Read(owner);
            if (change == "group") ((List<DeformerGroup>)raw.EmbeddedGroups)[raw.ActiveGroupIndex] = JsonUtility.FromJson<DeformerGroup>(JsonUtility.ToJson(raw.ActiveGroup));
            if (change == "layer") ((List<LatticeLayer>)raw.ActiveLayers)[raw.ActiveLayerIndex] = JsonUtility.FromJson<LatticeLayer>(JsonUtility.ToJson(raw.ActiveLayers[raw.ActiveLayerIndex]));
            if (change == "settings") raw.ActiveLayers[raw.ActiveLayerIndex].Settings = JsonUtility.FromJson<LatticeAsset>(JsonUtility.ToJson(owner.EditingSettings));
            if (change == "external-resize") owner.EditingSettings.ResizeGrid(new Vector3Int(3, 4, 2));
        }

        private static void AssertVertices(Vector3[] actual, Vector3[] expected)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length));
            for (int i = 0; i < actual.Length; i++)
                for (int axis = 0; axis < 3; axis++) Assert.That(actual[i][axis], Is.EqualTo((float)expected[i][axis]).Within(1e-6f));
        }

        private static void AssertRejected(LatticeDeformer owner, Func<bool> action)
        {
            string before = EditorJsonUtility.ToJson(owner); int dirty = EditorUtility.GetDirtyCount(owner), undo = Undo.GetCurrentGroup();
            var displayed = owner.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(action(), Is.False);
            Assert.That(EditorJsonUtility.ToJson(owner), Is.EqualTo(before));
            Assert.That(EditorUtility.GetDirtyCount(owner), Is.EqualTo(dirty));
            Assert.That(Undo.GetCurrentGroup(), Is.EqualTo(undo));
            Assert.That(owner.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(displayed));
        }

        private static void SetField(object owner, string name, object value) =>
            owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);

        private sealed class Fixture : IDisposable
        {
            internal readonly GameObject Root = new("Layer settings inspector");
            internal readonly Mesh Source;
            internal readonly LatticeDeformer Owner;
            internal Fixture(bool brush = false)
            {
                Root.SetActive(false);
                Source = new Mesh { name = "Layer settings source", vertices = new[] {
                    new Vector3(-1,-1,-1),new Vector3(1,-1,-1),new Vector3(-1,1,-1),new Vector3(1,1,-1),
                    new Vector3(-1,-1,1),new Vector3(1,-1,1),new Vector3(-1,1,1),new Vector3(1,1,1)},
                    triangles = new[] {0,2,1,1,2,3,4,5,6,5,7,6,0,1,4,1,5,4,2,6,3,3,6,7,0,4,2,2,4,6,1,3,5,3,7,5} };
                Source.RecalculateNormals(); Source.RecalculateBounds();
                Root.AddComponent<MeshFilter>().sharedMesh = Source; Root.AddComponent<MeshRenderer>();
                Owner = Root.AddComponent<LatticeDeformer>(); Owner.Reset();
                var lattice = Owner.Layers[0].Settings;
                lattice.LocalBounds = new Bounds(new Vector3(.1f,.2f,.3f), new Vector3(2.4f,1.8f,2.2f)); lattice.ResetControlPoints();
                for (int i = 0; i < lattice.ControlPointCount; i++) lattice.SetControlPointLocal(i,
                    lattice.GetControlPointLocal(i) + new Vector3((i%3)*.04f, (i%2)*.03f, .02f));
                if (brush)
                {
                    Owner.AddLayer("Brush", MeshDeformerLayerType.Brush); var layer = Owner.Layers[Owner.ActiveLayerIndex];
                    for (int i = 0; i < Source.vertexCount; i++) layer.SetBrushDisplacement(i, new Vector3((i+1)*.01f,(i%3)*.02f,(i%2)*.03f));
                    layer.VertexMask = Enumerable.Range(0,8).Select(i => i%2 == 0 ? .25f : 1f).ToArray();
                }
            }
            public void Dispose()
            {
                if (Owner != null) { Undo.ClearUndo(Owner); Owner.InvalidateCache(); Owner.RestoreOriginalMesh(); }
                if (Root != null) Object.DestroyImmediate(Root);
                if (Source != null && !EditorUtility.IsPersistent(Source)) Object.DestroyImmediate(Source);
            }
        }
    }
}
#endif
