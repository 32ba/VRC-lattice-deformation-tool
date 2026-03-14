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
        /// Uses a simple inside/outside test based on closest surface point and normal direction.
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

            var refVertices = referenceMesh.vertices;
            var refNormals = referenceMesh.normals;
            var refTriangles = referenceMesh.triangles;

            if (refVertices == null || refNormals == null || refTriangles == null ||
                refVertices.Length == 0 || refTriangles.Length == 0)
            {
                return penetrating;
            }

            // For each deformed vertex, find the closest point on the reference mesh
            // and check if the vertex is "inside" based on the normal direction
            for (int i = 0; i < deformedVertices.Length; i++)
            {
                Vector3 pointInRefSpace = deformedToReference.MultiplyPoint3x4(deformedVertices[i]);

                // Find closest vertex on reference mesh (simple brute force)
                int closestRefVertex = -1;
                float closestDistSq = float.MaxValue;

                for (int j = 0; j < refVertices.Length; j++)
                {
                    float distSq = (refVertices[j] - pointInRefSpace).sqrMagnitude;
                    if (distSq < closestDistSq)
                    {
                        closestDistSq = distSq;
                        closestRefVertex = j;
                    }
                }

                if (closestRefVertex < 0)
                {
                    continue;
                }

                // Check if point is "inside" the reference mesh
                // by comparing direction to closest vertex with the surface normal
                Vector3 toPoint = pointInRefSpace - refVertices[closestRefVertex];
                float dot = Vector3.Dot(toPoint, refNormals[closestRefVertex]);

                // If dot < 0, the point is on the inside (behind the surface normal)
                // Use a small threshold to avoid false positives on the surface
                if (dot < -0.0001f)
                {
                    penetrating.Add(i);
                }
            }

            return penetrating;
        }
    }
}
#endif
