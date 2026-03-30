using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace Net._32Ba.LatticeDeformationTool.Editor.WeightTransfer.BurstSolver
{
    /// <summary>
    /// Compressed Sparse Row (CSR) matrix format for Burst-compatible sparse matrix operations.
    /// </summary>
    public struct NativeSparseMatrixCSR : IDisposable
    {
        public int RowCount;
        public int ColCount;
        public int NonZeroCount;

        /// <summary>Non-zero values (length: NonZeroCount)</summary>
        public NativeArray<double> Values;

        /// <summary>Column index for each value (length: NonZeroCount)</summary>
        public NativeArray<int> ColIndices;

        /// <summary>Start index in Values for each row (length: RowCount + 1)</summary>
        public NativeArray<int> RowPointers;

        /// <summary>Diagonal values for preconditioning (length: min(RowCount, ColCount))</summary>
        public NativeArray<double> Diagonal;

        public bool IsCreated => Values.IsCreated;

        /// <summary>
        /// Creates a CSR matrix from COO (Coordinate) format entries.
        /// </summary>
        public static NativeSparseMatrixCSR FromCOO(
            int rows,
            int cols,
            List<(int row, int col, double val)> entries,
            Allocator allocator)
        {
            if (entries.Count == 0)
            {
                return new NativeSparseMatrixCSR
                {
                    RowCount = rows,
                    ColCount = cols,
                    NonZeroCount = 0,
                    Values = new NativeArray<double>(0, allocator),
                    ColIndices = new NativeArray<int>(0, allocator),
                    RowPointers = new NativeArray<int>(rows + 1, allocator),
                    Diagonal = new NativeArray<double>(math.min(rows, cols), allocator)
                };
            }

            // Sort entries by (row, col)
            entries.Sort((a, b) =>
            {
                int cmp = a.row.CompareTo(b.row);
                return cmp != 0 ? cmp : a.col.CompareTo(b.col);
            });

            // Remove duplicates by summing values (common in Laplacian construction)
            var uniqueEntries = new List<(int row, int col, double val)>(entries.Count);
            var prev = entries[0];

            for (int i = 1; i < entries.Count; i++)
            {
                var curr = entries[i];
                if (curr.row == prev.row && curr.col == prev.col)
                {
                    prev.val += curr.val;
                }
                else
                {
                    if (math.abs(prev.val) > 1e-15)
                        uniqueEntries.Add(prev);
                    prev = curr;
                }
            }
            if (math.abs(prev.val) > 1e-15)
                uniqueEntries.Add(prev);

            int nnz = uniqueEntries.Count;

            var result = new NativeSparseMatrixCSR
            {
                RowCount = rows,
                ColCount = cols,
                NonZeroCount = nnz,
                Values = new NativeArray<double>(nnz, allocator),
                ColIndices = new NativeArray<int>(nnz, allocator),
                RowPointers = new NativeArray<int>(rows + 1, allocator),
                Diagonal = new NativeArray<double>(math.min(rows, cols), allocator)
            };

            // Initialize row pointers
            for (int i = 0; i <= rows; i++)
                result.RowPointers[i] = 0;

            // Fill values and column indices, count entries per row
            int currentRow = -1;
            for (int i = 0; i < nnz; i++)
            {
                var (row, col, val) = uniqueEntries[i];

                result.Values[i] = val;
                result.ColIndices[i] = col;

                // Update row pointers
                while (currentRow < row)
                {
                    currentRow++;
                    result.RowPointers[currentRow] = i;
                }

                // Extract diagonal
                if (row == col && row < result.Diagonal.Length)
                {
                    result.Diagonal[row] = val;
                }
            }

            // Fill remaining row pointers
            while (currentRow < rows)
            {
                currentRow++;
                result.RowPointers[currentRow] = nnz;
            }

            return result;
        }

        /// <summary>
        /// Creates a CSR matrix from indexed entries (same as current L_uu_entries format).
        /// </summary>
        public static NativeSparseMatrixCSR FromIndexed(
            int rows,
            int cols,
            IEnumerable<(int row, int col, double val)> entries,
            Allocator allocator)
        {
            var entryList = new List<(int, int, double)>(entries);
            return FromCOO(rows, cols, entryList, allocator);
        }

        public void Dispose()
        {
            if (Values.IsCreated) Values.Dispose();
            if (ColIndices.IsCreated) ColIndices.Dispose();
            if (RowPointers.IsCreated) RowPointers.Dispose();
            if (Diagonal.IsCreated) Diagonal.Dispose();
        }
    }
}
