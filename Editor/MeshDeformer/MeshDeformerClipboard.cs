#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Net._32Ba.LatticeDeformationTool;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Result of a clipboard operation.  The code is deliberately stable so tests,
    /// reports, and the Inspector can distinguish a safe refusal from a no-op.
    /// </summary>
    internal sealed class MeshDeformerClipboardResult
    {
        internal bool Succeeded { get; }
        internal string Code { get; }
        internal string MessageKey { get; }
        internal string Detail { get; }

        private MeshDeformerClipboardResult(bool succeeded, string code, string messageKey, string detail)
        {
            Succeeded = succeeded;
            Code = code ?? string.Empty;
            MessageKey = messageKey ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        internal static MeshDeformerClipboardResult Success(string code = "LDT_CLIPBOARD_OK") =>
            new MeshDeformerClipboardResult(true, code, string.Empty, string.Empty);

        internal static MeshDeformerClipboardResult Refused(
            string code,
            string messageKey,
            string detail = "") =>
            new MeshDeformerClipboardResult(false, code, messageKey, detail);
    }

    /// <summary>
    /// Editor-session layer/group clipboard shared by the Inspector and interaction
    /// tests.  It owns the serialized snapshot and performs all compatibility checks
    /// before recording Undo or changing a deformer.
    /// </summary>
    internal static class MeshDeformerClipboard
    {
        private const string kNoClipboardCode = "LDT_CLIPBOARD_EMPTY";
        private const string kInvalidPayloadCode = "LDT_CLIPBOARD_INVALID_PAYLOAD";
        private const string kNonFiniteCode = "LDT_CLIPBOARD_NONFINITE_PAYLOAD";
        private const string kUnreadableCode = "LDT_CLIPBOARD_MESH_UNREADABLE";
        private const string kProfileCode = "LDT_CLIPBOARD_PROFILE_READ_ONLY";
        private const string kVertexCountCode = "LDT_CLIPBOARD_VERTEX_COUNT_MISMATCH";
        private const string kTopologyCode = "LDT_CLIPBOARD_TOPOLOGY_MISMATCH";
        private const string kGroupCode = "LDT_CLIPBOARD_GROUP_INCOMPATIBLE";

        private sealed class ClipboardPayload
        {
            internal string Json;
            internal MeshCompatibilityMetadata Compatibility;
            internal bool RequiresVertexCompatibility;
            internal MeshDeformerLayerType LayerType;
            internal bool IsGroup;
        }

        private static ClipboardPayload s_layer;
        private static ClipboardPayload s_group;

        internal static MeshDeformerClipboardResult LastResult { get; private set; } =
            MeshDeformerClipboardResult.Success();

        internal static LatticeDeformer LastTarget { get; private set; }

        internal static bool HasLayer => s_layer != null;
        internal static bool HasGroup => s_group != null;

        internal static MeshDeformerClipboardResult CopyLayer(LatticeDeformer source, int layerIndex)
        {
            if (source == null || layerIndex < 0) return SetResult(source, Refused(
                "LDT_CLIPBOARD_INVALID_SOURCE", LocKey.ClipboardInvalidSource));

            if (!TryGetRawActiveGroup(source, out var group) ||
                group.SerializedLayers == null || layerIndex >= group.SerializedLayers.Count ||
                group.SerializedLayers[layerIndex] == null)
                return SetResult(source, Refused(
                    "LDT_CLIPBOARD_INVALID_LAYER", LocKey.ClipboardInvalidLayer));

            var layer = group.SerializedLayers[layerIndex];
            if (!TryValidatePayload(layer, out string payloadCode))
                return SetResult(source, Refused(payloadCode, MessageKeyFor(payloadCode)));

            bool requiresCompatibility = RequiresVertexCompatibility(layer);
            MeshCompatibilityMetadata metadata = null;
            if (requiresCompatibility && !TryCaptureCompatibility(source, out metadata, out var refusal))
                return SetResult(source, refusal);
            if (requiresCompatibility && !HasExpectedVertexPayloadCount(layer, metadata.VertexCount))
                return SetResult(source, Refused(kVertexCountCode, LocKey.ClipboardVertexCountMismatch));

            string json;
            try
            {
                json = JsonUtility.ToJson(layer);
                // Force a deserialization round trip at copy time. This catches a
                // payload that is not safely reproducible before it enters the clipboard.
                var roundTrip = JsonUtility.FromJson<LatticeLayer>(json);
                if (roundTrip == null || !TryValidatePayload(roundTrip, out payloadCode))
                    return SetResult(source, Refused(kInvalidPayloadCode, LocKey.ClipboardInvalidPayload));
            }
            catch (Exception exception)
            {
                return SetResult(source, Refused(kInvalidPayloadCode, LocKey.ClipboardInvalidPayload, exception.Message));
            }

            s_layer = new ClipboardPayload
            {
                Json = json,
                Compatibility = metadata,
                RequiresVertexCompatibility = requiresCompatibility,
                LayerType = layer.Type,
                IsGroup = false
            };
            return SetResult(source, MeshDeformerClipboardResult.Success("LDT_CLIPBOARD_COPIED_LAYER"));
        }

        internal static MeshDeformerClipboardResult CopyGroup(LatticeDeformer source, int groupIndex)
        {
            if (source == null || groupIndex < 0) return SetResult(source, Refused(
                "LDT_CLIPBOARD_INVALID_SOURCE", LocKey.ClipboardInvalidSource));

            if (!TryGetRawGroups(source, out var groups) ||
                groupIndex >= groups.Count || groups[groupIndex] == null)
                return SetResult(source, Refused(
                    "LDT_CLIPBOARD_INVALID_GROUP", LocKey.ClipboardInvalidGroup));

            var group = groups[groupIndex];
            if (group.HasMalformedSerializedMetadata ||
                group.SerializedLayers == null || group.SerializedLayers.Count == 0 ||
                group.SerializedActiveLayerIndex < 0 ||
                group.SerializedActiveLayerIndex >= group.SerializedLayers.Count)
                return SetResult(source, Refused(kGroupCode, LocKey.ClipboardGroupIncompatible));
            var serializedLayers = group.SerializedLayers;
            if (serializedLayers == null)
                return SetResult(source, Refused(kGroupCode, LocKey.ClipboardGroupIncompatible));
            bool requiresCompatibility = false;
            foreach (var layer in serializedLayers)
            {
                if (layer == null || !TryValidatePayload(layer, out string payloadCode))
                    return SetResult(source, Refused(kGroupCode, LocKey.ClipboardGroupIncompatible));
                requiresCompatibility |= RequiresVertexCompatibility(layer);
            }

            MeshCompatibilityMetadata metadata = null;
            if (requiresCompatibility && !TryCaptureCompatibility(source, out metadata, out var refusal))
                return SetResult(source, refusal);
            if (requiresCompatibility)
            {
                foreach (var layer in serializedLayers)
                {
                    if (!HasExpectedVertexPayloadCount(layer, metadata.VertexCount))
                        return SetResult(source, Refused(kGroupCode, LocKey.ClipboardGroupIncompatible));
                }
            }

            string json;
            try
            {
                json = JsonUtility.ToJson(group);
                var roundTrip = JsonUtility.FromJson<DeformerGroup>(json);
                if (roundTrip == null)
                    return SetResult(source, Refused(kInvalidPayloadCode, LocKey.ClipboardInvalidPayload));
                foreach (var layer in roundTrip.SerializedLayers ?? new List<LatticeLayer>())
                {
                    if (layer == null || !TryValidatePayload(layer, out _))
                        return SetResult(source, Refused(kGroupCode, LocKey.ClipboardGroupIncompatible));
                }
            }
            catch (Exception exception)
            {
                return SetResult(source, Refused(kInvalidPayloadCode, LocKey.ClipboardInvalidPayload, exception.Message));
            }

            s_group = new ClipboardPayload
            {
                Json = json,
                Compatibility = metadata,
                RequiresVertexCompatibility = requiresCompatibility,
                IsGroup = true
            };
            return SetResult(source, MeshDeformerClipboardResult.Success("LDT_CLIPBOARD_COPIED_GROUP"));
        }

        /// <summary>Checks and creates a detached layer without mutating the target.</summary>
        internal static MeshDeformerClipboardResult PrepareLayerPaste(
            LatticeDeformer target,
            out LatticeLayer layer)
        {
            layer = null;
            if (target == null) return SetResult(target, Refused(
                "LDT_CLIPBOARD_INVALID_TARGET", LocKey.ClipboardInvalidTarget));
            if (target.DataSource == DeformerDataSource.Profile)
                return SetResult(target, Refused(kProfileCode, LocKey.ClipboardProfileReadOnly));
            if (!HasValidTargetStorage(target))
                return SetResult(target, Refused("LDT_CLIPBOARD_INVALID_TARGET_STRUCTURE", LocKey.ClipboardInvalidTarget));
            if (s_layer == null)
                return SetResult(target, Refused(kNoClipboardCode, LocKey.ClipboardEmpty));
            if (!TryCheckCompatibility(target, s_layer, out var refusal))
                return SetResult(target, refusal);

            try
            {
                layer = JsonUtility.FromJson<LatticeLayer>(s_layer.Json);
                if (layer == null)
                {
                    layer = null;
                    return SetResult(target, Refused(kInvalidPayloadCode, LocKey.ClipboardInvalidPayload));
                }
                if (!TryValidatePayload(layer, out string payloadCode))
                {
                    layer = null;
                    return SetResult(target, Refused(payloadCode ?? kInvalidPayloadCode,
                        MessageKeyFor(payloadCode ?? kInvalidPayloadCode)));
                }
                if (!HasExpectedVertexPayloadCount(layer, s_layer.Compatibility?.VertexCount ?? 0))
                {
                    layer = null;
                    return SetResult(target, Refused(kVertexCountCode, LocKey.ClipboardVertexCountMismatch));
                }
            }
            catch (Exception exception)
            {
                layer = null;
                return SetResult(target, Refused(kInvalidPayloadCode, LocKey.ClipboardInvalidPayload, exception.Message));
            }

            return SetResult(target, MeshDeformerClipboardResult.Success("LDT_CLIPBOARD_READY_LAYER"));
        }

        internal static MeshDeformerClipboardResult PasteLayer(LatticeDeformer target)
        {
            var result = PrepareLayerPaste(target, out var layer);
            if (!result.Succeeded) return result;

            // This is intentionally the first mutation. PrepareLayerPaste above has
            // already completed all compatibility and payload checks.
            Undo.RecordObject(target, "Paste Layer");
            if (target.InsertLayer(layer) < 0)
                return SetResult(target, Refused(kInvalidPayloadCode, LocKey.ClipboardInvalidPayload));
            EditorUtility.SetDirty(target);
            target.InvalidateCache();
            target.Deform(LatticePreviewUtility.ShouldAssignRuntimeMesh());
            LatticePrefabUtility.MarkModified(target);
            LatticePreviewUtility.RequestSceneRepaint();
            SceneView.RepaintAll();
            return SetResult(target, MeshDeformerClipboardResult.Success("LDT_CLIPBOARD_PASTED_LAYER"));
        }

        internal static MeshDeformerClipboardResult PrepareGroupPaste(
            LatticeDeformer target,
            out DeformerGroup group)
        {
            group = null;
            if (target == null) return SetResult(target, Refused(
                "LDT_CLIPBOARD_INVALID_TARGET", LocKey.ClipboardInvalidTarget));
            if (target.DataSource == DeformerDataSource.Profile)
                return SetResult(target, Refused(kProfileCode, LocKey.ClipboardProfileReadOnly));
            if (!HasValidTargetStorage(target))
                return SetResult(target, Refused("LDT_CLIPBOARD_INVALID_TARGET_STRUCTURE", LocKey.ClipboardInvalidTarget));
            if (s_group == null)
                return SetResult(target, Refused(kNoClipboardCode, LocKey.ClipboardEmpty));
            if (!TryCheckCompatibility(target, s_group, out var refusal))
                return SetResult(target, refusal);

            try
            {
                group = JsonUtility.FromJson<DeformerGroup>(s_group.Json);
                if (group == null || group.HasMalformedSerializedMetadata ||
                    group.SerializedLayers == null || group.SerializedLayers.Count == 0 ||
                    group.SerializedActiveLayerIndex < 0 ||
                    group.SerializedActiveLayerIndex >= group.SerializedLayers.Count)
                {
                    group = null;
                    return SetResult(target, Refused(kGroupCode, LocKey.ClipboardGroupIncompatible));
                }
                foreach (var layer in group.SerializedLayers)
                {
                    if (layer == null || !TryValidatePayload(layer, out _))
                    {
                        group = null;
                        return SetResult(target, Refused(kGroupCode, LocKey.ClipboardGroupIncompatible));
                    }
                    if (RequiresVertexCompatibility(layer) &&
                        !HasExpectedVertexPayloadCount(layer, s_group.Compatibility?.VertexCount ?? 0))
                    {
                        group = null;
                        return SetResult(target, Refused(kGroupCode, LocKey.ClipboardGroupIncompatible));
                    }
                }
            }
            catch (Exception exception)
            {
                group = null;
                return SetResult(target, Refused(kInvalidPayloadCode, LocKey.ClipboardInvalidPayload, exception.Message));
            }

            return SetResult(target, MeshDeformerClipboardResult.Success("LDT_CLIPBOARD_READY_GROUP"));
        }

        internal static MeshDeformerClipboardResult PasteGroup(LatticeDeformer target)
        {
            var result = PrepareGroupPaste(target, out var group);
            if (!result.Succeeded) return result;

            var groupsField = typeof(LatticeDeformer).GetField(
                "_groups", BindingFlags.Instance | BindingFlags.NonPublic);
            if (groupsField?.GetValue(target) is not List<DeformerGroup> groups)
                return SetResult(target, Refused(kInvalidPayloadCode, LocKey.ClipboardInvalidPayload));

            Undo.RecordObject(target, "Paste Group");
            groups.Add(group);
            target.ActiveGroupIndex = groups.Count - 1;
            EditorUtility.SetDirty(target);
            target.InvalidateCache();
            target.Deform(LatticePreviewUtility.ShouldAssignRuntimeMesh());
            LatticePrefabUtility.MarkModified(target);
            LatticePreviewUtility.RequestSceneRepaint();
            SceneView.RepaintAll();
            return SetResult(target, MeshDeformerClipboardResult.Success("LDT_CLIPBOARD_PASTED_GROUP"));
        }

        internal static void Clear()
        {
            s_layer = null;
            s_group = null;
            LastTarget = null;
            LastResult = MeshDeformerClipboardResult.Success();
        }

        private static bool TryValidatePayload(LatticeLayer layer, out string code)
        {
            code = null;
            if (layer == null || layer.HasMalformedSerializedMetadata)
            {
                code = kInvalidPayloadCode;
                return false;
            }
            if (layer.SerializedSettings == null || layer.SerializedSettings.HasMalformedSerializedShape)
            {
                code = kInvalidPayloadCode;
                return false;
            }
            if (layer.HasNonFiniteSerializedVertexData)
            {
                code = kNonFiniteCode;
                return false;
            }
            if (layer.Type == MeshDeformerLayerType.Brush &&
                layer.SerializedBrushDisplacementCount == 0)
            {
                code = kInvalidPayloadCode;
                return false;
            }
            if (layer.Type == MeshDeformerLayerType.Brush &&
                layer.SerializedVertexMaskCount != 0 &&
                layer.SerializedVertexMaskCount != layer.SerializedBrushDisplacementCount)
            {
                code = kInvalidPayloadCode;
                return false;
            }
            return true;
        }

        private static bool RequiresVertexCompatibility(LatticeLayer layer)
        {
            return layer != null &&
                   (layer.Type == MeshDeformerLayerType.Brush || layer.SerializedVertexMaskCount > 0);
        }

        private static bool HasExpectedVertexPayloadCount(LatticeLayer layer, int vertexCount)
        {
            if (layer == null || vertexCount < 0) return false;
            if (layer.Type == MeshDeformerLayerType.Brush &&
                layer.SerializedBrushDisplacementCount != vertexCount)
                return false;
            if (layer.SerializedVertexMaskCount != 0 &&
                layer.SerializedVertexMaskCount != vertexCount)
                return false;
            return true;
        }

        private static bool TryCheckCompatibility(
            LatticeDeformer target,
            ClipboardPayload payload,
            out MeshDeformerClipboardResult refusal)
        {
            refusal = null;
            if (!payload.RequiresVertexCompatibility) return true;
            if (!TryCaptureCompatibility(target, out var targetMetadata, out refusal)) return false;
            var sourceMetadata = payload.Compatibility;
            if (sourceMetadata == null || !sourceMetadata.IsAvailable)
            {
                refusal = Refused(kUnreadableCode, LocKey.ClipboardUnreadableMesh);
                return false;
            }
            if (sourceMetadata.VertexCount != targetMetadata.VertexCount)
            {
                refusal = Refused(kVertexCountCode, LocKey.ClipboardVertexCountMismatch);
                return false;
            }
            if (!string.Equals(sourceMetadata.TopologyHash, targetMetadata.TopologyHash, StringComparison.Ordinal))
            {
                refusal = Refused(kTopologyCode, LocKey.ClipboardTopologyMismatch);
                return false;
            }
            return true;
        }

        private static bool TryCaptureCompatibility(
            LatticeDeformer deformer,
            out MeshCompatibilityMetadata metadata,
            out MeshDeformerClipboardResult refusal)
        {
            metadata = null;
            refusal = null;
            Mesh mesh = ResolveSourceMesh(deformer);
            if (mesh == null)
            {
                refusal = Refused(kUnreadableCode, LocKey.ClipboardUnreadableMesh);
                return false;
            }

            if (mesh.isReadable)
            {
                try
                {
                    metadata = MeshCompatibilityMetadata.Capture(mesh);
                    if (metadata.IsAvailable) return true;
                }
                catch
                {
                    // Fall through to the Editor-only readable copy for imported meshes.
                }
            }

            Mesh readableCopy = null;
            try
            {
                readableCopy = LatticeDeformer.CreateEditorReadableMeshCopyForClipboard(mesh);
                metadata = readableCopy != null ? MeshCompatibilityMetadata.Capture(readableCopy) : null;
                if (metadata != null && metadata.IsAvailable) return true;
            }
            catch
            {
                // The refusal below is intentionally fail-closed.
            }
            finally
            {
                if (readableCopy != null) UnityEngine.Object.DestroyImmediate(readableCopy);
            }

            metadata = null;
            refusal = Refused(kUnreadableCode, LocKey.ClipboardUnreadableMesh);
            return false;
        }

        private static Mesh ResolveSourceMesh(LatticeDeformer deformer)
        {
            if (deformer == null) return null;
            if (deformer.SourceMesh != null) return deformer.SourceMesh;
            var renderer = deformer.TargetRenderer;
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
            return renderer != null ? renderer.GetComponent<MeshFilter>()?.sharedMesh : null;
        }

        private static bool HasValidTargetStorage(LatticeDeformer target)
        {
            if (!TryGetRawGroups(target, out var groups) || groups.Count == 0)
                return false;
            Mesh targetMesh = ResolveSourceMesh(target);
            if (targetMesh == null) return false;
            // Validator.Validate is a read-only diagnostic surface. Reuse its
            // authoritative checks for source drift and future/invalid payloads
            // before any clipboard operation can record Undo or add data.
            if (target.enabled && MeshDeformerValidator.HasErrors(
                    MeshDeformerValidator.Validate(target)))
                return false;
            var activeField = typeof(LatticeDeformer).GetField(
                "_activeGroupIndex", BindingFlags.Instance | BindingFlags.NonPublic);
            int active = activeField != null ? (int)activeField.GetValue(target) : -1;
            if (active < 0 || active >= groups.Count)
                return false;
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                var group = groups[groupIndex];
                if (group == null || group.HasMalformedSerializedMetadata ||
                    group.SerializedLayers == null || group.SerializedLayers.Count == 0 ||
                    group.SerializedActiveLayerIndex < 0 ||
                    group.SerializedActiveLayerIndex >= group.SerializedLayers.Count)
                    return false;
                foreach (var layer in group.SerializedLayers)
                {
                    if (layer == null || !TryValidatePayload(layer, out _))
                        return false;
                    if (layer.Type == MeshDeformerLayerType.Brush &&
                        layer.SerializedBrushDisplacementCount != targetMesh.vertexCount)
                        return false;
                    if (layer.SerializedVertexMaskCount != 0 &&
                        layer.SerializedVertexMaskCount != targetMesh.vertexCount)
                        return false;
                }
            }
            return groups[active] != null;
        }

        private static bool TryGetRawActiveGroup(
            LatticeDeformer deformer,
            out DeformerGroup group)
        {
            group = null;
            if (!TryGetRawGroups(deformer, out var groups)) return false;
            var activeField = typeof(LatticeDeformer).GetField(
                "_activeGroupIndex", BindingFlags.Instance | BindingFlags.NonPublic);
            if (activeField == null) return false;
            int active = (int)activeField.GetValue(deformer);
            if (active < 0 || active >= groups.Count) return false;
            group = groups[active];
            return group != null;
        }

        private static bool TryGetRawGroups(
            LatticeDeformer deformer,
            out List<DeformerGroup> groups)
        {
            groups = null;
            if (deformer == null) return false;
            var groupsField = typeof(LatticeDeformer).GetField(
                "_groups", BindingFlags.Instance | BindingFlags.NonPublic);
            groups = groupsField?.GetValue(deformer) as List<DeformerGroup>;
            return groups != null;
        }

        private static MeshDeformerClipboardResult SetResult(
            LatticeDeformer target,
            MeshDeformerClipboardResult result)
        {
            LastTarget = target;
            LastResult = result ?? MeshDeformerClipboardResult.Success();
            return LastResult;
        }

        private static MeshDeformerClipboardResult Refused(string code, string key, string detail = "") =>
            MeshDeformerClipboardResult.Refused(code, key, detail);

        private static string MessageKeyFor(string code)
        {
            return code == kNonFiniteCode ? LocKey.ClipboardNonFinitePayload : LocKey.ClipboardInvalidPayload;
        }
    }
}
#endif
