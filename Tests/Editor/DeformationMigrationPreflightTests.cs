#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class DeformationMigrationPreflightTests
    {
        [Test]
        public void KnownPublishedSelection_IsAcceptedWithoutCanonicalizingItsRawIndex()
        {
            var group = new DeformerGroup();
            group.LayersList.Add(new LatticeLayer());
            group.SetSerializedActiveLayerIndex(1);
            var groups = new List<DeformerGroup> { group };
            foreach (var version in new[] { DeformationDataVersion.Unversioned,
                DeformationDataVersion.V1_2_1, DeformationDataVersion.V1_3_0,
                DeformationDataVersion.V1_3_1, DeformationDataVersion.V1_4_0 })
            {
                var input = Input(groups, new List<LatticeLayer>(), version);
                Assert.That(DeformationMigrationPreflight.ValidateSchema(input),
                    Is.EqualTo(DeformationDataMigrationStatus.Ready), version.ToString());
                Assert.That(group.SerializedActiveLayerIndex, Is.EqualTo(1));
            }
            Assert.That(DeformationMigrationPreflight.ValidateSchema(
                Input(groups, new List<LatticeLayer>(), DeformationDataVersion.CurrentDevelopment)),
                Is.EqualTo(DeformationDataMigrationStatus.InvalidData));
            Assert.That(group.SerializedActiveLayerIndex, Is.EqualTo(1));
        }

        [Test]
        public void RawNullSlots_KeepDifferentFlatAndGroupContractsWithoutRepair()
        {
            var flat = new List<LatticeLayer> { new LatticeLayer(), null, new LatticeLayer() };
            var groups = new List<DeformerGroup>();
            var input = Input(groups, flat, DeformationDataVersion.V1_2_0, activeLayer: 2);
            Assert.That(DeformationMigrationPreflight.ValidateSchema(input), Is.EqualTo(DeformationDataMigrationStatus.Ready));
            Assert.That(flat.Count, Is.EqualTo(3));
            Assert.That(flat[1], Is.Null);
            Assert.That(input.ActiveLayerIndex, Is.EqualTo(2));
            groups.Add(new DeformerGroup());
            Assert.That(DeformationMigrationPreflight.ValidateSchema(input), Is.EqualTo(DeformationDataMigrationStatus.InvalidData));
            flat.Clear();
            groups.Add(null);
            Assert.That(DeformationMigrationPreflight.ValidateSchema(Input(groups, flat, DeformationDataVersion.CurrentDevelopment)),
                Is.EqualTo(DeformationDataMigrationStatus.InvalidData));
            Assert.That(groups[1], Is.Null);
            Assert.That(groups.Count, Is.EqualTo(2));
        }

        [Test]
        public void NestedFutureVersion_PrecedesMalformedSelectionWithoutTouchingData()
        {
            var layer = new LatticeLayer();
            var settings = layer.SerializedSettings;
            typeof(LatticeAsset).GetField("_serializationVersion", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(settings, 999);
            var flat = new List<LatticeLayer> { layer };
            var input = Input(new List<DeformerGroup>(), flat, DeformationDataVersion.V1_2_0, activeLayer: 8);
            Assert.That(DeformationMigrationPreflight.ValidateSchema(input),
                Is.EqualTo(DeformationDataMigrationStatus.UnsupportedFutureVersion));
            Assert.That(input.ActiveLayerIndex, Is.EqualTo(8));
            Assert.That(layer.SerializedSettings, Is.SameAs(settings));
            Assert.That(settings.HasUnsupportedFutureSerializationVersion, Is.True);
        }

        [Test]
        public void UnknownSource_PreservesVertexPayloadAndStillRejectsNonFiniteData()
        {
            var layer = new LatticeLayer();
            layer.BrushDisplacements = new[] { Vector3.right, Vector3.up };
            var original = layer.BrushDisplacements;
            var input = Input(new List<DeformerGroup>(), new List<LatticeLayer> { layer }, DeformationDataVersion.V1_2_0);
            Assert.That(DeformationMigrationPreflight.HasIncompatibleVertexData(input, -1), Is.False);
            Assert.That(DeformationMigrationPreflight.HasIncompatibleVertexData(input, 3), Is.True);
            Assert.That(layer.BrushDisplacements, Is.SameAs(original));
            Assert.That(original, Is.EqualTo(new[] { Vector3.right, Vector3.up }));
            original[0] = new Vector3(float.NaN, 0f, 0f);
            Assert.That(DeformationMigrationPreflight.HasIncompatibleVertexData(input, -1), Is.True);
            Assert.That(float.IsNaN(original[0].x), Is.True);
            Assert.That(layer.BrushDisplacements, Is.SameAs(original));
        }

        [Test]
        public void ProfileSelection_UsesSuppliedCountWithoutAnAssetOrComponent()
        {
            var input = new DeformationMigrationInput(null, new List<LatticeLayer>(), 0,
                new List<DeformerGroup>(), 1, DeformationDataVersion.CurrentDevelopment, 3,
                BlendShapeOutputMode.Disabled, profileGroupCount: 2);
            Assert.That(DeformationMigrationPreflight.ValidateSchema(input), Is.EqualTo(DeformationDataMigrationStatus.Ready));
            Assert.That(input.Groups.Count, Is.Zero);
            Assert.That(input.ActiveGroupIndex, Is.EqualTo(1));
        }

        private static DeformationMigrationInput Input(List<DeformerGroup> groups, List<LatticeLayer> flat,
            DeformationDataVersion version, int activeLayer = 0) => new DeformationMigrationInput(
                null, flat, activeLayer, groups, 0, version, 3, BlendShapeOutputMode.Disabled);
    }
}
#endif
