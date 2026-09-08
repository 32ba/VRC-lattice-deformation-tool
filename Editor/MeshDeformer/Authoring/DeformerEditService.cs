#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
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

        internal static bool PasteGroup(LatticeDeformer deformer, string json, string undoLabel)
        {
            if (string.IsNullOrEmpty(json)) return false;
            var copy = JsonUtility.FromJson<DeformerGroup>(json);
            return copy != null && Execute(deformer, undoLabel, target => target.InsertGroup(copy) >= 0);
        }

        internal static bool AddLayer(LatticeDeformer deformer, MeshDeformerLayerType type, string undoLabel) =>
            Execute(deformer, undoLabel, target => target.AddLayer(layerType: type) >= 0);

        internal static bool DuplicateLayer(LatticeDeformer deformer, int index, string undoLabel) =>
            Execute(deformer, undoLabel, target => target.DuplicateLayer(index) >= 0);

        internal static bool RemoveLayer(LatticeDeformer deformer, int index, string undoLabel) =>
            Execute(deformer, undoLabel, target => DeformerStore.RemoveLayer(target, index));

        internal static bool MoveLayer(LatticeDeformer deformer, int from, int to, string undoLabel) =>
            Execute(deformer, undoLabel, target => DeformerStore.MoveLayer(target, from, to));

        internal static bool PasteLayer(LatticeDeformer deformer, string json, string undoLabel)
        {
            if (string.IsNullOrEmpty(json)) return false;
            var layer = new LatticeLayer();
            JsonUtility.FromJsonOverwrite(json, layer);
            return Execute(deformer, undoLabel, target => target.InsertLayer(layer) >= 0);
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
                    SerializedDeformerReader.Read(deformer).UsesProfile) return false;
                targets[i] = deformer;
            }

            return Commit(targets, undoLabel, edit);
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
