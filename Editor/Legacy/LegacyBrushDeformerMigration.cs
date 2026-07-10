#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Converts the serialized payload of the legacy standalone brush component into a
    /// regular Mesh Deformer brush layer. The legacy component is retained, disabled,
    /// and can therefore still serve as a lossless backup.
    /// </summary>
    internal static class LegacyBrushDeformerMigration
    {
        internal const string MigratedGroupName = "Legacy Brush Migration";
        internal const string MigratedLayerName = "Migrated Legacy Brush";

        internal static bool TryMigrate(
            BrushDeformer legacy,
            out LatticeDeformer target,
            out string error)
        {
            target = null;
            error = null;

            if (legacy == null)
            {
                error = "The legacy Brush Deformer is missing.";
                return false;
            }

            var existingTarget = legacy.GetComponent<LatticeDeformer>();
            var displacements = legacy.Displacements ?? Array.Empty<Vector3>();

            // A completed migration remains successful even if the original source is
            // subsequently removed. This makes the operation safely idempotent.
            if (!legacy.enabled &&
                existingTarget != null &&
                TryFindMigratedLayer(existingTarget, displacements, requireMarkerName: false, out _))
            {
                target = existingTarget;
                return true;
            }

            var legacySerialized = new SerializedObject(legacy);
            legacySerialized.UpdateIfRequiredOrScript();

            var skinnedRenderer = ReadObjectReference<SkinnedMeshRenderer>(legacySerialized, "_skinnedMeshRenderer");
            var meshFilter = ReadObjectReference<MeshFilter>(legacySerialized, "_meshFilter");
            if (skinnedRenderer == null && meshFilter == null)
            {
                // Match the legacy component's Reset behavior without changing it during validation.
                skinnedRenderer = legacy.GetComponent<SkinnedMeshRenderer>();
                meshFilter = skinnedRenderer == null ? legacy.GetComponent<MeshFilter>() : null;
            }

            var sourceMesh = legacy.SourceMesh;
            if (sourceMesh == null)
            {
                sourceMesh = skinnedRenderer != null ? skinnedRenderer.sharedMesh : meshFilter != null ? meshFilter.sharedMesh : null;
            }

            if (sourceMesh == null)
            {
                error = "No source mesh is assigned to the legacy Brush Deformer.";
                return false;
            }

            if (displacements.Length != sourceMesh.vertexCount)
            {
                error = $"The displacement count ({displacements.Length}) does not match the source vertex count ({sourceMesh.vertexCount}).";
                return false;
            }

            if (existingTarget != null)
            {
                var existingSource = ResolveKnownTargetSource(existingTarget);
                if (existingSource != null && !ReferenceEquals(existingSource, sourceMesh))
                {
                    error = "The existing Mesh Deformer uses a different source mesh.";
                    return false;
                }

                if (!HasEquivalentComponentSettings(legacySerialized, existingTarget))
                {
                    error = "The existing Mesh Deformer uses different rebuild or weight-transfer settings. Review the settings manually before merging.";
                    return false;
                }
            }

            if (!ValidateBrushOutput(sourceMesh, displacements, out error))
            {
                return false;
            }

            bool alreadyHasLayer = existingTarget != null &&
                TryFindMigratedLayer(existingTarget, displacements, requireMarkerName: true, out _);

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Migrate Legacy Brush Deformer");

            try
            {
                Undo.RecordObject(legacy, "Migrate Legacy Brush Deformer");
                if (skinnedRenderer != null)
                {
                    Undo.RecordObject(skinnedRenderer, "Migrate Legacy Brush Deformer");
                }
                if (meshFilter != null)
                {
                    Undo.RecordObject(meshFilter, "Migrate Legacy Brush Deformer");
                }

                if (skinnedRenderer != null)
                {
                    skinnedRenderer.sharedMesh = sourceMesh;
                }
                if (meshFilter != null)
                {
                    meshFilter.sharedMesh = sourceMesh;
                }

                bool createdTarget = existingTarget == null;
                target = createdTarget
                    ? Undo.AddComponent<LatticeDeformer>(legacy.gameObject)
                    : existingTarget;
                Undo.RecordObject(target, "Migrate Legacy Brush Deformer");

                if (createdTarget)
                {
                    CopyComponentSettings(
                        legacySerialized,
                        target,
                        skinnedRenderer,
                        meshFilter,
                        sourceMesh);

                    // Let the new component establish source bounds before displacement data
                    // is installed; its first source initialization clears brush layers.
                    if (target.Deform(false) == null)
                    {
                        throw new InvalidOperationException("Failed to initialize the Mesh Deformer source mesh.");
                    }
                }
                else
                {
                    // Existing deformation stacks must never be reset while merging the
                    // legacy layer, even when their lazy source cache has not run yet.
                    SetBoolean(target, "_hasInitializedFromSource", true);
                }

                if (!alreadyHasLayer)
                {
                    int previousActiveGroup = target.ActiveGroupIndex;
                    int migrationGroup = target.AddGroup(MigratedGroupName);
                    target.ActiveGroupIndex = migrationGroup;
                    int layerIndex = target.AddLayer(MigratedLayerName, MeshDeformerLayerType.Brush);
                    if (layerIndex < 0 || target.ActiveGroup == null)
                    {
                        throw new InvalidOperationException("Failed to create the migrated brush layer.");
                    }

                    var layer = target.ActiveGroup.LayersList[layerIndex];
                    layer.BrushDisplacements = CloneBitExact(displacements);
                    layer.Enabled = true;
                    layer.Weight = 1f;

                    target.ActiveGroupIndex = Mathf.Clamp(previousActiveGroup, 0, target.GroupCount - 1);
                }

                if (!TryFindMigratedLayer(target, displacements, requireMarkerName: false, out var migratedLayer))
                {
                    throw new InvalidOperationException("The migrated displacement payload could not be verified.");
                }

                var migratedGroup = FindContainingGroup(target, migratedLayer);
                string targetValidationError = null;
                if (migratedGroup == null ||
                    !ValidateTargetBrushOutput(
                        target,
                        migratedGroup,
                        migratedLayer,
                        skinnedRenderer,
                        sourceMesh,
                        displacements,
                        out targetValidationError))
                {
                    throw new InvalidOperationException(targetValidationError ?? "The migrated target output could not be verified.");
                }

                target.InvalidateCache();

                // Disabling invokes the legacy component's normal source-mesh restoration.
                // The component itself is intentionally retained as a serialized backup.
                legacy.enabled = false;

                MarkMigrationDirty(legacy, target);
                Undo.CollapseUndoOperations(undoGroup);
                return true;
            }
            catch (Exception ex)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                target = null;
                error = ex.Message;
                return false;
            }
        }

        private static void CopyComponentSettings(
            SerializedObject legacySerialized,
            LatticeDeformer target,
            SkinnedMeshRenderer skinnedRenderer,
            MeshFilter meshFilter,
            Mesh sourceMesh)
        {
            var targetSerialized = new SerializedObject(target);
            targetSerialized.UpdateIfRequiredOrScript();

            SetObjectReference(targetSerialized, "_skinnedMeshRenderer", skinnedRenderer);
            SetObjectReference(targetSerialized, "_meshFilter", meshFilter);
            SetObjectReference(targetSerialized, "_serializedSourceMesh", sourceMesh);
            CopyBoolean(legacySerialized, targetSerialized, "_recalculateNormals");
            CopyBoolean(legacySerialized, targetSerialized, "_recalculateTangents");
            CopyBoolean(legacySerialized, targetSerialized, "_recalculateBounds");
            CopyBoolean(legacySerialized, targetSerialized, "_recalculateBoneWeights");
            targetSerialized.ApplyModifiedProperties();

            target.WeightTransferSettings = ReadWeightTransferSettings(legacySerialized);
        }

        private static WeightTransferSettingsData ReadWeightTransferSettings(SerializedObject serialized)
        {
            var result = new WeightTransferSettingsData();
            var settings = serialized.FindProperty("_weightTransferSettings");
            if (settings == null)
            {
                return result;
            }

            ReadFloat(settings, "maxTransferDistance", value => result.maxTransferDistance = value);
            ReadFloat(settings, "normalAngleThreshold", value => result.normalAngleThreshold = value);
            ReadBool(settings, "enableInpainting", value => result.enableInpainting = value);
            ReadInt(settings, "maxIterations", value => result.maxIterations = value);
            ReadFloat(settings, "tolerance", value => result.tolerance = value);
            return result;
        }

        private static Mesh ResolveKnownTargetSource(LatticeDeformer target)
        {
            if (target == null)
            {
                return null;
            }

            var serialized = new SerializedObject(target);
            serialized.UpdateIfRequiredOrScript();

            var serializedSource = ReadObjectReference<Mesh>(serialized, "_serializedSourceMesh");
            if (serializedSource != null)
            {
                return serializedSource;
            }

            var serializedSkinned = ReadObjectReference<SkinnedMeshRenderer>(serialized, "_skinnedMeshRenderer");
            if (serializedSkinned != null && serializedSkinned.sharedMesh != null)
            {
                return serializedSkinned.sharedMesh;
            }

            var serializedFilter = ReadObjectReference<MeshFilter>(serialized, "_meshFilter");
            if (serializedFilter != null && serializedFilter.sharedMesh != null)
            {
                return serializedFilter.sharedMesh;
            }

            var attachedSkinned = target.GetComponent<SkinnedMeshRenderer>();
            if (attachedSkinned != null && attachedSkinned.sharedMesh != null)
            {
                return attachedSkinned.sharedMesh;
            }

            var attachedFilter = target.GetComponent<MeshFilter>();
            return attachedFilter != null ? attachedFilter.sharedMesh : null;
        }

        private static bool HasEquivalentComponentSettings(
            SerializedObject legacySerialized,
            LatticeDeformer existingTarget)
        {
            if (legacySerialized == null || existingTarget == null)
            {
                return false;
            }

            var targetSerialized = new SerializedObject(existingTarget);
            targetSerialized.UpdateIfRequiredOrScript();

            string[] booleanSettings =
            {
                "_recalculateNormals",
                "_recalculateTangents",
                "_recalculateBounds",
                "_recalculateBoneWeights"
            };

            foreach (string propertyName in booleanSettings)
            {
                var legacyProperty = legacySerialized.FindProperty(propertyName);
                var targetProperty = targetSerialized.FindProperty(propertyName);
                if (legacyProperty == null || targetProperty == null ||
                    legacyProperty.boolValue != targetProperty.boolValue)
                {
                    return false;
                }
            }

            var legacyWeights = legacySerialized.FindProperty("_weightTransferSettings");
            var targetWeights = targetSerialized.FindProperty("_weightTransferSettings");
            if (legacyWeights == null || targetWeights == null)
            {
                return false;
            }

            return HaveSameFloat(legacyWeights, targetWeights, "maxTransferDistance") &&
                   HaveSameFloat(legacyWeights, targetWeights, "normalAngleThreshold") &&
                   HaveSameBool(legacyWeights, targetWeights, "enableInpainting") &&
                   HaveSameInt(legacyWeights, targetWeights, "maxIterations") &&
                   HaveSameFloat(legacyWeights, targetWeights, "tolerance");
        }

        private static bool HaveSameFloat(SerializedProperty left, SerializedProperty right, string name)
        {
            var leftValue = left.FindPropertyRelative(name);
            var rightValue = right.FindPropertyRelative(name);
            return leftValue != null && rightValue != null &&
                   BitConverter.SingleToInt32Bits(leftValue.floatValue) ==
                   BitConverter.SingleToInt32Bits(rightValue.floatValue);
        }

        private static bool HaveSameBool(SerializedProperty left, SerializedProperty right, string name)
        {
            var leftValue = left.FindPropertyRelative(name);
            var rightValue = right.FindPropertyRelative(name);
            return leftValue != null && rightValue != null &&
                   leftValue.boolValue == rightValue.boolValue;
        }

        private static bool HaveSameInt(SerializedProperty left, SerializedProperty right, string name)
        {
            var leftValue = left.FindPropertyRelative(name);
            var rightValue = right.FindPropertyRelative(name);
            return leftValue != null && rightValue != null &&
                   leftValue.intValue == rightValue.intValue;
        }

        private static bool TryFindMigratedLayer(
            LatticeDeformer target,
            Vector3[] expected,
            bool requireMarkerName,
            out LatticeLayer result)
        {
            result = null;
            if (target == null)
            {
                return false;
            }

            foreach (var group in target.Groups)
            {
                if (group == null)
                {
                    continue;
                }

                foreach (var layer in group.Layers)
                {
                    if (layer == null || layer.Type != MeshDeformerLayerType.Brush)
                    {
                        continue;
                    }
                    if (requireMarkerName && !string.Equals(layer.Name, MigratedLayerName, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (!AreBitExact(layer.BrushDisplacements, expected))
                    {
                        continue;
                    }

                    result = layer;
                    return true;
                }
            }

            return false;
        }

        private static DeformerGroup FindContainingGroup(LatticeDeformer target, LatticeLayer expectedLayer)
        {
            if (target == null || expectedLayer == null)
            {
                return null;
            }

            foreach (var group in target.Groups)
            {
                if (group == null) continue;
                foreach (var layer in group.Layers)
                {
                    if (ReferenceEquals(layer, expectedLayer)) return group;
                }
            }

            return null;
        }

        private static bool ValidateTargetBrushOutput(
            LatticeDeformer target,
            DeformerGroup migratedGroup,
            LatticeLayer migratedLayer,
            SkinnedMeshRenderer skinnedRenderer,
            Mesh source,
            Vector3[] displacements,
            out string error)
        {
            error = null;
            var groupStates = new List<GroupValidationState>();
            var layerStates = new List<LayerValidationState>();
            float[] blendShapeWeights = null;

            try
            {
                foreach (var group in target.Groups)
                {
                    if (group == null) continue;
                    groupStates.Add(new GroupValidationState(group));
                    foreach (var layer in group.Layers)
                    {
                        if (layer != null) layerStates.Add(new LayerValidationState(layer));
                    }
                    group.Enabled = false;
                }

                migratedGroup.Enabled = true;
                migratedGroup.BlendShapeOutput = BlendShapeOutputMode.Disabled;
                foreach (var layer in migratedGroup.Layers)
                {
                    if (layer != null) layer.Enabled = false;
                }
                migratedLayer.Enabled = true;
                migratedLayer.Weight = 1f;
                migratedLayer.BlendShapeOutput = BlendShapeOutputMode.Disabled;

                if (skinnedRenderer != null && source.blendShapeCount > 0)
                {
                    blendShapeWeights = new float[source.blendShapeCount];
                    for (int i = 0; i < blendShapeWeights.Length; i++)
                    {
                        blendShapeWeights[i] = skinnedRenderer.GetBlendShapeWeight(i);
                        skinnedRenderer.SetBlendShapeWeight(i, 0f);
                    }
                }

                target.InvalidateCache();
                var output = target.Deform(false);
                if (output == null)
                {
                    error = "The migrated Mesh Deformer did not produce an output mesh.";
                    return false;
                }

                var sourceVertices = source.vertices;
                var outputVertices = output.vertices;
                if (outputVertices.Length != sourceVertices.Length)
                {
                    error = "The migrated Mesh Deformer changed the output vertex count.";
                    return false;
                }

                for (int i = 0; i < sourceVertices.Length; i++)
                {
                    if (!AreApproximatelyEqual(sourceVertices[i] + displacements[i], outputVertices[i]))
                    {
                        error = $"The migrated Mesh Deformer output differs at vertex {i}.";
                        return false;
                    }
                }

                return true;
            }
            finally
            {
                for (int i = 0; i < groupStates.Count; i++) groupStates[i].Restore();
                for (int i = 0; i < layerStates.Count; i++) layerStates[i].Restore();

                if (blendShapeWeights != null && skinnedRenderer != null)
                {
                    for (int i = 0; i < blendShapeWeights.Length; i++)
                    {
                        skinnedRenderer.SetBlendShapeWeight(i, blendShapeWeights[i]);
                    }
                }

                target.InvalidateCache();
                target.Deform(false);
            }
        }

        private static bool ValidateBrushOutput(Mesh source, Vector3[] displacements, out string error)
        {
            error = null;
            GameObject validationObject = null;
            try
            {
                validationObject = new GameObject("Legacy Brush Migration Validation")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                var filter = validationObject.AddComponent<MeshFilter>();
                filter.sharedMesh = source;

                var deformer = validationObject.AddComponent<LatticeDeformer>();
                var serialized = new SerializedObject(deformer);
                serialized.UpdateIfRequiredOrScript();
                SetObjectReference(serialized, "_meshFilter", filter);
                SetObjectReference(serialized, "_serializedSourceMesh", source);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                if (deformer.Deform(false) == null)
                {
                    error = "The current Mesh Deformer could not initialize the source mesh.";
                    return false;
                }

                int layerIndex = deformer.AddLayer(MigratedLayerName, MeshDeformerLayerType.Brush);
                if (layerIndex < 0 || deformer.ActiveGroup == null)
                {
                    error = "The current Mesh Deformer could not create a brush layer.";
                    return false;
                }

                deformer.ActiveGroup.LayersList[layerIndex].BrushDisplacements = CloneBitExact(displacements);
                var result = deformer.Deform(false);
                if (result == null)
                {
                    error = "The current Mesh Deformer could not evaluate the migrated brush layer.";
                    return false;
                }

                var sourceVertices = source.vertices;
                var resultVertices = result.vertices;
                if (resultVertices.Length != sourceVertices.Length)
                {
                    error = "The migrated output vertex count changed during validation.";
                    return false;
                }

                for (int i = 0; i < sourceVertices.Length; i++)
                {
                    var expected = sourceVertices[i] + displacements[i];
                    if (!AreApproximatelyEqual(expected, resultVertices[i]))
                    {
                        error = $"The migrated output differs from the legacy output at vertex {i}.";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Migration validation failed: {ex.Message}";
                return false;
            }
            finally
            {
                if (validationObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(validationObject);
                }
            }
        }

        private static void MarkMigrationDirty(BrushDeformer legacy, LatticeDeformer target)
        {
            EditorUtility.SetDirty(legacy);
            EditorUtility.SetDirty(target);
            PrefabUtility.RecordPrefabInstancePropertyModifications(legacy);
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);

            var scene = legacy.gameObject.scene;
            if (scene.IsValid() && scene.isLoaded)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }
        }

        private static T ReadObjectReference<T>(SerializedObject serialized, string propertyName)
            where T : UnityEngine.Object
        {
            return serialized.FindProperty(propertyName)?.objectReferenceValue as T;
        }

        private static void SetObjectReference(SerializedObject serialized, string propertyName, UnityEngine.Object value)
        {
            var property = serialized.FindProperty(propertyName);
            if (property != null)
            {
                property.objectReferenceValue = value;
            }
        }

        private static void CopyBoolean(SerializedObject source, SerializedObject destination, string propertyName)
        {
            var sourceProperty = source.FindProperty(propertyName);
            var destinationProperty = destination.FindProperty(propertyName);
            if (sourceProperty != null && destinationProperty != null)
            {
                destinationProperty.boolValue = sourceProperty.boolValue;
            }
        }

        private static void SetBoolean(UnityEngine.Object target, string propertyName, bool value)
        {
            var serialized = new SerializedObject(target);
            serialized.UpdateIfRequiredOrScript();
            var property = serialized.FindProperty(propertyName);
            if (property != null)
            {
                property.boolValue = value;
                serialized.ApplyModifiedProperties();
            }
        }

        private static void ReadFloat(SerializedProperty parent, string name, Action<float> setter)
        {
            var property = parent.FindPropertyRelative(name);
            if (property != null) setter(property.floatValue);
        }

        private static void ReadBool(SerializedProperty parent, string name, Action<bool> setter)
        {
            var property = parent.FindPropertyRelative(name);
            if (property != null) setter(property.boolValue);
        }

        private static void ReadInt(SerializedProperty parent, string name, Action<int> setter)
        {
            var property = parent.FindPropertyRelative(name);
            if (property != null) setter(property.intValue);
        }

        private static Vector3[] CloneBitExact(Vector3[] source)
        {
            var clone = new Vector3[source.Length];
            Array.Copy(source, clone, source.Length);
            return clone;
        }

        private static bool AreBitExact(Vector3[] left, Vector3[] right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++)
            {
                if (!AreBitExact(left[i], right[i])) return false;
            }
            return true;
        }

        private static bool AreBitExact(Vector3 left, Vector3 right)
        {
            return BitConverter.SingleToInt32Bits(left.x) == BitConverter.SingleToInt32Bits(right.x) &&
                   BitConverter.SingleToInt32Bits(left.y) == BitConverter.SingleToInt32Bits(right.y) &&
                   BitConverter.SingleToInt32Bits(left.z) == BitConverter.SingleToInt32Bits(right.z);
        }

        private static bool AreApproximatelyEqual(Vector3 left, Vector3 right)
        {
            const float tolerance = 1e-6f;
            return Mathf.Abs(left.x - right.x) <= tolerance &&
                   Mathf.Abs(left.y - right.y) <= tolerance &&
                   Mathf.Abs(left.z - right.z) <= tolerance;
        }

        private readonly struct GroupValidationState
        {
            private readonly DeformerGroup _group;
            private readonly bool _enabled;
            private readonly BlendShapeOutputMode _blendShapeOutput;

            internal GroupValidationState(DeformerGroup group)
            {
                _group = group;
                _enabled = group.Enabled;
                _blendShapeOutput = group.BlendShapeOutput;
            }

            internal void Restore()
            {
                _group.Enabled = _enabled;
                _group.BlendShapeOutput = _blendShapeOutput;
            }
        }

        private readonly struct LayerValidationState
        {
            private readonly LatticeLayer _layer;
            private readonly bool _enabled;
            private readonly float _weight;
            private readonly BlendShapeOutputMode _blendShapeOutput;

            internal LayerValidationState(LatticeLayer layer)
            {
                _layer = layer;
                _enabled = layer.Enabled;
                _weight = layer.Weight;
                _blendShapeOutput = layer.BlendShapeOutput;
            }

            internal void Restore()
            {
                _layer.Enabled = _enabled;
                _layer.Weight = _weight;
                _layer.BlendShapeOutput = _blendShapeOutput;
            }
        }
    }
}
#endif
