#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class BlendShapeInspectorTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void MenuImport_UsesOneUndoAndPreservesSource(bool allFrames)
        {
            using var fixture = new Fixture();
            var owner = fixture.Owner;
            var vertices = fixture.Source.vertices;
            string before = EditorJsonUtility.ToJson(owner);
            var editor = (LatticeDeformerEditor)UnityEditor.Editor.CreateEditor(owner);
            try
            {
                var menu = BlendShapeImportMenu.TryCreate(owner);
                Assert.That(menu, Is.Not.Null);
                var action = editor.BlendShapeInspector.CreateImportAction(menu, 0, allFrames);
                Undo.IncrementCurrentGroup();
                action();
                AssertImported(owner, allFrames);
                string imported = EditorJsonUtility.ToJson(owner);
                action();
                Assert.That(EditorJsonUtility.ToJson(owner), Is.EqualTo(imported), "One menu choice cannot import twice.");
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.That(EditorJsonUtility.ToJson(owner), Is.EqualTo(before));
                Undo.PerformRedo();
                Assert.That(EditorJsonUtility.ToJson(owner), Is.EqualTo(imported));
                Assert.That(fixture.Source.vertices, Is.EqualTo(vertices));
                Assert.That(fixture.Source.GetBlendShapeFrameCount(0), Is.EqualTo(2));
                var deltas = new Vector3[3];
                fixture.Source.GetBlendShapeFrameVertices(0, 1, deltas, null, null);
                Assert.That(deltas, Is.EqualTo(new[] {Vector3.up * .7f, Vector3.zero, Vector3.zero}));
            }
            finally { Object.DestroyImmediate(editor); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MenuImport_IntoEmptyGroupKeepsExistingSelectionContract(bool allFrames)
        {
            using var fixture = new Fixture();
            Assert.That(DeformerEditService.RemoveLayer(fixture.Owner, 0, "Remove last Inspector layer"), Is.True);
            var menu = BlendShapeImportMenu.TryCreate(fixture.Owner);
            Assert.That(menu, Is.Not.Null);
            Assert.That(menu.Import(0, allFrames, "Import empty group"), Is.True);
            Assert.That(fixture.Owner.ActiveGroup.Layers.Count, Is.EqualTo(allFrames ? 2 : 1));
        }

        [TestCase("group")]
        [TestCase("layer")]
        [TestCase("replacement")]
        [TestCase("renderer")]
        [TestCase("topology")]
        [TestCase("shape-name")]
        [TestCase("frame-weight")]
        [TestCase("frame-count")]
        public void DeferredMenu_RejectsChangedDestinationOrShapeWithoutMutation(string change)
        {
            using var fixture = new Fixture();
            var menu = BlendShapeImportMenu.TryCreate(fixture.Owner);
            Mesh replacement = null;
            try
            {
                switch (change)
                {
                    case "group": fixture.Owner.ActiveGroupIndex = fixture.Owner.AddGroup(); break;
                    case "layer": fixture.Owner.ActiveLayerIndex = fixture.Owner.AddLayer("other"); break;
                    case "replacement":
                        var clone = JsonUtility.FromJson<DeformerGroup>(JsonUtility.ToJson(fixture.Owner.ActiveGroup));
                        var groups = (System.Collections.Generic.List<DeformerGroup>)SerializedDeformerReader.Read(fixture.Owner).EmbeddedGroups;
                        groups[0] = clone;
                        break;
                    case "renderer":
                        replacement = Object.Instantiate(fixture.Source);
                        fixture.Root.GetComponent<MeshFilter>().sharedMesh = replacement;
                        break;
                    case "topology": fixture.Source.triangles = new[] {0, 2, 1}; break;
                    case "shape-name": fixture.SetFrames("changed", 100f, true); break;
                    case "frame-weight": fixture.SetFrames("Smile", 90f, true); break;
                    case "frame-count": fixture.SetFrames("Smile", 100f, false); break;
                }
                AssertRejected(fixture.Owner, () => menu.Import(0, false, "stale menu"));
            }
            finally { if (replacement != null) Object.DestroyImmediate(replacement); }
        }

        [TestCase("profile")]
        [TestCase("future")]
        [TestCase("selection")]
        [TestCase("brush-count")]
        public void MenuCreation_IsReadOnlyAndRejectsInvalidAuthoringState(string kind)
        {
            using var fixture = new Fixture();
            if (kind == "profile") SetField(fixture.Owner, "_dataSource", DeformerDataSource.Profile);
            if (kind == "future") SetField(fixture.Owner, "_migrationReleaseIndex", int.MaxValue);
            if (kind == "selection") SetField(fixture.Owner, "_activeGroupIndex", 999);
            if (kind == "brush-count")
            {
                fixture.Owner.ActiveLayerIndex = fixture.Owner.AddLayer("bad", MeshDeformerLayerType.Brush);
                SetField(fixture.Owner.Layers[fixture.Owner.ActiveLayerIndex], "_brushDisplacements", new Vector3[2]);
            }
            AssertRejected(fixture.Owner, () => BlendShapeImportMenu.TryCreate(fixture.Owner) != null);
        }

        [Test]
        public void MenuCreation_DoesNotMaterializeNullRawGroup()
        {
            using var fixture = new Fixture();
            var groups = (System.Collections.Generic.List<DeformerGroup>)SerializedDeformerReader.Read(fixture.Owner).EmbeddedGroups;
            groups[0] = null;
            int dirty = EditorUtility.GetDirtyCount(fixture.Owner);
            Assert.That(BlendShapeImportMenu.TryCreate(fixture.Owner), Is.Null);
            Assert.That(SerializedDeformerReader.Read(fixture.Owner).EmbeddedGroups, Is.SameAs(groups));
            Assert.That(groups[0], Is.Null);
            Assert.That(EditorUtility.GetDirtyCount(fixture.Owner), Is.EqualTo(dirty));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Import_RejectsNonFiniteFrameBeforeUndo(bool allFrames)
        {
            using var fixture = new Fixture();
            fixture.Source.ClearBlendShapes();
            fixture.Source.AddBlendShapeFrame("Invalid", 100f,
                new[] {new Vector3(float.PositiveInfinity, 0, 0), Vector3.zero, Vector3.zero}, null, null);
            var stored = new Vector3[3];
            fixture.Source.GetBlendShapeFrameVertices(0, 0, stored, null, null);
            Assert.That(float.IsPositiveInfinity(stored[0].x), Is.True, "The rejection input must survive Unity storage.");
            var menu = BlendShapeImportMenu.TryCreate(fixture.Owner);
            Assert.That(menu, Is.Not.Null);
            AssertRejected(fixture.Owner, () => menu.Import(0, allFrames, "invalid frame"));
        }

        [Test]
        public void DisposedSection_DoesNotExecuteOutstandingMenuAction()
        {
            using var fixture = new Fixture();
            var editor = UnityEditor.Editor.CreateEditor(fixture.Owner);
            var section = new BlendShapeInspectorSection(editor, () => Assert.Fail("property callback"),
                () => Assert.Fail("import callback"));
            try
            {
                var action = section.CreateImportAction(BlendShapeImportMenu.TryCreate(fixture.Owner), 0, false);
                section.Dispose();
                string before = EditorJsonUtility.ToJson(fixture.Owner);
                action();
                Assert.That(EditorJsonUtility.ToJson(fixture.Owner), Is.EqualTo(before));
            }
            finally { section.Dispose(); Object.DestroyImmediate(editor); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ImportedData_PrefabVariantApplyAndSaveReloadPreservesBase(bool allFrames)
        {
            using var fixture = new Fixture();
            string folder = "Assets/__BlendShapeInspector_" + Guid.NewGuid().ToString("N");
            GameObject instance = null;
            try
            {
                AssetDatabase.CreateFolder("Assets", folder.Substring(7));
                AssetDatabase.CreateAsset(fixture.Source, folder + "/source.asset");
                var original = PrefabUtility.SaveAsPrefabAsset(fixture.Root, folder + "/base.prefab");
                string baseBefore = EditorJsonUtility.ToJson(original.GetComponent<LatticeDeformer>());
                instance = (GameObject)PrefabUtility.InstantiatePrefab(original);
                var variant = PrefabUtility.SaveAsPrefabAsset(instance, folder + "/variant.prefab");
                Object.DestroyImmediate(instance);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(variant);
                var owner = instance.GetComponent<LatticeDeformer>();
                Assert.That(BlendShapeImportMenu.TryCreate(owner).Import(0, allFrames, "Prefab import"), Is.True);
                AssertImported(owner, allFrames);
                Assert.That(PrefabUtility.HasPrefabInstanceAnyOverrides(instance, false), Is.True);
                PrefabUtility.ApplyPrefabInstance(instance, InteractionMode.AutomatedAction);
                Object.DestroyImmediate(instance); instance = null;
                var loaded = PrefabUtility.LoadPrefabContents(folder + "/variant.prefab");
                try { AssertImported(loaded.GetComponent<LatticeDeformer>(), allFrames); }
                finally { PrefabUtility.UnloadPrefabContents(loaded); }
                Assert.That(EditorJsonUtility.ToJson(original.GetComponent<LatticeDeformer>()), Is.EqualTo(baseBefore));
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private static void AssertImported(LatticeDeformer owner, bool allFrames)
        {
            if (allFrames)
            {
                Assert.That(owner.GroupCount, Is.EqualTo(2));
                Assert.That(owner.ActiveGroupIndex, Is.EqualTo(1));
                Assert.That(owner.ActiveGroup.BlendShapeComposition, Is.EqualTo(BlendShapeCompositionMode.Crossfade));
                Assert.That(owner.ActiveGroup.Layers.Select(l => l.ImportedBlendShapeFrameWeight), Is.EqualTo(new[] {25f,100f}));
                Assert.That(owner.ActiveGroup.Layers[1].GetBrushDisplacement(0), Is.EqualTo(Vector3.up * .7f));
            }
            else
            {
                Assert.That(owner.GroupCount, Is.EqualTo(1));
                Assert.That(owner.ActiveLayerIndex, Is.EqualTo(1));
                Assert.That(owner.Layers[owner.ActiveLayerIndex].Name, Is.EqualTo("Smile"));
            }
            Assert.That(owner.ActiveGroup.Layers[allFrames ? 0 : 1].GetBrushDisplacement(0), Is.EqualTo(Vector3.up * .2f));
        }

        private static void AssertRejected(LatticeDeformer owner, Func<bool> operation)
        {
            string before = EditorJsonUtility.ToJson(owner);
            int dirty = EditorUtility.GetDirtyCount(owner), undo = Undo.GetCurrentGroup();
            var mesh = owner.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(operation(), Is.False);
            Assert.That(EditorJsonUtility.ToJson(owner), Is.EqualTo(before));
            Assert.That(EditorUtility.GetDirtyCount(owner), Is.EqualTo(dirty));
            Assert.That(Undo.GetCurrentGroup(), Is.EqualTo(undo));
            Assert.That(owner.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
        }

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private sealed class Fixture : IDisposable
        {
            internal readonly GameObject Root = new("BlendShape inspector");
            internal readonly Mesh Source;
            internal readonly LatticeDeformer Owner;
            internal Fixture()
            {
                Root.SetActive(false);
                Source = new Mesh {name="BlendShape inspector source", vertices=new[]{Vector3.zero,Vector3.right,Vector3.up},triangles=new[]{0,1,2}};
                Source.RecalculateNormals(); Source.RecalculateBounds();
                SetFrames("Smile", 100f, true);
                Root.AddComponent<MeshFilter>().sharedMesh=Source; Root.AddComponent<MeshRenderer>();
                Owner=Root.AddComponent<LatticeDeformer>(); Owner.Reset();
            }
            internal void SetFrames(string name, float finalWeight, bool second)
            {
                Source.ClearBlendShapes();
                Source.AddBlendShapeFrame(name,25f,new[]{Vector3.up*.2f,Vector3.zero,Vector3.zero},null,null);
                if(second)Source.AddBlendShapeFrame(name,finalWeight,new[]{Vector3.up*.7f,Vector3.zero,Vector3.zero},null,null);
            }
            public void Dispose()
            {
                Undo.ClearAll();
                // Never-active fixtures do not receive Unity's OnDestroy callback.
                if(Owner!=null){Owner.InvalidateCache();Owner.RestoreOriginalMesh();}
                if(Root!=null)Object.DestroyImmediate(Root);
                if(Source!=null&&!EditorUtility.IsPersistent(Source))Object.DestroyImmediate(Source);
            }
        }
    }
}
#endif
