#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using MeshSnapshot = Net._32Ba.LatticeDeformationTool.Tests.Editor.DeformationOutputBaselineFixture.MeshSnapshot;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class DeformationOutputCompatibilityTests
    {
        [TestCaseSource(typeof(DeformationOutputBaselineFixture), nameof(DeformationOutputBaselineFixture.Cases))]
        public void Evaluation_MatchesBaselineAcrossAllMeshChannels(string scenario)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(ArchitectureContractSnapshot.PackagePath +
                "/Tests/Editor/Fixtures/ArchitectureBaseline/deformation-outputs.json");
            Assert.That(asset, Is.Not.Null);
            var baseline = JsonUtility.FromJson<DeformationOutputBaselineFixture.Document>(asset.text);
            Assert.That(baseline.baselineCommit, Is.EqualTo("c7f499c38e16f386fe6734e0f7937d50c502c529"));
            var expected = baseline.cases.Single(c => c.scenario == scenario);
            var actual = DeformationOutputBaselineFixture.Run(scenario);
            CompareMesh(expected.sourceBefore, actual.sourceBefore, "input source");
            Assert.That(JsonUtility.ToJson(actual.sourceAfter), Is.EqualTo(JsonUtility.ToJson(actual.sourceBefore)),
                "Evaluation changed an original source channel.");
            if (scenario.StartsWith("upstream", System.StringComparison.Ordinal))
            {
                CompareMesh(expected.inputBefore, actual.inputBefore, "upstream input");
                Assert.That(JsonUtility.ToJson(actual.inputAfter), Is.EqualTo(JsonUtility.ToJson(actual.inputBefore)),
                    "Evaluation changed an upstream channel.");
            }
            CompareMesh(expected.output, actual.output, scenario);
            Assert.That(actual.rendererRetainedSource, Is.True);
            Assert.That(actual.rendererWeights, Is.EqualTo(expected.rendererWeights));
        }

        private static void CompareMesh(MeshSnapshot expected, MeshSnapshot actual, string context)
        {
            Assert.That(actual.indexFormat, Is.EqualTo(expected.indexFormat), context + " index format");
            Assert.That(actual.attributes, Is.EqualTo(expected.attributes), context + " vertex layout");
            CompareVectors(expected.vertices, actual.vertices, context + " vertices");
            CompareVectors(expected.normals, actual.normals, context + " normals");
            Assert.That(actual.tangents.Length, Is.EqualTo(expected.tangents.Length), context + " tangent count");
            for (int i = 0; i < actual.tangents.Length; i++)
                Assert.That(Vector4.Distance(actual.tangents[i], expected.tangents[i]), Is.LessThanOrEqualTo(1e-5f), context + " tangent " + i);
            Assert.That(actual.colors, Is.EqualTo(expected.colors), context + " colors");
            Assert.That(Vector3.Distance(actual.bounds.center, expected.bounds.center), Is.LessThanOrEqualTo(1e-5f), context + " bounds center");
            Assert.That(Vector3.Distance(actual.bounds.size, expected.bounds.size), Is.LessThanOrEqualTo(1e-5f), context + " bounds size");
            Assert.That(actual.bindposes, Is.EqualTo(expected.bindposes), context + " bindposes");
            Assert.That(actual.bonesPerVertex, Is.EqualTo(expected.bonesPerVertex), context + " influence counts");
            Assert.That(actual.weights.Length, Is.EqualTo(expected.weights.Length), context + " weight count");
            for (int i = 0; i < actual.weights.Length; i++)
            {
                Assert.That(actual.weights[i].bone, Is.EqualTo(expected.weights[i].bone), context + " bone index " + i);
                Assert.That(actual.weights[i].value, Is.EqualTo(expected.weights[i].value).Within(1e-6f), context + " bone weight " + i);
            }
            Assert.That(actual.uv.Length, Is.EqualTo(8));
            for (int i = 0; i < 8; i++) Assert.That(actual.uv[i].values, Is.EqualTo(expected.uv[i].values), context + " UV " + i);
            Assert.That(actual.submeshes.Length, Is.EqualTo(expected.submeshes.Length), context + " submesh count");
            for (int i = 0; i < actual.submeshes.Length; i++)
            {
                Assert.That(actual.submeshes[i].topology, Is.EqualTo(expected.submeshes[i].topology), context + " topology " + i);
                Assert.That(actual.submeshes[i].indices, Is.EqualTo(expected.submeshes[i].indices), context + " indices " + i);
            }
            Assert.That(actual.frames.Length, Is.EqualTo(expected.frames.Length), context + " frame count");
            for (int i = 0; i < actual.frames.Length; i++)
            {
                string frame = context + " frame " + i;
                Assert.That(actual.frames[i].shape, Is.EqualTo(expected.frames[i].shape), frame + " name/order");
                Assert.That(actual.frames[i].weight, Is.EqualTo(expected.frames[i].weight), frame + " weight");
                CompareVectors(expected.frames[i].vertices, actual.frames[i].vertices, frame + " vertex delta");
                CompareVectors(expected.frames[i].normals, actual.frames[i].normals, frame + " normal delta");
                CompareVectors(expected.frames[i].tangents, actual.frames[i].tangents, frame + " tangent delta");
            }
        }

        private static void CompareVectors(Vector3[] expected, Vector3[] actual, string context)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length), context + " count");
            for (int i = 0; i < actual.Length; i++)
                Assert.That(Vector3.Distance(actual[i], expected[i]), Is.LessThanOrEqualTo(1e-5f), context + " " + i);
        }
    }
}
#endif
