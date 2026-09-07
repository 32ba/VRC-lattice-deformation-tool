#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

// Frozen full-channel input/capture for published-tag fixture generation.
// Derived from DeformationOutputBaselineFixture at c7f499c-compatible P0 capture.
// This file deliberately has no product type references: only the selected tag Runtime
// is compiled in the generation project. Changes require regenerating the later corpus.
public static class LaterReleaseMeshCapture
{
        [Serializable] public sealed class Frame
        {
            public string shape;
            public float weight;
            public Vector3[] vertices, normals, tangents;
        }
        [Serializable] public sealed class UvChannel { public Vector4[] values; }
        [Serializable] public sealed class Submesh { public int topology; public int[] indices; }
        [Serializable] public sealed class Weight { public int bone; public float value; }
        [Serializable] public sealed class MeshSnapshot
        {
            public int indexFormat;
            public string[] attributes;
            public Vector3[] vertices, normals;
            public Vector4[] tangents;
            public Color[] colors;
            public Bounds bounds;
            public Matrix4x4[] bindposes;
            public byte[] bonesPerVertex;
            public Weight[] weights;
            public UvChannel[] uv;
            public Submesh[] submeshes;
            public Frame[] frames;
        }
        public static Mesh CreateMesh(int side)
        {
            int count = side * side;
            var mesh = new Mesh { name = "Full channel source", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = Enumerable.Range(0, count).Select(i =>
                new Vector3(i % side - 1f, i / side - 1f, i % 2 == 0 ? 0.2f : 0f)).ToArray();
            mesh.colors = Enumerable.Range(0, count).Select(i => new Color(0.1f * i, 0.2f, 0.6f, 1f)).ToArray();
            for (int channel = 0; channel < 8; channel++)
                mesh.SetUVs(channel, Enumerable.Range(0, count).Select(i =>
                    new Vector4((i % side) / (float)side, (i / side) / (float)side, channel * 0.1f, 1f)).ToList());
            var triangles = new List<int>();
            for (int y = 0; y < side - 1; y++)
                for (int x = 0; x < side - 1; x++)
                {
                    int a = y * side + x;
                    triangles.AddRange(new[] { a, a + 1, a + side, a + 1, a + side + 1, a + side });
                }
            int half = triangles.Count / 2;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(triangles.Take(half).ToArray(), 0);
            mesh.SetTriangles(triangles.Skip(half).ToArray(), 1);
            mesh.RecalculateNormals();
            mesh.tangents = Enumerable.Repeat(new Vector4(1f, 0f, 0f, -1f), count).ToArray();
            mesh.bindposes = Enumerable.Range(0, 5).Select(i => Matrix4x4.Translate(new Vector3(i * 0.02f, 0f, 0f))).ToArray();
            var counts = new NativeArray<byte>(count, Allocator.Temp);
            var weights = default(NativeArray<BoneWeight1>);
            try
            {
                weights = new NativeArray<BoneWeight1>(count * 5, Allocator.Temp);
                var values = new[] { 0.4f, 0.25f, 0.15f, 0.12f, 0.08f };
                for (int vertex = 0; vertex < count; vertex++)
                {
                    counts[vertex] = 5;
                    for (int bone = 0; bone < 5; bone++)
                        weights[vertex * 5 + bone] = new BoneWeight1 { boneIndex = bone, weight = values[bone] };
                }
                mesh.SetBoneWeights(counts, weights);
            }
            finally
            {
                if (weights.IsCreated) weights.Dispose();
                counts.Dispose();
            }
            foreach (string shape in new[] { "Existing", "Output" })
                foreach (float weight in new[] { 25f, 100f })
                {
                    var delta = Enumerable.Range(0, count).Select(i => new Vector3(0.003f * i, weight * 0.001f, 0f)).ToArray();
                    var normals = Enumerable.Repeat(Vector3.right * 0.01f, count).ToArray();
                    var tangents = Enumerable.Repeat(Vector3.up * 0.02f, count).ToArray();
                    mesh.AddBlendShapeFrame(shape, weight, delta, normals, tangents);
                }
            mesh.bounds = new Bounds(new Vector3(0.2f, 0.3f, 0.4f), new Vector3(4f, 5f, 6f));
            return mesh;
        }

        public static MeshSnapshot CaptureMesh(Mesh mesh)
        {
            var channels = new UvChannel[8];
            for (int channel = 0; channel < channels.Length; channel++)
            {
                var uv = new List<Vector4>();
                mesh.GetUVs(channel, uv);
                channels[channel] = new UvChannel { values = uv.ToArray() };
            }
            var frames = new List<Frame>();
            for (int shape = 0; shape < mesh.blendShapeCount; shape++)
                for (int frame = 0; frame < mesh.GetBlendShapeFrameCount(shape); frame++)
                {
                    var value = new Frame
                    {
                        shape = mesh.GetBlendShapeName(shape), weight = mesh.GetBlendShapeFrameWeight(shape, frame),
                        vertices = new Vector3[mesh.vertexCount], normals = new Vector3[mesh.vertexCount],
                        tangents = new Vector3[mesh.vertexCount]
                    };
                    mesh.GetBlendShapeFrameVertices(shape, frame, value.vertices, value.normals, value.tangents);
                    frames.Add(value);
                }
            using var bones = mesh.GetBonesPerVertex();
            using var weights = mesh.GetAllBoneWeights();
            return new MeshSnapshot
            {
                indexFormat = (int)mesh.indexFormat,
                attributes = mesh.GetVertexAttributes().Select(a => $"{a.attribute}:{a.format}:{a.dimension}:{a.stream}").ToArray(),
                vertices = mesh.vertices, normals = mesh.normals, tangents = mesh.tangents,
                colors = mesh.colors, bounds = mesh.bounds, bindposes = mesh.bindposes,
                bonesPerVertex = bones.ToArray(),
                weights = weights.ToArray().Select(w => new Weight { bone = w.boneIndex, value = w.weight }).ToArray(),
                uv = channels,
                submeshes = Enumerable.Range(0, mesh.subMeshCount).Select(i => new Submesh
                    { topology = (int)mesh.GetTopology(i), indices = mesh.GetIndices(i) }).ToArray(),
                frames = frames.ToArray()
            };
        }
}
#endif
