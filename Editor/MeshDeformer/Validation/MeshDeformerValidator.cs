#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal enum MeshDeformerDiagnosticSeverity
    {
        Info,
        Warning,
        Error
    }

    internal sealed class MeshDeformerDiagnostic
    {
        internal string Code { get; }
        internal MeshDeformerDiagnosticSeverity Severity { get; }
        internal Object Target { get; }
        internal int GroupIndex { get; }
        internal int LayerIndex { get; }
        internal string PropertyPath { get; }
        internal string Message { get; }
        internal string FixLabel { get; }
        internal Action Fix { get; }

        internal MeshDeformerDiagnostic(
            string code,
            MeshDeformerDiagnosticSeverity severity,
            Object target,
            string message,
            int groupIndex = -1,
            int layerIndex = -1,
            string propertyPath = "",
            string fixLabel = "",
            Action fix = null)
        {
            Code = code;
            Severity = severity;
            Target = target;
            Message = message;
            GroupIndex = groupIndex;
            LayerIndex = layerIndex;
            PropertyPath = propertyPath ?? "";
            FixLabel = fixLabel ?? "";
            Fix = fix;
        }

        internal string FormatForLog()
        {
            string location = Target != null ? Target.name : "<destroyed>";
            if (GroupIndex >= 0) location += $" / group {GroupIndex}";
            if (LayerIndex >= 0) location += $" / layer {LayerIndex}";
            if (!string.IsNullOrEmpty(PropertyPath)) location += $" / {PropertyPath}";
            return $"[{Code}] {location}: {Message}";
        }
    }

    /// <summary>
    /// One validation surface shared by the Inspector, NDMF preview and NDMF bake.
    /// The optional target mesh is the mesh the caller is about to evaluate.
    /// </summary>
    internal static class MeshDeformerValidator
    {
        private static readonly FieldInfo s_groupsField = typeof(LatticeDeformer)
            .GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo s_layersField = typeof(DeformerGroup)
            .GetField("_layers", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo s_settingsField = typeof(LatticeLayer)
            .GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic);
        internal const string MissingRenderer = "MDV001";
        internal const string MissingSourceMesh = "MDV002";
        internal const string SourceMeshChanged = "MDV003";
        internal const string BrushLengthMismatch = "MDV004";
        internal const string MaskLengthMismatch = "MDV005";
        internal const string InvalidGridSize = "MDV006";
        internal const string ControlPointCountMismatch = "MDV007";
        internal const string InvalidBounds = "MDV008";
        internal const string InvalidGroupStructure = "MDV009";
        internal const string InvalidLayerStructure = "MDV010";
        internal const string EmptyBlendShapeName = "MDV011";
        internal const string DuplicateBlendShapeName = "MDV012";
        internal const string ExistingBlendShapeCollision = "MDV013";
        internal const string ProfileCompatibilityUnknown = "MDV014";
        internal const string ProfileTopologyMismatch = "MDV015";
        internal const string InvalidClearanceReference = "MDV016";
        internal const string RestSpaceConversionUnsafe = "MDV017";
        internal const string PreviewBakeTargetMismatch = "MDV018";
        internal const string NullGroupOrLayer = "MDV019";

        internal static IReadOnlyList<MeshDeformerDiagnostic> Validate(
            LatticeDeformer deformer,
            Mesh evaluationTargetMesh = null)
        {
            var results = new List<MeshDeformerDiagnostic>();
            if (deformer == null || !deformer.enabled) return results;

            var serialized = new SerializedObject(deformer);
            serialized.UpdateIfRequiredOrScript();
            var renderer = ResolveRenderer(serialized);
            if (renderer == null)
            {
                Add(results, MissingRenderer, MeshDeformerDiagnosticSeverity.Error, deformer,
                    "No target SkinnedMeshRenderer or MeshFilter is assigned.", property: "Renderer");
                return results;
            }

            Mesh currentMesh = GetRendererMesh(renderer);
            if (currentMesh == null)
            {
                Add(results, MissingSourceMesh, MeshDeformerDiagnosticSeverity.Error, deformer,
                    "The target renderer has no source mesh.", property: "sharedMesh");
                return results;
            }

            var serializedSource = serialized.FindProperty("_serializedSourceMesh")?.objectReferenceValue as Mesh;
            int serializedVertexCount = serialized.FindProperty("_serializedSourceVertexCount")?.intValue ?? 0;
            int serializedTopologyHash = serialized.FindProperty("_serializedSourceTopologyHash")?.intValue ?? 0;
            if (serializedSource != null && !ReferenceEquals(serializedSource, currentMesh))
            {
                Add(results, SourceMeshChanged, MeshDeformerDiagnosticSeverity.Error, deformer,
                    "The renderer mesh differs from the mesh used to serialize the deformation data. Retargeting is not automatic.",
                    property: "_serializedSourceMesh");
            }
            else if (serializedSource != null &&
                     ((serializedVertexCount > 0 && serializedVertexCount != currentMesh.vertexCount) ||
                      (serializedTopologyHash != 0 && serializedTopologyHash != CalculateTopologyHash(currentMesh))))
            {
                Add(results, SourceMeshChanged, MeshDeformerDiagnosticSeverity.Error, deformer,
                    "The source mesh topology changed after the deformation data was serialized. Automatic retargeting is blocked.",
                    property: "_serializedSourceTopologyHash");
            }

            if (evaluationTargetMesh != null && !ReferenceEquals(evaluationTargetMesh, currentMesh))
            {
                Add(results, PreviewBakeTargetMismatch, MeshDeformerDiagnosticSeverity.Warning, deformer,
                    "The mesh selected for evaluation differs from the renderer mesh; Preview and Bake may target different topology.",
                    property: "sharedMesh");
            }

            ValidateRawStructure(serialized, deformer, results);
            ValidateGroups(deformer, currentMesh, results);
            ValidateProfile(deformer, results);
            ValidateClearance(deformer, renderer, results);
            ValidateRestSpace(deformer, renderer, results);
            return results;
        }

        internal static bool HasErrors(IReadOnlyList<MeshDeformerDiagnostic> diagnostics)
        {
            return diagnostics != null && diagnostics.Any(d => d.Severity == MeshDeformerDiagnosticSeverity.Error);
        }

        internal static void Log(IReadOnlyList<MeshDeformerDiagnostic> diagnostics)
        {
            if (diagnostics == null) return;
            foreach (var diagnostic in diagnostics)
            {
                switch (diagnostic.Severity)
                {
                    case MeshDeformerDiagnosticSeverity.Error:
                        Debug.LogError(diagnostic.FormatForLog(), diagnostic.Target);
                        break;
                    case MeshDeformerDiagnosticSeverity.Warning:
                        Debug.LogWarning(diagnostic.FormatForLog() + " Bake will continue unless another error is present.", diagnostic.Target);
                        break;
                    default:
                        Debug.Log(diagnostic.FormatForLog(), diagnostic.Target);
                        break;
                }
            }
        }

        private static void ValidateRawStructure(
            SerializedObject serialized,
            LatticeDeformer deformer,
            List<MeshDeformerDiagnostic> results)
        {
            if (deformer.DataSource == DeformerDataSource.Profile && deformer.Profile != null)
            {
                serialized = new SerializedObject(deformer.Profile);
                serialized.UpdateIfRequiredOrScript();
            }
            var groups = serialized.FindProperty("_groups");
            var activeGroup = serialized.FindProperty("_activeGroupIndex");
            if (groups == null || !groups.isArray || groups.arraySize == 0)
            {
                Add(results, InvalidGroupStructure, MeshDeformerDiagnosticSeverity.Error, deformer,
                    "The deformer has no groups.", property: "_groups");
                return;
            }

            if (activeGroup == null || activeGroup.intValue < 0 || activeGroup.intValue >= groups.arraySize)
            {
                Add(results, InvalidGroupStructure, MeshDeformerDiagnosticSeverity.Error, deformer,
                    "The active group index is outside the group list.", property: "_activeGroupIndex");
            }

            for (int groupIndex = 0; groupIndex < groups.arraySize; groupIndex++)
            {
                var group = groups.GetArrayElementAtIndex(groupIndex);
                if (group == null)
                {
                    Add(results, NullGroupOrLayer, MeshDeformerDiagnosticSeverity.Error, deformer,
                        "The group reference is null.", groupIndex, property: "_groups");
                    continue;
                }

                var enabled = group.FindPropertyRelative("_enabled");
                if (enabled != null && !enabled.boolValue) continue;
                var layers = group.FindPropertyRelative("_layers");
                var activeLayer = group.FindPropertyRelative("_activeLayerIndex");
                if (layers == null || !layers.isArray || layers.arraySize == 0)
                {
                    Add(results, InvalidLayerStructure, MeshDeformerDiagnosticSeverity.Error, deformer,
                        "An enabled group has no layers.", groupIndex, property: "_layers");
                    continue;
                }

                if (activeLayer == null || activeLayer.intValue < 0 || activeLayer.intValue >= layers.arraySize)
                {
                    Add(results, InvalidLayerStructure, MeshDeformerDiagnosticSeverity.Error, deformer,
                        "The active layer index is outside the layer list.", groupIndex, property: "_activeLayerIndex");
                }
            }
        }

        private static void ValidateGroups(
            LatticeDeformer deformer,
            Mesh sourceMesh,
            List<MeshDeformerDiagnostic> results)
        {
            var outputNames = new Dictionary<string, (int group, int layer)>(StringComparer.Ordinal);
            var sourceNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < sourceMesh.blendShapeCount; i++) sourceNames.Add(sourceMesh.GetBlendShapeName(i));

            var groups = GetValidationGroups(deformer);
            if (groups == null) return;
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                var group = groups[groupIndex];
                if (group == null)
                {
                    Add(results, NullGroupOrLayer, MeshDeformerDiagnosticSeverity.Error, deformer,
                        "The group reference is null.", groupIndex, property: "_groups");
                    continue;
                }
                if (!group.Enabled) continue;

                if (group.BlendShapeOutput == BlendShapeOutputMode.OutputAsBlendShape)
                {
                    ValidateOutputName(deformer, results, outputNames, sourceNames,
                        group.BlendShapeName, groupIndex, -1, "_blendShapeName");
                }

                var layers = s_layersField?.GetValue(group) as List<LatticeLayer>;
                if (layers == null) continue;
                for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
                {
                    var layer = layers[layerIndex];
                    if (layer == null)
                    {
                        Add(results, NullGroupOrLayer, MeshDeformerDiagnosticSeverity.Error, deformer,
                            "The layer reference is null.", groupIndex, layerIndex, "_layers");
                        continue;
                    }
                    if (!layer.Enabled) continue;

                    if (layer.Type == MeshDeformerLayerType.Brush)
                    {
                        if (layer.BrushDisplacementCount != sourceMesh.vertexCount)
                        {
                            int g = groupIndex;
                            int l = layerIndex;
                            Action fix = deformer.DataSource == DeformerDataSource.Embedded
                                ? () => ResizeLayerArray(deformer, g, l, "_brushDisplacements",
                                    sourceMesh.vertexCount, initializeNewMaskValues: false)
                                : null;
                            Add(results, BrushLengthMismatch, MeshDeformerDiagnosticSeverity.Error, deformer,
                                $"Brush displacement count {layer.BrushDisplacementCount} does not match vertex count {sourceMesh.vertexCount}.",
                                g, l, "_brushDisplacements", fix != null ? "Resize" : "", fix);
                        }
                        int maskCount = layer.VertexMask?.Length ?? 0;
                        if (maskCount != 0 && maskCount != sourceMesh.vertexCount)
                        {
                            int g = groupIndex;
                            int l = layerIndex;
                            Action fix = deformer.DataSource == DeformerDataSource.Embedded
                                ? () => ResizeLayerArray(deformer, g, l, "_vertexMask",
                                    sourceMesh.vertexCount, initializeNewMaskValues: true)
                                : null;
                            Add(results, MaskLengthMismatch, MeshDeformerDiagnosticSeverity.Error, deformer,
                                $"Vertex mask count {maskCount} does not match vertex count {sourceMesh.vertexCount}.",
                                g, l, "_vertexMask", fix != null ? "Resize" : "", fix);
                        }
                    }
                    else
                    {
                        ValidateLattice(deformer, results,
                            s_settingsField?.GetValue(layer) as LatticeAsset, groupIndex, layerIndex);
                    }

                    if (layer.BlendShapeOutput == BlendShapeOutputMode.OutputAsBlendShape)
                    {
                        ValidateOutputName(deformer, results, outputNames, sourceNames,
                            layer.BlendShapeName, groupIndex, layerIndex, "_blendShapeName");
                    }
                }
            }
        }

        private static void ValidateLattice(
            LatticeDeformer deformer,
            List<MeshDeformerDiagnostic> results,
            LatticeAsset settings,
            int groupIndex,
            int layerIndex)
        {
            if (settings == null)
            {
                Add(results, InvalidGridSize, MeshDeformerDiagnosticSeverity.Error, deformer,
                    "Lattice settings are missing.", groupIndex, layerIndex, "_settings");
                return;
            }
            Vector3Int grid = settings.GridSize;
            bool overflow = (long)grid.x * grid.y * grid.z > int.MaxValue;
            if (grid.x < 2 || grid.y < 2 || grid.z < 2 || overflow)
            {
                Add(results, InvalidGridSize, MeshDeformerDiagnosticSeverity.Error, deformer,
                    $"Lattice grid size {grid} is invalid.", groupIndex, layerIndex, "_gridSize");
            }
            if (settings.ControlPointsLocal.Length != settings.ControlPointCount)
            {
                Add(results, ControlPointCountMismatch, MeshDeformerDiagnosticSeverity.Error, deformer,
                    $"Control point count {settings.ControlPointsLocal.Length} does not match grid count {settings.ControlPointCount}.",
                    groupIndex, layerIndex, "_controlPointsLocal");
            }
            Vector3 size = settings.LocalBounds.size;
            Vector3 center = settings.LocalBounds.center;
            if (!IsFinite(size) || !IsFinite(center) || size.x <= 0f || size.y <= 0f || size.z <= 0f)
            {
                Add(results, InvalidBounds, MeshDeformerDiagnosticSeverity.Error, deformer,
                    "Lattice bounds must be finite and have a positive size on every axis.",
                    groupIndex, layerIndex, "_localBounds");
            }
        }

        private static void ValidateOutputName(
            LatticeDeformer deformer,
            List<MeshDeformerDiagnostic> results,
            Dictionary<string, (int group, int layer)> outputNames,
            HashSet<string> sourceNames,
            string name,
            int groupIndex,
            int layerIndex,
            string property)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                Add(results, EmptyBlendShapeName, MeshDeformerDiagnosticSeverity.Error, deformer,
                    "BlendShape output is enabled but its name is empty.", groupIndex, layerIndex, property);
                return;
            }
            if (outputNames.TryGetValue(name, out var first))
            {
                Add(results, DuplicateBlendShapeName, MeshDeformerDiagnosticSeverity.Error, deformer,
                    $"BlendShape output name '{name}' duplicates group {first.group}, layer {first.layer}.",
                    groupIndex, layerIndex, property);
            }
            else
            {
                outputNames.Add(name, (groupIndex, layerIndex));
            }
            if (sourceNames.Contains(name))
            {
                Add(results, ExistingBlendShapeCollision, MeshDeformerDiagnosticSeverity.Warning, deformer,
                    $"BlendShape output name '{name}' already exists on the source mesh; Bake continues and appends another shape with that name.",
                    groupIndex, layerIndex, property);
            }
        }

        private static void ValidateProfile(LatticeDeformer deformer, List<MeshDeformerDiagnostic> results)
        {
            if (deformer.DataSource != DeformerDataSource.Profile) return;
            if (deformer.Profile == null)
            {
                Add(results, ProfileCompatibilityUnknown, MeshDeformerDiagnosticSeverity.Warning, deformer,
                    "Profile data source is selected but no Profile is assigned.", property: "_profile");
                return;
            }
            var status = deformer.EvaluateProfileCompatibility(deformer.Profile);
            if (status == ProfileCompatibilityStatus.TopologyMismatch)
            {
                Add(results, ProfileTopologyMismatch, MeshDeformerDiagnosticSeverity.Error, deformer,
                    "The Profile topology is incompatible with the source mesh. Automatic retargeting is blocked.", property: "_profile");
            }
            else if (status == ProfileCompatibilityStatus.InsufficientMetadata)
            {
                Add(results, ProfileCompatibilityUnknown, MeshDeformerDiagnosticSeverity.Warning, deformer,
                    "The Profile has insufficient compatibility metadata; Bake continues using legacy behavior.", property: "_profile");
            }
        }

        private static void ValidateClearance(
            LatticeDeformer deformer,
            Renderer targetRenderer,
            List<MeshDeformerDiagnostic> results)
        {
            if (!deformer.ShowClearanceHeatmap) return;
            var reference = deformer.ClearanceReferenceRenderer;
            string message = null;
            if (reference == null) message = "Clearance display is enabled but no reference Renderer is assigned.";
            else if (ReferenceEquals(reference, targetRenderer)) message = "The Clearance reference Renderer cannot be the target Renderer itself.";
            else if (!reference.enabled || !reference.gameObject.activeInHierarchy) message = "The Clearance reference Renderer is disabled or inactive.";
            else if (GetRendererMesh(reference) == null) message = "The Clearance reference Renderer has no mesh.";
            if (message != null)
            {
                Add(results, InvalidClearanceReference, MeshDeformerDiagnosticSeverity.Warning, deformer,
                    message + " Deformation Bake continues without relying on the visualization.", property: "_clearanceReferenceRenderer");
            }
        }

        private static void ValidateRestSpace(
            LatticeDeformer deformer,
            Renderer renderer,
            List<MeshDeformerDiagnostic> results)
        {
            if (!SkinnedVertexHelper.StoreMovesInRestSpace || renderer is not SkinnedMeshRenderer) return;
            if (SkinnedVertexHelper.CreateRestSpaceDeltaConverter(deformer) == null)
            {
                Add(results, RestSpaceConversionUnsafe, MeshDeformerDiagnosticSeverity.Warning, deformer,
                    "Rest-space move storage is enabled, but bones, bind poses, or skin weights are insufficient for safe conversion. Unconvertible edits remain in displayed space.",
                    property: "StoreMovesInRestSpace");
            }
        }

        private static Renderer ResolveRenderer(SerializedObject serialized)
        {
            var skinned = serialized.FindProperty("_skinnedMeshRenderer")?.objectReferenceValue as SkinnedMeshRenderer;
            if (skinned != null) return skinned;
            var filter = serialized.FindProperty("_meshFilter")?.objectReferenceValue as MeshFilter;
            return filter != null ? filter.GetComponent<MeshRenderer>() : null;
        }

        private static List<DeformerGroup> GetValidationGroups(LatticeDeformer deformer)
        {
            if (deformer.DataSource == DeformerDataSource.Profile && deformer.Profile != null)
            {
                return deformer.Groups as List<DeformerGroup> ?? deformer.Groups.ToList();
            }
            return s_groupsField?.GetValue(deformer) as List<DeformerGroup>;
        }

        private static Mesh GetRendererMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
            return renderer != null ? renderer.GetComponent<MeshFilter>()?.sharedMesh : null;
        }

        private static int CalculateTopologyHash(Mesh mesh)
        {
            if (mesh == null) return 0;
            try
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + mesh.vertexCount;
                    hash = hash * 31 + mesh.subMeshCount;
                    using Mesh.MeshDataArray meshDataArray = Mesh.AcquireReadOnlyMeshData(mesh);
                    Mesh.MeshData data = meshDataArray[0];
                    bool use16Bit = mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt16;
                    NativeArray<ushort> indices16 = use16Bit
                        ? data.GetIndexData<ushort>()
                        : default;
                    NativeArray<uint> indices32 = !use16Bit
                        ? data.GetIndexData<uint>()
                        : default;
                    for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                    {
                        UnityEngine.Rendering.SubMeshDescriptor descriptor = data.GetSubMesh(subMesh);
                        hash = hash * 31 + (int)descriptor.topology;
                        hash = hash * 31 + descriptor.indexCount;
                        int end = descriptor.indexStart + descriptor.indexCount;
                        for (int index = descriptor.indexStart; index < end; index++)
                        {
                            int value = use16Bit
                                ? indices16[index] + descriptor.baseVertex
                                : unchecked((int)indices32[index]) + descriptor.baseVertex;
                            hash = hash * 31 + value;
                        }
                    }
                    return hash;
                }
            }
            catch
            {
                return 0;
            }
        }

        private static void ResizeLayerArray(
            LatticeDeformer deformer,
            int groupIndex,
            int layerIndex,
            string propertyName,
            int size,
            bool initializeNewMaskValues)
        {
            if (deformer == null) return;
            var serialized = new SerializedObject(deformer);
            var groups = serialized.FindProperty("_groups");
            if (groups == null || groupIndex < 0 || groupIndex >= groups.arraySize) return;
            var layers = groups.GetArrayElementAtIndex(groupIndex).FindPropertyRelative("_layers");
            if (layers == null || layerIndex < 0 || layerIndex >= layers.arraySize) return;
            var array = layers.GetArrayElementAtIndex(layerIndex).FindPropertyRelative(propertyName);
            if (array == null || !array.isArray) return;

            int oldSize = array.arraySize;
            Undo.SetCurrentGroupName(initializeNewMaskValues ? "Resize Vertex Mask" : "Resize Brush Displacements");
            array.arraySize = size;
            if (initializeNewMaskValues)
            {
                for (int i = oldSize; i < size; i++) array.GetArrayElementAtIndex(i).floatValue = 1f;
            }
            serialized.ApplyModifiedProperties();
            deformer.InvalidateCache();
            EditorUtility.SetDirty(deformer);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void Add(
            List<MeshDeformerDiagnostic> results,
            string code,
            MeshDeformerDiagnosticSeverity severity,
            Object target,
            string message,
            int group = -1,
            int layer = -1,
            string property = "",
            string fixLabel = "",
            Action fix = null)
        {
            results.Add(new MeshDeformerDiagnostic(code, severity, target, message,
                group, layer, property, fixLabel, fix));
        }
    }
}
#endif
