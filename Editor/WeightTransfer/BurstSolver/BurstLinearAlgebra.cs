using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Net._32Ba.LatticeDeformationTool.Editor.WeightTransfer.BurstSolver
{
    /// <summary>
    /// Burst-compiled linear algebra operations for sparse matrix solvers.
    /// </summary>
    public static class BurstLinearAlgebra
    {
        /// <summary>
        /// Computes y = A * x (sparse matrix-vector multiplication).
        /// </summary>
        public static void SpMV(
            ref NativeSparseMatrixCSR A,
            NativeArray<double> x,
            NativeArray<double> y)
        {
            var job = new SpMVJob
            {
                values = A.Values,
                colIndices = A.ColIndices,
                rowPointers = A.RowPointers,
                x = x,
                y = y
            };
            job.Schedule(A.RowCount, 64).Complete();
        }

        /// <summary>
        /// Computes dot product: result = sum(a[i] * b[i])
        /// </summary>
        public static double Dot(NativeArray<double> a, NativeArray<double> b)
        {
            var result = new NativeArray<double>(1, Allocator.TempJob);
            var job = new DotProductJob
            {
                a = a,
                b = b,
                result = result
            };
            job.Schedule().Complete();
            double value = result[0];
            result.Dispose();
            return value;
        }

        /// <summary>
        /// Computes L2 norm: result = sqrt(sum(a[i]^2))
        /// </summary>
        public static double Norm(NativeArray<double> a)
        {
            var result = new NativeArray<double>(1, Allocator.TempJob);
            var job = new NormJob
            {
                a = a,
                result = result
            };
            job.Schedule().Complete();
            double value = result[0];
            result.Dispose();
            return value;
        }

        /// <summary>
        /// Computes y = alpha * x + y (AXPY operation)
        /// </summary>
        public static void AXPY(double alpha, NativeArray<double> x, NativeArray<double> y)
        {
            var job = new AXPYJob
            {
                alpha = alpha,
                x = x,
                y = y
            };
            job.Schedule(x.Length, 256).Complete();
        }

        /// <summary>
        /// Computes y = x (copy)
        /// </summary>
        public static void Copy(NativeArray<double> x, NativeArray<double> y)
        {
            var job = new CopyJob
            {
                x = x,
                y = y
            };
            job.Schedule(x.Length, 256).Complete();
        }

        /// <summary>
        /// Computes y = alpha * x
        /// </summary>
        public static void Scale(double alpha, NativeArray<double> x, NativeArray<double> y)
        {
            var job = new ScaleJob
            {
                alpha = alpha,
                x = x,
                y = y
            };
            job.Schedule(x.Length, 256).Complete();
        }

        /// <summary>
        /// Sets all elements to zero
        /// </summary>
        public static void Zero(NativeArray<double> x)
        {
            var job = new ZeroJob { x = x };
            job.Schedule(x.Length, 256).Complete();
        }

        /// <summary>
        /// Applies diagonal preconditioner: y[i] = x[i] / diag[i]
        /// </summary>
        public static void ApplyDiagonalPreconditioner(
            NativeArray<double> diag,
            NativeArray<double> x,
            NativeArray<double> y)
        {
            var job = new DiagonalPreconditionerJob
            {
                diag = diag,
                x = x,
                y = y
            };
            job.Schedule(x.Length, 256).Complete();
        }

        #region Burst Jobs

        [BurstCompile]
        private struct SpMVJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<double> values;
            [ReadOnly] public NativeArray<int> colIndices;
            [ReadOnly] public NativeArray<int> rowPointers;
            [ReadOnly] public NativeArray<double> x;
            [WriteOnly] public NativeArray<double> y;

            public void Execute(int row)
            {
                double sum = 0;
                int start = rowPointers[row];
                int end = rowPointers[row + 1];

                for (int k = start; k < end; k++)
                {
                    sum += values[k] * x[colIndices[k]];
                }

                y[row] = sum;
            }
        }

        [BurstCompile]
        private struct DotProductJob : IJob
        {
            [ReadOnly] public NativeArray<double> a;
            [ReadOnly] public NativeArray<double> b;
            public NativeArray<double> result;

            public void Execute()
            {
                double sum = 0;
                for (int i = 0; i < a.Length; i++)
                {
                    sum += a[i] * b[i];
                }
                result[0] = sum;
            }
        }

        [BurstCompile]
        private struct NormJob : IJob
        {
            [ReadOnly] public NativeArray<double> a;
            public NativeArray<double> result;

            public void Execute()
            {
                double sum = 0;
                for (int i = 0; i < a.Length; i++)
                {
                    sum += a[i] * a[i];
                }
                result[0] = math.sqrt(sum);
            }
        }

        [BurstCompile]
        private struct AXPYJob : IJobParallelFor
        {
            public double alpha;
            [ReadOnly] public NativeArray<double> x;
            public NativeArray<double> y;

            public void Execute(int i)
            {
                y[i] = alpha * x[i] + y[i];
            }
        }

        [BurstCompile]
        private struct CopyJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<double> x;
            [WriteOnly] public NativeArray<double> y;

            public void Execute(int i)
            {
                y[i] = x[i];
            }
        }

        [BurstCompile]
        private struct ScaleJob : IJobParallelFor
        {
            public double alpha;
            [ReadOnly] public NativeArray<double> x;
            [WriteOnly] public NativeArray<double> y;

            public void Execute(int i)
            {
                y[i] = alpha * x[i];
            }
        }

        [BurstCompile]
        private struct ZeroJob : IJobParallelFor
        {
            [WriteOnly] public NativeArray<double> x;

            public void Execute(int i)
            {
                x[i] = 0;
            }
        }

        [BurstCompile]
        private struct DiagonalPreconditionerJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<double> diag;
            [ReadOnly] public NativeArray<double> x;
            [WriteOnly] public NativeArray<double> y;

            public void Execute(int i)
            {
                double d = diag[i];
                // Avoid division by zero
                y[i] = math.abs(d) > 1e-15 ? x[i] / d : x[i];
            }
        }

        #endregion
    }
}
