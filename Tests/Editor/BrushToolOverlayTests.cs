#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Net._32Ba.LatticeDeformationTool.Editor;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class BrushToolOverlayTests
    {
        private OverlayWindow _window;
        private GameObject _root;
        private Mesh _mesh;
        private Action _restoreOptions;

        // An unexpected native log can stop a UnityTest iterator without running
        // its finally block. Detach the callback before restoring shared state so
        // this window cannot repaint during a later test.
        [TearDown]
        public void CleanupPainting()
        {
            if (_window != null)
            {
                _window.Draw = null;
                _window.Close();
                _window = null;
            }
            _restoreOptions?.Invoke();
            _restoreOptions = null;
            if (_root != null)
            {
                var owner = _root.GetComponent<LatticeDeformer>();
                if (owner != null) { owner.InvalidateCache(); owner.RestoreOriginalMesh(); }
                Object.DestroyImmediate(_root);
                _root = null;
            }
            if (_mesh != null) { Object.DestroyImmediate(_mesh); _mesh = null; }
        }

        private sealed class OverlayWindow : EditorWindow
        {
            internal Action Draw;
            internal int Repaints;
            private void OnGUI()
            {
                Draw?.Invoke();
                if (Event.current.type == EventType.Repaint) Repaints++;
            }
        }

        [UnityTest]
        [Category("LocalizedToolOverlay")]
        public IEnumerator AllToolLanguagesAndModes_DrawWithoutChangingPayload()
        {
            var oldLanguage = LatticeLocalization.CurrentLanguage;
            var oldMode = BrushToolHandler.CurrentBrushMode;
            var oldTransform = VertexSelectionHandler.CurrentTransformMode;
            var oldMirror = LatticeToolHandler.CurrentMirrorBehavior;
            var fields = new[] { typeof(BrushToolOverlay), typeof(VertexToolOverlay), typeof(LatticeToolOverlay) }
                .SelectMany(t => t.GetFields(BindingFlags.NonPublic | BindingFlags.Static))
                .Where(f => f.FieldType == typeof(bool)).ToArray();
            var values = new object[fields.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                values[i] = fields[i].GetValue(null);
                fields[i].SetValue(null, true);
            }
            _restoreOptions = () =>
            {
                for (int i = 0; i < fields.Length; i++) fields[i].SetValue(null, values[i]);
                BrushToolHandler.CurrentBrushMode = oldMode;
                VertexSelectionHandler.CurrentTransformMode = oldTransform;
                LatticeToolHandler.CurrentMirrorBehavior = oldMirror;
                LatticeLocalization.CurrentLanguage = oldLanguage;
            };
            var root = _root = new GameObject("Brush overlay read-only fixture");
            root.SetActive(false);
            var mesh = _mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 } };
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>();
            var owner = root.AddComponent<LatticeDeformer>();
            owner.Reset(); owner.AddLayer("Brush", MeshDeformerLayerType.Brush);
            var window = _window = ScriptableObject.CreateInstance<OverlayWindow>();
            try
            {
                window.position = new Rect(0, 0, 420, 900);
                window.Draw = () => BrushToolHandler.DrawOverlayGUI(owner);
                window.Show();
                foreach (int tool in new[] { 0, 1, 2 })
                {
                    owner.ActiveLayerIndex = tool == 2 ? 0 : 1;
                    string before = EditorJsonUtility.ToJson(owner);
                    int dirty = EditorUtility.GetDirtyCount(owner);
                    window.Draw = () =>
                    {
                        if (tool == 2) LatticeToolHandler.DrawOverlayGUI(owner);
                        else if (tool == 1) VertexSelectionHandler.DrawOverlayGUI(owner);
                        else BrushToolHandler.DrawOverlayGUI(owner);
                    };
                    foreach (LatticeLocalization.Language language in Enum.GetValues(typeof(LatticeLocalization.Language)))
                    {
                        LatticeLocalization.CurrentLanguage = language;
                        int modes = tool == 0 && LatticeDeformationFeatureFlags.VertexMaskEditing ? 4 : 3;
                        for (int mode = 0; mode < modes; mode++)
                        {
                            if (tool == 2) LatticeToolHandler.CurrentMirrorBehavior = (LatticeToolHandler.MirrorBehavior)mode;
                            else if (tool == 1) VertexSelectionHandler.CurrentTransformMode = (VertexSelectionHandler.TransformMode)mode;
                            else BrushToolHandler.CurrentBrushMode = (BrushToolHandler.BrushMode)mode;
                            int repaint = window.Repaints;
                            for (int frame = 0; frame < 60 && window.Repaints == repaint; frame++)
                            {
                                window.Repaint();
                                yield return null;
                            }
                            Assert.That(window.Repaints, Is.GreaterThan(repaint), language + "/" + mode);
                            Assert.That(EditorJsonUtility.ToJson(owner), Is.EqualTo(before));
                            Assert.That(EditorUtility.GetDirtyCount(owner), Is.EqualTo(dirty));
                        }
                    }
                }
            }
            finally { CleanupPainting(); }
        }
    }
}
#endif
