#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Net._32Ba.LatticeDeformationTool.Editor;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class ProfileAuthoringServiceTests
    {
        [Test]
        public void InspectorGroupList_FollowsProfileUndoRedoWithoutReselecting()
        {
            using var f = new Fixture();
            var inspector = UnityEditor.Editor.CreateEditor(f.Target, typeof(LatticeDeformerEditor));
            try
            {
                inspector.CreateInspectorGUI();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var check = typeof(LatticeDeformerEditor).GetMethod("CheckAndRebuildLayers", flags);
                var container = (VisualElement)typeof(LatticeDeformerEditor).GetField("_groupsContainer", flags).GetValue(inspector);
                Assert.That(container.Q<ListView>(), Is.Not.Null);
                Assert.That(ProfileAuthoringService.ChangeSource(f.Target, DeformerDataSource.Profile, f.Profile, "Use profile"), Is.True);
                check.Invoke(inspector, null);
                Assert.That(container.Q<ListView>(), Is.Null);
                Undo.PerformUndo();
                check.Invoke(inspector, null);
                Assert.That(container.Q<ListView>(), Is.Not.Null, "Undo must restore the editable group tree in the existing Inspector.");
                Undo.PerformRedo();
                check.Invoke(inspector, null);
                Assert.That(container.Q<ListView>(), Is.Null);
                Assert.That(ProfileAuthoringService.CopyToEmbedded(f.Target, "Copy profile"), Is.True);
                check.Invoke(inspector, null);
                Assert.That(container.Q<ListView>(), Is.Not.Null);
                Undo.PerformUndo();
                check.Invoke(inspector, null);
                Assert.That(container.Q<ListView>(), Is.Null);
                Undo.PerformRedo();
                check.Invoke(inspector, null);
                Assert.That(container.Q<ListView>(), Is.Not.Null);
            }
            finally { Object.DestroyImmediate(inspector); }
        }

        [Test]
        public void CompatibilityQuery_UsesCurrentRendererWithoutChangingOwnerOrDirtyState()
        {
            using var f = new Fixture();
            var replacement = Object.Instantiate(f.Mesh);
            try
            {
                replacement.triangles = new[] {0, 2, 1};
                f.Target.GetComponent<MeshFilter>().sharedMesh = replacement;
                string owner = EditorJsonUtility.ToJson(f.Target), profile = EditorJsonUtility.ToJson(f.Profile);
                int dirty = EditorUtility.GetDirtyCount(f.Target), profileDirty = EditorUtility.GetDirtyCount(f.Profile);
                Mesh runtime = f.Target.RuntimeMesh;
                Assert.That(ProfileAuthoringService.ReadCompatibility(f.Target, f.Profile), Is.EqualTo(ProfileCompatibilityStatus.TopologyMismatch));
                Assert.That(EditorJsonUtility.ToJson(f.Target), Is.EqualTo(owner));
                Assert.That(EditorJsonUtility.ToJson(f.Profile), Is.EqualTo(profile));
                Assert.That(EditorUtility.GetDirtyCount(f.Target), Is.EqualTo(dirty));
                Assert.That(EditorUtility.GetDirtyCount(f.Profile), Is.EqualTo(profileDirty));
                Assert.That(f.Target.SourceMesh, Is.SameAs(f.Mesh));
                Assert.That(f.Target.RuntimeMesh, Is.SameAs(runtime));
            }
            finally { Object.DestroyImmediate(replacement); }
        }

        [Test]
        public void AssignAndCopy_KeepIndependentPayloadAndOneUndoPerAction()
        {
            using var f = new Fixture();
            string embedded = JsonUtility.ToJson(f.Target);
            string profile = JsonUtility.ToJson(f.Profile);
            Assert.That(ProfileAuthoringService.ChangeSource(f.Target, DeformerDataSource.Profile, f.Profile, "Use profile"), Is.True);
            Assert.That(SerializedDeformerReader.Read(f.Target).EmbeddedGroups.Count, Is.Zero);
            Undo.PerformUndo();
            Assert.That(JsonUtility.ToJson(f.Target), Is.EqualTo(embedded));
            Undo.PerformRedo();
            Assert.That(f.Target.DataSource, Is.EqualTo(DeformerDataSource.Profile));
            Assert.That(ProfileAuthoringService.CopyToEmbedded(f.Target, "Copy profile"), Is.True);
            Assert.That(f.Target.DataSource, Is.EqualTo(DeformerDataSource.Embedded));
            Assert.That(f.Target.Layers[1].BrushDisplacements, Is.EqualTo(f.Profile.Groups[0].Layers[1].BrushDisplacements));
            Assert.That(f.Target.Layers[1], Is.Not.SameAs(f.Profile.Groups[0].Layers[1]));
            Undo.PerformUndo();
            Assert.That(f.Target.DataSource, Is.EqualTo(DeformerDataSource.Profile));
            Assert.That(SerializedDeformerReader.Read(f.Target).EmbeddedGroups.Count, Is.Zero);
            Undo.PerformRedo();
            Assert.That(f.Target.DataSource, Is.EqualTo(DeformerDataSource.Embedded));
            Assert.That(JsonUtility.ToJson(f.Profile), Is.EqualTo(profile));
        }

        [TestCase("groups")]
        [TestCase("mask")]
        [TestCase("future")]
        public void InvalidOwner_RejectsAllCommandsBeforeUndoOrAssetCreation(string corruption)
        {
            using var f = new Fixture();
            if (corruption == "groups") Set(f.Target, "_groups", new List<DeformerGroup> {null});
            if (corruption == "mask") Set(f.Target.Layers[1], "_vertexMask", new float[2]);
            if (corruption == "future") Set(f.Target, "_migrationReleaseIndex", 999);
            var snapshot = new Snapshot(f.Target, f.Profile);
            string path = f.Folder + "/Rejected.asset";
            Assert.That(ProfileAuthoringService.ChangeSource(f.Target, DeformerDataSource.Profile, f.Profile, "Rejected"), Is.False);
            Assert.That(ProfileAuthoringService.CopyToEmbedded(f.Target, "Rejected"), Is.False);
            Assert.That(ProfileAuthoringService.SaveCurrent(f.Target, f.Profile, "Rejected"), Is.False);
            Assert.That(ProfileAuthoringService.CreateAsset(f.Target, path, "Rejected", out var created), Is.False);
            Assert.That(created, Is.Null);
            Assert.That(AssetDatabase.LoadMainAssetAtPath(path), Is.Null);
            snapshot.AssertUnchanged();
        }

        [TestCase("indices")]
        [TestCase("renderer")]
        public void SourceDrift_RejectsSaveWithoutRebindingOriginalData(string change)
        {
            using var f = new Fixture();
            Mesh replacement = null;
            try
            {
                if (change == "indices") f.Mesh.triangles = new[] {0,2,1};
                else
                {
                    replacement = Object.Instantiate(f.Mesh);
                    f.Target.GetComponent<MeshFilter>().sharedMesh = replacement;
                }
                var before = new Snapshot(f.Target, f.Profile);
                Assert.That(ProfileAuthoringService.SaveCurrent(f.Target, f.Profile, "Rejected"), Is.False);
                before.AssertUnchanged();
            }
            finally { if (replacement != null) Object.DestroyImmediate(replacement); }
        }

        [Test]
        public void IncompatibleProfile_RejectsAssignmentAndCopyWithoutUndoOrDirtyChanges()
        {
            using var f = new Fixture();
            var other = Object.Instantiate(f.Mesh);
            try
            {
                other.triangles = new[] {0,2,1};
                f.Profile.Capture(f.Target.Groups, 0, other);
                var before = new Snapshot(f.Target, f.Profile);
                Assert.That(ProfileAuthoringService.ChangeSource(f.Target, DeformerDataSource.Profile, f.Profile, "Rejected"), Is.False);
                Assert.That(ProfileAuthoringService.CopyToEmbedded(f.Target, "Rejected"), Is.False);
                before.AssertUnchanged();
            }
            finally { Object.DestroyImmediate(other); }
        }

        [Test]
        public void SaveCurrent_PersistsOnlyDestinationAndSupportsUndoRedo()
        {
            using var f = new Fixture();
            string otherPath = f.Folder + "/Other.asset";
            var other = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            AssetDatabase.CreateAsset(other, otherPath);
            byte[] savedOther = File.ReadAllBytes(otherPath);
            other.Author = "Unrelated pending edit";
            EditorUtility.SetDirty(other);
            f.Target.Layers[1].SetBrushDisplacement(0, Vector3.forward * 0.25f);
            string owner = EditorJsonUtility.ToJson(f.Target);
            int dirty = EditorUtility.GetDirtyCount(f.Target);
            Assert.That(ProfileAuthoringService.SaveCurrent(f.Target, f.Profile, "Save profile"), Is.True);
            Assert.That(f.Profile.Groups[0].Layers[1].GetBrushDisplacement(0).z, Is.EqualTo(0.25f));
            Assert.That(ProfileAuthoringService.ReadCompatibility(f.Target, f.Profile), Is.EqualTo(ProfileCompatibilityStatus.ExactMatch));
            Assert.That(f.Profile.Author, Is.EqualTo("Preserved author"));
            Assert.That(f.Profile.name, Is.EqualTo("Profile"));
            Assert.That(EditorJsonUtility.ToJson(f.Target), Is.EqualTo(owner));
            Assert.That(EditorUtility.GetDirtyCount(f.Target), Is.EqualTo(dirty));
            Assert.That(File.ReadAllBytes(otherPath), Is.EqualTo(savedOther));
            Assert.That(EditorUtility.IsDirty(other), Is.True);
            string savedProfile = File.ReadAllText(f.Folder + "/Profile.asset");
            Assert.That(savedProfile, Does.Contain("0.25"));
            Undo.PerformUndo();
            Assert.That(f.Profile.Groups[0].Layers[1].GetBrushDisplacement(0).z, Is.EqualTo(0.1f));
            Undo.PerformRedo();
            Assert.That(f.Profile.Groups[0].Layers[1].GetBrushDisplacement(0).z, Is.EqualTo(0.25f));
            Assert.That(File.ReadAllBytes(otherPath), Is.EqualTo(savedOther));
        }

        [Test]
        public void ProfileModeSave_PreservesOwnerSelectionAndInstanceEdits()
        {
            using var f = new Fixture();
            f.Target.AddGroup("Second group");
            f.Target.ActiveGroupIndex = 1;
            Assert.That(f.Target.SaveToProfile(f.Profile), Is.True);
            Assert.That(f.Target.UseProfile(f.Profile), Is.True);
            Assert.That(f.Profile.ActiveGroupIndex, Is.EqualTo(1));
            f.Target.ActiveGroupIndex = 0;
            f.Target.Groups[0].Name = "Instance group";
            f.Target.Layers[1].SetBrushDisplacement(2, Vector3.up * 0.75f);
            string owner = JsonUtility.ToJson(f.Target);
            Assert.That(ProfileAuthoringService.SaveCurrent(f.Target, f.Profile, "Save edits"), Is.True);
            Assert.That(f.Profile.Groups[0].Name, Is.EqualTo("Instance group"));
            Assert.That(f.Profile.Groups[0].Layers[1].GetBrushDisplacement(2).y, Is.EqualTo(0.75f));
            Assert.That(f.Profile.ActiveGroupIndex, Is.Zero);
            Assert.That(JsonUtility.ToJson(f.Target), Is.EqualTo(owner));
        }

        [Test]
        public void ReadOnlyDestination_RejectsWithoutChangingMemoryOrFile()
        {
            using var f = new Fixture();
            string path = f.Folder + "/Profile.asset";
            byte[] bytes = File.ReadAllBytes(path);
            FileAttributes attributes = File.GetAttributes(path);
            try
            {
                File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
                var before = new Snapshot(f.Target, f.Profile);
                Assert.That(ProfileAuthoringService.SaveCurrent(f.Target, f.Profile, "Rejected"), Is.False);
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
                before.AssertUnchanged();
            }
            finally { File.SetAttributes(path, attributes); }
        }

        [Test]
        public void CreateAsset_RejectsExistingPathThenCreatesAndAssignsNewAsset()
        {
            using var f = new Fixture();
            string existing = f.Folder + "/Profile.asset";
            byte[] bytes = File.ReadAllBytes(existing);
            var before = new Snapshot(f.Target, f.Profile);
            Assert.That(ProfileAuthoringService.CreateAsset(f.Target, existing, "Rejected", out _), Is.False);
            Assert.That(File.ReadAllBytes(existing), Is.EqualTo(bytes));
            before.AssertUnchanged();
            string newPath = f.Folder + "/Created.asset";
            Assert.That(ProfileAuthoringService.CreateAsset(f.Target, newPath, "Create profile", out var created), Is.True);
            Assert.That(f.Target.Profile, Is.SameAs(created));
            Assert.That(f.Target.DataSource, Is.EqualTo(DeformerDataSource.Profile));
            Assert.That(AssetDatabase.LoadAssetAtPath<MeshDeformerProfile>(newPath), Is.SameAs(created));
            Assert.That(ProfileAuthoringService.ReadCompatibility(f.Target, created), Is.EqualTo(ProfileCompatibilityStatus.ExactMatch));
            Undo.PerformUndo();
            Assert.That(f.Target.Profile, Is.SameAs(f.Profile));
            Assert.That(f.Target.DataSource, Is.EqualTo(DeformerDataSource.Embedded));
            Assert.That(created, Is.Not.Null);
            Undo.PerformRedo();
            Assert.That(f.Target.Profile, Is.SameAs(created));
        }

        [Test]
        public void PrefabVariant_AssignmentAndCopySurviveUndoRedoAndSaveReload()
        {
            using var f = new Fixture();
            string basePath=f.Folder+"/Base.prefab", variantPath=f.Folder+"/Variant.prefab";
            var prefab=PrefabUtility.SaveAsPrefabAsset(f.Target.gameObject, basePath);
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab);
            GameObject reload=null;
            try
            {
                var d=instance.GetComponent<LatticeDeformer>();
                Assert.That(ProfileAuthoringService.ChangeSource(d,DeformerDataSource.Profile,f.Profile,"Assign"),Is.True);
                Undo.PerformUndo();
                Assert.That(d.DataSource,Is.EqualTo(DeformerDataSource.Embedded));
                Undo.PerformRedo();
                Assert.That(d.DataSource,Is.EqualTo(DeformerDataSource.Profile));
                Assert.That(ProfileAuthoringService.CopyToEmbedded(d,"Copy"),Is.True);
                Undo.PerformUndo();
                Assert.That(d.DataSource,Is.EqualTo(DeformerDataSource.Profile));
                Undo.PerformRedo();
                var variant=PrefabUtility.SaveAsPrefabAsset(instance,variantPath);
                reload=(GameObject)PrefabUtility.InstantiatePrefab(variant);
                var restored=reload.GetComponent<LatticeDeformer>();
                Assert.That(restored.DataSource,Is.EqualTo(DeformerDataSource.Embedded));
                Assert.That(restored.Layers[1].BrushDisplacements,Is.EqualTo(f.Profile.Groups[0].Layers[1].BrushDisplacements));
                Assert.That(prefab.GetComponent<LatticeDeformer>().DataSource,Is.EqualTo(DeformerDataSource.Embedded));
            }
            finally
            {
                if(reload!=null)Object.DestroyImmediate(reload);
                Object.DestroyImmediate(instance);
            }
        }

        private static void Set(object owner,string name,object value) => owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(owner,value);

        private sealed class Snapshot
        {
            private readonly LatticeDeformer _owner;
            private readonly MeshDeformerProfile _profile;
            private readonly string _ownerJson,_profileJson;
            private readonly int _ownerDirty,_profileDirty,_undo;
            private readonly Mesh _source,_runtime,_renderer;
            private readonly IReadOnlyList<DeformerGroup> _groups;
            private readonly DeformerGroup[] _groupEntries;
            private readonly DeformerDataSource _dataSource;
            private readonly int _selection;
            internal Snapshot(LatticeDeformer owner,MeshDeformerProfile profile)
            {
                _owner=owner;_profile=profile;
                var raw=SerializedDeformerReader.Read(owner);
                _groups=raw.EmbeddedGroups;_groupEntries=_groups?.ToArray();
                _dataSource=raw.DataSource;_selection=raw.EmbeddedActiveGroupIndex;
                // Unity's serializer materializes null inline objects. Do not let
                // the observation itself repair the input of a rejection test.
                _ownerJson=_groups!=null&&_groups.All(g=>g!=null)?EditorJsonUtility.ToJson(owner):null;
                _profileJson=EditorJsonUtility.ToJson(profile);
                _ownerDirty=EditorUtility.GetDirtyCount(owner);_profileDirty=EditorUtility.GetDirtyCount(profile);
                _undo=Undo.GetCurrentGroup();_source=owner.SourceMesh;_runtime=owner.RuntimeMesh;_renderer=owner.GetComponent<MeshFilter>().sharedMesh;
            }
            internal void AssertUnchanged()
            {
                if(_ownerJson!=null)Assert.That(EditorJsonUtility.ToJson(_owner),Is.EqualTo(_ownerJson));
                else
                {
                    var raw=SerializedDeformerReader.Read(_owner);
                    Assert.That(raw.EmbeddedGroups,Is.SameAs(_groups));
                    Assert.That(raw.EmbeddedGroups,Is.EqualTo(_groupEntries));
                    Assert.That(raw.DataSource,Is.EqualTo(_dataSource));
                    Assert.That(raw.EmbeddedActiveGroupIndex,Is.EqualTo(_selection));
                }
                Assert.That(EditorJsonUtility.ToJson(_profile),Is.EqualTo(_profileJson));
                Assert.That(EditorUtility.GetDirtyCount(_owner),Is.EqualTo(_ownerDirty));
                Assert.That(EditorUtility.GetDirtyCount(_profile),Is.EqualTo(_profileDirty));
                Assert.That(Undo.GetCurrentGroup(),Is.EqualTo(_undo));
                Assert.That(_owner.SourceMesh,Is.SameAs(_source));
                Assert.That(_owner.RuntimeMesh,Is.SameAs(_runtime));
                Assert.That(_owner.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(_renderer));
            }
        }

        private sealed class Fixture:IDisposable
        {
            internal readonly string Folder;
            internal readonly Mesh Mesh;
            internal readonly MeshDeformerProfile Profile;
            internal readonly LatticeDeformer Target;
            internal Fixture()
            {
                string folder="__ProfileAuthoring_"+Guid.NewGuid().ToString("N");
                AssetDatabase.CreateFolder("Assets",folder);Folder="Assets/"+folder;
                Mesh=new Mesh {name="Profile source",vertices=new[]{Vector3.zero,Vector3.right,Vector3.up},triangles=new[]{0,1,2}};
                Mesh.RecalculateNormals();Mesh.RecalculateBounds();AssetDatabase.CreateAsset(Mesh,Folder+"/Source.asset");
                var go=new GameObject("Profile authoring");go.SetActive(false);
                go.AddComponent<MeshFilter>().sharedMesh=Mesh;go.AddComponent<MeshRenderer>();
                Target=go.AddComponent<LatticeDeformer>();Target.Reset();Target.AddLayer("Brush",MeshDeformerLayerType.Brush);
                Target.ActiveLayerIndex=1;Target.Layers[1].BrushDisplacements=new[]{Vector3.forward*0.1f,Vector3.up*0.2f,Vector3.right*0.3f};Target.Deform(false);
                Profile=ScriptableObject.CreateInstance<MeshDeformerProfile>();Profile.name="Profile";Profile.Author="Preserved author";
                Assert.That(Target.SaveToProfile(Profile),Is.True);Target.Profile=Profile;
                AssetDatabase.CreateAsset(Profile,Folder+"/Profile.asset");
            }
            public void Dispose()
            {
                if(Target!=null){Undo.ClearUndo(Target);Object.DestroyImmediate(Target.gameObject);}
                if(Profile!=null)Undo.ClearUndo(Profile);
                AssetDatabase.DeleteAsset(Folder);
            }
        }
    }
}
#endif
