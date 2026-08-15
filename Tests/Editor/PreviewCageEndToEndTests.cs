#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using nadena.dev.ndmf.preview;
using Net._32Ba.LatticeDeformationTool;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class PreviewCageEndToEndTests
    {
        [UnityTest]
        [Category("GraphicsE2E")]
        public IEnumerator PostAaoProxyHandoff_KeepsEveryCageBoxVisibleAndStableDuringInteraction()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Assert.Ignore("Scene View cage E2E requires a graphics device.");
            }

            var original = new GameObject("cage-e2e-original");
            var preAaoProxy = new GameObject("cage-e2e-pre-aao");
            var postAaoProxy = new GameObject("cage-e2e-post-aao");
            var finalAaoProxy = new GameObject("cage-e2e-final-aao");
            var source = CreateSourceMesh();
            Mesh previewMesh = null;
            Mesh downstreamMesh = null;
            Mesh finalDownstreamMesh = null;
            IRenderFilterNode latticeNode = null;
            LatticeDeformerPostAaoPreviewFilter.PreviewNode postNode = null;
            LatticeDeformerPostAaoPreviewFilter.PreviewNode finalNode = null;
            LatticeToolHandler handler = null;
            SceneView sceneView = null;
            LatticeDeformer deformer = null;
            bool interactionActive = false;
            int simulatedHotControl = 0;
            bool previousPreviewAlignedCage = LatticePreviewUtility.UsePreviewAlignedCage;
            var frameMonitor = new CageFrameMonitor();

            try
            {
                LatticePreviewUtility.UsePreviewAlignedCage = true;
                original.AddComponent<MeshFilter>().sharedMesh = source;
                var originalRenderer = original.AddComponent<MeshRenderer>();
                deformer = original.AddComponent<LatticeDeformer>();
                deformer.Reset();
                deformer.AlignMode = LatticeDeformer.LatticeAlignMode.Mode3_BoundsRemap;

                previewMesh = GeneratePreviewMesh(deformer);
                Assert.That(previewMesh, Is.Not.Null);
                preAaoProxy.AddComponent<MeshFilter>().sharedMesh = previewMesh;
                var preAaoRenderer = preAaoProxy.AddComponent<MeshRenderer>();

                downstreamMesh = Object.Instantiate(previewMesh);
                var downstreamVertices = downstreamMesh.vertices;
                for (int i = 0; i < downstreamVertices.Length; i++)
                {
                    downstreamVertices[i] *= 1.5f;
                }
                downstreamMesh.vertices = downstreamVertices;
                downstreamMesh.RecalculateBounds();
                postAaoProxy.AddComponent<MeshFilter>().sharedMesh = downstreamMesh;
                var postAaoRenderer = postAaoProxy.AddComponent<MeshRenderer>();
                postAaoProxy.transform.position = new Vector3(0f, 5f, 0f);

                finalDownstreamMesh = Object.Instantiate(previewMesh);
                ScaleMesh(finalDownstreamMesh, 2f);
                finalAaoProxy.AddComponent<MeshFilter>().sharedMesh = finalDownstreamMesh;
                var finalAaoRenderer = finalAaoProxy.AddComponent<MeshRenderer>();
                finalAaoProxy.transform.position = new Vector3(3f, -2f, 0f);

                latticeNode = CreateLatticePreviewNode(
                    deformer,
                    originalRenderer,
                    preAaoRenderer,
                    previewMesh);
                handler = new LatticeToolHandler();
                handler.CaptureCageFramesForTests = true;
                handler.Activate(deformer);

                sceneView = EditorWindow.GetWindow<SceneView>();
                sceneView.Show();
                SceneView.duringSceneGui += DrawCage;

                yield return WaitForNextCageRepaint(handler, sceneView);
                AssertCageFrame(handler);
                Vector3[] dragStartFrame = handler.GetLastCageHandlePositionsForTests();

                interactionActive = true;
                frameMonitor.BeginInteraction(dragStartFrame);
                frameMonitor.CurrentOperation = "interaction-start";
                yield return WaitForCageRepaints(handler, sceneView, 2);

                frameMonitor.CurrentOperation = "register-post-aao-candidate";
                postNode = new LatticeDeformerPostAaoPreviewFilter.PreviewNode(
                    deformer,
                    originalRenderer,
                    postAaoRenderer,
                    downstreamMesh,
                    new ComputeContext("post AAO cage end-to-end test"));
                yield return WaitForCageRepaints(handler, sceneView, 2);

                frameMonitor.CurrentOperation = "commit-post-aao-candidate";
                postNode.OnFrame(originalRenderer, postAaoRenderer);
                yield return WaitForCageRepaints(handler, sceneView, 3);

                frameMonitor.CurrentOperation = "mutate-committed-mesh-in-place";
                ScaleMesh(downstreamMesh, 1.25f);
                EditorUtility.SetDirty(downstreamMesh);
                postAaoProxy.transform.position += new Vector3(-4f, 1f, 0f);
                yield return WaitForCageRepaints(handler, sceneView, 3);

                frameMonitor.CurrentOperation = "hierarchy-invalidation";
                var hierarchyPulse = new GameObject("cage-e2e-hierarchy-pulse");
                yield return WaitForCageRepaints(handler, sceneView, 2);
                Object.DestroyImmediate(hierarchyPulse);
                yield return WaitForCageRepaints(handler, sceneView, 2);

                frameMonitor.CurrentOperation = "register-and-commit-newer-candidate";
                finalNode = new LatticeDeformerPostAaoPreviewFilter.PreviewNode(
                    deformer,
                    originalRenderer,
                    finalAaoRenderer,
                    finalDownstreamMesh,
                    new ComputeContext("final AAO cage end-to-end test"));
                yield return WaitForCageRepaints(handler, sceneView, 2);
                finalNode.OnFrame(originalRenderer, finalAaoRenderer);
                yield return WaitForCageRepaints(handler, sceneView, 3);

                frameMonitor.CurrentOperation = "destroy-original-cage-proxy";
                Object.DestroyImmediate(preAaoProxy);
                yield return WaitForCageRepaints(handler, sceneView, 4);

                frameMonitor.EndInteraction();
                frameMonitor.AssertAllInteractionFramesStable(
                    minimumFrameCount: 20,
                    minimumOperationCount: 7);

                interactionActive = false;
                yield return WaitForNextCageRepaint(handler, sceneView);
                AssertCageFrame(handler, dragStartFrame.Length);
                Vector3[] settledFrame = handler.GetLastCageHandlePositionsForTests();
                Assert.That(
                    handler.ResolveProxyRenderer(originalRenderer),
                    Is.SameAs(finalAaoRenderer),
                    "The latest displayed post-AAO proxy must be adopted once interaction ends.");
                AssertFrameDiffers(settledFrame, dragStartFrame,
                    "The settled cage must reflect the post-AAO mesh bounds, proving that the handoff completed.");

                yield return WaitForNextCageRepaint(handler, sceneView);
                AssertCageFrameEquals(handler, settledFrame,
                    "The settled post-AAO cage must not alternate with the previous frame.");
            }
            finally
            {
                SceneView.duringSceneGui -= DrawCage;
                if (simulatedHotControl != 0 && GUIUtility.hotControl == simulatedHotControl)
                {
                    GUIUtility.hotControl = 0;
                }
                LatticePreviewUtility.UsePreviewAlignedCage = previousPreviewAlignedCage;

                handler?.Deactivate();
                finalNode?.Dispose();
                postNode?.Dispose();
                latticeNode?.Dispose();
                LatticePreviewUtility.ClearProxy(original.GetComponent<Renderer>());
                Object.DestroyImmediate(original);
                if (preAaoProxy != null) Object.DestroyImmediate(preAaoProxy);
                Object.DestroyImmediate(postAaoProxy);
                Object.DestroyImmediate(finalAaoProxy);
                Object.DestroyImmediate(source);
                if (previewMesh != null) Object.DestroyImmediate(previewMesh);
                if (downstreamMesh != null) Object.DestroyImmediate(downstreamMesh);
                if (finalDownstreamMesh != null) Object.DestroyImmediate(finalDownstreamMesh);
            }

            void DrawCage(SceneView view)
            {
                if (view != sceneView || Event.current == null)
                {
                    return;
                }

                if (interactionActive)
                {
                    if (simulatedHotControl == 0)
                    {
                        simulatedHotControl = GUIUtility.GetControlID(FocusType.Passive);
                    }
                    GUIUtility.hotControl = simulatedHotControl;
                }
                else if (simulatedHotControl != 0 && GUIUtility.hotControl == simulatedHotControl)
                {
                    GUIUtility.hotControl = 0;
                }

                handler.OnToolGUI(view, deformer);
                if (Event.current.type == EventType.Repaint)
                {
                    frameMonitor.Observe(
                        handler.CageRepaintCountForTests,
                        handler.LastCageHandleCountForTests,
                        handler.GetLastCageHandlePositionsForTests());
                }
            }
        }

        private sealed class CageFrameMonitor
        {
            private readonly List<string> _violations = new List<string>();
            private readonly HashSet<string> _observedOperations = new HashSet<string>();
            private Vector3[] _baseline = System.Array.Empty<Vector3>();
            private int _lastObservedSequence = -1;
            private int _interactionFrameCount;
            private bool _interactionActive;

            internal string CurrentOperation { get; set; } = "setup";

            internal void BeginInteraction(Vector3[] baseline)
            {
                _baseline = (Vector3[])baseline.Clone();
                _violations.Clear();
                _observedOperations.Clear();
                _interactionFrameCount = 0;
                _interactionActive = true;
            }

            internal void EndInteraction()
            {
                _interactionActive = false;
            }

            internal void Observe(int sequence, int handleCount, Vector3[] positions)
            {
                if (sequence == _lastObservedSequence)
                {
                    return;
                }
                _lastObservedSequence = sequence;

                if (!_interactionActive)
                {
                    return;
                }

                _interactionFrameCount++;
                _observedOperations.Add(CurrentOperation);
                if (handleCount != _baseline.Length || positions.Length != _baseline.Length)
                {
                    _violations.Add(
                        $"frame {sequence} during '{CurrentOperation}' changed the box count " +
                        $"from {_baseline.Length} to {handleCount} (captured {positions.Length}).");
                    return;
                }

                for (int i = 0; i < _baseline.Length; i++)
                {
                    float distance = Vector3.Distance(positions[i], _baseline[i]);
                    if (distance <= 1e-5f)
                    {
                        continue;
                    }

                    _violations.Add(
                        $"frame {sequence} during '{CurrentOperation}' changed box {i} " +
                        $"from {_baseline[i]} to {positions[i]} (distance {distance}).");
                    return;
                }
            }

            internal void AssertAllInteractionFramesStable(
                int minimumFrameCount,
                int minimumOperationCount)
            {
                Assert.That(
                    _interactionFrameCount,
                    Is.GreaterThanOrEqualTo(minimumFrameCount),
                    "The subscription must observe enough repaint frames to cover the whole interaction interval.");
                Assert.That(
                    _observedOperations.Count,
                    Is.GreaterThanOrEqualTo(minimumOperationCount),
                    "The subscription must observe frames from every preview mutation stage.");
                Assert.That(
                    _violations,
                    Is.Empty,
                    "No subscribed cage frame may change shape while a handle owns the interaction. " +
                    string.Join("\n", _violations));
            }
        }

        private static IEnumerator WaitForNextCageRepaint(
            LatticeToolHandler handler,
            SceneView sceneView)
        {
            int previousCount = handler.CageRepaintCountForTests;
            for (int frame = 0; frame < 60; frame++)
            {
                sceneView.Repaint();
                SceneView.RepaintAll();
                yield return null;
                if (handler.CageRepaintCountForTests > previousCount)
                {
                    yield break;
                }
            }

            Assert.Fail("The Scene View did not repaint the lattice cage within 60 editor frames.");
        }

        private static IEnumerator WaitForCageRepaints(
            LatticeToolHandler handler,
            SceneView sceneView,
            int repaintCount)
        {
            int targetCount = handler.CageRepaintCountForTests + repaintCount;
            for (int frame = 0; frame < 120; frame++)
            {
                sceneView.Repaint();
                SceneView.RepaintAll();
                yield return null;
                if (handler.CageRepaintCountForTests >= targetCount)
                {
                    yield break;
                }
            }

            Assert.Fail($"The Scene View did not produce {repaintCount} cage repaint frames.");
        }

        private static void AssertCageFrame(LatticeToolHandler handler)
        {
            Assert.That(handler.LastCageHandleCountForTests, Is.GreaterThan(0),
                "A repaint frame must submit at least one visible CubeHandleCap.");
            Assert.That(
                handler.GetLastCageHandlePositionsForTests(),
                Has.Length.EqualTo(handler.LastCageHandleCountForTests));
        }

        private static void AssertCageFrame(LatticeToolHandler handler, int expectedCount)
        {
            Assert.That(expectedCount, Is.GreaterThan(0));
            Assert.That(handler.LastCageHandleCountForTests, Is.EqualTo(expectedCount),
                "Every lattice control must submit a CubeHandleCap in the repaint frame.");
            Assert.That(handler.GetLastCageHandlePositionsForTests(), Has.Length.EqualTo(expectedCount));
        }

        private static void AssertCageFrameEquals(
            LatticeToolHandler handler,
            Vector3[] expected,
            string message)
        {
            AssertCageFrame(handler, expected.Length);
            var actual = handler.GetLastCageHandlePositionsForTests();
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(
                    Vector3.Distance(actual[i], expected[i]),
                    Is.LessThan(1e-5f),
                    $"{message} Control point {i} changed from {expected[i]} to {actual[i]}.");
            }
        }

        private static void AssertFrameDiffers(
            Vector3[] actual,
            Vector3[] before,
            string message)
        {
            Assert.That(actual, Has.Length.EqualTo(before.Length));
            bool differs = false;
            for (int i = 0; i < before.Length; i++)
            {
                if (Vector3.Distance(actual[i], before[i]) > 1e-4f)
                {
                    differs = true;
                    break;
                }
            }

            Assert.That(differs, Is.True, message);
        }

        private static void ScaleMesh(Mesh mesh, float scale)
        {
            var vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] *= scale;
            }
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
        }

        private static Mesh GeneratePreviewMesh(LatticeDeformer deformer)
        {
            var generate = typeof(LatticeDeformerPreviewFilter).GetMethod(
                "GeneratePreviewMesh",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(generate, Is.Not.Null);
            return (Mesh)generate.Invoke(null, new object[] { deformer });
        }

        private static IRenderFilterNode CreateLatticePreviewNode(
            LatticeDeformer deformer,
            Renderer original,
            Renderer proxy,
            Mesh previewMesh)
        {
            var nodeType = typeof(LatticeDeformerPreviewFilter).GetNestedType(
                "PreviewNode",
                BindingFlags.NonPublic);
            Assert.That(nodeType, Is.Not.Null);
            return (IRenderFilterNode)System.Activator.CreateInstance(
                nodeType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new object[]
                {
                    deformer,
                    new[] { (original, proxy) },
                    previewMesh,
                },
                null);
        }

        private static Mesh CreateSourceMesh()
        {
            var mesh = new Mesh
            {
                name = "Cage E2E Source",
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f),
                    new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(0f, 0.5f, 0f),
                },
                triangles = new[] { 0, 1, 2 },
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
#endif
