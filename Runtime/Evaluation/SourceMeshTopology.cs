using Unity.Collections;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    internal static class SourceMeshTopology
    {
        internal static int Calculate(Mesh mesh)
        {
            if (mesh == null) return 0;
            try
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + mesh.vertexCount;
                    hash = hash * 31 + mesh.subMeshCount;
                    if (!DeformerPlatformServices.CanReadMesh(mesh)) return 0;
                    using Mesh.MeshDataArray meshDataArray = DeformerPlatformServices.AcquireReadOnlyMeshData(mesh);
                    Mesh.MeshData data = meshDataArray[0];
                    bool use16Bit = mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt16;
                    NativeArray<ushort> indices16 = use16Bit
                        ? data.GetIndexData<ushort>()
                        : default;
                    NativeArray<uint> indices32 = !use16Bit
                        ? data.GetIndexData<uint>()
                        : default;
                    for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                    {
                        UnityEngine.Rendering.SubMeshDescriptor descriptor = data.GetSubMesh(subMesh);
                        hash = hash * 31 + (int)descriptor.topology;
                        hash = hash * 31 + descriptor.indexCount;
                        int end = descriptor.indexStart + descriptor.indexCount;
                        for (int index = descriptor.indexStart; index < end; index++)
                        {
                            int value = use16Bit
                                ? indices16[index] + descriptor.baseVertex
                                : unchecked((int)indices32[index]) + descriptor.baseVertex;
                            hash = hash * 31 + value;
                        }
                    }
                    return hash;
                }
            }
            catch
            {
                return 0;
            }
        }
    }
}
