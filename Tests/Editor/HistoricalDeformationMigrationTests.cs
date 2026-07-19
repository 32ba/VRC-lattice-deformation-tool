#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    /// <summary>
    /// Compatibility contract for deformation data written by published package releases.
    ///
    /// These tests intentionally live apart from the implementation-oriented migration tests.
    /// They fabricate only fields that existed in the named historical schema, advance the
    /// release manifest one entry at a time, and compare the final result with the historical
    /// evaluator instead of calculating expectations through current production code.
    /// </summary>
    public sealed class HistoricalDeformationMigrationTests
    {
        private const float Epsilon = 2e-5f;

        private static readonly DeformationDataVersion[] s_publishedReleases =
        {
            DeformationDataVersion.V0_0_1,
            DeformationDataVersion.V0_0_2,
            DeformationDataVersion.V0_0_3,
            DeformationDataVersion.V0_0_4,
            DeformationDataVersion.V0_0_5,
            DeformationDataVersion.V0_0_6,
            DeformationDataVersion.V1_0_0,
            DeformationDataVersion.V1_0_1,
            DeformationDataVersion.V1_1_0,
            DeformationDataVersion.V1_2_0,
            DeformationDataVersion.V1_2_1,
            DeformationDataVersion.V1_3_0,
            DeformationDataVersion.V1_3_1,
            DeformationDataVersion.V1_4_0,
            DeformationDataVersion.CurrentDevelopment,
        };

        [Test]
        public void ReleaseManifest_EnumeratesEveryPublishedTagOnceInUpgradeOrder()
        {
            var allValues = Enum.GetValues(typeof(DeformationDataVersion))
                .Cast<DeformationDataVersion>()
                .ToArray();

            var expected = new[] { DeformationDataVersion.Unversioned }
                .Concat(s_publishedReleases)
                .ToArray();

            CollectionAssert.AreEqual(expected, allValues);
            Assert.That(allValues.Distinct().Count(), Is.EqualTo(allValues.Length));
            Assert.That(s_publishedReleases.Select(ReleaseLabel).ToArray(), Is.EqualTo(new[]
            {
                "0.0.1", "0.0.2", "0.0.3", "0.0.4", "0.0.5", "0.0.6",
                "1.0.0", "1.0.1", "1.1.0", "1.2.0", "1.2.1", "1.3.0",
                "1.3.1", "1.4.0", "Current",
            }));
        }

        private static IEnumerable<TestCaseData> PublishedReleaseCases()
        {
            foreach (var release in s_publishedReleases)
            {
                yield return new TestCaseData(release)
                    .SetName($"EveryRelease_{ReleaseLabel(release).Replace('.', '_')}_DirectAndStepwiseReachCurrent");
            }
        }

        [TestCaseSource(nameof(PublishedReleaseCases))]
        public void EveryRelease_DirectAndStepwiseUpgrade_AdvanceWithoutSkippingAndConverge(
            DeformationDataVersion startRelease)
        {
            var source = CreateCompatibilityMesh("EveryRelease_" + ReleaseLabel(startRelease));
            HistoricalFixture stepwise = null;
            HistoricalFixture direct = null;
            try
            {
                stepwise = CreateFixture("Stepwise_" + ReleaseLabel(startRelease), source, startRelease);
                direct = CreateFixture("Direct_" + ReleaseLabel(startRelease), source, startRelease);
                ConfigureReleaseShape(stepwise.Deformer, startRelease, 0.11f);
                ConfigureReleaseShape(direct.Deformer, startRelease, 0.11f);

                int index = Array.IndexOf(s_publishedReleases, startRelease);
                Assert.That(index, Is.GreaterThanOrEqualTo(0));
                for (int next = index + 1; next < s_publishedReleases.Length; next++)
                {
                    var before = stepwise.Deformer.SerializedDeformationDataVersion;
                    Assert.That(stepwise.Deformer.TryUpgradeDeformationDataOneRelease(), Is.True);
                    var after = stepwise.Deformer.SerializedDeformationDataVersion;
                    Assert.That(after, Is.EqualTo(s_publishedReleases[next]));
                    Assert.That((int)after, Is.EqualTo((int)before + 1),
                        $"The {ReleaseLabel(before)} boundary skipped a manifest entry.");
                }

                Assert.That(stepwise.Deformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.CurrentDevelopment));

                // A normal public facade access performs the same release loop in one call.
                _ = direct.Deformer.Groups;
                Assert.That(direct.Deformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.CurrentDevelopment));
                _ = stepwise.Deformer.Groups;

                Assert.That(CaptureSemanticJson(direct.Deformer),
                    Is.EqualTo(CaptureSemanticJson(stepwise.Deformer)));
                AssertVectorsEqual(
                    direct.Deformer.Deform(false).vertices,
                    stepwise.Deformer.Deform(false).vertices);
                Assert.That(stepwise.Deformer.TryUpgradeDeformationDataOneRelease(), Is.False);
                Assert.That(direct.Deformer.TryUpgradeDeformationDataOneRelease(), Is.False);
            }
            finally
            {
                direct?.Dispose();
                stepwise?.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void V001Local_OneReleaseAtATime_PreservesAbsoluteOutputAndIsDeterministic()
        {
            var source = CreateCompatibilityMesh("HistoricalV001LocalMesh");
            var bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            var historicalA = CreateHistoricalAsset(bounds, worldSpace: false);
            var historicalB = CreateHistoricalAsset(bounds, worldSpace: false);
            ApplyDistinctControlPointEdits(historicalA);
            ApplyDistinctControlPointEdits(historicalB);
            var golden = EvaluateHistoricalAbsolute(source.vertices, historicalA, null, false);

            HistoricalFixture first = null;
            HistoricalFixture second = null;
            try
            {
                first = CreateFixture("V001Local_First", source, DeformationDataVersion.V0_0_1);
                second = CreateFixture("V001Local_Second", source, DeformationDataVersion.V0_0_1);
                ConfigureSingleSettingsV0(first.Deformer, historicalA);
                ConfigureSingleSettingsV0(second.Deformer, historicalB);

                var observed = new List<DeformationDataVersion>
                {
                    first.Deformer.SerializedDeformationDataVersion,
                };

                for (int release = 1; release < s_publishedReleases.Length; release++)
                {
                    Assert.That(first.Deformer.TryUpgradeDeformationDataOneRelease(), Is.True);
                    Assert.That(second.Deformer.TryUpgradeDeformationDataOneRelease(), Is.True);

                    var expected = s_publishedReleases[release];
                    Assert.That(first.Deformer.SerializedDeformationDataVersion, Is.EqualTo(expected));
                    Assert.That(second.Deformer.SerializedDeformationDataVersion, Is.EqualTo(expected));
                    Assert.That(CaptureSemanticJson(first.Deformer), Is.EqualTo(CaptureSemanticJson(second.Deformer)),
                        $"The {ReleaseLabel(expected)} step must be deterministic when applied twice to equal payloads.");
                    observed.Add(expected);
                }

                CollectionAssert.AreEqual(s_publishedReleases, observed);
                Assert.That(first.Deformer.MigrationStatus, Is.EqualTo(DeformationDataMigrationStatus.Ready));
                Assert.That(first.Deformer.Groups.Count, Is.EqualTo(1));
                Assert.That(first.Deformer.ActiveGroupIndex, Is.EqualTo(0));
                Assert.That(first.Deformer.Layers.Count, Is.EqualTo(1));
                Assert.That(first.Deformer.ActiveLayerIndex, Is.EqualTo(0));
                Assert.That(first.Deformer.Layers[0].Name, Is.EqualTo("Lattice Layer"));
                AssertAssetEqual(historicalA, first.Deformer.Layers[0].Settings);

                var finalMesh = first.Deformer.Deform(false);
                Assert.That(finalMesh, Is.Not.Null);
                AssertVectorsEqual(golden, finalMesh.vertices);

                string finalState = CaptureSemanticJson(first.Deformer);
                var finalVertices = finalMesh.vertices;
                Assert.That(first.Deformer.TryUpgradeDeformationDataOneRelease(), Is.False);
                Assert.That(first.Deformer.TryUpgradeDeformationDataOneRelease(), Is.False);
                Assert.That(CaptureSemanticJson(first.Deformer), Is.EqualTo(finalState));
                AssertVectorsEqual(finalVertices, first.Deformer.Deform(false).vertices);
            }
            finally
            {
                second?.Dispose();
                first?.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void MissingVersionField_SingleSettingsShape_ClassifiesBeforeFirstReleaseMutation()
        {
            var source = CreateCompatibilityMesh("HistoricalUnversionedSingleMesh");
            HistoricalFixture fixture = null;
            try
            {
                var historical = CreateHistoricalAsset(new Bounds(Vector3.zero, Vector3.one * 2f), false);
                ApplyDistinctControlPointEdits(historical, 0.14f);
                var golden = EvaluateHistoricalAbsolute(source.vertices, historical, null, false);
                fixture = CreateFixture("Unversioned Single", source, DeformationDataVersion.Unversioned);
                ConfigureSingleSettingsV0(fixture.Deformer, historical);
                string beforeAsset = JsonUtility.ToJson(historical);
                string beforeShape = CaptureSemanticJson(fixture.Deformer, includeVersionMarkers: false);

                Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.True);
                Assert.That(fixture.Deformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.V0_0_1));
                Assert.That(fixture.Deformer.SourceDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.V0_0_1));
                Assert.That(JsonUtility.ToJson(historical), Is.EqualTo(beforeAsset));
                Assert.That(CaptureSemanticJson(fixture.Deformer, includeVersionMarkers: false),
                    Is.EqualTo(beforeShape));

                UpgradeToCurrent(fixture.Deformer);
                AssertVectorsEqual(golden, fixture.Deformer.Deform(false).vertices);
            }
            finally
            {
                fixture?.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void MissingVersionField_GroupShape_ClassifiesAsFirstPublishedGroupRelease()
        {
            var source = CreateCompatibilityMesh("HistoricalUnversionedGroupMesh");
            HistoricalFixture fixture = null;
            try
            {
                fixture = CreateFixture("Unversioned Group", source, DeformationDataVersion.Unversioned);
                ConfigureReleaseShape(fixture.Deformer, DeformationDataVersion.V1_2_1, 0.09f);
                SetSerializedRelease(fixture.Deformer, DeformationDataVersion.Unversioned);
                string beforeShape = CaptureSemanticJson(fixture.Deformer, includeVersionMarkers: false);

                Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.True);
                Assert.That(fixture.Deformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.V1_2_1));
                Assert.That(fixture.Deformer.SourceDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.V1_2_1));
                Assert.That(CaptureSemanticJson(fixture.Deformer, includeVersionMarkers: false),
                    Is.EqualTo(beforeShape));
                Assert.That(fixture.Deformer.Groups.Count, Is.EqualTo(1));
                Assert.That(fixture.Deformer.Groups[0].Name, Is.EqualTo("Published Group"));
                Assert.That(beforeShape, Does.Contain("Published Group"));
            }
            finally
            {
                fixture?.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void MissingVersionField_FlatInternalShape_ClassifiesAsV120WithoutMutation()
        {
            var source = CreateCompatibilityMesh("HistoricalUnversionedFlatMesh");
            HistoricalFixture fixture = null;
            try
            {
                fixture = CreateFixture("Unversioned Flat", source, DeformationDataVersion.Unversioned);
                var asset = CreateHistoricalAsset(new Bounds(Vector3.zero, Vector3.one * 2f), false);
                ApplyDistinctControlPointEdits(asset, 0.1f);
                var layer = CreateLatticeLayer("Internal Flat Layer", asset, 0.6f);
                SetField(fixture.Deformer, "_settings", CreateHistoricalAsset(asset.LocalBounds, false));
                SetField(fixture.Deformer, "_layers", new List<LatticeLayer> { layer });
                SetField(fixture.Deformer, "_groups", new List<DeformerGroup>());
                SetField(fixture.Deformer, "_activeLayerIndex", 0);
                SetField(fixture.Deformer, "_layerModelVersion", 1);
                string beforeShape = CaptureSemanticJson(fixture.Deformer, includeVersionMarkers: false);

                Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.True);
                Assert.That(fixture.Deformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.V1_2_0));
                Assert.That(fixture.Deformer.SourceDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.V1_2_0));
                Assert.That(CaptureSemanticJson(fixture.Deformer, includeVersionMarkers: false),
                    Is.EqualTo(beforeShape));

                UpgradeToCurrent(fixture.Deformer);
                Assert.That(fixture.Deformer.Groups.Count, Is.EqualTo(1));
                Assert.That(fixture.Deformer.Layers[0].Name, Is.EqualTo("Internal Flat Layer"));
            }
            finally
            {
                fixture?.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void MissingVersionField_FreshDefaultShape_ClassifiesDirectlyAsCurrent()
        {
            var source = CreateCompatibilityMesh("HistoricalFreshClassifierMesh");
            HistoricalFixture fixture = null;
            try
            {
                fixture = CreateFixture("Fresh Unversioned", source, DeformationDataVersion.Unversioned);
                var freshSettings = new LatticeAsset();
                SetField(fixture.Deformer, "_settings", freshSettings);
                SetField(fixture.Deformer, "_layers", new List<LatticeLayer>());
                SetField(fixture.Deformer, "_groups", new List<DeformerGroup>());
                SetField(fixture.Deformer, "_layerModelVersion", 0);
                SetField(fixture.Deformer, "_hasInitializedFromSource", false);
                SetField<Mesh>(fixture.Deformer, "_serializedSourceMesh", null);

                Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.True);
                Assert.That(fixture.Deformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.CurrentDevelopment));
                Assert.That(fixture.Deformer.SourceDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.CurrentDevelopment));
                Assert.That(freshSettings.ControlPointsLocal.Length, Is.Zero);
                Assert.That(GetField<List<LatticeLayer>>(fixture.Deformer, "_layers"), Is.Empty);
                Assert.That(GetField<List<DeformerGroup>>(fixture.Deformer, "_groups"), Is.Empty);
                Assert.That(fixture.Deformer.UsesLegacyAbsoluteLatticeEvaluation, Is.False);
            }
            finally
            {
                fixture?.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void MissingVersionField_NeutralNonDefaultSingleSettings_UsesHistoricalChainAndPreservesSettings()
        {
            var source = CreateCompatibilityMesh("HistoricalNeutralNonDefaultMesh");
            HistoricalFixture fixture = null;
            try
            {
                fixture = CreateFixture("Neutral Nondefault Unversioned", source, DeformationDataVersion.Unversioned);
                var bounds = new Bounds(new Vector3(0.3f, -0.2f, 0.4f), new Vector3(2.5f, 1.5f, 3f));
                var neutral = new LatticeAsset();
                neutral.GridSize = new Vector3Int(2, 3, 2);
                neutral.LocalBounds = bounds;
                neutral.Interpolation = LatticeInterpolationMode.Trilinear;
                neutral.ResetControlPoints();
                SetField(neutral, "_serializationVersion", 0);
                SetField(neutral, "_applySpace", 0);
                SetField(fixture.Deformer, "_settings", neutral);
                SetField(fixture.Deformer, "_layers", new List<LatticeLayer>());
                SetField(fixture.Deformer, "_groups", new List<DeformerGroup>());
                SetField(fixture.Deformer, "_layerModelVersion", 0);
                SetField(fixture.Deformer, "_hasInitializedFromSource", false);
                SetField<Mesh>(fixture.Deformer, "_serializedSourceMesh", null);

                Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.True);
                Assert.That(fixture.Deformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.V0_0_1));
                UpgradeToCurrent(fixture.Deformer);

                var rawGroups = GetField<List<DeformerGroup>>(fixture.Deformer, "_groups");
                Assert.That(rawGroups.Count, Is.EqualTo(1));
                Assert.That(rawGroups[0].Layers.Count, Is.EqualTo(1));
                AssertAssetEqual(neutral, rawGroups[0].Layers[0].SerializedSettings);
                Assert.That(fixture.Deformer.UsesLegacyAbsoluteLatticeEvaluation, Is.True);
            }
            finally
            {
                fixture?.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void V001World_NonIdentityTransforms_PreserveDynamicHistoricalEvaluationAndMarker()
        {
            var source = CreateCompatibilityMesh("HistoricalV001WorldMesh");
            HistoricalFixture fixture = null;
            try
            {
                fixture = CreateFixture("V001World", source, DeformationDataVersion.V0_0_1);
                fixture.Root.transform.SetPositionAndRotation(
                    new Vector3(3.25f, -1.5f, 2.75f),
                    Quaternion.Euler(23f, -37f, 14f));
                fixture.Root.transform.localScale = new Vector3(1.75f, 0.45f, 2.2f);

                var localSourceBounds = new Bounds(Vector3.zero, Vector3.one * 2f);
                var worldBounds = TransformBounds(fixture.Root.transform.localToWorldMatrix, localSourceBounds);
                var worldAsset = CreateHistoricalAsset(worldBounds, worldSpace: true);
                ApplyDistinctControlPointEdits(worldAsset, 0.35f);
                ConfigureSingleSettingsV0(fixture.Deformer, worldAsset);

                var goldenAtUpgrade = EvaluateHistoricalAbsolute(
                    source.vertices,
                    worldAsset,
                    fixture.Root.transform,
                    worldSpace: true);

                UpgradeToCurrent(fixture.Deformer);

                var result = fixture.Deformer.Deform(false);
                Assert.That(result, Is.Not.Null);
                AssertVectorsEqual(goldenAtUpgrade, result.vertices, 4e-5f);

                var migrated = fixture.Deformer.Layers[0].Settings;
                Assert.That(migrated.HasPendingLegacyWorldSpace, Is.True);
                Assert.That(migrated.LegacyApplySpaceValue, Is.EqualTo(1));
                Assert.That(fixture.Deformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.CurrentDevelopment));

                fixture.Root.transform.SetPositionAndRotation(
                    new Vector3(-4.5f, 2.25f, -1.75f),
                    Quaternion.Euler(-31f, 18f, 47f));
                fixture.Root.transform.localScale = new Vector3(0.55f, 2.4f, 1.3f);
                var goldenAfterOwnerChange = EvaluateHistoricalAbsolute(
                    source.vertices,
                    worldAsset,
                    fixture.Root.transform,
                    worldSpace: true);
                fixture.Deformer.InvalidateCache();
                AssertVectorsEqual(
                    goldenAfterOwnerChange,
                    fixture.Deformer.Deform(false).vertices,
                    4e-5f);
                Assert.That(migrated.HasPendingLegacyWorldSpace, Is.True);
                Assert.That(migrated.LegacyApplySpaceValue, Is.EqualTo(1));
            }
            finally
            {
                fixture?.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void V120FlatMixedLayers_PreservesDataCurvesOrderAndActiveRemap()
        {
            var source = CreateCompatibilityMesh("HistoricalV120FlatMesh");
            HistoricalFixture fixture = null;
            try
            {
                fixture = CreateFixture("V120Flat", source, DeformationDataVersion.V1_2_0);
                var latticeAsset = CreateHistoricalAsset(new Bounds(Vector3.zero, Vector3.one * 2f), false);
                ApplyDistinctControlPointEdits(latticeAsset, 0.12f);

                var lattice = CreateLatticeLayer("Legacy Lattice", latticeAsset, 0.65f);
                var brush = CreateBrushLayer(
                    "Legacy Brush",
                    new[]
                    {
                        new Vector3(0.1f, 0.2f, -0.3f),
                        new Vector3(-0.4f, 0.05f, 0.2f),
                        new Vector3(0.25f, -0.1f, 0.15f),
                        new Vector3(-0.05f, 0.3f, -0.2f),
                    },
                    new[] { 0f, 0.25f, 0.75f, 1f },
                    0.8f);
                var curve = CreateWeightedCurve(0.37f);

                SetField(fixture.Deformer, "_settings", CreateHistoricalAsset(latticeAsset.LocalBounds, false));
                SetField(fixture.Deformer, "_layers", new List<LatticeLayer> { lattice, null, brush });
                SetField(fixture.Deformer, "_groups", new List<DeformerGroup>());
                SetField(fixture.Deformer, "_activeLayerIndex", 2);
                SetField(fixture.Deformer, "_activeGroupIndex", 0);
                SetField(fixture.Deformer, "_layerModelVersion", 2);
                SetField(fixture.Deformer, "_blendShapeOutput", BlendShapeOutputMode.OutputAsBlendShape);
                SetField(fixture.Deformer, "_blendShapeName", "Migrated Group Shape");
                SetField(fixture.Deformer, "_blendShapeCurve", curve);

                var expectedDisplacements = brush.BrushDisplacements.ToArray();
                var expectedMask = brush.VertexMask.ToArray();
                UpgradeToCurrent(fixture.Deformer);

                Assert.That(fixture.Deformer.Groups.Count, Is.EqualTo(1));
                var group = fixture.Deformer.Groups[0];
                Assert.That(group.Layers.Count, Is.EqualTo(2));
                Assert.That(group.Layers[0].Name, Is.EqualTo("Legacy Lattice"));
                Assert.That(group.Layers[1].Name, Is.EqualTo("Legacy Brush"));
                Assert.That(group.ActiveLayerIndex, Is.EqualTo(1), "The null slot before the active layer must be remapped.");
                Assert.That(group.BlendShapeOutput, Is.EqualTo(BlendShapeOutputMode.OutputAsBlendShape));
                Assert.That(group.BlendShapeName, Is.EqualTo("Migrated Group Shape"));
                AssertCurveEqual(curve, group.BlendShapeCurve);
                AssertAssetEqual(latticeAsset, group.Layers[0].Settings);
                AssertVectorsEqual(expectedDisplacements, group.Layers[1].BrushDisplacements);
                AssertFloatsEqual(expectedMask, group.Layers[1].VertexMask);
            }
            finally
            {
                fixture?.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void V121Groups_PreservesGroupCurvesAndDefaultsMissingPublishedLayerCurves()
        {
            var source = CreateCompatibilityMesh("HistoricalV121GroupMesh");
            HistoricalFixture fixture = null;
            try
            {
                fixture = CreateFixture("V121Groups", source, DeformationDataVersion.V1_2_1);
                var groupCurveA = CreateWeightedCurve(0.22f);
                var groupCurveB = CreateWeightedCurve(0.61f);
                var defaultLayerCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

                var firstAsset = CreateHistoricalAsset(new Bounds(Vector3.zero, Vector3.one * 2f), false);
                ApplyDistinctControlPointEdits(firstAsset, 0.08f);
                var firstLayer = CreateLatticeLayer("First Lattice", firstAsset, 0.45f);
                firstLayer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                firstLayer.BlendShapeName = "First Layer Shape";
                var firstBrush = CreateBrushLayer(
                    "First Brush",
                    new[] { Vector3.right, Vector3.up, Vector3.forward, Vector3.one },
                    new[] { 1f, 0.8f, 0.6f, 0.4f },
                    0.3f);

                var firstGroup = new DeformerGroup
                {
                    Name = "Direct Group",
                    Enabled = true,
                    BlendShapeOutput = BlendShapeOutputMode.Disabled,
                    BlendShapeName = "Direct Name",
                    BlendShapeCurve = groupCurveA,
                };
                firstGroup.LayersList.Add(firstLayer);
                firstGroup.LayersList.Add(firstBrush);
                firstGroup.ActiveLayerIndex = 1;

                var secondBrush = CreateBrushLayer(
                    "Second Brush",
                    new[] { Vector3.left, Vector3.down, Vector3.back, -Vector3.one },
                    new[] { 0.1f, 0.3f, 0.7f, 1f },
                    0.9f);
                secondBrush.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                secondBrush.BlendShapeName = "Second Layer Shape";

                var secondGroup = new DeformerGroup
                {
                    Name = "BlendShape Group",
                    Enabled = false,
                    BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape,
                    BlendShapeName = "Second Group Shape",
                    BlendShapeCurve = groupCurveB,
                };
                secondGroup.LayersList.Add(secondBrush);
                secondGroup.ActiveLayerIndex = 0;

                SetField(fixture.Deformer, "_settings", CreateHistoricalAsset(firstAsset.LocalBounds, false));
                SetField(fixture.Deformer, "_layers", new List<LatticeLayer>());
                SetField(fixture.Deformer, "_groups", new List<DeformerGroup> { firstGroup, secondGroup });
                SetField(fixture.Deformer, "_activeGroupIndex", 1);
                SetField(fixture.Deformer, "_layerModelVersion", 3);

                UpgradeToCurrent(fixture.Deformer);

                Assert.That(fixture.Deformer.Groups.Select(group => group.Name),
                    Is.EqualTo(new[] { "Direct Group", "BlendShape Group" }));
                Assert.That(fixture.Deformer.ActiveGroupIndex, Is.EqualTo(1));
                Assert.That(fixture.Deformer.Groups[0].ActiveLayerIndex, Is.EqualTo(1));
                Assert.That(fixture.Deformer.Groups[1].ActiveLayerIndex, Is.EqualTo(0));
                Assert.That(fixture.Deformer.Groups[1].Enabled, Is.False);
                AssertCurveEqual(groupCurveA, fixture.Deformer.Groups[0].BlendShapeCurve);
                AssertCurveEqual(groupCurveB, fixture.Deformer.Groups[1].BlendShapeCurve);
                AssertCurveEqual(defaultLayerCurve, fixture.Deformer.Groups[0].Layers[0].BlendShapeCurve);
                AssertCurveEqual(defaultLayerCurve, fixture.Deformer.Groups[1].Layers[0].BlendShapeCurve);
            }
            finally
            {
                fixture?.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void V120MismatchedSourceVertexCount_BlocksFirstStepAndPreservesFlatPayload()
        {
            var source = CreateCompatibilityMesh("HistoricalMismatchMesh");
            HistoricalFixture fixture = null;
            try
            {
                fixture = CreateFixture("MismatchedBrush", source, DeformationDataVersion.V1_2_0);
                var brush = CreateBrushLayer(
                    "Orphaned Brush Payload",
                    new[] { Vector3.right, Vector3.up, Vector3.forward, Vector3.one, new Vector3(5f, 6f, 7f) },
                    new[] { 0f, 0.5f, 1f },
                    1f);
                var expectedDisplacements = brush.BrushDisplacements.ToArray();
                var expectedMask = brush.VertexMask.ToArray();

                SetField(fixture.Deformer, "_layers", new List<LatticeLayer> { brush });
                SetField(fixture.Deformer, "_groups", new List<DeformerGroup>());
                SetField(fixture.Deformer, "_activeLayerIndex", 0);
                SetField(fixture.Deformer, "_layerModelVersion", 2);

                string before = CaptureSemanticJson(fixture.Deformer);
                Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.False);
                _ = fixture.Deformer.Groups;
                var recoveryLayers = fixture.Deformer.Layers;
                Assert.That(fixture.Deformer.Deform(false), Is.Null);

                Assert.That(fixture.Deformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.V1_2_0));
                Assert.That(fixture.Deformer.MigrationStatus,
                    Is.EqualTo(DeformationDataMigrationStatus.InvalidData));
                var recovered = recoveryLayers.Single(layer => layer != null && layer.Name == "Orphaned Brush Payload");
                AssertVectorsEqual(expectedDisplacements, recovered.BrushDisplacements);
                AssertFloatsEqual(expectedMask, recovered.VertexMask);
                AssertVectorsEqual(expectedDisplacements, brush.BrushDisplacements);
                AssertFloatsEqual(expectedMask, brush.VertexMask);
                Assert.That(CaptureSemanticJson(fixture.Deformer), Is.EqualTo(before));
            }
            finally
            {
                fixture?.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void V121GroupBrushMismatch_BlocksFirstStepAndExposesAuthoritativeRawGroup()
        {
            var source = CreateCompatibilityMesh("HistoricalV121GroupMismatch");
            HistoricalFixture fixture = null;
            try
            {
                fixture = CreateFixture(
                    "V121 Group Mismatch",
                    source,
                    DeformationDataVersion.V1_2_1);
                var brush = CreateBrushLayer(
                    "Historical Group Brush",
                    new[] { Vector3.right, Vector3.up, Vector3.forward, Vector3.one, Vector3.left },
                    new[] { 0f, 0.5f, 1f },
                    0.75f);
                var group = new DeformerGroup { Name = "Historical Authoritative Group", Enabled = true };
                group.LayersList.Add(brush);
                group.ActiveLayerIndex = 0;
                SetField(fixture.Deformer, "_layers", new List<LatticeLayer>());
                SetField(fixture.Deformer, "_groups", new List<DeformerGroup> { group });
                SetField(fixture.Deformer, "_activeGroupIndex", 0);
                SetField(fixture.Deformer, "_layerModelVersion", 3);
                var expectedDisplacements = brush.BrushDisplacements.ToArray();
                var expectedMask = brush.VertexMask.ToArray();
                string before = CaptureSemanticJson(fixture.Deformer);

                Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.False);
                var recovery = fixture.Deformer.Layers.Single(layer => layer.Name == "Historical Group Brush");
                Assert.That(fixture.Deformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.V1_2_1));
                Assert.That(fixture.Deformer.MigrationStatus,
                    Is.EqualTo(DeformationDataMigrationStatus.InvalidData));
                Assert.That(fixture.Deformer.Deform(false), Is.Null);
                AssertVectorsEqual(expectedDisplacements, recovery.BrushDisplacements);
                AssertFloatsEqual(expectedMask, recovery.VertexMask);
                Assert.That(CaptureSemanticJson(fixture.Deformer), Is.EqualTo(before));
            }
            finally
            {
                fixture?.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void CurrentCrossMeshBrushMismatch_ExposesRawRecoveryViewAndCanBeRepaired()
        {
            var source = CreateCompatibilityMesh("CurrentCrossMeshMismatch");
            HistoricalFixture fixture = null;
            try
            {
                fixture = CreateFixture(
                    "Current Cross Mesh Mismatch",
                    source,
                    DeformationDataVersion.CurrentDevelopment);
                var brush = CreateBrushLayer(
                    "Current Pasted Brush",
                    new[] { Vector3.right, Vector3.up, Vector3.forward, Vector3.one, new Vector3(5f, 6f, 7f) },
                    new[] { 0f, 0.5f, 1f },
                    1f);
                var group = new DeformerGroup { Name = "Authoritative Current Group", Enabled = true };
                group.LayersList.Add(brush);
                group.ActiveLayerIndex = 0;
                SetField(fixture.Deformer, "_settings", CreateHistoricalAsset(new Bounds(Vector3.zero, Vector3.one), false));
                SetField(fixture.Deformer, "_layers", new List<LatticeLayer>());
                SetField(fixture.Deformer, "_groups", new List<DeformerGroup> { group });
                SetField(fixture.Deformer, "_activeGroupIndex", 0);
                SetField(fixture.Deformer, "_layerModelVersion", 3);

                var expectedDisplacements = brush.BrushDisplacements.ToArray();
                var expectedMask = brush.VertexMask.ToArray();
                var recoveryLayers = fixture.Deformer.Layers;
                Assert.That(fixture.Deformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.CurrentDevelopment));
                Assert.That(fixture.Deformer.MigrationStatus,
                    Is.EqualTo(DeformationDataMigrationStatus.InvalidData));
                Assert.That(fixture.Deformer.Deform(false), Is.Null);
                var recovered = recoveryLayers.Single(layer => layer != null && layer.Name == "Current Pasted Brush");
                AssertVectorsEqual(expectedDisplacements, recovered.BrushDisplacements);
                AssertFloatsEqual(expectedMask, recovered.VertexMask);

                recovered.BrushDisplacements = expectedDisplacements.Take(source.vertexCount).ToArray();
                recovered.VertexMask = new[] { 0f, 0.5f, 1f, 1f };
                Assert.That(fixture.Deformer.Deform(false), Is.Not.Null);
                Assert.That(fixture.Deformer.MigrationStatus,
                    Is.EqualTo(DeformationDataMigrationStatus.Ready));
            }
            finally
            {
                fixture?.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void V001Local_MismatchedNonEmptyControlPointArray_FailsClosedWithoutMutation()
        {
            var source = CreateCompatibilityMesh("HistoricalMalformedV001Mesh");
            HistoricalFixture fixture = null;
            try
            {
                fixture = CreateFixture("Malformed V001", source, DeformationDataVersion.V0_0_1);
                var malformed = CreateRawHistoricalAsset(
                    new Vector3Int(2, 2, 2),
                    new Bounds(Vector3.zero, Vector3.one * 2f),
                    new[] { new Vector3(1.25f, -2.5f, 3.75f), new Vector3(-4f, 5f, -6f) });
                ConfigureSingleSettingsV0(fixture.Deformer, malformed);

                string beforeAsset = JsonUtility.ToJson(malformed);
                string beforeComponent = CaptureSemanticJson(fixture.Deformer);
                Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.False);
                _ = fixture.Deformer.Groups;

                Assert.That(fixture.Deformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.V0_0_1));
                Assert.That(fixture.Deformer.MigrationStatus,
                    Is.EqualTo(DeformationDataMigrationStatus.InvalidData));
                Assert.That(JsonUtility.ToJson(malformed), Is.EqualTo(beforeAsset));
                Assert.That(CaptureSemanticJson(fixture.Deformer), Is.EqualTo(beforeComponent));
            }
            finally
            {
                fixture?.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void V120FlatLattice_MismatchedControlPointArray_FailsClosedWithoutMutation()
        {
            var source = CreateCompatibilityMesh("HistoricalMalformedV120Mesh");
            HistoricalFixture fixture = null;
            try
            {
                fixture = CreateFixture("Malformed V120", source, DeformationDataVersion.V1_2_0);
                var malformed = CreateRawHistoricalAsset(
                    new Vector3Int(3, 2, 2),
                    new Bounds(Vector3.zero, Vector3.one * 3f),
                    new[] { Vector3.right, Vector3.up, Vector3.forward });
                var layer = CreateLatticeLayer("Malformed Flat Lattice", malformed, 1f);
                SetField(fixture.Deformer, "_settings", CreateHistoricalAsset(malformed.LocalBounds, false));
                SetField(fixture.Deformer, "_layers", new List<LatticeLayer> { layer });
                SetField(fixture.Deformer, "_groups", new List<DeformerGroup>());
                SetField(fixture.Deformer, "_activeLayerIndex", 0);
                SetField(fixture.Deformer, "_layerModelVersion", 2);

                string beforeAsset = JsonUtility.ToJson(malformed);
                string beforeComponent = CaptureSemanticJson(fixture.Deformer);
                Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.False);
                _ = fixture.Deformer.Groups;

                Assert.That(fixture.Deformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.V1_2_0));
                Assert.That(fixture.Deformer.MigrationStatus,
                    Is.EqualTo(DeformationDataMigrationStatus.InvalidData));
                Assert.That(JsonUtility.ToJson(malformed), Is.EqualTo(beforeAsset));
                Assert.That(CaptureSemanticJson(fixture.Deformer), Is.EqualTo(beforeComponent));
            }
            finally
            {
                fixture?.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void V001InvalidGridAxis_FailsClosedAndPreservesRawPayload(bool hasControlPoints)
        {
            var source = CreateCompatibilityMesh("HistoricalInvalidGridMesh");
            HistoricalFixture fixture = null;
            try
            {
                fixture = CreateFixture("Invalid Grid", source, DeformationDataVersion.V0_0_1);
                var rawPoints = hasControlPoints
                    ? new[] { new Vector3(7f, 8f, 9f), new Vector3(-1f, -2f, -3f) }
                    : Array.Empty<Vector3>();
                var malformed = CreateRawHistoricalAsset(
                    new Vector3Int(1, 2, 2),
                    new Bounds(new Vector3(2f, 3f, 4f), new Vector3(5f, 6f, 7f)),
                    rawPoints);
                ConfigureSingleSettingsV0(fixture.Deformer, malformed);

                string beforeAsset = JsonUtility.ToJson(malformed);
                string beforeComponent = CaptureSemanticJson(fixture.Deformer);
                Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.False);
                _ = fixture.Deformer.Groups;

                Assert.That(fixture.Deformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.V0_0_1));
                Assert.That(fixture.Deformer.MigrationStatus,
                    Is.EqualTo(DeformationDataMigrationStatus.InvalidData));
                Assert.That(JsonUtility.ToJson(malformed), Is.EqualTo(beforeAsset));
                Assert.That(CaptureSemanticJson(fixture.Deformer), Is.EqualTo(beforeComponent));
            }
            finally
            {
                fixture?.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        [TestCase(false, TestName = "V001NonFiniteLattice_ControlPointNaN_FailsClosedWithoutMutation")]
        [TestCase(true, TestName = "V001NonFiniteLattice_BoundsInfinity_FailsClosedWithoutMutation")]
        public void V001NonFiniteLatticePayload_FailsClosedWithoutMutation(bool corruptBounds)
        {
            var source = CreateCompatibilityMesh("HistoricalNonFiniteLatticeMesh");
            HistoricalFixture fixture = null;
            try
            {
                fixture = CreateFixture(
                    "Non-finite Historical Lattice",
                    source,
                    DeformationDataVersion.V0_0_1);
                var malformed = CreateHistoricalAsset(
                    new Bounds(new Vector3(0.1f, -0.2f, 0.3f), Vector3.one * 2f),
                    false);
                if (corruptBounds)
                {
                    SetField(
                        malformed,
                        "_localBounds",
                        new Bounds(
                            new Vector3(0.1f, float.PositiveInfinity, 0.3f),
                            Vector3.one * 2f));
                }
                else
                {
                    var points = malformed.ControlPointsLocal.ToArray();
                    points[3] = new Vector3(points[3].x, float.NaN, points[3].z);
                    SetField(malformed, "_controlPointsLocal", points);
                }
                ConfigureSingleSettingsV0(fixture.Deformer, malformed);

                string beforeAsset = JsonUtility.ToJson(malformed);
                string beforeComponent = CaptureSemanticJson(fixture.Deformer);
                Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.False);

                Assert.That(fixture.Deformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.V0_0_1));
                Assert.That(fixture.Deformer.MigrationStatus,
                    Is.EqualTo(DeformationDataMigrationStatus.InvalidData));
                Assert.That(JsonUtility.ToJson(malformed), Is.EqualTo(beforeAsset));
                Assert.That(CaptureSemanticJson(fixture.Deformer), Is.EqualTo(beforeComponent));
                if (corruptBounds)
                {
                    Assert.That(float.IsPositiveInfinity(malformed.LocalBounds.center.y), Is.True);
                }
                else
                {
                    Assert.That(float.IsNaN(malformed.ControlPointsLocal[3].y), Is.True);
                }
            }
            finally
            {
                fixture?.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void V001UnknownInterpolation_FailsClosedWithoutMutation()
        {
            var source = CreateCompatibilityMesh("HistoricalUnknownInterpolationMesh");
            HistoricalFixture fixture = null;
            try
            {
                fixture = CreateFixture(
                    "Unknown Historical Interpolation",
                    source,
                    DeformationDataVersion.V0_0_1);
                var malformed = CreateHistoricalAsset(
                    new Bounds(Vector3.zero, Vector3.one * 2f),
                    false);
                var unknown = (LatticeInterpolationMode)int.MaxValue;
                SetField(malformed, "_interpolation", unknown);
                ConfigureSingleSettingsV0(fixture.Deformer, malformed);

                string beforeAsset = JsonUtility.ToJson(malformed);
                string beforeComponent = CaptureSemanticJson(fixture.Deformer);
                Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.False);

                Assert.That(fixture.Deformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.V0_0_1));
                Assert.That(fixture.Deformer.MigrationStatus,
                    Is.EqualTo(DeformationDataMigrationStatus.InvalidData));
                Assert.That(JsonUtility.ToJson(malformed), Is.EqualTo(beforeAsset));
                Assert.That(CaptureSemanticJson(fixture.Deformer), Is.EqualTo(beforeComponent));
                Assert.That(malformed.Interpolation, Is.EqualTo(unknown));
            }
            finally
            {
                fixture?.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        [TestCase(false, TestName = "V121NonFiniteBrush_DisplacementNaN_FailsClosedWithoutMutation")]
        [TestCase(true, TestName = "V121NonFiniteBrush_MaskInfinity_FailsClosedWithoutMutation")]
        public void V121NonFiniteBrushPayload_FailsClosedWithoutMutation(bool corruptMask)
        {
            var source = CreateCompatibilityMesh("HistoricalNonFiniteBrushMesh");
            HistoricalFixture fixture = null;
            try
            {
                fixture = CreateFixture(
                    "Non-finite Historical Brush",
                    source,
                    DeformationDataVersion.V1_2_1);
                ConfigureReleaseShape(fixture.Deformer, DeformationDataVersion.V1_2_1, 0.13f);
                var brush = CreateBrushLayer(
                    "Non-finite Brush",
                    new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward },
                    new[] { 0f, 0.25f, 0.5f, 1f },
                    0.8f);
                if (corruptMask)
                {
                    brush.VertexMask[2] = float.NegativeInfinity;
                }
                else
                {
                    brush.BrushDisplacements[1] = new Vector3(float.NaN, 1f, 2f);
                }
                var group = GetField<List<DeformerGroup>>(fixture.Deformer, "_groups")[0];
                group.LayersList.Clear();
                group.LayersList.Add(brush);
                group.ActiveLayerIndex = 0;

                string before = CaptureSemanticJson(fixture.Deformer);
                Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.False);

                Assert.That(fixture.Deformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.V1_2_1));
                Assert.That(fixture.Deformer.MigrationStatus,
                    Is.EqualTo(DeformationDataMigrationStatus.InvalidData));
                Assert.That(CaptureSemanticJson(fixture.Deformer), Is.EqualTo(before));
                if (corruptMask)
                {
                    Assert.That(float.IsNegativeInfinity(brush.VertexMask[2]), Is.True);
                }
                else
                {
                    Assert.That(float.IsNaN(brush.BrushDisplacements[1].x), Is.True);
                }
            }
            finally
            {
                fixture?.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        [TestCase(false, TestName = "V121UnknownLayerType_FailsClosedWithoutMutation")]
        [TestCase(true, TestName = "V121UnknownLayerBlendShapeOutput_FailsClosedWithoutMutation")]
        public void V121UnknownLayerEnum_FailsClosedWithoutMutation(bool corruptBlendShapeOutput)
        {
            var source = CreateCompatibilityMesh("HistoricalUnknownLayerEnumMesh");
            HistoricalFixture fixture = null;
            try
            {
                fixture = CreateFixture(
                    "Unknown Historical Layer Enum",
                    source,
                    DeformationDataVersion.V1_2_1);
                ConfigureReleaseShape(fixture.Deformer, DeformationDataVersion.V1_2_1, 0.17f);
                var layer = GetField<List<DeformerGroup>>(fixture.Deformer, "_groups")[0].LayersList[0];
                if (corruptBlendShapeOutput)
                {
                    layer.BlendShapeOutput = (BlendShapeOutputMode)int.MaxValue;
                }
                else
                {
                    layer.SetType((MeshDeformerLayerType)int.MaxValue);
                }

                string before = CaptureSemanticJson(fixture.Deformer);
                Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.False);

                Assert.That(fixture.Deformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.V1_2_1));
                Assert.That(fixture.Deformer.MigrationStatus,
                    Is.EqualTo(DeformationDataMigrationStatus.InvalidData));
                Assert.That(CaptureSemanticJson(fixture.Deformer), Is.EqualTo(before));
                if (corruptBlendShapeOutput)
                {
                    Assert.That((int)layer.BlendShapeOutput, Is.EqualTo(int.MaxValue));
                }
                else
                {
                    Assert.That((int)layer.Type, Is.EqualTo(int.MaxValue));
                }
            }
            finally
            {
                fixture?.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CurrentAssetVersionWithMissingControlPoints_IsCorruptAndFailsClosed(bool useNull)
        {
            var source = CreateCompatibilityMesh("CurrentMissingControlPoints");
            HistoricalFixture fixture = null;
            try
            {
                fixture = CreateFixture(
                    "Current Missing Control Points",
                    source,
                    DeformationDataVersion.CurrentDevelopment);
                var malformed = CreateRawHistoricalAsset(
                    new Vector3Int(2, 2, 2),
                    new Bounds(Vector3.zero, Vector3.one * 2f),
                    useNull ? null : Array.Empty<Vector3>());
                SetField(malformed, "_serializationVersion", 1);
                var layer = CreateLatticeLayer("Current Corrupt Lattice", malformed, 1f);
                var group = new DeformerGroup { Name = "Current Corrupt Group", Enabled = true };
                group.LayersList.Add(layer);
                group.ActiveLayerIndex = 0;
                SetField(fixture.Deformer, "_layers", new List<LatticeLayer>());
                SetField(fixture.Deformer, "_groups", new List<DeformerGroup> { group });
                SetField(fixture.Deformer, "_activeGroupIndex", 0);
                SetField(fixture.Deformer, "_layerModelVersion", 3);
                string beforeAsset = JsonUtility.ToJson(malformed);
                string beforeComponent = CaptureSemanticJson(fixture.Deformer);

                Assert.That(fixture.Deformer.Groups, Is.Empty);
                Assert.That(fixture.Deformer.Deform(false), Is.Null);
                Assert.That(fixture.Deformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.CurrentDevelopment));
                Assert.That(fixture.Deformer.MigrationStatus,
                    Is.EqualTo(DeformationDataMigrationStatus.InvalidData));
                Assert.That(JsonUtility.ToJson(malformed), Is.EqualTo(beforeAsset));
                Assert.That(CaptureSemanticJson(fixture.Deformer), Is.EqualTo(beforeComponent));
            }
            finally
            {
                fixture?.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void V001EmptyControlPoints_AreAcceptedAsHistoricalSentinelAndInitializedOnce()
        {
            var source = CreateCompatibilityMesh("HistoricalEmptySentinelMesh");
            HistoricalFixture fixture = null;
            try
            {
                fixture = CreateFixture("V001 Empty Sentinel", source, DeformationDataVersion.V0_0_1);
                var bounds = new Bounds(new Vector3(0.2f, -0.3f, 0.1f), new Vector3(2f, 3f, 4f));
                var sentinel = CreateRawHistoricalAsset(
                    new Vector3Int(2, 2, 2),
                    bounds,
                    Array.Empty<Vector3>());
                ConfigureSingleSettingsV0(fixture.Deformer, sentinel);

                UpgradeToCurrent(fixture.Deformer);
                var migrated = fixture.Deformer.Layers[0].Settings;
                Assert.That(migrated.ControlPointsLocal.Length, Is.EqualTo(8));
                Assert.That(migrated.HasCustomizedControlPoints(), Is.False);
                AssertVectorEqual(bounds.min, migrated.GetControlPointLocal(0));
                AssertVectorEqual(bounds.max, migrated.GetControlPointLocal(7));

                string after = CaptureSemanticJson(fixture.Deformer);
                Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.False);
                Assert.That(CaptureSemanticJson(fixture.Deformer), Is.EqualTo(after));
            }
            finally
            {
                fixture?.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void UnsupportedFutureVersion_IsFailClosedAndDoesNotMutateSemanticPayload()
        {
            var source = CreateCompatibilityMesh("HistoricalFutureMesh");
            HistoricalFixture fixture = null;
            try
            {
                fixture = CreateFixture("FuturePayload", source, DeformationDataVersion.V1_4_0);
                var legacyAsset = CreateHistoricalAsset(new Bounds(Vector3.zero, Vector3.one * 2f), false);
                ApplyDistinctControlPointEdits(legacyAsset, 0.19f);
                var flat = CreateLatticeLayer("Future Flat", legacyAsset, 0.7f);
                var group = new DeformerGroup { Name = "Future Group", Enabled = true };
                group.LayersList.Add(CreateBrushLayer(
                    "Future Brush",
                    new[] { Vector3.right, Vector3.up, Vector3.forward, Vector3.one },
                    new[] { 1f, 0.8f, 0.6f, 0.4f },
                    0.5f));

                var future = (DeformationDataVersion)int.MaxValue;
                SetSerializedRelease(fixture.Deformer, future);
                SetField(fixture.Deformer, "_layerModelVersion", int.MaxValue);
                SetField(fixture.Deformer, "_settings", legacyAsset);
                SetField(fixture.Deformer, "_layers", new List<LatticeLayer> { flat });
                SetField(fixture.Deformer, "_groups", new List<DeformerGroup> { group });
                SetField(fixture.Deformer, "_activeGroupIndex", 0);
                SetField(fixture.Deformer, "_activeLayerIndex", 0);

                string before = CaptureSemanticJson(fixture.Deformer);
                Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.False);
                _ = fixture.Deformer.Groups;
                _ = fixture.Deformer.Layers;
                _ = fixture.Deformer.EditingSettings;
                Assert.That(fixture.Deformer.Deform(false), Is.Null);

                Assert.That(fixture.Deformer.SerializedDeformationDataVersion, Is.EqualTo(future));
                Assert.That(fixture.Deformer.MigrationStatus,
                    Is.EqualTo(DeformationDataMigrationStatus.UnsupportedFutureVersion));
                Assert.That(CaptureSemanticJson(fixture.Deformer), Is.EqualTo(before));
            }
            finally
            {
                fixture?.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void FutureNestedLatticeAssetVersion_IsFailClosedWithoutChangingCurrentComponent()
        {
            var source = CreateCompatibilityMesh("HistoricalFutureAssetMesh");
            HistoricalFixture fixture = null;
            try
            {
                fixture = CreateFixture(
                    "Future Nested Asset",
                    source,
                    DeformationDataVersion.CurrentDevelopment);
                ConfigureReleaseShape(fixture.Deformer, DeformationDataVersion.CurrentDevelopment, 0.13f);
                var asset = GetField<List<DeformerGroup>>(fixture.Deformer, "_groups")[0]
                    .LayersList[0]
                    .SerializedSettings;
                SetField(asset, "_serializationVersion", int.MaxValue);
                string beforeAsset = JsonUtility.ToJson(asset);
                string beforeComponent = CaptureSemanticJson(fixture.Deformer);

                Assert.That(fixture.Deformer.Groups, Is.Empty);
                Assert.That(fixture.Deformer.Deform(false), Is.Null);
                Assert.That(fixture.Deformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.CurrentDevelopment));
                Assert.That(fixture.Deformer.MigrationStatus,
                    Is.EqualTo(DeformationDataMigrationStatus.UnsupportedFutureVersion));
                Assert.That(JsonUtility.ToJson(asset), Is.EqualTo(beforeAsset));
                Assert.That(CaptureSemanticJson(fixture.Deformer), Is.EqualTo(beforeComponent));
            }
            finally
            {
                fixture?.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void FutureLayerModelVersion_DoesNotWrapOrClearFlatPayload()
        {
            var source = CreateCompatibilityMesh("HistoricalFutureLayerModelMesh");
            HistoricalFixture fixture = null;
            try
            {
                fixture = CreateFixture(
                    "Future Layer Model",
                    source,
                    DeformationDataVersion.V1_2_0);
                var asset = CreateHistoricalAsset(new Bounds(Vector3.zero, Vector3.one * 2f), false);
                ApplyDistinctControlPointEdits(asset, 0.18f);
                var layer = CreateLatticeLayer("Future Flat Layer", asset, 0.75f);
                SetField(fixture.Deformer, "_layers", new List<LatticeLayer> { layer });
                SetField(fixture.Deformer, "_groups", new List<DeformerGroup>());
                SetField(fixture.Deformer, "_activeLayerIndex", 0);
                SetField(fixture.Deformer, "_layerModelVersion", int.MaxValue);
                string before = CaptureSemanticJson(fixture.Deformer);

                Assert.That(fixture.Deformer.TryUpgradeDeformationDataOneRelease(), Is.False);
                Assert.That(fixture.Deformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.V1_2_0));
                Assert.That(fixture.Deformer.MigrationStatus,
                    Is.EqualTo(DeformationDataMigrationStatus.UnsupportedFutureVersion));
                Assert.That(CaptureSemanticJson(fixture.Deformer), Is.EqualTo(before));
            }
            finally
            {
                fixture?.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void InactiveDisabledPrefab_InspectorFirstAccess_MigratesPersistsAndReloadsIdempotently()
        {
            string folderName = "__LatticeMigrationTests_" + Guid.NewGuid().ToString("N");
            string folderPath = "Assets/" + folderName;
            string meshPath = folderPath + "/HistoricalSource.asset";
            string prefabPath = folderPath + "/HistoricalV0.prefab";
            GameObject loadedRoot = null;
            GameObject authoringRoot = null;

            try
            {
                Assert.That(AssetDatabase.CreateFolder("Assets", folderName), Is.Not.Empty);
                var source = CreateCompatibilityMesh("HistoricalPrefabSource");
                AssetDatabase.CreateAsset(source, meshPath);

                authoringRoot = new GameObject("Inactive Historical V0");
                authoringRoot.SetActive(false);
                var filter = authoringRoot.AddComponent<MeshFilter>();
                filter.sharedMesh = source;
                var deformer = authoringRoot.AddComponent<LatticeDeformer>();
                deformer.enabled = false;
                var historical = CreateHistoricalAsset(new Bounds(Vector3.zero, Vector3.one * 2f), false);
                ApplyDistinctControlPointEdits(historical, 0.16f);
                ConfigureSourceReferences(deformer, filter, source);
                SetSerializedRelease(deformer, DeformationDataVersion.V0_0_1);
                ConfigureSingleSettingsV0(deformer, historical);

                Assert.That(PrefabUtility.SaveAsPrefabAsset(authoringRoot, prefabPath), Is.Not.Null);
                Object.DestroyImmediate(authoringRoot);
                authoringRoot = null;
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceSynchronousImport);

                loadedRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                Assert.That(loadedRoot.activeSelf, Is.False);
                var loaded = loadedRoot.GetComponent<LatticeDeformer>();
                Assert.That(loaded.enabled, Is.False);

                // EditingSettings is the same facade the custom inspector reads first.
                var editingSettings = loaded.EditingSettings;
                Assert.That(editingSettings, Is.Not.Null);
                Assert.That(loaded.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.CurrentDevelopment));
                Assert.That(loaded.Groups.Count, Is.EqualTo(1));
                Assert.That(loaded.Layers.Count, Is.EqualTo(1));
                AssertAssetEqual(historical, loaded.Layers[0].Settings);

                string firstState = CaptureSemanticJson(loaded);
                var firstOutput = loaded.Deform(false).vertices;
                Assert.That(PrefabUtility.SaveAsPrefabAsset(loadedRoot, prefabPath), Is.Not.Null);
                PrefabUtility.UnloadPrefabContents(loadedRoot);
                loadedRoot = null;
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceSynchronousImport);

                loadedRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                loaded = loadedRoot.GetComponent<LatticeDeformer>();
                _ = loaded.EditingSettings;
                Assert.That(CaptureSemanticJson(loaded), Is.EqualTo(firstState));
                Assert.That(loaded.TryUpgradeDeformationDataOneRelease(), Is.False);
                Assert.That(CaptureSemanticJson(loaded), Is.EqualTo(firstState));
                AssertVectorsEqual(firstOutput, loaded.Deform(false).vertices);
            }
            finally
            {
                if (loadedRoot != null)
                {
                    PrefabUtility.UnloadPrefabContents(loadedRoot);
                }

                if (authoringRoot != null)
                {
                    Object.DestroyImmediate(authoringRoot);
                }

                AssetDatabase.DeleteAsset(folderPath);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }

        [Test]
        public void FreshCurrentComponent_NeutralOffsetField_IsIdentityOutsideBounds()
        {
            var source = CreateCompatibilityMesh("CurrentNeutralOutsideMesh");
            var root = new GameObject("Current Neutral Outside");
            try
            {
                var filter = root.AddComponent<MeshFilter>();
                filter.sharedMesh = source;
                root.AddComponent<MeshRenderer>();
                var deformer = root.AddComponent<LatticeDeformer>();
                var settings = deformer.Layers[0].Settings;
                settings.LocalBounds = new Bounds(Vector3.zero, Vector3.one * 2f);
                settings.GridSize = new Vector3Int(2, 2, 2);
                settings.ResetControlPoints();
                deformer.InvalidateCache();

                var result = deformer.Deform(false);
                Assert.That(result, Is.Not.Null);
                AssertVectorsEqual(source.vertices, result.vertices);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(source);
            }
        }

        private static string ReleaseLabel(DeformationDataVersion release)
        {
            return release switch
            {
                DeformationDataVersion.V0_0_1 => "0.0.1",
                DeformationDataVersion.V0_0_2 => "0.0.2",
                DeformationDataVersion.V0_0_3 => "0.0.3",
                DeformationDataVersion.V0_0_4 => "0.0.4",
                DeformationDataVersion.V0_0_5 => "0.0.5",
                DeformationDataVersion.V0_0_6 => "0.0.6",
                DeformationDataVersion.V1_0_0 => "1.0.0",
                DeformationDataVersion.V1_0_1 => "1.0.1",
                DeformationDataVersion.V1_1_0 => "1.1.0",
                DeformationDataVersion.V1_2_0 => "1.2.0",
                DeformationDataVersion.V1_2_1 => "1.2.1",
                DeformationDataVersion.V1_3_0 => "1.3.0",
                DeformationDataVersion.V1_3_1 => "1.3.1",
                DeformationDataVersion.V1_4_0 => "1.4.0",
                DeformationDataVersion.CurrentDevelopment => "Current",
                _ => release.ToString(),
            };
        }

        private static HistoricalFixture CreateFixture(
            string name,
            Mesh source,
            DeformationDataVersion release)
        {
            var root = new GameObject(name);
            root.SetActive(false);
            var filter = root.AddComponent<MeshFilter>();
            filter.sharedMesh = source;
            root.AddComponent<MeshRenderer>();
            var deformer = root.AddComponent<LatticeDeformer>();
            deformer.enabled = false;
            ConfigureSourceReferences(deformer, filter, source);
            SetSerializedRelease(deformer, release);
            return new HistoricalFixture(root, deformer);
        }

        private static void ConfigureReleaseShape(
            LatticeDeformer deformer,
            DeformationDataVersion release,
            float editScale)
        {
            var asset = CreateHistoricalAsset(new Bounds(Vector3.zero, Vector3.one * 2f), false);
            ApplyDistinctControlPointEdits(asset, editScale);

            if ((int)release <= (int)DeformationDataVersion.V1_2_0)
            {
                ConfigureSingleSettingsV0(deformer, asset);
                return;
            }

            var layer = CreateLatticeLayer("Published Lattice", asset, 1f);
            var group = new DeformerGroup
            {
                Name = "Published Group",
                Enabled = true,
                BlendShapeOutput = BlendShapeOutputMode.Disabled,
                BlendShapeName = "Published Shape",
                BlendShapeCurve = CreateWeightedCurve(0.33f),
            };
            group.LayersList.Add(layer);
            group.ActiveLayerIndex = 0;
            SetField(deformer, "_settings", CreateHistoricalAsset(asset.LocalBounds, false));
            SetField(deformer, "_layers", new List<LatticeLayer>());
            SetField(deformer, "_groups", new List<DeformerGroup> { group });
            SetField(deformer, "_activeLayerIndex", 0);
            SetField(deformer, "_activeGroupIndex", 0);
            SetField(deformer, "_layerModelVersion", 3);
            SetField(deformer, "_hasInitializedFromSource", true);
            deformer.InvalidateCache();
        }

        private static void ConfigureSourceReferences(LatticeDeformer deformer, MeshFilter filter, Mesh source)
        {
            SetField(deformer, "_meshFilter", filter);
            SetField<SkinnedMeshRenderer>(deformer, "_skinnedMeshRenderer", null);
            SetField(deformer, "_serializedSourceMesh", source);
            SetField(deformer, "_sourceMesh", source);
            SetField(deformer, "_hasInitializedFromSource", true);
            deformer.InvalidateCache();
        }

        private static void ConfigureSingleSettingsV0(LatticeDeformer deformer, LatticeAsset settings)
        {
            SetField(deformer, "_settings", settings);
            SetField(deformer, "_layers", new List<LatticeLayer>());
            SetField(deformer, "_groups", new List<DeformerGroup>());
            SetField(deformer, "_activeLayerIndex", -1);
            SetField(deformer, "_activeGroupIndex", 0);
            SetField(deformer, "_layerModelVersion", 0);
            SetField(deformer, "_hasInitializedFromSource", true);
            deformer.InvalidateCache();
        }

        private static void UpgradeToCurrent(LatticeDeformer deformer)
        {
            int guard = s_publishedReleases.Length + 2;
            while (deformer.SerializedDeformationDataVersion != DeformationDataVersion.CurrentDevelopment && guard-- > 0)
            {
                Assert.That(deformer.TryUpgradeDeformationDataOneRelease(), Is.True,
                    $"Upgrade stopped at {deformer.SerializedDeformationDataVersion} ({deformer.MigrationStatus}).");
            }

            Assert.That(guard, Is.GreaterThanOrEqualTo(0), "The release migration did not converge.");
            Assert.That(deformer.SerializedDeformationDataVersion,
                Is.EqualTo(DeformationDataVersion.CurrentDevelopment));
        }

        private static LatticeAsset CreateHistoricalAsset(Bounds bounds, bool worldSpace)
        {
            var asset = new LatticeAsset();
            asset.GridSize = new Vector3Int(2, 2, 2);
            asset.LocalBounds = bounds;
            asset.Interpolation = LatticeInterpolationMode.Trilinear;
            asset.ResetControlPoints();
            SetField(asset, "_serializationVersion", 0);
            SetField(asset, "_applySpace", worldSpace ? 1 : 0);
            return asset;
        }

        private static LatticeAsset CreateRawHistoricalAsset(
            Vector3Int grid,
            Bounds bounds,
            Vector3[] controlPoints)
        {
            var asset = new LatticeAsset();
            SetField(asset, "_gridSize", grid);
            SetField(asset, "_localBounds", bounds);
            SetField(asset, "_controlPointsLocal", controlPoints?.ToArray());
            SetField(asset, "_interpolation", LatticeInterpolationMode.Trilinear);
            SetField(asset, "_applySpace", 0);
            SetField(asset, "_serializationVersion", 0);
            return asset;
        }

        private static void ApplyDistinctControlPointEdits(LatticeAsset asset, float scale = 0.2f)
        {
            for (int i = 0; i < asset.ControlPointCount; i++)
            {
                float x = ((i & 1) == 0 ? -1f : 1f) * scale * (0.35f + i * 0.04f);
                float y = ((i & 2) == 0 ? 1f : -1f) * scale * (0.2f + i * 0.03f);
                float z = ((i & 4) == 0 ? -1f : 1f) * scale * (0.15f + i * 0.02f);
                asset.SetControlPointLocal(i, asset.GetControlPointLocal(i) + new Vector3(x, y, z));
            }
        }

        private static LatticeLayer CreateLatticeLayer(string name, LatticeAsset asset, float weight)
        {
            var layer = new LatticeLayer
            {
                Name = name,
                Enabled = true,
                Weight = weight,
                Settings = asset,
            };
            layer.SetType(MeshDeformerLayerType.Lattice);
            return layer;
        }

        private static LatticeLayer CreateBrushLayer(
            string name,
            Vector3[] displacements,
            float[] mask,
            float weight)
        {
            var layer = new LatticeLayer
            {
                Name = name,
                Enabled = true,
                Weight = weight,
                BrushDisplacements = displacements.ToArray(),
                VertexMask = mask.ToArray(),
            };
            layer.SetType(MeshDeformerLayerType.Brush);
            return layer;
        }

        private static AnimationCurve CreateWeightedCurve(float middleValue)
        {
            var first = new Keyframe(0f, 0f)
            {
                outTangent = 0.35f,
                outWeight = 0.2f,
                weightedMode = WeightedMode.Out,
            };
            var middle = new Keyframe(0.42f, middleValue)
            {
                inTangent = 0.15f,
                outTangent = 1.4f,
                inWeight = 0.17f,
                outWeight = 0.31f,
                weightedMode = WeightedMode.Both,
            };
            var last = new Keyframe(1f, 1f)
            {
                inTangent = 0.7f,
                inWeight = 0.26f,
                weightedMode = WeightedMode.In,
            };
            return new AnimationCurve(first, middle, last)
            {
                preWrapMode = WrapMode.PingPong,
                postWrapMode = WrapMode.Loop,
            };
        }

        private static Mesh CreateCompatibilityMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = new[]
            {
                new Vector3(-0.8f, -0.6f, -0.4f),
                new Vector3(0.2f, 0.1f, 0.3f),
                new Vector3(1.5f, 0.25f, -0.3f),
                new Vector3(-1.25f, 0.8f, 0.6f),
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3[] EvaluateHistoricalAbsolute(
            Vector3[] sourceVertices,
            LatticeAsset asset,
            Transform meshTransform,
            bool worldSpace)
        {
            var grid = asset.GridSize;
            var bounds = asset.LocalBounds;
            var points = asset.ControlPointsLocal.ToArray();
            if (worldSpace)
            {
                Assert.That(meshTransform, Is.Not.Null);
                for (int i = 0; i < points.Length; i++)
                {
                    points[i] = meshTransform.InverseTransformPoint(points[i]);
                }
            }

            var result = new Vector3[sourceVertices.Length];
            for (int i = 0; i < sourceVertices.Length; i++)
            {
                Vector3 normalized = NormalizeAndClamp(bounds, sourceVertices[i]);
                result[i] = SampleTrilinear(points, grid, normalized);
            }

            return result;
        }

        private static Vector3 NormalizeAndClamp(Bounds bounds, Vector3 point)
        {
            var min = bounds.min;
            var size = bounds.size;
            return new Vector3(
                size.x > Mathf.Epsilon ? Mathf.Clamp01((point.x - min.x) / size.x) : 0f,
                size.y > Mathf.Epsilon ? Mathf.Clamp01((point.y - min.y) / size.y) : 0f,
                size.z > Mathf.Epsilon ? Mathf.Clamp01((point.z - min.z) / size.z) : 0f);
        }

        private static Vector3 SampleTrilinear(Vector3[] points, Vector3Int grid, Vector3 normalized)
        {
            float sx = normalized.x * (grid.x - 1);
            float sy = normalized.y * (grid.y - 1);
            float sz = normalized.z * (grid.z - 1);
            int x0 = Mathf.Min(Mathf.FloorToInt(sx), grid.x - 2);
            int y0 = Mathf.Min(Mathf.FloorToInt(sy), grid.y - 2);
            int z0 = Mathf.Min(Mathf.FloorToInt(sz), grid.z - 2);
            int x1 = x0 + 1;
            int y1 = y0 + 1;
            int z1 = z0 + 1;
            float tx = Mathf.Clamp01(sx - x0);
            float ty = Mathf.Clamp01(sy - y0);
            float tz = Mathf.Clamp01(sz - z0);

            int Index(int x, int y, int z) => x + y * grid.x + z * grid.x * grid.y;

            Vector3 c00 = Vector3.Lerp(points[Index(x0, y0, z0)], points[Index(x1, y0, z0)], tx);
            Vector3 c10 = Vector3.Lerp(points[Index(x0, y1, z0)], points[Index(x1, y1, z0)], tx);
            Vector3 c01 = Vector3.Lerp(points[Index(x0, y0, z1)], points[Index(x1, y0, z1)], tx);
            Vector3 c11 = Vector3.Lerp(points[Index(x0, y1, z1)], points[Index(x1, y1, z1)], tx);
            return Vector3.Lerp(Vector3.Lerp(c00, c10, ty), Vector3.Lerp(c01, c11, ty), tz);
        }

        private static Bounds TransformBounds(Matrix4x4 matrix, Bounds bounds)
        {
            var center = matrix.MultiplyPoint3x4(bounds.center);
            var extents = bounds.extents;
            var axisX = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
            var axisY = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
            var axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));
            var transformedExtents = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
            return new Bounds(center, transformedExtents * 2f);
        }

        private static void AssertAssetEqual(LatticeAsset expected, LatticeAsset actual)
        {
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual.GridSize, Is.EqualTo(expected.GridSize));
            AssertVectorEqual(expected.LocalBounds.center, actual.LocalBounds.center);
            AssertVectorEqual(expected.LocalBounds.size, actual.LocalBounds.size);
            Assert.That(actual.Interpolation, Is.EqualTo(expected.Interpolation));
            AssertVectorsEqual(expected.ControlPointsLocal.ToArray(), actual.ControlPointsLocal.ToArray());
        }

        private static void AssertCurveEqual(AnimationCurve expected, AnimationCurve actual)
        {
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual.preWrapMode, Is.EqualTo(expected.preWrapMode));
            Assert.That(actual.postWrapMode, Is.EqualTo(expected.postWrapMode));
            Assert.That(actual.length, Is.EqualTo(expected.length));
            for (int i = 0; i < expected.length; i++)
            {
                var lhs = expected.keys[i];
                var rhs = actual.keys[i];
                Assert.That(rhs.time, Is.EqualTo(lhs.time).Within(Epsilon));
                Assert.That(rhs.value, Is.EqualTo(lhs.value).Within(Epsilon));
                Assert.That(rhs.inTangent, Is.EqualTo(lhs.inTangent).Within(Epsilon));
                Assert.That(rhs.outTangent, Is.EqualTo(lhs.outTangent).Within(Epsilon));
                Assert.That(rhs.inWeight, Is.EqualTo(lhs.inWeight).Within(Epsilon));
                Assert.That(rhs.outWeight, Is.EqualTo(lhs.outWeight).Within(Epsilon));
                Assert.That(rhs.weightedMode, Is.EqualTo(lhs.weightedMode));
            }
        }

        private static void AssertVectorsEqual(
            IReadOnlyList<Vector3> expected,
            IReadOnlyList<Vector3> actual,
            float tolerance = Epsilon)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            for (int i = 0; i < expected.Count; i++)
            {
                AssertVectorEqual(expected[i], actual[i], tolerance, $"vector[{i}]");
            }
        }

        private static void AssertFloatsEqual(
            IReadOnlyList<float> expected,
            IReadOnlyList<float> actual,
            float tolerance = Epsilon)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.That(actual[i], Is.EqualTo(expected[i]).Within(tolerance), $"float[{i}]");
            }
        }

        private static void AssertVectorEqual(
            Vector3 expected,
            Vector3 actual,
            float tolerance = Epsilon,
            string label = null)
        {
            Assert.That((actual - expected).sqrMagnitude, Is.LessThanOrEqualTo(tolerance * tolerance),
                $"{label ?? "vector"}: expected {expected}, actual {actual}");
        }

        private static string CaptureSemanticJson(
            LatticeDeformer deformer,
            bool includeVersionMarkers = true)
        {
            var payload = new SemanticPayload
            {
                release = includeVersionMarkers ? (int)deformer.SerializedDeformationDataVersion : 0,
                sourceRelease = includeVersionMarkers
                    ? (int)GetField<DeformationDataVersion>(deformer, "_deformationDataSourceVersion")
                    : 0,
                legacyAbsoluteEvaluation = includeVersionMarkers && GetField<bool>(
                    deformer,
                    "_legacyAbsoluteLatticeEvaluation"),
                layerModelVersion = includeVersionMarkers
                    ? GetField<int>(deformer, "_layerModelVersion")
                    : 0,
                legacySettings = GetField<LatticeAsset>(deformer, "_settings"),
                flatLayers = GetField<List<LatticeLayer>>(deformer, "_layers"),
                activeFlatLayer = GetField<int>(deformer, "_activeLayerIndex"),
                legacyBlendShapeOutput = GetField<BlendShapeOutputMode>(deformer, "_blendShapeOutput"),
                legacyBlendShapeName = GetField<string>(deformer, "_blendShapeName"),
                legacyBlendShapeCurve = GetField<AnimationCurve>(deformer, "_blendShapeCurve"),
                groups = GetField<List<DeformerGroup>>(deformer, "_groups"),
                activeGroup = GetField<int>(deformer, "_activeGroupIndex"),
            };
            return JsonUtility.ToJson(payload);
        }

        private static void SetSerializedRelease(LatticeDeformer deformer, DeformationDataVersion release)
        {
            var field = FindField(
                typeof(LatticeDeformer),
                "_deformationDataVersion",
                "_serializedDeformationDataVersion");
            Assert.That(field, Is.Not.Null, "The serialized deformation release field was not found.");
            field.SetValue(deformer, release);
            SetField(
                deformer,
                "_deformationDataSourceVersion",
                DeformationDataVersion.Unversioned);
            SetField(deformer, "_legacyAbsoluteLatticeEvaluation", false);
        }

        private static FieldInfo FindField(Type type, params string[] names)
        {
            foreach (string name in names)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field != null)
                {
                    return field;
                }
            }

            return null;
        }

        private static T GetField<T>(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, $"Field not found: {target.GetType().Name}.{name}");
            return (T)field.GetValue(target);
        }

        private static void SetField<T>(object target, string name, T value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, $"Field not found: {target.GetType().Name}.{name}");
            field.SetValue(target, value);
        }

        [Serializable]
        private sealed class SemanticPayload
        {
            public int release;
            public int sourceRelease;
            public bool legacyAbsoluteEvaluation;
            public int layerModelVersion;
            public LatticeAsset legacySettings;
            public List<LatticeLayer> flatLayers;
            public int activeFlatLayer;
            public BlendShapeOutputMode legacyBlendShapeOutput;
            public string legacyBlendShapeName;
            public AnimationCurve legacyBlendShapeCurve;
            public List<DeformerGroup> groups;
            public int activeGroup;
        }

        private sealed class HistoricalFixture : IDisposable
        {
            public GameObject Root { get; }
            public LatticeDeformer Deformer { get; }

            public HistoricalFixture(GameObject root, LatticeDeformer deformer)
            {
                Root = root;
                Deformer = deformer;
            }

            public void Dispose()
            {
                if (Root != null)
                {
                    Object.DestroyImmediate(Root);
                }
            }
        }
    }
}
#endif
