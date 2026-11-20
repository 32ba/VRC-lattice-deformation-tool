#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    // Clipboard provenance is session-only and tied to the exact copied JSON.
    // Equivalent separate assets are allowed; changed vertex ordering is not.
    internal sealed class DeformerClipboardSource
    {
        private readonly Mesh _mesh;
        private readonly MeshCompatibilityMetadata _metadata;
        private readonly string _json;

        private DeformerClipboardSource(Mesh mesh, MeshCompatibilityMetadata metadata, string json)
        { _mesh = mesh; _metadata = metadata; _json = json; }

        internal static DeformerClipboardSource Capture(LatticeDeformer owner, string json)
        {
            if (!DeformerAuthoringSource.TryRead(owner, out _, out var mesh, out _)) return null;
            using var lease = SourceMeshAccess.Acquire(mesh);
            return lease.Mesh == null ? null : new DeformerClipboardSource(mesh,
                MeshCompatibilityMetadata.Capture(lease.Mesh), json);
        }

        internal bool Matches(LatticeDeformer target, string json, out int count)
        {
            count = _metadata.VertexCount;
            if (_mesh == null || !string.Equals(_json, json, StringComparison.Ordinal) ||
                !DeformerAuthoringSource.TryRead(target, out _, out var destination, out _)) return false;
            using var originalLease = SourceMeshAccess.Acquire(_mesh);
            using var destinationLease = SourceMeshAccess.Acquire(destination);
            return Compatible(originalLease.Mesh) && Compatible(destinationLease.Mesh);
        }

        private bool Compatible(Mesh mesh)
        {
            var status = _metadata.Evaluate(mesh);
            return status == ProfileCompatibilityStatus.ExactMatch || status == ProfileCompatibilityStatus.CompatibleSourceDiffers;
        }
    }

    /// <summary>
    /// Commits one component edit as one Undo operation. Evaluation and UI refresh
    /// remain with the caller so repeated drawing cannot write authoring data.
    /// </summary>
    internal static class DeformerEditService
    {
        internal static bool AddGroup(LatticeDeformer deformer, string undoLabel) =>
            Execute(deformer, undoLabel, target => target.AddGroup() >= 0);

        internal static bool RemoveGroup(LatticeDeformer deformer, int index, string undoLabel) =>
            Execute(deformer, undoLabel, target => target.RemoveGroup(index));

        internal static bool MoveGroup(LatticeDeformer deformer, int oldIndex, int newIndex, string undoLabel) =>
            Execute(deformer, undoLabel, target => target.MoveGroup(oldIndex, newIndex));

        internal static bool DuplicateGroup(LatticeDeformer deformer, int index, string undoLabel)
        {
            if (deformer == null) return false;
            var groups = SerializedDeformerReader.Read(deformer).Groups;
            if (groups == null || index < 0 || index >= groups.Count || groups[index] == null) return false;
            var source = groups[index];
            var copy = JsonUtility.FromJson<DeformerGroup>(JsonUtility.ToJson(source));
            copy.Name = source.Name + " Copy";
            return Execute(deformer, undoLabel, target => target.InsertGroup(copy, index + 1) >= 0);
        }

        internal static bool PasteGroup(LatticeDeformer deformer, string json, string undoLabel,
            DeformerClipboardSource source = null)
        {
            if (string.IsNullOrEmpty(json)) return false;
            var copy = JsonUtility.FromJson<DeformerGroup>(json);
            if (copy == null || copy.HasMalformedSerializedMetadata || copy.SerializedLayers == null) return false;
            foreach (var layer in copy.SerializedLayers)
                if (!CanPasteLayer(deformer, layer, json, source)) return false;
            return Execute(deformer, undoLabel, target => target.InsertGroup(copy) >= 0);
        }

        internal static bool AddLayer(LatticeDeformer deformer, MeshDeformerLayerType type, string undoLabel) =>
            Execute(deformer, undoLabel, target => target.AddLayer(layerType: type) >= 0);

        internal static bool DuplicateLayer(LatticeDeformer deformer, int index, string undoLabel) =>
            Execute(deformer, undoLabel, target => target.DuplicateLayer(index) >= 0);

        internal static bool RemoveLayer(LatticeDeformer deformer, int index, string undoLabel) =>
            Execute(deformer, undoLabel, target => DeformerStore.RemoveLayer(target, index));

        internal static bool MoveLayer(LatticeDeformer deformer, int from, int to, string undoLabel) =>
            Execute(deformer, undoLabel, target => DeformerStore.MoveLayer(target, from, to));

        internal static bool PasteLayer(LatticeDeformer deformer, string json, string undoLabel,
            DeformerClipboardSource source = null)
        {
            if (string.IsNullOrEmpty(json)) return false;
            var layer = new LatticeLayer();
            JsonUtility.FromJsonOverwrite(json, layer);
            return CanPasteLayer(deformer, layer, json, source) &&
                   Execute(deformer, undoLabel, target => target.InsertLayer(layer) >= 0);
        }

        private static bool CanPasteLayer(LatticeDeformer target, LatticeLayer layer, string json,
            DeformerClipboardSource source)
        {
            if (layer == null || layer.HasMalformedSerializedMetadata || layer.HasNonFiniteSerializedVertexData)
                return false;
            var settings = layer.SerializedSettings;
            if (layer.Type == MeshDeformerLayerType.Lattice && settings == null) return false;
            if (settings != null && (settings.HasUnsupportedFutureSerializationVersion || settings.HasMalformedSerializedShape))
                return false;
            int displacements = layer.SerializedBrushDisplacementCount, mask = layer.SerializedVertexMaskCount;
            if (layer.Type != MeshDeformerLayerType.Brush && displacements == 0 && mask == 0) return true;
            if (source == null || !source.Matches(target, json, out int count)) return false;
            return (displacements == 0 || displacements == count) && (mask == 0 || mask == count);
        }

        internal static bool Execute(LatticeDeformer deformer, string undoLabel, Func<LatticeDeformer, bool> edit)
        {
            return ExecuteBatch(new[] { deformer }, undoLabel, edit);
        }

        internal static bool ExecuteBatch(IReadOnlyList<LatticeDeformer> deformers, string undoLabel,
            Func<LatticeDeformer, bool> edit)
        {
            if (deformers == null || deformers.Count == 0 || edit == null) return false;
            var targets = new LatticeDeformer[deformers.Count];
            var unique = new HashSet<LatticeDeformer>();
            for (int i = 0; i < deformers.Count; i++)
            {
                var deformer = deformers[i];
                if (deformer == null || !unique.Add(deformer) || !deformer.CanStartAuthoringEdit ||
                    !HasEditableStructure(SerializedDeformerReader.Read(deformer))) return false;
                targets[i] = deformer;
            }

            return Commit(targets, undoLabel, edit);
        }

        private static bool HasEditableStructure(in SerializedDeformerData data)
        {
            if (data.UsesProfile || data.Groups == null) return false;
            foreach (var group in data.Groups)
            {
                if (group?.SerializedLayers == null) return false;
                foreach (var layer in group.SerializedLayers)
                    if (layer == null || (layer.Type == MeshDeformerLayerType.Lattice &&
                                          layer.SerializedSettings == null)) return false;
            }
            return true;
        }

        // Profile assignment/copy changes the data source itself. Layer commands
        // continue to reject Profile mode; these commands validate the raw owner.
        internal static bool ExecuteDataSourceChange(LatticeDeformer deformer, string undoLabel,
            Func<LatticeDeformer, bool> edit)
        {
            if (deformer == null || edit == null || !deformer.HasValidSerializedAuthoringData) return false;
            return Commit(new[] { deformer }, undoLabel, edit);
        }

        private static bool Commit(LatticeDeformer[] targets, string undoLabel,
            Func<LatticeDeformer, bool> edit)
        {

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoLabel);
            Undo.RegisterCompleteObjectUndo(targets, undoLabel);
            try
            {
                foreach (var deformer in targets)
                {
                    if (!edit(deformer))
                    {
                        RollBack(targets, undoGroup);
                        return false;
                    }
                }

                foreach (var deformer in targets)
                {
                    deformer.InvalidateCache();
                    LatticePrefabUtility.MarkModified(deformer);
                }
                Undo.CollapseUndoOperations(undoGroup);
                return true;
            }
            catch
            {
                RollBack(targets, undoGroup);
                throw;
            }
        }

        private static void RollBack(IReadOnlyList<LatticeDeformer> deformers, int undoGroup)
        {
            Undo.RevertAllDownToGroup(undoGroup);
            foreach (var deformer in deformers)
                if (deformer != null) deformer.InvalidateCache();
        }
    }
}
#endif
