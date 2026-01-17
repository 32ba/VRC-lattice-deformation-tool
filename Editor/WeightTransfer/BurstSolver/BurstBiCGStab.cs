using System;
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

            // Allocate work vectors
            var r = new NativeArray<double>(n, Allocator.TempJob);
            var rtilde = new NativeArray<double>(n, Allocator.TempJob);
            var p = new NativeArray<double>(n, Allocator.TempJob);
            var v = new NativeArray<double>(n, Allocator.TempJob);
            var s = new NativeArray<double>(n, Allocator.TempJob);
            var t = new NativeArray<double>(n, Allocator.TempJob);
            var phat = new NativeArray<double>(n, Allocator.TempJob);
            var shat = new NativeArray<double>(n, Allocator.TempJob);
            var temp = new NativeArray<double>(n, Allocator.TempJob);

            try
            {
                return SolveInternal(ref A, b, x, r, rtilde, p, v, s, t, phat, shat, temp, maxIterations, tolerance);
            }
            finally
            {
                r.Dispose();
                rtilde.Dispose();
                p.Dispose();
                v.Dispose();
                s.Dispose();
                t.Dispose();
                phat.Dispose();
                shat.Dispose();
                temp.Dispose();
            }
        }

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

            double bnorm = BurstLinearAlgebra.Norm(b);
            if (bnorm < 1e-30)
            {
                bnorm = 1.0; // Avoid division by zero
            }

            double rnorm = BurstLinearAlgebra.Norm(r);
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
                double rho_new = BurstLinearAlgebra.Dot(rtilde, r);

                // Check for breakdown
                if (math.abs(rho_new) < 1e-30)
                {
                    return new BiCGStabResult
                    {
                        Converged = false,
                        Iterations = iter,
                        FinalResidual = rnorm / bnorm,
                        FailureReason = "rho breakdown"
                    };
                }

                // beta = (rho_new / rho) * (alpha / omega)
                double beta = (rho_new / rho) * (alpha / omega);

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
                double rtilde_v = BurstLinearAlgebra.Dot(rtilde, v);
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

                // s = r - alpha * v
                BurstLinearAlgebra.Copy(r, s);
                BurstLinearAlgebra.AXPY(-alpha, v, s);

                // Check for early convergence
                double snorm = BurstLinearAlgebra.Norm(s);
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
                double t_s = BurstLinearAlgebra.Dot(t, s);
                double t_t = BurstLinearAlgebra.Dot(t, t);
                if (math.abs(t_t) < 1e-30)
                {
                    return new BiCGStabResult
                    {
                        Converged = false,
                        Iterations = iter,
                        FinalResidual = rnorm / bnorm,
                        FailureReason = "omega breakdown (t_t)"
                    };
                }
                omega = t_s / t_t;

                if (math.abs(omega) < 1e-30)
                {
                    return new BiCGStabResult
                    {
                        Converged = false,
                        Iterations = iter,
                        FinalResidual = rnorm / bnorm,
                        FailureReason = "omega breakdown"
                    };
                }

                // x = x + alpha * phat + omega * shat
                BurstLinearAlgebra.AXPY(alpha, phat, x);
                BurstLinearAlgebra.AXPY(omega, shat, x);

                // r = s - omega * t
                BurstLinearAlgebra.Copy(s, r);
                BurstLinearAlgebra.AXPY(-omega, t, r);

                // Check convergence
                rnorm = BurstLinearAlgebra.Norm(r);
                if (rnorm / bnorm < tolerance)
                {
                    return new BiCGStabResult
                    {
                        Converged = true,
                        Iterations = iter + 1,
                        FinalResidual = rnorm / bnorm
                    };
                }

                rho = rho_new;
            }

            // Did not converge within max iterations
            return new BiCGStabResult
            {
                Converged = false,
                Iterations = maxIterations,
                FinalResidual = rnorm / bnorm,
                FailureReason = "max iterations reached"
            };
        }
    }
}
