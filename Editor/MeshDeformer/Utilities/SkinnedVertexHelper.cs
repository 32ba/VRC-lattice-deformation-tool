#if UNITY_EDITOR
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Computes world-space vertex positions that match the visual rendering.
    /// For SkinnedMeshRenderer, uses BakeMesh on the NDMF preview proxy renderer
    /// (which has the deformed mesh + bone skinning applied).
    /// </summary>
    internal static class SkinnedVertexHelper
    {
        // Compatibility entry points retain their historical shared lifetime. Scene
        // tools use their own SkinnedPoseSnapshot instead of borrowing this instance.
        private static readonly SkinnedPoseSnapshot s_compatibilitySnapshot =
            new SkinnedPoseSnapshot("Compatibility Posed Surface");
        internal static int WorldPositionBakeCountForTests
        {
            get => SkinnedPoseSnapshot.BakeCountForTests;
            set => SkinnedPoseSnapshot.BakeCountForTests = value;
        }

        [ExcludeFromCodeCoverage]
        internal static void ReleaseStaticResources() => s_compatibilitySnapshot.Reset();

        /// <summary>Returns posed world positions, or null for an unskinned/invalid target.</summary>
        [ExcludeFromCodeCoverage]
        public static Vector3[] ComputeWorldPositions(
            LatticeDeformer deformer,
            Vector3[] localVertices,
            Vector3[] reusableResult = null)
        {
            if (deformer == null || localVertices == null || localVertices.Length == 0 ||
                !TryGetSkinnedRenderer(deformer, out var renderer))
                return null;

            UnityEngine.Profiling.Profiler.BeginSample("VertexSelection.BakeMesh");
            try
            {
                return s_compatibilitySnapshot.TryCapture(renderer, localVertices.Length)
                    ? s_compatibilitySnapshot.CopyWorldPositions(reusableResult)
                    : null;
            }
            finally
            {
                UnityEngine.Profiling.Profiler.EndSample();
            }
        }

        [ExcludeFromCodeCoverage]
        internal static bool TryCaptureBrushSnapshot(
            LatticeDeformer deformer,
            Vector3[] localVertices,
            Vector3[] reusableWorldPositions,
            out Vector3[] worldPositions,
            out Mesh bakedMesh,
            out Matrix4x4 bakedMeshMatrix)
        {
            worldPositions = null;
            bakedMesh = null;
            bakedMeshMatrix = Matrix4x4.identity;
            if (deformer == null || !TryGetSkinnedRendererReference(deformer, out var renderer))
                return false;
            return TryCaptureBrushSnapshot(renderer, localVertices, reusableWorldPositions,
                out worldPositions, out bakedMesh, out bakedMeshMatrix);
        }

        [ExcludeFromCodeCoverage]
        internal static bool TryCaptureBrushSnapshot(
            SkinnedMeshRenderer renderer,
            Vector3[] localVertices,
            Vector3[] reusableWorldPositions,
            out Vector3[] worldPositions,
            out Mesh bakedMesh,
            out Matrix4x4 bakedMeshMatrix)
        {
            worldPositions = null;
            bakedMesh = null;
            bakedMeshMatrix = Matrix4x4.identity;
            if (localVertices == null ||
                !s_compatibilitySnapshot.TryCapture(renderer, localVertices.Length))
                return false;
            worldPositions = s_compatibilitySnapshot.CopyWorldPositions(reusableWorldPositions);
            bakedMesh = s_compatibilitySnapshot.Mesh;
            bakedMeshMatrix = s_compatibilitySnapshot.LocalToWorld;
            return true;
        }

        [ExcludeFromCodeCoverage]
        public static bool TryGetBakedMeshForRaycast(LatticeDeformer deformer,
            out Mesh bakedMesh, out Matrix4x4 bakedMeshMatrix)
        {
            bakedMesh = null;
            bakedMeshMatrix = Matrix4x4.identity;
            if (deformer == null || !TryGetSkinnedRenderer(deformer, out var renderer) ||
                !s_compatibilitySnapshot.TryCapture(renderer, renderer.sharedMesh.vertexCount))
                return false;
            bakedMesh = s_compatibilitySnapshot.Mesh;
            bakedMeshMatrix = s_compatibilitySnapshot.LocalToWorld;
            return true;
        }

        private static bool TryGetSkinnedRenderer(
            LatticeDeformer deformer,
            out SkinnedMeshRenderer skinnedRenderer)
        {
            return TryGetSkinnedRendererReference(deformer, out skinnedRenderer) &&
                   skinnedRenderer.bones != null &&
                   skinnedRenderer.bones.Length > 0;
        }

        private static bool TryGetSkinnedRendererReference(
            LatticeDeformer deformer,
            out SkinnedMeshRenderer skinnedRenderer)
        {
            skinnedRenderer = null;
            if (deformer == null) return false;

            var originalRenderer = deformer.GetComponent<Renderer>();
            if (originalRenderer == null) return false;

            if (NDMFPreviewProxyUtility.TryGetProxyRenderer(originalRenderer, out var proxyRenderer))
                skinnedRenderer = proxyRenderer as SkinnedMeshRenderer;
            if (skinnedRenderer == null)
                skinnedRenderer = originalRenderer as SkinnedMeshRenderer;
            return skinnedRenderer != null;
        }

        internal static int ComputePoseStateHash(
            SkinnedMeshRenderer renderer,
            Transform[] bones)
        {
            if (renderer == null) return 0;

            unchecked
            {
                int hash = 17;
                hash = hash * 31 + renderer.GetInstanceID();
                hash = hash * 31 + renderer.transform.localToWorldMatrix.GetHashCode();
                hash = hash * 31 + (renderer.sharedMesh != null
                    ? renderer.sharedMesh.GetInstanceID()
                    : 0);
                hash = hash * 31 + (renderer.sharedMesh != null
                    ? EditorUtility.GetDirtyCount(renderer.sharedMesh)
                    : 0);
                hash = hash * 31 + (renderer.sharedMesh != null
                    ? renderer.sharedMesh.bounds.GetHashCode()
                    : 0);
                hash = hash * 31 + (renderer.rootBone != null
                    ? renderer.rootBone.localToWorldMatrix.GetHashCode()
                    : 0);

                int boneCount = bones?.Length ?? 0;
                hash = hash * 31 + boneCount;
                for (int i = 0; i < boneCount; i++)
                {
                    Transform bone = bones[i];
                    hash = hash * 31 + (bone != null
                        ? bone.localToWorldMatrix.GetHashCode()
                        : 0);
                }

                int blendShapeCount = renderer.sharedMesh != null
                    ? renderer.sharedMesh.blendShapeCount
                    : 0;
                hash = hash * 31 + blendShapeCount;
                for (int i = 0; i < blendShapeCount; i++)
                    hash = hash * 31 + renderer.GetBlendShapeWeight(i).GetHashCode();
                return hash;
            }
        }

        /// <summary>
        /// Converts a local-space vertex position to world space,
        /// using the pre-computed world positions if available (SkinnedMeshRenderer),
        /// or localToWorldMatrix otherwise (MeshRenderer).
        /// </summary>
        public static Vector3 LocalToWorld(int vertexIndex, Vector3[] worldPositions,
            Vector3[] localVertices, Matrix4x4 localToWorld)
        {
            if (worldPositions != null && vertexIndex >= 0 && vertexIndex < worldPositions.Length)
                return worldPositions[vertexIndex];

            if (localVertices != null && vertexIndex >= 0 && vertexIndex < localVertices.Length)
                return localToWorld.MultiplyPoint3x4(localVertices[vertexIndex]);

            return Vector3.zero;
        }

        /// <summary>
        /// Converts a local-space vertex to world space using pre-computed world positions
        /// or falls back to localToWorldMatrix. Use this when you already have the local vertex value.
        /// </summary>
        public static Vector3 LocalToWorld(int vertexIndex, Vector3[] worldPositions,
            Vector3 localVertex, Matrix4x4 localToWorld)
        {
            if (worldPositions != null && vertexIndex >= 0 && vertexIndex < worldPositions.Length)
                return worldPositions[vertexIndex];

            return localToWorld.MultiplyPoint3x4(localVertex);
        }
    }
}
#endif
