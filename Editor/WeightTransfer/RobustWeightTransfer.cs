using System;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Net._32Ba.LatticeDeformationTool.Editor.WeightTransfer
{
    /// <summary>
    /// Main API for Robust Weight Transfer.
    /// Implements "Robust Skin Weights Transfer via Weight Inpainting" (SIGGRAPH Asia 2023).
    /// </summary>
    public static class RobustWeightTransfer
    {
        /// <summary>
        /// Result of weight transfer operation.
        /// </summary>
        public struct TransferResult
        {
            /// <summary>
            /// The transferred bone weights for each vertex.
            /// </summary>
            public BoneWeight[] weights;

            /// <summary>
            /// Number of vertices successfully transferred in Stage 1.
            /// </summary>
            public int transferredCount;

            /// <summary>
            /// Number of vertices filled by inpainting in Stage 2.
            /// </summary>
            public int inpaintedCount;

            /// <summary>
            /// Total number of vertices processed.
            /// </summary>
            public int totalVertices;

            /// <summary>
            /// Whether the operation completed successfully.
            /// </summary>
            public bool success;

            /// <summary>
            /// Error message if the operation failed.
            /// </summary>
            public string errorMessage;
        }

        /// <summary>
        /// Transfers bone weights from source mesh to target mesh.
        /// </summary>
        /// <param name="sourceMesh">Source mesh (before deformation) with original weights.</param>
        /// <param name="sourceWeights">Bone weights of the source mesh.</param>
        /// <param name="targetMesh">Target mesh (after deformation).</param>
        /// <param name="settings">Transfer settings. Uses default if null.</param>
        /// <returns>Transfer result containing the new bone weights.</returns>
        public static TransferResult Transfer(
            Mesh sourceMesh,
            BoneWeight[] sourceWeights,
            Mesh targetMesh,
            WeightTransferSettings settings = null)
        {
            if (sourceMesh == null)
            {
                return new TransferResult
                {
                    success = false,
                    errorMessage = "Source mesh is null."
                };
            }

            if (sourceWeights == null || sourceWeights.Length == 0)
            {
                return new TransferResult
                {
                    success = false,
                    errorMessage = "Source weights are null or empty."
                };
            }

            if (targetMesh == null)
            {
                return new TransferResult
                {
                    success = false,
                    errorMessage = "Target mesh is null."
                };
            }

            settings = settings ?? WeightTransferSettings.Default;

            var targetVertices = targetMesh.vertices;
            var targetNormals = targetMesh.normals;
            var targetTriangles = targetMesh.triangles;
            int vertexCount = targetVertices.Length;

            var result = new TransferResult
            {
                weights = new BoneWeight[vertexCount],
                totalVertices = vertexCount,
                transferredCount = 0,
                inpaintedCount = 0,
                success = true
            };

            // Confidence mask: 1.0 = transferred, 0.0 = needs inpainting
            var confidenceMask = new float[vertexCount];

            var sw = Stopwatch.StartNew();

            // Stage 1: Initial transfer
            Stage1Transfer(
                sourceMesh,
                sourceWeights,
                targetVertices,
                targetNormals,
                settings,
                result.weights,
                confidenceMask,
                ref result.transferredCount);

            var stage1Time = sw.ElapsedMilliseconds;
            sw.Restart();

            // Stage 2: Weight inpainting (if enabled and needed)
            if (settings.enableInpainting && result.transferredCount < vertexCount)
            {
                Stage2Inpainting(
                    targetVertices,
                    targetTriangles,
                    sourceMesh.bindposes.Length, // Number of bones
                    result.weights,
                    confidenceMask,
                    settings,
                    ref result.inpaintedCount);
            }

            var stage2Time = sw.ElapsedMilliseconds;
            Debug.Log($"[WeightTransfer] Stage1: {stage1Time}ms, Stage2: {stage2Time}ms");

            if (!settings.enableInpainting || result.transferredCount >= vertexCount)
            {
                // Fill non-transferred vertices with zero weights
                for (int i = 0; i < vertexCount; i++)
                {
                    if (confidenceMask[i] < 0.5f)
                    {
                        result.weights[i] = new BoneWeight();
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Simplified transfer method that returns only the bone weights array.
        /// </summary>
        public static BoneWeight[] TransferWeights(
            Mesh sourceMesh,
            BoneWeight[] sourceWeights,
            Mesh targetMesh,
            WeightTransferSettings settings = null)
        {
            var result = Transfer(sourceMesh, sourceWeights, targetMesh, settings);
            if (!result.success)
            {
                Debug.LogError($"Weight transfer failed: {result.errorMessage}");
                return sourceWeights; // Fallback to original weights
            }
            return result.weights;
        }

        /// <summary>
        /// Stage 1: Initial weight transfer based on closest point on source mesh.
        /// Uses batch processing with Burst jobs for performance.
        /// </summary>
        private static void Stage1Transfer(
            Mesh sourceMesh,
            BoneWeight[] sourceWeights,
            Vector3[] targetVertices,
            Vector3[] targetNormals,
            WeightTransferSettings settings,
            BoneWeight[] outWeights,
            float[] outConfidence,
            ref int transferredCount)
        {
            var sourceVertices = sourceMesh.vertices;
            var sourceNormals = sourceMesh.normals;
            var sourceTriangles = sourceMesh.triangles;

            // Build spatial query structure for source mesh
            using (var spatialQuery = new MeshSpatialQuery(sourceVertices, sourceTriangles, sourceNormals))
            {
                float maxDistSq = settings.maxTransferDistance * settings.maxTransferDistance;
                float normalThresholdCos = Mathf.Cos(settings.normalAngleThreshold * Mathf.Deg2Rad);

                // Use batch processing for all vertices at once
                var queryResults = spatialQuery.FindClosestPointsBatch(targetVertices);

                for (int i = 0; i < targetVertices.Length; i++)
                {
                    var queryResult = queryResults[i];
                    var targetPos = targetVertices[i];
                    var targetNormal = targetNormals != null && i < targetNormals.Length
                        ? targetNormals[i]
                        : Vector3.up;

                    if (!queryResult.found)
                    {
                        outConfidence[i] = 0f;
                        continue;
                    }

                    // Distance check
                    float distSq = (targetPos - queryResult.closestPoint).sqrMagnitude;
                    if (distSq > maxDistSq)
                    {
                        outConfidence[i] = 0f;
                        continue;
                    }

                    // Normal alignment check
                    float normalDot = Vector3.Dot(targetNormal.normalized, queryResult.interpolatedNormal.normalized);
                    if (normalDot < normalThresholdCos)
                    {
                        outConfidence[i] = 0f;
                        continue;
                    }

                    // Transfer weight using barycentric interpolation
                    outWeights[i] = InterpolateBoneWeight(
                        sourceWeights[queryResult.triangleIndices.x],
                        sourceWeights[queryResult.triangleIndices.y],
                        sourceWeights[queryResult.triangleIndices.z],
                        queryResult.barycentricCoords);

                    // Compute confidence based on distance and normal alignment
                    float distConfidence = 1f - Mathf.Sqrt(distSq) / settings.maxTransferDistance;
                    float normalConfidence = (normalDot - normalThresholdCos) / (1f - normalThresholdCos);
                    outConfidence[i] = distConfidence * normalConfidence;

                    transferredCount++;
                }
            }
        }

        /// <summary>
        /// Stage 2: Laplacian-based weight inpainting for vertices not transferred in Stage 1.
        /// </summary>
        private static void Stage2Inpainting(
            Vector3[] targetVertices,
            int[] targetTriangles,
            int boneCount,
            BoneWeight[] weights,
            float[] confidence,
            WeightTransferSettings settings,
            ref int inpaintedCount)
        {
            // Use WeightInpainting class for Laplacian-based interpolation
            var inpainting = new WeightInpainting(
                targetVertices,
                targetTriangles,
                settings.maxIterations,
                settings.tolerance);

            inpainting.Inpaint(weights, confidence, boneCount);

            // Count inpainted vertices
            for (int i = 0; i < confidence.Length; i++)
            {
                if (confidence[i] < 0.5f)
                {
                    inpaintedCount++;
                }
            }
        }

        /// <summary>
        /// Interpolates bone weights using barycentric coordinates.
        /// </summary>
        private static BoneWeight InterpolateBoneWeight(
            BoneWeight w0,
            BoneWeight w1,
            BoneWeight w2,
            Vector3 bary)
        {
            // Collect all bone indices and weights
            var boneWeights = new System.Collections.Generic.Dictionary<int, float>();

            AddBoneWeight(boneWeights, w0.boneIndex0, w0.weight0 * bary.x);
            AddBoneWeight(boneWeights, w0.boneIndex1, w0.weight1 * bary.x);
            AddBoneWeight(boneWeights, w0.boneIndex2, w0.weight2 * bary.x);
            AddBoneWeight(boneWeights, w0.boneIndex3, w0.weight3 * bary.x);

            AddBoneWeight(boneWeights, w1.boneIndex0, w1.weight0 * bary.y);
            AddBoneWeight(boneWeights, w1.boneIndex1, w1.weight1 * bary.y);
            AddBoneWeight(boneWeights, w1.boneIndex2, w1.weight2 * bary.y);
            AddBoneWeight(boneWeights, w1.boneIndex3, w1.weight3 * bary.y);

            AddBoneWeight(boneWeights, w2.boneIndex0, w2.weight0 * bary.z);
            AddBoneWeight(boneWeights, w2.boneIndex1, w2.weight1 * bary.z);
            AddBoneWeight(boneWeights, w2.boneIndex2, w2.weight2 * bary.z);
            AddBoneWeight(boneWeights, w2.boneIndex3, w2.weight3 * bary.z);

            // Sort by weight descending and take top 4
            var sortedWeights = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int, float>>(boneWeights);
            sortedWeights.Sort((a, b) => b.Value.CompareTo(a.Value));

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

            // Normalize weights
            if (totalWeight > 0f)
            {
                result.weight0 /= totalWeight;
                result.weight1 /= totalWeight;
                result.weight2 /= totalWeight;
                result.weight3 /= totalWeight;
            }

            return result;
        }

        private static void AddBoneWeight(
            System.Collections.Generic.Dictionary<int, float> dict,
            int boneIndex,
            float weight)
        {
            if (weight <= 0f) return;
            if (dict.ContainsKey(boneIndex))
                dict[boneIndex] += weight;
            else
                dict[boneIndex] = weight;
        }
    }
}
