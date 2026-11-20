using System.Collections.Generic;

namespace Net._32Ba.LatticeDeformationTool
{
    // Raw synchronous view of every legacy/current shape needed by migration
    // preflight. No component, Renderer, Mesh, getter normalization, or job access.
    internal readonly struct DeformationMigrationInput
    {
        internal readonly LatticeAsset Settings;
        internal readonly IReadOnlyList<LatticeLayer> FlatLayers;
        internal readonly IReadOnlyList<DeformerGroup> Groups;
        internal readonly int ActiveLayerIndex, ActiveGroupIndex, LayerModelVersion, ProfileGroupCount;
        internal readonly DeformationDataVersion Version;
        internal readonly BlendShapeOutputMode LegacyBlendShapeOutput;

        internal DeformationMigrationInput(LatticeAsset settings, IReadOnlyList<LatticeLayer> flatLayers,
            int activeLayerIndex, IReadOnlyList<DeformerGroup> groups, int activeGroupIndex,
            DeformationDataVersion version, int layerModelVersion, BlendShapeOutputMode blendShapeOutput,
            int profileGroupCount = 0)
        {
            Settings = settings;
            FlatLayers = flatLayers;
            Groups = groups;
            ActiveLayerIndex = activeLayerIndex;
            ActiveGroupIndex = activeGroupIndex;
            Version = version;
            LayerModelVersion = layerModelVersion;
            LegacyBlendShapeOutput = blendShapeOutput;
            ProfileGroupCount = profileGroupCount;
        }
    }

    internal static class DeformationMigrationPreflight
    {
        internal const int CurrentLayerModelVersion = 3;
        internal static DeformationDataMigrationStatus ValidateSchema(in DeformationMigrationInput data)
        {
            // Preserve the published priority of version, nested asset and raw
            // shape errors. Do not classify or repair any payload here.
            if ((int)data.Version > (int)DeformationDataVersion.CurrentDevelopment || data.LayerModelVersion > CurrentLayerModelVersion)
                return DeformationDataMigrationStatus.UnsupportedFutureVersion;
            if ((int)data.Version < (int)DeformationDataVersion.Unversioned)
                return DeformationDataMigrationStatus.InvalidData;
            if (HasUnsupportedFutureLatticeAsset(data))
                return DeformationDataMigrationStatus.UnsupportedFutureVersion;
            return HasMalformedLatticeAsset(data)
                ? DeformationDataMigrationStatus.InvalidData : DeformationDataMigrationStatus.Ready;
        }

        private static bool HasNonNullGroups(IReadOnlyList<DeformerGroup> groups)
        {
            if (groups == null) return false;
            for (int i = 0; i < groups.Count; i++)
                if (groups[i] != null) return true;
            return false;
        }

        internal static bool HasUnsupportedFutureLatticeAsset(in DeformationMigrationInput data)
        {
            if (data.Settings != null && data.Settings.HasUnsupportedFutureSerializationVersion)
            {
                return true;
            }

            if (data.FlatLayers != null)
            {
                for (int layerIndex = 0; layerIndex < data.FlatLayers.Count; layerIndex++)
                {
                    var layer = data.FlatLayers[layerIndex];
                    if (layer?.SerializedSettings != null &&
                        layer.SerializedSettings.HasUnsupportedFutureSerializationVersion)
                    {
                        return true;
                    }
                }
            }

            if (data.Groups != null)
            {
                for (int groupIndex = 0; groupIndex < data.Groups.Count; groupIndex++)
                {
                    var group = data.Groups[groupIndex];
                    var layers = group?.SerializedLayers;
                    if (layers == null) continue;
                    foreach (var layer in layers)
                    {
                        if (layer?.SerializedSettings != null &&
                            layer.SerializedSettings.HasUnsupportedFutureSerializationVersion)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        internal static bool HasMalformedLatticeAsset(in DeformationMigrationInput data)
        {
            if (HasMalformedSerializedSelection(data))
            {
                return true;
            }

            if (data.LegacyBlendShapeOutput != BlendShapeOutputMode.Disabled &&
                data.LegacyBlendShapeOutput != BlendShapeOutputMode.OutputAsBlendShape)
            {
                return true;
            }

            if (data.Settings != null && data.Settings.HasMalformedSerializedShape)
            {
                return true;
            }

            if (data.FlatLayers != null)
            {
                for (int layerIndex = 0; layerIndex < data.FlatLayers.Count; layerIndex++)
                {
                    var layer = data.FlatLayers[layerIndex];
                    if (layer != null &&
                        (layer.HasMalformedSerializedMetadata ||
                         (layer.SerializedSettings != null &&
                          layer.SerializedSettings.HasMalformedSerializedShape)))
                    {
                        return true;
                    }
                }
            }

            if (data.Groups != null)
            {
                for (int groupIndex = 0; groupIndex < data.Groups.Count; groupIndex++)
                {
                    var group = data.Groups[groupIndex];
                    if (group != null && group.HasMalformedSerializedMetadata)
                    {
                        return true;
                    }

                    var layers = group?.SerializedLayers;
                    if (layers == null) continue;
                    foreach (var layer in layers)
                    {
                        if (layer != null &&
                            (layer.HasMalformedSerializedMetadata ||
                             (layer.SerializedSettings != null &&
                              layer.SerializedSettings.HasMalformedSerializedShape)))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        internal static bool HasMalformedSerializedSelection(in DeformationMigrationInput data)
        {
            // Missing fields from old YAML retain these field-initializer lists. A
            // runtime null therefore represents an explicit/corrupt payload, and the
            // normalization paths below must not replace it with a guessed empty list.
            if (data.Groups == null || data.FlatLayers == null)
            {
                return true;
            }

            if (data.Groups.Count == 0)
            {
                int selectionCount = data.ProfileGroupCount;
                if (data.ActiveGroupIndex < 0 ||
                    (selectionCount == 0 ? data.ActiveGroupIndex != 0 : data.ActiveGroupIndex >= selectionCount))
                    return true;
            }
            else
            {
                if (data.ActiveGroupIndex < 0 || data.ActiveGroupIndex >= data.Groups.Count)
                {
                    return true;
                }

                for (int groupIndex = 0; groupIndex < data.Groups.Count; groupIndex++)
                {
                    var group = data.Groups[groupIndex];
                    // Group-schema releases never assigned semantics to a null inline
                    // entry. Dropping it or replacing it with a default group would be
                    // a guessed repair, even when that entry is not currently selected.
                    if (group == null)
                    {
                        return true;
                    }

                    var layers = group.SerializedLayers;
                    int activeLayer = group.SerializedActiveLayerIndex;
                    if (layers == null)
                    {
                        return true;
                    }

                    if (layers.Count == 0)
                    {
                        if (activeLayer != 0)
                        {
                            return true;
                        }
                    }
                    else
                    {
                        bool knownPublishedRemoveLastPattern =
                            CanContainPublishedRemoveLastSelectionBug(data) &&
                            activeLayer == layers.Count;
                        if (activeLayer < 0 ||
                            (activeLayer >= layers.Count && !knownPublishedRemoveLastPattern))
                        {
                            return true;
                        }

                        // As with groups, every inline layer slot must carry an actual
                        // payload. EnsureGroupsCore must not silently manufacture a
                        // neutral layer in place of corrupted serialized data.
                        for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
                        {
                            if (layers[layerIndex] == null)
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            if (data.FlatLayers.Count == 0)
            {
                // Published group initialization could leave the obsolete component
                // facade index behind after moving its selected flat layer into a
                // DeformerGroup. It has no target once the flat list is empty; preserve
                // it through classification, then canonicalize it at the structural
                // 1.2.1→1.3.0 boundary. Later/current payloads must already be canonical.
                bool awaitingPublishedGroupNormalization = data.Groups.Count > 0 &&
                    (data.Version == DeformationDataVersion.Unversioned ||
                     data.Version == DeformationDataVersion.V1_2_0 ||
                     data.Version == DeformationDataVersion.V1_2_1);
                if (awaitingPublishedGroupNormalization)
                {
                    return false;
                }

                // The single-settings schema used both the default zero and -1 as the
                // base-lattice selection sentinel before a flat list existed.
                return data.ActiveLayerIndex < -1 || data.ActiveLayerIndex > 0;
            }

            // A conceptual-v2 flat payload could historically contain null holes; the
            // immutable staged migration contract deterministically filters those while
            // remapping a non-null active layer. Once authoritative groups exist, the
            // same null is corruption in the stale backup and must fail closed.
            if (data.Groups != null && data.Groups.Count > 0)
            {
                for (int layerIndex = 0; layerIndex < data.FlatLayers.Count; layerIndex++)
                {
                    if (data.FlatLayers[layerIndex] == null)
                    {
                        return true;
                    }
                }
            }

            return data.ActiveLayerIndex < 0 || data.ActiveLayerIndex >= data.FlatLayers.Count ||
                   data.FlatLayers[data.ActiveLayerIndex] == null;
        }

        internal static bool CanContainPublishedRemoveLastSelectionBug(in DeformationMigrationInput data)
        {
            if (data.Version == DeformationDataVersion.Unversioned)
            {
                return HasNonNullGroups(data.Groups);
            }

            return data.Version >= DeformationDataVersion.V1_2_1 &&
                   data.Version <= DeformationDataVersion.V1_4_0;
        }

        internal static bool HasIncompatibleVertexData(in DeformationMigrationInput data, int expectedVertexCount)
        {
            bool IsIncompatible(LatticeLayer layer)
            {
                if (layer == null) return false;
                if (layer.HasNonFiniteSerializedVertexData) return true;

                int displacementCount = layer.SerializedBrushDisplacementCount;
                int maskCount = layer.SerializedVertexMaskCount;
                if (displacementCount == 0 && maskCount == 0)
                {
                    return false;
                }

                if (expectedVertexCount < 0)
                {
                    // Vertex identity cannot be established without the source mesh.
                    // Preserve the payload and allow shape-only migration; it will be
                    // validated as soon as a source becomes known.
                    return false;
                }

                return (displacementCount != 0 && displacementCount != expectedVertexCount) ||
                       (maskCount != 0 && maskCount != expectedVertexCount);
            }

            if (data.FlatLayers != null)
            {
                for (int layerIndex = 0; layerIndex < data.FlatLayers.Count; layerIndex++)
                {
                    var layer = data.FlatLayers[layerIndex];
                    if (IsIncompatible(layer)) return true;
                }
            }

            if (data.Groups != null)
            {
                for (int groupIndex = 0; groupIndex < data.Groups.Count; groupIndex++)
                {
                    var group = data.Groups[groupIndex];
                    var layers = group?.SerializedLayers;
                    if (layers == null) continue;
                    foreach (var layer in layers)
                    {
                        if (IsIncompatible(layer)) return true;
                    }
                }
            }

            return false;
        }
    }
}
