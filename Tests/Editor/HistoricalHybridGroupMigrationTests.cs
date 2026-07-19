#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class HistoricalHybridGroupMigrationTests
    {
        private const string RecoveryGroupName = "Recovered Legacy Flat Layers";
        private const float Epsilon = 1e-6f;

        [Test]
        public void PublishedHybrid_ClassifiesAsV121_ThenRecoversFlatPayloadWithoutChangingOutput()
        {
            using var fixture = CreateHybridFixture();
            var originalGroups = GetField<List<DeformerGroup>>(fixture.Deformer, "_groups");
            var originalFlatLayers = GetField<List<LatticeLayer>>(fixture.Deformer, "_layers");
            string beforeClassification = CaptureStructure(fixture.Deformer);

            Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.True);
            Assert.That(fixture.Deformer.SerializedDeformationDataVersion,
                Is.EqualTo(DeformationDataVersion.V1_2_1));
            Assert.That(fixture.Deformer.SourceDeformationDataVersion,
                Is.EqualTo(DeformationDataVersion.V1_2_1));
            Assert.That(CaptureStructure(fixture.Deformer), Is.EqualTo(beforeClassification),
                "Shape classification must not mutate the historical payload.");
            Assert.That(GetField<List<DeformerGroup>>(fixture.Deformer, "_groups"),
                Is.SameAs(originalGroups));
            Assert.That(GetField<List<LatticeLayer>>(fixture.Deformer, "_layers"),
                Is.SameAs(originalFlatLayers));

            Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.True);
            Assert.That(fixture.Deformer.SerializedDeformationDataVersion,
                Is.EqualTo(DeformationDataVersion.V1_3_0));
            Assert.That(GetField<int>(fixture.Deformer, "_layerModelVersion"), Is.EqualTo(3));
            Assert.That(GetField<int>(fixture.Deformer, "_activeGroupIndex"), Is.EqualTo(1));
            Assert.That(GetField<bool>(fixture.Deformer, "_legacyPublishedBlendShapeSemantics"), Is.True);

            var migratedGroups = GetField<List<DeformerGroup>>(fixture.Deformer, "_groups");
            var migratedFlatLayers = GetField<List<LatticeLayer>>(fixture.Deformer, "_layers");
            Assert.That(migratedGroups, Is.Not.SameAs(originalGroups),
                "The containing group list must migrate copy-on-write.");
            Assert.That(migratedFlatLayers, Is.Not.SameAs(originalFlatLayers));
            Assert.That(migratedFlatLayers, Is.Empty);
            Assert.That(migratedGroups.Count, Is.EqualTo(3));
            Assert.That(migratedGroups[0], Is.SameAs(originalGroups[0]));
            Assert.That(migratedGroups[1], Is.SameAs(originalGroups[1]));

            DeformerGroup recovery = migratedGroups[2];
            Assert.That(recovery.Name, Is.EqualTo(RecoveryGroupName));
            Assert.That(recovery.Enabled, Is.False);
            Assert.That(recovery.ActiveLayerIndex, Is.EqualTo(0));
            Assert.That(recovery.BlendShapeOutput, Is.EqualTo(BlendShapeOutputMode.OutputAsBlendShape));
            Assert.That(recovery.BlendShapeName, Is.EqualTo("Legacy Flat Backup Shape"));
            AssertCurvesEqual(fixture.LegacyFlatCurve, recovery.BlendShapeCurve);
            Assert.That(recovery.Layers.Count, Is.EqualTo(1));
            Assert.That(recovery.Layers[0], Is.SameAs(fixture.FlatLayer));
            Assert.That(originalFlatLayers.Count, Is.EqualTo(1),
                "The original rollback snapshot must not be edited in place.");

            string normalizedStructure = CaptureStructure(fixture.Deformer);
            Mesh output = fixture.Deformer.Deform(false);
            Assert.That(output, Is.Not.Null);
            AssertVectorsEqual(Add(fixture.Source.vertices, fixture.DirectDeltas), output.vertices);
            Assert.That(output.blendShapeCount, Is.EqualTo(1),
                "The disabled recovery group must not add its legacy BlendShape.");
            Assert.That(output.GetBlendShapeName(0), Is.EqualTo("Authoritative Hybrid Shape"));
            Assert.That(output.GetBlendShapeFrameCount(0), Is.EqualTo(100));

            var deltaVertices = new Vector3[output.vertexCount];
            var deltaNormals = new Vector3[output.vertexCount];
            var deltaTangents = new Vector3[output.vertexCount];
            output.GetBlendShapeFrameVertices(
                0,
                output.GetBlendShapeFrameCount(0) - 1,
                deltaVertices,
                deltaNormals,
                deltaTangents);
            AssertVectorsEqual(fixture.ShapeDeltas, deltaVertices);
            AssertVectorsEqual(new Vector3[output.vertexCount], deltaNormals);
            AssertVectorsEqual(new Vector3[output.vertexCount], deltaTangents);

            Assert.That(fixture.Deformer.SerializedDeformationDataVersion,
                Is.EqualTo(DeformationDataVersion.CurrentDevelopment));
            Assert.That(fixture.Deformer.SourceDeformationDataVersion,
                Is.EqualTo(DeformationDataVersion.V1_2_1));
            Assert.That(CaptureStructure(fixture.Deformer), Is.EqualTo(normalizedStructure));

            _ = fixture.Deformer.Groups;
            _ = fixture.Deformer.Groups;
            Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.False);
            Assert.That(GetField<List<DeformerGroup>>(fixture.Deformer, "_groups")
                    .FindAll(group => group != null && group.Name == RecoveryGroupName).Count,
                Is.EqualTo(1));
            Assert.That(CaptureStructure(fixture.Deformer), Is.EqualTo(normalizedStructure),
                "Repeated migration/facade access must be idempotent.");
            AssertVectorsEqual(output.vertices, fixture.Deformer.Deform(false).vertices);
        }

        [Test]
        public void V121Hybrid_InvalidActiveGroup_RollsBackEveryStructuralMutation()
        {
            using var fixture = CreateHybridFixture();
            SetField(fixture.Deformer, "_deformationDataVersion", DeformationDataVersion.V1_2_1);
            SetField(fixture.Deformer, "_deformationDataSourceVersion", DeformationDataVersion.V1_2_1);
            SetField(fixture.Deformer, "_activeGroupIndex", 99);

            var originalGroups = GetField<List<DeformerGroup>>(fixture.Deformer, "_groups");
            var originalLayers = GetField<List<LatticeLayer>>(fixture.Deformer, "_layers");
            string before = CaptureStructure(fixture.Deformer);

            Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.False);
            Assert.That(fixture.Deformer.SerializedDeformationDataVersion,
                Is.EqualTo(DeformationDataVersion.V1_2_1));
            Assert.That(fixture.Deformer.SourceDeformationDataVersion,
                Is.EqualTo(DeformationDataVersion.V1_2_1));
            Assert.That(fixture.Deformer.MigrationStatus,
                Is.EqualTo(DeformationDataMigrationStatus.InvalidData));
            Assert.That(GetField<List<DeformerGroup>>(fixture.Deformer, "_groups"),
                Is.SameAs(originalGroups));
            Assert.That(GetField<List<LatticeLayer>>(fixture.Deformer, "_layers"),
                Is.SameAs(originalLayers));
            Assert.That(GetField<int>(fixture.Deformer, "_layerModelVersion"), Is.EqualTo(2));
            Assert.That(GetField<int>(fixture.Deformer, "_activeGroupIndex"), Is.EqualTo(99));
            Assert.That(GetField<bool>(fixture.Deformer, "_legacyPublishedBlendShapeSemantics"), Is.False);
            Assert.That(CaptureStructure(fixture.Deformer), Is.EqualTo(before));

            Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.False);
            Assert.That(CaptureStructure(fixture.Deformer), Is.EqualTo(before),
                "A repeated failed attempt must remain non-destructive.");
        }

        [Test]
        public void PublishedHybrid_WithoutEnabledOutputMetadata_KeepsCurrentSemanticsAvailable()
        {
            using var fixture = CreateHybridFixture();
            var groups = GetField<List<DeformerGroup>>(fixture.Deformer, "_groups");
            foreach (var group in groups)
            {
                group.BlendShapeOutput = BlendShapeOutputMode.Disabled;
                foreach (var layer in group.LayersList)
                {
                    layer.BlendShapeOutput = BlendShapeOutputMode.Disabled;
                }
            }
            fixture.FlatLayer.BlendShapeOutput = BlendShapeOutputMode.Disabled;

            Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.True);
            Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.True);
            Assert.That(fixture.Deformer.Deform(false), Is.Not.Null);
            Assert.That(GetField<bool>(fixture.Deformer, "_legacyPublishedBlendShapeSemantics"), Is.False,
                "Disabled historical metadata must not lock out future current behavior.");

            LatticeLayer newlyEnabled = fixture.Deformer.Groups[0].LayersList[0];
            newlyEnabled.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
            newlyEnabled.BlendShapeName = "New Current Layer Shape";
            fixture.Deformer.InvalidateCache();
            Mesh currentOutput = fixture.Deformer.Deform(false);
            Assert.That(currentOutput.GetBlendShapeIndex("New Current Layer Shape"),
                Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void PublishedHybrid_OnlyStaleFlatLayerHasOutputMetadata_DoesNotChangeAuthoritativeSemantics()
        {
            using var fixture = CreateHybridFixture();
            var groups = GetField<List<DeformerGroup>>(fixture.Deformer, "_groups");
            foreach (var group in groups)
            {
                group.BlendShapeOutput = BlendShapeOutputMode.Disabled;
                foreach (var layer in group.LayersList)
                {
                    layer.BlendShapeOutput = BlendShapeOutputMode.Disabled;
                }
            }

            fixture.FlatLayer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
            fixture.FlatLayer.BlendShapeName = "Ignored Stale Flat Layer Shape";

            Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.True);
            Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.True);
            Assert.That(fixture.Deformer.Deform(false), Is.Not.Null,
                "Completing the remaining no-op release boundaries must succeed.");
            Assert.That(GetField<bool>(fixture.Deformer, "_legacyPublishedBlendShapeSemantics"), Is.False,
                "Metadata from the published runtime's ignored flat facade must not alter authoritative groups.");

            var migratedGroups = GetField<List<DeformerGroup>>(fixture.Deformer, "_groups");
            var recovery = migratedGroups.Single(group => group != null && group.Name == RecoveryGroupName);
            Assert.That(recovery.Enabled, Is.False);
            Assert.That(recovery.Layers[0].BlendShapeOutput,
                Is.EqualTo(BlendShapeOutputMode.OutputAsBlendShape),
                "The ignored metadata must remain inspectable in the disabled recovery group.");

            LatticeLayer currentLayer = migratedGroups[0].LayersList[0];
            currentLayer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
            currentLayer.BlendShapeName = "Current Authoritative Layer Shape";
            fixture.Deformer.InvalidateCache();
            Mesh output = fixture.Deformer.Deform(false);
            Assert.That(output, Is.Not.Null);
            Assert.That(output.GetBlendShapeIndex("Ignored Stale Flat Layer Shape"), Is.EqualTo(-1));
            Assert.That(output.GetBlendShapeIndex("Current Authoritative Layer Shape"),
                Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void PublishedHybrid_OnlyDisabledGroupHasOutputMetadata_DoesNotLockEnabledGroups()
        {
            using var fixture = CreateHybridFixture();
            var groups = GetField<List<DeformerGroup>>(fixture.Deformer, "_groups");

            DeformerGroup enabledGroup = groups[0];
            enabledGroup.BlendShapeOutput = BlendShapeOutputMode.Disabled;
            foreach (var layer in enabledGroup.LayersList)
            {
                layer.BlendShapeOutput = BlendShapeOutputMode.Disabled;
            }

            DeformerGroup disabledGroup = groups[1];
            disabledGroup.Enabled = false;
            Assert.That(disabledGroup.BlendShapeOutput,
                Is.EqualTo(BlendShapeOutputMode.OutputAsBlendShape));
            Assert.That(disabledGroup.LayersList[0].BlendShapeOutput,
                Is.EqualTo(BlendShapeOutputMode.OutputAsBlendShape));
            fixture.FlatLayer.BlendShapeOutput = BlendShapeOutputMode.Disabled;

            Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.True);
            Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.True);
            Assert.That(fixture.Deformer.Deform(false), Is.Not.Null,
                "Completing the remaining no-op release boundaries must succeed.");
            Assert.That(GetField<bool>(fixture.Deformer, "_legacyPublishedBlendShapeSemantics"), Is.False,
                "A group skipped by the published evaluator must not lock enabled groups into legacy mode.");

            var migratedGroups = GetField<List<DeformerGroup>>(fixture.Deformer, "_groups");
            Assert.That(migratedGroups[1].Enabled, Is.False);
            Assert.That(migratedGroups[1].BlendShapeOutput,
                Is.EqualTo(BlendShapeOutputMode.OutputAsBlendShape),
                "Disabled historical metadata must remain inspectable.");

            LatticeLayer currentLayer = migratedGroups[0].LayersList[0];
            currentLayer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
            currentLayer.BlendShapeName = "Enabled Group Current Shape";
            fixture.Deformer.InvalidateCache();
            Mesh output = fixture.Deformer.Deform(false);
            Assert.That(output, Is.Not.Null);
            Assert.That(output.GetBlendShapeIndex("Authoritative Hybrid Shape"), Is.EqualTo(-1));
            Assert.That(output.GetBlendShapeIndex("Historically Ignored Layer Shape"), Is.EqualTo(-1));
            Assert.That(output.GetBlendShapeIndex("Enabled Group Current Shape"),
                Is.GreaterThanOrEqualTo(0));
        }

        [TestCase(false, 1f, TestName = "PublishedHybrid_DisabledLayerOutputMetadata_DoesNotLockEnabledLayers")]
        [TestCase(true, 0f, TestName = "PublishedHybrid_ZeroWeightLayerOutputMetadata_DoesNotLockEnabledLayers")]
        public void PublishedHybrid_OnlySkippedLayerHasOutputMetadata_DoesNotLockEnabledLayers(
            bool layerEnabled,
            float layerWeight)
        {
            using var fixture = CreateHybridFixture();
            var groups = GetField<List<DeformerGroup>>(fixture.Deformer, "_groups");
            foreach (var group in groups)
            {
                group.BlendShapeOutput = BlendShapeOutputMode.Disabled;
                foreach (var layer in group.LayersList)
                {
                    layer.BlendShapeOutput = BlendShapeOutputMode.Disabled;
                }
            }

            LatticeLayer skippedLayer = groups[1].LayersList[0];
            skippedLayer.Enabled = layerEnabled;
            skippedLayer.Weight = layerWeight;
            skippedLayer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
            skippedLayer.BlendShapeName = "Skipped Historical Layer Shape";
            fixture.FlatLayer.BlendShapeOutput = BlendShapeOutputMode.Disabled;

            Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.True);
            Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.True);
            Assert.That(fixture.Deformer.Deform(false), Is.Not.Null,
                "Completing the remaining no-op release boundaries must succeed.");
            Assert.That(GetField<bool>(fixture.Deformer, "_legacyPublishedBlendShapeSemantics"), Is.False,
                "Layer metadata skipped by the published evaluator must not enable component-wide legacy mode.");

            var migratedGroups = GetField<List<DeformerGroup>>(fixture.Deformer, "_groups");
            LatticeLayer migratedSkippedLayer = migratedGroups[1].LayersList[0];
            Assert.That(migratedSkippedLayer.Enabled, Is.EqualTo(layerEnabled));
            Assert.That(migratedSkippedLayer.Weight, Is.EqualTo(layerWeight));
            Assert.That(migratedSkippedLayer.BlendShapeOutput,
                Is.EqualTo(BlendShapeOutputMode.OutputAsBlendShape),
                "Skipped historical metadata must remain inspectable.");

            LatticeLayer currentLayer = migratedGroups[0].LayersList[0];
            currentLayer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
            currentLayer.BlendShapeName = "Enabled Layer Current Shape";
            fixture.Deformer.InvalidateCache();
            Mesh output = fixture.Deformer.Deform(false);
            Assert.That(output, Is.Not.Null);
            Assert.That(output.GetBlendShapeIndex("Skipped Historical Layer Shape"), Is.EqualTo(-1));
            Assert.That(output.GetBlendShapeIndex("Enabled Layer Current Shape"),
                Is.GreaterThanOrEqualTo(0));
        }

        private static HybridFixture CreateHybridFixture()
        {
            var source = new Mesh { name = "Historical Hybrid Source" };
            source.vertices = new[]
            {
                new Vector3(-0.8f, -0.5f, -0.2f),
                new Vector3(0.7f, -0.35f, 0.1f),
                new Vector3(0.45f, 0.75f, -0.15f),
                new Vector3(-0.55f, 0.4f, 0.5f),
            };
            source.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            source.RecalculateNormals();
            source.RecalculateBounds();

            var root = new GameObject("Historical Hybrid Fixture");
            root.SetActive(false);
            var filter = root.AddComponent<MeshFilter>();
            root.AddComponent<MeshRenderer>();
            filter.sharedMesh = source;
            var deformer = root.AddComponent<LatticeDeformer>();

            Vector3[] directDeltas =
            {
                new Vector3(0.12f, -0.03f, 0.02f),
                new Vector3(-0.04f, 0.09f, 0.01f),
                new Vector3(0.03f, 0.02f, -0.08f),
                new Vector3(-0.06f, -0.01f, 0.07f),
            };
            Vector3[] shapeDeltas =
            {
                new Vector3(-0.02f, 0.05f, 0.03f),
                new Vector3(0.08f, -0.02f, 0.04f),
                new Vector3(-0.03f, 0.07f, -0.01f),
                new Vector3(0.05f, -0.04f, 0.02f),
            };
            Vector3[] staleDeltas =
            {
                Vector3.one * 3f,
                Vector3.one * -4f,
                new Vector3(5f, -6f, 7f),
                new Vector3(-8f, 9f, -10f),
            };

            var directGroup = new DeformerGroup { Name = "Authoritative Direct", Enabled = true };
            directGroup.LayersList.Add(CreateBrushLayer("Direct", directDeltas));

            var shapeGroup = new DeformerGroup
            {
                Name = "Authoritative Shape Group",
                Enabled = true,
                BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape,
                BlendShapeName = "Authoritative Hybrid Shape",
                BlendShapeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
            };
            LatticeLayer historicalShapeLayer = CreateBrushLayer("Shape", shapeDeltas);
            historicalShapeLayer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
            historicalShapeLayer.BlendShapeName = "Historically Ignored Layer Shape";
            shapeGroup.LayersList.Add(historicalShapeLayer);

            LatticeLayer flatLayer = CreateBrushLayer("Stale Flat Backup", staleDeltas);
            AnimationCurve legacyCurve = CreateLegacyCurve();
            var settings = new LatticeAsset
            {
                GridSize = new Vector3Int(2, 2, 2),
                LocalBounds = source.bounds,
                Interpolation = LatticeInterpolationMode.Trilinear,
            };
            settings.ResetControlPoints();

            SetField(deformer, "_settings", settings);
            SetField(deformer, "_layers", new List<LatticeLayer> { flatLayer });
            SetField(deformer, "_groups", new List<DeformerGroup> { directGroup, shapeGroup });
            SetField(deformer, "_activeLayerIndex", 0);
            SetField(deformer, "_activeGroupIndex", 1);
            SetField(deformer, "_layerModelVersion", 2);
            SetField(deformer, "_blendShapeOutput", BlendShapeOutputMode.OutputAsBlendShape);
            SetField(deformer, "_blendShapeName", "Legacy Flat Backup Shape");
            SetField(deformer, "_blendShapeCurve", legacyCurve);
            SetField(deformer, "_deformationDataVersion", DeformationDataVersion.Unversioned);
            SetField(deformer, "_deformationDataSourceVersion", DeformationDataVersion.Unversioned);
            SetField(deformer, "_legacyAbsoluteLatticeEvaluation", false);
            SetField(deformer, "_serializedSourceMesh", source);
            SetField(deformer, "_sourceMesh", source);
            SetField(deformer, "_meshFilter", filter);
            SetField(deformer, "_hasInitializedFromSource", true);
            SetField(deformer, "_hasIncompatibleBrushData", false);
            deformer.InvalidateCache();

            return new HybridFixture(
                root,
                deformer,
                source,
                flatLayer,
                directDeltas,
                shapeDeltas,
                legacyCurve);
        }

        private static LatticeLayer CreateBrushLayer(string name, Vector3[] displacements)
        {
            var layer = new LatticeLayer
            {
                Name = name,
                Enabled = true,
                Weight = 1f,
                BrushDisplacements = (Vector3[])displacements.Clone(),
                VertexMask = new[] { 1f, 1f, 1f, 1f },
            };
            layer.SetType(MeshDeformerLayerType.Brush);
            _ = layer.Settings;
            return layer;
        }

        private static AnimationCurve CreateLegacyCurve()
        {
            var middle = new Keyframe(0.45f, 0.3f)
            {
                inTangent = 0.4f,
                outTangent = 1.2f,
                inWeight = 0.2f,
                outWeight = 0.35f,
                weightedMode = WeightedMode.Both,
            };
            return new AnimationCurve(new Keyframe(0f, 0f), middle, new Keyframe(1f, 1f))
            {
                preWrapMode = WrapMode.PingPong,
                postWrapMode = WrapMode.Loop,
            };
        }

        private static Vector3[] Add(Vector3[] vertices, Vector3[] deltas)
        {
            var result = new Vector3[vertices.Length];
            for (int i = 0; i < result.Length; i++) result[i] = vertices[i] + deltas[i];
            return result;
        }

        private static void AssertVectorsEqual(Vector3[] expected, Vector3[] actual)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That((actual[i] - expected[i]).sqrMagnitude, Is.LessThanOrEqualTo(Epsilon * Epsilon),
                    $"vertex {i}: expected {expected[i]}, actual {actual[i]}");
            }
        }

        private static void AssertCurvesEqual(AnimationCurve expected, AnimationCurve actual)
        {
            Assert.That(actual.preWrapMode, Is.EqualTo(expected.preWrapMode));
            Assert.That(actual.postWrapMode, Is.EqualTo(expected.postWrapMode));
            Assert.That(actual.keys.Length, Is.EqualTo(expected.keys.Length));
            for (int i = 0; i < expected.keys.Length; i++)
            {
                Assert.That(actual.keys[i].time, Is.EqualTo(expected.keys[i].time));
                Assert.That(actual.keys[i].value, Is.EqualTo(expected.keys[i].value));
                Assert.That(actual.keys[i].inTangent, Is.EqualTo(expected.keys[i].inTangent));
                Assert.That(actual.keys[i].outTangent, Is.EqualTo(expected.keys[i].outTangent));
                Assert.That(actual.keys[i].inWeight, Is.EqualTo(expected.keys[i].inWeight));
                Assert.That(actual.keys[i].outWeight, Is.EqualTo(expected.keys[i].outWeight));
                Assert.That(actual.keys[i].weightedMode, Is.EqualTo(expected.keys[i].weightedMode));
            }
        }

        private static string CaptureStructure(LatticeDeformer deformer)
        {
            return JsonUtility.ToJson(new StructuralPayload
            {
                layerModelVersion = GetField<int>(deformer, "_layerModelVersion"),
                flatLayers = GetField<List<LatticeLayer>>(deformer, "_layers"),
                activeFlatLayer = GetField<int>(deformer, "_activeLayerIndex"),
                legacyBlendShapeOutput = GetField<BlendShapeOutputMode>(deformer, "_blendShapeOutput"),
                legacyBlendShapeName = GetField<string>(deformer, "_blendShapeName"),
                legacyBlendShapeCurve = GetField<AnimationCurve>(deformer, "_blendShapeCurve"),
                legacyPublishedBlendShapeSemantics = GetField<bool>(
                    deformer,
                    "_legacyPublishedBlendShapeSemantics"),
                groups = GetField<List<DeformerGroup>>(deformer, "_groups"),
                activeGroup = GetField<int>(deformer, "_activeGroupIndex"),
            });
        }

        private static T GetField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, $"Field not found: {target.GetType().Name}.{name}");
            return (T)field.GetValue(target);
        }

        private static void SetField<T>(object target, string name, T value)
        {
            FieldInfo field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, $"Field not found: {target.GetType().Name}.{name}");
            field.SetValue(target, value);
        }

        [Serializable]
        private sealed class StructuralPayload
        {
            public int layerModelVersion;
            public List<LatticeLayer> flatLayers;
            public int activeFlatLayer;
            public BlendShapeOutputMode legacyBlendShapeOutput;
            public string legacyBlendShapeName;
            public AnimationCurve legacyBlendShapeCurve;
            public bool legacyPublishedBlendShapeSemantics;
            public List<DeformerGroup> groups;
            public int activeGroup;
        }

        private sealed class HybridFixture : IDisposable
        {
            public GameObject Root { get; }
            public LatticeDeformer Deformer { get; }
            public Mesh Source { get; }
            public LatticeLayer FlatLayer { get; }
            public Vector3[] DirectDeltas { get; }
            public Vector3[] ShapeDeltas { get; }
            public AnimationCurve LegacyFlatCurve { get; }

            public HybridFixture(
                GameObject root,
                LatticeDeformer deformer,
                Mesh source,
                LatticeLayer flatLayer,
                Vector3[] directDeltas,
                Vector3[] shapeDeltas,
                AnimationCurve legacyFlatCurve)
            {
                Root = root;
                Deformer = deformer;
                Source = source;
                FlatLayer = flatLayer;
                DirectDeltas = directDeltas;
                ShapeDeltas = shapeDeltas;
                LegacyFlatCurve = legacyFlatCurve;
            }

            public void Dispose()
            {
                if (Deformer != null) Deformer.RestoreOriginalMesh();
                if (Root != null) Object.DestroyImmediate(Root);
                if (Source != null) Object.DestroyImmediate(Source);
            }
        }
    }
}
#endif
