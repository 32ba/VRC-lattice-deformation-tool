#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    // Cage coordinate mapping and referenced-vertex bounds. The index buffer is
    // scratch only; this class never edits a mesh, layer, selection, or Transform.
    internal sealed class LatticeCageGeometry
    {
        private readonly List<int> _skinningTopologyIndices = new List<int>();

        internal static Vector3 MapPointBetweenBounds(Vector3 point, Bounds from, Bounds to)
        {
            var fromSize = from.size;
            var toSize = to.size;

            float nx = fromSize.x != 0f ? (point.x - from.min.x) / fromSize.x : 0f;
            float ny = fromSize.y != 0f ? (point.y - from.min.y) / fromSize.y : 0f;
            float nz = fromSize.z != 0f ? (point.z - from.min.z) / fromSize.z : 0f;

            return new Vector3(
                to.min.x + nx * toSize.x,
                to.min.y + ny * toSize.y,
                to.min.z + nz * toSize.z);
        }

        internal static Vector3 MapDeltaBetweenBounds(Vector3 delta, Bounds from, Bounds to)
        {
            var fromSize = from.size;
            var toSize = to.size;

            float sx = fromSize.x != 0f ? toSize.x / fromSize.x : 0f;
            float sy = fromSize.y != 0f ? toSize.y / fromSize.y : 0f;
            float sz = fromSize.z != 0f ? toSize.z / fromSize.z : 0f;

            return new Vector3(delta.x * sx, delta.y * sy, delta.z * sz);
        }

        internal static bool AreBoundsApproximatelyEqual(Bounds a, Bounds b, float relativeTolerance)
        {
            float tolX = Mathf.Abs(a.size.x) * relativeTolerance + 1e-5f;
            float tolY = Mathf.Abs(a.size.y) * relativeTolerance + 1e-5f;
            float tolZ = Mathf.Abs(a.size.z) * relativeTolerance + 1e-5f;

            return Mathf.Abs(a.size.x - b.size.x) <= tolX &&
                   Mathf.Abs(a.size.y - b.size.y) <= tolY &&
                   Mathf.Abs(a.size.z - b.size.z) <= tolZ;
        }

        internal static Bounds ChooseLargerBounds(Bounds a, Bounds b)
        {
            var min = Vector3.Min(a.min, b.min);
            var max = Vector3.Max(a.max, b.max);
            return new Bounds((min + max) * 0.5f, max - min);
        }

        internal Bounds CalculateTransformedReferencedBounds(
            Mesh topologyMesh,
            IReadOnlyList<Vector3> vertices,
            Matrix4x4 matrix)
        {
            Bounds bounds = default;
            bool hasPoint = false;

            if (topologyMesh != null && topologyMesh.vertexCount == vertices.Count)
            {
                int subMeshCount = Mathf.Max(1, topologyMesh.subMeshCount);
                for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
                {
                    _skinningTopologyIndices.Clear();
                    topologyMesh.GetIndices(_skinningTopologyIndices, subMesh);
                    for (int i = 0; i < _skinningTopologyIndices.Count; i++)
                    {
                        int vertexIndex = _skinningTopologyIndices[i];
                        if (vertexIndex < 0 || vertexIndex >= vertices.Count)
                        {
                            continue;
                        }

                        Vector3 point = matrix.MultiplyPoint3x4(vertices[vertexIndex]);
                        if (!hasPoint)
                        {
                            bounds = new Bounds(point, Vector3.zero);
                            hasPoint = true;
                        }
                        else
                        {
                            bounds.Encapsulate(point);
                        }
                    }
                }
            }

            if (hasPoint)
            {
                return bounds;
            }

            bounds = new Bounds(matrix.MultiplyPoint3x4(vertices[0]), Vector3.zero);
            for (int i = 1; i < vertices.Count; i++)
            {
                bounds.Encapsulate(matrix.MultiplyPoint3x4(vertices[i]));
            }
            return bounds;
        }

        internal static Bounds RemapBounds(
            Bounds referenceBounds,
            Bounds referenceSampleBounds,
            Bounds currentSampleBounds)
        {
            Vector3 center = MapPointBetweenBounds(
                referenceBounds.center,
                referenceSampleBounds,
                currentSampleBounds);
            Vector3 size = MapDeltaBetweenBounds(
                referenceBounds.size,
                referenceSampleBounds,
                currentSampleBounds);
            size = new Vector3(
                Mathf.Abs(size.x),
                Mathf.Abs(size.y),
                Mathf.Abs(size.z));
            return new Bounds(center, size);
        }
    }
}
#endif
