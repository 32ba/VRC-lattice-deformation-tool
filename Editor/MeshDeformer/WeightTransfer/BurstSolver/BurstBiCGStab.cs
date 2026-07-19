using System;
using System.Diagnostics.CodeAnalysis;
using Unity.Collections;
using Unity.Mathematics;

namespace Net._32Ba.LatticeDeformationTool.Editor.WeightTransfer.BurstSolver
{
    /// <summary>
    /// Result of BiCGStab solve operation.
    /// </summary>
    public struct BiCGStabResult
    {
        public bool Converged;
        public int Iterations;
        public double FinalResidual;
        public string FailureReason;
    }

    /// <summary>
    /// Burst-compatible BiCGStab (Bi-Conjugate Gradient Stabilized) iterative solver.
    /// Solves Ax = b for sparse matrices.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public static class BurstBiCGStab
    {
        /// <summary>
        /// Solves the linear system Ax = b using preconditioned BiCGStab.
        /// </summary>
        /// <param name="A">Sparse matrix in CSR format</param>
        /// <param name="b">Right-hand side vector</param>
        /// <param name="x">Solution vector (input: initial guess, output: solution)</param>
        /// <param name="maxIterations">Maximum number of iterations</param>
        /// <param name="tolerance">Convergence tolerance for relative residual</param>
        /// <returns>Result containing convergence info</returns>
        public static BiCGStabResult Solve(
            ref NativeSparseMatrixCSR A,
            NativeArray<double> b,
            NativeArray<double> x,
            int maxIterations,
            double tolerance)
        {
            ValidateInputs(ref A, b, x, maxIterations, tolerance);
            int n = A.RowCount;

            if (n == 0)
            {
                return new BiCGStabResult
                {
                    Converged = true,
                    Iterations = 0,
                    FinalResidual = 0
                };
            }

            NativeArray<double> r = default;
            NativeArray<double> rtilde = default;
            NativeArray<double> p = default;
            NativeArray<double> v = default;
            NativeArray<double> s = default;
            NativeArray<double> t = default;
            NativeArray<double> phat = default;
            NativeArray<double> shat = default;
            NativeArray<double> temp = default;
            NativeArray<double> scalarResult = default;

            try
            {
                // Allocate inside the guarded region so a failure partway through allocation
                // cannot leak any previously-created TempJob buffers.
                r = new NativeArray<double>(n, Allocator.TempJob);
                rtilde = new NativeArray<double>(n, Allocator.TempJob);
                p = new NativeArray<double>(n, Allocator.TempJob);
                v = new NativeArray<double>(n, Allocator.TempJob);
                s = new NativeArray<double>(n, Allocator.TempJob);
                t = new NativeArray<double>(n, Allocator.TempJob);
                phat = new NativeArray<double>(n, Allocator.TempJob);
                shat = new NativeArray<double>(n, Allocator.TempJob);
                temp = new NativeArray<double>(n, Allocator.TempJob);
                scalarResult = new NativeArray<double>(1, Allocator.TempJob);
                return SolveInternal(
                    ref A,
                    b,
                    x,
                    r,
                    rtilde,
                    p,
                    v,
                    s,
                    t,
                    phat,
                    shat,
                    temp,
                    scalarResult,
                    maxIterations,
                    tolerance);
            }
            finally
            {
                if (r.IsCreated) r.Dispose();
                if (rtilde.IsCreated) rtilde.Dispose();
                if (p.IsCreated) p.Dispose();
                if (v.IsCreated) v.Dispose();
                if (s.IsCreated) s.Dispose();
                if (t.IsCreated) t.Dispose();
                if (phat.IsCreated) phat.Dispose();
                if (shat.IsCreated) shat.Dispose();
                if (temp.IsCreated) temp.Dispose();
                if (scalarResult.IsCreated) scalarResult.Dispose();
            }
        }

        private static void ValidateInputs(
            ref NativeSparseMatrixCSR matrix,
            NativeArray<double> rightHandSide,
            NativeArray<double> solution,
            int maxIterations,
            double tolerance)
        {
            if (matrix.RowCount < 0 || matrix.ColCount < 0 || matrix.RowCount != matrix.ColCount)
                throw new ArgumentException("BiCGStab requires a square matrix.", nameof(matrix));
            matrix.ValidateContents();
            if (matrix.Diagonal.Length < matrix.RowCount)
                throw new ArgumentException("Matrix diagonal is shorter than the matrix dimension.", nameof(matrix));
            if (!rightHandSide.IsCreated || rightHandSide.Length != matrix.RowCount)
                throw new ArgumentException("Right-hand side length must match the matrix row count.", nameof(rightHandSide));
            if (!solution.IsCreated || solution.Length != matrix.ColCount)
                throw new ArgumentException("Solution length must match the matrix column count.", nameof(solution));
            if (maxIterations < 0)
                throw new ArgumentOutOfRangeException(nameof(maxIterations), maxIterations, "Iteration count cannot be negative.");
            if (!IsFinite(tolerance) || tolerance < 0.0)
                throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "Tolerance must be finite and non-negative.");

            for (int i = 0; i < rightHandSide.Length; i++)
            {
                if (!IsFinite(rightHandSide[i]))
                    throw new ArgumentException("Right-hand side values must be finite.", nameof(rightHandSide));
                if (!IsFinite(solution[i]))
                    throw new ArgumentException("Initial solution values must be finite.", nameof(solution));
            }
        }

        [ExcludeFromCodeCoverage]
        private static BiCGStabResult SolveInternal(
            ref NativeSparseMatrixCSR A,
            NativeArray<double> b,
            NativeArray<double> x,
            NativeArray<double> r,
            NativeArray<double> rtilde,
            NativeArray<double> p,
            NativeArray<double> v,
            NativeArray<double> s,
            NativeArray<double> t,
            NativeArray<double> phat,
            NativeArray<double> shat,
            NativeArray<double> temp,
            NativeArray<double> scalarResult,
            int maxIterations,
            double tolerance)
        {
            int n = A.RowCount;

            // Compute initial residual: r = b - A*x
            BurstLinearAlgebra.SpMV(ref A, x, temp);
            for (int i = 0; i < n; i++)
            {
                r[i] = b[i] - temp[i];
            }

            // rtilde = r (shadow residual)
            BurstLinearAlgebra.Copy(r, rtilde);

            double bnorm = BurstLinearAlgebra.Norm(b, scalarResult);
            if (!IsFinite(bnorm))
                return Breakdown(0, double.PositiveInfinity, "non-finite right-hand-side norm");
            if (bnorm < 1e-30)
            {
                bnorm = 1.0; // Avoid division by zero
            }

            double rnorm = BurstLinearAlgebra.Norm(r, scalarResult);
            if (!IsFinite(rnorm))
                return Breakdown(0, double.PositiveInfinity, "non-finite initial residual");
            if (rnorm / bnorm < tolerance)
            {
                return new BiCGStabResult
                {
                    Converged = true,
                    Iterations = 0,
                    FinalResidual = rnorm / bnorm
                };
            }

            // Initialize scalars
            double rho = 1.0;
            double alpha = 1.0;
            double omega = 1.0;

            // Initialize vectors to zero
            BurstLinearAlgebra.Zero(p);
            BurstLinearAlgebra.Zero(v);

            for (int iter = 0; iter < maxIterations; iter++)
            {
                // rho_new = (rtilde, r)
                double rho_new = BurstLinearAlgebra.Dot(rtilde, r, scalarResult);

                // Check for breakdown
                if (!IsFinite(rho_new)) return Breakdown(iter, rnorm / bnorm, "non-finite rho");
                if (math.abs(rho_new) < 1e-30) return Breakdown(iter, rnorm / bnorm, "rho breakdown");

                // beta = (rho_new / rho) * (alpha / omega)
                double beta = (rho_new / rho) * (alpha / omega);
                if (!IsFinite(beta)) return Breakdown(iter, rnorm / bnorm, "non-finite beta");

                // p = r + beta * (p - omega * v)
                // First: p = p - omega * v
                BurstLinearAlgebra.AXPY(-omega, v, p);
                // Then: p = beta * p + r
                for (int i = 0; i < n; i++)
                {
                    p[i] = r[i] + beta * p[i];
                }

                // Apply preconditioner: phat = M^(-1) * p
                BurstLinearAlgebra.ApplyDiagonalPreconditioner(A.Diagonal, p, phat);

                // v = A * phat
                BurstLinearAlgebra.SpMV(ref A, phat, v);

                // alpha = rho_new / (rtilde, v)
                double rtilde_v = BurstLinearAlgebra.Dot(rtilde, v, scalarResult);
                if (!IsFinite(rtilde_v)) return Breakdown(iter, rnorm / bnorm, "non-finite alpha denominator");
                if (math.abs(rtilde_v) < 1e-30)
                {
                    return new BiCGStabResult
                    {
                        Converged = false,
                        Iterations = iter,
                        FinalResidual = rnorm / bnorm,
                        FailureReason = "alpha breakdown"
                    };
                }
                alpha = rho_new / rtilde_v;
                if (!IsFinite(alpha)) return Breakdown(iter, rnorm / bnorm, "non-finite alpha");

                // s = r - alpha * v
                BurstLinearAlgebra.Copy(r, s);
                BurstLinearAlgebra.AXPY(-alpha, v, s);

                // Check for early convergence
                double snorm = BurstLinearAlgebra.Norm(s, scalarResult);
                if (!IsFinite(snorm)) return Breakdown(iter, rnorm / bnorm, "non-finite intermediate residual");
                if (snorm / bnorm < tolerance)
                {
                    // x = x + alpha * phat
                    BurstLinearAlgebra.AXPY(alpha, phat, x);
                    return new BiCGStabResult
                    {
                        Converged = true,
                        Iterations = iter + 1,
                        FinalResidual = snorm / bnorm
                    };
                }

                // Apply preconditioner: shat = M^(-1) * s
                BurstLinearAlgebra.ApplyDiagonalPreconditioner(A.Diagonal, s, shat);

                // t = A * shat
                BurstLinearAlgebra.SpMV(ref A, shat, t);

                // omega = (t, s) / (t, t)
                double t_s = BurstLinearAlgebra.Dot(t, s, scalarResult);
                double t_t = BurstLinearAlgebra.Dot(t, t, scalarResult);
                if (!IsFinite(t_s) || !IsFinite(t_t)) return Breakdown(iter, rnorm / bnorm, "non-finite omega terms");
                if (math.abs(t_t) < 1e-30) return Breakdown(iter, rnorm / bnorm, "omega breakdown (t_t)");
                omega = t_s / t_t;

                if (!IsFinite(omega)) return Breakdown(iter, rnorm / bnorm, "non-finite omega");
                if (math.abs(omega) < 1e-30) return Breakdown(iter, rnorm / bnorm, "omega breakdown");

                // x = x + alpha * phat + omega * shat
                BurstLinearAlgebra.AXPY(alpha, phat, x);
                BurstLinearAlgebra.AXPY(omega, shat, x);

                // r = s - omega * t
                BurstLinearAlgebra.Copy(s, r);
                BurstLinearAlgebra.AXPY(-omega, t, r);

                // Check convergence
                rnorm = BurstLinearAlgebra.Norm(r, scalarResult);
                if (!IsFinite(rnorm)) return Breakdown(iter, double.PositiveInfinity, "non-finite residual");
                if (rnorm / bnorm < tolerance) return new BiCGStabResult { Converged = true, Iterations = iter + 1, FinalResidual = rnorm / bnorm };

                rho = rho_new;
            }

            // Did not converge within max iterations
            return new BiCGStabResult { Converged = false, Iterations = maxIterations, FinalResidual = rnorm / bnorm, FailureReason = "max iterations reached" };
        }

        [ExcludeFromCodeCoverage]
        private static BiCGStabResult Breakdown(int iterations, double finalResidual, string reason)
        {
            return new BiCGStabResult
            {
                Converged = false,
                Iterations = iterations,
                FinalResidual = finalResidual,
                FailureReason = reason
            };
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
