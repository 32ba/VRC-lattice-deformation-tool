#if UNITY_EDITOR
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>Pure preflight shared by single commands and interactive edit sessions.</summary>
    internal static class DeformerAuthoringSource
    {
        internal static bool TryRead(LatticeDeformer owner, out SerializedDeformerData data,
            out Mesh source, out int topologyHash)
        {
            data = default; source = null; topologyHash = 0;
            if (owner == null || !owner.HasValidSerializedAuthoringData) return false;
            data = SerializedDeformerReader.Read(owner);
            if (data.DataSource != DeformerDataSource.Embedded) return false;
            source = data.SourceMesh;
            if (source == null || source.vertexCount == 0 || !HasExpectedRendererMesh(owner, data, source) ||
                (data.SourceVertexCount > 0 && data.SourceVertexCount != source.vertexCount)) return false;
            topologyHash = SourceMeshTopology.Calculate(source);
            return topologyHash != 0 && (data.SourceTopologyHash == 0 || data.SourceTopologyHash == topologyHash);
        }

        internal static bool HasExpectedRendererMesh(LatticeDeformer owner, in SerializedDeformerData data, Mesh source)
        {
            Mesh displayed = data.SkinnedRenderer != null ? data.SkinnedRenderer.sharedMesh :
                data.MeshFilter != null ? data.MeshFilter.sharedMesh : null;
            return displayed != null && (ReferenceEquals(displayed, source) || ReferenceEquals(displayed, owner.RuntimeMesh));
        }
    }
}
#endif
