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
            DeformerPlatformServices.CaptureLegacyMigrationRecordRollback = CaptureLegacyMigrationRecordRollback;
        }

        private static System.Action CaptureLegacyMigrationRecordRollback(UnityEngine.Object target)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(target)) return null;
            var root = PrefabUtility.GetNearestPrefabInstanceRoot(target);
            if (root == null) return null;
            var modifications = PrefabUtility.GetPropertyModifications(root) ?? System.Array.Empty<PropertyModification>();
            var snapshot = new PropertyModification[modifications.Length];
            for (int i = 0; i < modifications.Length; i++)
            {
                var entry = modifications[i];
                snapshot[i] = new PropertyModification
                {
                    target = entry.target, propertyPath = entry.propertyPath,
                    value = entry.value, objectReference = entry.objectReference
                };
            }
            // This synchronous transaction may record several properties on the
            // instance. Preserve its complete pre-step override list, including
            // unrelated existing overrides, when recording the step fails.
            return () =>
            {
                if (root != null) PrefabUtility.SetPropertyModifications(root, snapshot);
            };
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
