using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    internal readonly struct SourceMeshLease : IDisposable
    {
        internal readonly Mesh Mesh;
        // True when Mesh is a readable copy rather than the caller's source.
        internal readonly bool OwnsMesh;
        // A cached copy belongs to its cache; disposing the lease only ends the borrow.
        private readonly ReadableSourceCache _cache;

        internal SourceMeshLease(Mesh mesh, bool ownsMesh) : this(mesh, ownsMesh, null) { }

        internal SourceMeshLease(Mesh mesh, bool ownsMesh, ReadableSourceCache cache)
        {
            Mesh = mesh;
            OwnsMesh = ownsMesh;
            _cache = cache;
        }

        public void Dispose()
        {
            if (_cache != null) _cache.Return();
            else if (OwnsMesh) DeformedMeshWriter.DestroyTemporaryMesh(Mesh);
        }
    }

    /// <summary>
    /// Keeps one readable copy of a non-readable source per owner slot while the
    /// source's <see cref="ObjectChangeToken"/> is unchanged. A changed token
    /// (Mesh write, reimport, Undo/Redo, different Mesh) replaces the copy. A copy
    /// is never destroyed while a lease borrows it: replacement during a borrow
    /// hands out an uncached copy, and Release during a borrow is deferred to the
    /// last Return. Without the Editor adapter no token is valid and every call
    /// gets an uncached copy as before.
    /// </summary>
    internal sealed class ReadableSourceCache
    {
        private static readonly List<ReadableSourceCache> s_live = new List<ReadableSourceCache>();
        private ObjectChangeToken _token;
        private Mesh _copy;
        private int _borrows;
        private bool _releasePending;

        // Test probe: readable copies created through any cache.
        internal static int CopiesCreated { get; private set; }

        internal Mesh CachedCopy => _copy;

        internal SourceMeshLease Acquire(Mesh source)
        {
            if (source == null || source.isReadable || DeformerPlatformServices.EditorMeshDataReader == null)
                return new SourceMeshLease(source, false);
            if (_copy != null && !_releasePending && DeformerChangeTokens.Capture(source).Equals(_token))
            {
                _borrows++;
                return new SourceMeshLease(_copy, true, this);
            }

            // A caller still reads the cached copy; never replace it underneath them.
            if (_borrows > 0) return new SourceMeshLease(SourceMeshAccess.CreateReadableCopy(source), true);

            DestroyCopy();
            var copy = SourceMeshAccess.CreateReadableCopy(source);
            unchecked { CopiesCreated++; }
            // Capture after copying so reads performed by the copy itself are absorbed.
            var token = DeformerChangeTokens.Capture(source);
            if (!token.IsValid) return new SourceMeshLease(copy, true);
            _copy = copy;
            _token = token;
            s_live.Add(this);
            _borrows++;
            return new SourceMeshLease(_copy, true, this);
        }

        internal void Return()
        {
            if (_borrows > 0) _borrows--;
            if (_borrows == 0 && _releasePending) DestroyCopy();
        }

        internal void Release()
        {
            if (_borrows > 0)
            {
                _releasePending = _copy != null;
                return;
            }
            DestroyCopy();
        }

        private void DestroyCopy()
        {
            var copy = _copy;
            _copy = null;
            _token = default;
            _releasePending = false;
            s_live.Remove(this);
            if (copy != null) DeformedMeshWriter.DestroyTemporaryMesh(copy);
        }

        // Domain reload drops the managed owners; destroy their HideAndDontSave copies first.
        internal static void ReleaseAll()
        {
            for (int i = s_live.Count - 1; i >= 0; i--)
            {
                if (i >= s_live.Count) continue;
                var cache = s_live[i];
                cache._borrows = 0;
                cache.DestroyCopy();
            }
            s_live.Clear();
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

        internal static Mesh CreateReadableCopy(Mesh sourceMesh, Func<Mesh, Mesh.MeshDataArray> reader = null,
            bool copyBlendShapes = true)
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

                if (copyBlendShapes)
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
