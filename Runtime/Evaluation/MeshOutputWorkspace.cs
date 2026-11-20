using System;
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    // One evaluation owner; Mesh APIs copy these buffers before the next write.
    internal sealed class MeshOutputWorkspace
    {
        internal Vector3[] FrameVertices = Array.Empty<Vector3>();
        internal Vector3[] FrameNormals = Array.Empty<Vector3>();
        internal Vector3[] FrameTangents = Array.Empty<Vector3>();
        internal readonly List<Vector3> SourceNormals = new List<Vector3>();
        internal readonly List<Vector4> SourceTangents = new List<Vector4>();

        internal void EnsureFrameCapacity(int count, bool normals, bool tangents)
        {
            Ensure(ref FrameVertices, count);
            if (normals) Ensure(ref FrameNormals, count);
            if (tangents) Ensure(ref FrameTangents, count);
        }

        internal void ClearFrameBuffers()
        {
            Array.Clear(FrameVertices, 0, FrameVertices.Length);
            Array.Clear(FrameNormals, 0, FrameNormals.Length);
            Array.Clear(FrameTangents, 0, FrameTangents.Length);
        }

        private static void Ensure(ref Vector3[] buffer, int count)
        {
            if (buffer.Length != count) buffer = count == 0 ? Array.Empty<Vector3>() : new Vector3[count];
        }
    }
}
