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
    // Reuse optimizations must reproduce the replaced algorithms bit for bit.
    // Each reference below is the previous implementation, kept in the test.
    public sealed class EvaluationReuseEquivalenceTests
    {
        private static readonly Bounds LatticeBounds =
            new Bounds(new Vector3(0.1f, -0.05f, 0.02f), new Vector3(2f, 1.6f, 1.8f));

        // ----- Generated BlendShape normal/tangent deltas -----

        [TestCase(NormalsRecalculationMode.LegacyUnityRecalculate)]
        [TestCase(NormalsRecalculationMode.PreserveSourceSmoothing)]
        public void GeneratedSurfaceDeltas_MatchPerCandidateInstantiationBitwise(NormalsRecalculationMode normalsMode)
        {
            var channelFlags = new[] { (true, false), (false, true), (true, true) };
            var scenarios = new[] { "Single", "Progressive", "Crossfade", "CrossfadeWeights", "ProgressiveOvershoot" };
            foreach (var (normals, tangents) in channelFlags)
            {
                foreach (string scenario in scenarios)
                {
                    var options = new MeshOutputOptions(normals, tangents, false, normalsMode, false);
                    string label = $"{normalsMode} normals={normals} tangents={tangents} {scenario}";
                    var random = new Random(StableSeed(label));
                    var expectedMesh = CreateSurfaceMesh();
                    var actualMesh = CreateSurfaceMesh();
                    try
                    {
                        int vertexCount = actualMesh.vertexCount;
                        var baseVertices = expectedMesh.vertices;
                        for (int i = 0; i < vertexCount; i++)
                            baseVertices[i] += RandomVector(random, 0.05f);
                        // Two shapes on one mesh: the second template already
                        // carries the first shape's appended frames.
                        var first = CreateGenerated(scenario, random, vertexCount, "First");
                        var second = CreateGenerated("Progressive", random, vertexCount, "Second");

                        var referenceWorkspace = new MeshOutputWorkspace();
                        LegacyAddGeneratedBlendShapeFrames(expectedMesh, "First", baseVertices, first, options, referenceWorkspace);
                        LegacyAddGeneratedBlendShapeFrames(expectedMesh, "Second", baseVertices, second, options, referenceWorkspace);
                        var workspace = new MeshOutputWorkspace();
                        DeformedMeshWriter.AddGeneratedBlendShapeFrames(actualMesh, "First", baseVertices, first, options, workspace);
                        DeformedMeshWriter.AddGeneratedBlendShapeFrames(actualMesh, "Second", baseVertices, second, options, workspace);

                        CompareAllFrames(expectedMesh, actualMesh, label);
                        AssertSameBits(expectedMesh.vertices, actualMesh.vertices, label + " template vertices");
                        AssertSameBits(expectedMesh.normals, actualMesh.normals, label + " template normals");
                    }
                    finally
                    {
                        Object.DestroyImmediate(expectedMesh);
                        Object.DestroyImmediate(actualMesh);
                    }
                }
            }
        }

        [Test]
        public void GeneratedSurfaceDeltas_SingleCallMatchesPreviousImplementation()
        {
            foreach (var mode in new[] { NormalsRecalculationMode.LegacyUnityRecalculate, NormalsRecalculationMode.PreserveSourceSmoothing })
            {
                var template = CreateSurfaceMesh();
                try
                {
                    var random = new Random(StableSeed(mode.ToString()));
                    var baseVertices = template.vertices;
                    var deltas = new Vector3[baseVertices.Length];
                    for (int i = 0; i < deltas.Length; i++) deltas[i] = RandomVector(random, 0.2f);
                    LegacyCalculateSurfaceDeltas(template, baseVertices, deltas, mode, true, true,
                        out var expectedNormals, out var expectedTangents);
                    DeformedMeshWriter.CalculateGeneratedSurfaceDeltasWithNormalsMode(template, baseVertices, deltas,
                        mode, true, true, out var actualNormals, out var actualTangents);
                    AssertSameBits(expectedNormals, actualNormals, mode + " normals");
                    AssertSameBits(expectedTangents, actualTangents, mode + " tangents");
                }
                finally
                {
                    Object.DestroyImmediate(template);
                }
            }
        }

        // The implementation that instantiated the template per candidate/frame.
        private static void LegacyAddGeneratedBlendShapeFrames(Mesh mesh, string shapeName, Vector3[] baseVertices,
            GeneratedBlendShapeOutput generated, in MeshOutputOptions options, MeshOutputWorkspace workspace)
        {
            var candidates = generated.Candidates;
            int vertexCount = mesh.vertexCount;
            var curve = generated.Curve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);
            Vector3[][] candidateDeltaNormals = null;
            Vector3[][] candidateDeltaTangents = null;
            bool direct = generated.CandidateWeights != null && generated.CandidateWeights.Length == candidates.Length;
            bool recompute = !options.LegacyPublishedBlendShapes &&
                             generated.Composition != BlendShapeCompositionMode.Single &&
                             (options.RecalculateNormals || options.RecalculateTangents);
            if ((direct || !recompute) && !options.LegacyPublishedBlendShapes &&
                (options.RecalculateNormals || options.RecalculateTangents))
            {
                candidateDeltaNormals = options.RecalculateNormals ? new Vector3[candidates.Length][] : null;
                candidateDeltaTangents = options.RecalculateTangents ? new Vector3[candidates.Length][] : null;
                for (int candidate = 0; candidate < candidates.Length; candidate++)
                {
                    LegacyCalculateSurfaceDeltas(mesh, baseVertices, candidates[candidate], options.NormalsMode,
                        options.RecalculateNormals, options.RecalculateTangents, out var n, out var t);
                    if (candidateDeltaNormals != null) candidateDeltaNormals[candidate] = n;
                    if (candidateDeltaTangents != null) candidateDeltaTangents[candidate] = t;
                }
            }

            if (direct)
            {
                for (int candidate = 0; candidate < candidates.Length; candidate++)
                    mesh.AddBlendShapeFrame(shapeName, generated.CandidateWeights[candidate], candidates[candidate],
                        candidateDeltaNormals?[candidate], candidateDeltaTangents?[candidate]);
                return;
            }

            workspace.EnsureFrameCapacity(vertexCount, candidateDeltaNormals != null, candidateDeltaTangents != null);
            for (int f = 0; f < 100; f++)
            {
                float progress = (f + 1f) / 100;
                float curveValue = curve.Evaluate(progress);
                if (generated.Composition != BlendShapeCompositionMode.Single) curveValue = Mathf.Clamp01(curveValue);
                var frameDeltas = BlendShapeComposer.Compose(candidates, generated.Composition, curveValue,
                    workspace.FrameVertices);
                Vector3[] frameNormals;
                Vector3[] frameTangents;
                if (recompute)
                {
                    LegacyCalculateSurfaceDeltas(mesh, baseVertices, frameDeltas, options.NormalsMode,
                        options.RecalculateNormals, options.RecalculateTangents, out frameNormals, out frameTangents);
                }
                else
                {
                    frameNormals = candidateDeltaNormals != null
                        ? BlendShapeComposer.Compose(candidateDeltaNormals, generated.Composition, curveValue, workspace.FrameNormals)
                        : null;
                    frameTangents = candidateDeltaTangents != null
                        ? BlendShapeComposer.Compose(candidateDeltaTangents, generated.Composition, curveValue, workspace.FrameTangents)
                        : null;
                }

                mesh.AddBlendShapeFrame(shapeName, progress * 100f, frameDeltas, frameNormals, frameTangents);
            }
        }

        private static void LegacyCalculateSurfaceDeltas(Mesh template, Vector3[] baseVertices, Vector3[] deltas,
            NormalsRecalculationMode normalsMode, bool includeNormals, bool includeTangents,
            out Vector3[] deltaNormals, out Vector3[] deltaTangents)
        {
            deltaNormals = null;
            deltaTangents = null;
            Mesh baseMesh = null;
            Mesh targetMesh = null;
            try
            {
                baseMesh = Object.Instantiate(template);
                targetMesh = Object.Instantiate(template);
                int vertexCount = baseVertices.Length;
                var targetVertices = new Vector3[vertexCount];
                for (int i = 0; i < vertexCount; i++) targetVertices[i] = baseVertices[i] + deltas[i];
                baseMesh.vertices = baseVertices;
                targetMesh.vertices = targetVertices;
                if (includeNormals)
                {
                    LegacyRecalculateNormals(baseMesh, template, normalsMode);
                    LegacyRecalculateNormals(targetMesh, template, normalsMode);
                    var baseNormals = baseMesh.normals;
                    var targetNormals = targetMesh.normals;
                    deltaNormals = new Vector3[vertexCount];
                    for (int i = 0; i < vertexCount; i++) deltaNormals[i] = targetNormals[i] - baseNormals[i];
                }

                if (includeTangents)
                {
                    LegacyRecalculateNormals(baseMesh, template, normalsMode);
                    LegacyRecalculateNormals(targetMesh, template, normalsMode);
                    baseMesh.RecalculateTangents();
                    targetMesh.RecalculateTangents();
                    var baseTangents = baseMesh.tangents;
                    var targetTangents = targetMesh.tangents;
                    deltaTangents = new Vector3[vertexCount];
                    for (int i = 0; i < vertexCount; i++)
                        deltaTangents[i] = new Vector3(targetTangents[i].x - baseTangents[i].x,
                            targetTangents[i].y - baseTangents[i].y, targetTangents[i].z - baseTangents[i].z);
                }
            }
            finally
            {
                if (baseMesh != null) Object.DestroyImmediate(baseMesh);
                if (targetMesh != null) Object.DestroyImmediate(targetMesh);
            }
        }

        private static void LegacyRecalculateNormals(Mesh mesh, Mesh source, NormalsRecalculationMode mode)
        {
            if (mode == NormalsRecalculationMode.PreserveSourceSmoothing)
                mesh.SetNormals(SeamAwareMeshNormalCalculator.Calculate(mesh, source));
            else
                mesh.RecalculateNormals();
        }

        private static GeneratedBlendShapeOutput CreateGenerated(string scenario, Random random, int vertexCount, string name)
        {
            int count = scenario == "Single" ? 1 : 3;
            var candidates = new Vector3[count][];
            for (int c = 0; c < count; c++)
            {
                candidates[c] = new Vector3[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                    candidates[c][i] = i % 4 == 0 ? Vector3.zero : RandomVector(random, 0.25f);
            }

            switch (scenario)
            {
                case "Single":
                    return new GeneratedBlendShapeOutput(name, null, candidates[0]);
                case "Progressive":
                    return new GeneratedBlendShapeOutput(name, null, BlendShapeCompositionMode.Progressive, candidates);
                case "Crossfade":
                    return new GeneratedBlendShapeOutput(name, null, BlendShapeCompositionMode.Crossfade, candidates);
                case "CrossfadeWeights":
                    return new GeneratedBlendShapeOutput(name, null, BlendShapeCompositionMode.Crossfade, candidates,
                        new[] { 20f, 55f, 100f });
                case "ProgressiveOvershoot":
                    var curve = new AnimationCurve(new Keyframe(0f, -0.2f), new Keyframe(0.5f, 1.3f), new Keyframe(1f, 0.9f));
                    return new GeneratedBlendShapeOutput(name, curve, BlendShapeCompositionMode.Progressive, candidates);
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
            }
        }

        // Two patches share a duplicated seam column with equal positions and
        // normals but different UVs, so the seam-aware rule merges them. The
        // mesh also carries source BlendShapes, two submeshes and 16-bit indices.
        private static Mesh CreateSurfaceMesh()
        {
            const int rows = 5;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var submeshes = new[] { new List<int>(), new List<int>() };
            for (int patch = 0; patch < 2; patch++)
            {
                int start = vertices.Count;
                for (int y = 0; y < rows; y++)
                {
                    for (int local = 0; local < 3; local++)
                    {
                        float x = patch * 2 + local;
                        float z = 0.3f * Mathf.Sin(x * 0.9f + y * 0.7f);
                        vertices.Add(new Vector3(x * 0.5f, y * 0.5f, z));
                        float dzdx = 0.27f * Mathf.Cos(x * 0.9f + y * 0.7f) * 2f;
                        float dzdy = 0.21f * Mathf.Cos(x * 0.9f + y * 0.7f) * 2f;
                        normals.Add(new Vector3(-dzdx, -dzdy, 1f).normalized);
                        uvs.Add(new Vector2(patch * 0.5f + local * 0.2f, y * 0.25f));
                    }
                }

                for (int y = 0; y < rows - 1; y++)
                {
                    for (int local = 0; local < 2; local++)
                    {
                        int a = start + y * 3 + local;
                        submeshes[patch].AddRange(new[] { a, a + 1, a + 3, a + 1, a + 4, a + 3 });
                    }
                }
            }

            var mesh = new Mesh { name = "Surface delta template" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(submeshes[0], 0);
            mesh.SetTriangles(submeshes[1], 1);
            mesh.RecalculateTangents();
            int count = vertices.Count;
            var shapeDeltas = new Vector3[count];
            var shapeNormals = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                shapeDeltas[i] = new Vector3(0f, 0.01f * i, 0.02f);
                shapeNormals[i] = new Vector3(0.01f, 0f, 0f);
            }
            mesh.AddBlendShapeFrame("Source", 100f, shapeDeltas, shapeNormals, null);
            return mesh;
        }

        private static void CompareAllFrames(Mesh expected, Mesh actual, string label)
        {
            Assert.That(actual.blendShapeCount, Is.EqualTo(expected.blendShapeCount), label);
            int vertexCount = expected.vertexCount;
            var expectedVertices = new Vector3[vertexCount];
            var expectedNormals = new Vector3[vertexCount];
            var expectedTangents = new Vector3[vertexCount];
            var actualVertices = new Vector3[vertexCount];
            var actualNormals = new Vector3[vertexCount];
            var actualTangents = new Vector3[vertexCount];
            for (int shape = 0; shape < expected.blendShapeCount; shape++)
            {
                Assert.That(actual.GetBlendShapeName(shape), Is.EqualTo(expected.GetBlendShapeName(shape)), label);
                int frames = expected.GetBlendShapeFrameCount(shape);
                Assert.That(actual.GetBlendShapeFrameCount(shape), Is.EqualTo(frames), label);
                for (int frame = 0; frame < frames; frame++)
                {
                    string frameLabel = $"{label} shape {shape} frame {frame}";
                    Assert.That(BitConverter.SingleToInt32Bits(actual.GetBlendShapeFrameWeight(shape, frame)),
                        Is.EqualTo(BitConverter.SingleToInt32Bits(expected.GetBlendShapeFrameWeight(shape, frame))), frameLabel);
                    expected.GetBlendShapeFrameVertices(shape, frame, expectedVertices, expectedNormals, expectedTangents);
                    actual.GetBlendShapeFrameVertices(shape, frame, actualVertices, actualNormals, actualTangents);
                    AssertSameBits(expectedVertices, actualVertices, frameLabel + " vertices");
                    AssertSameBits(expectedNormals, actualNormals, frameLabel + " normals");
                    AssertSameBits(expectedTangents, actualTangents, frameLabel + " tangents");
                }
            }
        }

        // ----- Lattice interpolation cache stash -----

        [TestCase("DistinctConfigs")]
        [TestCase("NearEqualBounds")]
        [TestCase("SingleLattice")]
        [TestCase("IdenticalLayers")]
        public void LatticeCacheStash_MatchesPublishedSingleCacheBitwise(string scenario)
        {
            const int vertexCount = 211;
            var random = new Random(StableSeed(scenario));
            var positionsA = CreatePositions(random, vertexCount);
            var positionsB = CreatePositions(random, vertexCount);
            var shared = (Vector3[])positionsA.Clone();
            var input = CreateInput(scenario, random, out var adjustable);
            var originalBounds = adjustable.LocalBounds;
            var nearBounds = new Bounds(originalBounds.center + new Vector3(3e-6f, 0f, -2e-6f), originalBounds.size);
            var farBounds = new Bounds(originalBounds.center + new Vector3(0.05f, 0f, 0f), originalBounds.size);

            using var stashed = new EvaluationWorkspace();
            using var reference = new EvaluationWorkspace();
            reference.Lattice.StashCapacity = 0;
            var stashedGenerated = new List<GeneratedBlendShapeOutput>();
            var referenceGenerated = new List<GeneratedBlendShapeOutput>();

            // Settings and positions change between steps the way authoring and
            // upstream preview frames do, including near-equal bounds that the
            // published rule reuses and a buffer rewritten in place.
            var steps = new (Vector3[] positions, Bounds bounds, bool mutateShared, bool dispose)[]
            {
                (positionsA, originalBounds, false, false),
                (positionsA, originalBounds, false, false),
                (positionsB, originalBounds, false, false),
                (positionsA, originalBounds, false, false),
                (positionsA, nearBounds, false, false),
                (positionsA, originalBounds, false, false),
                (positionsA, farBounds, false, false),
                (positionsA, originalBounds, false, false),
                (shared, originalBounds, false, false),
                (shared, originalBounds, true, false),
                (shared, originalBounds, false, false),
                (positionsA, originalBounds, false, true),
                (positionsA, originalBounds, false, false),
                (positionsB, nearBounds, false, false),
                (positionsB, originalBounds, false, false),
                (positionsA, originalBounds, false, false),
            };

            for (int step = 0; step < steps.Length; step++)
            {
                var (positions, bounds, mutateShared, dispose) = steps[step];
                adjustable.LocalBounds = bounds;
                if (mutateShared)
                {
                    for (int i = 0; i < shared.Length; i += 3) shared[i] += new Vector3(0.01f, -0.02f, 0.005f);
                }

                if (dispose)
                {
                    stashed.Dispose();
                    reference.Dispose();
                    Assert.That(stashed.Lattice.StashedCacheCount, Is.Zero, "Dispose releases every stashed cache.");
                }

                var expected = new Vector3[vertexCount];
                var actual = new Vector3[vertexCount];
                DeformationEvaluator.Evaluate(input, positions, expected, reference, referenceGenerated);
                DeformationEvaluator.Evaluate(input, positions, actual, stashed, stashedGenerated);
                AssertSameBits(expected, actual, $"{scenario} step {step}");
                Assert.That(stashedGenerated.Count, Is.EqualTo(referenceGenerated.Count), $"{scenario} step {step} generated");
                for (int shape = 0; shape < referenceGenerated.Count; shape++)
                {
                    var expectedCandidates = referenceGenerated[shape].Candidates;
                    var actualCandidates = stashedGenerated[shape].Candidates;
                    Assert.That(actualCandidates.Length, Is.EqualTo(expectedCandidates.Length));
                    for (int c = 0; c < expectedCandidates.Length; c++)
                        AssertSameBits(expectedCandidates[c], actualCandidates[c], $"{scenario} step {step} shape {shape} candidate {c}");
                }

                Assert.That(stashed.Lattice.StashedCacheCount, Is.LessThanOrEqualTo(LatticeEvaluator.DefaultStashCapacity));
                Assert.That(reference.Lattice.StashedCacheCount, Is.Zero);
            }
        }

        [Test]
        public void LatticeCacheStash_RestoresDistinctLayerEntriesInsteadOfRebuilding()
        {
            const int vertexCount = 97;
            var random = new Random(41);
            var positions = CreatePositions(random, vertexCount);
            var input = new DeformationEvaluationInput(new List<DeformerGroup>
            {
                Group(
                    Lattice(random, LatticeBounds, new Vector3Int(3, 3, 3), LatticeInterpolationMode.Trilinear, 0.8f),
                    Lattice(random, LatticeBounds, new Vector3Int(4, 3, 2), LatticeInterpolationMode.CubicBernstein, 0.6f))
            }, "Fallback", new EvaluationSemantics(false));
            foreach (bool enabled in new[] { true, false })
            {
                using var workspace = new EvaluationWorkspace();
                if (!enabled) workspace.Lattice.StashCapacity = 0;
                var output = new Vector3[vertexCount];
                DeformationEvaluator.Evaluate(input, positions, output, workspace, null);
                var lastEntries = workspace.Lattice.Cache.Entries;
                DeformationEvaluator.Evaluate(input, positions, output, workspace, null);
                if (enabled)
                {
                    Assert.That(workspace.Lattice.Cache.Entries, Is.SameAs(lastEntries),
                        "Each layer restores its own stashed entries on the next evaluation.");
                    Assert.That(workspace.Lattice.StashedCacheCount, Is.EqualTo(1));
                }
                else
                {
                    Assert.That(workspace.Lattice.Cache.Entries, Is.Not.SameAs(lastEntries),
                        "Without the stash every layer rebuilds its entries.");
                }
            }
        }

        [Test]
        public void LatticeCacheStash_IsBoundedAndReleasedByInvalidation()
        {
            const int vertexCount = 40;
            var random = new Random(7);
            var positions = CreatePositions(random, vertexCount);
            var layers = new List<LatticeLayer>();
            for (int i = 0; i < LatticeEvaluator.DefaultStashCapacity + 3; i++)
                layers.Add(Lattice(random, LatticeBounds, new Vector3Int(2 + i % 4, 2 + i / 4, 2),
                    LatticeInterpolationMode.Trilinear, 0.5f));
            var input = new DeformationEvaluationInput(new List<DeformerGroup> { Group(layers.ToArray()) }, "Fallback",
                new EvaluationSemantics(false));
            using var workspace = new EvaluationWorkspace();
            var output = new Vector3[vertexCount];
            DeformationEvaluator.Evaluate(input, positions, output, workspace, null);
            Assert.That(workspace.Lattice.StashedCacheCount, Is.EqualTo(LatticeEvaluator.DefaultStashCapacity));
            workspace.Lattice.Dispose();
            Assert.That(workspace.Lattice.StashedCacheCount, Is.Zero);
            Assert.That(workspace.Lattice.HasNativeResources, Is.False);
        }

        [Test]
        public void LatticeCacheStash_ReleasedAfterInactiveComponentEvaluation()
        {
            var root = new GameObject("Inactive stash owner");
            var mesh = DeformationOutputBaselineFixture.CreateMesh(3);
            try
            {
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                root.AddComponent<MeshRenderer>();
                var deformer = root.AddComponent<LatticeDeformer>();
                deformer.Reset();
                deformer.AddLayer("Second", MeshDeformerLayerType.Lattice);
                deformer.Layers[deformer.Layers.Count - 1].Settings.GridSize = new Vector3Int(4, 2, 3);
                root.SetActive(false);
                Assert.That(deformer.Deform(false), Is.Not.Null);
                var workspace = (EvaluationWorkspace)typeof(LatticeDeformer)
                    .GetField("_evaluationWorkspace", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(deformer);
                Assert.That(workspace.Lattice.HasNativeResources, Is.False);
                Assert.That(workspace.Lattice.StashedCacheCount, Is.Zero,
                    "An inactive evaluation releases its stashed caches with the native scratch.");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
            }
        }

        private static DeformationEvaluationInput CreateInput(string scenario, Random random, out LatticeAsset adjustable)
        {
            var nearby = new Bounds(LatticeBounds.center + new Vector3(2e-6f, -1e-6f, 0f),
                LatticeBounds.size + new Vector3(0f, 3e-6f, 0f));
            var groups = new List<DeformerGroup>();
            LatticeLayer first;
            switch (scenario)
            {
                case "DistinctConfigs":
                {
                    first = Lattice(random, LatticeBounds, new Vector3Int(3, 3, 3), LatticeInterpolationMode.Trilinear, 0.8f);
                    var brush = Brush(random, 211, 0.4f);
                    var cubic = Lattice(random, new Bounds(Vector3.zero, Vector3.one * 2.5f), new Vector3Int(4, 3, 5),
                        LatticeInterpolationMode.CubicBernstein, 0.7f);
                    var individual = Lattice(random, LatticeBounds, new Vector3Int(2, 4, 2), LatticeInterpolationMode.Trilinear, 0.9f);
                    individual.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                    individual.BlendShapeName = "Individual";
                    groups.Add(Group(first, brush, cubic, individual));
                    var output = Group(
                        Lattice(random, LatticeBounds, new Vector3Int(3, 3, 3), LatticeInterpolationMode.CubicBernstein, 1f),
                        Lattice(random, nearby, new Vector3Int(3, 3, 3), LatticeInterpolationMode.Trilinear, 0.5f));
                    output.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                    output.BlendShapeComposition = BlendShapeCompositionMode.Progressive;
                    output.BlendShapeName = "Staged";
                    groups.Add(output);
                    break;
                }
                case "NearEqualBounds":
                    first = Lattice(random, LatticeBounds, new Vector3Int(3, 3, 3), LatticeInterpolationMode.Trilinear, 1f);
                    groups.Add(Group(
                        first,
                        Lattice(random, nearby, new Vector3Int(3, 3, 3), LatticeInterpolationMode.Trilinear, 0.8f),
                        Lattice(random, nearby, new Vector3Int(3, 3, 3), LatticeInterpolationMode.CubicBernstein, 0.7f),
                        Lattice(random, LatticeBounds, new Vector3Int(3, 3, 3), LatticeInterpolationMode.CubicBernstein, 0.6f),
                        Lattice(random, nearby, new Vector3Int(3, 3, 3), LatticeInterpolationMode.Trilinear, 0.5f),
                        Lattice(random, LatticeBounds, new Vector3Int(2, 2, 2), LatticeInterpolationMode.Trilinear, 0.4f)));
                    break;
                case "SingleLattice":
                    first = Lattice(random, LatticeBounds, new Vector3Int(4, 4, 4), LatticeInterpolationMode.CubicBernstein, 0.9f);
                    groups.Add(Group(first));
                    break;
                case "IdenticalLayers":
                    first = Lattice(random, LatticeBounds, new Vector3Int(3, 4, 3), LatticeInterpolationMode.Trilinear, 0.6f);
                    groups.Add(Group(first,
                        Lattice(random, LatticeBounds, new Vector3Int(3, 4, 3), LatticeInterpolationMode.Trilinear, 0.4f)));
                    groups.Add(Group(
                        Lattice(random, LatticeBounds, new Vector3Int(3, 4, 3), LatticeInterpolationMode.Trilinear, 0.3f)));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
            }

            adjustable = first.Settings;
            return new DeformationEvaluationInput(groups, "Fallback", new EvaluationSemantics(false));
        }

        private static DeformerGroup Group(params LatticeLayer[] layers)
        {
            var group = new DeformerGroup();
            group.LayersList.AddRange(layers);
            return group;
        }

        private static LatticeLayer Brush(Random random, int count, float weight)
        {
            var layer = new LatticeLayer();
            layer.SetType(MeshDeformerLayerType.Brush);
            layer.Weight = weight;
            var displacements = new Vector3[count];
            for (int i = 0; i < count; i++) displacements[i] = RandomVector(random, 0.2f);
            layer.BrushDisplacements = displacements;
            return layer;
        }

        private static LatticeLayer Lattice(Random random, Bounds bounds, Vector3Int grid,
            LatticeInterpolationMode interpolation, float weight)
        {
            var layer = new LatticeLayer();
            layer.SetType(MeshDeformerLayerType.Lattice);
            layer.Weight = weight;
            var settings = layer.Settings;
            settings.LocalBounds = bounds;
            settings.GridSize = grid;
            settings.ResetControlPoints();
            settings.Interpolation = interpolation;
            for (int i = 0; i < settings.ControlPointCount; i++)
                settings.SetControlPointLocal(i, settings.GetControlPointLocal(i) + RandomVector(random, 0.15f));
            return layer;
        }

        private static Vector3[] CreatePositions(Random random, int count)
        {
            var positions = new Vector3[count];
            for (int i = 0; i < count; i++) positions[i] = LatticeBounds.center + RandomVector(random, 1.4f);
            positions[0] = new Vector3(-0f, 0f, -0f);
            positions[1] = LatticeBounds.min;
            positions[2] = LatticeBounds.max;
            return positions;
        }

        // ----- LatticeAsset copy -----

        [Test]
        public void CloneSettings_MatchesPublishedResizeAndCopySequence()
        {
            var random = new Random(23);
            var sources = new List<(string label, LatticeAsset asset)>
            {
                ("null", null),
                ("default", Initialized(new LatticeAsset())),
                ("2x2x2", Source(new Vector3Int(2, 2, 2), 8, random, LatticeInterpolationMode.Trilinear, false, 0, 1)),
                ("5x4x3 cubic legacy world v0", Source(new Vector3Int(5, 4, 3), 60, random,
                    LatticeInterpolationMode.CubicBernstein, true, 1, 0)),
                ("3x3x3 future", Source(new Vector3Int(3, 3, 3), 27, random, LatticeInterpolationMode.Trilinear, false, 0, 2)),
                ("4x2x6 invalid apply space", Source(new Vector3Int(4, 2, 6), 48, random,
                    LatticeInterpolationMode.CubicBernstein, false, 5, 1)),
                ("unknown interpolation", Source(new Vector3Int(3, 2, 2), 12, random, (LatticeInterpolationMode)7, false, 0, 1)),
                ("short points", Source(new Vector3Int(3, 3, 3), 5, random, LatticeInterpolationMode.Trilinear, false, 0, 1)),
                ("long points", Source(new Vector3Int(2, 3, 2), 20, random, LatticeInterpolationMode.Trilinear, false, 0, 1)),
                ("flat axis", Source(new Vector3Int(1, 3, 3), 9, random, LatticeInterpolationMode.Trilinear, false, 0, 1)),
                ("empty v0", Source(new Vector3Int(4, 4, 4), 0, random, LatticeInterpolationMode.Trilinear, false, 0, 0)),
            };

            var nonFinite = Source(new Vector3Int(3, 2, 4), 24, random, LatticeInterpolationMode.Trilinear, false, 0, 1);
            nonFinite.LocalBounds = new Bounds(new Vector3(float.NaN, 0f, 1f), new Vector3(1f, float.PositiveInfinity, 2f));
            var points = (Vector3[])GetField(nonFinite, "_controlPointsLocal");
            points[1] = new Vector3(float.NaN, -0f, float.NegativeInfinity);
            points[2] = new Vector3(-0f, -0f, -0f);
            sources.Add(("non-finite", nonFinite));
            var nullPoints = Source(new Vector3Int(3, 3, 3), 27, random, LatticeInterpolationMode.Trilinear, false, 0, 1);
            SetField(nullPoints, "_controlPointsLocal", null);
            sources.Add(("null points", nullPoints));

            foreach (var (label, asset) in sources)
            {
                var expected = LegacyCloneSettings(asset);
                var actual = DeformationModelCopy.CloneSettings(asset);
                Assert.That(actual, Is.Not.SameAs(asset), label);
                CompareAllFields(expected, actual, label);
                if (asset != null)
                    Assert.That(GetField(actual, "_controlPointsLocal"), Is.Not.SameAs(GetField(asset, "_controlPointsLocal")),
                        label + " owns its control points");
            }
        }

        // The published clone: GridSize setter on a fresh asset, then copy.
        private static LatticeAsset LegacyCloneSettings(LatticeAsset source)
        {
            var cloned = new LatticeAsset();
            if (source == null)
            {
                cloned.EnsureInitialized();
                return cloned;
            }

            cloned.GridSize = source.GridSize;
            cloned.LocalBounds = source.LocalBounds;
            cloned.Interpolation = source.Interpolation;
            cloned.EnsureInitialized();
            int count = Mathf.Min(cloned.ControlPointCount, source.ControlPointCount);
            for (int i = 0; i < count; i++) cloned.SetControlPointLocal(i, source.GetControlPointLocal(i));
            cloned.CopyLegacySerializationStateFrom(source);
            return cloned;
        }

        private static LatticeAsset Initialized(LatticeAsset asset)
        {
            asset.EnsureInitialized();
            return asset;
        }

        private static LatticeAsset Source(Vector3Int grid, int pointCount, Random random,
            LatticeInterpolationMode interpolation, bool legacyTrilinear, int applySpace, int version)
        {
            var asset = new LatticeAsset();
            SetField(asset, "_gridSize", grid);
            SetField(asset, "_localBounds", new Bounds(RandomVector(random, 0.5f), new Vector3(1.5f, 2f, 0.75f)));
            var values = new Vector3[pointCount];
            for (int i = 0; i < pointCount; i++) values[i] = RandomVector(random, 2f);
            SetField(asset, "_controlPointsLocal", values);
            SetField(asset, "_interpolation", interpolation);
            SetField(asset, "_legacyTrilinearInterpolation", legacyTrilinear);
            SetField(asset, "_applySpace", applySpace);
            SetField(asset, "_serializationVersion", version);
            return asset;
        }

        private static void CompareAllFields(LatticeAsset expected, LatticeAsset actual, string label)
        {
            foreach (var field in typeof(LatticeAsset).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object expectedValue = field.GetValue(expected);
                object actualValue = field.GetValue(actual);
                string fieldLabel = $"{label} {field.Name}";
                switch (expectedValue)
                {
                    case Vector3[] expectedArray:
                        AssertSameBits(expectedArray, (Vector3[])actualValue, fieldLabel);
                        break;
                    case Bounds expectedBounds:
                        var actualBounds = (Bounds)actualValue;
                        AssertSameBits(expectedBounds.center, actualBounds.center, fieldLabel + " center");
                        AssertSameBits(expectedBounds.extents, actualBounds.extents, fieldLabel + " extents");
                        break;
                    default:
                        Assert.That(actualValue, Is.EqualTo(expectedValue), fieldLabel);
                        break;
                }
            }
        }

        private static object GetField(LatticeAsset asset, string name) =>
            typeof(LatticeAsset).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(asset);

        private static void SetField(LatticeAsset asset, string name, object value) =>
            typeof(LatticeAsset).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(asset, value);

        // ----- Shared helpers -----

        private static Vector3 RandomVector(Random random, float scale) =>
            new Vector3(
                (float)(random.NextDouble() * 2.0 - 1.0) * scale,
                (float)(random.NextDouble() * 2.0 - 1.0) * scale,
                (float)(random.NextDouble() * 2.0 - 1.0) * scale);

        private static int StableSeed(string text)
        {
            unchecked
            {
                int hash = 17;
                foreach (char c in text) hash = hash * 31 + c;
                return hash;
            }
        }

        private static void AssertSameBits(Vector3[] expected, Vector3[] actual, string label)
        {
            if (expected == null || actual == null)
            {
                Assert.That(actual == null, Is.EqualTo(expected == null), label + " null");
                return;
            }

            Assert.That(actual.Length, Is.EqualTo(expected.Length), label + " length");
            for (int i = 0; i < expected.Length; i++) AssertSameBits(expected[i], actual[i], $"{label} [{i}]");
        }

        private static void AssertSameBits(Vector3 expected, Vector3 actual, string label)
        {
            if (BitConverter.SingleToInt32Bits(actual.x) == BitConverter.SingleToInt32Bits(expected.x) &&
                BitConverter.SingleToInt32Bits(actual.y) == BitConverter.SingleToInt32Bits(expected.y) &&
                BitConverter.SingleToInt32Bits(actual.z) == BitConverter.SingleToInt32Bits(expected.z))
            {
                return;
            }

            Assert.Fail($"{label}: expected ({expected.x:R}, {expected.y:R}, {expected.z:R}), " +
                        $"actual ({actual.x:R}, {actual.y:R}, {actual.z:R})");
        }
    }
}
#endif
