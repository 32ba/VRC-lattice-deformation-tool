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
            ValidateDimensions(rows, cols);
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));

            // Work on a copy so matrix construction does not reorder the caller's data.
            var sortedEntries = new List<(int row, int col, double val)>(entries);
            ValidateEntries(rows, cols, sortedEntries);

            if (sortedEntries.Count == 0)
            {
                NativeArray<double> values = default;
                NativeArray<int> colIndices = default;
                NativeArray<int> rowPointers = default;
                NativeArray<double> diagonal = default;
                try
                {
                    values = new NativeArray<double>(0, allocator);
                    colIndices = new NativeArray<int>(0, allocator);
                    rowPointers = new NativeArray<int>(rows + 1, allocator);
                    diagonal = new NativeArray<double>(math.min(rows, cols), allocator);

                    var result = new NativeSparseMatrixCSR
                    {
                        RowCount = rows,
                        ColCount = cols,
                        NonZeroCount = 0,
                        Values = values,
                        ColIndices = colIndices,
                        RowPointers = rowPointers,
                        Diagonal = diagonal
                    };
                    values = default;
                    colIndices = default;
                    rowPointers = default;
                    diagonal = default;
                    return result;
                }
                catch
                {
                    if (values.IsCreated) values.Dispose();
                    if (colIndices.IsCreated) colIndices.Dispose();
                    if (rowPointers.IsCreated) rowPointers.Dispose();
                    if (diagonal.IsCreated) diagonal.Dispose();
                    throw;
                }
            }

            // Sort entries by (row, col)
            sortedEntries.Sort((a, b) =>
            {
                int cmp = a.row.CompareTo(b.row);
                return cmp != 0 ? cmp : a.col.CompareTo(b.col);
            });

            // Remove duplicates by summing values (common in Laplacian construction)
            var uniqueEntries = new List<(int row, int col, double val)>(sortedEntries.Count);
            var prev = sortedEntries[0];

            for (int i = 1; i < sortedEntries.Count; i++)
            {
                var curr = sortedEntries[i];
                if (curr.row == prev.row && curr.col == prev.col)
                {
                    prev.val += curr.val;
                    if (double.IsNaN(prev.val) || double.IsInfinity(prev.val))
                        throw new ArgumentException("Duplicate CSR entries sum to a non-finite value.", nameof(entries));
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

            NativeArray<double> resultValues = default;
            NativeArray<int> resultColIndices = default;
            NativeArray<int> resultRowPointers = default;
            NativeArray<double> resultDiagonal = default;
            try
            {
                resultValues = new NativeArray<double>(nnz, allocator);
                resultColIndices = new NativeArray<int>(nnz, allocator);
                resultRowPointers = new NativeArray<int>(rows + 1, allocator);
                resultDiagonal = new NativeArray<double>(math.min(rows, cols), allocator);

                var result = new NativeSparseMatrixCSR
                {
                    RowCount = rows,
                    ColCount = cols,
                    NonZeroCount = nnz,
                    Values = resultValues,
                    ColIndices = resultColIndices,
                    RowPointers = resultRowPointers,
                    Diagonal = resultDiagonal
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

                resultValues = default;
                resultColIndices = default;
                resultRowPointers = default;
                resultDiagonal = default;
                return result;
            }
            catch
            {
                if (resultValues.IsCreated) resultValues.Dispose();
                if (resultColIndices.IsCreated) resultColIndices.Dispose();
                if (resultRowPointers.IsCreated) resultRowPointers.Dispose();
                if (resultDiagonal.IsCreated) resultDiagonal.Dispose();
                throw;
            }
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
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));

            var entryList = new List<(int, int, double)>(entries);
            return FromCOO(rows, cols, entryList, allocator);
        }

        internal void ValidateContents()
        {
            ValidateDimensions(RowCount, ColCount);
            if (!Values.IsCreated || !ColIndices.IsCreated ||
                !RowPointers.IsCreated || !Diagonal.IsCreated)
            {
                throw new ArgumentException("Matrix storage has not been created.", nameof(Values));
            }
            if (NonZeroCount < 0 || Values.Length != NonZeroCount ||
                ColIndices.Length != NonZeroCount || RowPointers.Length != RowCount + 1 ||
                Diagonal.Length < math.min(RowCount, ColCount))
            {
                throw new ArgumentException("Matrix storage dimensions are inconsistent.", nameof(Values));
            }

            if (RowPointers[0] != 0)
                throw new ArgumentException("CSR row pointers must start at zero.", nameof(RowPointers));

            int previous = 0;
            for (int i = 0; i < RowPointers.Length; i++)
            {
                int pointer = RowPointers[i];
                if (pointer < previous || pointer < 0 || pointer > NonZeroCount)
                    throw new ArgumentException("CSR row pointers must be monotonic and within the value range.", nameof(RowPointers));
                previous = pointer;
            }

            if (RowPointers[RowCount] != NonZeroCount)
                throw new ArgumentException("CSR row pointers must end at NonZeroCount.", nameof(RowPointers));

            for (int i = 0; i < NonZeroCount; i++)
            {
                if (ColIndices[i] < 0 || ColIndices[i] >= ColCount)
                    throw new ArgumentOutOfRangeException(nameof(ColIndices), ColIndices[i], "CSR column index is outside the matrix dimensions.");
                if (double.IsNaN(Values[i]) || double.IsInfinity(Values[i]))
                    throw new ArgumentException("CSR values must be finite.", nameof(Values));
            }

            for (int i = 0; i < Diagonal.Length; i++)
            {
                if (double.IsNaN(Diagonal[i]) || double.IsInfinity(Diagonal[i]))
                    throw new ArgumentException("CSR diagonal values must be finite.", nameof(Diagonal));
            }
        }

        private static void ValidateDimensions(int rows, int cols)
        {
            if (rows < 0)
                throw new ArgumentOutOfRangeException(nameof(rows), rows, "Row count cannot be negative.");
            if (cols < 0)
                throw new ArgumentOutOfRangeException(nameof(cols), cols, "Column count cannot be negative.");
        }

        private static void ValidateEntries(
            int rows,
            int cols,
            List<(int row, int col, double val)> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry.row < 0 || entry.row >= rows)
                    throw new ArgumentOutOfRangeException(nameof(entries), $"Entry {i} has row {entry.row}, outside [0, {rows}).");
                if (entry.col < 0 || entry.col >= cols)
                    throw new ArgumentOutOfRangeException(nameof(entries), $"Entry {i} has column {entry.col}, outside [0, {cols}).");
                if (double.IsNaN(entry.val) || double.IsInfinity(entry.val))
                    throw new ArgumentException($"Entry {i} has a non-finite value.", nameof(entries));
            }
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
