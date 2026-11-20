using System;
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    // Owned by one component. Results are borrowed until its next evaluation.
    // Callers that retain a result must copy it (Mesh setters do so).
    internal sealed class EvaluationWorkspace : IDisposable
    {
        internal Vector3[] DirectDeltas = Array.Empty<Vector3>();
        internal Vector3[] GroupVertices = Array.Empty<Vector3>();
        internal Vector3[] LayerVertices = Array.Empty<Vector3>();
        internal Vector3[] FinalVertices = Array.Empty<Vector3>();
        internal readonly List<GeneratedBlendShapeOutput> GeneratedShapes = new List<GeneratedBlendShapeOutput>();
        internal readonly LatticeEvaluator Lattice = new LatticeEvaluator();
        internal readonly MeshOutputWorkspace MeshOutput = new MeshOutputWorkspace();
        internal readonly PreviewFrameWorkspace PreviewFrames = new PreviewFrameWorkspace();

        public void Dispose() => Lattice.Dispose();

        internal void EnsureCapacity(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            Resize(ref DirectDeltas, count);
            Resize(ref GroupVertices, count);
            Resize(ref LayerVertices, count);
            Resize(ref FinalVertices, count);
        }

        internal static void Resize(ref Vector3[] buffer, int count)
        {
            if (buffer.Length != count) buffer = count == 0 ? Array.Empty<Vector3>() : new Vector3[count];
        }
    }

    internal sealed class PreviewFrameWorkspace
    {
        internal readonly SourceVertexWorkspace SourcePose = new SourceVertexWorkspace();
        internal readonly SourceVertexWorkspace OutputPose = new SourceVertexWorkspace();
        internal Vector3[] DeltaVertices = Array.Empty<Vector3>();
        internal Vector3[] DeltaNormals = Array.Empty<Vector3>();
        internal Vector3[] DeltaTangents = Array.Empty<Vector3>();
        internal Vector3[] Combined = Array.Empty<Vector3>();
        internal Vector3[] DeformedCombined = Array.Empty<Vector3>();
        internal Vector3[] OutputDelta = Array.Empty<Vector3>();

        internal void EnsureCapacity(int count)
        {
            EvaluationWorkspace.Resize(ref DeltaVertices, count);
            EvaluationWorkspace.Resize(ref DeltaNormals, count);
            EvaluationWorkspace.Resize(ref DeltaTangents, count);
            EvaluationWorkspace.Resize(ref Combined, count);
            EvaluationWorkspace.Resize(ref DeformedCombined, count);
            EvaluationWorkspace.Resize(ref OutputDelta, count);
        }
    }
}
