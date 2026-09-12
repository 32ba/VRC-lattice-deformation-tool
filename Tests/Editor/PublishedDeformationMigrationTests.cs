#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Net._32Ba.LatticeDeformationTool.Editor;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class PublishedDeformationMigrationTests
    {
        private const string Package = ArchitectureContractSnapshot.PackagePath;
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        [Serializable] private sealed class Inventory { public Release[] releases; }
        [Serializable] private sealed class Release { public string tag; }
        [Serializable] private sealed class PackageMetadata { public string version; }

        [Test]
        public void Journal_ContainsEveryPublishedReleaseAndKeepsTheLegacyEnumFrozen()
        {
            var published = JsonUtility.FromJson<Inventory>(File.ReadAllText(Package + "/Docs~/Architecture/2026-09-07-published-releases.json"));
            var package = JsonUtility.FromJson<PackageMetadata>(File.ReadAllText(Package + "/package.json"));
            Assert.That(DeformationReleaseManifest.Releases, Is.EqualTo(published.releases.Select(r => r.tag).Concat(new[] { package.version })));
            Assert.That(DeformationReleaseManifest.Releases.Distinct().Count(), Is.EqualTo(DeformationReleaseManifest.Current));
            Assert.That(Enum.GetValues(typeof(DeformationDataVersion)).Cast<DeformationDataVersion>().Select(v => (int)v), Is.EqualTo(Enumerable.Range(0, 16)));
            Assert.That(DeformationReleaseManifest.Label(15), Is.EqualTo("1.4.1"));
            Assert.That(DeformationReleaseManifest.Label(16), Is.EqualTo("1.4.2-rc.1"));
            Assert.That(DeformationReleaseManifest.EarliestBoundaryForLegacyMarker(DeformationDataVersion.CurrentDevelopment), Is.EqualTo(16));
        }

        private static IEnumerable<int> Boundaries() => Enumerable.Range(1, DeformationReleaseManifest.Current - 1);

        [TestCaseSource(nameof(Boundaries))]
        public void EveryBoundary_CommitsAtomicallyAndCanRetryWithoutChangingEarlierSuccess(int from)
        {
            var state = StateAt(from);
            string before = StatePayload(state);
            var groups = state.Groups; var flat = state.FlatLayers; var settings = state.Settings;
            bool fail = true;
            int checkpoint = 0;
            var runner = new PublishedDeformationMigrationRunner(state, next =>
            {
                checkpoint = next;
                if (fail) throw new InvalidOperationException("Injected release checkpoint failure");
            });
            Assert.That(runner.TryAdvanceOneRelease(), Is.False);
            Assert.That(checkpoint, Is.EqualTo(from + 1));
            Assert.That(state.Status, Is.EqualTo(DeformationDataMigrationStatus.InvalidData));
            Assert.That(StatePayload(state), Is.EqualTo(before));
            Assert.That(state.Groups, Is.SameAs(groups));
            Assert.That(state.FlatLayers, Is.SameAs(flat));
            Assert.That(state.Settings, Is.SameAs(settings));
            fail = false;
            Assert.That(runner.TryAdvanceOneRelease(), Is.True);
            Assert.That(state.ReleaseIndex, Is.EqualTo(from + 1));
            Assert.That(DeformationReleaseManifest.MatchesSchema(state.ReleaseIndex, state.Version), Is.True);
            if (from + 1 < DeformationReleaseManifest.Current)
            {
                string previousSuccess = StatePayload(state);
                fail = true;
                Assert.That(runner.TryAdvanceOneRelease(), Is.False);
                Assert.That(StatePayload(state), Is.EqualTo(previousSuccess));
                Assert.That(state.ReleaseIndex, Is.EqualTo(from + 1));
            }
        }

        [TestCase(-1)]
        [TestCase(44)]
        [TestCase(int.MaxValue)]
        public void InvalidJournal_IsRejectedBeforePayloadOrOwnerCommit(int index)
        {
            var state = StateAt(16); state.ReleaseIndex = index;
            string before = StatePayload(state);
            int calls = 0;
            var runner = new PublishedDeformationMigrationRunner(state, _ => calls++);
            Assert.That(runner.TryAdvanceOneRelease(_ => calls++), Is.False);
            Assert.That(calls, Is.Zero);
            Assert.That(state.Status, Is.EqualTo(index < 0 ? DeformationDataMigrationStatus.InvalidData
                : DeformationDataMigrationStatus.UnsupportedFutureVersion));
            Assert.That(StatePayload(state), Is.EqualTo(before));
        }

        [Test]
        public void SharedMarker_StartsAtEarliestCompatibleBoundaryAndNoOpsPreservePayload()
        {
            var state = StateAt(16); state.ReleaseIndex = 0;
            string raw = LegacyPayload(state);
            var runner = new PublishedDeformationMigrationRunner(state);
            var visited = new List<int>();
            while (runner.TryAdvanceOneRelease())
            {
                visited.Add(state.ReleaseIndex);
                Assert.That(LegacyPayload(state), Is.EqualTo(raw));
                Assert.That(state.Version, Is.EqualTo(DeformationDataVersion.CurrentDevelopment));
                Assert.That(visited.Count, Is.LessThan(44));
            }
            Assert.That(visited, Is.EqualTo(Enumerable.Range(16, 28)));
            Assert.That(state.Status, Is.EqualTo(DeformationDataMigrationStatus.Ready));
        }

        [Test]
        public void RebaseOfStaleCheckpoint_IsAtomicAndUsesTheValidatedSchemaMarker()
        {
            var state = StateAt(11); state.ReleaseIndex = 43;
            string before = StatePayload(state);
            bool reject = true;
            var runner = new PublishedDeformationMigrationRunner(state, _ => { if (reject) throw new InvalidOperationException(); });
            Assert.That(runner.TryAdvanceOneRelease(), Is.False);
            Assert.That(StatePayload(state), Is.EqualTo(before));
            reject = false;
            string raw = LegacyPayload(state);
            Assert.That(runner.TryAdvanceOneRelease(), Is.True);
            Assert.That(state.ReleaseIndex, Is.EqualTo(11));
            Assert.That(LegacyPayload(state), Is.EqualTo(raw));
        }

        [Test]
        public void RecoveryOfStaleCurrentStructure_CommitsBothMarkersAtomically()
        {
            var state = StateWithStaleStructure();
            string before = StatePayload(state);
            bool reject = true;
            var runner = new PublishedDeformationMigrationRunner(state, next =>
            {
                Assert.That(next, Is.EqualTo(10));
                if (reject) throw new InvalidOperationException();
            });
            Assert.That(runner.TryAdvanceOneRelease(), Is.False);
            Assert.That(StatePayload(state), Is.EqualTo(before));
            reject = false;
            Assert.That(runner.TryAdvanceOneRelease(), Is.True);
            Assert.That(state.ReleaseIndex, Is.EqualTo(10));
            Assert.That(state.Version, Is.EqualTo(DeformationDataVersion.V1_2_0));
            Assert.That(state.SourceVersion, Is.EqualTo(DeformationDataVersion.V1_2_0));
        }

        [Test]
        public void CorruptPayload_CannotUseAStaleCheckpointToBypassPreflight()
        {
            var state = StateAt(16); state.ReleaseIndex = 14;
            state.Groups[0].LayersList[0].BrushDisplacements = new[] { new Vector3(float.NaN, 0f, 0f) };
            string before = StatePayload(state);
            int calls = 0;
            var runner = new PublishedDeformationMigrationRunner(state, _ => calls++);
            Assert.That(runner.TryAdvanceOneRelease(), Is.False);
            Assert.That(calls, Is.Zero);
            Assert.That(state.Status, Is.EqualTo(DeformationDataMigrationStatus.InvalidData));
            Assert.That(StatePayload(state), Is.EqualTo(before));
        }

        [TestCase(14, false)] // Explicit 1.4.0 -> 1.4.1 no-op.
        [TestCase(15, false)] // Existing legacy conversion at 1.4.2-rc.1.
        [TestCase(42, false)] // Architecture beta boundary.
        [TestCase(43, true)] // Recovery of an older structure carrying current markers.
        public void PrefabRecordFailure_RestoresJournalPayloadAndExistingOverrides(int from, bool staleStructure)
        {
            string folder = CreateFolder();
            GameObject root = null;
            var previous = DeformerPlatformServices.RecordLegacyMigration;
            try
            {
                var mesh = DeformationOutputBaselineFixture.CreateMesh(2);
                AssetDatabase.CreateAsset(mesh, folder + "/source.asset");
                root = MakeRoot(mesh);
                var target = root.GetComponent<LatticeDeformer>();
                ApplyState(target, staleStructure ? StateWithStaleStructure() : StateAt(from));
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/base.prefab");
                DestroyRoot(root); root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                target = root.GetComponent<LatticeDeformer>();
                root.transform.localPosition = new Vector3(0.25f, 0.5f, 0.75f);
                PrefabUtility.RecordPrefabInstancePropertyModifications(root.transform);
                string before = EditorJsonUtility.ToJson(target);
                var modifications = Modifications(root);
                int recorded = 0;
                DeformerPlatformServices.RecordLegacyMigration = value =>
                {
                    recorded = ((LatticeDeformer)value).SerializedMigrationReleaseIndex;
                    previous?.Invoke(value);
                    throw new InvalidOperationException("Injected after actual Prefab recording");
                };
                Assert.That(target.TryUpgradePublishedDeformationDataOneRelease(), Is.False);
                Assert.That(recorded, Is.EqualTo(staleStructure ? 10 : from + 1));
                Assert.That(EditorJsonUtility.ToJson(target), Is.EqualTo(before));
                Assert.That(Modifications(root), Is.EquivalentTo(modifications));
                DeformerPlatformServices.RecordLegacyMigration = previous;
                Assert.That(target.TryUpgradePublishedDeformationDataOneRelease(), Is.True);
                Assert.That(target.SerializedMigrationReleaseIndex, Is.EqualTo(staleStructure ? 10 : from + 1));
            }
            finally { DeformerPlatformServices.RecordLegacyMigration = previous; DestroyRoot(root); AssetDatabase.DeleteAsset(folder); }
        }

        [TestCase(21)]
        [TestCase(42)]
        public void PartialJournal_SurvivesInactiveSaveReloadAndResumesAtTheNextBoundary(int boundary)
        {
            string folder = CreateFolder(); GameObject root = null;
            try
            {
                var source = DeformationOutputBaselineFixture.CreateMesh(2);
                AssetDatabase.CreateAsset(source, folder + "/source.asset");
                root = MakeRoot(source); var target = root.GetComponent<LatticeDeformer>();
                ApplyState(target, StateAt(boundary));
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/partial.prefab");
                DestroyRoot(root); root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                target = root.GetComponent<LatticeDeformer>();
                Assert.That(root.activeSelf || target.enabled, Is.False);
                Assert.That(target.SerializedMigrationReleaseIndex, Is.EqualTo(boundary));
                Assert.That(target.TryUpgradePublishedDeformationDataOneRelease(), Is.True);
                Assert.That(target.SerializedMigrationReleaseIndex, Is.EqualTo(boundary + 1));
                Assert.That(target.Deform(false), Is.Not.Null);
                Assert.That(target.SerializedMigrationReleaseIndex, Is.EqualTo(43));
            }
            finally { DestroyRoot(root); AssetDatabase.DeleteAsset(folder); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void VariantWithOlderSchemaOverrides_RebasesInheritedCurrentJournalWithoutChangingItsBase(bool staleStructure)
        {
            string folder = CreateFolder(); GameObject root = null;
            try
            {
                var source = DeformationOutputBaselineFixture.CreateMesh(2);
                AssetDatabase.CreateAsset(source, folder + "/source.asset");
                root = MakeRoot(source); var component = root.GetComponent<LatticeDeformer>();
                Assert.That(component.Deform(false), Is.Not.Null);
                Assert.That(component.SerializedMigrationReleaseIndex, Is.EqualTo(43));
                component.RestoreOriginalMesh(); component.InvalidateCache();
                var baseAsset = PrefabUtility.SaveAsPrefabAsset(root, folder + "/base.prefab");
                DestroyRoot(root); root = (GameObject)PrefabUtility.InstantiatePrefab(baseAsset);
                component = root.GetComponent<LatticeDeformer>();
                ApplyState(component, staleStructure ? StateWithStaleStructure() : StateAt(11), writeJournal: false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                var variant = PrefabUtility.SaveAsPrefabAsset(root, folder + "/variant.prefab");
                DestroyRoot(root); root = (GameObject)PrefabUtility.InstantiatePrefab(variant);
                component = root.GetComponent<LatticeDeformer>();
                Assert.That(component.SerializedMigrationReleaseIndex, Is.EqualTo(43));
                Assert.That(component.SerializedDeformationDataVersion, Is.EqualTo(staleStructure
                    ? DeformationDataVersion.CurrentDevelopment : DeformationDataVersion.V1_2_1));
                string baseBefore = File.ReadAllText(folder + "/base.prefab");
                Assert.That(component.TryUpgradePublishedDeformationDataOneRelease(), Is.True);
                Assert.That(component.SerializedMigrationReleaseIndex, Is.EqualTo(staleStructure ? 10 : 11));
                Assert.That(component.Deform(false), Is.Not.Null);
                Assert.That(component.SerializedMigrationReleaseIndex, Is.EqualTo(43));
                Assert.That(File.ReadAllText(folder + "/base.prefab"), Is.EqualTo(baseBefore));
                Assert.That(baseAsset.GetComponent<LatticeDeformer>().SerializedMigrationReleaseIndex, Is.EqualTo(43));
                Assert.That(baseAsset.GetComponent<LatticeDeformer>().SerializedDeformationDataVersion, Is.EqualTo(DeformationDataVersion.CurrentDevelopment));
            }
            finally { DestroyRoot(root); AssetDatabase.DeleteAsset(folder); }
        }

        [TestCase(-1)]
        [TestCase(44)]
        public void InvalidJournal_BlocksAuthoringEvaluationAndValidationWithoutMutation(int index)
        {
            var mesh = DeformationOutputBaselineFixture.CreateMesh(2); var root = MakeRoot(mesh);
            try
            {
                var target = root.GetComponent<LatticeDeformer>();
                ApplyState(target, StateAt(16)); Set(target, "_migrationReleaseIndex", index);
                target.enabled = true; // Root stays inactive; validation must remain read-only.
                string before = EditorJsonUtility.ToJson(target);
                Assert.That(target.CanStartAuthoringEdit, Is.False);
                var diagnostic = MeshDeformerValidator.Validate(target).Single(d => d.Code == MeshDeformerValidator.InvalidMigrationJournal);
                Assert.That(diagnostic.Severity, Is.EqualTo(MeshDeformerDiagnosticSeverity.Error));
                Assert.That(diagnostic.PropertyPath, Is.EqualTo("_migrationReleaseIndex"));
                Assert.That(target.Deform(false), Is.Null);
                Assert.That(target.TryUpgradeDeformationDataOneRelease(), Is.False);
                Assert.That(target.TryUpgradePublishedDeformationDataOneRelease(), Is.False);
                target.Reset();
                Assert.That(EditorJsonUtility.ToJson(target), Is.EqualTo(before));
                Assert.That(root.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
            }
            finally { DestroyRoot(root); Object.DestroyImmediate(mesh); }
        }

        [TestCase(-1)]
        [TestCase(44)]
        [TestCase(43)]
        public void RejectedMigration_OnEnableDoesNotCaptureAnotherSavedBaseline(int index)
        {
            var mesh = DeformationOutputBaselineFixture.CreateMesh(2); var root = MakeRoot(mesh);
            try
            {
                var target = root.GetComponent<LatticeDeformer>();
                var state = StateAt(16); state.ReleaseIndex = index;
                if (index == 43)
                {
                    var brush = new LatticeLayer
                    {
                        BrushDisplacements = new[] { new Vector3(float.NaN, 0f, 0f), Vector3.zero, Vector3.zero, Vector3.zero }
                    };
                    brush.SetType(MeshDeformerLayerType.Brush);
                    state.Groups[0].LayersList.Add(brush);
                }
                ApplyState(target, state);
                Set(target, "_hasInitialBlendShapeWeightBaseline", false);
                Set(target, "_initialBlendShapeWeights", Array.Empty<float>());
                target.enabled = true;
                string before = EditorJsonUtility.ToJson(target);
                root.SetActive(true); // Exercise Unity's real OnEnable callback.
                Assert.That(target.MigrationStatus, Is.EqualTo(index > 43
                    ? DeformationDataMigrationStatus.UnsupportedFutureVersion : DeformationDataMigrationStatus.InvalidData));
                Assert.That(EditorJsonUtility.ToJson(target), Is.EqualTo(before));
                Assert.That(root.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
            }
            finally { DestroyRoot(root); Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void AuthoringUndoAndRejectedEdit_RestoreTheJournalWithThePayload()
        {
            string folder = CreateFolder(); GameObject root = null;
            Undo.ClearAll();
            try
            {
                var mesh = DeformationOutputBaselineFixture.CreateMesh(2);
                AssetDatabase.CreateAsset(mesh, folder + "/source.asset");
                root = MakeRoot(mesh); var target = root.GetComponent<LatticeDeformer>();
                ApplyState(target, StateAt(42));
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/base.prefab");
                DestroyRoot(root); root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                target = root.GetComponent<LatticeDeformer>();
                string before = EditorJsonUtility.ToJson(target);
                var beforeOverrides = Modifications(root);
                Assert.That(DeformerEditService.Execute(target, "Rejected after migration", value =>
                {
                    Assert.That(value.AddGroup(), Is.GreaterThanOrEqualTo(0));
                    Assert.That(value.SerializedMigrationReleaseIndex, Is.EqualTo(43));
                    return false;
                }), Is.False);
                Assert.That(EditorJsonUtility.ToJson(target), Is.EqualTo(before));
                Assert.That(Modifications(root), Is.EquivalentTo(beforeOverrides));
                Assert.That(DeformerEditService.AddGroup(target, "Add group with migration"), Is.True);
                string after = EditorJsonUtility.ToJson(target);
                Assert.That(target.SerializedMigrationReleaseIndex, Is.EqualTo(43));
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Assert.That(EditorJsonUtility.ToJson(target), Is.EqualTo(before));
                Assert.That(Modifications(root), Is.EquivalentTo(beforeOverrides));
                Undo.PerformRedo();
                Assert.That(EditorJsonUtility.ToJson(target), Is.EqualTo(after));
            }
            finally { Undo.ClearAll(); DestroyRoot(root); AssetDatabase.DeleteAsset(folder); }
        }

        [Test]
        public void VariantJournalOverride_CanRevertApplyAndReloadWithoutChangingTheBase()
        {
            string folder = CreateFolder(); GameObject root = null;
            try
            {
                var mesh = DeformationOutputBaselineFixture.CreateMesh(2);
                AssetDatabase.CreateAsset(mesh, folder + "/source.asset");
                root = MakeRoot(mesh); var target = root.GetComponent<LatticeDeformer>();
                ApplyState(target, StateAt(42));
                string basePath = folder + "/base.prefab";
                var baseAsset = PrefabUtility.SaveAsPrefabAsset(root, basePath);
                DestroyRoot(root); root = (GameObject)PrefabUtility.InstantiatePrefab(baseAsset);
                target = root.GetComponent<LatticeDeformer>();
                SerializedDeformerReader.Read(target).Groups[0].Name = "Variant group";
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                string variantPath = folder + "/variant.prefab";
                var variant = PrefabUtility.SaveAsPrefabAsset(root, variantPath);
                DestroyRoot(root); root = (GameObject)PrefabUtility.InstantiatePrefab(variant);
                target = root.GetComponent<LatticeDeformer>();
                string baseBefore = File.ReadAllText(basePath);
                Assert.That(target.SerializedMigrationReleaseIndex, Is.EqualTo(42));
                string raw = EditorJsonUtility.ToJson(target);
                Assert.That(target.TryUpgradePublishedDeformationDataOneRelease(), Is.True);
                using (var serialized = new SerializedObject(target))
                    PrefabUtility.RevertPropertyOverride(serialized.FindProperty("_migrationReleaseIndex"), InteractionMode.AutomatedAction);
                Assert.That(EditorJsonUtility.ToJson(target), Is.EqualTo(raw));
                Assert.That(target.TryUpgradePublishedDeformationDataOneRelease(), Is.True);
                using (var serialized = new SerializedObject(target))
                    PrefabUtility.ApplyPropertyOverride(serialized.FindProperty("_migrationReleaseIndex"), variantPath, InteractionMode.AutomatedAction);
                DestroyRoot(root); root = null;
                AssetDatabase.ImportAsset(variantPath, ImportAssetOptions.ForceSynchronousImport);
                root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(variantPath));
                target = root.GetComponent<LatticeDeformer>();
                Assert.That(target.SerializedMigrationReleaseIndex, Is.EqualTo(43));
                Assert.That(SerializedDeformerReader.Read(target).Groups[0].Name, Is.EqualTo("Variant group"));
                Assert.That(target.Deform(false), Is.Not.Null);
                Assert.That(File.ReadAllText(basePath), Is.EqualTo(baseBefore));
                Assert.That(baseAsset.GetComponent<LatticeDeformer>().SerializedMigrationReleaseIndex, Is.EqualTo(42));
            }
            finally { DestroyRoot(root); AssetDatabase.DeleteAsset(folder); }
        }

        private static DeformationMigrationState StateAt(int index)
        {
            var version = index <= 14 ? (DeformationDataVersion)index : index == 15
                ? DeformationDataVersion.V1_4_0 : DeformationDataVersion.CurrentDevelopment;
            var state = (DeformationMigrationState)typeof(DeformationMigrationRunnerTests)
                .GetMethod("CreateState", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { version });
            state.ReleaseIndex = index;
            if (version == DeformationDataVersion.CurrentDevelopment)
            {
                state.LayerModelVersion = 3;
                foreach (var group in state.Groups) group.SetSerializedActiveLayerIndex(0);
            }
            return state;
        }
        private static DeformationMigrationState StateWithStaleStructure()
        {
            var state = StateAt(10);
            state.Version = state.SourceVersion = DeformationDataVersion.CurrentDevelopment;
            state.ReleaseIndex = DeformationReleaseManifest.Current;
            return state;
        }
        private static string LegacyPayload(DeformationMigrationState state) => (string)typeof(DeformationMigrationRunnerTests)
            .GetMethod("Payload", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { state });
        private static string StatePayload(DeformationMigrationState state) => state.ReleaseIndex + "|" + LegacyPayload(state);
        private static void ApplyState(LatticeDeformer target, DeformationMigrationState state, bool writeJournal = true)
        {
            Set(target, "_settings", state.Settings); Set(target, "_layers", state.FlatLayers); Set(target, "_groups", state.Groups);
            Set(target, "_activeLayerIndex", state.ActiveLayerIndex); Set(target, "_activeGroupIndex", state.ActiveGroupIndex);
            Set(target, "_layerModelVersion", state.LayerModelVersion); Set(target, "_deformationDataVersion", state.Version);
            Set(target, "_deformationDataSourceVersion", state.SourceVersion);
            Set(target, "_legacyAbsoluteLatticeEvaluation", state.LegacyAbsoluteEvaluation);
            Set(target, "_legacyPublishedBlendShapeSemantics", state.LegacyPublishedBlendShapeSemantics);
            Set(target, "_blendShapeOutput", state.BlendShapeOutput); Set(target, "_blendShapeName", state.BlendShapeName);
            Set(target, "_blendShapeCurve", state.BlendShapeCurve);
            if (writeJournal) Set(target, "_migrationReleaseIndex", state.ReleaseIndex);
            target.InvalidateCache();
        }
        private static void Set(LatticeDeformer target, string name, object value) => typeof(LatticeDeformer).GetField(name, PrivateInstance).SetValue(target, value);
        private static GameObject MakeRoot(Mesh source)
        {
            var root = new GameObject("Published journal fixture"); root.SetActive(false);
            root.AddComponent<MeshFilter>().sharedMesh = source; root.AddComponent<MeshRenderer>();
            var target = root.AddComponent<LatticeDeformer>(); target.enabled = false; target.Reset(); return root;
        }
        private static string CreateFolder()
        { string name = "__PublishedMigration_" + Guid.NewGuid().ToString("N"); AssetDatabase.CreateFolder("Assets", name); return "Assets/" + name; }
        private static void DestroyRoot(GameObject root)
        {
            if (root == null) return; var target = root.GetComponent<LatticeDeformer>();
            if (target != null) { target.RestoreOriginalMesh(); target.InvalidateCache(); }
            Object.DestroyImmediate(root);
        }
        private static string[] Modifications(GameObject root) => (PrefabUtility.GetPropertyModifications(root) ?? Array.Empty<PropertyModification>())
            .Select(m => (m.target == null ? 0 : m.target.GetInstanceID()) + "|" + m.propertyPath + "|" + m.value + "|" + (m.objectReference == null ? 0 : m.objectReference.GetInstanceID())).ToArray();
    }
}
#endif
