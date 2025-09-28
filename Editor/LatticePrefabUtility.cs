#if UNITY_EDITOR
using Net._32Ba.LatticeDeformationTool;
using UnityEditor;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class LatticePrefabUtility
    {
        public static void MarkModified(LatticeDeformer deformer)
        {
            if (deformer == null)
            {
                return;
            }

            EditorUtility.SetDirty(deformer);

            if (PrefabUtility.IsPartOfPrefabInstance(deformer))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(deformer);
            }
        }
    }
}
#endif
