#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>Inspector-local drafts keyed by owner/group/layer identity, never serialized indices.</summary>
    internal sealed class PendingLatticeGrid : IDisposable
    {
        private sealed class Draft
        {
            internal LatticeAsset Settings;
            internal Vector3Int Original, Pending;
        }
        private readonly Dictionary<(LatticeDeformer, DeformerGroup, LatticeLayer), Draft> _drafts = new();
        private bool _disposed;

        private Draft Read(LatticeDeformer owner)
        {
            if (_disposed || !LayerSettingsEdit.TryReadActive(owner, out var raw, out var layer) ||
                layer.Type != MeshDeformerLayerType.Lattice || layer.SerializedSettings == null) return null;
            var key = (owner, raw.ActiveGroup, layer);
            var settings = layer.SerializedSettings;
            if (!_drafts.TryGetValue(key, out var draft) || !ReferenceEquals(draft.Settings, settings) ||
                draft.Original != settings.GridSize)
            {
                draft = new Draft { Settings = settings, Original = settings.GridSize, Pending = settings.GridSize };
                _drafts[key] = draft;
            }
            return draft;
        }

        internal bool TryGet(LatticeDeformer owner, out Vector3Int pending)
        {
            var draft = Read(owner); pending = draft?.Pending ?? default; return draft != null;
        }

        internal bool Set(LatticeDeformer owner, Vector3Int pending)
        {
            var draft = Read(owner); if (draft == null) return false;
            draft.Pending = LayerSettingsEdit.ClampGrid(pending); return true;
        }

        internal void Revert(IReadOnlyList<LatticeDeformer> owners)
        {
            foreach (var owner in owners) { var draft = Read(owner); if (draft != null) draft.Pending = draft.Original; }
        }

        internal bool Apply(IReadOnlyList<LatticeDeformer> owners, string label)
        {
            if (_disposed || owners == null || owners.Count == 0) return false;
            var commands = new LayerSettingsEdit[owners.Count]; var grids = new Vector3Int[owners.Count];
            for (int i = 0; i < owners.Count; i++)
            {
                if (!TryGet(owners[i], out grids[i])) return false;
                commands[i] = LayerSettingsEdit.Capture(owners[i]);
            }
            if (!LayerSettingsEdit.ExecuteBatch(commands, LayerSettingsOperation.Resize, label, grids)) return false;
            Revert(owners); return true;
        }

        public void Dispose() { _disposed = true; _drafts.Clear(); }
    }
}
#endif
