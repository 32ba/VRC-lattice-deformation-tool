#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class DeformerAuthoringBoundaryTests
    {
        [TearDown]
        public void TearDown() => Undo.ClearAll();

        [TestCase(0)]
        [TestCase(99)]
        public void Read_DoesNotMigrateOrNormalizeStoredSelection(int version)
        {
            using var fixture = new Fixture();
            var deformer = fixture.Deformer;
            SetField(deformer, "_deformationDataVersion", (DeformationDataVersion)version);
            SetField(deformer, "_activeGroupIndex", -7);
            string before = EditorJsonUtility.ToJson(deformer);
            int dirty = EditorUtility.GetDirtyCount(deformer);

            var data = SerializedDeformerReader.Read(deformer);
            MeshDeformerValidator.ComputeInspectorStructureHash(deformer);

            Assert.That(data.ActiveGroupIndex, Is.EqualTo(-7));
            Assert.That(EditorJsonUtility.ToJson(deformer), Is.EqualTo(before));
            Assert.That(EditorUtility.GetDirtyCount(deformer), Is.EqualTo(dirty));
        }

        [Test]
        public void Validate_NullGroupStorageIsReportedWithoutInitialization()
        {
            using var fixture = new Fixture();
            SetField(fixture.Deformer, "_groups", null);
            string before = EditorJsonUtility.ToJson(fixture.Deformer);
            int dirty = EditorUtility.GetDirtyCount(fixture.Deformer);

            var diagnostics = MeshDeformerValidator.Validate(fixture.Deformer);

            Assert.That(diagnostics.Any(d => d.Code == MeshDeformerValidator.InvalidGroupStructure), Is.True);
            Assert.That(SerializedDeformerReader.Read(fixture.Deformer).Groups, Is.Null);
            Assert.That(EditorJsonUtility.ToJson(fixture.Deformer), Is.EqualTo(before));
            Assert.That(EditorUtility.GetDirtyCount(fixture.Deformer), Is.EqualTo(dirty));
        }

        [Test]
        public void Validate_NullSettingsAndInvalidLayerSelectionRemainInspectable()
        {
            using var fixture = new Fixture();
            var group = fixture.Deformer.ActiveGroup;
            var layer = group.Layers[0];
            SetField(layer, "_settings", null);
            group.SetSerializedActiveLayerIndex(42);
            string before = EditorJsonUtility.ToJson(fixture.Deformer);

            var diagnostics = MeshDeformerValidator.Validate(fixture.Deformer);

            Assert.That(diagnostics.Any(d => d.Code == MeshDeformerValidator.InvalidGridSize), Is.True);
            Assert.That(diagnostics.Any(d => d.Code == MeshDeformerValidator.InvalidLayerStructure), Is.True);
            Assert.That(layer.SerializedSettings, Is.Null);
            Assert.That(group.SerializedActiveLayerIndex, Is.EqualTo(42));
            Assert.That(EditorJsonUtility.ToJson(fixture.Deformer), Is.EqualTo(before));
        }

        [Test]
        public void Validate_ProfileReadsAuthoredPayloadWithoutExpandingOrClearingInstanceData()
        {
            using var fixture = new Fixture();
            var deformer = fixture.Deformer;
            var profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            try
            {
                Assert.That(deformer.SaveToProfile(profile), Is.True);
                // Reproduce a serialized Profile reference with residual embedded data.
                // Public setters would already expand the Profile before this test.
                SetField(deformer, "_dataSource", DeformerDataSource.Profile);
                SetField(deformer, "_profile", profile);
                var embedded = SerializedDeformerReader.Read(deformer).EmbeddedGroups;
                string before = EditorJsonUtility.ToJson(deformer);
                string profileBefore = EditorJsonUtility.ToJson(profile);
                int dirty = EditorUtility.GetDirtyCount(deformer);
                int profileDirty = EditorUtility.GetDirtyCount(profile);
                Assert.That(GetField(deformer, "_profileGroups"), Is.Null);

                var diagnostics = MeshDeformerValidator.Validate(deformer);
                MeshDeformerValidator.ComputeInspectorStructureHash(deformer);

                Assert.That(MeshDeformerValidator.HasErrors(diagnostics), Is.False);
                Assert.That(GetField(deformer, "_profileGroups"), Is.Null);
                Assert.That(SerializedDeformerReader.Read(deformer).EmbeddedGroups, Is.SameAs(embedded));
                Assert.That(embedded.Count, Is.EqualTo(1));
                Assert.That(EditorJsonUtility.ToJson(deformer), Is.EqualTo(before));
                Assert.That(EditorJsonUtility.ToJson(profile), Is.EqualTo(profileBefore));
                Assert.That(EditorUtility.GetDirtyCount(deformer), Is.EqualTo(dirty));
                Assert.That(EditorUtility.GetDirtyCount(profile), Is.EqualTo(profileDirty));
            }
            finally { Object.DestroyImmediate(profile); }
        }

        [Test]
        public void Validate_ProfileReportsCorruptBrushPayloadWithoutCreatingEvaluationCopies()
        {
            using var fixture = new Fixture();
            var deformer = fixture.Deformer;
            deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
            var profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            try
            {
                Assert.That(deformer.SaveToProfile(profile), Is.True);
                SetField(profile.SerializedGroups[0].SerializedLayers[1], "_brushDisplacements", new Vector3[1]);
                SetField(deformer, "_dataSource", DeformerDataSource.Profile);
                SetField(deformer, "_profile", profile);
                string before = EditorJsonUtility.ToJson(deformer);
                string profileBefore = EditorJsonUtility.ToJson(profile);

                var diagnostics = MeshDeformerValidator.Validate(deformer);

                Assert.That(diagnostics.Any(d => d.Code == MeshDeformerValidator.BrushLengthMismatch), Is.True);
                Assert.That(diagnostics.All(d => d.Fix == null), Is.True);
                Assert.That(GetField(deformer, "_profileGroups"), Is.Null);
                Assert.That(EditorJsonUtility.ToJson(deformer), Is.EqualTo(before));
                Assert.That(EditorJsonUtility.ToJson(profile), Is.EqualTo(profileBefore));
            }
            finally { Object.DestroyImmediate(profile); }
        }

        [TestCase(0, 2, 1, 0)]
        [TestCase(2, 0, 1, 2)]
        [TestCase(1, 2, 1, 2)]
        public void MoveGroup_PreservesSelectedGroupAndUndoRedo(int oldIndex, int newIndex, int active, int expected)
        {
            using var fixture = new Fixture();
            var d = fixture.Deformer;
            d.AddGroup("Second");
            d.AddLayer("Second Layer", MeshDeformerLayerType.Brush);
            d.AddGroup("Third");
            d.AddLayer("Third Layer", MeshDeformerLayerType.Brush);
            d.ActiveGroupIndex = active;
            string selectedName = d.ActiveGroup.Name;
            string before = EditorJsonUtility.ToJson(d);

            Assert.That(DeformerEditService.MoveGroup(d, oldIndex, newIndex, "Move Group"), Is.True);
            Assert.That(d.ActiveGroupIndex, Is.EqualTo(expected));
            Assert.That(d.ActiveGroup.Name, Is.EqualTo(selectedName));
            string after = EditorJsonUtility.ToJson(d);

            Undo.PerformUndo();
            Assert.That(EditorJsonUtility.ToJson(d), Is.EqualTo(before));
            Undo.PerformRedo();
            Assert.That(EditorJsonUtility.ToJson(d), Is.EqualTo(after));
        }

        [Test]
        public void DuplicateGroup_PreservesOutputAndCreatesIndependentEditablePayload()
        {
            using var fixture = new Fixture();
            var d = fixture.Deformer;
            d.AddLayer("Brush", MeshDeformerLayerType.Brush);
            var source = d.ActiveGroup;
            source.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
            source.BlendShapeName = "Original";
            source.BlendShapeComposition = BlendShapeCompositionMode.Crossfade;
            source.Layers[1].SetBrushDisplacement(0, Vector3.up);
            source.Layers[1].VertexMask = new[] { 0.5f, 1f, 1f, 1f };
            source.Layers[1].SetImportedBlendShapeFrameWeight(30f);
            string original = JsonUtility.ToJson(source);
            string before = EditorJsonUtility.ToJson(d);

            Assert.That(DeformerEditService.DuplicateGroup(d, 0, "Duplicate Group"), Is.True);
            var copy = d.ActiveGroup;
            Assert.That(d.ActiveGroupIndex, Is.EqualTo(1));
            Assert.That(copy.Name, Is.EqualTo(source.Name + " Copy"));
            Assert.That(copy.BlendShapeComposition, Is.EqualTo(source.BlendShapeComposition));
            Assert.That(copy.BlendShapeCurve, Is.Not.SameAs(source.BlendShapeCurve));
            Assert.That(copy.Layers[1].ImportedBlendShapeFrameWeight, Is.EqualTo(30f));
            Assert.That(copy.Layers[1].VertexMask, Is.EqualTo(source.Layers[1].VertexMask));
            Assert.That(copy.Layers[1].VertexMask, Is.Not.SameAs(source.Layers[1].VertexMask));
            Assert.That(copy.Layers[1].BrushDisplacements, Is.Not.SameAs(source.Layers[1].BrushDisplacements));
            copy.Layers[1].SetBrushDisplacement(0, Vector3.right);
            Assert.That(JsonUtility.ToJson(source), Is.EqualTo(original));

            Undo.PerformUndo();
            Assert.That(EditorJsonUtility.ToJson(d), Is.EqualTo(before));
        }

        [Test]
        public void PasteGroup_AcrossComponentsPreservesDataAndSourceMesh()
        {
            using var source = new Fixture();
            using var destination = new Fixture();
            source.Deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
            source.Deformer.ActiveGroup.Layers[1].SetBrushDisplacement(0, Vector3.up);
            string groupJson = JsonUtility.ToJson(source.Deformer.ActiveGroup);
            string before = EditorJsonUtility.ToJson(destination.Deformer);

            Assert.That(DeformerEditService.PasteGroup(destination.Deformer, groupJson, "Paste Group"), Is.True);
            Assert.That(JsonUtility.ToJson(destination.Deformer.ActiveGroup), Is.EqualTo(groupJson));
            Assert.That(destination.Deformer.Deform(false).vertices[0], Is.EqualTo(Vector3.up));
            Assert.That(destination.Mesh.vertices[0], Is.EqualTo(Vector3.zero));
            Assert.That(destination.Filter.sharedMesh, Is.SameAs(destination.Mesh));
            Undo.PerformUndo();
            Assert.That(EditorJsonUtility.ToJson(destination.Deformer), Is.EqualTo(before));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailedEdit_RestoresPayloadAndPreservesEarlierUndo(bool throwAfterEdit)
        {
            using var fixture = new Fixture();
            var d = fixture.Deformer;
            string originalName = d.ActiveGroup.Name;
            Assert.That(DeformerEditService.Execute(d, "Earlier Edit", target =>
            {
                target.ActiveGroup.Name = "Earlier";
                return true;
            }), Is.True);
            string before = EditorJsonUtility.ToJson(d);

            Func<bool> run = () => DeformerEditService.Execute(d, "Failed Edit", target =>
            {
                target.AddGroup("Must Roll Back");
                if (throwAfterEdit) throw new InvalidOperationException("Injected failure");
                return false;
            });
            if (throwAfterEdit) Assert.Throws<InvalidOperationException>(() => run());
            else Assert.That(run(), Is.False);

            Assert.That(EditorJsonUtility.ToJson(d), Is.EqualTo(before));
            Undo.PerformUndo();
            Assert.That(d.ActiveGroup.Name, Is.EqualTo(originalName));
        }

        [Test]
        public void ProfileEdit_IsRejectedWithoutChangingInstanceOrAsset()
        {
            using var fixture = new Fixture();
            var d = fixture.Deformer;
            var profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            try
            {
                Assert.That(d.SaveToProfile(profile), Is.True);
                SetField(d, "_dataSource", DeformerDataSource.Profile);
                SetField(d, "_profile", profile);
                string before = EditorJsonUtility.ToJson(d);
                string profileBefore = EditorJsonUtility.ToJson(profile);

                Assert.That(DeformerEditService.DuplicateGroup(d, 0, "Duplicate"), Is.False);
                Assert.That(DeformerEditService.AddGroup(d, "Add"), Is.False);
                Assert.That(EditorJsonUtility.ToJson(d), Is.EqualTo(before));
                Assert.That(EditorJsonUtility.ToJson(profile), Is.EqualTo(profileBefore));
                Assert.That(GetField(d, "_profileGroups"), Is.Null);
            }
            finally { Object.DestroyImmediate(profile); }
        }

        [Test]
        public void FutureVersionEdit_IsRejectedWithoutRewritingThePayload()
        {
            using var fixture = new Fixture();
            SetField(fixture.Deformer, "_deformationDataVersion", (DeformationDataVersion)99);
            string before = EditorJsonUtility.ToJson(fixture.Deformer);
            Assert.That(DeformerEditService.AddGroup(fixture.Deformer, "Add"), Is.False);
            Assert.That(EditorJsonUtility.ToJson(fixture.Deformer), Is.EqualTo(before));
        }

        [Test]
        public void InspectorGroupOperations_RefreshEvaluationAndKeepUndoRedoPayload()
        {
            using var fixture = new Fixture();
            var d = fixture.Deformer;
            d.AddLayer("Brush", MeshDeformerLayerType.Brush);
            d.ActiveGroup.Layers[1].SetBrushDisplacement(0, Vector3.up);
            var editor = UnityEditor.Editor.CreateEditor(d);
            try
            {
                editor.CreateInspectorGUI();
                d.Deform(false);
                string before = EditorJsonUtility.ToJson(d);

                InvokeInspector(editor, "DuplicateGroup", d, 0);
                Assert.That(d.GroupCount, Is.EqualTo(2));
                Assert.That(d.ActiveGroupIndex, Is.EqualTo(1));
                Assert.That(d.Deform(false).vertices[0], Is.EqualTo(Vector3.up * 2f));
                string duplicated = EditorJsonUtility.ToJson(d);
                Undo.PerformUndo();
                Assert.That(EditorJsonUtility.ToJson(d), Is.EqualTo(before));
                Undo.PerformRedo();
                Assert.That(EditorJsonUtility.ToJson(d), Is.EqualTo(duplicated));

                InvokeInspector(editor, "CopyGroup", d, 0);
                InvokeInspector(editor, "PasteGroup", d);
                Assert.That(d.GroupCount, Is.EqualTo(3));
                Assert.That(d.ActiveGroupIndex, Is.EqualTo(2));
                string pasted = EditorJsonUtility.ToJson(d);

                InvokeInspector(editor, "OnGroupReordered", 2, 0);
                Assert.That(d.ActiveGroupIndex, Is.Zero);
                Assert.That(d.ActiveGroup.Name, Is.EqualTo("Group"));
                Undo.PerformUndo();
                Assert.That(EditorJsonUtility.ToJson(d), Is.EqualTo(pasted));
                Assert.That(fixture.Mesh.vertices[0], Is.EqualTo(Vector3.zero));
            }
            finally
            {
                Object.DestroyImmediate(editor);
                typeof(LatticeDeformerEditor).GetField("s_copiedGroupJson",
                    BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            }
        }

        [Test]
        public void AddAndRemoveGroup_KeepSelectionAndRejectRemovingFinalGroup()
        {
            using var fixture = new Fixture();
            var d = fixture.Deformer;
            string before = EditorJsonUtility.ToJson(d);
            Assert.That(DeformerEditService.AddGroup(d, "Add Group"), Is.True);
            Assert.That(d.GroupCount, Is.EqualTo(2));
            Assert.That(d.ActiveGroupIndex, Is.EqualTo(1));
            string added = EditorJsonUtility.ToJson(d);
            Assert.That(DeformerEditService.RemoveGroup(d, 1, "Remove Group"), Is.True);
            Assert.That(d.GroupCount, Is.EqualTo(1));
            Assert.That(d.ActiveGroupIndex, Is.Zero);
            Assert.That(DeformerEditService.RemoveGroup(d, 0, "Remove Final Group"), Is.False);
            Undo.PerformUndo();
            Assert.That(EditorJsonUtility.ToJson(d), Is.EqualTo(added));
            Undo.PerformUndo();
            Assert.That(EditorJsonUtility.ToJson(d), Is.EqualTo(before));
        }

        private static void InvokeInspector(UnityEditor.Editor editor, string name, params object[] args) =>
            typeof(LatticeDeformerEditor).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(editor, args);

        [Test]
        public void GroupEdit_RecordsPrefabOverrideAndSurvivesApplyAndReload()
        {
            string folder = "Assets/__DeformerAuthoring_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            using var fixture = new Fixture();
            GameObject instance = null;
            GameObject loaded = null;
            try
            {
                AssetDatabase.CreateAsset(fixture.Mesh, folder + "/Source.asset");
                string path = folder + "/Subject.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(fixture.Root, path);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var d = instance.GetComponent<LatticeDeformer>();
                Assert.That(DeformerEditService.DuplicateGroup(d, 0, "Duplicate"), Is.True);
                Assert.That(PrefabUtility.GetPropertyModifications(d).Any(
                    p => p.propertyPath.StartsWith("_groups", StringComparison.Ordinal)), Is.True);
                PrefabUtility.ApplyPrefabInstance(instance, InteractionMode.AutomatedAction);
                Object.DestroyImmediate(instance);
                instance = null;

                loaded = PrefabUtility.LoadPrefabContents(path);
                var reloaded = loaded.GetComponent<LatticeDeformer>();
                Assert.That(reloaded.GroupCount, Is.EqualTo(2));
                Assert.That(reloaded.ActiveGroupIndex, Is.EqualTo(1));
                Assert.That(reloaded.ActiveGroup.Name, Is.EqualTo("Group Copy"));
                Assert.That(reloaded.GetComponent<MeshFilter>().sharedMesh,
                    Is.SameAs(AssetDatabase.LoadAssetAtPath<Mesh>(folder + "/Source.asset")));
            }
            finally
            {
                if (loaded != null) PrefabUtility.UnloadPrefabContents(loaded);
                if (instance != null) Object.DestroyImmediate(instance);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static object GetField(object target, string name) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        private sealed class Fixture : IDisposable
        {
            internal readonly GameObject Root = new GameObject("Authoring Boundary");
            internal readonly Mesh Mesh;
            internal readonly MeshFilter Filter;
            internal readonly LatticeDeformer Deformer;

            internal Fixture()
            {
                Mesh = new Mesh { name = "Source" };
                Mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one };
                Mesh.triangles = new[] { 0, 1, 2, 1, 3, 2 };
                Mesh.RecalculateBounds();
                Filter = Root.AddComponent<MeshFilter>();
                Filter.sharedMesh = Mesh;
                Root.AddComponent<MeshRenderer>();
                Deformer = Root.AddComponent<LatticeDeformer>();
                Deformer.Reset();
            }

            public void Dispose()
            {
                Object.DestroyImmediate(Root);
                if (Mesh != null) Object.DestroyImmediate(Mesh, AssetDatabase.Contains(Mesh));
            }
        }
    }
}
#endif
