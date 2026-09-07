using System;
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    internal enum DeformerDataResolutionStatus { Embedded, Profile, IncompatibleProfile, InvalidProfile }

    internal readonly struct ResolvedDeformerData
    {
        // Embedded is borrowed. Profile groups belong to this resolver's cache.
        // Both views are synchronous; callers must not retain them across edits.
        internal readonly IReadOnlyList<DeformerGroup> Groups;
        internal readonly int ActiveGroupIndex;
        internal readonly DeformerDataResolutionStatus Status;
        internal readonly int ProfileRevision;

        internal ResolvedDeformerData(IReadOnlyList<DeformerGroup> groups, int activeGroupIndex,
            DeformerDataResolutionStatus status, int revision = 0)
        {
            Groups = groups;
            ActiveGroupIndex = activeGroupIndex;
            Status = status;
            ProfileRevision = revision;
        }
    }

    // One component owns one cache. Resolving data never changes the component,
    // its embedded list/selection, the Profile asset, or any Unity dirty state.
    internal sealed class DeformerDataResolver
    {
        private MeshDeformerProfile _profile;
        private List<DeformerGroup> _profileGroups;
        private List<DeformerGroup> _blockedGroups;
        private string _fingerprint;
        private int _profileActiveGroupIndex;
        private int _profileRevision;

        internal List<DeformerGroup> ProfileGroups => _profileGroups;

        internal List<DeformerGroup> BlockedGroups => _blockedGroups ??= new List<DeformerGroup>
        {
            new DeformerGroup { Name = "Incompatible Profile", Enabled = false }
        };

        internal void Invalidate() => _fingerprint = null;

        internal void Clear()
        {
            _profile = null;
            _profileGroups = null;
            _blockedGroups = null;
            _fingerprint = null;
        }

        internal ResolvedDeformerData Resolve(DeformerDataSource dataSource,
            IReadOnlyList<DeformerGroup> embeddedGroups, int embeddedActiveGroupIndex,
            MeshDeformerProfile profile, Mesh compatibilitySource)
        {
            if (dataSource != DeformerDataSource.Profile || profile == null)
                return new ResolvedDeformerData(embeddedGroups, embeddedActiveGroupIndex,
                    DeformerDataResolutionStatus.Embedded);

            if (!HasValidProfilePayload(profile, compatibilitySource != null ? compatibilitySource.vertexCount : -1))
            {
                Clear();
                return new ResolvedDeformerData(null, profile.SerializedActiveGroupIndex,
                    DeformerDataResolutionStatus.InvalidProfile);
            }

            if (profile.EvaluateCompatibility(compatibilitySource) == ProfileCompatibilityStatus.TopologyMismatch)
            {
                _profileGroups = null;
                _fingerprint = null;
                return new ResolvedDeformerData(BlockedGroups, 0, DeformerDataResolutionStatus.IncompatibleProfile);
            }

            string fingerprint = profile.GetContentFingerprint();
            if (!ReferenceEquals(_profile, profile) || _profileGroups == null ||
                !string.Equals(_fingerprint, fingerprint, StringComparison.Ordinal))
            {
                var payload = profile.CreateIndependentPayload();
                _profileGroups = payload.Groups;
                _profileActiveGroupIndex = payload.ActiveGroupIndex;
                _profile = profile;
                _blockedGroups = null;
                _fingerprint = fingerprint;
                unchecked { _profileRevision++; }
            }
            return new ResolvedDeformerData(_profileGroups, _profileActiveGroupIndex,
                DeformerDataResolutionStatus.Profile, _profileRevision);
        }

        internal static bool HasValidProfilePayload(MeshDeformerProfile profile, int vertexCount)
        {
            var groups = profile.SerializedGroups;
            int active = profile.SerializedActiveGroupIndex;
            if (groups == null || active < 0 || (groups.Count == 0 ? active != 0 : active >= groups.Count))
                return false;
            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                if (group == null || group.HasMalformedSerializedMetadata) return false;
                var layers = group.SerializedLayers;
                int activeLayer = group.SerializedActiveLayerIndex;
                if (layers == null || activeLayer < 0 ||
                    (layers.Count == 0 ? activeLayer != 0 : activeLayer >= layers.Count)) return false;
                for (int l = 0; l < layers.Count; l++)
                {
                    var layer = layers[l];
                    if (layer == null || layer.HasMalformedSerializedMetadata || layer.HasNonFiniteSerializedVertexData)
                        return false;
                    var settings = layer.SerializedSettings;
                    if (settings != null && (settings.HasUnsupportedFutureSerializationVersion || settings.HasMalformedSerializedShape))
                        return false;
                    int displacements = layer.SerializedBrushDisplacementCount;
                    int mask = layer.SerializedVertexMaskCount;
                    if (vertexCount >= 0 && ((displacements != 0 && displacements != vertexCount) ||
                        (mask != 0 && mask != vertexCount))) return false;
                }
            }
            return true;
        }
    }
}
