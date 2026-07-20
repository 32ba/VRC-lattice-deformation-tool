#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Detects vertices that penetrate through a reference mesh.
    /// Used to highlight clipping issues during brush editing.
    /// </summary>
    internal static class PenetrationDetector
    {
        /// <summary>
        /// Finds vertices of the deformed mesh that are inside (penetrating) the reference mesh.
        /// Uses the shared triangle-surface clearance query and reference-normal sign mode.
        /// </summary>
        /// <param name="deformedVertices">Vertices of the deformed mesh (local space).</param>
        /// <param name="referenceMesh">The reference mesh to check against.</param>
        /// <param name="deformedToReference">Transform from deformed mesh space to reference mesh space.</param>
        /// <returns>Set of vertex indices that are penetrating the reference mesh.</returns>
        public static HashSet<int> DetectPenetration(
            Vector3[] deformedVertices,
            Mesh referenceMesh,
            Matrix4x4 deformedToReference)
        {
            var penetrating = new HashSet<int>();

            if (deformedVertices == null || !HasCompleteNormals(referenceMesh))
            {
                return penetrating;
            }

            if (!ClearanceQuery.TryCreate(referenceMesh, Matrix4x4.identity, out var query))
            {
                return penetrating;
            }

            var results = new ClearanceQueryResult[deformedVertices.Length];
            query.QueryPoints(
                deformedVertices,
                deformedToReference,
                ClearanceSignMode.ReferenceNormal,
                results);
            for (int i = 0; i < results.Length; i++)
            {
                ClearanceQueryResult result = results[i];
                if (result.IsValid && result.SignedClearance < -0.0001f)
                {
                    penetrating.Add(i);
                }
            }

            return penetrating;
        }

        public static HashSet<int> DetectPenetration(
            Vector3[] deformedVertices,
            Matrix4x4 deformedLocalToWorld,
            Renderer referenceRenderer,
            ClearanceSignMode signMode = ClearanceSignMode.ReferenceNormal)
        {
            var penetrating = new HashSet<int>();
            Mesh referenceMesh = null;
            if (referenceRenderer is SkinnedMeshRenderer skinned)
            {
                referenceMesh = skinned.sharedMesh;
            }
            else if (referenceRenderer is MeshRenderer meshRenderer)
            {
                var filter = meshRenderer.GetComponent<MeshFilter>();
                referenceMesh = filter != null ? filter.sharedMesh : null;
            }

            // Preserve the legacy penetration detector contract: a reference without
            // authored per-vertex normals is not classified by inferred face normals.
            if (!HasCompleteNormals(referenceMesh)) return penetrating;

            ClearanceQueryResult[] results = ClearanceQueryCache.QueryPoints(
                referenceRenderer,
                deformedVertices,
                deformedLocalToWorld,
                signMode);
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i].IsValid && results[i].SignedClearance < -0.0001f)
                    penetrating.Add(i);
            }
            return penetrating;
        }

        private static bool HasCompleteNormals(Mesh mesh)
        {
            if (mesh == null || mesh.vertexCount == 0) return false;
            try
            {
                Vector3[] normals = mesh.normals;
                return normals != null && normals.Length == mesh.vertexCount;
            }
            catch (UnityException)
            {
                return false;
            }
        }
    }
}
#endif
