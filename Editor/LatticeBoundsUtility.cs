#if UNITY_EDITOR
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Shared utility methods for bounds calculations used across the Lattice Deformation Tool.
    /// </summary>
    internal static class LatticeBoundsUtility
    {
        /// <summary>
        /// Divides the bounds center and size by the given scale.
        /// Used to convert scaled bounds back to unscaled local space.
        /// </summary>
        /// <param name="bounds">The bounds to divide.</param>
        /// <param name="scale">The scale to divide by.</param>
        /// <returns>A new Bounds with center and size divided by scale.</returns>
        public static Bounds DivideByScale(Bounds bounds, Vector3 scale)
        {
            var center = new Vector3(
                scale.x != 0f ? bounds.center.x / scale.x : bounds.center.x,
                scale.y != 0f ? bounds.center.y / scale.y : bounds.center.y,
                scale.z != 0f ? bounds.center.z / scale.z : bounds.center.z);

            var size = new Vector3(
                scale.x != 0f ? bounds.size.x / Mathf.Abs(scale.x) : bounds.size.x,
                scale.y != 0f ? bounds.size.y / Mathf.Abs(scale.y) : bounds.size.y,
                scale.z != 0f ? bounds.size.z / Mathf.Abs(scale.z) : bounds.size.z);

            return new Bounds(center, size);
        }

        /// <summary>
        /// Checks if two bounds are approximately equal within a relative tolerance.
        /// </summary>
        /// <param name="a">First bounds.</param>
        /// <param name="b">Second bounds.</param>
        /// <param name="relativeTolerance">Tolerance relative to bounds size.</param>
        /// <returns>True if bounds are approximately equal.</returns>
        public static bool AreApproximatelyEqual(Bounds a, Bounds b, float relativeTolerance)
        {
            float tolX = Mathf.Abs(a.size.x) * relativeTolerance + 1e-5f;
            float tolY = Mathf.Abs(a.size.y) * relativeTolerance + 1e-5f;
            float tolZ = Mathf.Abs(a.size.z) * relativeTolerance + 1e-5f;

            return Mathf.Abs(a.size.x - b.size.x) <= tolX &&
                   Mathf.Abs(a.size.y - b.size.y) <= tolY &&
                   Mathf.Abs(a.size.z - b.size.z) <= tolZ;
        }

        /// <summary>
        /// Creates a new bounds that encompasses both input bounds.
        /// </summary>
        /// <param name="a">First bounds.</param>
        /// <param name="b">Second bounds.</param>
        /// <returns>A bounds that contains both input bounds.</returns>
        public static Bounds Encapsulate(Bounds a, Bounds b)
        {
            var min = Vector3.Min(a.min, b.min);
            var max = Vector3.Max(a.max, b.max);
            return new Bounds((min + max) * 0.5f, max - min);
        }

        /// <summary>
        /// Maps a point from one bounds space to another.
        /// </summary>
        /// <param name="point">The point to map.</param>
        /// <param name="from">Source bounds.</param>
        /// <param name="to">Target bounds.</param>
        /// <returns>The point mapped to the target bounds space.</returns>
        public static Vector3 MapPointBetweenBounds(Vector3 point, Bounds from, Bounds to)
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

        /// <summary>
        /// Maps a delta vector from one bounds space to another.
        /// </summary>
        /// <param name="delta">The delta to map.</param>
        /// <param name="from">Source bounds.</param>
        /// <param name="to">Target bounds.</param>
        /// <returns>The delta mapped to the target bounds space.</returns>
        public static Vector3 MapDeltaBetweenBounds(Vector3 delta, Bounds from, Bounds to)
        {
            var fromSize = from.size;
            var toSize = to.size;

            float sx = fromSize.x != 0f ? toSize.x / fromSize.x : 0f;
            float sy = fromSize.y != 0f ? toSize.y / fromSize.y : 0f;
            float sz = fromSize.z != 0f ? toSize.z / fromSize.z : 0f;

            return new Vector3(delta.x * sx, delta.y * sy, delta.z * sz);
        }
    }
}
#endif
