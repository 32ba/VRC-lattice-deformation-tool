using System;
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    // One migration attempt owns the scalar state and copy-on-write containers.
    // Existing nested data remains borrowed and is covered by the step rollback.
    internal sealed class DeformationMigrationState
    {
        internal LatticeAsset Settings;
        internal List<LatticeLayer> FlatLayers;
        internal List<DeformerGroup> Groups;
        internal int ActiveLayerIndex, ActiveGroupIndex, LayerModelVersion, ProfileGroupCount, SourceVertexCount;
        internal int ReleaseIndex;
        internal DeformationDataVersion Version, SourceVersion;
        internal BlendShapeOutputMode BlendShapeOutput;
        internal string BlendShapeName;
        internal AnimationCurve BlendShapeCurve;
        internal bool LegacyAbsoluteEvaluation, LegacyPublishedBlendShapeSemantics;
        internal bool HasInitializedFromSource, HasSerializedSource, HasIncompatibleBrushData, CommitRequested;
        internal Matrix4x4? OwnerWorldToLocal;
        internal DeformationDataMigrationStatus Status;
        internal DeformationDataVersion EffectiveSourceVersion => SourceVersion == DeformationDataVersion.Unversioned ? Version : SourceVersion;
        internal DeformationMigrationInput PreflightInput => new DeformationMigrationInput(
            Settings, FlatLayers, ActiveLayerIndex, Groups, ActiveGroupIndex, Version, LayerModelVersion,
            BlendShapeOutput, ProfileGroupCount);
        internal DeformationMigrationState Copy() => (DeformationMigrationState)MemberwiseClone();
        internal void RestoreFrom(DeformationMigrationState source)
        {
            Settings = source.Settings;
            FlatLayers = source.FlatLayers;
            Groups = source.Groups;
            ActiveLayerIndex = source.ActiveLayerIndex;
            ActiveGroupIndex = source.ActiveGroupIndex;
            LayerModelVersion = source.LayerModelVersion;
            ProfileGroupCount = source.ProfileGroupCount;
            SourceVertexCount = source.SourceVertexCount;
            ReleaseIndex = source.ReleaseIndex;
            Version = source.Version;
            SourceVersion = source.SourceVersion;
            BlendShapeOutput = source.BlendShapeOutput;
            BlendShapeName = source.BlendShapeName;
            BlendShapeCurve = source.BlendShapeCurve;
            LegacyAbsoluteEvaluation = source.LegacyAbsoluteEvaluation;
            LegacyPublishedBlendShapeSemantics = source.LegacyPublishedBlendShapeSemantics;
            HasInitializedFromSource = source.HasInitializedFromSource;
            HasSerializedSource = source.HasSerializedSource;
            HasIncompatibleBrushData = source.HasIncompatibleBrushData;
            CommitRequested = source.CommitRequested;
            OwnerWorldToLocal = source.OwnerWorldToLocal;
            Status = source.Status;
        }
    }

    internal sealed class DeformationMigrationRunner
    {
        private const int k_CurrentLayerModelVersion = DeformationMigrationPreflight.CurrentLayerModelVersion;
        private const string k_PrimaryLayerName = "Lattice Layer";
        private const string k_RecoveredLegacyFlatLayersGroupName = "Recovered Legacy Flat Layers";
        private readonly DeformationMigrationState _state;
        private readonly Action<DeformationDataVersion> _beforeCommit;
        internal DeformationMigrationState State => _state;

        internal DeformationMigrationRunner(DeformationMigrationState state, Action<DeformationDataVersion> beforeCommit = null)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _beforeCommit = beforeCommit;
        }

        private bool ValidateInput()
        {
            var input = _state.PreflightInput;
            var status = DeformationMigrationPreflight.ValidateSchema(input);
            if (status != DeformationDataMigrationStatus.Ready)
            {
                _state.Status = status;
                return false;
            }
            if (DeformationMigrationPreflight.HasIncompatibleVertexData(input, _state.SourceVertexCount))
            {
                _state.HasIncompatibleBrushData = true;
                _state.Status = DeformationDataMigrationStatus.InvalidData;
                return false;
            }
            return true;
        }

        internal bool TryAdvanceOneRelease(Action<DeformationMigrationState> commit = null)
        {
            if (!ValidateInput()) return false;
            _state.CommitRequested = false;
            var original = _state.Copy();
            var selections = CaptureGroupSelections();
            var interpolation = CaptureInterpolationFlags();
            try
            {
                if (AdvanceValidatedRelease())
                {
                    // The owner applies serialized fields and records the change
                    // inside this boundary. A failed owner commit also restores the
                    // borrowed nested selections and compatibility flags below.
                    commit?.Invoke(_state);
                    return true;
                }
                // Current is a read-only query; keep its Ready/Invalid status.
                var status = _state.Status;
                RestoreGroupSelections(selections);
                RestoreLatticeInterpolationCompatibility(interpolation);
                _state.RestoreFrom(original);
                _state.Status = status;
                return false;
            }
            catch (Exception)
            {
                RestoreGroupSelections(selections);
                RestoreLatticeInterpolationCompatibility(interpolation);
                _state.RestoreFrom(original);
                _state.Status = DeformationDataMigrationStatus.InvalidData;
                return false;
            }
        }

        private List<GroupSelectionSnapshot> CaptureGroupSelections()
        {
            var result = new List<GroupSelectionSnapshot>();
            if (_state.Groups != null)
                foreach (var group in _state.Groups)
                    if (group != null) result.Add(new GroupSelectionSnapshot(group, group.SerializedActiveLayerIndex));
            return result;
        }

        private List<LatticeInterpolationCompatibilitySnapshot> CaptureInterpolationFlags()
        {
            var result = new List<LatticeInterpolationCompatibilitySnapshot>();
            var visited = new HashSet<LatticeAsset>();
            void Capture(LatticeAsset settings)
            {
                if (settings != null && visited.Add(settings))
                    result.Add(new LatticeInterpolationCompatibilitySnapshot(settings, settings.UsesLegacyTrilinearInterpolation));
            }
            Capture(_state.Settings);
            if (_state.FlatLayers != null)
                foreach (var layer in _state.FlatLayers) Capture(layer?.SerializedSettings);
            if (_state.Groups != null)
                foreach (var group in _state.Groups)
                    if (group?.SerializedLayers != null)
                        foreach (var layer in group.SerializedLayers) Capture(layer?.SerializedSettings);
            return result;
        }

        internal readonly struct LatticeInterpolationCompatibilitySnapshot
        {
            public readonly LatticeAsset Asset;
            public readonly bool UsedLegacyTrilinearInterpolation;

            public LatticeInterpolationCompatibilitySnapshot(
                LatticeAsset asset,
                bool usedLegacyTrilinearInterpolation)
            {
                Asset = asset;
                UsedLegacyTrilinearInterpolation = usedLegacyTrilinearInterpolation;
            }
        }

        internal readonly struct GroupSelectionSnapshot
        {
            public readonly DeformerGroup Group;
            public readonly int ActiveLayerIndex;

            public GroupSelectionSnapshot(DeformerGroup group, int activeLayerIndex)
            {
                Group = group;
                ActiveLayerIndex = activeLayerIndex;
            }
        }

        internal bool NeedsStaleCurrentStructureRecovery =>
            _state.Version == DeformationDataVersion.CurrentDevelopment &&
            _state.LayerModelVersion < k_CurrentLayerModelVersion &&
            !HasNonNullGroups(_state.Groups) &&
            (HasNonNullLayers(_state.FlatLayers) || HasMeaningfulBaseSettings());

        internal void RecoverStaleCurrentStructureVersionIfNeeded()
        {
            if (!NeedsStaleCurrentStructureRecovery)
            {
                return;
            }

            // A current release marker paired with only an older serialized shape can
            // result from an interrupted save or an Inspector-first partial migration.
            // Recover the older shape instead of creating a default group over it.
            _state.Version = _state.Settings != null && _state.Settings.HasPendingLegacyWorldSpace
                ? DeformationDataVersion.V0_0_1
                : DeformationDataVersion.V1_2_0;
            _state.SourceVersion = _state.Version;
            _state.Status = DeformationDataMigrationStatus.InProgress;
            _state.CommitRequested = true;
        }

        internal bool AdvanceValidatedRelease()
        {
            if (_state.Version == DeformationDataVersion.CurrentDevelopment)
            {
                _state.Status = _state.HasIncompatibleBrushData
                    ? DeformationDataMigrationStatus.InvalidData
                    : DeformationDataMigrationStatus.Ready;
                return false;
            }

            _state.Status = DeformationDataMigrationStatus.InProgress;
            switch (_state.Version)
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
                default:
                    _state.Status = DeformationDataMigrationStatus.InvalidData;
                    return false;
            }
        }

        internal void NormalizeAuthoritativeGroupShapeVersion()
        {
            if (HasNonNullGroups(_state.Groups) && !HasNonNullLayers(_state.FlatLayers) &&
                _state.LayerModelVersion < k_CurrentLayerModelVersion)
            {
                _state.LayerModelVersion = k_CurrentLayerModelVersion;
            }
        }

        internal bool ClassifyUnversionedDeformationData()
        {
            DeformationDataVersion detected;
            bool hasGroups = HasNonNullGroups(_state.Groups);
            bool hasFlatLayers = HasNonNullLayers(_state.FlatLayers);
            bool hasBaseSettings = HasMeaningfulBaseSettings();

            if (!hasGroups && !hasFlatLayers && !hasBaseSettings)
            {
                _state.LayerModelVersion = k_CurrentLayerModelVersion;
                _state.LegacyAbsoluteEvaluation = false;
                _state.SourceVersion = DeformationDataVersion.CurrentDevelopment;
                return CommitReleaseVersion(DeformationDataVersion.CurrentDevelopment);
            }

            if (hasGroups)
            {
                // Serialized groups first shipped in 1.2.1. The published releases can
                // also contain an eagerly-created group beside a stale flat-layer copy
                // and conceptual-v2 marker; those are still 1.2.1 evidence.
                detected = DeformationDataVersion.V1_2_1;
            }
            else if (hasFlatLayers || _state.LayerModelVersion > 0)
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

            _beforeCommit?.Invoke(detected);
            _state.SourceVersion = detected;
            _state.Version = detected;
            _state.CommitRequested = true;
            return true;
        }

        internal bool TryUpgradeV0_0_1ToV0_0_2()
        {
            if (_state.Settings == null)
            {
                _state.Status = DeformationDataMigrationStatus.InvalidData;
                return false;
            }

            if (_state.Settings.HasInvalidLegacyApplySpace)
            {
                _state.Status = DeformationDataMigrationStatus.InvalidData;
                return false;
            }

            if (_state.Settings.HasPendingLegacyWorldSpace)
            {
                if (!_state.OwnerWorldToLocal.HasValue)
                {
                    _state.Status = DeformationDataMigrationStatus.PendingOwnerTransform;
                    return false;
                }

                if (_state.Settings.ControlPointsLocal.Length != _state.Settings.ControlPointCount)
                {
                    _state.Status = DeformationDataMigrationStatus.InvalidData;
                    return false;
                }

                // 0.0.1 evaluated World control points against the owner's transform
                // on every deformation. Validate now, but retain both raw points and
                // marker so later transform changes keep those exact semantics.
                if (!_state.Settings.CanEvaluateLegacyWorldSpace(_state.OwnerWorldToLocal.Value))
                {
                    _state.Status = DeformationDataMigrationStatus.PendingOwnerTransform;
                    return false;
                }
            }

            return CommitReleaseVersion(DeformationDataVersion.V0_0_2);
        }

        internal bool TryUpgradeV1_2_0ToV1_2_1()
        {
            // The structural helpers below use copy-on-write for the containing lists,
            // so retaining the original references is a complete rollback snapshot.
            var originalLayers = _state.FlatLayers;
            var originalGroups = _state.Groups;
            int originalLayerVersion = _state.LayerModelVersion;
            int originalActiveLayer = _state.ActiveLayerIndex;
            int originalActiveGroup = _state.ActiveGroupIndex;

            try
            {
                bool hasGroups = HasNonNullGroups(_state.Groups);
                bool hasFlatLayers = HasNonNullLayers(_state.FlatLayers);

                if (hasGroups && !hasFlatLayers)
                {
                    // A partial save already contains the newest meaningful shape. Do
                    // not manufacture a duplicate layer from the facade _state.Settings copy.
                    _state.LayerModelVersion = k_CurrentLayerModelVersion;
                }
                else
                {
                    if (_state.LayerModelVersion < 2)
                    {
                        TryMigrateLegacyBaseToLayerStructure();
                    }

                    TryMigrateLayersToGroupStructure();
                }

                if (_state.LayerModelVersion != k_CurrentLayerModelVersion || !HasNonNullGroups(_state.Groups))
                {
                    throw new InvalidOperationException("Layer/group migration did not produce the v3 structure.");
                }
            }
            catch (Exception)
            {
                _state.FlatLayers = originalLayers;
                _state.Groups = originalGroups;
                _state.LayerModelVersion = originalLayerVersion;
                _state.ActiveLayerIndex = originalActiveLayer;
                _state.ActiveGroupIndex = originalActiveGroup;
                _state.Status = DeformationDataMigrationStatus.InvalidData;
                return false;
            }

            return CommitReleaseVersion(DeformationDataVersion.V1_2_1);
        }

        internal bool TryUpgradeV1_2_1ToV1_3_0()
        {
            // 1.2.1–1.4.0 could serialize authoritative groups together with a stale
            // flat-layer facade and _state.LayerModelVersion=2. The old runtime ignored that
            // flat copy. Preserve it in a disabled recovery group so the payload remains
            // inspectable without changing deformation or BlendShape output.
            var originalLayers = _state.FlatLayers;
            var originalGroups = _state.Groups;
            int originalLayerVersion = _state.LayerModelVersion;
            int originalActiveLayer = _state.ActiveLayerIndex;
            int originalActiveGroup = _state.ActiveGroupIndex;
            DeformationDataVersion originalVersion = _state.Version;
            DeformationDataVersion originalSourceVersion = _state.SourceVersion;
            bool originalPublishedBlendShapeSemantics = _state.LegacyPublishedBlendShapeSemantics;
            List<GroupSelectionSnapshot> selectionSnapshots = null;

            try
            {
                if (!HasNonNullGroups(_state.Groups))
                {
                    throw new InvalidOperationException("The 1.2.1 group payload is missing.");
                }
                bool preservePublishedBlendShapeSemantics =
                    ShouldPreserveHistoricalGroupBlendShapeSemantics();

                var migratedGroups = new List<DeformerGroup>(_state.Groups);
                if (HasNonNullLayers(_state.FlatLayers))
                {
                    var migratedLayers = FilterLayersAndRemapActive(
                        _state.FlatLayers,
                        _state.ActiveLayerIndex,
                        out int migratedActiveLayer);
                    // HasNonNullLayers guarantees the filter retains at least one layer.
                    if (migratedLayers.Count == 0)
                    {
                        throw new InvalidOperationException("The legacy flat-layer payload could not be recovered.");
                    }

                    var recoveryGroup = new DeformerGroup
                    {
                        Name = k_RecoveredLegacyFlatLayersGroupName,
                        Enabled = false,
                        ActiveLayerIndex = migratedActiveLayer,
                        BlendShapeOutput = _state.BlendShapeOutput,
                        BlendShapeName = _state.BlendShapeName ?? "",
                        BlendShapeCurve = DeformationModelCopy.CloneCurve(_state.BlendShapeCurve)
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

                _state.Groups = migratedGroups;
                _state.FlatLayers = new List<LatticeLayer>();
                // The recovery group owns the preserved flat selection from this point.
                _state.ActiveLayerIndex = 0;
                _state.LayerModelVersion = k_CurrentLayerModelVersion;
                // Existing groups are authoritative; keep the user's selected group.
                _state.ActiveGroupIndex = originalActiveGroup;
                if (preservePublishedBlendShapeSemantics)
                {
                    _state.LegacyPublishedBlendShapeSemantics = true;
                }
                if (_state.ActiveGroupIndex < 0 || _state.ActiveGroupIndex >= _state.Groups.Count ||
                    _state.Groups[_state.ActiveGroupIndex] == null)
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
                _state.FlatLayers = originalLayers;
                _state.Groups = originalGroups;
                _state.LayerModelVersion = originalLayerVersion;
                _state.ActiveLayerIndex = originalActiveLayer;
                _state.ActiveGroupIndex = originalActiveGroup;
                _state.Version = originalVersion;
                _state.SourceVersion = originalSourceVersion;
                _state.LegacyPublishedBlendShapeSemantics = originalPublishedBlendShapeSemantics;
                RestoreGroupSelections(selectionSnapshots);
                _state.Status = DeformationDataMigrationStatus.InvalidData;
                return false;
            }
        }

        internal bool TryNormalizePublishedGroupSelectionAndCommit(DeformationDataVersion next)
        {
            DeformationDataVersion originalVersion = _state.Version;
            DeformationDataVersion originalSourceVersion = _state.SourceVersion;
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
            catch (Exception)
            {
                RestoreGroupSelections(selectionSnapshots);
                _state.Version = originalVersion;
                _state.SourceVersion = originalSourceVersion;
                _state.Status = DeformationDataMigrationStatus.InvalidData;
                return false;
            }
        }

        internal bool TryUpgradeV1_4_0ToCurrent()
        {
            DeformationDataVersion originalVersion = _state.Version;
            DeformationDataVersion originalSourceVersion = _state.SourceVersion;
            int originalLayerModelVersion = _state.LayerModelVersion;
            bool originalPublishedSemantics = _state.LegacyPublishedBlendShapeSemantics;
            bool originalAbsoluteEvaluation = _state.LegacyAbsoluteEvaluation;
            List<GroupSelectionSnapshot> selectionSnapshots = null;
            List<LatticeInterpolationCompatibilitySnapshot> interpolationSnapshots = null;
            try
            {
                NormalizeAuthoritativeGroupShapeVersion();
                if (ShouldPreserveHistoricalGroupBlendShapeSemantics())
                {
                    _state.LegacyPublishedBlendShapeSemantics = true;
                }
                _state.LegacyAbsoluteEvaluation = HasMeaningfulSerializedLatticeData();
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
                _state.Version = originalVersion;
                _state.SourceVersion = originalSourceVersion;
                _state.LayerModelVersion = originalLayerModelVersion;
                _state.LegacyPublishedBlendShapeSemantics = originalPublishedSemantics;
                _state.LegacyAbsoluteEvaluation = originalAbsoluteEvaluation;
                RestoreLatticeInterpolationCompatibility(interpolationSnapshots);
                _state.Status = DeformationDataMigrationStatus.InvalidData;
                return false;
            }
        }

        internal List<LatticeInterpolationCompatibilitySnapshot> PreservePublishedCubicInterpolationSemantics()
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

            Preserve(_state.Settings);
            if (_state.FlatLayers != null)
            {
                foreach (var layer in _state.FlatLayers)
                {
                    if (layer != null && layer.Type == MeshDeformerLayerType.Lattice)
                    {
                        Preserve(layer.SerializedSettings);
                    }
                }
            }

            if (_state.Groups != null)
            {
                foreach (var group in _state.Groups)
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

        internal static void RestoreLatticeInterpolationCompatibility(
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

        internal List<GroupSelectionSnapshot> CanonicalizePublishedRemoveLastSelections()
        {
            var snapshots = new List<GroupSelectionSnapshot>();
            if (!DeformationMigrationPreflight.CanContainPublishedRemoveLastSelectionBug(_state.PreflightInput) || _state.Groups == null)
            {
                return snapshots;
            }

            for (int groupIndex = 0; groupIndex < _state.Groups.Count; groupIndex++)
            {
                var group = _state.Groups[groupIndex];
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

        internal static void RestoreGroupSelections(List<GroupSelectionSnapshot> snapshots)
        {
            if (snapshots == null) return;
            for (int index = snapshots.Count - 1; index >= 0; index--)
            {
                var snapshot = snapshots[index];
                snapshot.Group?.SetSerializedActiveLayerIndex(snapshot.ActiveLayerIndex);
            }
        }

        internal bool ShouldPreserveHistoricalGroupBlendShapeSemantics()
        {
            DeformationDataVersion source = _state.EffectiveSourceVersion;
            return source >= DeformationDataVersion.V1_2_1 &&
                   source <= DeformationDataVersion.V1_4_0 &&
                   HasEnabledPublishedBlendShapeMetadata();
        }

        internal bool HasEnabledPublishedBlendShapeMetadata()
        {
            if (_state.Groups != null)
            {
                foreach (var group in _state.Groups)
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

        internal bool CommitReleaseVersion(DeformationDataVersion next)
        {
            if ((int)next <= (int)_state.Version ||
                (int)next > (int)DeformationDataVersion.CurrentDevelopment)
            {
                _state.Status = DeformationDataMigrationStatus.InvalidData;
                return false;
            }

            _beforeCommit?.Invoke(next);

            if (_state.SourceVersion == DeformationDataVersion.Unversioned)
            {
                _state.SourceVersion = _state.Version;
            }

            _state.Version = next;
            _state.Status = next == DeformationDataVersion.CurrentDevelopment
                ? DeformationDataMigrationStatus.Ready
                : DeformationDataMigrationStatus.InProgress;
            _state.CommitRequested = true;
            return true;
        }

        internal bool HasMeaningfulBaseSettings()
        {
            if (_state.Settings == null)
            {
                return false;
            }

            if (_state.Settings.HasPendingLegacyWorldSpace || _state.Settings.HasInvalidLegacyApplySpace ||
                _state.HasInitializedFromSource || _state.HasSerializedSource)
            {
                return true;
            }

            // Unity may run the nested serialization callback while a brand-new
            // component is being constructed, which creates a neutral point array.
            // Neutral points without any source-initialization evidence are fresh, not
            // historical deformation data.
            return _state.Settings.HasNonDefaultSerializedConfiguration ||
                   (_state.Settings.HasSerializedControlPointData && _state.Settings.HasCustomizedControlPoints());
        }

        internal bool HasMeaningfulSerializedLatticeData()
        {
            if (_state.Groups != null)
            {
                foreach (var group in _state.Groups)
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

            if (_state.FlatLayers != null)
            {
                foreach (var layer in _state.FlatLayers)
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

        internal static bool HasNonNullGroups(List<DeformerGroup> groups)
        {
            if (groups == null) return false;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i] != null) return true;
            }

            return false;
        }

        internal static bool HasNonNullLayers(List<LatticeLayer> layers)
        {
            if (layers == null) return false;
            for (int i = 0; i < layers.Count; i++)
            {
                if (layers[i] != null) return true;
            }

            return false;
        }

        internal bool TryMigrateLayersToGroupStructure()
        {
            if (_state.LayerModelVersion > k_CurrentLayerModelVersion ||
                (int)_state.Version > (int)DeformationDataVersion.CurrentDevelopment)
            {
                return false;
            }

            var sourceLayers = _state.FlatLayers ?? new List<LatticeLayer>();
            var migratedLayers = FilterLayersAndRemapActive(
                sourceLayers,
                _state.ActiveLayerIndex,
                out int migratedActiveLayer);

            bool hasGroups = HasNonNullGroups(_state.Groups);
            if (hasGroups && migratedLayers.Count == 0)
            {
                if (_state.LayerModelVersion >= k_CurrentLayerModelVersion) return false;
                _state.LayerModelVersion = k_CurrentLayerModelVersion;
                return false;
            }

            if (!hasGroups && migratedLayers.Count == 0)
            {
                if (_state.LayerModelVersion >= k_CurrentLayerModelVersion) return false;
                _state.LayerModelVersion = k_CurrentLayerModelVersion;
                return false;
            }

            // Wrap the flat payload. If groups already exist due to a partial save or
            // Inspector-first access, append a recovery group instead of discarding
            // either representation.
            var group = new DeformerGroup();
            group.Name = hasGroups ? "Recovered Layers" : "Group";
            foreach (var layer in migratedLayers)
            {
                group.LayersList.Add(layer);
            }
            group.ActiveLayerIndex = migratedActiveLayer;
            group.BlendShapeOutput = _state.BlendShapeOutput;
            group.BlendShapeName = _state.BlendShapeName ?? "";
            group.BlendShapeCurve = _state.BlendShapeCurve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);

            var migratedGroups = _state.Groups == null
                ? new List<DeformerGroup>()
                : new List<DeformerGroup>(_state.Groups);
            migratedGroups.Add(group);
            _state.Groups = migratedGroups;
            _state.ActiveGroupIndex = migratedGroups.Count - 1;
            _state.FlatLayers = new List<LatticeLayer>();
            // The selected flat layer now lives in the migrated group. Keep the raw
            // facade index canonical so subsequent fail-closed preflights do not treat
            // an otherwise successful migration as a dangling selection.
            _state.ActiveLayerIndex = 0;
            _state.LayerModelVersion = k_CurrentLayerModelVersion;

            _state.CommitRequested = true;
            return true;
        }

        internal bool TryMigrateLegacyBaseToLayerStructure()
        {
            _state.Settings ??= new LatticeAsset();
            _state.Settings.EnsureInitialized();
            // This handles v0→v2 (flat _state.Settings → _state.FlatLayers). Skip if already at v2+.
            if (_state.LayerModelVersion >= 2)
            {
                return false;
            }

            var existingLayers = _state.FlatLayers ?? new List<LatticeLayer>();
            var migratedLayers = new List<LatticeLayer>();
            LatticeLayer selectedLayer = _state.ActiveLayerIndex >= 0 && _state.ActiveLayerIndex < existingLayers.Count
                ? existingLayers[_state.ActiveLayerIndex]
                : null;

            bool includeLegacyBase = _state.Settings != null &&
                                     (_state.Settings.HasCustomizedControlPoints() ||
                                      !HasNonNullLayers(existingLayers) ||
                                      _state.ActiveLayerIndex < 0);
            int migratedActive = 0;
            if (includeLegacyBase)
            {
                migratedLayers.Add(new LatticeLayer
                {
                    Name = k_PrimaryLayerName,
                    Enabled = true,
                    Weight = 1f,
                    Settings = DeformationModelCopy.CloneSettings(_state.Settings)
                });

                if (_state.ActiveLayerIndex < 0)
                {
                    migratedActive = 0;
                }
            }

            for (int i = 0; i < existingLayers.Count; i++)
            {
                var existing = existingLayers[i];
                if (existing == null)
                {
                    continue;
                }

                if (ReferenceEquals(existing, selectedLayer))
                {
                    migratedActive = migratedLayers.Count;
                }
                migratedLayers.Add(existing);
            }

            if (selectedLayer == null && _state.ActiveLayerIndex >= 0 && migratedLayers.Count > 0)
            {
                int nonNullBeforeOrAt = 0;
                int limit = Mathf.Clamp(_state.ActiveLayerIndex, 0, Math.Max(0, existingLayers.Count - 1));
                for (int i = 0; i <= limit && i < existingLayers.Count; i++)
                {
                    if (existingLayers[i] != null) nonNullBeforeOrAt++;
                }

                migratedActive = (includeLegacyBase ? 1 : 0) + nonNullBeforeOrAt - 1;
            }

            _state.FlatLayers = migratedLayers;
            _state.ActiveLayerIndex = _state.FlatLayers.Count == 0
                ? 0
                : Mathf.Clamp(migratedActive, 0, _state.FlatLayers.Count - 1);
            _state.LayerModelVersion = 2; // v0→v2 done; TryMigrateLayersToGroupStructure handles v2→v3

            _state.CommitRequested = true;
            return true;
        }

        internal static List<LatticeLayer> FilterLayersAndRemapActive(
            List<LatticeLayer> source,
            int sourceActive,
            out int active)
        {
            var filtered = new List<LatticeLayer>();
            LatticeLayer selected = sourceActive >= 0 && sourceActive < source.Count
                ? source[sourceActive]
                : null;
            active = 0;

            for (int i = 0; i < source.Count; i++)
            {
                var layer = source[i];
                if (layer == null) continue;
                if (ReferenceEquals(layer, selected)) active = filtered.Count;
                filtered.Add(layer);
            }

            if (selected == null && filtered.Count > 0)
            {
                int nonNullBeforeOrAt = 0;
                int limit = Mathf.Clamp(sourceActive, 0, source.Count - 1);
                for (int i = 0; i <= limit; i++)
                {
                    if (source[i] != null) nonNullBeforeOrAt++;
                }

                active = Mathf.Clamp(nonNullBeforeOrAt - 1, 0, filtered.Count - 1);
            }

            return filtered;
        }
    }
}
