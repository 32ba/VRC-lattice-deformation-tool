#if UNITY_EDITOR
using Net._32Ba.LatticeDeformationTool;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class PreviewInputEvaluationTests
    {
        [Test]
        public void CurrentUpstreamMesh_IsClonedAndDeformedWithoutMutatingRendererOrWeights()
        {
            var go = new GameObject("upstream-preview-evaluation");
            Mesh source = null;
            Mesh upstream = null;
            Mesh output = null;
            try
            {
                source = CreateMesh(Vector3.zero, Vector3.up);
                upstream = CreateMesh(Vector3.right * 2f, Vector3.forward * 0.4f);
                var renderer = go.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                renderer.SetBlendShapeWeight(0, 50f);
                var deformer = go.AddComponent<LatticeDeformer>();
                deformer.Reset();

                var lattice = deformer.EditingSettings;
                for (int control = 0; control < lattice.ControlPointCount; control++)
                {
                    lattice.SetControlPointLocal(
                        control,
                        lattice.GetControlPointLocal(control) + Vector3.right * 0.25f);
                }
                deformer.NotifyDeformationDataChanged();

                Vector3[] upstreamBefore = upstream.vertices;
                output = deformer.CreatePreviewMeshFromInput(upstream);

                Assert.That(output, Is.Not.Null);
                Assert.That(output, Is.Not.SameAs(upstream));
                Assert.That(renderer.sharedMesh, Is.SameAs(source));
                Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(50f));
                Assert.That(upstream.vertices, Is.EqualTo(upstreamBefore));
                Assert.That((output.vertices[0] -
                             (upstreamBefore[0] + Vector3.right * 0.25f)).magnitude,
                    Is.LessThan(1e-5f));
                Assert.That(output.blendShapeCount, Is.EqualTo(1));
                Assert.That(output.GetBlendShapeName(0), Is.EqualTo("Shape"));

                var delta = new Vector3[output.vertexCount];
                var normals = new Vector3[output.vertexCount];
                var tangents = new Vector3[output.vertexCount];
                output.GetBlendShapeFrameVertices(0, 0, delta, normals, tangents);
                Assert.That((delta[0] - Vector3.forward * 0.4f).magnitude, Is.LessThan(1e-5f),
                    "A same-named Shape changed by an upstream NDMF plugin must come from the " +
                    "current upstream mesh, not from the component's original SourceMesh.");
            }
            finally
            {
                if (output != null) Object.DestroyImmediate(output);
                Object.DestroyImmediate(go);
                if (source != null) Object.DestroyImmediate(source);
                if (upstream != null) Object.DestroyImmediate(upstream);
            }
        }

        [Test]
        public void UpstreamBlendShape_RoundTripWeightsAreDeterministicAndNeverAccumulate()
        {
            var go = new GameObject("upstream-shape-round-trip");
            Mesh source = null;
            Mesh upstream = null;
            Mesh output = null;
            try
            {
                source = CreateMesh(Vector3.zero, Vector3.up);
                upstream = CreateMesh(Vector3.zero, new Vector3(0.2f, 0.35f, -0.1f));
                var renderer = go.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                var deformer = go.AddComponent<LatticeDeformer>();
                deformer.Reset();

                var lattice = deformer.EditingSettings;
                for (int control = 0; control < lattice.ControlPointCount; control++)
                {
                    Vector3 point = lattice.GetControlPointLocal(control);
                    lattice.SetControlPointLocal(control, point +
                        new Vector3(point.y * 0.2f, point.x * 0.1f, 0.05f));
                }
                deformer.NotifyDeformationDataChanged();

                output = deformer.CreatePreviewMeshFromInput(upstream);
                Assert.That(output, Is.Not.Null);
                Vector3[] upstreamBefore = upstream.vertices;
                Vector3[] baseVertices = output.vertices;
                var delta = new Vector3[output.vertexCount];
                output.GetBlendShapeFrameVertices(
                    output.GetBlendShapeIndex("Shape"),
                    0,
                    delta,
                    new Vector3[output.vertexCount],
                    new Vector3[output.vertexCount]);

                Vector3[] firstZero = EvaluateWeight(baseVertices, delta, 0f);
                Vector3[] half = EvaluateWeight(baseVertices, delta, 0.5f);
                Vector3[] full = EvaluateWeight(baseVertices, delta, 1f);
                Vector3[] secondZero = EvaluateWeight(baseVertices, delta, 0f);

                Assert.That(secondZero, Is.EqualTo(firstZero),
                    "Returning a Shape weight to zero must return to the exact same preview vertices.");
                Assert.That(half, Is.Not.EqualTo(firstZero));
                Assert.That(full, Is.Not.EqualTo(half));
                Assert.That(renderer.sharedMesh, Is.SameAs(source));
                Assert.That(upstream.vertices, Is.EqualTo(upstreamBefore));
            }
            finally
            {
                if (output != null) Object.DestroyImmediate(output);
                Object.DestroyImmediate(go);
                if (source != null) Object.DestroyImmediate(source);
                if (upstream != null) Object.DestroyImmediate(upstream);
            }
        }

        [Test]
        public void LatticeOnlyStack_EvaluatesTopologyChangedUpstreamMesh()
        {
            var go = new GameObject("topology-changed-lattice-preview");
            Mesh source = null;
            Mesh reduced = null;
            Mesh output = null;
            try
            {
                source = CreateMesh(Vector3.zero, Vector3.up);
                reduced = new Mesh
                {
                    vertices = new[]
                    {
                        new Vector3(0.1f, 0.1f, 0f),
                        new Vector3(0.8f, 0.1f, 0f),
                        new Vector3(0.1f, 0.8f, 0f),
                        new Vector3(0.45f, 0.45f, 0f),
                    },
                    triangles = new[] { 0, 1, 3, 0, 3, 2 },
                };
                reduced.RecalculateBounds();
                var renderer = go.AddComponent<MeshFilter>();
                renderer.sharedMesh = source;
                go.AddComponent<MeshRenderer>();
                var deformer = go.AddComponent<LatticeDeformer>();
                deformer.Reset();
                foreach (int control in System.Linq.Enumerable.Range(
                             0, deformer.EditingSettings.ControlPointCount))
                {
                    deformer.EditingSettings.SetControlPointLocal(
                        control,
                        deformer.EditingSettings.GetControlPointLocal(control) + Vector3.up * 0.3f);
                }
                deformer.NotifyDeformationDataChanged();

                output = deformer.CreatePreviewMeshFromInput(reduced);

                Assert.That(deformer.CanPreviewAfterTopologyChanges(), Is.True);
                Assert.That(output, Is.Not.Null);
                Assert.That(output.vertexCount, Is.EqualTo(reduced.vertexCount));
                Assert.That(output.vertices[0].y, Is.EqualTo(reduced.vertices[0].y + 0.3f).Within(1e-5f));
                Assert.That(renderer.sharedMesh, Is.SameAs(source));
            }
            finally
            {
                if (output != null) Object.DestroyImmediate(output);
                Object.DestroyImmediate(go);
                if (source != null) Object.DestroyImmediate(source);
                if (reduced != null) Object.DestroyImmediate(reduced);
            }
        }

        [Test]
        public void ActiveBrushLayer_RejectsTopologyChangedLatePreview()
        {
            var go = new GameObject("topology-changed-brush-preview");
            Mesh source = null;
            Mesh reduced = null;
            try
            {
                source = CreateMesh(Vector3.zero, Vector3.up);
                reduced = new Mesh
                {
                    vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one },
                    triangles = new[] { 0, 1, 2, 1, 3, 2 },
                };
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = source;
                go.AddComponent<MeshRenderer>();
                var deformer = go.AddComponent<LatticeDeformer>();
                deformer.Reset();
                int brush = deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brush;
                deformer.EnsureDisplacementCapacity();

                Assert.That(deformer.CanPreviewAfterTopologyChanges(), Is.False);
                Assert.That(deformer.CreatePreviewMeshFromInput(reduced), Is.Null);
                Assert.That(filter.sharedMesh, Is.SameAs(source));
            }
            finally
            {
                Object.DestroyImmediate(go);
                if (source != null) Object.DestroyImmediate(source);
                if (reduced != null) Object.DestroyImmediate(reduced);
            }
        }

        private static Vector3[] EvaluateWeight(Vector3[] baseVertices, Vector3[] delta, float weight)
        {
            var result = new Vector3[baseVertices.Length];
            for (int i = 0; i < result.Length; i++)
                result[i] = baseVertices[i] + delta[i] * weight;
            return result;
        }

        private static Mesh CreateMesh(Vector3 offset, Vector3 shapeDelta)
        {
            var mesh = new Mesh
            {
                vertices = new[]
                {
                    offset + Vector3.zero,
                    offset + Vector3.right,
                    offset + Vector3.up
                },
                triangles = new[] { 0, 1, 2 }
            };
            mesh.RecalculateNormals();
            var delta = new[] { shapeDelta, shapeDelta, shapeDelta };
            var zero = new Vector3[3];
            mesh.AddBlendShapeFrame("Shape", 100f, delta, zero, zero);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
#endif
