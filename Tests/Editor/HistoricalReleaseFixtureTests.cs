#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using PackageManagerInfo = UnityEditor.PackageManager.PackageInfo;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    /// <summary>
    /// End-to-end compatibility tests for prefabs serialized by the Unity version and
    /// package code at each published tag. Unlike fabricated migration unit tests, these
    /// fixtures exercise Unity YAML field absence, Unity object references, and a real
    /// save/reload boundary.
    /// </summary>
    public sealed class HistoricalReleaseFixtureTests
    {
        private const string PackageRoot = "Packages/net.32ba.lattice-deformation-tool";
        private const string FixtureRoot = PackageRoot + "/Tests/Editor/Fixtures/HistoricalReleases";
        private const string GeneratorPath = "Tools~/HistoricalFixtures/HistoricalFixtureGenerator.cs";
        private const string RunnerPath = "Tools~/HistoricalFixtures/Generate-HistoricalFixtures.ps1";
        private const string MetaGuidScheme = "sha256-v1:tag/relative-asset-path";
        private const string MetaGuidNamespace = "net.32ba.lattice-deformation-tool/historical-fixture-meta-guid/v1";
        private const string PrefabFileIdScheme = "sha256-v1:tag/relative-prefab/class/ordinal";
        private const string PrefabFileIdNamespace = "net.32ba.lattice-deformation-tool/historical-fixture-prefab-file-id/v1";
        private const string UnityVersion = "2022.3.22f1";
        private const string LatticeScriptGuid = "29946ddaafa0c41468ecfc2a1a3a4297";
        private const string LegacyBrushScriptGuid = "555ef150b22858c4f8a226a1a0f51c73";

        private static readonly TagDefinition[] s_tags =
        {
            new TagDefinition("0.0.1", "646087621dcd27375946da8178fd1a12cb230181", DeformationDataVersion.V0_0_1),
            new TagDefinition("0.0.2", "daee78685540092a038a46b9cff40b21a5cf235f", DeformationDataVersion.V0_0_1),
            new TagDefinition("0.0.3", "200afd7a2a7b627d391c3ca395dccb18099f0782", DeformationDataVersion.V0_0_1),
            new TagDefinition("0.0.4", "b208a5bedbfef20f0ac3d9db72348f865ad98ea1", DeformationDataVersion.V0_0_1),
            new TagDefinition("0.0.5", "9cadc032e98b2f0c6099113fbad7e641d4f58278", DeformationDataVersion.V0_0_1),
            new TagDefinition("0.0.6", "d39dd018845922f40b868fdc2c12d07d56c639a8", DeformationDataVersion.V0_0_1),
            new TagDefinition("1.0.0", "ad1395f2908a57f03ee03f3c7d824e441f9c7f9f", DeformationDataVersion.V0_0_1),
            new TagDefinition("1.0.1", "ec5cf94d650e489e94b0df1a991b930d018c753b", DeformationDataVersion.V0_0_1),
            new TagDefinition("1.1.0", "b08327c3d5308a41d8dd5438bb7fe9eec91d8382", DeformationDataVersion.V0_0_1),
            // 1.2.0 still serialized only the marker-less single-settings schema. It is
            // deliberately classified at the oldest compatible release rather than
            // guessing a provenance version that has no serialized evidence.
            new TagDefinition("1.2.0", "3332e986f560956f99fe78d8d33947fcacc0ca98", DeformationDataVersion.V0_0_1),
            new TagDefinition("1.2.1", "8c199ab68f056d29ba2ec8ad160d8ff53b0994b2", DeformationDataVersion.V1_2_1, true),
            new TagDefinition("1.3.0", "6ffebe0f01b822292e4db090bcd5dea8b0b7648f", DeformationDataVersion.V1_2_1, true),
            new TagDefinition("1.3.1", "e787c5a1a49996c539b49e596c919740f20dc204", DeformationDataVersion.V1_2_1, true),
            new TagDefinition("1.4.0", "15f684040770617bbe0c3b22b6e9720107c64adb", DeformationDataVersion.V1_2_1, true),
        };

        private static IEnumerable<FixtureCase> AllFixtureCases()
        {
            foreach (var tag in s_tags)
            {
                yield return FixtureCase.Lattice(tag, "lattice", "fixture.prefab", "expected.json");
                if (tag.Tag == "0.0.1")
                {
                    yield return FixtureCase.Lattice(
                        tag,
                        "lattice-world",
                        "fixture-world.prefab",
                        "expected-world.json");
                }

                if (tag.HasLegacyBrush)
                {
                    yield return FixtureCase.Lattice(
                        tag,
                        "lattice-remove-active-last",
                        "fixture-remove-active-last.prefab",
                        "expected-remove-active-last.json");
                    yield return FixtureCase.LegacyBrush(
                        tag,
                        "legacy-brush.prefab",
                        "legacy-brush-expected.json");
                }
            }
        }

        private static IEnumerable<TestCaseData> LatticeFixtureCases()
        {
            return AllFixtureCases()
                .Where(fixture => !fixture.IsLegacyBrush)
                .Select(fixture => new TestCaseData(fixture)
                    .SetName($"HistoricalRelease_{fixture.Tag.Tag.Replace('.', '_')}_{fixture.Kind}_StepwiseSaveReload"));
        }

        private static IEnumerable<TestCaseData> LegacyBrushFixtureCases()
        {
            return AllFixtureCases()
                .Where(fixture => fixture.IsLegacyBrush)
                .Select(fixture => new TestCaseData(fixture)
                    .SetName($"HistoricalRelease_{fixture.Tag.Tag.Replace('.', '_')}_LegacyBrushMigrationSaveReload"));
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
        }

        [Test]
        public void FixtureManifest_ContainsEveryPublishedTagAndRequiredActualUnityAssets()
        {
            var errors = new List<string>();
            var seenMetaGuids = new HashSet<string>(StringComparer.Ordinal);
            TryRequireFile(PackageRoot + "/Tests/Editor/Fixtures.meta", errors, "corpus");
            TryRequireFile(FixtureRoot + ".meta", errors, "corpus");
            foreach (var tag in s_tags)
            {
                ValidateReleaseFixtureManifest(tag, seenMetaGuids, errors);
            }

            Assert.That(
                errors,
                Is.Empty,
                "Historical release fixture corpus is incomplete or unverifiable:\n" +
                string.Join("\n", errors.Select(error => "- " + error)));
        }

        [Test]
        public void FreshCurrentComponent_DoesNotEnablePublishedBlendShapeCompatibility()
        {
            var root = new GameObject("Fresh Current BlendShape Semantics");
            root.SetActive(false);
            try
            {
                var deformer = root.AddComponent<LatticeDeformer>();
                AssertLegacyPublishedBlendShapeSemantics(deformer, false);
                _ = deformer.Groups;
                AssertLegacyPublishedBlendShapeSemantics(deformer, false);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [TestCaseSource(nameof(LatticeFixtureCases))]
        public void ActualUnitySavedLatticeFixture_UpgradesEveryBoundaryAndSurvivesSaveReload(
            FixtureCase fixture)
        {
            var expected = LoadExpected(fixture);
            string temporaryFolder = CreateTemporaryAssetFolder();
            GameObject instance = null;
            GameObject directInstance = null;
            Mesh source = null;
            try
            {
                instance = InstantiateDisconnectedPrefab(fixture.PrefabPath);
                var deformer = FindSingleComponent<LatticeDeformer>(instance, expected.deformerPath);
                source = AssertSourceReference(instance, fixture.SourcePath);
                var originalSourceVertices = source.vertices;

                Assert.That(instance.activeSelf, Is.False,
                    "Historical fixtures must stay inactive so OnEnable cannot migrate before the test controls each step.");
                Assert.That(deformer.enabled, Is.False,
                    "Historical fixtures must keep the deformer disabled until the explicit migration starts.");
                Assert.That(deformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.Unversioned));
                Assert.That(deformer.SourceDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.Unversioned));
                AssertRawHistoricalLatticePayload(
                    expected,
                    deformer,
                    DeformationDataVersion.Unversioned,
                    DeformationDataVersion.Unversioned);

                Assert.That(deformer.TryUpgradeDeformationDataOneRelease(), Is.True);
                Assert.That(deformer.SerializedDeformationDataVersion, Is.EqualTo(fixture.Tag.Classification));
                Assert.That(deformer.SourceDeformationDataVersion, Is.EqualTo(fixture.Tag.Classification));
                Assert.That(expected.classifiedVersion, Is.EqualTo(fixture.Tag.Classification.ToString()));
                AssertRawHistoricalLatticePayload(
                    expected,
                    deformer,
                    fixture.Tag.Classification,
                    fixture.Tag.Classification);

                var visited = new List<DeformationDataVersion> { fixture.Tag.Classification };
                while (deformer.SerializedDeformationDataVersion != DeformationDataVersion.CurrentDevelopment)
                {
                    var before = deformer.SerializedDeformationDataVersion;
                    Assert.That(deformer.TryUpgradeDeformationDataOneRelease(), Is.True,
                        $"{fixture.Tag.Tag}/{fixture.Kind} stopped at {before} ({deformer.MigrationStatus}).");
                    var after = deformer.SerializedDeformationDataVersion;
                    Assert.That((int)after, Is.EqualTo((int)before + 1),
                        $"{fixture.Tag.Tag}/{fixture.Kind} skipped a published release boundary.");
                    visited.Add(after);
                    if (before == DeformationDataVersion.V1_2_1 &&
                        expected.serializedFlatLayerCount > 0)
                    {
                        AssertHybridNormalizationBoundary(fixture, expected, deformer, after);
                    }
                }

                CollectionAssert.AreEqual(ExpectedReleaseTail(fixture.Tag.Classification), visited);
                AssertLegacyPublishedBlendShapeSemantics(deformer, ExpectedLegacyPublishedBlendShapeSemantics(expected));
                Assert.That(deformer.SourceDeformationDataVersion, Is.EqualTo(fixture.Tag.Classification));
                Assert.That(deformer.UsesLegacyAbsoluteLatticeEvaluation,
                    Is.EqualTo(expected.legacyAbsoluteEvaluation));
                AssertGoldenGroups(fixture, expected, deformer);
                AssertComponentSettings(expected.componentSettings, deformer, expected.tolerance);

                var output = deformer.Deform(false);
                Assert.That(output, Is.Not.Null);
                AssertGoldenMesh(expected, output, "golden output");
                AssertVectors(originalSourceVertices, source.vertices, 0f, "source mesh changed");
                AssertWorldTransformProbes(fixture, expected, deformer);

                // Exercise the normal public-access path against the same real
                // historical YAML, then compare it with the explicitly stepped path.
                // Synthetic schema tests also cover this convergence, but only an
                // actual tag-saved fixture can detect a deserialization-shaped split.
                string stepwiseState = CaptureRuntimeState(deformer);
                int stepwiseHash = deformer.ComputeLayeredStateHash();
                directInstance = InstantiateDisconnectedPrefab(fixture.PrefabPath);
                var directDeformer = FindSingleComponent<LatticeDeformer>(
                    directInstance,
                    expected.deformerPath);
                AssertSourceReference(directInstance, fixture.SourcePath);
                _ = directDeformer.Groups;
                Assert.That(directDeformer.SerializedDeformationDataVersion,
                    Is.EqualTo(DeformationDataVersion.CurrentDevelopment));
                Assert.That(directDeformer.SourceDeformationDataVersion,
                    Is.EqualTo(fixture.Tag.Classification));
                AssertGoldenGroups(fixture, expected, directDeformer);
                Assert.That(CaptureRuntimeState(directDeformer), Is.EqualTo(stepwiseState),
                    "Direct and release-by-release upgrades diverged for an actual saved fixture.");
                Assert.That(directDeformer.ComputeLayeredStateHash(), Is.EqualTo(stepwiseHash),
                    "Direct and release-by-release upgrades produced different layered state hashes.");
                var directOutput = directDeformer.Deform(false);
                Assert.That(directOutput, Is.Not.Null);
                AssertGoldenMesh(expected, directOutput, "direct-upgrade golden output");
                AssertVectors(output.vertices, directOutput.vertices, expected.tolerance,
                    "direct/stepwise output");
                AssertWorldTransformProbes(fixture, expected, directDeformer);
                Object.DestroyImmediate(directInstance);
                directInstance = null;

                string beforeSave = CaptureRuntimeState(deformer);
                var beforeSaveVertices = output.vertices;
                instance = SaveReloadAndReplace(instance, temporaryFolder, fixture.Kind + ".prefab");
                deformer = FindSingleComponent<LatticeDeformer>(instance, expected.deformerPath);
                Assert.That(instance.activeSelf, Is.False);
                Assert.That(deformer.enabled, Is.False);
                AssertSourceReference(instance, fixture.SourcePath);
                AssertRawReloadedLatticePayload(fixture, expected, deformer);
                _ = deformer.Groups;

                Assert.That(CaptureRuntimeState(deformer), Is.EqualTo(beforeSave));
                AssertGoldenGroups(fixture, expected, deformer);
                AssertComponentSettings(expected.componentSettings, deformer, expected.tolerance);
                var reloadedOutput = deformer.Deform(false);
                Assert.That(reloadedOutput, Is.Not.Null);
                AssertGoldenMesh(expected, reloadedOutput, "reloaded golden output");
                AssertVectors(beforeSaveVertices, reloadedOutput.vertices, expected.tolerance, "save/reload output");
                AssertWorldTransformProbes(fixture, expected, deformer);

                string beforeNoOp = CaptureRuntimeState(deformer);
                Assert.That(deformer.TryUpgradeDeformationDataOneRelease(), Is.False);
                Assert.That(CaptureRuntimeState(deformer), Is.EqualTo(beforeNoOp));
            }
            finally
            {
                if (directInstance != null) Object.DestroyImmediate(directInstance);
                if (instance != null) Object.DestroyImmediate(instance);
                AssetDatabase.DeleteAsset(temporaryFolder);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }

        [TestCaseSource(nameof(LegacyBrushFixtureCases))]
        public void ActualUnitySavedStandaloneBrushFixture_MigratesLosslesslyAndSurvivesSaveReload(
            FixtureCase fixture)
        {
            var expected = LoadExpected(fixture);
            string temporaryFolder = CreateTemporaryAssetFolder();
            GameObject instance = null;
            try
            {
                instance = InstantiateDisconnectedPrefab(fixture.PrefabPath);
                var legacy = FindSingleComponent<BrushDeformer>(instance, expected.deformerPath);
                var source = AssertSourceReference(instance, fixture.SourcePath);
                Assert.That(instance.activeSelf, Is.False);
                Assert.That(legacy.enabled, Is.False);
                Assert.That(instance.GetComponentsInChildren<LatticeDeformer>(true), Is.Empty,
                    "A standalone historical Brush fixture must not contain a pre-migrated target.");

                AssertLegacyBrushGolden(expected, legacy);
                Assert.That(
                    LegacyBrushDeformerMigration.TryMigrate(legacy, out var target, out var error),
                    Is.True,
                    error);
                Assert.That(target, Is.Not.Null);
                Assert.That(legacy.enabled, Is.False);
                Assert.That(legacy.gameObject.GetComponent<BrushDeformer>(), Is.SameAs(legacy));
                AssertMigratedLegacyBrush(expected, legacy, target);

                var output = target.Deform(false);
                Assert.That(output, Is.Not.Null);
                Assert.That(output.vertexCount, Is.EqualTo(source.vertexCount));
                AssertGoldenMesh(expected, output, "legacy brush golden output");
                string beforeSave = CaptureRuntimeState(target);

                instance = SaveReloadAndReplace(instance, temporaryFolder, "legacy-brush.prefab");
                legacy = FindSingleComponent<BrushDeformer>(instance, expected.deformerPath);
                target = FindSingleComponent<LatticeDeformer>(instance, expected.deformerPath);
                Assert.That(instance.activeSelf, Is.False);
                Assert.That(legacy.enabled, Is.False);
                AssertSourceReference(instance, fixture.SourcePath);
                AssertRawReloadedLegacyBrushPayload(expected, beforeSave, legacy, target);
                _ = target.Groups;
                Assert.That(CaptureRuntimeState(target), Is.EqualTo(beforeSave));
                AssertMigratedLegacyBrush(expected, legacy, target);

                var reloadedOutput = target.Deform(false);
                Assert.That(reloadedOutput, Is.Not.Null);
                AssertGoldenMesh(expected, reloadedOutput, "reloaded brush output");

                string beforeRepeat = CaptureRuntimeState(target);
                Assert.That(
                    LegacyBrushDeformerMigration.TryMigrate(legacy, out var repeatedTarget, out error),
                    Is.True,
                    error);
                Assert.That(repeatedTarget, Is.SameAs(target));
                Assert.That(CaptureRuntimeState(target), Is.EqualTo(beforeRepeat),
                    "Repeating the legacy migration must not add another group or layer.");
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                AssetDatabase.DeleteAsset(temporaryFolder);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }

        private static void ValidateReleaseFixtureManifest(
            TagDefinition tag,
            ISet<string> seenMetaGuids,
            List<string> errors)
        {
            string directory = $"{FixtureRoot}/{tag.Tag}";
            if (!Directory.Exists(AbsolutePath(directory)))
            {
                errors.Add($"{tag.Tag}: directory is missing ({directory}).");
                return;
            }

            if (!File.Exists(AbsolutePath(directory + ".meta")))
            {
                errors.Add($"{tag.Tag}: directory meta is missing ({directory}.meta).");
            }
            else
            {
                ValidateDeterministicMetaGuid(
                    directory + ".meta",
                    tag.Tag,
                    ".",
                    seenMetaGuids,
                    errors);
            }

            string manifestPath = directory + "/manifest.json";
            if (!TryRequireFile(manifestPath, errors, tag.Tag) ||
                !TryRequireFile(manifestPath + ".meta", errors, tag.Tag))
            {
                return;
            }

            ReleaseManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<ReleaseManifest>(File.ReadAllText(AbsolutePath(manifestPath)));
            }
            catch (Exception exception)
            {
                errors.Add($"{tag.Tag}: manifest.json cannot be parsed ({exception.Message}).");
                return;
            }

            if (manifest == null)
            {
                errors.Add($"{tag.Tag}: manifest.json deserialized to null.");
                return;
            }

            Expect(errors, tag.Tag, "manifest tag", manifest.tag, tag.Tag);
            Expect(errors, tag.Tag, "manifest commitSha", manifest.commitSha, tag.CommitSha);
            Expect(errors, tag.Tag, "manifest packageVersion", manifest.packageVersion, tag.Tag);
            Expect(errors, tag.Tag, "manifest unityVersion", manifest.unityVersion, UnityVersion);
            Expect(errors, tag.Tag, "manifest generationMode", manifest.generationMode, "unity-batchmode-tag-checkout");
            Expect(errors, tag.Tag, "manifest goldenOutputSource", manifest.goldenOutputSource, "historical-runtime-deform");
            Expect(errors, tag.Tag, "manifest metaGuidScheme", manifest.metaGuidScheme, MetaGuidScheme);
            Expect(errors, tag.Tag, "manifest prefabFileIdScheme", manifest.prefabFileIdScheme, PrefabFileIdScheme);
            Expect(errors, tag.Tag, "manifest generator", manifest.generator, GeneratorPath);
            string generatorAssetPath = PackageRoot + "/" + GeneratorPath;
            if (TryRequireFile(generatorAssetPath, errors, tag.Tag))
            {
                string actualGeneratorHash = ComputeSha256(AbsolutePath(generatorAssetPath));
                if (!string.Equals(
                        manifest.generatorSha256,
                        actualGeneratorHash,
                        StringComparison.Ordinal))
                {
                    errors.Add(
                        $"{tag.Tag}: generator SHA-256 mismatch; expected {manifest.generatorSha256}, " +
                        $"actual {actualGeneratorHash}.");
                }
            }
            Expect(errors, tag.Tag, "manifest runner", manifest.runner, RunnerPath);
            string runnerAssetPath = PackageRoot + "/" + RunnerPath;
            if (TryRequireFile(runnerAssetPath, errors, tag.Tag))
            {
                string actualRunnerHash = ComputeSha256(AbsolutePath(runnerAssetPath));
                if (!string.Equals(manifest.runnerSha256, actualRunnerHash, StringComparison.Ordinal))
                {
                    errors.Add(
                        $"{tag.Tag}: runner SHA-256 mismatch; expected {manifest.runnerSha256}, " +
                        $"actual {actualRunnerHash}.");
                }
            }

            var fixtures = AllFixtureCases().Where(candidate => candidate.Tag.Tag == tag.Tag).ToArray();
            var expectedKinds = new HashSet<string>(
                fixtures.Select(fixture => fixture.Kind),
                StringComparer.Ordinal);
            var manifestKinds = new HashSet<string>(
                (manifest.fixtures ?? Array.Empty<ManifestFixture>())
                    .Where(entry => entry != null)
                    .Select(entry => entry.kind),
                StringComparer.Ordinal);
            if (!manifestKinds.SetEquals(expectedKinds) ||
                (manifest.fixtures?.Length ?? 0) != expectedKinds.Count)
            {
                errors.Add(
                    $"{tag.Tag}: manifest fixture kinds differ. Required " +
                    $"[{string.Join(", ", expectedKinds.OrderBy(x => x))}], listed " +
                    $"[{string.Join(", ", manifestKinds.OrderBy(x => x))}].");
            }

            var requiredFiles = new HashSet<string>(StringComparer.Ordinal);
            AddRequiredFile(requiredFiles, "source.asset");
            foreach (var fixture in fixtures)
            {
                AddRequiredFile(requiredFiles, fixture.PrefabFile);
                AddRequiredFile(requiredFiles, fixture.ExpectedFile);
                ValidateManifestFixture(manifest, fixture, errors);
                ValidateDeterministicPrefabFileIds(
                    fixture.PrefabPath,
                    tag.Tag,
                    fixture.PrefabFile,
                    errors);
            }

            var manifestFiles = manifest.files ?? Array.Empty<ManifestFile>();
            var duplicates = manifestFiles.GroupBy(file => file?.path ?? "", StringComparer.Ordinal)
                .Where(group => group.Count() != 1)
                .Select(group => group.Key)
                .ToArray();
            if (duplicates.Length > 0)
            {
                errors.Add($"{tag.Tag}: manifest contains duplicate file entries: {string.Join(", ", duplicates)}.");
            }

            var listed = new HashSet<string>(
                manifestFiles.Where(file => file != null).Select(file => file.path),
                StringComparer.Ordinal);
            if (!listed.SetEquals(requiredFiles))
            {
                errors.Add(
                    $"{tag.Tag}: manifest files differ. Required [{string.Join(", ", requiredFiles.OrderBy(x => x))}], " +
                    $"listed [{string.Join(", ", listed.OrderBy(x => x))}].");
            }

            foreach (string file in requiredFiles)
            {
                string assetPath = directory + "/" + file;
                if (!TryRequireFile(assetPath, errors, tag.Tag)) continue;
                var entry = manifestFiles.SingleOrDefault(candidate => candidate != null && candidate.path == file);
                if (entry == null) continue;
                string actualHash = ComputeSha256(AbsolutePath(assetPath));
                if (!string.Equals(entry.sha256, actualHash, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"{tag.Tag}: SHA-256 mismatch for {file}; expected {entry.sha256}, actual {actualHash}.");
                }
                if (file.EndsWith(".meta", StringComparison.Ordinal))
                {
                    ValidateDeterministicMetaGuid(
                        assetPath,
                        tag.Tag,
                        file.Substring(0, file.Length - ".meta".Length),
                        seenMetaGuids,
                        errors);
                }
            }
            ValidateDeterministicMetaGuid(
                manifestPath + ".meta",
                tag.Tag,
                "manifest.json",
                seenMetaGuids,
                errors);

            ValidateLoadableAssets(tag, fixtures, errors);
        }

        private static void ValidateDeterministicMetaGuid(
            string metaAssetPath,
            string tag,
            string relativeAssetPath,
            ISet<string> seenMetaGuids,
            ICollection<string> errors)
        {
            string absolutePath = AbsolutePath(metaAssetPath);
            if (!File.Exists(absolutePath))
            {
                return;
            }

            MatchCollection matches = Regex.Matches(
                File.ReadAllText(absolutePath),
                @"(?m)^guid: ([0-9a-fA-F]{32})\r?$",
                RegexOptions.CultureInvariant);
            if (matches.Count != 1)
            {
                errors.Add($"{tag}: {relativeAssetPath}.meta must contain exactly one GUID.");
                return;
            }

            string actual = matches[0].Groups[1].Value;
            string expected = ComputeDeterministicMetaGuid(tag, relativeAssetPath);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                errors.Add(
                    $"{tag}: deterministic meta GUID mismatch for {relativeAssetPath}; " +
                    $"expected {expected}, actual {actual}.");
            }
            if (!seenMetaGuids.Add(actual))
            {
                errors.Add($"{tag}: deterministic meta GUID is duplicated for {relativeAssetPath}: {actual}.");
            }
        }

        private static string ComputeDeterministicMetaGuid(string tag, string relativeAssetPath)
        {
            string identity = MetaGuidNamespace + "\n" + tag + "\n" + relativeAssetPath.Replace('\\', '/');
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(identity));
            return string.Concat(hash.Take(16).Select(value => value.ToString("x2")));
        }

        private static void ValidateDeterministicPrefabFileIds(
            string prefabAssetPath,
            string tag,
            string relativePrefabPath,
            ICollection<string> errors)
        {
            string absolutePath = AbsolutePath(prefabAssetPath);
            if (!File.Exists(absolutePath))
            {
                return;
            }

            MatchCollection anchors = Regex.Matches(
                File.ReadAllText(absolutePath),
                @"(?m)^--- !u!(\d+) &(\d+)\r?$",
                RegexOptions.CultureInvariant);
            if (anchors.Count == 0)
            {
                errors.Add($"{tag}: {relativePrefabPath} contains no local object anchors.");
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int ordinal = 0; ordinal < anchors.Count; ordinal++)
            {
                string classId = anchors[ordinal].Groups[1].Value;
                string actual = anchors[ordinal].Groups[2].Value;
                string expected = ComputeDeterministicPrefabFileId(
                    tag,
                    relativePrefabPath,
                    classId,
                    ordinal);
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                {
                    errors.Add(
                        $"{tag}: deterministic prefab fileID mismatch for {relativePrefabPath} " +
                        $"anchor {ordinal}; expected {expected}, actual {actual}.");
                }
                if (!seen.Add(actual))
                {
                    errors.Add($"{tag}: duplicate prefab fileID in {relativePrefabPath}: {actual}.");
                }
            }
        }

        private static string ComputeDeterministicPrefabFileId(
            string tag,
            string relativePrefabPath,
            string classId,
            int ordinal)
        {
            string identity = PrefabFileIdNamespace + "\n" + tag + "\n" +
                              relativePrefabPath.Replace('\\', '/') + "\n" + classId + "\n" +
                              ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(identity));
            ulong value = 0;
            for (int index = 0; index < sizeof(ulong); index++)
            {
                value = (value << 8) | hash[index];
            }
            value = (value & 0x3FFFFFFFFFFFFFFFUL) | 0x4000000000000000UL;
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void ValidateManifestFixture(
            ReleaseManifest manifest,
            FixtureCase fixture,
            List<string> errors)
        {
            var entries = (manifest.fixtures ?? Array.Empty<ManifestFixture>())
                .Where(entry => entry != null && entry.kind == fixture.Kind)
                .ToArray();
            if (entries.Length != 1)
            {
                errors.Add($"{fixture.Tag.Tag}: manifest must contain exactly one fixture entry for {fixture.Kind}.");
                return;
            }

            var entry = entries[0];
            Expect(errors, fixture.Tag.Tag, fixture.Kind + " prefab", entry.prefab, fixture.PrefabFile);
            Expect(errors, fixture.Tag.Tag, fixture.Kind + " expected", entry.expected, fixture.ExpectedFile);
            Expect(errors, fixture.Tag.Tag, fixture.Kind + " source", entry.source, "source.asset");
            Expect(
                errors,
                fixture.Tag.Tag,
                fixture.Kind + " goldenOutputSource",
                entry.goldenOutputSource,
                fixture.IsLegacyBrush ? "BrushDeformer.Deform(false)" : "LatticeDeformer.Deform(false)");
        }

        private static void ValidateLoadableAssets(
            TagDefinition tag,
            IReadOnlyList<FixtureCase> fixtures,
            List<string> errors)
        {
            string sourcePath = $"{FixtureRoot}/{tag.Tag}/source.asset";
            var source = AssetDatabase.LoadAssetAtPath<Mesh>(sourcePath);
            if (source == null)
            {
                errors.Add($"{tag.Tag}: source.asset is not a loadable Unity Mesh.");
            }

            foreach (var fixture in fixtures)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fixture.PrefabPath);
                if (prefab == null)
                {
                    errors.Add($"{tag.Tag}/{fixture.Kind}: prefab is not loadable through AssetDatabase.");
                    continue;
                }

                string yaml = File.ReadAllText(AbsolutePath(fixture.PrefabPath));
                if (!yaml.StartsWith("%YAML", StringComparison.Ordinal) ||
                    !yaml.Contains("--- !u!114"))
                {
                    errors.Add($"{tag.Tag}/{fixture.Kind}: prefab is not a text-serialized Unity YAML component fixture.");
                }
                if (yaml.Contains("_deformationDataVersion:") ||
                    yaml.Contains("_deformationDataSourceVersion:") ||
                    yaml.Contains("_legacyAbsoluteLatticeEvaluation:") ||
                    yaml.Contains("_legacyTrilinearInterpolation:") ||
                    yaml.Contains("_legacyPublishedBlendShapeSemantics:"))
                {
                    errors.Add($"{tag.Tag}/{fixture.Kind}: prefab unexpectedly contains current version marker fields.");
                }
                string requiredGuid = fixture.IsLegacyBrush ? LegacyBrushScriptGuid : LatticeScriptGuid;
                if (!yaml.Contains("guid: " + requiredGuid))
                {
                    errors.Add($"{tag.Tag}/{fixture.Kind}: prefab does not reference the historical script GUID {requiredGuid}.");
                }

                FixtureExpected expected;
                try
                {
                    expected = JsonUtility.FromJson<FixtureExpected>(
                        File.ReadAllText(AbsolutePath(fixture.ExpectedPath)));
                }
                catch (Exception exception)
                {
                    errors.Add($"{tag.Tag}/{fixture.Kind}: expected JSON cannot be parsed ({exception.Message}).");
                    continue;
                }

                if (prefab.activeSelf)
                {
                    errors.Add($"{tag.Tag}/{fixture.Kind}: prefab root must be inactive.");
                }
                if (fixture.IsLegacyBrush)
                {
                    var components = prefab.GetComponentsInChildren<BrushDeformer>(true);
                    if (components.Length != 1 || components[0].enabled)
                    {
                        errors.Add($"{tag.Tag}/{fixture.Kind}: expected exactly one disabled standalone BrushDeformer.");
                    }
                    if (prefab.GetComponentsInChildren<LatticeDeformer>(true).Length != 0)
                    {
                        errors.Add($"{tag.Tag}/{fixture.Kind}: standalone Brush fixture already contains LatticeDeformer.");
                    }
                }
                else
                {
                    ValidateHistoricalLatticeYamlShape(tag, fixture, expected, yaml, errors);
                    var components = prefab.GetComponentsInChildren<LatticeDeformer>(true);
                    if (components.Length != 1 || components[0].enabled)
                    {
                        errors.Add($"{tag.Tag}/{fixture.Kind}: expected exactly one disabled LatticeDeformer.");
                    }
                }

                ValidateExpectedDocument(fixture, expected, source, errors);
            }
        }

        private static void ValidateHistoricalLatticeYamlShape(
            TagDefinition tag,
            FixtureCase fixture,
            FixtureExpected expected,
            string yaml,
            ICollection<string> errors)
        {
            string label = tag.Tag + "/" + fixture.Kind;
            string[] coreFields =
            {
                "_settings:",
                "_gridSize:",
                "_localBounds:",
                "_controlPointsLocal:",
                "_interpolation:",
            };
            foreach (string field in coreFields)
            {
                if (!yaml.Contains(field))
                {
                    errors.Add($"{label}: historical core field is missing from YAML ({field}).");
                }
            }

            if (yaml.Contains("_serializationVersion:"))
            {
                errors.Add($"{label}: historical LatticeAsset unexpectedly contains current _serializationVersion.");
            }

            if (tag.Tag == "0.0.1")
            {
                string expectedApplySpace = fixture.Kind == "lattice-world"
                    ? "_applySpace: 1"
                    : "_applySpace: 0";
                if (!yaml.Contains(expectedApplySpace))
                {
                    errors.Add($"{label}: expected the 0.0.1 marker '{expectedApplySpace}'.");
                }
            }
            else if (yaml.Contains("_applySpace:"))
            {
                errors.Add($"{label}: _applySpace must be absent after tag 0.0.1.");
            }

            bool expectsUseJobs = tag.Tag == "0.0.1" || tag.Tag == "0.0.2" ||
                                  tag.Tag == "0.0.3" || tag.Tag == "0.0.4";
            if (expectsUseJobs && !yaml.Contains("_useJobsAndBurst:"))
            {
                errors.Add($"{label}: _useJobsAndBurst is required through tag 0.0.4.");
            }
            else if (!expectsUseJobs && yaml.Contains("_useJobsAndBurst:"))
            {
                errors.Add($"{label}: _useJobsAndBurst must be absent starting at tag 0.0.5.");
            }

            bool groupSchema = tag.Classification == DeformationDataVersion.V1_2_1;
            if (groupSchema)
            {
                int serializedModel = expected?.serializedLayerModelVersion ?? int.MinValue;
                if (!yaml.Contains("_groups:") ||
                    !yaml.Contains("_layerModelVersion: " + serializedModel))
                {
                    errors.Add(
                        $"{label}: group schema markers (_groups and _layerModelVersion={serializedModel}) are missing.");
                }
            }
            else if (yaml.Contains("_groups:") || yaml.Contains("_layerModelVersion:"))
            {
                errors.Add($"{label}: pre-1.2.1 YAML unexpectedly contains group schema markers.");
            }

            if (fixture.Kind != "lattice-world" && !yaml.Contains("_interpolation: 1"))
            {
                errors.Add(
                    $"{label}: local lattice fixture must serialize CubicBernstein (_interpolation: 1).");
            }
        }

        private static void ValidateExpectedDocument(
            FixtureCase fixture,
            FixtureExpected expected,
            Mesh source,
            List<string> errors)
        {
            if (expected == null)
            {
                errors.Add($"{fixture.Tag.Tag}/{fixture.Kind}: expected JSON deserialized to null.");
                return;
            }

            Expect(errors, fixture.Tag.Tag, fixture.Kind + " expected tag", expected.tag, fixture.Tag.Tag);
            Expect(errors, fixture.Tag.Tag, fixture.Kind + " expected kind", expected.kind, fixture.Kind);
            if (!(expected.tolerance > 0f) || expected.tolerance > 1e-4f ||
                float.IsNaN(expected.tolerance) || float.IsInfinity(expected.tolerance))
            {
                errors.Add($"{fixture.Tag.Tag}/{fixture.Kind}: tolerance must be finite, positive, and <= 1e-4.");
            }
            if (expected.expectedVertices == null || expected.expectedVertices.Length == 0)
            {
                errors.Add($"{fixture.Tag.Tag}/{fixture.Kind}: expectedVertices is missing or empty.");
            }
            else if (source != null && expected.expectedVertices.Length != source.vertexCount)
            {
                errors.Add($"{fixture.Tag.Tag}/{fixture.Kind}: golden vertex count differs from source.asset.");
            }
            if (expected.outputBlendShapes == null)
            {
                errors.Add($"{fixture.Tag.Tag}/{fixture.Kind}: outputBlendShapes must be present (use [] when empty).");
            }
            else
            {
                ValidateGoldenBlendShapes(
                    expected.outputBlendShapes,
                    source,
                    fixture.Tag.Tag + "/" + fixture.Kind,
                    errors);
            }
            if (expected.outputBlendShapes != null &&
                !fixture.IsLegacyBrush &&
                fixture.Tag.Classification == DeformationDataVersion.V0_0_1 &&
                expected.outputBlendShapes.Length != 0)
            {
                errors.Add($"{fixture.Tag.Tag}/{fixture.Kind}: single-settings schema must have zero generated BlendShapes.");
            }
            if (expected.outputBlendShapes != null &&
                !fixture.IsLegacyBrush &&
                fixture.Tag.Classification == DeformationDataVersion.V1_2_1 &&
                expected.outputBlendShapes.Length == 0)
            {
                errors.Add($"{fixture.Tag.Tag}/{fixture.Kind}: group-schema fixture must exercise generated BlendShape output.");
            }

            if (fixture.IsLegacyBrush)
            {
                if (expected.legacyBrush == null)
                {
                    errors.Add($"{fixture.Tag.Tag}/{fixture.Kind}: legacyBrush settings are missing.");
                }
                else if (source != null &&
                         (expected.legacyBrush.displacements == null ||
                          expected.legacyBrush.displacements.Length != source.vertexCount))
                {
                    errors.Add($"{fixture.Tag.Tag}/{fixture.Kind}: legacy displacement count differs from source.asset.");
                }
                return;
            }

            Expect(
                errors,
                fixture.Tag.Tag,
                fixture.Kind + " classifiedVersion",
                expected.classifiedVersion,
                fixture.Tag.Classification.ToString());
            int expectedSerializedModel = fixture.Tag.Classification == DeformationDataVersion.V1_2_1 ? 2 : -1;
            if (expected.serializedLayerModelVersion != expectedSerializedModel)
            {
                errors.Add(
                    $"{fixture.Tag.Tag}/{fixture.Kind}: serializedLayerModelVersion must be " +
                    $"{expectedSerializedModel}, actual {expected.serializedLayerModelVersion}.");
            }
            int expectedFlatCount = fixture.Tag.Classification == DeformationDataVersion.V1_2_1
                ? Math.Max(1, expected.serializedFlatLayerCount)
                : -1;
            if ((fixture.Tag.Classification == DeformationDataVersion.V1_2_1 &&
                 expected.serializedFlatLayerCount != expectedFlatCount) ||
                (fixture.Tag.Classification != DeformationDataVersion.V1_2_1 &&
                 expected.serializedFlatLayerCount != -1))
            {
                errors.Add(
                    $"{fixture.Tag.Tag}/{fixture.Kind}: serializedFlatLayerCount must describe " +
                    $"the published hybrid payload (actual {expected.serializedFlatLayerCount}).");
            }
            if (expected.serializedGroups == null || expected.serializedFlatLayers == null)
            {
                errors.Add($"{fixture.Tag.Tag}/{fixture.Kind}: serialized raw group/flat projections are missing.");
            }
            else if (expected.serializedGroups.Any(group => group == null || group.layers == null ||
                         group.layers.Any(layer => layer == null || layer.settings == null)) ||
                     expected.serializedFlatLayers.Any(layer => layer == null || layer.settings == null))
            {
                errors.Add($"{fixture.Tag.Tag}/{fixture.Kind}: serialized raw layer settings payload is incomplete.");
            }
            else if (expected.serializedFlatLayers.Length != Math.Max(0, expected.serializedFlatLayerCount))
            {
                errors.Add($"{fixture.Tag.Tag}/{fixture.Kind}: serializedFlatLayers count disagrees with metadata.");
            }
            else if (fixture.Tag.Classification == DeformationDataVersion.V1_2_1)
            {
                if (expected.serializedGroups.Length == 0 ||
                    expected.groups == null ||
                    expected.groups.Length != expected.serializedGroups.Length + 1)
                {
                    errors.Add($"{fixture.Tag.Tag}/{fixture.Kind}: hybrid final projection must append one recovery group.");
                }
                else
                {
                    var recovery = expected.groups[expected.groups.Length - 1];
                    if (recovery == null ||
                        recovery.name != "Recovered Legacy Flat Layers" ||
                        recovery.enabled ||
                        recovery.layers == null ||
                        recovery.layers.Length != expected.serializedFlatLayers.Length)
                    {
                        errors.Add($"{fixture.Tag.Tag}/{fixture.Kind}: disabled recovery group golden is malformed.");
                    }
                }
                if (expected.serializedFlatBlendShapeCurve == null)
                {
                    errors.Add($"{fixture.Tag.Tag}/{fixture.Kind}: serialized flat component curve is missing.");
                }
            }
            else if (expected.serializedGroups.Length != 0 || expected.serializedFlatLayers.Length != 0)
            {
                errors.Add($"{fixture.Tag.Tag}/{fixture.Kind}: pre-group fixture must expose empty serialized projections.");
            }
            if (expected.groups == null || expected.groups.Length == 0)
            {
                errors.Add($"{fixture.Tag.Tag}/{fixture.Kind}: normalized golden groups are missing.");
            }
            else if (expected.groups.Any(group => group == null || group.layers == null ||
                         group.layers.Any(layer => layer == null || layer.settings == null)))
            {
                errors.Add($"{fixture.Tag.Tag}/{fixture.Kind}: every final layer must include its serialized settings payload.");
            }
            if (expected.componentSettings == null || expected.componentSettings.weightTransfer == null)
            {
                errors.Add($"{fixture.Tag.Tag}/{fixture.Kind}: component rebuild/weight-transfer settings are missing.");
            }
            ValidateNonDefaultCompatibilityContract(fixture, expected, errors);
            if (fixture.Kind == "lattice-world")
            {
                if (expected.transformProbes == null || expected.transformProbes.Length == 0 ||
                    expected.transformProbes.Any(probe => probe == null ||
                        probe.expectedVertices == null || probe.outputBlendShapes == null))
                {
                    errors.Add($"{fixture.Tag.Tag}/{fixture.Kind}: transformProbes golden data is missing.");
                }
                else if (source != null && expected.transformProbes.Any(
                             probe => probe.expectedVertices.Length != source.vertexCount))
                {
                    errors.Add($"{fixture.Tag.Tag}/{fixture.Kind}: world probe vertex count differs from source.asset.");
                }
                else
                {
                    for (int probeIndex = 0; probeIndex < expected.transformProbes.Length; probeIndex++)
                    {
                        ValidateGoldenBlendShapes(
                            expected.transformProbes[probeIndex].outputBlendShapes,
                            source,
                            $"{fixture.Tag.Tag}/{fixture.Kind}/probe[{probeIndex}]",
                            errors);
                    }
                }
            }
            else if (expected.transformProbes == null || expected.transformProbes.Length != 0)
            {
                errors.Add($"{fixture.Tag.Tag}/{fixture.Kind}: non-world fixtures must define transformProbes as [].");
            }
        }

        private static void ValidateNonDefaultCompatibilityContract(
            FixtureCase fixture,
            FixtureExpected expected,
            ICollection<string> errors)
        {
            if (fixture.IsLegacyBrush)
            {
                return;
            }

            string label = fixture.Tag.Tag + "/" + fixture.Kind;
            var finalGroups = expected.groups ?? Array.Empty<GoldenGroup>();
            if (fixture.Kind != "lattice-world" && !ContainsCubicBernsteinLattice(finalGroups))
            {
                errors.Add($"{label}: local fixture golden must exercise CubicBernstein lattice interpolation.");
            }

            if (fixture.Tag.Classification != DeformationDataVersion.V1_2_1)
            {
                return;
            }

            bool removeActiveLast = fixture.Kind == "lattice-remove-active-last";
            int requiredActiveGroupIndex = removeActiveLast ? 0 : 1;
            if (expected.activeGroupIndex != requiredActiveGroupIndex)
            {
                errors.Add(
                    $"{label}: group-schema fixture must preserve activeGroupIndex={requiredActiveGroupIndex}.");
            }

            var rawGroups = expected.serializedGroups ?? Array.Empty<GoldenGroup>();
            if (rawGroups.Length < 2)
            {
                errors.Add($"{label}: group-schema fixture must serialize primary and secondary groups.");
                return;
            }

            var primary = rawGroups[0];
            if (removeActiveLast)
            {
                if (primary == null || primary.layers == null || primary.layers.Length == 0 ||
                    primary.activeLayerIndex != primary.layers.Length ||
                    primary.layers[primary.layers.Length - 1] == null ||
                    primary.layers[primary.layers.Length - 1].type != "Lattice")
                {
                    errors.Add(
                        $"{label}: raw RemoveLayer payload must retain the known one-past-end active index.");
                }
                if (finalGroups.Length == 0 || finalGroups[0] == null ||
                    finalGroups[0].layers == null || finalGroups[0].layers.Length == 0 ||
                    finalGroups[0].activeLayerIndex != finalGroups[0].layers.Length - 1)
                {
                    errors.Add(
                        $"{label}: migrated RemoveLayer payload must canonicalize active index to the last remaining layer.");
                }
            }
            else if (primary == null || primary.activeLayerIndex != 1 ||
                     primary.layers == null || primary.layers.Length <= 1 ||
                     primary.layers[1] == null || primary.layers[1].type != "Brush")
            {
                errors.Add(
                    $"{label}: inactive primary group must preserve activeLayerIndex=1 selecting its Brush layer.");
            }
            if (!ContainsCubicBernsteinLattice(rawGroups))
            {
                errors.Add($"{label}: serialized group payload must contain a CubicBernstein lattice layer.");
            }

            for (int groupIndex = 0; groupIndex < rawGroups.Length; groupIndex++)
            {
                if (!(removeActiveLast && groupIndex == 0))
                {
                    ValidateGoldenActiveLayerIndex(rawGroups[groupIndex], label + $" raw group[{groupIndex}]", errors);
                }
                int canonicalActiveLayerIndex = rawGroups[groupIndex] == null ||
                                                rawGroups[groupIndex].layers == null ||
                                                rawGroups[groupIndex].layers.Length == 0
                    ? 0
                    : Mathf.Clamp(
                        rawGroups[groupIndex].activeLayerIndex,
                        0,
                        rawGroups[groupIndex].layers.Length - 1);
                if (groupIndex >= finalGroups.Length || finalGroups[groupIndex] == null ||
                    rawGroups[groupIndex] == null ||
                    finalGroups[groupIndex].activeLayerIndex != canonicalActiveLayerIndex)
                {
                    errors.Add(
                        $"{label}: final group[{groupIndex}] does not preserve/canonicalize its raw activeLayerIndex.");
                }
            }
            for (int groupIndex = 0; groupIndex < finalGroups.Length; groupIndex++)
            {
                ValidateGoldenActiveLayerIndex(finalGroups[groupIndex], label + $" final group[{groupIndex}]", errors);
            }
        }

        private static void ValidateGoldenActiveLayerIndex(
            GoldenGroup group,
            string label,
            ICollection<string> errors)
        {
            if (group == null || group.layers == null || group.layers.Length == 0 ||
                group.activeLayerIndex < 0 || group.activeLayerIndex >= group.layers.Length)
            {
                errors.Add(label + ": activeLayerIndex is outside the serialized layer list.");
            }
        }

        private static bool ContainsCubicBernsteinLattice(IEnumerable<GoldenGroup> groups)
        {
            return (groups ?? Array.Empty<GoldenGroup>())
                .Where(group => group != null && group.layers != null)
                .SelectMany(group => group.layers)
                .Any(layer => layer != null && layer.type == "Lattice" && layer.settings != null &&
                              layer.settings.interpolation == LatticeInterpolationMode.CubicBernstein.ToString());
        }

        private static FixtureExpected LoadExpected(FixtureCase fixture)
        {
            Assert.That(File.Exists(AbsolutePath(fixture.ExpectedPath)), Is.True,
                $"Required historical expected file is missing: {fixture.ExpectedPath}");
            var result = JsonUtility.FromJson<FixtureExpected>(File.ReadAllText(AbsolutePath(fixture.ExpectedPath)));
            Assert.That(result, Is.Not.Null);
            Assert.That(result.tag, Is.EqualTo(fixture.Tag.Tag));
            Assert.That(result.kind, Is.EqualTo(fixture.Kind));
            return result;
        }

        private static void ValidateGoldenBlendShapes(
            GoldenBlendShape[] shapes,
            Mesh source,
            string label,
            ICollection<string> errors)
        {
            if (shapes == null) return;
            int vertexCount = source != null ? source.vertexCount : -1;
            for (int shapeIndex = 0; shapeIndex < shapes.Length; shapeIndex++)
            {
                var shape = shapes[shapeIndex];
                if (shape == null || string.IsNullOrWhiteSpace(shape.name) || shape.frames == null)
                {
                    errors.Add($"{label}: BlendShape[{shapeIndex}] metadata is incomplete.");
                    continue;
                }
                for (int frameIndex = 0; frameIndex < shape.frames.Length; frameIndex++)
                {
                    var frame = shape.frames[frameIndex];
                    if (frame == null || !IsFinite(frame.weight) ||
                        frame.deltaVertices == null || frame.deltaNormals == null || frame.deltaTangents == null ||
                        (vertexCount >= 0 &&
                         (frame.deltaVertices.Length != vertexCount ||
                          frame.deltaNormals.Length != vertexCount ||
                          frame.deltaTangents.Length != vertexCount)))
                    {
                        errors.Add($"{label}: BlendShape[{shapeIndex}].frame[{frameIndex}] surface deltas are incomplete.");
                    }
                }
            }
        }

        private static void AssertGoldenGroups(
            FixtureCase fixture,
            FixtureExpected expected,
            LatticeDeformer deformer)
        {
            AssertNonDefaultCompatibilityContract(fixture, expected, deformer);
            float tolerance = expected.tolerance;
            Assert.That(deformer.ActiveGroupIndex, Is.EqualTo(expected.activeGroupIndex));
            Assert.That(deformer.Groups.Count, Is.EqualTo(expected.groups.Length));
            for (int groupIndex = 0; groupIndex < expected.groups.Length; groupIndex++)
            {
                var goldenGroup = expected.groups[groupIndex];
                var actualGroup = deformer.Groups[groupIndex];
                Assert.That(actualGroup, Is.Not.Null, $"group[{groupIndex}]");
                Assert.That(actualGroup.Name, Is.EqualTo(goldenGroup.name), $"group[{groupIndex}].name");
                Assert.That(actualGroup.Enabled, Is.EqualTo(goldenGroup.enabled), $"group[{groupIndex}].enabled");
                Assert.That(actualGroup.ActiveLayerIndex, Is.EqualTo(goldenGroup.activeLayerIndex));
                Assert.That(actualGroup.BlendShapeOutput.ToString(), Is.EqualTo(goldenGroup.blendShapeOutput));
                Assert.That(actualGroup.BlendShapeName, Is.EqualTo(goldenGroup.blendShapeName ?? ""));
                AssertCurve(goldenGroup.blendShapeCurve, actualGroup.BlendShapeCurve, tolerance,
                    $"group[{groupIndex}].curve");

                var goldenLayers = goldenGroup.layers ?? Array.Empty<GoldenLayer>();
                Assert.That(actualGroup.Layers.Count, Is.EqualTo(goldenLayers.Length));
                for (int layerIndex = 0; layerIndex < goldenLayers.Length; layerIndex++)
                {
                    var goldenLayer = goldenLayers[layerIndex];
                    var actualLayer = actualGroup.Layers[layerIndex];
                    string label = $"group[{groupIndex}].layer[{layerIndex}]";
                    Assert.That(actualLayer, Is.Not.Null, label);
                    Assert.That(actualLayer.Name, Is.EqualTo(goldenLayer.name), label + ".name");
                    Assert.That(actualLayer.Type.ToString(), Is.EqualTo(goldenLayer.type), label + ".type");
                    Assert.That(actualLayer.Enabled, Is.EqualTo(goldenLayer.enabled), label + ".enabled");
                    Assert.That(actualLayer.Weight, Is.EqualTo(goldenLayer.weight).Within(tolerance), label + ".weight");
                    if (fixture.Tag.Classification == DeformationDataVersion.V1_2_1)
                    {
                        // Group-schema releases carried layer output mode/name, but did
                        // not yet serialize a per-layer curve.
                        Assert.That(actualLayer.BlendShapeOutput.ToString(),
                            Is.EqualTo(goldenLayer.blendShapeOutput));
                        Assert.That(actualLayer.BlendShapeName,
                            Is.EqualTo(goldenLayer.blendShapeName ?? ""));
                    }
                    else
                    {
                        Assert.That(actualLayer.BlendShapeOutput, Is.EqualTo(BlendShapeOutputMode.Disabled));
                        Assert.That(actualLayer.BlendShapeName, Is.Empty);
                    }
                    AssertDefaultCurve(actualLayer.BlendShapeCurve, tolerance, label + ".curve");
                    AssertVectors(
                        goldenLayer.brushDisplacements ?? Array.Empty<Vector3>(),
                        actualLayer.BrushDisplacements,
                        tolerance,
                        label + ".brushDisplacements");
                    AssertFloats(
                        goldenLayer.vertexMask ?? Array.Empty<float>(),
                        actualLayer.VertexMask,
                        tolerance,
                        label + ".vertexMask");

                    if (goldenLayer.settings != null)
                    {
                        var settings = actualLayer.Settings;
                        Assert.That(settings.GridSize, Is.EqualTo(goldenLayer.settings.gridSize));
                        AssertVector(goldenLayer.settings.boundsCenter, settings.LocalBounds.center, tolerance, label + ".boundsCenter");
                        AssertVector(goldenLayer.settings.boundsSize, settings.LocalBounds.size, tolerance, label + ".boundsSize");
                        Assert.That(settings.Interpolation.ToString(), Is.EqualTo(goldenLayer.settings.interpolation));
                        Assert.That(settings.LegacyApplySpaceValue, Is.EqualTo(goldenLayer.settings.legacyApplySpaceValue));
                        AssertVectors(goldenLayer.settings.controlPoints, settings.ControlPointsLocal.ToArray(), tolerance,
                            label + ".controlPoints");
                    }
                }
            }
        }

        private static void AssertNonDefaultCompatibilityContract(
            FixtureCase fixture,
            FixtureExpected expected,
            LatticeDeformer deformer)
        {
            if (fixture.IsLegacyBrush)
            {
                return;
            }

            if (fixture.Kind != "lattice-world")
            {
                Assert.That(ContainsCubicBernsteinLattice(expected.groups), Is.True,
                    "The local release golden must explicitly cover CubicBernstein interpolation.");
                Assert.That(
                    deformer.Groups
                        .Where(group => group != null)
                        .SelectMany(group => group.Layers)
                        .Any(layer => layer != null && layer.Type == MeshDeformerLayerType.Lattice &&
                                      layer.Settings.Interpolation == LatticeInterpolationMode.CubicBernstein),
                    Is.True,
                    "The upgraded local fixture lost its CubicBernstein lattice interpolation.");
                Assert.That(
                    deformer.Groups
                        .Where(group => group != null)
                        .SelectMany(group => group.Layers)
                        .Where(layer => layer != null && layer.Type == MeshDeformerLayerType.Lattice &&
                                        layer.Settings.Interpolation == LatticeInterpolationMode.CubicBernstein)
                        .All(layer => layer.Settings.UsesLegacyTrilinearInterpolation),
                    Is.True,
                    "Published CubicBernstein data must retain its historical trilinear output.");
            }

            if (fixture.Tag.Classification != DeformationDataVersion.V1_2_1)
            {
                return;
            }

            bool removeActiveLast = fixture.Kind == "lattice-remove-active-last";
            int requiredActiveGroupIndex = removeActiveLast ? 0 : 1;
            Assert.That(expected.activeGroupIndex, Is.EqualTo(requiredActiveGroupIndex));
            Assert.That(deformer.ActiveGroupIndex, Is.EqualTo(requiredActiveGroupIndex),
                "The historical active group selection was not preserved.");
            Assert.That(expected.serializedGroups, Has.Length.GreaterThanOrEqualTo(2));
            Assert.That(deformer.Groups, Has.Count.GreaterThanOrEqualTo(2));
            if (removeActiveLast)
            {
                var rawPrimary = expected.serializedGroups[0];
                Assert.That(rawPrimary.layers, Has.Length.GreaterThan(0));
                Assert.That(rawPrimary.activeLayerIndex, Is.EqualTo(rawPrimary.layers.Length),
                    "The actual tag fixture must retain the published one-past-end raw index.");
                Assert.That(expected.groups[0].activeLayerIndex,
                    Is.EqualTo(expected.groups[0].layers.Length - 1),
                    "The migration golden must canonicalize the removed-last selection.");
                Assert.That(deformer.Groups[0].ActiveLayerIndex,
                    Is.EqualTo(deformer.Groups[0].Layers.Count - 1),
                    "The removed-last selection was not canonicalized to the last remaining layer.");
            }
            else
            {
                Assert.That(expected.serializedGroups[0].activeLayerIndex, Is.EqualTo(1));
                Assert.That(expected.serializedGroups[0].layers, Has.Length.GreaterThan(1));
                Assert.That(expected.serializedGroups[0].layers[1].type, Is.EqualTo("Brush"));
                Assert.That(deformer.Groups[0].ActiveLayerIndex, Is.EqualTo(1),
                    "The inactive primary group's Brush-layer selection was not preserved.");
                Assert.That(deformer.Groups[0].Layers, Has.Count.GreaterThan(1));
                Assert.That(deformer.Groups[0].Layers[1].Type, Is.EqualTo(MeshDeformerLayerType.Brush));
            }

            Assert.That(deformer.Groups.Count, Is.EqualTo(expected.groups.Length));
            for (int groupIndex = 0; groupIndex < expected.groups.Length; groupIndex++)
            {
                var goldenGroup = expected.groups[groupIndex];
                Assert.That(goldenGroup.activeLayerIndex,
                    Is.InRange(0, goldenGroup.layers.Length - 1),
                    $"golden group[{groupIndex}].activeLayerIndex");
                Assert.That(deformer.Groups[groupIndex].ActiveLayerIndex,
                    Is.EqualTo(goldenGroup.activeLayerIndex),
                    $"group[{groupIndex}].activeLayerIndex");
            }
        }

        private static void AssertRawReloadedLatticePayload(
            FixtureCase fixture,
            FixtureExpected expected,
            LatticeDeformer deformer)
        {
            var serialized = new SerializedObject(deformer);
            serialized.UpdateIfRequiredOrScript();
            Assert.That(Require(serialized, "_deformationDataVersion").intValue,
                Is.EqualTo((int)DeformationDataVersion.CurrentDevelopment));
            Assert.That(Require(serialized, "_deformationDataSourceVersion").intValue,
                Is.EqualTo((int)fixture.Tag.Classification));
            Assert.That(Require(serialized, "_legacyAbsoluteLatticeEvaluation").boolValue,
                Is.EqualTo(expected.legacyAbsoluteEvaluation));
            Assert.That(Require(serialized, "_legacyPublishedBlendShapeSemantics").boolValue,
                Is.EqualTo(ExpectedLegacyPublishedBlendShapeSemantics(expected)));
            Assert.That(Require(serialized, "_layerModelVersion").intValue, Is.EqualTo(3));
            Assert.That(Require(serialized, "_activeGroupIndex").intValue,
                Is.EqualTo(expected.activeGroupIndex));
            Assert.That(Require(serialized, "_layers").arraySize, Is.Zero,
                "The obsolete flat-layer payload must stay empty after reload.");

            var groups = Require(serialized, "_groups");
            Assert.That(groups.arraySize, Is.EqualTo(expected.groups.Length));
            for (int groupIndex = 0; groupIndex < expected.groups.Length; groupIndex++)
            {
                var goldenGroup = expected.groups[groupIndex];
                var group = groups.GetArrayElementAtIndex(groupIndex);
                string groupLabel = $"raw.groups[{groupIndex}]";
                Assert.That(RequireRelative(group, "_name").stringValue, Is.EqualTo(goldenGroup.name), groupLabel);
                Assert.That(RequireRelative(group, "_enabled").boolValue, Is.EqualTo(goldenGroup.enabled), groupLabel);
                Assert.That(RequireRelative(group, "_activeLayerIndex").intValue,
                    Is.EqualTo(goldenGroup.activeLayerIndex), groupLabel);
                Assert.That(RequireRelative(group, "_blendShapeOutput").intValue,
                    Is.EqualTo((int)ParseEnum<BlendShapeOutputMode>(goldenGroup.blendShapeOutput)), groupLabel);
                Assert.That(RequireRelative(group, "_blendShapeName").stringValue,
                    Is.EqualTo(goldenGroup.blendShapeName ?? ""), groupLabel);

                var layers = RequireRelative(group, "_layers");
                var goldenLayers = goldenGroup.layers ?? Array.Empty<GoldenLayer>();
                Assert.That(layers.arraySize, Is.EqualTo(goldenLayers.Length), groupLabel + ".layers");
                for (int layerIndex = 0; layerIndex < goldenLayers.Length; layerIndex++)
                {
                    var goldenLayer = goldenLayers[layerIndex];
                    var layer = layers.GetArrayElementAtIndex(layerIndex);
                    string layerLabel = groupLabel + $".layers[{layerIndex}]";
                    Assert.That(RequireRelative(layer, "_name").stringValue, Is.EqualTo(goldenLayer.name), layerLabel);
                    Assert.That(RequireRelative(layer, "_type").intValue,
                        Is.EqualTo((int)ParseEnum<MeshDeformerLayerType>(goldenLayer.type)), layerLabel);
                    Assert.That(RequireRelative(layer, "_enabled").boolValue,
                        Is.EqualTo(goldenLayer.enabled), layerLabel);
                    Assert.That(RequireRelative(layer, "_weight").floatValue,
                        Is.EqualTo(goldenLayer.weight).Within(expected.tolerance), layerLabel);
                    var expectedLayerOutput = fixture.Tag.Classification == DeformationDataVersion.V1_2_1
                        ? ParseEnum<BlendShapeOutputMode>(goldenLayer.blendShapeOutput)
                        : BlendShapeOutputMode.Disabled;
                    string expectedLayerName = fixture.Tag.Classification == DeformationDataVersion.V1_2_1
                        ? goldenLayer.blendShapeName ?? ""
                        : "";
                    Assert.That(RequireRelative(layer, "_blendShapeOutput").intValue,
                        Is.EqualTo((int)expectedLayerOutput), layerLabel);
                    Assert.That(RequireRelative(layer, "_blendShapeName").stringValue,
                        Is.EqualTo(expectedLayerName), layerLabel);

                    var displacements = RequireRelative(layer, "_brushDisplacements");
                    AssertSerializedVectors(
                        goldenLayer.brushDisplacements ?? Array.Empty<Vector3>(),
                        displacements,
                        expected.tolerance,
                        layerLabel + ".brushDisplacements");
                    var mask = RequireRelative(layer, "_vertexMask");
                    AssertSerializedFloats(
                        goldenLayer.vertexMask ?? Array.Empty<float>(),
                        mask,
                        expected.tolerance,
                        layerLabel + ".vertexMask");

                    if (goldenLayer.settings != null)
                    {
                        var settings = RequireRelative(layer, "_settings");
                        Assert.That(RequireRelative(settings, "_gridSize").vector3IntValue,
                            Is.EqualTo(goldenLayer.settings.gridSize), layerLabel);
                        var bounds = RequireRelative(settings, "_localBounds").boundsValue;
                        AssertVector(goldenLayer.settings.boundsCenter, bounds.center, expected.tolerance,
                            layerLabel + ".boundsCenter");
                        AssertVector(goldenLayer.settings.boundsSize, bounds.size, expected.tolerance,
                            layerLabel + ".boundsSize");
                        Assert.That(RequireRelative(settings, "_interpolation").intValue,
                            Is.EqualTo((int)ParseEnum<LatticeInterpolationMode>(goldenLayer.settings.interpolation)),
                            layerLabel);
                        bool expectsLegacyTrilinearInterpolation =
                            ParseEnum<MeshDeformerLayerType>(goldenLayer.type) == MeshDeformerLayerType.Lattice &&
                            ParseEnum<LatticeInterpolationMode>(goldenLayer.settings.interpolation) ==
                            LatticeInterpolationMode.CubicBernstein;
                        Assert.That(
                            RequireRelative(settings, "_legacyTrilinearInterpolation").boolValue,
                            Is.EqualTo(expectsLegacyTrilinearInterpolation),
                            layerLabel);
                        Assert.That(RequireRelative(settings, "_applySpace").intValue,
                            Is.EqualTo(goldenLayer.settings.legacyApplySpaceValue), layerLabel);
                        Assert.That(RequireRelative(settings, "_serializationVersion").intValue,
                            Is.EqualTo(1), layerLabel);
                        AssertSerializedVectors(
                            goldenLayer.settings.controlPoints,
                            RequireRelative(settings, "_controlPointsLocal"),
                            expected.tolerance,
                            layerLabel + ".controlPoints");
                    }
                }
            }
        }

        private static void AssertRawHistoricalLatticePayload(
            FixtureExpected expected,
            LatticeDeformer deformer,
            DeformationDataVersion version,
            DeformationDataVersion sourceVersion)
        {
            var serialized = new SerializedObject(deformer);
            serialized.UpdateIfRequiredOrScript();
            Assert.That(Require(serialized, "_deformationDataVersion").intValue,
                Is.EqualTo((int)version));
            Assert.That(Require(serialized, "_deformationDataSourceVersion").intValue,
                Is.EqualTo((int)sourceVersion));
            Assert.That(Require(serialized, "_legacyPublishedBlendShapeSemantics").boolValue, Is.False,
                "The compatibility flag must remain absent/false through classification.");
            Assert.That(Require(serialized, "_layerModelVersion").intValue,
                Is.EqualTo(Mathf.Max(0, expected.serializedLayerModelVersion)));
            Assert.That(Require(serialized, "_activeGroupIndex").intValue,
                Is.EqualTo(expected.activeGroupIndex),
                "The raw historical active group selection must survive deserialization/classification.");
            var flatLayers = Require(serialized, "_layers");
            int expectedLoadedFlatCount = Math.Max(0, expected.serializedFlatLayerCount);
            Assert.That(flatLayers.arraySize, Is.EqualTo(expectedLoadedFlatCount));
            for (int layerIndex = 0; layerIndex < flatLayers.arraySize; layerIndex++)
            {
                AssertRawLayerProjection(
                    expected.serializedFlatLayers[layerIndex],
                    flatLayers.GetArrayElementAtIndex(layerIndex),
                    expected.tolerance,
                    $"raw historical flatLayers[{layerIndex}]");
            }
            if (flatLayers.arraySize > 0)
            {
                Assert.That(Require(serialized, "_activeLayerIndex").intValue,
                    Is.EqualTo(expected.serializedActiveFlatLayerIndex));
                Assert.That(Require(serialized, "_blendShapeOutput").intValue,
                    Is.EqualTo((int)ParseEnum<BlendShapeOutputMode>(expected.serializedFlatBlendShapeOutput)));
                Assert.That(Require(serialized, "_blendShapeName").stringValue,
                    Is.EqualTo(expected.serializedFlatBlendShapeName ?? ""));
                AssertCurve(
                    expected.serializedFlatBlendShapeCurve,
                    Require(serialized, "_blendShapeCurve").animationCurveValue,
                    expected.tolerance,
                    "raw historical flat blend shape curve");
            }
            var groups = Require(serialized, "_groups");
            int expectedRawGroupCount = expected.serializedGroups.Length;
            Assert.That(groups.arraySize, Is.EqualTo(expectedRawGroupCount));
            for (int groupIndex = 0; groupIndex < expectedRawGroupCount; groupIndex++)
            {
                AssertRawGroupProjection(
                    expected.serializedGroups[groupIndex],
                    groups.GetArrayElementAtIndex(groupIndex),
                    expected.tolerance,
                    $"raw historical groups[{groupIndex}]");
            }

            if (expectedRawGroupCount == 0 && expected.serializedFlatLayerCount < 0)
            {
                var goldenSettings = expected.groups[0].layers[0].settings;
                var rawSettings = Require(serialized, "_settings");
                Assert.That(RequireRelative(rawSettings, "_interpolation").intValue,
                    Is.EqualTo((int)ParseEnum<LatticeInterpolationMode>(goldenSettings.interpolation)),
                    "The raw single-settings interpolation must match the release golden.");
            }
        }

        private static void AssertHybridNormalizationBoundary(
            FixtureCase fixture,
            FixtureExpected expected,
            LatticeDeformer deformer,
            DeformationDataVersion version)
        {
            Assert.That(version, Is.EqualTo(DeformationDataVersion.V1_3_0));
            var serialized = new SerializedObject(deformer);
            serialized.UpdateIfRequiredOrScript();
            Assert.That(Require(serialized, "_deformationDataVersion").intValue, Is.EqualTo((int)version));
            Assert.That(Require(serialized, "_deformationDataSourceVersion").intValue,
                Is.EqualTo((int)fixture.Tag.Classification));
            Assert.That(Require(serialized, "_legacyPublishedBlendShapeSemantics").boolValue,
                Is.EqualTo(ExpectedLegacyPublishedBlendShapeSemantics(expected)));
            Assert.That(Require(serialized, "_layerModelVersion").intValue, Is.EqualTo(3));
            Assert.That(Require(serialized, "_layers").arraySize, Is.Zero);
            Assert.That(Require(serialized, "_activeGroupIndex").intValue,
                Is.EqualTo(expected.activeGroupIndex), "Published active group must remain authoritative.");

            var groups = Require(serialized, "_groups");
            Assert.That(groups.arraySize, Is.EqualTo(expected.groups.Length));
            for (int groupIndex = 0; groupIndex < expected.groups.Length; groupIndex++)
            {
                AssertRawGroupProjection(
                    expected.groups[groupIndex],
                    groups.GetArrayElementAtIndex(groupIndex),
                    expected.tolerance,
                    $"normalized groups[{groupIndex}]");
            }

            Assert.That(expected.groups.Length, Is.EqualTo(expected.serializedGroups.Length + 1));
            for (int groupIndex = 0; groupIndex < expected.serializedGroups.Length; groupIndex++)
            {
                var canonicalPublishedGroup = JsonUtility.FromJson<GoldenGroup>(
                    JsonUtility.ToJson(expected.serializedGroups[groupIndex]));
                canonicalPublishedGroup.activeLayerIndex = canonicalPublishedGroup.layers == null ||
                                                          canonicalPublishedGroup.layers.Length == 0
                    ? 0
                    : Mathf.Clamp(
                        canonicalPublishedGroup.activeLayerIndex,
                        0,
                        canonicalPublishedGroup.layers.Length - 1);
                Assert.That(
                    JsonUtility.ToJson(expected.groups[groupIndex]),
                    Is.EqualTo(JsonUtility.ToJson(canonicalPublishedGroup)),
                    $"canonical published group prefix[{groupIndex}]");
            }
            var recovery = expected.groups[expected.groups.Length - 1];
            Assert.That(recovery.name, Is.EqualTo("Recovered Legacy Flat Layers"));
            Assert.That(recovery.enabled, Is.False);
            Assert.That(recovery.activeLayerIndex, Is.EqualTo(expected.serializedActiveFlatLayerIndex));
            Assert.That(recovery.blendShapeOutput,
                Is.EqualTo(expected.serializedFlatBlendShapeOutput));
            Assert.That(recovery.blendShapeName,
                Is.EqualTo(expected.serializedFlatBlendShapeName ?? ""));
            AssertCurveEqualGolden(
                expected.serializedFlatBlendShapeCurve,
                recovery.blendShapeCurve,
                expected.tolerance,
                "recovery curve golden");
            Assert.That(recovery.layers.Length, Is.EqualTo(expected.serializedFlatLayers.Length));
            for (int layerIndex = 0; layerIndex < recovery.layers.Length; layerIndex++)
            {
                Assert.That(
                    JsonUtility.ToJson(recovery.layers[layerIndex]),
                    Is.EqualTo(JsonUtility.ToJson(expected.serializedFlatLayers[layerIndex])),
                    $"recovery layer projection[{layerIndex}]");
            }

        }

        private static void AssertCurveEqualGolden(
            GoldenCurve expected,
            GoldenCurve actual,
            float tolerance,
            string label)
        {
            Assert.That(expected, Is.Not.Null, label);
            Assert.That(actual, Is.Not.Null, label);
            Assert.That(actual.preWrapMode, Is.EqualTo(expected.preWrapMode), label + ".preWrapMode");
            Assert.That(actual.postWrapMode, Is.EqualTo(expected.postWrapMode), label + ".postWrapMode");
            var expectedKeys = expected.keys ?? Array.Empty<GoldenKeyframe>();
            var actualKeys = actual.keys ?? Array.Empty<GoldenKeyframe>();
            Assert.That(actualKeys.Length, Is.EqualTo(expectedKeys.Length), label + ".keys");
            for (int i = 0; i < expectedKeys.Length; i++)
            {
                Assert.That(actualKeys[i].time, Is.EqualTo(expectedKeys[i].time).Within(tolerance), label + $"[{i}].time");
                Assert.That(actualKeys[i].value, Is.EqualTo(expectedKeys[i].value).Within(tolerance), label + $"[{i}].value");
                Assert.That(actualKeys[i].inTangent, Is.EqualTo(expectedKeys[i].inTangent).Within(tolerance), label + $"[{i}].inTangent");
                Assert.That(actualKeys[i].outTangent, Is.EqualTo(expectedKeys[i].outTangent).Within(tolerance), label + $"[{i}].outTangent");
                Assert.That(actualKeys[i].inWeight, Is.EqualTo(expectedKeys[i].inWeight).Within(tolerance), label + $"[{i}].inWeight");
                Assert.That(actualKeys[i].outWeight, Is.EqualTo(expectedKeys[i].outWeight).Within(tolerance), label + $"[{i}].outWeight");
                Assert.That(actualKeys[i].weightedMode, Is.EqualTo(expectedKeys[i].weightedMode), label + $"[{i}].weightedMode");
            }
        }

        private static void AssertRawGroupProjection(
            GoldenGroup expected,
            SerializedProperty actual,
            float tolerance,
            string label)
        {
            Assert.That(RequireRelative(actual, "_name").stringValue, Is.EqualTo(expected.name), label);
            Assert.That(RequireRelative(actual, "_enabled").boolValue, Is.EqualTo(expected.enabled), label);
            Assert.That(RequireRelative(actual, "_activeLayerIndex").intValue,
                Is.EqualTo(expected.activeLayerIndex), label);
            Assert.That(RequireRelative(actual, "_blendShapeOutput").intValue,
                Is.EqualTo((int)ParseEnum<BlendShapeOutputMode>(expected.blendShapeOutput)), label);
            Assert.That(RequireRelative(actual, "_blendShapeName").stringValue,
                Is.EqualTo(expected.blendShapeName ?? ""), label);
            AssertCurve(
                expected.blendShapeCurve,
                RequireRelative(actual, "_blendShapeCurve").animationCurveValue,
                tolerance,
                label + ".curve");
            var layers = RequireRelative(actual, "_layers");
            var expectedLayers = expected.layers ?? Array.Empty<GoldenLayer>();
            Assert.That(layers.arraySize, Is.EqualTo(expectedLayers.Length), label + ".layers");
            for (int layerIndex = 0; layerIndex < expectedLayers.Length; layerIndex++)
            {
                AssertRawLayerProjection(
                    expectedLayers[layerIndex],
                    layers.GetArrayElementAtIndex(layerIndex),
                    tolerance,
                    label + $".layers[{layerIndex}]");
            }
        }

        private static void AssertRawLayerProjection(
            GoldenLayer expected,
            SerializedProperty actual,
            float tolerance,
            string label)
        {
            Assert.That(RequireRelative(actual, "_name").stringValue, Is.EqualTo(expected.name), label);
            Assert.That(RequireRelative(actual, "_type").intValue,
                Is.EqualTo((int)ParseEnum<MeshDeformerLayerType>(expected.type)), label);
            Assert.That(RequireRelative(actual, "_enabled").boolValue, Is.EqualTo(expected.enabled), label);
            Assert.That(RequireRelative(actual, "_weight").floatValue,
                Is.EqualTo(expected.weight).Within(tolerance), label);
            Assert.That(RequireRelative(actual, "_blendShapeOutput").intValue,
                Is.EqualTo((int)ParseEnum<BlendShapeOutputMode>(expected.blendShapeOutput)), label);
            Assert.That(RequireRelative(actual, "_blendShapeName").stringValue,
                Is.EqualTo(expected.blendShapeName ?? ""), label);
            AssertSerializedVectors(
                expected.brushDisplacements ?? Array.Empty<Vector3>(),
                RequireRelative(actual, "_brushDisplacements"),
                tolerance,
                label + ".brushDisplacements");
            AssertSerializedFloats(
                expected.vertexMask ?? Array.Empty<float>(),
                RequireRelative(actual, "_vertexMask"),
                tolerance,
                label + ".vertexMask");
            if (expected.settings == null)
            {
                return;
            }

            var settings = RequireRelative(actual, "_settings");
            Assert.That(RequireRelative(settings, "_gridSize").vector3IntValue,
                Is.EqualTo(expected.settings.gridSize), label);
            Bounds bounds = RequireRelative(settings, "_localBounds").boundsValue;
            AssertVector(expected.settings.boundsCenter, bounds.center, tolerance, label + ".boundsCenter");
            AssertVector(expected.settings.boundsSize, bounds.size, tolerance, label + ".boundsSize");
            Assert.That(RequireRelative(settings, "_interpolation").intValue,
                Is.EqualTo((int)ParseEnum<LatticeInterpolationMode>(expected.settings.interpolation)), label);
            Assert.That(RequireRelative(settings, "_applySpace").intValue,
                Is.EqualTo(expected.settings.legacyApplySpaceValue), label);
            AssertSerializedVectors(
                expected.settings.controlPoints,
                RequireRelative(settings, "_controlPointsLocal"),
                tolerance,
                label + ".controlPoints");
        }

        private static void AssertRawReloadedLegacyBrushPayload(
            FixtureExpected expected,
            string beforeSaveState,
            BrushDeformer legacy,
            LatticeDeformer target)
        {
            var legacySerialized = new SerializedObject(legacy);
            legacySerialized.UpdateIfRequiredOrScript();
            AssertSerializedVectorsBitExact(
                expected.legacyBrush.displacements,
                Require(legacySerialized, "_displacements"),
                "raw legacy backup displacements");

            var expectedState = JsonUtility.FromJson<RuntimeState>(beforeSaveState);
            var targetSerialized = new SerializedObject(target);
            targetSerialized.UpdateIfRequiredOrScript();
            Assert.That(Require(targetSerialized, "_deformationDataVersion").intValue,
                Is.EqualTo((int)DeformationDataVersion.CurrentDevelopment));
            Assert.That(Require(targetSerialized, "_deformationDataSourceVersion").intValue,
                Is.EqualTo(expectedState.sourceVersion));
            Assert.That(Require(targetSerialized, "_legacyPublishedBlendShapeSemantics").boolValue,
                Is.EqualTo(expectedState.legacyPublishedBlendShapeSemantics));
            Assert.That(Require(targetSerialized, "_layerModelVersion").intValue, Is.EqualTo(3));
            Assert.That(Require(targetSerialized, "_layers").arraySize, Is.Zero);
            var groups = Require(targetSerialized, "_groups");
            Assert.That(groups.arraySize, Is.EqualTo(expectedState.groups.Length));

            int matchingLayers = 0;
            for (int groupIndex = 0; groupIndex < groups.arraySize; groupIndex++)
            {
                var layers = RequireRelative(groups.GetArrayElementAtIndex(groupIndex), "_layers");
                for (int layerIndex = 0; layerIndex < layers.arraySize; layerIndex++)
                {
                    var layer = layers.GetArrayElementAtIndex(layerIndex);
                    if (RequireRelative(layer, "_type").intValue != (int)MeshDeformerLayerType.Brush)
                    {
                        continue;
                    }

                    if (SerializedVectorsAreBitExact(
                            expected.legacyBrush.displacements,
                            RequireRelative(layer, "_brushDisplacements")))
                    {
                        matchingLayers++;
                    }
                }
            }
            Assert.That(matchingLayers, Is.EqualTo(1),
                "The raw reloaded target must contain exactly one lossless migrated brush payload.");
        }

        private static void AssertGoldenMesh(FixtureExpected expected, Mesh actual, string label)
        {
            AssertGoldenMesh(
                expected.expectedVertices,
                expected.outputBlendShapes,
                expected.tolerance,
                actual,
                label);
        }

        private static void AssertGoldenMesh(
            Vector3[] expectedVertices,
            GoldenBlendShape[] expectedBlendShapes,
            float tolerance,
            Mesh actual,
            string label)
        {
            AssertVectors(expectedVertices, actual.vertices, tolerance, label + ".vertices");
            var blendShapes = expectedBlendShapes ?? Array.Empty<GoldenBlendShape>();
            Assert.That(actual.blendShapeCount, Is.EqualTo(blendShapes.Length), label + ".blendShapeCount");
            for (int shapeIndex = 0; shapeIndex < blendShapes.Length; shapeIndex++)
            {
                var shape = blendShapes[shapeIndex];
                string shapeLabel = label + $".blendShapes[{shapeIndex}]";
                Assert.That(actual.GetBlendShapeName(shapeIndex), Is.EqualTo(shape.name), shapeLabel + ".name");
                var frames = shape.frames ?? Array.Empty<GoldenBlendShapeFrame>();
                Assert.That(actual.GetBlendShapeFrameCount(shapeIndex), Is.EqualTo(frames.Length),
                    shapeLabel + ".frameCount");
                for (int frameIndex = 0; frameIndex < frames.Length; frameIndex++)
                {
                    var frame = frames[frameIndex];
                    Assert.That(actual.GetBlendShapeFrameWeight(shapeIndex, frameIndex),
                        Is.EqualTo(frame.weight).Within(tolerance),
                        shapeLabel + $".frames[{frameIndex}].weight");
                    var vertices = new Vector3[actual.vertexCount];
                    var normals = new Vector3[actual.vertexCount];
                    var tangents = new Vector3[actual.vertexCount];
                    actual.GetBlendShapeFrameVertices(
                        shapeIndex,
                        frameIndex,
                        vertices,
                        normals,
                        tangents);
                    AssertVectors(
                        frame.deltaVertices,
                        vertices,
                        tolerance,
                        shapeLabel + $".frames[{frameIndex}].deltaVertices");
                    AssertVectors(
                        frame.deltaNormals,
                        normals,
                        tolerance,
                        shapeLabel + $".frames[{frameIndex}].deltaNormals");
                    AssertVectors(
                        frame.deltaTangents,
                        tangents,
                        tolerance,
                        shapeLabel + $".frames[{frameIndex}].deltaTangents");
                }
            }
        }

        private static void AssertWorldTransformProbes(
            FixtureCase fixture,
            FixtureExpected expected,
            LatticeDeformer deformer)
        {
            if (fixture.Kind != "lattice-world")
            {
                Assert.That(expected.transformProbes, Is.Empty);
                return;
            }

            Assert.That(expected.transformProbes, Is.Not.Null.And.Not.Empty);
            Transform owner = deformer.transform;
            Vector3 savedPosition = owner.position;
            Quaternion savedRotation = owner.rotation;
            Vector3 savedScale = owner.localScale;
            string savedPayload = CaptureRuntimeState(deformer);
            foreach (var probe in expected.transformProbes)
            {
                try
                {
                    owner.SetPositionAndRotation(probe.position, probe.rotation);
                    owner.localScale = probe.scale;
                    deformer.InvalidateCache();
                    var output = deformer.Deform(false);
                    Assert.That(output, Is.Not.Null);
                    AssertGoldenMesh(
                        probe.expectedVertices,
                        probe.outputBlendShapes,
                        expected.tolerance,
                        output,
                        "world transform probe");
                }
                finally
                {
                    owner.SetPositionAndRotation(savedPosition, savedRotation);
                    owner.localScale = savedScale;
                    deformer.InvalidateCache();
                }
            }

            AssertVector(savedPosition, owner.position, 0f, "restored owner position");
            Assert.That(Quaternion.Dot(savedRotation, owner.rotation),
                Is.EqualTo(1f).Within(expected.tolerance), "restored owner rotation");
            AssertVector(savedScale, owner.localScale, 0f, "restored owner scale");
            Assert.That(CaptureRuntimeState(deformer), Is.EqualTo(savedPayload),
                "World probes must not mutate serialized deformation data.");
            AssertSourceReference(owner.root.gameObject, fixture.SourcePath);
            var restored = deformer.Deform(false);
            Assert.That(restored, Is.Not.Null);
            AssertGoldenMesh(expected, restored, "world transform restored output");
        }

        private static void AssertLegacyBrushGolden(FixtureExpected expected, BrushDeformer legacy)
        {
            Assert.That(expected.legacyBrush, Is.Not.Null);
            AssertVectorsBitExact(expected.legacyBrush.displacements, legacy.Displacements, "historical displacement");
            var serialized = new SerializedObject(legacy);
            serialized.UpdateIfRequiredOrScript();
            Assert.That(serialized.FindProperty("_recalculateNormals").boolValue,
                Is.EqualTo(expected.legacyBrush.recalculateNormals));
            Assert.That(serialized.FindProperty("_recalculateTangents").boolValue,
                Is.EqualTo(expected.legacyBrush.recalculateTangents));
            Assert.That(serialized.FindProperty("_recalculateBounds").boolValue,
                Is.EqualTo(expected.legacyBrush.recalculateBounds));
            Assert.That(serialized.FindProperty("_recalculateBoneWeights").boolValue,
                Is.EqualTo(expected.legacyBrush.recalculateBoneWeights));
            AssertWeightTransfer(expected.legacyBrush.weightTransfer, legacy.WeightTransferSettings, expected.tolerance);
        }

        private static void AssertMigratedLegacyBrush(
            FixtureExpected expected,
            BrushDeformer legacy,
            LatticeDeformer target)
        {
            AssertLegacyBrushGolden(expected, legacy);
            Assert.That(legacy.enabled, Is.False);
            Assert.That(target.SerializedDeformationDataVersion,
                Is.EqualTo(DeformationDataVersion.CurrentDevelopment));
            var matches = target.Groups
                .Where(group => group != null)
                .SelectMany(group => group.Layers)
                .Where(layer => layer != null && layer.Type == MeshDeformerLayerType.Brush)
                .Where(layer => AreVectorsBitExact(expected.legacyBrush.displacements, layer.BrushDisplacements))
                .ToArray();
            Assert.That(matches, Has.Length.EqualTo(1));

            var serialized = new SerializedObject(target);
            serialized.UpdateIfRequiredOrScript();
            Assert.That(serialized.FindProperty("_recalculateNormals").boolValue,
                Is.EqualTo(expected.legacyBrush.recalculateNormals));
            Assert.That(serialized.FindProperty("_recalculateTangents").boolValue,
                Is.EqualTo(expected.legacyBrush.recalculateTangents));
            Assert.That(serialized.FindProperty("_recalculateBounds").boolValue,
                Is.EqualTo(expected.legacyBrush.recalculateBounds));
            Assert.That(serialized.FindProperty("_recalculateBoneWeights").boolValue,
                Is.EqualTo(expected.legacyBrush.recalculateBoneWeights));
            AssertWeightTransfer(expected.legacyBrush.weightTransfer, target.WeightTransferSettings, expected.tolerance);
        }

        private static void AssertComponentSettings(
            GoldenComponentSettings expected,
            LatticeDeformer actual,
            float tolerance)
        {
            Assert.That(expected, Is.Not.Null);
            var serialized = new SerializedObject(actual);
            serialized.UpdateIfRequiredOrScript();
            Assert.That(serialized.FindProperty("_recalculateNormals").boolValue,
                Is.EqualTo(expected.recalculateNormals));
            Assert.That(serialized.FindProperty("_recalculateTangents").boolValue,
                Is.EqualTo(expected.recalculateTangents));
            Assert.That(serialized.FindProperty("_recalculateBounds").boolValue,
                Is.EqualTo(expected.recalculateBounds));
            Assert.That(serialized.FindProperty("_recalculateBoneWeights").boolValue,
                Is.EqualTo(expected.recalculateBoneWeights));
            AssertWeightTransfer(expected.weightTransfer, actual.WeightTransferSettings, tolerance);
        }

        private static void AssertWeightTransfer(
            GoldenWeightTransfer expected,
            WeightTransferSettingsData actual,
            float tolerance)
        {
            Assert.That(expected, Is.Not.Null);
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual.maxTransferDistance, Is.EqualTo(expected.maxTransferDistance).Within(tolerance));
            Assert.That(actual.normalAngleThreshold, Is.EqualTo(expected.normalAngleThreshold).Within(tolerance));
            Assert.That(actual.enableInpainting, Is.EqualTo(expected.enableInpainting));
            Assert.That(actual.maxIterations, Is.EqualTo(expected.maxIterations));
            Assert.That(actual.tolerance, Is.EqualTo(expected.tolerance).Within(tolerance));
        }

        private static string CaptureRuntimeState(LatticeDeformer deformer)
        {
            var serialized = new SerializedObject(deformer);
            serialized.UpdateIfRequiredOrScript();
            var state = new RuntimeState
            {
                version = (int)deformer.SerializedDeformationDataVersion,
                sourceVersion = (int)deformer.SourceDeformationDataVersion,
                legacyAbsoluteEvaluation = deformer.UsesLegacyAbsoluteLatticeEvaluation,
                activeGroupIndex = deformer.ActiveGroupIndex,
                groups = deformer.Groups.Select(CaptureGroup).ToArray(),
                componentSettings = CaptureComponentSettings(deformer),
                legacyPublishedBlendShapeSemantics = Require(
                    serialized,
                    "_legacyPublishedBlendShapeSemantics").boolValue,
            };
            return JsonUtility.ToJson(state);
        }

        private static GoldenComponentSettings CaptureComponentSettings(LatticeDeformer deformer)
        {
            var serialized = new SerializedObject(deformer);
            serialized.UpdateIfRequiredOrScript();
            var weights = deformer.WeightTransferSettings;
            return new GoldenComponentSettings
            {
                recalculateNormals = serialized.FindProperty("_recalculateNormals").boolValue,
                recalculateTangents = serialized.FindProperty("_recalculateTangents").boolValue,
                recalculateBounds = serialized.FindProperty("_recalculateBounds").boolValue,
                recalculateBoneWeights = serialized.FindProperty("_recalculateBoneWeights").boolValue,
                weightTransfer = new GoldenWeightTransfer
                {
                    maxTransferDistance = weights.maxTransferDistance,
                    normalAngleThreshold = weights.normalAngleThreshold,
                    enableInpainting = weights.enableInpainting,
                    maxIterations = weights.maxIterations,
                    tolerance = weights.tolerance,
                },
            };
        }

        private static GoldenGroup CaptureGroup(DeformerGroup group)
        {
            return new GoldenGroup
            {
                name = group.Name,
                enabled = group.Enabled,
                activeLayerIndex = group.ActiveLayerIndex,
                blendShapeOutput = group.BlendShapeOutput.ToString(),
                blendShapeName = group.BlendShapeName,
                blendShapeCurve = CaptureCurve(group.BlendShapeCurve),
                layers = group.Layers.Select(CaptureLayer).ToArray(),
            };
        }

        private static GoldenLayer CaptureLayer(LatticeLayer layer)
        {
            var result = new GoldenLayer
            {
                name = layer.Name,
                type = layer.Type.ToString(),
                enabled = layer.Enabled,
                weight = layer.Weight,
                blendShapeOutput = layer.BlendShapeOutput.ToString(),
                blendShapeName = layer.BlendShapeName,
                blendShapeCurve = CaptureCurve(layer.BlendShapeCurve),
                brushDisplacements = layer.BrushDisplacements.ToArray(),
                vertexMask = layer.VertexMask.ToArray(),
            };
            var settings = layer.Settings;
            result.settings = new GoldenLatticeSettings
            {
                gridSize = settings.GridSize,
                boundsCenter = settings.LocalBounds.center,
                boundsSize = settings.LocalBounds.size,
                interpolation = settings.Interpolation.ToString(),
                legacyApplySpaceValue = settings.LegacyApplySpaceValue,
                controlPoints = settings.ControlPointsLocal.ToArray(),
            };
            return result;
        }

        private static GoldenCurve CaptureCurve(AnimationCurve curve)
        {
            return new GoldenCurve
            {
                preWrapMode = (int)curve.preWrapMode,
                postWrapMode = (int)curve.postWrapMode,
                keys = curve.keys.Select(key => new GoldenKeyframe
                {
                    time = key.time,
                    value = key.value,
                    inTangent = key.inTangent,
                    outTangent = key.outTangent,
                    inWeight = key.inWeight,
                    outWeight = key.outWeight,
                    weightedMode = (int)key.weightedMode,
                }).ToArray(),
            };
        }

        private static void AssertCurve(GoldenCurve expected, AnimationCurve actual, float tolerance, string label)
        {
            Assert.That(expected, Is.Not.Null, label);
            Assert.That(actual, Is.Not.Null, label);
            Assert.That((int)actual.preWrapMode, Is.EqualTo(expected.preWrapMode), label + ".preWrapMode");
            Assert.That((int)actual.postWrapMode, Is.EqualTo(expected.postWrapMode), label + ".postWrapMode");
            var keys = expected.keys ?? Array.Empty<GoldenKeyframe>();
            Assert.That(actual.length, Is.EqualTo(keys.Length), label + ".keys");
            for (int i = 0; i < keys.Length; i++)
            {
                var lhs = keys[i];
                var rhs = actual.keys[i];
                Assert.That(rhs.time, Is.EqualTo(lhs.time).Within(tolerance), label + $".keys[{i}].time");
                Assert.That(rhs.value, Is.EqualTo(lhs.value).Within(tolerance), label + $".keys[{i}].value");
                Assert.That(rhs.inTangent, Is.EqualTo(lhs.inTangent).Within(tolerance), label + $".keys[{i}].inTangent");
                Assert.That(rhs.outTangent, Is.EqualTo(lhs.outTangent).Within(tolerance), label + $".keys[{i}].outTangent");
                Assert.That(rhs.inWeight, Is.EqualTo(lhs.inWeight).Within(tolerance), label + $".keys[{i}].inWeight");
                Assert.That(rhs.outWeight, Is.EqualTo(lhs.outWeight).Within(tolerance), label + $".keys[{i}].outWeight");
                Assert.That((int)rhs.weightedMode, Is.EqualTo(lhs.weightedMode), label + $".keys[{i}].weightedMode");
            }
        }

        private static void AssertDefaultCurve(AnimationCurve actual, float tolerance, string label)
        {
            AssertCurve(
                CaptureCurve(AnimationCurve.Linear(0f, 0f, 1f, 1f)),
                actual,
                tolerance,
                label);
        }

        private static IReadOnlyList<DeformationDataVersion> ExpectedReleaseTail(DeformationDataVersion start)
        {
            return Enum.GetValues(typeof(DeformationDataVersion))
                .Cast<DeformationDataVersion>()
                .Where(version => version >= start && version <= DeformationDataVersion.CurrentDevelopment)
                .ToArray();
        }

        private static bool ExpectedLegacyPublishedBlendShapeSemantics(FixtureExpected expected)
        {
            return (expected.serializedGroups ?? Array.Empty<GoldenGroup>()).Any(group =>
                group != null &&
                (ParseEnum<BlendShapeOutputMode>(group.blendShapeOutput) ==
                 BlendShapeOutputMode.OutputAsBlendShape ||
                 (group.layers ?? Array.Empty<GoldenLayer>()).Any(layer =>
                     layer != null &&
                     ParseEnum<BlendShapeOutputMode>(layer.blendShapeOutput) ==
                     BlendShapeOutputMode.OutputAsBlendShape)));
        }

        private static void AssertLegacyPublishedBlendShapeSemantics(
            LatticeDeformer deformer,
            bool expected)
        {
            var serialized = new SerializedObject(deformer);
            serialized.UpdateIfRequiredOrScript();
            Assert.That(Require(serialized, "_legacyPublishedBlendShapeSemantics").boolValue,
                Is.EqualTo(expected));
        }

        private static Mesh AssertSourceReference(GameObject root, string expectedPath)
        {
            var meshes = root.GetComponentsInChildren<Renderer>(true)
                .Select(renderer => renderer is SkinnedMeshRenderer skinned
                    ? skinned.sharedMesh
                    : renderer.GetComponent<MeshFilter>()?.sharedMesh)
                .Where(mesh => mesh != null)
                .Distinct()
                .ToArray();
            Assert.That(meshes, Has.Length.EqualTo(1));
            Assert.That(AssetDatabase.GetAssetPath(meshes[0]), Is.EqualTo(expectedPath));
            return meshes[0];
        }

        private static T FindSingleComponent<T>(GameObject root, string objectPath) where T : Component
        {
            if (!string.IsNullOrEmpty(objectPath))
            {
                var owner = root.transform.Find(objectPath);
                Assert.That(owner, Is.Not.Null, $"Component owner path was not found: {objectPath}");
                var component = owner.GetComponent<T>();
                Assert.That(component, Is.Not.Null, $"{typeof(T).Name} was not found at {objectPath}.");
                return component;
            }

            var components = root.GetComponentsInChildren<T>(true);
            Assert.That(components, Has.Length.EqualTo(1), $"Expected one {typeof(T).Name} in fixture.");
            return components[0];
        }

        private static GameObject InstantiateDisconnectedPrefab(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"Historical prefab is missing: {prefabPath}");
            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            Assert.That(instance, Is.Not.Null);
            if (PrefabUtility.IsPartOfPrefabInstance(instance))
            {
                PrefabUtility.UnpackPrefabInstance(
                    instance,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
            }
            return instance;
        }

        private static GameObject SaveReloadAndReplace(
            GameObject instance,
            string temporaryFolder,
            string prefabFile)
        {
            string path = temporaryFolder + "/" + prefabFile;
            var saved = PrefabUtility.SaveAsPrefabAsset(instance, path, out bool success);
            Assert.That(success, Is.True, $"Failed to save migrated prefab at {path}.");
            Assert.That(saved, Is.Not.Null);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            Object.DestroyImmediate(instance);
            return InstantiateDisconnectedPrefab(path);
        }

        private static string CreateTemporaryAssetFolder()
        {
            string name = "__HistoricalReleaseFixtureTests_" + Guid.NewGuid().ToString("N");
            string guid = AssetDatabase.CreateFolder("Assets", name);
            Assert.That(guid, Is.Not.Empty);
            return "Assets/" + name;
        }

        private static void AddRequiredFile(ISet<string> files, string file)
        {
            files.Add(file);
            files.Add(file + ".meta");
        }

        private static bool TryRequireFile(string path, ICollection<string> errors, string tag)
        {
            if (File.Exists(AbsolutePath(path))) return true;
            errors.Add($"{tag}: required file is missing ({path}).");
            return false;
        }

        private static SerializedProperty Require(SerializedObject serialized, string name)
        {
            var property = serialized.FindProperty(name);
            Assert.That(property, Is.Not.Null, $"Serialized field is missing: {serialized.targetObject.GetType().Name}.{name}");
            return property;
        }

        private static SerializedProperty RequireRelative(SerializedProperty parent, string name)
        {
            var property = parent.FindPropertyRelative(name);
            Assert.That(property, Is.Not.Null, $"Serialized field is missing: {parent.propertyPath}.{name}");
            return property;
        }

        private static T ParseEnum<T>(string value) where T : struct
        {
            Assert.That(Enum.TryParse(value, out T result), Is.True,
                $"Unknown {typeof(T).Name} golden value: {value ?? "<null>"}");
            return result;
        }

        private static void AssertSerializedVectors(
            IReadOnlyList<Vector3> expected,
            SerializedProperty actual,
            float tolerance,
            string label)
        {
            Assert.That(actual.isArray, Is.True, label);
            Assert.That(actual.arraySize, Is.EqualTo(expected.Count), label + ".count");
            for (int i = 0; i < expected.Count; i++)
            {
                AssertVector(
                    expected[i],
                    actual.GetArrayElementAtIndex(i).vector3Value,
                    tolerance,
                    label + $"[{i}]");
            }
        }

        private static void AssertSerializedFloats(
            IReadOnlyList<float> expected,
            SerializedProperty actual,
            float tolerance,
            string label)
        {
            Assert.That(actual.isArray, Is.True, label);
            Assert.That(actual.arraySize, Is.EqualTo(expected.Count), label + ".count");
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.That(actual.GetArrayElementAtIndex(i).floatValue,
                    Is.EqualTo(expected[i]).Within(tolerance),
                    label + $"[{i}]");
            }
        }

        private static void AssertSerializedVectorsBitExact(
            IReadOnlyList<Vector3> expected,
            SerializedProperty actual,
            string label)
        {
            Assert.That(SerializedVectorsAreBitExact(expected, actual), Is.True, label);
        }

        private static bool SerializedVectorsAreBitExact(
            IReadOnlyList<Vector3> expected,
            SerializedProperty actual)
        {
            if (expected == null || actual == null || !actual.isArray || actual.arraySize != expected.Count)
            {
                return false;
            }

            for (int i = 0; i < expected.Count; i++)
            {
                Vector3 value = actual.GetArrayElementAtIndex(i).vector3Value;
                if (BitConverter.SingleToInt32Bits(value.x) != BitConverter.SingleToInt32Bits(expected[i].x) ||
                    BitConverter.SingleToInt32Bits(value.y) != BitConverter.SingleToInt32Bits(expected[i].y) ||
                    BitConverter.SingleToInt32Bits(value.z) != BitConverter.SingleToInt32Bits(expected[i].z))
                {
                    return false;
                }
            }

            return true;
        }

        private static void Expect(
            ICollection<string> errors,
            string tag,
            string label,
            string actual,
            string expected)
        {
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                errors.Add($"{tag}: {label} must be '{expected}', actual '{actual ?? "<null>"}'.");
            }
        }

        private static string AbsolutePath(string assetPath)
        {
            string packagePrefix = PackageRoot + "/";
            if (assetPath.StartsWith(packagePrefix, StringComparison.Ordinal))
            {
                PackageManagerInfo package = PackageManagerInfo.FindForAssembly(typeof(LatticeDeformer).Assembly);
                if (package == null ||
                    !string.Equals(package.name, "net.32ba.lattice-deformation-tool", StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(package.resolvedPath))
                {
                    throw new InvalidOperationException(
                        "Could not resolve the package filesystem root through PackageInfo.");
                }

                string relative = assetPath.Substring(packagePrefix.Length)
                    .Replace('/', Path.DirectorySeparatorChar);
                return Path.GetFullPath(Path.Combine(package.resolvedPath, relative));
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(
                projectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string ComputeSha256(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return string.Concat(algorithm.ComputeHash(stream).Select(value => value.ToString("x2")));
            }
        }

        private static void AssertVectors(
            IReadOnlyList<Vector3> expected,
            IReadOnlyList<Vector3> actual,
            float tolerance,
            string label)
        {
            Assert.That(expected, Is.Not.Null, label);
            Assert.That(actual, Is.Not.Null, label);
            Assert.That(actual.Count, Is.EqualTo(expected.Count), label + ".count");
            for (int i = 0; i < expected.Count; i++)
            {
                AssertVector(expected[i], actual[i], tolerance, label + $"[{i}]");
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void AssertFloats(
            IReadOnlyList<float> expected,
            IReadOnlyList<float> actual,
            float tolerance,
            string label)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count), label + ".count");
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.That(actual[i], Is.EqualTo(expected[i]).Within(tolerance), label + $"[{i}]");
            }
        }

        private static void AssertVector(Vector3 expected, Vector3 actual, float tolerance, string label)
        {
            if (tolerance == 0f)
            {
                Assert.That(BitConverter.SingleToInt32Bits(actual.x),
                    Is.EqualTo(BitConverter.SingleToInt32Bits(expected.x)), label + ".x");
                Assert.That(BitConverter.SingleToInt32Bits(actual.y),
                    Is.EqualTo(BitConverter.SingleToInt32Bits(expected.y)), label + ".y");
                Assert.That(BitConverter.SingleToInt32Bits(actual.z),
                    Is.EqualTo(BitConverter.SingleToInt32Bits(expected.z)), label + ".z");
                return;
            }

            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance), label + ".x");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance), label + ".y");
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tolerance), label + ".z");
        }

        private static void AssertVectorsBitExact(Vector3[] expected, Vector3[] actual, string label)
        {
            Assert.That(AreVectorsBitExact(expected, actual), Is.True, label);
        }

        private static bool AreVectorsBitExact(IReadOnlyList<Vector3> left, IReadOnlyList<Vector3> right)
        {
            if (left == null || right == null || left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++)
            {
                if (BitConverter.SingleToInt32Bits(left[i].x) != BitConverter.SingleToInt32Bits(right[i].x) ||
                    BitConverter.SingleToInt32Bits(left[i].y) != BitConverter.SingleToInt32Bits(right[i].y) ||
                    BitConverter.SingleToInt32Bits(left[i].z) != BitConverter.SingleToInt32Bits(right[i].z))
                {
                    return false;
                }
            }
            return true;
        }

        internal sealed class TagDefinition
        {
            internal TagDefinition(
                string tag,
                string commitSha,
                DeformationDataVersion classification,
                bool hasLegacyBrush = false)
            {
                Tag = tag;
                CommitSha = commitSha;
                Classification = classification;
                HasLegacyBrush = hasLegacyBrush;
            }

            internal string Tag { get; }
            internal string CommitSha { get; }
            internal DeformationDataVersion Classification { get; }
            internal bool HasLegacyBrush { get; }
        }

        public sealed class FixtureCase
        {
            private FixtureCase(
                TagDefinition tag,
                string kind,
                string prefabFile,
                string expectedFile,
                bool legacyBrush)
            {
                Tag = tag;
                Kind = kind;
                PrefabFile = prefabFile;
                ExpectedFile = expectedFile;
                IsLegacyBrush = legacyBrush;
            }

            internal TagDefinition Tag { get; }
            internal string Kind { get; }
            internal string PrefabFile { get; }
            internal string ExpectedFile { get; }
            internal bool IsLegacyBrush { get; }
            internal string Directory => $"{FixtureRoot}/{Tag.Tag}";
            internal string PrefabPath => Directory + "/" + PrefabFile;
            internal string ExpectedPath => Directory + "/" + ExpectedFile;
            internal string SourcePath => Directory + "/source.asset";

            internal static FixtureCase Lattice(
                TagDefinition tag,
                string kind,
                string prefab,
                string expected)
            {
                return new FixtureCase(tag, kind, prefab, expected, false);
            }

            internal static FixtureCase LegacyBrush(
                TagDefinition tag,
                string prefab,
                string expected)
            {
                return new FixtureCase(tag, "legacy-brush", prefab, expected, true);
            }

            public override string ToString() => Tag.Tag + "/" + Kind;
        }

        [Serializable]
        private sealed class ReleaseManifest
        {
            public string tag;
            public string commitSha;
            public string packageVersion;
            public string unityVersion;
            public string generator;
            public string generatorSha256;
            public string runner;
            public string runnerSha256;
            public string metaGuidScheme;
            public string prefabFileIdScheme;
            public string generationMode;
            public string goldenOutputSource;
            public ManifestFixture[] fixtures;
            public ManifestFile[] files;
        }

        [Serializable]
        private sealed class ManifestFixture
        {
            public string kind;
            public string prefab;
            public string expected;
            public string source;
            public string goldenOutputSource;
        }

        [Serializable]
        private sealed class ManifestFile
        {
            public string path;
            public string sha256;
        }

        [Serializable]
        private sealed class FixtureExpected
        {
            public string tag;
            public string kind;
            public string deformerPath;
            public string classifiedVersion;
            public bool legacyAbsoluteEvaluation;
            public float tolerance;
            public int serializedLayerModelVersion;
            public int serializedFlatLayerCount;
            public GoldenGroup[] serializedGroups;
            public GoldenLayer[] serializedFlatLayers;
            public int serializedActiveFlatLayerIndex;
            public string serializedFlatBlendShapeOutput;
            public string serializedFlatBlendShapeName;
            public GoldenCurve serializedFlatBlendShapeCurve;
            public int activeGroupIndex;
            public GoldenGroup[] groups;
            public GoldenComponentSettings componentSettings;
            public Vector3[] expectedVertices;
            public GoldenBlendShape[] outputBlendShapes;
            public GoldenLegacyBrush legacyBrush;
            public GoldenWorldTransformProbe[] transformProbes;
        }

        [Serializable]
        private sealed class RuntimeState
        {
            public int version;
            public int sourceVersion;
            public bool legacyAbsoluteEvaluation;
            public int activeGroupIndex;
            public GoldenGroup[] groups;
            public GoldenComponentSettings componentSettings;
            public bool legacyPublishedBlendShapeSemantics;
        }

        [Serializable]
        private sealed class GoldenGroup
        {
            public string name;
            public bool enabled;
            public int activeLayerIndex;
            public string blendShapeOutput;
            public string blendShapeName;
            public GoldenCurve blendShapeCurve;
            public GoldenLayer[] layers;
        }

        [Serializable]
        private sealed class GoldenLayer
        {
            public string name;
            public string type;
            public bool enabled;
            public float weight;
            public string blendShapeOutput;
            public string blendShapeName;
            public GoldenCurve blendShapeCurve;
            public GoldenLatticeSettings settings;
            public Vector3[] brushDisplacements;
            public float[] vertexMask;
        }

        [Serializable]
        private sealed class GoldenLatticeSettings
        {
            public Vector3Int gridSize;
            public Vector3 boundsCenter;
            public Vector3 boundsSize;
            public string interpolation;
            public int legacyApplySpaceValue;
            public Vector3[] controlPoints;
        }

        [Serializable]
        private sealed class GoldenCurve
        {
            public int preWrapMode;
            public int postWrapMode;
            public GoldenKeyframe[] keys;
        }

        [Serializable]
        private sealed class GoldenKeyframe
        {
            public float time;
            public float value;
            public float inTangent;
            public float outTangent;
            public float inWeight;
            public float outWeight;
            public int weightedMode;
        }

        [Serializable]
        private sealed class GoldenLegacyBrush
        {
            public Vector3[] displacements;
            public bool recalculateNormals;
            public bool recalculateTangents;
            public bool recalculateBounds;
            public bool recalculateBoneWeights;
            public GoldenWeightTransfer weightTransfer;
        }

        [Serializable]
        private sealed class GoldenComponentSettings
        {
            public bool recalculateNormals;
            public bool recalculateTangents;
            public bool recalculateBounds;
            public bool recalculateBoneWeights;
            public GoldenWeightTransfer weightTransfer;
        }

        [Serializable]
        private sealed class GoldenBlendShape
        {
            public string name;
            public GoldenBlendShapeFrame[] frames;
        }

        [Serializable]
        private sealed class GoldenBlendShapeFrame
        {
            public float weight;
            public Vector3[] deltaVertices;
            public Vector3[] deltaNormals;
            public Vector3[] deltaTangents;
        }

        [Serializable]
        private sealed class GoldenWorldTransformProbe
        {
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public Vector3[] expectedVertices;
            public GoldenBlendShape[] outputBlendShapes;
        }

        [Serializable]
        private sealed class GoldenWeightTransfer
        {
            public float maxTransferDistance;
            public float normalAngleThreshold;
            public bool enableInpainting;
            public int maxIterations;
            public float tolerance;
        }
    }
}
#endif
