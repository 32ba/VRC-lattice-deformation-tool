#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Unity's serialized authoring adapter. The edit service owns Undo and commit
    /// notifications; this type preserves the Inspector's existing selection rules.
    /// </summary>
    internal static class DeformerStore
    {
        internal static bool MoveLayer(LatticeDeformer deformer, int from, int to)
        {
            using var serialized = new SerializedObject(deformer);
            if (!TryGetActiveLayers(deformer, serialized, out var layers, out var active) ||
                from < 0 || to < 0 || from >= layers.arraySize || to >= layers.arraySize || from == to)
                return false;
            int selected = Mathf.Clamp(active.intValue, 0, layers.arraySize - 1);
            layers.MoveArrayElement(from, to);
            if (selected == from) selected = to;
            else if (from < selected && to >= selected) selected--;
            else if (from > selected && to <= selected) selected++;
            active.intValue = selected;
            return serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static bool RemoveLayer(LatticeDeformer deformer, int index)
        {
            using var serialized = new SerializedObject(deformer);
            if (!TryGetActiveLayers(deformer, serialized, out var layers, out var active) ||
                index < 0 || index >= layers.arraySize) return false;
            layers.DeleteArrayElementAtIndex(index);
            // Keep the baseline Inspector's numeric selection and clamp it after
            // deleting. The public component API retains its separate contract.
            active.intValue = Mathf.Clamp(active.intValue, 0, Mathf.Max(0, layers.arraySize - 1));
            return serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool TryGetActiveLayers(LatticeDeformer deformer, SerializedObject serialized,
            out SerializedProperty layers, out SerializedProperty active)
        {
            layers = null;
            active = null;
            var data = SerializedDeformerReader.Read(deformer);
            if (data.UsesProfile || data.Groups == null || data.ActiveGroupIndex < 0 ||
                data.ActiveGroupIndex >= data.Groups.Count || data.Groups[data.ActiveGroupIndex] == null)
                return false;
            var groups = serialized.FindProperty("_groups");
            if (groups == null || data.ActiveGroupIndex >= groups.arraySize) return false;
            var group = groups.GetArrayElementAtIndex(data.ActiveGroupIndex);
            layers = group.FindPropertyRelative("_layers");
            active = group.FindPropertyRelative("_activeLayerIndex");
            return layers != null && active != null;
        }
    }
}
#endif
