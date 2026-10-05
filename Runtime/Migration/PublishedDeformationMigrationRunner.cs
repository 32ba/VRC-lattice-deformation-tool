using System;

namespace Net._32Ba.LatticeDeformationTool
{
    /// <summary>
    /// Owns the append-only published-release journal. Legacy data transformations
    /// remain in their tested runner; later no-op boundaries still commit separately.
    /// Neither runner depends on components, renderers, or Unity recording APIs.
    /// </summary>
    internal sealed class PublishedDeformationMigrationRunner
    {
        private readonly DeformationMigrationState _state;
        private readonly Action<int> _beforeCommit;
        internal DeformationMigrationState State => _state;

        internal PublishedDeformationMigrationRunner(DeformationMigrationState state, Action<int> beforeCommit = null)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _beforeCommit = beforeCommit;
        }

        internal bool TryAdvanceOneRelease(Action<DeformationMigrationState> commit = null)
        {
            var input = _state.PreflightInput;
            var status = DeformationMigrationPreflight.ValidateSchema(input);
            if (status == DeformationDataMigrationStatus.Ready)
                status = DeformationReleaseManifest.ValidateCursor(_state.ReleaseIndex);
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
            var legacy = new DeformationMigrationRunner(_state);
            if (legacy.NeedsStaleCurrentStructureRecovery)
            {
                // The published stale-current recovery changes both markers.
                // Include it in the same transaction as its journal and owner
                // recording, even when an inherited journal already says current.
                var original = _state.Copy();
                try
                {
                    legacy.RecoverStaleCurrentStructureVersionIfNeeded();
                    Commit(DeformationReleaseManifest.EarliestBoundaryForLegacyMarker(_state.Version), commit);
                    return true;
                }
                catch (Exception)
                {
                    _state.RestoreFrom(original);
                    _state.Status = DeformationDataMigrationStatus.InvalidData;
                    return false;
                }
            }
            if (_state.ReleaseIndex == DeformationReleaseManifest.Current &&
                _state.Version == DeformationDataVersion.CurrentDevelopment)
            {
                _state.Status = _state.HasIncompatibleBrushData
                    ? DeformationDataMigrationStatus.InvalidData : DeformationDataMigrationStatus.Ready;
                return false;
            }

            if (!DeformationReleaseManifest.MatchesSchema(_state.ReleaseIndex, _state.Version))
            {
                if (_state.Version == DeformationDataVersion.Unversioned)
                {
                    // Classification and its journal entry are a single atomic owner
                    // commit. No guessed exact tag is attached to the original data.
                    return legacy.TryAdvanceOneRelease(state =>
                        Commit(DeformationReleaseManifest.EarliestBoundaryForLegacyMarker(state.Version), commit));
                }
                return CommitWithoutPayloadChange(
                    DeformationReleaseManifest.EarliestBoundaryForLegacyMarker(_state.Version), commit);
            }

            int next = _state.ReleaseIndex + 1;
            if (DeformationReleaseManifest.RequiresLegacyStep(_state.ReleaseIndex))
                return legacy.TryAdvanceOneRelease(state => Commit(next, commit));

            // 1.4.0 -> 1.4.1 retains the old group schema. Its conversion to the
            // frozen current marker occurs at 1.4.2-rc.1, the first published tag
            // containing that marker. Every later release is an explicit no-op
            // for this schema, including the architecture-only 2.0 beta boundary.
            return CommitWithoutPayloadChange(next, commit);
        }

        private bool CommitWithoutPayloadChange(int next, Action<DeformationMigrationState> commit)
        {
            var original = _state.Copy();
            try
            {
                Commit(next, commit);
                return true;
            }
            catch (Exception)
            {
                _state.RestoreFrom(original);
                _state.Status = DeformationDataMigrationStatus.InvalidData;
                return false;
            }
        }

        private void Commit(int next, Action<DeformationMigrationState> commit)
        {
            if (next <= DeformationReleaseManifest.Unclassified || next > DeformationReleaseManifest.Current)
                throw new InvalidOperationException("Cannot commit an unknown migration boundary.");
            _beforeCommit?.Invoke(next);
            _state.ReleaseIndex = next;
            _state.CommitRequested = true;
            _state.Status = next == DeformationReleaseManifest.Current
                ? DeformationDataMigrationStatus.Ready : DeformationDataMigrationStatus.InProgress;
            commit?.Invoke(_state);
        }
    }
}
