#if UNITY_EDITOR
using System;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    [InitializeOnLoad]
    internal static class ReleaseChecker
    {
        private const string PackageId = "net.32ba.lattice-deformation-tool";
        private const string ReleasePageUrl = "https://booth.pm/ja/items/7488375";
        private const string LastCheckKeyPrefix = "Net32Ba.LatticeDeformationTool.LastVersionCheck";
        private const double CheckIntervalHours = 24.0;

        private static readonly VpmApiClient Api = new(PackageId);

        internal static string LatestVersion { get; private set; }
        internal static bool HasNewVersion { get; private set; }
        internal static bool IsChecking { get; private set; }
        internal static string CheckError { get; private set; }

        internal static event Action OnUpdateCheckCompleted;

        static ReleaseChecker()
        {
            EditorApplication.delayCall += () => CheckForUpdates();
        }

        internal static void CheckForUpdates(bool forceCheck = false)
        {
            if (IsChecking)
            {
                return;
            }

            if (!forceCheck && !ShouldCheckForUpdates())
            {
                Debug.Log($"[LatticeDeformationTool] Skipping update check: checked within the last {CheckIntervalHours:0} hours for this project.");
                return;
            }

            IsChecking = true;
            HasNewVersion = false;
            CheckError = null;
            OnUpdateCheckCompleted?.Invoke();

            EditorCoroutine.Start(CheckRoutine());
        }

        internal static void OpenReleasePage()
        {
            Application.OpenURL(ReleasePageUrl);
        }

        internal static string GetCurrentVersion()
        {
            try
            {
                var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ReleaseChecker).Assembly);
                if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.version))
                {
                    return packageInfo.version;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LatticeDeformationTool] Failed to read package version: {ex.Message}");
            }

            return "0.0.0";
        }

        private static IEnumerator CheckRoutine()
        {
            yield return Api.GetLatestVersionCoroutine(HandleSuccess, HandleError);
        }

        private static void HandleSuccess(string latest)
        {
            IsChecking = false;

            if (string.IsNullOrEmpty(latest))
            {
                CheckError = "Empty version response";
                OnUpdateCheckCompleted?.Invoke();
                return;
            }

            LatestVersion = latest;
            string current = GetCurrentVersion();
            EditorPrefs.SetString(GetLastCheckKey(), DateTime.Now.ToBinary().ToString());

            if (VersionUtility.IsNewerVersion(current, latest))
            {
                HasNewVersion = true;
                Debug.Log($"[LatticeDeformationTool] New version available: {current} -> {latest}");
            }
            else
            {
                Debug.Log($"[LatticeDeformationTool] Package is up to date: {current}");
            }

            OnUpdateCheckCompleted?.Invoke();
        }

        private static void HandleError(string error)
        {
            IsChecking = false;
            CheckError = error;
            Debug.LogWarning($"[LatticeDeformationTool] Update check failed: {error}");
            OnUpdateCheckCompleted?.Invoke();
        }

        private static bool ShouldCheckForUpdates()
        {
            string stored = EditorPrefs.GetString(GetLastCheckKey(), "");
            if (string.IsNullOrEmpty(stored))
            {
                return true;
            }

            if (long.TryParse(stored, out long binary))
            {
                var last = DateTime.FromBinary(binary);
                return (DateTime.Now - last).TotalHours >= CheckIntervalHours;
            }

            return true;
        }

        private static string GetLastCheckKey()
        {
            return $"{LastCheckKeyPrefix}.{GetProjectScopeSuffix()}";
        }

        private static string GetProjectScopeSuffix()
        {
            string projectPath = Application.dataPath;
            if (string.IsNullOrEmpty(projectPath))
            {
                return "unknown";
            }

            using (var sha1 = SHA1.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(projectPath);
                byte[] hash = sha1.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }

    internal sealed class EditorCoroutine
    {
        private readonly IEnumerator _routine;
        private IEnumerator _nested;

        internal static EditorCoroutine Start(IEnumerator routine)
        {
            var coroutine = new EditorCoroutine(routine);
            EditorApplication.update += coroutine.Update;
            return coroutine;
        }

        private EditorCoroutine(IEnumerator routine)
        {
            _routine = routine;
        }

        private void Update()
        {
            if (_nested != null)
            {
                if (_nested.MoveNext())
                {
                    return;
                }

                _nested = null;
            }

            if (!_routine.MoveNext())
            {
                EditorApplication.update -= Update;
                return;
            }

            if (_routine.Current is IEnumerator nestedRoutine)
            {
                _nested = nestedRoutine;
            }
        }
    }
}
#endif
