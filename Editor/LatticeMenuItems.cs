#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class LatticeMenuItems
    {
        [MenuItem("GameObject/32ba/Lattice Deformer", false, 32)]
        private static void CreateLatticeDeformer(MenuCommand command)
        {
            var go = new GameObject("Lattice Deformer");
            Undo.RegisterCreatedObjectUndo(go, "Create Lattice Deformer");

            if (command.context is GameObject ctx)
            {
                GameObjectUtility.SetParentAndAlign(go, ctx);
            }

            go.AddComponent<LatticeDeformer>();
            Selection.activeGameObject = go;
        }
    }
}
#endif
