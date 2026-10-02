#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using MeshSnapshot = Net._32Ba.LatticeDeformationTool.Tests.Editor.DeformationOutputBaselineFixture.MeshSnapshot;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class LaterReleaseFixtureTests
    {
        private const string Package = ArchitectureContractSnapshot.PackagePath;
        private const string Corpus = Package + "/Tests/Editor/Fixtures/LaterReleases";
        private const string Inventory = Package + "/Docs~/Architecture/2026-09-07-published-releases.json";
        [Serializable] public sealed class Release { public string tag, commit, packageVersion; public bool historicalCorpus; }
        [Serializable] public sealed class Releases { public Release[] releases; }
        [Serializable] public sealed class FileRecord { public string path, sha256; }
        [Serializable] public sealed class Fixture { public string kind, prefab, expected, source, profile; }
        [Serializable] public sealed class Manifest
        {
            public int schemaVersion;
            public string tag, commitSha, packageVersion, unityVersion, generationMode, goldenOutputSource;
            public string metaGuidScheme, prefabFileIdScheme;
            public FileRecord[] tools, files;
            public Fixture[] fixtures;
        }
        [Serializable] public sealed class SavedValue { public string path, type, value; }
        [Serializable] public sealed class Expected
        {
            public string tag, kind;
            public int rawVersion, rawSourceVersion, rawLayerModelVersion, rawActiveGroupIndex, rawEmbeddedGroupCount, rawFlatLayerCount;
            public SavedValue[] componentValues, profileValues;
            public MeshSnapshot sourceBefore, sourceAfter, output;
            public bool rendererRetainedSource, inactivePrefab, disabledComponent;
        }
        [Serializable] public sealed class CurveValue { public CurveKey[] keys; public int preWrapMode, postWrapMode; }
        [Serializable] public sealed class CurveKey
        { public float time, value, inTangent, outTangent, inWeight, outWeight; public int weightedMode; }

        private static Release[] Published => JsonUtility.FromJson<Releases>(File.ReadAllText(Inventory))
            .releases.Where(r => !r.historicalCorpus).ToArray();
        public static IEnumerable<TestCaseData> Cases()
        {
            foreach (var release in Published)
                foreach (string kind in release.tag == "1.4.1"
                    ? new[] { "embedded-preserve", "embedded-rebuild" }
                    : new[] { "embedded-preserve", "embedded-rebuild", "profile" })
                    yield return new TestCaseData(release.tag, kind)
                        .SetName("LaterRelease_" + release.tag.Replace('.', '_') + "_" + kind + "_UpgradeAndSaveReload");
        }

        [Test]
        public void LaterCorpus_ContainsEveryPublishedReleaseWithExactTagAndToolProvenance()
        {
            Assert.That(Published.Length, Is.EqualTo(28));
            Assert.That(Directory.GetDirectories(Corpus).Select(Path.GetFileName).OrderBy(x => x),
                Is.EqualTo(Published.Select(r => r.tag).OrderBy(x => x)));
            foreach (var release in Published)
            {
                string directory = Corpus + "/" + release.tag;
                var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(directory + "/manifest.json"));
                Assert.That(manifest.schemaVersion, Is.EqualTo(1));
                Assert.That(manifest.tag, Is.EqualTo(release.tag));
                Assert.That(manifest.commitSha, Is.EqualTo(release.commit));
                Assert.That(manifest.packageVersion, Is.EqualTo(release.packageVersion));
                Assert.That(manifest.unityVersion, Is.EqualTo("2022.3.22f1"));
                Assert.That(manifest.generationMode, Is.EqualTo("unity-batchmode-tag-checkout"));
                Assert.That(manifest.goldenOutputSource, Is.EqualTo("historical-runtime-deform"));
                Assert.That(manifest.metaGuidScheme, Is.EqualTo("sha256-v1:tag/relative-asset-path"));
                Assert.That(manifest.prefabFileIdScheme, Is.EqualTo("sha256-v1:tag/relative-prefab/class/ordinal"));
                var expectedTools = new[]
                {
                    "Tools~/HistoricalFixtures/HistoricalFixtureGenerator.cs",
                    "Tools~/HistoricalFixtures/LaterReleases/LaterReleaseMeshCapture.cs",
                    "Tools~/HistoricalFixtures/LaterReleases/LaterReleaseFixtureGenerator.cs",
                    "Tools~/HistoricalFixtures/LaterReleases/Generate-LaterReleaseFixtures.ps1"
                };
                Assert.That(manifest.tools.Select(t => t.path), Is.EquivalentTo(expectedTools));
                foreach (var tool in manifest.tools)
                    Assert.That(Sha(Package + "/" + tool.path), Is.EqualTo(tool.sha256), release.tag + " " + tool.path);
                var expectedKinds = release.tag == "1.4.1" ? new[] { "embedded-preserve", "embedded-rebuild" }
                    : new[] { "embedded-preserve", "embedded-rebuild", "profile" };
                Assert.That(manifest.fixtures.Select(f => f.kind), Is.EquivalentTo(expectedKinds));
                var requiredFiles = new HashSet<string>(StringComparer.Ordinal) { "source.asset", "source.asset.meta" };
                foreach (var fixture in manifest.fixtures)
                {
                    Assert.That(fixture.prefab, Is.EqualTo(fixture.kind + ".prefab"));
                    Assert.That(fixture.expected, Is.EqualTo(fixture.kind + ".json"));
                    Assert.That(fixture.source, Is.EqualTo("source.asset"));
                    Assert.That(fixture.profile, Is.EqualTo(fixture.kind == "profile" ? "profile.asset" : ""));
                    foreach (string file in new[] { fixture.prefab, fixture.expected, fixture.profile }.Where(p => p != ""))
                    { requiredFiles.Add(file); requiredFiles.Add(file + ".meta"); }
                }
                Assert.That(manifest.files.Select(f => f.path), Is.EquivalentTo(requiredFiles));
                foreach (var file in manifest.files)
                {
                    Assert.That(file.path, Is.EqualTo(Path.GetFileName(file.path)));
                    Assert.That(Sha(directory + "/" + file.path), Is.EqualTo(file.sha256), release.tag + " " + file.path);
                    if (file.path.EndsWith(".meta", StringComparison.Ordinal))
                        AssertMetaGuid(directory + "/" + file.path, release.tag, file.path.Substring(0, file.path.Length - 5));
                    if (file.path.EndsWith(".prefab", StringComparison.Ordinal))
                        AssertPrefabIds(directory + "/" + file.path, release.tag, file.path);
                }
                AssertMetaGuid(directory + ".meta", release.tag, ".");
                AssertMetaGuid(directory + "/manifest.json.meta", release.tag, "manifest.json");
                Assert.That(Directory.GetFiles(directory).Select(Path.GetFileName),
                    Is.EquivalentTo(requiredFiles.Concat(new[] { "manifest.json", "manifest.json.meta" })));
            }
        }

        [TestCaseSource(nameof(Cases))]
        public void PublishedSavedData_UpgradesDirectlyAndStepwiseWithIdenticalOutputAndSelection(string tag, string kind)
        {
            string directory = Corpus + "/" + tag;
            var expected = JsonUtility.FromJson<Expected>(File.ReadAllText(directory + "/" + kind + ".json"));
            Assert.That(expected.tag, Is.EqualTo(tag));
            Assert.That(expected.kind, Is.EqualTo(kind));
            Assert.That(expected.inactivePrefab && expected.disabledComponent && expected.rendererRetainedSource, Is.True);
            Assert.That(expected.rawVersion, Is.EqualTo(tag == "1.4.1" ? -1 : 15));
            Assert.That(expected.rawActiveGroupIndex, Is.EqualTo(kind == "profile" ? 0 : 1));
            Assert.That(expected.output.frames.Length, Is.GreaterThan(4), "Fixture must exercise generated frames as well as source frames.");
            var source = AssetDatabase.LoadAssetAtPath<Mesh>(directory + "/source.asset");
            Assert.That(source, Is.Not.Null);
            var profile = kind == "profile" ? AssetDatabase.LoadAssetAtPath<MeshDeformerProfile>(directory + "/profile.asset") : null;
            string profileBefore = profile == null ? null : JsonUtility.ToJson(profile);
            string profileFileHash = profile == null ? null : Sha(directory + "/profile.asset");
            string folder = "Assets/__LaterRelease_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            GameObject stepped = null, direct = null;
            try
            {
                stepped = Instantiate(directory + "/" + kind + ".prefab");
                var deformer = stepped.GetComponent<LatticeDeformer>();
                Assert.That(stepped.activeSelf || deformer.enabled, Is.False);
                Assert.That((int)deformer.SerializedDeformationDataVersion, Is.EqualTo(Math.Max(0, expected.rawVersion)));
                AssertValues(expected.componentValues, deformer, expected, migrated: false);
                if (profile != null) AssertValues(expected.profileValues, profile, null, migrated: false);
                Assert.That(deformer.Profile, Is.SameAs(profile));
                var visited = new List<int>();
                while (deformer.SerializedDeformationDataVersion != DeformationDataVersion.CurrentDevelopment)
                {
                    int before = (int)deformer.SerializedDeformationDataVersion;
                    Assert.That(deformer.TryUpgradeDeformationDataOneRelease(), Is.True, tag + " " + deformer.MigrationStatus);
                    int after = (int)deformer.SerializedDeformationDataVersion;
                    Assert.That(after, Is.EqualTo(before == 0 ? (int)DeformationDataVersion.V1_2_1 : before + 1));
                    visited.Add(after);
                    Assert.That(visited.Count, Is.LessThan(16));
                }
                Assert.That(visited, Is.EqualTo(tag == "1.4.1" ? new[] { 11, 12, 13, 14, 15 } : Array.Empty<int>()));
                AssertValues(expected.componentValues, deformer, expected, migrated: true);
                CompareOutput(expected, deformer, source);
                string steppedState = CaptureState(deformer);

                direct = Instantiate(directory + "/" + kind + ".prefab");
                var directDeformer = direct.GetComponent<LatticeDeformer>();
                CompareOutput(expected, directDeformer, source);
                Assert.That(CaptureState(directDeformer), Is.EqualTo(steppedState), "Direct and release-step upgrades diverged.");
                AssertValues(expected.componentValues, directDeformer, expected, migrated: true);

                string savedPath = folder + "/migrated.prefab";
                deformer.RestoreOriginalMesh();
                deformer.InvalidateCache();
                var saved = PrefabUtility.SaveAsPrefabAsset(stepped, savedPath, out bool savedOk);
                Assert.That(savedOk && saved != null, Is.True);
                AssetDatabase.SaveAssets();
                DestroyFixture(stepped);
                stepped = null;
                AssetDatabase.ImportAsset(savedPath, ImportAssetOptions.ForceSynchronousImport);
                stepped = Instantiate(savedPath);
                deformer = stepped.GetComponent<LatticeDeformer>();
                Assert.That(stepped.activeSelf || deformer.enabled, Is.False);
                Assert.That(CaptureState(deformer), Is.EqualTo(steppedState));
                Assert.That(deformer.Profile, Is.SameAs(profile));
                AssertValues(expected.componentValues, deformer, expected, migrated: true);
                CompareOutput(expected, deformer, source);
                string current = CaptureState(deformer);
                Assert.That(deformer.TryUpgradeDeformationDataOneRelease(), Is.False);
                Assert.That(CaptureState(deformer), Is.EqualTo(current));
                if (profile != null)
                {
                    Assert.That(JsonUtility.ToJson(profile), Is.EqualTo(profileBefore), "Evaluation or upgrade wrote to the shared Profile.");
                    Assert.That(Sha(directory + "/profile.asset"), Is.EqualTo(profileFileHash));
                    AssertValues(expected.profileValues, profile, null, migrated: false);
                }
                Assert.That(JsonUtility.ToJson(DeformationOutputBaselineFixture.CaptureMesh(source)),
                    Is.EqualTo(JsonUtility.ToJson(expected.sourceBefore)), "A source channel changed.");
            }
            finally
            {
                DestroyFixture(stepped);
                DestroyFixture(direct);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private static void CompareOutput(Expected expected, LatticeDeformer component, Mesh source)
        {
            Assert.That(component.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(source));
            DeformationOutputCompatibilityTests.CompareMesh(expected.sourceBefore, DeformationOutputBaselineFixture.CaptureMesh(source), "source");
            Assert.That(JsonUtility.ToJson(expected.sourceAfter), Is.EqualTo(JsonUtility.ToJson(expected.sourceBefore)));
            Mesh output = component.Deform(false);
            Assert.That(output, Is.Not.Null);
            DeformationOutputCompatibilityTests.CompareMesh(expected.output, DeformationOutputBaselineFixture.CaptureMesh(output), expected.tag + "/" + expected.kind);
            Assert.That(component.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(source));
        }

        private static void AssertValues(SavedValue[] values, Object target, Expected document, bool migrated)
        {
            using var serialized = new SerializedObject(target);
            bool recovered = migrated && document != null && document.rawVersion < 0 && document.rawFlatLayerCount > 0;
            foreach (var saved in values)
            {
                string path = saved.path;
                string expected = saved.value;
                if (recovered)
                {
                    string recovery = "_groups.Array.data[" + document.rawEmbeddedGroupCount + "]";
                    if (path.StartsWith("_layers.", StringComparison.Ordinal)) path = recovery + "." + path;
                    if (path == "_activeLayerIndex") path = recovery + "._activeLayerIndex";
                    if (path == "_groups.Array.size") expected = (document.rawEmbeddedGroupCount + 1).ToString(CultureInfo.InvariantCulture);
                    if (path == "_layerModelVersion") expected = "3";
                }
                var property = serialized.FindProperty(path);
                Assert.That(property, Is.Not.Null, target.name + ": missing " + path);
                Assert.That(property.propertyType.ToString(), Is.EqualTo(saved.type), path);
                switch (property.propertyType)
                {
                    case SerializedPropertyType.Integer: case SerializedPropertyType.Enum: case SerializedPropertyType.ArraySize:
                        Assert.That(property.longValue, Is.EqualTo(long.Parse(expected, CultureInfo.InvariantCulture)), path); break;
                    case SerializedPropertyType.Boolean:
                        Assert.That(property.boolValue, Is.EqualTo(bool.Parse(expected)), path); break;
                    case SerializedPropertyType.Float:
                        Assert.That(property.floatValue, Is.EqualTo(float.Parse(expected, CultureInfo.InvariantCulture)), path); break;
                    case SerializedPropertyType.String:
                        Assert.That(property.stringValue, Is.EqualTo(expected), path); break;
                    case SerializedPropertyType.ObjectReference:
                        AssertReference(property.objectReferenceValue, target, expected, path); break;
                    case SerializedPropertyType.AnimationCurve:
                        AssertCurve(property.animationCurveValue, JsonUtility.FromJson<CurveValue>(expected), path); break;
                    default:
                        // Every vector/bounds component is independently present as a
                        // following scalar path; avoid relying on boxed-struct JSON.
                        Assert.That(values.Any(v => v.path.StartsWith(saved.path + ".", StringComparison.Ordinal)), Is.True, path + " missing scalar components"); break;
                }
            }
            if (recovered)
            {
                Assert.That(serialized.FindProperty("_layers").arraySize, Is.Zero);
                var group = serialized.FindProperty("_groups").GetArrayElementAtIndex(document.rawEmbeddedGroupCount);
                Assert.That(group.FindPropertyRelative("_name").stringValue, Is.EqualTo("Recovered Legacy Flat Layers"));
                Assert.That(group.FindPropertyRelative("_enabled").boolValue, Is.False);
            }
        }
        private static void AssertReference(Object reference, Object owner, string expected, string path)
        {
            if (expected == "null") { Assert.That(reference, Is.Null, path); return; }
            if (expected.StartsWith("local:self:", StringComparison.Ordinal))
            {
                Assert.That(reference, Is.InstanceOf<Component>(), path);
                Assert.That(((Component)reference).gameObject, Is.SameAs(((Component)owner).gameObject), path);
                Assert.That(reference.GetType().FullName, Is.EqualTo(expected.Substring("local:self:".Length)), path);
                return;
            }
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(reference, out string guid, out long id), Is.True, path);
            Assert.That(guid + ":" + id.ToString(CultureInfo.InvariantCulture), Is.EqualTo(expected), path);
        }
        private static void AssertCurve(AnimationCurve actual, CurveValue expected, string path)
        {
            Assert.That(expected.keys, Is.Not.Null.And.Not.Empty, "Missing captured curve keys: " + path);
            Assert.That((int)actual.preWrapMode, Is.EqualTo(expected.preWrapMode), path);
            Assert.That((int)actual.postWrapMode, Is.EqualTo(expected.postWrapMode), path);
            Assert.That(actual.length, Is.EqualTo(expected.keys.Length), path);
            for (int i = 0; i < actual.length; i++)
            {
                var a = actual[i]; var e = expected.keys[i];
                Assert.That(new[] { a.time, a.value, a.inTangent, a.outTangent, a.inWeight, a.outWeight },
                    Is.EqualTo(new[] { e.time, e.value, e.inTangent, e.outTangent, e.inWeight, e.outWeight }), path);
                Assert.That((int)a.weightedMode, Is.EqualTo(e.weightedMode), path);
            }
        }
        private static GameObject Instantiate(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            return instance;
        }
        private static void DestroyFixture(GameObject instance)
        {
            if (instance == null) return;
            // These fixtures intentionally never activate. Unity does not promise an
            // OnDestroy callback for never-active objects, so the test owner releases
            // the buffers it requested through explicit public evaluation.
            var component = instance.GetComponent<LatticeDeformer>();
            if (component != null) { component.RestoreOriginalMesh(); component.InvalidateCache(); }
            Object.DestroyImmediate(instance);
        }
        private static string CaptureState(LatticeDeformer component) => Regex.Replace(JsonUtility.ToJson(component),
            "\"instanceID\":\\s*-?\\d+", "\"instanceID\":0"); // References are checked separately, including their owner and source identity.
        private static string Sha(string path)
        { using var sha = SHA256.Create(); using var stream = File.OpenRead(path); return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2"))); }
        private static void AssertMetaGuid(string path, string tag, string relative)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes("net.32ba.lattice-deformation-tool/historical-fixture-meta-guid/v1\n" + tag + "\n" + relative));
            string expected = string.Concat(hash.Take(16).Select(b => b.ToString("x2")));
            var matches = Regex.Matches(File.ReadAllText(path), @"(?m)^guid: ([0-9a-f]{32})\r?$");
            Assert.That(matches.Count, Is.EqualTo(1), path);
            Assert.That(matches[0].Groups[1].Value, Is.EqualTo(expected), path);
        }
        private static void AssertPrefabIds(string path, string tag, string relative)
        {
            var anchors = Regex.Matches(File.ReadAllText(path), @"(?m)^--- !u!(\d+) &(\d+)\r?$");
            Assert.That(anchors.Count, Is.GreaterThan(0), path);
            using var sha = SHA256.Create();
            for (int ordinal = 0; ordinal < anchors.Count; ordinal++)
            {
                string identity = "net.32ba.lattice-deformation-tool/historical-fixture-prefab-file-id/v1\n" + tag + "\n" + relative + "\n" + anchors[ordinal].Groups[1].Value + "\n" + ordinal.ToString(CultureInfo.InvariantCulture);
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(identity));
                ulong id = 0;
                for (int i = 0; i < 8; i++) id = (id << 8) | hash[i];
                id = (id & 0x3FFFFFFFFFFFFFFFUL) | 0x4000000000000000UL;
                Assert.That(anchors[ordinal].Groups[2].Value, Is.EqualTo(id.ToString(CultureInfo.InvariantCulture)), path);
            }
        }
    }
}
#endif
