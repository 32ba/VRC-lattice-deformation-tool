#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class ArchitectureCompatibilityTests
    {
        [Test]
        public void Refactoring_RetainsBaselinePublicApiSerializedFieldsEnumsAndGuids()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(ArchitectureContractSnapshot.PackagePath +
                "/Tests/Editor/Fixtures/ArchitectureBaseline/contract.json");
            Assert.That(asset, Is.Not.Null, "Generate the baseline from the fixed original commit first.");
            var baseline = JsonUtility.FromJson<ArchitectureContractSnapshot.Document>(asset.text);
            Assert.That(baseline.baselineCommit, Is.EqualTo("c7f499c38e16f386fe6734e0f7937d50c502c529"));
            var current = ArchitectureContractSnapshot.Capture();
            Assert.That(baseline.publicApi.Except(current.publicApi), Is.Empty, "Removed or changed public API");
            Assert.That(baseline.serializedFields.Except(current.serializedFields), Is.Empty, "Changed saved field contract");
            Assert.That(baseline.serializedPaths.Except(current.serializedPaths), Is.Empty, "Changed serialized property paths");
            Assert.That(baseline.enumValues.Except(current.enumValues), Is.Empty, "Changed enum values");
            Assert.That(baseline.assetGuids.Except(current.assetGuids), Is.Empty, "Lost existing script/assembly GUIDs");
        }
    }
}
#endif
