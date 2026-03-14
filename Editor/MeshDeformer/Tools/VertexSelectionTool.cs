#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;
using Net._32Ba.LatticeDeformationTool;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    [EditorTool("Vertex Tool", typeof(LatticeDeformer))]
    public sealed class VertexSelectionTool : EditorTool
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
            Constant = 2
        }

        private static GUIContent s_icon;
        private static TransformMode s_transformMode = TransformMode.Move;
        private static bool s_proportionalEditing = false;
        private static float s_proportionalRadius = 0.1f;
        private static FalloffType s_proportionalFalloff = FalloffType.Smooth;
        private static float s_vertexDotSize = 3f;

        private static readonly HashSet<int> s_selectedVertices = new HashSet<int>();

        private Mesh _cachedMesh;
        private Vector3[] _meshVertices;
        private int[] _meshTriangles;

        // Drag-selection
        private bool _isDraggingSelection;
        private Vector2 _selectionStartPos;

        // Transform handle tracking
        private Quaternion _handleRotation = Quaternion.identity;
        private Vector3 _handleScale = Vector3.one;
        private bool _isTransforming;
        private Vector3[] _preTransformDisplacements;
        private Vector3[] _preTransformPositions;

        private static readonly Color k_UnselectedVertexColor = new Color(0.2f, 0.8f, 1f, 0.6f);
        private static readonly Color k_SelectedVertexColor = new Color(1f, 1f, 0f, 1f);
        private static readonly Color k_ProportionalRadiusColor = new Color(0.5f, 1f, 0.5f, 0.4f);

        static VertexSelectionTool()
        {
            LatticeLocalization.LanguageChanged += OnLanguageChanged;
        }

        private static void OnLanguageChanged()
        {
            if (s_icon != null)
            {
                s_icon.tooltip = LatticeLocalization.Tr("Vertex Tool");
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
            get => s_proportionalEditing;
            set
            {
                if (s_proportionalEditing == value) return;
                s_proportionalEditing = value;
                SceneView.RepaintAll();
            }
        }

        internal static float ProportionalRadius
        {
            get => s_proportionalRadius;
            set
            {
                s_proportionalRadius = Mathf.Clamp(value, 0.001f, 5.0f);
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
                SceneView.RepaintAll();
            }
        }

        internal static float VertexDotSize
        {
            get => s_vertexDotSize;
            set
            {
                s_vertexDotSize = Mathf.Clamp(value, 1f, 8f);
                SceneView.RepaintAll();
            }
        }

        internal static int SelectedVertexCount => s_selectedVertices.Count;

        public override GUIContent toolbarIcon
        {
            get
            {
                if (s_icon == null)
                {
                    s_icon = EditorGUIUtility.IconContent("d_EditCollider");
                }

                if (s_icon != null)
                {
                    s_icon.tooltip = LatticeLocalization.Tr("Vertex Tool");
                }

                return s_icon;
            }
        }

        public override bool IsAvailable()
        {
            var deformer = target as LatticeDeformer;
            if (deformer == null && Selection.activeGameObject != null)
            {
                deformer = Selection.activeGameObject.GetComponent<LatticeDeformer>();
            }

            return deformer != null && deformer.ActiveLayerType == MeshDeformerLayerType.Brush;
        }

        public override void OnActivated()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            SceneView.RepaintAll();
        }

        public override void OnWillBeDeactivated()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            ClearSelection();
            InvalidateCache();
        }

        private void OnUndoRedo()
        {
            if (target is LatticeDeformer deformer)
            {
                bool assignToRenderer = LatticePreviewUtility.ShouldAssignRuntimeMesh();
                deformer.Deform(assignToRenderer);
            }

            _isTransforming = false;
            _preTransformDisplacements = null;
            _preTransformPositions = null;
            SceneView.RepaintAll();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (Event.current != null && Event.current.commandName == "UndoRedoPerformed")
            {
                return;
            }

            if (target is not LatticeDeformer deformer)
            {
                return;
            }

            if (deformer.ActiveLayerType != MeshDeformerLayerType.Brush)
            {
                Handles.Label(deformer.transform.position, LatticeLocalization.Tr("Active layer is not a Brush layer."));
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

            RebuildCacheIfNeeded(sourceMesh);

            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            var evt = Event.current;

            // W/E/R key shortcuts to switch transform mode
            if (evt.type == EventType.KeyDown && !evt.alt && !evt.control && !evt.command && !evt.shift)
            {
                switch (evt.keyCode)
                {
                    case KeyCode.W:
                        CurrentTransformMode = TransformMode.Move;
                        evt.Use();
                        return;
                    case KeyCode.E:
                        CurrentTransformMode = TransformMode.Rotate;
                        evt.Use();
                        return;
                    case KeyCode.R:
                        CurrentTransformMode = TransformMode.Scale;
                        evt.Use();
                        return;
                }
            }

            // Alt+Scroll to adjust proportional radius
            if (evt.type == EventType.ScrollWheel && evt.alt && s_proportionalEditing)
            {
                float delta = -evt.delta.y * 0.01f;
                ProportionalRadius = s_proportionalRadius + delta;
                evt.Use();
                return;
            }

            // Handle selection input
            HandleSelectionInput(deformer, meshTransform, evt);

            // Draw vertex dots
            DrawVertices(deformer, meshTransform);

            // Draw and handle transform
            if (s_selectedVertices.Count > 0)
            {
                DrawTransformHandle(deformer, meshTransform);
            }

            // Draw proportional radius
            if (s_proportionalEditing && s_selectedVertices.Count > 0)
            {
                DrawProportionalRadius(deformer, meshTransform);
            }

            // Draw rect selection
            if (_isDraggingSelection)
            {
                DrawSelectionRect(evt.mousePosition);
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
                    int nearest = FindVertexAtScreenPos(_selectionStartPos, meshTransform, deformer, 20f);

                    if (evt.shift)
                    {
                        // Shift+click: add to selection
                        if (nearest >= 0)
                        {
                            s_selectedVertices.Add(nearest);
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
                        }
                    }
                    else
                    {
                        // Click: replace selection
                        s_selectedVertices.Clear();
                        if (nearest >= 0)
                        {
                            s_selectedVertices.Add(nearest);
                        }
                    }
                }
                else
                {
                    // Rect selection
                    var rect = MakeRect(_selectionStartPos, endPos);
                    var selectedInRect = FindVerticesInScreenRect(rect, meshTransform, deformer);

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
                _preTransformDisplacements = null;
                _preTransformPositions = null;
                SceneView.RepaintAll();
                evt.Use();
            }
        }

        private void DrawVertices(LatticeDeformer deformer, Transform meshTransform)
        {
            if (_meshVertices == null || _meshVertices.Length == 0)
            {
                return;
            }

            var matrix = meshTransform.localToWorldMatrix;
            var camForward = Camera.current != null ? Camera.current.transform.forward : Vector3.forward;
            int vertexCount = _meshVertices.Length;
            var displacements = deformer.Displacements;

            for (int i = 0; i < vertexCount; i++)
            {
                var vertex = _meshVertices[i];
                if (displacements != null && i < displacements.Length)
                {
                    vertex += displacements[i];
                }

                var worldPos = matrix.MultiplyPoint3x4(vertex);
                bool isSelected = s_selectedVertices.Contains(i);

                Handles.color = isSelected ? k_SelectedVertexColor : k_UnselectedVertexColor;

                float dotSize = isSelected ? s_vertexDotSize * 1.5f : s_vertexDotSize;
                float dotRadius = HandleUtility.GetHandleSize(worldPos) * 0.004f * dotSize;
                Handles.DrawSolidDisc(worldPos, camForward, dotRadius);
            }
        }

        private void DrawTransformHandle(LatticeDeformer deformer, Transform meshTransform)
        {
            // Compute centroid of selected vertices in world space
            Vector3 centroid = Vector3.zero;
            var displacements = deformer.Displacements;
            var matrix = meshTransform.localToWorldMatrix;
            int count = 0;

            foreach (int i in s_selectedVertices)
            {
                if (i < 0 || i >= _meshVertices.Length) continue;

                var vertex = _meshVertices[i];
                if (displacements != null && i < displacements.Length)
                {
                    vertex += displacements[i];
                }

                centroid += matrix.MultiplyPoint3x4(vertex);
                count++;
            }

            if (count == 0)
            {
                return;
            }

            centroid /= count;

            var handleRotation = meshTransform.rotation;

            switch (s_transformMode)
            {
                case TransformMode.Move:
                    DrawMoveHandle(deformer, meshTransform, centroid, handleRotation);
                    break;
                case TransformMode.Rotate:
                    DrawRotateHandle(deformer, meshTransform, centroid, handleRotation);
                    break;
                case TransformMode.Scale:
                    DrawScaleHandle(deformer, meshTransform, centroid, handleRotation);
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
            Undo.RecordObject(deformer, GetUndoLabel());
            deformer.EnsureDisplacementCapacity();
            _isTransforming = true;

            // Snapshot current displacements and world positions for proportional editing
            var displacements = deformer.Displacements;
            if (displacements != null)
            {
                _preTransformDisplacements = (Vector3[])displacements.Clone();
            }
            else
            {
                _preTransformDisplacements = null;
            }

            // Cache positions for proportional distance computations
            if (_meshVertices != null)
            {
                _preTransformPositions = (Vector3[])_meshVertices.Clone();
            }
        }

        private void EndTransform()
        {
            if (_isTransforming)
            {
                var deformer = target as LatticeDeformer;
                if (deformer != null)
                {
                    LatticePrefabUtility.MarkModified(deformer);
                }
            }

            _isTransforming = false;
            _preTransformDisplacements = null;
            _preTransformPositions = null;
        }

        private void ApplyMoveDelta(LatticeDeformer deformer, Vector3 localDelta)
        {
            foreach (int i in s_selectedVertices)
            {
                deformer.AddDisplacement(i, localDelta);
            }

            if (s_proportionalEditing)
            {
                ApplyProportionalMove(deformer, localDelta);
            }

            bool assignToRenderer = LatticePreviewUtility.ShouldAssignRuntimeMesh();
            deformer.Deform(assignToRenderer);
            LatticePreviewUtility.RequestSceneRepaint();
        }

        private void ApplyRotationDelta(LatticeDeformer deformer, Transform meshTransform, Vector3 worldCentroid, Quaternion deltaRotation)
        {
            var displacements = deformer.Displacements;
            if (displacements == null) return;

            var matrix = meshTransform.localToWorldMatrix;
            var invMatrix = meshTransform.worldToLocalMatrix;

            foreach (int i in s_selectedVertices)
            {
                if (i < 0 || i >= _meshVertices.Length) continue;

                var vertex = _meshVertices[i];
                if (i < displacements.Length)
                {
                    vertex += displacements[i];
                }

                var worldPos = matrix.MultiplyPoint3x4(vertex);
                var rotated = deltaRotation * (worldPos - worldCentroid) + worldCentroid;
                var newLocal = invMatrix.MultiplyPoint3x4(rotated);
                var newDisplacement = newLocal - _meshVertices[i];
                deformer.SetDisplacement(i, newDisplacement);
            }

            if (s_proportionalEditing)
            {
                ApplyProportionalRotation(deformer, meshTransform, worldCentroid, deltaRotation);
            }

            bool assignToRenderer = LatticePreviewUtility.ShouldAssignRuntimeMesh();
            deformer.Deform(assignToRenderer);
            LatticePreviewUtility.RequestSceneRepaint();
        }

        private void ApplyScaleDelta(LatticeDeformer deformer, Transform meshTransform, Vector3 worldCentroid, Vector3 relativeScale)
        {
            var displacements = deformer.Displacements;
            if (displacements == null) return;

            var matrix = meshTransform.localToWorldMatrix;
            var invMatrix = meshTransform.worldToLocalMatrix;
            var rotation = meshTransform.rotation;
            var invRotation = Quaternion.Inverse(rotation);

            foreach (int i in s_selectedVertices)
            {
                if (i < 0 || i >= _meshVertices.Length) continue;

                var vertex = _meshVertices[i];
                if (i < displacements.Length)
                {
                    vertex += displacements[i];
                }

                var worldPos = matrix.MultiplyPoint3x4(vertex);
                var localOffset = invRotation * (worldPos - worldCentroid);
                localOffset = Vector3.Scale(localOffset, relativeScale);
                var scaled = worldCentroid + rotation * localOffset;
                var newLocal = invMatrix.MultiplyPoint3x4(scaled);
                var newDisplacement = newLocal - _meshVertices[i];
                deformer.SetDisplacement(i, newDisplacement);
            }

            if (s_proportionalEditing)
            {
                ApplyProportionalScale(deformer, meshTransform, worldCentroid, relativeScale);
            }

            bool assignToRenderer = LatticePreviewUtility.ShouldAssignRuntimeMesh();
            deformer.Deform(assignToRenderer);
            LatticePreviewUtility.RequestSceneRepaint();
        }

        private void ApplyProportionalMove(LatticeDeformer deformer, Vector3 localDelta)
        {
            if (_meshVertices == null) return;

            int vertexCount = _meshVertices.Length;
            for (int i = 0; i < vertexCount; i++)
            {
                if (s_selectedVertices.Contains(i)) continue;

                float influence = ComputeProportionalInfluence(i);
                if (influence <= 0f) continue;

                deformer.AddDisplacement(i, localDelta * influence);
            }
        }

        private void ApplyProportionalRotation(LatticeDeformer deformer, Transform meshTransform, Vector3 worldCentroid, Quaternion deltaRotation)
        {
            if (_meshVertices == null) return;

            var displacements = deformer.Displacements;
            if (displacements == null) return;

            var matrix = meshTransform.localToWorldMatrix;
            var invMatrix = meshTransform.worldToLocalMatrix;
            int vertexCount = _meshVertices.Length;

            for (int i = 0; i < vertexCount; i++)
            {
                if (s_selectedVertices.Contains(i)) continue;

                float influence = ComputeProportionalInfluence(i);
                if (influence <= 0f) continue;

                var vertex = _meshVertices[i];
                if (i < displacements.Length)
                {
                    vertex += displacements[i];
                }

                var worldPos = matrix.MultiplyPoint3x4(vertex);
                var rotated = Quaternion.Slerp(Quaternion.identity, deltaRotation, influence) * (worldPos - worldCentroid) + worldCentroid;
                var newLocal = invMatrix.MultiplyPoint3x4(rotated);
                var newDisplacement = newLocal - _meshVertices[i];
                deformer.SetDisplacement(i, newDisplacement);
            }
        }

        private void ApplyProportionalScale(LatticeDeformer deformer, Transform meshTransform, Vector3 worldCentroid, Vector3 relativeScale)
        {
            if (_meshVertices == null) return;

            var displacements = deformer.Displacements;
            if (displacements == null) return;

            var matrix = meshTransform.localToWorldMatrix;
            var invMatrix = meshTransform.worldToLocalMatrix;
            var rotation = meshTransform.rotation;
            var invRotation = Quaternion.Inverse(rotation);
            int vertexCount = _meshVertices.Length;

            for (int i = 0; i < vertexCount; i++)
            {
                if (s_selectedVertices.Contains(i)) continue;

                float influence = ComputeProportionalInfluence(i);
                if (influence <= 0f) continue;

                var vertex = _meshVertices[i];
                if (i < displacements.Length)
                {
                    vertex += displacements[i];
                }

                var blendedScale = Vector3.Lerp(Vector3.one, relativeScale, influence);

                var worldPos = matrix.MultiplyPoint3x4(vertex);
                var localOffset = invRotation * (worldPos - worldCentroid);
                localOffset = Vector3.Scale(localOffset, blendedScale);
                var scaled = worldCentroid + rotation * localOffset;
                var newLocal = invMatrix.MultiplyPoint3x4(scaled);
                var newDisplacement = newLocal - _meshVertices[i];
                deformer.SetDisplacement(i, newDisplacement);
            }
        }

        private float ComputeProportionalInfluence(int vertexIndex)
        {
            if (_meshVertices == null || vertexIndex < 0 || vertexIndex >= _meshVertices.Length)
            {
                return 0f;
            }

            var vertexPos = _preTransformPositions != null ? _preTransformPositions[vertexIndex] : _meshVertices[vertexIndex];
            float minDist = float.MaxValue;

            foreach (int sel in s_selectedVertices)
            {
                if (sel < 0 || sel >= _meshVertices.Length) continue;

                var selPos = _preTransformPositions != null ? _preTransformPositions[sel] : _meshVertices[sel];
                float dist = (vertexPos - selPos).magnitude;
                if (dist < minDist)
                {
                    minDist = dist;
                }
            }

            if (minDist >= s_proportionalRadius)
            {
                return 0f;
            }

            float t = minDist / s_proportionalRadius;
            return EvaluateFalloff(t);
        }

        private static float EvaluateFalloff(float t)
        {
            t = Mathf.Clamp01(t);
            switch (s_proportionalFalloff)
            {
                case FalloffType.Linear:
                    return 1f - t;
                case FalloffType.Smooth:
                    float s = 1f - t;
                    return s * s * (3f - 2f * s);
                case FalloffType.Constant:
                    return 1f;
                default:
                    return 1f - t;
            }
        }

        private void DrawProportionalRadius(LatticeDeformer deformer, Transform meshTransform)
        {
            // Draw a wire sphere around the centroid of selected vertices
            Vector3 centroid = Vector3.zero;
            var displacements = deformer.Displacements;
            var matrix = meshTransform.localToWorldMatrix;
            int count = 0;

            foreach (int i in s_selectedVertices)
            {
                if (i < 0 || i >= _meshVertices.Length) continue;

                var vertex = _meshVertices[i];
                if (displacements != null && i < displacements.Length)
                {
                    vertex += displacements[i];
                }

                centroid += matrix.MultiplyPoint3x4(vertex);
                count++;
            }

            if (count == 0) return;
            centroid /= count;

            var prevMatrix = Handles.matrix;
            Handles.matrix = meshTransform.localToWorldMatrix;
            var localCentroid = meshTransform.InverseTransformPoint(centroid);

            Handles.color = k_ProportionalRadiusColor;
            Handles.DrawWireDisc(localCentroid, Vector3.up, s_proportionalRadius);
            Handles.DrawWireDisc(localCentroid, Vector3.right, s_proportionalRadius);
            Handles.DrawWireDisc(localCentroid, Vector3.forward, s_proportionalRadius);

            Handles.matrix = prevMatrix;
        }

        private void DrawSelectionRect(Vector2 currentMousePos)
        {
            var rect = MakeRect(_selectionStartPos, currentMousePos);

            Handles.BeginGUI();
            var fillColor = new Color(0.3f, 0.6f, 1f, 0.15f);
            var outlineColor = new Color(0.3f, 0.6f, 1f, 0.6f);

            EditorGUI.DrawRect(rect, fillColor);

            // Draw outline
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), outlineColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), outlineColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), outlineColor);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), outlineColor);

            Handles.EndGUI();
        }

        private int FindVertexAtScreenPos(Vector2 screenPos, Transform meshTransform, LatticeDeformer deformer, float maxScreenDist)
        {
            if (_meshVertices == null) return -1;

            int nearest = -1;
            float nearestDist = maxScreenDist;
            var displacements = deformer.Displacements;

            for (int i = 0; i < _meshVertices.Length; i++)
            {
                var vertex = _meshVertices[i];
                if (displacements != null && i < displacements.Length)
                {
                    vertex += displacements[i];
                }

                Vector3 worldPos = meshTransform.TransformPoint(vertex);
                Vector2 guiPos = HandleUtility.WorldToGUIPoint(worldPos);
                float dist = Vector2.Distance(guiPos, screenPos);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = i;
                }
            }

            return nearest;
        }

        private List<int> FindVerticesInScreenRect(Rect screenRect, Transform meshTransform, LatticeDeformer deformer)
        {
            var result = new List<int>();
            if (_meshVertices == null) return result;

            var displacements = deformer.Displacements;

            for (int i = 0; i < _meshVertices.Length; i++)
            {
                var vertex = _meshVertices[i];
                if (displacements != null && i < displacements.Length)
                {
                    vertex += displacements[i];
                }

                Vector3 worldPos = meshTransform.TransformPoint(vertex);
                Vector2 guiPos = HandleUtility.WorldToGUIPoint(worldPos);
                if (screenRect.Contains(guiPos))
                {
                    result.Add(i);
                }
            }

            return result;
        }

        private static Rect MakeRect(Vector2 a, Vector2 b)
        {
            float x = Mathf.Min(a.x, b.x);
            float y = Mathf.Min(a.y, b.y);
            float w = Mathf.Abs(a.x - b.x);
            float h = Mathf.Abs(a.y - b.y);
            return new Rect(x, y, w, h);
        }

        private string GetUndoLabel()
        {
            switch (s_transformMode)
            {
                case TransformMode.Move:
                    return LatticeLocalization.Tr("Vertex Move");
                case TransformMode.Rotate:
                    return LatticeLocalization.Tr("Vertex Rotate");
                case TransformMode.Scale:
                    return LatticeLocalization.Tr("Vertex Scale");
                default:
                    return LatticeLocalization.Tr("Vertex Transform");
            }
        }

        private void RebuildCacheIfNeeded(Mesh mesh)
        {
            if (mesh == null)
            {
                InvalidateCache();
                return;
            }

            if (ReferenceEquals(_cachedMesh, mesh) && _meshVertices != null)
            {
                return;
            }

            _cachedMesh = mesh;
            _meshVertices = mesh.vertices;
            _meshTriangles = mesh.triangles;
        }

        private void InvalidateCache()
        {
            _cachedMesh = null;
            _meshVertices = null;
            _meshTriangles = null;
        }

        internal static void ClearSelection()
        {
            if (s_selectedVertices.Count == 0)
            {
                return;
            }

            s_selectedVertices.Clear();
            SceneView.RepaintAll();
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

            SceneView.RepaintAll();
        }

        internal static string GetSelectionLabel()
        {
            if (s_selectedVertices.Count == 0)
            {
                return LatticeLocalization.Tr("Selected: None");
            }

            if (s_selectedVertices.Count == 1)
            {
                foreach (var index in s_selectedVertices)
                {
                    return string.Format(LatticeLocalization.Tr("Selected: {0}"), index);
                }
            }

            return string.Format(LatticeLocalization.Tr("Selected: {0} vertices"), s_selectedVertices.Count);
        }
    }

    [Overlay(typeof(SceneView), "Vertex Tool", defaultDisplay = true)]
    internal sealed class VertexSelectionToolOverlay : IMGUIOverlay, ITransientOverlay
    {
        public bool visible => ToolManager.activeToolType == typeof(VertexSelectionTool);

        public override void OnGUI()
        {
            displayName = LatticeLocalization.Tr("Vertex Tool");

            if (ToolManager.activeToolType != typeof(VertexSelectionTool))
            {
                GUILayout.Label(LatticeLocalization.Content("Vertex Tool"), EditorStyles.miniLabel);
                return;
            }

            var selectedDeformer = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<LatticeDeformer>()
                : null;
            if (selectedDeformer == null || selectedDeformer.ActiveLayerType != MeshDeformerLayerType.Brush)
            {
                EditorGUILayout.HelpBox(LatticeLocalization.Tr("Select a Mesh Deformer with an active Brush layer to edit."), MessageType.Info);
                return;
            }

            using (new GUILayout.VerticalScope(GUILayout.MinWidth(260f)))
            {
                GUILayout.Label(LatticeLocalization.Content("Vertex Tool"), EditorStyles.boldLabel);

                DrawLanguageSelector();
                GUILayout.Space(4f);

                // Transform mode selector
                var modeContent = new GUIContent[]
                {
                    LatticeLocalization.Content("Move"),
                    LatticeLocalization.Content("Rotate"),
                    LatticeLocalization.Content("Scale")
                };
                GUILayout.Label(LatticeLocalization.Content("Transform Mode"), EditorStyles.miniLabel);
                int modeIndex = GUILayout.Toolbar((int)VertexSelectionTool.CurrentTransformMode, modeContent);
                modeIndex = Mathf.Clamp(modeIndex, 0, modeContent.Length - 1);
                VertexSelectionTool.CurrentTransformMode = (VertexSelectionTool.TransformMode)modeIndex;

                GUILayout.Space(4f);

                // Proportional editing
                VertexSelectionTool.ProportionalEditing = GUILayout.Toggle(
                    VertexSelectionTool.ProportionalEditing,
                    LatticeLocalization.Content("Proportional Editing"));

                using (new EditorGUI.DisabledScope(!VertexSelectionTool.ProportionalEditing))
                {
                    VertexSelectionTool.ProportionalRadius = EditorGUILayout.Slider(
                        LatticeLocalization.Content("Proportional Radius"),
                        VertexSelectionTool.ProportionalRadius, 0.001f, 5.0f);

                    var falloffContent = new GUIContent[]
                    {
                        LatticeLocalization.Content("Smooth"),
                        LatticeLocalization.Content("Linear"),
                        new GUIContent("Constant")
                    };
                    int falloffIndex = EditorGUILayout.Popup(
                        LatticeLocalization.Content("Falloff"),
                        (int)VertexSelectionTool.ProportionalFalloffType,
                        falloffContent);
                    falloffIndex = Mathf.Clamp(falloffIndex, 0, falloffContent.Length - 1);
                    VertexSelectionTool.ProportionalFalloffType = (VertexSelectionTool.FalloffType)falloffIndex;
                }

                GUILayout.Space(4f);

                // Visualization
                GUILayout.Label(LatticeLocalization.Content("Visualization"), EditorStyles.boldLabel);
                VertexSelectionTool.VertexDotSize = EditorGUILayout.Slider(
                    LatticeLocalization.Content("Dot Size"),
                    VertexSelectionTool.VertexDotSize, 1f, 8f);

                GUILayout.Space(4f);

                // Selection info and actions
                GUILayout.Label(LatticeLocalization.Content("Selection"), EditorStyles.boldLabel);
                GUILayout.Label(VertexSelectionTool.GetSelectionLabel(), EditorStyles.miniLabel);

                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(LatticeLocalization.Content("Select All")))
                    {
                        VertexSelectionTool.SelectAll(selectedDeformer);
                    }

                    if (GUILayout.Button(LatticeLocalization.Content("Select None")))
                    {
                        VertexSelectionTool.ClearSelection();
                    }

                    if (GUILayout.Button(LatticeLocalization.Content("Invert")))
                    {
                        VertexSelectionTool.InvertSelection(selectedDeformer);
                    }
                }

                GUILayout.Space(2f);
                GUILayout.Label(LatticeLocalization.Tr("W/E/R: Move/Rotate/Scale"), EditorStyles.miniLabel);
                GUILayout.Label(LatticeLocalization.Tr("Shift+Click: Add / Ctrl+Click: Toggle"), EditorStyles.miniLabel);
                if (VertexSelectionTool.ProportionalEditing)
                {
                    GUILayout.Label(LatticeLocalization.Tr("Alt+Scroll: Proportional Radius"), EditorStyles.miniLabel);
                }
            }
        }

        private static void DrawLanguageSelector()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(LatticeLocalization.Content("Tool Language"), EditorStyles.miniLabel, GUILayout.Width(130f));

                int current = (int)LatticeLocalization.CurrentLanguage;
                int next = EditorGUILayout.Popup(current, LatticeLocalization.DisplayNames);
                if (next != current)
                {
                    next = Mathf.Clamp(next, 0, LatticeLocalization.DisplayNames.Length - 1);
                    LatticeLocalization.CurrentLanguage = (LatticeLocalization.Language)next;
                }
            }
        }
    }
}
#endif
