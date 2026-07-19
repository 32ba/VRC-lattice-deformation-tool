#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Runs legacy Brush Deformer migration only from safe delayed Editor callbacks.
    /// Serialization callbacks never create components, record Undo, or save assets.
    /// </summary>
    [InitializeOnLoad]
    internal static class LegacyBrushDeformerAutoMigration
    {
        private static readonly Queue<string> s_prefabQueue = new Queue<string>();
        private static readonly HashSet<string> s_queuedPrefabs =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> s_reportedFailures = new HashSet<string>();
        private static bool s_scanLoadedScenes;
        private static bool s_scanProjectPrefabs;
        private static bool s_scheduled;
        private static bool s_allowBatchExecution;
        private static bool s_hooksSubscribed;

        static LegacyBrushDeformerAutoMigration()
        {
            SubscribeHooks();
            if (Application.isBatchMode) return;

            QueueLoadedSceneScan();
            QueueProjectPrefabScan();
        }

        internal static IDisposable EnableEventExecutionForTests()
        {
            SubscribeHooks();
            ClearPendingState();
            s_allowBatchExecution = true;
            return new TestExecutionScope();
        }

        internal static void QueueImportedPrefabs(IEnumerable<string> paths)
        {
            if (Application.isBatchMode && !s_allowBatchExecution) return;
            if (paths == null) return;
            foreach (string path in paths)
            {
                QueuePrefab(path);
            }
            Schedule();
        }

        internal static bool TryMigrateScene(
            Scene scene,
            out int migratedCount,
            out string error)
        {
            return TryMigrateScene(
                scene,
                allowPreviewScene: false,
                editAssetPath: null,
                out migratedCount,
                out error);
        }

        private static bool TryMigrateScene(
            Scene scene,
            bool allowPreviewScene,
            string editAssetPath,
            out int migratedCount,
            out string error)
        {
            migratedCount = 0;
            error = null;
            if (!scene.IsValid() || !scene.isLoaded ||
                (!allowPreviewScene && EditorSceneManager.IsPreviewScene(scene)))
            {
                error = "The Scene is not loaded or cannot be edited safely.";
                return false;
            }

            var candidates = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<BrushDeformer>(true))
                .Where(NeedsMigration)
                .Distinct()
                .ToArray();
            if (candidates.Length == 0) return true;

            string path = string.IsNullOrEmpty(editAssetPath) ? scene.path : editAssetPath;
            if (!IsAssetEditable(path))
            {
                error = $"The Scene or Prefab is read-only: {path}";
                return false;
            }

            if (!BrushDeformerEditor.TryMigrateAll(candidates, out error)) return false;
            migratedCount = candidates.Length;
            return true;
        }

        internal static bool TryMigratePrefabAsset(
            string assetPath,
            out int migratedCount,
            out string error)
        {
            migratedCount = 0;
            error = null;
            if (!IsEditablePrefabPath(assetPath))
            {
                error = $"The Prefab is outside Assets or read-only: {assetPath}";
                return false;
            }

            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(assetPath);
                var candidates = root.GetComponentsInChildren<BrushDeformer>(true)
                    .Where(NeedsMigration)
                    .Where(component => !IsNestedPrefabContent(root, component))
                    .Distinct()
                    .ToArray();
                if (candidates.Length == 0) return true;

                if (!BrushDeformerEditor.TryMigrateAll(candidates, out error)) return false;
                if (PrefabUtility.SaveAsPrefabAsset(root, assetPath) == null)
                {
                    error = $"Unity could not save the migrated Prefab: {assetPath}";
                    return false;
                }

                migratedCount = candidates.Length;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (root != null) PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool NeedsMigration(BrushDeformer legacy)
        {
            return legacy != null && !LegacyBrushDeformerMigration.IsMigrationComplete(legacy);
        }

        private static bool IsNestedPrefabContent(GameObject root, BrushDeformer component)
        {
            if (root == null || component == null) return false;
            GameObject nearest = PrefabUtility.GetNearestPrefabInstanceRoot(component.gameObject);
            return nearest != null && nearest != root;
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            QueueLoadedSceneScan();
        }

        private static void OnPrefabStageOpened(PrefabStage stage)
        {
            QueueLoadedSceneScan();
        }

        private static void QueueLoadedSceneScan()
        {
            if (Application.isBatchMode && !s_allowBatchExecution) return;
            s_scanLoadedScenes = true;
            Schedule();
        }

        private static void QueueProjectPrefabScan()
        {
            if (Application.isBatchMode && !s_allowBatchExecution) return;
            s_scanProjectPrefabs = true;
            Schedule();
        }

        private static void DiscoverProjectPrefabs()
        {
            string scriptPath = ResolveLegacyScriptPath();
            if (string.IsNullOrEmpty(scriptPath)) return;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.GetDependencies(path, false)
                    .Any(dependency => string.Equals(
                        dependency,
                        scriptPath,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    QueuePrefab(path);
                }
            }
        }

        private static string ResolveLegacyScriptPath()
        {
            return MonoImporter.GetAllRuntimeMonoScripts()
                .Where(script => script != null && script.GetClass() == typeof(BrushDeformer))
                .Select(AssetDatabase.GetAssetPath)
                .FirstOrDefault(path => !string.IsNullOrEmpty(path));
        }

        private static void QueuePrefab(string path)
        {
            if (!IsCandidatePrefabPath(path) || !s_queuedPrefabs.Add(path)) return;
            s_prefabQueue.Enqueue(path);
        }

        private static void Schedule()
        {
            if ((Application.isBatchMode && !s_allowBatchExecution) || s_scheduled) return;
            s_scheduled = true;
            EditorApplication.delayCall += RunPending;
            EditorApplication.update += RunPending;
        }

        private static void RunPending()
        {
            EditorApplication.delayCall -= RunPending;
            EditorApplication.update -= RunPending;
            s_scheduled = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling ||
                EditorApplication.isUpdating)
            {
                Schedule();
                return;
            }

            if (s_scanLoadedScenes)
            {
                s_scanLoadedScenes = false;
                for (int index = 0; index < SceneManager.sceneCount; index++)
                {
                    Scene scene = SceneManager.GetSceneAt(index);
                    if (EditorSceneManager.IsPreviewScene(scene)) continue;
                    if (!TryMigrateScene(scene, out int migrated, out string error))
                    {
                        ReportFailure($"scene:{scene.handle}:{error}", error, null);
                    }
                    else if (migrated > 0)
                    {
                        SceneView.RepaintAll();
                    }
                }

                PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                if (prefabStage != null)
                {
                    if (!TryMigrateScene(
                            prefabStage.scene,
                            allowPreviewScene: true,
                            editAssetPath: prefabStage.assetPath,
                            out int stageMigrated,
                            out string stageError))
                    {
                        ReportFailure(
                            $"prefab-stage:{prefabStage.assetPath}:{stageError}",
                            stageError,
                            prefabStage.prefabContentsRoot);
                    }
                    else if (stageMigrated > 0)
                    {
                        SceneView.RepaintAll();
                    }
                }
            }

            if (s_scanProjectPrefabs)
            {
                s_scanProjectPrefabs = false;
                DiscoverProjectPrefabs();
            }

            if (s_prefabQueue.Count > 0)
            {
                string path = s_prefabQueue.Dequeue();
                s_queuedPrefabs.Remove(path);
                if (!TryMigratePrefabAsset(path, out _, out string error))
                {
                    ReportFailure($"prefab:{path}:{error}", error, AssetDatabase.LoadAssetAtPath<GameObject>(path));
                }
            }

            if (s_prefabQueue.Count > 0 || s_scanLoadedScenes || s_scanProjectPrefabs) Schedule();
        }

        private static void ReportFailure(string key, string error, UnityEngine.Object context)
        {
            if (string.IsNullOrEmpty(error) || !s_reportedFailures.Add(key)) return;
            Debug.LogWarning(
                $"Legacy Brush Deformer automatic migration was skipped without changing data. " +
                $"Review the component manually. {error}",
                context);
        }

        private static bool IsEditablePrefabPath(string path)
        {
            return IsCandidatePrefabPath(path) && IsAssetEditable(path);
        }

        private static bool IsCandidatePrefabPath(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                   path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) &&
                   path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAssetEditable(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return false;
            if (!AssetDatabase.IsOpenForEdit(path, StatusQueryOptions.UseCachedIfPossible)) return false;

            string fullPath = Path.GetFullPath(path);
            return !File.Exists(fullPath) ||
                   (File.GetAttributes(fullPath) & FileAttributes.ReadOnly) == 0;
        }

        private static void SubscribeHooks()
        {
            if (s_hooksSubscribed) return;
            s_hooksSubscribed = true;
            EditorApplication.hierarchyChanged += QueueLoadedSceneScan;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            PrefabStage.prefabStageOpened += OnPrefabStageOpened;
        }

        private static void ClearPendingState()
        {
            EditorApplication.delayCall -= RunPending;
            EditorApplication.update -= RunPending;
            s_prefabQueue.Clear();
            s_queuedPrefabs.Clear();
            s_reportedFailures.Clear();
            s_scanLoadedScenes = false;
            s_scanProjectPrefabs = false;
            s_scheduled = false;
        }

        private sealed class TestExecutionScope : IDisposable
        {
            public void Dispose()
            {
                ClearPendingState();
                s_allowBatchExecution = false;
            }
        }
    }

    internal sealed class LegacyBrushDeformerAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            LegacyBrushDeformerAutoMigration.QueueImportedPrefabs(
                (importedAssets ?? Array.Empty<string>())
                .Concat(movedAssets ?? Array.Empty<string>()));
        }
    }
}
#endif
