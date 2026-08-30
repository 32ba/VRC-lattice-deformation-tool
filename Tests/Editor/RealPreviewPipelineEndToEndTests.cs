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
using UnityEngine.TestTools.Utils;
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
        public IEnumerator ActualNdmfAaoGraph_KeepsExternalCageStableAndAppliesLatticeEditsDuringInteraction()
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
            bool previewWasEnabled = PreviewSession.Current != null;
            int previousDisableDepth = NDMFPreview.DisablePreviewDepth;
            Object previousSelection = Selection.activeObject;
            Type previousTool = ToolManager.activeToolType;
            var monitor = new CageIntervalMonitor();
            Vector3[] verticesBeforeEdit = null;

            try
            {
                removeMeshInBox = meshObject.AddComponent(removeMeshInBoxType);
                InitializeRemoveMeshInBox(removeMeshInBox, new Vector3(-0.75f, 0f, 0f));

                NDMFPreview.DisablePreviewDepth = 0;
                if (PreviewSession.Current == null)
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
                sceneView.Focus();
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

                // A stable external alignment must not freeze the edit itself. Reproduce
                // the LatticeToolHandler write path while a Scene View control owns the
                // interaction, then require both the cage and the real post-AAO proxy to
                // publish the new shape within a small continuous frame window.
                Vector3 handleBeforeEdit = monitor.LastFrame.Value.HandlePositions[0];
                Assert.That(NDMFPreviewProxyUtility.TryGetProxyRenderer(
                    sourceRenderer,
                    out Renderer displayedProxyBeforeEdit), Is.True);
                Mesh proxyMeshBeforeEdit = LatticeDeformerPreviewFilter.GetRendererMesh(
                    displayedProxyBeforeEdit);
                Assert.That(proxyMeshBeforeEdit, Is.Not.Null);
                verticesBeforeEdit = proxyMeshBeforeEdit.vertices;

                holdInteraction = true;
                yield return WaitForInteractionState(monitor, sceneView, true);
                LatticeAsset settings = deformer.EditingSettings;
                Assert.That(settings, Is.Not.Null);
                for (int control = 0; control < settings.ControlPointCount; control++)
                {
                    settings.SetControlPointLocal(
                        control,
                        settings.GetControlPointLocal(control) + Vector3.up * 0.2f);
                }
                deformer.NotifyDeformationDataChanged();
                deformer.Deform(false);
                LatticePreviewUtility.PublishInteractiveDeformation(deformer);

                bool handleFollowedEdit = false;
                bool displayedMeshFollowedEdit = false;
                for (int responseFrame = 0; responseFrame < 30; responseFrame++)
                {
                    sceneView.Repaint();
                    SceneView.RepaintAll();
                    yield return null;
                    if (!monitor.LastFrame.HasValue)
                        continue;

                    LatticeToolHandler.CageFrameSnapshot frame = monitor.LastFrame.Value;
                    handleFollowedEdit |= frame.InteractionActive &&
                                          Vector3.Distance(
                                              frame.HandlePositions[0],
                                              handleBeforeEdit) > 1e-5f;
                    if (NDMFPreviewProxyUtility.TryGetProxyRenderer(
                            sourceRenderer,
                            out Renderer displayedProxy))
                    {
                        displayedMeshFollowedEdit |= HasAnyVertexMoved(
                            verticesBeforeEdit,
                            LatticeDeformerPreviewFilter.GetRendererMesh(displayedProxy));
                    }
                }

                Assert.That(handleFollowedEdit, Is.True,
                    "The lattice cage did not follow its own control-point edit during the drag.");
                NDMFPreviewProxyUtility.TryGetProxyRenderer(
                    sourceRenderer,
                    out Renderer finalDisplayedProxy);
                LatticePreviewUtility.TryGetPreviewProxy(
                    sourceRenderer,
                    out Renderer registeredProxy);
                Assert.That(displayedMeshFollowedEdit, Is.True,
                    "The displayed post-AAO mesh did not follow the lattice edit during the drag. " +
                    $"final={DescribeMesh(LatticeDeformerPreviewFilter.GetRendererMesh(finalDisplayedProxy))}, " +
                    $"registered={DescribeMesh(LatticeDeformerPreviewFilter.GetRendererMesh(registeredProxy))}");

                holdInteraction = false;
                yield return WaitForInteractionState(monitor, sceneView, false);
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
                if (!previewWasEnabled && PreviewSession.Current != null && previousDisableDepth == 0)
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

        [UnityTest]
        [Category("GraphicsE2E")]
        [Category("PlaygroundE2E")]
        public IEnumerator ActualNdmfMeshiaGraph_RebuildsReducedMeshAfterEveryLatticeEdit()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("The real NDMF preview E2E requires a graphics device.");

            Type meshiaType = FindType("Meshia.MeshSimplification.Ndmf.MeshiaMeshSimplifier");
            if (meshiaType == null)
            {
                yield return VerifySyntheticTopologyChangingConsumerStream();
                yield break;
            }

            var root = new GameObject("real-meshia-preview-e2e-root");
            var meshObject = new GameObject("real-meshia-preview-e2e-mesh");
            meshObject.transform.SetParent(root.transform, false);
            Mesh source = CreateGridMesh(8, 8);
            meshObject.AddComponent<MeshFilter>().sharedMesh = source;
            var sourceRenderer = meshObject.AddComponent<MeshRenderer>();
            var deformer = meshObject.AddComponent<LatticeDeformer>();
            deformer.Reset();
            Component meshia = meshObject.AddComponent(meshiaType);
            bool previewWasEnabled = PreviewSession.Current != null;
            int previousDisableDepth = NDMFPreview.DisablePreviewDepth;
            Object previousSelection = Selection.activeObject;
            Type previousTool = ToolManager.activeToolType;
            bool previousMeshiaPreviewEnabled = SetMeshiaPreviewEnabled(true);
            SceneView sceneView = null;
            var monitor = new CageIntervalMonitor();

            try
            {
                LateDownstreamPreviewTestPlugin.InstantiationCount = 0;
                LateDownstreamPreviewTestPlugin.OutputCount = 0;
                NDMFPreview.DisablePreviewDepth = 0;
                // Recreate the session after the test assembly is loaded so its
                // intentionally-late preview consumer is present in the graph.
                if (PreviewSession.Current != null)
                {
                    Assert.That(EditorApplication.ExecuteMenuItem(EnablePreviewMenu), Is.True);
                    yield return WaitUntil(
                        () => PreviewSession.Current == null,
                        null,
                        "NDMF did not stop the existing preview session.");
                }
                Assert.That(EditorApplication.ExecuteMenuItem(EnablePreviewMenu), Is.True);

                yield return WaitUntil(
                    () => PreviewSession.Current != null,
                    null,
                    "NDMF did not publish its global PreviewSession.");

                LatticeDeformerPreviewFilter.ForcePreviewState(true);
                Selection.activeGameObject = meshObject;
                PreviewSession.Current.ForceRebuild();
                sceneView = EditorWindow.GetWindow<SceneView>();
                sceneView.Show();
                sceneView.Focus();
                ActiveEditorTracker.sharedTracker.ForceRebuild();
                yield return null;
                ToolManager.SetActiveTool<MeshDeformerTool>();
                LatticeToolHandler.CageFrameRendered += monitor.Observe;

                Renderer initialProxy = null;
                Mesh initialMesh = null;
                yield return WaitUntil(
                    () =>
                    {
                        if (!NDMFPreviewProxyUtility.TryGetProxyRenderer(sourceRenderer, out initialProxy))
                            return false;
                        initialMesh = LatticeDeformerPreviewFilter.GetRendererMesh(initialProxy);
                        return IsGenuineNdmfProxy(initialProxy, root) &&
                               initialMesh != null &&
                               initialMesh.triangles.Length < source.triangles.Length;
                    },
                    sceneView,
                    "The real Meshia preview did not publish a vertex-reduced mesh.");

                Assert.That(LateDownstreamPreviewTestPlugin.InstantiationCount, Is.GreaterThan(0),
                    "The E2E late downstream preview consumer was not part of the real graph.");
                Assert.That(LateDownstreamPreviewTestPlugin.OutputCount, Is.GreaterThan(0),
                    "The E2E late downstream preview consumer did not own a copied mesh.");

                yield return WaitUntil(
                    () => monitor.LastFrame.HasValue &&
                          monitor.LastFrame.Value.HandlePositions != null &&
                          monitor.LastFrame.Value.HandlePositions.Length > 0,
                    sceneView,
                    "The lattice handles did not appear on the actual Meshia preview proxy.");

                float beforeCenterY = initialMesh.bounds.center.y;
                LatticeAsset settings = deformer.EditingSettings;
                float greatestPublishedCenterY = beforeCenterY;
                for (int edit = 0; edit < 12; edit++)
                {
                    Assert.That(NDMFPreviewProxyUtility.TryGetProxyRenderer(
                        sourceRenderer, out Renderer proxyBeforePublish), Is.True);
                    Mesh meshBeforePublish = LatticeDeformerPreviewFilter.GetRendererMesh(proxyBeforePublish);
                    int downstreamGenerationBefore = LateDownstreamPreviewTestPlugin.OutputCount;

                    for (int control = 0; control < settings.ControlPointCount; control++)
                    {
                        settings.SetControlPointLocal(
                            control,
                            settings.GetControlPointLocal(control) + Vector3.up * 0.02f);
                    }
                    deformer.NotifyDeformationDataChanged();
                    deformer.Deform(false);
                    LatticePreviewUtility.PublishInteractiveDeformation(deformer);

                    Assert.That(NDMFPreviewProxyUtility.TryGetProxyRenderer(
                        sourceRenderer, out Renderer immediateProxy), Is.True);
                    Assert.That(immediateProxy, Is.SameAs(proxyBeforePublish),
                        "A lattice handle edit must not replace the global NDMF preview generation.");
                    Assert.That(LatticeDeformerPreviewFilter.GetRendererMesh(immediateProxy),
                        Is.SameAs(meshBeforePublish),
                        "The late lattice node must update its owned final mesh in place.");
                    Assert.That(meshBeforePublish.bounds.center.y,
                        Is.GreaterThan(beforeCenterY + edit * 0.015f),
                        "The final Meshia output must follow the handle before the next editor frame.");

                    for (int frame = 0; frame < 12; frame++)
                    {
                        sceneView.Repaint();
                        SceneView.RepaintAll();
                        yield return null;
                        Assert.That(monitor.LastFrame.HasValue, Is.True);
                        Assert.That(monitor.LastFrame.Value.HandlePositions.Length, Is.GreaterThan(0),
                            $"Meshia edit stream lost every lattice handle at edit {edit}, frame {frame}.");
                        if (NDMFPreviewProxyUtility.TryGetProxyRenderer(
                                sourceRenderer,
                                out Renderer streamedProxy))
                        {
                            Mesh streamedMesh = LatticeDeformerPreviewFilter.GetRendererMesh(streamedProxy);
                            if (streamedMesh != null && streamedMesh.triangles.Length < source.triangles.Length)
                            {
                                Assert.That(streamedMesh.bounds.center.y,
                                    Is.GreaterThan(beforeCenterY + edit * 0.015f),
                                    "The displayed final proxy reverted to a downstream-owned stale copy " +
                                    $"at edit {edit}, frame {frame}.");
                                Assert.That(streamedMesh.bounds.center.y + 1e-5f,
                                    Is.GreaterThanOrEqualTo(greatestPublishedCenterY),
                                    "A completed downstream preview generation must never regress to an older edit.");
                                greatestPublishedCenterY = Mathf.Max(
                                    greatestPublishedCenterY,
                                    streamedMesh.bounds.center.y);
                            }
                        }
                    }

                    Assert.That(LateDownstreamPreviewTestPlugin.OutputCount,
                        Is.GreaterThan(downstreamGenerationBefore),
                        "The interactive revision did not propagate through the actual later " +
                        $"NDMF consumer after edit {edit}.");
                }

                // A real handle gesture is undoable. The serialized control point
                // already returns through Unity's Undo system; require that the same
                // dependency-scoped preview notification also reaches every later
                // NDMF consumer instead of leaving the displayed mesh at the edited
                // generation.
                Assert.That(NDMFPreviewProxyUtility.TryGetProxyRenderer(
                    sourceRenderer, out Renderer beforeUndoEditProxy), Is.True);
                Mesh beforeUndoEditMesh = LatticeDeformerPreviewFilter.GetRendererMesh(beforeUndoEditProxy);
                Assert.That(beforeUndoEditMesh, Is.Not.Null);
                float centerBeforeUndoEdit = beforeUndoEditMesh.bounds.center.y;
                Vector3 controlBeforeUndoEdit = settings.GetControlPointLocal(0);

                Undo.RecordObject(deformer, "E2E undo lattice handle edit");
                for (int control = 0; control < settings.ControlPointCount; control++)
                {
                    settings.SetControlPointLocal(
                        control,
                        settings.GetControlPointLocal(control) + Vector3.up * 0.1f);
                }
                deformer.NotifyDeformationDataChanged();
                deformer.Deform(false);
                LatticePrefabUtility.MarkModified(deformer);
                Undo.FlushUndoRecordObjects();
                int generationBeforeUndoableEdit = LateDownstreamPreviewTestPlugin.OutputCount;
                LatticePreviewUtility.PublishInteractiveDeformation(deformer);
                yield return WaitUntil(
                    () => LateDownstreamPreviewTestPlugin.OutputCount > generationBeforeUndoableEdit,
                    sceneView,
                    "The undoable handle edit did not reach the late NDMF consumer.");

                int generationBeforeUndo = LateDownstreamPreviewTestPlugin.OutputCount;
                int revisionBeforeUndo = deformer.DeformationDataRevision;
                Undo.PerformUndo();
                yield return null;
                Assert.That(settings.GetControlPointLocal(0),
                    Is.EqualTo(controlBeforeUndoEdit).Using(Vector3ComparerWithEqualsOperator.Instance),
                    $"Unity Undo did not restore the recorded control point; " +
                    $"revisionBefore={revisionBeforeUndo}, revisionAfter={deformer.DeformationDataRevision}.");
                yield return WaitUntil(
                    () => LateDownstreamPreviewTestPlugin.OutputCount > generationBeforeUndo,
                    sceneView,
                    "Undo restored serialized lattice data but did not invalidate the later NDMF preview consumer. " +
                    $"revisionBefore={revisionBeforeUndo}, revisionAfter={deformer.DeformationDataRevision}, " +
                    $"published={LatticePreviewUtility.GetInteractiveRevision(deformer).Value}.");

                Assert.That(NDMFPreviewProxyUtility.TryGetProxyRenderer(
                    sourceRenderer, out Renderer afterUndoProxy), Is.True);
                Mesh afterUndoMesh = LatticeDeformerPreviewFilter.GetRendererMesh(afterUndoProxy);
                Assert.That(afterUndoMesh, Is.Not.Null);
                Assert.That(afterUndoMesh.bounds.center.y,
                    Is.EqualTo(centerBeforeUndoEdit).Within(1e-4f),
                    "Undo did not restore the displayed final preview mesh.");

                yield return WaitUntil(
                    () =>
                    {
                        if (!NDMFPreviewProxyUtility.TryGetProxyRenderer(sourceRenderer, out Renderer proxy))
                            return false;
                        Mesh current = LatticeDeformerPreviewFilter.GetRendererMesh(proxy);
                        return IsGenuineNdmfProxy(proxy, root) &&
                               current != null &&
                               current.triangles.Length < source.triangles.Length &&
                               current.bounds.center.y > beforeCenterY + 0.2f;
                    },
                    sceneView,
                    "The Meshia output stayed stale after the lattice handle edit.",
                    480);
            }
            finally
            {
                LatticeToolHandler.CageFrameRendered -= monitor.Observe;
                if (previousTool != null)
                    ToolManager.SetActiveTool(previousTool);
                else
                    ToolManager.RestorePreviousTool();
                Selection.activeObject = previousSelection;
                SetMeshiaPreviewEnabled(previousMeshiaPreviewEnabled);
                NDMFPreview.DisablePreviewDepth = previousDisableDepth;
                if (!previewWasEnabled && PreviewSession.Current != null && previousDisableDepth == 0)
                    EditorApplication.ExecuteMenuItem(EnablePreviewMenu);
                LatticePreviewUtility.ClearProxy(sourceRenderer);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(source);
            }
        }

        [UnityTest]
        [Category("GraphicsE2E")]
        public IEnumerator SyntheticTopologyChangingConsumer_FollowsEveryInteractiveGeneration()
        {
            yield return VerifySyntheticTopologyChangingConsumerStream();
        }

#if LATTICE_MODULAR_AVATAR_TESTS
        [UnityTest]
        [Category("MaSetupOutfitPreviewE2E")]
        [Category("GraphicsE2E")]
        public IEnumerator ActualNdmfMaSetupOutfitGraph_CageStaysStableAndMatchesPreviewSkinning()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("The real Scene View preview E2E requires a graphics device.");

            int seed = ReadInteger("LATTICE_MA_PREVIEW_EXPLORATION_SEED", 14401);
            int steps = Mathf.Clamp(ReadInteger("LATTICE_MA_PREVIEW_EXPLORATION_STEPS", 24), 1, 128);
            var random = new System.Random(seed);
            ModularAvatarSetupOutfitWorkflowTests.PreviewFixture fixture = null;
            SceneView sceneView = null;
            bool previewWasEnabled = PreviewSession.Current != null;
            int previousDisableDepth = NDMFPreview.DisablePreviewDepth;
            Object previousSelection = Selection.activeObject;
            Type previousTool = ToolManager.activeToolType;
            bool previousPreviewAlignedCage = LatticePreviewUtility.UsePreviewAlignedCage;
            var monitor = new CageIntervalMonitor();

            try
            {
                fixture = ModularAvatarSetupOutfitWorkflowTests.CreatePreviewFixture();
                AssertMaShapeChangerAnalysis(fixture);
                LatticePreviewUtility.UsePreviewAlignedCage = true;
                NDMFPreview.DisablePreviewDepth = 0;
                if (PreviewSession.Current == null)
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
                sceneView.Focus();
                sceneView.pivot = fixture.MeshObject.transform.position;
                sceneView.size = 2f;
                Selection.activeGameObject = fixture.MeshObject;
                ActiveEditorTracker.sharedTracker.ForceRebuild();
                yield return null;
                ToolManager.SetActiveTool<MeshDeformerTool>();
                LatticeToolHandler.CageFrameRendered += monitor.Observe;

                yield return WaitUntil(
                    () => monitor.LastFrame.HasValue &&
                          IsGenuineNdmfProxy(monitor.LastFrame.Value.ProxyRenderer, fixture.AvatarRoot),
                    sceneView,
                    "The actual NDMF + MA graph did not publish an outfit proxy to the active tool.");
                Assert.That(
                    IsGenuineMaSetupOutfitProxy(monitor.LastFrame.Value.ProxyRenderer, fixture),
                    Is.True,
                    DescribeProxy(monitor.LastFrame.Value.ProxyRenderer));
                AssertCageMatchesRetargetedSkinning(monitor.LastFrame.Value, fixture, seed, 0);

                for (int step = 1; step <= steps; step++)
                {
                    monitor.CurrentOperation = ApplyMaOperation(random, fixture, step);
                    monitor.BeginSettlingWindow();
                    PreviewSession.Current.ForceRebuild();
                    yield return WaitUntil(
                        () => monitor.PostInteractionFrameCount >= 3 &&
                              monitor.LastSettledFramesAreEqual &&
                              monitor.LastFrame.HasValue &&
                              IsGenuineMaSetupOutfitProxy(monitor.LastFrame.Value.ProxyRenderer, fixture) &&
                              CageMatchesRetargetedSkinning(
                                  monitor.LastFrame.Value,
                                  fixture,
                                  seed,
                                  step),
                        sceneView,
                        $"The MA preview did not settle after seed={seed}, step={step}/{steps}, " +
                        $"operation={monitor.CurrentOperation}.");
                    monitor.AssertSettledFramesDoNotAlternate();
                    AssertCageMatchesRetargetedSkinning(monitor.LastFrame.Value, fixture, seed, step);
                }
            }
            finally
            {
                LatticeToolHandler.CageFrameRendered -= monitor.Observe;
                if (previousTool != null)
                    ToolManager.SetActiveTool(previousTool);
                else
                    ToolManager.RestorePreviousTool();
                Selection.activeObject = previousSelection;
                LatticePreviewUtility.UsePreviewAlignedCage = previousPreviewAlignedCage;
                NDMFPreview.DisablePreviewDepth = previousDisableDepth;
                if (!previewWasEnabled && PreviewSession.Current != null && previousDisableDepth == 0)
                    EditorApplication.ExecuteMenuItem(EnablePreviewMenu);
                if (fixture != null)
                {
                    LatticePreviewUtility.ClearProxy(fixture.Renderer);
                    fixture.Dispose();
                }
            }
        }
#endif

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
            internal bool LastSettledFramesAreEqual
            {
                get
                {
                    if (_settledFrames.Count < 3)
                        return false;
                    Vector3[] expected = _settledFrames[_settledFrames.Count - 1];
                    return _settledFrames
                        .Skip(_settledFrames.Count - 3)
                        .All(frame => FramesEqual(expected, frame));
                }
            }
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

            internal void BeginSettlingWindow()
            {
                Assert.That(LastFrame.HasValue, Is.True);
                _baseline = (Vector3[])LastFrame.Value.HandlePositions.Clone();
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

            private static bool FramesEqual(Vector3[] expected, Vector3[] actual)
            {
                if (expected == null || actual == null || expected.Length != actual.Length)
                    return false;
                for (int i = 0; i < expected.Length; i++)
                {
                    if (Vector3.Distance(actual[i], expected[i]) > 1e-5f)
                        return false;
                }
                return true;
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

        private static IEnumerator WaitForInteractionState(
            CageIntervalMonitor monitor,
            SceneView sceneView,
            bool expected)
        {
            yield return WaitUntil(
                () => monitor.LastFrame.HasValue &&
                      monitor.LastFrame.Value.InteractionActive == expected,
                sceneView,
                $"The Scene View interaction state did not become {expected}.");
        }

        private static IEnumerator WaitUntil(
            Func<bool> predicate,
            SceneView sceneView,
            string failure,
            int maximumFrames = 240)
        {
            for (int frame = 0; frame < maximumFrames; frame++)
            {
                sceneView?.Repaint();
                SceneView.RepaintAll();
                yield return null;
                if (predicate())
                    yield break;
            }

            Assert.Fail(failure);
        }

        private static bool HasAnyVertexMoved(Vector3[] before, Mesh currentMesh)
        {
            if (before == null || currentMesh == null || currentMesh.vertexCount != before.Length)
                return false;

            Vector3[] current = currentMesh.vertices;
            for (int vertex = 0; vertex < before.Length; vertex++)
            {
                if ((current[vertex] - before[vertex]).sqrMagnitude > 1e-10f)
                    return true;
            }

            return false;
        }

        private static string DescribeMesh(Mesh mesh)
        {
            if (mesh == null)
                return "null";

            var indexCounts = new List<ulong>();
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                indexCounts.Add(mesh.GetIndexCount(subMesh));
            return $"id={mesh.GetInstanceID()}, vertices={mesh.vertexCount}, indices=[{string.Join(",", indexCounts)}]";
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

#if LATTICE_MODULAR_AVATAR_TESTS
        private static bool IsGenuineMaSetupOutfitProxy(
            Renderer renderer,
            ModularAvatarSetupOutfitWorkflowTests.PreviewFixture fixture)
        {
            if (!(renderer is SkinnedMeshRenderer proxy) ||
                !IsGenuineNdmfProxy(proxy, fixture.AvatarRoot) ||
                NDMFPreview.GetOriginalObjectForProxy(proxy.gameObject) != fixture.MeshObject ||
                proxy.bones == null ||
                proxy.bones.Length < 2)
            {
                return false;
            }

            return proxy.bones.All(bone => bone != null);
        }

        private static string DescribeProxy(Renderer renderer)
        {
            if (!(renderer is SkinnedMeshRenderer proxy))
                return $"Proxy is {renderer?.GetType().FullName ?? "null"}.";
            string BonePath(Transform bone)
            {
                if (bone == null) return "<null>";
                var names = new List<string>();
                for (Transform current = bone; current != null; current = current.parent)
                    names.Add(current.name);
                names.Reverse();
                Object original = NDMFPreview.GetOriginalObjectForProxy(bone.gameObject);
                return string.Join("/", names) + $" -> original={original?.name ?? "null"}";
            }
            return "Proxy bones:\n" + string.Join("\n", proxy.bones.Select(BonePath));
        }

        private static void AssertMaShapeChangerAnalysis(
            ModularAvatarSetupOutfitWorkflowTests.PreviewFixture fixture)
        {
            Type analyzerType = FindType("nadena.dev.modular_avatar.core.editor.ReactiveObjectAnalyzer");
            object analyzer = Activator.CreateInstance(
                analyzerType,
                new object[] { new ComputeContext("MA Shape Changer fixture validation") });
            object result = analyzerType.GetMethod("Analyze")?.Invoke(
                analyzer,
                new object[] { fixture.AvatarRoot });
            object shapes = result?.GetType().GetField("Shapes")?.GetValue(result);
            int count = shapes is System.Collections.IDictionary dictionary ? dictionary.Count : 0;
            Assert.That(count, Is.GreaterThan(0),
                "MA did not recognize the representative Shape Changer configuration.");
            bool initiallyActive = false;
            foreach (System.Collections.DictionaryEntry entry in (System.Collections.IDictionary)shapes)
            {
                FieldInfo groupsField = entry.Value.GetType().GetField("actionGroups");
                if (!(groupsField?.GetValue(entry.Value) is System.Collections.IEnumerable groups))
                    continue;
                foreach (object group in groups)
                {
                    PropertyInfo activeProperty = group.GetType().GetProperty("InitiallyActive");
                    if (activeProperty?.GetValue(group) is bool active && active)
                        initiallyActive = true;
                }
            }
            Assert.That(initiallyActive, Is.True,
                "MA recognized the Shape Changer, but its representative rule was not initially active.");
        }

        private static string ApplyMaOperation(
            System.Random random,
            ModularAvatarSetupOutfitWorkflowTests.PreviewFixture fixture,
            int step)
        {
            switch (random.Next(8))
            {
                case 0:
                    fixture.Renderer.SetBlendShapeWeight(0, RandomRange(random, -25f, 125f));
                    EditorUtility.SetDirty(fixture.Renderer);
                    return $"step {step}: shape weight";
                case 1:
                    fixture.OutfitRoot.transform.localScale = RandomScale(random, 0.65f, 1.45f);
                    return $"step {step}: outfit nonuniform scale";
                case 2:
                    fixture.MeshObject.transform.localPosition = RandomVector(random, 0.12f);
                    fixture.MeshObject.transform.localRotation = Quaternion.Euler(RandomVector(random, 35f));
                    return $"step {step}: renderer transform";
                case 3:
                    fixture.BaseHips.localRotation = Quaternion.Euler(RandomVector(random, 28f));
                    return $"step {step}: retarget hips rotation";
                case 4:
                    fixture.BaseLeftUpperArm.localRotation = Quaternion.Euler(RandomVector(random, 55f));
                    fixture.BaseLeftUpperArm.localScale = RandomScale(random, 0.72f, 1.3f);
                    return $"step {step}: retarget arm pose and scale";
                case 5:
                    fixture.AvatarRoot.transform.localScale = RandomScale(random, 0.75f, 1.3f);
                    return $"step {step}: avatar nonuniform scale";
                case 6:
                {
                    var pulse = new GameObject($"ma-preview-hierarchy-pulse-{step}");
                    pulse.transform.SetParent(fixture.OutfitRoot.transform, false);
                    Object.DestroyImmediate(pulse);
                    return $"step {step}: hierarchy pulse";
                }
                default:
                    fixture.Renderer.SetBlendShapeWeight(0, RandomRange(random, -25f, 125f));
                    fixture.OutfitRoot.transform.localScale = RandomScale(random, 0.62f, 1.5f);
                    fixture.BaseHips.localRotation = Quaternion.Euler(RandomVector(random, 32f));
                    fixture.BaseLeftUpperArm.localRotation = Quaternion.Euler(RandomVector(random, 60f));
                    EditorUtility.SetDirty(fixture.Renderer);
                    return $"step {step}: combined setup-outfit burst";
            }
        }

        private static void AssertCageMatchesRetargetedSkinning(
            LatticeToolHandler.CageFrameSnapshot frame,
            ModularAvatarSetupOutfitWorkflowTests.PreviewFixture fixture,
            int seed,
            int steps)
        {
            var proxy = frame.ProxyRenderer as SkinnedMeshRenderer;
            Assert.That(proxy, Is.Not.Null);
            Mesh proxyMesh = LatticeDeformerPreviewFilter.GetRendererMesh(proxy);
            Assert.That(proxyMesh, Is.Not.Null);
            int shapeIndex = proxyMesh.GetBlendShapeIndex("Representative Shape");
            Assert.That(shapeIndex, Is.GreaterThanOrEqualTo(0));
            float shapeWeight = fixture.Renderer.GetBlendShapeWeight(shapeIndex);
            if (steps == 0)
            {
                Assert.That(shapeWeight, Is.EqualTo(100f).Within(1e-4f),
                    "The representative user workflow must begin with the source shape active.");
            }
            var evaluator = new LatticeControlPointSkinning();
            LatticeAsset settings = fixture.Deformer.EditingSettings;
            Assert.That(evaluator.Update(
                proxy,
                fixture.SourceMesh,
                proxyMesh,
                settings.LocalBounds,
                settings.GridSize,
                fixture.MeshObject.transform.worldToLocalMatrix), Is.True,
                $"Could not build the independent final-pose cage oracle. seed={seed}, steps={steps}");
            Vector3Int grid = settings.GridSize;
            var displayedIndices = new List<int>();
            for (int index = 0; index < settings.ControlPointCount; index++)
            {
                int x = index % grid.x;
                int y = (index / grid.x) % grid.y;
                int z = index / (grid.x * grid.y);
                if (x == 0 || x == grid.x - 1 ||
                    y == 0 || y == grid.y - 1 ||
                    z == 0 || z == grid.z - 1)
                {
                    displayedIndices.Add(index);
                }
            }
            Assert.That(frame.HandlePositions, Has.Length.EqualTo(displayedIndices.Count));
            for (int displayedIndex = 0; displayedIndex < frame.HandlePositions.Length; displayedIndex++)
            {
                int controlIndex = displayedIndices[displayedIndex];
                Assert.That(evaluator.TryTransformPoint(
                    controlIndex,
                    settings.GetControlPointLocal(controlIndex) +
                    fixture.ShapeDelta * (shapeWeight / 100f),
                    out Vector3 corrected), Is.True);
                Vector3 expected = fixture.MeshObject.transform.TransformPoint(corrected);
                Assert.That(Vector3.Distance(frame.HandlePositions[displayedIndex], expected), Is.LessThanOrEqualTo(1e-4f),
                    $"MA Setup Outfit cage offset at control {controlIndex}. seed={seed}, steps={steps}, " +
                    $"expected={expected}, actual={frame.HandlePositions[displayedIndex]}");
            }
        }

        private static bool CageMatchesRetargetedSkinning(
            LatticeToolHandler.CageFrameSnapshot frame,
            ModularAvatarSetupOutfitWorkflowTests.PreviewFixture fixture,
            int seed,
            int step)
        {
            try
            {
                AssertCageMatchesRetargetedSkinning(frame, fixture, seed, step);
                return true;
            }
            catch (AssertionException)
            {
                return false;
            }
        }

        private static int ReadInteger(string name, int fallback)
        {
            string value = Environment.GetEnvironmentVariable(name);
            return int.TryParse(value, out int parsed) ? parsed : fallback;
        }

        private static float RandomRange(System.Random random, float min, float max) =>
            min + (float)random.NextDouble() * (max - min);

        private static Vector3 RandomVector(System.Random random, float magnitude) =>
            new Vector3(
                RandomRange(random, -magnitude, magnitude),
                RandomRange(random, -magnitude, magnitude),
                RandomRange(random, -magnitude, magnitude));

        private static Vector3 RandomScale(System.Random random, float min, float max) =>
            new Vector3(
                RandomRange(random, min, max),
                RandomRange(random, min, max),
                RandomRange(random, min, max));
#endif

        private static IEnumerator VerifySyntheticTopologyChangingConsumerStream()
        {
            var meshObject = new GameObject("synthetic-topology-preview-e2e-mesh");
            Mesh source = CreateGridMesh(8, 8);
            try
            {
                meshObject.AddComponent<MeshFilter>().sharedMesh = source;
                meshObject.AddComponent<MeshRenderer>();
                var deformer = meshObject.AddComponent<LatticeDeformer>();
                deformer.Reset();
                LatticeAsset settings = deformer.EditingSettings;
                int previousRevision = deformer.DeformationDataRevision;
                float previousCenterY = source.bounds.center.y;

                for (int edit = 0; edit < 12; edit++)
                {
                    for (int control = 0; control < settings.ControlPointCount; control++)
                    {
                        settings.SetControlPointLocal(
                            control,
                            settings.GetControlPointLocal(control) + Vector3.up * 0.02f);
                    }

                    LatticePreviewUtility.RefreshInteractiveDeformation(deformer);
                    Assert.That(deformer.DeformationDataRevision, Is.EqualTo(previousRevision + 1));
                    previousRevision = deformer.DeformationDataRevision;

                    Mesh downstream = LateDownstreamPreviewTestPlugin.CreateTopologyReducedCopy(
                        deformer.RuntimeMesh);
                    try
                    {
                        Assert.That(downstream, Is.Not.Null);
                        Assert.That(downstream.triangles.Length, Is.LessThan(source.triangles.Length));
                        Assert.That(downstream.bounds.center.y, Is.GreaterThan(previousCenterY + 0.015f),
                            $"The topology-changing downstream copy stayed stale after edit {edit}.");
                        previousCenterY = downstream.bounds.center.y;
                    }
                    finally
                    {
                        if (downstream != null) Object.DestroyImmediate(downstream);
                    }

                    yield return null;
                }
            }
            finally
            {
                Object.DestroyImmediate(meshObject);
                Object.DestroyImmediate(source);
            }
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

        private static Mesh CreateGridMesh(int columns, int rows)
        {
            var vertices = new Vector3[(columns + 1) * (rows + 1)];
            for (int y = 0; y <= rows; y++)
            for (int x = 0; x <= columns; x++)
                vertices[y * (columns + 1) + x] = new Vector3(x, y, 0f);

            var triangles = new int[columns * rows * 6];
            int index = 0;
            for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
            {
                int lowerLeft = y * (columns + 1) + x;
                int lowerRight = lowerLeft + 1;
                int upperLeft = lowerLeft + columns + 1;
                int upperRight = upperLeft + 1;
                triangles[index++] = lowerLeft;
                triangles[index++] = upperLeft;
                triangles[index++] = upperRight;
                triangles[index++] = lowerLeft;
                triangles[index++] = upperRight;
                triangles[index++] = lowerRight;
            }

            var mesh = new Mesh
            {
                name = "Real Meshia Preview E2E Source",
                vertices = vertices,
                triangles = triangles,
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static bool SetMeshiaPreviewEnabled(bool enabled)
        {
            Type previewType = FindType(
                "Meshia.MeshSimplification.Ndmf.Editor.Preview.MeshiaMeshSimplifierPreview");
            Assert.That(previewType, Is.Not.Null);
            PropertyInfo nodeProperty = previewType.GetProperty(
                "PreviewControlNode",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            Assert.That(nodeProperty, Is.Not.Null);
            object node = nodeProperty.GetValue(null);
            PropertyInfo isEnabledProperty = node.GetType().GetProperty(
                "IsEnabled",
                BindingFlags.Public | BindingFlags.Instance);
            object reactiveValue = isEnabledProperty.GetValue(node);
            PropertyInfo valueProperty = reactiveValue.GetType().GetProperty("Value");
            bool previous = (bool)valueProperty.GetValue(reactiveValue);
            valueProperty.SetValue(reactiveValue, enabled);
            return previous;
        }
    }
}
#endif
