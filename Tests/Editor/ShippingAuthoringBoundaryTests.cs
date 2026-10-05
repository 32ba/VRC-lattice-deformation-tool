#if UNITY_EDITOR
using System;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class ShippingAuthoringBoundaryTests
    {
        [TestCase("ProfileAuthoringService")]
        [TestCase("ClearanceAuthoringSession")]
        [TestCase("ClearanceScanRunner")]
        [TestCase("FitCorrectionGenerator")]
        [TestCase("ClearanceQaReport")]
        [TestCase("ValidationInspectorSection")]
        public void RetiredAuthoringExecutors_AreAbsentFromShippingAssembly(string name)
        {
            Assert.That(typeof(LatticeDeformerEditor).Assembly.GetType(
                "Net._32Ba.LatticeDeformationTool.Editor." + name), Is.Null);
        }

        [TestCase(typeof(MeshDeformerProfile), typeof(LegacyProfileAssetInspector))]
        [TestCase(typeof(ClearanceScanSet), typeof(LegacyScanSetAssetInspector))]
        public void PublishedAssetTypes_HaveNoCreationMenuAndUseReadOnlyInspector(Type assetType, Type inspectorType)
        {
            Assert.That(Attribute.IsDefined(assetType, typeof(CreateAssetMenuAttribute)), Is.False);
            var asset = ScriptableObject.CreateInstance(assetType);
            UnityEditor.Editor editor = null;
            try
            {
                string before = EditorJsonUtility.ToJson(asset);
                editor = UnityEditor.Editor.CreateEditor(asset);
                Assert.That(editor.GetType(), Is.EqualTo(inspectorType));
                Assert.That(EditorJsonUtility.ToJson(asset), Is.EqualTo(before));
            }
            finally { if (editor != null) Object.DestroyImmediate(editor); Object.DestroyImmediate(asset); }
        }

        [Test]
        public void RetiredClearanceSettings_DoNotAffectValidationOrEraseSavedValues()
        {
            var root = new GameObject("Legacy clearance settings");
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 }
            };
            try
            {
                root.AddComponent<MeshRenderer>();
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                var owner = root.AddComponent<LatticeDeformer>();
                owner.ShowClearanceHeatmap = true;
                owner.ClearanceReferenceRenderer = null;
                string before = EditorJsonUtility.ToJson(owner);
                var diagnostics = MeshDeformerValidator.Validate(owner);
                Assert.That(diagnostics, Has.None.Matches<MeshDeformerDiagnostic>(
                    d => d.Code == MeshDeformerValidator.InvalidClearanceReference));
                Assert.That(MeshDeformerValidator.HasErrors(diagnostics), Is.False);
                Assert.That(EditorJsonUtility.ToJson(owner), Is.EqualTo(before));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void LegacyScanAsset_SaveReloadPreservesConditionPayloadAndScriptReference()
        {
            string path = "Assets/__LegacyScan_" + Guid.NewGuid().ToString("N") + ".asset";
            var asset = ScriptableObject.CreateInstance<ClearanceScanSet>();
            asset.Conditions.Add(new ClearanceScanCondition
            {
                Name = "Published condition", SampleTime = .75f, AnimationRootPath = "Body",
                OverrideThresholds = true, WarningDistance = .003f, TargetDistance = .02f
            });
            asset.Conditions[0].BlendShapeOverrides.Add(new ClearanceBlendShapeOverride());
            asset.Conditions[0].TransformOverrides.Add(new ClearanceTransformPoseOverride());
            try
            {
                AssetDatabase.CreateAsset(asset, path);
                AssetDatabase.SaveAssets();
                string before = EditorJsonUtility.ToJson(asset);
                Resources.UnloadAsset(asset);
                asset = AssetDatabase.LoadAssetAtPath<ClearanceScanSet>(path);
                Assert.That(asset, Is.Not.Null);
                Assert.That(EditorJsonUtility.ToJson(asset), Is.EqualTo(before));
                Assert.That(asset.Conditions[0].Name, Is.EqualTo("Published condition"));
                Assert.That(MonoScript.FromScriptableObject(asset), Is.Not.Null);
            }
            finally { AssetDatabase.DeleteAsset(path); }
        }
    }
}
#endif
