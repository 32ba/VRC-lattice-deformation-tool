#if UNITY_EDITOR
using UnityEditor;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    // Retain inspection of published assets without exposing new authoring paths.
    [CustomEditor(typeof(MeshDeformerProfile))]
    internal sealed class LegacyProfileAssetInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            using (new EditorGUI.DisabledScope(true)) DrawDefaultInspector();
        }
    }

    [CustomEditor(typeof(ClearanceScanSet))]
    internal sealed class LegacyScanSetAssetInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            using (new EditorGUI.DisabledScope(true)) DrawDefaultInspector();
        }
    }
}
#endif
