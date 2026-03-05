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
    [EditorTool("Brush Tool", typeof(BrushDeformer))]
    public sealed class BrushDeformerTool : EditorTool
    {
        internal enum BrushMode
        {
            Normal = 0,
            Move = 1,
            Smooth = 2
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

        private Mesh _cachedMesh;
        private Vector3[] _meshVertices;
        private Vector3[] _meshNormals;
        private int[] _meshTriangles;
        private List<HashSet<int>> _adjacency;
        private bool _isPainting;
        private Vector2 _lastMousePosition;

        private static MethodInfo s_intersectRayMeshMethod;
        private static bool s_intersectRayMeshResolved;

        private static readonly Color k_NormalBrushColor = new Color(0.3f, 0.5f, 1f, 0.8f);
        private static readonly Color k_SmoothBrushColor = new Color(0.3f, 1f, 0.5f, 0.8f);
        private static readonly Color k_MoveBrushColor = new Color(1f, 0.6f, 0.2f, 0.8f);

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

        public override void OnActivated()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            _isPainting = false;
            SceneView.RepaintAll();
        }

        public override void OnWillBeDeactivated()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            _isPainting = false;
            InvalidateCache();
        }

        private void OnUndoRedo()
        {
            if (target is BrushDeformer deformer)
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

            if (target is not BrushDeformer deformer)
            {
                return;
            }

            var sourceMesh = deformer.SourceMesh;
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
                    _isPainting = true;
                    _lastMousePosition = evt.mousePosition;

                    Undo.RecordObject(deformer, GetUndoLabel());
                    deformer.EnsureDisplacementCapacity();

                    ApplyBrush(deformer, meshTransform, localHitPoint, localHitNormal, evt);
                    evt.Use();
                }
                else if (evt.type == EventType.MouseUp && evt.button == 0)
                {
                    _isPainting = false;
                    evt.Use();
                }
            }
            else
            {
                // Even without hit, handle mouse up to stop painting
                if (evt.type == EventType.MouseUp && evt.button == 0)
                {
                    _isPainting = false;
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

        private void ApplyBrush(BrushDeformer deformer, Transform meshTransform, Vector3 localHitPoint, Vector3 localHitNormal, Event evt)
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

        private bool ApplyNormalBrush(BrushDeformer deformer, Vector3 localHitPoint, float radiusSq, float strength, float direction)
        {
            bool modified = false;
            int vertexCount = _meshVertices.Length;

            for (int i = 0; i < vertexCount; i++)
            {
                var vertex = _meshVertices[i] + deformer.GetDisplacement(i);
                float distSq = (vertex - localHitPoint).sqrMagnitude;
                if (distSq > radiusSq) continue;

                float dist = Mathf.Sqrt(distSq);
                float t = dist / s_brushRadius;
                float falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);

                var normal = _meshNormals[i].normalized;
                if (normal.sqrMagnitude < 0.001f) normal = Vector3.up;

                var delta = normal * (strength * falloff * direction);
                deformer.AddDisplacement(i, delta);
                modified = true;
            }

            return modified;
        }

        private bool ApplyMoveBrush(BrushDeformer deformer, Transform meshTransform, Vector3 localHitPoint, float radiusSq, float strength, Event evt)
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
                var vertex = _meshVertices[i] + deformer.GetDisplacement(i);
                float distSq = (vertex - localHitPoint).sqrMagnitude;
                if (distSq > radiusSq) continue;

                float dist = Mathf.Sqrt(distSq);
                float t = dist / s_brushRadius;
                float falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);

                var delta = localDelta * (strength * falloff * 10f);
                deformer.AddDisplacement(i, delta);
                modified = true;
            }

            return modified;
        }

        private bool ApplySmoothBrush(BrushDeformer deformer, Vector3 localHitPoint, float radiusSq, float strength)
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
                var vertex = _meshVertices[i] + currentDisplacements[i];
                float distSq = (vertex - localHitPoint).sqrMagnitude;
                if (distSq > radiusSq) continue;

                float dist = Mathf.Sqrt(distSq);
                float t = dist / s_brushRadius;
                float falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);

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
                var targetDisp = Vector3.Lerp(currentDisp, averageDisp, smoothFactor * falloff);
                deformer.SetDisplacement(i, targetDisp);
                modified = true;
            }

            return modified;
        }

        private void ApplyMirror(BrushDeformer deformer, Vector3 localHitPoint, float radiusSq, float strength, float direction)
        {
            if (_meshVertices == null || _meshVertices.Length == 0) return;

            // Mirror the brush center
            var mirroredCenter = MirrorPosition(localHitPoint);
            int vertexCount = _meshVertices.Length;

            switch (s_brushMode)
            {
                case BrushMode.Normal:
                {
                    for (int i = 0; i < vertexCount; i++)
                    {
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
                default: return k_NormalBrushColor;
            }
        }

        private string GetUndoLabel()
        {
            switch (s_brushMode)
            {
                case BrushMode.Smooth: return LatticeLocalization.Tr("Brush Smooth");
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
        }

        private void DrawAffectedVertices(BrushDeformer deformer, Vector3 localHitPoint, Transform meshTransform)
        {
            float radiusSq = s_brushRadius * s_brushRadius;
            int vertexCount = _meshVertices.Length;
            Color brushColor = GetBrushColor();
            var matrix = meshTransform.localToWorldMatrix;

            for (int i = 0; i < vertexCount; i++)
            {
                var vertex = _meshVertices[i] + deformer.GetDisplacement(i);
                float distSq = (vertex - localHitPoint).sqrMagnitude;
                if (distSq > radiusSq) continue;

                float dist = Mathf.Sqrt(distSq);
                float t = dist / s_brushRadius;
                float falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);

                var worldPos = matrix.MultiplyPoint3x4(vertex);
                float dotSize = s_vertexDotSize * falloff;
                if (dotSize < 0.5f) continue;

                Color dotColor = Color.Lerp(new Color(brushColor.r, brushColor.g, brushColor.b, 0.15f), brushColor, falloff);
                Handles.color = dotColor;
                Handles.DrawSolidDisc(worldPos, Camera.current != null ? Camera.current.transform.forward : Vector3.forward, HandleUtility.GetHandleSize(worldPos) * 0.004f * dotSize);
            }
        }

        private void DrawDisplacementHeatmap(BrushDeformer deformer, Transform meshTransform)
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

        internal static void ClearAllDisplacements(BrushDeformer deformer)
        {
            if (deformer == null) return;
            Undo.RecordObject(deformer, LatticeLocalization.Tr("Clear All"));
            deformer.ClearDisplacements();
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
                    LatticeLocalization.Content("Smooth")
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
                        var deformer = activeToolObj.GetComponent<BrushDeformer>();
                        if (deformer != null)
                        {
                            BrushDeformerTool.ClearAllDisplacements(deformer);
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
