#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using MeshSnapshot = Net._32Ba.LatticeDeformationTool.Tests.Editor.DeformationOutputBaselineFixture.MeshSnapshot;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    // Separate from the original corpus gates: those continue to exercise the
    // frozen legacy steps. This gate drives the complete published-release entry.
    public sealed class ReleaseJournalFixtureTests
    {
        private const string Package = ArchitectureContractSnapshot.PackagePath;
        [Serializable] private sealed class Inventory { public Release[] releases; }
        [Serializable] private sealed class Release { public string tag; public bool historicalCorpus; }
        [Serializable] private sealed class Manifest { public Fixture[] fixtures; }
        [Serializable] private sealed class Fixture { public string kind, prefab, expected, source, profile; }
        [Serializable] private sealed class HistoricalOutput
        { public float tolerance; public Vector3[] expectedVertices; public Shape[] outputBlendShapes; }
        [Serializable] private sealed class Shape { public string name; public Frame[] frames; }
        [Serializable] private sealed class Frame
        { public float weight; public Vector3[] deltaVertices, deltaNormals, deltaTangents; }

        private static IEnumerable<TestCaseData> Cases()
        {
            var inventory = JsonUtility.FromJson<Inventory>(File.ReadAllText(Package + "/Docs~/Architecture/2026-09-07-published-releases.json"));
            foreach (var release in inventory.releases)
            {
                string directory = Package + "/Tests/Editor/Fixtures/" +
                    (release.historicalCorpus ? "HistoricalReleases/" : "LaterReleases/") + release.tag;
                var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(directory + "/manifest.json"));
                foreach (var fixture in manifest.fixtures.Where(f => f.kind != "legacy-brush"))
                    yield return new TestCaseData(directory, fixture.prefab, fixture.expected, fixture.source,
                        fixture.profile ?? "", release.historicalCorpus, release.historicalCorpus
                            ? (Array.IndexOf(new[] { "1.2.1", "1.3.0", "1.3.1", "1.4.0" }, release.tag) >= 0 ? 11 : 1)
                            : release.tag == "1.4.1" ? 11 : 16)
                        .SetName("ReleaseJournal_" + release.tag.Replace('.', '_') + "_" + fixture.kind);
            }
        }

        [TestCaseSource(nameof(Cases))]
        public void ActualPublishedData_VisitsEveryBoundaryAndConvergesWithDirectAndSavedOutput(
            string directory, string prefab, string expectedFile, string sourceFile, string profileFile,
            bool historical, int classified)
        {
            string folderName = "__ReleaseJournal_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folderName);
            string folder = "Assets/" + folderName;
            var source = AssetDatabase.LoadAssetAtPath<Mesh>(directory + "/" + sourceFile);
            var sourceBefore = DeformationOutputBaselineFixture.CaptureMesh(source);
            var profile = profileFile == "" ? null : AssetDatabase.LoadAssetAtPath<MeshDeformerProfile>(directory + "/" + profileFile);
            string profileBefore = profile == null ? null : JsonUtility.ToJson(profile);
            byte[] prefabBytes = File.ReadAllBytes(directory + "/" + prefab);
            byte[] sourceBytes = File.ReadAllBytes(directory + "/" + sourceFile);
            byte[] profileBytes = profile == null ? null : File.ReadAllBytes(directory + "/" + profileFile);
            GameObject stepped = null, direct = null;
            try
            {
                stepped = Instantiate(directory + "/" + prefab);
                var target = FindTarget(stepped);
                Assert.That(target.SerializedMigrationReleaseIndex, Is.Zero, "An actual old tag cannot contain the new journal.");
                var visited = new List<int>();
                while (target.SerializedMigrationReleaseIndex != DeformationReleaseManifest.Current)
                {
                    int before = target.SerializedMigrationReleaseIndex;
                    string payload = PayloadWithoutJournal(target);
                    Assert.That(target.TryUpgradePublishedDeformationDataOneRelease(), Is.True,
                        directory + "/" + prefab + " stopped at " + before + ": " + target.MigrationStatus);
                    int after = target.SerializedMigrationReleaseIndex;
                    Assert.That(after, Is.EqualTo(before == 0 ? classified : before + 1));
                    visited.Add(after);
                    Assert.That(visited.Count, Is.LessThan(44));
                    if (before == 14 || before >= 16)
                        Assert.That(PayloadWithoutJournal(target), Is.EqualTo(payload), "No-op changed saved deformation data.");
                    AssertReferences(target, source, profile);
                }
                Assert.That(visited, Is.EqualTo(Enumerable.Range(classified, 44 - classified)));
                var steppedOutput = Evaluate(target, directory + "/" + expectedFile, historical);
                string steppedState = State(target);
                direct = Instantiate(directory + "/" + prefab);
                var directTarget = FindTarget(direct);
                AssertReferences(directTarget, source, profile);
                var directOutput = Evaluate(directTarget, directory + "/" + expectedFile, historical);
                Assert.That(directTarget.SerializedMigrationReleaseIndex, Is.EqualTo(43));
                Assert.That(State(directTarget), Is.EqualTo(steppedState));
                DeformationOutputCompatibilityTests.CompareMesh(steppedOutput, directOutput, "direct/stepwise all channels");

                target.RestoreOriginalMesh(); target.InvalidateCache();
                string savedPath = folder + "/migrated.prefab";
                Assert.That(PrefabUtility.SaveAsPrefabAsset(stepped, savedPath), Is.Not.Null);
                DestroyFixture(stepped); stepped = null;
                AssetDatabase.ImportAsset(savedPath, ImportAssetOptions.ForceSynchronousImport);
                stepped = Instantiate(savedPath); target = FindTarget(stepped);
                Assert.That(target.SerializedMigrationReleaseIndex, Is.EqualTo(43));
                Assert.That(State(target), Is.EqualTo(steppedState));
                AssertReferences(target, source, profile);
                var reloaded = Evaluate(target, directory + "/" + expectedFile, historical);
                DeformationOutputCompatibilityTests.CompareMesh(steppedOutput, reloaded, "save/reload all channels");
                string current = EditorJsonUtility.ToJson(target);
                Assert.That(target.TryUpgradePublishedDeformationDataOneRelease(), Is.False);
                Assert.That(target.MigrationStatus, Is.EqualTo(DeformationDataMigrationStatus.Ready));
                Assert.That(EditorJsonUtility.ToJson(target), Is.EqualTo(current));
                DeformationOutputCompatibilityTests.CompareMesh(sourceBefore,
                    DeformationOutputBaselineFixture.CaptureMesh(source), "unchanged source");
                Assert.That(File.ReadAllBytes(directory + "/" + prefab), Is.EqualTo(prefabBytes));
                Assert.That(File.ReadAllBytes(directory + "/" + sourceFile), Is.EqualTo(sourceBytes));
                if (profile != null)
                {
                    Assert.That(JsonUtility.ToJson(profile), Is.EqualTo(profileBefore));
                    Assert.That(File.ReadAllBytes(directory + "/" + profileFile), Is.EqualTo(profileBytes));
                }
            }
            finally { DestroyFixture(stepped); DestroyFixture(direct); AssetDatabase.DeleteAsset(folder); }
        }

        private static MeshSnapshot Evaluate(LatticeDeformer target, string expectedPath, bool historical)
        {
            var mesh = target.Deform(false);
            Assert.That(mesh, Is.Not.Null);
            var actual = DeformationOutputBaselineFixture.CaptureMesh(mesh);
            string json = File.ReadAllText(expectedPath);
            if (!historical)
            {
                var expected = JsonUtility.FromJson<LaterReleaseFixtureTests.Expected>(json);
                DeformationOutputCompatibilityTests.CompareMesh(expected.output, actual, "published golden all channels");
                return actual;
            }
            // The first corpus captured vertices and all BlendShape channels.
            // Compare only those channels to its golden; the complete mesh is
            // additionally compared between direct, stepwise, and reloaded paths.
            var old = JsonUtility.FromJson<HistoricalOutput>(json);
            CompareVectors(old.expectedVertices, actual.vertices, old.tolerance, "published vertices");
            var frames = (old.outputBlendShapes ?? Array.Empty<Shape>())
                .SelectMany(shape => (shape.frames ?? Array.Empty<Frame>()).Select(frame => (shape.name, frame))).ToArray();
            Assert.That(actual.frames.Length, Is.EqualTo(frames.Length));
            for (int i = 0; i < frames.Length; i++)
            {
                var expected = frames[i]; var observed = actual.frames[i];
                Assert.That(observed.shape, Is.EqualTo(expected.name));
                Assert.That(observed.weight, Is.EqualTo(expected.frame.weight).Within(old.tolerance));
                CompareVectors(expected.frame.deltaVertices, observed.vertices, old.tolerance, "frame vertices " + i);
                CompareVectors(expected.frame.deltaNormals, observed.normals, old.tolerance, "frame normals " + i);
                CompareVectors(expected.frame.deltaTangents, observed.tangents, old.tolerance, "frame tangents " + i);
            }
            return actual;
        }

        private static void CompareVectors(Vector3[] expected, Vector3[] actual, float tolerance, string label)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length), label);
            for (int i = 0; i < expected.Length; i++)
                Assert.That(Vector3.Distance(actual[i], expected[i]), Is.LessThanOrEqualTo(tolerance), label + "[" + i + "]");
        }
        private static GameObject Instantiate(string path)
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            Assert.That(root.activeSelf, Is.False);
            Assert.That(FindTarget(root).enabled, Is.False);
            return root;
        }
        private static LatticeDeformer FindTarget(GameObject root) => root.GetComponentsInChildren<LatticeDeformer>(true).Single();
        private static void AssertReferences(LatticeDeformer target, Mesh source, MeshDeformerProfile profile)
        {
            var data = SerializedDeformerReader.Read(target);
            // Some early tags only stored the renderer source; the serialized
            // source identity is populated by normal initialization after upgrade.
            Assert.That(data.SourceMesh, Is.Null.Or.SameAs(source));
            Assert.That(target.Profile, Is.SameAs(profile));
            var skinned = target.GetComponent<SkinnedMeshRenderer>();
            Assert.That(skinned != null ? skinned.sharedMesh : target.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(source));
        }
        private static string State(LatticeDeformer target) => Regex.Replace(JsonUtility.ToJson(target),
            "\"instanceID\":\\s*-?\\d+", "\"instanceID\":0");
        private static string PayloadWithoutJournal(LatticeDeformer target) => Regex.Replace(EditorJsonUtility.ToJson(target),
            "\"_migrationReleaseIndex\":\\s*-?\\d+", "\"_migrationReleaseIndex\":0");
        private static void DestroyFixture(GameObject root)
        {
            if (root == null) return;
            foreach (var target in root.GetComponentsInChildren<LatticeDeformer>(true))
            { target.RestoreOriginalMesh(); target.InvalidateCache(); }
            Object.DestroyImmediate(root);
        }
    }
}
#endif
