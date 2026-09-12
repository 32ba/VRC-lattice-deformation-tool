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
    public sealed class SourceBindingPreservationTests
    {
        [TestCase("missing-mesh", false)]
        [TestCase("missing-mesh", true)]
        [TestCase("missing-renderer-reference", false)]
        [TestCase("missing-renderer-reference", true)]
        [TestCase("replacement", false)]
        [TestCase("replacement", true)]
        [TestCase("lost-source-reference", false)]
        [TestCase("lost-source-reference", true)]
        public void ReadAndEnable_RejectUnavailableBindingWithoutErasingPayloadOrBaseline(string scenario, bool activate)
        {
            using var fixture = new Fixture();
            var before = Snapshot(fixture.Owner);
            fixture.BreakBinding(scenario);
            var assigned = Snapshot(fixture.Owner);
            Mesh displayed = fixture.Filter.sharedMesh;
            if (activate) fixture.Root.SetActive(true);
            else _ = fixture.Owner.Groups.Count;
            Assert.That(Snapshot(fixture.Owner), Is.EqualTo(assigned));
            Assert.That(fixture.Owner.Deform(), Is.Null);
            Assert.That(Snapshot(fixture.Owner), Is.EqualTo(assigned));
            Assert.That(fixture.Filter.sharedMesh, Is.SameAs(displayed));
            string diagnostic = scenario == "missing-mesh" ? MeshDeformerValidator.MissingSourceMesh :
                scenario == "missing-renderer-reference" ? MeshDeformerValidator.MissingRenderer : MeshDeformerValidator.SourceMeshChanged;
            Assert.That(MeshDeformerValidator.Validate(fixture.Owner).Any(d => d.Code == diagnostic), Is.True);
            fixture.Owner.enabled = false;
            Assert.That(fixture.Filter.sharedMesh, Is.SameAs(displayed), "Lifecycle cleanup must not undo an external mesh edit.");
            fixture.RestoreBinding();
            fixture.Owner.enabled = true;
            var output = fixture.Owner.Deform(false);
            Assert.That(output, Is.Not.Null);
            Assert.That(output.vertices[0], Is.EqualTo(fixture.Source.vertices[0] + Fixture.Delta));
            Assert.That(Snapshot(fixture.Owner), Is.EqualTo(before));
        }

        [Test]
        public void MissingBinding_DoesNotAdvancePendingReleaseOrExplicitlyResetData()
        {
            using var fixture = new Fixture();
            Set(fixture.Owner, "_migrationReleaseIndex", DeformationReleaseManifest.Current - 1);
            fixture.Filter.sharedMesh = null;
            var before = Snapshot(fixture.Owner);
            Assert.That(fixture.Owner.TryUpgradePublishedDeformationDataOneRelease(), Is.False);
            _ = fixture.Owner.Groups.Count;
            fixture.Owner.Reset();
            Assert.That(Snapshot(fixture.Owner), Is.EqualTo(before));
            fixture.Filter.sharedMesh = fixture.Source;
            Assert.That(fixture.Owner.TryUpgradePublishedDeformationDataOneRelease(), Is.True);
            Assert.That(Get<int>(fixture.Owner, "_migrationReleaseIndex"), Is.EqualTo(DeformationReleaseManifest.Current));
            Assert.That(fixture.Owner.Deform(false).vertices[0], Is.EqualTo(Fixture.Delta));
        }

        [TestCase(DeformationDataVersion.Unversioned)]
        [TestCase(DeformationDataVersion.V1_2_1)]
        public void LostSourceInGroupPayload_RejectsClassificationAndMigrationWithoutLegacyProvenance(DeformationDataVersion version)
        {
            using var fixture = new Fixture();
            Set(fixture.Owner, "_deformationDataVersion", version);
            Set(fixture.Owner, "_deformationDataSourceVersion", DeformationDataVersion.Unversioned);
            Set(fixture.Owner, "_migrationReleaseIndex", DeformationReleaseManifest.Unclassified);
            Set(fixture.Owner, "_serializedSourceMesh", null);
            string before = Snapshot(fixture.Owner);
            Assert.That(fixture.Owner.TryUpgradePublishedDeformationDataOneRelease(), Is.False);
            Assert.That(Snapshot(fixture.Owner), Is.EqualTo(before));
            Assert.That(fixture.Owner.TryUpgradeDeformationDataOneRelease(), Is.False);
            Assert.That(Snapshot(fixture.Owner), Is.EqualTo(before));
        }

        [Test]
        public void ExplicitReset_RebindsCurrentDataAndUndoRestoresThePreviousPayload()
        {
            using var fixture = new Fixture();
            var before = Snapshot(fixture.Owner);
            fixture.Filter.sharedMesh = fixture.Replacement;
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(fixture.Owner, "Explicit source reset");
            fixture.Owner.Reset();
            Undo.FlushUndoRecordObjects();
            var data = SerializedDeformerReader.Read(fixture.Owner);
            Assert.That(data.SourceMesh, Is.SameAs(fixture.Replacement));
            Assert.That(data.SourceTopologyHash, Is.EqualTo(SourceMeshTopology.Calculate(fixture.Replacement)));
            Assert.That(data.ActiveLayers[1].BrushDisplacements, Is.All.EqualTo(Vector3.zero));
            Undo.PerformUndo();
            Assert.That(Snapshot(fixture.Owner), Is.EqualTo(before));
            Assert.That(fixture.Filter.sharedMesh, Is.SameAs(fixture.Replacement));
            Assert.That(fixture.Owner.Deform(false), Is.Null, "Undo of the reset must reinstate the prior source binding.");
        }

        [Test]
        public void ExplicitReset_WithChangedTopology_RefreshesOnlyOnTheExplicitOperation()
        {
            using var fixture = new Fixture();
            var before = Snapshot(fixture.Owner);
            fixture.Source.triangles = new[] { 0, 2, 1 };
            _ = fixture.Owner.Groups.Count;
            Assert.That(Snapshot(fixture.Owner), Is.EqualTo(before));
            Assert.That(MeshDeformerValidator.Validate(fixture.Owner).Any(d => d.Code == MeshDeformerValidator.SourceMeshChanged), Is.True);
            fixture.Owner.Reset();
            Assert.That(SerializedDeformerReader.Read(fixture.Owner).SourceTopologyHash,
                Is.EqualTo(SourceMeshTopology.Calculate(fixture.Source)));
            Assert.That(MeshDeformerValidator.Validate(fixture.Owner).Any(d => d.Code == MeshDeformerValidator.SourceMeshChanged), Is.False);
        }

        [Test]
        public void UndoOfMissingMesh_RecoversTheOriginalDeformationWithoutInitialization()
        {
            using var fixture = new Fixture();
            var before = Snapshot(fixture.Owner);
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(fixture.Filter, "Remove source mesh");
            fixture.Filter.sharedMesh = null;
            Undo.FlushUndoRecordObjects();
            _ = fixture.Owner.Groups.Count;
            Assert.That(Snapshot(fixture.Owner), Is.EqualTo(before));
            Undo.PerformUndo();
            Assert.That(fixture.Filter.sharedMesh, Is.SameAs(fixture.Source));
            Assert.That(fixture.Owner.Deform(false).vertices[0], Is.EqualTo(Fixture.Delta));
            Assert.That(Snapshot(fixture.Owner), Is.EqualTo(before));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LifecycleCleanup_RestoresOnlyOwnedOutputAndPreservesForeignMesh(bool replaceOutput)
        {
            using var fixture = new Fixture();
            fixture.Root.SetActive(true);
            Mesh output = fixture.Owner.Deform(true);
            Assert.That(fixture.Filter.sharedMesh, Is.SameAs(output));
            if (replaceOutput) fixture.Filter.sharedMesh = fixture.Replacement;
            fixture.Owner.enabled = false;
            Assert.That(fixture.Filter.sharedMesh, Is.SameAs(replaceOutput ? fixture.Replacement : fixture.Source));
            Assert.That(output == null, Is.True);
        }

        [Test]
        public void InactivePrefabVariant_SaveReloadPreservesUnavailableSourceAndRestoresAfterRepair()
        {
            string folder = "Assets/__SourceBindingPreservation_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            using var fixture = new Fixture();
            GameObject instance = null, contents = null;
            try
            {
                AssetDatabase.CreateAsset(fixture.Source, folder + "/Source.asset");
                var basePrefab = PrefabUtility.SaveAsPrefabAsset(fixture.Root, folder + "/Base.prefab");
                instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
                var filter = instance.GetComponent<MeshFilter>();
                var owner = instance.GetComponent<LatticeDeformer>();
                filter.sharedMesh = null;
                PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
                var before = Snapshot(owner);
                _ = owner.Groups.Count;
                PrefabUtility.SaveAsPrefabAsset(instance, folder + "/Variant.prefab");
                Object.DestroyImmediate(instance); instance = null;
                contents = PrefabUtility.LoadPrefabContents(folder + "/Variant.prefab");
                owner = contents.GetComponent<LatticeDeformer>();
                filter = contents.GetComponent<MeshFilter>();
                Assert.That(contents.activeSelf, Is.False);
                Assert.That(filter.sharedMesh, Is.Null);
                Assert.That(Snapshot(owner), Is.EqualTo(before));
                _ = owner.Groups.Count;
                PrefabUtility.SaveAsPrefabAsset(contents, folder + "/Variant.prefab");
                PrefabUtility.UnloadPrefabContents(contents); contents = null;
                contents = PrefabUtility.LoadPrefabContents(folder + "/Variant.prefab");
                owner = contents.GetComponent<LatticeDeformer>();
                Assert.That(Snapshot(owner), Is.EqualTo(before));
                contents.GetComponent<MeshFilter>().sharedMesh = fixture.Source;
                Assert.That(owner.Deform(false).vertices[0], Is.EqualTo(Fixture.Delta));
                owner.InvalidateCache(); owner.RestoreOriginalMesh();
                Assert.That(basePrefab.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(fixture.Source));
            }
            finally
            {
                if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
                if (instance != null) Object.DestroyImmediate(instance);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReimportOfSameAsset_PreservesEditedPayloadAndBinding(bool readable)
        {
            string folder = "Assets/__SourceBindingReimport_" + Guid.NewGuid().ToString("N");
            string path = folder + "/Source.obj";
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            var root = new GameObject("__SourceBindingReimport"); root.SetActive(false);
            LatticeDeformer owner = null;
            try
            {
                System.IO.File.WriteAllText(path, "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 3 2\n");
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.isReadable = true; importer.SaveAndReimport();
                var source = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().Single();
                var filter = root.AddComponent<MeshFilter>(); root.AddComponent<MeshRenderer>();
                filter.sharedMesh = source;
                owner = root.AddComponent<LatticeDeformer>(); owner.Reset();
                owner.ActiveLayerIndex = owner.AddLayer("Edited", MeshDeformerLayerType.Brush);
                owner.EnsureDisplacementCapacity(); owner.Displacements[0] = Fixture.Delta;
                var expected = owner.Deform(false).vertices;
                string before = Snapshot(owner);
                importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.isReadable = readable; importer.SaveAndReimport();
                var reimported = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().Single();
                filter.sharedMesh = reimported;
                _ = owner.Groups.Count;
                Assert.That(Snapshot(owner), Is.EqualTo(before));
                Assert.That(owner.Deform(false), Is.Not.Null);
                Assert.That(owner.RuntimeMesh.vertices, Is.EqualTo(expected));
                Assert.That(Snapshot(owner), Is.EqualTo(before));
                Assert.That(filter.sharedMesh, Is.SameAs(reimported));
                Assert.That(MeshDeformerValidator.Validate(owner).Any(d => d.Code == MeshDeformerValidator.SourceMeshChanged), Is.False);
            }
            finally
            {
                if (owner != null) { owner.InvalidateCache(); owner.RestoreOriginalMesh(); }
                Object.DestroyImmediate(root);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private static string Snapshot(LatticeDeformer owner)
        {
            var data = SerializedDeformerReader.Read(owner);
            return (data.SourceMesh != null ? data.SourceMesh.GetInstanceID() : 0) + "|" + data.SourceVertexCount + "|" +
                data.SourceTopologyHash + "|" + Get<bool>(owner, "_hasInitializedFromSource") + "|" +
                Get<int>(owner, "_migrationReleaseIndex") + "|" + Get<DeformationDataVersion>(owner, "_deformationDataVersion") + "|" +
                Get<DeformationDataVersion>(owner, "_deformationDataSourceVersion") + "|" +
                data.EmbeddedActiveGroupIndex + "|" + string.Join("|", data.EmbeddedGroups.Select(JsonUtility.ToJson));
        }

        private static T Get<T>(LatticeDeformer owner, string name) =>
            (T)typeof(LatticeDeformer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static void Set(LatticeDeformer owner, string name, object value) =>
            typeof(LatticeDeformer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);

        private sealed class Fixture : IDisposable
        {
            internal static readonly Vector3 Delta = new Vector3(0.125f, 0.25f, 0.5f);
            internal readonly GameObject Root;
            internal readonly Mesh Source, Replacement;
            internal readonly MeshFilter Filter;
            internal readonly LatticeDeformer Owner;

            internal Fixture()
            {
                Root = new GameObject("__SourceBindingFixture"); Root.SetActive(false);
                Source = new Mesh { name = "__SourceBindingSource", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                Source.RecalculateNormals();
                Replacement = Object.Instantiate(Source); Replacement.name = "__SourceBindingReplacement";
                Replacement.triangles = new[] { 0, 2, 1 };
                Filter = Root.AddComponent<MeshFilter>(); Root.AddComponent<MeshRenderer>(); Filter.sharedMesh = Source;
                Owner = Root.AddComponent<LatticeDeformer>(); Owner.Reset();
                Owner.ActiveLayerIndex = Owner.AddLayer("Brush", MeshDeformerLayerType.Brush);
                Owner.EnsureDisplacementCapacity(); Owner.Displacements[0] = Delta;
            }

            internal void BreakBinding(string scenario)
            {
                switch (scenario)
                {
                    case "missing-mesh": Filter.sharedMesh = null; break;
                    case "missing-renderer-reference": Set(Owner, "_meshFilter", null); break;
                    case "lost-source-reference": Set(Owner, "_serializedSourceMesh", null); break;
                    default: Filter.sharedMesh = Replacement; break;
                }
            }

            internal void RestoreBinding()
            {
                Filter.sharedMesh = Source;
                Set(Owner, "_meshFilter", Filter);
                Set(Owner, "_serializedSourceMesh", Source);
            }

            public void Dispose()
            {
                if (Owner != null) { Undo.ClearUndo(Owner); Owner.InvalidateCache(); Owner.RestoreOriginalMesh(); }
                if (Filter != null) Undo.ClearUndo(Filter);
                Object.DestroyImmediate(Root);
                if (Source != null && !EditorUtility.IsPersistent(Source)) Object.DestroyImmediate(Source);
                if (Replacement != null) Object.DestroyImmediate(Replacement);
            }
        }
    }
}
#endif
