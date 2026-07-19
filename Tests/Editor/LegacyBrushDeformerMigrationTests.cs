#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Net._32Ba.LatticeDeformationTool.Tests
{
    [TestFixture]
    public class LegacyBrushDeformerMigrationTests
    {
        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
        }

        [Test]
        public void LegacyComponent_IsHiddenFromAddComponentMenu()
        {
            var attribute = typeof(BrushDeformer)
                .GetCustomAttribute<AddComponentMenu>();

            Assert.That(attribute, Is.Not.Null);
            Assert.That(attribute.componentMenu, Is.Empty);
        }

        [Test]
        public void AutoMigration_InactiveDisabledSceneObject_IsUndoableRedoableAndIdempotent()
        {
            var fixture = CreateFixture("LegacyBrushAutoScene");
            Scene scene = fixture.GameObject.scene;
            try
            {
                fixture.GameObject.SetActive(false);
                fixture.Legacy.enabled = false;
                SetLegacyDisplacements(
                    fixture.Legacy,
                    CreateDisplacements(fixture.Mesh.vertexCount, 0.23f));

                Assert.That(
                    LegacyBrushDeformerAutoMigration.TryMigrateScene(
                        scene,
                        out int migrated,
                        out string error),
                    Is.True,
                    error);
                Assert.That(migrated, Is.EqualTo(1));
                Assert.That(fixture.GameObject.GetComponent<LatticeDeformer>(), Is.Not.Null);
                Assert.That(fixture.Legacy.enabled, Is.False);

                Assert.That(
                    LegacyBrushDeformerAutoMigration.TryMigrateScene(
                        scene,
                        out int repeated,
                        out error),
                    Is.True,
                    error);
                Assert.That(repeated, Is.Zero);

                Undo.PerformUndo();
                Assert.That(fixture.GameObject.GetComponent<LatticeDeformer>(), Is.Null);
                Assert.That(fixture.Legacy.enabled, Is.False,
                    "The pre-migration disabled state must be restored exactly.");

                Undo.PerformRedo();
                Assert.That(fixture.GameObject.GetComponent<LatticeDeformer>(), Is.Not.Null);
                Assert.That(fixture.Legacy.enabled, Is.False);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void AutoMigration_PublishedLegacyBrushPrefab_MigratesSavesAndReloadsIdempotently()
        {
            const string source =
                "Packages/net.32ba.lattice-deformation-tool/Tests/Editor/Fixtures/" +
                "HistoricalReleases/1.4.0/legacy-brush.prefab";
            string folder = "Assets/__LegacyBrushAutoMigration_" + Guid.NewGuid().ToString("N");
            string copy = folder + "/legacy-brush.prefab";
            try
            {
                Assert.That(AssetDatabase.IsValidFolder("Assets"), Is.True);
                AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
                Assert.That(AssetDatabase.CopyAsset(source, copy), Is.True);
                AssetDatabase.ImportAsset(copy, ImportAssetOptions.ForceSynchronousImport);

                Assert.That(
                    LegacyBrushDeformerAutoMigration.TryMigratePrefabAsset(
                        copy,
                        out int migrated,
                        out string error),
                    Is.True,
                    error);
                Assert.That(migrated, Is.EqualTo(1));

                GameObject contents = PrefabUtility.LoadPrefabContents(copy);
                try
                {
                    var legacy = contents.GetComponentInChildren<BrushDeformer>(true);
                    var target = contents.GetComponentInChildren<LatticeDeformer>(true);
                    Assert.That(contents.activeSelf, Is.False);
                    Assert.That(legacy, Is.Not.Null);
                    Assert.That(legacy.enabled, Is.False);
                    Assert.That(target, Is.Not.Null);
                    Assert.That(FindMigratedLayer(target), Is.Not.Null);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }

                Assert.That(
                    LegacyBrushDeformerAutoMigration.TryMigratePrefabAsset(
                        copy,
                        out int repeated,
                        out error),
                    Is.True,
                    error);
                Assert.That(repeated, Is.Zero);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }

        [Test]
        public void AutoMigration_PrefabFailureLeavesSerializedAssetUnchanged()
        {
            string folder = "Assets/__LegacyBrushAutoMigrationFailure_" + Guid.NewGuid().ToString("N");
            string meshPath = folder + "/source.asset";
            string prefabPath = folder + "/invalid.prefab";
            var fixture = CreateFixture("LegacyBrushAutoInvalidPrefab");
            try
            {
                AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
                AssetDatabase.CreateAsset(UnityEngine.Object.Instantiate(fixture.Mesh), meshPath);
                var persistentMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                fixture.Filter.sharedMesh = persistentMesh;
                var serialized = new SerializedObject(fixture.Legacy);
                serialized.FindProperty("_serializedSourceMesh").objectReferenceValue = persistentMesh;
                var displacements = serialized.FindProperty("_displacements");
                displacements.arraySize = persistentMesh.vertexCount;
                displacements.GetArrayElementAtIndex(0).vector3Value =
                    new Vector3(float.NaN, 0f, 0f);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(fixture.GameObject, prefabPath);
                string before = System.IO.File.ReadAllText(prefabPath);

                Assert.That(
                    LegacyBrushDeformerAutoMigration.TryMigratePrefabAsset(
                        prefabPath,
                        out int migrated,
                        out string error),
                    Is.False);
                Assert.That(migrated, Is.Zero);
                Assert.That(error, Is.Not.Empty);
                Assert.That(System.IO.File.ReadAllText(prefabPath), Is.EqualTo(before));
            }
            finally
            {
                fixture.Dispose();
                AssetDatabase.DeleteAsset(folder);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }

        [UnityTest]
        public IEnumerator AutoEvents_SceneOpenDelayCall_MigratesSavesReloadsAndRemainsIdempotent()
        {
            const string legacyPrefab =
                "Packages/net.32ba.lattice-deformation-tool/Tests/Editor/Fixtures/" +
                "HistoricalReleases/1.4.0/legacy-brush.prefab";
            string folder = "Assets/__LegacyBrushAutoSceneEvent_" + Guid.NewGuid().ToString("N");
            string scenePath = folder + "/legacy.unity";
            IDisposable scope = null;
            try
            {
                AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(legacyPrefab);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                PrefabUtility.UnpackPrefabInstance(
                    instance,
                    PrefabUnpackMode.OutermostRoot,
                    InteractionMode.AutomatedAction);
                Assert.That(EditorSceneManager.SaveScene(scene, scenePath), Is.True);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                scope = LegacyBrushDeformerAutoMigration.EnableEventExecutionForTests();
                Scene loaded = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                yield return WaitUntil(
                    () => FindSingleLegacy(loaded)?.GetComponent<LatticeDeformer>() != null,
                    "sceneOpened -> delayCall did not migrate the inactive legacy component");

                var legacy = FindSingleLegacy(loaded);
                Assert.That(legacy.gameObject.activeSelf, Is.False);
                Assert.That(legacy.enabled, Is.False);
                Assert.That(CountMigratedLayers(legacy.GetComponent<LatticeDeformer>()), Is.EqualTo(1));
                Assert.That(EditorSceneManager.SaveScene(loaded), Is.True);

                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                loaded = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                yield return null;
                yield return null;
                legacy = FindSingleLegacy(loaded);
                Assert.That(legacy.GetComponent<LatticeDeformer>(), Is.Not.Null);
                Assert.That(CountMigratedLayers(legacy.GetComponent<LatticeDeformer>()), Is.EqualTo(1));
            }
            finally
            {
                scope?.Dispose();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(folder);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }

        [UnityTest]
        public IEnumerator AutoEvents_PrefabImportPostprocessor_MigratesSavesReloadsAndRemainsIdempotent()
        {
            const string source =
                "Packages/net.32ba.lattice-deformation-tool/Tests/Editor/Fixtures/" +
                "HistoricalReleases/1.4.0/legacy-brush.prefab";
            string folder = "Assets/__LegacyBrushAutoImportEvent_" + Guid.NewGuid().ToString("N");
            string copy = folder + "/legacy-brush.prefab";
            IDisposable scope = null;
            try
            {
                AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
                scope = LegacyBrushDeformerAutoMigration.EnableEventExecutionForTests();
                Assert.That(AssetDatabase.CopyAsset(source, copy), Is.True);
                AssetDatabase.ImportAsset(
                    copy,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

                yield return WaitUntil(
                    () => PrefabHasMigratedTarget(copy),
                    "AssetPostprocessor -> delayCall did not migrate and save the imported Prefab");
                Assert.That(CountMigratedLayersInPrefab(copy), Is.EqualTo(1));

                AssetDatabase.ImportAsset(
                    copy,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                yield return null;
                yield return null;
                Assert.That(CountMigratedLayersInPrefab(copy), Is.EqualTo(1));
            }
            finally
            {
                scope?.Dispose();
                AssetDatabase.DeleteAsset(folder);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }

        [UnityTest]
        public IEnumerator AutoEvents_PrefabStageOpen_MigratesDirtySavesAndReopensIdempotently()
        {
            const string source =
                "Packages/net.32ba.lattice-deformation-tool/Tests/Editor/Fixtures/" +
                "HistoricalReleases/1.4.0/legacy-brush.prefab";
            string folder = "Assets/__LegacyBrushAutoStageEvent_" + Guid.NewGuid().ToString("N");
            string copy = folder + "/legacy-brush.prefab";
            IDisposable scope = null;
            try
            {
                AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
                Assert.That(AssetDatabase.CopyAsset(source, copy), Is.True);
                AssetDatabase.ImportAsset(copy, ImportAssetOptions.ForceSynchronousImport);

                scope = LegacyBrushDeformerAutoMigration.EnableEventExecutionForTests();
                PrefabStage stage = PrefabStageUtility.OpenPrefab(copy);
                yield return WaitUntil(
                    () => stage != null &&
                          stage.prefabContentsRoot.GetComponentInChildren<LatticeDeformer>(true) != null,
                    "prefabStageOpened -> delayCall did not migrate the Prefab contents");
                Assert.That(stage.scene.isDirty || PrefabHasMigratedTarget(copy), Is.True,
                    "The Prefab Stage must be dirty or already persisted by Unity auto-save.");
                Assert.That(
                    CountMigratedLayers(
                        stage.prefabContentsRoot.GetComponentInChildren<LatticeDeformer>(true)),
                    Is.EqualTo(1));
                StageUtility.GoBackToPreviousStage();
                yield return WaitUntil(
                    () => PrefabHasMigratedTarget(copy),
                    "Closing the Prefab Stage did not persist the automatic migration");

                stage = PrefabStageUtility.OpenPrefab(copy);
                yield return null;
                yield return null;
                Assert.That(
                    CountMigratedLayers(
                        stage.prefabContentsRoot.GetComponentInChildren<LatticeDeformer>(true)),
                    Is.EqualTo(1));
            }
            finally
            {
                scope?.Dispose();
                if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                {
                    StageUtility.GoBackToPreviousStage();
                }
                AssetDatabase.DeleteAsset(folder);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }

        [UnityTest]
        public IEnumerator AutoEvents_ReadOnlyPrefabImport_WarnsAndLeavesAssetUnchanged()
        {
            const string source =
                "Packages/net.32ba.lattice-deformation-tool/Tests/Editor/Fixtures/" +
                "HistoricalReleases/1.4.0/legacy-brush.prefab";
            string folder = "Assets/__LegacyBrushAutoReadOnlyEvent_" + Guid.NewGuid().ToString("N");
            string copy = folder + "/legacy-brush.prefab";
            string fullPath = System.IO.Path.GetFullPath(copy);
            IDisposable scope = null;
            try
            {
                AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
                Assert.That(AssetDatabase.CopyAsset(source, copy), Is.True);
                AssetDatabase.ImportAsset(copy, ImportAssetOptions.ForceSynchronousImport);
                string before = System.IO.File.ReadAllText(fullPath);
                System.IO.File.SetAttributes(
                    fullPath,
                    System.IO.File.GetAttributes(fullPath) | System.IO.FileAttributes.ReadOnly);

                scope = LegacyBrushDeformerAutoMigration.EnableEventExecutionForTests();
                LogAssert.Expect(
                    LogType.Warning,
                    new System.Text.RegularExpressions.Regex("read-only", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
                AssetDatabase.ImportAsset(
                    copy,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                yield return null;
                yield return null;

                Assert.That(PrefabHasMigratedTarget(copy), Is.False);
                Assert.That(System.IO.File.ReadAllText(fullPath), Is.EqualTo(before));
            }
            finally
            {
                scope?.Dispose();
                if (System.IO.File.Exists(fullPath))
                {
                    System.IO.File.SetAttributes(
                        fullPath,
                        System.IO.File.GetAttributes(fullPath) & ~System.IO.FileAttributes.ReadOnly);
                }
                AssetDatabase.DeleteAsset(folder);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }

        [Test]
        public void TryMigrate_CopiesPayloadOptionsAndOutputAndKeepsLegacyBackup()
        {
            var fixture = CreateFixture("LegacyBrushMigrationSuccess");
            try
            {
                ConfigureLegacyOptions(fixture.Legacy);
                var expectedDisplacements = new[]
                {
                    new Vector3(0.125f, -0.25f, 0.5f),
                    new Vector3(-0.75f, 0.0625f, 0.25f),
                    new Vector3(0.5f, 0.375f, -0.125f),
                    new Vector3(-0.03125f, -0.5f, 0.75f)
                };
                SetLegacyDisplacements(fixture.Legacy, expectedDisplacements);

                Assert.That(
                    LegacyBrushDeformerMigration.TryMigrate(fixture.Legacy, out var target, out var error),
                    Is.True,
                    error);
                Assert.That(target, Is.Not.Null);
                Assert.That(target.gameObject, Is.SameAs(fixture.GameObject));
                Assert.That(fixture.Legacy.enabled, Is.False);
                Assert.That(fixture.GameObject.GetComponent<BrushDeformer>(), Is.SameAs(fixture.Legacy));

                var migratedLayer = FindMigratedLayer(target);
                Assert.That(migratedLayer, Is.Not.Null);
                AssertBitExact(expectedDisplacements, migratedLayer.BrushDisplacements);
                Assert.That(migratedLayer.Enabled, Is.True);
                Assert.That(migratedLayer.Weight, Is.EqualTo(1f));

                var targetSerialized = new SerializedObject(target);
                Assert.That(targetSerialized.FindProperty("_meshFilter").objectReferenceValue, Is.SameAs(fixture.Filter));
                Assert.That(targetSerialized.FindProperty("_skinnedMeshRenderer").objectReferenceValue, Is.Null);
                Assert.That(targetSerialized.FindProperty("_recalculateNormals").boolValue, Is.False);
                Assert.That(targetSerialized.FindProperty("_recalculateTangents").boolValue, Is.True);
                Assert.That(targetSerialized.FindProperty("_recalculateBounds").boolValue, Is.False);
                Assert.That(targetSerialized.FindProperty("_recalculateBoneWeights").boolValue, Is.True);

                var weightSettings = target.WeightTransferSettings;
                Assert.That(weightSettings.maxTransferDistance, Is.EqualTo(0.123f));
                Assert.That(weightSettings.normalAngleThreshold, Is.EqualTo(37f));
                Assert.That(weightSettings.enableInpainting, Is.False);
                Assert.That(weightSettings.maxIterations, Is.EqualTo(4321));
                Assert.That(weightSettings.tolerance, Is.EqualTo(2.5e-5f));

                var output = target.Deform(false);
                Assert.That(output, Is.Not.Null);
                var sourceVertices = fixture.Mesh.vertices;
                var outputVertices = output.vertices;
                Assert.That(outputVertices, Has.Length.EqualTo(sourceVertices.Length));
                for (int i = 0; i < sourceVertices.Length; i++)
                {
                    var expected = sourceVertices[i] + expectedDisplacements[i];
                    Assert.That(outputVertices[i].x, Is.EqualTo(expected.x).Within(1e-6f), $"vertex {i} x");
                    Assert.That(outputVertices[i].y, Is.EqualTo(expected.y).Within(1e-6f), $"vertex {i} y");
                    Assert.That(outputVertices[i].z, Is.EqualTo(expected.z).Within(1e-6f), $"vertex {i} z");
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void TryMigrate_MergesIntoExistingTargetWithoutChangingExistingGroupsOrSelection()
        {
            var fixture = CreateFixture("LegacyBrushMigrationMerge");
            try
            {
                SetLegacyDisplacements(fixture.Legacy, CreateDisplacements(fixture.Mesh.vertexCount, 0.2f));

                var existing = fixture.GameObject.AddComponent<LatticeDeformer>();
                AssignTargetMeshFilter(existing, fixture.Filter, fixture.Mesh);
                int preservedGroupIndex = existing.AddGroup("Preserved Group");
                existing.ActiveGroupIndex = preservedGroupIndex;
                int preservedLayerIndex = existing.AddLayer("Preserved Layer", MeshDeformerLayerType.Lattice);
                var preservedGroup = existing.ActiveGroup;
                var preservedLayer = preservedGroup.Layers[preservedLayerIndex];
                existing.ActiveGroupIndex = 0;
                int activeBefore = existing.ActiveGroupIndex;
                int groupsBefore = existing.GroupCount;

                Assert.That(
                    LegacyBrushDeformerMigration.TryMigrate(fixture.Legacy, out var migrated, out var error),
                    Is.True,
                    error);

                Assert.That(migrated, Is.SameAs(existing));
                Assert.That(existing.GroupCount, Is.EqualTo(groupsBefore + 1));
                Assert.That(existing.ActiveGroupIndex, Is.EqualTo(activeBefore));
                Assert.That(existing.Groups.Contains(preservedGroup), Is.True);
                Assert.That(preservedGroup.Layers.Contains(preservedLayer), Is.True);
                Assert.That(FindMigratedLayer(existing), Is.Not.Null);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void TryMigrate_ExistingTargetWithDifferentKnownSourceFailsWithoutMutation()
        {
            var fixture = CreateFixture("LegacyBrushMigrationDifferentTargetSource");
            Mesh differentSource = null;
            try
            {
                var legacyDisplacements = CreateDisplacements(fixture.Mesh.vertexCount, 0.23f);
                SetLegacyDisplacements(fixture.Legacy, legacyDisplacements);

                var existing = fixture.GameObject.AddComponent<LatticeDeformer>();
                AssignTargetMeshFilter(existing, fixture.Filter, fixture.Mesh);
                int preservedGroupIndex = existing.AddGroup("Preserved Different Source Group");
                existing.ActiveGroupIndex = preservedGroupIndex;
                int preservedLayerIndex = existing.AddLayer(
                    "Preserved Different Source Layer",
                    MeshDeformerLayerType.Lattice);
                var preservedGroup = existing.ActiveGroup;
                var preservedLayer = preservedGroup.Layers[preservedLayerIndex];

                differentSource = UnityEngine.Object.Instantiate(fixture.Mesh);
                differentSource.name = "Different Existing Target Source";
                var targetSerialized = new SerializedObject(existing);
                targetSerialized.FindProperty("_serializedSourceMesh").objectReferenceValue = differentSource;
                targetSerialized.ApplyModifiedPropertiesWithoutUndo();

                string targetBefore = EditorJsonUtility.ToJson(existing);
                string legacyBefore = EditorJsonUtility.ToJson(fixture.Legacy);
                var filterMeshBefore = fixture.Filter.sharedMesh;

                Assert.That(
                    LegacyBrushDeformerMigration.TryMigrate(fixture.Legacy, out var migrated, out var error),
                    Is.False);

                Assert.That(migrated, Is.Null);
                Assert.That(error, Does.Contain("different source mesh"));
                Assert.That(EditorJsonUtility.ToJson(existing), Is.EqualTo(targetBefore));
                Assert.That(EditorJsonUtility.ToJson(fixture.Legacy), Is.EqualTo(legacyBefore));
                Assert.That(fixture.Legacy.enabled, Is.True);
                Assert.That(fixture.Filter.sharedMesh, Is.SameAs(filterMeshBefore));
                Assert.That(preservedGroup.Layers.Contains(preservedLayer), Is.True);
                AssertBitExact(legacyDisplacements, fixture.Legacy.Displacements);

                targetSerialized.UpdateIfRequiredOrScript();
                Assert.That(
                    targetSerialized.FindProperty("_serializedSourceMesh").objectReferenceValue,
                    Is.SameAs(differentSource));
                Assert.That(
                    targetSerialized.FindProperty("_meshFilter").objectReferenceValue,
                    Is.SameAs(fixture.Filter));
            }
            finally
            {
                if (differentSource != null) UnityEngine.Object.DestroyImmediate(differentSource);
                fixture.Dispose();
            }
        }

        [Test]
        public void TryMigrate_ExistingTargetWithDifferentSettingsFailsWithoutMutation()
        {
            var fixture = CreateFixture("LegacyBrushMigrationDifferentTargetSettings");
            try
            {
                var legacyDisplacements = CreateDisplacements(fixture.Mesh.vertexCount, 0.27f);
                SetLegacyDisplacements(fixture.Legacy, legacyDisplacements);

                var legacySerialized = new SerializedObject(fixture.Legacy);
                legacySerialized.FindProperty("_recalculateNormals").boolValue = false;
                legacySerialized.ApplyModifiedPropertiesWithoutUndo();

                var existing = fixture.GameObject.AddComponent<LatticeDeformer>();
                AssignTargetMeshFilter(existing, fixture.Filter, fixture.Mesh);
                int preservedGroupIndex = existing.AddGroup("Preserved Settings Group");
                existing.ActiveGroupIndex = preservedGroupIndex;
                int preservedLayerIndex = existing.AddLayer(
                    "Preserved Settings Layer",
                    MeshDeformerLayerType.Lattice);
                var preservedGroup = existing.ActiveGroup;
                var preservedLayer = preservedGroup.Layers[preservedLayerIndex];

                string targetBefore = EditorJsonUtility.ToJson(existing);
                string legacyBefore = EditorJsonUtility.ToJson(fixture.Legacy);
                var filterMeshBefore = fixture.Filter.sharedMesh;

                Assert.That(
                    LegacyBrushDeformerMigration.TryMigrate(fixture.Legacy, out var migrated, out var error),
                    Is.False);

                Assert.That(migrated, Is.Null);
                Assert.That(error, Does.Contain("different rebuild or weight-transfer settings"));
                Assert.That(EditorJsonUtility.ToJson(existing), Is.EqualTo(targetBefore));
                Assert.That(EditorJsonUtility.ToJson(fixture.Legacy), Is.EqualTo(legacyBefore));
                Assert.That(fixture.Legacy.enabled, Is.True);
                Assert.That(fixture.Filter.sharedMesh, Is.SameAs(filterMeshBefore));
                Assert.That(preservedGroup.Layers.Contains(preservedLayer), Is.True);
                AssertBitExact(legacyDisplacements, fixture.Legacy.Displacements);

                var targetSerialized = new SerializedObject(existing);
                Assert.That(targetSerialized.FindProperty("_recalculateNormals").boolValue, Is.True);
                Assert.That(
                    targetSerialized.FindProperty("_serializedSourceMesh").objectReferenceValue,
                    Is.SameAs(fixture.Mesh));
                Assert.That(
                    targetSerialized.FindProperty("_meshFilter").objectReferenceValue,
                    Is.SameAs(fixture.Filter));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void TryMigrate_RepeatedCallIsIdempotent()
        {
            var fixture = CreateFixture("LegacyBrushMigrationIdempotent");
            try
            {
                var expected = CreateDisplacements(fixture.Mesh.vertexCount, 0.3f);
                SetLegacyDisplacements(fixture.Legacy, expected);

                Assert.That(
                    LegacyBrushDeformerMigration.TryMigrate(fixture.Legacy, out var firstTarget, out var firstError),
                    Is.True,
                    firstError);
                int groupsAfterFirst = firstTarget.GroupCount;
                int matchingLayersAfterFirst = CountMatchingMigratedLayers(firstTarget, expected);

                Assert.That(
                    LegacyBrushDeformerMigration.TryMigrate(fixture.Legacy, out var secondTarget, out var secondError),
                    Is.True,
                    secondError);

                Assert.That(secondTarget, Is.SameAs(firstTarget));
                Assert.That(secondTarget.GroupCount, Is.EqualTo(groupsAfterFirst));
                Assert.That(CountMatchingMigratedLayers(secondTarget, expected), Is.EqualTo(matchingLayersAfterFirst));
                Assert.That(matchingLayersAfterFirst, Is.EqualTo(1));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void TryMigrate_DisabledLegacyDoesNotMistakeUnrelatedIdenticalBrushLayerForMigration()
        {
            var fixture = CreateFixture("LegacyBrushMigrationMarker");
            try
            {
                var expected = CreateDisplacements(fixture.Mesh.vertexCount, 0.33f);
                SetLegacyDisplacements(fixture.Legacy, expected);

                var existing = fixture.GameObject.AddComponent<LatticeDeformer>();
                AssignTargetMeshFilter(existing, fixture.Filter, fixture.Mesh);
                int unrelatedGroup = existing.AddGroup("Unrelated Group");
                existing.ActiveGroupIndex = unrelatedGroup;
                int unrelatedLayerIndex = existing.AddLayer(
                    "Unrelated Identical Brush",
                    MeshDeformerLayerType.Brush);
                existing.ActiveGroup.LayersList[unrelatedLayerIndex].BrushDisplacements =
                    (Vector3[])expected.Clone();

                int groupMarkerOnly = existing.AddGroup(
                    LegacyBrushDeformerMigration.MigratedGroupName);
                existing.ActiveGroupIndex = groupMarkerOnly;
                int groupMarkerOnlyLayer = existing.AddLayer(
                    "Unrelated Layer In Marker-Named Group",
                    MeshDeformerLayerType.Brush);
                existing.ActiveGroup.LayersList[groupMarkerOnlyLayer].BrushDisplacements =
                    (Vector3[])expected.Clone();

                int layerMarkerOnlyGroup = existing.AddGroup("Unrelated Group With Marker-Named Layer");
                existing.ActiveGroupIndex = layerMarkerOnlyGroup;
                int layerMarkerOnly = existing.AddLayer(
                    LegacyBrushDeformerMigration.MigratedLayerName,
                    MeshDeformerLayerType.Brush);
                existing.ActiveGroup.LayersList[layerMarkerOnly].BrushDisplacements =
                    (Vector3[])expected.Clone();
                int groupsBefore = existing.GroupCount;

                fixture.Legacy.enabled = false;

                Assert.That(
                    LegacyBrushDeformerMigration.TryMigrate(
                        fixture.Legacy,
                        out var migrated,
                        out var error),
                    Is.True,
                    error);

                Assert.That(migrated, Is.SameAs(existing));
                Assert.That(existing.GroupCount, Is.EqualTo(groupsBefore + 1));
                Assert.That(
                    existing.Groups.Any(group =>
                        group != null &&
                        group.Name == LegacyBrushDeformerMigration.MigratedGroupName &&
                        group.Layers.Any(layer =>
                            layer != null &&
                            layer.Name == LegacyBrushDeformerMigration.MigratedLayerName)),
                    Is.True,
                    "Idempotence requires an explicit migration marker, not payload coincidence alone.");
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void TryMigrate_DisabledMarkedBackupStillRejectsDifferentKnownTargetSource()
        {
            var fixture = CreateFixture("LegacyBrushMigrationMarkedDifferentSource");
            Mesh differentSource = null;
            try
            {
                SetLegacyDisplacements(
                    fixture.Legacy,
                    CreateDisplacements(fixture.Mesh.vertexCount, 0.37f));
                Assert.That(
                    LegacyBrushDeformerMigration.TryMigrate(
                        fixture.Legacy,
                        out var target,
                        out var firstError),
                    Is.True,
                    firstError);

                differentSource = UnityEngine.Object.Instantiate(fixture.Mesh);
                differentSource.name = "Different Source After Marked Migration";
                var serializedTarget = new SerializedObject(target);
                serializedTarget.FindProperty("_serializedSourceMesh").objectReferenceValue = differentSource;
                serializedTarget.ApplyModifiedPropertiesWithoutUndo();

                string targetBefore = EditorJsonUtility.ToJson(target);
                string legacyBefore = EditorJsonUtility.ToJson(fixture.Legacy);

                Assert.That(
                    LegacyBrushDeformerMigration.TryMigrate(
                        fixture.Legacy,
                        out var repeatedTarget,
                        out var repeatedError),
                    Is.False);
                Assert.That(repeatedTarget, Is.Null);
                Assert.That(repeatedError, Does.Contain("different source mesh"));
                Assert.That(EditorJsonUtility.ToJson(target), Is.EqualTo(targetBefore));
                Assert.That(EditorJsonUtility.ToJson(fixture.Legacy), Is.EqualTo(legacyBefore));
                Assert.That(fixture.Legacy.enabled, Is.False);
            }
            finally
            {
                if (differentSource != null) UnityEngine.Object.DestroyImmediate(differentSource);
                fixture.Dispose();
            }
        }

        [Test]
        public void TryMigrate_VertexCountMismatchFailsWithoutPersistentMutation()
        {
            var fixture = CreateFixture("LegacyBrushMigrationMismatch");
            try
            {
                var invalid = CreateDisplacements(fixture.Mesh.vertexCount - 1, 0.4f);
                SetLegacyDisplacements(fixture.Legacy, invalid);
                bool enabledBefore = fixture.Legacy.enabled;
                var filterMeshBefore = fixture.Filter.sharedMesh;

                Assert.That(
                    LegacyBrushDeformerMigration.TryMigrate(fixture.Legacy, out var target, out var error),
                    Is.False);

                Assert.That(target, Is.Null);
                Assert.That(error, Does.Contain("does not match"));
                Assert.That(fixture.Legacy.enabled, Is.EqualTo(enabledBefore));
                Assert.That(fixture.GameObject.GetComponent<LatticeDeformer>(), Is.Null);
                Assert.That(fixture.Filter.sharedMesh, Is.SameAs(filterMeshBefore));
                AssertBitExact(invalid, fixture.Legacy.Displacements);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void TryMigrate_CanBeUndoneAsOneOperation()
        {
            var fixture = CreateFixture("LegacyBrushMigrationUndo");
            try
            {
                SetLegacyDisplacements(fixture.Legacy, CreateDisplacements(fixture.Mesh.vertexCount, 0.5f));

                Assert.That(
                    LegacyBrushDeformerMigration.TryMigrate(fixture.Legacy, out var target, out var error),
                    Is.True,
                    error);
                Assert.That(target, Is.Not.Null);
                Assert.That(fixture.Legacy.enabled, Is.False);

                Undo.PerformUndo();

                Assert.That(fixture.Legacy.enabled, Is.True);
                Assert.That(fixture.GameObject.GetComponent<LatticeDeformer>(), Is.Null);
                Assert.That(fixture.GameObject.GetComponent<BrushDeformer>(), Is.SameAs(fixture.Legacy));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void BrushEditor_ClearAllRespectsPreviewRendererAssignmentPolicy()
        {
            var fixture = CreateFixture("LegacyBrushClearAssignment");
            try
            {
                SetLegacyDisplacements(
                    fixture.Legacy,
                    CreateDisplacements(fixture.Mesh.vertexCount, 0.75f));

                fixture.Legacy.RestoreOriginalMesh();
                Assert.That(fixture.Filter.sharedMesh, Is.SameAs(fixture.Mesh));

                BrushDeformerEditor.ClearAllDisplacements(
                    fixture.Legacy,
                    assignRuntimeMesh: false);

                Assert.That(
                    fixture.Filter.sharedMesh,
                    Is.SameAs(fixture.Mesh),
                    "NDMF preview mode must leave the original renderer mesh untouched.");
                Assert.That(fixture.Legacy.RuntimeMesh, Is.Not.Null);
                Assert.That(fixture.Legacy.Displacements, Is.All.EqualTo(Vector3.zero));

                SetLegacyDisplacements(
                    fixture.Legacy,
                    CreateDisplacements(fixture.Mesh.vertexCount, 0.5f));
                fixture.Legacy.RestoreOriginalMesh();

                BrushDeformerEditor.ClearAllDisplacements(
                    fixture.Legacy,
                    assignRuntimeMesh: true);

                Assert.That(fixture.Legacy.RuntimeMesh, Is.Not.Null);
                Assert.That(fixture.Filter.sharedMesh, Is.SameAs(fixture.Legacy.RuntimeMesh));
                Assert.That(fixture.Legacy.Displacements, Is.All.EqualTo(Vector3.zero));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void BrushEditor_MultiObjectMigrationRollsBackEarlierTargetsWhenLaterValidationFails()
        {
            var valid = CreateFixture("LegacyBrushBatchValid");
            var invalid = CreateFixture("LegacyBrushBatchInvalid");
            try
            {
                var validDisplacements = CreateDisplacements(valid.Mesh.vertexCount, 0.41f);
                SetLegacyDisplacements(
                    valid.Legacy,
                    validDisplacements);
                SetLegacyDisplacements(
                    invalid.Legacy,
                    CreateDisplacements(invalid.Mesh.vertexCount - 1, 0.43f));

                var runtimeBeforeMigration = valid.Legacy.Deform(true);
                Assert.That(runtimeBeforeMigration, Is.Not.Null);
                Assert.That(valid.Filter.sharedMesh, Is.SameAs(runtimeBeforeMigration));

                Assert.That(
                    BrushDeformerEditor.TryMigrateAll(
                        new[] { valid.Legacy, invalid.Legacy },
                        out var failure),
                    Is.False);

                Assert.That(failure, Does.Contain(invalid.GameObject.name));
                Assert.That(valid.Legacy.enabled, Is.True);
                Assert.That(invalid.Legacy.enabled, Is.True);
                Assert.That(valid.GameObject.GetComponent<LatticeDeformer>(), Is.Null);
                Assert.That(invalid.GameObject.GetComponent<LatticeDeformer>(), Is.Null);
                Assert.That(valid.Legacy.RuntimeMesh, Is.Not.Null);
                Assert.That(valid.Filter.sharedMesh, Is.SameAs(valid.Legacy.RuntimeMesh));
                var restoredVertices = valid.Legacy.RuntimeMesh.vertices;
                var sourceVertices = valid.Mesh.vertices;
                for (int i = 0; i < restoredVertices.Length; i++)
                {
                    var expected = sourceVertices[i] + validDisplacements[i];
                    Assert.That(restoredVertices[i].x, Is.EqualTo(expected.x).Within(1e-6f));
                    Assert.That(restoredVertices[i].y, Is.EqualTo(expected.y).Within(1e-6f));
                    Assert.That(restoredVertices[i].z, Is.EqualTo(expected.z).Within(1e-6f));
                }
            }
            finally
            {
                valid.Dispose();
                invalid.Dispose();
            }
        }

        [Test]
        public void TryMigrate_FailedProbeDoesNotInitializeExistingTarget()
        {
            var fixture = CreateFixture("LegacyBrushProbeMustBePure");
            try
            {
                var existing = fixture.GameObject.AddComponent<LatticeDeformer>();
                var targetSerialized = new SerializedObject(existing);
                targetSerialized.FindProperty("_groups").arraySize = 0;
                targetSerialized.FindProperty("_layers").arraySize = 0;
                targetSerialized.FindProperty("_activeGroupIndex").intValue = 0;
                targetSerialized.FindProperty("_activeLayerIndex").intValue = 0;
                targetSerialized.FindProperty("_deformationDataVersion").intValue =
                    (int)DeformationDataVersion.CurrentDevelopment;
                targetSerialized.FindProperty("_layerModelVersion").intValue = 3;
                targetSerialized.ApplyModifiedPropertiesWithoutUndo();

                fixture.Legacy.enabled = false;
                fixture.Filter.sharedMesh = null;
                var legacySerialized = new SerializedObject(fixture.Legacy);
                legacySerialized.FindProperty("_serializedSourceMesh").objectReferenceValue = null;
                legacySerialized.ApplyModifiedPropertiesWithoutUndo();
                typeof(BrushDeformer)
                    .GetField("_sourceMesh", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(fixture.Legacy, null);

                string before = EditorJsonUtility.ToJson(existing);

                Assert.That(
                    LegacyBrushDeformerMigration.TryMigrate(fixture.Legacy, out var target, out var error),
                    Is.False);
                Assert.That(target, Is.Null);
                Assert.That(error, Does.Contain("No source mesh"));
                Assert.That(EditorJsonUtility.ToJson(existing), Is.EqualTo(before),
                    "A validation-only layer probe must not run public lazy initialization.");
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void TryMigrate_HandlesNullFallbackRendererAndNonFiniteOutput()
        {
            Assert.That(
                LegacyBrushDeformerMigration.TryMigrate(null, out var missingTarget, out var missingError),
                Is.False);
            Assert.That(missingTarget, Is.Null);
            Assert.That(missingError, Does.Contain("missing"));

            var fallback = CreateFixture("LegacyBrushFallbackRenderer");
            try
            {
                SetLegacyDisplacements(fallback.Legacy, CreateDisplacements(fallback.Mesh.vertexCount, 0.1f));
                var serialized = new SerializedObject(fallback.Legacy);
                serialized.FindProperty("_meshFilter").objectReferenceValue = null;
                serialized.FindProperty("_skinnedMeshRenderer").objectReferenceValue = null;
                serialized.FindProperty("_serializedSourceMesh").objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                typeof(BrushDeformer)
                    .GetField("_sourceMesh", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(fallback.Legacy, null);

                Assert.That(
                    LegacyBrushDeformerMigration.TryMigrate(fallback.Legacy, out var target, out var error),
                    Is.True,
                    error);
                Assert.That(target, Is.Not.Null);
            }
            finally
            {
                fallback.Dispose();
            }

            var nonFinite = CreateFixture("LegacyBrushNonFinite");
            try
            {
                var displacements = CreateDisplacements(nonFinite.Mesh.vertexCount, 0.1f);
                displacements[0] = new Vector3(float.NaN, 0f, 0f);
                SetLegacyDisplacements(nonFinite.Legacy, displacements);

                Assert.That(
                    LegacyBrushDeformerMigration.TryMigrate(nonFinite.Legacy, out _, out var error),
                    Is.False);
                Assert.That(error, Is.Not.Empty);
            }
            finally
            {
                nonFinite.Dispose();
            }
        }

        [Test]
        public void TryMigrate_SkinnedRendererPath_AssignsAndRestoresSource()
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.AddBlendShapeFrame(
                "Legacy Shape",
                100f,
                new[] { Vector3.forward, Vector3.forward, Vector3.forward },
                new Vector3[3],
                new Vector3[3]);
            var go = new GameObject("LegacyBrushSkinnedRenderer");
            try
            {
                var renderer = go.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.SetBlendShapeWeight(0, 42f);
                var legacy = go.AddComponent<BrushDeformer>();
                var serialized = new SerializedObject(legacy);
                serialized.FindProperty("_skinnedMeshRenderer").objectReferenceValue = renderer;
                serialized.FindProperty("_meshFilter").objectReferenceValue = null;
                serialized.FindProperty("_serializedSourceMesh").objectReferenceValue = mesh;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                legacy.CacheSourceMesh();
                SetLegacyDisplacements(legacy, CreateDisplacements(mesh.vertexCount, 0.05f));

                Assert.That(
                    LegacyBrushDeformerMigration.TryMigrate(legacy, out var target, out var error),
                    Is.True,
                    error);
                Assert.That(target, Is.Not.Null);
                Assert.That(renderer.sharedMesh, Is.SameAs(mesh));
                Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(42f));
                Assert.That(legacy.enabled, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void MigrationPrivateHelpers_HandleMissingAndAlternativeSources()
        {
            var fixture = CreateFixture("LegacyBrushPrivateHelpers");
            var detached = new GameObject("LegacyBrushDetachedTarget");
            var alternateMesh = UnityEngine.Object.Instantiate(fixture.Mesh);
            try
            {
                Assert.That(InvokeMigrationPrivate("ResolveKnownTargetSource", (object)null), Is.Null);
                Assert.That(
                    InvokeMigrationPrivate("HasEquivalentComponentSettings", null, null),
                    Is.EqualTo(false));
                Assert.That(
                    InvokeMigrationPrivate("HasSerializedMigratedLayer", null, null),
                    Is.EqualTo(false));

                var settingsWithoutPayload = (WeightTransferSettingsData)InvokeMigrationPrivate(
                    "ReadWeightTransferSettings",
                    new SerializedObject(fixture.Mesh));
                Assert.That(settingsWithoutPayload, Is.Not.Null);

                var target = detached.AddComponent<LatticeDeformer>();
                var skinned = detached.AddComponent<SkinnedMeshRenderer>();
                skinned.sharedMesh = alternateMesh;
                var targetSerialized = new SerializedObject(target);
                targetSerialized.FindProperty("_skinnedMeshRenderer").objectReferenceValue = skinned;
                targetSerialized.FindProperty("_meshFilter").objectReferenceValue = null;
                targetSerialized.FindProperty("_serializedSourceMesh").objectReferenceValue = null;
                targetSerialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(
                    InvokeMigrationPrivate("ResolveKnownTargetSource", target),
                    Is.SameAs(alternateMesh));

                targetSerialized.FindProperty("_skinnedMeshRenderer").objectReferenceValue = null;
                targetSerialized.FindProperty("_meshFilter").objectReferenceValue = null;
                targetSerialized.FindProperty("_serializedSourceMesh").objectReferenceValue = null;
                targetSerialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(
                    InvokeMigrationPrivate("ResolveKnownTargetSource", target),
                    Is.SameAs(alternateMesh));
                UnityEngine.Object.DestroyImmediate(skinned);
                var filter = detached.AddComponent<MeshFilter>();
                filter.sharedMesh = alternateMesh;
                Assert.That(
                    InvokeMigrationPrivate("ResolveKnownTargetSource", target),
                    Is.SameAs(alternateMesh));
                targetSerialized = new SerializedObject(target);
                targetSerialized.FindProperty("_meshFilter").objectReferenceValue = filter;
                targetSerialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(
                    InvokeMigrationPrivate("ResolveKnownTargetSource", target),
                    Is.SameAs(alternateMesh));

                Assert.That(
                    InvokeMigrationPrivate("FindContainingGroup", target, new LatticeLayer()),
                    Is.Null);
                Assert.That(
                    InvokeMigrationPrivate("FindContainingGroup", null, null),
                    Is.Null);

                var tryFindArgs = new object[] { null, Array.Empty<Vector3>(), true, null };
                Assert.That((bool)InvokeMigrationPrivateWithArguments("TryFindMigratedLayer", tryFindArgs), Is.False);
                Assert.That(tryFindArgs[3], Is.Null);

                typeof(LatticeDeformer).GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(target, new System.Collections.Generic.List<DeformerGroup> { null });
                typeof(LatticeDeformer).GetField("_layers", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(target, new System.Collections.Generic.List<LatticeLayer>());
                typeof(LatticeDeformer).GetField("_deformationDataVersion", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(target, DeformationDataVersion.CurrentDevelopment);
                tryFindArgs = new object[] { target, Array.Empty<Vector3>(), true, null };
                Assert.That((bool)InvokeMigrationPrivateWithArguments("TryFindMigratedLayer", tryFindArgs), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(alternateMesh);
                UnityEngine.Object.DestroyImmediate(detached);
                fixture.Dispose();
            }
        }

        [Test]
        public void MigrationPrivateValidation_RejectsMismatchedSerializedAndEvaluatedOutput()
        {
            var fixture = CreateFixture("LegacyBrushPrivateValidation");
            try
            {
                var displacements = CreateDisplacements(fixture.Mesh.vertexCount, 0.17f);
                SetLegacyDisplacements(fixture.Legacy, displacements);
                Assert.That(
                    LegacyBrushDeformerMigration.TryMigrate(fixture.Legacy, out var target, out var error),
                    Is.True,
                    error);

                var changed = (Vector3[])displacements.Clone();
                changed[0] += Vector3.one;
                Assert.That(
                    InvokeMigrationPrivate("HasSerializedMigratedLayer", target, changed),
                    Is.EqualTo(false));

                var layer = FindMigratedLayer(target);
                var group = target.Groups.First(candidate => candidate.Layers.Contains(layer));
                var tryFindArgs = new object[] { target, changed, true, null };
                Assert.That(
                    (bool)InvokeMigrationPrivateWithArguments("TryFindMigratedLayer", tryFindArgs),
                    Is.False);
                var targetArgs = new object[]
                {
                    target,
                    group,
                    layer,
                    null,
                    fixture.Mesh,
                    changed,
                    null
                };
                Assert.That(
                    (bool)InvokeMigrationPrivateWithArguments("ValidateTargetBrushOutput", targetArgs),
                    Is.False);
                Assert.That((string)targetArgs[6], Does.Contain("differs"));

                layer.BrushDisplacements = new[] { Vector3.zero };
                targetArgs[5] = displacements;
                targetArgs[6] = null;
                Assert.That(
                    (bool)InvokeMigrationPrivateWithArguments("ValidateTargetBrushOutput", targetArgs),
                    Is.False);
                Assert.That((string)targetArgs[6], Is.Not.Empty);

                var brushArgs = new object[] { null, Array.Empty<Vector3>(), null };
                Assert.That(
                    (bool)InvokeMigrationPrivateWithArguments("ValidateBrushOutput", brushArgs),
                    Is.False);
                Assert.That((string)brushArgs[2], Is.Not.Empty);

            }
            finally
            {
                fixture.Dispose();
            }
        }

        private static object InvokeMigrationPrivate(string name, params object[] args)
        {
            return InvokeMigrationPrivateWithArguments(name, args);
        }

        private static object InvokeMigrationPrivateWithArguments(string name, object[] args)
        {
            var methods = typeof(LegacyBrushDeformerMigration)
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Where(method => method.Name == name && method.GetParameters().Length == args.Length)
                .ToArray();
            Assert.That(methods, Has.Length.EqualTo(1), name);
            return methods[0].Invoke(null, args);
        }

        private static IEnumerator WaitUntil(Func<bool> predicate, string failure)
        {
            const int maxFrames = 120;
            for (int frame = 0; frame < maxFrames; frame++)
            {
                if (predicate()) yield break;
                yield return null;
            }
            Assert.Fail(failure);
        }

        private static BrushDeformer FindSingleLegacy(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<BrushDeformer>(true))
                .SingleOrDefault();
        }

        private static bool PrefabHasMigratedTarget(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return prefab != null && prefab.GetComponentInChildren<LatticeDeformer>(true) != null;
        }

        private static int CountMigratedLayersInPrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return prefab == null
                ? 0
                : CountMigratedLayers(prefab.GetComponentInChildren<LatticeDeformer>(true));
        }

        private static int CountMigratedLayers(LatticeDeformer target)
        {
            if (target == null) return 0;
            return target.Groups
                .Where(group => group != null &&
                                group.Name == LegacyBrushDeformerMigration.MigratedGroupName)
                .SelectMany(group => group.Layers)
                .Count(layer => layer != null &&
                                layer.Type == MeshDeformerLayerType.Brush &&
                                layer.Name == LegacyBrushDeformerMigration.MigratedLayerName);
        }

        private static Fixture CreateFixture(string name)
        {
            var mesh = new Mesh
            {
                name = name + " Mesh",
                vertices = new[]
                {
                    new Vector3(-1f, -1f, 0f),
                    new Vector3(1f, -1f, 0f),
                    new Vector3(1f, 1f, 0f),
                    new Vector3(-1f, 1f, 0f)
                },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            var gameObject = new GameObject(name);
            var filter = gameObject.AddComponent<MeshFilter>();
            gameObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;

            var legacy = gameObject.AddComponent<BrushDeformer>();
            var serialized = new SerializedObject(legacy);
            serialized.FindProperty("_meshFilter").objectReferenceValue = filter;
            serialized.FindProperty("_skinnedMeshRenderer").objectReferenceValue = null;
            serialized.FindProperty("_serializedSourceMesh").objectReferenceValue = mesh;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            legacy.CacheSourceMesh();

            return new Fixture(gameObject, filter, mesh, legacy);
        }

        private static void ConfigureLegacyOptions(BrushDeformer legacy)
        {
            var serialized = new SerializedObject(legacy);
            serialized.FindProperty("_recalculateNormals").boolValue = false;
            serialized.FindProperty("_recalculateTangents").boolValue = true;
            serialized.FindProperty("_recalculateBounds").boolValue = false;
            serialized.FindProperty("_recalculateBoneWeights").boolValue = true;

            var settings = serialized.FindProperty("_weightTransferSettings");
            settings.FindPropertyRelative("maxTransferDistance").floatValue = 0.123f;
            settings.FindPropertyRelative("normalAngleThreshold").floatValue = 37f;
            settings.FindPropertyRelative("enableInpainting").boolValue = false;
            settings.FindPropertyRelative("maxIterations").intValue = 4321;
            settings.FindPropertyRelative("tolerance").floatValue = 2.5e-5f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetLegacyDisplacements(BrushDeformer legacy, Vector3[] values)
        {
            var serialized = new SerializedObject(legacy);
            var property = serialized.FindProperty("_displacements");
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).vector3Value = values[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignTargetMeshFilter(LatticeDeformer target, MeshFilter filter, Mesh source)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty("_meshFilter").objectReferenceValue = filter;
            serialized.FindProperty("_skinnedMeshRenderer").objectReferenceValue = null;
            serialized.FindProperty("_serializedSourceMesh").objectReferenceValue = source;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            target.InvalidateCache();
        }

        private static LatticeLayer FindMigratedLayer(LatticeDeformer target)
        {
            return target.Groups
                .Where(group => group != null)
                .SelectMany(group => group.Layers)
                .FirstOrDefault(layer =>
                    layer != null &&
                    layer.Type == MeshDeformerLayerType.Brush &&
                    layer.Name == LegacyBrushDeformerMigration.MigratedLayerName);
        }

        private static int CountMatchingMigratedLayers(LatticeDeformer target, Vector3[] expected)
        {
            return target.Groups
                .Where(group => group != null)
                .SelectMany(group => group.Layers)
                .Count(layer =>
                    layer != null &&
                    layer.Type == MeshDeformerLayerType.Brush &&
                    layer.Name == LegacyBrushDeformerMigration.MigratedLayerName &&
                    AreBitExact(expected, layer.BrushDisplacements));
        }

        private static Vector3[] CreateDisplacements(int count, float scale)
        {
            var result = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = new Vector3(scale * (i + 1), -scale * i, scale * 0.5f);
            }
            return result;
        }

        private static void AssertBitExact(Vector3[] expected, Vector3[] actual)
        {
            Assert.That(actual, Has.Length.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                AssertVectorBitExact(expected[i], actual[i], i);
            }
        }

        private static bool AreBitExact(Vector3[] left, Vector3[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++)
            {
                if (!AreBitExact(left[i].x, right[i].x) ||
                    !AreBitExact(left[i].y, right[i].y) ||
                    !AreBitExact(left[i].z, right[i].z))
                {
                    return false;
                }
            }
            return true;
        }

        private static void AssertVectorBitExact(Vector3 expected, Vector3 actual, int index)
        {
            Assert.That(AreBitExact(expected.x, actual.x), Is.True, $"vertex {index} x");
            Assert.That(AreBitExact(expected.y, actual.y), Is.True, $"vertex {index} y");
            Assert.That(AreBitExact(expected.z, actual.z), Is.True, $"vertex {index} z");
        }

        private static bool AreBitExact(float left, float right)
        {
            return BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right);
        }

        private sealed class Fixture : IDisposable
        {
            internal Fixture(GameObject gameObject, MeshFilter filter, Mesh mesh, BrushDeformer legacy)
            {
                GameObject = gameObject;
                Filter = filter;
                Mesh = mesh;
                Legacy = legacy;
            }

            internal GameObject GameObject { get; }
            internal MeshFilter Filter { get; }
            internal Mesh Mesh { get; }
            internal BrushDeformer Legacy { get; }

            public void Dispose()
            {
                if (GameObject != null) UnityEngine.Object.DestroyImmediate(GameObject);
                if (Mesh != null) UnityEngine.Object.DestroyImmediate(Mesh);
            }
        }
    }
}
#endif
