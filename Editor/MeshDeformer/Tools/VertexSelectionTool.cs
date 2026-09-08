#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Net._32Ba.LatticeDeformationTool;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    [ExcludeFromCodeCoverage]
    internal sealed class VertexSelectionHandler
    {
        internal enum TransformMode
        {
            Move = 0,
            Rotate = 1,
            Scale = 2
        }

        internal enum FalloffType
        {
            Smooth = 0,
            Linear = 1,
            Constant = 2,
            Sphere = 3,
            Gaussian = 4
        }

        internal enum HandleOrientation
        {
            Local = 0,
            Global = 1,
            Normal = 2
        }

        internal enum PivotMode
        {
            Center = 0,
            LastSelected = 1
        }

        private static GUIContent s_icon;
        private static TransformMode s_transformMode = TransformMode.Move;
        private static float s_proportionalRadius = 0f;
        private static FalloffType s_proportionalFalloff = FalloffType.Smooth;
        // Shared with BrushToolHandler — removed local field
        private static bool s_showWireframe = true;
        // Shared with BrushToolHandler — removed local field
        private static HandleOrientation s_handleOrientation = HandleOrientation.Normal;
        private static PivotMode s_pivotMode = PivotMode.Center;
        private static int s_lastSelectedVertex = -1;
        private static int s_selectionRevision;
        private static int s_proportionalSettingsRevision;

        // Overlay foldout states
        private static bool s_showProportionalSection = false;
        private static bool s_showVisualizationSection = false;

        private static readonly HashSet<int> s_selectedVertices = new HashSet<int>();

        private LatticeDeformer _activeDeformer;

        private Mesh _cachedMesh;
        private Vector3[] _meshVertices;     // Source mesh vertices (for displacement base)
        private Vector3[] _meshNormals;      // Source mesh normals (for backface culling / normal orientation)
        private Vector3[] _deformedVertices; // Posed/skinned vertices (for display/selection)
        private int[] _meshTriangles;
        private readonly List<Vector3> _sourceVertexScratch = new List<Vector3>();
        private readonly List<Vector3> _runtimeVertexScratch = new List<Vector3>();
        private Mesh _cachedRuntimeMesh;
        private int _cachedSourceVertexHash;
        private int _cachedRuntimeVertexHash;
        private int _cachedDisplacementHash;
        private int _cachedPoseHash;
        private bool _snapshotValid;
        private Renderer _cachedOriginalRenderer;
        private SkinnedMeshRenderer _cachedSkinnedRenderer;
        private Transform[] _cachedBones;
        private int _cachedRendererDirtyCount;
        private bool _poseRendererResolved;
        private int _cachedProxyMappingRevision = -1;
        private readonly SkinnedPoseSnapshot _poseSnapshot = new SkinnedPoseSnapshot("Vertex Selection Posed Surface");
        internal int RefreshCountForTests { get; private set; }
        internal Vector3[] DeformedVerticesForTests => _deformedVertices;

        // Drag-selection
        private bool _isDraggingSelection;
        private Vector2 _selectionStartPos;

        // Transform handle tracking
        private Quaternion _handleRotation = Quaternion.identity;
        private Vector3 _handleScale = Vector3.one;
        private bool _isTransforming;
        private Vector3[] _preTransformWorldPositions;
        private bool _preTransformWorldPositionsValid;
        private Vector3[] _proportionalWorldPositions;
        private readonly VertexProportionalInfluenceCache _proportionalInfluenceCache =
            new VertexProportionalInfluenceCache();
        private readonly SkinnedVertexHelper.RestSpaceDeltaConverterCache _restSpaceConverterCache =
            new SkinnedVertexHelper.RestSpaceDeltaConverterCache();
        private int _cachedInfluenceSelectionRevision = -1;
        private int _cachedInfluenceSettingsRevision = -1;
        private int _cachedInfluenceSourceRevision = -1;
        private int _transformInfluenceRevision;
        private Matrix4x4 _cachedInfluenceMatrix;

        private static readonly Color k_ProportionalRadiusColor = new Color(0.5f, 1f, 0.5f, 0.4f);

        static VertexSelectionHandler()
        {
            LatticeLocalization.LanguageChanged += OnLanguageChanged;
        }

        private static void OnLanguageChanged()
        {
            if (s_icon != null)
            {
                s_icon.tooltip = LatticeLocalization.Tr(LocKey.VertexTool);
            }

            SceneView.RepaintAll();
        }

        internal static TransformMode CurrentTransformMode
        {
            get => s_transformMode;
            set
            {
                if (s_transformMode == value) return;
                s_transformMode = value;
                SceneView.RepaintAll();
            }
        }

        internal static bool ProportionalEditing
        {
            get => s_proportionalRadius > 0f;
            set
            {
                if (!value && s_proportionalRadius != 0f)
                {
                    s_proportionalRadius = 0f;
                    s_proportionalSettingsRevision++;
                }
                SceneView.RepaintAll();
            }
        }

        internal static float ProportionalRadius
        {
            get => s_proportionalRadius;
            set
            {
                float clamped = Mathf.Clamp(value, 0f, 5.0f);
                if (Mathf.Approximately(s_proportionalRadius, clamped)) return;
                s_proportionalRadius = clamped;
                s_proportionalSettingsRevision++;
                SceneView.RepaintAll();
            }
        }

        internal static FalloffType ProportionalFalloffType
        {
            get => s_proportionalFalloff;
            set
            {
                if (s_proportionalFalloff == value) return;
                s_proportionalFalloff = value;
                s_proportionalSettingsRevision++;
                SceneView.RepaintAll();
            }
        }

        internal static float VertexDotSize
        {
            get => BrushToolHandler.VertexDotSize;
            set => BrushToolHandler.VertexDotSize = value;
        }

        internal static bool BackfaceCulling
        {
            get => BrushToolHandler.BackfaceCulling;
            set => BrushToolHandler.BackfaceCulling = value;
        }

        internal static HandleOrientation CurrentHandleOrientation
        {
            get => s_handleOrientation;
            set
            {
                if (s_handleOrientation == value) return;
                s_handleOrientation = value;
                SceneView.RepaintAll();
            }
        }

        internal static PivotMode CurrentPivotMode
        {
            get => s_pivotMode;
            set
            {
                if (s_pivotMode == value) return;
                s_pivotMode = value;
                SceneView.RepaintAll();
            }
        }

        internal static int SelectedVertexCount => s_selectedVertices.Count;

        internal void Activate(LatticeDeformer deformer)
        {
            ResetSelectionGesture();
            ResetTransformGesture();
            _activeDeformer = deformer;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.hierarchyChanged += OnExternalStateChanged;
            EditorApplication.projectChanged += OnExternalStateChanged;
            SceneView.RepaintAll();
        }

        internal void Deactivate()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.hierarchyChanged -= OnExternalStateChanged;
            EditorApplication.projectChanged -= OnExternalStateChanged;
            try
            {
                EndTransform();
            }
            finally
            {
                ResetSelectionGesture();
                ClearSelection();
                InvalidateCache();
                _activeDeformer = null;
            }
        }

        private void OnUndoRedo()
        {
            ResetSelectionGesture();
            ResetTransformGesture();
            if (_activeDeformer != null)
            {
                _snapshotValid = false;
                InvalidatePoseRendererCache();
                bool assignToRenderer = LatticePreviewUtility.ShouldAssignRuntimeMesh();
                _activeDeformer.Deform(assignToRenderer);
            }

            SceneView.RepaintAll();
        }

        private void OnExternalStateChanged()
        {
            _snapshotValid = false;
            InvalidatePoseRendererCache();
            SceneView.RepaintAll();
        }

        private void ResetSelectionGesture()
        {
            _isDraggingSelection = false;
            _selectionStartPos = Vector2.zero;
        }

        private DeformerEditSession _editSession;

        private void ResetTransformGesture()
        {
            _editSession?.Abandon();
            _editSession = null;
            _isTransforming = false;
            _preTransformWorldPositionsValid = false;
            InvalidateProportionalInfluenceCache();
            _handleRotation = Quaternion.identity;
            _handleScale = Vector3.one;
        }

        internal void OnToolGUI(EditorWindow window, LatticeDeformer deformer)
        {
            // Handles may consume MouseUp; retain its original type for finalization.
            bool endsGesture = Event.current != null && Event.current.rawType == EventType.MouseUp && Event.current.button == 0;
            Profiler.BeginSample("VertexSelection.OnToolGUI");
            try
            {
                if (Event.current != null && Event.current.commandName == "UndoRedoPerformed")
                {
                    return;
                }

                if (_isTransforming && (_editSession == null || !_editSession.MatchesTarget(deformer)))
                {
                    EndTransform();
                    ResetSelectionGesture();
                    ClearSelection();
                    GUIUtility.hotControl = 0;
                    return;
                }
                if (_isTransforming && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
                {
                    _editSession.TryCancel();
                    ResetTransformGesture();
                    GUIUtility.hotControl = 0;
                    Event.current.Use();
                    return;
                }

            if (deformer.ActiveLayerType != MeshDeformerLayerType.Brush)
            {
                Handles.Label(deformer.transform.position, LatticeLocalization.Tr(LocKey.ActiveLayerNotBrush));
                return;
            }

            var sourceMesh = deformer.SourceMesh;
            if (sourceMesh == null)
            {
                deformer.Deform(false);
                sourceMesh = deformer.SourceMesh;
            }

            if (sourceMesh == null)
            {
                return;
            }

            var meshTransform = deformer.MeshTransform;
            if (meshTransform == null)
            {
                return;
            }

            RebuildCacheIfNeeded(sourceMesh, deformer);

            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            var evt = Event.current;

            // Handle selection input
            // Draw vertex dots
            if (ShouldDrawVertices(evt.type))
            {
                DrawVertices(meshTransform);
            }

            // Draw and handle transform BEFORE selection input so handles get priority
            if (s_selectedVertices.Count > 0)
            {
                DrawTransformHandle(deformer, meshTransform);
            }

            // Handle selection input AFTER transform handles (handles claim hotControl first)
            HandleSelectionInput(deformer, meshTransform, evt);

            // Draw rect selection
            if (_isDraggingSelection)
            {
                SelectedVertexVisualization.DrawSelectionRectangle(
                    VertexPickingQuery.Rectangle(_selectionStartPos, evt.mousePosition));
            }

            // Force repaint for interactive feedback
            if (evt.type == EventType.MouseMove || evt.type == EventType.MouseDrag)
            {
                SceneView.RepaintAll();
            }

            // Prevent scene view from deselecting
            if (evt.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }
            }
            finally
            {
                if (endsGesture) EndTransform();
                Profiler.EndSample();
            }
        }

        private void HandleSelectionInput(LatticeDeformer deformer, Transform meshTransform, Event evt)
        {
            if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt)
            {
                // Check if we're clicking on a transform handle area (don't start selection if
                // the mouse is on the handle). We detect this by checking if the nearest control
                // is the default control we registered.
                int nearestControl = HandleUtility.nearestControl;
                int defaultControl = GUIUtility.GetControlID(FocusType.Passive);

                // If the hot control is already set (handle is being used), skip selection
                if (GUIUtility.hotControl != 0)
                {
                    return;
                }

                _isDraggingSelection = true;
                _selectionStartPos = evt.mousePosition;
                evt.Use();
            }
            else if (evt.type == EventType.MouseDrag && evt.button == 0 && _isDraggingSelection)
            {
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && evt.button == 0 && _isDraggingSelection)
            {
                _isDraggingSelection = false;

                var endPos = evt.mousePosition;
                float dragDist = Vector2.Distance(_selectionStartPos, endPos);

                if (dragDist < 5f)
                {
                    // Click selection
                    int nearest = CreatePickingQuery(meshTransform).Nearest((_deformedVertices ?? _meshVertices)?.Length ?? 0, _selectionStartPos, 20f);

                    if (evt.shift)
                    {
                        // Shift+click: add to selection
                        if (nearest >= 0)
                        {
                            s_selectedVertices.Add(nearest);
                            s_lastSelectedVertex = nearest;
                        }
                    }
                    else if (evt.control || evt.command)
                    {
                        // Ctrl+click: toggle in selection
                        if (nearest >= 0)
                        {
                            if (!s_selectedVertices.Add(nearest))
                            {
                                s_selectedVertices.Remove(nearest);
                            }
                            else
                            {
                                s_lastSelectedVertex = nearest;
                            }
                        }
                    }
                    else
                    {
                        // Click: replace selection
                        s_selectedVertices.Clear();
                        if (nearest >= 0)
                        {
                            s_selectedVertices.Add(nearest);
                            s_lastSelectedVertex = nearest;
                        }
                    }
                }
                else
                {
                    // Rect selection
                    var rect = VertexPickingQuery.Rectangle(_selectionStartPos, endPos);
                    var selectedInRect = CreatePickingQuery(meshTransform).Inside(_deformedVertices?.Length ?? 0, rect);

                    if (evt.shift)
                    {
                        foreach (int idx in selectedInRect)
                        {
                            s_selectedVertices.Add(idx);
                        }
                    }
                    else if (evt.control || evt.command)
                    {
                        foreach (int idx in selectedInRect)
                        {
                            if (!s_selectedVertices.Add(idx))
                            {
                                s_selectedVertices.Remove(idx);
                            }
                        }
                    }
                    else
                    {
                        s_selectedVertices.Clear();
                        foreach (int idx in selectedInRect)
                        {
                            s_selectedVertices.Add(idx);
                        }
                    }
                }

                _isTransforming = false;
                _preTransformWorldPositions = null;
                _preTransformWorldPositionsValid = false;
                NotifySelectionChanged();
                SceneView.RepaintAll();
                evt.Use();
            }
        }

        private Vector3 DeformedToWorld(int index, Matrix4x4 localToWorld)
        {
            return SkinnedVertexHelper.LocalToWorld(index, _worldPositions, _deformedVertices, localToWorld);
        }

        private void DrawVertices(Transform meshTransform)
        {
            Profiler.BeginSample("VertexSelection.DrawVertices");
            try
            {
                if (_deformedVertices == null || _deformedVertices.Length == 0)
                {
                    return;
                }

                // Wireframe
                if (s_showWireframe && _meshTriangles != null)
                {
                    WireframeRenderer.Draw(
                        _meshTriangles,
                        _worldPositions,
                        _deformedVertices,
                        meshTransform.localToWorldMatrix);
                }

                var cam = Camera.current;
                if (cam == null)
                {
                    return;
                }

                var matrix = meshTransform.localToWorldMatrix;
                var camRight = cam.transform.right;
                var camUp = cam.transform.up;
                bool showInfluence = ProportionalEditing && s_selectedVertices.Count > 0;
                if (showInfluence)
                {
                    EnsureProportionalInfluences(meshTransform);
                }

                // Precompute a uniform dot scale from camera distance to mesh center
                // instead of calling HandleUtility.GetHandleSize per vertex.
                float baseRadius = HandleUtility.GetHandleSize(meshTransform.position) * 0.004f;

                Profiler.BeginSample("VertexSelection.DrawVertices.Loop");
                try
                {
                    SelectedVertexVisualization.Draw(
                        new VertexDisplayGeometry(_deformedVertices, _worldPositions, null, matrix),
                        s_selectedVertices, _proportionalInfluenceCache, showInfluence, VertexDotSize,
                        baseRadius, camRight, camUp,
                        BackfaceCulling ? CompareFunction.LessEqual : CompareFunction.Always);
                }
                finally
                {
                    Profiler.EndSample();
                }
            }
            finally
            {
                Profiler.EndSample();
            }
        }

        internal static bool ShouldDrawVertices(EventType eventType)
        {
            return eventType == EventType.Repaint;
        }

        private void DrawTransformHandle(LatticeDeformer deformer, Transform meshTransform)
        {
            // Compute centroid of selected vertices in world space using deformed positions
            Vector3 centroid = Vector3.zero;
            var matrix = meshTransform.localToWorldMatrix;
            int count = 0;

            foreach (int i in s_selectedVertices)
            {
                if (_deformedVertices == null || i < 0 || i >= _deformedVertices.Length) continue;

                centroid += DeformedToWorld(i, matrix);
                count++;
            }

            if (count == 0)
            {
                return;
            }

            centroid /= count;

            // Pivot position: centroid or last selected vertex
            Vector3 pivotPos;
            if (s_pivotMode == PivotMode.LastSelected && s_lastSelectedVertex >= 0
                && _deformedVertices != null && s_lastSelectedVertex < _deformedVertices.Length
                && s_selectedVertices.Contains(s_lastSelectedVertex))
            {
                pivotPos = DeformedToWorld(s_lastSelectedVertex, matrix);
            }
            else
            {
                pivotPos = centroid;
            }

            // Handle orientation
            Quaternion handleRotation;
            switch (s_handleOrientation)
            {
                case HandleOrientation.Global:
                    handleRotation = Quaternion.identity;
                    break;
                case HandleOrientation.Normal:
                    handleRotation = ComputeAverageNormalRotation(meshTransform);
                    break;
                case HandleOrientation.Local:
                default:
                    handleRotation = meshTransform.rotation;
                    break;
            }

            switch (s_transformMode)
            {
                case TransformMode.Move:
                    DrawMoveHandle(deformer, meshTransform, pivotPos, handleRotation);
                    break;
                case TransformMode.Rotate:
                    DrawRotateHandle(deformer, meshTransform, pivotPos, handleRotation);
                    break;
                case TransformMode.Scale:
                    DrawScaleHandle(deformer, meshTransform, pivotPos, handleRotation);
                    break;
            }
        }

        private void DrawMoveHandle(LatticeDeformer deformer, Transform meshTransform, Vector3 centroid, Quaternion rotation)
        {
            EditorGUI.BeginChangeCheck();
            var newPos = Handles.PositionHandle(centroid, rotation);
            if (EditorGUI.EndChangeCheck())
            {
                var delta = newPos - centroid;
                if (delta.sqrMagnitude > 1e-12f)
                {
                    if (!_isTransforming)
                    {
                        BeginTransform(deformer);
                    }

                    if (!_isTransforming || !_editSession.TryPrepareWrite(deformer)) { EndTransform(); return; }

                    var localDelta = meshTransform.InverseTransformVector(delta);
                    ApplyMoveDelta(deformer, localDelta);
                }
            }

            // Detect end of transform
            if (_isTransforming && Event.current.type == EventType.MouseUp && Event.current.button == 0)
            {
                EndTransform();
            }
        }

        private void DrawRotateHandle(LatticeDeformer deformer, Transform meshTransform, Vector3 centroid, Quaternion rotation)
        {
            if (!_isTransforming)
            {
                _handleRotation = rotation;
            }

            EditorGUI.BeginChangeCheck();
            var newRotation = Handles.RotationHandle(_handleRotation, centroid);
            if (EditorGUI.EndChangeCheck())
            {
                if (!_isTransforming)
                {
                    BeginTransform(deformer);
                    _handleRotation = rotation;
                }

                if (!_isTransforming || !_editSession.TryPrepareWrite(deformer)) { EndTransform(); return; }

                var deltaRotation = newRotation * Quaternion.Inverse(_handleRotation);
                _handleRotation = newRotation;

                ApplyRotationDelta(deformer, meshTransform, centroid, deltaRotation);
            }

            if (_isTransforming && Event.current.type == EventType.MouseUp && Event.current.button == 0)
            {
                EndTransform();
            }
        }

        private void DrawScaleHandle(LatticeDeformer deformer, Transform meshTransform, Vector3 centroid, Quaternion rotation)
        {
            if (!_isTransforming)
            {
                _handleScale = Vector3.one;
            }

            EditorGUI.BeginChangeCheck();
            var newScale = Handles.ScaleHandle(_handleScale, centroid, rotation, HandleUtility.GetHandleSize(centroid));
            if (EditorGUI.EndChangeCheck())
            {
                if (!_isTransforming)
                {
                    BeginTransform(deformer);
                    _handleScale = Vector3.one;
                }

                if (!_isTransforming || !_editSession.TryPrepareWrite(deformer)) { EndTransform(); return; }

                // Compute relative scale from previous
                var relativeScale = new Vector3(
                    _handleScale.x != 0f ? newScale.x / _handleScale.x : 1f,
                    _handleScale.y != 0f ? newScale.y / _handleScale.y : 1f,
                    _handleScale.z != 0f ? newScale.z / _handleScale.z : 1f);
                _handleScale = newScale;

                ApplyScaleDelta(deformer, meshTransform, centroid, relativeScale);
            }

            if (_isTransforming && Event.current.type == EventType.MouseUp && Event.current.button == 0)
            {
                EndTransform();
            }
        }

        private void BeginTransform(LatticeDeformer deformer)
        {
            EndTransform();
            _editSession = DeformerEditSession.TryBegin(deformer, MeshDeformerLayerType.Brush, GetUndoLabel());
            if (_editSession == null) return;
            deformer.EnsureDisplacementCapacity();
            _isTransforming = true;
            _preTransformWorldPositionsValid = false;

            // Freeze world-space positions for proportional distance computations so
            // non-uniform Transform scale and posed skinning remain geometrically exact
            // throughout the drag gesture.
            if (_deformedVertices != null)
            {
                int count = _deformedVertices.Length;
                if (_preTransformWorldPositions == null || _preTransformWorldPositions.Length != count)
                {
                    _preTransformWorldPositions = new Vector3[count];
                }

                Matrix4x4 matrix = deformer.MeshTransform.localToWorldMatrix;
                for (int i = 0; i < count; i++)
                {
                    _preTransformWorldPositions[i] = DeformedToWorld(i, matrix);
                }

                _preTransformWorldPositionsValid = true;
                _transformInfluenceRevision++;
                InvalidateProportionalInfluenceCache();
            }
        }

        private void EndTransform()
        {
            try
            {
                _editSession?.Dispose();
            }
            finally
            {
                ResetTransformGesture();
            }
        }

        private void ApplyMoveDelta(LatticeDeformer deformer, Vector3 localDelta) =>
            ApplyVertexTransform(deformer, deformer.MeshTransform, VertexTransformOperation.Move(localDelta));

        private void ApplyRotationDelta(LatticeDeformer deformer, Transform meshTransform, Vector3 worldCentroid, Quaternion deltaRotation)
        {
            if (deformer.Displacements == null) return;
            ApplyVertexTransform(deformer, meshTransform, VertexTransformOperation.Rotate(worldCentroid, deltaRotation));
        }

        private void ApplyScaleDelta(LatticeDeformer deformer, Transform meshTransform, Vector3 worldCentroid, Vector3 relativeScale)
        {
            if (deformer.Displacements == null) return;
            ApplyVertexTransform(deformer, meshTransform,
                VertexTransformOperation.Scale(worldCentroid, meshTransform.rotation, relativeScale));
        }

        private void ApplyVertexTransform(LatticeDeformer deformer, Transform meshTransform, VertexTransformOperation operation)
        {
            var restSpace = SkinnedVertexHelper.StoreMovesInRestSpace
                ? _restSpaceConverterCache.Get(deformer) : null;
            if (ProportionalEditing) EnsureProportionalInfluences(meshTransform);
            var geometry = new VertexTransformGeometry(_meshVertices?.Length ?? 0,
                _deformedVertices, _worldPositions, meshTransform);
            VertexTransformApplication.Apply(deformer, geometry, s_selectedVertices,
                ProportionalEditing ? _proportionalInfluenceCache : null, restSpace, operation);
            _editSession?.RecordChange();
            LatticePreviewUtility.RefreshInteractiveDeformation(deformer);
        }

        private void EnsureProportionalInfluences(Transform meshTransform)
        {
            if (meshTransform == null || _deformedVertices == null)
            {
                _proportionalInfluenceCache.Clear();
                return;
            }

            Matrix4x4 matrix = meshTransform.localToWorldMatrix;
            int sourceRevision = _preTransformWorldPositionsValid
                ? _transformInfluenceRevision
                : RefreshCountForTests;
            if (_cachedInfluenceSelectionRevision == s_selectionRevision &&
                _cachedInfluenceSettingsRevision == s_proportionalSettingsRevision &&
                _cachedInfluenceSourceRevision == sourceRevision &&
                _cachedInfluenceMatrix == matrix)
            {
                return;
            }

            Vector3[] worldPositions = _preTransformWorldPositionsValid
                ? _preTransformWorldPositions
                : null;
            if (worldPositions == null)
            {
                int count = _deformedVertices.Length;
                if (_worldPositions != null && _worldPositions.Length == count)
                {
                    worldPositions = _worldPositions;
                }
                else
                {
                    if (_proportionalWorldPositions == null ||
                        _proportionalWorldPositions.Length != count)
                    {
                        _proportionalWorldPositions = new Vector3[count];
                    }

                    for (int i = 0; i < count; i++)
                    {
                        _proportionalWorldPositions[i] = matrix.MultiplyPoint3x4(_deformedVertices[i]);
                    }

                    worldPositions = _proportionalWorldPositions;
                }
            }

            _proportionalInfluenceCache.Rebuild(
                worldPositions,
                s_selectedVertices,
                s_proportionalRadius,
                s_proportionalFalloff);
            _cachedInfluenceSelectionRevision = s_selectionRevision;
            _cachedInfluenceSettingsRevision = s_proportionalSettingsRevision;
            _cachedInfluenceSourceRevision = sourceRevision;
            _cachedInfluenceMatrix = matrix;
        }

        private void InvalidateProportionalInfluenceCache()
        {
            _cachedInfluenceSelectionRevision = -1;
            _cachedInfluenceSettingsRevision = -1;
            _cachedInfluenceSourceRevision = -1;
        }

        private void DrawProportionalRadius(LatticeDeformer deformer, Transform meshTransform)
        {
            // Draw a wire sphere around the pivot position
            var matrix = meshTransform.localToWorldMatrix;

            Vector3 pivotWorld;
            if (s_pivotMode == PivotMode.LastSelected && s_lastSelectedVertex >= 0
                && _deformedVertices != null && s_lastSelectedVertex < _deformedVertices.Length
                && s_selectedVertices.Contains(s_lastSelectedVertex))
            {
                pivotWorld = DeformedToWorld(s_lastSelectedVertex, matrix);
            }
            else
            {
                Vector3 centroid = Vector3.zero;
                int count = 0;
                foreach (int i in s_selectedVertices)
                {
                    if (_deformedVertices == null || i < 0 || i >= _deformedVertices.Length) continue;
                    centroid += DeformedToWorld(i, matrix);
                    count++;
                }
                if (count == 0) return;
                pivotWorld = centroid / count;
            }

            // s_proportionalRadius is world-space — draw directly
            var prevMatrix = Handles.matrix;
            try
            {
                Handles.matrix = Matrix4x4.identity;

                Handles.color = k_ProportionalRadiusColor;
                Handles.DrawWireDisc(pivotWorld, Vector3.up, s_proportionalRadius);
                Handles.DrawWireDisc(pivotWorld, Vector3.right, s_proportionalRadius);
                Handles.DrawWireDisc(pivotWorld, Vector3.forward, s_proportionalRadius);
            }
            finally
            {
                Handles.matrix = prevMatrix;
            }
        }

        private static readonly Func<Vector3, Vector2> s_projectVertex = HandleUtility.WorldToGUIPoint;

        private VertexPickingQuery CreatePickingQuery(Transform meshTransform)
        {
            var camera = Camera.current;
            return new VertexPickingQuery(_deformedVertices, _worldPositions, _meshNormals,
                meshTransform.localToWorldMatrix, camera != null ? camera.transform.position : (Vector3?)null,
                BackfaceCulling, s_projectVertex);
        }

        private string GetUndoLabel()
        {
            switch (s_transformMode)
            {
                case TransformMode.Move:
                    return LatticeLocalization.Tr(LocKey.VertexMove);
                case TransformMode.Rotate:
                    return LatticeLocalization.Tr(LocKey.VertexRotate);
                case TransformMode.Scale:
                    return LatticeLocalization.Tr(LocKey.VertexScale);
                default:
                    return LatticeLocalization.Tr(LocKey.VertexTransform);
            }
        }

        internal void RebuildCacheIfNeeded(Mesh mesh, LatticeDeformer deformer = null)
        {
            if (mesh == null)
            {
                InvalidateCache();
                return;
            }

            int sourceVertexHash = ComputeMeshStateHash(mesh, 0);
            var runtimeMesh = deformer != null ? deformer.RuntimeMesh : null;
            bool hasRuntimeVertices = runtimeMesh != null && runtimeMesh.vertexCount == mesh.vertexCount;
            int runtimeVertexHash = hasRuntimeVertices
                ? ComputeMeshStateHash(runtimeMesh, deformer.RuntimeMeshRevision)
                : 0;
            int displacementHash = ComputeDeformerStateHash(deformer);
            int poseHash = GetPoseHash(deformer);

            bool sourceChanged = !ReferenceEquals(_cachedMesh, mesh) ||
                                 _meshVertices == null ||
                                 _meshNormals == null ||
                                 _cachedSourceVertexHash != sourceVertexHash;
            bool snapshotChanged = !_snapshotValid ||
                                   sourceChanged ||
                                   !ReferenceEquals(_cachedRuntimeMesh, runtimeMesh) ||
                                   _cachedRuntimeVertexHash != runtimeVertexHash ||
                                   _cachedDisplacementHash != displacementHash ||
                                   _cachedPoseHash != poseHash;
            if (!snapshotChanged)
                return;

            if (sourceChanged)
            {
                _cachedMesh = mesh;
                ReadVertices(mesh, _sourceVertexScratch);
                CopyVertices(_sourceVertexScratch, ref _meshVertices);
                _meshTriangles = mesh.triangles;
                _meshNormals = MeshNormalUtility.GetOrCalculateNormals(
                    mesh,
                    _meshVertices,
                    _meshTriangles);
            }

            if (hasRuntimeVertices)
                ReadVertices(runtimeMesh, _runtimeVertexScratch);
            RefreshDeformedVertices(deformer, hasRuntimeVertices);
            _cachedRuntimeMesh = runtimeMesh;
            _cachedSourceVertexHash = sourceVertexHash;
            _cachedRuntimeVertexHash = runtimeVertexHash;
            _cachedDisplacementHash = displacementHash;
            _cachedPoseHash = poseHash;
            _snapshotValid = true;
        }

        // World-space positions computed via bone skinning (null for MeshRenderer).
        private Vector3[] _worldPositions;

        private void RefreshDeformedVertices(LatticeDeformer deformer, bool hasRuntimeVertices)
        {
            Profiler.BeginSample("VertexSelection.RefreshDeformedVertices");
            try
            {
                RefreshCountForTests++;
                if (deformer == null || _meshVertices == null)
                {
                    _poseSnapshot.Reset();
                    _deformedVertices = _meshVertices;
                    _worldPositions = null;
                    return;
                }

                if (hasRuntimeVertices)
                {
                    CopyVertices(_runtimeVertexScratch, ref _deformedVertices);
                }
                else
                {
                    int count = _meshVertices.Length;
                    if (_deformedVertices == null || _deformedVertices.Length != count ||
                        ReferenceEquals(_deformedVertices, _meshVertices))
                        _deformedVertices = new Vector3[count];

                    var displacements = deformer.Displacements;
                    for (int i = 0; i < count; i++)
                    {
                        _deformedVertices[i] = _meshVertices[i];
                        if (displacements != null && i < displacements.Length)
                            _deformedVertices[i] += displacements[i];
                    }
                }

                if (_cachedSkinnedRenderer == null)
                {
                    _poseSnapshot.Reset();
                    _worldPositions = null;
                }
                else
                {
                    Profiler.BeginSample("VertexSelection.BakeMesh");
                    try
                    {
                        _poseSnapshot.TryCapture(_cachedSkinnedRenderer, _deformedVertices.Length);
                        _worldPositions = _poseSnapshot.CopyWorldPositions(_worldPositions);
                    }
                    finally
                    {
                        Profiler.EndSample();
                    }
                }
            }
            finally
            {
                Profiler.EndSample();
            }
        }

        private int GetPoseHash(LatticeDeformer deformer)
        {
            Renderer originalRenderer = deformer != null
                ? deformer.GetComponent<Renderer>()
                : null;
            int mappingRevision = LatticePreviewUtility.ProxyMappingRevision;
            if (!_poseRendererResolved || !ReferenceEquals(_cachedOriginalRenderer, originalRenderer) ||
                _cachedProxyMappingRevision != mappingRevision ||
                (!ReferenceEquals(_cachedSkinnedRenderer, null) && _cachedSkinnedRenderer == null))
            {
                _cachedOriginalRenderer = originalRenderer;
                _cachedProxyMappingRevision = mappingRevision;
                Renderer resolvedRenderer = originalRenderer;
                if (originalRenderer != null &&
                    NDMFPreviewProxyUtility.TryGetProxyRenderer(originalRenderer, out var proxyRenderer))
                {
                    resolvedRenderer = proxyRenderer;
                }

                _cachedSkinnedRenderer = resolvedRenderer as SkinnedMeshRenderer;
                _cachedBones = _cachedSkinnedRenderer != null
                    ? _cachedSkinnedRenderer.bones
                    : null;
                _cachedRendererDirtyCount = _cachedSkinnedRenderer != null
                    ? EditorUtility.GetDirtyCount(_cachedSkinnedRenderer)
                    : 0;
                _poseRendererResolved = true;
                _snapshotValid = false;
            }

            if (_cachedSkinnedRenderer == null)
                return 0;

            int rendererDirtyCount = EditorUtility.GetDirtyCount(_cachedSkinnedRenderer);
            if (_cachedBones == null || rendererDirtyCount != _cachedRendererDirtyCount)
                _cachedBones = _cachedSkinnedRenderer.bones;
            _cachedRendererDirtyCount = rendererDirtyCount;

            return SkinnedVertexHelper.ComputePoseStateHash(_cachedSkinnedRenderer, _cachedBones);
        }

        private void InvalidatePoseRendererCache()
        {
            _cachedOriginalRenderer = null;
            _cachedSkinnedRenderer = null;
            _cachedBones = null;
            _cachedRendererDirtyCount = 0;
            _poseRendererResolved = false;
            _cachedProxyMappingRevision = -1;
        }

        private static void ReadVertices(Mesh mesh, List<Vector3> scratch)
        {
            scratch.Clear();
            mesh.GetVertices(scratch);
        }

        private static int ComputeMeshStateHash(Mesh mesh, int revision)
        {
            if (mesh == null) return 0;
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + mesh.GetInstanceID();
                hash = hash * 31 + mesh.vertexCount;
                hash = hash * 31 + mesh.subMeshCount;
                hash = hash * 31 + mesh.bounds.GetHashCode();
                hash = hash * 31 + EditorUtility.GetDirtyCount(mesh);
                hash = hash * 31 + revision;
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    hash = hash * 31 + (int)mesh.GetIndexCount(i);
                    hash = hash * 31 + (int)mesh.GetTopology(i);
                }
                return hash;
            }
        }

        private static int ComputeDeformerStateHash(LatticeDeformer deformer)
        {
            if (deformer == null) return 0;
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + deformer.GetInstanceID();
                hash = hash * 31 + EditorUtility.GetDirtyCount(deformer);
                hash = hash * 31 + deformer.RuntimeMeshRevision;
                return hash;
            }
        }

        private static void CopyVertices(List<Vector3> source, ref Vector3[] destination)
        {
            if (destination == null || destination.Length != source.Count)
                destination = new Vector3[source.Count];
            source.CopyTo(destination);
        }

        private void InvalidateCache()
        {
            _poseSnapshot.Reset();
            _cachedMesh = null;
            _meshVertices = null;
            _meshNormals = null;
            _deformedVertices = null;
            _meshTriangles = null;
            _worldPositions = null;
            _cachedRuntimeMesh = null;
            _restSpaceConverterCache.Clear();
            _preTransformWorldPositions = null;
            _preTransformWorldPositionsValid = false;
            InvalidateProportionalInfluenceCache();
            _proportionalInfluenceCache.Clear();
            InvalidatePoseRendererCache();
            _snapshotValid = false;
        }

        private Quaternion ComputeAverageNormalRotation(Transform meshTransform)
        {
            if (_meshNormals == null || s_selectedVertices.Count == 0)
                return meshTransform.rotation;

            Vector3 avgNormal = Vector3.zero;
            foreach (int i in s_selectedVertices)
            {
                if (i >= 0 && i < _meshNormals.Length)
                    avgNormal += _meshNormals[i];
            }

            if (avgNormal.sqrMagnitude < 1e-6f)
                return meshTransform.rotation;

            avgNormal = meshTransform.TransformDirection(avgNormal.normalized);

            // Build rotation with Z aligned to average normal
            // Use world up as hint, fall back to world right if normal is nearly vertical
            var up = Mathf.Abs(Vector3.Dot(avgNormal, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
            return Quaternion.LookRotation(avgNormal, up);
        }

        private static void ResetSelectedVertices(LatticeDeformer deformer)
        {
            if (deformer == null || s_selectedVertices.Count == 0) return;

            Undo.RecordObject(deformer, LatticeLocalization.Tr(LocKey.ResetSelectedVertices));
            deformer.EnsureDisplacementCapacity();

            foreach (int i in s_selectedVertices)
            {
                deformer.SetDisplacement(i, Vector3.zero);
            }

            LatticePreviewUtility.RefreshInteractiveDeformation(deformer);
            LatticePrefabUtility.MarkModified(deformer);
        }

        private static void ResetAllVertices(LatticeDeformer deformer)
        {
            if (deformer == null) return;

            Undo.RecordObject(deformer, LatticeLocalization.Tr(LocKey.ResetAllVertices));
            deformer.ClearDisplacements();

            LatticePreviewUtility.RefreshInteractiveDeformation(deformer);
            LatticePrefabUtility.MarkModified(deformer);
        }

        internal static void ClearSelection()
        {
            bool hadSelection = s_selectedVertices.Count != 0;
            s_selectedVertices.Clear();
            s_lastSelectedVertex = -1;
            if (hadSelection)
            {
                NotifySelectionChanged();
                SceneView.RepaintAll();
            }
        }

        internal static void SelectAll(LatticeDeformer deformer)
        {
            if (deformer == null) return;
            var mesh = deformer.SourceMesh;
            if (mesh == null) return;

            s_selectedVertices.Clear();
            int vertexCount = mesh.vertexCount;
            for (int i = 0; i < vertexCount; i++)
            {
                s_selectedVertices.Add(i);
            }

            NotifySelectionChanged();
            SceneView.RepaintAll();
        }

        internal static void InvertSelection(LatticeDeformer deformer)
        {
            if (deformer == null) return;
            var mesh = deformer.SourceMesh;
            if (mesh == null) return;

            int vertexCount = mesh.vertexCount;
            var newSelection = new HashSet<int>();
            for (int i = 0; i < vertexCount; i++)
            {
                if (!s_selectedVertices.Contains(i))
                {
                    newSelection.Add(i);
                }
            }

            s_selectedVertices.Clear();
            foreach (int i in newSelection)
            {
                s_selectedVertices.Add(i);
            }

            NotifySelectionChanged();
            SceneView.RepaintAll();
        }

        internal static void SelectMirrorPartners(LatticeDeformer deformer, int axis)
        {
            if (deformer == null || s_selectedVertices.Count == 0) return;
            var mesh = deformer.SourceMesh;
            if (mesh == null) return;

            var mirrorMap = SymmetryVertexMapCache.GetOrCreate(
                mesh,
                axis,
                unmatchedBehavior: UnmatchedSymmetryVertexBehavior.Skip);
            var originalSelection = new int[s_selectedVertices.Count];
            s_selectedVertices.CopyTo(originalSelection);

            for (int i = 0; i < originalSelection.Length; i++)
            {
                if (mirrorMap.TryGetPartner(originalSelection[i], out int partnerIndex))
                {
                    s_selectedVertices.Add(partnerIndex);
                }
            }

            NotifySelectionChanged();
            SceneView.RepaintAll();
        }

        private static void NotifySelectionChanged()
        {
            unchecked
            {
                s_selectionRevision++;
            }
        }

        internal static string GetSelectionLabel()
        {
            if (s_selectedVertices.Count == 0)
            {
                return LatticeLocalization.Tr(LocKey.SelectedNone);
            }

            if (s_selectedVertices.Count == 1)
            {
                foreach (var index in s_selectedVertices)
                {
                    return string.Format(LatticeLocalization.Tr(LocKey.SelectedFormat), index);
                }
            }

            return string.Format(LatticeLocalization.Tr(LocKey.SelectedVerticesFormat), s_selectedVertices.Count);
        }

        private static GUIContent IconContent(string locKey, string iconName)
        {
            var text = LatticeLocalization.Tr(locKey);
            var tooltip = LatticeLocalization.Tooltip(locKey);
            var icon = EditorGUIUtility.IconContent(iconName);
            return icon?.image != null
                ? new GUIContent(text, icon.image, tooltip)
                : new GUIContent(text, tooltip);
        }

        internal static void DrawOverlayGUI(LatticeDeformer deformer)
        {
            // Transform mode selector (icon + text)
            var modeContent = new GUIContent[]
            {
                ToolIcons.Content(ToolIcons.Move, LocKey.Move),
                ToolIcons.Content(ToolIcons.Rotate, LocKey.Rotate),
                ToolIcons.Content(ToolIcons.Scale, LocKey.Scale)
            };
            GUILayout.Label(LatticeLocalization.Content(LocKey.TransformMode), EditorStyles.miniLabel);
            int modeIndex = GUILayout.Toolbar((int)VertexSelectionHandler.CurrentTransformMode, modeContent);
            modeIndex = Mathf.Clamp(modeIndex, 0, modeContent.Length - 1);
            VertexSelectionHandler.CurrentTransformMode = (VertexSelectionHandler.TransformMode)modeIndex;

            if (LatticeDeformationFeatureFlags.RestSpaceEditing &&
                VertexSelectionHandler.CurrentTransformMode == VertexSelectionHandler.TransformMode.Move &&
                deformer != null && deformer.GetComponent<SkinnedMeshRenderer>() != null)
            {
                SkinnedVertexHelper.StoreMovesInRestSpace = EditorGUILayout.Toggle(
                    LatticeLocalization.Content(LocKey.StoreMoveInRestSpace),
                    SkinnedVertexHelper.StoreMovesInRestSpace);
            }

            // Handle orientation selector
            var orientContent = new GUIContent[]
            {
                ToolIcons.Content(ToolIcons.Local, LocKey.Local),
                ToolIcons.Content(ToolIcons.Global, LocKey.Global),
                ToolIcons.Content(ToolIcons.Normal, LocKey.Normal)
            };
            GUILayout.Label(LatticeLocalization.Content(LocKey.HandleOrientation), EditorStyles.miniLabel);
            int orientIndex = GUILayout.Toolbar((int)VertexSelectionHandler.CurrentHandleOrientation, orientContent);
            orientIndex = Mathf.Clamp(orientIndex, 0, orientContent.Length - 1);
            VertexSelectionHandler.CurrentHandleOrientation = (VertexSelectionHandler.HandleOrientation)orientIndex;

            // Pivot mode selector
            var pivotContent = new GUIContent[]
            {
                ToolIcons.Content(ToolIcons.Pivot, LocKey.Center),
                LatticeLocalization.Content(LocKey.LastSelected)
            };
            GUILayout.Label(LatticeLocalization.Content(LocKey.Pivot), EditorStyles.miniLabel);
            int pivotIndex = GUILayout.Toolbar((int)VertexSelectionHandler.CurrentPivotMode, pivotContent);
            pivotIndex = Mathf.Clamp(pivotIndex, 0, pivotContent.Length - 1);
            VertexSelectionHandler.CurrentPivotMode = (VertexSelectionHandler.PivotMode)pivotIndex;

            GUILayout.Space(4f);

            // --- Proportional Editing (radius 0 = disabled, no separate toggle) ---
            // Display in cm, store internally in meters (mesh-local)
            float propCm = VertexSelectionHandler.ProportionalRadius * 100f;
            EditorGUI.BeginChangeCheck();
            propCm = EditorGUILayout.Slider(
                new GUIContent(LatticeLocalization.Tr(LocKey.ProportionalRadius) + " (cm)", LatticeLocalization.Tooltip(LocKey.ProportionalRadius)),
                propCm, 0f, 10f);
            if (EditorGUI.EndChangeCheck())
                VertexSelectionHandler.ProportionalRadius = propCm / 100f;

            var falloffContent = new GUIContent[]
            {
                LatticeLocalization.Content(LocKey.Smooth),
                LatticeLocalization.Content(LocKey.Linear),
                LatticeLocalization.Content(LocKey.Constant),
                LatticeLocalization.Content(LocKey.Sphere),
                LatticeLocalization.Content(LocKey.Gaussian)
            };
            int falloffIndex = EditorGUILayout.Popup(
                LatticeLocalization.Content(LocKey.Falloff),
                (int)VertexSelectionHandler.ProportionalFalloffType,
                falloffContent);
            falloffIndex = Mathf.Clamp(falloffIndex, 0, falloffContent.Length - 1);
            VertexSelectionHandler.ProportionalFalloffType = (VertexSelectionHandler.FalloffType)falloffIndex;

            // --- Visualization section (foldout) ---
            s_showVisualizationSection = EditorGUILayout.Foldout(s_showVisualizationSection, LatticeLocalization.Tr(LocKey.Visualization), true);
            if (s_showVisualizationSection)
            {
                EditorGUI.indentLevel++;
                s_showWireframe = GUILayout.Toggle(s_showWireframe,
                    ToolIcons.Content(ToolIcons.Eye, LocKey.ShowWireframe));
                VertexSelectionHandler.VertexDotSize = EditorGUILayout.Slider(
                    LatticeLocalization.Content(LocKey.DotSize),
                    VertexSelectionHandler.VertexDotSize, 1f, 8f);
                VertexSelectionHandler.BackfaceCulling = GUILayout.Toggle(
                    VertexSelectionHandler.BackfaceCulling,
                    ToolIcons.Content(ToolIcons.BackfaceCull, LocKey.BackfaceCulling));
                EditorGUI.indentLevel--;
            }

            GUILayout.Space(2f);

            // Selection info and actions
            GUILayout.Label(VertexSelectionHandler.GetSelectionLabel(), EditorStyles.miniLabel);

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button(LatticeLocalization.Content(LocKey.SelectAll)))
                {
                    VertexSelectionHandler.SelectAll(deformer);
                }

                if (GUILayout.Button(LatticeLocalization.Content(LocKey.SelectNone)))
                {
                    VertexSelectionHandler.ClearSelection();
                }

                if (GUILayout.Button(ToolIcons.Content(ToolIcons.Invert, LocKey.Invert)))
                {
                    VertexSelectionHandler.InvertSelection(deformer);
                }
            }

            if (LatticeDeformationFeatureFlags.SymmetricVertexSelection)
            {
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label(LatticeLocalization.Content(LocKey.MirrorAxis), GUILayout.Width(75f));
                    int mirrorAxis = GUILayout.Toolbar(
                        (int)BrushToolHandler.CurrentMirrorAxis,
                        BrushToolHandler.AxisOptions);
                    mirrorAxis = Mathf.Clamp(mirrorAxis, 0, BrushToolHandler.AxisOptions.Length - 1);
                    BrushToolHandler.CurrentMirrorAxis = (BrushToolHandler.MirrorAxis)mirrorAxis;

                    using (new EditorGUI.DisabledScope(VertexSelectionHandler.SelectedVertexCount == 0))
                    {
                        if (GUILayout.Button(ToolIcons.Content(ToolIcons.Mirror, LocKey.Mirror)))
                        {
                            VertexSelectionHandler.SelectMirrorPartners(deformer, mirrorAxis);
                        }
                    }
                }
            }

            using (new GUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(VertexSelectionHandler.SelectedVertexCount == 0))
                {
                    if (GUILayout.Button(ToolIcons.Content(ToolIcons.Reset, LocKey.ResetSelectedVertices)))
                    {
                        ResetSelectedVertices(deformer);
                    }
                }

                if (GUILayout.Button(ToolIcons.Content(ToolIcons.Reset, LocKey.ResetAllVertices)))
                {
                    ResetAllVertices(deformer);
                }
            }

            GUILayout.Space(2f);
            GUILayout.Label(LatticeLocalization.Tr(LocKey.ShiftClickHint), EditorStyles.miniLabel);
        }
    }
}
#endif
