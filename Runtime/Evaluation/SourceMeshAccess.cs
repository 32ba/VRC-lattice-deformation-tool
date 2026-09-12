using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    internal readonly struct SourceMeshLease : IDisposable
    {
        internal readonly Mesh Mesh;
        internal readonly bool OwnsMesh;

        internal SourceMeshLease(Mesh mesh, bool ownsMesh)
        {
            Mesh = mesh;
            OwnsMesh = ownsMesh;
        }

        public void Dispose()
        {
            if (OwnsMesh) DeformedMeshWriter.DestroyTemporaryMesh(Mesh);
        }
    }

    internal static class SourceMeshAccess
    {
        internal static SourceMeshLease Acquire(Mesh source)
        {
            if (source == null || source.isReadable || DeformerPlatformServices.EditorMeshDataReader == null)
                return new SourceMeshLease(source, false);
            return new SourceMeshLease(CreateReadableCopy(source), true);
        }

        internal static Mesh CreateReadableCopy(Mesh sourceMesh, Func<Mesh, Mesh.MeshDataArray> reader = null)
        {
            if (sourceMesh == null) throw new ArgumentNullException(nameof(sourceMesh));
            // NDMF may hand this component a cloned imported mesh whose managed
            // getters no longer have a CPU copy. MeshUtility can still read its
            // vertex and index buffers in the Editor, so copy those buffers into
            // a new readable Mesh without touching the imported asset.
            using Mesh.MeshDataArray sourceDataArray = (reader ?? DeformerPlatformServices.AcquireReadOnlyMeshData)(sourceMesh);
            Mesh.MeshData sourceData = sourceDataArray[0];
            Mesh readableMesh = null;
            Mesh.MeshDataArray writableDataArray = default;
            bool writableDataAllocated = false;
            bool writableDataApplied = false;
            try
            {
                readableMesh = new Mesh { name = sourceMesh.name, hideFlags = HideFlags.HideAndDontSave };
                writableDataArray = Mesh.AllocateWritableMeshData(1);
                writableDataAllocated = true;
                Mesh.MeshData writableData = writableDataArray[0];
                var attributes = new List<UnityEngine.Rendering.VertexAttributeDescriptor>();
                foreach (UnityEngine.Rendering.VertexAttribute attribute in
                         Enum.GetValues(typeof(UnityEngine.Rendering.VertexAttribute)))
                {
                    if (sourceData.HasVertexAttribute(attribute))
                    {
                        attributes.Add(new UnityEngine.Rendering.VertexAttributeDescriptor(
                            attribute,
                            sourceData.GetVertexAttributeFormat(attribute),
                            sourceData.GetVertexAttributeDimension(attribute),
                            sourceData.GetVertexAttributeStream(attribute)));
                    }
                }

                attributes.Sort((left, right) =>
                {
                    int streamOrder = left.stream.CompareTo(right.stream);
                    return streamOrder != 0
                        ? streamOrder
                        : sourceData.GetVertexAttributeOffset(left.attribute)
                            .CompareTo(sourceData.GetVertexAttributeOffset(right.attribute));
                });

                writableData.SetVertexBufferParams(sourceData.vertexCount, attributes.ToArray());
                for (int stream = 0; stream < sourceData.vertexBufferCount; stream++)
                {
                    NativeArray<byte> sourceBuffer = sourceData.GetVertexData<byte>(stream);
                    NativeArray<byte> destinationBuffer = writableData.GetVertexData<byte>(stream);
                    sourceBuffer.CopyTo(destinationBuffer);
                }

                NativeArray<byte> sourceIndices = sourceData.GetIndexData<byte>();
                int indexElementSize = sourceData.indexFormat == UnityEngine.Rendering.IndexFormat.UInt16
                    ? sizeof(ushort)
                    : sizeof(uint);
                writableData.SetIndexBufferParams(
                    sourceIndices.Length / indexElementSize,
                    sourceData.indexFormat);
                sourceIndices.CopyTo(writableData.GetIndexData<byte>());

                writableData.subMeshCount = sourceData.subMeshCount;
                for (int subMesh = 0; subMesh < sourceData.subMeshCount; subMesh++)
                {
                    writableData.SetSubMesh(
                        subMesh,
                        sourceData.GetSubMesh(subMesh),
                        UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds |
                        UnityEngine.Rendering.MeshUpdateFlags.DontValidateIndices);
                }

                Mesh.ApplyAndDisposeWritableMeshData(
                    writableDataArray,
                    readableMesh,
                    UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds |
                    UnityEngine.Rendering.MeshUpdateFlags.DontValidateIndices);
                writableDataApplied = true;

                readableMesh.bounds = sourceMesh.bounds;
                Matrix4x4[] bindPoses = sourceMesh.bindposes;
                if (bindPoses != null && bindPoses.Length > 0)
                {
                    readableMesh.bindposes = bindPoses;
                }

                using NativeArray<byte> bonesPerVertex = sourceMesh.GetBonesPerVertex();
                using NativeArray<BoneWeight1> boneWeights = sourceMesh.GetAllBoneWeights();
                if (bonesPerVertex.Length == sourceMesh.vertexCount && boneWeights.Length > 0)
                {
                    readableMesh.SetBoneWeights(bonesPerVertex, boneWeights);
                }

                DeformedMeshWriter.CopyBlendShapes(sourceMesh, readableMesh, new MeshOutputWorkspace());
            }
            catch
            {
                DeformedMeshWriter.DestroyTemporaryMesh(readableMesh);
                throw;
            }
            finally
            {
                if (writableDataAllocated && !writableDataApplied)
                {
                    writableDataArray.Dispose();
                }
            }

            return readableMesh;
        }
    }
}
