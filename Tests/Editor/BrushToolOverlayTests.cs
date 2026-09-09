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
        public IEnumerator BrushAndVertexLanguagesAndModes_DrawWithoutChangingPayload()
        {
            var oldLanguage = LatticeLocalization.CurrentLanguage;
            var oldMode = BrushToolHandler.CurrentBrushMode;
            var oldTransform = VertexSelectionHandler.CurrentTransformMode;
            var fields = new[] { typeof(BrushToolOverlay), typeof(VertexToolOverlay) }
                .SelectMany(t => t.GetFields(BindingFlags.NonPublic | BindingFlags.Static))
                .Where(f => f.FieldType == typeof(bool)).ToArray();
            var values = new object[fields.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                values[i] = fields[i].GetValue(null);
                fields[i].SetValue(null, true);
            }
            var root = new GameObject("Brush overlay read-only fixture");
            root.SetActive(false);
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 } };
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>();
            var owner = root.AddComponent<LatticeDeformer>();
            owner.Reset(); owner.AddLayer("Brush", MeshDeformerLayerType.Brush);
            var window = ScriptableObject.CreateInstance<OverlayWindow>();
            try
            {
                window.position = new Rect(0, 0, 420, 900);
                window.Draw = () => BrushToolHandler.DrawOverlayGUI(owner);
                window.Show();
                string before = EditorJsonUtility.ToJson(owner);
                int dirty = EditorUtility.GetDirtyCount(owner);
                foreach (bool vertex in new[] { false, true })
                {
                    window.Draw = () => { if (vertex) VertexSelectionHandler.DrawOverlayGUI(owner);
                        else BrushToolHandler.DrawOverlayGUI(owner); };
                    foreach (LatticeLocalization.Language language in Enum.GetValues(typeof(LatticeLocalization.Language)))
                    {
                        LatticeLocalization.CurrentLanguage = language;
                        int modes = !vertex && LatticeDeformationFeatureFlags.VertexMaskEditing ? 4 : 3;
                        for (int mode = 0; mode < modes; mode++)
                        {
                            if (vertex) VertexSelectionHandler.CurrentTransformMode = (VertexSelectionHandler.TransformMode)mode;
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
            finally
            {
                window.Draw = null; window.Close();
                for (int i = 0; i < fields.Length; i++) fields[i].SetValue(null, values[i]);
                BrushToolHandler.CurrentBrushMode = oldMode;
                VertexSelectionHandler.CurrentTransformMode = oldTransform;
                LatticeLocalization.CurrentLanguage = oldLanguage;
                owner.InvalidateCache(); owner.RestoreOriginalMesh();
                Object.DestroyImmediate(root); Object.DestroyImmediate(mesh);
            }
        }
    }
}
#endif
