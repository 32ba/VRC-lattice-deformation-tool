#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.TestTools;
using Net._32Ba.LatticeDeformationTool.Editor;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class EditorUtilitiesCoreTests
    {
        [TestCase("1.2.3", "1.2.4", true)]
        [TestCase("v1.2.3", "V1.3.0", true)]
        [TestCase("1.2", "1.2.1", true)]
        [TestCase("1.2.3-preview.1", "1.2.3", false)]
        [TestCase("2.0.0", "1.9.9", false)]
        [TestCase("", "1.0.0", false)]
        [TestCase("1.0.0", "", false)]
        public void VersionUtility_IsNewerVersion_HandlesCommonVersionForms(
            string current,
            string latest,
            bool expected)
        {
            Assert.That(VersionUtility.IsNewerVersion(current, latest), Is.EqualTo(expected));
        }

        [Test]
        public void VersionUtility_IsNewerVersion_ReturnsFalseAndLogsWarningForInvalidVersion()
        {
            LogAssert.Expect(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex(
                    @"\[LatticeDeformationTool\] Failed to compare versions 'invalid' and '1\.0\.0'"));

            Assert.That(VersionUtility.IsNewerVersion("invalid", "1.0.0"), Is.False);
        }

        [TestCase(null, "Unknown")]
        [TestCase("", "Unknown")]
        [TestCase("1.2.3", "v1.2.3")]
        [TestCase("v1.2.3", "v1.2.3")]
        [TestCase("V1.2.3", "V1.2.3")]
        public void VersionUtility_FormatVersion_AddsPrefixWhenNeeded(string input, string expected)
        {
            Assert.That(VersionUtility.FormatVersion(input), Is.EqualTo(expected));
        }

        [Test]
        public void LatticeLocalization_ParsePo_HandlesMultilineAndEscapedValues()
        {
            var catalog = LatticeLocalization.ParsePo(new[]
            {
                "# comment",
                "msgid \"simple\"",
                "msgstr \"Simple Value\"",
                "msgid \"multi\"",
                "\"line\"",
                "msgstr \"first\\n\"",
                "\"second\\\"quoted\\\"\"",
                "msgid \"empty\"",
                "msgstr \"\""
            });

            Assert.That(catalog["simple"], Is.EqualTo("Simple Value"));
            Assert.That(catalog["multiline"], Is.EqualTo("first\nsecond\"quoted\""));
            Assert.That(catalog["empty"], Is.EqualTo(""));
        }

        [Test]
        public void LatticeLocalization_ParsePo_IgnoresMalformedLines()
        {
            var catalog = LatticeLocalization.ParsePo(new[]
            {
                "msgid no_quote",
                "msgstr no_quote",
                "msgid \"valid\"",
                "msgstr \"value\"",
                "msgid \"unterminated",
                "msgstr \"ignored\""
            });

            Assert.That(catalog["valid"], Is.EqualTo("value"));
            Assert.That(catalog.ContainsKey("unterminated"), Is.False);
        }

        [Test]
        public void LatticeLocalization_Content_ReturnsKeyWhenMissingAndNullTooltip()
        {
            bool previousTooltips = LatticeLocalization.ShowTooltips;
            try
            {
                LatticeLocalization.ShowTooltips = true;
                var content = LatticeLocalization.Content("__missing_key__");

                Assert.That(content.text, Is.EqualTo("__missing_key__"));
                Assert.That(content.tooltip, Is.Null);
            }
            finally
            {
                LatticeLocalization.ShowTooltips = previousTooltips;
            }
        }

        [Test]
        public void LatticeLocalization_Content_UsesExplicitTooltipKeyAndToggle()
        {
            bool previousTooltips = LatticeLocalization.ShowTooltips;
            try
            {
                LatticeLocalization.ShowTooltips = false;
                var withoutTooltip = LatticeLocalization.Content("__missing_key__", "__missing_tooltip__");
                Assert.That(withoutTooltip.text, Is.EqualTo("__missing_key__"));
                Assert.That(withoutTooltip.tooltip, Is.Null);

                LatticeLocalization.ShowTooltips = true;
                Assert.That(LatticeLocalization.Tooltip(null), Is.Null);
                Assert.That(LatticeLocalization.Content(null).text, Is.Null);
            }
            finally
            {
                LatticeLocalization.ShowTooltips = previousTooltips;
            }
        }

        [Test]
        public void LatticeLocalization_CurrentLanguage_FallsBackAndRaisesChange()
        {
            var previous = LatticeLocalization.CurrentLanguage;
            var key = "Net32Ba.LatticeLocalization.Language";
            var previousRaw = EditorPrefs.GetString(key, "");
            int changed = 0;
            void OnChanged() => changed++;

            LatticeLocalization.LanguageChanged += OnChanged;
            try
            {
                EditorPrefs.SetString(key, "__invalid_language__");
                Assert.That(LatticeLocalization.CurrentLanguage, Is.EqualTo(LatticeLocalization.Language.English));

                LatticeLocalization.CurrentLanguage = LatticeLocalization.Language.Korean;
                LatticeLocalization.CurrentLanguage = LatticeLocalization.Language.Korean;

                Assert.That(LatticeLocalization.CurrentLanguage, Is.EqualTo(LatticeLocalization.Language.Korean));
                Assert.That(changed, Is.EqualTo(previous == LatticeLocalization.Language.Korean ? 0 : 1));
                Assert.That(LatticeLocalization.DisplayNames.Length, Is.EqualTo(5));
            }
            finally
            {
                LatticeLocalization.CurrentLanguage = previous;
                if (string.IsNullOrEmpty(previousRaw))
                {
                    EditorPrefs.DeleteKey(key);
                }
                else
                {
                    EditorPrefs.SetString(key, previousRaw);
                }
                LatticeLocalization.LanguageChanged -= OnChanged;
            }
        }

        [Test]
        public void LatticeLocalization_Tr_LoadsCatalogAndFallsBackToEnglish()
        {
            var previousLanguage = LatticeLocalization.CurrentLanguage;
            var root = Path.Combine(Path.GetTempPath(), "lattice-localization-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Editor", "Localization"));
                File.WriteAllLines(
                    Path.Combine(root, "Editor", "Localization", "en.po"),
                    new[]
                    {
                        "msgid \"hello\"",
                        "msgstr \"Hello\"",
                        "msgid \"hello.tooltip\"",
                        "msgstr \"Tooltip\"",
                        "msgid \"englishOnly\"",
                        "msgstr \"English fallback\""
                    });
                File.WriteAllLines(
                    Path.Combine(root, "Editor", "Localization", "ja.po"),
                    new[]
                    {
                        "msgid \"hello\"",
                        "msgstr \"こんにちは\""
                    });

                SetLocalizationPackageRoot(root);
                ClearLocalizationCatalogs();

                LatticeLocalization.CurrentLanguage = LatticeLocalization.Language.Japanese;

                Assert.That(LatticeLocalization.Tr("hello"), Is.EqualTo("こんにちは"));
                Assert.That(LatticeLocalization.Tr("englishOnly"), Is.EqualTo("English fallback"));
                Assert.That(LatticeLocalization.Content("hello").tooltip, Is.EqualTo("Tooltip"));
            }
            finally
            {
                LatticeLocalization.CurrentLanguage = previousLanguage;
                SetLocalizationPackageRoot(null);
                ClearLocalizationCatalogs();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public void LatticeLocalization_AllCatalogsContainShowWireframeLabelAndTooltip()
        {
            var method = typeof(LatticeLocalization).GetMethod(
                "EnsureCatalogLoaded",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            foreach (LatticeLocalization.Language language in Enum.GetValues(typeof(LatticeLocalization.Language)))
            {
                var catalog = method.Invoke(null, new object[] { language, true })
                    as IReadOnlyDictionary<string, string>;

                Assert.That(catalog, Is.Not.Null, language.ToString());
                Assert.That(catalog[LocKey.ShowWireframe], Is.Not.Null.And.Not.Empty, language.ToString());
                Assert.That(
                    catalog[LocKey.ShowWireframe + ".tooltip"],
                    Is.Not.Null.And.Not.Empty,
                    language.ToString());
            }
        }

        [Test]
        public void LatticeLocalization_AllCatalogsContainLegacyBrushMigrationMessages()
        {
            var method = typeof(LatticeLocalization).GetMethod(
                "EnsureCatalogLoaded",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            string[] keys =
            {
                LocKey.LegacyBrushMigrationWarning,
                LocKey.MigrateLegacyBrush,
                LocKey.LegacyBrushMigrationSucceeded,
                LocKey.LegacyBrushMigrationFailed
            };

            foreach (LatticeLocalization.Language language in Enum.GetValues(typeof(LatticeLocalization.Language)))
            {
                var catalog = method.Invoke(null, new object[] { language, true })
                    as IReadOnlyDictionary<string, string>;

                Assert.That(catalog, Is.Not.Null, language.ToString());
                foreach (string key in keys)
                {
                    Assert.That(catalog.ContainsKey(key), Is.True, $"{language}: {key}");
                    Assert.That(catalog[key], Is.Not.Null.And.Not.Empty, $"{language}: {key}");
                    Assert.That(catalog.ContainsKey(key + ".tooltip"), Is.True, $"{language}: {key}.tooltip");
                    Assert.That(
                        catalog[key + ".tooltip"],
                        Is.Not.Null.And.Not.Empty,
                        $"{language}: {key}.tooltip");
                }

                Assert.That(
                    catalog[LocKey.LegacyBrushMigrationFailed],
                    Does.Contain("{0}"),
                    $"{language}: failure reason placeholder");
            }
        }

        [Test]
        public void LatticeLocalization_AllPoFilesDeclareIdenticalMessageIds()
        {
            string[] relativePaths =
            {
                "Editor/Localization/en.po",
                "Editor/Localization/ja.po",
                "Editor/Localization/ko.po",
                "Editor/Localization/zh-Hans.po",
                "Editor/Localization/zh-Hant.po"
            };

            var keySets = relativePaths.ToDictionary(
                relativePath => relativePath,
                relativePath =>
                {
                    string absolutePath = InvokeLocalizationPrivate<string>(
                        "ResolveCatalogPath",
                        relativePath);
                    Assert.That(absolutePath, Is.Not.Null.And.Not.Empty, relativePath);
                    Assert.That(File.Exists(absolutePath), Is.True, relativePath);
                    return LatticeLocalization.ParsePo(File.ReadAllLines(absolutePath))
                        .Keys
                        .ToHashSet(StringComparer.Ordinal);
                });

            var baseline = keySets[relativePaths[0]];
            foreach (string relativePath in relativePaths.Skip(1))
            {
                var current = keySets[relativePath];
                Assert.That(
                    baseline.Except(current).OrderBy(key => key),
                    Is.Empty,
                    $"{relativePath} is missing msgid values declared by {relativePaths[0]}");
                Assert.That(
                    current.Except(baseline).OrderBy(key => key),
                    Is.Empty,
                    $"{relativePath} declares msgid values missing from {relativePaths[0]}");
            }
        }

        [Test]
        public void LatticeLocalization_PrivateLoadCatalog_HandlesMissingAndInvalidRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "lattice-localization-missing-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                SetLocalizationPackageRoot(root);
                ClearLocalizationCatalogs();

                LogAssert.Expect(LogType.Warning, "Localization catalogue not found: Editor/Localization/missing.po");
                var missing = InvokeLocalizationPrivate<IReadOnlyDictionary<string, string>>(
                    "LoadCatalog",
                    "Editor/Localization/missing.po");

                Assert.That(missing, Is.Empty);

                SetLocalizationPackageRoot(Path.Combine(root, "does-not-exist"));
                Assert.That(
                    InvokeLocalizationPrivate<string>("ResolveCatalogPath", "Editor/Localization/missing.po"),
                    Is.Null);
            }
            finally
            {
                SetLocalizationPackageRoot(null);
                ClearLocalizationCatalogs();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public void LatticeLocalization_PrivateCatalogLoader_HandlesUnknownLanguageAndReadFailure()
        {
            LogAssert.Expect(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex("Failed to load localization catalogue"));

            Assert.That(
                InvokeLocalizationPrivate<IReadOnlyDictionary<string, string>>("EnsureCatalogLoaded", (LatticeLocalization.Language)999, false),
                Is.Null);
            Assert.That(
                InvokeLocalizationPrivate<IReadOnlyDictionary<string, string>>("LoadCatalog", "\0"),
                Is.Empty);
        }

        [Test]
        public void LatticeLocalization_ParsePo_UnescapesControlCharacters()
        {
            var catalog = LatticeLocalization.ParsePo(new[]
            {
                "msgid \"escaped\"",
                "msgstr \"a\\rb\\tc\""
            });

            Assert.That(catalog["escaped"], Is.EqualTo("a\rb\tc"));
        }

        [Test]
        public void VpmApiClient_BuildErrorMessage_IncludesResultHttpUrlAndTrimmedResponse()
        {
            using var request = new UnityWebRequest("https://example.invalid/package");
            request.downloadHandler = new DownloadHandlerBuffer();

            var message = VpmApiClient.BuildErrorMessage(
                request,
                "https://vpm.example.test/api/packages/test/latest/version",
                " " + new string('x', 220) + " ");

            Assert.That(message, Does.Contain("VPM API request failed"));
            Assert.That(message, Does.Contain("URL=https://vpm.example.test/api/packages/test/latest/version"));
            Assert.That(message, Does.Contain("Response="));
            Assert.That(message, Does.EndWith("..."));
        }

        [Test]
        public void VpmApiClient_BuildErrorMessage_InternalOverloadIncludesErrorAndHttpStatus()
        {
            var message = VpmApiClient.BuildErrorMessage(
                UnityWebRequest.Result.ProtocolError,
                "connection failed",
                503,
                "https://vpm.example.test/latest",
                " unavailable ");

            Assert.That(message, Does.Contain("(ProtocolError): connection failed [HTTP 503]"));
            Assert.That(message, Does.Contain("URL=https://vpm.example.test/latest"));
            Assert.That(message, Does.Contain("Response=unavailable"));
        }

        [Test]
        public void VpmApiClient_BuildErrorMessage_TwoArgumentOverloadHandlesMissingResponseBody()
        {
            using var request = new UnityWebRequest("https://example.invalid/package");

            var message = VpmApiClient.BuildErrorMessage(request, "https://vpm.example.test/latest");

            Assert.That(message, Does.Contain("VPM API request failed"));
            Assert.That(message, Does.Contain("URL=https://vpm.example.test/latest"));
            Assert.That(message, Does.Not.Contain("Response="));
        }

        [Test]
        public void VpmApiClient_CanBeConstructedForPackageId()
        {
            var client = new VpmApiClient("__missing_package__");

            Assert.That(client, Is.Not.Null);
        }

        [Test]
        public void ToolIcons_Get_ReturnsNullForUnknownIcon()
        {
            Assert.That(ToolIcons.Get("__missing_icon__" + Guid.NewGuid().ToString("N")), Is.Null);
        }

        [Test]
        public void ToolIcons_Get_UsesBuiltinFallbackAndCache()
        {
            var first = ToolIcons.Get(ToolIcons.Move);
            var second = ToolIcons.Get(ToolIcons.Move);

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void ToolIcons_Get_HandlesEmptyResolvedIconDirectory()
        {
            var field = typeof(ToolIcons).GetField("s_iconDir", BindingFlags.Static | BindingFlags.NonPublic);
            var previous = field.GetValue(null);
            try
            {
                field.SetValue(null, "");

                Assert.That(ToolIcons.Get("__missing_icon__" + Guid.NewGuid().ToString("N")), Is.Null);
            }
            finally
            {
                field.SetValue(null, previous);
            }
        }

        [Test]
        public void ToolIcons_PrivateLoaders_CoverAssetSearchAndBuiltinFallback()
        {
            var iconDirField = typeof(ToolIcons).GetField("s_iconDir", BindingFlags.Static | BindingFlags.NonPublic);
            var previousIconDir = iconDirField.GetValue(null);
            try
            {
                iconDirField.SetValue(null, null);
                Assert.That(InvokeToolIconsPrivate<Texture2D>("LoadFromPackage", "__missing_icon__"), Is.Null);

                Assert.That(InvokeToolIconsPrivate<Texture2D>("LoadBuiltinFallback", ToolIcons.Move), Is.Not.Null);
            }
            finally
            {
                iconDirField.SetValue(null, previousIconDir);
            }
        }

        [Test]
        public void GeodesicDistanceCalculator_HandlesInvalidAndSparseAdjacency()
        {
            Assert.That(
                GeodesicDistanceCalculator.ComputeDistances(0, 1f, null, Array.Empty<Vector3>()),
                Is.Empty);

            var adjacency = new List<HashSet<int>>
            {
                new HashSet<int> { 1 },
                null,
                new HashSet<int>()
            };
            var vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };

            var distances = GeodesicDistanceCalculator.ComputeDistances(0, 2f, adjacency, vertices);
            var sparse = GeodesicDistanceCalculator.ComputeDistances(1, 2f, adjacency, vertices);

            Assert.That(distances[0], Is.EqualTo(0f));
            Assert.That(distances[1], Is.EqualTo(1f).Within(1e-6f));
            Assert.That(sparse, Does.ContainKey(1));
        }

        [Test]
        public void GeodesicDistanceCalculator_SkipsStaleQueueEntries()
        {
            var adjacency = new List<HashSet<int>>
            {
                new HashSet<int> { 1, 2 },
                new HashSet<int> { 3 },
                new HashSet<int> { 3 },
                new HashSet<int>()
            };
            var vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 1f, 0f),
                new Vector3(5f, 0f, 0f),
                new Vector3(10f, 0f, 0f)
            };

            var distances = GeodesicDistanceCalculator.ComputeDistances(0, 20f, adjacency, vertices);

            Assert.That(distances[3], Is.EqualTo(10f).Within(1e-6f));
        }

        [Test]
        public void GeodesicDistanceCalculator_IgnoresInvalidNeighborsAndDistance()
        {
            var adjacency = new List<HashSet<int>>
            {
                new HashSet<int> { -1, 1, 99 },
                new HashSet<int> { 0 },
                new HashSet<int> { 0 }
            };
            var vertices = new[] { Vector3.zero, Vector3.right };

            var distances = GeodesicDistanceCalculator.ComputeDistances(0, 2f, adjacency, vertices);

            Assert.That(distances.Keys, Is.EquivalentTo(new[] { 0, 1 }));
            var nanDistances = GeodesicDistanceCalculator.ComputeDistances(0, float.NaN, adjacency, vertices);
            Assert.That(nanDistances.Keys, Is.EquivalentTo(new[] { 0, 1 }));
            Assert.That(
                GeodesicDistanceCalculator.ComputeDistances(0, -1f, adjacency, vertices),
                Does.ContainKey(0));
            Assert.That(
                GeodesicDistanceCalculator.ComputeDistances(2, 2f, adjacency, vertices),
                Is.Empty);
        }

        [Test]
        public void GeodesicDistanceCalculator_SkipsNonFiniteEdges()
        {
            var adjacency = new List<HashSet<int>>
            {
                new HashSet<int> { 1 },
                new HashSet<int> { 0 }
            };
            var vertices = new[] { Vector3.zero, new Vector3(float.NaN, 0f, 0f) };

            var distances = GeodesicDistanceCalculator.ComputeDistances(0, 10f, adjacency, vertices);

            Assert.That(distances.Keys, Is.EquivalentTo(new[] { 0 }));
        }

        [Test]
        public void MeshAdjacency_UsesFlatArraysAndPreservesUniqueNeighbors()
        {
            var adjacency = MeshAdjacency.Build(4, new[]
            {
                0, 1, 2,
                2, 1, 3
            });

            Assert.That(adjacency.VertexCount, Is.EqualTo(4));
            Assert.That(adjacency.NeighborCount, Is.EqualTo(10));
            Assert.That(
                typeof(MeshAdjacency).GetFields(
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic),
                Has.All.Matches<System.Reflection.FieldInfo>(field => field.FieldType == typeof(int[])));
            Assert.That(adjacency.GetNeighborEnd(1) - adjacency.GetNeighborStart(1), Is.EqualTo(3));
        }

        [Test]
        public void GeodesicWorkspace_WarmRecomputeAllocatesZeroBytes()
        {
            var vertices = new[]
            {
                Vector3.zero,
                Vector3.right,
                Vector3.right * 2f,
                Vector3.right * 3f
            };
            var adjacency = MeshAdjacency.Build(4, new[] { 0, 1, 2, 1, 2, 3 });
            var workspace = new GeodesicDistanceCalculator.Workspace();
            Assert.That(GeodesicDistanceCalculator.ComputeDistances(
                0, 4f, adjacency, vertices, workspace), Is.True);

            long before = System.GC.GetAllocatedBytesForCurrentThread();
            Assert.That(GeodesicDistanceCalculator.ComputeDistances(
                0, 4f, adjacency, vertices, workspace), Is.True);
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
            Assert.That(workspace.TryGetDistance(3, out float distance), Is.True);
            Assert.That(distance, Is.EqualTo(3f).Within(1e-5f));
        }

        [Test]
        public void GeodesicWorkspace_SeventyThousandVertexWarmHoverAllocatesZeroBytes()
        {
            const int vertexCount = 70000;
            var vertices = new Vector3[vertexCount];
            var triangles = new int[(vertexCount - 2) * 3];
            for (int i = 0; i < vertexCount; i++)
                vertices[i] = new Vector3(i * 0.001f, 0f, 0f);
            for (int i = 0; i < vertexCount - 2; i++)
            {
                triangles[i * 3] = i;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }

            var buildWatch = System.Diagnostics.Stopwatch.StartNew();
            var adjacency = MeshAdjacency.Build(vertexCount, triangles);
            buildWatch.Stop();
            var workspace = new GeodesicDistanceCalculator.Workspace();
            Assert.That(GeodesicDistanceCalculator.ComputeDistances(
                vertexCount / 2, 0.05f, adjacency, vertices, workspace), Is.True);

            var hoverWatch = System.Diagnostics.Stopwatch.StartNew();
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            Assert.That(GeodesicDistanceCalculator.ComputeDistances(
                vertexCount / 2, 0.05f, adjacency, vertices, workspace), Is.True);
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            hoverWatch.Stop();

            TestContext.WriteLine(
                $"70k adjacency: {buildWatch.Elapsed.TotalMilliseconds:F3} ms; " +
                $"warm geodesic: {hoverWatch.Elapsed.TotalMilliseconds:F3} ms, {allocated} B");
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void ToolIcons_Content_FallsBackToTextWhenIconIsMissing()
        {
            var key = "__missing_loc_key__" + Guid.NewGuid().ToString("N");
            var content = ToolIcons.Content("__missing_icon__", key);

            Assert.That(content.text, Is.EqualTo(key));
            Assert.That(content.image, Is.Null);
        }

        [Test]
        public void ReleaseChecker_ShouldCheckForUpdates_ReturnsTrueForEmptyInvalidOrExpiredValue()
        {
            var now = new DateTime(2026, 6, 3, 12, 0, 0, DateTimeKind.Local);
            var expired = now.AddHours(-25).ToBinary().ToString();

            Assert.That(ReleaseChecker.ShouldCheckForUpdates("", now), Is.True);
            Assert.That(ReleaseChecker.ShouldCheckForUpdates("not-a-binary-date", now), Is.True);
            Assert.That(ReleaseChecker.ShouldCheckForUpdates(expired, now), Is.True);
        }

        [Test]
        public void ReleaseChecker_ShouldCheckForUpdates_ReturnsFalseWithinCheckInterval()
        {
            var now = new DateTime(2026, 6, 3, 12, 0, 0, DateTimeKind.Local);
            var recent = now.AddHours(-23).ToBinary().ToString();

            Assert.That(ReleaseChecker.ShouldCheckForUpdates(recent, now), Is.False);
        }

        [Test]
        public void ReleaseChecker_GetCurrentVersionAndKeys_ReturnStableValues()
        {
            Assert.That(ReleaseChecker.GetCurrentVersion(), Is.Not.Empty);
            Assert.That(ReleaseChecker.GetLastCheckKey(), Does.StartWith("Net32Ba.LatticeDeformationTool.LastVersionCheck."));
            Assert.That(ReleaseChecker.GetProjectScopeSuffix(), Is.Not.Empty);
        }

        [Test]
        public void ReleaseChecker_PrivateHandlers_UpdateStateAndRaiseEvent()
        {
            var previousState = CaptureReleaseCheckerState();
            int completed = 0;
            void OnCompleted() => completed++;

            ReleaseChecker.OnUpdateCheckCompleted += OnCompleted;
            try
            {
                InvokeReleaseCheckerHandler("HandleSuccess", "");

                Assert.That(ReleaseChecker.IsChecking, Is.False);
                Assert.That(ReleaseChecker.CheckError, Is.EqualTo("Empty version response"));
                Assert.That(completed, Is.EqualTo(1));

                InvokeReleaseCheckerHandler("HandleError", "network failed");

                Assert.That(ReleaseChecker.IsChecking, Is.False);
                Assert.That(ReleaseChecker.CheckError, Is.EqualTo("network failed"));
                Assert.That(completed, Is.EqualTo(2));

                InvokeReleaseCheckerHandler("HandleSuccess", "999.0.0");

                Assert.That(ReleaseChecker.LatestVersion, Is.EqualTo("999.0.0"));
                Assert.That(ReleaseChecker.HasNewVersion, Is.True);
                Assert.That(completed, Is.EqualTo(3));

                LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"\[LatticeDeformationTool\] Package is up to date: .*"));
                InvokeReleaseCheckerHandler("HandleSuccess", "0.0.0");
                Assert.That(completed, Is.EqualTo(4));
            }
            finally
            {
                ReleaseChecker.OnUpdateCheckCompleted -= OnCompleted;
                RestoreReleaseCheckerState(previousState);
            }
        }

        [Test]
        public void ReleaseChecker_NotifyWithoutSubscribers_ReturnsNormally()
        {
            var field = typeof(ReleaseChecker).GetField(
                "OnUpdateCheckCompleted",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            var previous = field.GetValue(null);
            try
            {
                field.SetValue(null, null);
                Assert.That(
                    () => InvokeReleaseCheckerPrivate<object>("NotifyUpdateCheckCompleted"),
                    Throws.Nothing);
            }
            finally
            {
                field.SetValue(null, previous);
            }
        }

        [Test]
        public void ReleaseChecker_CompletionNotificationException_DoesNotStopOtherSubscribers()
        {
            var previousState = CaptureReleaseCheckerState();
            int completed = 0;
            void ThrowingSubscriber() => throw new InvalidOperationException("completion subscriber failed");
            void CountingSubscriber() => completed++;

            ReleaseChecker.OnUpdateCheckCompleted += ThrowingSubscriber;
            ReleaseChecker.OnUpdateCheckCompleted += CountingSubscriber;
            try
            {
                LogAssert.Expect(
                    LogType.Warning,
                    new System.Text.RegularExpressions.Regex(@"\[LatticeDeformationTool\] Update check failed: network failed"));
                LogAssert.Expect(
                    LogType.Exception,
                    new System.Text.RegularExpressions.Regex("completion subscriber failed"));

                InvokeReleaseCheckerHandler("HandleError", "network failed");

                Assert.That(ReleaseChecker.IsChecking, Is.False);
                Assert.That(completed, Is.EqualTo(1));
                Assert.That(ReleaseChecker.CheckError, Is.EqualTo("network failed"));
            }
            finally
            {
                ReleaseChecker.OnUpdateCheckCompleted -= ThrowingSubscriber;
                ReleaseChecker.OnUpdateCheckCompleted -= CountingSubscriber;
                RestoreReleaseCheckerState(previousState);
            }
        }

        [Test]
        public void EditorCoroutine_Exception_ClearsReleaseCheckerStateAndNotifiesOnce()
        {
            var previousState = CaptureReleaseCheckerState();
            SetReleaseCheckerIsChecking(true);
            int completed = 0;
            void OnCompleted() => completed++;
            ReleaseChecker.OnUpdateCheckCompleted += OnCompleted;
            try
            {
                LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("routine failed"));

                var coroutine = EditorCoroutine.Start(
                    ThrowingRoutine(),
                    exception => InvokeReleaseCheckerExceptionHandler(exception));
                Assert.That(InvokeRegisteredEditorCoroutineUpdate(coroutine), Is.True);
                Assert.That(InvokeRegisteredEditorCoroutineUpdate(coroutine), Is.True);

                Assert.That(ReleaseChecker.IsChecking, Is.False);
                Assert.That(ReleaseChecker.CheckError, Is.EqualTo("routine failed"));
                Assert.That(completed, Is.EqualTo(1));
                Assert.That(IsEditorCoroutineRegistered(coroutine), Is.False);
            }
            finally
            {
                ReleaseChecker.OnUpdateCheckCompleted -= OnCompleted;
                RestoreReleaseCheckerState(previousState);
            }
        }

        [Test]
        public void ReleaseChecker_PrivateShouldCheckForUpdates_UsesEditorPrefsValue()
        {
            var key = ReleaseChecker.GetLastCheckKey();
            var previous = EditorPrefs.GetString(key, "");
            try
            {
                EditorPrefs.DeleteKey(key);
                Assert.That(InvokeReleaseCheckerPrivate<bool>("ShouldCheckForUpdates"), Is.True);

                EditorPrefs.SetString(key, DateTime.Now.ToBinary().ToString());
                Assert.That(InvokeReleaseCheckerPrivate<bool>("ShouldCheckForUpdates"), Is.False);
            }
            finally
            {
                if (string.IsNullOrEmpty(previous))
                {
                    EditorPrefs.DeleteKey(key);
                }
                else
                {
                    EditorPrefs.SetString(key, previous);
                }
            }
        }

        [Test]
        public void ReleaseChecker_BuildKeysAndProjectScopeSuffix_AreStable()
        {
            var suffix = ReleaseChecker.BuildProjectScopeSuffix("D:/VRC/Projects/Plugin-dev-playground/Assets");

            Assert.That(suffix, Is.Not.Empty);
            Assert.That(suffix, Does.Not.Contain("-"));
            Assert.That(suffix, Is.EqualTo(suffix.ToLowerInvariant()));
            Assert.That(ReleaseChecker.BuildProjectScopeSuffix(null), Is.EqualTo("unknown"));
            Assert.That(
                ReleaseChecker.BuildLastCheckKey("abc123"),
                Is.EqualTo("Net32Ba.LatticeDeformationTool.LastVersionCheck.abc123"));
        }

        private static void InvokeReleaseCheckerHandler(string methodName, string value)
        {
            var method = typeof(ReleaseChecker).GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { value });
        }

        private static void InvokeReleaseCheckerExceptionHandler(Exception exception)
        {
            var method = typeof(ReleaseChecker).GetMethod(
                "HandleCoroutineException",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { exception });
        }

        private static void SetReleaseCheckerIsChecking(bool value)
        {
            SetReleaseCheckerBackingField("<IsChecking>k__BackingField", value);
        }

        private readonly struct ReleaseCheckerState
        {
            public readonly bool IsChecking;
            public readonly string CheckError;
            public readonly string LatestVersion;
            public readonly bool HasNewVersion;

            public ReleaseCheckerState(bool isChecking, string checkError, string latestVersion, bool hasNewVersion)
            {
                IsChecking = isChecking;
                CheckError = checkError;
                LatestVersion = latestVersion;
                HasNewVersion = hasNewVersion;
            }
        }

        private static ReleaseCheckerState CaptureReleaseCheckerState()
        {
            return new ReleaseCheckerState(
                ReleaseChecker.IsChecking,
                ReleaseChecker.CheckError,
                ReleaseChecker.LatestVersion,
                ReleaseChecker.HasNewVersion);
        }

        private static void RestoreReleaseCheckerState(ReleaseCheckerState state)
        {
            SetReleaseCheckerBackingField("<IsChecking>k__BackingField", state.IsChecking);
            SetReleaseCheckerBackingField("<CheckError>k__BackingField", state.CheckError);
            SetReleaseCheckerBackingField("<LatestVersion>k__BackingField", state.LatestVersion);
            SetReleaseCheckerBackingField("<HasNewVersion>k__BackingField", state.HasNewVersion);
        }

        private static void SetReleaseCheckerBackingField(string fieldName, object value)
        {
            var field = typeof(ReleaseChecker).GetField(
                fieldName,
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(null, value);
        }

        private static bool InvokeRegisteredEditorCoroutineUpdate(EditorCoroutine coroutine)
        {
            var field = typeof(EditorApplication).GetField(
                "update",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);

            var callbacks = field.GetValue(null) as Delegate;
            var callback = callbacks?.GetInvocationList()
                .FirstOrDefault(item => ReferenceEquals(item.Target, coroutine));
            if (callback == null)
            {
                return false;
            }

            callback.DynamicInvoke();
            return true;
        }

        private static bool IsEditorCoroutineRegistered(EditorCoroutine coroutine)
        {
            var field = typeof(EditorApplication).GetField(
                "update",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);

            var callbacks = field.GetValue(null) as Delegate;
            return callbacks != null && callbacks.GetInvocationList()
                .Any(item => ReferenceEquals(item.Target, coroutine));
        }

        private static System.Collections.IEnumerator ThrowingRoutine()
        {
            yield return null;
            throw new InvalidOperationException("routine failed");
        }

        private static T InvokeReleaseCheckerPrivate<T>(string methodName, params object[] args)
        {
            var method = typeof(ReleaseChecker).GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                args.Select(arg => arg?.GetType() ?? typeof(object)).ToArray(),
                null);
            if (method == null && args.Length == 0)
            {
                method = typeof(ReleaseChecker)
                    .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == 0);
            }
            Assert.That(method, Is.Not.Null);
            return (T)method.Invoke(null, args);
        }

        private static void SetLocalizationPackageRoot(string value)
        {
            typeof(LatticeLocalization)
                .GetField("s_packageRootPath", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, value);
        }

        private static void ClearLocalizationCatalogs()
        {
            var field = typeof(LatticeLocalization)
                .GetField("s_catalogs", BindingFlags.Static | BindingFlags.NonPublic);
            var value = field.GetValue(null);
            value.GetType().GetMethod("Clear").Invoke(value, Array.Empty<object>());
        }

        private static T InvokeLocalizationPrivate<T>(string methodName, params object[] args)
        {
            var method = typeof(LatticeLocalization).GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (T)method.Invoke(null, args);
        }

        private static T InvokeToolIconsPrivate<T>(string methodName, params object[] args)
        {
            var method = typeof(ToolIcons).GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (T)method.Invoke(null, args);
        }
    }
}
#endif
