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
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    /// <summary>
    /// Actual Inspector mouse input and SceneView mouse input, observed through the
    /// final renderer of the real NDMF PreviewSession. No direct brush-handler calls.
    /// </summary>
    public sealed class InteractionE2ETests
    {
        private const string PreviewMenu = "Tools/NDM Framework/Enable Previews";
        private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly List<Fixture> _fixtures = new();
        private readonly Dictionary<PropertyInfo, object> _brushSettings = new();
        private readonly Dictionary<FieldInfo, object> _clipboardState = new();
        private Object[] _selection;
        private Object _activeSelection;
        private Type _toolType;
        private SceneView _view;
        private bool _createdView;
        private Vector3 _pivot;
        private Quaternion _rotation;
        private float _size;
        private bool _orthographic;
        private bool _in2DMode;
        private bool _previewEnabled;
        private bool _filterEnabled;
        private int _disableDepth;
        private MeshDeformerTool.BrushSubMode _subMode;
        private InteractionInspectorWindow _inspector;
        private InteractionE2EReport _report;
        private int _updateSerial;
        private Vector3[] _projectionWorld = Array.Empty<Vector3>();
        private Vector2[] _projectionGui;

        [SetUp]
        public void SetUp()
        {
            _selection = Selection.objects;
            _activeSelection = Selection.activeObject;
            _toolType = ToolManager.activeToolType;
            _previewEnabled = IsPreviewEnabled();
            _filterEnabled = LatticeDeformerPreviewFilter.PreviewToggleEnabled;
            _disableDepth = NDMFPreview.DisablePreviewDepth;
            _subMode = MeshDeformerTool.CurrentBrushSubMode;
            foreach (var property in typeof(BrushToolHandler).GetProperties(StaticMembers))
                if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
                    _brushSettings.Add(property, property.GetValue(null));
            foreach (var field in typeof(MeshDeformerClipboard).GetFields(StaticMembers))
                if (!field.IsLiteral && !field.IsInitOnly) _clipboardState.Add(field, field.GetValue(null));
            EditorApplication.update += CountUpdate;
            Application.logMessageReceived += ObserveLog;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try
            {
                if (_inspector != null) _inspector.Close();
                Tools.current = Tool.Move;
                // Remove fixture renderers from the live graph before destroying them.
                foreach (var fixture in _fixtures)
                    if (fixture.GameObject != null) fixture.GameObject.SetActive(false);
                PreviewSession.Current?.ForceRebuild();
                for (int i = 0; i < 5; i++) { SceneView.RepaintAll(); yield return null; }
                foreach (var fixture in _fixtures)
                {
                    if (fixture.Renderer != null) LatticePreviewUtility.ClearProxy(fixture.Renderer);
                    if (fixture.GameObject != null) Object.DestroyImmediate(fixture.GameObject);
                    if (fixture.Mesh != null) Object.DestroyImmediate(fixture.Mesh);
                    if (fixture.Material != null) Object.DestroyImmediate(fixture.Material);
                }
                _fixtures.Clear();
                PreviewSession.Current?.ForceRebuild();
                for (int i = 0; i < 3; i++) { SceneView.RepaintAll(); yield return null; }
            }
            finally
            {
                SceneView.beforeSceneGui -= ProjectInputPoints;
                foreach (var pair in _brushSettings) pair.Key.SetValue(null, pair.Value);
                _brushSettings.Clear();
                foreach (var pair in _clipboardState) pair.Key.SetValue(null, pair.Value);
                _clipboardState.Clear();
                MeshDeformerTool.CurrentBrushSubMode = _subMode;
                LatticeDeformerPreviewFilter.ForcePreviewState(_filterEnabled);
                NDMFPreview.DisablePreviewDepth = _disableDepth;
                if (IsPreviewEnabled() != _previewEnabled) EditorApplication.ExecuteMenuItem(PreviewMenu);
                if (_view != null)
                {
                    if (_createdView) _view.Close();
                    else
                    {
                        _view.in2DMode = _in2DMode;
                        _view.pivot = _pivot;
                        _view.rotation = _rotation;
                        _view.size = _size;
                        _view.orthographic = _orthographic;
                        _view.Repaint();
                    }
                }
                Selection.objects = _selection.Where(o => o != null).ToArray();
                Selection.activeObject = _activeSelection;
                if (_toolType != null) ToolManager.SetActiveTool(_toolType);
                EditorApplication.update -= CountUpdate;
                Application.logMessageReceived -= ObserveLog;
                if (_report != null)
                {
                    if (_report.observedErrors.Count != 0)
                    {
                        _report.succeeded = false;
                        _report.failureReason = string.Join("\n", _report.observedErrors);
                    }
                    _report.Save();
                }
                _report = null;
            }
        }

        [UnityTest]
        [Category("InteractionE2E")]
        public IEnumerator ScenarioA_InspectorStart_StrokeUndoRedoUsesVisibleMesh() =>
            Run("interaction-a-stroke-undo-redo", "inspector-mouse-and-scene-view-mouse", ScenarioA);

        [UnityTest]
        [Category("InteractionE2E")]
        public IEnumerator ScenarioB_TargetSwitch_ReactivatesHandlerAndKeepsEditsSeparate() =>
            Run("interaction-b-target-switch", "inspector-mouse-and-scene-view-mouse", ScenarioB);

        [UnityTest]
        [Category("InteractionE2E")]
        public IEnumerator ScenarioC_PasteRefusalIsExplainedAndBrushCanResume() =>
            Run("interaction-c-paste-rejection-resume", "clipboard-command-and-inspector-mouse-and-scene-view-mouse", ScenarioC);

        private IEnumerator Run(string id, string inputKind, Func<IEnumerator> scenario)
        {
            _report = InteractionE2EReport.Begin(id, inputKind);
            double started = EditorApplication.timeSinceStartup;
            var stack = new Stack<IEnumerator>();
            stack.Push(scenario());
            try
            {
                // Flatten nested iterators so failures in a wait/stroke are captured in
                // the report without placing a C# yield inside a try/catch block.
                while (stack.Count > 0)
                {
                    IEnumerator current = stack.Peek();
                    bool hasNext;
                    object value = null;
                    try { hasNext = current.MoveNext(); if (hasNext) value = current.Current; }
                    catch (Exception exception) { _report.failureReason = exception.ToString(); throw; }
                    if (!hasNext) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                    if (value is IEnumerator nested) { stack.Push(nested); continue; }
                    yield return value;
                }
                _report.succeeded = true;
                _report.failureReason = "";
            }
            finally
            {
                while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
                _report.scenarioElapsedMs = (EditorApplication.timeSinceStartup - started) * 1000;
                _report.Save();
            }
        }

        private IEnumerator ScenarioA()
        {
            yield return PrepareEnvironment();
            var fixture = CreateFixture("Interaction A", Vector3.zero);
            yield return StartPreview();
            yield return StartFromInspector(fixture);
            yield return WaitForProxy(fixture);
            Vector3[] before = ReadProxy(fixture);
            yield return Stroke(fixture, "first-stroke");
            Vector3[] edited = ReadProxy(fixture);
            _report.changedVertexCount = CountChanged(before, edited);
            Assert.That(_report.changedVertexCount, Is.GreaterThan(0));
            AssertSourcesUnchanged();

            Undo.FlushUndoRecordObjects();
            _report.undoRedoEvaluated = true;
            var measurement = BeginResponse("undo");
            Undo.PerformUndo();
            _report.operationCount++;
            Assert.That(fixture.Deformer.Layers[1].BrushDisplacements.Count(v => v.sqrMagnitude > 1e-10f),
                Is.Zero, "One Undo left displacement from later MouseDrag events in the serialized layer.");
            yield return WaitFor(() => MatchesProxy(fixture, before), "One Undo did not restore the complete stroke.");
            EndResponse(measurement);
            _report.undoMatch = true;
            measurement = BeginResponse("redo");
            Undo.PerformRedo();
            _report.operationCount++;
            yield return WaitFor(() => MatchesProxy(fixture, edited), "One Redo did not restore the visible stroke.");
            EndResponse(measurement);
            _report.redoMatch = true;
            AssertSourcesUnchanged();
        }

        private IEnumerator ScenarioB()
        {
            yield return PrepareEnvironment();
            var first = CreateFixture("Interaction B First", new Vector3(-0.8f, 0, 0));
            var second = CreateFixture("Interaction B Second", new Vector3(0.8f, 0, 0));
            yield return StartPreview();
            yield return StartFromInspector(first);
            yield return WaitForProxy(first);
            yield return WaitForProxy(second);
            Vector3[] secondBefore = ReadProxy(second);
            yield return Stroke(first, "first-target-stroke");
            Vector3[] firstEdited = ReadProxy(first);
            Assert.That(MatchesProxy(second, secondBefore), Is.True, "The unselected target changed.");

            Selection.activeGameObject = second.GameObject;
            _report.operationCount++;
            yield return null;
            yield return null;
            if (ToolManager.activeToolType != typeof(MeshDeformerTool)) yield return StartFromInspector(second);
            yield return Stroke(second, "second-target-stroke");
            Vector3[] secondEdited = ReadProxy(second);
            Assert.That(MatchesProxy(first, firstEdited), Is.True, "Switching target changed the first target.");

            Tools.current = Tool.Move;
            _report.operationCount++;
            yield return null;
            Assert.That(ToolManager.activeToolType, Is.Not.EqualTo(typeof(MeshDeformerTool)), "The editing tool did not stop.");
            yield return StartFromInspector(first);
            yield return Stroke(first, "return-and-resume-stroke");
            Assert.That(CountChanged(firstEdited, ReadProxy(first)), Is.GreaterThan(0), "The third stroke was a no-op.");
            Assert.That(MatchesProxy(second, secondEdited), Is.True, "Resuming on the first target changed the second target.");
            _report.changedVertexCount = CountChanged(first.SourceVertices, ReadProxy(first)) +
                                         CountChanged(second.SourceVertices, ReadProxy(second));
            AssertSourcesUnchanged();
        }

        private IEnumerator ScenarioC()
        {
            yield return PrepareEnvironment();
            var source = CreateFixture("Interaction C Source", new Vector3(-0.8f, 0, 0));
            var target = CreateFixture("Interaction C Target", new Vector3(0.8f, 0, 0));
            var incompatible = CreateFixture("Interaction C Incompatible", new Vector3(3, 0, 0), true);
            yield return StartPreview();
            yield return StartFromInspector(source);
            yield return WaitForProxy(source);
            yield return WaitForProxy(target);
            yield return Stroke(source, "copy-source-stroke");
            Vector3[] sourceEdited = ReadProxy(source);
            Assert.That(MeshDeformerClipboard.CopyLayer(source.Deformer, 1).Succeeded, Is.True);
            _report.operationCount++;
            var measurement = BeginResponse("compatible-paste");
            var pasted = MeshDeformerClipboard.PasteLayer(target.Deformer);
            _report.operationCount++;
            Assert.That(pasted.Succeeded, Is.True, pasted.Code);
            yield return WaitFor(() => MatchesProxy(target, sourceEdited), "Compatible paste did not reach the final preview.");
            EndResponse(measurement);
            Vector3[] pastedVertices = ReadProxy(target);
            string beforeRefusal = EditorJsonUtility.ToJson(target.Deformer);
            Mesh assignedBefore = LatticeDeformerPreviewFilter.GetRendererMesh(target.Renderer);

            Assert.That(MeshDeformerClipboard.CopyLayer(incompatible.Deformer, 1).Succeeded, Is.True);
            var refused = MeshDeformerClipboard.PasteLayer(target.Deformer);
            _report.operationCount += 2;
            Assert.That(refused.Succeeded, Is.False);
            Assert.That(refused.Code, Is.EqualTo("LDT_CLIPBOARD_TOPOLOGY_MISMATCH"));
            Assert.That(EditorJsonUtility.ToJson(target.Deformer), Is.EqualTo(beforeRefusal));
            Assert.That(LatticeDeformerPreviewFilter.GetRendererMesh(target.Renderer), Is.SameAs(assignedBefore));
            Assert.That(MatchesProxy(target, pastedVertices), Is.True);
            _report.refusalReasonCode = refused.Code;

            yield return OpenInspector(target);
            yield return WaitFor(() => _inspector.LastDrawnClipboardCode == refused.Code,
                "The refusal was not actually drawn by the standard Inspector.");
            _report.refusalReasonDisplayed = true;
            CloseInspector();
            yield return StartFromInspector(target);
            yield return Stroke(target, "resume-after-refusal");
            Assert.That(CountChanged(pastedVertices, ReadProxy(target)), Is.GreaterThan(0));
            Assert.That(MatchesProxy(source, sourceEdited), Is.True, "Resuming changed the copied source.");
            _report.changedVertexCount = CountChanged(pastedVertices, ReadProxy(target));
            AssertSourcesUnchanged();
        }

        private IEnumerator PrepareEnvironment()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("InteractionE2E requires graphics; the CI category gate must reject this skip.");
            _view = SceneView.lastActiveSceneView;
            _createdView = _view == null;
            if (_view == null) _view = EditorWindow.GetWindow<SceneView>();
            _pivot = _view.pivot;
            _rotation = _view.rotation;
            _size = _view.size;
            _orthographic = _view.orthographic;
            _in2DMode = _view.in2DMode;
            _view.Show();
            _view.Focus();
            _view.in2DMode = false;
            _view.pivot = Vector3.zero;
            _view.rotation = Quaternion.identity;
            _view.size = 2.2f;
            _view.orthographic = true;
            BrushToolHandler.BrushRadius = 0.16f;
            BrushToolHandler.BrushStrength = 0.8f;
            BrushToolHandler.CurrentBrushMode = BrushToolHandler.BrushMode.Normal;
            BrushToolHandler.BrushFalloff = BrushFalloffType.Constant;
            BrushToolHandler.BackfaceCulling = false;
            BrushToolHandler.MirrorEditing = false;
            BrushToolHandler.InvertBrush = false;
            BrushToolHandler.ConnectedOnly = false;
            BrushToolHandler.UseSurfaceDistance = false;
            BrushToolHandler.ShowAffectedVertices = false;
            BrushToolHandler.ShowDisplacementHeatmap = false;
            BrushToolHandler.ShowPenetration = false;
            MeshDeformerTool.CurrentBrushSubMode = MeshDeformerTool.BrushSubMode.Brush;
            SceneView.beforeSceneGui += ProjectInputPoints;
            _view.Repaint();
            yield return null;
            yield return null;
        }

        private Fixture CreateFixture(string name, Vector3 offset, bool reordered = false)
        {
            const int columns = 9, rows = 5;
            var vertices = new Vector3[columns * rows];
            var indices = new List<int>();
            for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
            {
                vertices[y * columns + x] = new Vector3((x - 4) * 0.125f, (y - 2) * 0.125f, 0);
                if (x == columns - 1 || y == rows - 1) continue;
                int n = y * columns + x;
                indices.AddRange(new[] { n, n + columns, n + 1, n + 1, n + columns, n + columns + 1 });
            }
            if (reordered)
            {
                (vertices[0], vertices[1]) = (vertices[1], vertices[0]);
                for (int i = 0; i < indices.Count; i++)
                    if (indices[i] == 0) indices[i] = 1; else if (indices[i] == 1) indices[i] = 0;
            }
            var mesh = new Mesh { name = name + " Mesh", vertices = vertices, triangles = indices.ToArray() };
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            Shader shader = Shader.Find("Standard");
            Assert.That(shader, Is.Not.Null, "A fully initialized graphics Editor is required.");
            var material = new Material(shader);
            var go = new GameObject(name);
            go.transform.position = offset;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            var deformer = go.AddComponent<LatticeDeformer>();
            deformer.Reset();
            deformer.AddLayer("Interactive brush", MeshDeformerLayerType.Brush);
            deformer.EnsureDisplacementCapacity();
            deformer.Deform(false);
            var fixture = new Fixture { GameObject = go, Renderer = renderer, Mesh = mesh,
                Material = material, Deformer = deformer, SourceVertices = mesh.vertices };
            _fixtures.Add(fixture);
            return fixture;
        }

        private IEnumerator StartPreview()
        {
            NDMFPreview.DisablePreviewDepth = 0;
            if (!IsPreviewEnabled()) Assert.That(EditorApplication.ExecuteMenuItem(PreviewMenu), Is.True);
            yield return WaitFor(() => PreviewSession.Current != null, "The real NDMF PreviewSession did not start.");
            LatticeDeformerPreviewFilter.ForcePreviewState(true);
            PreviewSession.Current.ForceRebuild();
        }

        private IEnumerator OpenInspector(Fixture fixture)
        {
            CloseInspector();
            Selection.activeGameObject = fixture.GameObject;
            _report.operationCount++;
            ActiveEditorTracker.sharedTracker.ForceRebuild();
            yield return null;
            _inspector = ScriptableObject.CreateInstance<InteractionInspectorWindow>();
            _inspector.position = new Rect(80, 80, 620, 900);
            _inspector.Initialize(fixture.Deformer);
            _inspector.ShowUtility();
            _inspector.Focus();
            yield return WaitFor(() => _inspector.EditStartButtonScreenRect.width > 0,
                "The standard Inspector did not draw its editing button.");
            // Scroll the real Inspector so the measured IMGUI control is in the viewport.
            _inspector.RevealEditButton();
            yield return null;
            yield return null;
        }

        private IEnumerator StartFromInspector(Fixture fixture)
        {
            yield return OpenInspector(fixture);
            Rect rect = _inspector.EditStartButtonScreenRect;
            Vector2 point = rect.center - _inspector.position.position;
            Assert.That(point.y, Is.InRange(0f, _inspector.position.height), "The editing button is outside the Inspector viewport.");
            SendWindowMouse(_inspector, EventType.MouseDown, point, Vector2.zero);
            yield return null;
            SendWindowMouse(_inspector, EventType.MouseUp, point, Vector2.zero);
            yield return WaitFor(() => ToolManager.activeToolType == typeof(MeshDeformerTool),
                "The actual Inspector mouse click did not start MeshDeformerTool.");
            CloseInspector();
            _view.Focus();
            _view.Repaint();
            yield return null;
        }

        private void CloseInspector()
        {
            if (_inspector != null) _inspector.Close();
            _inspector = null;
        }

        private IEnumerator Stroke(Fixture fixture, string action)
        {
            Assert.That(Selection.activeGameObject, Is.SameAs(fixture.GameObject));
            Assert.That(ToolManager.activeToolType, Is.EqualTo(typeof(MeshDeformerTool)));
            Vector3[] before = ReadProxy(fixture);
            // Frame the selected mesh before projecting input. In a small Scene View,
            // the right-hand Overlay can otherwise cover an off-center target.
            _view.pivot = fixture.GameObject.transform.position;
            _projectionWorld = new[] { -0.375f, 0f, 0.375f }
                .Select(x => fixture.GameObject.transform.TransformPoint(new Vector3(x, 0, 0))).ToArray();
            _projectionGui = null;
            _view.Repaint();
            yield return WaitFor(() => _projectionGui != null, "Scene View did not project the input path.");
            var points = (Vector2[])_projectionGui.Clone();
            var measurement = BeginResponse(action);
            SendWindowMouse(_view, EventType.MouseMove, points[0], Vector2.zero);
            SendWindowMouse(_view, EventType.MouseDown, points[0], Vector2.zero);
            yield return null;
            SendWindowMouse(_view, EventType.MouseDrag, points[1], points[1] - points[0]);
            yield return null;
            SendWindowMouse(_view, EventType.MouseDrag, points[2], points[2] - points[1]);
            yield return null;
            SendWindowMouse(_view, EventType.MouseUp, points[2], Vector2.zero);
            // This is an oracle, never an input substitute: only the GUI events above
            // write displacement. Require the final NDMF proxy to reach all new data.
            Mesh expectedMesh = fixture.Deformer.Deform(false);
            Assert.That(expectedMesh, Is.Not.Null);
            Vector3[] expected = expectedMesh.vertices;
            Assert.That(CountChanged(before, expected), Is.GreaterThan(0), "The real Scene View stroke did not edit data.");
            string changedIndices = string.Join(",", Enumerable.Range(0, expected.Length).Where(i => (expected[i] - before[i]).sqrMagnitude > 1e-10f));
            Assert.That((expected[2 * 9 + 1] - before[2 * 9 + 1]).sqrMagnitude, Is.GreaterThan(1e-10f), "MouseDown missed its vertex. Changed indices: " + changedIndices);
            Assert.That((expected[2 * 9 + 7] - before[2 * 9 + 7]).sqrMagnitude, Is.GreaterThan(1e-10f), "The final drag did not reach another vertex. Action: " + action + "; changed indices: " + changedIndices + "; GUI path: " + string.Join(",", points.Select(p => p.ToString())));
            yield return WaitFor(() => MatchesProxy(fixture, expected), "The completed stroke did not reach the final NDMF proxy.");
            EndResponse(measurement);
        }

        private void SendWindowMouse(EditorWindow window, EventType type, Vector2 point, Vector2 delta)
        {
            var input = new Event { type = type, button = 0, mousePosition = point, delta = delta,
                clickCount = 1, modifiers = EventModifiers.None };
            window.SendEvent(input);
            _report.operationCount++;
            window.Repaint();
        }

        private void ProjectInputPoints(SceneView view)
        {
            if (view != _view || Event.current.type != EventType.Repaint) return;
            Matrix4x4 previous = Handles.matrix;
            try
            {
                Handles.matrix = Matrix4x4.identity;
                _projectionGui = _projectionWorld.Select(point =>
                    GUIUtility.GUIToScreenPoint(HandleUtility.WorldToGUIPoint(point)) - view.position.position).ToArray();
            }
            finally { Handles.matrix = previous; }
        }

        private static bool TryReadProxy(Fixture fixture, out Vector3[] vertices)
        {
            vertices = null;
            if (PreviewSession.Current == null ||
                !NDMFPreviewProxyUtility.TryGetProxyRenderer(fixture.Renderer, out Renderer proxy) ||
                proxy == null || proxy == fixture.Renderer ||
                proxy.gameObject.scene != NDMFPreviewSceneManager.GetPreviewScene() ||
                NDMFPreview.GetOriginalObjectForProxy(proxy.gameObject) != fixture.GameObject) return false;
            Mesh mesh = LatticeDeformerPreviewFilter.GetRendererMesh(proxy);
            if (mesh == null || mesh == fixture.Mesh || mesh.vertexCount != fixture.Mesh.vertexCount) return false;
            vertices = mesh.vertices;
            return true;
        }

        private static Vector3[] ReadProxy(Fixture fixture)
        {
            Assert.That(TryReadProxy(fixture, out var vertices), Is.True, "A genuine final NDMF proxy is required; no runtime mesh fallback.");
            return vertices;
        }

        private static bool MatchesProxy(Fixture fixture, Vector3[] expected) =>
            TryReadProxy(fixture, out var vertices) && CountChanged(expected, vertices) == 0;

        private IEnumerator WaitForProxy(Fixture fixture) =>
            WaitFor(() => TryReadProxy(fixture, out _), "The real NDMF graph did not publish the fixture's final proxy.");

        private IEnumerator WaitFor(Func<bool> predicate, string failure)
        {
            double started = EditorApplication.timeSinceStartup;
            int updates = _updateSerial;
            while (!predicate())
            {
                Assert.That(EditorApplication.timeSinceStartup - started, Is.LessThan(5d), failure);
                Assert.That(_updateSerial - updates, Is.LessThan(120), failure);
                _view?.Repaint();
                _inspector?.Repaint();
                yield return null;
            }
        }

        private (string action, double time, int update) BeginResponse(string action) =>
            (action, EditorApplication.timeSinceStartup, _updateSerial);

        private void EndResponse((string action, double time, int update) start)
        {
            double milliseconds = (EditorApplication.timeSinceStartup - start.time) * 1000;
            int updates = _updateSerial - start.update;
            _report.responses.Add(new InteractionE2EReport.Response { action = start.action, elapsedMs = milliseconds, editorUpdates = updates });
            Assert.That(milliseconds, Is.LessThan(5000d), "Input-to-visible timeout; this ceiling is not a smoothness target.");
            Assert.That(updates, Is.LessThan(120));
        }

        private void AssertSourcesUnchanged()
        {
            foreach (var fixture in _fixtures)
                Assert.That(fixture.Mesh.vertices, Is.EqualTo(fixture.SourceVertices), "The original source mesh changed.");
            _report.sourceUnchanged = true;
        }

        private static int CountChanged(Vector3[] before, Vector3[] after)
        {
            if (before == null || after == null || before.Length != after.Length) return -1;
            int count = 0;
            for (int i = 0; i < before.Length; i++) if ((before[i] - after[i]).sqrMagnitude > 1e-10f) count++;
            return count;
        }

        private static bool IsPreviewEnabled() => typeof(NDMFPreview)
            .GetProperty("EnablePreviewsUI", StaticMembers)?.GetValue(null) is bool enabled && enabled;
        private void CountUpdate() => _updateSerial++;
        private void ObserveLog(string condition, string trace, LogType type)
        {
            if (_report != null && (type == LogType.Exception || type == LogType.Error || type == LogType.Assert))
                _report.observedErrors.Add(condition);
        }

        private sealed class Fixture
        {
            internal GameObject GameObject;
            internal MeshRenderer Renderer;
            internal Mesh Mesh;
            internal Material Material;
            internal LatticeDeformer Deformer;
            internal Vector3[] SourceVertices;
        }
    }

    internal sealed class InteractionInspectorWindow : EditorWindow
    {
        private LatticeDeformerEditor _editor;
        private ScrollView _scroll;
        internal Rect EditStartButtonScreenRect => ReadProperty<Rect>("EditStartButtonScreenRect");
        internal string LastDrawnClipboardCode => ReadProperty<string>("LastDrawnClipboardCode");

        internal void Initialize(LatticeDeformer target)
        {
            titleContent = new GUIContent("Interaction QA - standard Inspector");
            _editor = UnityEditor.Editor.CreateEditor(target) as LatticeDeformerEditor;
            Assert.That(_editor, Is.Not.Null);
            _scroll = new ScrollView { style = { flexGrow = 1 } };
            _scroll.Add(_editor.CreateInspectorGUI());
            rootVisualElement.Add(_scroll);
        }

        internal void RevealEditButton()
        {
            var rect = EditStartButtonScreenRect;
            float delta = rect.center.y - (position.y + position.height * 0.6f);
            _scroll.scrollOffset = new Vector2(0, Mathf.Max(0, _scroll.scrollOffset.y + delta));
            Repaint();
        }

        private T ReadProperty<T>(string name)
        {
            var property = typeof(LatticeDeformerEditor).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(property, Is.Not.Null, "The Inspector's read-only repaint observation is required: " + name);
            return _editor == null ? default : (T)property.GetValue(_editor);
        }

        private void OnDisable()
        {
            rootVisualElement.Clear();
            if (_editor != null) Object.DestroyImmediate(_editor);
            _editor = null;
        }
    }
}
#endif
