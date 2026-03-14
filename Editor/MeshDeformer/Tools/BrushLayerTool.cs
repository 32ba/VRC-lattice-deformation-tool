#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;
using Net._32Ba.LatticeDeformationTool;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    [EditorTool("Brush Tool", typeof(LatticeDeformer))]
    public sealed class BrushDeformerTool : EditorTool
    {
        internal enum BrushMode
        {
            Normal = 0,
            Move = 1,
            Smooth = 2,
            Mask = 3
        }

        internal enum MirrorAxis
        {
            X = 0,
            Y = 1,
            Z = 2
        }

        private static GUIContent s_icon;
        private static float s_brushRadius = 0.05f;
        private static float s_brushStrength = 0.5f;
        private static BrushFalloffType s_brushFalloff = BrushFalloffType.Smooth;
        private static BrushMode s_brushMode = BrushMode.Normal;
        private static bool s_mirrorEditing = false;
        private static MirrorAxis s_mirrorAxis = MirrorAxis.X;
        private static bool s_invertBrush = false;
        private static bool s_showAffectedVertices = true;
        private static bool s_showDisplacementHeatmap = true;
        private static float s_vertexDotSize = 3f;
        private static bool s_connectedOnly = false;
        private static bool s_useSurfaceDistance = false;

        private Mesh _cachedMesh;
        private Vector3[] _meshVertices;
        private Vector3[] _meshNormals;
        private int[] _meshTriangles;
        private List<HashSet<int>> _adjacency;
        private Vector2 _lastMousePosition;
        private HashSet<int> _connectedVerticesCache;
        private int _connectedCacheStartVertex = -1;
        private Dictionary<int, float> _geodesicDistanceCache;
        private int _geodesicCacheStartVertex = -1;

        private static MethodInfo s_intersectRayMeshMethod;
        private static bool s_intersectRayMeshResolved;

        private static readonly Color k_NormalBrushColor = new Color(0.3f, 0.5f, 1f, 0.8f);
        private static readonly Color k_SmoothBrushColor = new Color(0.3f, 1f, 0.5f, 0.8f);
        private static readonly Color k_MoveBrushColor = new Color(1f, 0.6f, 0.2f, 0.8f);
        private static readonly Color k_MaskBrushColor = new Color(1f, 0.3f, 0.3f, 0.8f);

        static BrushDeformerTool()
        {
            LatticeLocalization.LanguageChanged += OnLanguageChanged;
        }

        private static void OnLanguageChanged()
        {
            if (s_icon != null)
            {
                s_icon.tooltip = LatticeLocalization.Tr("Brush Tool");
            }

            SceneView.RepaintAll();
        }

        internal static float BrushRadius
        {
            get => s_brushRadius;
            set
            {
                s_brushRadius = Mathf.Clamp(value, 0.001f, 1.0f);
                SceneView.RepaintAll();
            }
        }

        internal static float BrushStrength
        {
            get => s_brushStrength;
            set
            {
                s_brushStrength = Mathf.Clamp01(value);
                SceneView.RepaintAll();
            }
        }

        internal static BrushFalloffType BrushFalloff
        {
            get => s_brushFalloff;
            set
            {
                if (s_brushFalloff == value) return;
                s_brushFalloff = value;
                SceneView.RepaintAll();
            }
        }

        internal static BrushMode CurrentBrushMode
        {
            get => s_brushMode;
            set
            {
                if (s_brushMode == value) return;
                s_brushMode = value;
                SceneView.RepaintAll();
            }
        }

        internal static bool MirrorEditing
        {
            get => s_mirrorEditing;
            set
            {
                if (s_mirrorEditing == value) return;
                s_mirrorEditing = value;
                SceneView.RepaintAll();
            }
        }

        internal static MirrorAxis CurrentMirrorAxis
        {
            get => s_mirrorAxis;
            set
            {
                if (s_mirrorAxis == value) return;
                s_mirrorAxis = value;
                SceneView.RepaintAll();
            }
        }

        internal static bool InvertBrush
        {
            get => s_invertBrush;
            set
            {
                if (s_invertBrush == value) return;
                s_invertBrush = value;
                SceneView.RepaintAll();
            }
        }

        internal static bool ShowAffectedVertices
        {
            get => s_showAffectedVertices;
            set
            {
                if (s_showAffectedVertices == value) return;
                s_showAffectedVertices = value;
                SceneView.RepaintAll();
            }
        }

        internal static bool ShowDisplacementHeatmap
        {
            get => s_showDisplacementHeatmap;
            set
            {
                if (s_showDisplacementHeatmap == value) return;
                s_showDisplacementHeatmap = value;
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

        internal static bool ConnectedOnly
        {
            get => s_connectedOnly;
            set
            {
                if (s_connectedOnly == value) return;
                s_connectedOnly = value;
                SceneView.RepaintAll();
            }
        }

        internal static bool UseSurfaceDistance
        {
            get => s_useSurfaceDistance;
            set
            {
                if (s_useSurfaceDistance == value) return;
                s_useSurfaceDistance = value;
                SceneView.RepaintAll();
            }
        }

        internal static GUIContent[] AxisOptions => new[]
        {
            LatticeLocalization.Content("X"),
            LatticeLocalization.Content("Y"),
            LatticeLocalization.Content("Z")
        };

        public override GUIContent toolbarIcon
        {
            get
            {
                if (s_icon == null)
                {
                    s_icon = EditorGUIUtility.IconContent("ClothInspector.PaintTool");
                }

                if (s_icon != null)
                {
                    s_icon.tooltip = LatticeLocalization.Tr("Brush Tool");
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
            InvalidateCache();
        }

        private void OnUndoRedo()
        {
            if (target is LatticeDeformer deformer)
            {
                bool assignToRenderer = LatticePreviewUtility.ShouldAssignRuntimeMesh();
                deformer.Deform(assignToRenderer);
            }

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

            // Handle scroll wheel for radius/strength adjustment
            if (evt.type == EventType.ScrollWheel)
            {
                if (evt.shift)
                {
                    float delta = -evt.delta.y * 0.01f;
                    BrushStrength = s_brushStrength + delta;
                    evt.Use();
                    return;
                }
                else if (evt.alt)
                {
                    float delta = -evt.delta.y * 0.005f;
                    BrushRadius = s_brushRadius + delta;
                    evt.Use();
                    return;
                }
            }

            // Draw displacement heatmap (always visible when enabled)
            if (s_showDisplacementHeatmap && deformer.HasDisplacements())
            {
                DrawDisplacementHeatmap(deformer, meshTransform);
            }

            // Draw vertex mask visualization when in Mask mode
            if (s_brushMode == BrushMode.Mask)
            {
                DrawVertexMaskVisualization(deformer, meshTransform);
            }

            // Raycast to find brush center on mesh surface
            var mouseRay = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
            bool hitSurface = IntersectRayMesh(mouseRay, sourceMesh, meshTransform.localToWorldMatrix, out var hit);

            if (hitSurface)
            {
                // Convert hit point to local space for drawing and calculations
                var localHitPoint = meshTransform.InverseTransformPoint(hit.point);
                var localHitNormal = meshTransform.InverseTransformDirection(hit.normal).normalized;

                // Draw brush disc
                var prevMatrix = Handles.matrix;
                Handles.matrix = meshTransform.localToWorldMatrix;

                Color brushColor = GetBrushColor();
                Handles.color = brushColor;
                Handles.DrawWireDisc(localHitPoint, localHitNormal, s_brushRadius);

                // Draw a second, slightly transparent filled disc for better visibility
                Color fillColor = brushColor;
                fillColor.a = 0.1f;
                Handles.color = fillColor;
                Handles.DrawSolidDisc(localHitPoint, localHitNormal, s_brushRadius);

                // Draw affected vertex dots within brush radius
                if (s_showAffectedVertices && _meshVertices != null)
                {
                    // Update geodesic cache for preview visualization during hover
                    if (s_useSurfaceDistance)
                    {
                        UpdateGeodesicDistanceCache(localHitPoint);
                    }

                    DrawAffectedVertices(deformer, localHitPoint, meshTransform);
                }

                Handles.matrix = prevMatrix;

                // Handle brush painting on left mouse drag
                if (evt.type == EventType.MouseDrag && evt.button == 0)
                {
                    ApplyBrush(deformer, meshTransform, localHitPoint, localHitNormal, evt);
                    evt.Use();
                }
                else if (evt.type == EventType.MouseDown && evt.button == 0)
                {
                    _lastMousePosition = evt.mousePosition;

                    // Build connected vertices cache at stroke start
                    UpdateConnectedVerticesCache(localHitPoint);

                    // Build geodesic distance cache at stroke start
                    UpdateGeodesicDistanceCache(localHitPoint);

                    Undo.RecordObject(deformer, GetUndoLabel());
                    deformer.EnsureDisplacementCapacity();

                    ApplyBrush(deformer, meshTransform, localHitPoint, localHitNormal, evt);
                    evt.Use();
                }
                else if (evt.type == EventType.MouseUp && evt.button == 0)
                {
                    ClearConnectedVerticesCache();
                    ClearGeodesicDistanceCache();
                    evt.Use();
                }
            }
            else
            {
                // Even without hit, handle mouse up to stop painting
                if (evt.type == EventType.MouseUp && evt.button == 0)
                {
                    ClearConnectedVerticesCache();
                    ClearGeodesicDistanceCache();
                }
            }

            // Force repaint so brush disc follows cursor
            if (evt.type == EventType.MouseMove || evt.type == EventType.MouseDrag)
            {
                SceneView.RepaintAll();
            }

            // Prevent scene view from deselecting on click
            if (evt.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }
        }

        private void ApplyBrush(LatticeDeformer deformer, Transform meshTransform, Vector3 localHitPoint, Vector3 localHitNormal, Event evt)
        {
            if (_meshVertices == null || _meshVertices.Length == 0)
            {
                return;
            }

            deformer.EnsureDisplacementCapacity();

            float radiusSq = s_brushRadius * s_brushRadius;
            float strength = s_brushStrength * 0.01f;
            float direction = s_invertBrush ? -1f : 1f;
            bool assignToRenderer = LatticePreviewUtility.ShouldAssignRuntimeMesh();

            bool modified = false;

            switch (s_brushMode)
            {
                case BrushMode.Normal:
                    modified = ApplyNormalBrush(deformer, localHitPoint, radiusSq, strength, direction);
                    break;

                case BrushMode.Move:
                    modified = ApplyMoveBrush(deformer, meshTransform, localHitPoint, radiusSq, strength, evt);
                    break;

                case BrushMode.Smooth:
                    modified = ApplySmoothBrush(deformer, localHitPoint, radiusSq, strength);
                    break;

                case BrushMode.Mask:
                    modified = ApplyMaskBrush(deformer, localHitPoint, radiusSq);
                    break;
            }

            if (modified)
            {
                if (s_mirrorEditing)
                {
                    ApplyMirror(deformer, localHitPoint, radiusSq, strength, direction);
                }

                deformer.Deform(assignToRenderer);
                LatticePreviewUtility.RequestSceneRepaint();
            }

            _lastMousePosition = evt.mousePosition;
        }

        private bool ApplyNormalBrush(LatticeDeformer deformer, Vector3 localHitPoint, float radiusSq, float strength, float direction)
        {
            bool modified = false;
            int vertexCount = _meshVertices.Length;

            for (int i = 0; i < vertexCount; i++)
            {
                if (s_connectedOnly && _connectedVerticesCache != null && !_connectedVerticesCache.Contains(i))
                {
                    continue;
                }

                float falloff;
                if (s_useSurfaceDistance && _geodesicDistanceCache != null)
                {
                    if (!_geodesicDistanceCache.TryGetValue(i, out float geodesicDist))
                    {
                        continue; // Not reachable via surface
                    }
                    float t = geodesicDist / s_brushRadius;
                    falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);
                }
                else
                {
                    var vertex = _meshVertices[i] + deformer.GetDisplacement(i);
                    float distSq = (vertex - localHitPoint).sqrMagnitude;
                    if (distSq > radiusSq) continue;

                    float dist = Mathf.Sqrt(distSq);
                    float t = dist / s_brushRadius;
                    falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);
                }

                var normal = _meshNormals[i].normalized;
                if (normal.sqrMagnitude < 0.001f) normal = Vector3.up;

                var delta = normal * (strength * falloff * direction);
                float maskValue = GetActiveLayerMaskValue(deformer, i);
                if (maskValue < 1e-6f) continue;
                delta *= maskValue;
                deformer.AddDisplacement(i, delta);
                modified = true;
            }

            return modified;
        }

        private bool ApplyMoveBrush(LatticeDeformer deformer, Transform meshTransform, Vector3 localHitPoint, float radiusSq, float strength, Event evt)
        {
            // Compute mouse delta in world space, then convert to local
            var mouseDelta = evt.delta;
            if (mouseDelta.sqrMagnitude < 0.001f) return false;

            var camera = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
            if (camera == null) return false;

            // Convert mouse delta to world space direction
            var screenPoint = camera.WorldToScreenPoint(meshTransform.TransformPoint(localHitPoint));
            var screenPointMoved = screenPoint + new Vector3(mouseDelta.x, -mouseDelta.y, 0f);
            var worldPoint = camera.ScreenToWorldPoint(screenPoint);
            var worldPointMoved = camera.ScreenToWorldPoint(screenPointMoved);
            var worldDelta = worldPointMoved - worldPoint;

            // Convert world delta to local space
            var localDelta = meshTransform.InverseTransformVector(worldDelta);

            bool modified = false;
            int vertexCount = _meshVertices.Length;

            for (int i = 0; i < vertexCount; i++)
            {
                if (s_connectedOnly && _connectedVerticesCache != null && !_connectedVerticesCache.Contains(i))
                {
                    continue;
                }

                float falloff;
                if (s_useSurfaceDistance && _geodesicDistanceCache != null)
                {
                    if (!_geodesicDistanceCache.TryGetValue(i, out float geodesicDist))
                    {
                        continue; // Not reachable via surface
                    }
                    float t = geodesicDist / s_brushRadius;
                    falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);
                }
                else
                {
                    var vertex = _meshVertices[i] + deformer.GetDisplacement(i);
                    float distSq = (vertex - localHitPoint).sqrMagnitude;
                    if (distSq > radiusSq) continue;

                    float dist = Mathf.Sqrt(distSq);
                    float t = dist / s_brushRadius;
                    falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);
                }

                var delta = localDelta * (strength * falloff * 10f);
                float maskValue = GetActiveLayerMaskValue(deformer, i);
                if (maskValue < 1e-6f) continue;
                delta *= maskValue;
                deformer.AddDisplacement(i, delta);
                modified = true;
            }

            return modified;
        }

        private bool ApplySmoothBrush(LatticeDeformer deformer, Vector3 localHitPoint, float radiusSq, float strength)
        {
            EnsureAdjacencyBuilt();

            bool modified = false;
            int vertexCount = _meshVertices.Length;

            // Snapshot current displacements for reading during averaging
            var currentDisplacements = new Vector3[vertexCount];
            Array.Copy(deformer.Displacements, currentDisplacements, vertexCount);

            float smoothFactor = Mathf.Clamp01(strength * 10f);

            for (int i = 0; i < vertexCount; i++)
            {
                if (s_connectedOnly && _connectedVerticesCache != null && !_connectedVerticesCache.Contains(i))
                {
                    continue;
                }

                float falloff;
                if (s_useSurfaceDistance && _geodesicDistanceCache != null)
                {
                    if (!_geodesicDistanceCache.TryGetValue(i, out float geodesicDist))
                    {
                        continue; // Not reachable via surface
                    }
                    float t = geodesicDist / s_brushRadius;
                    falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);
                }
                else
                {
                    var vertex = _meshVertices[i] + currentDisplacements[i];
                    float distSq = (vertex - localHitPoint).sqrMagnitude;
                    if (distSq > radiusSq) continue;

                    float dist = Mathf.Sqrt(distSq);
                    float t = dist / s_brushRadius;
                    falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);
                }

                // Compute average displacement of neighbors
                var neighbors = _adjacency[i];
                if (neighbors == null || neighbors.Count == 0) continue;

                var averageDisp = Vector3.zero;
                foreach (int neighbor in neighbors)
                {
                    averageDisp += currentDisplacements[neighbor];
                }
                averageDisp /= neighbors.Count;

                // Blend toward neighbor average
                var currentDisp = currentDisplacements[i];
                float maskValue = GetActiveLayerMaskValue(deformer, i);
                if (maskValue < 1e-6f) continue;
                var targetDisp = Vector3.Lerp(currentDisp, averageDisp, smoothFactor * falloff * maskValue);
                deformer.SetDisplacement(i, targetDisp);
                modified = true;
            }

            return modified;
        }

        private bool ApplyMaskBrush(LatticeDeformer deformer, Vector3 localHitPoint, float radiusSq)
        {
            if (_meshVertices == null || _meshVertices.Length == 0)
            {
                return false;
            }

            if (!TryGetActiveLayer(deformer, out var layer))
            {
                return false;
            }

            layer.EnsureVertexMaskCapacity(_meshVertices.Length);
            // When inverted: erase mask (unprotect), otherwise: paint mask (protect)
            float targetValue = s_invertBrush ? 1f : 0f;
            bool modified = false;

            for (int i = 0; i < _meshVertices.Length; i++)
            {
                if (s_connectedOnly && _connectedVerticesCache != null && !_connectedVerticesCache.Contains(i))
                {
                    continue;
                }

                float falloff;
                if (s_useSurfaceDistance && _geodesicDistanceCache != null)
                {
                    if (!_geodesicDistanceCache.TryGetValue(i, out float geodesicDist))
                    {
                        continue;
                    }
                    float t = geodesicDist / s_brushRadius;
                    falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);
                }
                else
                {
                    var vertex = _meshVertices[i] + deformer.GetDisplacement(i);
                    float distSq = (vertex - localHitPoint).sqrMagnitude;
                    if (distSq > radiusSq) continue;

                    float dist = Mathf.Sqrt(distSq);
                    float t = dist / s_brushRadius;
                    falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);
                }

                float current = layer.GetVertexMask(i);
                float blend = Mathf.Lerp(current, targetValue, falloff * s_brushStrength);
                layer.SetVertexMask(i, blend);
                modified = true;
            }

            return modified;
        }

        private static bool TryGetActiveLayer(LatticeDeformer deformer, out LatticeLayer layer)
        {
            layer = null;
            if (deformer == null)
            {
                return false;
            }

            var layers = deformer.Layers;
            int index = deformer.ActiveLayerIndex;
            if (index < 0 || index >= layers.Count)
            {
                return false;
            }

            layer = layers[index];
            return layer != null && layer.Type == MeshDeformerLayerType.Brush;
        }

        private static float GetActiveLayerMaskValue(LatticeDeformer deformer, int vertexIndex)
        {
            if (!TryGetActiveLayer(deformer, out var layer))
            {
                return 1f;
            }

            return layer.GetVertexMask(vertexIndex);
        }

        private void ApplyMirror(LatticeDeformer deformer, Vector3 localHitPoint, float radiusSq, float strength, float direction)
        {
            if (_meshVertices == null || _meshVertices.Length == 0) return;

            // Mirror the brush center
            var mirroredCenter = MirrorPosition(localHitPoint);
            int vertexCount = _meshVertices.Length;

            // Build connected vertices cache for the mirrored side
            HashSet<int> mirrorConnected = null;
            if (s_connectedOnly)
            {
                EnsureAdjacencyBuilt();
                int mirrorNearest = FindNearestVertex(mirroredCenter);
                if (mirrorNearest >= 0)
                {
                    mirrorConnected = FindConnectedVertices(mirrorNearest, s_brushRadius);
                }
            }

            switch (s_brushMode)
            {
                case BrushMode.Normal:
                {
                    for (int i = 0; i < vertexCount; i++)
                    {
                        if (s_connectedOnly && mirrorConnected != null && !mirrorConnected.Contains(i))
                        {
                            continue;
                        }

                        var vertex = _meshVertices[i] + deformer.GetDisplacement(i);
                        float distSq = (vertex - mirroredCenter).sqrMagnitude;
                        if (distSq > radiusSq) continue;

                        float dist = Mathf.Sqrt(distSq);
                        float t = dist / s_brushRadius;
                        float falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);

                        var normal = _meshNormals[i].normalized;
                        if (normal.sqrMagnitude < 0.001f) normal = Vector3.up;

                        // Mirror the normal direction for the mirrored side
                        var mirroredNormal = MirrorDirection(normal);
                        var delta = mirroredNormal * (strength * falloff * direction);
                        deformer.AddDisplacement(i, delta);
                    }
                    break;
                }

                case BrushMode.Smooth:
                {
                    EnsureAdjacencyBuilt();
                    var currentDisplacements = new Vector3[vertexCount];
                    Array.Copy(deformer.Displacements, currentDisplacements, vertexCount);
                    float smoothFactor = Mathf.Clamp01(strength * 10f);

                    for (int i = 0; i < vertexCount; i++)
                    {
                        if (s_connectedOnly && mirrorConnected != null && !mirrorConnected.Contains(i))
                        {
                            continue;
                        }

                        var vertex = _meshVertices[i] + currentDisplacements[i];
                        float distSq = (vertex - mirroredCenter).sqrMagnitude;
                        if (distSq > radiusSq) continue;

                        float dist = Mathf.Sqrt(distSq);
                        float t = dist / s_brushRadius;
                        float falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);

                        var neighbors = _adjacency[i];
                        if (neighbors == null || neighbors.Count == 0) continue;

                        var averageDisp = Vector3.zero;
                        foreach (int neighbor in neighbors)
                        {
                            averageDisp += currentDisplacements[neighbor];
                        }
                        averageDisp /= neighbors.Count;

                        var currentDisp = currentDisplacements[i];
                        var targetDisp = Vector3.Lerp(currentDisp, averageDisp, smoothFactor * falloff);
                        deformer.SetDisplacement(i, targetDisp);
                    }
                    break;
                }

                case BrushMode.Move:
                {
                    // Move mirror is handled implicitly during the primary pass since it affects
                    // vertices near the mirrored brush center with mirrored direction.
                    // We do a second pass here for the mirrored region.
                    // The mouse delta has already been consumed, so we retrieve the last applied local delta.
                    // For simplicity in mirrored move mode, we skip the mirror pass as the direction would
                    // need to be re-derived from the event which is already consumed.
                    break;
                }

                case BrushMode.Mask:
                {
                    if (!TryGetActiveLayer(deformer, out var layer)) break;
                    layer.EnsureVertexMaskCapacity(vertexCount);
                    float targetValue = s_invertBrush ? 1f : 0f;

                    for (int i = 0; i < vertexCount; i++)
                    {
                        if (s_connectedOnly && mirrorConnected != null && !mirrorConnected.Contains(i))
                        {
                            continue;
                        }

                        var vertex = _meshVertices[i] + deformer.GetDisplacement(i);
                        float distSq = (vertex - mirroredCenter).sqrMagnitude;
                        if (distSq > radiusSq) continue;

                        float dist = Mathf.Sqrt(distSq);
                        float t = dist / s_brushRadius;
                        float falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);

                        float current = layer.GetVertexMask(i);
                        float blend = Mathf.Lerp(current, targetValue, falloff * s_brushStrength);
                        layer.SetVertexMask(i, blend);
                    }
                    break;
                }
            }
        }

        private Vector3 MirrorPosition(Vector3 position)
        {
            switch (s_mirrorAxis)
            {
                case MirrorAxis.X: return new Vector3(-position.x, position.y, position.z);
                case MirrorAxis.Y: return new Vector3(position.x, -position.y, position.z);
                case MirrorAxis.Z: return new Vector3(position.x, position.y, -position.z);
                default: return position;
            }
        }

        private Vector3 MirrorDirection(Vector3 dir)
        {
            switch (s_mirrorAxis)
            {
                case MirrorAxis.X: return new Vector3(-dir.x, dir.y, dir.z);
                case MirrorAxis.Y: return new Vector3(dir.x, -dir.y, dir.z);
                case MirrorAxis.Z: return new Vector3(dir.x, dir.y, -dir.z);
                default: return dir;
            }
        }

        private Color GetBrushColor()
        {
            switch (s_brushMode)
            {
                case BrushMode.Normal: return k_NormalBrushColor;
                case BrushMode.Smooth: return k_SmoothBrushColor;
                case BrushMode.Move: return k_MoveBrushColor;
                case BrushMode.Mask: return k_MaskBrushColor;
                default: return k_NormalBrushColor;
            }
        }

        private string GetUndoLabel()
        {
            switch (s_brushMode)
            {
                case BrushMode.Smooth: return LatticeLocalization.Tr("Brush Smooth");
                case BrushMode.Mask: return LatticeLocalization.Tr("Brush Mask");
                default: return LatticeLocalization.Tr("Brush Deform");
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
            _meshNormals = mesh.normals;
            _meshTriangles = mesh.triangles;
            _adjacency = null;

            if (_meshNormals == null || _meshNormals.Length != _meshVertices.Length)
            {
                mesh.RecalculateNormals();
                _meshNormals = mesh.normals;
            }
        }

        private void InvalidateCache()
        {
            _cachedMesh = null;
            _meshVertices = null;
            _meshNormals = null;
            _meshTriangles = null;
            _adjacency = null;
            ClearConnectedVerticesCache();
            ClearGeodesicDistanceCache();
        }

        private void DrawAffectedVertices(LatticeDeformer deformer, Vector3 localHitPoint, Transform meshTransform)
        {
            float radiusSq = s_brushRadius * s_brushRadius;
            int vertexCount = _meshVertices.Length;
            Color brushColor = GetBrushColor();
            var matrix = meshTransform.localToWorldMatrix;

            for (int i = 0; i < vertexCount; i++)
            {
                if (s_connectedOnly && _connectedVerticesCache != null && !_connectedVerticesCache.Contains(i))
                {
                    continue;
                }

                var vertex = _meshVertices[i] + deformer.GetDisplacement(i);

                float falloff;
                if (s_useSurfaceDistance && _geodesicDistanceCache != null)
                {
                    if (!_geodesicDistanceCache.TryGetValue(i, out float geodesicDist))
                    {
                        continue;
                    }
                    float t = geodesicDist / s_brushRadius;
                    falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);
                }
                else
                {
                    float distSq = (vertex - localHitPoint).sqrMagnitude;
                    if (distSq > radiusSq) continue;

                    float dist = Mathf.Sqrt(distSq);
                    float t = dist / s_brushRadius;
                    falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);
                }

                var worldPos = matrix.MultiplyPoint3x4(vertex);
                float dotSize = s_vertexDotSize * falloff;
                if (dotSize < 0.5f) continue;

                Color dotColor = Color.Lerp(new Color(brushColor.r, brushColor.g, brushColor.b, 0.15f), brushColor, falloff);
                Handles.color = dotColor;
                Handles.DrawSolidDisc(worldPos, Camera.current != null ? Camera.current.transform.forward : Vector3.forward, HandleUtility.GetHandleSize(worldPos) * 0.004f * dotSize);
            }
        }

        private void DrawDisplacementHeatmap(LatticeDeformer deformer, Transform meshTransform)
        {
            if (_meshVertices == null) return;

            var displacements = deformer.Displacements;
            if (displacements == null || displacements.Length == 0) return;

            int vertexCount = Mathf.Min(_meshVertices.Length, displacements.Length);
            var matrix = meshTransform.localToWorldMatrix;

            // Find max displacement for normalization
            float maxMag = 0f;
            for (int i = 0; i < vertexCount; i++)
            {
                float mag = displacements[i].sqrMagnitude;
                if (mag > maxMag) maxMag = mag;
            }

            if (maxMag < 1e-12f) return;
            maxMag = Mathf.Sqrt(maxMag);

            var camForward = Camera.current != null ? Camera.current.transform.forward : Vector3.forward;

            for (int i = 0; i < vertexCount; i++)
            {
                float mag = displacements[i].magnitude;
                if (mag < 1e-6f) continue;

                float normalized = Mathf.Clamp01(mag / maxMag);
                var vertex = _meshVertices[i] + displacements[i];
                var worldPos = matrix.MultiplyPoint3x4(vertex);

                // Blue (low) -> Cyan -> Green -> Yellow -> Red (high)
                Color heatColor = HeatmapColor(normalized);
                heatColor.a = 0.3f + 0.6f * normalized;

                Handles.color = heatColor;
                float dotRadius = HandleUtility.GetHandleSize(worldPos) * 0.003f * (1f + normalized * 2f);
                Handles.DrawSolidDisc(worldPos, camForward, dotRadius);
            }
        }

        private static Color HeatmapColor(float t)
        {
            // 0.0=blue -> 0.25=cyan -> 0.5=green -> 0.75=yellow -> 1.0=red
            if (t < 0.25f)
                return Color.Lerp(new Color(0f, 0.2f, 1f), new Color(0f, 0.8f, 1f), t * 4f);
            if (t < 0.5f)
                return Color.Lerp(new Color(0f, 0.8f, 1f), new Color(0.2f, 1f, 0.2f), (t - 0.25f) * 4f);
            if (t < 0.75f)
                return Color.Lerp(new Color(0.2f, 1f, 0.2f), new Color(1f, 1f, 0f), (t - 0.5f) * 4f);
            return Color.Lerp(new Color(1f, 1f, 0f), new Color(1f, 0.1f, 0f), (t - 0.75f) * 4f);
        }

        private void DrawVertexMaskVisualization(LatticeDeformer deformer, Transform meshTransform)
        {
            if (_meshVertices == null) return;
            if (!TryGetActiveLayer(deformer, out var layer)) return;
            if (!layer.HasVertexMask()) return;

            var mask = layer.VertexMask;
            if (mask == null || mask.Length == 0) return;

            int vertexCount = Mathf.Min(_meshVertices.Length, mask.Length);
            var matrix = meshTransform.localToWorldMatrix;
            var camForward = Camera.current != null ? Camera.current.transform.forward : Vector3.forward;

            for (int i = 0; i < vertexCount; i++)
            {
                float maskValue = mask[i];
                if (maskValue > 1f - 1e-6f) continue; // Fully editable, skip

                var vertex = _meshVertices[i] + deformer.GetDisplacement(i);
                var worldPos = matrix.MultiplyPoint3x4(vertex);

                // Red = protected (mask=0), Green = editable (mask=1)
                float protection = 1f - maskValue;
                Color dotColor = Color.Lerp(new Color(0.2f, 1f, 0.2f, 0.4f), new Color(1f, 0.2f, 0.2f, 0.8f), protection);
                Handles.color = dotColor;
                float dotRadius = HandleUtility.GetHandleSize(worldPos) * 0.004f * (1f + protection * 2f);
                Handles.DrawSolidDisc(worldPos, camForward, dotRadius);
            }
        }

        private static bool IntersectRayMesh(Ray ray, Mesh mesh, Matrix4x4 matrix, out RaycastHit hit)
        {
            if (!s_intersectRayMeshResolved)
            {
                s_intersectRayMeshResolved = true;
                s_intersectRayMeshMethod = typeof(HandleUtility).GetMethod(
                    "IntersectRayMesh",
                    BindingFlags.Static | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(Ray), typeof(Mesh), typeof(Matrix4x4), typeof(RaycastHit).MakeByRefType() },
                    null);
            }

            hit = default;
            if (s_intersectRayMeshMethod == null) return false;

            var args = new object[] { ray, mesh, matrix, null };
            var result = (bool)s_intersectRayMeshMethod.Invoke(null, args);
            if (result)
            {
                hit = (RaycastHit)args[3];
            }
            return result;
        }

        private void EnsureAdjacencyBuilt()
        {
            if (_adjacency != null) return;
            if (_meshVertices == null || _meshTriangles == null) return;

            int vertexCount = _meshVertices.Length;
            _adjacency = new List<HashSet<int>>(vertexCount);
            for (int i = 0; i < vertexCount; i++)
            {
                _adjacency.Add(new HashSet<int>());
            }

            int triCount = _meshTriangles.Length;
            for (int i = 0; i < triCount; i += 3)
            {
                int a = _meshTriangles[i];
                int b = _meshTriangles[i + 1];
                int c = _meshTriangles[i + 2];

                if (a < vertexCount && b < vertexCount)
                {
                    _adjacency[a].Add(b);
                    _adjacency[b].Add(a);
                }
                if (b < vertexCount && c < vertexCount)
                {
                    _adjacency[b].Add(c);
                    _adjacency[c].Add(b);
                }
                if (a < vertexCount && c < vertexCount)
                {
                    _adjacency[a].Add(c);
                    _adjacency[c].Add(a);
                }
            }
        }

        private int FindNearestVertex(Vector3 localPoint)
        {
            if (_meshVertices == null || _meshVertices.Length == 0)
            {
                return -1;
            }

            int nearest = -1;
            float nearestDistSq = float.MaxValue;
            for (int i = 0; i < _meshVertices.Length; i++)
            {
                float distSq = (_meshVertices[i] - localPoint).sqrMagnitude;
                if (distSq < nearestDistSq)
                {
                    nearestDistSq = distSq;
                    nearest = i;
                }
            }

            return nearest;
        }

        private HashSet<int> FindConnectedVertices(int startVertex, float maxDistance)
        {
            var connected = new HashSet<int>();
            if (_adjacency == null || startVertex < 0 || startVertex >= _adjacency.Count)
            {
                return connected;
            }

            var queue = new Queue<int>();
            queue.Enqueue(startVertex);
            connected.Add(startVertex);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (!(_adjacency[current] is { } neighbors))
                {
                    continue;
                }

                foreach (int neighbor in neighbors)
                {
                    if (connected.Contains(neighbor))
                    {
                        continue;
                    }

                    // Only include vertices within brush radius (Euclidean check for performance)
                    float distSq = (_meshVertices[neighbor] - _meshVertices[startVertex]).sqrMagnitude;
                    if (distSq <= maxDistance * maxDistance)
                    {
                        connected.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            return connected;
        }

        private void UpdateConnectedVerticesCache(Vector3 localHitPoint)
        {
            if (!s_connectedOnly)
            {
                _connectedVerticesCache = null;
                _connectedCacheStartVertex = -1;
                return;
            }

            EnsureAdjacencyBuilt();

            int nearestVertex = FindNearestVertex(localHitPoint);
            if (nearestVertex < 0)
            {
                _connectedVerticesCache = null;
                _connectedCacheStartVertex = -1;
                return;
            }

            // Only rebuild cache if the start vertex changed
            if (nearestVertex != _connectedCacheStartVertex)
            {
                _connectedCacheStartVertex = nearestVertex;
                _connectedVerticesCache = FindConnectedVertices(nearestVertex, s_brushRadius);
            }
        }

        private void ClearConnectedVerticesCache()
        {
            _connectedVerticesCache = null;
            _connectedCacheStartVertex = -1;
        }

        private void UpdateGeodesicDistanceCache(Vector3 localHitPoint)
        {
            if (!s_useSurfaceDistance)
            {
                _geodesicDistanceCache = null;
                _geodesicCacheStartVertex = -1;
                return;
            }

            EnsureAdjacencyBuilt();
            int nearest = FindNearestVertex(localHitPoint);
            if (nearest < 0 || nearest == _geodesicCacheStartVertex)
            {
                return;
            }

            _geodesicCacheStartVertex = nearest;
            _geodesicDistanceCache = GeodesicDistanceCalculator.ComputeDistances(
                nearest, s_brushRadius, _adjacency, _meshVertices);
        }

        private void ClearGeodesicDistanceCache()
        {
            _geodesicDistanceCache = null;
            _geodesicCacheStartVertex = -1;
        }

        internal static void ClearAllDisplacements(LatticeDeformer deformer)
        {
            if (deformer == null) return;
            if (deformer.ActiveLayerType != MeshDeformerLayerType.Brush) return;
            Undo.RecordObject(deformer, LatticeLocalization.Tr("Clear All"));
            deformer.ClearDisplacements();
            bool assignToRenderer = LatticePreviewUtility.ShouldAssignRuntimeMesh();
            deformer.Deform(assignToRenderer);
            LatticePreviewUtility.RequestSceneRepaint();
        }

        internal static void ClearActiveMask(LatticeDeformer deformer)
        {
            if (deformer == null) return;
            if (!TryGetActiveLayer(deformer, out var layer)) return;
            Undo.RecordObject(deformer, LatticeLocalization.Tr("Clear Mask"));
            layer.ClearVertexMask();
            bool assignToRenderer = LatticePreviewUtility.ShouldAssignRuntimeMesh();
            deformer.Deform(assignToRenderer);
            LatticePreviewUtility.RequestSceneRepaint();
        }
    }

    [Overlay(typeof(SceneView), "Brush Tool", defaultDisplay = true)]
    internal sealed class BrushDeformerToolOverlay : IMGUIOverlay, ITransientOverlay
    {
        public bool visible => ToolManager.activeToolType == typeof(BrushDeformerTool);

        public override void OnGUI()
        {
            displayName = LatticeLocalization.Tr("Brush Tool");

            if (ToolManager.activeToolType != typeof(BrushDeformerTool))
            {
                GUILayout.Label(LatticeLocalization.Content("Brush Tool"), EditorStyles.miniLabel);
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
                GUILayout.Label(LatticeLocalization.Content("Brush Tool"), EditorStyles.boldLabel);

                DrawLanguageSelector();
                GUILayout.Space(4f);

                // Brush Mode dropdown
                var modeContent = new GUIContent[]
                {
                    LatticeLocalization.Content("Normal"),
                    LatticeLocalization.Content("Move"),
                    LatticeLocalization.Content("Smooth"),
                    LatticeLocalization.Content("Mask")
                };
                int modeIndex = EditorGUILayout.Popup(
                    LatticeLocalization.Content("Brush Mode"),
                    (int)BrushDeformerTool.CurrentBrushMode,
                    modeContent);
                modeIndex = Mathf.Clamp(modeIndex, 0, modeContent.Length - 1);
                BrushDeformerTool.CurrentBrushMode = (BrushDeformerTool.BrushMode)modeIndex;

                GUILayout.Space(2f);

                // Brush Radius slider
                BrushDeformerTool.BrushRadius = EditorGUILayout.Slider(
                    LatticeLocalization.Content("Brush Radius"),
                    BrushDeformerTool.BrushRadius, 0.001f, 1.0f);

                // Brush Strength slider
                BrushDeformerTool.BrushStrength = EditorGUILayout.Slider(
                    LatticeLocalization.Content("Brush Strength"),
                    BrushDeformerTool.BrushStrength, 0.0f, 1.0f);

                // Falloff type dropdown
                var falloffContent = new GUIContent[]
                {
                    LatticeLocalization.Content("Smooth"),
                    LatticeLocalization.Content("Linear"),
                    new GUIContent("Constant")
                };
                int falloffIndex = EditorGUILayout.Popup(
                    LatticeLocalization.Content("Brush Falloff"),
                    (int)BrushDeformerTool.BrushFalloff,
                    falloffContent);
                falloffIndex = Mathf.Clamp(falloffIndex, 0, falloffContent.Length - 1);
                BrushDeformerTool.BrushFalloff = (BrushFalloffType)falloffIndex;

                GUILayout.Space(2f);

                // Invert brush toggle
                BrushDeformerTool.InvertBrush = GUILayout.Toggle(
                    BrushDeformerTool.InvertBrush,
                    LatticeLocalization.Content("Invert Brush"));

                // Connected only toggle
                BrushDeformerTool.ConnectedOnly = GUILayout.Toggle(
                    BrushDeformerTool.ConnectedOnly,
                    LatticeLocalization.Content("Connected Only"));

                // Surface distance toggle
                BrushDeformerTool.UseSurfaceDistance = GUILayout.Toggle(
                    BrushDeformerTool.UseSurfaceDistance,
                    LatticeLocalization.Content("Surface Distance"));

                GUILayout.Space(4f);

                // Mirror editing toggle
                BrushDeformerTool.MirrorEditing = GUILayout.Toggle(
                    BrushDeformerTool.MirrorEditing,
                    LatticeLocalization.Content("Enable Mirror"));

                using (new EditorGUI.DisabledScope(!BrushDeformerTool.MirrorEditing))
                {
                    GUILayout.Label(LatticeLocalization.Content("Mirror Axis"), EditorStyles.miniLabel);
                    int axisSelection = GUILayout.Toolbar(
                        (int)BrushDeformerTool.CurrentMirrorAxis,
                        BrushDeformerTool.AxisOptions);
                    axisSelection = Mathf.Clamp(axisSelection, 0, BrushDeformerTool.AxisOptions.Length - 1);
                    BrushDeformerTool.CurrentMirrorAxis = (BrushDeformerTool.MirrorAxis)axisSelection;
                }

                GUILayout.Space(4f);

                // Visualization toggles
                GUILayout.Label(LatticeLocalization.Content("Visualization"), EditorStyles.boldLabel);
                BrushDeformerTool.ShowAffectedVertices = GUILayout.Toggle(
                    BrushDeformerTool.ShowAffectedVertices,
                    LatticeLocalization.Content("Show Affected Vertices"));
                BrushDeformerTool.ShowDisplacementHeatmap = GUILayout.Toggle(
                    BrushDeformerTool.ShowDisplacementHeatmap,
                    LatticeLocalization.Content("Show Displacement Heatmap"));
                BrushDeformerTool.VertexDotSize = EditorGUILayout.Slider(
                    LatticeLocalization.Content("Dot Size"),
                    BrushDeformerTool.VertexDotSize, 1f, 8f);

                GUILayout.Space(4f);

                // Clear All button
                if (GUILayout.Button(LatticeLocalization.Content("Clear All")))
                {
                    var activeToolObj = ToolManager.activeToolType == typeof(BrushDeformerTool)
                        ? Selection.activeGameObject
                        : null;
                    if (activeToolObj != null)
                    {
                        var deformer = activeToolObj.GetComponent<LatticeDeformer>();
                        if (deformer != null && deformer.ActiveLayerType == MeshDeformerLayerType.Brush)
                        {
                            BrushDeformerTool.ClearAllDisplacements(deformer);
                        }
                    }
                }

                // Clear Mask button (only visible in Mask mode)
                if (BrushDeformerTool.CurrentBrushMode == BrushDeformerTool.BrushMode.Mask)
                {
                    if (GUILayout.Button(LatticeLocalization.Content("Clear Mask")))
                    {
                        if (selectedDeformer != null && selectedDeformer.ActiveLayerType == MeshDeformerLayerType.Brush)
                        {
                            BrushDeformerTool.ClearActiveMask(selectedDeformer);
                        }
                    }
                }

                GUILayout.Space(2f);
                GUILayout.Label(LatticeLocalization.Tr("Alt+Scroll: Radius / Shift+Scroll: Strength"), EditorStyles.miniLabel);
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
