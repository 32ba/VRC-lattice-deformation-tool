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
            out Mesh source, out int topologyHash) =>
            TryReadCore(owner, AuthoringPreflightScope.TopologyHash, out data, out source, out topologyHash);

        private static bool TryReadCore(LatticeDeformer owner, Func<Mesh, int> topology,
            out SerializedDeformerData data, out Mesh source, out int topologyHash)
        {
            data = default; source = null; topologyHash = 0;
            if (owner == null || !AuthoringPreflightScope.HasValidSerializedAuthoringData(owner)) return false;
            data = SerializedDeformerReader.Read(owner);
            if (data.DataSource != DeformerDataSource.Embedded) return false;
            source = data.SourceMesh;
            if (source == null || source.vertexCount == 0 || !HasExpectedRendererMesh(owner, data, source) ||
                (data.SourceVertexCount > 0 && data.SourceVertexCount != source.vertexCount)) return false;
            topologyHash = topology(source);
            return topologyHash != 0 && (data.SourceTopologyHash == 0 || data.SourceTopologyHash == topologyHash);
        }

        private readonly struct RowInputKey : IEquatable<RowInputKey>
        {
            private readonly ObjectChangeToken _owner, _source, _displayed, _skinned, _filter, _profile;
            private readonly int _dataRevision, _runtimeMesh, _generation;
            internal readonly bool IsValid;

            private RowInputKey(LatticeDeformer owner, in SerializedDeformerData data)
            {
                _owner = DeformerChangeTokens.Capture(owner);
                _source = DeformerChangeTokens.Capture(data.SourceMesh);
                Mesh displayed = data.SkinnedRenderer != null ? data.SkinnedRenderer.sharedMesh :
                    data.MeshFilter != null ? data.MeshFilter.sharedMesh : null;
                _displayed = DeformerChangeTokens.Capture(displayed);
                _skinned = DeformerChangeTokens.Capture(data.SkinnedRenderer);
                _filter = DeformerChangeTokens.Capture(data.MeshFilter);
                _profile = DeformerChangeTokens.Capture(data.Profile);
                _dataRevision = owner.DeformationDataRevision;
                var runtime = owner.RuntimeMesh;
                _runtimeMesh = runtime != null ? runtime.GetInstanceID() : 0;
                _generation = DeformerChangeTokens.Generation;
                IsValid = _owner.IsValid;
            }

            internal static RowInputKey Capture(LatticeDeformer owner) =>
                new RowInputKey(owner, SerializedDeformerReader.Read(owner));

            // Unassigned references (renderer, Profile) yield matching invalid tokens; compare
            // them by validity so an assignment or removal still changes the key.
            private static bool Same(in ObjectChangeToken a, in ObjectChangeToken b) =>
                a.IsValid ? a.Equals(b) : !b.IsValid;

            public bool Equals(RowInputKey other) =>
                IsValid && other.IsValid && Same(_owner, other._owner) && Same(_source, other._source) &&
                Same(_displayed, other._displayed) && Same(_skinned, other._skinned) &&
                Same(_filter, other._filter) && Same(_profile, other._profile) &&
                _dataRevision == other._dataRevision && _runtimeMesh == other._runtimeMesh &&
                _generation == other._generation;

            public override bool Equals(object obj) => obj is RowInputKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(_owner, _source, _displayed, _dataRevision, _generation);
        }

        private static readonly Dictionary<LatticeDeformer, (RowInputKey Key, bool Result)> s_rowInput = new();
        private static readonly Dictionary<int, (ObjectChangeToken Token, int Hash)> s_topology = new();

        // Test probes: full preflights and topology hashes computed for row input.
        internal static int RowInputPreflightsComputed { get; private set; }
        internal static int RowInputTopologyHashesComputed { get; private set; }

        /// <summary>
        /// The same preflight as <see cref="TryRead"/> for Group/Layer row input
        /// (name, enabled, weight). Every keystroke or slider step dispatches one
        /// ChangeEvent, so the result is reused while the deformer, its serialized and
        /// displayed source meshes, its renderer components, its Profile, its data
        /// revision and the global change generation are unchanged. The source
        /// topology hash is reused by Mesh change token across those recomputations.
        /// </summary>
        internal static bool TryReadForRowInput(LatticeDeformer owner)
        {
            if (owner == null) return false;
            var key = RowInputKey.Capture(owner);
            if (key.IsValid && s_rowInput.TryGetValue(owner, out var memo) && memo.Key.Equals(key))
                return memo.Result;
            bool result = TryReadCore(owner, CachedTopologyHash, out _, out _, out _);
            unchecked { RowInputPreflightsComputed++; }
            var after = RowInputKey.Capture(owner);
            if (!after.IsValid)
            {
                s_rowInput.Remove(owner);
                return result;
            }
            if (s_rowInput.Count >= 64) s_rowInput.Clear();
            s_rowInput[owner] = (after, result);
            return result;
        }

        internal static void InvalidateRowInputMemo()
        {
            s_rowInput.Clear();
        }

        private static int CachedTopologyHash(Mesh source)
        {
            if (source == null) return 0;
            int id = source.GetInstanceID();
            if (s_topology.TryGetValue(id, out var cached) && DeformerChangeTokens.Capture(source).Equals(cached.Token))
                return cached.Hash;
            int hash = SourceMeshTopology.Calculate(source);
            unchecked { RowInputTopologyHashesComputed++; }
            var token = DeformerChangeTokens.Capture(source);
            if (!token.IsValid)
            {
                s_topology.Remove(id);
                return hash;
            }
            if (s_topology.Count >= 64) s_topology.Clear();
            s_topology[id] = (token, hash);
            return hash;
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
