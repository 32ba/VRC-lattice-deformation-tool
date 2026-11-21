#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Net._32Ba.LatticeDeformationTool.Editor.WeightTransfer;
using Net._32Ba.LatticeDeformationTool.Editor.WeightTransfer.BurstSolver;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    /// <summary>
    /// Pins the allocation-free weight transfer helpers to the original allocating routines,
    /// bit for bit. The reference implementations below are verbatim copies of the code that
    /// was replaced and must not be "fixed" to match the new code.
    /// </summary>
    public sealed class WeightTransferScratchEquivalenceTests
    {
        private static readonly float[] SpecialWeights =
        {
            0f, -0f, 1f, 0.5f, 0.25f, 0.125f, 1f / 3f, -0.25f, -1f, 1e-30f, 1e-8f,
            float.Epsilon, float.MaxValue, float.NaN, float.PositiveInfinity, float.NegativeInfinity
        };

        [Test]
        public void InterpolateBoneWeight_MatchesOriginalDictionaryImplementationBitwise()
        {
            var random = new System.Random(20261121);
            for (int caseIndex = 0; caseIndex < 20000; caseIndex++)
            {
                int mode = caseIndex % 4;
                var w0 = RandomBoneWeight(random, mode);
                var w1 = RandomBoneWeight(random, mode);
                var w2 = RandomBoneWeight(random, mode);
                var bary = RandomBarycentric(random, mode);

                AssertBitwiseEqual(
                    ReferenceInterpolateBoneWeight(w0, w1, w2, bary),
                    RobustWeightTransfer.InterpolateBoneWeight(w0, w1, w2, bary),
                    caseIndex);
            }
        }

        [Test]
        public void InterpolateBoneWeight_TiesAndManyInfluences_MatchOriginalBitwise()
        {
            var cases = new List<(BoneWeight, BoneWeight, BoneWeight, Vector3)>
            {
                // Four-way tie: bone index ascending decides.
                (Bone4(3, 0.25f, 2, 0.25f, 1, 0.25f, 0, 0.25f), new BoneWeight(), new BoneWeight(), new Vector3(1f, 0f, 0f)),
                // Twelve distinct influences, all tied after barycentric scaling.
                (Bone4(11, 0.25f, 10, 0.25f, 9, 0.25f, 8, 0.25f),
                    Bone4(7, 0.25f, 6, 0.25f, 5, 0.25f, 4, 0.25f),
                    Bone4(3, 0.25f, 2, 0.25f, 1, 0.25f, 0, 0.25f),
                    new Vector3(1f / 3f, 1f / 3f, 1f / 3f)),
                // Same bone repeated in every slot of every corner (accumulation order).
                (Bone4(5, 0.1f, 5, 0.2f, 5, 0.3f, 5, 0.4f),
                    Bone4(5, 0.7f, 5, 0.1f, 5, 0.1f, 5, 0.1f),
                    Bone4(5, 0.33f, 5, 0.33f, 5, 0.33f, 5, 0.01f),
                    new Vector3(0.2f, 0.3f, 0.5f)),
                // Overflow to +Infinity on accumulation and NaN after normalization.
                (Bone4(1, float.MaxValue, 1, float.MaxValue, 2, float.MaxValue, 2, float.MaxValue),
                    Bone4(3, float.MaxValue, 0, 0f, 0, 0f, 0, 0f),
                    new BoneWeight(),
                    new Vector3(1f, 1f, 0f)),
                // Negative bone indices, negative/NaN weights and negative barycentrics are ignored.
                (Bone4(-1, 1f, 2, float.NaN, 3, -0.5f, 4, 0.5f),
                    Bone4(4, 0.5f, -7, 0.5f, 6, float.PositiveInfinity, 7, 0f),
                    Bone4(8, 1f, 9, 1f, 10, 1f, 11, 1f),
                    new Vector3(0.6f, 0.6f, -0.2f)),
                // Nothing valid at all.
                (Bone4(0, -1f, 1, float.NaN, 2, 0f, 3, -0f), new BoneWeight(), new BoneWeight(), new Vector3(float.NaN, 1f, 0f)),
            };

            for (int caseIndex = 0; caseIndex < cases.Count; caseIndex++)
            {
                var (w0, w1, w2, bary) = cases[caseIndex];
                AssertBitwiseEqual(
                    ReferenceInterpolateBoneWeight(w0, w1, w2, bary),
                    RobustWeightTransfer.InterpolateBoneWeight(w0, w1, w2, bary),
                    caseIndex);
            }
        }

        [Test]
        public void BiCGStab_ReusedDirtyWorkspace_MatchesAllocatingSolveBitwise()
        {
            const int size = 48;
            var random = new System.Random(4242);
            var entries = new List<(int row, int col, double val)>();
            for (int row = 0; row < size; row++)
            {
                double rowSum = 0.0;
                for (int offset = 1; offset <= 3; offset++)
                {
                    int col = (row + offset * 7) % size;
                    if (col == row)
                        continue;
                    double value = -(0.1 + random.NextDouble());
                    entries.Add((row, col, value));
                    rowSum += Math.Abs(value);
                }

                entries.Add((row, row, rowSum + 0.5 + random.NextDouble()));
            }

            var matrix = NativeSparseMatrixCSR.FromCOO(size, size, entries, Allocator.TempJob);
            NativeArray<double> rhs = default;
            NativeArray<double> expectedSolution = default;
            NativeArray<double> actualSolution = default;
            BiCGStabWorkspace workspace = null;
            try
            {
                rhs = new NativeArray<double>(size, Allocator.TempJob);
                expectedSolution = new NativeArray<double>(size, Allocator.TempJob);
                actualSolution = new NativeArray<double>(size, Allocator.TempJob);
                workspace = new BiCGStabWorkspace(size);
                FillWorkspace(workspace, double.NaN);

                var iterationLimits = new[] { 0, 1, 2, 3, 5, 200 };
                for (int solveIndex = 0; solveIndex < 12; solveIndex++)
                {
                    for (int i = 0; i < size; i++)
                    {
                        rhs[i] = solveIndex == 3 ? 0.0 : random.NextDouble() * 2.0 - 1.0;
                        double initial = solveIndex % 2 == 0 ? 0.0 : random.NextDouble();
                        expectedSolution[i] = initial;
                        actualSolution[i] = initial;
                    }

                    int maxIterations = iterationLimits[solveIndex % iterationLimits.Length];
                    double tolerance = solveIndex % 3 == 0 ? 1e-12 : 1e-5;

                    double expectedNorm = BurstLinearAlgebra.Norm(rhs);
                    double actualNorm = BurstLinearAlgebra.Norm(rhs, workspace.ScalarResult);
                    Assert.That(
                        BitConverter.DoubleToInt64Bits(actualNorm),
                        Is.EqualTo(BitConverter.DoubleToInt64Bits(expectedNorm)),
                        $"norm solve {solveIndex}");

                    var expected = BurstBiCGStab.Solve(ref matrix, rhs, expectedSolution, maxIterations, tolerance);
                    var actual = BurstBiCGStab.Solve(ref matrix, rhs, actualSolution, maxIterations, tolerance, workspace);

                    Assert.That(actual.Converged, Is.EqualTo(expected.Converged), $"solve {solveIndex}");
                    Assert.That(actual.Iterations, Is.EqualTo(expected.Iterations), $"solve {solveIndex}");
                    Assert.That(actual.FailureReason, Is.EqualTo(expected.FailureReason), $"solve {solveIndex}");
                    Assert.That(
                        BitConverter.DoubleToInt64Bits(actual.FinalResidual),
                        Is.EqualTo(BitConverter.DoubleToInt64Bits(expected.FinalResidual)),
                        $"solve {solveIndex}");
                    for (int i = 0; i < size; i++)
                    {
                        Assert.That(
                            BitConverter.DoubleToInt64Bits(actualSolution[i]),
                            Is.EqualTo(BitConverter.DoubleToInt64Bits(expectedSolution[i])),
                            $"solve {solveIndex} element {i}");
                    }

                    // Leave stale garbage behind for the next solve.
                    FillWorkspace(workspace, solveIndex % 2 == 0 ? double.PositiveInfinity : -12345.678);
                }
            }
            finally
            {
                workspace?.Dispose();
                if (rhs.IsCreated) rhs.Dispose();
                if (expectedSolution.IsCreated) expectedSolution.Dispose();
                if (actualSolution.IsCreated) actualSolution.Dispose();
                matrix.Dispose();
            }
        }

        [Test]
        public void BiCGStabWorkspace_RejectsMismatchedLengthAndDisposesIdempotently()
        {
            var matrix = NativeSparseMatrixCSR.FromCOO(
                2,
                2,
                new List<(int row, int col, double val)> { (0, 0, 2.0), (1, 1, 4.0) },
                Allocator.TempJob);
            using var b = new NativeArray<double>(new[] { 6.0, 20.0 }, Allocator.TempJob);
            using var x = new NativeArray<double>(2, Allocator.TempJob);
            var wrongSize = new BiCGStabWorkspace(3);
            var disposed = new BiCGStabWorkspace(2);
            try
            {
                Assert.Throws<ArgumentException>(() =>
                    BurstBiCGStab.Solve(ref matrix, b, x, 8, 1e-12, wrongSize));
                Assert.Throws<ArgumentNullException>(() =>
                    BurstBiCGStab.Solve(ref matrix, b, x, 8, 1e-12, null));
                Assert.Throws<ArgumentOutOfRangeException>(() => new BiCGStabWorkspace(-1));

                disposed.Dispose();
                Assert.That(disposed.IsCreated, Is.False);
                Assert.DoesNotThrow(disposed.Dispose);
                Assert.Throws<ArgumentException>(() =>
                    BurstBiCGStab.Solve(ref matrix, b, x, 8, 1e-12, disposed));

                using (var workspace = new BiCGStabWorkspace(2))
                {
                    var result = BurstBiCGStab.Solve(ref matrix, b, x, 8, 1e-12, workspace);
                    Assert.That(result.Converged, Is.True, result.FailureReason);
                    Assert.That(x[0], Is.EqualTo(3.0).Within(1e-8));
                    Assert.That(x[1], Is.EqualTo(5.0).Within(1e-8));
                }
            }
            finally
            {
                wrongSize.Dispose();
                disposed.Dispose();
                matrix.Dispose();
            }
        }

        private static void FillWorkspace(BiCGStabWorkspace workspace, double value)
        {
            Fill(workspace.R, value);
            Fill(workspace.RTilde, value);
            Fill(workspace.P, value);
            Fill(workspace.V, value);
            Fill(workspace.S, value);
            Fill(workspace.T, value);
            Fill(workspace.PHat, value);
            Fill(workspace.SHat, value);
            Fill(workspace.Temp, value);
            Fill(workspace.ScalarResult, value);
        }

        private static void Fill(NativeArray<double> array, double value)
        {
            for (int i = 0; i < array.Length; i++)
                array[i] = value;
        }

        private static BoneWeight RandomBoneWeight(System.Random random, int mode)
        {
            return Bone4(
                RandomBone(random, mode), RandomWeight(random, mode),
                RandomBone(random, mode), RandomWeight(random, mode),
                RandomBone(random, mode), RandomWeight(random, mode),
                RandomBone(random, mode), RandomWeight(random, mode));
        }

        private static int RandomBone(System.Random random, int mode)
        {
            switch (mode)
            {
                case 0: return random.Next(0, 3); // heavy duplication
                case 1: return random.Next(0, 12); // up to 12 distinct influences
                case 2: return random.Next(-2, 6); // includes negative indices
                default: return random.Next(0, 6);
            }
        }

        private static float RandomWeight(System.Random random, int mode)
        {
            int pick = random.Next(0, 10);
            if (mode == 3 || pick < 2)
                return SpecialWeights[random.Next(SpecialWeights.Length)];
            if (pick < 5)
                return random.Next(0, 5) * 0.25f; // exact ties
            return (float)random.NextDouble();
        }

        private static Vector3 RandomBarycentric(System.Random random, int mode)
        {
            if (mode == 3 && random.Next(0, 4) == 0)
            {
                return new Vector3(
                    SpecialWeights[random.Next(SpecialWeights.Length)],
                    SpecialWeights[random.Next(SpecialWeights.Length)],
                    SpecialWeights[random.Next(SpecialWeights.Length)]);
            }

            if (random.Next(0, 5) == 0)
            {
                return new Vector3(1f / 3f, 1f / 3f, 1f / 3f);
            }

            float u = (float)random.NextDouble();
            float v = (float)random.NextDouble() * (1f - u);
            if (mode == 2 && random.Next(0, 3) == 0)
                u = -u;
            return new Vector3(u, v, 1f - u - v);
        }

        private static BoneWeight Bone4(
            int bone0, float weight0,
            int bone1, float weight1,
            int bone2, float weight2,
            int bone3, float weight3)
        {
            return new BoneWeight
            {
                boneIndex0 = bone0, weight0 = weight0,
                boneIndex1 = bone1, weight1 = weight1,
                boneIndex2 = bone2, weight2 = weight2,
                boneIndex3 = bone3, weight3 = weight3
            };
        }

        private static void AssertBitwiseEqual(BoneWeight expected, BoneWeight actual, int caseIndex)
        {
            Assert.That(actual.boneIndex0, Is.EqualTo(expected.boneIndex0), $"case {caseIndex} boneIndex0");
            Assert.That(actual.boneIndex1, Is.EqualTo(expected.boneIndex1), $"case {caseIndex} boneIndex1");
            Assert.That(actual.boneIndex2, Is.EqualTo(expected.boneIndex2), $"case {caseIndex} boneIndex2");
            Assert.That(actual.boneIndex3, Is.EqualTo(expected.boneIndex3), $"case {caseIndex} boneIndex3");
            Assert.That(Bits(actual.weight0), Is.EqualTo(Bits(expected.weight0)), $"case {caseIndex} weight0");
            Assert.That(Bits(actual.weight1), Is.EqualTo(Bits(expected.weight1)), $"case {caseIndex} weight1");
            Assert.That(Bits(actual.weight2), Is.EqualTo(Bits(expected.weight2)), $"case {caseIndex} weight2");
            Assert.That(Bits(actual.weight3), Is.EqualTo(Bits(expected.weight3)), $"case {caseIndex} weight3");
        }

        private static int Bits(float value)
        {
            return BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        }

        // ---- Verbatim copy of the original allocating RobustWeightTransfer.InterpolateBoneWeight ----

        private static BoneWeight ReferenceInterpolateBoneWeight(
            BoneWeight w0,
            BoneWeight w1,
            BoneWeight w2,
            Vector3 bary)
        {
            var boneWeights = new Dictionary<int, float>();

            ReferenceAddBoneWeight(boneWeights, w0.boneIndex0, w0.weight0 * bary.x);
            ReferenceAddBoneWeight(boneWeights, w0.boneIndex1, w0.weight1 * bary.x);
            ReferenceAddBoneWeight(boneWeights, w0.boneIndex2, w0.weight2 * bary.x);
            ReferenceAddBoneWeight(boneWeights, w0.boneIndex3, w0.weight3 * bary.x);

            ReferenceAddBoneWeight(boneWeights, w1.boneIndex0, w1.weight0 * bary.y);
            ReferenceAddBoneWeight(boneWeights, w1.boneIndex1, w1.weight1 * bary.y);
            ReferenceAddBoneWeight(boneWeights, w1.boneIndex2, w1.weight2 * bary.y);
            ReferenceAddBoneWeight(boneWeights, w1.boneIndex3, w1.weight3 * bary.y);

            ReferenceAddBoneWeight(boneWeights, w2.boneIndex0, w2.weight0 * bary.z);
            ReferenceAddBoneWeight(boneWeights, w2.boneIndex1, w2.weight1 * bary.z);
            ReferenceAddBoneWeight(boneWeights, w2.boneIndex2, w2.weight2 * bary.z);
            ReferenceAddBoneWeight(boneWeights, w2.boneIndex3, w2.weight3 * bary.z);

            var sortedWeights = new List<KeyValuePair<int, float>>(boneWeights);
            sortedWeights.Sort((a, b) =>
            {
                int weightComparison = b.Value.CompareTo(a.Value);
                return weightComparison != 0 ? weightComparison : a.Key.CompareTo(b.Key);
            });

            var result = new BoneWeight();
            float totalWeight = 0f;

            for (int i = 0; i < Mathf.Min(4, sortedWeights.Count); i++)
            {
                var kv = sortedWeights[i];
                if (kv.Value <= 0f) continue;

                switch (i)
                {
                    case 0:
                        result.boneIndex0 = kv.Key;
                        result.weight0 = kv.Value;
                        break;
                    case 1:
                        result.boneIndex1 = kv.Key;
                        result.weight1 = kv.Value;
                        break;
                    case 2:
                        result.boneIndex2 = kv.Key;
                        result.weight2 = kv.Value;
                        break;
                    case 3:
                        result.boneIndex3 = kv.Key;
                        result.weight3 = kv.Value;
                        break;
                }
                totalWeight += kv.Value;
            }

            if (totalWeight > 0f)
            {
                result.weight0 /= totalWeight;
                result.weight1 /= totalWeight;
                result.weight2 /= totalWeight;
                result.weight3 /= totalWeight;
            }

            return result;
        }

        private static void ReferenceAddBoneWeight(Dictionary<int, float> dict, int boneIndex, float weight)
        {
            if (float.IsNaN(weight) || float.IsInfinity(weight) || weight <= 0f || boneIndex < 0) return;
            if (dict.ContainsKey(boneIndex))
                dict[boneIndex] += weight;
            else
                dict[boneIndex] = weight;
        }
    }
}
#endif
