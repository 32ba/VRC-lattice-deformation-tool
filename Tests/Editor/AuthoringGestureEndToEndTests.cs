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
    /// <summary>Scene View mouse events drive the active EditorTool; assertions read the real final NDMF proxy.</summary>
    public sealed class AuthoringGestureEndToEndTests
    {
        private const string PreviewMenu = "Tools/NDM Framework/Enable Previews";
        private readonly Dictionary<PropertyInfo, object> _settings = new();
        private Object[] _selection;
        private Type _tool;
        private MeshDeformerTool.BrushSubMode _subMode;
        private bool _previewEnabled, _filterEnabled, _createdView;
        private int _disableDepth;
        private int _undoGroup = -1;
        private SceneView _view;
        private Vector3 _pivot;
        private Quaternion _rotation;
        private float _size;
        private bool _orthographic, _in2DMode;
        private GameObject _owner;
        private Mesh _source;
        private Material _material;
        private Renderer _renderer;
        private LatticeDeformer _deformer;
        private Vector2[] _inputPoints;
        private Vector3 _handlePivot;
        private Vector2 _pickPoint, _handlePoint;
        private PivotRotation _pivotRotation;

        [UnityTest]
        [Category("InteractionE2E")]
        public IEnumerator BrushStrokeAcrossFrames_OneUndoAndRedoRestoreAllDataAndVisibleVertices()
        {
            yield return PrepareBrushEdit();
            TryReadProxy(out var before);
            var points = (Vector2[])_inputPoints.Clone();
            SendMouse(EventType.MouseMove, points[0], Vector2.zero);
            SendMouse(EventType.MouseDown, points[0], Vector2.zero);
            yield return null;
            SendMouse(EventType.MouseDrag, points[1], points[1] - points[0]);
            yield return null;
            SendMouse(EventType.MouseDrag, points[2], points[2] - points[1]);
            yield return null;
            SendMouse(EventType.MouseUp, points[2], Vector2.zero);

            Vector3[] edited = _deformer.Deform(false).vertices;
            Assert.That((edited[19] - before[19]).sqrMagnitude, Is.GreaterThan(1e-10f), "MouseDown missed its vertex.");
            Assert.That((edited[25] - before[25]).sqrMagnitude, Is.GreaterThan(1e-10f), "The last drag must reach a different vertex.");
            yield return WaitFor(() => MatchesProxy(edited), "The final proxy did not display the complete stroke.");
            int changed = _deformer.Displacements.Count(v => v.sqrMagnitude > 1e-10f);
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            int remaining = _deformer.Displacements.Count(v => v.sqrMagnitude > 1e-10f);
            Debug.Log($"Authoring gesture: {changed} changed vertices; {remaining} remaining after one Undo.");
            Assert.That(remaining, Is.Zero, "A single Undo must include vertices first touched in later Editor frames.");
            yield return WaitFor(() => MatchesProxy(before), "Undo did not restore the visible proxy.");
            Undo.PerformRedo();
            yield return WaitFor(() => MatchesProxy(edited), "Redo did not restore the complete visible stroke.");
            Assert.That(_source.vertices, Is.EqualTo(CreateVertices()), "Authoring changed the source mesh.");
        }

        [UnityTest]
        [Category("InteractionE2E")]
        public IEnumerator BrushStroke_AcrossActualProxyReplacement_PreservesLaterInputAndUndo()
        {
            yield return PrepareBrushEdit();
            Assert.That(TryReadProxy(out var before), Is.True);
            Assert.That(NDMFPreviewProxyUtility.TryGetProxyRenderer(_renderer, out var firstProxy), Is.True);
            int firstProxyId = firstProxy.GetInstanceID();
            var points = (Vector2[])_inputPoints.Clone();
            SendMouse(EventType.MouseDown, points[0], Vector2.zero);
            yield return null;
            SendMouse(EventType.MouseDrag, points[1], points[1] - points[0]);
            yield return null;
            var beforeReplacement = (Vector3[])_deformer.Displacements.Clone();
            Assert.That(beforeReplacement.Any(v => v.sqrMagnitude > 1e-10f), Is.True);

            PreviewSession.Current.ForceRebuild();
            yield return WaitFor(() =>
                NDMFPreviewProxyUtility.TryGetProxyRenderer(_renderer, out var current) &&
                current != null && current.GetInstanceID() != firstProxyId && TryReadProxy(out _),
                "NDMF did not replace the actual proxy while the stroke was held.");
            Assert.That(_deformer.Displacements, Is.EqualTo(beforeReplacement),
                "Replacing the proxy changed the stored stroke payload.");

            SendMouse(EventType.MouseDrag, points[2], points[2] - points[1]);
            yield return null;
            SendMouse(EventType.MouseUp, points[2], Vector2.zero);
            Assert.That((_deformer.Displacements[25] - beforeReplacement[25]).sqrMagnitude,
                Is.GreaterThan(1e-10f), "Input after replacement did not reach its vertex.");
            var edited = _deformer.Deform(false).vertices;
            yield return WaitFor(() => MatchesProxy(edited), "The replacement proxy lost the completed stroke.");
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.That(_deformer.Displacements, Is.All.EqualTo(Vector3.zero));
            yield return WaitFor(() => MatchesProxy(before), "Undo did not restore the replacement proxy.");
            Undo.PerformRedo();
            yield return WaitFor(() => MatchesProxy(edited), "Redo did not restore the complete stroke.");
            Assert.That(_source.vertices, Is.EqualTo(CreateVertices()));
        }

        [UnityTest]
        [Category("InteractionE2E")]
        public IEnumerator BrushEscape_CancelsAllFramesAndRestoresTheVisibleProxy()
        {
            yield return PrepareBrushEdit();
            TryReadProxy(out var before);
            var points = (Vector2[])_inputPoints.Clone();
            SendMouse(EventType.MouseDown, points[0], Vector2.zero);
            yield return null;
            SendMouse(EventType.MouseDrag, points[1], points[1] - points[0]);
            yield return null;
            Assert.That(_deformer.Displacements.Count(v => v.sqrMagnitude > 1e-10f), Is.GreaterThan(5));
            _view.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape });
            SendMouse(EventType.MouseDrag, points[2], points[2] - points[1]);
            SendMouse(EventType.MouseUp, points[2], Vector2.zero);
            Assert.That(_deformer.Displacements, Is.All.EqualTo(Vector3.zero));
            yield return WaitFor(() => MatchesProxy(before), "Escape did not restore the visible proxy.");
        }

        [UnityTest]
        [Category("InteractionE2E")]
        public IEnumerator BrushEscape_AfterAnotherUndoOperationPreservesBothEdits()
        {
            yield return PrepareBrushEdit();
            var points = (Vector2[])_inputPoints.Clone();
            SendMouse(EventType.MouseDown, points[0], Vector2.zero);
            yield return null;
            var payload = (Vector3[])_deformer.Displacements.Clone();
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(_owner.transform, "Intervening move");
            _owner.transform.localPosition = Vector3.up * 0.01f;
            _view.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape });
            SendMouse(EventType.MouseUp, points[0], Vector2.zero);
            Assert.That(_deformer.Displacements, Is.EqualTo(payload));
            Assert.That(_owner.transform.localPosition, Is.EqualTo(Vector3.up * 0.01f));
            Undo.PerformUndo();
            Assert.That(_owner.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(_deformer.Displacements, Is.EqualTo(payload));
            Undo.PerformUndo();
            Assert.That(_deformer.Displacements, Is.All.EqualTo(Vector3.zero));
        }

        [UnityTest]
        [Category("InteractionE2E")]
        public IEnumerator BrushLayerSwitch_DoesNotContinueTheStrokeIntoAnotherLayer()
        {
            yield return PrepareBrushEdit();
            int originalIndex = _deformer.ActiveLayerIndex;
            int otherIndex = _deformer.AddLayer("Other brush", MeshDeformerLayerType.Brush);
            _deformer.ActiveLayerIndex = originalIndex;
            var points = (Vector2[])_inputPoints.Clone();
            var firstLayer = _deformer.ActiveGroup.Layers[_deformer.ActiveLayerIndex];
            SendMouse(EventType.MouseDown, points[0], Vector2.zero);
            yield return null;
            Assert.That(firstLayer.BrushDisplacements.Any(v => v.sqrMagnitude > 1e-10f), Is.True);
            var firstPayload = (Vector3[])firstLayer.BrushDisplacements.Clone();
            _deformer.ActiveLayerIndex = otherIndex;
            SendMouse(EventType.MouseDrag, points[2], points[2] - points[0]);
            yield return null;
            SendMouse(EventType.MouseUp, points[2], Vector2.zero);
            Assert.That(_deformer.Displacements, Is.All.EqualTo(Vector3.zero));
            Assert.That(firstLayer.BrushDisplacements, Is.EqualTo(firstPayload));
            Vector3[] edited = _deformer.Deform(false).vertices;
            yield return WaitFor(() => MatchesProxy(edited), "Layer switching left a stale visible proxy.");
        }

        [UnityTest]
        [Category("InteractionE2E")]
        public IEnumerator VertexHandleAcrossFrames_OneUndoAndRedoRestoreDataAndProxy() => HandleGesture(false);

        [UnityTest]
        [Category("InteractionE2E")]
        public IEnumerator LatticeHandleAcrossFrames_OneUndoAndRedoRestoreDataAndProxy() => HandleGesture(true);

        private IEnumerator HandleGesture(bool lattice)
        {
            yield return PrepareBrushEdit();
            if (lattice)
            {
                _deformer.ActiveLayerIndex = 0;
                var settings = _deformer.EditingSettings;
                settings.GridSize = new Vector3Int(2, 2, 2);
                settings.LocalBounds = new Bounds(Vector3.zero, new Vector3(1, 0.5f, 0.25f));
                settings.ResetControlPoints();
                LatticeToolHandler.MirrorEditing = false;
                LatticeToolHandler.IncludeInteriorControls = false;
                _handlePivot = settings.GetControlPointLocal(0);
            }
            else
            {
                MeshDeformerTool.CurrentBrushSubMode = MeshDeformerTool.BrushSubMode.VertexSelection;
                VertexSelectionHandler.CurrentTransformMode = VertexSelectionHandler.TransformMode.Move;
                VertexSelectionHandler.CurrentHandleOrientation = VertexSelectionHandler.HandleOrientation.Global;
                VertexSelectionHandler.ProportionalRadius = 0;
                _handlePivot = Vector3.zero;
            }
            Tools.pivotRotation = PivotRotation.Local;
            _deformer.InvalidateCache();
            LatticePreviewUtility.RefreshInteractiveDeformation(_deformer);
            _view.Repaint();
            yield return null;
            yield return null;
            var before = _deformer.Deform(false).vertices;
            var controlsBefore = lattice ? _deformer.EditingSettings.ControlPointsLocal.ToArray() : null;
            SendMouse(EventType.MouseMove, _pickPoint, Vector2.zero);
            yield return null;
            SendMouse(EventType.MouseDown, _pickPoint, Vector2.zero);
            SendMouse(EventType.MouseUp, _pickPoint, Vector2.zero);
            yield return null;
            if (!lattice) Assert.That(VertexSelectionHandler.SelectedVertexCount, Is.EqualTo(1), "Click did not select one vertex.");
            var start = _handlePoint;
            SendMouse(EventType.MouseMove, start, Vector2.zero);
            yield return null;
            SendMouse(EventType.MouseDown, start, Vector2.zero);
            yield return null;
            SendMouse(EventType.MouseDrag, start + new Vector2(12, 0), new Vector2(12, 0));
            yield return null;
            var firstFrame = _deformer.Deform(false).vertices;
            Assert.That(firstFrame.Zip(before, (a, b) => (a - b).sqrMagnitude).Max(), Is.GreaterThan(1e-8f), "The native handle did not move the mesh.");
            SendMouse(EventType.MouseDrag, start + new Vector2(24, 0), new Vector2(12, 0));
            yield return null;
            SendMouse(EventType.MouseUp, start + new Vector2(24, 0), Vector2.zero);
            var edited = _deformer.Deform(false).vertices;
            Assert.That(edited.Zip(firstFrame, (a, b) => (a - b).sqrMagnitude).Max(), Is.GreaterThan(1e-8f), "The second input frame did not add a change.");
            var controlsEdited = lattice ? _deformer.EditingSettings.ControlPointsLocal.ToArray() : null;
            yield return WaitFor(() => MatchesProxy(edited), "Handle output did not reach the final proxy.");
            Undo.PerformUndo();
            if (lattice) Assert.That(_deformer.EditingSettings.ControlPointsLocal.ToArray(), Is.EqualTo(controlsBefore));
            else Assert.That(_deformer.Displacements, Is.All.EqualTo(Vector3.zero));
            yield return WaitFor(() => MatchesProxy(before), "Handle Undo did not restore the final proxy.");
            Undo.PerformRedo();
            if (lattice) Assert.That(_deformer.EditingSettings.ControlPointsLocal.ToArray(), Is.EqualTo(controlsEdited));
            yield return WaitFor(() => MatchesProxy(edited), "Handle Redo did not restore the final proxy.");
            Assert.That(_source.vertices, Is.EqualTo(CreateVertices()));
        }

        private IEnumerator PrepareBrushEdit()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Scene View input and a real NDMF proxy require an initialized graphics Editor.");

            CaptureEditorState();
            CreateFixture();
            ConfigureBrush();
            NDMFPreview.DisablePreviewDepth = 0;
            if (!_previewEnabled) Assert.That(EditorApplication.ExecuteMenuItem(PreviewMenu), Is.True);
            yield return WaitFor(() => PreviewSession.Current != null, "NDMF did not start.");
            LatticeDeformerPreviewFilter.ForcePreviewState(true);
            Selection.activeGameObject = _owner;
            ActiveEditorTracker.sharedTracker.ForceRebuild();
            yield return null;
            ToolManager.SetActiveTool<MeshDeformerTool>();
            PreviewSession.Current.ForceRebuild();
            _view.Focus();
            _view.Repaint();
            yield return WaitFor(() => TryReadProxy(out _), "NDMF did not publish a genuine final proxy.");


            SceneView.beforeSceneGui += ProjectInput;
            yield return WaitFor(() => _inputPoints != null, "Scene View did not project the input path.");
        }

        private void CaptureEditorState()
        {
            _selection = Selection.objects;
            _tool = ToolManager.activeToolType;
            _subMode = MeshDeformerTool.CurrentBrushSubMode;
            _pivotRotation = Tools.pivotRotation;
            _previewEnabled = PreviewEnabled();
            _filterEnabled = LatticeDeformerPreviewFilter.PreviewToggleEnabled;
            _disableDepth = NDMFPreview.DisablePreviewDepth;
            foreach (var property in new[] { typeof(BrushToolHandler), typeof(VertexSelectionHandler), typeof(LatticeToolHandler) }
                         .SelectMany(t => t.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)))
                if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
                    _settings.Add(property, property.GetValue(null));
            _view = SceneView.sceneViews.Cast<SceneView>().FirstOrDefault(v => v != null);
            _createdView = _view == null;
            if (_view == null) _view = EditorWindow.GetWindow<SceneView>();
            _pivot = _view.pivot; _rotation = _view.rotation; _size = _view.size;
            _orthographic = _view.orthographic; _in2DMode = _view.in2DMode;
            _view.Show();
            _view.in2DMode = false; _view.pivot = Vector3.zero; _view.rotation = Quaternion.identity;
            _view.size = 2.2f; _view.orthographic = true;
            Undo.IncrementCurrentGroup();
            _undoGroup = Undo.GetCurrentGroup();
        }

        private static Vector3[] CreateVertices()
        {
            var vertices = new Vector3[45];
            for (int y = 0; y < 5; y++)
                for (int x = 0; x < 9; x++) vertices[y * 9 + x] = new Vector3((x - 4) * 0.125f, (y - 2) * 0.125f, 0);
            return vertices;
        }

        private void CreateFixture()
        {
            var indices = new List<int>();
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 8; x++)
                {
                    int n = y * 9 + x;
                    indices.AddRange(new[] { n, n + 9, n + 1, n + 1, n + 9, n + 10 });
                }
            _source = new Mesh { name = "authoring gesture source", vertices = CreateVertices(), triangles = indices.ToArray() };
            _source.RecalculateBounds(); _source.RecalculateNormals();
            Shader shader = Shader.Find("Standard");
            Assert.That(shader != null && shader.isSupported, Is.True, "A healthy graphics Editor is required.");
            _material = new Material(shader);
            _owner = new GameObject("authoring gesture fixture");
            _owner.AddComponent<MeshFilter>().sharedMesh = _source;
            _renderer = _owner.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            _deformer = _owner.AddComponent<LatticeDeformer>();
            _deformer.Reset();
            _deformer.ActiveLayerIndex = _deformer.AddLayer("Gesture brush", MeshDeformerLayerType.Brush);
            _deformer.EnsureDisplacementCapacity();
            _deformer.Deform(false);
        }

        private static void ConfigureBrush()
        {
            BrushToolHandler.BrushRadius = 0.16f; BrushToolHandler.BrushStrength = 0.8f;
            BrushToolHandler.CurrentBrushMode = BrushToolHandler.BrushMode.Normal;
            BrushToolHandler.BrushFalloff = BrushFalloffType.Constant;
            BrushToolHandler.BackfaceCulling = false; BrushToolHandler.MirrorEditing = false;
            BrushToolHandler.InvertBrush = false; BrushToolHandler.ConnectedOnly = false;
            BrushToolHandler.UseSurfaceDistance = false; BrushToolHandler.ShowAffectedVertices = false;
            BrushToolHandler.ShowDisplacementHeatmap = false; BrushToolHandler.ShowPenetration = false;
            MeshDeformerTool.CurrentBrushSubMode = MeshDeformerTool.BrushSubMode.Brush;
        }

        private void ProjectInput(SceneView view)
        {
            if (view != _view || Event.current.type != EventType.Repaint) return;
            Matrix4x4 previous = Handles.matrix;
            try
            {
                Handles.matrix = Matrix4x4.identity;
                _inputPoints = new[] { -0.375f, 0f, 0.375f }.Select(x =>
                    GUIUtility.GUIToScreenPoint(HandleUtility.WorldToGUIPoint(new Vector3(x, 0, 0))) - view.position.position).ToArray();
                _pickPoint = GUIUtility.GUIToScreenPoint(HandleUtility.WorldToGUIPoint(_handlePivot)) - view.position.position;
                Vector3 handlePosition = _handlePivot + Vector3.right * (HandleUtility.GetHandleSize(_handlePivot) * 0.75f);
                _handlePoint = GUIUtility.GUIToScreenPoint(HandleUtility.WorldToGUIPoint(handlePosition)) - view.position.position;
            }
            finally { Handles.matrix = previous; }
        }

        private void SendMouse(EventType type, Vector2 point, Vector2 delta)
        {
            _view.SendEvent(new Event { type = type, button = 0, mousePosition = point, delta = delta, clickCount = 1 });
            _view.Repaint();
        }

        private bool TryReadProxy(out Vector3[] vertices)
        {
            vertices = null;
            if (_renderer == null || PreviewSession.Current == null ||
                !NDMFPreviewProxyUtility.TryGetProxyRenderer(_renderer, out var proxy) || proxy == null || proxy == _renderer ||
                NDMFPreview.GetOriginalObjectForProxy(proxy.gameObject) != _owner) return false;
            var mesh = LatticeDeformerPreviewFilter.GetRendererMesh(proxy);
            if (mesh == null || mesh == _source || mesh.vertexCount != _source.vertexCount) return false;
            vertices = mesh.vertices;
            return true;
        }

        private bool MatchesProxy(Vector3[] expected) => TryReadProxy(out var actual) && actual.Length == expected.Length &&
            actual.Zip(expected, (a, b) => (a - b).sqrMagnitude <= 1e-10f).All(equal => equal);

        private IEnumerator WaitFor(Func<bool> condition, string message)
        {
            double start = EditorApplication.timeSinceStartup;
            while (!condition())
            {
                Assert.That(EditorApplication.timeSinceStartup - start, Is.LessThan(5d), message);
                _view?.Repaint();
                yield return null;
            }
        }

        private static bool PreviewEnabled() => typeof(NDMFPreview).GetProperty("EnablePreviewsUI",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(null) is bool enabled && enabled;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_selection == null) yield break;
            SceneView.beforeSceneGui -= ProjectInput;
            Tools.current = Tool.Move;
            if (_owner != null) _owner.SetActive(false);
            PreviewSession.Current?.ForceRebuild();
            yield return null;
            yield return null;
            if (_undoGroup >= 0) Undo.RevertAllDownToGroup(_undoGroup);
            if (_owner != null) Object.DestroyImmediate(_owner);
            if (_source != null) Object.DestroyImmediate(_source);
            if (_material != null) Object.DestroyImmediate(_material);
            foreach (var item in _settings) item.Key.SetValue(null, item.Value);
            _settings.Clear();
            MeshDeformerTool.CurrentBrushSubMode = _subMode;
            Tools.pivotRotation = _pivotRotation;
            LatticeDeformerPreviewFilter.ForcePreviewState(_filterEnabled);
            NDMFPreview.DisablePreviewDepth = _disableDepth;
            if (PreviewEnabled() != _previewEnabled) EditorApplication.ExecuteMenuItem(PreviewMenu);
            Selection.objects = _selection;
            if (_tool != null && _tool != typeof(MeshDeformerTool)) ToolManager.SetActiveTool(_tool);
            if (_view != null)
            {
                _view.pivot = _pivot; _view.rotation = _rotation; _view.size = _size;
                _view.orthographic = _orthographic; _view.in2DMode = _in2DMode;
                if (_createdView) _view.Close(); else _view.Repaint();
            }
            _selection = null;
        }
    }
}
#endif
