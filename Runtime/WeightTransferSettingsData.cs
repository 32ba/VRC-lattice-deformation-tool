using System;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    /// <summary>
    /// Serializable settings data for weight transfer operations.
    /// This class is in Runtime so it can be serialized with LatticeDeformer.
    /// The actual transfer logic is in Editor (RobustWeightTransfer).
    /// </summary>
    [Serializable]
    public class WeightTransferSettingsData
    {
        [Header("Stage 1: Initial Transfer")]
        [Tooltip("Maximum distance for weight transfer. Vertices farther than this will be marked for inpainting.")]
        [Range(0.001f, 1.0f)]
        public float maxTransferDistance = 0.1f;

        [Tooltip("Maximum angle difference between source and target normals (in degrees).")]
        [Range(0f, 180f)]
        public float normalAngleThreshold = 60f;

        [Header("Stage 2: Weight Inpainting")]
        [Tooltip("Enable Laplacian-based weight inpainting for vertices that couldn't be transferred.")]
        public bool enableInpainting = true;

        [Tooltip("Maximum iterations for the iterative solver.")]
        [Range(100, 10000)]
        public int maxIterations = 1000;

        [Tooltip("Convergence tolerance for the iterative solver.")]
        public float tolerance = 1e-6f;

        /// <summary>
        /// Creates a default settings instance.
        /// </summary>
        public static WeightTransferSettingsData Default => new WeightTransferSettingsData();

        /// <summary>
        /// Creates a copy of this settings instance.
        /// </summary>
        public WeightTransferSettingsData Clone()
        {
            return new WeightTransferSettingsData
            {
                maxTransferDistance = this.maxTransferDistance,
                normalAngleThreshold = this.normalAngleThreshold,
                enableInpainting = this.enableInpainting,
                maxIterations = this.maxIterations,
                tolerance = this.tolerance
            };
        }
    }
}
