#if UNITY_EDITOR
using System;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Supplies editor visualization normals without modifying the imported source mesh.
    /// Calling Mesh.RecalculateNormals directly on a shared asset can dirty or persist
    /// generated normals that were never part of the user's source data.
    /// </summary>
    internal static class MeshNormalUtility
    {
        internal static Vector3[] GetOrCalculateNormals(
            Mesh mesh,
            Vector3[] vertices,
            int[] triangles)
        {
            if (vertices == null || vertices.Length == 0)
            {
                return Array.Empty<Vector3>();
            }

            var storedNormals = mesh != null ? mesh.normals : null;
            if (storedNormals != null && storedNormals.Length == vertices.Length)
            {
                return storedNormals;
            }

            var calculated = new Vector3[vertices.Length];
            if (triangles == null)
            {
                return calculated;
            }

            for (int triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
            {
                int i0 = triangles[triangle];
                int i1 = triangles[triangle + 1];
                int i2 = triangles[triangle + 2];
                if ((uint)i0 >= (uint)vertices.Length ||
                    (uint)i1 >= (uint)vertices.Length ||
                    (uint)i2 >= (uint)vertices.Length)
                {
                    continue;
                }

                var faceNormal = Vector3.Cross(
                    vertices[i1] - vertices[i0],
                    vertices[i2] - vertices[i0]);
                calculated[i0] += faceNormal;
                calculated[i1] += faceNormal;
                calculated[i2] += faceNormal;
            }

            for (int i = 0; i < calculated.Length; i++)
            {
                float squaredMagnitude = calculated[i].sqrMagnitude;
                if (squaredMagnitude > 1e-20f &&
                    !float.IsNaN(squaredMagnitude) &&
                    !float.IsInfinity(squaredMagnitude))
                {
                    calculated[i] *= 1f / Mathf.Sqrt(squaredMagnitude);
                }
                else
                {
                    calculated[i] = Vector3.zero;
                }
            }

            return calculated;
        }
    }
}
#endif
