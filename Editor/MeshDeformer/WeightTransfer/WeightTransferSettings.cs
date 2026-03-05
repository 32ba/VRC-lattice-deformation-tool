using System;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor.WeightTransfer
{
    /// <summary>
    /// Settings for weight transfer operations.
    /// Based on "Robust Skin Weights Transfer via Weight Inpainting" (SIGGRAPH Asia 2023).
    /// </summary>
    [Serializable]
    public class WeightTransferSettings
    {
        [Header("Stage 1: Initial Transfer")]
        [Tooltip("Maximum transfer distance as a fraction of the target mesh bounds diagonal (paper default: 0.05). May expand based on deformation magnitude.")]
        [Range(0.001f, 1.0f)]
        public float maxTransferDistance = 0.05f;

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
        [Range(1e-9f, 1e-3f)]
        public float tolerance = 1e-6f;

        /// <summary>
        /// Creates a default settings instance.
        /// </summary>
        public static WeightTransferSettings Default => new WeightTransferSettings();

        /// <summary>
        /// Creates a copy of this settings instance.
        /// </summary>
        public WeightTransferSettings Clone()
        {
            return new WeightTransferSettings
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
