#if UNITY_EDITOR
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class CompatibilityHashStreamTests
    {
        [TestCase(0)] [TestCase(1)] [TestCase(4095)] [TestCase(4096)] [TestCase(4097)] [TestCase(8193)]
        public void FragmentedWrites_MatchWholePayloadSha256(int length)
        {
            var bytes = new byte[length];
            new System.Random(194).NextBytes(bytes);
            using var stream = new CompatibilityHashStream();
            int index = 0;
            while (index < length)
            {
                if ((index & 1) == 0) stream.WriteByte(bytes[index++]);
                else
                {
                    int count = Math.Min(137, length - index);
                    stream.Write(bytes, index, count); index += count;
                }
                if (index % 5 == 0) stream.Flush();
            }
            using var hash = SHA256.Create();
            CollectionAssert.AreEqual(hash.ComputeHash(bytes), stream.Finish());
            Assert.Throws<InvalidOperationException>(() => stream.WriteByte(1));
            Assert.Throws<InvalidOperationException>(() => stream.Finish());
        }

        [Test]
        public void FloatBitWrites_PreserveHistoricalBinaryWriterBytes()
        {
            using var legacy = new MemoryStream();
            using var current = new CompatibilityHashStream();
            using var oldWriter = new BinaryWriter(legacy, Encoding.UTF8, true);
            using var newWriter = new BinaryWriter(current, Encoding.UTF8, true);
            foreach (int bits in new[] { 0, int.MinValue, 1, -1, 0x7f800000, 0x7fc12345, 0x3f800001 })
            {
                float value = BitConverter.Int32BitsToSingle(bits);
                oldWriter.Write(value);
                newWriter.Write(BitConverter.SingleToInt32Bits(value));
            }
            oldWriter.Flush(); newWriter.Flush();
            using var hash = SHA256.Create();
            CollectionAssert.AreEqual(hash.ComputeHash(legacy.ToArray()), current.Finish());
        }

        [TestCase(IndexFormat.UInt16)] [TestCase(IndexFormat.UInt32)]
        public void MeshMetadata_MatchesHistoricalHashAcrossSubmeshes(IndexFormat format)
        {
            var mesh = new Mesh { indexFormat = format };
            try
            {
                var vertices = new Vector3[5000];
                for (int i = 0; i < vertices.Length; i++) vertices[i] = new Vector3(i * .0031f, -i * .012f, i % 7);
                mesh.vertices = vertices; mesh.subMeshCount = 2;
                mesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0, false);
                mesh.SetIndices(new[] { 0, 1, 2, 3 }, MeshTopology.Lines, 1, false, 100);
                using var bytes = new MemoryStream();
                using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true))
                {
                    writer.Write(vertices.Length);
                    foreach (var vertex in mesh.vertices) { writer.Write(vertex.x); writer.Write(vertex.y); writer.Write(vertex.z); }
                    writer.Write(mesh.subMeshCount);
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    {
                        writer.Write((int)mesh.GetTopology(sub));
                        var indices = mesh.GetIndices(sub); writer.Write(indices.Length);
                        foreach (int index in indices) writer.Write(index);
                    }
                }
                using var sha = SHA256.Create();
                string expected = BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "").ToLowerInvariant();
                Assert.That(MeshCompatibilityMetadata.Capture(mesh).TopologyHash, Is.EqualTo(expected));
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
    }
}
#endif
