#if UNITY_EDITOR
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
        private static Mesh s_bakeMesh;

        /// <summary>
        /// Computes world-space positions matching the rendered output.
        /// Returns world-space positions for SkinnedMeshRenderer (via proxy BakeMesh),
        /// or null for MeshRenderer (caller should use localToWorldMatrix).
        /// </summary>
        public static Vector3[] ComputeWorldPositions(LatticeDeformer deformer, Vector3[] localVertices)
        {
            if (deformer == null || localVertices == null || localVertices.Length == 0)
                return null;

            // Try to find the NDMF proxy renderer for this deformer
            var originalRenderer = deformer.GetComponent<Renderer>();
            if (originalRenderer == null)
                return null;

            // Find proxy SkinnedMeshRenderer (NDMF preview creates this)
            SkinnedMeshRenderer proxySMR = null;
            if (NDMFPreviewProxyUtility.TryGetProxyRenderer(originalRenderer, out var proxyRenderer))
            {
                proxySMR = proxyRenderer as SkinnedMeshRenderer;
            }

            // Fall back to original SMR if no proxy
            if (proxySMR == null)
            {
                proxySMR = originalRenderer as SkinnedMeshRenderer;
            }

            if (proxySMR == null || proxySMR.bones == null || proxySMR.bones.Length == 0)
                return null;

            // BakeMesh on the proxy (which has the deformed mesh + correct bones)
            if (s_bakeMesh == null)
            {
                s_bakeMesh = new Mesh();
                s_bakeMesh.hideFlags = HideFlags.HideAndDontSave;
            }

            proxySMR.BakeMesh(s_bakeMesh);

            if (s_bakeMesh.vertexCount != localVertices.Length)
                return null;

            // BakeMesh returns vertices in the SMR's local space (without scale).
            // Convert to world space using the proxy's transform.
            var bakedVerts = s_bakeMesh.vertices;
            var proxyTransform = proxySMR.transform;
            var matrix = proxyTransform.localToWorldMatrix;
            int count = bakedVerts.Length;
            var result = new Vector3[count];

            for (int i = 0; i < count; i++)
            {
                result[i] = matrix.MultiplyPoint3x4(bakedVerts[i]);
            }

            return result;
        }

        /// <summary>
        /// Returns the baked mesh and its transform for raycasting against the posed mesh.
        /// For SkinnedMeshRenderer, bakes the proxy (or original) SMR.
        /// Returns false for MeshRenderer (caller should raycast against source mesh + localToWorldMatrix).
        /// </summary>
        public static bool TryGetBakedMeshForRaycast(LatticeDeformer deformer,
            out Mesh bakedMesh, out Matrix4x4 bakedMeshMatrix)
        {
            bakedMesh = null;
            bakedMeshMatrix = Matrix4x4.identity;

            if (deformer == null)
                return false;

            var originalRenderer = deformer.GetComponent<Renderer>();
            if (originalRenderer == null)
                return false;

            SkinnedMeshRenderer targetSMR = null;
            if (NDMFPreviewProxyUtility.TryGetProxyRenderer(originalRenderer, out var proxyRenderer))
                targetSMR = proxyRenderer as SkinnedMeshRenderer;
            if (targetSMR == null)
                targetSMR = originalRenderer as SkinnedMeshRenderer;
            if (targetSMR == null || targetSMR.bones == null || targetSMR.bones.Length == 0)
                return false;

            if (s_bakeMesh == null)
            {
                s_bakeMesh = new Mesh();
                s_bakeMesh.hideFlags = HideFlags.HideAndDontSave;
            }

            targetSMR.BakeMesh(s_bakeMesh);
            bakedMesh = s_bakeMesh;
            bakedMeshMatrix = targetSMR.transform.localToWorldMatrix;
            return true;
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
