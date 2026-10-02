#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal sealed class InspectorValidationState
    {
        private IReadOnlyList<MeshDeformerDiagnostic> _cachedValidationDiagnostics;
        private int _cachedValidationStateHash;
        private bool _hasCachedValidationState;
        internal void Invalidate()
        {
            _cachedValidationDiagnostics = null;
            _hasCachedValidationState = false;
        }
        internal IReadOnlyList<MeshDeformerDiagnostic> Read(
            LatticeDeformer deformer)
        {
            int validationStateHash = ComputeValidationStateHash(deformer);
            if (!_hasCachedValidationState ||
                validationStateHash != _cachedValidationStateHash ||
                _cachedValidationDiagnostics == null)
            {
                _cachedValidationDiagnostics = MeshDeformerValidator.Validate(deformer);
                _cachedValidationStateHash = validationStateHash;
                _hasCachedValidationState = true;
            }
            return _cachedValidationDiagnostics;
        }

        internal static int ComputeValidationStateHash(LatticeDeformer deformer)
        {
            if (deformer == null) return 0;
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + deformer.GetInstanceID();
                hash = hash * 31 + EditorUtility.GetDirtyCount(deformer);
                hash = hash * 31 + MeshDeformerValidator.ComputeInspectorStructureHash(deformer);
                hash = hash * 31 + deformer.enabled.GetHashCode();
                Mesh source = deformer.SourceMesh;
                hash = hash * 31 + (source != null ? source.GetInstanceID() : 0);
                hash = hash * 31 + (source != null ? EditorUtility.GetDirtyCount(source) : 0);
                MeshDeformerProfile profile = deformer.Profile;
                hash = hash * 31 + (profile != null ? profile.GetInstanceID() : 0);
                hash = hash * 31 + (profile != null ? EditorUtility.GetDirtyCount(profile) : 0);
                MeshCompatibilityMetadata compatibility = profile?.Compatibility;
                if (compatibility != null)
                {
                    hash = hash * 31 + compatibility.VertexCount;
                    hash = hash * 31 + compatibility.IndexCount.GetHashCode();
                    hash = hash * 31 + compatibility.TriangleCount.GetHashCode();
                    hash = hash * 31 + compatibility.SubMeshCount;
                    hash = hash * 31 + compatibility.BindPoseCount;
                    hash = hash * 31 + StringComparer.Ordinal.GetHashCode(
                        compatibility.BlendShapeSignature);
                    hash = hash * 31 + StringComparer.Ordinal.GetHashCode(
                        compatibility.TopologyHash);
                    hash = hash * 31 + StringComparer.Ordinal.GetHashCode(
                        compatibility.SourceAssetGuid);
                    hash = hash * 31 + compatibility.SourceAssetLocalId.GetHashCode();
                }
                hash = hash * 31 + SkinnedVertexHelper.StoreMovesInRestSpace.GetHashCode();
                Renderer targetRenderer = deformer.TargetRenderer;
                if (ClearanceQueryCache.TryGetRendererLightweightStateHash(targetRenderer, out int targetHash))
                    hash = hash * 31 + targetHash;
                Renderer reference = deformer.ClearanceReferenceRenderer;
                if (ClearanceQueryCache.TryGetRendererLightweightStateHash(reference, out int referenceHash))
                    hash = hash * 31 + referenceHash;
                return hash;
            }
        }

    }
}
#endif
