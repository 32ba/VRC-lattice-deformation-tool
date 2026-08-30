#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
        public IEnumerator SkinnedShapeAndScale_CageFollowsRenderedControlPoints()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Assert.Ignore("Scene View cage E2E requires a graphics device.");
            }

            var avatarRoot = new GameObject("skinned-cage-bounds-e2e-avatar");
            avatarRoot.transform.localScale = Vector3.one * 1.1f;

            var original = new GameObject("skinned-cage-bounds-e2e-renderer");
            original.transform.SetParent(avatarRoot.transform, false);
            original.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            var armatureObject = new GameObject("skinned-cage-bounds-e2e-armature");
            armatureObject.transform.SetParent(avatarRoot.transform, false);
            var boneObject = new GameObject("skinned-cage-bounds-e2e-bone");
            boneObject.transform.SetParent(armatureObject.transform, false);
            Matrix4x4 bindPose =
                boneObject.transform.worldToLocalMatrix * original.transform.localToWorldMatrix;
            var source = CreateSkinnedBoundsRegressionMesh(bindPose);
            LatticeToolHandler handler = null;
            SceneView sceneView = null;
            bool previousPreviewAlignedCage = LatticePreviewUtility.UsePreviewAlignedCage;

            try
            {
                LatticePreviewUtility.UsePreviewAlignedCage = false;
                var renderer = original.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                renderer.rootBone = boneObject.transform;
                renderer.bones = new[] { boneObject.transform };
                renderer.SetBlendShapeWeight(0, 40f);
                renderer.SetBlendShapeWeight(1, 0f);

                // Reproduce the combined result of an active MA Shape Changer and
                // accumulated Scale Adjuster-style hierarchy/bone edits. The bind pose
                // remains the original one while the current rendered pose changes.
                armatureObject.transform.localRotation = Quaternion.Euler(-6f, 11f, 4f);
                armatureObject.transform.localScale = new Vector3(0.95f, 1.08f, 1.03f);
                boneObject.transform.localPosition = new Vector3(0.08f, 0.12f, -0.04f);
                boneObject.transform.localRotation = Quaternion.Euler(12f, -18f, 7f);
                boneObject.transform.localScale = new Vector3(1.25f, 0.82f, 1.12f);

                var deformer = original.AddComponent<LatticeDeformer>();
                deformer.Reset();

                handler = new LatticeToolHandler
                {
                    CaptureCageFramesForTests = true,
                };
                handler.Activate(deformer);

                sceneView = EditorWindow.GetWindow<SceneView>();
                sceneView.Show();
                SceneView.duringSceneGui += DrawCage;

                yield return WaitForNextCageRepaint(handler, sceneView);
                AssertCageFrame(handler);

                Bounds renderedBounds = GetBakedVertexWorldBounds(
                    renderer,
                    out Bounds conservativeReportedBounds);
                Vector3[] cagePositions = handler.GetLastCageHandlePositionsForTests();

                Assert.That(
                    Mathf.Max(
                        Vector3.Distance(conservativeReportedBounds.center, renderedBounds.center),
                        Vector3.Distance(conservativeReportedBounds.size, renderedBounds.size)),
                    Is.GreaterThan(0.5f),
                    "The fixture must reproduce Unity's conservative BakeMesh bounds: " +
                    "an unused large BlendShape frame must make Mesh.bounds disagree with " +
                    "the vertices rendered after the active Shape Changer and scale edits.");
                AssertCageCornersFollowRenderedShape(
                    cagePositions,
                    deformer.EditingSettings,
                    boneObject.transform.localToWorldMatrix * bindPose);
            }
            finally
            {
                SceneView.duringSceneGui -= DrawCage;
                LatticePreviewUtility.UsePreviewAlignedCage = previousPreviewAlignedCage;
                handler?.Deactivate();
                Object.DestroyImmediate(avatarRoot);
                Object.DestroyImmediate(source);
            }

            void DrawCage(SceneView view)
            {
                if (view == sceneView && Event.current != null)
                {
                    handler.OnToolGUI(view, original.GetComponent<LatticeDeformer>());
                }
            }
        }

        [UnityTest]
        [Category("GraphicsE2E")]
        public IEnumerator TopologyChangedSkinnedProxy_WithReorderedBones_UsesRemappedProxySkinning()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Assert.Ignore("Scene View cage E2E requires a graphics device.");
            }

            var avatarRoot = new GameObject("topology-mismatch-cage-e2e-avatar");
            var original = new GameObject("topology-mismatch-cage-e2e-original");
            var proxy = new GameObject("topology-mismatch-cage-e2e-aao-proxy");
            original.transform.SetParent(avatarRoot.transform, false);
            proxy.transform.SetParent(avatarRoot.transform, false);
            original.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            proxy.transform.localRotation = original.transform.localRotation;

            var stableBone = new GameObject("topology-mismatch-stable-bone");
            var scaledBone = new GameObject("topology-mismatch-scaled-bone");
            stableBone.transform.SetParent(avatarRoot.transform, false);
            scaledBone.transform.SetParent(avatarRoot.transform, false);

            Matrix4x4 stableBindPose =
                stableBone.transform.worldToLocalMatrix * original.transform.localToWorldMatrix;
            Matrix4x4 scaledBindPose =
                scaledBone.transform.worldToLocalMatrix * original.transform.localToWorldMatrix;
            Mesh source = CreateBoneReorderMesh(
                extraVertex: false,
                boneIndex: 0,
                new[] { stableBindPose, scaledBindPose });
            Mesh proxyMesh = CreateBoneReorderMesh(
                extraVertex: true,
                boneIndex: 1,
                new[] { scaledBindPose, stableBindPose });
            LatticeToolHandler handler = null;
            SceneView sceneView = null;
            bool previousPreviewAlignedCage = LatticePreviewUtility.UsePreviewAlignedCage;

            try
            {
                var originalRenderer = original.AddComponent<SkinnedMeshRenderer>();
                originalRenderer.sharedMesh = source;
                originalRenderer.bones = new[] { stableBone.transform, scaledBone.transform };
                originalRenderer.rootBone = stableBone.transform;

                var proxyRenderer = proxy.AddComponent<SkinnedMeshRenderer>();
                proxyRenderer.sharedMesh = proxyMesh;
                proxyRenderer.bones = new[] { scaledBone.transform, stableBone.transform };
                proxyRenderer.rootBone = stableBone.transform;

                // Simulate a scale-adjusted bone after AAO has rebuilt and reordered its
                // renderer arrays. The proxy mesh remaps the stable source bone to index
                // 1, while the source mesh still uses index 0.
                scaledBone.transform.localPosition = new Vector3(5f, 0f, 0f);
                scaledBone.transform.localScale = new Vector3(0.02f, 1f, 1f);

                var deformer = original.AddComponent<LatticeDeformer>();
                deformer.Reset();
                deformer.AlignMode = LatticeDeformer.LatticeAlignMode.Mode3_BoundsRemap;

                LatticePreviewUtility.UsePreviewAlignedCage = true;
                LatticePreviewUtility.RegisterProxy(originalRenderer, proxyRenderer);
                handler = new LatticeToolHandler
                {
                    CaptureCageFramesForTests = true,
                };
                handler.Activate(deformer);

                sceneView = EditorWindow.GetWindow<SceneView>();
                sceneView.Show();
                SceneView.duringSceneGui += DrawCage;
                yield return WaitForNextCageRepaint(handler, sceneView);
                AssertCageFrame(handler);

                Bounds renderedBounds = GetBakedVertexWorldBounds(
                    proxyRenderer,
                    out _);
                Vector3[] cagePositions = handler.GetLastCageHandlePositionsForTests();
                var cageBounds = new Bounds(cagePositions[0], Vector3.zero);
                for (int i = 1; i < cagePositions.Length; i++)
                {
                    cageBounds.Encapsulate(cagePositions[i]);
                }

                Assert.That(
                    Vector3.Distance(cageBounds.center, renderedBounds.center),
                    Is.LessThanOrEqualTo(1e-3f),
                    "The cage must stay on the rendered AAO proxy instead of following a differently indexed bone.");
                Assert.That(
                    Vector3.Distance(cageBounds.size, renderedBounds.size),
                    Is.LessThanOrEqualTo(1e-3f),
                    "A topology-changing proxy must keep its remapped vertices, weights, and bones together.");
            }
            finally
            {
                SceneView.duringSceneGui -= DrawCage;
                LatticePreviewUtility.UsePreviewAlignedCage = previousPreviewAlignedCage;
                handler?.Deactivate();
                LatticePreviewUtility.ClearProxy(original.GetComponent<Renderer>());
                Object.DestroyImmediate(avatarRoot);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(proxyMesh);
            }

            void DrawCage(SceneView view)
            {
                if (view == sceneView && Event.current != null)
                {
                    handler.OnToolGUI(view, original.GetComponent<LatticeDeformer>());
                }
            }
        }

        [UnityTest]
        [Category("GraphicsE2E")]
        public IEnumerator BlendShapeWeightChange_MovesCageWithoutRebindingOrJitter()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Assert.Ignore("Scene View cage E2E requires a graphics device.");
            }

            var root = new GameObject("blend-shape-cage-e2e-root");
            var proxy = new GameObject("blend-shape-cage-e2e-preview-proxy");
            proxy.transform.SetParent(root.transform, false);
            var bone0 = new GameObject("blend-shape-cage-e2e-bone-0");
            var bone1 = new GameObject("blend-shape-cage-e2e-bone-1");
            bone0.transform.SetParent(root.transform, false);
            bone1.transform.SetParent(root.transform, false);
            bone1.transform.localPosition = new Vector3(2f, 0f, 0f);
            Mesh source = CreateBlendShapeBindingRegressionMesh();
            IRenderFilterNode previewNode = null;
            LatticeToolHandler handler = null;
            SceneView sceneView = null;
            bool previousPreviewAlignedCage = LatticePreviewUtility.UsePreviewAlignedCage;

            try
            {
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                renderer.bones = new[] { bone0.transform, bone1.transform };
                renderer.rootBone = bone0.transform;
                renderer.SetBlendShapeWeight(0, 0f);

                var deformer = root.AddComponent<LatticeDeformer>();
                deformer.Reset();

                var proxyRenderer = proxy.AddComponent<SkinnedMeshRenderer>();
                proxyRenderer.sharedMesh = source;
                proxyRenderer.bones = new[] { bone0.transform, bone1.transform };
                proxyRenderer.rootBone = bone0.transform;
                Mesh previewMesh = GeneratePreviewMesh(deformer);
                Assert.That(previewMesh, Is.Not.Null);
                previewNode = CreateLatticePreviewNode(
                    deformer,
                    renderer,
                    proxyRenderer,
                    previewMesh);
                LatticePreviewUtility.UsePreviewAlignedCage = true;

                handler = new LatticeToolHandler
                {
                    CaptureCageFramesForTests = true,
                };
                handler.Activate(deformer);

                sceneView = EditorWindow.GetWindow<SceneView>();
                sceneView.Show();
                SceneView.duringSceneGui += DrawCage;

                yield return WaitForNextCageRepaint(handler, sceneView);
                AssertCageFrame(handler);
                Vector3[] weightZero = handler.GetLastCageHandlePositionsForTests();
                int initialBindingRefreshes = handler.ControlPointBindingRefreshCountForTests;
                int initialPreviewMeshId = proxyRenderer.sharedMesh.GetInstanceID();

                renderer.SetBlendShapeWeight(0, 100f);
                EditorUtility.SetDirty(renderer);
                previewNode.OnFrameGroup();
                Assert.That(proxyRenderer.GetBlendShapeWeight(0), Is.Zero);
                yield return WaitForNextCageRepaint(handler, sceneView);
                Assert.That(proxyRenderer.sharedMesh.GetInstanceID(), Is.EqualTo(initialPreviewMeshId));
                Assert.That(
                    handler.ControlPointBindingRefreshCountForTests,
                    Is.EqualTo(initialBindingRefreshes),
                    "In-place BlendShape preview updates must retain the established control-point bindings.");
                Vector3[] weightOneHundred = handler.GetLastCageHandlePositionsForTests();
                Assert.That(
                    weightOneHundred.Zip(weightZero, Vector3.Distance).Max(),
                    Is.GreaterThan(1f),
                    "An active Shape must move the cage to the currently rendered geometry.");
                yield return WaitForNextCageRepaint(handler, sceneView);
                AssertCageFrameEquals(
                    handler,
                    weightOneHundred,
                    "A stable Shape weight must not make the cage jitter between bindings.");

                renderer.SetBlendShapeWeight(0, 0f);
                EditorUtility.SetDirty(renderer);
                previewNode.OnFrameGroup();
                yield return WaitForNextCageRepaint(handler, sceneView);
                AssertCageFrameEquals(
                    handler,
                    weightZero,
                    "Returning the BlendShape weight to its initialization value must restore the cage.");
            }
            finally
            {
                SceneView.duringSceneGui -= DrawCage;
                LatticePreviewUtility.UsePreviewAlignedCage = previousPreviewAlignedCage;
                handler?.Deactivate();
                previewNode?.Dispose();
                LatticePreviewUtility.ClearProxy(root.GetComponent<Renderer>());
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(source);
            }

            void DrawCage(SceneView view)
            {
                if (view == sceneView && Event.current != null)
                {
                    handler.OnToolGUI(view, root.GetComponent<LatticeDeformer>());
                }
            }
        }

        [UnityTest]
        [Category("GraphicsE2E")]
        public IEnumerator DelayedBoneLessSkinnedProxy_DoesNotMoveAnAlreadyCorrectCage()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Scene View cage E2E requires a graphics device.");

            var sourceObject = new GameObject("delayed-boneless-source");
            var proxyObject = new GameObject("delayed-boneless-proxy");
            var mesh = CreateSourceMesh();
            LatticeToolHandler handler = null;
            SceneView sceneView = null;
            long proxyGeneration = 0;
            bool previousPreviewAlignedCage = LatticePreviewUtility.UsePreviewAlignedCage;
            try
            {
                sourceObject.transform.position = new Vector3(0f, 1.1684f, 0f);
                sourceObject.transform.localScale = Vector3.one * 0.02f;
                var sourceRenderer = sourceObject.AddComponent<SkinnedMeshRenderer>();
                sourceRenderer.sharedMesh = mesh;
                var deformer = sourceObject.AddComponent<LatticeDeformer>();
                deformer.Reset();
                deformer.AlignMode = LatticeDeformer.LatticeAlignMode.Mode3_BoundsRemap;

                LatticePreviewUtility.UsePreviewAlignedCage = true;
                handler = new LatticeToolHandler { CaptureCageFramesForTests = true };
                handler.Activate(deformer);
                sceneView = EditorWindow.GetWindow<SceneView>();
                sceneView.Show();
                SceneView.duringSceneGui += DrawCage;

                yield return WaitForNextCageRepaint(handler, sceneView);
                AssertCageFrame(handler);
                Vector3[] correctSourceCage = handler.GetLastCageHandlePositionsForTests();

                var proxyRenderer = proxyObject.AddComponent<SkinnedMeshRenderer>();
                proxyRenderer.sharedMesh = mesh;
                proxyGeneration = LatticePreviewUtility.RegisterProxy(sourceRenderer, proxyRenderer);
                yield return WaitForCageRepaints(handler, sceneView, 3);

                AssertCageFrameEquals(
                    handler,
                    correctSourceCage,
                    "Glasses_Ver_2_default-ON first draws correctly. A delayed NDMF proxy " +
                    "without bones, bind poses, or a usable baked pose must not replace that " +
                    "correct source alignment with the proxy transform.");
            }
            finally
            {
                SceneView.duringSceneGui -= DrawCage;
                LatticePreviewUtility.UsePreviewAlignedCage = previousPreviewAlignedCage;
                handler?.Deactivate();
                var sourceRenderer = sourceObject.GetComponent<SkinnedMeshRenderer>();
                var proxyRenderer = proxyObject.GetComponent<SkinnedMeshRenderer>();
                if (sourceRenderer != null && proxyRenderer != null && proxyGeneration != 0)
                    LatticePreviewUtility.ClearProxy(sourceRenderer, proxyRenderer, proxyGeneration);
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(proxyObject);
                Object.DestroyImmediate(mesh);
            }

            void DrawCage(SceneView view)
            {
                if (view == sceneView && Event.current != null)
                    handler.OnToolGUI(view, sourceObject.GetComponent<LatticeDeformer>());
            }
        }

        private static int ReadExplorationInteger(string name, int fallback)
        {
            string value = System.Environment.GetEnvironmentVariable(name);
            return int.TryParse(value, out int parsed) ? parsed : fallback;
        }

        private static string ApplyExplorationOperation(
            System.Random random,
            Transform avatar,
            Transform outfit,
            Transform rendererTransform,
            Transform proxyTransform,
            Transform armature,
            Transform bone,
            Transform secondaryBone,
            Transform retargetBone,
            Transform secondaryRetargetBone,
            SkinnedMeshRenderer renderer)
        {
            int operation = random.Next(19);
            switch (operation)
            {
                case 0:
                    avatar.localScale = RandomScale(random, 0.72f, 1.32f);
                    return $"avatar-scale={avatar.localScale}";
                case 1:
                    SetRandomTransform(random, outfit, 0.16f, 22f, 0.68f, 1.38f);
                    return "outfit-root-trs";
                case 2:
                    SetRandomTransform(random, rendererTransform, 0.08f, 105f, 0.78f, 1.24f);
                    return "renderer-trs";
                case 3:
                    SetRandomTransform(random, armature, 0.14f, 28f, 0.62f, 1.46f);
                    return "armature-trs";
                case 4:
                    SetRandomTransform(random, bone, 0.2f, 38f, 0.55f, 1.58f);
                    return "bone-trs";
                case 5:
                {
                    float weight = RandomRange(random, -25f, 125f);
                    renderer.SetBlendShapeWeight(0, weight);
                    return $"active-shape={weight:F3}";
                }
                case 6:
                {
                    float weight = RandomRange(random, 0f, 35f);
                    renderer.SetBlendShapeWeight(1, weight);
                    return $"large-unused-shape={weight:F3}";
                }
                case 7:
                    outfit.localScale = RandomScale(random, 0.64f, 1.42f);
                    armature.localScale = RandomScale(random, 0.58f, 1.52f);
                    bone.localScale = RandomScale(random, 0.52f, 1.64f);
                    return "stacked-nonuniform-scale";
                case 8:
                    SetRandomTransform(random, outfit, 0.18f, 25f, 0.64f, 1.42f);
                    SetRandomTransform(random, rendererTransform, 0.1f, 110f, 0.74f, 1.28f);
                    SetRandomTransform(random, armature, 0.16f, 32f, 0.58f, 1.52f);
                    SetRandomTransform(random, bone, 0.22f, 42f, 0.52f, 1.64f);
                    renderer.SetBlendShapeWeight(0, RandomRange(random, -25f, 125f));
                    renderer.SetBlendShapeWeight(1, RandomRange(random, 0f, 35f));
                    return "combined-burst";
                case 9:
                    proxyTransform.localPosition = rendererTransform.localPosition;
                    proxyTransform.localRotation = rendererTransform.localRotation;
                    proxyTransform.localScale = rendererTransform.localScale;
                    return "synchronize-preview-proxy-trs";
                case 10:
                    SetRandomTransform(random, proxyTransform, 0.12f, 115f, 0.7f, 1.34f);
                    return "preview-proxy-independent-trs";
                case 11:
                    LatticePreviewUtility.UsePreviewAlignedCage =
                        !LatticePreviewUtility.UsePreviewAlignedCage;
                    return $"preview-aligned-cage={LatticePreviewUtility.UsePreviewAlignedCage}";
                case 12:
                    SetRandomTransform(random, retargetBone, 0.24f, 46f, 0.48f, 1.72f);
                    return "retargeted-avatar-bone-trs";
                case 13:
                    bone.position = retargetBone.position;
                    bone.rotation = retargetBone.rotation;
                    bone.localScale = retargetBone.localScale;
                    return "setup-outfit-base-to-merge-sync";
                case 14:
                    SetRandomTransform(random, retargetBone, 0.26f, 50f, 0.46f, 1.76f);
                    SetRandomTransform(random, bone, 0.22f, 42f, 0.52f, 1.64f);
                    LatticePreviewUtility.UsePreviewAlignedCage = random.Next(2) == 0;
                    return "retarget-handoff-burst";
                case 15:
                    SetRandomTransform(random, secondaryBone, 0.24f, 48f, 0.5f, 1.68f);
                    return "secondary-outfit-bone-trs";
                case 16:
                    SetRandomTransform(random, secondaryRetargetBone, 0.28f, 54f, 0.44f, 1.8f);
                    return "secondary-retargeted-avatar-bone-trs";
                case 17:
                    secondaryBone.position = secondaryRetargetBone.position;
                    secondaryBone.rotation = secondaryRetargetBone.rotation;
                    secondaryBone.localScale = secondaryRetargetBone.localScale;
                    return "secondary-setup-outfit-sync";
                default:
                    SetRandomTransform(random, bone, 0.24f, 46f, 0.5f, 1.68f);
                    SetRandomTransform(random, secondaryBone, 0.24f, 46f, 0.5f, 1.68f);
                    SetRandomTransform(random, retargetBone, 0.28f, 54f, 0.44f, 1.8f);
                    SetRandomTransform(random, secondaryRetargetBone, 0.28f, 54f, 0.44f, 1.8f);
                    return "multi-bone-retarget-burst";
            }
        }

        private static void SetRandomTransform(
            System.Random random,
            Transform transform,
            float positionRange,
            float rotationRange,
            float minimumScale,
            float maximumScale)
        {
            transform.localPosition = RandomVector(random, -positionRange, positionRange);
            transform.localRotation = Quaternion.Euler(
                RandomVector(random, -rotationRange, rotationRange));
            transform.localScale = RandomScale(random, minimumScale, maximumScale);
        }

        private static Vector3 RandomScale(System.Random random, float minimum, float maximum)
        {
            return new Vector3(
                RandomRange(random, minimum, maximum),
                RandomRange(random, minimum, maximum),
                RandomRange(random, minimum, maximum));
        }

        private static Vector3 RandomVector(System.Random random, float minimum, float maximum)
        {
            return new Vector3(
                RandomRange(random, minimum, maximum),
                RandomRange(random, minimum, maximum),
                RandomRange(random, minimum, maximum));
        }

        private static float RandomRange(System.Random random, float minimum, float maximum)
        {
            return minimum + (float)random.NextDouble() * (maximum - minimum);
        }

        private static string DescribeExplorationState(
            Transform avatar,
            Transform outfit,
            Transform rendererTransform,
            Transform proxyTransform,
            Transform armature,
            Transform bone,
            Transform secondaryBone,
            Transform retargetBone,
            Transform secondaryRetargetBone,
            SkinnedMeshRenderer renderer)
        {
            return
                $"avatar=({DescribeTransform(avatar)}), outfit=({DescribeTransform(outfit)}), " +
                $"renderer=({DescribeTransform(rendererTransform)}), " +
                $"proxy=({DescribeTransform(proxyTransform)}), " +
                $"armature=({DescribeTransform(armature)}), bone=({DescribeTransform(bone)}), " +
                $"secondaryBone=({DescribeTransform(secondaryBone)}), " +
                $"retargetBone=({DescribeTransform(retargetBone)}), " +
                $"secondaryRetargetBone=({DescribeTransform(secondaryRetargetBone)}), " +
                $"shape0={renderer.GetBlendShapeWeight(0):F3}, " +
                $"shape1={renderer.GetBlendShapeWeight(1):F3}, " +
                $"previewAligned={LatticePreviewUtility.UsePreviewAlignedCage}";
        }

        private static string DescribeTransform(Transform transform)
        {
            return $"p={transform.localPosition}, r={transform.localEulerAngles}, s={transform.localScale}";
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

        private static Bounds GetBakedVertexWorldBounds(
            SkinnedMeshRenderer renderer,
            out Bounds reportedWorldBounds)
        {
            var baked = new Mesh();
            try
            {
                renderer.BakeMesh(baked);
                reportedWorldBounds = TransformBounds(
                    baked.bounds,
                    renderer.transform.localToWorldMatrix);
                Vector3[] vertices = baked.vertices;
                Assert.That(vertices, Is.Not.Empty);
                var bounds = new Bounds(
                    renderer.transform.TransformPoint(vertices[0]),
                    Vector3.zero);
                for (int i = 1; i < vertices.Length; i++)
                {
                    bounds.Encapsulate(renderer.transform.TransformPoint(vertices[i]));
                }
                return bounds;
            }
            finally
            {
                Object.DestroyImmediate(baked);
            }
        }

        private static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            var corners = new[]
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, max.y, max.z),
            };

            var transformed = new Bounds(matrix.MultiplyPoint3x4(corners[0]), Vector3.zero);
            for (int i = 1; i < corners.Length; i++)
            {
                transformed.Encapsulate(matrix.MultiplyPoint3x4(corners[i]));
            }
            return transformed;
        }

        private static void AssertCageCornersFollowRenderedShape(
            Vector3[] cagePositions,
            LatticeAsset settings,
            Matrix4x4 skinnedLocalToWorld)
        {
            Vector3Int gridSize = settings.GridSize;
            int controlCount = gridSize.x * gridSize.y * gridSize.z;
            bool includesInterior = cagePositions.Length == controlCount;
            int drawnIndex = 0;
            float maximumError = 0f;

            for (int controlIndex = 0; controlIndex < controlCount; controlIndex++)
            {
                int x = controlIndex % gridSize.x;
                int y = (controlIndex / gridSize.x) % gridSize.y;
                int z = controlIndex / (gridSize.x * gridSize.y);
                bool onBoundary =
                    x == 0 || x == gridSize.x - 1 ||
                    y == 0 || y == gridSize.y - 1 ||
                    z == 0 || z == gridSize.z - 1;
                if (!onBoundary && !includesInterior)
                {
                    continue;
                }

                bool isCorner =
                    (x == 0 || x == gridSize.x - 1) &&
                    (y == 0 || y == gridSize.y - 1) &&
                    (z == 0 || z == gridSize.z - 1);
                if (isCorner)
                {
                    Vector3 sourcePoint = settings.GetControlPointLocal(controlIndex);
                    Vector3 expected = skinnedLocalToWorld.MultiplyPoint3x4(sourcePoint);
                    maximumError = Mathf.Max(
                        maximumError,
                        Vector3.Distance(cagePositions[drawnIndex], expected));
                }
                drawnIndex++;
            }

            Assert.That(
                maximumError,
                Is.LessThanOrEqualTo(1e-4f),
                "Every cage corner must follow its corresponding source point through " +
                "the same bone and bind-pose matrices as the rendered mesh. The lattice " +
                "was initialized from the active Shape Changer geometry, so remapping it " +
                "to a disagreeing BakeMesh snapshot must not move the cage.");
        }

        private static void AssertCageCornersFollowCurrentBindings(
            LatticeToolHandler handler,
            Vector3[] cagePositions,
            LatticeAsset settings,
            Matrix4x4[] boneMatrices,
            System.Func<int, Vector3> shapeOffset = null)
        {
            Vector3Int gridSize = settings.GridSize;
            int controlCount = gridSize.x * gridSize.y * gridSize.z;
            bool includesInterior = cagePositions.Length == controlCount;
            int drawnIndex = 0;
            float maximumError = 0f;
            int maximumErrorControl = -1;
            Vector3 maximumErrorExpected = default;
            Vector3 maximumErrorActual = default;

            for (int controlIndex = 0; controlIndex < controlCount; controlIndex++)
            {
                int x = controlIndex % gridSize.x;
                int y = (controlIndex / gridSize.x) % gridSize.y;
                int z = controlIndex / (gridSize.x * gridSize.y);
                bool onBoundary =
                    x == 0 || x == gridSize.x - 1 ||
                    y == 0 || y == gridSize.y - 1 ||
                    z == 0 || z == gridSize.z - 1;
                if (!onBoundary && !includesInterior)
                {
                    continue;
                }

                Assert.That(
                    handler.TryGetControlPointBindingForTests(
                        controlIndex,
                        out int[] boneIndices,
                        out float[] weights),
                    Is.True);
                Vector3 sourcePoint = settings.GetControlPointLocal(controlIndex) +
                                      (shapeOffset?.Invoke(controlIndex) ?? Vector3.zero);
                Vector3 expected = Vector3.zero;
                for (int influence = 0; influence < boneIndices.Length; influence++)
                {
                    Assert.That(boneIndices[influence],
                        Is.InRange(0, boneMatrices.Length - 1));
                    expected += boneMatrices[boneIndices[influence]]
                        .MultiplyPoint3x4(sourcePoint) * weights[influence];
                }
                Vector3 actual = cagePositions[drawnIndex];
                float error = Vector3.Distance(actual, expected);
                if (error > maximumError)
                {
                    maximumError = error;
                    maximumErrorControl = controlIndex;
                    maximumErrorExpected = expected;
                    maximumErrorActual = actual;
                }
                drawnIndex++;
            }

            Assert.That(
                maximumError,
                Is.LessThanOrEqualTo(1e-4f),
                "The Scene View cage must apply the current proxy bone and bind-pose " +
                "matrices to every cached control-point binding exactly once. " +
                $"Worst control={maximumErrorControl}, expected={maximumErrorExpected}, " +
                $"actual={maximumErrorActual}.");
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
                    null,
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

        private static Mesh CreateSkinnedBoundsRegressionMesh(Matrix4x4 bindPose)
        {
            var vertices = new[]
            {
                new Vector3(-0.5f, -0.25f, -0.1f),
                new Vector3(0.5f, -0.25f, -0.1f),
                new Vector3(-0.5f, 0.25f, -0.1f),
                new Vector3(0.5f, 0.25f, -0.1f),
                new Vector3(-0.5f, -0.25f, 0.1f),
                new Vector3(0.5f, -0.25f, 0.1f),
                new Vector3(-0.5f, 0.25f, 0.1f),
                new Vector3(0.5f, 0.25f, 0.1f),
            };
            var boneWeights = new BoneWeight[vertices.Length];
            for (int i = 0; i < boneWeights.Length; i++)
            {
                boneWeights[i] = new BoneWeight
                {
                    boneIndex0 = 0,
                    weight0 = 1f,
                };
            }

            var mesh = new Mesh
            {
                name = "Skinned Cage Bounds E2E Source",
                vertices = vertices,
                triangles = new[]
                {
                    0, 2, 1, 1, 2, 3,
                    4, 5, 6, 5, 7, 6,
                    0, 1, 4, 1, 5, 4,
                    2, 6, 3, 3, 6, 7,
                    0, 4, 2, 2, 4, 6,
                    1, 3, 5, 3, 7, 5,
                },
                boneWeights = boneWeights,
                bindposes = new[] { bindPose },
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var activeShapeDeltas = new Vector3[vertices.Length];
            for (int i = 0; i < activeShapeDeltas.Length; i++)
            {
                activeShapeDeltas[i] = GetActiveShapeDelta(vertices[i]);
            }
            mesh.AddBlendShapeFrame(
                "ShapeChangerActive",
                100f,
                activeShapeDeltas,
                null,
                null);

            var unusedFrameDeltas = new Vector3[vertices.Length];
            for (int i = 0; i < unusedFrameDeltas.Length; i++)
            {
                unusedFrameDeltas[i] = new Vector3(2f, 5f, -3f);
            }
            mesh.AddBlendShapeFrame(
                "UnusedLargeFrame",
                100f,
                unusedFrameDeltas,
                null,
                null);
            return mesh;
        }

        private static Mesh CreateTwoBoneSkinnedBoundsRegressionMesh(
            Matrix4x4 leftBindPose,
            Matrix4x4 rightBindPose)
        {
            Mesh mesh = CreateSkinnedBoundsRegressionMesh(leftBindPose);
            mesh.name = "Two-Bone Skinned Cage Exploration Source";
            mesh.bindposes = new[] { leftBindPose, rightBindPose };
            Vector3[] vertices = mesh.vertices;
            var weights = new BoneWeight[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                weights[i] = new BoneWeight
                {
                    boneIndex0 = vertices[i].x < 0f ? 0 : 1,
                    weight0 = 1f,
                };
            }
            mesh.boneWeights = weights;
            return mesh;
        }

        private static Mesh CreateBoneReorderMesh(
            bool extraVertex,
            int boneIndex,
            Matrix4x4[] bindPoses)
        {
            var vertices = new List<Vector3>
            {
                new Vector3(-0.5f, -0.25f, -0.1f),
                new Vector3(0.5f, -0.25f, -0.1f),
                new Vector3(-0.5f, 0.25f, -0.1f),
                new Vector3(0.5f, 0.25f, -0.1f),
                new Vector3(-0.5f, -0.25f, 0.1f),
                new Vector3(0.5f, -0.25f, 0.1f),
                new Vector3(-0.5f, 0.25f, 0.1f),
                new Vector3(0.5f, 0.25f, 0.1f),
            };
            if (extraVertex)
            {
                vertices.Add(Vector3.zero);
            }

            var boneWeights = new BoneWeight[vertices.Count];
            for (int i = 0; i < boneWeights.Length; i++)
            {
                boneWeights[i] = new BoneWeight
                {
                    boneIndex0 = boneIndex,
                    weight0 = 1f,
                };
            }

            var mesh = new Mesh
            {
                name = extraVertex
                    ? "Topology Changed AAO Proxy Mesh"
                    : "Topology Mismatch Source Mesh",
                vertices = vertices.ToArray(),
                triangles = new[]
                {
                    0, 2, 1, 1, 2, 3,
                    4, 5, 6, 5, 7, 6,
                    0, 1, 4, 1, 5, 4,
                    2, 6, 3, 3, 6, 7,
                    0, 4, 2, 2, 4, 6,
                    1, 3, 5, 3, 7, 5,
                },
                boneWeights = boneWeights,
                bindposes = bindPoses,
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateBlendShapeBindingRegressionMesh()
        {
            var mesh = new Mesh
            {
                name = "BlendShape Cage Binding Regression Mesh",
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
                boneWeights = new[]
                {
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 1, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                },
                bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity },
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.AddBlendShapeFrame(
                "MoveBindingAcrossBoneBoundary",
                100f,
                new[] { Vector3.left * 10f, Vector3.left * 0.5f, Vector3.zero },
                null,
                null);
            return mesh;
        }

        private static Vector3 GetActiveShapeDelta(Vector3 vertex)
        {
            return new Vector3(
                vertex.y * 0.18f,
                0.08f + vertex.x * 0.12f,
                vertex.x * vertex.y * 0.3f);
        }

        private static Vector3 InterpolateExpectedShapeDelta(
            Vector3[] vertices,
            Vector3 point,
            System.Func<Vector3, Vector3> getDelta)
        {
            const int neighborCount = 4;
            var nearest = new int[neighborCount] { -1, -1, -1, -1 };
            var distances = new float[neighborCount]
            {
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity,
            };
            for (int vertex = 0; vertex < vertices.Length; vertex++)
            {
                float distance = (vertices[vertex] - point).sqrMagnitude;
                if (distance >= distances[neighborCount - 1])
                    continue;
                int insertion = neighborCount - 1;
                while (insertion > 0 && distance < distances[insertion - 1])
                {
                    distances[insertion] = distances[insertion - 1];
                    nearest[insertion] = nearest[insertion - 1];
                    insertion--;
                }
                distances[insertion] = distance;
                nearest[insertion] = vertex;
            }
            if (distances[0] <= 1e-12f)
                return getDelta(vertices[nearest[0]]);

            Vector3 result = Vector3.zero;
            float total = 0f;
            for (int i = 0; i < neighborCount && nearest[i] >= 0; i++)
            {
                float weight = 1f / Mathf.Max(Mathf.Sqrt(distances[i]), 1e-6f);
                result += getDelta(vertices[nearest[i]]) * weight;
                total += weight;
            }
            return result / total;
        }
    }
}
#endif
