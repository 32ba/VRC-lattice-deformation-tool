using System;
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    public partial class LatticeDeformer
    {
        private bool EnsureLayerModelReady()
        {
            if (_isEnsuringLayerModelReady)
            {
                return _deformationDataVersion == DeformationDataVersion.CurrentDevelopment &&
                       !_hasIncompatibleBrushData;
            }

            int rawVersion = (int)_deformationDataVersion;
            if (rawVersion > (int)DeformationDataVersion.CurrentDevelopment)
            {
                _migrationStatus = DeformationDataMigrationStatus.UnsupportedFutureVersion;
                return false;
            }

            if (_layerModelVersion > k_CurrentLayerModelVersion)
            {
                _migrationStatus = DeformationDataMigrationStatus.UnsupportedFutureVersion;
                return false;
            }

            if (rawVersion < (int)DeformationDataVersion.Unversioned)
            {
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return false;
            }

            if (HasUnsupportedFutureLatticeAsset())
            {
                _migrationStatus = DeformationDataMigrationStatus.UnsupportedFutureVersion;
                return false;
            }

            if (HasMalformedLatticeAsset())
            {
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return false;
            }

            if (HasIncompatibleSerializedVertexIndexedData())
            {
                _hasIncompatibleBrushData = true;
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return false;
            }

            _isEnsuringLayerModelReady = true;
            try
            {
                RecoverStaleCurrentStructureVersionIfNeeded();

                while (_deformationDataVersion != DeformationDataVersion.CurrentDevelopment)
                {
                    if (!TryUpgradeDeformationDataOneRelease())
                    {
                        return false;
                    }
                }

                EnsureSettings();
                if (_layers == null) _layers = new List<LatticeLayer>();
                if (_groups == null) _groups = new List<DeformerGroup>();

                EnsureGroupsCore();
                CacheSourceMesh();
                TryAutoConfigureSettings();

                _migrationStatus = _hasIncompatibleBrushData
                    ? DeformationDataMigrationStatus.InvalidData
                    : DeformationDataMigrationStatus.Ready;
                return !_hasIncompatibleBrushData;
            }
            finally
            {
                _isEnsuringLayerModelReady = false;
            }
        }

        private void RecoverStaleCurrentStructureVersionIfNeeded()
        {
            if (_deformationDataVersion != DeformationDataVersion.CurrentDevelopment ||
                _layerModelVersion >= k_CurrentLayerModelVersion ||
                HasNonNullGroups(_groups) ||
                (!HasNonNullLayers(_layers) && !HasMeaningfulBaseSettings()))
            {
                return;
            }

            // A current release marker paired with only an older serialized shape can
            // result from an interrupted save or an Inspector-first partial migration.
            // Recover the older shape instead of creating a default group over it.
            _deformationDataVersion = _settings != null && _settings.HasPendingLegacyWorldSpace
                ? DeformationDataVersion.V0_0_1
                : DeformationDataVersion.V1_2_0;
            _deformationDataSourceVersion = _deformationDataVersion;
            _migrationStatus = DeformationDataMigrationStatus.InProgress;
            MarkMigrationCommitted();
        }

        /// <summary>
        /// Advances exactly one published release boundary. Unversioned data is first
        /// classified by its oldest unambiguous serialized shape; no release-specific
        /// mutation occurs until the following call. A failed step never advances the
        /// version and must leave its source payload intact.
        /// </summary>
        internal bool TryUpgradeDeformationDataOneRelease()
        {
            int rawVersion = (int)_deformationDataVersion;
            if (rawVersion > (int)DeformationDataVersion.CurrentDevelopment)
            {
                _migrationStatus = DeformationDataMigrationStatus.UnsupportedFutureVersion;
                return false;
            }

            if (_layerModelVersion > k_CurrentLayerModelVersion)
            {
                _migrationStatus = DeformationDataMigrationStatus.UnsupportedFutureVersion;
                return false;
            }

            if (rawVersion < (int)DeformationDataVersion.Unversioned)
            {
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return false;
            }

            if (HasUnsupportedFutureLatticeAsset())
            {
                _migrationStatus = DeformationDataMigrationStatus.UnsupportedFutureVersion;
                return false;
            }

            if (HasMalformedLatticeAsset())
            {
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return false;
            }

            if (HasIncompatibleSerializedVertexIndexedData())
            {
                _hasIncompatibleBrushData = true;
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return false;
            }

            if (_deformationDataVersion == DeformationDataVersion.CurrentDevelopment)
            {
                _migrationStatus = _hasIncompatibleBrushData
                    ? DeformationDataMigrationStatus.InvalidData
                    : DeformationDataMigrationStatus.Ready;
                return false;
            }

            _migrationStatus = DeformationDataMigrationStatus.InProgress;
            switch (_deformationDataVersion)
            {
                case DeformationDataVersion.Unversioned:
                    return ClassifyUnversionedDeformationData();

                case DeformationDataVersion.V0_0_1:
                    return TryUpgradeV0_0_1ToV0_0_2();

                // These releases did not alter the serialized deformation payload.
                // They remain explicit so interrupted upgrades resume deterministically.
                case DeformationDataVersion.V0_0_2:
                    return CommitReleaseVersion(DeformationDataVersion.V0_0_3);
                case DeformationDataVersion.V0_0_3:
                    return CommitReleaseVersion(DeformationDataVersion.V0_0_4);
                case DeformationDataVersion.V0_0_4:
                    return CommitReleaseVersion(DeformationDataVersion.V0_0_5);
                case DeformationDataVersion.V0_0_5:
                    return CommitReleaseVersion(DeformationDataVersion.V0_0_6);
                case DeformationDataVersion.V0_0_6:
                    return CommitReleaseVersion(DeformationDataVersion.V1_0_0);
                case DeformationDataVersion.V1_0_0:
                    return CommitReleaseVersion(DeformationDataVersion.V1_0_1);
                case DeformationDataVersion.V1_0_1:
                    return CommitReleaseVersion(DeformationDataVersion.V1_1_0);
                case DeformationDataVersion.V1_1_0:
                    return CommitReleaseVersion(DeformationDataVersion.V1_2_0);

                case DeformationDataVersion.V1_2_0:
                    return TryUpgradeV1_2_0ToV1_2_1();

                case DeformationDataVersion.V1_2_1:
                    return TryUpgradeV1_2_1ToV1_3_0();
                case DeformationDataVersion.V1_3_0:
                    return TryNormalizePublishedGroupSelectionAndCommit(
                        DeformationDataVersion.V1_3_1);
                case DeformationDataVersion.V1_3_1:
                    return TryNormalizePublishedGroupSelectionAndCommit(
                        DeformationDataVersion.V1_4_0);

                case DeformationDataVersion.V1_4_0:
                    return TryUpgradeV1_4_0ToCurrent();

                // The serialized enum is contiguous; range guards reject every unknown value.
#line hidden
                default:
                    _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                    return false;
#line default
            }
        }

        private void NormalizeAuthoritativeGroupShapeVersion()
        {
            if (HasNonNullGroups(_groups) && !HasNonNullLayers(_layers) &&
                _layerModelVersion < k_CurrentLayerModelVersion)
            {
                _layerModelVersion = k_CurrentLayerModelVersion;
            }
        }

        private bool ClassifyUnversionedDeformationData()
        {
            DeformationDataVersion detected;
            bool hasGroups = HasNonNullGroups(_groups);
            bool hasFlatLayers = HasNonNullLayers(_layers);
            bool hasBaseSettings = HasMeaningfulBaseSettings();

            if (!hasGroups && !hasFlatLayers && !hasBaseSettings)
            {
                _layerModelVersion = k_CurrentLayerModelVersion;
                _legacyAbsoluteLatticeEvaluation = false;
                _deformationDataSourceVersion = DeformationDataVersion.CurrentDevelopment;
                return CommitReleaseVersion(DeformationDataVersion.CurrentDevelopment);
            }

            if (hasGroups)
            {
                // Serialized groups first shipped in 1.2.1. The published releases can
                // also contain an eagerly-created group beside a stale flat-layer copy
                // and conceptual-v2 marker; those are still 1.2.1 evidence.
                detected = DeformationDataVersion.V1_2_1;
            }
            else if (hasFlatLayers || _layerModelVersion > 0)
            {
                // Internal conceptual-v1/v2 builds are treated as the immediately
                // preceding public release and normalized in the 1.2.0→1.2.1 step.
                detected = DeformationDataVersion.V1_2_0;
            }
            else
            {
                // Single-settings payloads are intentionally classified at the oldest
                // compatible release. Only an intact _applySpace=1 marker identifies
                // 0.0.1 World data; marker-less 0.0.2+ data is never guessed as World.
                detected = DeformationDataVersion.V0_0_1;
            }

            _deformationDataSourceVersion = detected;
            _deformationDataVersion = detected;
            MarkMigrationCommitted();
            return true;
        }

        private bool TryUpgradeV0_0_1ToV0_0_2()
        {
            if (_settings == null)
            {
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return false;
            }

            if (_settings.HasInvalidLegacyApplySpace)
            {
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return false;
            }

            if (_settings.HasPendingLegacyWorldSpace)
            {
                Transform owner = MeshTransform;
                // A live MonoBehaviour always owns a Transform.
#line hidden
                if (owner == null)
                {
                    _migrationStatus = DeformationDataMigrationStatus.PendingOwnerTransform;
                    return false;
                }
#line default

                if (_settings.ControlPointsLocal.Length != _settings.ControlPointCount)
                {
                    _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                    return false;
                }

                // 0.0.1 evaluated World control points against the owner's transform
                // on every deformation. Validate now, but retain both raw points and
                // marker so later transform changes keep those exact semantics.
                if (!_settings.CanEvaluateLegacyWorldSpace(owner.worldToLocalMatrix))
                {
                    _migrationStatus = DeformationDataMigrationStatus.PendingOwnerTransform;
                    return false;
                }
            }

            return CommitReleaseVersion(DeformationDataVersion.V0_0_2);
        }

        private bool TryUpgradeV1_2_0ToV1_2_1()
        {
            // The structural helpers below use copy-on-write for the containing lists,
            // so retaining the original references is a complete rollback snapshot.
            var originalLayers = _layers;
            var originalGroups = _groups;
            int originalLayerVersion = _layerModelVersion;
            int originalActiveLayer = _activeLayerIndex;
            int originalActiveGroup = _activeGroupIndex;

            try
            {
                bool hasGroups = HasNonNullGroups(_groups);
                bool hasFlatLayers = HasNonNullLayers(_layers);

                if (hasGroups && !hasFlatLayers)
                {
                    // A partial save already contains the newest meaningful shape. Do
                    // not manufacture a duplicate layer from the facade _settings copy.
                    _layerModelVersion = k_CurrentLayerModelVersion;
                }
                else
                {
                    if (_layerModelVersion < 2)
                    {
                        TryMigrateLegacyBaseToLayerStructure();
                    }

                    TryMigrateLayersToGroupStructure();
                }

                if (_layerModelVersion != k_CurrentLayerModelVersion || !HasNonNullGroups(_groups))
                {
                    throw new InvalidOperationException("Layer/group migration did not produce the v3 structure.");
                }
            }
            catch (Exception)
            {
                _layers = originalLayers;
                _groups = originalGroups;
                _layerModelVersion = originalLayerVersion;
                _activeLayerIndex = originalActiveLayer;
                _activeGroupIndex = originalActiveGroup;
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return false;
            }

            return CommitReleaseVersion(DeformationDataVersion.V1_2_1);
        }

        private bool TryUpgradeV1_2_1ToV1_3_0()
        {
            // 1.2.1–1.4.0 could serialize authoritative groups together with a stale
            // flat-layer facade and _layerModelVersion=2. The old runtime ignored that
            // flat copy. Preserve it in a disabled recovery group so the payload remains
            // inspectable without changing deformation or BlendShape output.
            var originalLayers = _layers;
            var originalGroups = _groups;
            int originalLayerVersion = _layerModelVersion;
            int originalActiveLayer = _activeLayerIndex;
            int originalActiveGroup = _activeGroupIndex;
            DeformationDataVersion originalVersion = _deformationDataVersion;
            DeformationDataVersion originalSourceVersion = _deformationDataSourceVersion;
            bool originalPublishedBlendShapeSemantics = _legacyPublishedBlendShapeSemantics;
            List<GroupSelectionSnapshot> selectionSnapshots = null;

            try
            {
                if (!HasNonNullGroups(_groups))
                {
                    throw new InvalidOperationException("The 1.2.1 group payload is missing.");
                }
                bool preservePublishedBlendShapeSemantics =
                    ShouldPreserveHistoricalGroupBlendShapeSemantics();

                var migratedGroups = new List<DeformerGroup>(_groups);
                if (HasNonNullLayers(_layers))
                {
                    var migratedLayers = FilterLayersAndRemapActive(
                        _layers,
                        _activeLayerIndex,
                        out int migratedActiveLayer);
                    // HasNonNullLayers guarantees the filter retains at least one layer.
#line hidden
                    if (migratedLayers.Count == 0)
                    {
                        throw new InvalidOperationException("The legacy flat-layer payload could not be recovered.");
                    }
#line default

                    var recoveryGroup = new DeformerGroup
                    {
                        Name = k_RecoveredLegacyFlatLayersGroupName,
                        Enabled = false,
                        ActiveLayerIndex = migratedActiveLayer,
                        BlendShapeOutput = _blendShapeOutput,
                        BlendShapeName = _blendShapeName ?? "",
                        BlendShapeCurve = CloneCurve(_blendShapeCurve)
                    };
                    foreach (var layer in migratedLayers)
                    {
                        recoveryGroup.LayersList.Add(layer);
                    }
                    // ActiveLayerIndex clamps against the destination list, so restore
                    // it after the layers have been copied.
                    recoveryGroup.ActiveLayerIndex = migratedActiveLayer;
                    migratedGroups.Add(recoveryGroup);
                }

                _groups = migratedGroups;
                _layers = new List<LatticeLayer>();
                // The recovery group owns the preserved flat selection from this point.
                _activeLayerIndex = 0;
                _layerModelVersion = k_CurrentLayerModelVersion;
                // Existing groups are authoritative; keep the user's selected group.
                _activeGroupIndex = originalActiveGroup;
                if (preservePublishedBlendShapeSemantics)
                {
                    _legacyPublishedBlendShapeSemantics = true;
                }
                if (_activeGroupIndex < 0 || _activeGroupIndex >= _groups.Count ||
                    _groups[_activeGroupIndex] == null)
                {
                    throw new InvalidOperationException("The active 1.2.1 group index is invalid.");
                }

                selectionSnapshots = CanonicalizePublishedRemoveLastSelections();

                if (!CommitReleaseVersion(DeformationDataVersion.V1_3_0))
                {
                    throw new InvalidOperationException("Could not commit the 1.2.1→1.3.0 migration boundary.");
                }

                return true;
            }
            catch (Exception)
            {
                _layers = originalLayers;
                _groups = originalGroups;
                _layerModelVersion = originalLayerVersion;
                _activeLayerIndex = originalActiveLayer;
                _activeGroupIndex = originalActiveGroup;
                _deformationDataVersion = originalVersion;
                _deformationDataSourceVersion = originalSourceVersion;
                _legacyPublishedBlendShapeSemantics = originalPublishedBlendShapeSemantics;
                RestoreGroupSelections(selectionSnapshots);
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return false;
            }
        }

        private bool TryNormalizePublishedGroupSelectionAndCommit(DeformationDataVersion next)
        {
            DeformationDataVersion originalVersion = _deformationDataVersion;
            DeformationDataVersion originalSourceVersion = _deformationDataSourceVersion;
            List<GroupSelectionSnapshot> selectionSnapshots = null;
            try
            {
                selectionSnapshots = CanonicalizePublishedRemoveLastSelections();
                if (!CommitReleaseVersion(next))
                {
                    RestoreGroupSelections(selectionSnapshots);
                    return false;
                }

                return true;
            }
            // Canonicalization and commit are non-throwing for validated state.
#line hidden
            catch (Exception)
            {
                RestoreGroupSelections(selectionSnapshots);
                _deformationDataVersion = originalVersion;
                _deformationDataSourceVersion = originalSourceVersion;
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return false;
            }
        }

        private bool TryUpgradeV1_4_0ToCurrent()
        {
            DeformationDataVersion originalVersion = _deformationDataVersion;
            DeformationDataVersion originalSourceVersion = _deformationDataSourceVersion;
            int originalLayerModelVersion = _layerModelVersion;
            bool originalPublishedSemantics = _legacyPublishedBlendShapeSemantics;
            bool originalAbsoluteEvaluation = _legacyAbsoluteLatticeEvaluation;
            List<GroupSelectionSnapshot> selectionSnapshots = null;
            List<LatticeInterpolationCompatibilitySnapshot> interpolationSnapshots = null;
            try
            {
                NormalizeAuthoritativeGroupShapeVersion();
                if (ShouldPreserveHistoricalGroupBlendShapeSemantics())
                {
                    _legacyPublishedBlendShapeSemantics = true;
                }
                _legacyAbsoluteLatticeEvaluation = HasMeaningfulSerializedLatticeData();
                interpolationSnapshots = PreservePublishedCubicInterpolationSemantics();
                selectionSnapshots = CanonicalizePublishedRemoveLastSelections();
                if (!CommitReleaseVersion(DeformationDataVersion.CurrentDevelopment))
                {
                    throw new InvalidOperationException("Could not commit the 1.4.0→current migration boundary.");
                }

                return true;
            }
            catch (Exception)
            {
                RestoreGroupSelections(selectionSnapshots);
                _deformationDataVersion = originalVersion;
                _deformationDataSourceVersion = originalSourceVersion;
                _layerModelVersion = originalLayerModelVersion;
                _legacyPublishedBlendShapeSemantics = originalPublishedSemantics;
                _legacyAbsoluteLatticeEvaluation = originalAbsoluteEvaluation;
                RestoreLatticeInterpolationCompatibility(interpolationSnapshots);
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return false;
            }
#line default
        }

        private List<LatticeInterpolationCompatibilitySnapshot> PreservePublishedCubicInterpolationSemantics()
        {
            var snapshots = new List<LatticeInterpolationCompatibilitySnapshot>();
            var visited = new HashSet<LatticeAsset>();

            void Preserve(LatticeAsset asset)
            {
                if (asset == null || !visited.Add(asset) ||
                    asset.Interpolation != LatticeInterpolationMode.CubicBernstein)
                {
                    return;
                }

                snapshots.Add(new LatticeInterpolationCompatibilitySnapshot(
                    asset,
                    asset.UsesLegacyTrilinearInterpolation));
                asset.SetLegacyTrilinearInterpolation(true);
            }

            Preserve(_settings);
            if (_layers != null)
            {
                foreach (var layer in _layers)
                {
                    if (layer != null && layer.Type == MeshDeformerLayerType.Lattice)
                    {
                        Preserve(layer.SerializedSettings);
                    }
                }
            }

            if (_groups != null)
            {
                foreach (var group in _groups)
                {
                    var layers = group?.SerializedLayers;
                    if (layers == null) continue;
                    foreach (var layer in layers)
                    {
                        if (layer != null && layer.Type == MeshDeformerLayerType.Lattice)
                        {
                            Preserve(layer.SerializedSettings);
                        }
                    }
                }
            }

            return snapshots;
        }

        private static void RestoreLatticeInterpolationCompatibility(
            List<LatticeInterpolationCompatibilitySnapshot> snapshots)
        {
            if (snapshots == null) return;
            for (int index = snapshots.Count - 1; index >= 0; index--)
            {
                var snapshot = snapshots[index];
                snapshot.Asset?.SetLegacyTrilinearInterpolation(
                    snapshot.UsedLegacyTrilinearInterpolation);
            }
        }

        /// <summary>
        /// Releases 1.2.1 through 1.4.0 read ActiveLayerIndex only after removing a
        /// layer. Removing the selected last layer therefore serialized exactly one past
        /// the new Count. That exact, tag-proven pattern is recoverable without guessing;
        /// every other out-of-range value remains invalid.
        /// </summary>
        private List<GroupSelectionSnapshot> CanonicalizePublishedRemoveLastSelections()
        {
            var snapshots = new List<GroupSelectionSnapshot>();
            if (!CanContainPublishedRemoveLastSelectionBug() || _groups == null)
            {
                return snapshots;
            }

            for (int groupIndex = 0; groupIndex < _groups.Count; groupIndex++)
            {
                var group = _groups[groupIndex];
                var layers = group?.SerializedLayers;
                if (layers == null || layers.Count == 0 ||
                    group.SerializedActiveLayerIndex != layers.Count)
                {
                    continue;
                }

                snapshots.Add(new GroupSelectionSnapshot(group, group.SerializedActiveLayerIndex));
            }

            for (int index = 0; index < snapshots.Count; index++)
            {
                var snapshot = snapshots[index];
                snapshot.Group.SetSerializedActiveLayerIndex(snapshot.ActiveLayerIndex - 1);
            }

            return snapshots;
        }

        private static void RestoreGroupSelections(List<GroupSelectionSnapshot> snapshots)
        {
            if (snapshots == null) return;
            for (int index = snapshots.Count - 1; index >= 0; index--)
            {
                var snapshot = snapshots[index];
                snapshot.Group?.SetSerializedActiveLayerIndex(snapshot.ActiveLayerIndex);
            }
        }

        private bool CanContainPublishedRemoveLastSelectionBug()
        {
            if (_deformationDataVersion == DeformationDataVersion.Unversioned)
            {
                return HasNonNullGroups(_groups);
            }

            return _deformationDataVersion >= DeformationDataVersion.V1_2_1 &&
                   _deformationDataVersion <= DeformationDataVersion.V1_4_0;
        }

        private bool ShouldPreserveHistoricalGroupBlendShapeSemantics()
        {
            DeformationDataVersion source = SourceDeformationDataVersion;
            return source >= DeformationDataVersion.V1_2_1 &&
                   source <= DeformationDataVersion.V1_4_0 &&
                   HasEnabledPublishedBlendShapeMetadata();
        }

        private bool HasEnabledPublishedBlendShapeMetadata()
        {
            if (_groups != null)
            {
                foreach (var group in _groups)
                {
                    // Published Deform skipped disabled groups before inspecting any
                    // output metadata. Such dormant fields must not lock unrelated,
                    // enabled groups into component-wide compatibility semantics.
                    if (group == null || !group.Enabled) continue;
                    if (group.BlendShapeOutput == BlendShapeOutputMode.OutputAsBlendShape)
                    {
                        return true;
                    }

                    var layers = group.SerializedLayers;
                    if (layers == null) continue;
                    foreach (var layer in layers)
                    {
                        if (layer != null && layer.Enabled && layer.Weight > 0f &&
                            layer.BlendShapeOutput == BlendShapeOutputMode.OutputAsBlendShape)
                        {
                            return true;
                        }
                    }
                }
            }

            // Once published groups existed, the old runtime never evaluated the
            // component's stale flat-layer facade. Metadata found only in that backup
            // must therefore not switch the authoritative groups into component-wide
            // compatibility mode. The backup is retained in a disabled recovery group.
            return false;
        }

        private bool CommitReleaseVersion(DeformationDataVersion next)
        {
            if ((int)next <= (int)_deformationDataVersion ||
                (int)next > (int)DeformationDataVersion.CurrentDevelopment)
            {
                _migrationStatus = DeformationDataMigrationStatus.InvalidData;
                return false;
            }

            if (_deformationDataSourceVersion == DeformationDataVersion.Unversioned)
            {
                _deformationDataSourceVersion = _deformationDataVersion;
            }

            _deformationDataVersion = next;
            _migrationStatus = next == DeformationDataVersion.CurrentDevelopment
                ? DeformationDataMigrationStatus.Ready
                : DeformationDataMigrationStatus.InProgress;
            MarkMigrationCommitted();
            return true;
        }

        private void MarkMigrationCommitted()
        {
            InvalidateCache();
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                MarkDirtyInEditor(this);
            }
#endif
        }

        private bool HasMeaningfulBaseSettings()
        {
            if (_settings == null)
            {
                return false;
            }

            if (_settings.HasPendingLegacyWorldSpace || _settings.HasInvalidLegacyApplySpace ||
                _hasInitializedFromSource || _serializedSourceMesh != null)
            {
                return true;
            }

            // Unity may run the nested serialization callback while a brand-new
            // component is being constructed, which creates a neutral point array.
            // Neutral points without any source-initialization evidence are fresh, not
            // historical deformation data.
            return _settings.HasNonDefaultSerializedConfiguration ||
                   (_settings.HasSerializedControlPointData && _settings.HasCustomizedControlPoints());
        }

        private bool HasMeaningfulSerializedLatticeData()
        {
            if (_groups != null)
            {
                foreach (var group in _groups)
                {
                    if (group == null) continue;
                    var serializedLayers = group.SerializedLayers;
                    if (serializedLayers == null) continue;
                    foreach (var layer in serializedLayers)
                    {
                        if (layer != null && layer.Type == MeshDeformerLayerType.Lattice &&
                            layer.SerializedSettings != null &&
                            layer.SerializedSettings.HasSerializedControlPointData)
                        {
                            return true;
                        }
                    }
                }
            }

            if (_layers != null)
            {
                foreach (var layer in _layers)
                {
                    if (layer != null && layer.Type == MeshDeformerLayerType.Lattice &&
                        layer.SerializedSettings != null &&
                        layer.SerializedSettings.HasSerializedControlPointData)
                    {
                        return true;
                    }
                }
            }

            return HasMeaningfulBaseSettings();
        }

        private static bool HasNonNullGroups(List<DeformerGroup> groups)
        {
            if (groups == null) return false;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i] != null) return true;
            }

            return false;
        }

        private static bool HasNonNullLayers(List<LatticeLayer> layers)
        {
            if (layers == null) return false;
            for (int i = 0; i < layers.Count; i++)
            {
                if (layers[i] != null) return true;
            }

            return false;
        }

        private bool HasUnsupportedFutureLatticeAsset()
        {
            if (_settings != null && _settings.HasUnsupportedFutureSerializationVersion)
            {
                return true;
            }

            if (_layers != null)
            {
                foreach (var layer in _layers)
                {
                    if (layer?.SerializedSettings != null &&
                        layer.SerializedSettings.HasUnsupportedFutureSerializationVersion)
                    {
                        return true;
                    }
                }
            }

            if (_groups != null)
            {
                foreach (var group in _groups)
                {
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

        private bool HasMalformedLatticeAsset()
        {
            if (HasMalformedSerializedSelection())
            {
                return true;
            }

            if (_blendShapeOutput != BlendShapeOutputMode.Disabled &&
                _blendShapeOutput != BlendShapeOutputMode.OutputAsBlendShape)
            {
                return true;
            }

            if (_settings != null && _settings.HasMalformedSerializedShape)
            {
                return true;
            }

            if (_layers != null)
            {
                foreach (var layer in _layers)
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

            if (_groups != null)
            {
                foreach (var group in _groups)
                {
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

        /// <summary>
        /// Validates raw selection indices before any migration or model-normalization
        /// code can clamp them. Active selection is serialized user data: silently
        /// choosing another group/layer would make a corrupt payload appear to migrate
        /// successfully while changing which deformation the Inspector edits.
        /// </summary>
        private bool HasMalformedSerializedSelection()
        {
            // Missing fields from old YAML retain these field-initializer lists. A
            // runtime null therefore represents an explicit/corrupt payload, and the
            // normalization paths below must not replace it with a guessed empty list.
            if (_groups == null || _layers == null)
            {
                return true;
            }

            if (_groups.Count == 0)
            {
                if (_activeGroupIndex != 0)
                {
                    return true;
                }
            }
            else
            {
                if (_activeGroupIndex < 0 || _activeGroupIndex >= _groups.Count)
                {
                    return true;
                }

                for (int groupIndex = 0; groupIndex < _groups.Count; groupIndex++)
                {
                    var group = _groups[groupIndex];
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
                            CanContainPublishedRemoveLastSelectionBug() &&
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

            if (_layers.Count == 0)
            {
                // Published group initialization could leave the obsolete component
                // facade index behind after moving its selected flat layer into a
                // DeformerGroup. It has no target once the flat list is empty; preserve
                // it through classification, then canonicalize it at the structural
                // 1.2.1→1.3.0 boundary. Later/current payloads must already be canonical.
                bool awaitingPublishedGroupNormalization = _groups.Count > 0 &&
                    (_deformationDataVersion == DeformationDataVersion.Unversioned ||
                     _deformationDataVersion == DeformationDataVersion.V1_2_0 ||
                     _deformationDataVersion == DeformationDataVersion.V1_2_1);
                if (awaitingPublishedGroupNormalization)
                {
                    return false;
                }

                // The single-settings schema used both the default zero and -1 as the
                // base-lattice selection sentinel before a flat list existed.
                return _activeLayerIndex < -1 || _activeLayerIndex > 0;
            }

            // A conceptual-v2 flat payload could historically contain null holes; the
            // immutable staged migration contract deterministically filters those while
            // remapping a non-null active layer. Once authoritative groups exist, the
            // same null is corruption in the stale backup and must fail closed.
            if (_groups != null && _groups.Count > 0)
            {
                for (int layerIndex = 0; layerIndex < _layers.Count; layerIndex++)
                {
                    if (_layers[layerIndex] == null)
                    {
                        return true;
                    }
                }
            }

            return _activeLayerIndex < 0 || _activeLayerIndex >= _layers.Count ||
                   _layers[_activeLayerIndex] == null;
        }

        /// <summary>
        /// Validates non-empty vertex-indexed payloads without allocating, resizing, or
        /// caching anything. This preflight runs before every release step so a brush or
        /// mask mismatch cannot be committed through later release markers first.
        /// </summary>
        private bool HasIncompatibleSerializedVertexIndexedData()
        {
            Mesh validationMesh = null;
            if (_skinnedMeshRenderer != null)
            {
                validationMesh = _skinnedMeshRenderer.sharedMesh;
            }
            if (validationMesh == null && _meshFilter != null)
            {
                validationMesh = _meshFilter.sharedMesh;
            }

            if (validationMesh == null)
            {
                var serializedSkinnedRenderer = GetComponent<SkinnedMeshRenderer>();
                if (serializedSkinnedRenderer != null)
                {
                    validationMesh = serializedSkinnedRenderer.sharedMesh;
                }
                if (validationMesh == null)
                {
                    var serializedMeshFilter = GetComponent<MeshFilter>();
                    if (serializedMeshFilter != null)
                    {
                        validationMesh = serializedMeshFilter.sharedMesh;
                    }
                }
            }

            if (validationMesh == null)
            {
                validationMesh = _serializedSourceMesh != null ? _serializedSourceMesh : _sourceMesh;
            }

            int expectedVertexCount = validationMesh != null ? validationMesh.vertexCount : -1;

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

            if (_layers != null)
            {
                foreach (var layer in _layers)
                {
                    if (IsIncompatible(layer)) return true;
                }
            }

            if (_groups != null)
            {
                foreach (var group in _groups)
                {
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
