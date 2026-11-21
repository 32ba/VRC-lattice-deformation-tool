#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>Pure preflight shared by single commands and interactive edit sessions.</summary>
    internal static class DeformerAuthoringSource
    {
        internal static bool TryRead(LatticeDeformer owner, out SerializedDeformerData data,
            out Mesh source, out int topologyHash)
        {
            data = default; source = null; topologyHash = 0;
            if (owner == null || !AuthoringPreflightScope.HasValidSerializedAuthoringData(owner)) return false;
            data = SerializedDeformerReader.Read(owner);
            if (data.DataSource != DeformerDataSource.Embedded) return false;
            source = data.SourceMesh;
            if (source == null || source.vertexCount == 0 || !HasExpectedRendererMesh(owner, data, source) ||
                (data.SourceVertexCount > 0 && data.SourceVertexCount != source.vertexCount)) return false;
            topologyHash = AuthoringPreflightScope.TopologyHash(source);
            return topologyHash != 0 && (data.SourceTopologyHash == 0 || data.SourceTopologyHash == topologyHash);
        }

        internal static bool HasExpectedRendererMesh(LatticeDeformer owner, in SerializedDeformerData data, Mesh source)
        {
            Mesh displayed = data.SkinnedRenderer != null ? data.SkinnedRenderer.sharedMesh :
                data.MeshFilter != null ? data.MeshFilter.sharedMesh : null;
            return displayed != null && (displayed == source || ReferenceEquals(displayed, owner.RuntimeMesh));
        }
    }

    /// <summary>
    /// Memoizes the full-vertex authoring preflight and source topology hash for one synchronous,
    /// read-only Inspector refresh pass. Outside a scope every query is evaluated directly. The memo
    /// is discarded when the outermost scope ends and whenever an edit commits, so it never survives
    /// across frames or edits and the fail-closed checks stay current.
    /// </summary>
    internal readonly struct AuthoringPreflightScope : IDisposable
    {
        private static readonly Dictionary<LatticeDeformer, bool> s_validity = new();
        private static readonly Dictionary<Mesh, int> s_topology = new();
        private static int s_depth;
        private readonly bool _active;

        private AuthoringPreflightScope(bool active) { _active = active; }

        internal static bool IsActive => s_depth > 0;

        /// <summary>Starts a read-only pass. Nothing inside it may write deformer or mesh data.</summary>
        internal static AuthoringPreflightScope Begin()
        {
            s_depth++;
            return new AuthoringPreflightScope(true);
        }

        public void Dispose()
        {
            if (!_active || s_depth == 0) return;
            if (--s_depth == 0) Invalidate();
        }

        internal static void Invalidate()
        {
            s_validity.Clear();
            s_topology.Clear();
        }

        internal static bool HasValidSerializedAuthoringData(LatticeDeformer owner)
        {
            if (owner == null) return false;
            if (s_depth == 0) return owner.HasValidSerializedAuthoringData;
            if (s_validity.TryGetValue(owner, out bool valid)) return valid;
            valid = owner.HasValidSerializedAuthoringData;
            s_validity[owner] = valid;
            return valid;
        }

        internal static int TopologyHash(Mesh source)
        {
            if (s_depth == 0 || source == null) return SourceMeshTopology.Calculate(source);
            if (s_topology.TryGetValue(source, out int hash)) return hash;
            hash = SourceMeshTopology.Calculate(source);
            s_topology[source] = hash;
            return hash;
        }
    }
}
#endif
