#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Text.RegularExpressions;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class ReleaseCheckerPersistenceTests
    {
        private string _lastCheckKey, _latestKey, _previousLastCheck, _previousLatest;
        private bool _hadLastCheck, _hadLatest;
        private object[] _previousState;

        private static readonly string[] StateFields =
        {
            "<IsChecking>k__BackingField", "<CheckError>k__BackingField",
            "<LatestVersion>k__BackingField", "<HasNewVersion>k__BackingField"
        };

        [SetUp]
        public void SetUp()
        {
            _lastCheckKey = ReleaseChecker.GetLastCheckKey();
            _latestKey = ReleaseChecker.GetLatestVersionKey();
            _hadLastCheck = EditorPrefs.HasKey(_lastCheckKey);
            _hadLatest = EditorPrefs.HasKey(_latestKey);
            _previousLastCheck = EditorPrefs.GetString(_lastCheckKey, "");
            _previousLatest = EditorPrefs.GetString(_latestKey, "");
            _previousState = new object[StateFields.Length];
            for (int i = 0; i < StateFields.Length; i++) _previousState[i] = Field(StateFields[i]).GetValue(null);
        }

        [TearDown]
        public void TearDown()
        {
            Restore(_lastCheckKey, _hadLastCheck, _previousLastCheck);
            Restore(_latestKey, _hadLatest, _previousLatest);
            for (int i = 0; i < StateFields.Length; i++) Field(StateFields[i]).SetValue(null, _previousState[i]);
        }

        [Test]
        public void TryRestoreLatestVersion_ComparesThePersistedVersionWithTheInstalledOne()
        {
            Assert.That(ReleaseChecker.TryRestoreLatestVersion("2.1.0", "2.0.0", out string latest, out bool hasNew), Is.True);
            Assert.That(latest, Is.EqualTo("2.1.0"));
            Assert.That(hasNew, Is.True);

            // After the user updates, the persisted result no longer shows a notice.
            Assert.That(ReleaseChecker.TryRestoreLatestVersion(" 2.1.0 ", "2.1.0", out latest, out hasNew), Is.True);
            Assert.That(latest, Is.EqualTo("2.1.0"));
            Assert.That(hasNew, Is.False);

            Assert.That(ReleaseChecker.TryRestoreLatestVersion("", "2.0.0", out latest, out hasNew), Is.False);
            Assert.That(latest, Is.Null);
            Assert.That(hasNew, Is.False);
            Assert.That(ReleaseChecker.TryRestoreLatestVersion(null, "2.0.0", out _, out _), Is.False);
        }

        [Test]
        public void BuildFailedCheckRecord_RetriesAfterTheBackoffWithoutDelayingOrAdvancingADueSuccess()
        {
            var now = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Local);

            foreach (string stored in new[] { "", "not-a-binary-date", now.AddHours(-30).ToBinary().ToString(),
                         now.AddHours(5).ToBinary().ToString(), long.MaxValue.ToString() })
            {
                string record = ReleaseChecker.BuildFailedCheckRecord(stored, now);
                Assert.That(ReleaseChecker.ShouldCheckForUpdates(record, now), Is.False, stored);
                Assert.That(ReleaseChecker.ShouldCheckForUpdates(record,
                    now.AddHours(ReleaseChecker.FailureRetryHours).AddMinutes(-1)), Is.False, stored);
                Assert.That(ReleaseChecker.ShouldCheckForUpdates(record,
                    now.AddHours(ReleaseChecker.FailureRetryHours)), Is.True, stored);
            }

            // A recent success keeps its own, later, next-check time.
            string recentSuccess = now.AddHours(-2).ToBinary().ToString();
            Assert.That(ReleaseChecker.BuildFailedCheckRecord(recentSuccess, now), Is.EqualTo(recentSuccess));
        }

        [Test]
        public void LatestVersionKey_IsProjectScoped()
        {
            Assert.That(ReleaseChecker.BuildLatestVersionKey("abc123"),
                Is.EqualTo("Net32Ba.LatticeDeformationTool.LatestVersion.abc123"));
            Assert.That(ReleaseChecker.GetLatestVersionKey(),
                Is.EqualTo(ReleaseChecker.BuildLatestVersionKey(ReleaseChecker.GetProjectScopeSuffix())));
            Assert.That(ReleaseChecker.GetLatestVersionKey(), Is.Not.EqualTo(ReleaseChecker.GetLastCheckKey()));
        }

        [Test]
        public void SuccessfulCheck_IsRestoredAfterADomainReload()
        {
            Invoke("HandleSuccess", "999.0.0");
            Assert.That(EditorPrefs.GetString(_latestKey, ""), Is.EqualTo("999.0.0"));
            Assert.That(ReleaseChecker.ShouldCheckForUpdates(EditorPrefs.GetString(_lastCheckKey, ""), DateTime.Now), Is.False);

            // A domain reload clears the static state but keeps EditorPrefs.
            Field("<IsChecking>k__BackingField").SetValue(null, false);
            Field("<LatestVersion>k__BackingField").SetValue(null, null);
            Field("<HasNewVersion>k__BackingField").SetValue(null, false);
            Invoke("RestorePersistedResult");

            Assert.That(ReleaseChecker.LatestVersion, Is.EqualTo("999.0.0"));
            Assert.That(ReleaseChecker.HasNewVersion, Is.True);
        }

        [Test]
        public void FailedCheck_RecordsTheBackoffAndKeepsTheLastKnownResult()
        {
            EditorPrefs.DeleteKey(_lastCheckKey);
            Field("<LatestVersion>k__BackingField").SetValue(null, "999.0.0");
            Field("<HasNewVersion>k__BackingField").SetValue(null, true);
            LogAssert.Expect(LogType.Warning, new Regex(@"\[LatticeDeformationTool\] Update check failed: offline"));

            Invoke("HandleError", "offline");

            string record = EditorPrefs.GetString(_lastCheckKey, "");
            Assert.That(record, Is.Not.Empty);
            Assert.That(ReleaseChecker.ShouldCheckForUpdates(record, DateTime.Now), Is.False);
            Assert.That(ReleaseChecker.ShouldCheckForUpdates(record,
                DateTime.Now.AddHours(ReleaseChecker.FailureRetryHours).AddMinutes(1)), Is.True);
            Assert.That(ReleaseChecker.LatestVersion, Is.EqualTo("999.0.0"));
            Assert.That(ReleaseChecker.HasNewVersion, Is.True);
            Assert.That(ReleaseChecker.CheckError, Is.EqualTo("offline"));
        }

        [Test]
        public void EmptyResponse_RecordsTheBackoff()
        {
            EditorPrefs.DeleteKey(_lastCheckKey);
            Invoke("HandleSuccess", "");
            Assert.That(ReleaseChecker.CheckError, Is.EqualTo("Empty version response"));
            Assert.That(ReleaseChecker.ShouldCheckForUpdates(EditorPrefs.GetString(_lastCheckKey, ""), DateTime.Now), Is.False);
        }

        private static void Restore(string key, bool had, string value)
        {
            if (had) EditorPrefs.SetString(key, value);
            else EditorPrefs.DeleteKey(key);
        }

        private static FieldInfo Field(string name)
        {
            var field = typeof(ReleaseChecker).GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            return field;
        }

        private static void Invoke(string name, params object[] args)
        {
            var method = typeof(ReleaseChecker).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, name);
            method.Invoke(null, args);
        }
    }
}
#endif
