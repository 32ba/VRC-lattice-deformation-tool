#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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

        [ExcludeFromCodeCoverage]
        static ReleaseChecker()
        {
            EditorApplication.delayCall += () => CheckForUpdates();
        }

        [ExcludeFromCodeCoverage]
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
            try
            {
                NotifyUpdateCheckCompleted();
                EditorCoroutine.Start(CheckRoutine(), HandleCoroutineException);
            }
            // Unity logging and in-memory assignment do not expose injectable failures.
#line hidden
            catch (Exception ex)
            {
                HandleCoroutineException(ex);
            }
        }

        [ExcludeFromCodeCoverage]
        internal static void OpenReleasePage()
        {
            Application.OpenURL(ReleasePageUrl);
        }

        [ExcludeFromCodeCoverage]
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

        [ExcludeFromCodeCoverage]
        private static IEnumerator CheckRoutine()
        {
            yield return Api.GetLatestVersionCoroutine(HandleSuccess, HandleError);
        }

        private static void HandleSuccess(string latest)
        {
            IsChecking = false;
            try
            {
                if (string.IsNullOrEmpty(latest))
                {
                    CheckError = "Empty version response";
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
            }
            catch (Exception ex)
            {
                CheckError = ex.Message;
                Debug.LogException(ex);
            }
#line default
            finally
            {
                NotifyUpdateCheckCompleted();
            }
        }

        private static void HandleError(string error)
        {
            IsChecking = false;
            try
            {
                CheckError = error;
                Debug.LogWarning($"[LatticeDeformationTool] Update check failed: {error}");
            }
#line hidden
            catch (Exception ex)
            {
                CheckError = ex.Message;
                Debug.LogException(ex);
            }
#line default
            finally
            {
                NotifyUpdateCheckCompleted();
            }
        }

        private static void HandleCoroutineException(Exception exception)
        {
            IsChecking = false;
            CheckError = exception?.Message ?? "Update check coroutine failed";
            if (exception != null)
            {
                Debug.LogException(exception);
            }

            NotifyUpdateCheckCompleted();
        }

        private static void NotifyUpdateCheckCompleted()
        {
            var handlers = OnUpdateCheckCompleted;
            if (handlers == null)
            {
                return;
            }

            foreach (Action handler in handlers.GetInvocationList())
            {
                try
                {
                    handler();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }

        private static bool ShouldCheckForUpdates()
        {
            string stored = EditorPrefs.GetString(GetLastCheckKey(), "");
            if (string.IsNullOrEmpty(stored))
            {
                return true;
            }

            return ShouldCheckForUpdates(stored, DateTime.Now);
        }

        internal static bool ShouldCheckForUpdates(string stored, DateTime now)
        {
            if (string.IsNullOrEmpty(stored))
            {
                return true;
            }

            if (long.TryParse(stored, out long binary))
            {
                var last = DateTime.FromBinary(binary);
                return (now - last).TotalHours >= CheckIntervalHours;
            }

            return true;
        }

        internal static string GetLastCheckKey()
        {
            return BuildLastCheckKey(GetProjectScopeSuffix());
        }

        internal static string BuildLastCheckKey(string projectScopeSuffix)
        {
            return $"{LastCheckKeyPrefix}.{projectScopeSuffix}";
        }

        internal static string GetProjectScopeSuffix()
        {
            string projectPath = Application.dataPath;
            return BuildProjectScopeSuffix(projectPath);
        }

        internal static string BuildProjectScopeSuffix(string projectPath)
        {
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

    [ExcludeFromCodeCoverage]
    internal sealed class EditorCoroutine
    {
        private readonly Stack<IEnumerator> _routines = new Stack<IEnumerator>();
        private readonly Action<Exception> _onException;
        private object _yielded;

        internal static EditorCoroutine Start(IEnumerator routine, Action<Exception> onException = null)
        {
            var coroutine = new EditorCoroutine(routine, onException);
            EditorApplication.update += coroutine.Update;
            return coroutine;
        }

        private EditorCoroutine(IEnumerator routine, Action<Exception> onException)
        {
            if (routine == null) throw new ArgumentNullException(nameof(routine));
            _routines.Push(routine);
            _onException = onException;
        }

        private void Update()
        {
            try
            {
                if (!IsYieldComplete(_yielded))
                {
                    return;
                }
                _yielded = null;

                while (_routines.Count > 0)
                {
                    IEnumerator routine = _routines.Peek();
                    if (!routine.MoveNext())
                    {
                        _routines.Pop();
                        continue;
                    }

                    object yielded = routine.Current;
                    if (yielded is AsyncOperation || yielded is CustomYieldInstruction)
                    {
                        _yielded = yielded;
                        return;
                    }

                    if (yielded is IEnumerator nestedRoutine)
                    {
                        _routines.Push(nestedRoutine);
                        continue;
                    }

                    // null and ordinary YieldInstruction values resume on the next
                    // editor update, matching Unity coroutine frame semantics.
                    _yielded = yielded;
                    return;
                }

                EditorApplication.update -= Update;
            }
            catch (Exception ex)
            {
                EditorApplication.update -= Update;
                try
                {
                    _onException?.Invoke(ex);
                }
                catch (Exception callbackException)
                {
                    Debug.LogException(callbackException);
                }
            }
        }

        internal static bool IsYieldComplete(object yielded)
        {
            if (yielded is AsyncOperation asyncOperation)
            {
                return asyncOperation.isDone;
            }

            if (yielded is CustomYieldInstruction customYieldInstruction)
            {
                return !customYieldInstruction.keepWaiting;
            }

            return true;
        }
    }
}
#endif
