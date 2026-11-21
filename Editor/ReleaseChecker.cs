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
        // From version 2 the package is distributed only through VPM, so the update notice opens
        // the documentation, which explains how to update.
        internal const string UpdateGuideUrl = "https://vrc.32ba.net/docs/packages/vrc-lattice-deformation-tool/overview/";
        private const string LastCheckKeyPrefix = "Net32Ba.LatticeDeformationTool.LastVersionCheck";
        private const string LatestVersionKeyPrefix = "Net32Ba.LatticeDeformationTool.LatestVersion";
        private const double CheckIntervalHours = 24.0;
        // A failed check retries sooner than the success interval, but not on every compile.
        internal const double FailureRetryHours = 1.0;

        private static readonly VpmApiClient Api = new(PackageId);

        internal static string LatestVersion { get; private set; }
        internal static bool HasNewVersion { get; private set; }
        internal static bool IsChecking { get; private set; }
        internal static string CheckError { get; private set; }

        internal static event Action OnUpdateCheckCompleted;

        [ExcludeFromCodeCoverage]
        static ReleaseChecker()
        {
            EditorApplication.delayCall += () =>
            {
                RestorePersistedResult();
                CheckForUpdates();
            };
        }

        /// <summary>
        /// The last check time survives a domain reload, so the result it produced must survive too;
        /// otherwise the notice disappears until the next check is due.
        /// </summary>
        [ExcludeFromCodeCoverage]
        private static void RestorePersistedResult()
        {
            if (IsChecking || !string.IsNullOrEmpty(LatestVersion)) return;
            if (!TryRestoreLatestVersion(EditorPrefs.GetString(GetLatestVersionKey(), ""), GetCurrentVersion(),
                    out string latest, out bool hasNewVersion)) return;
            LatestVersion = latest;
            HasNewVersion = hasNewVersion;
            if (hasNewVersion) NotifyUpdateCheckCompleted();
        }

        internal static bool TryRestoreLatestVersion(string storedLatest, string currentVersion,
            out string latest, out bool hasNewVersion)
        {
            latest = null; hasNewVersion = false;
            if (string.IsNullOrWhiteSpace(storedLatest)) return false;
            latest = storedLatest.Trim();
            hasNewVersion = VersionUtility.IsNewerVersion(currentVersion, latest);
            return true;
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
                return;
            }

            // Keep the last known result visible while checking; a failed check must not hide it.
            IsChecking = true;
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
        internal static void OpenUpdateGuide()
        {
            Application.OpenURL(UpdateGuideUrl);
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
                    RecordFailedCheck();
                    return;
                }

                LatestVersion = latest;
                string current = GetCurrentVersion();
                EditorPrefs.SetString(GetLatestVersionKey(), latest);
                EditorPrefs.SetString(GetLastCheckKey(), DateTime.Now.ToBinary().ToString());

                HasNewVersion = VersionUtility.IsNewerVersion(current, latest);
                if (HasNewVersion)
                {
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
                RecordFailedCheck();
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

            try
            {
                RecordFailedCheck();
            }
            // EditorPrefs writes do not expose injectable failures.
#line hidden
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
#line default

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

        /// <summary>
        /// Stores a check time that makes the next automatic check due after the failure backoff,
        /// so offline projects do not hit the network on every compile. It never brings a check
        /// that is already due later (after a recent success) forward.
        /// </summary>
        private static void RecordFailedCheck()
        {
            string key = GetLastCheckKey();
            EditorPrefs.SetString(key, BuildFailedCheckRecord(EditorPrefs.GetString(key, ""), DateTime.Now));
        }

        internal static string BuildFailedCheckRecord(string stored, DateTime now)
        {
            var retryRecord = now.AddHours(FailureRetryHours - CheckIntervalHours);
            if (!string.IsNullOrEmpty(stored) && long.TryParse(stored, out long binary))
            {
                DateTime last;
                try { last = DateTime.FromBinary(binary); }
                catch (ArgumentException) { return retryRecord.ToBinary().ToString(); }
                // A later record keeps its later next-check time; a future record (clock change) is replaced.
                if (last > retryRecord && last <= now) return stored;
            }

            return retryRecord.ToBinary().ToString();
        }

        internal static string GetLatestVersionKey()
        {
            return BuildLatestVersionKey(GetProjectScopeSuffix());
        }

        internal static string BuildLatestVersionKey(string projectScopeSuffix)
        {
            return $"{LatestVersionKeyPrefix}.{projectScopeSuffix}";
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
