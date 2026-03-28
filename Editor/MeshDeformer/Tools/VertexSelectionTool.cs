#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Net._32Ba.LatticeDeformationTool;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
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
        private static bool s_proportionalEditing = false;
        private static float s_proportionalRadius = 0.1f;
        private static FalloffType s_proportionalFalloff = FalloffType.Smooth;
        private static float s_vertexDotSize = 6f;
        // Shared with BrushToolHandler — removed local field
        private static HandleOrientation s_handleOrientation = HandleOrientation.Local;
        private static PivotMode s_pivotMode = PivotMode.Center;
        private static int s_lastSelectedVertex = -1;

        // Overlay foldout states
        private static bool s_showProportionalSection = false;
        private static bool s_showVisualizationSection = false;

        private const float k_PrecisionMultiplier = 0.1f;

        private static readonly HashSet<int> s_selectedVertices = new HashSet<int>();

        private LatticeDeformer _activeDeformer;

        private Mesh _cachedMesh;
        private Vector3[] _meshVertices;     // Source mesh vertices (for displacement base)
        private Vector3[] _meshNormals;      // Source mesh normals (for backface culling / normal orientation)
        private Vector3[] _deformedVertices; // Posed/skinned vertices (for display/selection)
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

        private static Material s_vertexDotMaterial;
        private static Texture2D s_circleTex;

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
            _activeDeformer = deformer;
            Undo.undoRedoPerformed += OnUndoRedo;
            SceneView.RepaintAll();
        }

        internal void Deactivate()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            ClearSelection();
            InvalidateCache();
            _activeDeformer = null;
        }

        private void OnUndoRedo()
        {
            if (_activeDeformer != null)
            {
                bool assignToRenderer = LatticePreviewUtility.ShouldAssignRuntimeMesh();
                _activeDeformer.Deform(assignToRenderer);
            }

            _isTransforming = false;
            _preTransformDisplacements = null;
            _preTransformPositions = null;
            SceneView.RepaintAll();
        }

        internal void OnToolGUI(EditorWindow window, LatticeDeformer deformer)
        {
            Profiler.BeginSample("VertexSelection.OnToolGUI");
            if (Event.current != null && Event.current.commandName == "UndoRedoPerformed")
            {
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

            // W/E/R key shortcuts to switch transform mode, Z to toggle pivot
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
                    case KeyCode.Z:
                        CurrentPivotMode = s_pivotMode == PivotMode.Center
                            ? PivotMode.LastSelected
                            : PivotMode.Center;
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
            // Draw vertex dots
            DrawVertices(deformer, meshTransform);

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
            Profiler.EndSample();
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

        private Vector3 DeformedToWorld(int index, Matrix4x4 localToWorld)
        {
            return SkinnedVertexHelper.LocalToWorld(index, _worldPositions, _deformedVertices, localToWorld);
        }

        private static Texture2D EnsureCircleTexture()
        {
            if (s_circleTex == null)
            {
                const int size = 32;
                s_circleTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                s_circleTex.hideFlags = HideFlags.HideAndDontSave;
                s_circleTex.filterMode = FilterMode.Bilinear;
                float center = (size - 1) * 0.5f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = x - center, dy = y - center;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy) / center;
                        // Smooth edge with anti-aliasing
                        float alpha = Mathf.Clamp01(1f - Mathf.Clamp01((dist - 0.7f) / 0.3f));
                        s_circleTex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    }
                }
                s_circleTex.Apply();
            }
            return s_circleTex;
        }

        private static Material EnsureVertexDotMaterial()
        {
            if (s_vertexDotMaterial == null)
            {
                s_vertexDotMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));
                s_vertexDotMaterial.hideFlags = HideFlags.HideAndDontSave;
                s_vertexDotMaterial.SetInt("_ZWrite", 0);
                s_vertexDotMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                s_vertexDotMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                s_vertexDotMaterial.SetInt("_Cull", (int)CullMode.Off);
                s_vertexDotMaterial.mainTexture = EnsureCircleTexture();
            }
            return s_vertexDotMaterial;
        }

        private void DrawVertices(LatticeDeformer deformer, Transform meshTransform)
        {
            Profiler.BeginSample("VertexSelection.DrawVertices");
            if (_deformedVertices == null || _deformedVertices.Length == 0)
            {
                Profiler.EndSample();
                return;
            }

            var cam = Camera.current;
            if (cam == null) { Profiler.EndSample(); return; }

            var matrix = meshTransform.localToWorldMatrix;
            var camRight = cam.transform.right;
            var camUp = cam.transform.up;

            int vertexCount = _deformedVertices.Length;
            bool showInfluence = s_proportionalEditing && s_selectedVertices.Count > 0;

            // Set up batched GL drawing with depth test
            var mat = EnsureVertexDotMaterial();
            mat.SetInt("_ZTest", BackfaceCulling
                ? (int)CompareFunction.LessEqual
                : (int)CompareFunction.Always);
            mat.SetPass(0);

            // Precompute a uniform dot scale from camera distance to mesh center
            // instead of calling HandleUtility.GetHandleSize per vertex
            float baseHandleSize = HandleUtility.GetHandleSize(meshTransform.position);
            float baseRadius = baseHandleSize * 0.004f;

            Profiler.BeginSample("VertexSelection.DrawVertices.Loop");
            GL.PushMatrix();
            GL.MultMatrix(Matrix4x4.identity);
            GL.Begin(GL.QUADS);

            for (int i = 0; i < vertexCount; i++)
            {
                var worldPos = DeformedToWorld(i, matrix);
                bool isSelected = s_selectedVertices.Contains(i);

                Color col;
                float dotSize;
                if (isSelected)
                {
                    col = k_SelectedVertexColor;
                    dotSize = s_vertexDotSize * 1.5f;
                }
                else if (showInfluence)
                {
                    float influence = ComputeProportionalInfluence(i);
                    if (influence > 0f)
                    {
                        col = InfluenceToColor(influence);
                        dotSize = Mathf.Lerp(s_vertexDotSize * 0.6f, s_vertexDotSize * 1.4f, influence);
                    }
                    else
                    {
                        col = k_UnselectedVertexColor;
                        dotSize = s_vertexDotSize;
                    }
                }
                else
                {
                    col = k_UnselectedVertexColor;
                    dotSize = s_vertexDotSize;
                }

                float r = baseRadius * dotSize;
                var right = camRight * r;
                var up = camUp * r;

                GL.Color(col);
                GL.TexCoord2(0f, 0f); GL.Vertex(worldPos - right - up);
                GL.TexCoord2(1f, 0f); GL.Vertex(worldPos + right - up);
                GL.TexCoord2(1f, 1f); GL.Vertex(worldPos + right + up);
                GL.TexCoord2(0f, 1f); GL.Vertex(worldPos - right + up);
            }

            GL.End();
            GL.PopMatrix();
            Profiler.EndSample();

            Profiler.EndSample();
        }

        private static Color InfluenceToColor(float t)
        {
            // 0.0 = blue, 0.25 = cyan, 0.5 = green, 0.75 = yellow, 1.0 = red
            t = Mathf.Clamp01(t);
            if (t < 0.25f)
            {
                float s = t / 0.25f;
                return new Color(0f, s, 1f, 0.9f);
            }
            if (t < 0.5f)
            {
                float s = (t - 0.25f) / 0.25f;
                return new Color(0f, 1f, 1f - s, 0.9f);
            }
            if (t < 0.75f)
            {
                float s = (t - 0.5f) / 0.25f;
                return new Color(s, 1f, 0f, 0.9f);
            }
            {
                float s = (t - 0.75f) / 0.25f;
                return new Color(1f, 1f - s, 0f, 0.9f);
            }
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

                    var localDelta = meshTransform.InverseTransformVector(delta);
                    if (Event.current.shift)
                        localDelta *= k_PrecisionMultiplier;
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
                if (Event.current.shift)
                    deltaRotation = Quaternion.Slerp(Quaternion.identity, deltaRotation, k_PrecisionMultiplier);

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
                if (Event.current.shift)
                    relativeScale = Vector3.Lerp(Vector3.one, relativeScale, k_PrecisionMultiplier);

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
                if (_activeDeformer != null)
                {
                    LatticePrefabUtility.MarkModified(_activeDeformer);
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
                if (_deformedVertices == null || i < 0 || i >= _meshVertices.Length) continue;

                var worldPos = DeformedToWorld(i, matrix);
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
                if (_deformedVertices == null || i < 0 || i >= _meshVertices.Length) continue;

                var worldPos = DeformedToWorld(i, matrix);
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
            if (_meshVertices == null || _deformedVertices == null) return;

            var matrix = meshTransform.localToWorldMatrix;
            var invMatrix = meshTransform.worldToLocalMatrix;
            int vertexCount = _meshVertices.Length;

            for (int i = 0; i < vertexCount; i++)
            {
                if (s_selectedVertices.Contains(i)) continue;

                float influence = ComputeProportionalInfluence(i);
                if (influence <= 0f) continue;

                var worldPos = DeformedToWorld(i, matrix);
                var rotated = Quaternion.Slerp(Quaternion.identity, deltaRotation, influence) * (worldPos - worldCentroid) + worldCentroid;
                var newLocal = invMatrix.MultiplyPoint3x4(rotated);
                var newDisplacement = newLocal - _meshVertices[i];
                deformer.SetDisplacement(i, newDisplacement);
            }
        }

        private void ApplyProportionalScale(LatticeDeformer deformer, Transform meshTransform, Vector3 worldCentroid, Vector3 relativeScale)
        {
            if (_meshVertices == null || _deformedVertices == null) return;

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

                var blendedScale = Vector3.Lerp(Vector3.one, relativeScale, influence);

                var worldPos = DeformedToWorld(i, matrix);
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
                case FalloffType.Sphere:
                    return t < 0.9f ? 1f : Mathf.Clamp01((1f - t) / 0.1f);
                case FalloffType.Gaussian:
                    return Mathf.Exp(-3f * t * t);
                default:
                    return 1f - t;
            }
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

            var prevMatrix = Handles.matrix;
            Handles.matrix = meshTransform.localToWorldMatrix;
            var localCentroid = meshTransform.InverseTransformPoint(pivotWorld);

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

        private bool IsVertexFrontFacing(int index, Matrix4x4 matrix)
        {
            if (!BackfaceCulling || _meshNormals == null || index < 0 || index >= _meshNormals.Length)
                return true;
            var cam = Camera.current;
            if (cam == null) return true;
            Vector3 worldPos = DeformedToWorld(index, matrix);
            Vector3 worldNormal = matrix.MultiplyVector(_meshNormals[index]).normalized;
            Vector3 viewDir = (cam.transform.position - worldPos).normalized;
            return Vector3.Dot(worldNormal, viewDir) > 0f;
        }

        private int FindVertexAtScreenPos(Vector2 screenPos, Transform meshTransform, LatticeDeformer deformer, float maxScreenDist)
        {
            var displayVerts = _deformedVertices ?? _meshVertices;
            if (displayVerts == null) return -1;

            int nearest = -1;
            float nearestDist = maxScreenDist;

            var matrix = meshTransform.localToWorldMatrix;
            for (int i = 0; i < displayVerts.Length; i++)
            {
                if (!IsVertexFrontFacing(i, matrix)) continue;
                Vector3 worldPos = DeformedToWorld(i, matrix);
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
            if (_deformedVertices == null) return result;

            var matrix = meshTransform.localToWorldMatrix;
            for (int i = 0; i < _deformedVertices.Length; i++)
            {
                if (!IsVertexFrontFacing(i, matrix)) continue;
                Vector3 worldPos = DeformedToWorld(i, matrix);
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
                    return LatticeLocalization.Tr(LocKey.VertexMove);
                case TransformMode.Rotate:
                    return LatticeLocalization.Tr(LocKey.VertexRotate);
                case TransformMode.Scale:
                    return LatticeLocalization.Tr(LocKey.VertexScale);
                default:
                    return LatticeLocalization.Tr(LocKey.VertexTransform);
            }
        }

        private void RebuildCacheIfNeeded(Mesh mesh, LatticeDeformer deformer = null)
        {
            if (mesh == null)
            {
                InvalidateCache();
                return;
            }

            if (ReferenceEquals(_cachedMesh, mesh) && _meshVertices != null && _meshNormals != null)
            {
                // Still need to refresh deformed vertices each frame
                RefreshDeformedVertices(deformer);
                return;
            }

            _cachedMesh = mesh;
            _meshVertices = mesh.vertices;
            _meshTriangles = mesh.triangles;
            _meshNormals = mesh.normals;
            if (_meshNormals == null || _meshNormals.Length != _meshVertices.Length)
            {
                mesh.RecalculateNormals();
                _meshNormals = mesh.normals;
            }
            RefreshDeformedVertices(deformer);
        }

        // World-space positions computed via bone skinning (null for MeshRenderer).
        private Vector3[] _worldPositions;

        private void RefreshDeformedVertices(LatticeDeformer deformer)
        {
            if (deformer == null || _meshVertices == null)
            {
                _deformedVertices = _meshVertices;
                _worldPositions = null;
                return;
            }

            // Get deformed local-space vertices (all layers applied)
            var runtimeMesh = deformer.RuntimeMesh;
            if (runtimeMesh != null && runtimeMesh.vertexCount == _meshVertices.Length)
            {
                _deformedVertices = runtimeMesh.vertices;
            }
            else
            {
                int count = _meshVertices.Length;
                if (_deformedVertices == null || _deformedVertices.Length != count)
                    _deformedVertices = new Vector3[count];

                var displacements = deformer.Displacements;
                for (int i = 0; i < count; i++)
                {
                    _deformedVertices[i] = _meshVertices[i];
                    if (displacements != null && i < displacements.Length)
                        _deformedVertices[i] += displacements[i];
                }
            }

            // Compute world-space positions for SkinnedMeshRenderer (null for MeshRenderer)
            _worldPositions = SkinnedVertexHelper.ComputeWorldPositions(deformer, _deformedVertices);
        }

        private void InvalidateCache()
        {
            _cachedMesh = null;
            _meshVertices = null;
            _meshNormals = null;
            _deformedVertices = null;
            _meshTriangles = null;
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

            bool assignToRenderer = LatticePreviewUtility.ShouldAssignRuntimeMesh();
            deformer.Deform(assignToRenderer);
            LatticePrefabUtility.MarkModified(deformer);
            LatticePreviewUtility.RequestSceneRepaint();
        }

        private static void ResetAllVertices(LatticeDeformer deformer)
        {
            if (deformer == null) return;

            Undo.RecordObject(deformer, LatticeLocalization.Tr(LocKey.ResetAllVertices));
            deformer.ClearDisplacements();

            bool assignToRenderer = LatticePreviewUtility.ShouldAssignRuntimeMesh();
            deformer.Deform(assignToRenderer);
            LatticePrefabUtility.MarkModified(deformer);
            LatticePreviewUtility.RequestSceneRepaint();
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
                IconContent(LocKey.Move, "MoveTool"),
                IconContent(LocKey.Rotate, "RotateTool"),
                IconContent(LocKey.Scale, "ScaleTool")
            };
            GUILayout.Label(LatticeLocalization.Content(LocKey.TransformMode), EditorStyles.miniLabel);
            int modeIndex = GUILayout.Toolbar((int)VertexSelectionHandler.CurrentTransformMode, modeContent);
            modeIndex = Mathf.Clamp(modeIndex, 0, modeContent.Length - 1);
            VertexSelectionHandler.CurrentTransformMode = (VertexSelectionHandler.TransformMode)modeIndex;

            // Handle orientation selector
            var orientContent = new GUIContent[]
            {
                LatticeLocalization.Content(LocKey.Local),
                LatticeLocalization.Content(LocKey.Global),
                LatticeLocalization.Content(LocKey.Normal)
            };
            GUILayout.Label(LatticeLocalization.Content(LocKey.HandleOrientation), EditorStyles.miniLabel);
            int orientIndex = GUILayout.Toolbar((int)VertexSelectionHandler.CurrentHandleOrientation, orientContent);
            orientIndex = Mathf.Clamp(orientIndex, 0, orientContent.Length - 1);
            VertexSelectionHandler.CurrentHandleOrientation = (VertexSelectionHandler.HandleOrientation)orientIndex;

            // Pivot mode selector
            var pivotContent = new GUIContent[]
            {
                LatticeLocalization.Content(LocKey.Center),
                LatticeLocalization.Content(LocKey.LastSelected)
            };
            GUILayout.Label(LatticeLocalization.Content(LocKey.Pivot), EditorStyles.miniLabel);
            int pivotIndex = GUILayout.Toolbar((int)VertexSelectionHandler.CurrentPivotMode, pivotContent);
            pivotIndex = Mathf.Clamp(pivotIndex, 0, pivotContent.Length - 1);
            VertexSelectionHandler.CurrentPivotMode = (VertexSelectionHandler.PivotMode)pivotIndex;

            GUILayout.Space(2f);

            // --- Proportional Editing section (foldout) ---
            s_showProportionalSection = EditorGUILayout.Foldout(s_showProportionalSection, LatticeLocalization.Tr(LocKey.ProportionalEditing), true);
            if (s_showProportionalSection)
            {
                EditorGUI.indentLevel++;
                VertexSelectionHandler.ProportionalEditing = GUILayout.Toggle(
                    VertexSelectionHandler.ProportionalEditing,
                    LatticeLocalization.Content(LocKey.ProportionalEditing));

                using (new EditorGUI.DisabledScope(!VertexSelectionHandler.ProportionalEditing))
                {
                    VertexSelectionHandler.ProportionalRadius = EditorGUILayout.Slider(
                        LatticeLocalization.Content(LocKey.ProportionalRadius),
                        VertexSelectionHandler.ProportionalRadius, 0.001f, 5.0f);

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
                }
                EditorGUI.indentLevel--;
            }

            // --- Visualization section (foldout) ---
            s_showVisualizationSection = EditorGUILayout.Foldout(s_showVisualizationSection, LatticeLocalization.Tr(LocKey.Visualization), true);
            if (s_showVisualizationSection)
            {
                EditorGUI.indentLevel++;
                VertexSelectionHandler.VertexDotSize = EditorGUILayout.Slider(
                    LatticeLocalization.Content(LocKey.DotSize),
                    VertexSelectionHandler.VertexDotSize, 1f, 8f);
                VertexSelectionHandler.BackfaceCulling = GUILayout.Toggle(
                    VertexSelectionHandler.BackfaceCulling,
                    LatticeLocalization.Content(LocKey.BackfaceCulling));
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

                if (GUILayout.Button(LatticeLocalization.Content(LocKey.Invert)))
                {
                    VertexSelectionHandler.InvertSelection(deformer);
                }
            }

            using (new GUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(VertexSelectionHandler.SelectedVertexCount == 0))
                {
                    if (GUILayout.Button(LatticeLocalization.Content(LocKey.ResetSelectedVertices)))
                    {
                        ResetSelectedVertices(deformer);
                    }
                }

                if (GUILayout.Button(LatticeLocalization.Content(LocKey.ResetAllVertices)))
                {
                    ResetAllVertices(deformer);
                }
            }

            GUILayout.Space(2f);
            GUILayout.Label(LatticeLocalization.Tr(LocKey.WERHint) + "  " + LatticeLocalization.Tr(LocKey.ZTogglePivot), EditorStyles.miniLabel);
            GUILayout.Label(LatticeLocalization.Tr(LocKey.ShiftClickHint) + "  " + LatticeLocalization.Tr(LocKey.ShiftDragPrecision), EditorStyles.miniLabel);
            if (VertexSelectionHandler.ProportionalEditing)
            {
                GUILayout.Label(LatticeLocalization.Tr(LocKey.AltScrollProportionalHint), EditorStyles.miniLabel);
            }
        }
    }
}
#endif
