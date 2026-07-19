#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using Net._32Ba.LatticeDeformationTool.Editor.WeightTransfer;
using Net._32Ba.LatticeDeformationTool.Editor.WeightTransfer.BurstSolver;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class WeightTransferCoreTests
    {
        [Test]
        public void NativeSparseMatrix_FromCOO_DoesNotMutateInput()
        {
            var entries = new List<(int row, int col, double val)>
            {
                (1, 1, 2.0),
                (0, 0, 1.0)
            };

            var matrix = NativeSparseMatrixCSR.FromCOO(2, 2, entries, Allocator.TempJob);
            try
            {
                Assert.That(entries[0], Is.EqualTo((1, 1, 2.0)));
                Assert.That(entries[1], Is.EqualTo((0, 0, 1.0)));
            }
            finally
            {
                matrix.Dispose();
            }

        }

        [Test]
        public void NativeSparseMatrix_FromCOO_RejectsInvalidShapeAndEntries()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                NativeSparseMatrixCSR.FromCOO(-1, 0, new List<(int, int, double)>(), Allocator.TempJob));
            Assert.Throws<ArgumentNullException>(() =>
                NativeSparseMatrixCSR.FromCOO(1, 1, null, Allocator.TempJob));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                NativeSparseMatrixCSR.FromCOO(
                    1, 1, new List<(int, int, double)> { (1, 0, 1.0) }, Allocator.TempJob));
            Assert.Throws<ArgumentException>(() =>
                NativeSparseMatrixCSR.FromCOO(
                    1, 1, new List<(int, int, double)> { (0, 0, double.NaN) }, Allocator.TempJob));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                NativeSparseMatrixCSR.FromCOO(0, -1, new List<(int, int, double)>(), Allocator.TempJob));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                NativeSparseMatrixCSR.FromCOO(
                    1, 1, new List<(int, int, double)> { (0, 1, 1.0) }, Allocator.TempJob));
            Assert.Throws<ArgumentException>(() =>
                NativeSparseMatrixCSR.FromCOO(
                    1,
                    1,
                    new List<(int, int, double)>
                    {
                        (0, 0, double.MaxValue),
                        (0, 0, double.MaxValue)
                    },
                    Allocator.TempJob));
            Assert.Throws<ArgumentNullException>(() =>
                NativeSparseMatrixCSR.FromIndexed(1, 1, null, Allocator.TempJob));
        }

        [Test]
        public void NativeSparseMatrix_ValidateContents_RejectsEveryMalformedStorageInvariant()
        {
            Assert.Throws<ArgumentException>(() => default(NativeSparseMatrixCSR).ValidateContents());

            var inconsistent = CreateRawMatrix(1, 1, 1, new[] { 1.0 }, new[] { 0 }, new[] { 0, 1 }, new[] { 1.0 });
            inconsistent.NonZeroCount = 2;
            try { Assert.Throws<ArgumentException>(() => inconsistent.ValidateContents()); }
            finally { inconsistent.Dispose(); }

            var badStart = CreateRawMatrix(1, 1, 1, new[] { 1.0 }, new[] { 0 }, new[] { 1, 1 }, new[] { 1.0 });
            try { Assert.Throws<ArgumentException>(() => badStart.ValidateContents()); }
            finally { badStart.Dispose(); }

            var badEnd = CreateRawMatrix(1, 1, 1, new[] { 1.0 }, new[] { 0 }, new[] { 0, 0 }, new[] { 1.0 });
            try { Assert.Throws<ArgumentException>(() => badEnd.ValidateContents()); }
            finally { badEnd.Dispose(); }

            var badDiagonal = CreateRawMatrix(1, 1, 1, new[] { 1.0 }, new[] { 0 }, new[] { 0, 1 }, new[] { double.NaN });
            try { Assert.Throws<ArgumentException>(() => badDiagonal.ValidateContents()); }
            finally { badDiagonal.Dispose(); }
        }

        [Test]
        public void NativeSparseMatrix_FromCOO_SortsSumsAndDropsZeroEntries()
        {
            var entries = new List<(int row, int col, double val)>
            {
                (1, 0, 3.0),
                (0, 1, 2.0),
                (0, 1, -2.0),
                (0, 0, 4.0)
            };

            var matrix = NativeSparseMatrixCSR.FromCOO(2, 2, entries, Allocator.TempJob);
            try
            {
                Assert.That(matrix.IsCreated, Is.True);
                Assert.That(matrix.RowCount, Is.EqualTo(2));
                Assert.That(matrix.ColCount, Is.EqualTo(2));
                Assert.That(matrix.NonZeroCount, Is.EqualTo(2));
                Assert.That(matrix.RowPointers[0], Is.EqualTo(0));
                Assert.That(matrix.RowPointers[1], Is.EqualTo(1));
                Assert.That(matrix.RowPointers[2], Is.EqualTo(2));
                Assert.That(matrix.ColIndices[0], Is.EqualTo(0));
                Assert.That(matrix.Values[0], Is.EqualTo(4.0).Within(1e-12));
                Assert.That(matrix.Diagonal[0], Is.EqualTo(4.0).Within(1e-12));
            }
            finally
            {
                matrix.Dispose();
            }
        }

        [Test]
        public void NativeSparseMatrix_FromCOO_EmptyEntriesCreatesEmptyMatrix()
        {
            var matrix = NativeSparseMatrixCSR.FromCOO(
                3,
                2,
                new List<(int row, int col, double val)>(),
                Allocator.TempJob);
            try
            {
                Assert.That(matrix.RowCount, Is.EqualTo(3));
                Assert.That(matrix.ColCount, Is.EqualTo(2));
                Assert.That(matrix.NonZeroCount, Is.EqualTo(0));
                Assert.That(matrix.RowPointers.Length, Is.EqualTo(4));
                Assert.That(matrix.Diagonal.Length, Is.EqualTo(2));
            }
            finally
            {
                matrix.Dispose();
            }
        }

        [Test]
        public void NativeSparseMatrix_FromIndexed_ForwardsEntries()
        {
            var matrix = NativeSparseMatrixCSR.FromIndexed(
                1,
                1,
                new[] { (0, 0, 5.0) },
                Allocator.TempJob);
            try
            {
                Assert.That(matrix.NonZeroCount, Is.EqualTo(1));
                Assert.That(matrix.Values[0], Is.EqualTo(5.0).Within(1e-12));
            }
            finally
            {
                matrix.Dispose();
            }
        }

        [Test]
        public void BurstLinearAlgebra_Operations_ProduceExpectedValues()
        {
            using var x = new NativeArray<double>(new[] { 1.0, 2.0, 3.0 }, Allocator.TempJob);
            using var y = new NativeArray<double>(new[] { 4.0, 5.0, 6.0 }, Allocator.TempJob);
            using var z = new NativeArray<double>(3, Allocator.TempJob);
            using var diag = new NativeArray<double>(new[] { 2.0, 0.0, -4.0 }, Allocator.TempJob);

            Assert.That(BurstLinearAlgebra.Dot(x, y), Is.EqualTo(32.0).Within(1e-12));
            Assert.That(BurstLinearAlgebra.Norm(x), Is.EqualTo(Math.Sqrt(14.0)).Within(1e-12));

            BurstLinearAlgebra.Copy(x, z);
            Assert.That(z[2], Is.EqualTo(3.0).Within(1e-12));

            BurstLinearAlgebra.Scale(2.0, x, z);
            Assert.That(z[1], Is.EqualTo(4.0).Within(1e-12));

            BurstLinearAlgebra.AXPY(-0.5, x, y);
            Assert.That(y[0], Is.EqualTo(3.5).Within(1e-12));

            BurstLinearAlgebra.ApplyDiagonalPreconditioner(diag, x, z);
            Assert.That(z[0], Is.EqualTo(0.5).Within(1e-12));
            Assert.That(z[1], Is.EqualTo(2.0).Within(1e-12));
            Assert.That(z[2], Is.EqualTo(-0.75).Within(1e-12));

            BurstLinearAlgebra.Zero(z);
            Assert.That(z[0], Is.EqualTo(0.0).Within(1e-12));
        }

        [Test]
        public void BurstLinearAlgebra_SpMV_MultipliesSparseMatrix()
        {
            var entries = new List<(int row, int col, double val)>
            {
                (0, 0, 2.0),
                (0, 1, 1.0),
                (1, 0, -1.0),
                (1, 1, 3.0)
            };

            var matrix = NativeSparseMatrixCSR.FromCOO(2, 2, entries, Allocator.TempJob);
            using var x = new NativeArray<double>(new[] { 4.0, 5.0 }, Allocator.TempJob);
            using var y = new NativeArray<double>(2, Allocator.TempJob);
            try
            {
                BurstLinearAlgebra.SpMV(ref matrix, x, y);
                Assert.That(y[0], Is.EqualTo(13.0).Within(1e-12));
                Assert.That(y[1], Is.EqualTo(11.0).Within(1e-12));
            }
            finally
            {
                matrix.Dispose();
            }
        }

        [Test]
        public void BurstBiCGStab_Solve_ConvergesForDiagonalSystem()
        {
            var matrix = NativeSparseMatrixCSR.FromCOO(
                2,
                2,
                new List<(int row, int col, double val)> { (0, 0, 2.0), (1, 1, 4.0) },
                Allocator.TempJob);
            using var b = new NativeArray<double>(new[] { 6.0, 20.0 }, Allocator.TempJob);
            using var x = new NativeArray<double>(new[] { 0.0, 0.0 }, Allocator.TempJob);
            try
            {
                var result = BurstBiCGStab.Solve(ref matrix, b, x, 32, 1e-12);

                Assert.That(result.Converged, Is.True, result.FailureReason);
                Assert.That(x[0], Is.EqualTo(3.0).Within(1e-8));
                Assert.That(x[1], Is.EqualTo(5.0).Within(1e-8));
            }
            finally
            {
                matrix.Dispose();
            }
        }

        [Test]
        public void BurstBiCGStab_Solve_ConvergesForNonsymmetricSystemThroughOmegaStep()
        {
            var matrix = NativeSparseMatrixCSR.FromCOO(
                2,
                2,
                new List<(int row, int col, double val)>
                {
                    (0, 0, 4.0),
                    (0, 1, 1.0),
                    (1, 0, 2.0),
                    (1, 1, 3.0)
                },
                Allocator.TempJob);
            using var b = new NativeArray<double>(new[] { 1.0, 2.0 }, Allocator.TempJob);
            using var x = new NativeArray<double>(new[] { 0.0, 0.0 }, Allocator.TempJob);
            try
            {
                var result = BurstBiCGStab.Solve(ref matrix, b, x, 32, 1e-12);

                Assert.That(result.Converged, Is.True, result.FailureReason);
                Assert.That(x[0], Is.EqualTo(0.1).Within(1e-8));
                Assert.That(x[1], Is.EqualTo(0.6).Within(1e-8));
                Assert.That(result.Iterations, Is.GreaterThanOrEqualTo(1));
            }
            finally
            {
                matrix.Dispose();
            }
        }

        [Test]
        public void BurstBiCGStab_Solve_ReportsMaxIterationsWhenNotConverged()
        {
            var matrix = NativeSparseMatrixCSR.FromCOO(
                1,
                1,
                new List<(int row, int col, double val)> { (0, 0, 2.0) },
                Allocator.TempJob);
            using var b = new NativeArray<double>(new[] { 1.0 }, Allocator.TempJob);
            using var x = new NativeArray<double>(new[] { 0.0 }, Allocator.TempJob);
            try
            {
                var result = BurstBiCGStab.Solve(ref matrix, b, x, 0, 1e-12);

                Assert.That(result.Converged, Is.False);
                Assert.That(result.Iterations, Is.EqualTo(0));
                Assert.That(result.FailureReason, Is.EqualTo("max iterations reached"));
            }
            finally
            {
                matrix.Dispose();
            }
        }

        [Test]
        public void BurstBiCGStab_Solve_ReturnsConvergedForEmptySystem()
        {
            var matrix = NativeSparseMatrixCSR.FromCOO(
                0,
                0,
                new List<(int row, int col, double val)>(),
                Allocator.TempJob);
            using var b = new NativeArray<double>(0, Allocator.TempJob);
            using var x = new NativeArray<double>(0, Allocator.TempJob);
            try
            {
                var result = BurstBiCGStab.Solve(ref matrix, b, x, 4, 1e-12);

                Assert.That(result.Converged, Is.True);
                Assert.That(result.Iterations, Is.EqualTo(0));
                Assert.That(result.FinalResidual, Is.EqualTo(0.0).Within(1e-12));
            }
            finally
            {
                matrix.Dispose();
            }
        }

        [Test]
        public void BurstBiCGStab_Solve_ReturnsConvergedWhenInitialGuessAlreadySolves()
        {
            var matrix = NativeSparseMatrixCSR.FromCOO(
                1,
                1,
                new List<(int row, int col, double val)> { (0, 0, 2.0) },
                Allocator.TempJob);
            using var b = new NativeArray<double>(new[] { 6.0 }, Allocator.TempJob);
            using var x = new NativeArray<double>(new[] { 3.0 }, Allocator.TempJob);
            try
            {
                var result = BurstBiCGStab.Solve(ref matrix, b, x, 4, 1e-12);

                Assert.That(result.Converged, Is.True);
                Assert.That(result.Iterations, Is.EqualTo(0));
                Assert.That(result.FinalResidual, Is.EqualTo(0.0).Within(1e-12));
            }
            finally
            {
                matrix.Dispose();
            }
        }

        [Test]
        public void BurstBiCGStab_Solve_HandlesZeroRightHandSide()
        {
            var matrix = NativeSparseMatrixCSR.FromCOO(
                1,
                1,
                new List<(int row, int col, double val)> { (0, 0, 2.0) },
                Allocator.TempJob);
            using var b = new NativeArray<double>(new[] { 0.0 }, Allocator.TempJob);
            using var x = new NativeArray<double>(new[] { 0.0 }, Allocator.TempJob);
            try
            {
                var result = BurstBiCGStab.Solve(ref matrix, b, x, 4, 1e-12);

                Assert.That(result.Converged, Is.True);
                Assert.That(result.FinalResidual, Is.EqualTo(0.0).Within(1e-12));
            }
            finally
            {
                matrix.Dispose();
            }
        }

        [Test]
        public void BurstBiCGStab_Solve_ReportsAlphaBreakdown()
        {
            var matrix = NativeSparseMatrixCSR.FromCOO(
                1,
                1,
                new List<(int row, int col, double val)>(),
                Allocator.TempJob);
            using var b = new NativeArray<double>(new[] { 1.0 }, Allocator.TempJob);
            using var x = new NativeArray<double>(new[] { 0.0 }, Allocator.TempJob);
            try
            {
                var result = BurstBiCGStab.Solve(ref matrix, b, x, 4, 1e-12);

                Assert.That(result.Converged, Is.False);
                Assert.That(result.FailureReason, Is.EqualTo("alpha breakdown"));
            }
            finally
            {
                matrix.Dispose();
            }
        }

        [Test]
        public void MeshSpatialQuery_FindClosestPoint_ReturnsTriangleProjectionAndNormal()
        {
            var vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f)
            };
            var triangles = new[] { 0, 1, 2 };
            var normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };

            using var query = new MeshSpatialQuery(vertices, triangles, normals);
            var result = query.FindClosestPoint(new Vector3(0.25f, 0.25f, 1f), 2f);

            Assert.That(result.found, Is.True);
            Assert.That(result.closestPoint.x, Is.EqualTo(0.25f).Within(1e-5f));
            Assert.That(result.closestPoint.y, Is.EqualTo(0.25f).Within(1e-5f));
            Assert.That(result.closestPoint.z, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(result.interpolatedNormal, Is.EqualTo(Vector3.forward));
            Assert.That(result.triangleIndices, Is.EqualTo(new Vector3Int(0, 1, 2)));
            Assert.That(result.distance, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void BurstBiCGStab_Solve_RejectsInvalidVectorLengthAndTolerance()
        {
            var matrix = NativeSparseMatrixCSR.FromCOO(
                1,
                1,
                new List<(int row, int col, double val)> { (0, 0, 1.0) },
                Allocator.TempJob);
            using var empty = new NativeArray<double>(0, Allocator.TempJob);
            using var vector = new NativeArray<double>(1, Allocator.TempJob);
            using var nonZeroRightHandSide = new NativeArray<double>(new[] { 1.0 }, Allocator.TempJob);
            using var zeroSolution = new NativeArray<double>(new[] { 0.0 }, Allocator.TempJob);
            using var nonFiniteRightHandSide = new NativeArray<double>(new[] { double.NaN }, Allocator.TempJob);
            using var nonFiniteSolution = new NativeArray<double>(new[] { double.PositiveInfinity }, Allocator.TempJob);
            try
            {
                Assert.Throws<ArgumentException>(() =>
                    BurstBiCGStab.Solve(ref matrix, empty, vector, 1, 1e-6));

                var zeroTolerance = BurstBiCGStab.Solve(
                    ref matrix,
                    nonZeroRightHandSide,
                    zeroSolution,
                    1,
                    0.0);
                Assert.That(zeroTolerance.Converged, Is.False);

                Assert.Throws<ArgumentOutOfRangeException>(() => BurstBiCGStab.Solve(
                    ref matrix, nonZeroRightHandSide, vector, -1, 1e-6));
                Assert.Throws<ArgumentOutOfRangeException>(() => BurstBiCGStab.Solve(
                    ref matrix, nonZeroRightHandSide, vector, 1, double.NaN));
                Assert.Throws<ArgumentOutOfRangeException>(() => BurstBiCGStab.Solve(
                    ref matrix, nonZeroRightHandSide, vector, 1, -1e-6));
                Assert.Throws<ArgumentException>(() => BurstBiCGStab.Solve(
                    ref matrix, nonFiniteRightHandSide, vector, 1, 1e-6));
                Assert.Throws<ArgumentException>(() => BurstBiCGStab.Solve(
                    ref matrix, nonZeroRightHandSide, nonFiniteSolution, 1, 1e-6));
            }
            finally
            {
                matrix.Dispose();
            }

        }

        [Test]
        public void BurstBiCGStab_Solve_RejectsMalformedCsrContents()
        {
            var matrix = new NativeSparseMatrixCSR
            {
                RowCount = 1,
                ColCount = 1,
                NonZeroCount = 1,
                Values = new NativeArray<double>(new[] { 1.0 }, Allocator.TempJob),
                ColIndices = new NativeArray<int>(new[] { 0 }, Allocator.TempJob),
                RowPointers = new NativeArray<int>(new[] { 0, 2 }, Allocator.TempJob),
                Diagonal = new NativeArray<double>(new[] { 1.0 }, Allocator.TempJob)
            };
            using var vector = new NativeArray<double>(new[] { 1.0 }, Allocator.TempJob);
            try
            {
                Assert.Throws<ArgumentException>(() =>
                    BurstBiCGStab.Solve(ref matrix, vector, vector, 1, 1e-6));
            }
            finally
            {
                matrix.Dispose();
            }

            matrix = new NativeSparseMatrixCSR
            {
                RowCount = 1,
                ColCount = 1,
                NonZeroCount = 1,
                Values = new NativeArray<double>(new[] { 1.0 }, Allocator.TempJob),
                ColIndices = new NativeArray<int>(new[] { 1 }, Allocator.TempJob),
                RowPointers = new NativeArray<int>(new[] { 0, 1 }, Allocator.TempJob),
                Diagonal = new NativeArray<double>(new[] { 1.0 }, Allocator.TempJob)
            };
            try
            {
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    BurstBiCGStab.Solve(ref matrix, vector, vector, 1, 1e-6));
            }
            finally
            {
                matrix.Dispose();
            }

            matrix = new NativeSparseMatrixCSR
            {
                RowCount = 1,
                ColCount = 1,
                NonZeroCount = 1,
                Values = new NativeArray<double>(new[] { double.NaN }, Allocator.TempJob),
                ColIndices = new NativeArray<int>(new[] { 0 }, Allocator.TempJob),
                RowPointers = new NativeArray<int>(new[] { 0, 1 }, Allocator.TempJob),
                Diagonal = new NativeArray<double>(new[] { 1.0 }, Allocator.TempJob)
            };
            try
            {
                Assert.Throws<ArgumentException>(() =>
                    BurstBiCGStab.Solve(ref matrix, vector, vector, 1, 1e-6));
            }
            finally
            {
                matrix.Dispose();
            }
        }

        [Test]
        public void MeshSpatialQuery_RejectsInconsistentMeshData()
        {
            var vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };

            Assert.Throws<ArgumentNullException>(() => new MeshSpatialQuery(null, Array.Empty<int>(), null));
            Assert.Throws<ArgumentNullException>(() => new MeshSpatialQuery(Array.Empty<Vector3>(), null, null));

            using (var empty = new MeshSpatialQuery(Array.Empty<Vector3>(), Array.Empty<int>(), null))
            {
                Assert.That(empty.FindClosestPoint(Vector3.zero).found, Is.False);
            }

            using (var query = new MeshSpatialQuery(vertices, new[] { 0, 1 }, null))
            {
                Assert.That(query.FindClosestPoint(Vector3.zero).found, Is.False);
            }
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new MeshSpatialQuery(vertices, new[] { 0, 1, 3 }, null));
            Assert.Throws<ArgumentException>(() =>
                new MeshSpatialQuery(vertices, new[] { 0, 1, 2 }, new[] { Vector3.forward }));
            Assert.Throws<ArgumentException>(() =>
                new MeshSpatialQuery(
                    new[] { Vector3.zero, new Vector3(float.NaN, 0f, 0f), Vector3.up },
                    new[] { 0, 1, 2 },
                    null));
            Assert.Throws<ArgumentException>(() =>
                new MeshSpatialQuery(
                    vertices,
                    new[] { 0, 1, 2 },
                    new[] { Vector3.forward, new Vector3(0f, float.PositiveInfinity, 0f), Vector3.forward }));

            using var valid = new MeshSpatialQuery(vertices, new[] { 0, 1, 2 }, null);
            Assert.That(InvokeSpatialPrivate<int>(valid, "GetMaxSearchRadius", 0f), Is.EqualTo(-1));
        }

        [Test]
        public void MeshSpatialQuery_UsesAnImmutableInputSnapshot()
        {
            var vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            var triangles = new[] { 0, 1, 2 };
            using var query = new MeshSpatialQuery(vertices, triangles, null);

            vertices[0] = new Vector3(100f, 100f, 100f);
            triangles[0] = 99;

            var result = query.FindClosestPoint(new Vector3(0.25f, 0.25f, 1f), 2f);
            Assert.That(result.found, Is.True);
            Assert.That(result.closestPoint.x, Is.EqualTo(0.25f).Within(1e-5f));
            Assert.That(result.closestPoint.y, Is.EqualTo(0.25f).Within(1e-5f));
            Assert.That(result.closestPoint.z, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void MeshSpatialQuery_EnforcesExactMaximumSearchDistance()
        {
            var vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            var triangles = new[] { 0, 1, 2 };
            using var query = new MeshSpatialQuery(vertices, triangles, null);

            var result = query.FindClosestPoint(new Vector3(0.25f, 0.25f, 0.01f), 0.001f);

            Assert.That(result.found, Is.False);
            Assert.That(result.distance, Is.EqualTo(float.MaxValue));
            Assert.Throws<ArgumentNullException>(() => query.FindClosestPointsBatch(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => query.FindClosestPoint(Vector3.zero, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                query.FindClosestPoint(new Vector3(float.NaN, 0f, 0f)));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                query.FindClosestPointsBatch(new[] { Vector3.zero, new Vector3(0f, float.PositiveInfinity, 0f) }));
        }

        [Test]
        public void MeshSpatialQuery_UnlimitedSearchFindsTriangleBeyondEightCells()
        {
            var mesh = CreateLongRangeTriangleMesh();
            try
            {
                using var query = new MeshSpatialQuery(mesh.vertices, mesh.triangles, null);
                var point = new Vector3(0.5f, 0.25f, 0.25f);

                var result = query.FindClosestPoint(point);

                Assert.That(result.found, Is.True);
                Assert.That(result.distance, Is.EqualTo(0.5f).Within(1e-5f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void MeshSpatialQuery_AdjacentCellClosestResultMatchesBruteForce()
        {
            var mesh = CreateAdjacentCellMesh();
            try
            {
                using var query = new MeshSpatialQuery(mesh.vertices, mesh.triangles, null);
                var point = new Vector3(0.03124f, 0.5f, 0.001f);
                var result = query.FindClosestPoint(point, 0.01f);
                var expectedDistance = BruteForceClosestDistance(point, mesh);

                Assert.That(result.found, Is.True);
                Assert.That(result.triangleIndex, Is.EqualTo(1));
                Assert.That(result.distance, Is.EqualTo(expectedDistance).Within(1e-5f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void MeshSpatialQuery_SequentialAndBurstBatchMatchBruteForce()
        {
            var mesh = CreateAdjacentCellMesh();
            try
            {
                using var query = new MeshSpatialQuery(mesh.vertices, mesh.triangles, null);
                var points = new[]
                {
                    new Vector3(0.03124f, 0.5f, 0.001f),
                    new Vector3(0.03124f, 0.495f, 0.001f),
                    new Vector3(0.03124f, 0.505f, 0.001f)
                };
                var sequential = query.FindClosestPointsBatch(points, 0.01f);
                var burstPoints = new Vector3[128];
                for (int i = 0; i < burstPoints.Length; i++)
                    burstPoints[i] = points[i % points.Length];
                var burst = query.FindClosestPointsBatch(burstPoints, 0.01f);

                for (int i = 0; i < points.Length; i++)
                {
                    var expectedDistance = BruteForceClosestDistance(points[i], mesh);
                    Assert.That(sequential[i].found, Is.True);
                    Assert.That(sequential[i].distance, Is.EqualTo(expectedDistance).Within(1e-5f));
                }

                for (int i = 0; i < burst.Length; i++)
                {
                    int sourceIndex = i % points.Length;
                    var expectedDistance = BruteForceClosestDistance(burstPoints[i], mesh);
                    Assert.That(burst[i].found, Is.True);
                    Assert.That(burst[i].distance, Is.EqualTo(expectedDistance).Within(1e-5f));
                    Assert.That(burst[i].triangleIndex, Is.EqualTo(sequential[sourceIndex].triangleIndex));
                    Assert.That(burst[i].distance, Is.EqualTo(sequential[sourceIndex].distance).Within(1e-5f));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void MeshSpatialQuery_RejectsUseAfterDisposeAndAllowsRepeatedDispose()
        {
            var query = new MeshSpatialQuery(
                new[] { Vector3.zero, Vector3.right, Vector3.up },
                new[] { 0, 1, 2 },
                null);

            query.Dispose();
            query.Dispose();

            Assert.Throws<ObjectDisposedException>(() => query.FindClosestPoint(Vector3.zero));
            Assert.Throws<ObjectDisposedException>(() => query.FindClosestPointsBatch(Array.Empty<Vector3>()));
        }

        [Test]
        public void MeshSpatialQuery_FindClosestPoint_ReturnsNotFoundWhenSearchRadiusMissesGrid()
        {
            var vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f)
            };
            var triangles = new[] { 0, 1, 2 };

            using var query = new MeshSpatialQuery(vertices, triangles, null);
            var result = query.FindClosestPoint(new Vector3(100f, 100f, 100f), 0.001f);

            Assert.That(result.found, Is.False);
            Assert.That(result.distance, Is.EqualTo(float.MaxValue));
        }

        [Test]
        public void MeshSpatialQuery_FindClosestPointsBatch_UsesSequentialPathForSmallBatch()
        {
            var vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f)
            };
            var triangles = new[] { 0, 1, 2 };
            var queryPoints = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(1.5f, 0f, 0f)
            };

            using var query = new MeshSpatialQuery(vertices, triangles, null);
            var results = query.FindClosestPointsBatch(queryPoints, 2f);

            Assert.That(results, Has.Length.EqualTo(2));
            Assert.That(results[0].found, Is.True);
            Assert.That(results[0].barycentricCoords.x, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(results[0].interpolatedNormal, Is.EqualTo(Vector3.forward));
            Assert.That(results[1].found, Is.True);
            Assert.That(results[1].barycentricCoords.y, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void MeshSpatialQuery_FindClosestPointsBatch_UsesBurstPathForLargeBatch()
        {
            var vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f)
            };
            var triangles = new[] { 0, 1, 2 };
            var queryPoints = new Vector3[128];
            for (int i = 0; i < queryPoints.Length; i++)
            {
                queryPoints[i] = new Vector3(0.1f, 0.1f, 0.25f);
            }

            using var query = new MeshSpatialQuery(vertices, triangles, null);
            var results = query.FindClosestPointsBatch(queryPoints);

            Assert.That(results, Has.Length.EqualTo(128));
            Assert.That(results[0].found, Is.True);
            Assert.That(results[0].interpolatedNormal.z, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(results[127].closestPoint.z, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void MeshSpatialQuery_FindClosestPointsBatch_InitializesNativeNormalsAndCellBounds()
        {
            var vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f)
            };
            var triangles = new[] { 0, 1, 2 };
            var normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
            var queryPoints = new Vector3[128];
            for (int i = 0; i < queryPoints.Length; i++)
            {
                queryPoints[i] = new Vector3(0.25f, 0.25f, 0.1f);
            }

            using var query = new MeshSpatialQuery(vertices, triangles, normals);
            var results = query.FindClosestPointsBatch(queryPoints, 2f);

            Assert.That(results[0].interpolatedNormal.z, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(InvokeSpatialPrivate<int>(query, "GetCellIndex", -1, 0, 0), Is.EqualTo(-1));
        }

        [Test]
        public void MeshSpatialQuery_FindClosestPoint_CoversTriangleVoronoiRegions()
        {
            var vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f)
            };
            var triangles = new[] { 0, 1, 2 };

            using var query = new MeshSpatialQuery(vertices, triangles, null);

            var edgeAb = query.FindClosestPoint(new Vector3(0.5f, -0.25f, 0f), 2f);
            Assert.That(edgeAb.found, Is.True);
            Assert.That(edgeAb.barycentricCoords.z, Is.EqualTo(0f).Within(1e-5f));

            var vertexC = query.FindClosestPoint(new Vector3(-0.25f, 1.25f, 0f), 2f);
            Assert.That(vertexC.found, Is.True);
            Assert.That(vertexC.barycentricCoords.z, Is.EqualTo(1f).Within(1e-5f));

            var edgeAc = query.FindClosestPoint(new Vector3(-0.25f, 0.5f, 0f), 2f);
            Assert.That(edgeAc.found, Is.True);
            Assert.That(edgeAc.barycentricCoords.y, Is.EqualTo(0f).Within(1e-5f));

            var edgeBc = query.FindClosestPoint(new Vector3(0.75f, 0.75f, 0f), 2f);
            Assert.That(edgeBc.found, Is.True);
            Assert.That(edgeBc.barycentricCoords.x, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void WeightInpainting_Inpaint_ReturnsWithoutChangingKnownWeightsWhenNoUnknownVertices()
        {
            var vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f)
            };
            var triangles = new[] { 0, 1, 2 };
            var weights = new[] { Bone(0, 1f), Bone(1, 1f), Bone(2, 1f) };
            var confidence = new[] { 1f, 1f, 1f };

            var inpainting = new WeightInpainting(vertices, triangles, 128, 1e-8f);
            inpainting.Inpaint(weights, confidence, 3);

            Assert.That(weights[0].boneIndex0, Is.EqualTo(0));
            Assert.That(weights[1].boneIndex0, Is.EqualTo(1));
            Assert.That(weights[2].boneIndex0, Is.EqualTo(2));
        }

        [Test]
        public void WeightInpainting_RejectsNullAndShortInputsExplicitly()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new WeightInpainting(null, Array.Empty<int>(), 8, 1e-8f));
            Assert.Throws<ArgumentNullException>(() =>
                new WeightInpainting(Array.Empty<Vector3>(), null, 8, 1e-8f));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new WeightInpainting(Array.Empty<Vector3>(), Array.Empty<int>(), -1, 1e-8f));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new WeightInpainting(Array.Empty<Vector3>(), Array.Empty<int>(), 8, float.NaN));
            Assert.Throws<ArgumentException>(() =>
                new WeightInpainting(
                    new[] { new Vector3(float.NegativeInfinity, 0f, 0f) },
                    Array.Empty<int>(),
                    8,
                    1e-8f));

            var inpainting = new WeightInpainting(
                new[] { Vector3.zero },
                Array.Empty<int>(),
                8,
                1e-8f);
            Assert.Throws<ArgumentNullException>(() =>
                inpainting.Inpaint(null, new[] { 1f }, 1));
            Assert.Throws<ArgumentException>(() =>
                inpainting.Inpaint(new BoneWeight[0], new[] { 1f }, 1));
            Assert.Throws<ArgumentException>(() =>
                inpainting.Inpaint(new[] { new BoneWeight() }, Array.Empty<float>(), 1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                inpainting.Inpaint(new[] { new BoneWeight() }, new[] { 1f }, 0));
        }

        [Test]
        public void WeightInpainting_DegenerateTrianglesDoNotCreateArtificialWeightPaths()
        {
            var vertices = new[]
            {
                Vector3.zero,
                Vector3.right,
                Vector3.right * 2f,
                Vector3.up,
            };
            var weights = new[]
            {
                Bone(0, 1f),
                new BoneWeight(),
                new BoneWeight(),
                Bone(1, 1f),
            };
            var confidence = new[] { 1f, 0f, 0f, 1f };
            var inpainting = new WeightInpainting(
                vertices,
                new[] { 0, 1, 2, 0, 1, 3 },
                maxIterations: 0,
                tolerance: 1e-8f);

            inpainting.Inpaint(weights, confidence, 2);

            Assert.That(weights[1].weight0, Is.GreaterThan(0f),
                "The valid triangle must still participate in the forced fallback.");
            Assert.That(weights[2].weight0, Is.EqualTo(0f));
        }

        [Test]
        public void WeightInpainting_Inpaint_LogsWarningWhenNoKnownVertices()
        {
            var vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f)
            };
            var triangles = new[] { 0, 1, 2 };
            var weights = new[] { new BoneWeight(), new BoneWeight(), new BoneWeight() };
            var confidence = new[] { 0f, 0f, 0f };

            LogAssert.Expect(LogType.Warning, "WeightInpainting: No known vertices to interpolate from.");
            var inpainting = new WeightInpainting(vertices, triangles, 128, 1e-8f);
            inpainting.Inpaint(weights, confidence, 3);

            Assert.That(weights[0].weight0, Is.EqualTo(0f));
        }

        [Test]
        public void WeightInpainting_Inpaint_LogsWarningWhenKnownVerticesHaveNoPositiveWeights()
        {
            var vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f)
            };
            var triangles = new[] { 0, 1, 2 };
            var weights = new[] { new BoneWeight(), new BoneWeight(), new BoneWeight() };
            var confidence = new[] { 1f, 0f, 0f };

            LogAssert.Expect(LogType.Warning, "WeightInpainting: No bone weights found in known vertices.");
            var inpainting = new WeightInpainting(vertices, triangles, 128, 1e-8f);
            inpainting.Inpaint(weights, confidence, 3);

            Assert.That(weights[1].weight0, Is.EqualTo(0f));
        }

        [Test]
        public void WeightInpainting_Inpaint_FillsUnknownVertexFromKnownNeighbors()
        {
            var vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f)
            };
            var triangles = new[] { 0, 1, 2 };
            var weights = new[]
            {
                Bone(0, 1f),
                new BoneWeight(),
                Bone(1, 1f)
            };
            var confidence = new[] { 1f, 0f, 1f };

            var inpainting = new WeightInpainting(vertices, triangles, 128, 1e-8f);
            inpainting.Inpaint(weights, confidence, 2);

            Assert.That(weights[1].weight0 + weights[1].weight1 + weights[1].weight2 + weights[1].weight3,
                Is.EqualTo(1f).Within(1e-5f));
            Assert.That(weights[1].weight0, Is.GreaterThan(0f));
        }

        [Test]
        public void WeightInpainting_Inpaint_CoversUnknownNeighborAndSolverFallbackPaths()
        {
            var vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(1f, 1f, 0f),
                new Vector3(0f, 1f, 0f)
            };
            var triangles = new[] { 0, 1, 2, 0, 2, 3 };
            var weights = new[]
            {
                Bone(0, 1f),
                new BoneWeight(),
                new BoneWeight(),
                Bone(1, 1f)
            };
            var confidence = new[] { 1f, 0f, 0f, 1f };

            var inpainting = new WeightInpainting(vertices, triangles, 0, 1e-12f);
            inpainting.Inpaint(weights, confidence, 2);

            Assert.That(weights[1].weight0 + weights[1].weight1 + weights[1].weight2 + weights[1].weight3,
                Is.EqualTo(1f).Within(1e-5f));
            Assert.That(weights[2].weight0 + weights[2].weight1 + weights[2].weight2 + weights[2].weight3,
                Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void WeightInpainting_Inpaint_SkipsMissingLaplacianEntries()
        {
            var vertices = new[]
            {
                Vector3.zero,
                Vector3.right,
                Vector3.up
            };
            var triangles = new[] { 0, 1, 2 };
            var weights = new[] { Bone(0, 1f), new BoneWeight(), Bone(1, 1f) };
            var confidence = new[] { 1f, 0f, 1f };
            var inpainting = new WeightInpainting(vertices, triangles, 8, 1e-8f);
            var entries = (Dictionary<(int, int), double>)typeof(WeightInpainting)
                .GetField("_laplacianEntries", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(inpainting);
            entries.Remove((1, 0));

            inpainting.Inpaint(weights, confidence, 2);

            Assert.That(weights[1].weight0 + weights[1].weight1 + weights[1].weight2 + weights[1].weight3,
                Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void WeightInpainting_PrivateHelpers_HandleDegenerateCotangentAndFallbackAveraging()
        {
            var vertices = new[]
            {
                Vector3.zero,
                Vector3.right,
                Vector3.right * 2f,
                Vector3.up
            };
            var triangles = new[] { 0, 1, 2, 0, 1, 3 };
            var inpainting = new WeightInpainting(vertices, triangles, 1, 1e-12f);

            Assert.That(
                InvokeInpaintingPrivate<double>(inpainting, "ComputeCotangent", 0, 1, 2),
                Is.EqualTo(0.0).Within(1e-12));

            var dict = new Dictionary<(int, int), List<int>>();
            InvokeInpaintingPrivate<object>(inpainting, "AddOppositeVertex", dict, 2, 0, 1);
            Assert.That(dict[(0, 2)], Is.EqualTo(new[] { 1 }));

            var weights = new[] { Bone(0, 1f), new BoneWeight(), new BoneWeight(), Bone(0, 0.25f) };
            var unknown = new List<int> { 1, 2 };
            var known = new List<int> { 0, 3 };
            var results = new double[2, 1];

            InvokeInpaintingPrivate<object>(
                inpainting,
                "FallbackNeighborAveraging",
                unknown,
                known,
                weights,
                0,
                results,
                0);

            Assert.That(results[0, 0], Is.GreaterThan(0.0));
            Assert.That(results[1, 0], Is.EqualTo(0.0),
                "Fallback must not traverse the edge contributed only by a degenerate triangle.");

            var selfEdge = new WeightInpainting(
                new[] { Vector3.zero },
                new[] { 0, 0, 0 },
                1,
                1e-8f);
            var selfAdjacency = (List<int>[])typeof(WeightInpainting)
                .GetField("_adjacency", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(selfEdge);
            Assert.That(selfAdjacency[0], Is.Empty);

            InvokeInpaintingPrivate<object>(selfEdge, "AddOppositeVertex", dict, 0, 0, 0);
        }

        [Test]
        public void WeightInpainting_RejectsInvalidTriangleIndexAndNullConfidence()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WeightInpainting(
                new[] { Vector3.zero },
                new[] { 1, 0, 0 },
                1,
                1e-8f));

            var inpainting = new WeightInpainting(
                new[] { Vector3.zero },
                Array.Empty<int>(),
                1,
                1e-8f);
            Assert.Throws<ArgumentNullException>(() =>
                inpainting.Inpaint(new[] { Bone(0, 1f) }, null, 1));
        }

        [Test]
        public void WeightInpainting_PrivateConvertToBoneWeights_KeepsTopFourNormalizedWeights()
        {
            var vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            var triangles = new[] { 0, 1, 2 };
            var inpainting = new WeightInpainting(vertices, triangles, 8, 1e-8f);
            var weights = new[] { new BoneWeight(), new BoneWeight(), new BoneWeight() };
            var unknown = new List<int> { 1 };
            var bones = new List<int> { 0, 1, 2, 3, 4 };
            var results = new double[1, 5];
            results[0, 0] = 1.0;
            results[0, 1] = 2.0;
            results[0, 2] = 3.0;
            results[0, 3] = 4.0;
            results[0, 4] = 5.0;

            InvokeInpaintingPrivate<object>(
                inpainting,
                "ConvertToBoneWeights",
                unknown,
                bones,
                results,
                weights);

            Assert.That(weights[1].boneIndex0, Is.EqualTo(4));
            Assert.That(weights[1].boneIndex1, Is.EqualTo(3));
            Assert.That(weights[1].boneIndex2, Is.EqualTo(2));
            Assert.That(weights[1].boneIndex3, Is.EqualTo(1));
            Assert.That(
                weights[1].weight0 + weights[1].weight1 + weights[1].weight2 + weights[1].weight3,
                Is.EqualTo(1f).Within(1e-6f));

            var tiedBones = new List<int> { 4, 3, 2, 1, 0 };
            var tiedResults = new double[1, 5];
            for (int i = 0; i < tiedBones.Count; i++)
                tiedResults[0, i] = 1.0;
            InvokeInpaintingPrivate<object>(
                inpainting,
                "ConvertToBoneWeights",
                unknown,
                tiedBones,
                tiedResults,
                weights);
            Assert.That(
                new[]
                {
                    weights[1].boneIndex0,
                    weights[1].boneIndex1,
                    weights[1].boneIndex2,
                    weights[1].boneIndex3
                },
                Is.EqualTo(new[] { 0, 1, 2, 3 }));
        }

        [Test]
        public void WeightTransferSettings_Clone_CopiesAllValues()
        {
            var settings = new WeightTransferSettings
            {
                maxTransferDistance = 0.2f,
                normalAngleThreshold = 35f,
                enableInpainting = false,
                maxIterations = 321,
                tolerance = 1e-5f
            };

            var clone = settings.Clone();

            Assert.That(clone, Is.Not.SameAs(settings));
            Assert.That(clone.maxTransferDistance, Is.EqualTo(0.2f).Within(1e-6f));
            Assert.That(clone.normalAngleThreshold, Is.EqualTo(35f).Within(1e-6f));
            Assert.That(clone.enableInpainting, Is.False);
            Assert.That(clone.maxIterations, Is.EqualTo(321));
            Assert.That(clone.tolerance, Is.EqualTo(1e-5f).Within(1e-9f));
        }

        [Test]
        public void WeightTransferSettings_Default_ReturnsNewSettings()
        {
            var first = WeightTransferSettings.Default;
            var second = WeightTransferSettings.Default;

            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(first.maxTransferDistance, Is.EqualTo(0.05f).Within(1e-6f));
        }

        [Test]
        public void RobustWeightTransfer_IsKnownConfidence_RequiresFiniteThresholdValue()
        {
            Assert.That(RobustWeightTransfer.IsKnownConfidence(
                RobustWeightTransfer.KnownConfidenceThreshold), Is.True);
            Assert.That(RobustWeightTransfer.IsKnownConfidence(
                RobustWeightTransfer.KnownConfidenceThreshold - 1e-6f), Is.False);
            Assert.That(RobustWeightTransfer.IsKnownConfidence(float.NaN), Is.False);
            Assert.That(RobustWeightTransfer.IsKnownConfidence(float.PositiveInfinity), Is.False);
            Assert.That(RobustWeightTransfer.IsKnownConfidence(float.NegativeInfinity), Is.False);
        }

        [Test]
        public void RobustWeightTransfer_Transfer_ValidatesNullInputs()
        {
            var mesh = CreateTriangleMesh();
            try
            {
                Assert.That(RobustWeightTransfer.Transfer(null, new[] { Bone(0, 1f) }, mesh).success, Is.False);
                Assert.That(RobustWeightTransfer.Transfer(mesh, null, mesh).success, Is.False);
                Assert.That(RobustWeightTransfer.Transfer(mesh, Array.Empty<BoneWeight>(), mesh).success, Is.False);
                Assert.That(
                    RobustWeightTransfer.Transfer(
                        mesh,
                        new[] { Bone(0, 1f), Bone(0, 1f), Bone(0, 1f) },
                        null).success,
                    Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void RobustWeightTransfer_Transfer_ReturnsFailureForShortSourceWeights()
        {
            var sourceMesh = CreateTriangleMesh();
            var targetMesh = CreateTriangleMesh();
            try
            {
                var result = RobustWeightTransfer.Transfer(
                    sourceMesh,
                    new[] { Bone(0, 1f) },
                    targetMesh,
                    new WeightTransferSettings { enableInpainting = false });

                Assert.That(result.success, Is.False);
                Assert.That(result.errorMessage, Does.Contain("do not cover"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceMesh);
                UnityEngine.Object.DestroyImmediate(targetMesh);
            }
        }

        [Test]
        public void RobustWeightTransfer_Transfer_RejectsUnsafeSettingsAndMeshPayloadsWithoutThrowing()
        {
            var sourceMesh = CreateTriangleMesh();
            var targetMesh = CreateTriangleMesh();
            var sourceWeights = new[] { Bone(0, 1f), Bone(1, 1f), Bone(2, 1f) };
            try
            {
                var invalidDistance = RobustWeightTransfer.Transfer(
                    sourceMesh,
                    sourceWeights,
                    targetMesh,
                    new WeightTransferSettings { maxTransferDistance = float.NaN });
                Assert.That(invalidDistance.success, Is.False);
                Assert.That(invalidDistance.errorMessage, Does.Contain("finite"));

                var invalidTolerance = RobustWeightTransfer.Transfer(
                    sourceMesh,
                    sourceWeights,
                    targetMesh,
                    new WeightTransferSettings { tolerance = float.PositiveInfinity });
                Assert.That(invalidTolerance.success, Is.False);

                var invalidAngle = RobustWeightTransfer.Transfer(
                    sourceMesh,
                    sourceWeights,
                    targetMesh,
                    new WeightTransferSettings { normalAngleThreshold = float.NaN });
                Assert.That(invalidAngle.success, Is.False);

                var invalidIterations = RobustWeightTransfer.Transfer(
                    sourceMesh,
                    sourceWeights,
                    targetMesh,
                    new WeightTransferSettings { maxIterations = -1 });
                Assert.That(invalidIterations.success, Is.False);

                var vertices = targetMesh.vertices;
                vertices[1] = new Vector3(float.NaN, 0f, 0f);
                targetMesh.vertices = vertices;
                var invalidVertices = RobustWeightTransfer.Transfer(sourceMesh, sourceWeights, targetMesh);
                Assert.That(invalidVertices.success, Is.False);
                Assert.That(invalidVertices.errorMessage, Does.Contain("Target vertices"));

                targetMesh.vertices = sourceMesh.vertices;
                var sourceVertices = sourceMesh.vertices;
                sourceVertices[0] = new Vector3(float.NaN, 0f, 0f);
                sourceMesh.vertices = sourceVertices;
                var invalidSourceVertices = RobustWeightTransfer.Transfer(sourceMesh, sourceWeights, targetMesh);
                Assert.That(invalidSourceVertices.success, Is.False);
                Assert.That(invalidSourceVertices.errorMessage, Does.Contain("Source vertices"));

                sourceMesh.vertices = targetMesh.vertices;
                sourceMesh.normals = new[]
                {
                    Vector3.forward,
                    new Vector3(float.NaN, 0f, 0f),
                    Vector3.forward
                };
                var invalidSourceNormals = RobustWeightTransfer.Transfer(sourceMesh, sourceWeights, targetMesh);
                Assert.That(invalidSourceNormals.success, Is.False);
                Assert.That(invalidSourceNormals.errorMessage, Does.Contain("Source normals"));

                sourceMesh.RecalculateNormals();
                targetMesh.normals = new[]
                {
                    Vector3.forward,
                    new Vector3(float.NaN, 0f, 0f),
                    Vector3.forward
                };
                var invalidTargetNormals = RobustWeightTransfer.Transfer(sourceMesh, sourceWeights, targetMesh);
                Assert.That(invalidTargetNormals.success, Is.False);
                Assert.That(invalidTargetNormals.errorMessage, Does.Contain("Target normals"));

                targetMesh.vertices = sourceMesh.vertices;
                targetMesh.RecalculateNormals();
                sourceMesh.bindposes = Array.Empty<Matrix4x4>();
                var missingBindPoses = RobustWeightTransfer.Transfer(sourceMesh, sourceWeights, targetMesh);
                Assert.That(missingBindPoses.success, Is.False);
                Assert.That(missingBindPoses.errorMessage, Does.Contain("bind poses"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceMesh);
                UnityEngine.Object.DestroyImmediate(targetMesh);
            }
        }

        [Test]
        public void RobustWeightTransfer_PrivateValidation_RejectsUnavailableAndOverflowingVectors()
        {
            var method = typeof(RobustWeightTransfer).GetMethod(
                "TryValidateFiniteVectors",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            var unavailable = new object[] { null, "Vectors", true, null };
            Assert.That((bool)method.Invoke(null, unavailable), Is.False);
            Assert.That((string)unavailable[3], Does.Contain("unavailable"));

            var overflow = new object[]
            {
                new[]
                {
                    new Vector3(-float.MaxValue, 0f, 0f),
                    new Vector3(float.MaxValue, 0f, 0f)
                },
                "Vectors",
                true,
                null
            };
            Assert.That((bool)method.Invoke(null, overflow), Is.False);
            Assert.That((string)overflow[3], Does.Contain("supported"));

            Assert.Throws<ArgumentNullException>(() => RobustWeightTransfer.ApplyZeroWeightFallback(null, Array.Empty<BoneWeight>()));
            Assert.Throws<ArgumentNullException>(() => RobustWeightTransfer.ApplyZeroWeightFallback(Array.Empty<BoneWeight>(), null));
        }

        [Test]
        public void RobustWeightTransfer_Transfer_SanitizesInvalidSourceInfluences()
        {
            var sourceMesh = CreateTriangleMesh();
            var targetMesh = CreateTriangleMesh();
            var sourceWeights = new[]
            {
                new BoneWeight { boneIndex0 = 0, weight0 = float.NaN },
                Bone(99, 1f),
                Bone(2, 1f)
            };
            try
            {
                var result = RobustWeightTransfer.Transfer(
                    sourceMesh,
                    sourceWeights,
                    targetMesh,
                    new WeightTransferSettings
                    {
                        maxTransferDistance = 0.5f,
                        normalAngleThreshold = 180f,
                        enableInpainting = false
                    });

                Assert.That(result.success, Is.True, result.errorMessage);
                Assert.That(result.transferredCount, Is.EqualTo(1));
                Assert.That(result.weights[0].boneIndex0, Is.EqualTo(0));
                Assert.That(result.weights[0].weight0, Is.EqualTo(1f));
                Assert.That(result.weights[1].boneIndex0, Is.EqualTo(0));
                Assert.That(result.weights[1].weight0, Is.EqualTo(1f));
                Assert.That(result.weights[2].boneIndex0, Is.EqualTo(2));
                for (int i = 0; i < result.weights.Length; i++)
                {
                    Assert.That(float.IsNaN(result.weights[i].weight0), Is.False);
                    Assert.That(result.weights[i].boneIndex0, Is.InRange(0, 2));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceMesh);
                UnityEngine.Object.DestroyImmediate(targetMesh);
            }
        }

        [Test]
        public void RobustWeightTransfer_Transfer_UsesSurfaceNormalWhenTargetNormalsAreMissing()
        {
            var sourceMesh = CreateTriangleMesh();
            var targetMesh = CreateTriangleMesh();
            targetMesh.normals = Array.Empty<Vector3>();
            try
            {
                var result = RobustWeightTransfer.Transfer(
                    sourceMesh,
                    new[] { Bone(0, 1f), Bone(1, 1f), Bone(2, 1f) },
                    targetMesh,
                    new WeightTransferSettings
                    {
                        maxTransferDistance = 0.5f,
                        normalAngleThreshold = 0f,
                        enableInpainting = false
                    });

                Assert.That(result.success, Is.True, result.errorMessage);
                Assert.That(result.transferredCount, Is.EqualTo(3));

                sourceMesh.normals = new[] { Vector3.zero, Vector3.zero, Vector3.zero };
                targetMesh.normals = new[] { Vector3.zero, Vector3.zero, Vector3.zero };
                var zeroNormals = RobustWeightTransfer.Transfer(
                    sourceMesh,
                    new[] { Bone(0, 1f), Bone(1, 1f), Bone(2, 1f) },
                    targetMesh,
                    new WeightTransferSettings
                    {
                        maxTransferDistance = 0.5f,
                        normalAngleThreshold = 0f,
                        enableInpainting = false
                    });
                Assert.That(zeroNormals.success, Is.True, zeroNormals.errorMessage);
                Assert.That(zeroNormals.transferredCount, Is.EqualTo(3));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceMesh);
                UnityEngine.Object.DestroyImmediate(targetMesh);
            }
        }

        [Test]
        public void RobustWeightTransfer_TransferWeights_ReturnsSourceWeightsOnFailure()
        {
            var source = new[] { Bone(2, 1f) };
            var target = CreateTriangleMesh();
            try
            {
                LogAssert.Expect(LogType.Error, "Weight transfer failed: Source mesh is null.");

                Assert.That(RobustWeightTransfer.TransferWeights(null, source, target), Is.SameAs(source));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void RobustWeightTransfer_Transfer_CopiesWeightsForMatchingTriangle()
        {
            var sourceMesh = CreateTriangleMesh();
            var targetMesh = CreateTriangleMesh();
            var sourceWeights = new[] { Bone(0, 1f), Bone(1, 1f), Bone(2, 1f) };
            try
            {
                var result = RobustWeightTransfer.Transfer(
                    sourceMesh,
                    sourceWeights,
                    targetMesh,
                    new WeightTransferSettings
                    {
                        maxTransferDistance = 0.5f,
                        normalAngleThreshold = 180f,
                        enableInpainting = false,
                        maxIterations = 64,
                        tolerance = 1e-6f
                    });

                Assert.That(result.success, Is.True, result.errorMessage);
                Assert.That(result.totalVertices, Is.EqualTo(3));
                Assert.That(result.transferredCount, Is.EqualTo(3));
                Assert.That(result.weights[0].boneIndex0, Is.EqualTo(0));
                Assert.That(result.weights[1].boneIndex0, Is.EqualTo(1));
                Assert.That(result.weights[2].boneIndex0, Is.EqualTo(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceMesh);
                UnityEngine.Object.DestroyImmediate(targetMesh);
            }
        }

        [Test]
        public void RobustWeightTransfer_UniformOffsetFoundMatchesRemainUnknownBelowConfidenceThreshold()
        {
            var sourceMesh = CreateTriangleMesh();
            var targetMesh = CreateTriangleMesh();
            var sourceWeights = new[] { Bone(0, 1f), Bone(1, 1f), Bone(2, 1f) };
            var settings = new WeightTransferSettings
            {
                maxTransferDistance = 0.001f,
                normalAngleThreshold = 180f,
                enableInpainting = true,
                maxIterations = 64,
                tolerance = 1e-6f
            };

            try
            {
                var targetVertices = targetMesh.vertices;
                for (int i = 0; i < targetVertices.Length; i++)
                {
                    targetVertices[i] += Vector3.forward;
                }
                targetMesh.vertices = targetVertices;
                targetMesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
                targetMesh.RecalculateBounds();

                float searchDistance = InvokePrivate<float>(
                    "ResolveMaxTransferDistance",
                    settings,
                    sourceMesh.vertices,
                    targetVertices);
                using (var query = new MeshSpatialQuery(
                           sourceMesh.vertices,
                           sourceMesh.triangles,
                           sourceMesh.normals))
                {
                    for (int i = 0; i < targetVertices.Length; i++)
                    {
                        Assert.That(query.FindClosestPoint(targetVertices[i], searchDistance).found, Is.True);
                    }
                }

                var result = RobustWeightTransfer.Transfer(
                    sourceMesh,
                    sourceWeights,
                    targetMesh,
                    settings);

                Assert.That(result.success, Is.True, result.errorMessage);
                Assert.That(result.transferredCount, Is.EqualTo(0));
                Assert.That(result.inpaintedCount, Is.EqualTo(0));
                for (int i = 0; i < result.weights.Length; i++)
                {
                    Assert.That(result.weights[i].boneIndex0, Is.EqualTo(sourceWeights[i].boneIndex0));
                    Assert.That(result.weights[i].weight0, Is.EqualTo(1f).Within(1e-6f));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceMesh);
                UnityEngine.Object.DestroyImmediate(targetMesh);
            }
        }

        [Test]
        public void RobustWeightTransfer_TransferWeights_ReturnsTransferredWeightsOnSuccess()
        {
            var sourceMesh = CreateTriangleMesh();
            var targetMesh = CreateTriangleMesh();
            var sourceWeights = new[] { Bone(0, 1f), Bone(1, 1f), Bone(2, 1f) };
            try
            {
                var transferred = RobustWeightTransfer.TransferWeights(
                    sourceMesh,
                    sourceWeights,
                    targetMesh,
                    new WeightTransferSettings
                    {
                        maxTransferDistance = 0.5f,
                        normalAngleThreshold = 180f,
                        enableInpainting = false,
                        maxIterations = 16,
                        tolerance = 1e-6f
                    });

                Assert.That(transferred, Is.Not.SameAs(sourceWeights));
                Assert.That(transferred[0].boneIndex0, Is.EqualTo(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceMesh);
                UnityEngine.Object.DestroyImmediate(targetMesh);
            }
        }

        [Test]
        public void RobustWeightTransfer_PrivateHelpers_HandleSubMeshesAndFallbackDistance()
        {
            var mesh = new Mesh { name = "SubMeshTriangle" };
            try
            {
                mesh.vertices = new[]
                {
                    Vector3.zero,
                    Vector3.right,
                    Vector3.up,
                    Vector3.forward
                };
                mesh.subMeshCount = 2;
                mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
                mesh.SetIndices(new[] { 0, 3 }, MeshTopology.Lines, 1);
                mesh.RecalculateBounds();

                var triangles = InvokePrivate<int[]>("GetAllTriangles", mesh);
                Assert.That(triangles, Is.EqualTo(new[] { 0, 1, 2 }));
                Assert.That(InvokePrivate<int[]>("GetAllTriangles", new object[] { null }), Is.Empty);

                var settings = new WeightTransferSettings { maxTransferDistance = -1f };
                var distance = InvokePrivate<float>(
                    "ResolveMaxTransferDistance",
                    settings,
                    new[] { Vector3.zero, Vector3.right },
                    new[] { Vector3.zero, Vector3.right * 2f });

                Assert.That(distance, Is.GreaterThan(0f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void RobustWeightTransfer_PrivateStage2Inpainting_CountsUnknownVertices()
        {
            var vertices = new[]
            {
                Vector3.zero,
                Vector3.right,
                Vector3.up
            };
            var triangles = new[] { 0, 1, 2 };
            var weights = new[] { Bone(0, 1f), new BoneWeight(), Bone(1, 1f) };
            var confidence = new[] { 1f, 0f, 1f };
            var arguments = new object[]
            {
                vertices,
                triangles,
                2,
                weights,
                confidence,
                new WeightTransferSettings
                {
                    maxIterations = 8,
                    tolerance = 1e-8f
                },
                0
            };

            InvokePrivate<object>("Stage2Inpainting", arguments);

            Assert.That(weights[1].weight0 + weights[1].weight1 + weights[1].weight2 + weights[1].weight3,
                Is.EqualTo(1f).Within(1e-5f));
            Assert.That(RobustWeightTransfer.IsKnownConfidence(confidence[1]), Is.True);
            Assert.That((int)arguments[6], Is.EqualTo(1));

            var isolatedWeights = new[]
            {
                Bone(0, 1f),
                Bone(1, 1f),
                Bone(0, 1f),
                new BoneWeight()
            };
            var isolatedConfidence = new[] { 1f, 1f, 1f, 0f };
            var isolatedArguments = new object[]
            {
                new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward },
                triangles,
                2,
                isolatedWeights,
                isolatedConfidence,
                new WeightTransferSettings
                {
                    maxIterations = 8,
                    tolerance = 1e-8f
                },
                0
            };

            InvokePrivate<object>("Stage2Inpainting", isolatedArguments);

            Assert.That((int)isolatedArguments[6], Is.EqualTo(0));
            Assert.That(isolatedConfidence[3], Is.EqualTo(0f));
            Assert.That(isolatedWeights[3].weight0, Is.EqualTo(0f));

            var invalidWeightArguments = new object[]
            {
                new BoneWeight { boneIndex0 = 0, weight0 = float.NaN },
                2
            };
            Assert.That(
                InvokePrivate<bool>("TryNormalizeInpaintedWeight", invalidWeightArguments),
                Is.False);
        }

        [Test]
        public void RobustWeightTransfer_RunStage2_RequiresPrerequisitesAndClearsUnknownWeights()
        {
            var vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            var triangles = new[] { 0, 1, 2 };
            var settings = new WeightTransferSettings
            {
                enableInpainting = true,
                maxIterations = 8,
                tolerance = 1e-8f
            };

            var allUnknownWeights = new[] { Bone(0, 1f), Bone(1, 1f), Bone(1, 1f) };
            var allUnknownConfidence = new[] { 0f, float.NaN, 0f };
            var allUnknownArguments = new object[]
            {
                settings,
                triangles,
                vertices,
                2,
                allUnknownWeights,
                allUnknownConfidence,
                0
            };

            InvokePrivate<object>("RunStage2InpaintingIfNeeded", allUnknownArguments);

            Assert.That((int)allUnknownArguments[6], Is.EqualTo(0));
            Assert.That(allUnknownConfidence, Is.EqualTo(new[] { 0f, 0f, 0f }));
            for (int i = 0; i < allUnknownWeights.Length; i++)
            {
                Assert.That(
                    allUnknownWeights[i].weight0 +
                    allUnknownWeights[i].weight1 +
                    allUnknownWeights[i].weight2 +
                    allUnknownWeights[i].weight3,
                    Is.EqualTo(0f));
            }

            var missingTrianglesWeights = new[] { Bone(0, 1f), Bone(1, 1f), Bone(1, 1f) };
            var missingTrianglesConfidence = new[] { 1f, 0f, 1f };
            var missingTrianglesArguments = new object[]
            {
                settings,
                Array.Empty<int>(),
                vertices,
                2,
                missingTrianglesWeights,
                missingTrianglesConfidence,
                0
            };

            InvokePrivate<object>("RunStage2InpaintingIfNeeded", missingTrianglesArguments);

            Assert.That((int)missingTrianglesArguments[6], Is.EqualTo(0));
            Assert.That(missingTrianglesWeights[0].weight0, Is.EqualTo(1f));
            Assert.That(missingTrianglesWeights[1].weight0, Is.EqualTo(0f));
            Assert.That(missingTrianglesWeights[2].weight0, Is.EqualTo(1f));

            var noBonesWeights = new[] { Bone(0, 1f), Bone(1, 1f), Bone(1, 1f) };
            var noBonesConfidence = new[] { 1f, 0f, 1f };
            var noBonesArguments = new object[]
            {
                settings,
                triangles,
                vertices,
                0,
                noBonesWeights,
                noBonesConfidence,
                0
            };

            InvokePrivate<object>("RunStage2InpaintingIfNeeded", noBonesArguments);

            Assert.That((int)noBonesArguments[6], Is.EqualTo(0));
            Assert.That(noBonesWeights[1].weight0, Is.EqualTo(0f));

            var validWeights = new[] { Bone(0, 1f), Bone(1, 1f), Bone(1, 1f) };
            var validConfidence = new[] { 1f, 0f, 1f };
            var validArguments = new object[]
            {
                settings,
                triangles,
                vertices,
                2,
                validWeights,
                validConfidence,
                0
            };

            InvokePrivate<object>("RunStage2InpaintingIfNeeded", validArguments);

            Assert.That((int)validArguments[6], Is.EqualTo(1));
            Assert.That(RobustWeightTransfer.IsKnownConfidence(validConfidence[1]), Is.True);
            Assert.That(
                validWeights[1].weight0 + validWeights[1].weight1 +
                validWeights[1].weight2 + validWeights[1].weight3,
                Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void RobustWeightTransfer_PrivateHelpers_InterpolateAndPercentile()
        {
            var interpolated = InvokePrivate<BoneWeight>(
                "InterpolateBoneWeight",
                Bone(0, 1f),
                Bone(1, 1f),
                Bone(2, 1f),
                new Vector3(0.2f, 0.3f, 0.5f));

            Assert.That(interpolated.weight0 + interpolated.weight1 + interpolated.weight2 + interpolated.weight3, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(new[] { interpolated.boneIndex0, interpolated.boneIndex1, interpolated.boneIndex2 }, Does.Contain(2));

            var fourSlots = InvokePrivate<BoneWeight>(
                "InterpolateBoneWeight",
                new BoneWeight { boneIndex0 = 0, weight0 = 0.5f, boneIndex1 = 1, weight1 = 0.5f },
                new BoneWeight { boneIndex0 = 2, weight0 = 1f },
                new BoneWeight { boneIndex0 = 3, weight0 = 1f },
                new Vector3(0.25f, 0.25f, 0.5f));

            Assert.That(fourSlots.weight3, Is.GreaterThan(0f));

            var deterministicTies = InvokePrivate<BoneWeight>(
                "InterpolateBoneWeight",
                new BoneWeight
                {
                    boneIndex0 = 3, weight0 = 0.25f,
                    boneIndex1 = 2, weight1 = 0.25f,
                    boneIndex2 = 1, weight2 = 0.25f,
                    boneIndex3 = 0, weight3 = 0.25f
                },
                new BoneWeight(),
                new BoneWeight(),
                new Vector3(1f, 0f, 0f));
            Assert.That(
                new[]
                {
                    deterministicTies.boneIndex0,
                    deterministicTies.boneIndex1,
                    deterministicTies.boneIndex2,
                    deterministicTies.boneIndex3
                },
                Is.EqualTo(new[] { 0, 1, 2, 3 }));

            Assert.That(InvokePrivate<float>("CalculateBoundsDiagonal", new object[] { null }), Is.EqualTo(0f));
            Assert.That(
                InvokePrivate<float>(
                    "CalculatePercentileDisplacement",
                    null,
                    new[] { Vector3.zero },
                    1f),
                Is.EqualTo(0f));
            Assert.That(
                InvokePrivate<float>(
                    "CalculatePercentileDisplacement",
                    Array.Empty<Vector3>(),
                    Array.Empty<Vector3>(),
                    1f),
                Is.EqualTo(0f));
            Assert.That(
                InvokePrivate<float>(
                    "CalculatePercentileDisplacement",
                    new[] { Vector3.zero, Vector3.right, Vector3.up },
                    new[] { Vector3.zero, Vector3.right * 3f, Vector3.up * 2f },
                    1f),
                Is.EqualTo(2f).Within(1e-6f));
        }

        [Test]
        public void RobustWeightTransfer_Transfer_CoversFallbackDefaults()
        {
            var sourcePointMesh = new Mesh { name = "WeightTransferSourcePoint" };
            var targetMesh = CreateTriangleMesh();
            try
            {
                sourcePointMesh.vertices = new[] { Vector3.zero };
                sourcePointMesh.normals = new[] { Vector3.forward };
                sourcePointMesh.triangles = Array.Empty<int>();
                sourcePointMesh.bindposes = new[] { Matrix4x4.identity };
                sourcePointMesh.RecalculateBounds();

                var shortWeights = new[] { Bone(5, 1f) };
                var noTransfer = RobustWeightTransfer.Transfer(
                    sourcePointMesh,
                    shortWeights,
                    targetMesh,
                    new WeightTransferSettings
                    {
                        maxTransferDistance = 0.0001f,
                        normalAngleThreshold = 180f,
                        enableInpainting = false,
                        maxIterations = 4,
                        tolerance = 1e-6f
                    });

                Assert.That(noTransfer.success, Is.True);
                Assert.That(noTransfer.weights, Has.Length.EqualTo(3));
                Assert.That(noTransfer.weights[0].boneIndex0, Is.EqualTo(0));
                Assert.That(noTransfer.weights[0].weight0, Is.EqualTo(1f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourcePointMesh);
                UnityEngine.Object.DestroyImmediate(targetMesh);
            }
        }

        [Test]
        public void RobustWeightTransfer_PrivateHelpers_CoverRemainingBranches()
        {
            var lineOnlyMesh = new Mesh { name = "LineOnlyFallbackMesh" };
            try
            {
                var dict = new System.Collections.Generic.Dictionary<int, float> { { 2, 0.25f } };
                InvokePrivate<object>("AddBoneWeight", dict, 2, 0.5f);
                InvokePrivate<object>("AddBoneWeight", dict, 3, 0f);
                Assert.That(dict[2], Is.EqualTo(0.75f).Within(1e-6f));
                Assert.That(dict.ContainsKey(3), Is.False);

                lineOnlyMesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                lineOnlyMesh.triangles = new[] { 0, 1, 2 };
                lineOnlyMesh.subMeshCount = 2;
                lineOnlyMesh.SetIndices(new[] { 0, 1 }, MeshTopology.Lines, 0);
                lineOnlyMesh.SetIndices(new[] { 1, 2 }, MeshTopology.Lines, 1);
                LogAssert.Expect(LogType.Error, "Failed getting triangles. Submesh topology is lines or points.");
                LogAssert.Expect(LogType.Error, "Failed getting triangles. Submesh topology is lines or points.");
                Assert.That(InvokePrivate<int[]>("GetAllTriangles", lineOnlyMesh), Is.Empty);

                var distance = InvokePrivate<float>(
                    "ResolveMaxTransferDistance",
                    new WeightTransferSettings { maxTransferDistance = 0.1f },
                    new[] { Vector3.zero, Vector3.right },
                    new[] { Vector3.zero, Vector3.right * 10f });
                Assert.That(distance, Is.EqualTo(1f).Within(1e-6f));

                var deformationDrivenDistance = InvokePrivate<float>(
                    "ResolveMaxTransferDistance",
                    new WeightTransferSettings { maxTransferDistance = 0.1f },
                    new[] { Vector3.zero, Vector3.right, Vector3.up },
                    new[] { Vector3.right * 2f, Vector3.right * 3f, Vector3.up + Vector3.right * 2f });
                Assert.That(deformationDrivenDistance, Is.EqualTo(2.4f).Within(1e-6f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(lineOnlyMesh);
            }
        }

        [Test]
        public void RobustWeightTransfer_HelperGuards_CoverRejectAndFallbackBranches()
        {
            Assert.That(
                RobustWeightTransfer.ShouldRejectTransfer(Vector3.one, Vector3.zero, 0.1f, 1f, 0f),
                Is.True);
            Assert.That(
                RobustWeightTransfer.ShouldRejectTransfer(Vector3.zero, Vector3.zero, 1f, -1f, 0f),
                Is.True);
            Assert.That(
                RobustWeightTransfer.ShouldRejectTransfer(Vector3.zero, Vector3.zero, 1f, 1f, 0f),
                Is.False);

            var sourceFallback = new[] { Bone(4, 1f), Bone(5, 1f) };
            var matchingWeights = new[] { new BoneWeight(), Bone(1, 1f) };
            Assert.That(RobustWeightTransfer.ApplyZeroWeightFallback(matchingWeights, sourceFallback), Is.EqualTo(1));
            Assert.That(matchingWeights[0].boneIndex0, Is.EqualTo(4));

            var defaultWeights = new[] { new BoneWeight(), new BoneWeight() };
            Assert.That(RobustWeightTransfer.ApplyZeroWeightFallback(defaultWeights, new[] { Bone(9, 1f) }), Is.EqualTo(2));
            Assert.That(defaultWeights[0].boneIndex0, Is.EqualTo(0));
            Assert.That(defaultWeights[0].weight0, Is.EqualTo(1f));
        }

        private static Mesh CreateLongRangeTriangleMesh()
        {
            var mesh = new Mesh { name = "LongRangeTriangle" };
            mesh.vertices = new[]
            {
                Vector3.zero,
                Vector3.up,
                Vector3.forward
            };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateAdjacentCellMesh()
        {
            var mesh = new Mesh { name = "AdjacentCellTriangles" };
            mesh.vertices = new[]
            {
                new Vector3(0.030f, 0.490f, 0f),
                new Vector3(0.030f, 0.510f, 0f),
                new Vector3(0.030f, 0.500f, 0.020f),
                new Vector3(0.032f, 0.490f, 0f),
                new Vector3(0.032f, 0.510f, 0f),
                new Vector3(0.032f, 0.500f, 0.020f),
                new Vector3(0f, 0f, 1f),
                new Vector3(0f, 1f, 1f),
                new Vector3(0f, 1f, 1f)
            };
            mesh.triangles = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static float BruteForceClosestDistance(Vector3 point, Mesh mesh)
        {
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            float closestDistance = float.MaxValue;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                var closest = ClosestPointOnTriangle(
                    point,
                    vertices[triangles[i]],
                    vertices[triangles[i + 1]],
                    vertices[triangles[i + 2]]);
                closestDistance = Mathf.Min(closestDistance, Vector3.Distance(point, closest));
            }

            return closestDistance;
        }

        private static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = p - a;
            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;

            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                return a + v * ab;
            }

            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                return a + w * ac;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return b + w * (c - b);
            }

            float denominator = va + vb + vc;
            if (Mathf.Abs(denominator) <= 1e-12f)
                return a;
            float v2 = vb / denominator;
            float w2 = vc / denominator;
            return a + ab * v2 + ac * w2;
        }

        private static Mesh CreateTriangleMesh()
        {
            var mesh = new Mesh { name = "WeightTransferTriangle" };
            mesh.vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f)
            };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity, Matrix4x4.identity };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static BoneWeight Bone(int boneIndex, float weight)
        {
            return new BoneWeight
            {
                boneIndex0 = boneIndex,
                weight0 = weight
            };
        }

        private static T InvokePrivate<T>(string methodName, params object[] args)
        {
            var method = typeof(RobustWeightTransfer).GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (T)method.Invoke(null, args);
        }

        private static T InvokeInpaintingPrivate<T>(WeightInpainting target, string methodName, params object[] args)
        {
            var method = typeof(WeightInpainting).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (T)method.Invoke(target, args);
        }

        private static NativeSparseMatrixCSR CreateRawMatrix(
            int rows,
            int cols,
            int nonZeroCount,
            double[] values,
            int[] colIndices,
            int[] rowPointers,
            double[] diagonal)
        {
            return new NativeSparseMatrixCSR
            {
                RowCount = rows,
                ColCount = cols,
                NonZeroCount = nonZeroCount,
                Values = new NativeArray<double>(values, Allocator.TempJob),
                ColIndices = new NativeArray<int>(colIndices, Allocator.TempJob),
                RowPointers = new NativeArray<int>(rowPointers, Allocator.TempJob),
                Diagonal = new NativeArray<double>(diagonal, Allocator.TempJob)
            };
        }

        private static T InvokeSpatialPrivate<T>(MeshSpatialQuery target, string methodName, params object[] args)
        {
            var method = typeof(MeshSpatialQuery).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (T)method.Invoke(target, args);
        }
    }
}
#endif
