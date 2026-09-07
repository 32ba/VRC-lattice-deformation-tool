#if UNITY_EDITOR
using System;
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

        internal static bool Execute(LatticeDeformer deformer, string undoLabel, Func<LatticeDeformer, bool> edit)
        {
            if (deformer == null || edit == null || SerializedDeformerReader.Read(deformer).UsesProfile)
                return false;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoLabel);
            Undo.RegisterCompleteObjectUndo(deformer, undoLabel);
            try
            {
                if (!edit(deformer))
                {
                    Undo.RevertAllDownToGroup(undoGroup);
                    return false;
                }

                deformer.InvalidateCache();
                LatticePrefabUtility.MarkModified(deformer);
                Undo.CollapseUndoOperations(undoGroup);
                return true;
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                deformer.InvalidateCache();
                throw;
            }
        }
    }
}
#endif
