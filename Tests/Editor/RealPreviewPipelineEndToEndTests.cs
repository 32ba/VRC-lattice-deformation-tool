#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using nadena.dev.ndmf.preview;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    /// <summary>
    /// Playground-level coverage for the real NDMF preview graph. This suite deliberately
    /// does not construct render-filter nodes or call a tool handler directly.
    /// </summary>
    public sealed class RealPreviewPipelineEndToEndTests
    {
        private const string EnablePreviewMenu = "Tools/NDM Framework/Enable Previews";

        [UnityTest]
        [Category("GraphicsE2E")]
        [Category("PlaygroundE2E")]
        public IEnumerator ActualNdmfAaoGraph_NeverChangesCageShapeDuringAnInteraction()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("The real Scene View preview E2E requires a graphics device.");

            Type removeMeshInBoxType = FindType("Anatawa12.AvatarOptimizer.RemoveMeshInBox");
            if (removeMeshInBoxType == null)
                Assert.Ignore("Avatar Optimizer is not installed. Run this E2E in Plugin-dev-playground.");

            var root = new GameObject("real-preview-e2e-root");
            var meshObject = new GameObject("real-preview-e2e-mesh");
            meshObject.transform.SetParent(root.transform, false);
            Mesh source = CreateSeparatedTrianglesMesh();
            meshObject.AddComponent<MeshFilter>().sharedMesh = source;
            var sourceRenderer = meshObject.AddComponent<MeshRenderer>();
            var deformer = meshObject.AddComponent<LatticeDeformer>();
            deformer.Reset();
            deformer.AlignMode = LatticeDeformer.LatticeAlignMode.Mode3_BoundsRemap;
            Component removeMeshInBox = null;
            SceneView sceneView = null;
            bool holdInteraction = false;
            int ownedHotControl = 0;
            bool previewWasEnabled = IsPreviewUiEnabled();
            int previousDisableDepth = NDMFPreview.DisablePreviewDepth;
            Object previousSelection = Selection.activeObject;
            Type previousTool = ToolManager.activeToolType;
            var monitor = new CageIntervalMonitor();

            try
            {
                removeMeshInBox = meshObject.AddComponent(removeMeshInBoxType);
                InitializeRemoveMeshInBox(removeMeshInBox, new Vector3(-0.75f, 0f, 0f));

                NDMFPreview.DisablePreviewDepth = 0;
                if (PreviewSession.Current == null && !previewWasEnabled)
                {
                    Assert.That(EditorApplication.ExecuteMenuItem(EnablePreviewMenu), Is.True,
                        "The E2E must enable the same global preview session used by the Scene View.");
                }

                yield return WaitUntil(
                    () => PreviewSession.Current != null,
                    null,
                    "NDMF did not publish its global PreviewSession.");

                LatticeDeformerPreviewFilter.ForcePreviewState(true);
                PreviewSession.Current.ForceRebuild();

                sceneView = EditorWindow.GetWindow<SceneView>();
                sceneView.Show();
                sceneView.pivot = Vector3.zero;
                sceneView.size = 3f;
                Selection.activeGameObject = meshObject;
                ActiveEditorTracker.sharedTracker.ForceRebuild();
                yield return null;
                ToolManager.SetActiveTool<MeshDeformerTool>();
                LatticeToolHandler.CageFrameRendered += monitor.Observe;
                SceneView.beforeSceneGui += OwnInteractionControl;

                yield return WaitUntil(
                    () => monitor.LastFrame.HasValue &&
                          IsGenuineAaoOutput(monitor.LastFrame.Value.ProxyRenderer, source, root),
                    sceneView,
                    "The actual NDMF + AAO graph did not publish a post-AAO proxy to the active tool.");

                holdInteraction = true;
                monitor.BeginInteraction();
                yield return WaitForInteractionFrames(monitor, sceneView, 3);

                monitor.CurrentOperation = "AAO box moves to the other island";
                SetRemoveMeshBox(removeMeshInBox, new Vector3(0.75f, 0f, 0f));
                PreviewSession.Current.ForceRebuild();
                yield return WaitForInteractionFrames(monitor, sceneView, 4);

                monitor.CurrentOperation = "AAO component is disabled";
                ((Behaviour)removeMeshInBox).enabled = false;
                EditorUtility.SetDirty(removeMeshInBox);
                PreviewSession.Current.ForceRebuild();
                yield return WaitForInteractionFrames(monitor, sceneView, 4);

                monitor.CurrentOperation = "AAO component is enabled again";
                ((Behaviour)removeMeshInBox).enabled = true;
                SetRemoveMeshBox(removeMeshInBox, new Vector3(-0.75f, 0f, 0f));
                PreviewSession.Current.ForceRebuild();
                yield return WaitForInteractionFrames(monitor, sceneView, 4);

                monitor.CurrentOperation = "hierarchy changes while preview rebuilds";
                var hierarchyPulse = new GameObject("real-preview-e2e-hierarchy-pulse");
                hierarchyPulse.transform.SetParent(root.transform, false);
                PreviewSession.Current.ForceRebuild();
                yield return WaitForInteractionFrames(monitor, sceneView, 3);
                Object.DestroyImmediate(hierarchyPulse);
                PreviewSession.Current.ForceRebuild();
                yield return WaitForInteractionFrames(monitor, sceneView, 3);

                monitor.CurrentOperation = "AAO setting is undone and redone";
                Undo.RecordObject(removeMeshInBox, "E2E AAO box mutation");
                SetRemoveMeshBox(removeMeshInBox, Vector3.zero);
                Undo.PerformUndo();
                PreviewSession.Current.ForceRebuild();
                yield return WaitForInteractionFrames(monitor, sceneView, 3);
                Undo.PerformRedo();
                PreviewSession.Current.ForceRebuild();
                yield return WaitForInteractionFrames(monitor, sceneView, 3);

                monitor.EndInteraction();
                monitor.AssertIntervalWasStable(minimumFrames: 24, minimumOperations: 5);

                holdInteraction = false;
                yield return WaitUntil(
                    () => monitor.PostInteractionFrameCount >= 3 &&
                          monitor.LastFrame.HasValue &&
                          IsGenuineNdmfProxy(monitor.LastFrame.Value.ProxyRenderer, root),
                    sceneView,
                    "The tool did not settle on the latest genuine NDMF proxy after the interaction.");
                monitor.AssertSettledFramesDoNotAlternate();
            }
            finally
            {
                LatticeToolHandler.CageFrameRendered -= monitor.Observe;
                SceneView.beforeSceneGui -= OwnInteractionControl;
                if (ownedHotControl != 0 && GUIUtility.hotControl == ownedHotControl)
                    GUIUtility.hotControl = 0;

                if (previousTool != null)
                    ToolManager.SetActiveTool(previousTool);
                else
                    ToolManager.RestorePreviousTool();
                Selection.activeObject = previousSelection;

                NDMFPreview.DisablePreviewDepth = previousDisableDepth;
                // The menu item is a toggle: only flip it back when this test was the
                // one that enabled it, otherwise the restore would disable previews.
                if (!previewWasEnabled)
                    EditorApplication.ExecuteMenuItem(EnablePreviewMenu);

                LatticePreviewUtility.ClearProxy(sourceRenderer);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(source);
            }

            void OwnInteractionControl(SceneView view)
            {
                if (view != sceneView || Event.current == null)
                    return;

                if (holdInteraction)
                {
                    if (ownedHotControl == 0)
                        ownedHotControl = GUIUtility.GetControlID(FocusType.Passive);
                    GUIUtility.hotControl = ownedHotControl;
                }
                else if (ownedHotControl != 0 && GUIUtility.hotControl == ownedHotControl)
                {
                    GUIUtility.hotControl = 0;
                }
            }
        }

        private sealed class CageIntervalMonitor
        {
            private readonly List<string> _violations = new List<string>();
            private readonly HashSet<string> _operations = new HashSet<string>();
            private readonly List<Vector3[]> _settledFrames = new List<Vector3[]>();
            private Vector3[] _baseline;
            private bool _interaction;

            internal string CurrentOperation { get; set; } = "interaction begins";
            internal int InteractionFrameCount { get; private set; }
            internal int PostInteractionFrameCount => _settledFrames.Count;
            internal LatticeToolHandler.CageFrameSnapshot? LastFrame { get; private set; }

            internal void BeginInteraction()
            {
                Assert.That(LastFrame.HasValue, Is.True);
                _baseline = (Vector3[])LastFrame.Value.HandlePositions.Clone();
                Assert.That(_baseline, Is.Not.Empty);
                _violations.Clear();
                _operations.Clear();
                _settledFrames.Clear();
                InteractionFrameCount = 0;
                _interaction = true;
            }

            internal void EndInteraction()
            {
                _interaction = false;
                _settledFrames.Clear();
            }

            internal void Observe(LatticeToolHandler.CageFrameSnapshot frame)
            {
                LastFrame = frame;
                if (_interaction)
                {
                    InteractionFrameCount++;
                    _operations.Add(CurrentOperation);
                    ValidateInteractionFrame(frame);
                }
                else if (_baseline != null && frame.HandlePositions.Length > 0)
                {
                    _settledFrames.Add((Vector3[])frame.HandlePositions.Clone());
                }
            }

            internal void AssertIntervalWasStable(int minimumFrames, int minimumOperations)
            {
                Assert.That(InteractionFrameCount, Is.GreaterThanOrEqualTo(minimumFrames),
                    "The subscription did not cover the complete interaction interval.");
                Assert.That(_operations.Count, Is.GreaterThanOrEqualTo(minimumOperations),
                    "Every asynchronous preview mutation stage must be observed.");
                Assert.That(_violations, Is.Empty,
                    "No Scene View repaint may change the cage shape while another control owns the interaction.\n" +
                    string.Join("\n", _violations));
            }

            internal void AssertSettledFramesDoNotAlternate()
            {
                Assert.That(_settledFrames.Count, Is.GreaterThanOrEqualTo(3));
                Vector3[] expected = _settledFrames[_settledFrames.Count - 1];
                foreach (Vector3[] frame in _settledFrames.Skip(Math.Max(0, _settledFrames.Count - 3)))
                    AssertFramesEqual(expected, frame, "Settled cage frames must not alternate between proxies.");
            }

            private void ValidateInteractionFrame(LatticeToolHandler.CageFrameSnapshot frame)
            {
                if (!frame.InteractionActive)
                {
                    _violations.Add($"frame {frame.Sequence} during '{CurrentOperation}' lost interaction ownership.");
                    return;
                }

                if (frame.HandlePositions.Length != _baseline.Length)
                {
                    _violations.Add(
                        $"frame {frame.Sequence} during '{CurrentOperation}' changed the box count " +
                        $"from {_baseline.Length} to {frame.HandlePositions.Length}.");
                    return;
                }

                for (int i = 0; i < _baseline.Length; i++)
                {
                    if (Vector3.Distance(_baseline[i], frame.HandlePositions[i]) <= 1e-5f)
                        continue;
                    _violations.Add(
                        $"frame {frame.Sequence} during '{CurrentOperation}' moved box {i} " +
                        $"from {_baseline[i]} to {frame.HandlePositions[i]}.");
                    return;
                }
            }

            private static void AssertFramesEqual(Vector3[] expected, Vector3[] actual, string message)
            {
                Assert.That(actual, Has.Length.EqualTo(expected.Length), message);
                for (int i = 0; i < expected.Length; i++)
                    Assert.That(Vector3.Distance(actual[i], expected[i]), Is.LessThanOrEqualTo(1e-5f), message);
            }
        }

        private static IEnumerator WaitForInteractionFrames(
            CageIntervalMonitor monitor,
            SceneView sceneView,
            int additionalFrames)
        {
            int target = monitor.InteractionFrameCount + additionalFrames;
            yield return WaitUntil(
                () => monitor.InteractionFrameCount >= target,
                sceneView,
                $"The Scene View did not publish {additionalFrames} interaction repaint frames.");
        }

        private static IEnumerator WaitUntil(Func<bool> predicate, SceneView sceneView, string failure)
        {
            for (int frame = 0; frame < 240; frame++)
            {
                sceneView?.Repaint();
                SceneView.RepaintAll();
                yield return null;
                if (predicate())
                    yield break;
            }

            Assert.Fail(failure);
        }

        /// <summary>
        /// Reads NDMF's "Enable Previews" toggle. The menu item is a toggle, so tests
        /// must consult this state instead of inferring it from PreviewSession.Current,
        /// which is also null while an already-enabled session is still being built.
        /// </summary>
        private static bool IsPreviewUiEnabled()
        {
            return typeof(NDMFPreview)
                .GetProperty("EnablePreviewsUI", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                ?.GetValue(null) is bool enabled && enabled;
        }

        private static bool IsGenuineAaoOutput(Renderer proxy, Mesh source, GameObject avatarRoot)
        {
            if (!IsGenuineNdmfProxy(proxy, avatarRoot))
                return false;
            Mesh proxyMesh = LatticeDeformerPreviewFilter.GetRendererMesh(proxy);
            return proxyMesh != null && proxyMesh.triangles.Length < source.triangles.Length;
        }

        private static bool IsGenuineNdmfProxy(Renderer proxy, GameObject avatarRoot)
        {
            return proxy != null &&
                   proxy.gameObject.scene == NDMFPreviewSceneManager.GetPreviewScene() &&
                   NDMFPreview.GetOriginalObjectForProxy(proxy.gameObject)?.transform.IsChildOf(avatarRoot.transform) == true;
        }

        private static Type FindType(string fullName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(type => type != null);
        }

        private static void InitializeRemoveMeshInBox(Component component, Vector3 center)
        {
            MethodInfo initialize = component.GetType().GetMethod("Initialize", BindingFlags.Public | BindingFlags.Instance);
            Assert.That(initialize, Is.Not.Null);
            initialize.Invoke(component, new object[] { 1 });
            SetRemoveMeshBox(component, center);
        }

        private static void SetRemoveMeshBox(Component component, Vector3 center)
        {
            Type componentType = component.GetType();
            Type boxType = componentType.GetNestedType("BoundingBox", BindingFlags.Public);
            PropertyInfo boxesProperty = componentType.GetProperty("Boxes", BindingFlags.Public | BindingFlags.Instance);
            Assert.That(boxType, Is.Not.Null);
            Assert.That(boxesProperty, Is.Not.Null);

            object box = Activator.CreateInstance(boxType);
            boxType.GetProperty("Center")?.SetValue(box, center);
            boxType.GetProperty("Size")?.SetValue(box, new Vector3(0.9f, 1.2f, 1.2f));
            boxType.GetProperty("Rotation")?.SetValue(box, Quaternion.identity);
            Array boxes = Array.CreateInstance(boxType, 1);
            boxes.SetValue(box, 0);
            boxesProperty.SetValue(component, boxes);
            EditorUtility.SetDirty(component);
        }

        private static Mesh CreateSeparatedTrianglesMesh()
        {
            var mesh = new Mesh
            {
                name = "Real Preview E2E Source",
                vertices = new[]
                {
                    new Vector3(-1.0f, -0.35f, 0f),
                    new Vector3(-0.5f, -0.35f, 0f),
                    new Vector3(-0.75f, 0.35f, 0f),
                    new Vector3(0.5f, -0.35f, 0f),
                    new Vector3(1.0f, -0.35f, 0f),
                    new Vector3(0.75f, 0.35f, 0f),
                },
                triangles = new[] { 0, 1, 2, 3, 4, 5 },
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
#endif
