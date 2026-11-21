#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    // The preview evaluates only vertices that an upstream frame moves. These
    // tests compare float bits, not approximate values, against the whole-mesh
    // evaluation that the sparse path replaces.
    public sealed class SparseFrameEvaluationTests
    {
        private static readonly Bounds LatticeBounds =
            new Bounds(new Vector3(0.1f, -0.05f, 0.02f), new Vector3(2f, 1.6f, 1.8f));

        private static readonly MeshOutputOptions PreserveChannels = new MeshOutputOptions(
            false, false, false, NormalsRecalculationMode.LegacyUnityRecalculate, false);

        [TestCase("BrushMask")]
        [TestCase("Trilinear")]
        [TestCase("Cubic")]
        [TestCase("LegacyTrilinear")]
        [TestCase("AbsoluteLocal")]
        [TestCase("AbsoluteWorld")]
        [TestCase("MultiGroup")]
        [TestCase("PublishedSemantics")]
        [TestCase("SharedCacheKey")]
        public void EvaluateSubset_MatchesFullEvaluationBitwise(string scenario)
        {
            const int vertexCount = 257;
            var random = new Random(StableSeed(scenario));
            var positions = CreatePositions(random, vertexCount);
            var input = CreateInput(scenario, random, vertexCount);
            using var workspace = new EvaluationWorkspace();
            var full = new Vector3[vertexCount];
            DeformationEvaluator.Evaluate(input, positions, full, workspace, null);
            var entries = workspace.Lattice.Cache.Entries;

            foreach (int count in new[] { 1, 63, 64, 65, 130, vertexCount })
            {
                var indices = PickIndices(random, vertexCount, count, count + 3);
                var compact = new Vector3[count + 2];
                DeformationEvaluator.EvaluateSubset(input, positions, indices, count, compact, workspace);
                for (int i = 0; i < count; i++)
                    AssertSameBits(full[indices[i]], compact[i], $"{scenario} count {count} vertex {indices[i]}");
            }

            Assert.That(workspace.Lattice.Cache.Entries, Is.SameAs(entries),
                "Subset evaluation must not replace the whole-mesh interpolation cache.");
            // With approximately equal bounds the whole-mesh result depends on
            // which layer last filled its cache, so a repeat is not comparable.
            if (scenario == "SharedCacheKey") return;
            var again = new Vector3[vertexCount];
            DeformationEvaluator.Evaluate(input, positions, again, workspace, null);
            for (int vertex = 0; vertex < vertexCount; vertex++)
                AssertSameBits(full[vertex], again[vertex], $"{scenario} repeated full vertex {vertex}");
        }

        [TestCase("BrushMask")]
        [TestCase("Cubic")]
        [TestCase("AbsoluteWorld")]
        [TestCase("MultiGroup")]
        [TestCase("PublishedSemantics")]
        public void CreatePreviewMeshFromInput_MatchesWholeMeshFrameEvaluationBitwise(string scenario)
        {
            const int vertexCount = 144;
            var random = new Random(StableSeed(scenario) + 17);
            var mesh = CreateUpstreamMesh(random, vertexCount);
            var input = CreateInput(scenario, random, vertexCount);
            Mesh referenceMesh = null;
            Mesh output = null;
            try
            {
                using var referenceWorkspace = new EvaluationWorkspace();
                referenceMesh = BuildReference(mesh, input, referenceWorkspace, out var referenceVertices,
                    out int generatedCount);
                using var workspace = new EvaluationWorkspace();
                for (int pass = 0; pass < 2; pass++)
                {
                    output = DeformationPipeline.CreatePreviewMeshFromInput(mesh, input, PreserveChannels, workspace);
                    var actualVertices = output.vertices;
                    for (int vertex = 0; vertex < vertexCount; vertex++)
                        AssertSameBits(referenceVertices[vertex], actualVertices[vertex],
                            $"{scenario} pass {pass} base vertex {vertex}");
                    Assert.That(output.blendShapeCount, Is.EqualTo(mesh.blendShapeCount + generatedCount));
                    CompareFrames(mesh, referenceMesh, output, $"{scenario} pass {pass}");
                    Object.DestroyImmediate(output);
                    output = null;
                }
            }
            finally
            {
                if (output != null) Object.DestroyImmediate(output);
                if (referenceMesh != null) Object.DestroyImmediate(referenceMesh);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void SparseUpstreamFrames_KeepTheBaseInterpolationCache()
        {
            const int vertexCount = 144;
            var random = new Random(91);
            var positions = CreatePositions(random, vertexCount);
            var mesh = new Mesh { vertices = positions, triangles = Triangles(vertexCount) };
            for (int shape = 0; shape < 4; shape++)
                mesh.AddBlendShapeFrame("Sparse" + shape, 100f, Deltas(random, vertexCount, 6), null, null);
            var input = CreateInput("Trilinear", random, vertexCount);
            Mesh output = null;
            try
            {
                using var workspace = new EvaluationWorkspace();
                output = DeformationPipeline.CreatePreviewMeshFromInput(mesh, input, PreserveChannels, workspace);
                var entries = workspace.Lattice.Cache.Entries;
                Assert.That(entries.Length, Is.EqualTo(vertexCount));
                Object.DestroyImmediate(output);
                output = DeformationPipeline.CreatePreviewMeshFromInput(mesh, input, PreserveChannels, workspace);
                Assert.That(workspace.Lattice.Cache.Entries, Is.SameAs(entries),
                    "Upstream frames must not rebuild the base cache between preview updates.");
                Assert.That(workspace.Lattice.HasNativeResources, Is.True);
                workspace.Dispose();
                Assert.That(workspace.Lattice.HasNativeResources, Is.False,
                    "Dispose releases the subset scratch together with the base buffers.");
            }
            finally
            {
                if (output != null) Object.DestroyImmediate(output);
                Object.DestroyImmediate(mesh);
            }
        }

        // The algorithm the sparse path replaced: a whole-mesh evaluation per frame.
        private static Mesh BuildReference(Mesh mesh, in DeformationEvaluationInput input,
            EvaluationWorkspace workspace, out Vector3[] baseVertices, out int generatedCount)
        {
            int vertexCount = mesh.vertexCount;
            var inputVertices = mesh.vertices;
            baseVertices = new Vector3[vertexCount];
            var generated = new List<GeneratedBlendShapeOutput>();
            DeformationEvaluator.Evaluate(input, inputVertices, baseVertices, workspace, generated);
            generatedCount = generated.Count;
            var reference = new Mesh { vertices = baseVertices };
            var delta = new Vector3[vertexCount];
            var combined = new Vector3[vertexCount];
            var deformed = new Vector3[vertexCount];
            var outputDelta = new Vector3[vertexCount];
            for (int shape = 0; shape < mesh.blendShapeCount; shape++)
            {
                for (int frame = 0; frame < mesh.GetBlendShapeFrameCount(shape); frame++)
                {
                    mesh.GetBlendShapeFrameVertices(shape, frame, delta, null, null);
                    for (int vertex = 0; vertex < vertexCount; vertex++)
                        combined[vertex] = inputVertices[vertex] + delta[vertex];
                    DeformationEvaluator.Evaluate(input, combined, deformed, workspace, null);
                    for (int vertex = 0; vertex < vertexCount; vertex++)
                        outputDelta[vertex] = deformed[vertex] - baseVertices[vertex];
                    // Store through Unity as the preview does, so storage rules
                    // apply identically to both sides of the comparison.
                    reference.AddBlendShapeFrame(mesh.GetBlendShapeName(shape),
                        mesh.GetBlendShapeFrameWeight(shape, frame), outputDelta, null, null);
                }
            }
            return reference;
        }

        private static void CompareFrames(Mesh source, Mesh reference, Mesh output, string label)
        {
            int vertexCount = source.vertexCount;
            var expected = new Vector3[vertexCount];
            var actual = new Vector3[vertexCount];
            for (int shape = 0; shape < source.blendShapeCount; shape++)
            {
                Assert.That(output.GetBlendShapeName(shape), Is.EqualTo(source.GetBlendShapeName(shape)));
                int frames = source.GetBlendShapeFrameCount(shape);
                Assert.That(output.GetBlendShapeFrameCount(shape), Is.EqualTo(frames));
                for (int frame = 0; frame < frames; frame++)
                {
                    Assert.That(output.GetBlendShapeFrameWeight(shape, frame),
                        Is.EqualTo(source.GetBlendShapeFrameWeight(shape, frame)));
                    reference.GetBlendShapeFrameVertices(shape, frame, expected, null, null);
                    output.GetBlendShapeFrameVertices(shape, frame, actual, null, null);
                    for (int vertex = 0; vertex < vertexCount; vertex++)
                        AssertSameBits(expected[vertex], actual[vertex],
                            $"{label} shape {source.GetBlendShapeName(shape)} frame {frame} vertex {vertex}");
                }
            }
        }

        private static Mesh CreateUpstreamMesh(Random random, int vertexCount)
        {
            var mesh = new Mesh { vertices = CreatePositions(random, vertexCount), triangles = Triangles(vertexCount) };
            int half = vertexCount / 2;
            mesh.AddBlendShapeFrame("Empty", 100f, new Vector3[vertexCount], null, null);
            mesh.AddBlendShapeFrame("Sparse", 100f, Deltas(random, vertexCount, 5), null, null);
            // The sparse limit is half the vertices; one more selects the whole mesh.
            mesh.AddBlendShapeFrame("AtLimit", 100f, Deltas(random, vertexCount, half), null, null);
            mesh.AddBlendShapeFrame("OverLimit", 100f, Deltas(random, vertexCount, half + 1), null, null);
            mesh.AddBlendShapeFrame("Dense", 100f, Deltas(random, vertexCount, vertexCount), null, null);
            var signed = new Vector3[vertexCount];
            for (int vertex = 0; vertex < vertexCount; vertex += 3)
                signed[vertex] = new Vector3(-0f, 0f, vertex % 2 == 0 ? -0f : 0.01f);
            mesh.AddBlendShapeFrame("SignedZero", 100f, signed, null, null);
            foreach (float weight in new[] { 25f, 50f, 100f })
                mesh.AddBlendShapeFrame("MultiFrame", weight, Deltas(random, vertexCount, vertexCount / 5), null, null);
            mesh.AddBlendShapeFrame("Tail", 100f, Deltas(random, vertexCount, 40), null, null);
            return mesh;
        }

        private static DeformationEvaluationInput CreateInput(string scenario, Random random, int count)
        {
            var groups = new List<DeformerGroup>();
            bool published = false;
            bool absolute = false;
            var worldToLocal = Matrix4x4.identity;
            switch (scenario)
            {
                case "BrushMask":
                    groups.Add(Group(
                        Brush(random, count, count, 0.7f),
                        Brush(random, count, -1, 1f),
                        Brush(random, count + 1, count, 1f),
                        Brush(random, count, count - 1, 0.35f)));
                    break;
                case "Trilinear":
                    groups.Add(Group(Lattice(random, LatticeBounds, new Vector3Int(3, 4, 2),
                        LatticeInterpolationMode.Trilinear, false, 0.8f)));
                    break;
                case "Cubic":
                    groups.Add(Group(Lattice(random, LatticeBounds, new Vector3Int(4, 3, 5),
                        LatticeInterpolationMode.CubicBernstein, false, 0.9f)));
                    break;
                case "LegacyTrilinear":
                    groups.Add(Group(Lattice(random, LatticeBounds, new Vector3Int(4, 3, 5),
                        LatticeInterpolationMode.CubicBernstein, true, 1f)));
                    break;
                case "AbsoluteLocal":
                    absolute = true;
                    groups.Add(Group(
                        Lattice(random, LatticeBounds, new Vector3Int(3, 3, 3), LatticeInterpolationMode.Trilinear, false, 0.6f),
                        Brush(random, count, count, 0.5f)));
                    break;
                case "AbsoluteWorld":
                {
                    absolute = true;
                    var owner = Matrix4x4.TRS(new Vector3(0.4f, -1.2f, 2.5f), Quaternion.Euler(12f, 35f, -7f),
                        new Vector3(1.1f, 0.9f, 1.3f));
                    worldToLocal = owner.inverse;
                    var layer = Lattice(random, LatticeBounds, new Vector3Int(3, 4, 3),
                        LatticeInterpolationMode.CubicBernstein, false, 0.75f);
                    var settings = layer.SerializedSettings;
                    for (int i = 0; i < settings.ControlPointCount; i++)
                        settings.SetControlPointLocal(i, owner.MultiplyPoint3x4(settings.GetControlPointLocal(i)));
                    typeof(LatticeAsset).GetField("_applySpace", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(settings, 1);
                    groups.Add(Group(layer));
                    break;
                }
                case "PublishedSemantics":
                    published = true;
                    AddMultiGroups(groups, random, count);
                    break;
                case "MultiGroup":
                    AddMultiGroups(groups, random, count);
                    break;
                case "SharedCacheKey":
                {
                    // Approximately equal bounds reuse the previous layer's entries
                    // in the whole-mesh cache; the subset path mirrors that rule.
                    var nearby = new Bounds(LatticeBounds.center + new Vector3(2e-6f, -1e-6f, 0f),
                        LatticeBounds.size + new Vector3(0f, 3e-6f, 0f));
                    groups.Add(Group(
                        Lattice(random, LatticeBounds, new Vector3Int(3, 3, 3), LatticeInterpolationMode.Trilinear, false, 1f),
                        Lattice(random, nearby, new Vector3Int(3, 3, 3), LatticeInterpolationMode.Trilinear, false, 0.8f),
                        Lattice(random, nearby, new Vector3Int(3, 3, 3), LatticeInterpolationMode.CubicBernstein, false, 0.7f),
                        Lattice(random, LatticeBounds, new Vector3Int(3, 3, 3), LatticeInterpolationMode.CubicBernstein, false, 0.6f),
                        Lattice(random, nearby, new Vector3Int(3, 3, 3), LatticeInterpolationMode.Trilinear, false, 0.5f)));
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
            }

            return new DeformationEvaluationInput(groups, "Fallback",
                new EvaluationSemantics(published, absolute, worldToLocal));
        }

        private static void AddMultiGroups(List<DeformerGroup> groups, Random random, int count)
        {
            var disabledLayer = Brush(random, count, count, 1f);
            disabledLayer.Enabled = false;
            var zeroWeight = Lattice(random, LatticeBounds, new Vector3Int(2, 2, 2),
                LatticeInterpolationMode.Trilinear, false, 1f);
            zeroWeight.Weight = 0f;
            var individual = Brush(random, count, -1, 0.9f);
            individual.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
            individual.BlendShapeName = "Individual";
            groups.Add(Group(
                Brush(random, count, count, 0.6f),
                Lattice(random, LatticeBounds, new Vector3Int(3, 3, 3), LatticeInterpolationMode.Trilinear, false, 0.8f),
                null,
                disabledLayer,
                zeroWeight,
                individual));

            var output = Group(
                Brush(random, count, -1, 1f),
                Lattice(random, LatticeBounds, new Vector3Int(2, 3, 2), LatticeInterpolationMode.CubicBernstein, false, 1f));
            output.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
            output.BlendShapeName = "GroupOutput";
            groups.Add(output);

            var disabledGroup = Group(Brush(random, count, -1, 1f));
            disabledGroup.Enabled = false;
            groups.Add(disabledGroup);
            groups.Add(null);

            groups.Add(Group(
                Lattice(random, LatticeBounds, new Vector3Int(4, 4, 4), LatticeInterpolationMode.CubicBernstein, false, 0.7f),
                Brush(random, count, -1, 0.4f),
                Lattice(random, LatticeBounds, new Vector3Int(4, 4, 4), LatticeInterpolationMode.CubicBernstein, true, 0.5f)));
        }

        private static DeformerGroup Group(params LatticeLayer[] layers)
        {
            var group = new DeformerGroup();
            group.LayersList.AddRange(layers);
            return group;
        }

        // maskLength < 0 leaves the mask empty; a length other than the vertex
        // count is ignored by evaluation, as is a mismatched displacement count.
        private static LatticeLayer Brush(Random random, int displacementCount, int maskLength, float weight)
        {
            var layer = new LatticeLayer();
            layer.SetType(MeshDeformerLayerType.Brush);
            layer.Weight = weight;
            var displacements = new Vector3[displacementCount];
            for (int i = 0; i < displacementCount; i++)
                displacements[i] = i % 5 == 0 ? Vector3.zero : RandomVector(random, 0.2f);
            layer.BrushDisplacements = displacements;
            if (maskLength >= 0)
            {
                var mask = new float[maskLength];
                for (int i = 0; i < maskLength; i++) mask[i] = i % 7 == 0 ? 0f : (float)random.NextDouble();
                layer.VertexMask = mask;
            }
            return layer;
        }

        private static LatticeLayer Lattice(Random random, Bounds bounds, Vector3Int grid,
            LatticeInterpolationMode interpolation, bool legacyTrilinear, float weight)
        {
            var layer = new LatticeLayer();
            layer.SetType(MeshDeformerLayerType.Lattice);
            layer.Weight = weight;
            var settings = layer.Settings;
            settings.LocalBounds = bounds;
            settings.GridSize = grid;
            settings.ResetControlPoints();
            settings.Interpolation = interpolation;
            settings.SetLegacyTrilinearInterpolation(legacyTrilinear);
            for (int i = 0; i < settings.ControlPointCount; i++)
                settings.SetControlPointLocal(i, settings.GetControlPointLocal(i) + RandomVector(random, 0.15f));
            return layer;
        }

        private static Vector3[] CreatePositions(Random random, int count)
        {
            var positions = new Vector3[count];
            for (int i = 0; i < count; i++)
                positions[i] = LatticeBounds.center + RandomVector(random, 1.4f);
            // Signed zeros, exact bounds corners and far points exercise clamping.
            positions[0] = new Vector3(-0f, 0f, -0f);
            positions[1] = LatticeBounds.min;
            positions[2] = LatticeBounds.max;
            positions[3] = new Vector3(40f, -25f, 3f);
            positions[4] = new Vector3(0f, -0f, 0.5f);
            return positions;
        }

        private static Vector3[] Deltas(Random random, int vertexCount, int changed)
        {
            var deltas = new Vector3[vertexCount];
            foreach (int vertex in PickIndices(random, vertexCount, changed, changed))
            {
                var delta = RandomVector(random, 0.3f);
                if (delta.x == 0f && delta.y == 0f && delta.z == 0f) delta.x = 0.01f;
                deltas[vertex] = delta;
            }
            return deltas;
        }

        private static int[] PickIndices(Random random, int vertexCount, int count, int length)
        {
            var all = new int[vertexCount];
            for (int i = 0; i < vertexCount; i++) all[i] = i;
            for (int i = vertexCount - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (all[i], all[j]) = (all[j], all[i]);
            }
            var result = new int[length];
            Array.Copy(all, result, count);
            return result;
        }

        private static int[] Triangles(int vertexCount)
        {
            var triangles = new int[vertexCount - vertexCount % 3];
            for (int i = 0; i < triangles.Length; i++) triangles[i] = i;
            return triangles;
        }

        private static Vector3 RandomVector(Random random, float extent) => new Vector3(
            (float)(random.NextDouble() * 2.0 - 1.0) * extent,
            (float)(random.NextDouble() * 2.0 - 1.0) * extent,
            (float)(random.NextDouble() * 2.0 - 1.0) * extent);

        private static int StableSeed(string text)
        {
            int seed = 7;
            foreach (char c in text) seed = seed * 31 + c;
            return seed;
        }

        private static void AssertSameBits(Vector3 expected, Vector3 actual, string label)
        {
            Assert.That(BitConverter.SingleToInt32Bits(actual.x), Is.EqualTo(BitConverter.SingleToInt32Bits(expected.x)),
                $"{label} x: expected {expected.x:R}, actual {actual.x:R}");
            Assert.That(BitConverter.SingleToInt32Bits(actual.y), Is.EqualTo(BitConverter.SingleToInt32Bits(expected.y)),
                $"{label} y: expected {expected.y:R}, actual {actual.y:R}");
            Assert.That(BitConverter.SingleToInt32Bits(actual.z), Is.EqualTo(BitConverter.SingleToInt32Bits(expected.z)),
                $"{label} z: expected {expected.z:R}, actual {actual.z:R}");
        }
    }
}
#endif
