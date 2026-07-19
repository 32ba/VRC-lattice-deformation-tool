#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class ClearanceQaReportTests
    {
        private const float Epsilon = 1e-6f;

        [Test]
        public void CurrentEvaluation_JsonAndMarkdownContainMatchingStatisticsAndMetadata()
        {
            using var fixture = Fixture.Create("対象 | 衣装", "参照 Body");
            var statistics = new ClearanceHeatmapStatistics(-0.003f, 0.003f, 2, 3);
            var evaluation = Evaluation(statistics);

            ClearanceQaReport report = ClearanceQaReportBuilder.FromCurrentEvaluation(
                fixture.Deformer,
                fixture.Reference,
                fixture.Target,
                evaluation,
                ClearanceQueryMode.ReferenceNormal,
                0.005f,
                0.01f,
                false,
                new DateTime(2026, 7, 19, 12, 34, 56, DateTimeKind.Utc));
            string json = ClearanceQaReportBuilder.ToJson(report);
            string markdown = ClearanceQaReportBuilder.ToMarkdown(report);

            Assert.That(report.schemaVersion, Is.EqualTo(1));
            Assert.That(report.packageVersion, Is.Not.Empty);
            Assert.That(report.unityVersion, Is.EqualTo(Application.unityVersion));
            Assert.That(report.evaluatedAtUtc, Is.EqualTo("2026-07-19T12:34:56.0000000Z"));
            Assert.That(report.targetRenderer, Does.Contain("対象 | 衣装"));
            Assert.That(report.referenceRenderer, Does.Contain("参照 Body"));
            Assert.That(report.conditions.Single().minimumClearance, Is.EqualTo(-0.003f));
            Assert.That(json, Does.Contain("\"minimumClearance\": -0.003"));
            Assert.That(markdown, Does.Contain("-3"));
            Assert.That(markdown, Does.Contain("対象 \\| 衣装"));
            Assert.That(markdown, Does.Contain("| 2 | 3 |"));
        }

        [Test]
        public void ScanResult_PreservesOrderWorstConditionProxyAndIndividualErrors()
        {
            using var fixture = Fixture.Create("Target", "Reference");
            var scan = new ClearanceScanResult
            {
                QueryMode = ClearanceQueryMode.ClosedMesh,
                WorstClearances = new[] { 0.002f, -0.004f, 0.003f },
                WorstConditionIndices = new[] { 0, 1, 0 },
                ScanSet = ScriptableObject.CreateInstance<ClearanceScanSet>()
            };
            var firstDefinition = new ClearanceScanCondition { Name = "通常" };
            var secondDefinition = new ClearanceScanCondition
            {
                Name = "腕上げ",
                UseAnimationClip = true,
                AnimationClip = new AnimationClip { name = "Arm Pose" },
                SampleTime = 0.25f,
                AnimationRootPath = "Avatar/Body",
                OverrideThresholds = true,
                WarningDistance = 0.004f,
                TargetDistance = 0.008f
            };
            secondDefinition.BlendShapeOverrides.Add(new ClearanceBlendShapeOverride
            {
                RendererRole = ClearanceScanRendererRole.Target,
                BlendShapeName = "Sleeve",
                Weight = 75f
            });
            secondDefinition.TransformOverrides.Add(new ClearanceTransformPoseOverride
            {
                RelativePath = "Arm",
                OverridePosition = true,
                LocalPosition = new Vector3(1f, 2f, 3f)
            });
            scan.ScanSet.Conditions.Add(firstDefinition);
            scan.ScanSet.Conditions.Add(secondDefinition);
            scan.ScanSet.Conditions.Add(new ClearanceScanCondition { Name = "Missing" });
            scan.Conditions.Add(new ClearanceScanConditionResult(
                0, "通常", ClearanceScanConditionStatus.Success,
                warningDistance: 0.005f,
                targetDistance: 0.01f,
                statistics: new ClearanceHeatmapStatistics(0.002f, 0f, 2, 3),
                vertexClearances: new[] { 0.002f, 0.004f, 0.003f },
                evaluatedRendererName: "Root/Target"));
            scan.Conditions.Add(new ClearanceScanConditionResult(
                1, "腕上げ", ClearanceScanConditionStatus.Success,
                warningDistance: 0.004f,
                targetDistance: 0.008f,
                statistics: new ClearanceHeatmapStatistics(-0.004f, 0.004f, 3, 3),
                vertexClearances: new[] { 0.003f, -0.004f, 0.004f },
                usedNdmfPreviewProxy: true,
                evaluatedRendererName: "Preview/Target"));
            scan.Conditions.Add(new ClearanceScanConditionResult(
                2, "Missing", ClearanceScanConditionStatus.MissingBlendShape,
                "BlendShape was not found: Missing"));

            ClearanceQaReport report = ClearanceQaReportBuilder.FromScanResult(
                fixture.Deformer,
                fixture.Reference,
                scan,
                DateTime.UnixEpoch);

            Assert.That(report.queryMode, Is.EqualTo("ClosedMesh"));
            Assert.That(report.worstConditionIndex, Is.EqualTo(1));
            Assert.That(report.worstConditionName, Is.EqualTo("腕上げ"));
            Assert.That(report.conditions.Select(condition => condition.name),
                Is.EqualTo(new[] { "通常", "腕上げ", "Missing" }));
            Assert.That(report.conditions[1].usedNdmfPreviewProxy, Is.True);
            Assert.That(report.conditions[1].useAnimationClip, Is.True);
            Assert.That(report.conditions[1].animationClip, Does.Contain("Arm Pose"));
            Assert.That(report.conditions[1].sampleTime, Is.EqualTo(0.25f));
            Assert.That(report.conditions[1].animationRootPath, Is.EqualTo("Avatar/Body"));
            Assert.That(report.conditions[1].blendShapeOverrides.Single().blendShapeName,
                Is.EqualTo("Sleeve"));
            Assert.That(report.conditions[1].transformOverrides.Single().relativePath,
                Is.EqualTo("Arm"));
            Assert.That(report.conditions[1].conditionFingerprint, Is.Not.Empty);
            Assert.That(report.conditions[2].status, Is.EqualTo("MissingBlendShape"));
            Assert.That(report.conditions[2].error, Does.Contain("Missing"));
            Object.DestroyImmediate(secondDefinition.AnimationClip);
            Object.DestroyImmediate(scan.ScanSet);
        }

        [Test]
        public void RendererIdentifiers_DisambiguateSameNamesBySiblingIndexAndType()
        {
            using var fixture = Fixture.Create("Same", "Same");

            ClearanceQaReport report = ClearanceQaReportBuilder.FromCurrentEvaluation(
                fixture.Deformer,
                fixture.Reference,
                fixture.Target,
                Evaluation(new ClearanceHeatmapStatistics(0f, 0f, 0, 0)),
                ClearanceQueryMode.ReferenceNormal,
                0.005f,
                0.01f,
                false,
                DateTime.UnixEpoch);

            Assert.That(report.targetRenderer, Does.Contain("Same[0]|MeshRenderer"));
            Assert.That(report.referenceRenderer, Does.Contain("Same[1]|MeshRenderer"));
            Assert.That(report.targetRenderer, Is.Not.EqualTo(report.referenceRenderer));
        }

        [Test]
        public void DefaultJson_DoesNotContainReconstructableMeshOrVertexResultArrays()
        {
            using var fixture = Fixture.Create("Target", "Reference");
            var scan = new ClearanceScanResult
            {
                QueryMode = ClearanceQueryMode.ReferenceNormal,
                WorstClearances = new[] { -0.123456f, 0.654321f },
                WorstConditionIndices = new[] { 0, 0 }
            };
            scan.Conditions.Add(new ClearanceScanConditionResult(
                0, "Secret", ClearanceScanConditionStatus.Success,
                statistics: new ClearanceHeatmapStatistics(-0.1f, 0.1f, 1, 2),
                vertexClearances: new[] { -0.123456f, 0.654321f }));

            string json = ClearanceQaReportBuilder.ToJson(
                ClearanceQaReportBuilder.FromScanResult(
                    fixture.Deformer, fixture.Reference, scan, DateTime.UnixEpoch));

            Assert.That(json, Does.Not.Contain("VertexClearances").IgnoreCase);
            Assert.That(json, Does.Not.Contain("WorstClearances").IgnoreCase);
            Assert.That(json, Does.Not.Contain("vertexPositions").IgnoreCase);
            Assert.That(json, Does.Not.Contain("triangleIndices").IgnoreCase);
            Assert.That(json, Does.Not.Contain("displacements").IgnoreCase);
            Assert.That(json, Does.Not.Contain("-0.123456"));
            Assert.That(json, Does.Not.Contain("0.654321"));
        }

        [Test]
        public void JsonRoundTrip_RequiresSupportedStableSchema()
        {
            var source = new ClearanceQaReport
            {
                packageVersion = "1.2.3",
                unityVersion = "2022.3.22f1",
                targetTopology = new ClearanceQaTopology
                {
                    vertexCount = 3,
                    triangleCount = 1,
                    subMeshCount = 1,
                    topologyHash = "abc"
                }
            };
            source.conditions.Add(new ClearanceQaCondition
            {
                index = 0,
                name = "日本語 Condition",
                status = "Success",
                minimumClearance = 0.001f
            });

            bool parsed = ClearanceQaReportBuilder.TryFromJson(
                ClearanceQaReportBuilder.ToJson(source),
                out ClearanceQaReport roundTrip,
                out string error);

            Assert.That(parsed, Is.True, error);
            Assert.That(roundTrip.schemaVersion, Is.EqualTo(ClearanceQaReport.CurrentSchemaVersion));
            Assert.That(roundTrip.conditions.Single().name, Is.EqualTo("日本語 Condition"));
            Assert.That(ClearanceQaReportBuilder.TryFromJson(
                "{\"schemaVersion\":999}", out _, out error), Is.False);
            Assert.That(error, Does.Contain("Schema"));
        }

        [Test]
        public void TopologyHash_IsStableAcrossPositionChangesAndDiffersForIndexChanges()
        {
            Mesh first = CreateQuad(new[] { 0, 1, 2, 0, 2, 3 });
            Mesh moved = CreateQuad(new[] { 0, 1, 2, 0, 2, 3 });
            moved.vertices = moved.vertices.Select(vertex => vertex + Vector3.forward).ToArray();
            Mesh rewired = CreateQuad(new[] { 0, 1, 3, 1, 2, 3 });
            try
            {
                ClearanceQaTopology firstTopology = ClearanceQaReportBuilder.ComputeTopology(first);
                ClearanceQaTopology movedTopology = ClearanceQaReportBuilder.ComputeTopology(moved);
                ClearanceQaTopology rewiredTopology = ClearanceQaReportBuilder.ComputeTopology(rewired);

                Assert.That(firstTopology.vertexCount, Is.EqualTo(4));
                Assert.That(firstTopology.triangleCount, Is.EqualTo(2));
                Assert.That(firstTopology.subMeshCount, Is.EqualTo(1));
                Assert.That(movedTopology.topologyHash, Is.EqualTo(firstTopology.topologyHash));
                Assert.That(rewiredTopology.topologyHash, Is.Not.EqualTo(firstTopology.topologyHash));
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(moved);
                Object.DestroyImmediate(rewired);
            }
        }

        [Test]
        public void TopologyHash_WorksAfterMeshBecomesNonReadable()
        {
            Mesh mesh = CreateQuad(new[] { 0, 1, 2, 0, 2, 3 });
            mesh.UploadMeshData(true);
            try
            {
                ClearanceQaTopology topology = ClearanceQaReportBuilder.ComputeTopology(mesh);

                Assert.That(topology.vertexCount, Is.EqualTo(4));
                Assert.That(topology.topologyHash, Is.Not.Empty);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Compare_ReportsDeltasForSameTopologyAndRejectsDifferentTopology()
        {
            ClearanceQaReport before = ReportForComparison("same", -0.004f, 5);
            ClearanceQaReport after = ReportForComparison("same", 0.001f, 2);
            ClearanceQaReport different = ReportForComparison("different", 0.002f, 0);

            ClearanceQaComparison comparison = ClearanceQaReportBuilder.Compare(before, after);
            ClearanceQaComparison incompatible = ClearanceQaReportBuilder.Compare(before, different);

            Assert.That(comparison.IsCompatible, Is.True);
            Assert.That(comparison.MinimumClearanceDelta, Is.EqualTo(0.005f).Within(Epsilon));
            Assert.That(comparison.ViolationVertexDelta, Is.EqualTo(-3));
            Assert.That(comparison.ComparedConditionCount, Is.EqualTo(1));
            Assert.That(incompatible.IsCompatible, Is.False);
            Assert.That(incompatible.Reason, Does.Contain("topology"));
        }

        [Test]
        public void Compare_RejectsDifferentReferenceQueryThresholdsAndConditionDefinition()
        {
            ClearanceQaReport before = ReportForComparison("same", 0.001f, 1);
            before.referenceRenderer = "Avatar/Body|SkinnedMeshRenderer";
            before.queryMode = "ClosedMesh";
            before.conditions[0].warningDistance = 0.005f;
            before.conditions[0].targetDistance = 0.01f;
            before.conditions[0].conditionFingerprint = "definition-a";

            ClearanceQaReport changedReference = ReportForComparison("same", 0.002f, 0);
            CopyComparisonContext(before, changedReference);
            changedReference.referenceRenderer = "Avatar/Other|SkinnedMeshRenderer";
            ClearanceQaReport changedQuery = ReportForComparison("same", 0.002f, 0);
            CopyComparisonContext(before, changedQuery);
            changedQuery.queryMode = "ReferenceNormal";
            ClearanceQaReport changedThreshold = ReportForComparison("same", 0.002f, 0);
            CopyComparisonContext(before, changedThreshold);
            changedThreshold.conditions[0].targetDistance = 0.02f;
            ClearanceQaReport changedCondition = ReportForComparison("same", 0.002f, 0);
            CopyComparisonContext(before, changedCondition);
            changedCondition.conditions[0].conditionFingerprint = "definition-b";

            Assert.That(ClearanceQaReportBuilder.Compare(before, changedReference).Reason,
                Does.Contain("Reference"));
            Assert.That(ClearanceQaReportBuilder.Compare(before, changedQuery).Reason,
                Does.Contain("Query"));
            Assert.That(ClearanceQaReportBuilder.Compare(before, changedThreshold).Reason,
                Does.Contain("thresholds"));
            Assert.That(ClearanceQaReportBuilder.Compare(before, changedCondition).Reason,
                Does.Contain("definition"));
        }

        [Test]
        public void Writer_AtomicallyWritesAndOverwritesJsonAndMarkdown()
        {
            string directory = NewTemporaryDirectory();
            string jsonPath = Path.Combine(directory, "検査 report.json");
            string markdownPath = Path.Combine(directory, "検査 report.md");
            try
            {
                Assert.That(ClearanceQaReportWriter.TryWritePair(
                    jsonPath, markdownPath, "json-1", "markdown-1", out string error), Is.True, error);
                Assert.That(File.ReadAllText(jsonPath), Is.EqualTo("json-1"));
                Assert.That(File.ReadAllText(markdownPath), Is.EqualTo("markdown-1"));

                Assert.That(ClearanceQaReportWriter.TryWritePair(
                    jsonPath, markdownPath, "json-2", "markdown-2", out error), Is.True, error);
                Assert.That(File.ReadAllText(jsonPath), Is.EqualTo("json-2"));
                Assert.That(File.ReadAllText(markdownPath), Is.EqualTo("markdown-2"));
                Assert.That(Directory.GetFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly), Is.Empty);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void Writer_InvalidSecondPathDoesNotOverwriteExistingJson()
        {
            string directory = NewTemporaryDirectory();
            string jsonPath = Path.Combine(directory, "existing.json");
            File.WriteAllText(jsonPath, "original");
            string invalidMarkdownPath = Path.Combine(directory, "missing", "report.md");
            try
            {
                bool written = ClearanceQaReportWriter.TryWritePair(
                    jsonPath, invalidMarkdownPath, "replacement", "markdown", out string error);

                Assert.That(written, Is.False);
                Assert.That(error, Is.Not.Empty);
                Assert.That(File.ReadAllText(jsonPath), Is.EqualTo("original"));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static ClearanceHeatmapEvaluation Evaluation(ClearanceHeatmapStatistics statistics)
        {
            return new ClearanceHeatmapEvaluation(
                Array.Empty<Vector3>(),
                Array.Empty<ClearanceQueryResult>(),
                Array.Empty<ClearanceClassification>(),
                statistics,
                ClearanceEvaluationStatus.Valid,
                ClearanceSignMode.ReferenceNormal,
                false);
        }

        private static ClearanceQaReport ReportForComparison(
            string topologyHash,
            float minimumClearance,
            int violations)
        {
            var report = new ClearanceQaReport
            {
                targetTopology = new ClearanceQaTopology { topologyHash = topologyHash }
            };
            report.conditions.Add(new ClearanceQaCondition
            {
                index = 0,
                name = "Pose",
                status = ClearanceScanConditionStatus.Success.ToString(),
                minimumClearance = minimumClearance,
                violationVertexCount = violations
            });
            return report;
        }

        private static void CopyComparisonContext(ClearanceQaReport source, ClearanceQaReport destination)
        {
            destination.referenceRenderer = source.referenceRenderer;
            destination.queryMode = source.queryMode;
            destination.conditions[0].warningDistance = source.conditions[0].warningDistance;
            destination.conditions[0].targetDistance = source.conditions[0].targetDistance;
            destination.conditions[0].conditionFingerprint = source.conditions[0].conditionFingerprint;
        }

        private static Mesh CreateQuad(int[] triangles)
        {
            var mesh = new Mesh { name = "QA Topology" };
            mesh.vertices = new[]
            {
                new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f),
                new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f)
            };
            mesh.triangles = triangles;
            return mesh;
        }

        private static string NewTemporaryDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "ClearanceQaReportTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly GameObject Root;
            internal readonly MeshRenderer Target;
            internal readonly MeshRenderer Reference;
            internal readonly LatticeDeformer Deformer;
            private readonly Mesh _targetMesh;
            private readonly Mesh _referenceMesh;

            private Fixture(
                GameObject root,
                MeshRenderer target,
                MeshRenderer reference,
                LatticeDeformer deformer,
                Mesh targetMesh,
                Mesh referenceMesh)
            {
                Root = root;
                Target = target;
                Reference = reference;
                Deformer = deformer;
                _targetMesh = targetMesh;
                _referenceMesh = referenceMesh;
            }

            internal static Fixture Create(string targetName, string referenceName)
            {
                var root = new GameObject("Root");
                Mesh targetMesh = CreateQuad(new[] { 0, 1, 2, 0, 2, 3 });
                Mesh referenceMesh = CreateQuad(new[] { 0, 1, 2, 0, 2, 3 });
                var targetObject = new GameObject(targetName);
                targetObject.transform.SetParent(root.transform);
                targetObject.AddComponent<MeshFilter>().sharedMesh = targetMesh;
                var target = targetObject.AddComponent<MeshRenderer>();
                var referenceObject = new GameObject(referenceName);
                referenceObject.transform.SetParent(root.transform);
                referenceObject.AddComponent<MeshFilter>().sharedMesh = referenceMesh;
                var reference = referenceObject.AddComponent<MeshRenderer>();
                var deformer = targetObject.AddComponent<LatticeDeformer>();
                deformer.Reset();
                return new Fixture(root, target, reference, deformer, targetMesh, referenceMesh);
            }

            public void Dispose()
            {
                Mesh runtime = Deformer != null ? Deformer.RuntimeMesh : null;
                if (runtime != null) Object.DestroyImmediate(runtime);
                if (Root != null) Object.DestroyImmediate(Root);
                if (_targetMesh != null) Object.DestroyImmediate(_targetMesh);
                if (_referenceMesh != null) Object.DestroyImmediate(_referenceMesh);
            }
        }
    }
}
#endif
