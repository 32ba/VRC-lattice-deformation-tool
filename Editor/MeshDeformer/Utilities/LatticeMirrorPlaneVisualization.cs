#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    // Axis follows Unity vector component order: X=0, Y=1, Z=2.
    // Borrows bounds/matrix and owns only the four-corner drawing scratch.
    internal static class LatticeMirrorPlaneVisualization
    {
        private static readonly Vector3[] s_mirrorPlaneCorners = new Vector3[4];
        internal static void Draw(Bounds bounds, Matrix4x4 meshToWorld, int axis)
        {
            var size = bounds.size;
            if (size == Vector3.zero)
            {
                return;
            }

            var centerLocal = bounds.center;
            Vector3 axisA;
            Vector3 axisB;

            switch (axis)
            {
                case 0:
                    axisA = Vector3.up * (size.y * 0.5f);
                    axisB = Vector3.forward * (size.z * 0.5f);
                    break;
                case 1:
                    axisA = Vector3.right * (size.x * 0.5f);
                    axisB = Vector3.forward * (size.z * 0.5f);
                    break;
                case 2:
                default:
                    axisA = Vector3.right * (size.x * 0.5f);
                    axisB = Vector3.up * (size.y * 0.5f);
                    break;
            }

            var localCorners = s_mirrorPlaneCorners;
            localCorners[0] = centerLocal + axisA + axisB;
            localCorners[1] = centerLocal + axisA - axisB;
            localCorners[2] = centerLocal - axisA - axisB;
            localCorners[3] = centerLocal - axisA + axisB;

            for (int i = 0; i < localCorners.Length; i++)
            {
                localCorners[i] = meshToWorld.MultiplyPoint3x4(localCorners[i]);
            }

            var fillColor = new Color(0.3f, 0.6f, 1f, 0.3f);
            var outlineColor = new Color(0.3f, 0.6f, 1f, 0.6f);
            Handles.DrawSolidRectangleWithOutline(localCorners, fillColor, outlineColor);
        }
    }
}
#endif
