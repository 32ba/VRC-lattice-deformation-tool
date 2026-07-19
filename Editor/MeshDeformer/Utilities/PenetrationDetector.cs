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

            if (deformedVertices == null || referenceMesh == null)
            {
                return penetrating;
            }

            if (!ClearanceQuery.TryCreate(referenceMesh, Matrix4x4.identity, out var query))
            {
                return penetrating;
            }

            for (int i = 0; i < deformedVertices.Length; i++)
            {
                Vector3 pointInRefSpace = deformedToReference.MultiplyPoint3x4(deformedVertices[i]);
                ClearanceQueryResult result = query.QueryPoint(
                    pointInRefSpace,
                    ClearanceSignMode.ReferenceNormal);
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
    }
}
#endif
