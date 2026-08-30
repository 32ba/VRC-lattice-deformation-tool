#if UNITY_EDITOR
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using nadena.dev.ndmf;
using nadena.dev.ndmf.preview;
using Net._32Ba.LatticeDeformationTool.Editor;
using UnityEngine;

[assembly: ExportsPlugin(typeof(Net._32Ba.LatticeDeformationTool.Tests.Editor.LateDownstreamPreviewTestPlugin))]

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    /// <summary>
    /// Models an unrelated NDMF plugin which consumes and owns a copy of the
    /// lattice output later in the Optimizing phase.
    /// </summary>
    internal sealed class LateDownstreamPreviewTestPlugin : Plugin<LateDownstreamPreviewTestPlugin>
    {
        internal static int InstantiationCount { get; set; }
        internal static int OutputCount { get; set; }
        public override string QualifiedName => "net.32ba.lattice-deformation-tool.tests.late-downstream";
        public override string DisplayName => "Late downstream preview test consumer";

        protected override void Configure()
        {
            InPhase(BuildPhase.Optimizing)
                .AfterPlugin("net.32ba.lattice-deformation-tool")
                .Run("Copy lattice output", _ => { })
                .PreviewingWith(new CopyingFilter());
        }

        private sealed class CopyingFilter : IRenderFilter
        {
            public IEnumerable<TogglablePreviewNode> GetPreviewControlNodes()
            {
                yield break;
            }

            public bool CanEnableRenderers => false;

            public ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context)
            {
                return context.GetComponentsByType<LatticeDeformer>()
                    .Where(deformer => deformer != null &&
                                       deformer.name.StartsWith("real-meshia-preview-e2e-"))
                    .Select(deformer => context.GetComponent<Renderer>(deformer.gameObject))
                    .Where(renderer => renderer != null)
                    .Select(RenderGroup.For)
                    .ToImmutableList();
            }

            public Task<IRenderFilterNode> Instantiate(
                RenderGroup group,
                IEnumerable<(Renderer, Renderer)> proxyPairs,
                ComputeContext context)
            {
                return Task.FromResult<IRenderFilterNode>(new CopyingNode(proxyPairs));
            }
        }

        private sealed class CopyingNode : IRenderFilterNode
        {
            private readonly List<(Renderer proxy, Mesh mesh)> _outputs =
                new List<(Renderer proxy, Mesh mesh)>();

            public CopyingNode(IEnumerable<(Renderer, Renderer)> proxyPairs)
            {
                InstantiationCount++;
                foreach (var (_, proxy) in proxyPairs)
                {
                    Mesh input = LatticeDeformerPreviewFilter.GetRendererMesh(proxy);
                    if (proxy == null || input == null) continue;

                    Mesh output = Object.Instantiate(input);
                    if (output.triangles.Length >= 6)
                    {
                        int retainedIndexCount = output.triangles.Length / 2;
                        retainedIndexCount -= retainedIndexCount % 3;
                        output.triangles = output.triangles.Take(retainedIndexCount).ToArray();
                        output.RecalculateBounds();
                    }
                    OutputCount++;
                    output.name = input.name + "_LateDownstreamCopy";
                    LatticeDeformerPreviewFilter.AssignRendererMesh(proxy, output);
                    _outputs.Add((proxy, output));
                }
            }

            public RenderAspects WhatChanged => RenderAspects.Mesh;
            public void OnFrameGroup() { }

            public void OnFrame(Renderer original, Renderer proxy)
            {
                var output = _outputs.FirstOrDefault(pair => pair.proxy == proxy);
                if (output.mesh != null)
                    LatticeDeformerPreviewFilter.AssignRendererMesh(proxy, output.mesh);
            }

            public void Dispose()
            {
                foreach (var (_, mesh) in _outputs)
                {
                    if (mesh != null) Object.DestroyImmediate(mesh);
                }
                _outputs.Clear();
            }
        }
    }
}
#endif
