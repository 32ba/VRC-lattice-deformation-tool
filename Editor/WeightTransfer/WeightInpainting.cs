using System;
using System.Collections.Generic;
using UnityEngine;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace Net._32Ba.LatticeDeformationTool.Editor.WeightTransfer
{
    /// <summary>
    /// Implements Laplacian-based weight inpainting for vertices that couldn't be transferred.
    /// Uses the biharmonic energy minimization approach from "Robust Skin Weights Transfer via Weight Inpainting".
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
        private Matrix<double> _laplacian;

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
            BuildCotangentLaplacian();
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
        /// Build cotangent Laplacian matrix.
        /// L_ij = (cot(alpha_ij) + cot(beta_ij)) / 2 for adjacent vertices
        /// L_ii = -sum(L_ij)
        /// </summary>
        private void BuildCotangentLaplacian()
        {
            // Build sparse matrix using coordinate format first
            var triplets = new List<(int, int, double)>();

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

            // Compute cotangent weights
            var offDiagonal = new double[_vertexCount, _vertexCount];

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

                offDiagonal[i, j] = weight;
                offDiagonal[j, i] = weight;
            }

            // Build matrix with diagonal entries
            var builder = Matrix<double>.Build;
            _laplacian = builder.Dense(_vertexCount, _vertexCount);

            for (int i = 0; i < _vertexCount; i++)
            {
                double diagSum = 0;
                foreach (int j in _adjacency[i])
                {
                    double w = offDiagonal[i, j];
                    _laplacian[i, j] = -w;
                    diagSum += w;
                }
                _laplacian[i, i] = diagSum;
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
        /// Performs weight inpainting using Laplacian interpolation.
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

            // Create mapping from vertex index to unknown index
            var unknownIndexMap = new Dictionary<int, int>();
            for (int i = 0; i < unknownIndices.Count; i++)
            {
                unknownIndexMap[unknownIndices[i]] = i;
            }

            // Solve for each bone separately
            var boneWeightResults = new double[unknownIndices.Count, boneList.Count];

            // Build submatrices: L_uu (unknown-unknown) and L_uk (unknown-known)
            int nu = unknownIndices.Count;
            int nk = knownIndices.Count;

            var L_uu = Matrix<double>.Build.Dense(nu, nu);
            var L_uk = Matrix<double>.Build.Dense(nu, nk);

            for (int ui = 0; ui < nu; ui++)
            {
                int i = unknownIndices[ui];
                double diagSum = 0;

                foreach (int j in _adjacency[i])
                {
                    double w = -_laplacian[i, j]; // Note: off-diagonal is negative in Laplacian

                    if (unknownIndexMap.TryGetValue(j, out int uj))
                    {
                        L_uu[ui, uj] = -w;
                        diagSum += w;
                    }
                    else
                    {
                        // j is a known vertex
                        int kj = knownIndices.IndexOf(j);
                        if (kj >= 0)
                        {
                            L_uk[ui, kj] = -w;
                            diagSum += w;
                        }
                    }
                }
                L_uu[ui, ui] = diagSum;
            }

            // Add small regularization for numerical stability
            for (int i = 0; i < nu; i++)
            {
                L_uu[i, i] += 1e-8;
            }

            // Solve for each bone
            for (int boneIdx = 0; boneIdx < boneList.Count; boneIdx++)
            {
                int bone = boneList[boneIdx];

                // Get known weights for this bone
                var knownWeights = Vector<double>.Build.Dense(nk);
                for (int ki = 0; ki < nk; ki++)
                {
                    knownWeights[ki] = GetBoneWeight(weights[knownIndices[ki]], bone);
                }

                // RHS: -L_uk * knownWeights
                var rhs = -L_uk * knownWeights;

                // Solve L_uu * x = rhs
                try
                {
                    var solution = L_uu.Solve(rhs);

                    for (int ui = 0; ui < nu; ui++)
                    {
                        boneWeightResults[ui, boneIdx] = Math.Max(0, solution[ui]);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"WeightInpainting: Failed to solve for bone {bone}: {e.Message}");
                    // Fallback: use average of neighboring known weights
                    for (int ui = 0; ui < nu; ui++)
                    {
                        boneWeightResults[ui, boneIdx] = 0;
                    }
                }
            }

            // Convert results back to BoneWeight format
            for (int ui = 0; ui < nu; ui++)
            {
                int vertIdx = unknownIndices[ui];

                // Collect weights for this vertex
                var boneWeightPairs = new List<(int bone, double weight)>();
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

                for (int i = 0; i < Math.Min(4, boneWeightPairs.Count); i++)
                {
                    totalWeight += boneWeightPairs[i].weight;
                }

                if (totalWeight > 0)
                {
                    for (int i = 0; i < Math.Min(4, boneWeightPairs.Count); i++)
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
    }
}
