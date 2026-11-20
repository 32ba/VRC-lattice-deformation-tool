#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    // Only inputs and capture logic are shared. Export expected outputs with the
    // immutable baseline package in a separate Editor before replacing evaluation.
    public static class DeformationOutputBaselineFixture
    {
        public static readonly string[] Cases =
        {
            "direct-trilinear", "direct-bernstein", "single-output", "progressive-output",
            "crossfade-output", "layer-output", "imported-frames", "profile", "skinned-weighted",
            "upstream", "upstream-reduced", "preserve-channels", "legacy-semantics"
        };

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
        [Serializable] public sealed class Snapshot
        {
            public string scenario;
            public MeshSnapshot sourceBefore, sourceAfter, inputBefore, inputAfter, output;
            public bool rendererRetainedSource;
            public float[] rendererWeights;
        }
        [Serializable] public sealed class Document
        {
            public string baselineCommit;
            public string unityVersion;
            public Snapshot[] cases;
        }

        public static void Export()
        {
            var args = Environment.GetCommandLineArgs();
            int output = Array.IndexOf(args, "-latticeOutputSnapshot");
            int commit = Array.IndexOf(args, "-latticeBaselineCommit");
            if (output < 0 || commit < 0) throw new ArgumentException("Explicit output and baseline are required.");
            var snapshots = Cases.Select(Run).ToArray();
            File.WriteAllText(args[output + 1], JsonUtility.ToJson(new Document
            {
                baselineCommit = args[commit + 1], unityVersion = Application.unityVersion, cases = snapshots
            }, true) + "\n");
            Debug.Log($"Captured {snapshots.Length} baseline full-channel deformation outputs.");
        }

        public static Snapshot Run(string scenario)
        {
            var root = new GameObject("Baseline deformation");
            var source = CreateMesh(3);
            Mesh upstream = null;
            Mesh previewOutput = null;
            MeshDeformerProfile profile = null;
            try
            {
                SkinnedMeshRenderer skinned = null;
                MeshFilter filter = null;
                if (scenario == "skinned-weighted")
                {
                    skinned = root.AddComponent<SkinnedMeshRenderer>();
                    skinned.sharedMesh = source;
                    skinned.bones = Enumerable.Range(0, 5).Select(i =>
                    {
                        var bone = new GameObject("Bone " + i).transform;
                        bone.SetParent(root.transform, false);
                        return bone;
                    }).ToArray();
                    skinned.SetBlendShapeWeight(0, 60f);
                }
                else
                {
                    filter = root.AddComponent<MeshFilter>();
                    filter.sharedMesh = source;
                    root.AddComponent<MeshRenderer>();
                }
                var deformer = root.AddComponent<LatticeDeformer>();
                deformer.Reset();
                bool preserveChannels = scenario == "preserve-channels";
                using (var serialized = new SerializedObject(deformer))
                {
                    serialized.FindProperty("_recalculateNormals").boolValue = !preserveChannels;
                    serialized.FindProperty("_recalculateTangents").boolValue = !preserveChannels;
                    serialized.FindProperty("_recalculateBounds").boolValue = !preserveChannels;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                var lattice = deformer.Layers[0].Settings;
                lattice.Interpolation = scenario == "direct-bernstein"
                    ? LatticeInterpolationMode.CubicBernstein : LatticeInterpolationMode.Trilinear;
                for (int i = 0; i < lattice.ControlPointCount; i++)
                {
                    var point = lattice.GetControlPointLocal(i);
                    lattice.SetControlPointLocal(i, point + new Vector3(
                        0.08f * point.y * point.y, 0.03f * point.x, 0.05f));
                }
                if (scenario != "upstream-reduced") AddBrush(deformer, "Direct brush", 0.12f);

                if (scenario == "single-output" || scenario == "progressive-output" ||
                    scenario == "crossfade-output" || scenario == "legacy-semantics")
                {
                    deformer.AddGroup("Generated output");
                    AddBrush(deformer, "First stage", 0.2f);
                    AddBrush(deformer, "Second stage", -0.1f);
                    var group = deformer.ActiveGroup;
                    group.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                    group.BlendShapeName = "Output"; // Collides with a source shape.
                    group.BlendShapeComposition = scenario == "progressive-output"
                        ? BlendShapeCompositionMode.Progressive : scenario == "crossfade-output"
                            ? BlendShapeCompositionMode.Crossfade : BlendShapeCompositionMode.Single;
                    group.BlendShapeCurve = new AnimationCurve(
                        new Keyframe(0f, 0f), new Keyframe(0.45f, 0.25f), new Keyframe(1f, 1f));
                    if (scenario == "legacy-semantics")
                    {
                        group.Layers[0].BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                        group.Layers[0].BlendShapeName = "Latent layer output";
                        typeof(LatticeDeformer).GetField("_legacyPublishedBlendShapeSemantics",
                            BindingFlags.Instance | BindingFlags.NonPublic).SetValue(deformer, true);
                    }
                }
                if (scenario == "layer-output")
                {
                    deformer.Layers[1].BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                    deformer.Layers[1].BlendShapeName = "Existing";
                }
                if (scenario == "imported-frames") deformer.ImportBlendShapeAllFramesAsGroup(0);
                if (scenario == "profile")
                {
                    profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
                    if (!deformer.SaveToProfile(profile) || !deformer.UseProfile(profile))
                        throw new InvalidOperationException("Cannot create baseline profile input.");
                }
                deformer.InvalidateCache();
                var sourceBefore = CaptureMesh(source);
                Mesh output;
                MeshSnapshot inputBefore = null;
                if (scenario.StartsWith("upstream", StringComparison.Ordinal))
                {
                    upstream = scenario == "upstream-reduced" ? CreateMesh(2) : Object.Instantiate(source);
                    upstream.vertices = upstream.vertices.Select(v => v + new Vector3(0.2f, -0.1f, 0.3f)).ToArray();
                    inputBefore = CaptureMesh(upstream);
                    previewOutput = deformer.CreatePreviewMeshFromInput(upstream);
                    output = previewOutput;
                }
                else output = deformer.Deform(false);
                if (output == null) throw new InvalidOperationException("Baseline rejected " + scenario);
                return new Snapshot
                {
                    scenario = scenario, sourceBefore = sourceBefore, sourceAfter = CaptureMesh(source),
                    inputBefore = inputBefore, inputAfter = upstream != null ? CaptureMesh(upstream) : null,
                    output = CaptureMesh(output),
                    rendererRetainedSource = skinned != null ? skinned.sharedMesh == source : filter.sharedMesh == source,
                    rendererWeights = skinned != null
                        ? Enumerable.Range(0, source.blendShapeCount).Select(skinned.GetBlendShapeWeight).ToArray()
                        : Array.Empty<float>()
                };
            }
            finally
            {
                if (previewOutput != null) Object.DestroyImmediate(previewOutput);
                if (upstream != null) Object.DestroyImmediate(upstream);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(source);
                if (profile != null) Object.DestroyImmediate(profile);
            }
        }

        private static void AddBrush(LatticeDeformer deformer, string name, float amount)
        {
            int index = deformer.AddLayer(name, MeshDeformerLayerType.Brush);
            var layer = deformer.Layers[index];
            layer.Weight = 0.7f;
            int count = layer.BrushDisplacements.Length;
            layer.VertexMask = Enumerable.Range(0, count).Select(i => i % 2 == 0 ? 0.5f : 1f).ToArray();
            for (int i = 0; i < count; i++) layer.SetBrushDisplacement(i, new Vector3(amount * i / count, amount, 0.02f));
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
}
#endif
