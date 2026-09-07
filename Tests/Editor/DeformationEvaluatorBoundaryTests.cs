#if UNITY_EDITOR
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class DeformationEvaluatorBoundaryTests
    {
        [TestCase(BlendShapeCompositionMode.Single, false)]
        [TestCase(BlendShapeCompositionMode.Single, true)]
        [TestCase(BlendShapeCompositionMode.Progressive, false)]
        [TestCase(BlendShapeCompositionMode.Progressive, true)]
        [TestCase(BlendShapeCompositionMode.Crossfade, false)]
        [TestCase(BlendShapeCompositionMode.Crossfade, true)]
        public void SourceAndUpstreamAdapters_ShareGeneratedOutputAndPreserveInput(
            BlendShapeCompositionMode composition, bool individualOutput)
        {
            var root = new GameObject("Shared evaluation boundary");
            var mesh = DeformationOutputBaselineFixture.CreateMesh(3);
            Mesh preview = null;
            try
            {
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                root.AddComponent<MeshRenderer>();
                var deformer = root.AddComponent<LatticeDeformer>();
                deformer.Reset();
                using (var serialized = new SerializedObject(deformer))
                {
                    serialized.FindProperty("_recalculateNormals").boolValue = false;
                    serialized.FindProperty("_recalculateTangents").boolValue = false;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                AddBrush(deformer, "Direct", Vector3.right * 0.2f);
                if (individualOutput)
                {
                    AddBrush(deformer, "Individual", Vector3.forward * 0.1f);
                    deformer.Layers[deformer.ActiveLayerIndex].BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                    deformer.Layers[deformer.ActiveLayerIndex].BlendShapeName = "Existing";
                }
                deformer.AddGroup("Stages");
                AddBrush(deformer, "A", Vector3.up * 0.3f);
                AddBrush(deformer, "B", Vector3.forward * 0.4f);
                deformer.ActiveGroup.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.ActiveGroup.BlendShapeName = "Output";
                deformer.ActiveGroup.BlendShapeComposition = composition;
                deformer.InvalidateCache();
                string input = JsonUtility.ToJson(DeformationOutputBaselineFixture.CaptureMesh(mesh));

                var sourceOutput = DeformationOutputBaselineFixture.CaptureMesh(deformer.Deform(false));
                string authored = EditorJsonUtility.ToJson(deformer);
                preview = deformer.CreatePreviewMeshFromInput(mesh);
                Assert.That(preview, Is.Not.Null);
                DeformationOutputCompatibilityTests.CompareMesh(sourceOutput,
                    DeformationOutputBaselineFixture.CaptureMesh(preview), "same input adapters");

                // The preview evaluator reuses scratch storage for every upstream
                // frame. That must not overwrite the retained generated candidates.
                var previewBefore = DeformationOutputBaselineFixture.CaptureMesh(preview);
                deformer.Deform(false);
                DeformationOutputCompatibilityTests.CompareMesh(previewBefore,
                    DeformationOutputBaselineFixture.CaptureMesh(preview), "retained preview after evaluation");
                Assert.That(EditorJsonUtility.ToJson(deformer), Is.EqualTo(authored));
                Assert.That(JsonUtility.ToJson(DeformationOutputBaselineFixture.CaptureMesh(mesh)), Is.EqualTo(input));
                Assert.That(root.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
            }
            finally
            {
                if (preview != null) Object.DestroyImmediate(preview);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void AlternatingUpstreamTopology_DoesNotRetainPreviousWorkspaceVertices()
        {
            var root = new GameObject("Alternating evaluation sizes");
            var source = DeformationOutputBaselineFixture.CreateMesh(3);
            var reduced = DeformationOutputBaselineFixture.CreateMesh(2);
            Mesh preview = null;
            try
            {
                root.AddComponent<MeshFilter>().sharedMesh = source;
                root.AddComponent<MeshRenderer>();
                var deformer = root.AddComponent<LatticeDeformer>();
                deformer.Reset();
                var settings = deformer.EditingSettings;
                for (int i = 0; i < settings.ControlPointCount; i++)
                    settings.SetControlPointLocal(i, settings.GetControlPointLocal(i) + Vector3.up * 0.1f);
                var expected = DeformationOutputBaselineFixture.CaptureMesh(deformer.Deform(false));
                for (int i = 0; i < 3; i++)
                {
                    preview = deformer.CreatePreviewMeshFromInput(reduced);
                    Assert.That(preview.vertexCount, Is.EqualTo(reduced.vertexCount));
                    Object.DestroyImmediate(preview);
                    preview = null;
                    DeformationOutputCompatibilityTests.CompareMesh(expected,
                        DeformationOutputBaselineFixture.CaptureMesh(deformer.Deform(false)), "restored workspace size");
                }
            }
            finally
            {
                if (preview != null) Object.DestroyImmediate(preview);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(reduced);
            }
        }

        private static void AddBrush(LatticeDeformer deformer, string name, Vector3 offset)
        {
            int index = deformer.AddLayer(name, MeshDeformerLayerType.Brush);
            var layer = deformer.Layers[index];
            layer.Weight = 0.6f;
            int count = layer.BrushDisplacements.Length;
            layer.VertexMask = new float[count];
            for (int i = 0; i < count; i++)
            {
                layer.SetBrushDisplacement(i, offset);
                layer.VertexMask[i] = i % 2 == 0 ? 0.5f : 1f;
            }
        }
    }
}
#endif
