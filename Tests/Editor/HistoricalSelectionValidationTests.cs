#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    /// <summary>
    /// Corruption-focused coverage kept separate from the established staged migration
    /// contract. Only the exact one-past selection produced by the published RemoveLayer
    /// bug is repaired inside its release step; every other invalid raw selection remains
    /// fail-closed instead of being silently clamped across later boundaries.
    /// </summary>
    public sealed class HistoricalSelectionValidationTests
    {
        public enum SelectionCorruption
        {
            ActiveGroupOutOfRange,
            ActiveGroupSelectsNull,
            ActiveLayerOutOfRange,
            ActiveLayerNegative,
            ActiveLayerSelectsNull,
            NonSelectedNullGroup,
            NonSelectedNullLayer,
            NonSelectedNullFlatLayer,
        }

        public enum NullListCorruption
        {
            TopLevelGroups,
            TopLevelFlatLayers,
            GroupLayers,
        }

        private static IEnumerable<TestCaseData> PublishedGroupCases()
        {
            var releases = new[]
            {
                DeformationDataVersion.V1_2_1,
                DeformationDataVersion.V1_3_0,
                DeformationDataVersion.V1_3_1,
                DeformationDataVersion.V1_4_0,
                DeformationDataVersion.CurrentDevelopment,
            };

            foreach (var release in releases)
            {
                foreach (SelectionCorruption corruption in Enum.GetValues(typeof(SelectionCorruption)))
                {
                    yield return new TestCaseData(release, corruption)
                        .SetName($"{release}_{corruption}_FailsClosedWithoutMutation");
                }
            }
        }

        private static IEnumerable<TestCaseData> NullListCases()
        {
            var releases = new[]
            {
                DeformationDataVersion.V1_2_1,
                DeformationDataVersion.V1_3_0,
                DeformationDataVersion.V1_3_1,
                DeformationDataVersion.V1_4_0,
                DeformationDataVersion.CurrentDevelopment,
            };

            foreach (var release in releases)
            {
                foreach (NullListCorruption corruption in Enum.GetValues(typeof(NullListCorruption)))
                {
                    yield return new TestCaseData(release, corruption)
                        .SetName($"{release}_{corruption}_FailsClosedWithoutMutation");
                }
            }
        }

        [TestCaseSource(nameof(PublishedGroupCases))]
        public void PublishedGroupSelectionCorruption_FailsClosedWithoutMutation(
            DeformationDataVersion release,
            SelectionCorruption corruption)
        {
            var source = CreateSourceMesh();
            var root = new GameObject($"{release} {corruption}");
            root.SetActive(false);
            try
            {
                var filter = root.AddComponent<MeshFilter>();
                filter.sharedMesh = source;
                root.AddComponent<MeshRenderer>();
                var deformer = root.AddComponent<LatticeDeformer>();
                deformer.enabled = false;

                var selectedGroup = CreateValidGroup();
                var groups = new List<DeformerGroup> { selectedGroup };
                var flatLayers = new List<LatticeLayer>();
                int activeGroup = 0;
                int activeFlatLayer = 0;
                switch (corruption)
                {
                    case SelectionCorruption.ActiveGroupOutOfRange:
                        activeGroup = 3;
                        break;
                    case SelectionCorruption.ActiveGroupSelectsNull:
                        groups.Add(null);
                        activeGroup = 1;
                        break;
                    case SelectionCorruption.ActiveLayerOutOfRange:
                        SetField(selectedGroup, "_activeLayerIndex", 3);
                        break;
                    case SelectionCorruption.ActiveLayerNegative:
                        SetField(selectedGroup, "_activeLayerIndex", -1);
                        break;
                    case SelectionCorruption.ActiveLayerSelectsNull:
                        selectedGroup.LayersList.Add(null);
                        SetField(selectedGroup, "_activeLayerIndex", 1);
                        break;
                    case SelectionCorruption.NonSelectedNullGroup:
                        groups.Add(null);
                        break;
                    case SelectionCorruption.NonSelectedNullLayer:
                        selectedGroup.LayersList.Add(null);
                        break;
                    case SelectionCorruption.NonSelectedNullFlatLayer:
                        flatLayers.Add(selectedGroup.LayersList[0]);
                        flatLayers.Add(null);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(corruption), corruption, null);
                }

                SetField(deformer, "_deformationDataVersion", release);
                SetField(deformer, "_deformationDataSourceVersion", DeformationDataVersion.Unversioned);
                var selectedLayers = selectedGroup.LayersList;
                int selectedRawActiveLayer = GetField<int>(selectedGroup, "_activeLayerIndex");
                SetField(deformer, "_groups", groups);
                SetField(deformer, "_activeGroupIndex", activeGroup);
                SetField(deformer, "_layers", flatLayers);
                SetField(deformer, "_activeLayerIndex", activeFlatLayer);
                SetField(deformer, "_layerModelVersion", 3);
                SetField(deformer, "_meshFilter", filter);
                SetField<Mesh>(deformer, "_serializedSourceMesh", source);

                Assert.That(deformer.TryUpgradeDeformationDataOneRelease(), Is.False);

                Assert.That(deformer.SerializedDeformationDataVersion, Is.EqualTo(release));
                Assert.That(deformer.SourceDeformationDataVersion, Is.EqualTo(release));
                Assert.That(deformer.MigrationStatus, Is.EqualTo(DeformationDataMigrationStatus.InvalidData));
                Assert.That(GetField<DeformationDataVersion>(deformer, "_deformationDataSourceVersion"),
                    Is.EqualTo(DeformationDataVersion.Unversioned));
                Assert.That(GetField<List<DeformerGroup>>(deformer, "_groups"), Is.SameAs(groups));
                Assert.That(GetField<int>(deformer, "_activeGroupIndex"), Is.EqualTo(activeGroup));
                Assert.That(GetField<List<LatticeLayer>>(deformer, "_layers"), Is.SameAs(flatLayers));
                Assert.That(GetField<int>(deformer, "_activeLayerIndex"), Is.EqualTo(activeFlatLayer));
                Assert.That(selectedGroup.LayersList, Is.SameAs(selectedLayers));
                Assert.That(GetField<int>(selectedGroup, "_activeLayerIndex"),
                    Is.EqualTo(selectedRawActiveLayer));
                if (corruption == SelectionCorruption.ActiveGroupSelectsNull)
                {
                    Assert.That(groups[activeGroup], Is.Null);
                }
                if (corruption == SelectionCorruption.ActiveLayerSelectsNull)
                {
                    Assert.That(selectedLayers[selectedRawActiveLayer], Is.Null);
                }
                if (corruption == SelectionCorruption.NonSelectedNullGroup)
                {
                    Assert.That(groups[1], Is.Null);
                }
                if (corruption == SelectionCorruption.NonSelectedNullLayer)
                {
                    Assert.That(selectedLayers[1], Is.Null);
                }
                if (corruption == SelectionCorruption.NonSelectedNullFlatLayer)
                {
                    Assert.That(flatLayers[1], Is.Null);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(source);
            }
        }

        [TestCaseSource(nameof(NullListCases))]
        public void GroupSchemaNullList_FailsClosedWithoutMutation(
            DeformationDataVersion release,
            NullListCorruption corruption)
        {
            var source = CreateSourceMesh();
            var root = new GameObject($"{release} {corruption}");
            root.SetActive(false);
            try
            {
                var filter = root.AddComponent<MeshFilter>();
                filter.sharedMesh = source;
                root.AddComponent<MeshRenderer>();
                var deformer = root.AddComponent<LatticeDeformer>();
                deformer.enabled = false;

                var group = CreateValidGroup();
                var groups = new List<DeformerGroup> { group };
                var flatLayers = new List<LatticeLayer>();
                SetField(deformer, "_deformationDataVersion", release);
                SetField(deformer, "_deformationDataSourceVersion", DeformationDataVersion.Unversioned);
                SetField(deformer, "_groups", groups);
                SetField(deformer, "_activeGroupIndex", 0);
                SetField(deformer, "_layers", flatLayers);
                SetField(deformer, "_activeLayerIndex", 0);
                SetField(deformer, "_layerModelVersion", 3);
                SetField(deformer, "_meshFilter", filter);
                SetField<Mesh>(deformer, "_serializedSourceMesh", source);

                switch (corruption)
                {
                    case NullListCorruption.TopLevelGroups:
                        SetField<List<DeformerGroup>>(deformer, "_groups", null);
                        break;
                    case NullListCorruption.TopLevelFlatLayers:
                        SetField<List<LatticeLayer>>(deformer, "_layers", null);
                        break;
                    case NullListCorruption.GroupLayers:
                        SetField<List<LatticeLayer>>(group, "_layers", null);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(corruption), corruption, null);
                }

                Assert.That(deformer.TryUpgradeDeformationDataOneRelease(), Is.False);

                Assert.That(deformer.SerializedDeformationDataVersion, Is.EqualTo(release));
                Assert.That(GetField<DeformationDataVersion>(deformer, "_deformationDataSourceVersion"),
                    Is.EqualTo(DeformationDataVersion.Unversioned));
                Assert.That(deformer.MigrationStatus, Is.EqualTo(DeformationDataMigrationStatus.InvalidData));
                if (corruption == NullListCorruption.TopLevelGroups)
                {
                    Assert.That(GetField<List<DeformerGroup>>(deformer, "_groups"), Is.Null);
                }
                else
                {
                    Assert.That(GetField<List<DeformerGroup>>(deformer, "_groups"), Is.SameAs(groups));
                }

                if (corruption == NullListCorruption.TopLevelFlatLayers)
                {
                    Assert.That(GetField<List<LatticeLayer>>(deformer, "_layers"), Is.Null);
                }
                else
                {
                    Assert.That(GetField<List<LatticeLayer>>(deformer, "_layers"), Is.SameAs(flatLayers));
                }

                if (corruption == NullListCorruption.GroupLayers)
                {
                    Assert.That(GetField<List<LatticeLayer>>(group, "_layers"), Is.Null);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void CurrentExplicitEmptyGroupLayerList_RemainsValidAndDeforms()
        {
            var source = CreateSourceMesh();
            var root = new GameObject("Explicit empty group layer list");
            root.SetActive(false);
            try
            {
                var filter = root.AddComponent<MeshFilter>();
                filter.sharedMesh = source;
                root.AddComponent<MeshRenderer>();
                var deformer = root.AddComponent<LatticeDeformer>();
                deformer.enabled = false;

                var group = new DeformerGroup { Name = "Intentionally Empty" };
                var emptyLayers = group.LayersList;
                var groups = new List<DeformerGroup> { group };
                SetField(deformer, "_deformationDataVersion", DeformationDataVersion.CurrentDevelopment);
                SetField(deformer, "_deformationDataSourceVersion", DeformationDataVersion.CurrentDevelopment);
                SetField(deformer, "_groups", groups);
                SetField(deformer, "_activeGroupIndex", 0);
                SetField(deformer, "_layers", new List<LatticeLayer>());
                SetField(deformer, "_activeLayerIndex", 0);
                SetField(deformer, "_layerModelVersion", 3);
                SetField(deformer, "_meshFilter", filter);
                SetField<Mesh>(deformer, "_serializedSourceMesh", source);

                Assert.That(deformer.TryUpgradeDeformationDataOneRelease(), Is.False);
                Assert.That(deformer.MigrationStatus, Is.EqualTo(DeformationDataMigrationStatus.Ready));
                Assert.That(GetField<List<LatticeLayer>>(group, "_layers"), Is.SameAs(emptyLayers));
                Assert.That(emptyLayers, Is.Empty);
                var output = deformer.Deform(false);
                Assert.That(output, Is.Not.Null);
                Assert.That(output.vertices, Is.EqualTo(source.vertices));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void UnversionedPublishedGroupWithEmptyFlatList_CanonicalizesOnlyAtStructuralBoundary()
        {
            var source = CreateSourceMesh();
            var root = new GameObject("Published group with stale flat selection");
            root.SetActive(false);
            try
            {
                var filter = root.AddComponent<MeshFilter>();
                filter.sharedMesh = source;
                root.AddComponent<MeshRenderer>();
                var deformer = root.AddComponent<LatticeDeformer>();
                deformer.enabled = false;

                var group = CreateValidGroup();
                group.LayersList.Add(CreateValidGroup().LayersList[0]);
                group.ActiveLayerIndex = 1;
                var groups = new List<DeformerGroup> { group };
                var emptyFlatLayers = new List<LatticeLayer>();
                SetField(deformer, "_deformationDataVersion", DeformationDataVersion.Unversioned);
                SetField(deformer, "_deformationDataSourceVersion", DeformationDataVersion.Unversioned);
                SetField(deformer, "_groups", groups);
                SetField(deformer, "_activeGroupIndex", 0);
                SetField(deformer, "_layers", emptyFlatLayers);
                SetField(deformer, "_activeLayerIndex", 2);
                SetField(deformer, "_layerModelVersion", 2);
                SetField(deformer, "_meshFilter", filter);
                SetField<Mesh>(deformer, "_serializedSourceMesh", source);

                Assert.That(deformer.TryUpgradeDeformationDataOneRelease(), Is.True);
                Assert.That(deformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.V1_2_1));
                Assert.That(GetField<int>(deformer, "_activeLayerIndex"), Is.EqualTo(2),
                    "Classification must not mutate the known stale facade field.");
                Assert.That(group.ActiveLayerIndex, Is.EqualTo(1));

                Assert.That(deformer.TryUpgradeDeformationDataOneRelease(), Is.True);
                Assert.That(deformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.V1_3_0));
                Assert.That(GetField<int>(deformer, "_activeLayerIndex"), Is.Zero,
                    "The structural group boundary must canonicalize the obsolete facade index.");
                Assert.That(deformer.Groups[0], Is.SameAs(group));
                Assert.That(deformer.Groups[0].ActiveLayerIndex, Is.EqualTo(1),
                    "The authoritative group's real selection must be preserved.");
                Assert.That(deformer.Deform(false), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(source);
            }
        }

        [TestCase(DeformationDataVersion.V1_2_1)]
        [TestCase(DeformationDataVersion.V1_3_0)]
        [TestCase(DeformationDataVersion.V1_3_1)]
        [TestCase(DeformationDataVersion.V1_4_0)]
        public void PublishedRemoveActiveLastPattern_CanonicalizesAtNextReleaseBoundary(
            DeformationDataVersion release)
        {
            var source = CreateSourceMesh();
            var root = new GameObject($"{release} published RemoveLayer pattern");
            root.SetActive(false);
            try
            {
                var filter = root.AddComponent<MeshFilter>();
                filter.sharedMesh = source;
                root.AddComponent<MeshRenderer>();
                var deformer = root.AddComponent<LatticeDeformer>();
                deformer.enabled = false;

                var group = CreateValidGroup();
                group.LayersList.Add(CreateValidGroup().LayersList[0]);
                var layers = group.LayersList;
                SetField(group, "_activeLayerIndex", layers.Count);
                SetField(deformer, "_deformationDataVersion", release);
                SetField(deformer, "_deformationDataSourceVersion", DeformationDataVersion.Unversioned);
                SetField(deformer, "_groups", new List<DeformerGroup> { group });
                SetField(deformer, "_activeGroupIndex", 0);
                SetField(deformer, "_layers", new List<LatticeLayer>());
                SetField(deformer, "_activeLayerIndex", 0);
                SetField(deformer, "_layerModelVersion", 3);
                SetField(deformer, "_meshFilter", filter);
                SetField<Mesh>(deformer, "_serializedSourceMesh", source);

                Assert.That(deformer.TryUpgradeDeformationDataOneRelease(), Is.True);

                Assert.That(deformer.SerializedDeformationDataVersion,
                    Is.EqualTo((DeformationDataVersion)((int)release + 1)));
                Assert.That(GetField<List<LatticeLayer>>(group, "_layers"), Is.SameAs(layers));
                Assert.That(layers, Has.Count.EqualTo(2));
                Assert.That(GetField<int>(group, "_activeLayerIndex"), Is.EqualTo(layers.Count - 1));
                Assert.That(group.Layers[0].Name, Is.EqualTo("Published Lattice"));
                Assert.That(group.Layers[1].Name, Is.EqualTo("Published Lattice"));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void UnversionedPublishedRemoveActiveLastPattern_ClassifiesWithoutMutationThenCanonicalizes()
        {
            var source = CreateSourceMesh();
            var root = new GameObject("Unversioned published RemoveLayer pattern");
            root.SetActive(false);
            try
            {
                var filter = root.AddComponent<MeshFilter>();
                filter.sharedMesh = source;
                root.AddComponent<MeshRenderer>();
                var deformer = root.AddComponent<LatticeDeformer>();
                deformer.enabled = false;

                var group = CreateValidGroup();
                group.LayersList.Add(CreateValidGroup().LayersList[0]);
                int onePastLast = group.LayersList.Count;
                SetField(group, "_activeLayerIndex", onePastLast);
                SetField(deformer, "_deformationDataVersion", DeformationDataVersion.Unversioned);
                SetField(deformer, "_deformationDataSourceVersion", DeformationDataVersion.Unversioned);
                SetField(deformer, "_groups", new List<DeformerGroup> { group });
                SetField(deformer, "_activeGroupIndex", 0);
                SetField(deformer, "_layers", new List<LatticeLayer>());
                SetField(deformer, "_activeLayerIndex", 0);
                SetField(deformer, "_layerModelVersion", 2);
                SetField(deformer, "_meshFilter", filter);
                SetField<Mesh>(deformer, "_serializedSourceMesh", source);

                Assert.That(deformer.TryUpgradeDeformationDataOneRelease(), Is.True);
                Assert.That(deformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.V1_2_1));
                Assert.That(GetField<int>(group, "_activeLayerIndex"), Is.EqualTo(onePastLast));

                Assert.That(deformer.TryUpgradeDeformationDataOneRelease(), Is.True);
                Assert.That(deformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.V1_3_0));
                Assert.That(GetField<int>(group, "_activeLayerIndex"),
                    Is.EqualTo(group.Layers.Count - 1));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void CurrentPublishedSource_OnePastActiveLayerFailsClosedOutsideReleaseStep()
        {
            var source = CreateSourceMesh();
            var root = new GameObject("Current published RemoveLayer recovery");
            root.SetActive(false);
            try
            {
                var filter = root.AddComponent<MeshFilter>();
                filter.sharedMesh = source;
                root.AddComponent<MeshRenderer>();
                var deformer = root.AddComponent<LatticeDeformer>();
                deformer.enabled = false;

                var group = CreateValidGroup();
                group.LayersList.Add(CreateValidGroup().LayersList[0]);
                SetField(group, "_activeLayerIndex", group.LayersList.Count);
                SetField(deformer, "_deformationDataVersion", DeformationDataVersion.CurrentDevelopment);
                SetField(deformer, "_deformationDataSourceVersion", DeformationDataVersion.V1_2_1);
                SetField(deformer, "_groups", new List<DeformerGroup> { group });
                SetField(deformer, "_activeGroupIndex", 0);
                SetField(deformer, "_layers", new List<LatticeLayer>());
                SetField(deformer, "_activeLayerIndex", 0);
                SetField(deformer, "_layerModelVersion", 3);
                SetField(deformer, "_meshFilter", filter);
                SetField<Mesh>(deformer, "_serializedSourceMesh", source);

                Assert.That(deformer.TryUpgradeDeformationDataOneRelease(), Is.False);
                Assert.That(GetField<int>(group, "_activeLayerIndex"),
                    Is.EqualTo(group.Layers.Count));
                Assert.That(deformer.MigrationStatus, Is.EqualTo(DeformationDataMigrationStatus.InvalidData));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void CurrentNativeSource_OnePastActiveLayerStillFailsClosed()
        {
            var source = CreateSourceMesh();
            var root = new GameObject("Current native invalid one-past selection");
            root.SetActive(false);
            try
            {
                var filter = root.AddComponent<MeshFilter>();
                filter.sharedMesh = source;
                root.AddComponent<MeshRenderer>();
                var deformer = root.AddComponent<LatticeDeformer>();
                deformer.enabled = false;

                var group = CreateValidGroup();
                SetField(group, "_activeLayerIndex", group.LayersList.Count);
                SetField(deformer, "_deformationDataVersion", DeformationDataVersion.CurrentDevelopment);
                SetField(deformer, "_deformationDataSourceVersion", DeformationDataVersion.CurrentDevelopment);
                SetField(deformer, "_groups", new List<DeformerGroup> { group });
                SetField(deformer, "_activeGroupIndex", 0);
                SetField(deformer, "_layers", new List<LatticeLayer>());
                SetField(deformer, "_activeLayerIndex", 0);
                SetField(deformer, "_layerModelVersion", 3);
                SetField(deformer, "_meshFilter", filter);
                SetField<Mesh>(deformer, "_serializedSourceMesh", source);

                Assert.That(deformer.TryUpgradeDeformationDataOneRelease(), Is.False);
                Assert.That(deformer.MigrationStatus, Is.EqualTo(DeformationDataMigrationStatus.InvalidData));
                Assert.That(GetField<int>(group, "_activeLayerIndex"),
                    Is.EqualTo(group.Layers.Count));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void CurrentRemoveActiveLastLayer_UpdatesRawSelectionAndStillDeforms()
        {
            var source = CreateSourceMesh();
            var root = new GameObject("Remove active last layer");
            root.SetActive(false);
            try
            {
                var filter = root.AddComponent<MeshFilter>();
                filter.sharedMesh = source;
                root.AddComponent<MeshRenderer>();
                var deformer = root.AddComponent<LatticeDeformer>();
                deformer.enabled = false;

                int first = deformer.AddLayer("First stroke", MeshDeformerLayerType.Brush);
                int removed = deformer.AddLayer("Removed last stroke", MeshDeformerLayerType.Brush);
                Assert.That(removed, Is.GreaterThan(first));
                Assert.That(deformer.ActiveLayerIndex, Is.EqualTo(removed));

                Assert.That(deformer.RemoveLayer(removed), Is.True);

                var activeGroup = deformer.ActiveGroup;
                Assert.That(activeGroup, Is.Not.Null);
                Assert.That(GetField<int>(activeGroup, "_activeLayerIndex"), Is.EqualTo(first),
                    "Removing the selected last layer must update the serialized index, not only its clamped view.");
                Assert.That(deformer.ActiveLayerIndex, Is.EqualTo(first));
                Assert.That(deformer.Deform(false), Is.Not.Null,
                    "A normal editor operation must not create a payload rejected by migration preflight.");
                Assert.That(deformer.MigrationStatus, Is.EqualTo(DeformationDataMigrationStatus.Ready));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(source);
            }
        }

        private static DeformerGroup CreateValidGroup()
        {
            var settings = new LatticeAsset
            {
                GridSize = new Vector3Int(2, 2, 2),
                LocalBounds = new Bounds(Vector3.zero, Vector3.one * 2f),
            };
            settings.ResetControlPoints();

            var layer = new LatticeLayer
            {
                Name = "Published Lattice",
                Settings = settings,
            };
            var group = new DeformerGroup { Name = "Published Group" };
            group.LayersList.Add(layer);
            group.ActiveLayerIndex = 0;
            return group;
        }

        private static Mesh CreateSourceMesh()
        {
            var mesh = new Mesh { name = "SelectionValidationSource" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, 0f),
                new Vector3(0.5f, 0f, 0f),
                new Vector3(0f, 0.75f, 0f),
            };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void SetField<T>(object target, string fieldName, T value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing private field {target.GetType().Name}.{fieldName}.");
            field.SetValue(target, value);
        }

        private static T GetField<T>(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing private field {target.GetType().Name}.{fieldName}.");
            return (T)field.GetValue(target);
        }
    }
}
#endif
