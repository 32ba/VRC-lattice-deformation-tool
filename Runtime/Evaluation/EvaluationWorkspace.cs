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
        // Compact scratch for sparse evaluation; grow-only so repeated frames
        // with different subset sizes reuse the same storage.
        internal Vector3[] SubsetSource = Array.Empty<Vector3>();
        internal Vector3[] SubsetGroup = Array.Empty<Vector3>();
        internal Vector3[] SubsetDirect = Array.Empty<Vector3>();

        public void Dispose() => Lattice.Dispose();

        internal void EnsureSubsetCapacity(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            Grow(ref SubsetSource, count);
            Grow(ref SubsetGroup, count);
            Grow(ref SubsetDirect, count);
        }

        internal static void Grow(ref Vector3[] buffer, int count)
        {
            if (buffer.Length < count) buffer = new Vector3[Math.Max(count, buffer.Length + (buffer.Length >> 1))];
        }

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
        internal int[] ChangedIndices = Array.Empty<int>();
        internal Vector3[] ChangedOutput = Array.Empty<Vector3>();

        internal void EnsureCapacity(int count)
        {
            EvaluationWorkspace.Resize(ref DeltaVertices, count);
            EvaluationWorkspace.Resize(ref DeltaNormals, count);
            EvaluationWorkspace.Resize(ref DeltaTangents, count);
            EvaluationWorkspace.Resize(ref Combined, count);
            EvaluationWorkspace.Resize(ref DeformedCombined, count);
            EvaluationWorkspace.Resize(ref OutputDelta, count);
        }

        // Sparse frame evaluation records at most `count` changed vertices.
        internal void EnsureChangedCapacity(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (ChangedIndices.Length != count) ChangedIndices = count == 0 ? Array.Empty<int>() : new int[count];
            EvaluationWorkspace.Resize(ref ChangedOutput, count);
        }
    }
}
