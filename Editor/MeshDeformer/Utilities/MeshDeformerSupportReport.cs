#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using PackageManagerInfo = UnityEditor.PackageManager.PackageInfo;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class MeshDeformerSupportReport
    {
        internal const int FormatVersion = 1;
        private const byte CodecJsonGzip = 1;
        private static readonly byte[] s_envelopeMagic = { 0x4C, 0x44, 0x54, 0x44, 0x42, 0x47 };
        private static readonly string[] s_packageNames =
        {
            "net.32ba.lattice-deformation-tool",
            "nadena.dev.ndmf",
            "nadena.dev.modular-avatar",
            "com.anatawa12.avatar-optimizer",
            "com.vrchat.avatars",
            "com.vrchat.base",
        };

        internal static string Generate(LatticeDeformer deformer)
        {
            string json = ConvertPlainTextToJson(GeneratePlainText(deformer));
            byte[] compressed = Compress(Encoding.UTF8.GetBytes(json));
            byte[] checksum = ComputeChecksum(compressed);
            using var envelope = new MemoryStream(
                s_envelopeMagic.Length + 2 + checksum.Length + compressed.Length);
            envelope.Write(s_envelopeMagic, 0, s_envelopeMagic.Length);
            envelope.WriteByte(FormatVersion);
            envelope.WriteByte(CodecJsonGzip);
            envelope.Write(checksum, 0, checksum.Length);
            envelope.Write(compressed, 0, compressed.Length);
            return ToBase64Url(envelope.ToArray());
        }

        internal static string Decode(string encodedReport)
        {
            if (string.IsNullOrEmpty(encodedReport))
                throw new FormatException("The Mesh Deformer support report is empty.");

            byte[] envelope = FromBase64Url(encodedReport);
            int headerLength = s_envelopeMagic.Length + 2 + 8;
            if (envelope.Length <= headerLength)
                throw new FormatException("The Mesh Deformer support report is incomplete.");
            for (int i = 0; i < s_envelopeMagic.Length; i++)
            {
                if (envelope[i] != s_envelopeMagic[i])
                    throw new FormatException("Unsupported Mesh Deformer support report envelope.");
            }
            if (envelope[s_envelopeMagic.Length] != FormatVersion ||
                envelope[s_envelopeMagic.Length + 1] != CodecJsonGzip)
            {
                throw new FormatException("Unsupported Mesh Deformer support report format.");
            }

            int checksumOffset = s_envelopeMagic.Length + 2;
            int payloadOffset = checksumOffset + 8;
            var compressed = new byte[envelope.Length - payloadOffset];
            Buffer.BlockCopy(envelope, payloadOffset, compressed, 0, compressed.Length);
            byte[] actualChecksum = ComputeChecksum(compressed);
            bool checksumMatches = true;
            for (int i = 0; i < actualChecksum.Length; i++)
                checksumMatches &= envelope[checksumOffset + i] == actualChecksum[i];
            if (!checksumMatches)
                throw new InvalidDataException("The Mesh Deformer support report checksum does not match.");

            using var input = new MemoryStream(compressed, false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return Encoding.UTF8.GetString(output.ToArray());
        }

        private static string GeneratePlainText(LatticeDeformer deformer)
        {
            var report = new StringBuilder(8192);
            report.AppendLine("Mesh Deformer Support Report");
            Append(report, "format-version", FormatVersion);
            Append(report, "generated-utc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            Append(report, "unity-version", Application.unityVersion);
            Append(report, "platform", Application.platform);
            Append(report, "operating-system", SystemInfo.operatingSystem);
            Append(report, "graphics-api", SystemInfo.graphicsDeviceType);
            AppendSection(report, "packages", () => AppendPackages(report));

            if (deformer == null)
            {
                report.AppendLine();
                report.AppendLine("[component]");
                Append(report, "present", false);
                return report.ToString();
            }

            AppendSection(report, "component", () => AppendComponent(report, deformer));
            Renderer renderer = deformer.GetComponent<Renderer>();
            AppendSection(report, "source-renderer", () => AppendRenderer(report, renderer, deformer.SourceMesh));
            AppendSection(report, "blend-shapes", () => AppendBlendShapes(report, deformer, renderer));
            AppendSection(report, "deformer-stack", () => AppendStack(report, deformer));
            AppendSection(report, "preview", () => AppendPreview(report, renderer));
            AppendSection(report, "validation", () => AppendValidation(report, deformer));
            return report.ToString();
        }

        private static void AppendPackages(StringBuilder report)
        {
            var versions = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                foreach (PackageManagerInfo package in PackageManagerInfo.GetAllRegisteredPackages())
                {
                    if (package != null && !string.IsNullOrEmpty(package.name))
                        versions[package.name] = package.version ?? "unknown";
                }
            }
            catch (Exception exception)
            {
                Append(report, "lookup-error", exception.GetType().Name);
            }

            foreach (string packageName in s_packageNames)
                Append(report, packageName, versions.TryGetValue(packageName, out string version)
                    ? version
                    : "not-installed");
        }

        private static void AppendComponent(StringBuilder report, LatticeDeformer deformer)
        {
            Append(report, "present", true);
            Append(report, "hierarchy", GetHierarchyPath(deformer.transform));
            Append(report, "enabled", deformer.enabled);
            Append(report, "active-in-hierarchy", deformer.gameObject.activeInHierarchy);
            Append(report, "data-source", deformer.DataSource);
            Append(report, "profile-present", deformer.Profile != null);
            Append(report, "active-group", deformer.ActiveGroupIndex);
            Append(report, "active-layer", deformer.ActiveLayerIndex);
            AppendTransform(report, "component-transform", deformer.transform);

            var serialized = new SerializedObject(deformer);
            serialized.UpdateIfRequiredOrScript();
            AppendSerialized(report, serialized, "recalculate-normals", "_recalculateNormals");
            AppendSerialized(report, serialized, "recalculate-tangents", "_recalculateTangents");
            AppendSerialized(report, serialized, "recalculate-bounds", "_recalculateBounds");
            Append(report, "recalculate-bone-weights", deformer.RecalculateBoneWeights);
            Append(report, "align-mode", deformer.AlignMode);
            Append(report, "manual-offset", FormatVector(deformer.ManualOffsetProxy));
            Append(report, "manual-scale", FormatVector(deformer.ManualScaleProxy));
        }

        private static void AppendRenderer(StringBuilder report, Renderer renderer, Mesh sourceMesh)
        {
            Append(report, "present", renderer != null);
            if (renderer == null) return;
            Append(report, "type", renderer.GetType().Name);
            Append(report, "enabled", renderer.enabled);
            Append(report, "hierarchy", GetHierarchyPath(renderer.transform));
            AppendTransform(report, "renderer-transform", renderer.transform);
            Mesh assignedMesh = GetRendererMesh(renderer);
            AppendMesh(report, "source-mesh", sourceMesh);
            AppendMesh(report, "assigned-mesh", assignedMesh);
            Append(report, "assigned-is-source", ReferenceEquals(assignedMesh, sourceMesh));
            if (renderer is SkinnedMeshRenderer skinned)
            {
                Append(report, "bones", skinned.bones?.Length ?? 0);
                Append(report, "root-bone", skinned.rootBone != null
                    ? GetHierarchyPath(skinned.rootBone)
                    : "none");
                Append(report, "quality", skinned.quality);
                Append(report, "update-when-offscreen", skinned.updateWhenOffscreen);
                AppendSkinnedLocalBounds(report, "local-bounds", skinned, assignedMesh);
            }
        }

        private static void AppendBlendShapes(
            StringBuilder report,
            LatticeDeformer deformer,
            Renderer renderer)
        {
            if (renderer is not SkinnedMeshRenderer skinned || skinned.sharedMesh == null)
            {
                Append(report, "available", false);
                return;
            }

            Mesh mesh = skinned.sharedMesh;
            float[] baseline = deformer.InitialBlendShapeWeightsForEditor;
            Append(report, "available", true);
            Append(report, "count", mesh.blendShapeCount);
            Append(report, "baseline-count", baseline?.Length ?? 0);
            int written = 0;
            const int maximumEntries = 256;
            for (int shape = 0; shape < mesh.blendShapeCount; shape++)
            {
                float current = skinned.GetBlendShapeWeight(shape);
                float initial = baseline != null && shape < baseline.Length ? baseline[shape] : 0f;
                if (Mathf.Abs(current) <= 1e-5f && Mathf.Abs(initial) <= 1e-5f) continue;
                if (written >= maximumEntries) break;
                Append(
                    report,
                    $"shape[{shape}]",
                    $"name={Sanitize(mesh.GetBlendShapeName(shape))}, current={FormatFloat(current)}, " +
                    $"initial={FormatFloat(initial)}, frames={mesh.GetBlendShapeFrameCount(shape)}");
                written++;
            }
            Append(report, "reported-nonzero", written);
            Append(report, "truncated", written >= maximumEntries);
        }

        private static void AppendStack(StringBuilder report, LatticeDeformer deformer)
        {
            IReadOnlyList<DeformerGroup> groups = deformer.Groups;
            Append(report, "group-count", groups?.Count ?? 0);
            if (groups == null) return;
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                DeformerGroup group = groups[groupIndex];
                if (group == null)
                {
                    Append(report, $"group[{groupIndex}]", "null");
                    continue;
                }
                Append(
                    report,
                    $"group[{groupIndex}]",
                    $"name={Sanitize(group.Name)}, enabled={group.Enabled}, active-layer={group.ActiveLayerIndex}, " +
                    $"output={group.BlendShapeOutput}, composition={group.BlendShapeComposition}, " +
                    $"layers={group.Layers.Count}");
                for (int layerIndex = 0; layerIndex < group.Layers.Count; layerIndex++)
                {
                    LatticeLayer layer = group.Layers[layerIndex];
                    if (layer == null)
                    {
                        Append(report, $"group[{groupIndex}].layer[{layerIndex}]", "null");
                        continue;
                    }
                    LatticeAsset settings = layer.Settings;
                    Append(
                        report,
                        $"group[{groupIndex}].layer[{layerIndex}]",
                        $"name={Sanitize(layer.Name)}, enabled={layer.Enabled}, type={layer.Type}, " +
                        $"weight={FormatFloat(layer.Weight)}, output={layer.BlendShapeOutput}, " +
                        $"grid={FormatVector(settings.GridSize)}, bounds={FormatBounds(settings.LocalBounds)}, " +
                        $"interpolation={settings.Interpolation}, controls={settings.ControlPointCount}, " +
                        $"brush={layer.BrushDisplacementCount}, mask={layer.VertexMask?.Length ?? 0}");
                }
            }
        }

        private static void AppendPreview(StringBuilder report, Renderer original)
        {
            Append(report, "ndmf-toggle", LatticeDeformerPreviewFilter.PreviewToggleEnabled);
            Append(report, "assign-runtime-mesh", LatticePreviewUtility.ShouldAssignRuntimeMesh());
            Append(report, "preview-aligned-cage", LatticePreviewUtility.UsePreviewAlignedCage);
            Append(report, "proxy-mapping-revision", LatticePreviewUtility.ProxyMappingRevision);
            Renderer proxy = null;
            bool hasProxy = original != null &&
                            LatticePreviewUtility.TryGetPreviewProxy(original, out proxy) &&
                            proxy != null;
            Append(report, "proxy-present", hasProxy);
            if (!hasProxy) return;
            Append(report, "proxy-type", proxy.GetType().Name);
            Append(report, "proxy-hierarchy", GetHierarchyPath(proxy.transform));
            AppendTransform(report, "proxy-transform", proxy.transform);
            AppendMesh(report, "proxy-mesh", GetRendererMesh(proxy));
            if (proxy is SkinnedMeshRenderer skinned)
            {
                Append(report, "proxy-bones", skinned.bones?.Length ?? 0);
                AppendSkinnedLocalBounds(report, "proxy-local-bounds", skinned, skinned.sharedMesh);
            }
            bool hasPreviewMesh = LatticePreviewUtility.TryGetPreviewMesh(original, out Mesh previewMesh);
            Append(report, "registered-preview-mesh", hasPreviewMesh);
            if (hasPreviewMesh) AppendMesh(report, "registered-preview", previewMesh);
        }

        private static void AppendValidation(StringBuilder report, LatticeDeformer deformer)
        {
            IReadOnlyList<MeshDeformerDiagnostic> diagnostics = MeshDeformerValidator.Validate(deformer);
            Append(report, "count", diagnostics.Count);
            for (int i = 0; i < diagnostics.Count; i++)
            {
                MeshDeformerDiagnostic diagnostic = diagnostics[i];
                Append(
                    report,
                    $"diagnostic[{i}]",
                    $"severity={diagnostic.Severity}, {Sanitize(diagnostic.FormatForLog())}");
            }
        }

        private static void AppendMesh(StringBuilder report, string prefix, Mesh mesh)
        {
            Append(report, $"{prefix}.present", mesh != null);
            if (mesh == null) return;
            Append(report, $"{prefix}.name", mesh.name);
            Append(report, $"{prefix}.readable", mesh.isReadable);
            Append(report, $"{prefix}.vertices", mesh.vertexCount);
            Append(report, $"{prefix}.submeshes", mesh.subMeshCount);
            Append(report, $"{prefix}.blend-shapes", mesh.blendShapeCount);
            Append(report, $"{prefix}.bindposes", mesh.bindposeCount);
            Append(report, $"{prefix}.bounds", FormatBounds(mesh.bounds));
            long indices = 0;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                indices += (long)mesh.GetIndexCount(subMesh);
            Append(report, $"{prefix}.indices", indices);
        }

        private static void AppendSkinnedLocalBounds(
            StringBuilder report,
            string key,
            SkinnedMeshRenderer renderer,
            Mesh mesh)
        {
            int boneCount = renderer.bones?.Length ?? 0;
            if (mesh != null && mesh.bindposeCount != boneCount)
            {
                Append(report, key, "unavailable (bone-bindpose-count-mismatch)");
                return;
            }

            Append(report, key, FormatBounds(renderer.localBounds));
        }

        private static void AppendTransform(StringBuilder report, string prefix, Transform transform)
        {
            if (transform == null) return;
            Append(report, $"{prefix}.local-position", FormatVector(transform.localPosition));
            Append(report, $"{prefix}.local-rotation", FormatVector(transform.localEulerAngles));
            Append(report, $"{prefix}.local-scale", FormatVector(transform.localScale));
            Append(report, $"{prefix}.lossy-scale", FormatVector(transform.lossyScale));
        }

        private static void AppendSerialized(
            StringBuilder report,
            SerializedObject serialized,
            string key,
            string propertyPath)
        {
            SerializedProperty property = serialized.FindProperty(propertyPath);
            Append(report, key, property != null ? property.boolValue.ToString() : "unavailable");
        }

        private static Mesh GetRendererMesh(Renderer renderer)
        {
            return renderer switch
            {
                SkinnedMeshRenderer skinned => skinned.sharedMesh,
                MeshRenderer meshRenderer => meshRenderer.GetComponent<MeshFilter>()?.sharedMesh,
                _ => null,
            };
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null) return "none";
            var parts = new Stack<string>();
            for (Transform current = transform; current != null; current = current.parent)
                parts.Push(Sanitize(current.name));
            return string.Join("/", parts);
        }

        private static void AppendSection(StringBuilder report, string name, Action body)
        {
            report.AppendLine();
            report.Append('[').Append(name).AppendLine("]");
            try
            {
                body();
            }
            catch (Exception exception)
            {
                Append(report, "section-error", exception.GetType().Name + ": " + exception.Message);
            }
        }

        private static void Append(StringBuilder report, string key, object value)
        {
            report.Append(key).Append('=').AppendLine(Sanitize(value?.ToString() ?? "null"));
        }

        private static string FormatFloat(float value) =>
            value.ToString("0.######", CultureInfo.InvariantCulture);

        private static string FormatVector(Vector3 value) =>
            $"({FormatFloat(value.x)},{FormatFloat(value.y)},{FormatFloat(value.z)})";

        private static string FormatVector(Vector3Int value) =>
            $"({value.x},{value.y},{value.z})";

        private static string FormatBounds(Bounds value) =>
            $"center={FormatVector(value.center)}, size={FormatVector(value.size)}";

        private static byte[] Compress(byte[] input)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(
                       output,
                       System.IO.Compression.CompressionLevel.Optimal,
                       true))
                gzip.Write(input, 0, input.Length);
            return output.ToArray();
        }

        private static string ToBase64Url(byte[] value) =>
            Convert.ToBase64String(value)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

        private static byte[] FromBase64Url(string value)
        {
            string base64 = value.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2:
                    base64 += "==";
                    break;
                case 3:
                    base64 += "=";
                    break;
                case 1:
                    throw new FormatException("The support report Base64URL payload is invalid.");
            }
            return Convert.FromBase64String(base64);
        }

        private static byte[] ComputeChecksum(byte[] value)
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(value);
            var checksum = new byte[8];
            Buffer.BlockCopy(hash, 0, checksum, 0, checksum.Length);
            return checksum;
        }

        private static string ConvertPlainTextToJson(string plainText)
        {
            string[] lines = plainText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var json = new StringBuilder(plainText.Length + 256);
            json.Append('{');
            bool firstRootProperty = true;
            if (lines.Length > 0 && !string.IsNullOrEmpty(lines[0]))
                AppendJsonProperty(json, ref firstRootProperty, "report", lines[0]);

            string currentSection = null;
            var sectionProperties = new List<KeyValuePair<string, string>>();
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0) continue;
                if (line.Length >= 2 && line[0] == '[' && line[line.Length - 1] == ']')
                {
                    FlushJsonSection(json, ref firstRootProperty, currentSection, sectionProperties);
                    currentSection = line.Substring(1, line.Length - 2);
                    sectionProperties.Clear();
                    continue;
                }

                int separator = line.IndexOf('=');
                string key = separator >= 0 ? line.Substring(0, separator) : "unparsed";
                string value = separator >= 0 ? line.Substring(separator + 1) : line;
                if (currentSection == null)
                    AppendJsonProperty(json, ref firstRootProperty, key, value);
                else
                    sectionProperties.Add(new KeyValuePair<string, string>(key, value));
            }
            FlushJsonSection(json, ref firstRootProperty, currentSection, sectionProperties);
            json.Append('}');
            return json.ToString();
        }

        private static void FlushJsonSection(
            StringBuilder json,
            ref bool firstRootProperty,
            string section,
            IReadOnlyList<KeyValuePair<string, string>> properties)
        {
            if (section == null) return;
            if (!firstRootProperty) json.Append(',');
            firstRootProperty = false;
            AppendJsonString(json, section);
            json.Append(":{");
            bool firstSectionProperty = true;
            for (int i = 0; i < properties.Count; i++)
                AppendJsonProperty(
                    json,
                    ref firstSectionProperty,
                    properties[i].Key,
                    properties[i].Value);
            json.Append('}');
        }

        private static void AppendJsonProperty(
            StringBuilder json,
            ref bool firstProperty,
            string key,
            string value)
        {
            if (!firstProperty) json.Append(',');
            firstProperty = false;
            AppendJsonString(json, key);
            json.Append(':');
            AppendJsonString(json, value);
        }

        private static void AppendJsonString(StringBuilder json, string value)
        {
            json.Append('"');
            if (value != null)
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char character = value[i];
                    switch (character)
                    {
                        case '"': json.Append("\\\""); break;
                        case '\\': json.Append("\\\\"); break;
                        case '\b': json.Append("\\b"); break;
                        case '\f': json.Append("\\f"); break;
                        case '\n': json.Append("\\n"); break;
                        case '\r': json.Append("\\r"); break;
                        case '\t': json.Append("\\t"); break;
                        default:
                            if (character < 0x20)
                                json.Append("\\u").Append(((int)character).ToString("x4"));
                            else
                                json.Append(character);
                            break;
                    }
                }
            }
            json.Append('"');
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            const int maximumLength = 1024;
            string sanitized = value
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
            return sanitized.Length <= maximumLength
                ? sanitized
                : sanitized.Substring(0, maximumLength) + "...[truncated]";
        }
    }
}
#endif
