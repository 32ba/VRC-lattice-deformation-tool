#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Net._32Ba.LatticeDeformationTool;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal sealed class BrushToolHandler
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
        private static float s_brushRadius = 0.02f; // world-space units (meters)
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
        private static bool s_backfaceCulling = false;
        private static bool s_showPenetration = false;
        private static Renderer s_penetrationReference = null;

        // Overlay foldout states
        private static bool s_showMirrorSection = false;
        private static bool s_showAdvancedSection = false;
        private static bool s_showVisualizationSection = false;

        private LatticeDeformer _activeDeformer;

        private Mesh _cachedMesh;
        private Vector3[] _meshVertices;
        private Vector3[] _meshNormals;
        private int[] _meshTriangles;
        private Vector3[] _worldPositions; // Skinned world-space positions (null for MeshRenderer)
        private List<HashSet<int>> _adjacency;
        private Vector2 _lastMousePosition;
        private HashSet<int> _connectedVerticesCache;
        private int _connectedCacheStartVertex = -1;
        private Dictionary<int, float> _geodesicDistanceCache;
        private int _geodesicCacheStartVertex = -1;
        private HashSet<int> _penetratingVertices;

        private static MethodInfo s_intersectRayMeshMethod;
        private static bool s_intersectRayMeshResolved;

        private static readonly Color k_NormalBrushColor = new Color(0.3f, 0.5f, 1f, 0.8f);
        private static readonly Color k_SmoothBrushColor = new Color(0.3f, 1f, 0.5f, 0.8f);
        private static readonly Color k_MoveBrushColor = new Color(1f, 0.6f, 0.2f, 0.8f);
        private static readonly Color k_MaskBrushColor = new Color(1f, 0.3f, 0.3f, 0.8f);

        static BrushToolHandler()
        {
            LatticeLocalization.LanguageChanged += OnLanguageChanged;
        }

        private static void OnLanguageChanged()
        {
            if (s_icon != null)
            {
                s_icon.tooltip = LatticeLocalization.Tr(LocKey.BrushTool);
            }

            SceneView.RepaintAll();
        }

        internal static float BrushRadius
        {
            get => s_brushRadius;
            set
            {
                s_brushRadius = Mathf.Max(value, 1e-6f);
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

        internal static bool BackfaceCulling
        {
            get => s_backfaceCulling;
            set
            {
                if (s_backfaceCulling == value) return;
                s_backfaceCulling = value;
                SceneView.RepaintAll();
            }
        }

        internal static bool ShowPenetration
        {
            get => s_showPenetration;
            set
            {
                if (s_showPenetration == value) return;
                s_showPenetration = value;
                SceneView.RepaintAll();
            }
        }

        internal static Renderer PenetrationReference
        {
            get => s_penetrationReference;
            set
            {
                if (s_penetrationReference == value) return;
                s_penetrationReference = value;
                SceneView.RepaintAll();
            }
        }

        internal static GUIContent[] AxisOptions => new[]
        {
            LatticeLocalization.Content(LocKey.X),
            LatticeLocalization.Content(LocKey.Y),
            LatticeLocalization.Content(LocKey.Z)
        };

        /// <summary>
        /// Converts the world-space brush radius to mesh-local space.
        /// </summary>
        private float WorldToLocalRadius()
        {
            if (_activeDeformer == null) return s_brushRadius;
            var scale = _activeDeformer.MeshTransform.lossyScale;
            float avg = (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f;
            return s_brushRadius / Mathf.Max(avg, 1e-6f);
        }

        internal void Activate(LatticeDeformer deformer)
        {
            _activeDeformer = deformer;
            Undo.undoRedoPerformed += OnUndoRedo;
            SceneView.RepaintAll();
        }

        internal void Deactivate()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
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

            SceneView.RepaintAll();
        }

        internal void OnToolGUI(EditorWindow window, LatticeDeformer deformer)
        {
            UnityEngine.Profiling.Profiler.BeginSample("BrushTool.OnToolGUI");
            if (Event.current != null && Event.current.commandName == "UndoRedoPerformed")
            {
                UnityEngine.Profiling.Profiler.EndSample();
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

            // Penetration detection
            if (s_showPenetration)
            {
                UpdatePenetrationDetection(deformer);
                DrawPenetrationHighlight(meshTransform);
            }

            // Raycast to find brush center on mesh surface.
            // For SkinnedMeshRenderer, raycast against the baked (posed) mesh so the hit
            // position matches the visual. For MeshRenderer, use source mesh directly.
            var mouseRay = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
            bool hitSurface;
            RaycastHit hit;
            bool usedBakedMesh = SkinnedVertexHelper.TryGetBakedMeshForRaycast(
                deformer, out var bakedMesh, out var bakedMatrix);

            if (usedBakedMesh)
            {
                hitSurface = IntersectRayMesh(mouseRay, bakedMesh, bakedMatrix, out hit);
            }
            else
            {
                hitSurface = IntersectRayMesh(mouseRay, sourceMesh, meshTransform.localToWorldMatrix, out hit);
            }

            if (hitSurface)
            {
                // Convert hit to source mesh local space for brush calculations.
                // For baked meshes, use triangle index + barycentric coords to map back
                // to source mesh space (since baked positions differ from bind pose).
                Vector3 localHitPoint;
                Vector3 localHitNormal;

                if (usedBakedMesh && hit.triangleIndex >= 0 && _meshTriangles != null &&
                    hit.triangleIndex * 3 + 2 < _meshTriangles.Length)
                {
                    int triBase = hit.triangleIndex * 3;
                    int i0 = _meshTriangles[triBase];
                    int i1 = _meshTriangles[triBase + 1];
                    int i2 = _meshTriangles[triBase + 2];
                    var bary = hit.barycentricCoordinate;

                    // Interpolate in source mesh local space
                    var v0 = _meshVertices[i0] + deformer.GetDisplacement(i0);
                    var v1 = _meshVertices[i1] + deformer.GetDisplacement(i1);
                    var v2 = _meshVertices[i2] + deformer.GetDisplacement(i2);
                    localHitPoint = v0 * bary.x + v1 * bary.y + v2 * bary.z;

                    if (_meshNormals != null && _meshNormals.Length > Mathf.Max(i0, Mathf.Max(i1, i2)))
                    {
                        localHitNormal = (_meshNormals[i0] * bary.x +
                                          _meshNormals[i1] * bary.y +
                                          _meshNormals[i2] * bary.z).normalized;
                    }
                    else
                    {
                        localHitNormal = meshTransform.InverseTransformDirection(hit.normal).normalized;
                    }
                }
                else
                {
                    localHitPoint = meshTransform.InverseTransformPoint(hit.point);
                    localHitNormal = meshTransform.InverseTransformDirection(hit.normal).normalized;
                }

                // Draw brush disc at the visual hit position in world space.
                // The raycast hit point is where the user sees the mesh (post-skinning),
                // so we draw there. The radius is in mesh-local space, so scale it to world.
                var prevMatrix = Handles.matrix;
                Handles.matrix = Matrix4x4.identity;

                // s_brushRadius is in world-space units — draw directly
                Color brushColor = GetBrushColor();
                Handles.color = brushColor;
                Handles.DrawWireDisc(hit.point, hit.normal, s_brushRadius);
                Color fillColor = brushColor;
                fillColor.a = 0.1f;
                Handles.color = fillColor;
                Handles.DrawSolidDisc(hit.point, hit.normal, s_brushRadius);

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
            UnityEngine.Profiling.Profiler.EndSample();
        }

        private void ApplyBrush(LatticeDeformer deformer, Transform meshTransform, Vector3 localHitPoint, Vector3 localHitNormal, Event evt)
        {
            if (_meshVertices == null || _meshVertices.Length == 0)
            {
                return;
            }

            deformer.EnsureDisplacementCapacity();

            // Convert world-space radius to mesh-local space for distance comparisons
            var meshScale = meshTransform.lossyScale;
            float avgScale = (Mathf.Abs(meshScale.x) + Mathf.Abs(meshScale.y) + Mathf.Abs(meshScale.z)) / 3f;
            float localRadius = s_brushRadius / Mathf.Max(avgScale, 1e-6f);
            float effectiveStrength = s_brushStrength;
            if (evt != null && evt.control)
            {
                effectiveStrength *= 0.1f;
            }
            float strength = effectiveStrength * 0.01f;
            float direction = s_invertBrush ? -1f : 1f;
            bool assignToRenderer = LatticePreviewUtility.ShouldAssignRuntimeMesh();

            bool modified = false;

            switch (s_brushMode)
            {
                case BrushMode.Normal:
                    modified = ApplyNormalBrush(deformer, localHitPoint, localRadius, strength, direction);
                    break;

                case BrushMode.Move:
                    modified = ApplyMoveBrush(deformer, meshTransform, localHitPoint, localRadius, strength, evt);
                    break;

                case BrushMode.Smooth:
                    modified = ApplySmoothBrush(deformer, localHitPoint, localRadius, strength);
                    break;

                case BrushMode.Mask:
                    modified = ApplyMaskBrush(deformer, localHitPoint, localRadius);
                    break;
            }

            if (modified)
            {
                if (s_mirrorEditing)
                {
                    ApplyMirror(deformer, localHitPoint, localRadius, strength, direction);
                }

                deformer.Deform(assignToRenderer);
                LatticePreviewUtility.RequestSceneRepaint();
            }

            _lastMousePosition = evt.mousePosition;
        }

        private bool ApplyNormalBrush(LatticeDeformer deformer, Vector3 localHitPoint, float localRadius, float strength, float direction)
        {
            float radiusSq = localRadius * localRadius;
            bool modified = false;
            int vertexCount = _meshVertices.Length;

            // Pre-compute camera forward in local space for backface culling
            Vector3 localCameraForward = Vector3.forward;
            if (s_backfaceCulling)
            {
                var cam = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
                if (cam != null)
                {
                    var deformerTransform = deformer.MeshTransform;
                    if (deformerTransform != null)
                    {
                        localCameraForward = deformerTransform.InverseTransformDirection(cam.transform.forward);
                    }
                }
            }

            for (int i = 0; i < vertexCount; i++)
            {
                if (s_connectedOnly && _connectedVerticesCache != null && !_connectedVerticesCache.Contains(i))
                {
                    continue;
                }

                if (s_backfaceCulling && _meshNormals != null && i < _meshNormals.Length)
                {
                    if (Vector3.Dot(_meshNormals[i], localCameraForward) > 0f)
                    {
                        continue;
                    }
                }

                float falloff;
                if (s_useSurfaceDistance && _geodesicDistanceCache != null)
                {
                    if (!_geodesicDistanceCache.TryGetValue(i, out float geodesicDist))
                    {
                        continue; // Not reachable via surface
                    }
                    float t = geodesicDist / localRadius;
                    falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);
                }
                else
                {
                    var vertex = _meshVertices[i] + deformer.GetDisplacement(i);
                    float distSq = (vertex - localHitPoint).sqrMagnitude;
                    if (distSq > radiusSq) continue;

                    float dist = Mathf.Sqrt(distSq);
                    float t = dist / localRadius;
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

        private bool ApplyMoveBrush(LatticeDeformer deformer, Transform meshTransform, Vector3 localHitPoint, float localRadius, float strength, Event evt)
        {
            float radiusSq = localRadius * localRadius;
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

            // Pre-compute camera forward in local space for backface culling
            Vector3 localCameraForward = Vector3.forward;
            if (s_backfaceCulling && meshTransform != null)
            {
                localCameraForward = meshTransform.InverseTransformDirection(camera.transform.forward);
            }

            bool modified = false;
            int vertexCount = _meshVertices.Length;

            for (int i = 0; i < vertexCount; i++)
            {
                if (s_connectedOnly && _connectedVerticesCache != null && !_connectedVerticesCache.Contains(i))
                {
                    continue;
                }

                if (s_backfaceCulling && _meshNormals != null && i < _meshNormals.Length)
                {
                    if (Vector3.Dot(_meshNormals[i], localCameraForward) > 0f)
                    {
                        continue;
                    }
                }

                float falloff;
                if (s_useSurfaceDistance && _geodesicDistanceCache != null)
                {
                    if (!_geodesicDistanceCache.TryGetValue(i, out float geodesicDist))
                    {
                        continue; // Not reachable via surface
                    }
                    float t = geodesicDist / localRadius;
                    falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);
                }
                else
                {
                    var vertex = _meshVertices[i] + deformer.GetDisplacement(i);
                    float distSq = (vertex - localHitPoint).sqrMagnitude;
                    if (distSq > radiusSq) continue;

                    float dist = Mathf.Sqrt(distSq);
                    float t = dist / localRadius;
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

        private bool ApplySmoothBrush(LatticeDeformer deformer, Vector3 localHitPoint, float localRadius, float strength)
        {
            float radiusSq = localRadius * localRadius;
            EnsureAdjacencyBuilt();

            bool modified = false;
            int vertexCount = _meshVertices.Length;

            // Pre-compute camera forward in local space for backface culling
            Vector3 localCameraForward = Vector3.forward;
            if (s_backfaceCulling)
            {
                var cam = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
                if (cam != null)
                {
                    var deformerTransform = deformer.MeshTransform;
                    if (deformerTransform != null)
                    {
                        localCameraForward = deformerTransform.InverseTransformDirection(cam.transform.forward);
                    }
                }
            }

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

                if (s_backfaceCulling && _meshNormals != null && i < _meshNormals.Length)
                {
                    if (Vector3.Dot(_meshNormals[i], localCameraForward) > 0f)
                    {
                        continue;
                    }
                }

                float falloff;
                if (s_useSurfaceDistance && _geodesicDistanceCache != null)
                {
                    if (!_geodesicDistanceCache.TryGetValue(i, out float geodesicDist))
                    {
                        continue; // Not reachable via surface
                    }
                    float t = geodesicDist / localRadius;
                    falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);
                }
                else
                {
                    var vertex = _meshVertices[i] + currentDisplacements[i];
                    float distSq = (vertex - localHitPoint).sqrMagnitude;
                    if (distSq > radiusSq) continue;

                    float dist = Mathf.Sqrt(distSq);
                    float t = dist / localRadius;
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

        private bool ApplyMaskBrush(LatticeDeformer deformer, Vector3 localHitPoint, float localRadius)
        {
            float radiusSq = localRadius * localRadius;
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

            // Pre-compute camera forward in local space for backface culling
            Vector3 localCameraForward = Vector3.forward;
            if (s_backfaceCulling)
            {
                var cam = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
                if (cam != null)
                {
                    var deformerTransform = deformer.MeshTransform;
                    if (deformerTransform != null)
                    {
                        localCameraForward = deformerTransform.InverseTransformDirection(cam.transform.forward);
                    }
                }
            }

            for (int i = 0; i < _meshVertices.Length; i++)
            {
                if (s_connectedOnly && _connectedVerticesCache != null && !_connectedVerticesCache.Contains(i))
                {
                    continue;
                }

                if (s_backfaceCulling && _meshNormals != null && i < _meshNormals.Length)
                {
                    if (Vector3.Dot(_meshNormals[i], localCameraForward) > 0f)
                    {
                        continue;
                    }
                }

                float falloff;
                if (s_useSurfaceDistance && _geodesicDistanceCache != null)
                {
                    if (!_geodesicDistanceCache.TryGetValue(i, out float geodesicDist))
                    {
                        continue;
                    }
                    float t = geodesicDist / localRadius;
                    falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);
                }
                else
                {
                    var vertex = _meshVertices[i] + deformer.GetDisplacement(i);
                    float distSq = (vertex - localHitPoint).sqrMagnitude;
                    if (distSq > radiusSq) continue;

                    float dist = Mathf.Sqrt(distSq);
                    float t = dist / localRadius;
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

        private void ApplyMirror(LatticeDeformer deformer, Vector3 localHitPoint, float localRadius, float strength, float direction)
        {
            float radiusSq = localRadius * localRadius;
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
                    mirrorConnected = FindConnectedVertices(mirrorNearest, localRadius);
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
                        float t = dist / localRadius;
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
                        float t = dist / localRadius;
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
                        float t = dist / localRadius;
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
                case BrushMode.Smooth: return LatticeLocalization.Tr(LocKey.BrushSmooth);
                case BrushMode.Mask: return LatticeLocalization.Tr(LocKey.BrushMask);
                default: return LatticeLocalization.Tr(LocKey.BrushDeform);
            }
        }

        private void RebuildCacheIfNeeded(Mesh mesh, LatticeDeformer deformer = null)
        {
            if (mesh == null)
            {
                InvalidateCache();
                return;
            }

            if (ReferenceEquals(_cachedMesh, mesh) && _meshVertices != null)
            {
                // Refresh skinned positions each frame
                RefreshWorldPositions(deformer);
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

            RefreshWorldPositions(deformer);
        }

        private void RefreshWorldPositions(LatticeDeformer deformer)
        {
            if (deformer == null || _meshVertices == null)
            {
                _worldPositions = null;
                return;
            }

            // Use RuntimeMesh (all layers applied) if available, else source + active layer
            var runtimeMesh = deformer.RuntimeMesh;
            Vector3[] localDeformed;
            if (runtimeMesh != null && runtimeMesh.vertexCount == _meshVertices.Length)
            {
                localDeformed = runtimeMesh.vertices;
            }
            else
            {
                int count = _meshVertices.Length;
                localDeformed = new Vector3[count];
                for (int i = 0; i < count; i++)
                {
                    localDeformed[i] = _meshVertices[i] + deformer.GetDisplacement(i);
                }
            }

            // Compute skinned world positions (null for MeshRenderer)
            _worldPositions = SkinnedVertexHelper.ComputeWorldPositions(deformer, localDeformed);
        }

        private Vector3 VertexToWorld(int index, Vector3 localVertex, Matrix4x4 localToWorld)
        {
            return SkinnedVertexHelper.LocalToWorld(index, _worldPositions, null, localToWorld);
        }

        private void InvalidateCache()
        {
            _cachedMesh = null;
            _meshVertices = null;
            _meshNormals = null;
            _meshTriangles = null;
            _worldPositions = null;
            _adjacency = null;
            _penetratingVertices = null;
            ClearConnectedVerticesCache();
            ClearGeodesicDistanceCache();
        }

        private static Material s_brushDotMaterial;
        private static Texture2D s_brushCircleTex;

        private static void BeginBatchedDotDraw()
        {
            if (s_brushCircleTex == null)
            {
                const int size = 32;
                s_brushCircleTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                s_brushCircleTex.hideFlags = HideFlags.HideAndDontSave;
                s_brushCircleTex.filterMode = FilterMode.Bilinear;
                float center = (size - 1) * 0.5f;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dx = x - center, dy = y - center;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy) / center;
                        float alpha = Mathf.Clamp01(1f - Mathf.Clamp01((dist - 0.7f) / 0.3f));
                        s_brushCircleTex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    }
                s_brushCircleTex.Apply();
            }

            if (s_brushDotMaterial == null)
            {
                s_brushDotMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));
                s_brushDotMaterial.hideFlags = HideFlags.HideAndDontSave;
                s_brushDotMaterial.SetInt("_ZWrite", 0);
                s_brushDotMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                s_brushDotMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                s_brushDotMaterial.SetInt("_Cull", (int)CullMode.Off);
                s_brushDotMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
                s_brushDotMaterial.mainTexture = s_brushCircleTex;
            }

            s_brushDotMaterial.SetPass(0);
            GL.PushMatrix();
            GL.MultMatrix(Matrix4x4.identity);
            GL.Begin(GL.QUADS);
        }

        private static void EndBatchedDotDraw()
        {
            GL.End();
            GL.PopMatrix();
        }

        private static void DrawBatchedDot(Vector3 worldPos, Color col, float radius, Vector3 camRight, Vector3 camUp)
        {
            var right = camRight * radius;
            var up = camUp * radius;
            GL.Color(col);
            GL.TexCoord2(0f, 0f); GL.Vertex(worldPos - right - up);
            GL.TexCoord2(1f, 0f); GL.Vertex(worldPos + right - up);
            GL.TexCoord2(1f, 1f); GL.Vertex(worldPos + right + up);
            GL.TexCoord2(0f, 1f); GL.Vertex(worldPos - right + up);
        }

        private void DrawAffectedVertices(LatticeDeformer deformer, Vector3 localHitPoint, Transform meshTransform)
        {
            var meshScale = meshTransform.lossyScale;
            float avgScale = (Mathf.Abs(meshScale.x) + Mathf.Abs(meshScale.y) + Mathf.Abs(meshScale.z)) / 3f;
            float localRadius = s_brushRadius / Mathf.Max(avgScale, 1e-6f);
            float radiusSq = localRadius * localRadius;
            int vertexCount = _meshVertices.Length;
            Color brushColor = GetBrushColor();
            var matrix = meshTransform.localToWorldMatrix;
            var cam = Camera.current;
            if (cam == null) return;
            var camRight = cam.transform.right;
            var camUp = cam.transform.up;
            float baseSize = HandleUtility.GetHandleSize(meshTransform.position) * 0.004f;

            BeginBatchedDotDraw();
            for (int i = 0; i < vertexCount; i++)
            {
                if (s_connectedOnly && _connectedVerticesCache != null && !_connectedVerticesCache.Contains(i))
                    continue;

                var vertex = _meshVertices[i] + deformer.GetDisplacement(i);

                float falloff;
                if (s_useSurfaceDistance && _geodesicDistanceCache != null)
                {
                    if (!_geodesicDistanceCache.TryGetValue(i, out float geodesicDist))
                        continue;
                    float t = geodesicDist / localRadius;
                    falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);
                }
                else
                {
                    float distSq = (vertex - localHitPoint).sqrMagnitude;
                    if (distSq > radiusSq) continue;
                    float dist = Mathf.Sqrt(distSq);
                    float t = dist / localRadius;
                    falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);
                }

                if (falloff < 0.01f) continue;

                var worldPos = SkinnedVertexHelper.LocalToWorld(i, _worldPositions, vertex, matrix);
                Color dotColor = HeatmapColor(falloff);
                dotColor.a = 0.4f + 0.5f * falloff;
                float dotSize = Mathf.Lerp(s_vertexDotSize * 0.6f, s_vertexDotSize * 1.4f, falloff);
                DrawBatchedDot(worldPos, dotColor, baseSize * dotSize, camRight, camUp);
            }
            EndBatchedDotDraw();
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

            var cam = Camera.current;
            if (cam == null) return;
            var camRight = cam.transform.right;
            var camUp = cam.transform.up;
            float baseSize = HandleUtility.GetHandleSize(meshTransform.position) * 0.003f;

            BeginBatchedDotDraw();
            for (int i = 0; i < vertexCount; i++)
            {
                float mag = displacements[i].magnitude;
                if (mag < 1e-6f) continue;

                float normalized = Mathf.Clamp01(mag / maxMag);
                var vertex = _meshVertices[i] + displacements[i];
                var worldPos = SkinnedVertexHelper.LocalToWorld(i, _worldPositions, vertex, matrix);

                Color heatColor = HeatmapColor(normalized);
                heatColor.a = 0.3f + 0.6f * normalized;

                float dotRadius = baseSize * (1f + normalized * 2f);
                DrawBatchedDot(worldPos, heatColor, dotRadius, camRight, camUp);
            }
            EndBatchedDotDraw();
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
            var cam = Camera.current;
            if (cam == null) return;
            var camRight = cam.transform.right;
            var camUp = cam.transform.up;
            float baseSize = HandleUtility.GetHandleSize(meshTransform.position) * 0.004f;

            BeginBatchedDotDraw();
            for (int i = 0; i < vertexCount; i++)
            {
                float maskValue = mask[i];
                if (maskValue > 1f - 1e-6f) continue; // Fully editable, skip

                var vertex = _meshVertices[i] + deformer.GetDisplacement(i);
                var worldPos = SkinnedVertexHelper.LocalToWorld(i, _worldPositions, vertex, matrix);

                // Red = protected (mask=0), Green = editable (mask=1)
                float protection = 1f - maskValue;
                Color dotColor = Color.Lerp(new Color(0.2f, 1f, 0.2f, 0.4f), new Color(1f, 0.2f, 0.2f, 0.8f), protection);
                float dotRadius = baseSize * (1f + protection * 2f);
                DrawBatchedDot(worldPos, dotColor, dotRadius, camRight, camUp);
            }
            EndBatchedDotDraw();
        }

        private void UpdatePenetrationDetection(LatticeDeformer deformer)
        {
            if (!s_showPenetration || s_penetrationReference == null || _meshVertices == null)
            {
                _penetratingVertices = null;
                return;
            }

            Mesh refMesh = null;
            if (s_penetrationReference is SkinnedMeshRenderer smr)
            {
                refMesh = smr.sharedMesh;
            }
            else if (s_penetrationReference is MeshRenderer mr)
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (mf != null) refMesh = mf.sharedMesh;
            }

            if (refMesh == null)
            {
                _penetratingVertices = null;
                return;
            }

            // Compute transform from deformer space to reference space
            var deformerTransform = deformer.MeshTransform;
            var refTransform = s_penetrationReference.transform;
            Matrix4x4 deformedToRef = refTransform.worldToLocalMatrix * deformerTransform.localToWorldMatrix;

            // Apply current displacements to get deformed positions
            var deformedVertices = new Vector3[_meshVertices.Length];
            for (int i = 0; i < _meshVertices.Length; i++)
            {
                deformedVertices[i] = _meshVertices[i] + deformer.GetDisplacement(i);
            }

            _penetratingVertices = PenetrationDetector.DetectPenetration(deformedVertices, refMesh, deformedToRef);
        }

        private void DrawPenetrationHighlight(Transform meshTransform)
        {
            if (_penetratingVertices == null || _penetratingVertices.Count == 0 || _meshVertices == null)
            {
                return;
            }

            var matrix = meshTransform.localToWorldMatrix;
            var camForward = Camera.current != null ? Camera.current.transform.forward : Vector3.forward;
            Handles.color = new Color(1f, 0f, 0f, 0.8f);

            foreach (int i in _penetratingVertices)
            {
                if (i < _meshVertices.Length)
                {
                    Vector3 worldPos = SkinnedVertexHelper.LocalToWorld(i, _worldPositions, _meshVertices[i], matrix);
                    float dotSize = HandleUtility.GetHandleSize(worldPos) * 0.01f;
                    Handles.DotHandleCap(0, worldPos, Quaternion.identity, dotSize, EventType.Repaint);
                }
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
                _connectedVerticesCache = FindConnectedVertices(nearestVertex, WorldToLocalRadius());
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
                nearest, WorldToLocalRadius(), _adjacency, _meshVertices);
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
            Undo.RecordObject(deformer, LatticeLocalization.Tr(LocKey.ClearAll));
            deformer.ClearDisplacements();
            bool assignToRenderer = LatticePreviewUtility.ShouldAssignRuntimeMesh();
            deformer.Deform(assignToRenderer);
            LatticePreviewUtility.RequestSceneRepaint();
        }

        internal static void ClearActiveMask(LatticeDeformer deformer)
        {
            if (deformer == null) return;
            if (!TryGetActiveLayer(deformer, out var layer)) return;
            Undo.RecordObject(deformer, LatticeLocalization.Tr(LocKey.ClearMask));
            layer.ClearVertexMask();
            bool assignToRenderer = LatticePreviewUtility.ShouldAssignRuntimeMesh();
            deformer.Deform(assignToRenderer);
            LatticePreviewUtility.RequestSceneRepaint();
        }

        /// <summary>
        /// Creates GUIContent with a built-in Unity icon (null-safe).
        /// Falls back to text-only if the icon name doesn't exist.
        /// </summary>
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
            // Brush Mode toolbar (icon + text) — Mask mode hidden from UI
            var modeContent = new GUIContent[]
            {
                IconContent(LocKey.Normal, "TerrainInspector.TerrainToolSetHeight"),
                IconContent(LocKey.Move, "MoveTool"),
                IconContent(LocKey.Smooth, "TerrainInspector.TerrainToolSmoothHeight"),
            };
            // Map toolbar index (0-2) to BrushMode enum (Normal=0, Move=1, Smooth=2)
            int currentModeIndex = Mathf.Min((int)BrushToolHandler.CurrentBrushMode, modeContent.Length - 1);
            int modeIndex = GUILayout.Toolbar(currentModeIndex, modeContent);
            modeIndex = Mathf.Clamp(modeIndex, 0, modeContent.Length - 1);
            BrushToolHandler.CurrentBrushMode = (BrushToolHandler.BrushMode)modeIndex;

            GUILayout.Space(2f);

            // Primary parameters (always visible)
            // Display world-space cm: mesh-local radius * transform scale * 100
            // s_brushRadius is world-space meters — display as cm directly
            float radiusCm = BrushToolHandler.BrushRadius * 100f;
            EditorGUI.BeginChangeCheck();
            radiusCm = EditorGUILayout.Slider(
                new GUIContent(LatticeLocalization.Tr(LocKey.BrushRadius) + " (cm)", LatticeLocalization.Tooltip(LocKey.BrushRadius)),
                radiusCm, 0.1f, 20f);
            if (EditorGUI.EndChangeCheck())
                BrushToolHandler.BrushRadius = radiusCm / 100f;

            // Display strength as 0-100%, store internally as 0-1
            float strengthPercent = BrushToolHandler.BrushStrength * 100f;
            strengthPercent = EditorGUILayout.Slider(
                new GUIContent(LatticeLocalization.Tr(LocKey.BrushStrength) + " (%)", LatticeLocalization.Tooltip(LocKey.BrushStrength)),
                strengthPercent, 0f, 100f);
            BrushToolHandler.BrushStrength = strengthPercent / 100f;

            // Falloff type (text only — falloff curves are self-explanatory with names)
            var falloffContent = new GUIContent[]
            {
                LatticeLocalization.Content(LocKey.Smooth),
                LatticeLocalization.Content(LocKey.Linear),
                LatticeLocalization.Content(LocKey.Constant),
                LatticeLocalization.Content(LocKey.Sphere),
                LatticeLocalization.Content(LocKey.Gaussian)
            };
            int falloffIndex = EditorGUILayout.Popup(
                LatticeLocalization.Content(LocKey.BrushFalloff),
                (int)BrushToolHandler.BrushFalloff,
                falloffContent);
            falloffIndex = Mathf.Clamp(falloffIndex, 0, falloffContent.Length - 1);
            BrushToolHandler.BrushFalloff = (BrushFalloffType)falloffIndex;

            GUILayout.Space(2f);

            // Compact toggles (horizontal)
            using (new GUILayout.HorizontalScope())
            {
                BrushToolHandler.InvertBrush = GUILayout.Toggle(
                    BrushToolHandler.InvertBrush,
                    LatticeLocalization.Content(LocKey.InvertBrush));
                BrushToolHandler.BackfaceCulling = GUILayout.Toggle(
                    BrushToolHandler.BackfaceCulling,
                    LatticeLocalization.Content(LocKey.BackfaceCulling));
            }

            // --- Advanced section (foldout) ---
            s_showAdvancedSection = EditorGUILayout.Foldout(s_showAdvancedSection, LatticeLocalization.Tr(LocKey.Advanced), true);
            if (s_showAdvancedSection)
            {
                EditorGUI.indentLevel++;
                BrushToolHandler.ConnectedOnly = GUILayout.Toggle(
                    BrushToolHandler.ConnectedOnly,
                    LatticeLocalization.Content(LocKey.ConnectedOnly));
                BrushToolHandler.UseSurfaceDistance = GUILayout.Toggle(
                    BrushToolHandler.UseSurfaceDistance,
                    LatticeLocalization.Content(LocKey.SurfaceDistance));
                EditorGUI.indentLevel--;
            }

            // --- Mirror section (foldout) ---
            s_showMirrorSection = EditorGUILayout.Foldout(s_showMirrorSection, LatticeLocalization.Tr(LocKey.EnableMirror), true);
            if (s_showMirrorSection)
            {
                EditorGUI.indentLevel++;
                BrushToolHandler.MirrorEditing = GUILayout.Toggle(
                    BrushToolHandler.MirrorEditing,
                    LatticeLocalization.Content(LocKey.EnableMirror));

                using (new EditorGUI.DisabledScope(!BrushToolHandler.MirrorEditing))
                {
                    GUILayout.Label(LatticeLocalization.Content(LocKey.MirrorAxis), EditorStyles.miniLabel);
                    int axisSelection = GUILayout.Toolbar(
                        (int)BrushToolHandler.CurrentMirrorAxis,
                        BrushToolHandler.AxisOptions);
                    axisSelection = Mathf.Clamp(axisSelection, 0, BrushToolHandler.AxisOptions.Length - 1);
                    BrushToolHandler.CurrentMirrorAxis = (BrushToolHandler.MirrorAxis)axisSelection;
                }
                EditorGUI.indentLevel--;
            }

            // --- Visualization section (foldout) ---
            s_showVisualizationSection = EditorGUILayout.Foldout(s_showVisualizationSection, LatticeLocalization.Tr(LocKey.Visualization), true);
            if (s_showVisualizationSection)
            {
                EditorGUI.indentLevel++;
                BrushToolHandler.ShowAffectedVertices = GUILayout.Toggle(
                    BrushToolHandler.ShowAffectedVertices,
                    LatticeLocalization.Content(LocKey.ShowAffectedVertices));
                BrushToolHandler.ShowDisplacementHeatmap = GUILayout.Toggle(
                    BrushToolHandler.ShowDisplacementHeatmap,
                    LatticeLocalization.Content(LocKey.ShowDisplacementHeatmap));
                BrushToolHandler.VertexDotSize = EditorGUILayout.Slider(
                    LatticeLocalization.Content(LocKey.DotSize),
                    BrushToolHandler.VertexDotSize, 1f, 8f);

                BrushToolHandler.ShowPenetration = GUILayout.Toggle(
                    BrushToolHandler.ShowPenetration,
                    LatticeLocalization.Content(LocKey.ShowPenetration));
                if (BrushToolHandler.ShowPenetration)
                {
                    BrushToolHandler.PenetrationReference = (Renderer)EditorGUILayout.ObjectField(
                        LatticeLocalization.Content(LocKey.ReferenceMesh),
                        BrushToolHandler.PenetrationReference,
                        typeof(Renderer),
                        true);
                }
                EditorGUI.indentLevel--;
            }

            GUILayout.Space(4f);

            // Action buttons
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button(LatticeLocalization.Content(LocKey.ClearAll)))
                {
                    if (deformer != null && deformer.ActiveLayerType == MeshDeformerLayerType.Brush)
                    {
                        BrushToolHandler.ClearAllDisplacements(deformer);
                    }
                }

                // Mask mode UI hidden — mask brush mode is disabled in UI
            }

            GUILayout.Space(2f);
            GUILayout.Label(LatticeLocalization.Tr(LocKey.AltScrollHint), EditorStyles.miniLabel);
        }
    }
}
#endif
