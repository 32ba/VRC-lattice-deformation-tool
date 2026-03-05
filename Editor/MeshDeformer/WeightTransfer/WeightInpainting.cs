using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Net._32Ba.LatticeDeformationTool.Editor.WeightTransfer.BurstSolver;

namespace Net._32Ba.LatticeDeformationTool.Editor.WeightTransfer
{
    /// <summary>
    /// Implements Laplacian-based weight inpainting for vertices that couldn't be transferred.
    /// Uses the biharmonic energy minimization approach from "Robust Skin Weights Transfer via Weight Inpainting".
    /// Optimized with Burst-compiled sparse matrix solver.
    /// </summary>
    public class WeightInpainting
    {
        private readonly Vector3[] _vertices;
        private readonly int[] _triangles;
        private readonly int _maxIterations;
        private readonly double _tolerance;
        private readonly int _vertexCount;

        // Adjacency data
        private List<int>[] _adjacency;

        // Sparse Laplacian data (COO format for construction)
        private Dictionary<(int, int), double> _laplacianEntries;

        /// <summary>
        /// Creates a new weight inpainting solver.
        /// </summary>
        public WeightInpainting(Vector3[] vertices, int[] triangles, int maxIterations, float tolerance)
        {
            _vertices = vertices;
            _triangles = triangles;
            _maxIterations = maxIterations;
            _tolerance = tolerance;
            _vertexCount = vertices.Length;

            BuildAdjacency();
            BuildCotangentLaplacianSparse();
        }

        /// <summary>
        /// Build vertex adjacency list from triangles.
        /// </summary>
        private void BuildAdjacency()
        {
            _adjacency = new List<int>[_vertexCount];
            for (int i = 0; i < _vertexCount; i++)
            {
                _adjacency[i] = new List<int>();
            }

            int triangleCount = _triangles.Length / 3;
            for (int t = 0; t < triangleCount; t++)
            {
                int i0 = _triangles[t * 3];
                int i1 = _triangles[t * 3 + 1];
                int i2 = _triangles[t * 3 + 2];

                AddEdge(i0, i1);
                AddEdge(i1, i2);
                AddEdge(i2, i0);
            }
        }

        private void AddEdge(int a, int b)
        {
            if (!_adjacency[a].Contains(b))
                _adjacency[a].Add(b);
            if (!_adjacency[b].Contains(a))
                _adjacency[b].Add(a);
        }

        /// <summary>
        /// Build cotangent Laplacian matrix in sparse format.
        /// L_ij = (cot(alpha_ij) + cot(beta_ij)) / 2 for adjacent vertices
        /// L_ii = -sum(L_ij)
        /// </summary>
        private void BuildCotangentLaplacianSparse()
        {
            // Edge to opposite vertices mapping for cotangent weights
            var edgeToOppositeVertices = new Dictionary<(int, int), List<int>>();

            int triangleCount = _triangles.Length / 3;
            for (int t = 0; t < triangleCount; t++)
            {
                int i0 = _triangles[t * 3];
                int i1 = _triangles[t * 3 + 1];
                int i2 = _triangles[t * 3 + 2];

                // Edge (i0, i1) opposite to i2
                AddOppositeVertex(edgeToOppositeVertices, i0, i1, i2);
                // Edge (i1, i2) opposite to i0
                AddOppositeVertex(edgeToOppositeVertices, i1, i2, i0);
                // Edge (i2, i0) opposite to i1
                AddOppositeVertex(edgeToOppositeVertices, i2, i0, i1);
            }

            // Compute cotangent weights and store in sparse format
            _laplacianEntries = new Dictionary<(int, int), double>();
            var diagSum = new double[_vertexCount];

            foreach (var kvp in edgeToOppositeVertices)
            {
                int i = kvp.Key.Item1;
                int j = kvp.Key.Item2;
                var oppositeVertices = kvp.Value;

                double weight = 0;
                foreach (int k in oppositeVertices)
                {
                    weight += ComputeCotangent(i, j, k);
                }
                weight *= 0.5;

                // Clamp to avoid numerical issues
                weight = Math.Max(weight, 1e-6);

                // Store off-diagonal entries (negative of weight)
                _laplacianEntries[(i, j)] = -weight;
                _laplacianEntries[(j, i)] = -weight;

                // Accumulate diagonal
                diagSum[i] += weight;
                diagSum[j] += weight;
            }

            // Store diagonal entries
            for (int i = 0; i < _vertexCount; i++)
            {
                _laplacianEntries[(i, i)] = diagSum[i];
            }
        }

        private void AddOppositeVertex(
            Dictionary<(int, int), List<int>> dict,
            int edgeV1, int edgeV2, int oppositeV)
        {
            // Ensure consistent edge ordering
            var key = edgeV1 < edgeV2 ? (edgeV1, edgeV2) : (edgeV2, edgeV1);

            if (!dict.TryGetValue(key, out var list))
            {
                list = new List<int>();
                dict[key] = list;
            }
            list.Add(oppositeV);
        }

        private double ComputeCotangent(int edgeV1, int edgeV2, int oppositeV)
        {
            Vector3 p1 = _vertices[edgeV1];
            Vector3 p2 = _vertices[edgeV2];
            Vector3 po = _vertices[oppositeV];

            // Vectors from opposite vertex to edge vertices
            Vector3 e1 = p1 - po;
            Vector3 e2 = p2 - po;

            // cot(angle) = cos(angle) / sin(angle) = dot(e1, e2) / |cross(e1, e2)|
            float dot = Vector3.Dot(e1, e2);
            float crossMag = Vector3.Cross(e1, e2).magnitude;

            if (crossMag < 1e-10f)
            {
                return 0; // Degenerate triangle
            }

            return dot / crossMag;
        }

        /// <summary>
        /// Performs weight inpainting using Laplacian interpolation with Burst-compiled sparse solver.
        /// </summary>
        public void Inpaint(BoneWeight[] weights, float[] confidence, int boneCount)
        {
            // Find known (transferred) and unknown (to inpaint) vertices
            var knownIndices = new List<int>();
            var unknownIndices = new List<int>();

            for (int i = 0; i < _vertexCount; i++)
            {
                if (confidence[i] >= 0.5f)
                    knownIndices.Add(i);
                else
                    unknownIndices.Add(i);
            }

            if (unknownIndices.Count == 0)
                return; // Nothing to inpaint

            if (knownIndices.Count == 0)
            {
                Debug.LogWarning("WeightInpainting: No known vertices to interpolate from.");
                return;
            }

            // Collect all unique bone indices used
            var usedBones = new HashSet<int>();
            foreach (int idx in knownIndices)
            {
                var w = weights[idx];
                if (w.weight0 > 0) usedBones.Add(w.boneIndex0);
                if (w.weight1 > 0) usedBones.Add(w.boneIndex1);
                if (w.weight2 > 0) usedBones.Add(w.boneIndex2);
                if (w.weight3 > 0) usedBones.Add(w.boneIndex3);
            }

            if (usedBones.Count == 0)
            {
                Debug.LogWarning("WeightInpainting: No bone weights found in known vertices.");
                return;
            }

            var boneList = new List<int>(usedBones);
            boneList.Sort();

            // Create O(1) lookup maps
            var unknownIndexMap = new Dictionary<int, int>(unknownIndices.Count);
            for (int i = 0; i < unknownIndices.Count; i++)
            {
                unknownIndexMap[unknownIndices[i]] = i;
            }

            var knownIndexMap = new Dictionary<int, int>(knownIndices.Count);
            for (int i = 0; i < knownIndices.Count; i++)
            {
                knownIndexMap[knownIndices[i]] = i;
            }

            // Build sparse submatrices: L_uu (unknown-unknown) and L_uk (unknown-known)
            int nu = unknownIndices.Count;
            int nk = knownIndices.Count;

            // Collect entries for sparse matrix construction
            var L_uu_entries = new List<(int, int, double)>();
            var L_uk_entries = new List<(int, int, double)>();
            var diagSums = new double[nu];

            for (int ui = 0; ui < nu; ui++)
            {
                int i = unknownIndices[ui];

                foreach (int j in _adjacency[i])
                {
                    // Get Laplacian weight (off-diagonal is negative in our storage)
                    if (!_laplacianEntries.TryGetValue((i, j), out double negW))
                        continue;
                    double w = -negW; // Convert back to positive weight

                    if (unknownIndexMap.TryGetValue(j, out int uj))
                    {
                        // j is unknown - add to L_uu
                        L_uu_entries.Add((ui, uj, -w));
                        diagSums[ui] += w;
                    }
                    else if (knownIndexMap.TryGetValue(j, out int kj))
                    {
                        // j is known - add to L_uk
                        L_uk_entries.Add((ui, kj, -w));
                        diagSums[ui] += w;
                    }
                }
            }

            // Add diagonal entries with regularization
            for (int ui = 0; ui < nu; ui++)
            {
                L_uu_entries.Add((ui, ui, diagSums[ui] + 1e-8));
            }

            // Build sparse matrices using Burst-compatible format
            var L_uu = NativeSparseMatrixCSR.FromIndexed(nu, nu, L_uu_entries, Allocator.TempJob);
            var L_uk = NativeSparseMatrixCSR.FromIndexed(nu, nk, L_uk_entries, Allocator.TempJob);

            // Allocate native arrays for solving
            var knownWeightsArray = new NativeArray<double>(nk, Allocator.TempJob);
            var rhsArray = new NativeArray<double>(nu, Allocator.TempJob);
            var solutionArray = new NativeArray<double>(nu, Allocator.TempJob);
            var tempArray = new NativeArray<double>(nu, Allocator.TempJob);

            try
            {
                // Solve for each bone
                var boneWeightResults = new double[nu, boneList.Count];

                for (int boneIdx = 0; boneIdx < boneList.Count; boneIdx++)
                {
                    int bone = boneList[boneIdx];

                    // Get known weights for this bone
                    for (int ki = 0; ki < nk; ki++)
                    {
                        knownWeightsArray[ki] = GetBoneWeight(weights[knownIndices[ki]], bone);
                    }

                    // RHS: -L_uk * knownWeights
                    BurstLinearAlgebra.SpMV(ref L_uk, knownWeightsArray, rhsArray);
                    for (int i = 0; i < nu; i++)
                    {
                        rhsArray[i] = -rhsArray[i];
                    }

                    // Skip if RHS is essentially zero (bone not used in known vertices)
                    double rhsNorm = BurstLinearAlgebra.Norm(rhsArray);
                    if (rhsNorm < 1e-10)
                    {
                        // No contribution from this bone - leave as zero
                        continue;
                    }

                    // Initialize solution to zero
                    BurstLinearAlgebra.Zero(solutionArray);

                    // Solve L_uu * x = rhs using Burst BiCGStab
                    var result = BurstBiCGStab.Solve(ref L_uu, rhsArray, solutionArray, _maxIterations, _tolerance);

                    if (result.Converged)
                    {
                        for (int ui = 0; ui < nu; ui++)
                        {
                            boneWeightResults[ui, boneIdx] = Math.Max(0, solutionArray[ui]);
                        }
                    }
                    else
                    {
                        // Fallback: use simple neighbor averaging for this bone
                        FallbackNeighborAveraging(unknownIndices, knownIndices, weights, bone, boneWeightResults, boneIdx);
                    }
                }

                // Convert results back to BoneWeight format
                ConvertToBoneWeights(unknownIndices, boneList, boneWeightResults, weights);
            }
            finally
            {
                // Dispose all native arrays
                L_uu.Dispose();
                L_uk.Dispose();
                knownWeightsArray.Dispose();
                rhsArray.Dispose();
                solutionArray.Dispose();
                tempArray.Dispose();
            }
        }

        private void ConvertToBoneWeights(
            List<int> unknownIndices,
            List<int> boneList,
            double[,] boneWeightResults,
            BoneWeight[] weights)
        {
            int nu = unknownIndices.Count;

            // Preallocate to avoid per-vertex allocations
            var boneWeightPairs = new List<(int bone, double weight)>(boneList.Count);

            for (int ui = 0; ui < nu; ui++)
            {
                int vertIdx = unknownIndices[ui];

                // Collect weights for this vertex
                boneWeightPairs.Clear();
                for (int bi = 0; bi < boneList.Count; bi++)
                {
                    double w = boneWeightResults[ui, bi];
                    if (w > 1e-6)
                    {
                        boneWeightPairs.Add((boneList[bi], w));
                    }
                }

                // Sort by weight descending
                boneWeightPairs.Sort((a, b) => b.weight.CompareTo(a.weight));

                // Take top 4 and normalize
                var bw = new BoneWeight();
                double totalWeight = 0;

                int count = Math.Min(4, boneWeightPairs.Count);
                for (int i = 0; i < count; i++)
                {
                    totalWeight += boneWeightPairs[i].weight;
                }

                if (totalWeight > 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        float normalizedWeight = (float)(boneWeightPairs[i].weight / totalWeight);
                        switch (i)
                        {
                            case 0:
                                bw.boneIndex0 = boneWeightPairs[i].bone;
                                bw.weight0 = normalizedWeight;
                                break;
                            case 1:
                                bw.boneIndex1 = boneWeightPairs[i].bone;
                                bw.weight1 = normalizedWeight;
                                break;
                            case 2:
                                bw.boneIndex2 = boneWeightPairs[i].bone;
                                bw.weight2 = normalizedWeight;
                                break;
                            case 3:
                                bw.boneIndex3 = boneWeightPairs[i].bone;
                                bw.weight3 = normalizedWeight;
                                break;
                        }
                    }
                }

                weights[vertIdx] = bw;
            }
        }

        private double GetBoneWeight(BoneWeight bw, int boneIndex)
        {
            if (bw.boneIndex0 == boneIndex) return bw.weight0;
            if (bw.boneIndex1 == boneIndex) return bw.weight1;
            if (bw.boneIndex2 == boneIndex) return bw.weight2;
            if (bw.boneIndex3 == boneIndex) return bw.weight3;
            return 0;
        }

        private void FallbackNeighborAveraging(
            List<int> unknownIndices,
            List<int> knownIndices,
            BoneWeight[] weights,
            int bone,
            double[,] boneWeightResults,
            int boneIdx)
        {
            var knownSet = new HashSet<int>(knownIndices);

            for (int ui = 0; ui < unknownIndices.Count; ui++)
            {
                int vertIdx = unknownIndices[ui];
                double sum = 0;
                int count = 0;

                // Average weights from known neighbors
                foreach (int neighbor in _adjacency[vertIdx])
                {
                    if (knownSet.Contains(neighbor))
                    {
                        sum += GetBoneWeight(weights[neighbor], bone);
                        count++;
                    }
                }

                boneWeightResults[ui, boneIdx] = count > 0 ? sum / count : 0;
            }
        }
    }
}
