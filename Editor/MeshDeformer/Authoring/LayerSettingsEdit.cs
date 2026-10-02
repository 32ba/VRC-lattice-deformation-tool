#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal enum LayerSettingsOperation { Resize, Reset, Clear, SplitLeft, SplitRight, FlipX, FlipY, FlipZ }

    /// <summary>A selected layer and source identity, revalidated before an atomic edit.</summary>
    internal sealed class LayerSettingsEdit
    {
        internal LatticeDeformer Owner { get; }
        private readonly DeformerGroup _group;
        private readonly LatticeLayer _layer;
        private readonly LatticeAsset _settings;
        private readonly Mesh _source;
        private readonly SkinnedMeshRenderer _skinned;
        private readonly MeshFilter _filter;
        private readonly int _groupIndex, _layerIndex, _topology;
        private readonly MeshDeformerLayerType _type;

        private LayerSettingsEdit(LatticeDeformer owner, in SerializedDeformerData raw,
            LatticeLayer layer, Mesh source, int topology)
        {
            Owner = owner; _group = raw.ActiveGroup; _layer = layer; _settings = layer.SerializedSettings;
            _source = source; _topology = topology; _type = layer.Type;
            _groupIndex = raw.ActiveGroupIndex; _layerIndex = raw.ActiveLayerIndex;
            _skinned = raw.SkinnedRenderer; _filter = raw.MeshFilter;
        }

        internal static bool TryReadActive(LatticeDeformer owner, out SerializedDeformerData raw,
            out LatticeLayer layer)
        {
            raw = default; layer = null;
            if (owner == null || !owner.HasValidSerializedAuthoringData) return false;
            raw = SerializedDeformerReader.Read(owner);
            if (raw.DataSource != DeformerDataSource.Embedded || raw.ActiveLayers == null ||
                raw.ActiveLayerIndex < 0 || raw.ActiveLayerIndex >= raw.ActiveLayers.Count) return false;
            layer = raw.ActiveLayers[raw.ActiveLayerIndex];
            return layer != null && (layer.Type == MeshDeformerLayerType.Lattice || layer.Type == MeshDeformerLayerType.Brush);
        }

        internal static LayerSettingsEdit Capture(LatticeDeformer owner)
        {
            if (!TryReadActive(owner, out var raw, out var layer) ||
                !DeformerAuthoringSource.TryRead(owner, out _, out var source, out int topology)) return null;
            return new LayerSettingsEdit(owner, raw, layer, source, topology);
        }

        private bool Matches()
        {
            return TryReadActive(Owner, out var raw, out var layer) &&
                DeformerAuthoringSource.TryRead(Owner, out _, out var source, out int topology) &&
                ReferenceEquals(source, _source) && topology == _topology &&
                raw.ActiveGroupIndex == _groupIndex && raw.ActiveLayerIndex == _layerIndex &&
                ReferenceEquals(raw.ActiveGroup, _group) && ReferenceEquals(layer, _layer) &&
                ReferenceEquals(layer.SerializedSettings, _settings) && layer.Type == _type &&
                ReferenceEquals(raw.SkinnedRenderer, _skinned) && ReferenceEquals(raw.MeshFilter, _filter);
        }

        internal bool Execute(LayerSettingsOperation operation, string label, Vector3Int grid = default)
            => ExecuteBatch(new[] { this }, operation, label, new[] { grid });

        internal static bool ExecuteBatch(IReadOnlyList<LayerSettingsEdit> edits, LayerSettingsOperation operation,
            string label, IReadOnlyList<Vector3Int> grids = null)
        {
            if (edits == null || edits.Count == 0 || !Enum.IsDefined(typeof(LayerSettingsOperation), operation) ||
                (operation == LayerSettingsOperation.Resize && (grids == null || grids.Count != edits.Count))) return false;
            var owners = new LatticeDeformer[edits.Count];
            var indices = new Dictionary<LatticeDeformer, int>();
            var sizes = new Vector3Int[edits.Count];
            bool anyChange = false;
            for (int i = 0; i < edits.Count; i++)
            {
                var edit = edits[i];
                if (edit == null || !edit.Matches() || indices.ContainsKey(edit.Owner)) return false;
                bool lattice = edit._type == MeshDeformerLayerType.Lattice;
                if ((operation == LayerSettingsOperation.Resize || operation == LayerSettingsOperation.Reset) &&
                    (!lattice || edit._settings == null)) return false;
                if (operation == LayerSettingsOperation.Clear && lattice) return false;
                if (operation == LayerSettingsOperation.Reset && !FiniteBounds(edit._source.bounds)) return false;
                if (operation == LayerSettingsOperation.Resize)
                {
                    sizes[i] = ClampGrid(grids[i]);
                    try { if (checked((long)sizes[i].x * sizes[i].y * sizes[i].z) > int.MaxValue) return false; }
                    catch (OverflowException) { return false; }
                    anyChange |= sizes[i] != edit._settings.GridSize;
                }
                else anyChange = true;
                owners[i] = edit.Owner; indices.Add(edit.Owner, i);
            }
            if (!anyChange) return false;
            return DeformerEditService.ExecuteBatch(owners, label, owner =>
            {
                int index = indices[owner]; var edit = edits[index];
                switch (operation)
                {
                    case LayerSettingsOperation.Resize: edit._settings.ResizeGrid(sizes[index]); break;
                    case LayerSettingsOperation.Reset:
                        edit._settings.LocalBounds = edit._source.bounds; edit._settings.ResetControlPoints(); break;
                    case LayerSettingsOperation.Clear: edit._layer.ClearBrushDisplacements(); break;
                    case LayerSettingsOperation.SplitLeft: owner.SplitLayerByAxis(edit._layerIndex, 0, false); break;
                    case LayerSettingsOperation.SplitRight: owner.SplitLayerByAxis(edit._layerIndex, 0, true); break;
                    case LayerSettingsOperation.FlipX: owner.FlipLayerByAxis(edit._layerIndex, 0); break;
                    case LayerSettingsOperation.FlipY: owner.FlipLayerByAxis(edit._layerIndex, 1); break;
                    case LayerSettingsOperation.FlipZ: owner.FlipLayerByAxis(edit._layerIndex, 2); break;
                }
                return true;
            });
        }

        internal static Vector3Int ClampGrid(Vector3Int size) =>
            new Vector3Int(Mathf.Max(2, size.x), Mathf.Max(2, size.y), Mathf.Max(2, size.z));
        private static bool FiniteBounds(Bounds bounds) => Finite(bounds.center) && Finite(bounds.size) &&
            bounds.size.x >= 0 && bounds.size.y >= 0 && bounds.size.z >= 0;
        private static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsInfinity(v.x) ||
            float.IsNaN(v.y) || float.IsInfinity(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.z));
    }
}
#endif
