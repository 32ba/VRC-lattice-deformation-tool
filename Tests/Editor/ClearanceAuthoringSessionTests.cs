#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class ClearanceAuthoringSessionTests
    {
        [SetUp]
        public void SetUp() { Undo.ClearAll(); ClearanceQueryCache.Clear(); }

        [Test]
        public void IndependentSessions_RetainTheirOwnEvaluationsAndReleaseReferences()
        {
            using var a = new Fixture("Clearance A");
            using var b = new Fixture("Clearance B");
            b.Target.transform.localPosition = Vector3.forward;
            using var first = new ClearanceAuthoringSession();
            using var second = new ClearanceAuthoringSession();
            var near = Evaluate(first, a);
            var far = Evaluate(second, b);
            Assert.That(near.Statistics.MinimumClearance, Is.EqualTo(-0.002f).Within(1e-5f));
            Assert.That(far.Statistics.MinimumClearance, Is.EqualTo(0.998f).Within(1e-5f));
            Assert.That(Evaluate(first, a), Is.SameAs(near));
            first.Dispose();
            Assert.That(Evaluate(first, a), Is.Null);
            Assert.That(Evaluate(second, b), Is.SameAs(far));
        }

        [UnityTest]
        public IEnumerator EditorUpdates_CompleteScanAndRestoreSceneWithoutInspector()
        {
            using var fixture = new Fixture("Incremental session");
            using var session = new ClearanceAuthoringSession();
            int notifications = 0;
            session.Changed += () => notifications++;
            StartScan(session, fixture);
            double timeout = EditorApplication.timeSinceStartup + 5;
            while (session.ScanOperation != null && EditorApplication.timeSinceStartup < timeout)
                yield return null;
            Assert.That(session.ScanOperation, Is.Null);
            Assert.That(session.ScanResult.SuccessfulConditionCount, Is.EqualTo(2));
            Assert.That(session.ScanResult.WasCancelled, Is.False);
            Assert.That(fixture.Target.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(notifications, Is.GreaterThanOrEqualTo(2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CancelAndDispose_EndPendingScanAndDoNotAdvanceAgain(bool dispose)
        {
            using var fixture = new Fixture("Cancelled session");
            using var session = new ClearanceAuthoringSession();
            StartScan(session, fixture);
            var operation = session.ScanOperation;
            operation.Step();
            Assert.That(operation.NextConditionIndex, Is.EqualTo(1));
            if (dispose) session.Dispose();
            else session.CancelScan();
            Assert.That(session.ScanOperation, Is.Null);
            operation.Step();
            Assert.That(operation.NextConditionIndex, Is.EqualTo(1));
            Assert.That(fixture.Target.transform.localPosition, Is.EqualTo(Vector3.zero));
            if (!dispose) Assert.That(session.ScanResult.WasCancelled, Is.True);
            session.Dispose();
            StartScan(session, fixture);
            Assert.That(session.ScanOperation, Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AppliedCondition_RestoresOnExplicitRestoreOrOwnerDispose(bool dispose)
        {
            using var fixture = new Fixture("Preview session");
            using var session = new ClearanceAuthoringSession();
            CompleteScan(session, fixture);
            Assert.That(session.ApplyScanCondition(1, fixture.Deformer, fixture.Reference,
                ClearanceQueryMode.ReferenceNormal, 0.005f, 0.01f), Is.True);
            Assert.That(session.HasScanPreview, Is.True);
            Assert.That(fixture.Target.transform.localPosition.z, Is.EqualTo(0.03f).Within(1e-6f));
            if (dispose) session.Dispose();
            else session.RestoreScanPreview();
            Assert.That(session.HasScanPreview, Is.False);
            Assert.That(fixture.Target.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(fixture.Target.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(fixture.TargetMesh));
        }

        [TestCase("step")]
        [TestCase("cancel")]
        [TestCase("dispose")]
        public void UserUndoBoundaryDuringScan_IsRetainedWhenSessionEnds(string end)
        {
            using var fixture = new Fixture("Scan user edit");
            using var session = new ClearanceAuthoringSession();
            StartScan(session, fixture);
            session.ScanOperation.Step();
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(fixture.Target.transform, "User pose edit");
            fixture.Target.transform.localPosition = Vector3.right * 0.17f;
            if (end == "step") session.ScanOperation.Step();
            if (end == "dispose") session.Dispose();
            else
            {
                session.CancelScan();
                Assert.That(session.ScanResult.WasCancelled, Is.True);
            }
            session.Dispose();
            Assert.That(fixture.Target.transform.localPosition, Is.EqualTo(Vector3.right * 0.17f));
            Undo.PerformUndo();
            Assert.That(fixture.Target.transform.localPosition, Is.EqualTo(Vector3.zero));
        }

        [TestCase("groups")]
        [TestCase("layer")]
        [TestCase("mask")]
        public void MalformedPayload_QueryDoesNotRepairAndGenerationIsRejected(string malformed)
        {
            using var fixture = new Fixture("Malformed clearance source");
            fixture.Root.SetActive(false);
            var groups = (List<DeformerGroup>)SerializedDeformerReader.Read(fixture.Deformer).EmbeddedGroups;
            if (malformed == "groups") groups[0] = null;
            else if (malformed == "layer") ((List<LatticeLayer>)groups[0].SerializedLayers)[0] = null;
            else typeof(LatticeLayer).GetField("_vertexMask", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(groups[0].SerializedLayers[0], new[] {0.5f, 1f});
            string before = EditorJsonUtility.ToJson(fixture.Deformer);
            int dirty = EditorUtility.GetDirtyCount(fixture.Deformer);
            using var session = new ClearanceAuthoringSession();
            var raw = session.GetFitCorrectionRawEvaluation(fixture.Deformer, fixture.Reference,
                ClearanceQueryMode.ReferenceNormal, 0.02f);
            var constraints = new FitCorrectionConstraintOptions(true, false, false, false,
                0, 0f, false, false, 0, 0.001f);
            ClearanceAuthoringSession.ComputeFitCorrectionPlanKey(fixture.Deformer, raw, fixture.Reference,
                ClearanceQueryMode.ReferenceNormal, FitCorrectionScope.TargetClearance,
                0.005f, 0.01f, 0.1f, constraints);
            session.GetCachedFitCorrectionPlan(fixture.Deformer, raw, fixture.Reference,
                ClearanceQueryMode.ReferenceNormal, FitCorrectionScope.TargetClearance,
                0.005f, 0.01f, 0.1f, constraints);
            Assert.That(Generate(session, fixture.Deformer, fixture.Reference), Is.False);
            Assert.That(EditorJsonUtility.ToJson(fixture.Deformer), Is.EqualTo(before));
            Assert.That(EditorUtility.GetDirtyCount(fixture.Deformer), Is.EqualTo(dirty));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FitGeneration_OneUndoRedoAndPrefabRoundTripPreserveLayerData(bool usePrefab)
        {
            using var fixture = new Fixture("Fit session");
            using var session = new ClearanceAuthoringSession();
            string folder = "Assets/__ClearanceSession_" + Guid.NewGuid().ToString("N");
            GameObject instance = null;
            try
            {
                var deformer = fixture.Deformer;
                var reference = fixture.Reference;
                if (usePrefab)
                {
                    fixture.Root.SetActive(false);
                    AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
                    AssetDatabase.CreateAsset(fixture.TargetMesh, folder + "/target.asset");
                    AssetDatabase.CreateAsset(fixture.ReferenceMesh, folder + "/reference.asset");
                    AssetDatabase.CreateAsset(fixture.ScanSet, folder + "/scan.asset");
                    var prefab = PrefabUtility.SaveAsPrefabAsset(fixture.Root, folder + "/base.prefab");
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    instance.SetActive(true);
                    deformer = instance.GetComponentInChildren<LatticeDeformer>(true);
                    reference = instance.transform.Find("Reference").GetComponent<Renderer>();
                }
                var vertices = fixture.TargetMesh.vertices;
                int beforeCount = deformer.Layers.Count;
                string oldLayer = JsonUtility.ToJson(deformer.Layers[0]);
                Assert.That(Generate(session, deformer, reference), Is.True,
                    "Generation status: " + session.LastFitCorrectionReport?.Status);
                int added = session.LastFitCorrectionReport.LayerIndex;
                var delta = deformer.Layers[added].BrushDisplacements.ToArray();
                Assert.That(delta.All(v => Mathf.Abs(v.z - 0.012f) < 1e-5f), Is.True);
                Assert.That(deformer.Layers.Count, Is.EqualTo(beforeCount + 1));
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.That(deformer.Layers.Count, Is.EqualTo(beforeCount));
                Assert.That(JsonUtility.ToJson(deformer.Layers[0]), Is.EqualTo(oldLayer));
                Undo.PerformRedo();
                Assert.That(deformer.Layers.Count, Is.EqualTo(beforeCount + 1));
                Assert.That(deformer.Layers[added].BrushDisplacements, Is.EqualTo(delta));
                if (usePrefab)
                {
                    var variant = PrefabUtility.SaveAsPrefabAsset(instance, folder + "/variant.prefab");
                    Object.DestroyImmediate(instance);
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(variant);
                    deformer = instance.GetComponentInChildren<LatticeDeformer>(true);
                    Assert.That(deformer.Layers.Count, Is.EqualTo(beforeCount + 1));
                    Assert.That(deformer.Layers[added].BrushDisplacements, Is.EqualTo(delta));
                    var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(folder + "/base.prefab");
                    Assert.That(basePrefab.GetComponentInChildren<LatticeDeformer>(true).Layers.Count, Is.EqualTo(beforeCount));
                }
                Assert.That(fixture.TargetMesh.vertices, Is.EqualTo(vertices));
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            }
        }

        private static ClearanceHeatmapEvaluation Evaluate(ClearanceAuthoringSession session, Fixture fixture) =>
            session.GetClearanceEvaluation(fixture.Deformer, fixture.Reference,
                ClearanceQueryMode.ReferenceNormal, 0.005f, 0.01f, 0.02f);

        private static void StartScan(ClearanceAuthoringSession session, Fixture fixture) =>
            session.StartScan(fixture.Deformer, fixture.Reference, ClearanceQueryMode.ReferenceNormal, 0.005f, 0.01f);

        private static void CompleteScan(ClearanceAuthoringSession session, Fixture fixture)
        {
            StartScan(session, fixture);
            session.ScanOperation.RunToCompletion();
            session.CancelScan();
        }

        private static bool Generate(ClearanceAuthoringSession session, LatticeDeformer deformer, Renderer reference) =>
            session.CreateFitCorrectionLayer(deformer, reference, ClearanceQueryMode.ReferenceNormal,
                FitCorrectionScope.TargetClearance, 0.005f, 0.01f, 0.1f, default, "Create fit correction");

        private sealed class Fixture : IDisposable
        {
            internal readonly GameObject Root;
            internal readonly Renderer Target;
            internal readonly Renderer Reference;
            internal readonly Mesh TargetMesh;
            internal readonly Mesh ReferenceMesh;
            internal readonly ClearanceScanSet ScanSet;
            internal readonly LatticeDeformer Deformer;

            internal Fixture(string name)
            {
                Root = new GameObject(name);
                TargetMesh = MakeTriangle(0.4f, -0.002f);
                ReferenceMesh = MakeTriangle(2f, 0f);
                Target = MakeRenderer("Target", TargetMesh);
                Reference = MakeRenderer("Reference", ReferenceMesh);
                Deformer = Target.gameObject.AddComponent<LatticeDeformer>();
                Deformer.Reset();
                Deformer.Deform(false);
                ScanSet = ScriptableObject.CreateInstance<ClearanceScanSet>();
                ScanSet.name = name + " scan";
                foreach (float z in new[] {0.01f, 0.03f})
                {
                    var condition = new ClearanceScanCondition {Name = "Pose " + z};
                    condition.TransformOverrides.Add(new ClearanceTransformPoseOverride
                    {
                        RelativePath = "Target", OverridePosition = true,
                        LocalPosition = Vector3.forward * z, OverrideRotation = false, OverrideScale = false
                    });
                    ScanSet.Conditions.Add(condition);
                }
                Deformer.ClearanceScanSet = ScanSet;
                Deformer.ClearanceScanAvatarRoot = Root.transform;
            }

            private Renderer MakeRenderer(string name, Mesh mesh)
            {
                var child = new GameObject(name);
                child.transform.SetParent(Root.transform);
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                return child.AddComponent<MeshRenderer>();
            }

            private static Mesh MakeTriangle(float radius, float z)
            {
                var mesh = new Mesh
                {
                    vertices = new[] {new Vector3(-radius,-radius,z),new Vector3(radius,-radius,z),new Vector3(0,radius,z)},
                    triangles = new[] {0,1,2}
                };
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                return mesh;
            }

            public void Dispose()
            {
                if (Root != null) Object.DestroyImmediate(Root);
                foreach (var asset in new Object[] {TargetMesh, ReferenceMesh, ScanSet})
                    if (asset != null && !AssetDatabase.Contains(asset)) Object.DestroyImmediate(asset);
            }
        }
    }
}
#endif
