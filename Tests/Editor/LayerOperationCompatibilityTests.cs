#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class LayerOperationCompatibilityTests
    {
        [TestCaseSource(typeof(LayerOperationBaselineFixture), nameof(LayerOperationBaselineFixture.Cases))]
        public void InspectorOperation_MatchesOriginalSelectionPayloadAndOutput(string operation)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(ArchitectureContractSnapshot.PackagePath +
                "/Tests/Editor/Fixtures/ArchitectureBaseline/layer-operations.json");
            Assert.That(asset, Is.Not.Null);
            var baseline = JsonUtility.FromJson<LayerOperationBaselineFixture.Document>(asset.text);
            Assert.That(baseline.baselineCommit, Is.EqualTo("c7f499c38e16f386fe6734e0f7937d50c502c529"));
            var expected = baseline.cases.Single(c => c.operation == operation);
            var actual = LayerOperationBaselineFixture.Run(operation);
            Assert.That(actual.activeGroup, Is.EqualTo(expected.activeGroup));
            Assert.That(actual.activeLayer, Is.EqualTo(expected.activeLayer));
            Assert.That(actual.groupsJson, Is.EqualTo(expected.groupsJson));
            Assert.That(actual.hasOutput, Is.EqualTo(expected.hasOutput));
            Assert.That(actual.vertices, Is.EqualTo(expected.vertices));
            Assert.That(actual.sourceVertices, Is.EqualTo(expected.sourceVertices));
        }
    }
}
#endif
