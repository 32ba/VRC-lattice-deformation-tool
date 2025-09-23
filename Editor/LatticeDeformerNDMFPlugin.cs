using nadena.dev.ndmf;
using Net._32Ba.LatticeDeformationTool;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: nadena.dev.ndmf.ExportsPlugin(typeof(Net._32Ba.LatticeDeformationTool.Editor.LatticeDeformerNDMFPlugin))]

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal sealed class LatticeDeformerNDMFPlugin : Plugin<LatticeDeformerNDMFPlugin>
    {
        public override string QualifiedName => "net.32ba.lattice-deformation-tool";
        public override string DisplayName => "Lattice Deformer";

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
        protected override void Execute(BuildContext context)
        {
            var deformers = context.AvatarRootObject.GetComponentsInChildren<LatticeDeformer>(true);
            if (deformers == null || deformers.Length == 0)
            {
                return;
            }

            try
            {
                LatticeDeformer.SuppressRestoreOnDisable = true;

                foreach (var deformer in deformers)
                {
                    ProcessDeformer(context, deformer);
                }
            }
            finally
            {
                LatticeDeformer.SuppressRestoreOnDisable = false;
            }
        }

        private static void ProcessDeformer(BuildContext context, LatticeDeformer deformer)
        {
            if (deformer == null)
            {
                return;
            }

            var skinnedMesh = deformer.GetComponent<SkinnedMeshRenderer>();
            var meshFilter = deformer.GetComponent<MeshFilter>();

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
                return;
            }

            var bakedMesh = deformer.Deform(false) ?? deformer.RuntimeMesh;
            if (bakedMesh == null)
            {
                return;
            }

            var ownerName = skinnedMesh != null ? skinnedMesh.name : meshFilter != null ? meshFilter.name : deformer.name;

            var exportMesh = Object.Instantiate(bakedMesh);
            if (!string.IsNullOrEmpty(ownerName))
            {
                exportMesh.name = ownerName + "_LatticeBaked";
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
    }
}
