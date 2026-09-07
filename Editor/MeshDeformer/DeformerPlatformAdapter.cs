using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    [InitializeOnLoad]
    internal static class DeformerPlatformAdapter
    {
        static DeformerPlatformAdapter()
        {
            DeformerPlatformServices.EditorMeshDataReader = MeshUtility.AcquireReadOnlyMeshData;
            DeformerPlatformServices.RecordLegacyMigration = RecordLegacyMigration;
        }

        private static void RecordLegacyMigration(UnityEngine.Object target)
        {
            UnityEditor.EditorUtility.SetDirty(target);
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(target))
            {
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            }

            if (target is Component component)
            {
                var scene = component.gameObject.scene;
                if (scene.IsValid() && scene.isLoaded)
                {
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                }
            }
        }
    }
}
