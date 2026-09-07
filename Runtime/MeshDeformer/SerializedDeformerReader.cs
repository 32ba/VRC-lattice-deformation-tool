using System;
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    /// <summary>
    /// A borrowed view of authoring data, including malformed payloads. Reading it
    /// does not initialize, migrate, normalize, clone a Profile, or invalidate a
    /// cache. References are valid for the current synchronous operation only;
    /// this is not an immutable snapshot for asynchronous evaluation.
    /// </summary>
    internal readonly struct SerializedDeformerData
    {
        internal IReadOnlyList<DeformerGroup> EmbeddedGroups { get; }
        internal int EmbeddedActiveGroupIndex { get; }
        internal DeformerDataSource DataSource { get; }
        internal MeshDeformerProfile Profile { get; }
        internal SkinnedMeshRenderer SkinnedRenderer { get; }
        internal MeshFilter MeshFilter { get; }
        internal Mesh SourceMesh { get; }
        internal int SourceVertexCount { get; }
        internal int SourceTopologyHash { get; }

        internal bool UsesProfile => DataSource == DeformerDataSource.Profile && Profile != null;
        internal IReadOnlyList<DeformerGroup> Groups => UsesProfile
            ? Profile.SerializedGroups : EmbeddedGroups;
        internal int ActiveGroupIndex => UsesProfile
            ? Profile.SerializedActiveGroupIndex : EmbeddedActiveGroupIndex;

        internal SerializedDeformerData(
            IReadOnlyList<DeformerGroup> groups, int activeGroupIndex,
            DeformerDataSource dataSource, MeshDeformerProfile profile,
            SkinnedMeshRenderer skinnedRenderer, MeshFilter meshFilter,
            Mesh sourceMesh, int sourceVertexCount, int sourceTopologyHash)
        {
            EmbeddedGroups = groups;
            EmbeddedActiveGroupIndex = activeGroupIndex;
            DataSource = dataSource;
            Profile = profile;
            SkinnedRenderer = skinnedRenderer;
            MeshFilter = meshFilter;
            SourceMesh = sourceMesh;
            SourceVertexCount = sourceVertexCount;
            SourceTopologyHash = sourceTopologyHash;
        }
    }

    internal static class SerializedDeformerReader
    {
        internal static SerializedDeformerData Read(LatticeDeformer deformer)
        {
            if (deformer == null) throw new ArgumentNullException(nameof(deformer));
            return deformer.ReadSerializedData();
        }
    }
}
