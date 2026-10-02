#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    [Category("EditorWindowLifecycle")]
    public sealed class EditorWindowLifecycleTests
    {
        private readonly List<SceneView> _views = new();
        private LatticeLocalization.Language _language;

        [SetUp]
        public void SetUp() => _language = LatticeLocalization.CurrentLanguage;

        [TearDown]
        public void TearDown()
        {
            foreach (var view in _views) if (view != null) view.Close();
            _views.Clear();
            LatticeLocalization.CurrentLanguage = _language;
        }

        private SceneView OpenView()
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(UnityEngine.Rendering.GraphicsDeviceType.Null));
            var view = ScriptableObject.CreateInstance<SceneView>();
            _views.Add(view);
            view.Show();
            return view;
        }

        private static MeshDeformerToolOverlay Find(SceneView view)
        {
            Assert.That(view.TryGetOverlay("Mesh Deformer", out var overlay), Is.True,
                "Overlay identity must use its stable id, not its translated title.");
            Assert.That(overlay, Is.TypeOf<MeshDeformerToolOverlay>());
            return (MeshDeformerToolOverlay)overlay;
        }

        [UnityTest]
        public IEnumerator OverlayTitle_IsLocalizedOnCreationBeforeBodyPaint()
        {
            LatticeLocalization.CurrentLanguage = LatticeLocalization.Language.Japanese;
            var view = OpenView();
            yield return null;
            Assert.That(Find(view).displayName, Is.EqualTo(LatticeLocalization.Tr(LocKey.MeshDeformer)));
        }

        [UnityTest]
        public IEnumerator HiddenCollapsedOverlay_TracksAllLanguagesWithoutChangingIdentity()
        {
            var view = OpenView();
            yield return null;
            var overlay = Find(view);
            overlay.displayed = false;
            overlay.collapsed = true;
            foreach (LatticeLocalization.Language language in Enum.GetValues(typeof(LatticeLocalization.Language)))
            {
                LatticeLocalization.CurrentLanguage = language;
                Assert.That(overlay.displayName, Is.EqualTo(LatticeLocalization.Tr(LocKey.MeshDeformer)), language.ToString());
                Assert.That(Find(view), Is.SameAs(overlay));
            }
            overlay.displayed = true;
            overlay.collapsed = false;
            yield return null;
            Assert.That(Find(view), Is.SameAs(overlay));
        }

        [UnityTest]
        public IEnumerator TypedSceneViewLookup_DoesNotUseTitleAsIdentityOrRenameExistingWindow()
        {
            var view = OpenView();
            yield return null;
            view.titleContent = new GUIContent("Lifecycle lookup probe");
            var titles = Resources.FindObjectsOfTypeAll<SceneView>().ToDictionary(v => v, v => v.titleContent.text);
            var found = EditorWindow.GetWindow<SceneView>("This is not a lookup key", false);
            Assert.That(titles.ContainsKey(found), Is.True, "Typed lookup must reuse an existing SceneView.");
            Assert.That(found.titleContent.text, Is.EqualTo(titles[found]));
            Assert.That(Resources.FindObjectsOfTypeAll<SceneView>().Length, Is.EqualTo(titles.Count));
            Assert.That(view.titleContent.text, Is.EqualTo("Lifecycle lookup probe"));
            Assert.That(Find(view), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator ClosingAndReopeningSceneView_RecreatesLocalizedOverlayWithoutLeakingSubscription()
        {
            LatticeLocalization.CurrentLanguage = LatticeLocalization.Language.English;
            var view = OpenView();
            yield return null;
            var old = Find(view);
            view.titleContent = new GUIContent("Renamed Scene View");
            view.Show();
            Assert.That(view.titleContent.text, Is.EqualTo("Renamed Scene View"));
            Assert.That(Find(view), Is.SameAs(old));
            view.Close();
            yield return null;
            var field = typeof(LatticeLocalization).GetField("LanguageChanged", BindingFlags.Static | BindingFlags.NonPublic);
            var subscribers = ((Delegate)field.GetValue(null))?.GetInvocationList() ?? Array.Empty<Delegate>();
            Assert.That(subscribers.Any(d => ReferenceEquals(d.Target, old)), Is.False);
            LatticeLocalization.CurrentLanguage = LatticeLocalization.Language.ChineseTraditional;
            var reopened = OpenView();
            yield return null;
            Assert.That(Find(reopened), Is.Not.SameAs(old));
            Assert.That(Find(reopened).displayName, Is.EqualTo(LatticeLocalization.Tr(LocKey.MeshDeformer)));
        }

        [Test]
        public void ToolbarIcon_LocalizesOwnedContentWithoutMutatingUnitySharedIcon()
        {
            var shared = EditorGUIUtility.IconContent("EditCollider");
            string previousTooltip = shared.tooltip;
            const string otherConsumerTooltip = "Tooltip owned by another Unity icon consumer";
            shared.tooltip = otherConsumerTooltip;
            var tool = ScriptableObject.CreateInstance<MeshDeformerTool>();
            try
            {
                foreach (LatticeLocalization.Language language in Enum.GetValues(typeof(LatticeLocalization.Language)))
                {
                    LatticeLocalization.CurrentLanguage = language;
                    var actual = tool.toolbarIcon;
                    Assert.That(shared.tooltip, Is.EqualTo(otherConsumerTooltip));
                    Assert.That(actual, Is.Not.SameAs(shared));
                    Assert.That(actual.image, Is.SameAs(shared.image));
                    Assert.That(actual.tooltip, Is.EqualTo(LatticeLocalization.Tr(LocKey.MeshDeformer)));
                }
            }
            finally
            {
                shared.tooltip = previousTooltip;
                Object.DestroyImmediate(tool);
            }
        }
    }
}
#endif
