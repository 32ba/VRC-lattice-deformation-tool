using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using nadena.dev.ndmf;
using Net._32Ba.LatticeDeformationTool;
using Net._32Ba.LatticeDeformationTool.Editor.WeightTransfer;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: nadena.dev.ndmf.ExportsPlugin(typeof(Net._32Ba.LatticeDeformationTool.Editor.LatticeDeformerNDMFPlugin))]

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    [ExcludeFromCodeCoverage]
    internal sealed class LatticeDeformerNDMFPlugin : Plugin<LatticeDeformerNDMFPlugin>
    {
        public override string QualifiedName => "net.32ba.lattice-deformation-tool";
        public override string DisplayName => LatticeLocalization.Tr(LocKey.MeshDeformer);

        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming)
                .Run(LatticeDeformerBakePass.Instance)
                .PreviewingWith(new LatticeDeformerPreviewFilter())
                .BeforePlugin("com.anatawa12.avatar-optimizer");
        }
    }

    internal sealed class LatticeDeformerBakePass : Pass<LatticeDeformerBakePass>
    {
        [ExcludeFromCodeCoverage]
        protected override void Execute(BuildContext context)
        {
            var deformers = context.AvatarRootObject.GetComponentsInChildren<LatticeDeformer>(true);
            if (deformers == null || deformers.Length == 0)
            {
                return;
            }

            // Validate the complete build set before any mesh is instantiated, saved, or
            // assigned. Processing one component at a time would otherwise leave earlier
            // renderers mutated when a later component fails validation.
            if (!ValidateAllBeforeBake(deformers, out var firstInvalid))
            {
                string invalidName = firstInvalid != null ? firstInvalid.name : "unknown";
                throw new InvalidOperationException(
                    $"Mesh Deformer validation failed for '{invalidName}'. See MDV diagnostic codes in the Editor log.");
            }

            bool previousSuppressRestore = LatticeDeformer.SuppressRestoreOnDisable;
            try
            {
                LatticeDeformer.SuppressRestoreOnDisable = true;

                foreach (var deformer in deformers)
                {
                    ProcessValidatedDeformer(context, deformer);
                }
            }
            finally
            {
                LatticeDeformer.SuppressRestoreOnDisable = previousSuppressRestore;
            }
        }

        [ExcludeFromCodeCoverage]
        private static void ProcessValidatedDeformer(BuildContext context, LatticeDeformer deformer)
        {
            if (!ShouldProcessDeformer(deformer))
            {
                return;
            }

            var skinnedMesh = deformer.GetComponent<SkinnedMeshRenderer>();
            var meshFilter = deformer.GetComponent<MeshFilter>();

            var bakedMesh = deformer.Deform(false) ?? deformer.RuntimeMesh;
            if (bakedMesh == null)
            {
                throw new InvalidOperationException(
                    $"Validated Mesh Deformer '{deformer.name}' did not produce an output mesh.");
            }

            var sourceMesh = deformer.SourceMesh;
            if (sourceMesh == null)
            {
                if (skinnedMesh != null)
                {
                    sourceMesh = skinnedMesh.sharedMesh;
                }
                else if (meshFilter != null)
                {
                    sourceMesh = meshFilter.sharedMesh;
                }
            }

            if (sourceMesh == null)
            {
                throw new InvalidOperationException(
                    $"Validated Mesh Deformer '{deformer.name}' lost its source mesh before export.");
            }

            var ownerName = skinnedMesh != null ? skinnedMesh.name : meshFilter != null ? meshFilter.name : deformer.name;

            var exportMesh = Object.Instantiate(bakedMesh);
            if (!string.IsNullOrEmpty(ownerName))
            {
                exportMesh.name = ownerName + "_MeshDeformed";
            }

            // Recalculate bone weights if enabled and using SkinnedMeshRenderer
            if (deformer.RecalculateBoneWeights && skinnedMesh != null && sourceMesh.boneWeights != null && sourceMesh.boneWeights.Length > 0)
            {
                var settingsData = deformer.WeightTransferSettings;
                var settings = new WeightTransferSettings
                {
                    maxTransferDistance = settingsData.maxTransferDistance,
                    normalAngleThreshold = settingsData.normalAngleThreshold,
                    enableInpainting = settingsData.enableInpainting,
                    maxIterations = settingsData.maxIterations,
                    tolerance = settingsData.tolerance
                };

                var result = RobustWeightTransfer.Transfer(sourceMesh, sourceMesh.boneWeights, exportMesh, settings);
                if (result.success)
                {
                    exportMesh.boneWeights = result.weights;
                    Debug.Log($"[MeshDeformer] Weight transfer completed for {ownerName}: {result.transferredCount} transferred, {result.inpaintedCount} inpainted out of {result.totalVertices} vertices.");
                }
                else
                {
                    Debug.LogWarning($"[MeshDeformer] Weight transfer failed for {ownerName}: {result.errorMessage}");
                }
            }

            exportMesh.UploadMeshData(false);

            context.AssetSaver.SaveAsset(exportMesh);
            ObjectRegistry.RegisterReplacedObject(sourceMesh, exportMesh);

            if (skinnedMesh != null)
            {
                skinnedMesh.sharedMesh = exportMesh;
            }
            else if (meshFilter != null)
            {
                meshFilter.sharedMesh = exportMesh;
            }

            Object.DestroyImmediate(deformer, true);
        }

        internal static bool ShouldProcessDeformer(LatticeDeformer deformer)
        {
            return deformer != null && deformer.enabled;
        }

        internal static bool ValidateBeforeBake(LatticeDeformer deformer)
        {
            return !MeshDeformerValidator.HasErrors(ValidateBeforeBakeDiagnostics(deformer));
        }

        internal static bool ValidateAllBeforeBake(
            IReadOnlyList<LatticeDeformer> deformers,
            out LatticeDeformer firstInvalid)
        {
            firstInvalid = null;
            if (deformers == null) return true;

            for (int i = 0; i < deformers.Count; i++)
            {
                var deformer = deformers[i];
                if (!ShouldProcessDeformer(deformer)) continue;

                var diagnostics = ValidateBeforeBakeDiagnostics(deformer);
                MeshDeformerValidator.Log(diagnostics);
                if (firstInvalid == null && MeshDeformerValidator.HasErrors(diagnostics))
                {
                    firstInvalid = deformer;
                }
            }

            return firstInvalid == null;
        }

        internal static IReadOnlyList<MeshDeformerDiagnostic> ValidateBeforeBakeDiagnostics(LatticeDeformer deformer)
        {
            return MeshDeformerValidator.Validate(deformer, GetCurrentTargetMesh(deformer));
        }

        private static Mesh GetCurrentTargetMesh(LatticeDeformer deformer)
        {
            if (deformer == null) return null;
            var skinned = deformer.GetComponent<SkinnedMeshRenderer>();
            if (skinned != null) return skinned.sharedMesh;
            return deformer.GetComponent<MeshFilter>()?.sharedMesh;
        }
    }
}
