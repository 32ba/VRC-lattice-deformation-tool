using System;
using System.Collections.Generic;

namespace Net._32Ba.LatticeDeformationTool
{
    // This append-only journal is independent of the frozen legacy enum and the
    // Group/Layer schema number. Zero means the saved payload predates the journal.
    internal static class DeformationReleaseManifest
    {
        internal const int Unclassified = 0;
        internal const int LastLegacyEnumRelease = 14;
        internal const int UnversionedGroupSuccessor = 15; // 1.4.1
        internal const int FirstSharedCurrentMarker = 16; // 1.4.2-rc.1
        internal const int Current = 43;
        private static readonly string[] s_releases =
        {
            "0.0.1",
            "0.0.2",
            "0.0.3",
            "0.0.4",
            "0.0.5",
            "0.0.6",
            "1.0.0",
            "1.0.1",
            "1.1.0",
            "1.2.0",
            "1.2.1",
            "1.3.0",
            "1.3.1",
            "1.4.0",
            "1.4.1",
            "1.4.2-rc.1",
            "1.4.2-rc.2",
            "1.4.2-rc.3",
            "1.4.2-rc.4",
            "1.4.2-rc.5",
            "1.4.2",
            "1.4.3-rc.1",
            "1.4.3-rc.2",
            "1.4.3",
            "1.4.4-rc.1",
            "1.4.4-rc.2",
            "1.4.4-rc.3",
            "1.4.4-rc.4",
            "1.4.4-rc.5",
            "1.4.4-rc.6",
            "1.4.4",
            "1.4.5-rc.1",
            "1.4.5-rc.2",
            "1.4.5-rc.3",
            "1.4.5-rc.5",
            "1.4.5-beta.1",
            "1.4.5-beta.2",
            "1.4.5-beta.3",
            "1.4.5-beta.4",
            "1.4.5-beta.5",
            "1.4.5",
            "1.4.6-beta.1",
            "2.0.0-beta.1"
        };
        internal static IReadOnlyList<string> Releases { get; } = Array.AsReadOnly(s_releases);

        internal static string Label(int index) => index >= 1 && index <= Current
            ? s_releases[index - 1] : throw new ArgumentOutOfRangeException(nameof(index));

        internal static int EarliestBoundaryForLegacyMarker(DeformationDataVersion marker)
        {
            int value = (int)marker;
            if (value >= 1 && value <= LastLegacyEnumRelease) return value;
            if (marker == DeformationDataVersion.CurrentDevelopment) return FirstSharedCurrentMarker;
            return Unclassified;
        }

        internal static DeformationDataMigrationStatus ValidateCursor(int index)
        {
            if (index > Current) return DeformationDataMigrationStatus.UnsupportedFutureVersion;
            if (index < Unclassified) return DeformationDataMigrationStatus.InvalidData;
            return DeformationDataMigrationStatus.Ready;
        }

        // A Prefab Variant may inherit a newer base's journal while retaining its
        // own older schema overrides. The journal is a checkpoint, not a schema
        // assertion: rebase it from the validated raw marker before proceeding.
        internal static bool MatchesSchema(int index, DeformationDataVersion marker)
        {
            if (index < 1 || index > Current) return false;
            var expected = index <= LastLegacyEnumRelease ? (DeformationDataVersion)index
                : index == UnversionedGroupSuccessor ? DeformationDataVersion.V1_4_0
                : DeformationDataVersion.CurrentDevelopment;
            return marker == expected;
        }

        internal static bool RequiresLegacyStep(int fromIndex) => fromIndex >= 1 &&
            (fromIndex < LastLegacyEnumRelease || fromIndex == UnversionedGroupSuccessor);
    }
}
