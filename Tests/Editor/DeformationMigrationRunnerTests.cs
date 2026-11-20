#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class DeformationMigrationRunnerTests
    {
        private static IEnumerable<int> ReleaseBoundaries()
        {
            for (int version = 0; version < (int)DeformationDataVersion.CurrentDevelopment; version++)
                yield return version;
        }

        [TestCaseSource(nameof(ReleaseBoundaries))]
        public void CommitFailure_PreservesEntireBoundaryAndRetryAdvancesExactlyOnce(int version)
        {
            var state = CreateState((DeformationDataVersion)version);
            var originalGroups = state.Groups;
            var originalFlat = state.FlatLayers;
            var originalSettings = state.Settings;
            string before = Payload(state);
            bool rejectCommit = true;
            int commits = 0;
            var runner = new DeformationMigrationRunner(state, _ =>
            {
                commits++;
                if (rejectCommit) throw new InvalidOperationException("Injected before release commit");
            });

            Assert.That(runner.TryAdvanceOneRelease(), Is.False);
            Assert.That(commits, Is.EqualTo(1), "The failure must occur after the release transformation.");
            Assert.That(runner.State, Is.SameAs(state));
            Assert.That(state.Status, Is.EqualTo(DeformationDataMigrationStatus.InvalidData));
            Assert.That(state.CommitRequested, Is.False);
            Assert.That(Payload(state), Is.EqualTo(before));
            Assert.That(state.Groups, Is.SameAs(originalGroups));
            Assert.That(state.FlatLayers, Is.SameAs(originalFlat));
            Assert.That(state.Settings, Is.SameAs(originalSettings));

            rejectCommit = false;
            Assert.That(runner.TryAdvanceOneRelease(), Is.True);
            Assert.That(commits, Is.EqualTo(2));
            Assert.That((int)state.Version, Is.EqualTo(version + 1));
            Assert.That(state.CommitRequested, Is.True);
            Assert.That(state.SourceVersion, Is.EqualTo(version == 0 ? DeformationDataVersion.V0_0_1 : (DeformationDataVersion)version));
        }

        [Test]
        public void FailedLaterStep_PreservesPreviouslyCommittedBoundary()
        {
            var state = CreateState(DeformationDataVersion.V1_2_0);
            bool rejectCommit = false;
            var runner = new DeformationMigrationRunner(state, _ =>
            {
                if (rejectCommit) throw new InvalidOperationException("Later boundary failed");
            });
            Assert.That(runner.TryAdvanceOneRelease(), Is.True);
            Assert.That(state.Version, Is.EqualTo(DeformationDataVersion.V1_2_1));
            string afterFirst = Payload(state);
            rejectCommit = true;
            Assert.That(runner.TryAdvanceOneRelease(), Is.False);
            Assert.That(Payload(state), Is.EqualTo(afterFirst));
            Assert.That(state.Version, Is.EqualTo(DeformationDataVersion.V1_2_1));
        }

        [Test]
        public void LegacyWorld_WaitsForValidOwnerMatrixWithoutRewritingControlPoints()
        {
            var state = CreateState(DeformationDataVersion.V0_0_1);
            typeof(LatticeAsset).GetField("_applySpace", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(state.Settings, 1);
            var points = state.Settings.ControlPointsLocal.ToArray();
            state.OwnerWorldToLocal = null;
            var runner = new DeformationMigrationRunner(state);
            Assert.That(runner.TryAdvanceOneRelease(), Is.False);
            Assert.That(state.Status, Is.EqualTo(DeformationDataMigrationStatus.PendingOwnerTransform));
            Assert.That(state.Version, Is.EqualTo(DeformationDataVersion.V0_0_1));
            state.OwnerWorldToLocal = Matrix4x4.zero;
            Assert.That(runner.TryAdvanceOneRelease(), Is.False);
            Assert.That(state.Status, Is.EqualTo(DeformationDataMigrationStatus.PendingOwnerTransform));
            state.OwnerWorldToLocal = Matrix4x4.Translate(Vector3.one);
            Assert.That(runner.TryAdvanceOneRelease(), Is.True);
            Assert.That(state.Settings.HasPendingLegacyWorldSpace, Is.True);
            Assert.That(state.Settings.ControlPointsLocal.ToArray(), Is.EqualTo(points));
        }

        [Test]
        public void FutureAndCorruptPayloads_AreRejectedBeforeCommitCheckpoint()
        {
            var state = CreateState(DeformationDataVersion.V1_2_1);
            int commits = 0;
            var runner = new DeformationMigrationRunner(state, _ => commits++);
            state.Version = (DeformationDataVersion)999;
            string before = Payload(state);
            Assert.That(runner.TryAdvanceOneRelease(), Is.False);
            Assert.That(state.Status, Is.EqualTo(DeformationDataMigrationStatus.UnsupportedFutureVersion));
            Assert.That(Payload(state), Is.EqualTo(before));
            state.Version = DeformationDataVersion.V1_2_1;
            state.Groups[0].SerializedLayers[0].BrushDisplacements = new Vector3[3];
            before = Payload(state);
            Assert.That(runner.TryAdvanceOneRelease(), Is.False);
            Assert.That(state.Status, Is.EqualTo(DeformationDataMigrationStatus.InvalidData));
            Assert.That(Payload(state), Is.EqualTo(before));
            Assert.That(commits, Is.Zero);
        }

        [TestCase(DeformationDataVersion.V1_2_0, false)]
        [TestCase(DeformationDataVersion.V1_2_1, false)]
        [TestCase(DeformationDataVersion.V1_4_0, false)]
        [TestCase(DeformationDataVersion.V1_2_0, true)]
        [TestCase(DeformationDataVersion.V1_2_1, true)]
        [TestCase(DeformationDataVersion.V1_4_0, true)]
        public void OwnerRecordFailure_RestoresComponentPayloadAndAllowsRetry(DeformationDataVersion version, bool prefab)
        {
            var mesh = DeformationOutputBaselineFixture.CreateMesh(2);
            var root = new GameObject("Migration owner rollback");
            var previousRecorder = DeformerPlatformServices.RecordLegacyMigration;
            string folder = null;
            try
            {
                root.SetActive(false);
                var filter = root.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                root.AddComponent<MeshRenderer>();
                var target = root.AddComponent<LatticeDeformer>();
                target.Reset();
                if (prefab)
                {
                    folder = "Assets/__MigrationOwner_" + Guid.NewGuid().ToString("N");
                    AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
                    AssetDatabase.CreateAsset(mesh, folder + "/source.asset");
                    var asset = PrefabUtility.SaveAsPrefabAsset(root, folder + "/base.prefab");
                    UnityEngine.Object.DestroyImmediate(root);
                    root = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                    target = root.GetComponent<LatticeDeformer>();
                    filter = root.GetComponent<MeshFilter>();
                    root.transform.localPosition = new Vector3(0.21f, 0.37f, 0.42f);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(root.transform);
                }
                var state = CreateState(version);
                var rawFields = new Dictionary<string, object>
                {
                    ["_settings"] = state.Settings, ["_layers"] = state.FlatLayers, ["_groups"] = state.Groups,
                    ["_activeLayerIndex"] = state.ActiveLayerIndex, ["_activeGroupIndex"] = state.ActiveGroupIndex,
                    ["_layerModelVersion"] = state.LayerModelVersion, ["_deformationDataVersion"] = state.Version,
                    ["_deformationDataSourceVersion"] = state.SourceVersion, ["_blendShapeOutput"] = state.BlendShapeOutput,
                    ["_blendShapeName"] = state.BlendShapeName, ["_blendShapeCurve"] = state.BlendShapeCurve,
                    ["_legacyAbsoluteLatticeEvaluation"] = false, ["_legacyPublishedBlendShapeSemantics"] = false,
                    ["_hasInitializedFromSource"] = true, ["_serializedSourceMesh"] = mesh
                };
                foreach (var field in rawFields)
                    typeof(LatticeDeformer).GetField(field.Key, BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(target, field.Value);
                if (prefab) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                string before = EditorJsonUtility.ToJson(target);
                var modificationsBefore = prefab ? CapturePrefabModifications(root) : null;
                var source = mesh.vertices;
                int recordedVersion = -1;
                DeformerPlatformServices.RecordLegacyMigration = value =>
                {
                    recordedVersion = (int)((LatticeDeformer)value).SerializedDeformationDataVersion;
                    previousRecorder?.Invoke(value);
                    throw new InvalidOperationException("Injected owner record failure");
                };
                Assert.That(target.TryUpgradeDeformationDataOneRelease(), Is.False);
                Assert.That(recordedVersion, Is.EqualTo((int)version + 1));
                Assert.That(target.MigrationStatus, Is.EqualTo(DeformationDataMigrationStatus.InvalidData));
                Assert.That(EditorJsonUtility.ToJson(target), Is.EqualTo(before));
                if (prefab) Assert.That(CapturePrefabModifications(root), Is.EquivalentTo(modificationsBefore));
                Assert.That(filter.sharedMesh, Is.SameAs(mesh));
                Assert.That(mesh.vertices, Is.EqualTo(source));
                DeformerPlatformServices.RecordLegacyMigration = previousRecorder;
                Assert.That(target.TryUpgradeDeformationDataOneRelease(), Is.True);
                Assert.That((int)target.SerializedDeformationDataVersion, Is.EqualTo((int)version + 1));
            }
            finally
            {
                DeformerPlatformServices.RecordLegacyMigration = previousRecorder;
                UnityEngine.Object.DestroyImmediate(root);
                if (folder != null) AssetDatabase.DeleteAsset(folder);
                else UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static List<string> CapturePrefabModifications(GameObject root)
        {
            var result = new List<string>();
            foreach (var modification in PrefabUtility.GetPropertyModifications(root) ?? Array.Empty<PropertyModification>())
                result.Add((modification.target != null ? modification.target.GetInstanceID() : 0) + "|" +
                    modification.propertyPath + "|" + modification.value + "|" +
                    (modification.objectReference != null ? modification.objectReference.GetInstanceID() : 0));
            return result;
        }

        private static DeformationMigrationState CreateState(DeformationDataVersion version)
        {
            var settings = new LatticeAsset { Interpolation = LatticeInterpolationMode.CubicBernstein };
            settings.EnsureInitialized();
            settings.SetControlPointLocal(0, settings.GetControlPointLocal(0) + Vector3.up * 0.17f);
            var state = new DeformationMigrationState
            {
                Settings = settings, FlatLayers = new List<LatticeLayer>(), Groups = new List<DeformerGroup>(),
                Version = version, SourceVersion = version, LayerModelVersion = 0,
                SourceVertexCount = 4, HasInitializedFromSource = true, HasSerializedSource = true,
                BlendShapeName = "Legacy", BlendShapeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f)
            };
            if (version >= DeformationDataVersion.V1_2_1)
            {
                state.LayerModelVersion = 2;
                for (int g = 0; g < 2; g++)
                {
                    var group = new DeformerGroup
                    {
                        Name = "Published " + g, BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape,
                        BlendShapeName = "Output " + g
                    };
                    // Shared nested assets exercise snapshot deduplication.
                    group.LayersList.Add(new LatticeLayer { Settings = settings });
                    group.SetSerializedActiveLayerIndex(1);
                    state.Groups.Add(group);
                }
                state.ActiveGroupIndex = 1;
                if (version == DeformationDataVersion.V1_2_1)
                    state.FlatLayers.Add(new LatticeLayer { Name = "Stale flat", Settings = settings });
            }
            return state;
        }

        [Serializable]
        private sealed class RawPayload
        {
            public LatticeAsset settings;
            public List<LatticeLayer> flat;
            public List<DeformerGroup> groups;
            public int activeLayer, activeGroup, layerModelVersion, version, sourceVersion;
            public bool absoluteEvaluation, publishedBlendShapes;
            public BlendShapeOutputMode output;
            public string name;
            public AnimationCurve curve;
        }

        private static string Payload(DeformationMigrationState state) => JsonUtility.ToJson(new RawPayload
        {
            settings = state.Settings, flat = state.FlatLayers, groups = state.Groups,
            activeLayer = state.ActiveLayerIndex, activeGroup = state.ActiveGroupIndex,
            layerModelVersion = state.LayerModelVersion, version = (int)state.Version, sourceVersion = (int)state.SourceVersion,
            absoluteEvaluation = state.LegacyAbsoluteEvaluation, publishedBlendShapes = state.LegacyPublishedBlendShapeSemantics,
            output = state.BlendShapeOutput, name = state.BlendShapeName, curve = state.BlendShapeCurve
        });
    }
}
#endif
