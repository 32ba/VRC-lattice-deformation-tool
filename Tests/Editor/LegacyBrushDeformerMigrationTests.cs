#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

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
