#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using nadena.dev.ndmf.preview;
using Net._32Ba.LatticeDeformationTool;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class PreviewUtilityCoreTests
    {
        [Test]
        public void NDMFPreviewProxyUtility_TryBuildPair_AcceptsRenderersComponentsAndGameObjects()
        {
            var original = new GameObject("original");
            var proxy = new GameObject("proxy");
            try
            {
                var originalRenderer = original.AddComponent<MeshRenderer>();
                proxy.AddComponent<MeshFilter>();
                var proxyRenderer = proxy.AddComponent<MeshRenderer>();

                Assert.That(
                    NDMFPreviewProxyUtility.TryBuildPair(originalRenderer, proxy, out var pair),
                    Is.True);
                Assert.That(pair.Item1, Is.SameAs(originalRenderer));
                Assert.That(pair.Item2, Is.SameAs(proxyRenderer));

                Assert.That(
                    NDMFPreviewProxyUtility.TryBuildPair(original, proxyRenderer, out pair),
                    Is.True);
                Assert.That(pair.Item1, Is.SameAs(originalRenderer));
                Assert.That(pair.Item2, Is.SameAs(proxyRenderer));
            }
            finally
            {
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void NDMFPreviewProxyUtility_ExtractPairs_ReadsDictionaryEntries()
        {
            var original = new GameObject("original");
            var proxy = new GameObject("proxy");
            try
            {
                var originalRenderer = original.AddComponent<MeshRenderer>();
                var proxyRenderer = proxy.AddComponent<MeshRenderer>();
                var map = new Dictionary<Renderer, Renderer>
                {
                    [originalRenderer] = proxyRenderer
                };

                var pairs = NDMFPreviewProxyUtility.ExtractPairs(map).ToArray();

                Assert.That(pairs, Has.Length.EqualTo(1));
                Assert.That(pairs[0].original, Is.SameAs(originalRenderer));
                Assert.That(pairs[0].proxy, Is.SameAs(proxyRenderer));
            }
            finally
            {
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void NDMFPreviewProxyUtility_ExtractPairs_ReadsEnumerableKeyValuePairsAndSkipsInvalidItems()
        {
            var original = new GameObject("original");
            var proxy = new GameObject("proxy");
            try
            {
                var originalRenderer = original.AddComponent<MeshRenderer>();
                var proxyRenderer = proxy.AddComponent<MeshRenderer>();
                var entries = new object[]
                {
                    null,
                    new KeyValuePair<Renderer, Renderer>(originalRenderer, proxyRenderer),
                    new { Missing = originalRenderer, Value = proxyRenderer },
                    new KeyValuePair<object, object>("not-renderer", proxyRenderer)
                };

                var pairs = NDMFPreviewProxyUtility.ExtractPairs(entries).ToArray();

                Assert.That(pairs, Has.Length.EqualTo(1));
                Assert.That(pairs[0].original, Is.SameAs(originalRenderer));
                Assert.That(pairs[0].proxy, Is.SameAs(proxyRenderer));
            }
            finally
            {
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void NDMFPreviewProxyUtility_TryBuildPair_RejectsObjectsWithoutRenderers()
        {
            Assert.That(NDMFPreviewProxyUtility.TryBuildPair("key", "value", out var pair), Is.False);
            Assert.That(pair.Item1, Is.Null);
            Assert.That(pair.Item2, Is.Null);
        }

        [Test]
        public void NDMFPreviewProxyUtility_FakeSessionMembersAndMethods_AreEnumerated()
        {
            var original = new GameObject("original");
            var proxyFromField = new GameObject("proxy-field");
            var proxyFromProperty = new GameObject("proxy-property");
            var proxyFromMethod = new GameObject("proxy-method");
            try
            {
                var originalRenderer = original.AddComponent<MeshRenderer>();
                var fieldRenderer = proxyFromField.AddComponent<MeshRenderer>();
                var propertyRenderer = proxyFromProperty.AddComponent<MeshRenderer>();
                var methodRenderer = proxyFromMethod.AddComponent<MeshRenderer>();
                var session = new FakePreviewSession(originalRenderer, fieldRenderer, propertyRenderer, methodRenderer);

                var memberNames = NDMFPreviewProxyUtility.EnumerateProxyMapMembers(session)
                    .Select(member => member.Name)
                    .ToArray();
                var pairs = NDMFPreviewProxyUtility.GetOriginalToProxyPairs(session).ToArray();
                var methodPairs = NDMFPreviewProxyUtility.EnumerateProxyPairsFromMethods(session).ToArray();

                Assert.That(memberNames, Does.Contain(nameof(FakePreviewSession.OriginalToProxyField)));
                Assert.That(memberNames, Does.Contain(nameof(FakePreviewSession.OriginalToProxyProperty)));
                Assert.That(pairs.Select(pair => pair.proxy), Does.Contain(fieldRenderer));
                Assert.That(pairs.Select(pair => pair.proxy), Does.Contain(propertyRenderer));
                Assert.That(pairs.Select(pair => pair.proxy), Does.Contain(methodRenderer));
                Assert.That(methodPairs.Select(pair => pair.proxy), Does.Contain(methodRenderer));
            }
            finally
            {
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(proxyFromField);
                Object.DestroyImmediate(proxyFromProperty);
                Object.DestroyImmediate(proxyFromMethod);
            }
        }

        [Test]
        public void NDMFPreviewProxyUtility_TryInvokeProxyLookup_UsesExactOrFallbackMethod()
        {
            var original = new GameObject("original");
            var exactProxy = new GameObject("exact-proxy");
            var fallbackProxy = new GameObject("fallback-proxy");
            try
            {
                var originalRenderer = original.AddComponent<MeshRenderer>();
                var exactRenderer = exactProxy.AddComponent<MeshRenderer>();
                var fallbackRenderer = fallbackProxy.AddComponent<MeshRenderer>();

                Assert.That(
                    NDMFPreviewProxyUtility.TryInvokeProxyLookup(
                        new ExactProxyLookupSession(exactRenderer),
                        originalRenderer,
                        out var proxy),
                    Is.True);
                Assert.That(proxy, Is.SameAs(exactRenderer));

                Assert.That(
                    NDMFPreviewProxyUtility.TryInvokeProxyLookup(
                        new FallbackProxyLookupSession(fallbackRenderer),
                        originalRenderer,
                        out proxy),
                    Is.True);
                Assert.That(proxy, Is.SameAs(fallbackRenderer));

                Assert.That(
                    NDMFPreviewProxyUtility.TryInvokeProxyLookup(
                        new ThrowingProxyLookupSession(),
                        originalRenderer,
                        out proxy),
                    Is.False);
                Assert.That(proxy, Is.Null);
                Assert.That(NDMFPreviewProxyUtility.TryInvokeProxyLookup(null, originalRenderer, out proxy), Is.False);
                Assert.That(NDMFPreviewProxyUtility.TryInvokeProxyLookup(new object(), null, out proxy), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(exactProxy);
                Object.DestroyImmediate(fallbackProxy);
            }
        }

        [Test]
        public void NDMFPreviewProxyUtility_SessionEnumeration_SkipsNullThrowingAndNonCandidateMembers()
        {
            var original = new GameObject("original-edge");
            var proxy = new GameObject("proxy-edge");
            try
            {
                var originalRenderer = original.AddComponent<MeshRenderer>();
                var proxyRenderer = proxy.AddComponent<MeshRenderer>();
                var session = new EdgePreviewSession(originalRenderer, proxyRenderer);

                var members = NDMFPreviewProxyUtility.EnumerateProxyMapMembers(null).ToArray();
                Assert.That(members, Is.Empty);

                var pairs = NDMFPreviewProxyUtility.GetOriginalToProxyPairs(session).ToArray();
                Assert.That(pairs, Has.Length.EqualTo(1));
                Assert.That(pairs[0].original, Is.SameAs(originalRenderer));
                Assert.That(pairs[0].proxy, Is.SameAs(proxyRenderer));

                Assert.That(NDMFPreviewProxyUtility.EnumerateProxyPairsFromMethods(null).ToArray(), Is.Empty);
                Assert.That(NDMFPreviewProxyUtility.EnumerateProxyPairsFromMethods(new NoProxyMapMethods()).ToArray(), Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void NDMFPreviewProxyUtility_TryGetProxyRenderer_HandlesNullAndNoCurrentSession()
        {
            var original = new GameObject("original-no-session");
            try
            {
                var renderer = original.AddComponent<MeshRenderer>();

                Assert.That(NDMFPreviewProxyUtility.TryGetProxyRenderer(null, out var proxy), Is.False);
                Assert.That(proxy, Is.Null);
                Assert.That(NDMFPreviewProxyUtility.TryGetProxyRenderer(renderer, out proxy), Is.False);
                Assert.That(proxy, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(original);
            }
        }

        [Test]
        public void NDMFPreviewProxyUtility_TryGetProxyRenderer_UsesInjectedSessionLookup()
        {
            var lookupOriginal = new GameObject("lookup-original");
            var lookupProxy = new GameObject("lookup-proxy");
            try
            {
                var lookupRenderer = lookupOriginal.AddComponent<MeshRenderer>();
                var lookupProxyRenderer = lookupProxy.AddComponent<MeshRenderer>();

                Assert.That(
                    NDMFPreviewProxyUtility.TryGetProxyRenderer(
                        lookupRenderer,
                        new ExactProxyLookupSession(lookupProxyRenderer),
                        out var proxy),
                    Is.True);
                Assert.That(proxy, Is.SameAs(lookupProxyRenderer));
            }
            finally
            {
                Object.DestroyImmediate(lookupOriginal);
                Object.DestroyImmediate(lookupProxy);
            }
        }

        [Test]
        public void NDMFPreviewProxyUtility_TryGetProxyRenderer_SkipsNullPairsAndMatchesSameGameObject()
        {
            var original = new GameObject("same-go-original");
            var proxy = new GameObject("same-go-proxy");
            try
            {
                var originalRenderer = original.AddComponent<MeshRenderer>();
                original.AddComponent<MeshFilter>();
                var proxyRenderer = proxy.AddComponent<MeshRenderer>();
                var session = new SameGameObjectProxySession(originalRenderer, proxyRenderer);

                Assert.That(
                    NDMFPreviewProxyUtility.TryGetProxyRenderer(
                        originalRenderer,
                        session,
                        out var resolved),
                    Is.True);
                Assert.That(resolved, Is.SameAs(proxyRenderer));
            }
            finally
            {
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void LatticePreviewUtility_GetEditingTransform_UsesRegisteredProxyWhenEnabled()
        {
            var original = new GameObject("original");
            var proxy = new GameObject("proxy");
            bool previous = LatticePreviewUtility.UsePreviewAlignedCage;
            try
            {
                original.AddComponent<MeshRenderer>();
                var deformer = original.AddComponent<LatticeDeformer>();
                var proxyRenderer = proxy.AddComponent<MeshRenderer>();
                proxy.transform.position = new Vector3(1f, 2f, 3f);

                LatticePreviewUtility.UsePreviewAlignedCage = true;
                LatticePreviewUtility.RegisterProxy(original.GetComponent<Renderer>(), proxyRenderer);

                Assert.That(LatticePreviewUtility.GetEditingTransform(deformer), Is.SameAs(proxy.transform));
            }
            finally
            {
                LatticePreviewUtility.UsePreviewAlignedCage = previous;
                LatticePreviewUtility.ClearProxy(original.GetComponent<Renderer>());
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void LatticePreviewUtility_GetEditingTransform_FallsBackToMeshTransformWhenDisabled()
        {
            var original = new GameObject("original");
            bool previous = LatticePreviewUtility.UsePreviewAlignedCage;
            try
            {
                var deformer = original.AddComponent<LatticeDeformer>();
                LatticePreviewUtility.UsePreviewAlignedCage = false;

                Assert.That(LatticePreviewUtility.GetEditingTransform(deformer), Is.SameAs(deformer.MeshTransform));
            }
            finally
            {
                LatticePreviewUtility.UsePreviewAlignedCage = previous;
                Object.DestroyImmediate(original);
            }
        }

        [Test]
        public void LatticePreviewUtility_GetEditingTransform_ReturnsNullForNullAndFallsBackWithoutRenderer()
        {
            bool previous = LatticePreviewUtility.UsePreviewAlignedCage;
            var original = new GameObject("no-renderer");
            try
            {
                var deformer = original.AddComponent<LatticeDeformer>();
                LatticePreviewUtility.UsePreviewAlignedCage = true;

                Assert.That(LatticePreviewUtility.GetEditingTransform(null), Is.Null);
                Assert.That(LatticePreviewUtility.GetEditingTransform(deformer), Is.SameAs(deformer.MeshTransform));
            }
            finally
            {
                LatticePreviewUtility.UsePreviewAlignedCage = previous;
                Object.DestroyImmediate(original);
            }
        }

        [Test]
        public void LatticePreviewUtility_SettingsAccessors_ReturnDefaultsAndInstanceValues()
        {
            Assert.That(
                LatticePreviewUtility.GetAlignMode(null),
                Is.EqualTo(LatticeDeformer.LatticeAlignMode.Mode3_BoundsRemap));
            Assert.That(LatticePreviewUtility.GetCenterClampMulXY(null), Is.EqualTo(0f));
            Assert.That(LatticePreviewUtility.GetCenterClampMinXY(null), Is.EqualTo(0f));
            Assert.That(LatticePreviewUtility.GetCenterClampMulZ(null), Is.EqualTo(0f));
            Assert.That(LatticePreviewUtility.GetCenterClampMinZ(null), Is.EqualTo(0f));
            Assert.That(LatticePreviewUtility.GetAllowCenterOffsetWhenSkipped(null), Is.False);
            Assert.That(LatticePreviewUtility.GetManualOffsetProxy(null), Is.EqualTo(Vector3.zero));
            Assert.That(LatticePreviewUtility.GetManualScaleProxy(null), Is.EqualTo(Vector3.one));

            var go = new GameObject("deformer");
            try
            {
                var deformer = go.AddComponent<LatticeDeformer>();
                deformer.AlignMode = LatticeDeformer.LatticeAlignMode.Mode2_TransformPlusCenter;
                deformer.CenterClampMulXY = 2f;
                deformer.CenterClampMinXY = 0.25f;
                deformer.CenterClampMulZ = 3f;
                deformer.CenterClampMinZ = 0.5f;
                deformer.AllowCenterOffsetWhenBoundsSkipped = true;
                deformer.ManualOffsetProxy = new Vector3(1f, 2f, 3f);
                deformer.ManualScaleProxy = new Vector3(4f, 5f, 6f);

                Assert.That(LatticePreviewUtility.GetAlignMode(deformer), Is.EqualTo(deformer.AlignMode));
                Assert.That(LatticePreviewUtility.GetCenterClampMulXY(deformer), Is.EqualTo(2f));
                Assert.That(LatticePreviewUtility.GetCenterClampMinXY(deformer), Is.EqualTo(0.25f));
                Assert.That(LatticePreviewUtility.GetCenterClampMulZ(deformer), Is.EqualTo(3f));
                Assert.That(LatticePreviewUtility.GetCenterClampMinZ(deformer), Is.EqualTo(0.5f));
                Assert.That(LatticePreviewUtility.GetAllowCenterOffsetWhenSkipped(deformer), Is.True);
                Assert.That(LatticePreviewUtility.GetManualOffsetProxy(deformer), Is.EqualTo(new Vector3(1f, 2f, 3f)));
                Assert.That(LatticePreviewUtility.GetManualScaleProxy(deformer), Is.EqualTo(new Vector3(4f, 5f, 6f)));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LatticePreviewUtility_ShouldAssignRuntimeMesh_ReflectsPreviewState()
        {
            int previousDepth = NDMFPreview.DisablePreviewDepth;
            bool previousPreview = NDMFPreviewPrefs.instance.EnablePreview;
            try
            {
                NDMFPreview.DisablePreviewDepth = 1;
                NDMFPreviewPrefs.instance.EnablePreview = true;
                Assert.That(LatticePreviewUtility.ShouldAssignRuntimeMesh(), Is.True);

                NDMFPreview.DisablePreviewDepth = 0;
                NDMFPreviewPrefs.instance.EnablePreview = false;
                Assert.That(LatticePreviewUtility.ShouldAssignRuntimeMesh(), Is.True);

                Assert.That(LatticePreviewUtility.ShouldAssignRuntimeMesh(1, true, true), Is.True);
                Assert.That(LatticePreviewUtility.ShouldAssignRuntimeMesh(0, false, true), Is.True);
                Assert.That(LatticePreviewUtility.ShouldAssignRuntimeMesh(0, true, false), Is.True);
                Assert.That(LatticePreviewUtility.ShouldAssignRuntimeMesh(0, true, true), Is.False);
            }
            finally
            {
                NDMFPreview.DisablePreviewDepth = previousDepth;
                NDMFPreviewPrefs.instance.EnablePreview = previousPreview;
            }
        }

        [Test]
        public void LatticeDeformerPreviewFilter_BlendShapeWeightStateHash_IsStableAndTracksChanges()
        {
            var go = new GameObject("blendshape-state-hash");
            var source = CreateBlendShapeMesh(2);
            var replacement = CreateBlendShapeMesh(2);
            try
            {
                var renderer = go.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;

                int initial = LatticeDeformerPreviewFilter.ComputeBlendShapeWeightStateHash(renderer, source);
                int unchanged = LatticeDeformerPreviewFilter.ComputeBlendShapeWeightStateHash(renderer, source);
                Assert.That(unchanged, Is.EqualTo(initial));

                renderer.SetBlendShapeWeight(0, 37.5f);
                int weightChanged = LatticeDeformerPreviewFilter.ComputeBlendShapeWeightStateHash(renderer, source);
                Assert.That(weightChanged, Is.Not.EqualTo(initial));
                Assert.That(
                    LatticeDeformerPreviewFilter.ComputeBlendShapeWeightStateHash(renderer, source),
                    Is.EqualTo(weightChanged));

                int sourceChanged = LatticeDeformerPreviewFilter.ComputeBlendShapeWeightStateHash(renderer, replacement);
                Assert.That(sourceChanged, Is.Not.EqualTo(weightChanged));

                source.AddBlendShapeFrame(
                    "Shape2",
                    100f,
                    new Vector3[source.vertexCount],
                    new Vector3[source.vertexCount],
                    new Vector3[source.vertexCount]);
                int countChanged = LatticeDeformerPreviewFilter.ComputeBlendShapeWeightStateHash(renderer, source);
                Assert.That(countChanged, Is.Not.EqualTo(weightChanged));
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(replacement);
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LatticeDeformerPreviewFilter_CubicBernsteinPreviewMatchesBakeInput()
        {
            var root = new GameObject("cubic-preview-bake-parity");
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var source = Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh);
            Mesh preview = null;
            try
            {
                Object.DestroyImmediate(primitive);
                primitive = null;
                root.AddComponent<MeshFilter>().sharedMesh = source;
                root.AddComponent<MeshRenderer>();
                var deformer = root.AddComponent<LatticeDeformer>();
                deformer.Reset();
                deformer.Deform(false);

                var settings = deformer.Layers[0].Settings;
                settings.Interpolation = LatticeInterpolationMode.CubicBernstein;
                settings.SetControlPointLocal(
                    0,
                    settings.GetControlPointLocal(0) + Vector3.up * 0.2f);
                deformer.InvalidateCache();

                // LatticeDeformerBakePass uses Deform(false) as its bake input.
                var bakeInput = deformer.Deform(false).vertices;
                var generate = typeof(LatticeDeformerPreviewFilter).GetMethod(
                    "GeneratePreviewMesh",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(generate, Is.Not.Null);
                preview = (Mesh)generate.Invoke(null, new object[] { deformer });

                Assert.That(preview, Is.Not.Null);
                Assert.That(preview.vertexCount, Is.EqualTo(bakeInput.Length));
                var previewVertices = preview.vertices;
                for (int vertex = 0; vertex < bakeInput.Length; vertex++)
                {
                    Assert.That(
                        (previewVertices[vertex] - bakeInput[vertex]).sqrMagnitude,
                        Is.LessThanOrEqualTo(1e-8f),
                        $"Preview and bake input diverged at vertex {vertex}.");
                }
            }
            finally
            {
                if (preview != null) Object.DestroyImmediate(preview);
                if (primitive != null) Object.DestroyImmediate(primitive);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void LatticeDeformerPreviewFilter_RestoreProxyMesh_RestoresUpstreamMeshAndClearsRegistration()
        {
            var original = new GameObject("restore-proxy-original");
            var proxy = new GameObject("restore-proxy-target");
            var upstreamMesh = new Mesh();
            var previewMesh = new Mesh();
            try
            {
                var originalRenderer = original.AddComponent<MeshRenderer>();
                proxy.AddComponent<MeshFilter>().sharedMesh = upstreamMesh;
                var proxyRenderer = proxy.AddComponent<MeshRenderer>();
                long generation = LatticePreviewUtility.RegisterProxy(
                    originalRenderer,
                    proxyRenderer,
                    upstreamMesh,
                    out var restorationMesh);
                Assert.That(restorationMesh, Is.SameAs(upstreamMesh));

                LatticeDeformerPreviewFilter.AssignRendererMesh(proxyRenderer, previewMesh);
                Assert.That(LatticeDeformerPreviewFilter.GetRendererMesh(proxyRenderer), Is.SameAs(previewMesh));

                LatticeDeformerPreviewFilter.RestoreProxyMesh(
                    originalRenderer,
                    proxyRenderer,
                    restorationMesh,
                    generation);

                Assert.That(LatticeDeformerPreviewFilter.GetRendererMesh(proxyRenderer), Is.SameAs(upstreamMesh));
                Assert.That(LatticePreviewUtility.TryGetPreviewProxy(originalRenderer, out _), Is.False);
            }
            finally
            {
                LatticePreviewUtility.ClearProxy(original.GetComponent<Renderer>());
                Object.DestroyImmediate(upstreamMesh);
                Object.DestroyImmediate(previewMesh);
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void LatticeDeformerPreviewFilter_StaleNodeCannotClearOrOverwriteReplacementProxy()
        {
            var original = new GameObject("proxy-generation-original");
            var proxy = new GameObject("proxy-generation-reused");
            var upstreamMesh = new Mesh();
            var oldPreviewMesh = new Mesh();
            var replacementPreviewMesh = new Mesh();
            try
            {
                var originalRenderer = original.AddComponent<MeshRenderer>();
                proxy.AddComponent<MeshFilter>().sharedMesh = upstreamMesh;
                var proxyRenderer = proxy.AddComponent<MeshRenderer>();

                long oldGeneration = LatticePreviewUtility.RegisterProxy(
                    originalRenderer,
                    proxyRenderer,
                    upstreamMesh,
                    out var oldRestorationMesh);
                LatticeDeformerPreviewFilter.AssignRendererMesh(proxyRenderer, oldPreviewMesh);

                long replacementGeneration = LatticePreviewUtility.RegisterProxy(
                    originalRenderer,
                    proxyRenderer,
                    oldPreviewMesh,
                    out var replacementRestorationMesh);
                LatticeDeformerPreviewFilter.AssignRendererMesh(proxyRenderer, replacementPreviewMesh);

                Assert.That(oldRestorationMesh, Is.SameAs(upstreamMesh));
                Assert.That(
                    replacementRestorationMesh,
                    Is.SameAs(upstreamMesh),
                    "A replacement node must inherit the original upstream mesh.");

                LatticeDeformerPreviewFilter.RestoreProxyMesh(
                    originalRenderer,
                    proxyRenderer,
                    oldRestorationMesh,
                    oldGeneration);

                Assert.That(
                    LatticeDeformerPreviewFilter.GetRendererMesh(proxyRenderer),
                    Is.SameAs(replacementPreviewMesh),
                    "A stale node must not perform teardown after losing registration ownership.");
                Assert.That(
                    LatticePreviewUtility.TryGetPreviewProxy(originalRenderer, out var currentProxy),
                    Is.True);
                Assert.That(currentProxy, Is.SameAs(proxyRenderer));

                LatticeDeformerPreviewFilter.RestoreProxyMesh(
                    originalRenderer,
                    proxyRenderer,
                    replacementRestorationMesh,
                    replacementGeneration);

                Assert.That(
                    LatticeDeformerPreviewFilter.GetRendererMesh(proxyRenderer),
                    Is.SameAs(upstreamMesh));
                Assert.That(LatticePreviewUtility.HasRegisteredProxy(originalRenderer), Is.False);
            }
            finally
            {
                LatticePreviewUtility.ClearProxy(original.GetComponent<Renderer>());
                Object.DestroyImmediate(upstreamMesh);
                Object.DestroyImmediate(oldPreviewMesh);
                Object.DestroyImmediate(replacementPreviewMesh);
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void LatticeDeformerPreviewFilter_StaleNodeFrameCannotOverwriteReplacementProxy()
        {
            var original = new GameObject("proxy-frame-generation-original");
            var proxy = new GameObject("proxy-frame-generation-reused");
            var upstreamMesh = new Mesh();
            var oldPreviewMesh = new Mesh();
            var replacementPreviewMesh = new Mesh();
            IRenderFilterNode oldNode = null;
            IRenderFilterNode replacementNode = null;
            try
            {
                var originalRenderer = original.AddComponent<MeshRenderer>();
                proxy.AddComponent<MeshFilter>().sharedMesh = upstreamMesh;
                var proxyRenderer = proxy.AddComponent<MeshRenderer>();
                var pairs = new List<(Renderer original, Renderer proxy)>
                {
                    (originalRenderer, proxyRenderer)
                };

                oldNode = CreateLatticePreviewNode(pairs, oldPreviewMesh);
                Assert.That(
                    LatticeDeformerPreviewFilter.GetRendererMesh(proxyRenderer),
                    Is.SameAs(oldPreviewMesh));

                replacementNode = CreateLatticePreviewNode(pairs, replacementPreviewMesh);
                Assert.That(
                    LatticeDeformerPreviewFilter.GetRendererMesh(proxyRenderer),
                    Is.SameAs(replacementPreviewMesh));

                oldNode.OnFrame(originalRenderer, proxyRenderer);

                Assert.That(
                    LatticeDeformerPreviewFilter.GetRendererMesh(proxyRenderer),
                    Is.SameAs(replacementPreviewMesh),
                    "An invalidated node may still receive a frame while its replacement is being installed.");

                oldNode.Dispose();
                oldNode = null;
                Assert.That(
                    LatticeDeformerPreviewFilter.GetRendererMesh(proxyRenderer),
                    Is.SameAs(replacementPreviewMesh));

                replacementNode.Dispose();
                replacementNode = null;
                Assert.That(
                    LatticeDeformerPreviewFilter.GetRendererMesh(proxyRenderer),
                    Is.SameAs(upstreamMesh));
            }
            finally
            {
                oldNode?.Dispose();
                replacementNode?.Dispose();
                LatticePreviewUtility.ClearProxy(original.GetComponent<Renderer>());
                if (oldPreviewMesh != null) Object.DestroyImmediate(oldPreviewMesh);
                if (replacementPreviewMesh != null) Object.DestroyImmediate(replacementPreviewMesh);
                Object.DestroyImmediate(upstreamMesh);
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void LatticeDeformerPreviewFilter_RestoreProxyMesh_ClearsDestroyedOriginalAndProxy()
        {
            var original = new GameObject("destroyed-proxy-original");
            var proxy = new GameObject("destroyed-proxy-target");
            var originalRenderer = original.AddComponent<MeshRenderer>();
            var proxyRenderer = proxy.AddComponent<MeshRenderer>();
            try
            {
                long generation = LatticePreviewUtility.RegisterProxy(
                    originalRenderer,
                    proxyRenderer,
                    null,
                    out var restorationMesh);
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(proxy);

                Assert.DoesNotThrow(() =>
                    LatticeDeformerPreviewFilter.RestoreProxyMesh(
                        originalRenderer,
                        proxyRenderer,
                        restorationMesh,
                        generation));
                Assert.That(LatticePreviewUtility.HasRegisteredProxy(originalRenderer), Is.False);
                Assert.That(LatticePreviewUtility.TryGetPreviewProxy(originalRenderer, out _), Is.False);
            }
            finally
            {
                LatticePreviewUtility.ClearProxy(originalRenderer);
                if (original != null) Object.DestroyImmediate(original);
                if (proxy != null) Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void LatticePreviewUtility_RegisterClearAndLog_HandleNullAndDebugState()
        {
            bool previousDebug = LatticePreviewUtility.DebugAlignLogs;
            try
            {
                LatticePreviewUtility.RegisterProxy(null, null);
                LatticePreviewUtility.ClearProxy(null);

                LatticePreviewUtility.DebugAlignLogs = false;
                LatticePreviewUtility.LogAlign("test", "hidden");

                LatticePreviewUtility.DebugAlignLogs = true;
                LogAssert.Expect(LogType.Log, "[LatticeAlign] test: visible");
                LatticePreviewUtility.LogAlign("test", "visible");
            }
            finally
            {
                LatticePreviewUtility.DebugAlignLogs = previousDebug;
            }
        }

        [Test]
        public void LatticePreviewUtility_GetMeshLocalBounds_ReturnsMeshFilterBounds()
        {
            var go = new GameObject("mesh");
            var mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-1f, -2f, -3f),
                    new Vector3(3f, 2f, 1f)
                }
            };
            try
            {
                mesh.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();

                var bounds = LatticePreviewUtility.GetMeshLocalBounds(renderer);

                Assert.That(bounds.center, Is.EqualTo(new Vector3(1f, 0f, -1f)));
                Assert.That(bounds.size, Is.EqualTo(new Vector3(4f, 4f, 4f)));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LatticePreviewUtility_GetMeshLocalBounds_ReturnsSkinnedAndFallbackBounds()
        {
            var skinnedGo = new GameObject("skinned");
            var meshGo = new GameObject("mesh-renderer-no-filter");
            try
            {
                var skinned = skinnedGo.AddComponent<SkinnedMeshRenderer>();
                skinned.localBounds = new Bounds(Vector3.one, Vector3.one * 2f);
                var meshRenderer = meshGo.AddComponent<MeshRenderer>();

                Assert.That(LatticePreviewUtility.GetMeshLocalBounds(null).size, Is.EqualTo(Vector3.zero));
                Assert.That(LatticePreviewUtility.GetMeshLocalBounds(skinned).center, Is.EqualTo(Vector3.one));
                Assert.That(LatticePreviewUtility.GetMeshLocalBounds(meshRenderer).size, Is.EqualTo(meshRenderer.bounds.size));
            }
            finally
            {
                Object.DestroyImmediate(skinnedGo);
                Object.DestroyImmediate(meshGo);
            }
        }

        [Test]
        public void LatticePreviewUtility_GetRendererLocalBounds_DoesNotDoubleApplyNonUniformScale()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                go.transform.position = new Vector3(2f, 3f, 4f);
                go.transform.localScale = new Vector3(2f, 3f, 4f);
                var renderer = go.GetComponent<Renderer>();

                var meshBounds = LatticePreviewUtility.GetMeshLocalBounds(renderer);
                var bounds = LatticePreviewUtility.GetRendererLocalBounds(renderer);

                Assert.That(meshBounds.size.x, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(meshBounds.size.y, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(meshBounds.size.z, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(bounds.center.x, Is.EqualTo(0f).Within(1e-5f));
                Assert.That(bounds.center.y, Is.EqualTo(0f).Within(1e-5f));
                Assert.That(bounds.center.z, Is.EqualTo(0f).Within(1e-5f));
                Assert.That(bounds.size.x, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(bounds.size.y, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(bounds.size.z, Is.EqualTo(1f).Within(1e-5f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LatticePreviewUtility_GetRendererLocalBounds_ReturnsZeroForNullRenderer()
        {
            var bounds = LatticePreviewUtility.GetRendererLocalBounds(null);

            Assert.That(bounds.center, Is.EqualTo(Vector3.zero));
            Assert.That(bounds.size, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void LatticePreviewUtility_GetEditingBounds_FallsBackWithoutProxy()
        {
            var original = new GameObject("editing-bounds");
            bool previous = LatticePreviewUtility.UsePreviewAlignedCage;
            try
            {
                var deformer = original.AddComponent<LatticeDeformer>();
                var sourceBounds = new Bounds(Vector3.one, Vector3.one * 2f);

                LatticePreviewUtility.UsePreviewAlignedCage = false;
                Assert.That(
                    LatticePreviewUtility.GetEditingBounds(deformer, sourceBounds, original.transform),
                    Is.EqualTo(sourceBounds));

                LatticePreviewUtility.UsePreviewAlignedCage = true;
                Assert.That(
                    LatticePreviewUtility.GetEditingBounds(null, sourceBounds, original.transform),
                    Is.EqualTo(sourceBounds));
                Assert.That(
                    LatticePreviewUtility.GetEditingBounds(deformer, sourceBounds, original.transform),
                    Is.EqualTo(sourceBounds));

                original.AddComponent<MeshRenderer>();
                Assert.That(
                    LatticePreviewUtility.GetEditingBounds(deformer, sourceBounds, original.transform),
                    Is.EqualTo(sourceBounds));
            }
            finally
            {
                LatticePreviewUtility.UsePreviewAlignedCage = previous;
                Object.DestroyImmediate(original);
            }
        }

        [Test]
        public void LatticePreviewUtility_GetEditingBounds_FallsBackWhenEditingTransformIsNull()
        {
            var original = new GameObject("editing-bounds-original-null-transform");
            var proxy = new GameObject("editing-bounds-proxy-null-transform");
            bool previous = LatticePreviewUtility.UsePreviewAlignedCage;
            try
            {
                var originalRenderer = original.AddComponent<MeshRenderer>();
                var deformer = original.AddComponent<LatticeDeformer>();
                var proxyRenderer = proxy.AddComponent<MeshRenderer>();

                LatticePreviewUtility.UsePreviewAlignedCage = true;
                LatticePreviewUtility.RegisterProxy(originalRenderer, proxyRenderer);

                var sourceBounds = new Bounds(Vector3.zero, Vector3.one);
                var bounds = LatticePreviewUtility.GetEditingBounds(deformer, sourceBounds, null);

                Assert.That(bounds.size, Is.EqualTo(proxyRenderer.bounds.size));
            }
            finally
            {
                LatticePreviewUtility.UsePreviewAlignedCage = previous;
                LatticePreviewUtility.ClearProxy(original.GetComponent<Renderer>());
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void LatticePreviewUtility_GetEditingBounds_UsesSkinnedMeshLocalBounds()
        {
            var original = new GameObject("editing-bounds-skinned-original");
            var proxy = new GameObject("editing-bounds-skinned-proxy");
            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f) }
            };
            bool previous = LatticePreviewUtility.UsePreviewAlignedCage;
            try
            {
                mesh.RecalculateBounds();
                var originalRenderer = original.AddComponent<MeshRenderer>();
                var deformer = original.AddComponent<LatticeDeformer>();
                var proxyRenderer = proxy.AddComponent<SkinnedMeshRenderer>();
                proxyRenderer.sharedMesh = mesh;

                LatticePreviewUtility.UsePreviewAlignedCage = true;
                LatticePreviewUtility.RegisterProxy(originalRenderer, proxyRenderer);

                var bounds = LatticePreviewUtility.GetEditingBounds(
                    deformer,
                    new Bounds(Vector3.zero, Vector3.one),
                    proxy.transform);

                Assert.That(bounds.size.x, Is.EqualTo(4f).Within(1e-5f));
            }
            finally
            {
                LatticePreviewUtility.UsePreviewAlignedCage = previous;
                LatticePreviewUtility.ClearProxy(original.GetComponent<Renderer>());
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void LatticePreviewUtility_GetEditingBounds_UsesRegisteredProxyBounds()
        {
            var original = new GameObject("editing-bounds-original");
            var proxy = new GameObject("editing-bounds-proxy");
            var mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-1f, -2f, -3f),
                    new Vector3(3f, 2f, 1f)
                }
            };
            bool previous = LatticePreviewUtility.UsePreviewAlignedCage;
            try
            {
                var originalRenderer = original.AddComponent<MeshRenderer>();
                var deformer = original.AddComponent<LatticeDeformer>();
                mesh.RecalculateBounds();
                proxy.AddComponent<MeshFilter>().sharedMesh = mesh;
                var proxyRenderer = proxy.AddComponent<MeshRenderer>();
                proxy.transform.position = new Vector3(10f, 0f, 0f);

                LatticePreviewUtility.UsePreviewAlignedCage = true;
                LatticePreviewUtility.RegisterProxy(originalRenderer, proxyRenderer);

                Assert.That(LatticePreviewUtility.TryGetPreviewProxy(originalRenderer, out var resolved), Is.True);
                Assert.That(resolved, Is.SameAs(proxyRenderer));

                var sourceBounds = new Bounds(Vector3.zero, Vector3.one);
                var bounds = LatticePreviewUtility.GetEditingBounds(deformer, sourceBounds, proxy.transform);

                Assert.That(bounds.center.x, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(bounds.size, Is.EqualTo(new Vector3(4f, 4f, 4f)));
            }
            finally
            {
                LatticePreviewUtility.UsePreviewAlignedCage = previous;
                LatticePreviewUtility.ClearProxy(original.GetComponent<Renderer>());
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void LatticePreviewUtility_PrivateBoundsHelpers_TransformAndLocalizeBounds()
        {
            var target = new GameObject("bounds-target");
            try
            {
                target.transform.position = new Vector3(2f, 0f, 0f);
                target.transform.localScale = new Vector3(2f, 3f, 4f);

                var local = new Bounds(Vector3.zero, Vector3.one * 2f);
                var world = InvokePreviewPrivate<Bounds>(
                    "TransformMeshBounds",
                    local,
                    target.transform);

                Assert.That(world.center, Is.EqualTo(new Vector3(2f, 0f, 0f)));
                Assert.That(world.size, Is.EqualTo(new Vector3(4f, 6f, 8f)));

                var localized = InvokePreviewPrivate<Bounds>(
                    "ToLocalBounds",
                    target.transform,
                    world);
                Assert.That(localized.center.x, Is.EqualTo(0f).Within(1e-5f));
                Assert.That(localized.size.x, Is.EqualTo(2f).Within(1e-5f));

                Assert.That(
                    InvokePreviewPrivate<Bounds>("ToLocalBounds", null, world),
                    Is.EqualTo(world));
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }

        private static Mesh CreateBlendShapeMesh(int blendShapeCount)
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 }
            };

            for (int shape = 0; shape < blendShapeCount; shape++)
            {
                var deltas = new Vector3[mesh.vertexCount];
                deltas[shape % deltas.Length] = Vector3.forward * (shape + 1) * 0.1f;
                mesh.AddBlendShapeFrame(
                    $"Shape{shape}",
                    100f,
                    deltas,
                    new Vector3[mesh.vertexCount],
                    new Vector3[mesh.vertexCount]);
            }

            return mesh;
        }

        private sealed class FakePreviewSession
        {
            public readonly Dictionary<Renderer, Renderer> OriginalToProxyField;
            private readonly Dictionary<Renderer, Renderer> _methodMap;

            public FakePreviewSession(Renderer original, Renderer fieldProxy, Renderer propertyProxy, Renderer methodProxy)
            {
                OriginalToProxyField = new Dictionary<Renderer, Renderer>
                {
                    [original] = fieldProxy
                };
                OriginalToProxyProperty = new Dictionary<Renderer, Renderer>
                {
                    [original] = propertyProxy
                };
                _methodMap = new Dictionary<Renderer, Renderer>
                {
                    [original] = methodProxy
                };
            }

            public Dictionary<Renderer, Renderer> OriginalToProxyProperty { get; }

            public Dictionary<Renderer, Renderer> GetOriginalToProxyMap()
            {
                return _methodMap;
            }
        }

        private sealed class ExactProxyLookupSession
        {
            private readonly Renderer _proxy;

            public ExactProxyLookupSession(Renderer proxy)
            {
                _proxy = proxy;
            }

            public Renderer GetProxyRenderer(Renderer original)
            {
                return _proxy;
            }
        }

        private sealed class FallbackProxyLookupSession
        {
            private readonly Renderer _proxy;

            public FallbackProxyLookupSession(Renderer proxy)
            {
                _proxy = proxy;
            }

            public Renderer ResolveProxy(Renderer original)
            {
                return _proxy;
            }
        }

        private sealed class ThrowingProxyLookupSession
        {
            public Renderer GetProxyRenderer(Renderer original)
            {
                throw new TargetInvocationException(new System.Exception("boom"));
            }
        }

        private sealed class EdgePreviewSession
        {
            private readonly Renderer _original;
            private readonly Renderer _proxy;

            public EdgePreviewSession(Renderer original, Renderer proxy)
            {
                _original = original;
                _proxy = proxy;
            }

            public Dictionary<Renderer, Renderer> OriginalToProxyNull => null;

            public Dictionary<Renderer, Renderer> OriginalToProxyThrowing =>
                throw new TargetInvocationException(new System.Exception("property failed"));

            public Dictionary<Renderer, Renderer> NotAProxyMap => new();

            public Dictionary<Renderer, Renderer> GetOriginalToProxyThrowing()
            {
                throw new TargetInvocationException(new System.Exception("method failed"));
            }

            public Dictionary<Renderer, Renderer> GetOriginalToProxyWithParameter(Renderer renderer)
            {
                return new Dictionary<Renderer, Renderer> { [renderer] = _proxy };
            }

            public Dictionary<Renderer, Renderer> GetOriginalToProxyValid()
            {
                return new Dictionary<Renderer, Renderer> { [_original] = _proxy };
            }
        }

        private sealed class NoProxyMapMethods
        {
            public string GetSomethingElse()
            {
                return "";
            }
        }

        private sealed class SameGameObjectProxySession
        {
            private readonly Renderer _original;
            private readonly Renderer _proxy;

            public SameGameObjectProxySession(Renderer original, Renderer proxy)
            {
                _original = original;
                _proxy = proxy;
            }

            public object[] OriginalToProxyPairs => new object[]
            {
                new KeyValuePair<Renderer, Renderer>(null, _proxy),
                new KeyValuePair<GameObject, Renderer>(_original.gameObject, _proxy)
            };
        }

        private static T InvokePreviewPrivate<T>(string methodName, params object[] args)
        {
            var method = typeof(LatticePreviewUtility).GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (T)method.Invoke(null, args);
        }

        private static IRenderFilterNode CreateLatticePreviewNode(
            IEnumerable<(Renderer original, Renderer proxy)> proxyPairs,
            Mesh previewMesh)
        {
            var nodeType = typeof(LatticeDeformerPreviewFilter).GetNestedType(
                "PreviewNode",
                BindingFlags.NonPublic);
            Assert.That(nodeType, Is.Not.Null);
            var constructor = nodeType
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single();
            return (IRenderFilterNode)constructor.Invoke(
                new object[] { null, proxyPairs, previewMesh });
        }
    }
}
#endif
