#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.Rendering;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    [Serializable]
    internal sealed class ClearanceQaTopology
    {
        public int vertexCount;
        public int triangleCount;
        public int subMeshCount;
        public string topologyHash = "";
    }

    [Serializable]
    internal sealed class ClearanceQaBlendShapeOverride
    {
        public string rendererRole = "";
        public string blendShapeName = "";
        public float weight;
    }

    [Serializable]
    internal sealed class ClearanceQaTransformOverride
    {
        public string relativePath = "";
        public bool overridePosition;
        public Vector3 localPosition;
        public bool overrideRotation;
        public Vector3 localEulerAngles;
        public bool overrideScale;
        public Vector3 localScale = Vector3.one;
    }

    [Serializable]
    internal sealed class ClearanceQaCondition
    {
        public int index;
        public string name = "";
        public string status = "";
        public string error = "";
        public float warningDistance;
        public float targetDistance;
        public float minimumClearance;
        public float maximumPenetrationDepth;
        public int violationVertexCount;
        public int evaluatedVertexCount;
        public bool usedNdmfPreviewProxy;
        public string evaluatedRenderer = "";
        public bool useAnimationClip;
        public string animationClip = "";
        public float sampleTime;
        public string animationRootPath = "";
        public bool overrideThresholds;
        public List<ClearanceQaBlendShapeOverride> blendShapeOverrides =
            new List<ClearanceQaBlendShapeOverride>();
        public List<ClearanceQaTransformOverride> transformOverrides =
            new List<ClearanceQaTransformOverride>();
        public string conditionFingerprint = "";
    }

    [Serializable]
    internal sealed class ClearanceQaReport
    {
        internal const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public string packageVersion = "";
        public string unityVersion = "";
        public string evaluatedAtUtc = "";
        public string targetRenderer = "";
        public string referenceRenderer = "";
        public ClearanceQaTopology targetTopology = new ClearanceQaTopology();
        public string queryMode = "";
        public bool scanCancelled;
        public int worstConditionIndex = -1;
        public string worstConditionName = "";
        public List<ClearanceQaCondition> conditions = new List<ClearanceQaCondition>();
    }

    internal sealed class ClearanceQaComparison
    {
        internal readonly bool IsCompatible;
        internal readonly string Reason;
        internal readonly float MinimumClearanceDelta;
        internal readonly int ViolationVertexDelta;
        internal readonly int ComparedConditionCount;

        internal ClearanceQaComparison(
            bool isCompatible,
            string reason,
            float minimumClearanceDelta = 0f,
            int violationVertexDelta = 0,
            int comparedConditionCount = 0)
        {
            IsCompatible = isCompatible;
            Reason = reason ?? "";
            MinimumClearanceDelta = minimumClearanceDelta;
            ViolationVertexDelta = violationVertexDelta;
            ComparedConditionCount = comparedConditionCount;
        }
    }

    internal static class ClearanceQaReportBuilder
    {
        internal static ClearanceQaReport FromCurrentEvaluation(
            LatticeDeformer deformer,
            Renderer referenceRenderer,
            Renderer evaluatedRenderer,
            ClearanceHeatmapEvaluation evaluation,
            ClearanceQueryMode queryMode,
            float warningDistance,
            float targetDistance,
            bool usedNdmfPreviewProxy,
            DateTime? evaluatedAtUtc = null)
        {
            Renderer sourceRenderer = deformer != null ? deformer.TargetRenderer : null;
            var report = CreateHeader(
                sourceRenderer,
                referenceRenderer,
                queryMode,
                evaluatedAtUtc ?? DateTime.UtcNow);
            if (evaluation != null && evaluation.Status == ClearanceEvaluationStatus.Valid)
            {
                report.conditions.Add(new ClearanceQaCondition
                {
                    index = 0,
                    name = "Current",
                    status = ClearanceScanConditionStatus.Success.ToString(),
                    warningDistance = Mathf.Max(0f, warningDistance),
                    targetDistance = Mathf.Max(warningDistance, targetDistance),
                    minimumClearance = evaluation.Statistics.MinimumClearance,
                    maximumPenetrationDepth = evaluation.Statistics.MaximumPenetrationDepth,
                    violationVertexCount = evaluation.Statistics.ViolationVertexCount,
                    evaluatedVertexCount = evaluation.Statistics.EvaluatedVertexCount,
                    usedNdmfPreviewProxy = usedNdmfPreviewProxy,
                    evaluatedRenderer = GetRendererIdentifier(evaluatedRenderer)
                });
                report.worstConditionIndex = 0;
                report.worstConditionName = "Current";
            }
            else
            {
                report.conditions.Add(new ClearanceQaCondition
                {
                    index = 0,
                    name = "Current",
                    status = ClearanceScanConditionStatus.EvaluationFailed.ToString(),
                    error = "Clearance evaluation is not valid.",
                    warningDistance = Mathf.Max(0f, warningDistance),
                    targetDistance = Mathf.Max(warningDistance, targetDistance),
                    evaluatedRenderer = GetRendererIdentifier(evaluatedRenderer)
                });
            }
            return report;
        }

        internal static ClearanceQaReport FromScanResult(
            LatticeDeformer deformer,
            Renderer referenceRenderer,
            ClearanceScanResult scanResult,
            DateTime? evaluatedAtUtc = null)
        {
            Renderer sourceRenderer = deformer != null ? deformer.TargetRenderer : null;
            var report = CreateHeader(
                sourceRenderer,
                referenceRenderer,
                scanResult != null ? scanResult.QueryMode : ClearanceQueryMode.ReferenceNormal,
                evaluatedAtUtc ?? DateTime.UtcNow);
            if (scanResult == null) return report;
            report.scanCancelled = scanResult.WasCancelled;
            report.worstConditionIndex = scanResult.WorstConditionIndex;
            for (int index = 0; index < scanResult.Conditions.Count; index++)
            {
                ClearanceScanConditionResult source = scanResult.Conditions[index];
                report.conditions.Add(new ClearanceQaCondition
                {
                    index = source.ConditionIndex,
                    name = source.ConditionName,
                    status = source.Status.ToString(),
                    error = source.ErrorMessage,
                    warningDistance = source.WarningDistance,
                    targetDistance = source.TargetDistance,
                    minimumClearance = source.IsSuccess ? source.Statistics.MinimumClearance : 0f,
                    maximumPenetrationDepth = source.IsSuccess
                        ? source.Statistics.MaximumPenetrationDepth
                        : 0f,
                    violationVertexCount = source.IsSuccess
                        ? source.Statistics.ViolationVertexCount
                        : 0,
                    evaluatedVertexCount = source.IsSuccess
                        ? source.Statistics.EvaluatedVertexCount
                        : 0,
                    usedNdmfPreviewProxy = source.UsedNdmfPreviewProxy,
                    evaluatedRenderer = source.EvaluatedRendererName
                });
                ClearanceScanCondition definition = scanResult.ScanSet != null &&
                                                    source.ConditionIndex >= 0 &&
                                                    source.ConditionIndex < scanResult.ScanSet.Conditions.Count
                    ? scanResult.ScanSet.Conditions[source.ConditionIndex]
                    : null;
                PopulateConditionDefinition(report.conditions[report.conditions.Count - 1], definition);
                if (source.ConditionIndex == report.worstConditionIndex)
                    report.worstConditionName = source.ConditionName;
            }
            return report;
        }

        internal static ClearanceQaComparison Compare(
            ClearanceQaReport before,
            ClearanceQaReport after)
        {
            if (before == null || after == null)
                return new ClearanceQaComparison(false, "A report is null.");
            if (before.schemaVersion != ClearanceQaReport.CurrentSchemaVersion ||
                after.schemaVersion != ClearanceQaReport.CurrentSchemaVersion)
                return new ClearanceQaComparison(false, "Schema version is unsupported.");
            if (before.targetTopology == null || after.targetTopology == null ||
                string.IsNullOrEmpty(before.targetTopology.topologyHash) ||
                !string.Equals(
                    before.targetTopology.topologyHash,
                    after.targetTopology.topologyHash,
                    StringComparison.Ordinal))
                return new ClearanceQaComparison(false, "Target topology does not match.");
            if (!string.Equals(before.referenceRenderer, after.referenceRenderer, StringComparison.Ordinal))
                return new ClearanceQaComparison(false, "Reference renderer does not match.");
            if (!string.Equals(before.queryMode, after.queryMode, StringComparison.Ordinal))
                return new ClearanceQaComparison(false, "Query mode does not match.");
            if (before.conditions == null || after.conditions == null ||
                before.conditions.Count != after.conditions.Count)
                return new ClearanceQaComparison(false, "Condition set does not match.");

            var beforeByKey = new Dictionary<string, ClearanceQaCondition>();
            for (int index = 0; index < before.conditions.Count; index++)
            {
                ClearanceQaCondition condition = before.conditions[index];
                beforeByKey[ConditionKey(condition)] = condition;
            }
            float beforeMinimum = float.PositiveInfinity;
            float afterMinimum = float.PositiveInfinity;
            int beforeViolations = 0;
            int afterViolations = 0;
            int compared = 0;
            for (int index = 0; index < after.conditions.Count; index++)
            {
                ClearanceQaCondition current = after.conditions[index];
                if (!beforeByKey.TryGetValue(ConditionKey(current), out ClearanceQaCondition previous))
                    return new ClearanceQaComparison(false, "Condition set does not match.");
                if (!ConditionDefinitionsMatch(previous, current))
                    return new ClearanceQaComparison(false, "Condition definition or thresholds do not match.");
                if (!IsSuccess(previous) || !IsSuccess(current)) continue;
                beforeMinimum = Mathf.Min(beforeMinimum, previous.minimumClearance);
                afterMinimum = Mathf.Min(afterMinimum, current.minimumClearance);
                beforeViolations += previous.violationVertexCount;
                afterViolations += current.violationVertexCount;
                compared++;
            }
            if (compared == 0)
                return new ClearanceQaComparison(false, "No matching successful conditions.");
            return new ClearanceQaComparison(
                true,
                "",
                afterMinimum - beforeMinimum,
                afterViolations - beforeViolations,
                compared);
        }

        internal static string ToJson(ClearanceQaReport report, bool prettyPrint = true)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            return JsonUtility.ToJson(report, prettyPrint);
        }

        internal static bool TryFromJson(
            string json,
            out ClearanceQaReport report,
            out string error)
        {
            report = null;
            error = "";
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "JSON is empty.";
                return false;
            }
            try
            {
                report = JsonUtility.FromJson<ClearanceQaReport>(json);
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
            if (report == null || report.schemaVersion != ClearanceQaReport.CurrentSchemaVersion)
            {
                report = null;
                error = "Schema version is unsupported.";
                return false;
            }
            report.conditions ??= new List<ClearanceQaCondition>();
            return true;
        }

        internal static string ToMarkdown(ClearanceQaReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var builder = new StringBuilder();
            builder.AppendLine("# Clearance QA Report");
            builder.AppendLine();
            AppendField(builder, "Schema", report.schemaVersion.ToString(CultureInfo.InvariantCulture));
            AppendField(builder, "Package", report.packageVersion);
            AppendField(builder, "Unity", report.unityVersion);
            AppendField(builder, "Evaluated (UTC)", report.evaluatedAtUtc);
            AppendField(builder, "Target", report.targetRenderer);
            AppendField(builder, "Reference", report.referenceRenderer);
            AppendField(builder, "Query mode", report.queryMode);
            AppendField(builder, "Topology hash", report.targetTopology?.topologyHash ?? "");
            AppendField(builder, "Vertex count", (report.targetTopology?.vertexCount ?? 0).ToString(CultureInfo.InvariantCulture));
            AppendField(builder, "Triangle count", (report.targetTopology?.triangleCount ?? 0).ToString(CultureInfo.InvariantCulture));
            AppendField(builder, "Submesh count", (report.targetTopology?.subMeshCount ?? 0).ToString(CultureInfo.InvariantCulture));
            AppendField(builder, "Worst condition", report.worstConditionName);
            builder.AppendLine();
            builder.AppendLine("## Conditions");
            builder.AppendLine();
            builder.AppendLine("| # | Name | Status | Warning (mm) | Target (mm) | Minimum (mm) | Penetration (mm) | Violations | Evaluated | Target | Error |");
            builder.AppendLine("|---:|---|---|---:|---:|---:|---:|---:|---:|---|---|");
            for (int index = 0; index < report.conditions.Count; index++)
            {
                ClearanceQaCondition condition = report.conditions[index];
                builder.Append("| ").Append(condition.index).Append(" | ")
                    .Append(EscapeMarkdown(condition.name)).Append(" | ")
                    .Append(EscapeMarkdown(condition.status)).Append(" | ")
                    .Append(Millimeters(condition.warningDistance)).Append(" | ")
                    .Append(Millimeters(condition.targetDistance)).Append(" | ")
                    .Append(IsSuccess(condition) ? Millimeters(condition.minimumClearance) : "-").Append(" | ")
                    .Append(IsSuccess(condition) ? Millimeters(condition.maximumPenetrationDepth) : "-").Append(" | ")
                    .Append(condition.violationVertexCount).Append(" | ")
                    .Append(condition.evaluatedVertexCount).Append(" | ")
                    .Append(EscapeMarkdown(condition.usedNdmfPreviewProxy
                        ? "NDMF Proxy: " + condition.evaluatedRenderer
                        : condition.evaluatedRenderer))
                    .Append(" | ").Append(EscapeMarkdown(condition.error)).AppendLine(" |");
            }
            builder.AppendLine();
            builder.AppendLine("## Condition definitions");
            for (int index = 0; index < report.conditions.Count; index++)
            {
                ClearanceQaCondition condition = report.conditions[index];
                builder.AppendLine();
                builder.Append("### ").Append(condition.index).Append(". ")
                    .AppendLine(EscapeMarkdown(condition.name));
                AppendField(builder, "Animation clip", condition.useAnimationClip
                    ? condition.animationClip
                    : "Disabled");
                AppendField(builder, "Sample time (s)",
                    condition.sampleTime.ToString("R", CultureInfo.InvariantCulture));
                AppendField(builder, "Animation root", condition.animationRootPath);
                AppendField(builder, "Threshold override", condition.overrideThresholds.ToString());
                AppendField(builder, "BlendShape overrides",
                    JsonUtility.ToJson(new BlendShapeOverrideList
                    {
                        items = condition.blendShapeOverrides ?? new List<ClearanceQaBlendShapeOverride>()
                    }, false));
                AppendField(builder, "Transform overrides",
                    JsonUtility.ToJson(new TransformOverrideList
                    {
                        items = condition.transformOverrides ?? new List<ClearanceQaTransformOverride>()
                    }, false));
            }
            return builder.ToString();
        }

        [Serializable]
        private sealed class BlendShapeOverrideList
        {
            public List<ClearanceQaBlendShapeOverride> items = new List<ClearanceQaBlendShapeOverride>();
        }

        [Serializable]
        private sealed class TransformOverrideList
        {
            public List<ClearanceQaTransformOverride> items = new List<ClearanceQaTransformOverride>();
        }

        internal static ClearanceQaTopology ComputeTopology(Mesh mesh)
        {
            var topology = new ClearanceQaTopology();
            if (mesh == null) return topology;
            topology.vertexCount = mesh.vertexCount;
            topology.subMeshCount = mesh.subMeshCount;
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(mesh.vertexCount);
                writer.Write(mesh.subMeshCount);
                writer.Write((int)mesh.indexFormat);
                using Mesh.MeshDataArray meshDataArray = Mesh.AcquireReadOnlyMeshData(mesh);
                Mesh.MeshData meshData = meshDataArray[0];
                for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                {
                    SubMeshDescriptor descriptor = meshData.GetSubMesh(subMesh);
                    MeshTopology meshTopology = mesh.GetTopology(subMesh);
                    writer.Write((int)meshTopology);
                    writer.Write(descriptor.indexStart);
                    writer.Write(descriptor.indexCount);
                    writer.Write(descriptor.baseVertex);
                    if (meshTopology == MeshTopology.Triangles)
                        topology.triangleCount += descriptor.indexCount / 3;
                    if (mesh.indexFormat == IndexFormat.UInt16)
                    {
                        var indices = meshData.GetIndexData<ushort>();
                        int end = descriptor.indexStart + descriptor.indexCount;
                        for (int index = descriptor.indexStart; index < end; index++) writer.Write(indices[index]);
                    }
                    else
                    {
                        var indices = meshData.GetIndexData<uint>();
                        int end = descriptor.indexStart + descriptor.indexCount;
                        for (int index = descriptor.indexStart; index < end; index++) writer.Write(indices[index]);
                    }
                }
            }
            stream.Position = 0;
            using SHA256 sha256 = SHA256.Create();
            topology.topologyHash = Convert.ToBase64String(sha256.ComputeHash(stream));
            return topology;
        }

        private static ClearanceQaReport CreateHeader(
            Renderer targetRenderer,
            Renderer referenceRenderer,
            ClearanceQueryMode queryMode,
            DateTime evaluatedAtUtc)
        {
            Mesh targetMesh = targetRenderer switch
            {
                SkinnedMeshRenderer skinned => skinned.sharedMesh,
                MeshRenderer meshRenderer => meshRenderer.GetComponent<MeshFilter>()?.sharedMesh,
                _ => null
            };
            PackageInfo package = PackageInfo.FindForAssembly(typeof(LatticeDeformer).Assembly);
            return new ClearanceQaReport
            {
                packageVersion = package?.version ?? "unknown",
                unityVersion = Application.unityVersion,
                evaluatedAtUtc = evaluatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                targetRenderer = GetRendererIdentifier(targetRenderer),
                referenceRenderer = GetRendererIdentifier(referenceRenderer),
                targetTopology = ComputeTopology(targetMesh),
                queryMode = queryMode.ToString()
            };
        }

        private static string GetRendererIdentifier(Renderer renderer)
        {
            if (renderer == null) return "";
            var segments = new Stack<string>();
            Transform current = renderer.transform;
            while (current != null)
            {
                segments.Push(current.name + "[" + current.GetSiblingIndex() + "]");
                current = current.parent;
            }
            return string.Join("/", segments) + "|" + renderer.GetType().Name;
        }

        private static string ConditionKey(ClearanceQaCondition condition) =>
            condition.index.ToString(CultureInfo.InvariantCulture) + "\n" + (condition.name ?? "");

        private static void PopulateConditionDefinition(
            ClearanceQaCondition destination,
            ClearanceScanCondition source)
        {
            if (destination == null || source == null) return;
            destination.useAnimationClip = source.UseAnimationClip;
            destination.animationClip = GetObjectIdentifier(source.AnimationClip);
            destination.sampleTime = source.SampleTime;
            destination.animationRootPath = source.AnimationRootPath;
            destination.overrideThresholds = source.OverrideThresholds;
            foreach (ClearanceBlendShapeOverride item in source.BlendShapeOverrides)
            {
                if (item == null) continue;
                destination.blendShapeOverrides.Add(new ClearanceQaBlendShapeOverride
                {
                    rendererRole = item.RendererRole.ToString(),
                    blendShapeName = item.BlendShapeName,
                    weight = item.Weight
                });
            }
            foreach (ClearanceTransformPoseOverride item in source.TransformOverrides)
            {
                if (item == null) continue;
                destination.transformOverrides.Add(new ClearanceQaTransformOverride
                {
                    relativePath = item.RelativePath,
                    overridePosition = item.OverridePosition,
                    localPosition = item.LocalPosition,
                    overrideRotation = item.OverrideRotation,
                    localEulerAngles = item.LocalEulerAngles,
                    overrideScale = item.OverrideScale,
                    localScale = item.LocalScale
                });
            }
            destination.conditionFingerprint = ComputeConditionFingerprint(destination);
        }

        private static string GetObjectIdentifier(UnityEngine.Object value)
        {
            if (value == null) return "";
            return UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    value, out string guid, out long localId)
                ? guid + ":" + localId.ToString(CultureInfo.InvariantCulture)
                : value.name + "|" + value.GetType().Name;
        }

        private static string ComputeConditionFingerprint(ClearanceQaCondition condition)
        {
            var definition = new ClearanceQaCondition
            {
                useAnimationClip = condition.useAnimationClip,
                animationClip = condition.animationClip,
                sampleTime = condition.sampleTime,
                animationRootPath = condition.animationRootPath,
                overrideThresholds = condition.overrideThresholds,
                blendShapeOverrides = condition.blendShapeOverrides,
                transformOverrides = condition.transformOverrides
            };
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(definition, false));
            using SHA256 sha256 = SHA256.Create();
            return Convert.ToBase64String(sha256.ComputeHash(bytes));
        }

        private static bool ConditionDefinitionsMatch(
            ClearanceQaCondition before,
            ClearanceQaCondition after)
        {
            if (before == null || after == null) return false;
            if (!Mathf.Approximately(before.warningDistance, after.warningDistance) ||
                !Mathf.Approximately(before.targetDistance, after.targetDistance)) return false;
            return string.Equals(
                before.conditionFingerprint ?? "",
                after.conditionFingerprint ?? "",
                StringComparison.Ordinal);
        }

        private static bool IsSuccess(ClearanceQaCondition condition) =>
            condition != null && string.Equals(
                condition.status,
                ClearanceScanConditionStatus.Success.ToString(),
                StringComparison.Ordinal);

        private static void AppendField(StringBuilder builder, string name, string value)
        {
            builder.Append("- **").Append(name).Append(":** ")
                .AppendLine(value?.Replace("\r", " ").Replace("\n", " ") ?? "");
        }

        private static string Millimeters(float meters) =>
            (meters * 1000f).ToString("0.###", CultureInfo.InvariantCulture);

        private static string EscapeMarkdown(string value) =>
            (value ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }

    internal static class ClearanceQaReportWriter
    {
        internal static bool TryWritePair(
            string jsonPath,
            string markdownPath,
            string json,
            string markdown,
            out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(jsonPath) || string.IsNullOrWhiteSpace(markdownPath))
            {
                error = "Output path is empty.";
                return false;
            }
            string fullJsonPath;
            string fullMarkdownPath;
            try
            {
                fullJsonPath = Path.GetFullPath(jsonPath);
                fullMarkdownPath = Path.GetFullPath(markdownPath);
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
            if (string.Equals(fullJsonPath, fullMarkdownPath, StringComparison.OrdinalIgnoreCase))
            {
                error = "JSON and Markdown paths must be different.";
                return false;
            }

            string jsonDirectory = Path.GetDirectoryName(fullJsonPath);
            string markdownDirectory = Path.GetDirectoryName(fullMarkdownPath);
            if (string.IsNullOrEmpty(jsonDirectory) || string.IsNullOrEmpty(markdownDirectory) ||
                !Directory.Exists(jsonDirectory) || !Directory.Exists(markdownDirectory))
            {
                error = "Output directory does not exist.";
                return false;
            }

            string token = Guid.NewGuid().ToString("N");
            string jsonTemp = Path.Combine(jsonDirectory, "." + Path.GetFileName(fullJsonPath) + "." + token + ".tmp");
            string markdownTemp = Path.Combine(markdownDirectory, "." + Path.GetFileName(fullMarkdownPath) + "." + token + ".tmp");
            string jsonBackup = jsonTemp + ".bak";
            string markdownBackup = markdownTemp + ".bak";
            bool jsonExisted = File.Exists(fullJsonPath);
            bool markdownExisted = File.Exists(fullMarkdownPath);
            try
            {
                File.WriteAllText(jsonTemp, json ?? "", new UTF8Encoding(false));
                File.WriteAllText(markdownTemp, markdown ?? "", new UTF8Encoding(false));
                if (jsonExisted) File.Copy(fullJsonPath, jsonBackup, true);
                if (markdownExisted) File.Copy(fullMarkdownPath, markdownBackup, true);
                Replace(jsonTemp, fullJsonPath);
                try
                {
                    Replace(markdownTemp, fullMarkdownPath);
                }
                catch
                {
                    Restore(fullJsonPath, jsonBackup, jsonExisted);
                    throw;
                }
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                Restore(fullJsonPath, jsonBackup, jsonExisted);
                Restore(fullMarkdownPath, markdownBackup, markdownExisted);
                return false;
            }
            finally
            {
                DeleteIfExists(jsonTemp);
                DeleteIfExists(markdownTemp);
                DeleteIfExists(jsonBackup);
                DeleteIfExists(markdownBackup);
            }
        }

        private static void Replace(string temporaryPath, string destinationPath)
        {
            if (File.Exists(destinationPath))
                File.Replace(temporaryPath, destinationPath, null);
            else
                File.Move(temporaryPath, destinationPath);
        }

        private static void Restore(string destinationPath, string backupPath, bool existed)
        {
            try
            {
                if (existed && File.Exists(backupPath))
                    File.Copy(backupPath, destinationPath, true);
                else if (!existed && File.Exists(destinationPath))
                    File.Delete(destinationPath);
            }
            catch
            {
                // Preserve the original failure; best-effort rollback cannot hide it.
            }
        }

        private static void DeleteIfExists(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // Temporary cleanup is best effort.
            }
        }
    }
}
#endif
