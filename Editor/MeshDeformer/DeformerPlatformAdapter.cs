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
            DeformerPlatformServices.AssignedMeshInstanceId = AssignedMeshInstanceId;
            DeformerPlatformServices.RecordLegacyMigration = RecordLegacyMigration;
            DeformerPlatformServices.CaptureLegacyMigrationRecordRollback = CaptureLegacyMigrationRecordRollback;
            DeformerPlatformServices.DirtyCountReader = target => EditorUtility.GetDirtyCount(target);
            // Undo/Redo restores serialized state wholesale; never let a cached token
            // survive it, whatever happens to the restored objects' dirty counts.
            Undo.undoRedoPerformed += DeformerChangeTokens.AdvanceGeneration;
            // Instances re-merged from their Prefab asset (apply, asset save) are rewritten
            // by the Prefab system; treat that like a restore as well.
            PrefabUtility.prefabInstanceUpdated += _ => DeformerChangeTokens.AdvanceGeneration();
            // Readable source copies are HideAndDontSave meshes owned by managed caches.
            AssemblyReloadEvents.beforeAssemblyReload += ReadableSourceCache.ReleaseAll;
        }

        private static int AssignedMeshInstanceId(Component target)
        {
            if (target == null || (target is not MeshFilter && target is not SkinnedMeshRenderer)) return 0;
            using var serialized = new SerializedObject(target);
            return serialized.FindProperty("m_Mesh")?.objectReferenceInstanceIDValue ?? 0;
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

    /// <summary>Any import, deletion or move can replace asset contents in place.</summary>
    internal sealed class DeformerChangeTokenAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if ((importedAssets?.Length ?? 0) + (deletedAssets?.Length ?? 0) +
                (movedAssets?.Length ?? 0) + (movedFromAssetPaths?.Length ?? 0) > 0)
                DeformerChangeTokens.AdvanceGeneration();
        }
    }
}
