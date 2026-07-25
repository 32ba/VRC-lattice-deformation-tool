#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using UnityEditor;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using Net._32Ba.LatticeDeformationTool;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal readonly struct PenetrationDetectionCacheKey : IEquatable<PenetrationDetectionCacheKey>
    {
        private readonly int _layeredStateHash;
        private readonly int _settingsRevision;
        private readonly int _referenceRendererId;
        private readonly int _referenceMeshId;
        private readonly int _sourceMeshId;
        private readonly int _runtimeMeshId;
        private readonly int _targetVertexCount;
        private readonly int _referenceVertexCount;
        private readonly Matrix4x4 _targetLocalToWorld;
        private readonly Matrix4x4 _referenceWorldToLocal;

        internal PenetrationDetectionCacheKey(
            int layeredStateHash,
            int settingsRevision,
            int referenceRendererId,
            int referenceMeshId,
            int sourceMeshId,
            int runtimeMeshId,
            int targetVertexCount,
            int referenceVertexCount,
            Matrix4x4 targetLocalToWorld,
            Matrix4x4 referenceWorldToLocal)
        {
            _layeredStateHash = layeredStateHash;
            _settingsRevision = settingsRevision;
            _referenceRendererId = referenceRendererId;
            _referenceMeshId = referenceMeshId;
            _sourceMeshId = sourceMeshId;
            _runtimeMeshId = runtimeMeshId;
            _targetVertexCount = targetVertexCount;
            _referenceVertexCount = referenceVertexCount;
            _targetLocalToWorld = targetLocalToWorld;
            _referenceWorldToLocal = referenceWorldToLocal;
        }

        public bool Equals(PenetrationDetectionCacheKey other)
        {
            return _layeredStateHash == other._layeredStateHash &&
                   _settingsRevision == other._settingsRevision &&
                   _referenceRendererId == other._referenceRendererId &&
                   _referenceMeshId == other._referenceMeshId &&
                   _sourceMeshId == other._sourceMeshId &&
                   _runtimeMeshId == other._runtimeMeshId &&
                   _targetVertexCount == other._targetVertexCount &&
                   _referenceVertexCount == other._referenceVertexCount &&
                   _targetLocalToWorld.Equals(other._targetLocalToWorld) &&
                   _referenceWorldToLocal.Equals(other._referenceWorldToLocal);
        }

        public override bool Equals(object obj)
        {
            return obj is PenetrationDetectionCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            int first = HashCode.Combine(
                _layeredStateHash,
                _settingsRevision,
                _referenceRendererId,
                _referenceMeshId,
                _sourceMeshId,
                _runtimeMeshId,
                _targetVertexCount,
                _referenceVertexCount);
            return HashCode.Combine(first, _targetLocalToWorld, _referenceWorldToLocal);
        }
    }

    [ExcludeFromCodeCoverage]
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
        private static bool s_showWireframe = true;
        private static float s_vertexDotSize = 3f;
        private static bool s_connectedOnly = false;
        private static bool s_useSurfaceDistance = false;
        private static bool s_backfaceCulling = true;
        private static bool s_showPenetration = false;
        private static Renderer s_penetrationReference = null;
        private static int s_penetrationSettingsRevision;

        // Overlay foldout states
        private static bool s_showMirrorSection = false;
        private static bool s_showAdvancedSection = false;
        private static bool s_showVisualizationSection = false;

        private LatticeDeformer _activeDeformer;
        private LatticeLayer _cachedActiveBrushLayer;

        private Mesh _cachedMesh;
        private Vector3[] _meshVertices;
        private Vector3[] _meshNormals;
        private int[] _meshTriangles;
        private Vector3[] _worldPositions; // Current visual mesh positions in world space
        private readonly List<Vector3> _visualVertexScratch = new List<Vector3>();
        private Vector3[] _distanceWorldPositions;
        private Vector3[] _wireframeVertices;
        private MeshAdjacency _adjacency;
        private Vector2 _lastMousePosition;
        private Vector3 _lastMoveBrushLocalDelta;
        private bool _hasLastMoveBrushLocalDelta;
        private HashSet<int> _connectedVerticesCache;
        private HashSet<int> _mirrorConnectedVerticesCache;
        private int _connectedCacheStartVertex = -1;
        private float _connectedCacheRadius = -1f;
        private int _connectedCacheGeometryRevision = -1;
        private Matrix4x4 _connectedCacheMatrix;
        private readonly GeodesicDistanceCalculator.Workspace _geodesicWorkspace =
            new GeodesicDistanceCalculator.Workspace();
        private bool _hasGeodesicDistanceCache;
        private int _geodesicCacheStartVertex = -1;
        private float _geodesicCacheRadius = -1f;
        private int _geodesicCacheGeometryRevision = -1;
        private Matrix4x4 _geodesicCacheMatrix;
        private readonly Queue<int> _connectedQueue = new Queue<int>();
        private readonly SkinnedVertexHelper.RestSpaceDeltaConverterCache _restSpaceConverterCache =
            new SkinnedVertexHelper.RestSpaceDeltaConverterCache();
        private Mesh _raycastMesh;
        private Matrix4x4 _raycastMatrix;
        private bool _hasBakedRaycastMesh;
        private Renderer _cachedBrushSourceRenderer;
        private Renderer _cachedBrushTargetRenderer;
        private SkinnedMeshRenderer _cachedBrushSkinnedRenderer;
        private Transform[] _cachedBrushBones;
        private int _cachedBrushPoseHash;
        private int _cachedBrushRuntimeRevision = -1;
        private int _cachedBrushRendererDirtyCount = -1;
        private int _cachedBrushVisualMeshDirtyCount = -1;
        private bool _hasBrushSnapshotState;
        private int _distanceGeometryRevision;
        private int _cachedBrushProxyMappingRevision = -1;
        private int _cachedMeshDirtyCount = -1;
        private Bounds _cachedMeshBounds;
        private int _currentHitTriangleIndex = -1;
        private Vector3 _currentHitBarycentric;
        private static readonly ProfilerMarker s_bakeMeshMarker = new ProfilerMarker("Brush.BakeMesh");
        private static readonly ProfilerMarker s_raycastMarker = new ProfilerMarker("Brush.Raycast");
        private static readonly ProfilerMarker s_buildAdjacencyMarker = new ProfilerMarker("Brush.BuildAdjacency");
        private static readonly ProfilerMarker s_geodesicMarker = new ProfilerMarker("Brush.Geodesic");
        private static readonly ProfilerMarker s_restSpaceMarker = new ProfilerMarker("Brush.RestSpaceConverter");
        private static readonly ProfilerMarker s_visualizationMarker = new ProfilerMarker("Brush.Visualization");
        private static readonly ProfilerMarker s_deformMarker = new ProfilerMarker("Brush.Deform");
        internal const int MaxAffectedVertexDots = 4096;
        private HashSet<int> _penetratingVertices;
        private Vector3[] _penetrationDeformedVertices;
        private Vector3[] _smoothDisplacements;
        private SymmetryVertexMap _mirrorMap;
        private int _mirrorMapMeshId;
        private int _mirrorMapDirtyCount = -1;
        private MirrorAxis _mirrorMapAxis;
        private PenetrationDetectionCacheKey _penetrationCacheKey;
        private bool _hasPenetrationCacheKey;
        private bool _isStrokeActive;
        private int _strokeInitialStateHash;
        private LatticeDeformer _strokeDeformer;

        private delegate bool IntersectRayMeshDelegate(
            Ray ray,
            Mesh mesh,
            Matrix4x4 matrix,
            out RaycastHit hit);

        private static IntersectRayMeshDelegate s_intersectRayMesh;
        private static bool s_intersectRayMeshResolved;

        private static readonly Color k_NormalBrushColor = new Color(0.3f, 0.5f, 1f, 0.8f);
        private static readonly Color k_SmoothBrushColor = new Color(0.3f, 1f, 0.5f, 0.8f);
        private static readonly Color k_MoveBrushColor = new Color(1f, 0.6f, 0.2f, 0.8f);
        private static readonly Color k_MaskBrushColor = new Color(1f, 0.3f, 0.3f, 0.8f);

        static BrushToolHandler()
        {
            LatticeLocalization.LanguageChanged += OnLanguageChanged;
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseStaticResources;
        }

        internal static void ReleaseStaticResources()
        {
            if (s_brushDotMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(s_brushDotMaterial);
                s_brushDotMaterial = null;
            }
            if (s_brushCircleTex != null)
            {
                UnityEngine.Object.DestroyImmediate(s_brushCircleTex);
                s_brushCircleTex = null;
            }
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
                unchecked
                {
                    s_penetrationSettingsRevision++;
                }
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
                unchecked
                {
                    s_penetrationSettingsRevision++;
                }
                SceneView.RepaintAll();
            }
        }

        internal static GUIContent[] AxisOptions => new[]
        {
            LatticeLocalization.Content(LocKey.X),
            LatticeLocalization.Content(LocKey.Y),
            LatticeLocalization.Content(LocKey.Z)
        };

        internal void Activate(LatticeDeformer deformer)
        {
            _activeDeformer = deformer;
            deformer?.EnsureDisplacementCapacity();
            TryGetActiveLayer(deformer, out _cachedActiveBrushLayer);
            Undo.undoRedoPerformed += OnUndoRedo;
            SceneView.RepaintAll();
        }

        internal void Deactivate()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EndStroke();
            InvalidateCache();
            _activeDeformer = null;
            _cachedActiveBrushLayer = null;
        }

        private void OnUndoRedo()
        {
            ResetStrokeState();
            if (_activeDeformer != null)
            {
                bool assignToRenderer = LatticePreviewUtility.ShouldAssignRuntimeMesh();
                _activeDeformer.Deform(assignToRenderer);
            }

            SceneView.RepaintAll();
        }

        private void BeginStroke(LatticeDeformer deformer)
        {
            EndStroke();
            _isStrokeActive = deformer != null;
            _strokeDeformer = deformer;
            _strokeInitialStateHash = deformer != null ? deformer.ComputeLayeredStateHash() : 0;
        }

        private void EndStroke()
        {
            if (_isStrokeActive && _strokeDeformer != null &&
                _strokeDeformer.ComputeLayeredStateHash() != _strokeInitialStateHash)
            {
                LatticePrefabUtility.MarkModified(_strokeDeformer);
            }

            ResetStrokeState();
        }

        private void ResetStrokeState()
        {
            _isStrokeActive = false;
            _strokeInitialStateHash = 0;
            _strokeDeformer = null;
        }

        internal void OnToolGUI(EditorWindow window, LatticeDeformer deformer)
        {
            UnityEngine.Profiling.Profiler.BeginSample("BrushTool.OnToolGUI");
            try
            {
                if (Event.current != null && Event.current.commandName == "UndoRedoPerformed")
                {
                    return;
                }

                var evt = Event.current;
                if (evt == null) return;

                // Layout only registers control ownership. Baking a posed mesh and
                // raycasting here duplicates the following Repaint event in the same
                // Scene GUI frame without producing any visible or editable result.
                if (evt.type == EventType.Layout)
                {
                    HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                    return;
                }

                // Radius/strength shortcuts do not depend on a surface hit.
                if (evt.type == EventType.ScrollWheel)
                {
                    if (evt.shift)
                    {
                        BrushStrength = s_brushStrength - evt.delta.y * 0.01f;
                        evt.Use();
                    }
                    else if (evt.alt)
                    {
                        BrushRadius = s_brushRadius - evt.delta.y * 0.005f;
                        evt.Use();
                    }
                    return;
                }

                // Ending a stroke does not require another posed-mesh bake/raycast.
                if (evt.type == EventType.MouseUp && evt.button == 0)
                {
                    bool hadActiveStroke = _isStrokeActive;
                    EndStroke();
                    ClearConnectedVerticesCache();
                    ClearGeodesicDistanceCache();
                    if (hadActiveStroke) evt.Use();
                    return;
                }

                if (!RequiresSurfaceQuery(evt.type))
                {
                    return;
                }

            if (!TryGetBrushLayerFast(deformer, out var activeBrushLayer) ||
                activeBrushLayer.Type != MeshDeformerLayerType.Brush)
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
            _cachedActiveBrushLayer = activeBrushLayer;
            if (!TryGetBrushBuffers(
                    deformer, out _, out var activeDisplacements, out _)) return;

            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            // Draw mesh wireframe
            if (evt.type == EventType.Repaint && s_showWireframe &&
                _meshTriangles != null && _meshVertices != null)
            {
                using (s_visualizationMarker.Auto())
                {
                Vector3[] deformedLocal = null;
                if (_worldPositions == null)
                {
                    if (_wireframeVertices == null || _wireframeVertices.Length != _meshVertices.Length)
                        _wireframeVertices = new Vector3[_meshVertices.Length];
                    for (int i = 0; i < _wireframeVertices.Length; i++)
                        _wireframeVertices[i] = _meshVertices[i] + activeDisplacements[i];
                    deformedLocal = _wireframeVertices;
                }

                WireframeRenderer.Draw(
                    _meshTriangles,
                    _worldPositions,
                    deformedLocal,
                    meshTransform.localToWorldMatrix,
                    HashCode.Combine(_cachedMesh.GetInstanceID(), _cachedMeshDirtyCount));
                }
            }

            // Draw displacement heatmap (always visible when enabled)
            if (s_showDisplacementHeatmap && deformer.HasDisplacements())
            {
                using (s_visualizationMarker.Auto())
                    DrawDisplacementHeatmap(deformer, meshTransform);
            }

            // Draw vertex mask visualization when in Mask mode
            if (s_brushMode == BrushMode.Mask)
            {
                using (s_visualizationMarker.Auto())
                    DrawVertexMaskVisualization(deformer, meshTransform);
            }

            // Penetration detection
            if (s_showPenetration)
            {
                using (s_visualizationMarker.Auto())
                {
                    UpdatePenetrationDetection(deformer);
                    DrawPenetrationHighlight(meshTransform);
                }
            }
            else
            {
                InvalidatePenetrationCache();
            }

            // Raycast against the current visual snapshot. This keeps the cursor and
            // affected vertices aligned for posed skinning, runtime deformation, and
            // NDMF preview proxies alike.
            var mouseRay = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
            bool hitSurface;
            RaycastHit hit;
            bool usedBakedMesh = _hasBakedRaycastMesh;
            var bakedMesh = _raycastMesh;
            var bakedMatrix = _raycastMatrix;

            using (s_raycastMarker.Auto())
            {
                if (usedBakedMesh)
                    hitSurface = IntersectRayMesh(mouseRay, bakedMesh, bakedMatrix, out hit);
                else
                    hitSurface = IntersectRayMesh(mouseRay, sourceMesh, meshTransform.localToWorldMatrix, out hit);
            }

            if (hitSurface)
            {
                _currentHitTriangleIndex = hit.triangleIndex;
                _currentHitBarycentric = hit.barycentricCoordinate;
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
                    var v0 = _meshVertices[i0] + activeDisplacements[i0];
                    var v1 = _meshVertices[i1] + activeDisplacements[i1];
                    var v2 = _meshVertices[i2] + activeDisplacements[i2];
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
                try
                {
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
                        using (s_visualizationMarker.Auto())
                        {
                            // Update geodesic cache for preview visualization during hover
                            if (s_useSurfaceDistance)
                            {
                                UpdateGeodesicDistanceCache(hit.point);
                            }

                            DrawAffectedVertices(deformer, hit.point, meshTransform);
                        }
                    }
                }
                finally
                {
                    Handles.matrix = prevMatrix;
                }

                // Handle brush painting on left mouse drag
                if (evt.type == EventType.MouseDrag && evt.button == 0 && !evt.alt && _isStrokeActive)
                {
                    // Keep topology-limited and geodesic falloff centered on the
                    // current stroke position. The cache keys above make this a no-op
                    // while the nearest vertex and geometry remain unchanged.
                    UpdateConnectedVerticesCache(hit.point);
                    UpdateGeodesicDistanceCache(hit.point);
                    ApplyBrush(deformer, meshTransform, localHitPoint, hit.point, localHitNormal, evt);
                    evt.Use();
                }
                else if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt)
                {
                    _lastMousePosition = evt.mousePosition;

                    // Build connected vertices cache at stroke start
                    UpdateConnectedVerticesCache(hit.point);

                    // Build geodesic distance cache at stroke start
                    UpdateGeodesicDistanceCache(hit.point);

                    BeginStroke(deformer);
                    Undo.RecordObject(deformer, GetUndoLabel());
                    deformer.EnsureDisplacementCapacity();

                    ApplyBrush(deformer, meshTransform, localHitPoint, hit.point, localHitNormal, evt);
                    evt.Use();
                }
            }

            // Force repaint so brush disc follows cursor
            if (evt.type == EventType.MouseMove || evt.type == EventType.MouseDrag)
            {
                SceneView.RepaintAll();
            }

            }
            finally
            {
                UnityEngine.Profiling.Profiler.EndSample();
            }
        }

        private void ApplyBrush(
            LatticeDeformer deformer,
            Transform meshTransform,
            Vector3 localHitPoint,
            Vector3 worldHitPoint,
            Vector3 localHitNormal,
            Event evt)
        {
            if (_meshVertices == null || _meshVertices.Length == 0)
            {
                return;
            }

            deformer.EnsureDisplacementCapacity();

            float worldRadius = s_brushRadius;
            float effectiveStrength = s_brushStrength;
            if (evt != null && evt.control)
            {
                effectiveStrength *= 0.1f;
            }
            float strength = effectiveStrength * 0.01f;
            float direction = s_invertBrush ? -1f : 1f;
            bool assignToRenderer = LatticePreviewUtility.ShouldAssignRuntimeMesh();

            bool modified = false;
            _hasLastMoveBrushLocalDelta = false;

            switch (s_brushMode)
            {
                case BrushMode.Normal:
                    modified = ApplyNormalBrush(deformer, worldHitPoint, worldRadius, strength, direction);
                    break;

                case BrushMode.Move:
                    modified = ApplyMoveBrush(deformer, meshTransform, worldHitPoint, worldRadius, strength, evt);
                    break;

                case BrushMode.Smooth:
                    modified = ApplySmoothBrush(deformer, worldHitPoint, worldRadius, strength);
                    break;

                case BrushMode.Mask:
                    modified = ApplyMaskBrush(deformer, worldHitPoint, worldRadius);
                    break;
            }

            if (modified)
            {
                if (s_mirrorEditing)
                {
                    ApplyMirror(deformer, localHitPoint, worldHitPoint, worldRadius, strength, direction);
                }

                using (s_deformMarker.Auto())
                    deformer.Deform(assignToRenderer);
                LatticePreviewUtility.RequestSceneRepaint();
            }

            _lastMousePosition = evt.mousePosition;
        }

        private bool ApplyNormalBrush(LatticeDeformer deformer, Vector3 worldHitPoint, float worldRadius, float strength, float direction)
        {
            if (!TryGetBrushBuffers(
                    deformer, out _, out var displacements, out var vertexMask)) return false;
            Transform meshTransform = deformer.MeshTransform;
            Matrix4x4 localToWorld = meshTransform.localToWorldMatrix;
            Vector3 worldCenter = worldHitPoint;
            float radiusSq = worldRadius * worldRadius;
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

            bool useGeodesicCandidates = s_useSurfaceDistance && _hasGeodesicDistanceCache;
            int iterationCount = useGeodesicCandidates ? _geodesicWorkspace.VisitedCount : vertexCount;
            for (int iteration = 0; iteration < iterationCount; iteration++)
            {
                int i = useGeodesicCandidates ? _geodesicWorkspace.GetVisitedVertex(iteration) : iteration;
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
                if (useGeodesicCandidates)
                {
                    if (!_geodesicWorkspace.TryGetDistance(i, out float geodesicDist))
                    {
                        continue; // Not reachable via surface
                    }
                    float t = geodesicDist / worldRadius;
                    falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);
                }
                else
                {
                    var vertex = _meshVertices[i] + displacements[i];
                    float distSq = WorldDistanceSquared(i, vertex, worldCenter, localToWorld);
                    if (distSq > radiusSq) continue;

                    float dist = Mathf.Sqrt(distSq);
                    float t = dist / worldRadius;
                    falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);
                }

                var normal = _meshNormals[i].normalized;
                if (normal.sqrMagnitude < 0.001f) normal = Vector3.up;

                var delta = normal * (strength * falloff * direction);
                float maskValue = GetMaskValue(vertexMask, i);
                if (maskValue < 1e-6f) continue;
                delta *= maskValue;
                displacements[i] += delta;
                modified = true;
            }

            return modified;
        }

        private bool ApplyMoveBrush(LatticeDeformer deformer, Transform meshTransform, Vector3 worldHitPoint, float worldRadius, float strength, Event evt)
        {
            // Compute mouse delta in world space, then convert to local
            var mouseDelta = evt.delta;
            if (mouseDelta.sqrMagnitude < 0.001f) return false;

            var camera = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
            if (camera == null) return false;

            // Convert mouse delta to world space direction
            var screenPoint = camera.WorldToScreenPoint(worldHitPoint);
            var screenPointMoved = screenPoint + new Vector3(mouseDelta.x, -mouseDelta.y, 0f);
            var worldPoint = camera.ScreenToWorldPoint(screenPoint);
            var worldPointMoved = camera.ScreenToWorldPoint(screenPointMoved);
            var worldDelta = worldPointMoved - worldPoint;

            // Convert world delta to local space
            var localDelta = meshTransform.InverseTransformVector(worldDelta);
            _lastMoveBrushLocalDelta = localDelta;
            _hasLastMoveBrushLocalDelta = true;

            // Pre-compute camera forward in local space for backface culling
            Vector3 localCameraForward = Vector3.forward;
            if (s_backfaceCulling && meshTransform != null)
            {
                localCameraForward = meshTransform.InverseTransformDirection(camera.transform.forward);
            }

            return ApplyMoveBrushLocalDelta(
                deformer, worldHitPoint, worldRadius, strength, localDelta, localCameraForward);
        }

        private bool ApplyMoveBrushLocalDelta(
            LatticeDeformer deformer,
            Vector3 worldHitPoint,
            float worldRadius,
            float strength,
            Vector3 localDelta,
            Vector3 localCameraForward)
        {
            if (!TryGetBrushBuffers(
                    deformer, out _, out var displacements, out var vertexMask)) return false;
            Matrix4x4 localToWorld = deformer.MeshTransform.localToWorldMatrix;
            Vector3 worldCenter = worldHitPoint;
            float radiusSq = worldRadius * worldRadius;

            bool modified = false;
            int vertexCount = _meshVertices.Length;
            SkinnedVertexHelper.RestSpaceDeltaConverter restSpaceConverter = null;
            if (SkinnedVertexHelper.StoreMovesInRestSpace)
            {
                using (s_restSpaceMarker.Auto())
                    restSpaceConverter = _restSpaceConverterCache.Get(deformer);
            }

            bool useGeodesicCandidates = s_useSurfaceDistance && _hasGeodesicDistanceCache;
            int iterationCount = useGeodesicCandidates ? _geodesicWorkspace.VisitedCount : vertexCount;
            for (int iteration = 0; iteration < iterationCount; iteration++)
            {
                int i = useGeodesicCandidates ? _geodesicWorkspace.GetVisitedVertex(iteration) : iteration;
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
                if (useGeodesicCandidates)
                {
                    if (!_geodesicWorkspace.TryGetDistance(i, out float geodesicDist))
                    {
                        continue; // Not reachable via surface
                    }
                    float t = geodesicDist / worldRadius;
                    falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);
                }
                else
                {
                    var vertex = _meshVertices[i] + displacements[i];
                    float distSq = WorldDistanceSquared(i, vertex, worldCenter, localToWorld);
                    if (distSq > radiusSq) continue;

                    float dist = Mathf.Sqrt(distSq);
                    float t = dist / worldRadius;
                    falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);
                }

                var storedDelta = restSpaceConverter != null
                    ? restSpaceConverter.ConvertOrFallback(i, localDelta)
                    : localDelta;
                var delta = storedDelta * (strength * falloff * 10f);
                float maskValue = GetMaskValue(vertexMask, i);
                if (maskValue < 1e-6f) continue;
                delta *= maskValue;
                displacements[i] += delta;
                modified = true;
            }

            return modified;
        }

        private bool ApplySmoothBrush(LatticeDeformer deformer, Vector3 worldHitPoint, float worldRadius, float strength)
        {
            if (!TryGetBrushBuffers(
                    deformer, out _, out var displacements, out var vertexMask)) return false;
            Matrix4x4 localToWorld = deformer.MeshTransform.localToWorldMatrix;
            Vector3 worldCenter = worldHitPoint;
            float radiusSq = worldRadius * worldRadius;
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
            var currentDisplacements = GetSmoothDisplacementBuffer(vertexCount);
            Array.Copy(displacements, currentDisplacements, vertexCount);

            float smoothFactor = Mathf.Clamp01(strength * 10f);

            bool useGeodesicCandidates = s_useSurfaceDistance && _hasGeodesicDistanceCache;
            int iterationCount = useGeodesicCandidates ? _geodesicWorkspace.VisitedCount : vertexCount;
            for (int iteration = 0; iteration < iterationCount; iteration++)
            {
                int i = useGeodesicCandidates ? _geodesicWorkspace.GetVisitedVertex(iteration) : iteration;
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
                if (useGeodesicCandidates)
                {
                    if (!_geodesicWorkspace.TryGetDistance(i, out float geodesicDist))
                    {
                        continue; // Not reachable via surface
                    }
                    float t = geodesicDist / worldRadius;
                    falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);
                }
                else
                {
                    var vertex = _meshVertices[i] + currentDisplacements[i];
                    float distSq = WorldDistanceSquared(i, vertex, worldCenter, localToWorld);
                    if (distSq > radiusSq) continue;

                    float dist = Mathf.Sqrt(distSq);
                    float t = dist / worldRadius;
                    falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);
                }

                // Compute average displacement of neighbors
                int neighborStart = _adjacency.GetNeighborStart(i);
                int neighborEnd = _adjacency.GetNeighborEnd(i);
                if (neighborStart == neighborEnd) continue;
                var averageDisp = Vector3.zero;
                for (int edge = neighborStart; edge < neighborEnd; edge++)
                {
                    int neighbor = _adjacency.GetNeighbor(edge);
                    averageDisp += currentDisplacements[neighbor];
                }
                averageDisp /= neighborEnd - neighborStart;

                // Blend toward neighbor average
                var currentDisp = currentDisplacements[i];
                float maskValue = GetMaskValue(vertexMask, i);
                if (maskValue < 1e-6f) continue;
                var targetDisp = Vector3.Lerp(currentDisp, averageDisp, smoothFactor * falloff * maskValue);
                displacements[i] = targetDisp;
                modified = true;
            }

            return modified;
        }

        private bool ApplyMaskBrush(LatticeDeformer deformer, Vector3 worldHitPoint, float worldRadius)
        {
            Matrix4x4 localToWorld = deformer.MeshTransform.localToWorldMatrix;
            Vector3 worldCenter = worldHitPoint;
            float radiusSq = worldRadius * worldRadius;
            if (_meshVertices == null || _meshVertices.Length == 0)
            {
                return false;
            }

            if (!TryGetBrushBuffers(
                    deformer, out var layer, out var displacements, out _))
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

            bool useGeodesicCandidates = s_useSurfaceDistance && _hasGeodesicDistanceCache;
            int iterationCount = useGeodesicCandidates
                ? _geodesicWorkspace.VisitedCount
                : _meshVertices.Length;
            for (int iteration = 0; iteration < iterationCount; iteration++)
            {
                int i = useGeodesicCandidates ? _geodesicWorkspace.GetVisitedVertex(iteration) : iteration;
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
                if (useGeodesicCandidates)
                {
                    if (!_geodesicWorkspace.TryGetDistance(i, out float geodesicDist))
                    {
                        continue;
                    }
                    float t = geodesicDist / worldRadius;
                    falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);
                }
                else
                {
                    var vertex = _meshVertices[i] + displacements[i];
                    float distSq = WorldDistanceSquared(i, vertex, worldCenter, localToWorld);
                    if (distSq > radiusSq) continue;

                    float dist = Mathf.Sqrt(distSq);
                    float t = dist / worldRadius;
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

            if (!deformer.TryGetActiveLayerFast(out layer)) return false;
            return layer != null && layer.Type == MeshDeformerLayerType.Brush;
        }

        private static float GetMaskValue(float[] mask, int vertexIndex)
        {
            return mask != null && vertexIndex >= 0 && vertexIndex < mask.Length
                ? mask[vertexIndex]
                : 1f;
        }

        internal bool TryGetBrushLayerFast(LatticeDeformer deformer, out LatticeLayer layer)
        {
            if (deformer == null || !deformer.TryGetActiveLayerFast(out layer) ||
                layer.Type != MeshDeformerLayerType.Brush)
            {
                layer = null;
                return false;
            }

            if (ReferenceEquals(deformer, _activeDeformer))
                _cachedActiveBrushLayer = layer;
            return true;
        }

        private bool TryGetBrushBuffers(
            LatticeDeformer deformer,
            out LatticeLayer layer,
            out Vector3[] displacements,
            out float[] vertexMask)
        {
            displacements = null;
            vertexMask = null;
            if (!TryGetBrushLayerFast(deformer, out layer) || _meshVertices == null)
                return false;
            displacements = layer.BrushDisplacements;
            vertexMask = layer.VertexMask;
            return displacements != null && displacements.Length == _meshVertices.Length;
        }

        private void ApplyMirror(
            LatticeDeformer deformer,
            Vector3 localHitPoint,
            Vector3 worldHitPoint,
            float worldRadius,
            float strength,
            float direction)
        {
            Matrix4x4 localToWorld = deformer.MeshTransform.localToWorldMatrix;
            float radiusSq = worldRadius * worldRadius;
            if (_cachedMesh == null || _meshVertices == null || _meshVertices.Length == 0) return;
            if (!TryGetBrushBuffers(
                    deformer, out _, out var displacements, out var vertexMask)) return;

            var mirrorMap = ResolveMirrorMap();

            // Mirror the brush center
            var mirroredCenter = MirrorPosition(localHitPoint);
            Vector3 mirroredWorldCenter = GetMirroredWorldCenter(
                mirroredCenter, worldHitPoint, mirrorMap, localToWorld);
            int vertexCount = _meshVertices.Length;

            // Build connected vertices cache for the mirrored side
            HashSet<int> mirrorConnected = null;
            if (s_connectedOnly)
            {
                EnsureAdjacencyBuilt();
                int mirrorNearest = FindNearestVertex(mirroredWorldCenter);
                if (mirrorNearest >= 0)
                {
                    _mirrorConnectedVerticesCache = FindConnectedVertices(
                        mirrorNearest, worldRadius, _mirrorConnectedVerticesCache);
                    mirrorConnected = _mirrorConnectedVerticesCache;
                }
            }

            switch (s_brushMode)
            {
                case BrushMode.Normal:
                {
                    for (int i = 0; i < vertexCount; i++)
                    {
                        if (!mirrorMap.TryGetPartner(i, out _)) continue;
                        if (s_connectedOnly && mirrorConnected != null && !mirrorConnected.Contains(i))
                        {
                            continue;
                        }

                        var vertex = _meshVertices[i] + displacements[i];
                        float distSq = WorldDistanceSquared(i, vertex, mirroredWorldCenter, localToWorld);
                        if (distSq > radiusSq) continue;

                        float dist = Mathf.Sqrt(distSq);
                        float t = dist / worldRadius;
                        float falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);

                        var normal = _meshNormals[i].normalized;
                        if (normal.sqrMagnitude < 0.001f) normal = Vector3.up;

                        // Mirror the normal direction for the mirrored side
                        var mirroredNormal = MirrorDirection(normal);
                        var delta = mirroredNormal * (strength * falloff * direction);
                        float maskValue = GetMaskValue(vertexMask, i);
                        if (maskValue < 1e-6f) continue;
                        delta *= maskValue;
                        displacements[i] += delta;
                    }
                    break;
                }

                case BrushMode.Smooth:
                {
                    EnsureAdjacencyBuilt();
                    var currentDisplacements = GetSmoothDisplacementBuffer(vertexCount);
                    Array.Copy(displacements, currentDisplacements, vertexCount);
                    float smoothFactor = Mathf.Clamp01(strength * 10f);

                    for (int i = 0; i < vertexCount; i++)
                    {
                        if (!mirrorMap.TryGetPartner(i, out _)) continue;
                        if (s_connectedOnly && mirrorConnected != null && !mirrorConnected.Contains(i))
                        {
                            continue;
                        }

                        var vertex = _meshVertices[i] + currentDisplacements[i];
                        float distSq = WorldDistanceSquared(i, vertex, mirroredWorldCenter, localToWorld);
                        if (distSq > radiusSq) continue;

                        float dist = Mathf.Sqrt(distSq);
                        float t = dist / worldRadius;
                        float falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);

                        int neighborStart = _adjacency.GetNeighborStart(i);
                        int neighborEnd = _adjacency.GetNeighborEnd(i);
                        if (neighborStart == neighborEnd) continue;
                        var averageDisp = Vector3.zero;
                        for (int edge = neighborStart; edge < neighborEnd; edge++)
                        {
                            int neighbor = _adjacency.GetNeighbor(edge);
                            averageDisp += currentDisplacements[neighbor];
                        }
                        averageDisp /= neighborEnd - neighborStart;

                        var currentDisp = currentDisplacements[i];
                        float maskValue = GetMaskValue(vertexMask, i);
                        if (maskValue < 1e-6f) continue;
                        var targetDisp = Vector3.Lerp(currentDisp, averageDisp, smoothFactor * falloff * maskValue);
                        displacements[i] = targetDisp;
                    }
                    break;
                }

                case BrushMode.Move:
                {
                    if (!_hasLastMoveBrushLocalDelta)
                    {
                        break;
                    }

                    var mirroredDelta = MirrorDirection(_lastMoveBrushLocalDelta);
                    SkinnedVertexHelper.RestSpaceDeltaConverter restSpaceConverter = null;
                    if (SkinnedVertexHelper.StoreMovesInRestSpace)
                    {
                        using (s_restSpaceMarker.Auto())
                            restSpaceConverter = _restSpaceConverterCache.Get(deformer);
                    }
                    for (int i = 0; i < vertexCount; i++)
                    {
                        if (s_connectedOnly && mirrorConnected != null && !mirrorConnected.Contains(i))
                        {
                            continue;
                        }

                        var vertex = _meshVertices[i] + displacements[i];
                        float distSq = WorldDistanceSquared(i, vertex, mirroredWorldCenter, localToWorld);
                        if (distSq > radiusSq) continue;

                        float dist = Mathf.Sqrt(distSq);
                        float t = dist / worldRadius;
                        float falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);

                        var storedDelta = restSpaceConverter != null
                            ? restSpaceConverter.ConvertOrFallback(i, mirroredDelta)
                            : mirroredDelta;
                        var delta = storedDelta * (strength * falloff * 10f);
                        float maskValue = GetMaskValue(vertexMask, i);
                        if (maskValue < 1e-6f) continue;
                        delta *= maskValue;
                        displacements[i] += delta;
                    }
                    break;
                }

                case BrushMode.Mask:
                {
                    if (!TryGetBrushLayerFast(deformer, out var layer)) break;
                    layer.EnsureVertexMaskCapacity(vertexCount);
                    float targetValue = s_invertBrush ? 1f : 0f;

                    for (int i = 0; i < vertexCount; i++)
                    {
                        if (!mirrorMap.TryGetPartner(i, out _)) continue;
                        if (s_connectedOnly && mirrorConnected != null && !mirrorConnected.Contains(i))
                        {
                            continue;
                        }

                        var vertex = _meshVertices[i] + displacements[i];
                        float distSq = WorldDistanceSquared(i, vertex, mirroredWorldCenter, localToWorld);
                        if (distSq > radiusSq) continue;

                        float dist = Mathf.Sqrt(distSq);
                        float t = dist / worldRadius;
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
            return SymmetryVertexMapCache.Mirror(position, (int)s_mirrorAxis);
        }

        private Vector3 MirrorDirection(Vector3 dir)
        {
            return SymmetryVertexMapCache.MirrorDirection(dir, (int)s_mirrorAxis);
        }

        private Vector3 GetMirroredWorldCenter(
            Vector3 mirroredLocalCenter,
            Vector3 worldHitPoint,
            SymmetryVertexMap mirrorMap,
            Matrix4x4 localToWorld)
        {
            int triBase = _currentHitTriangleIndex * 3;
            if (_currentHitTriangleIndex >= 0 && _meshTriangles != null &&
                triBase + 2 < _meshTriangles.Length &&
                _worldPositions != null && _worldPositions.Length == _meshVertices.Length)
            {
                int i0 = _meshTriangles[triBase];
                int i1 = _meshTriangles[triBase + 1];
                int i2 = _meshTriangles[triBase + 2];
                if (mirrorMap.TryGetPartner(i0, out int m0) &&
                    mirrorMap.TryGetPartner(i1, out int m1) &&
                    mirrorMap.TryGetPartner(i2, out int m2))
                {
                    Vector3 bary = _currentHitBarycentric;
                    return _worldPositions[m0] * bary.x +
                           _worldPositions[m1] * bary.y +
                           _worldPositions[m2] * bary.z;
                }
            }

            int nearest = FindNearestVertex(worldHitPoint);
            if (nearest >= 0 && mirrorMap.TryGetPartner(nearest, out int partner) &&
                _worldPositions != null && partner < _worldPositions.Length)
            {
                return _worldPositions[partner];
            }

            return localToWorld.MultiplyPoint3x4(mirroredLocalCenter);
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

        internal void RebuildCacheIfNeeded(Mesh mesh, LatticeDeformer deformer = null)
        {
            if (mesh == null)
            {
                InvalidateCache();
                return;
            }

            int dirtyCount = EditorUtility.GetDirtyCount(mesh);
            Bounds meshBounds = mesh.bounds;
            if (ReferenceEquals(_cachedMesh, mesh) && _meshVertices != null &&
                _cachedMeshDirtyCount == dirtyCount && _cachedMeshBounds == meshBounds)
            {
                // Refresh skinned positions each frame
                RefreshWorldPositions(deformer);
                return;
            }

            _cachedMesh = mesh;
            _cachedMeshDirtyCount = dirtyCount;
            _cachedMeshBounds = meshBounds;
            InvalidatePenetrationCache();
            _meshVertices = mesh.vertices;
            _meshTriangles = mesh.triangles;
            _meshNormals = MeshNormalUtility.GetOrCalculateNormals(
                mesh,
                _meshVertices,
                _meshTriangles);
            unchecked
            {
                _distanceGeometryRevision++;
            }
            _adjacency = null;
            ClearConnectedVerticesCache();
            ClearGeodesicDistanceCache();

            RefreshWorldPositions(deformer);
        }

        private void RefreshWorldPositions(LatticeDeformer deformer)
        {
            if (deformer == null || _meshVertices == null)
            {
                _worldPositions = null;
                _raycastMesh = null;
                _hasBakedRaycastMesh = false;
                _hasBrushSnapshotState = false;
                return;
            }

            Renderer targetRenderer = ResolveBrushRenderer(deformer);
            if (targetRenderer == null)
            {
                _worldPositions = null;
                _raycastMesh = null;
                _hasBakedRaycastMesh = false;
                _hasBrushSnapshotState = false;
                return;
            }

            SkinnedMeshRenderer renderer = targetRenderer as SkinnedMeshRenderer;
            if (renderer == null)
            {
                RefreshStaticWorldPositions(deformer, targetRenderer);
                return;
            }

            int rendererDirtyCount = EditorUtility.GetDirtyCount(renderer);
            if (rendererDirtyCount != _cachedBrushRendererDirtyCount)
            {
                _cachedBrushBones = renderer.bones;
                _cachedBrushRendererDirtyCount = rendererDirtyCount;
                _hasBrushSnapshotState = false;
            }

            int poseHash = SkinnedVertexHelper.ComputePoseStateHash(renderer, _cachedBrushBones);
            int runtimeRevision = deformer.RuntimeMeshRevision;
            if (_hasBrushSnapshotState && _hasBakedRaycastMesh &&
                poseHash == _cachedBrushPoseHash &&
                runtimeRevision == _cachedBrushRuntimeRevision)
            {
                return;
            }

            // The helper uses this array only to validate the baked vertex count. The
            // proxy renderer already contains the complete layered deformation, so no
            // intermediate deformed-vertex array is required here.
            using (s_bakeMeshMarker.Auto())
            {
                _hasBakedRaycastMesh = SkinnedVertexHelper.TryCaptureBrushSnapshot(
                    renderer,
                    _meshVertices,
                    _worldPositions,
                    out _worldPositions,
                    out _raycastMesh,
                    out _raycastMatrix);
            }

            unchecked
            {
                _distanceGeometryRevision++;
            }

            _cachedBrushPoseHash = poseHash;
            _cachedBrushRuntimeRevision = runtimeRevision;
            _hasBrushSnapshotState = _hasBakedRaycastMesh;
        }

        private void RefreshStaticWorldPositions(LatticeDeformer deformer, Renderer targetRenderer)
        {
            var meshFilter = targetRenderer.GetComponent<MeshFilter>();
            Mesh visualMesh = meshFilter != null ? meshFilter.sharedMesh : null;
            if (visualMesh == null || visualMesh.vertexCount != _meshVertices.Length)
            {
                _worldPositions = null;
                _raycastMesh = null;
                _hasBakedRaycastMesh = false;
                _hasBrushSnapshotState = false;
                return;
            }

            Matrix4x4 matrix = targetRenderer.transform.localToWorldMatrix;
            int runtimeRevision = deformer.RuntimeMeshRevision;
            int meshDirtyCount = EditorUtility.GetDirtyCount(visualMesh);
            if (_hasBrushSnapshotState && _hasBakedRaycastMesh &&
                ReferenceEquals(_raycastMesh, visualMesh) &&
                _raycastMatrix == matrix &&
                _cachedBrushRuntimeRevision == runtimeRevision &&
                _cachedBrushVisualMeshDirtyCount == meshDirtyCount)
            {
                return;
            }

            int vertexCount = visualMesh.vertexCount;
            if (_worldPositions == null || _worldPositions.Length != vertexCount)
                _worldPositions = new Vector3[vertexCount];
            if (_visualVertexScratch.Capacity < vertexCount)
                _visualVertexScratch.Capacity = vertexCount;
            visualMesh.GetVertices(_visualVertexScratch);
            for (int i = 0; i < vertexCount; i++)
                _worldPositions[i] = matrix.MultiplyPoint3x4(_visualVertexScratch[i]);

            _raycastMesh = visualMesh;
            _raycastMatrix = matrix;
            _hasBakedRaycastMesh = true;
            _hasBrushSnapshotState = true;
            _cachedBrushRuntimeRevision = runtimeRevision;
            _cachedBrushVisualMeshDirtyCount = meshDirtyCount;
            unchecked
            {
                _distanceGeometryRevision++;
            }
        }

        private Vector3 VertexToWorld(int index, Vector3 localVertex, Matrix4x4 localToWorld)
        {
            return SkinnedVertexHelper.LocalToWorld(index, _worldPositions, null, localToWorld);
        }

        private float WorldDistanceSquared(
            int vertexIndex,
            Vector3 localVertex,
            Vector3 worldCenter,
            Matrix4x4 localToWorld)
        {
            Vector3 worldVertex = _worldPositions != null &&
                                  vertexIndex >= 0 && vertexIndex < _worldPositions.Length
                ? _worldPositions[vertexIndex]
                : localToWorld.MultiplyPoint3x4(localVertex);
            return (worldVertex - worldCenter).sqrMagnitude;
        }

        private void InvalidateCache()
        {
            _cachedMesh = null;
            _cachedMeshDirtyCount = -1;
            _meshVertices = null;
            _meshNormals = null;
            _meshTriangles = null;
            _worldPositions = null;
            _distanceWorldPositions = null;
            _raycastMesh = null;
            _hasBakedRaycastMesh = false;
            _restSpaceConverterCache.Clear();
            _cachedBrushSourceRenderer = null;
            _cachedBrushTargetRenderer = null;
            _cachedBrushSkinnedRenderer = null;
            _cachedBrushBones = null;
            _cachedBrushPoseHash = 0;
            _cachedBrushRuntimeRevision = -1;
            _cachedBrushRendererDirtyCount = -1;
            _cachedBrushVisualMeshDirtyCount = -1;
            _hasBrushSnapshotState = false;
            _cachedBrushProxyMappingRevision = -1;
            _mirrorMap = null;
            _mirrorMapMeshId = 0;
            _mirrorMapDirtyCount = -1;
            _adjacency = null;
            InvalidatePenetrationCache();
            ClearConnectedVerticesCache();
            ClearGeodesicDistanceCache();
        }

        private Renderer ResolveBrushRenderer(LatticeDeformer deformer)
        {
            Renderer sourceRenderer = deformer != null ? deformer.GetComponent<Renderer>() : null;
            int mappingRevision = LatticePreviewUtility.ProxyMappingRevision;
            if (ReferenceEquals(sourceRenderer, _cachedBrushSourceRenderer) &&
                _cachedBrushProxyMappingRevision == mappingRevision)
                return _cachedBrushTargetRenderer;

            _cachedBrushSourceRenderer = sourceRenderer;
            _cachedBrushProxyMappingRevision = mappingRevision;
            Renderer targetRenderer = sourceRenderer;
            if (sourceRenderer != null &&
                LatticePreviewUtility.TryGetPreviewProxy(sourceRenderer, out Renderer proxy))
                targetRenderer = proxy;
            _cachedBrushTargetRenderer = targetRenderer;
            _cachedBrushSkinnedRenderer = targetRenderer as SkinnedMeshRenderer;
            _cachedBrushBones = _cachedBrushSkinnedRenderer != null
                ? _cachedBrushSkinnedRenderer.bones
                : null;
            _cachedBrushRendererDirtyCount = _cachedBrushSkinnedRenderer != null
                ? EditorUtility.GetDirtyCount(_cachedBrushSkinnedRenderer)
                : -1;
            _hasBrushSnapshotState = false;
            return _cachedBrushTargetRenderer;
        }

        private SymmetryVertexMap ResolveMirrorMap()
        {
            int meshId = _cachedMesh != null ? _cachedMesh.GetInstanceID() : 0;
            int dirtyCount = _cachedMesh != null ? EditorUtility.GetDirtyCount(_cachedMesh) : -1;
            if (_mirrorMap != null && _mirrorMapMeshId == meshId &&
                _mirrorMapDirtyCount == dirtyCount && _mirrorMapAxis == s_mirrorAxis)
                return _mirrorMap;

            _mirrorMap = SymmetryVertexMapCache.GetOrCreate(
                _cachedMesh,
                (int)s_mirrorAxis,
                unmatchedBehavior: UnmatchedSymmetryVertexBehavior.Skip);
            _mirrorMapMeshId = meshId;
            _mirrorMapDirtyCount = dirtyCount;
            _mirrorMapAxis = s_mirrorAxis;
            return _mirrorMap;
        }

        private static Material s_brushDotMaterial;
        private static Texture2D s_brushCircleTex;

        private static void BeginBatchedDotDraw(
            out bool matrixPushed,
            out bool drawingQuads)
        {
            matrixPushed = false;
            drawingQuads = false;
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
            matrixPushed = true;
            GL.MultMatrix(Matrix4x4.identity);
            GL.Begin(GL.QUADS);
            drawingQuads = true;
        }

        private static void EndBatchedDotDraw(bool matrixPushed, bool drawingQuads)
        {
            try
            {
                if (drawingQuads) GL.End();
            }
            finally
            {
                if (matrixPushed) GL.PopMatrix();
            }
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

        private void DrawAffectedVertices(LatticeDeformer deformer, Vector3 worldHitPoint, Transform meshTransform)
        {
            if (!TryGetBrushBuffers(
                    deformer, out _, out var displacements, out _)) return;
            float worldRadius = s_brushRadius;
            float radiusSq = worldRadius * worldRadius;
            int vertexCount = _meshVertices.Length;
            Color brushColor = GetBrushColor();
            var matrix = meshTransform.localToWorldMatrix;
            var worldCenter = worldHitPoint;
            var cam = Camera.current;
            if (cam == null) return;
            var camRight = cam.transform.right;
            var camUp = cam.transform.up;
            float baseSize = HandleUtility.GetHandleSize(meshTransform.position) * 0.004f;

            bool matrixPushed = false;
            bool drawingQuads = false;
            try
            {
                BeginBatchedDotDraw(out matrixPushed, out drawingQuads);
                bool useGeodesicCandidates = s_useSurfaceDistance && _hasGeodesicDistanceCache;
                int candidateCount = useGeodesicCandidates
                    ? _geodesicWorkspace.VisitedCount
                    : vertexCount;
                int sampleStride = GetVisualizationSampleStride(candidateCount);
                int iterationCount = (candidateCount + sampleStride - 1) / sampleStride;
                for (int iteration = 0; iteration < iterationCount; iteration++)
                {
                    int candidateIndex = iteration * sampleStride;
                    int i = useGeodesicCandidates
                        ? _geodesicWorkspace.GetVisitedVertex(candidateIndex)
                        : candidateIndex;
                    if (s_connectedOnly && _connectedVerticesCache != null && !_connectedVerticesCache.Contains(i))
                        continue;

                    var vertex = _meshVertices[i] + displacements[i];

                    float falloff;
                    if (useGeodesicCandidates)
                    {
                        if (!_geodesicWorkspace.TryGetDistance(i, out float geodesicDist))
                            continue;
                        float t = geodesicDist / worldRadius;
                        falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);
                    }
                    else
                    {
                        float distSq = WorldDistanceSquared(i, vertex, worldCenter, matrix);
                        if (distSq > radiusSq) continue;
                        float dist = Mathf.Sqrt(distSq);
                        float t = dist / worldRadius;
                        falloff = BrushDeformer.EvaluateFalloff(s_brushFalloff, t);
                    }

                    if (falloff < 0.01f) continue;

                    var worldPos = SkinnedVertexHelper.LocalToWorld(i, _worldPositions, vertex, matrix);
                    Color dotColor = HeatmapColor(falloff);
                    dotColor.a = 0.4f + 0.5f * falloff;
                    float dotSize = Mathf.Lerp(s_vertexDotSize * 0.6f, s_vertexDotSize * 1.4f, falloff);
                    DrawBatchedDot(worldPos, dotColor, baseSize * dotSize, camRight, camUp);
                }
            }
            finally
            {
                EndBatchedDotDraw(matrixPushed, drawingQuads);
            }
        }

        internal static int GetVisualizationSampleStride(int candidateCount)
        {
            return candidateCount <= MaxAffectedVertexDots
                ? 1
                : (candidateCount + MaxAffectedVertexDots - 1) / MaxAffectedVertexDots;
        }

        internal static bool RequiresSurfaceQuery(EventType eventType)
        {
            return eventType == EventType.Repaint ||
                   eventType == EventType.MouseMove ||
                   eventType == EventType.MouseDown ||
                   eventType == EventType.MouseDrag;
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

            bool matrixPushed = false;
            bool drawingQuads = false;
            try
            {
                BeginBatchedDotDraw(out matrixPushed, out drawingQuads);
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
            }
            finally
            {
                EndBatchedDotDraw(matrixPushed, drawingQuads);
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
            if (!TryGetBrushBuffers(
                    deformer, out var layer, out var displacements, out _)) return;
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

            bool matrixPushed = false;
            bool drawingQuads = false;
            try
            {
                BeginBatchedDotDraw(out matrixPushed, out drawingQuads);
                for (int i = 0; i < vertexCount; i++)
                {
                    float maskValue = mask[i];
                    if (maskValue > 1f - 1e-6f) continue; // Fully editable, skip

                    var vertex = _meshVertices[i] + displacements[i];
                    var worldPos = SkinnedVertexHelper.LocalToWorld(i, _worldPositions, vertex, matrix);

                    // Red = protected (mask=0), Green = editable (mask=1)
                    float protection = 1f - maskValue;
                    Color dotColor = Color.Lerp(new Color(0.2f, 1f, 0.2f, 0.4f), new Color(1f, 0.2f, 0.2f, 0.8f), protection);
                    float dotRadius = baseSize * (1f + protection * 2f);
                    DrawBatchedDot(worldPos, dotColor, dotRadius, camRight, camUp);
                }
            }
            finally
            {
                EndBatchedDotDraw(matrixPushed, drawingQuads);
            }
        }

        private void UpdatePenetrationDetection(LatticeDeformer deformer)
        {
            if (!s_showPenetration || s_penetrationReference == null || _meshVertices == null)
            {
                InvalidatePenetrationCache();
                return;
            }

            var deformerTransform = deformer.MeshTransform;
            if (deformerTransform == null)
            {
                InvalidatePenetrationCache();
                return;
            }

            var refTransform = s_penetrationReference.transform;
            Mesh refMesh = null;
            if (s_penetrationReference is SkinnedMeshRenderer skinnedReference)
            {
                refMesh = skinnedReference.sharedMesh;
            }
            else if (s_penetrationReference is MeshRenderer meshReference)
            {
                var filter = meshReference.GetComponent<MeshFilter>();
                refMesh = filter != null ? filter.sharedMesh : null;
            }

            if (refTransform == null || refMesh == null)
            {
                InvalidatePenetrationCache();
                return;
            }

            var sourceMesh = deformer.SourceMesh;
            var runtimeMesh = deformer.RuntimeMesh;
            var nextKey = new PenetrationDetectionCacheKey(
                deformer.ComputeLayeredStateHash(),
                s_penetrationSettingsRevision,
                s_penetrationReference.GetInstanceID(),
                refMesh.GetInstanceID(),
                sourceMesh != null ? sourceMesh.GetInstanceID() : 0,
                runtimeMesh != null ? runtimeMesh.GetInstanceID() : 0,
                _meshVertices.Length,
                refMesh.vertexCount,
                deformerTransform.localToWorldMatrix,
                refTransform.worldToLocalMatrix);

            // A skinned reference can change pose without changing its shared mesh or transform.
            // Let ClearanceQueryCache inspect its baked geometry on every update in that case.
            if (!(s_penetrationReference is SkinnedMeshRenderer) &&
                _hasPenetrationCacheKey && _penetrationCacheKey.Equals(nextKey))
            {
                return;
            }

            // Penetration is a property of the final stack, not only the active brush
            // layer. Force evaluation so both detection and highlighting use it.
            runtimeMesh = deformer.Deform(false);
            if (runtimeMesh == null || runtimeMesh.vertexCount != _meshVertices.Length)
            {
                InvalidatePenetrationCache();
                return;
            }

            var deformedVertices = runtimeMesh.vertices;
            nextKey = new PenetrationDetectionCacheKey(
                deformer.ComputeLayeredStateHash(),
                s_penetrationSettingsRevision,
                s_penetrationReference.GetInstanceID(),
                refMesh.GetInstanceID(),
                sourceMesh != null ? sourceMesh.GetInstanceID() : 0,
                runtimeMesh.GetInstanceID(),
                deformedVertices.Length,
                refMesh.vertexCount,
                deformerTransform.localToWorldMatrix,
                refTransform.worldToLocalMatrix);

            var detected = PenetrationDetector.DetectPenetration(
                deformedVertices,
                deformerTransform.localToWorldMatrix,
                s_penetrationReference);
            _penetratingVertices = detected;
            _penetrationDeformedVertices = deformedVertices;
            _penetrationCacheKey = nextKey;
            _hasPenetrationCacheKey = true;
        }

        private void InvalidatePenetrationCache()
        {
            _penetratingVertices = null;
            _penetrationDeformedVertices = null;
            _penetrationCacheKey = default;
            _hasPenetrationCacheKey = false;
        }

        private void DrawPenetrationHighlight(Transform meshTransform)
        {
            if (_penetratingVertices == null ||
                _penetratingVertices.Count == 0 ||
                _penetrationDeformedVertices == null)
            {
                return;
            }

            var matrix = meshTransform.localToWorldMatrix;
            var camForward = Camera.current != null ? Camera.current.transform.forward : Vector3.forward;
            Handles.color = new Color(1f, 0f, 0f, 0.8f);

            foreach (int i in _penetratingVertices)
            {
                if (i >= 0 && i < _penetrationDeformedVertices.Length)
                {
                    Vector3 worldPos = SkinnedVertexHelper.LocalToWorld(
                        i,
                        _worldPositions,
                        _penetrationDeformedVertices[i],
                        matrix);
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
                var method = typeof(HandleUtility).GetMethod(
                    "IntersectRayMesh",
                    BindingFlags.Static | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(Ray), typeof(Mesh), typeof(Matrix4x4), typeof(RaycastHit).MakeByRefType() },
                    null);
                if (method != null)
                {
                    s_intersectRayMesh = (IntersectRayMeshDelegate)Delegate.CreateDelegate(
                        typeof(IntersectRayMeshDelegate),
                        method);
                }
            }

            hit = default;
            return s_intersectRayMesh != null &&
                   s_intersectRayMesh(ray, mesh, matrix, out hit);
        }

        private void EnsureAdjacencyBuilt()
        {
            if (_adjacency != null) return;
            if (_meshVertices == null || _meshTriangles == null) return;
            using (s_buildAdjacencyMarker.Auto())
            {
                _adjacency = MeshAdjacency.Build(_meshVertices.Length, _meshTriangles);
            }
        }

        private int FindNearestVertex(Vector3 worldPoint)
        {
            if (_meshVertices == null || _meshVertices.Length == 0)
            {
                return -1;
            }

            int nearest = -1;
            float nearestDistSq = float.MaxValue;
            Matrix4x4 localToWorld = _activeDeformer != null
                ? _activeDeformer.MeshTransform.localToWorldMatrix
                : Matrix4x4.identity;
            for (int i = 0; i < _meshVertices.Length; i++)
            {
                Vector3 worldVertex = _worldPositions != null && i < _worldPositions.Length
                    ? _worldPositions[i]
                    : localToWorld.MultiplyPoint3x4(_meshVertices[i]);
                float distSq = (worldVertex - worldPoint).sqrMagnitude;
                if (distSq < nearestDistSq)
                {
                    nearestDistSq = distSq;
                    nearest = i;
                }
            }

            return nearest;
        }

        private HashSet<int> FindConnectedVertices(
            int startVertex,
            float maxDistance,
            HashSet<int> reusable = null)
        {
            var connected = reusable ?? new HashSet<int>();
            connected.Clear();
            _connectedQueue.Clear();
            if (_adjacency == null || startVertex < 0 || startVertex >= _adjacency.VertexCount)
            {
                return connected;
            }
            _connectedQueue.Enqueue(startVertex);
            connected.Add(startVertex);
            Vector3[] worldPositions = GetDistanceWorldPositions();
            if (worldPositions == null || startVertex >= worldPositions.Length)
            {
                return connected;
            }

            while (_connectedQueue.Count > 0)
            {
                int current = _connectedQueue.Dequeue();
                for (int edge = _adjacency.GetNeighborStart(current);
                     edge < _adjacency.GetNeighborEnd(current); edge++)
                {
                    int neighbor = _adjacency.GetNeighbor(edge);
                    if (connected.Contains(neighbor))
                    {
                        continue;
                    }

                    // Only include vertices within brush radius (Euclidean check for performance)
                    float distSq = (worldPositions[neighbor] - worldPositions[startVertex]).sqrMagnitude;
                    if (distSq <= maxDistance * maxDistance)
                    {
                        connected.Add(neighbor);
                        _connectedQueue.Enqueue(neighbor);
                    }
                }
            }

            return connected;
        }

        private void UpdateConnectedVerticesCache(Vector3 worldHitPoint)
        {
            if (!s_connectedOnly)
            {
                _connectedVerticesCache?.Clear();
                _mirrorConnectedVerticesCache?.Clear();
                _connectedCacheStartVertex = -1;
                return;
            }

            EnsureAdjacencyBuilt();

            int nearestVertex = FindNearestVertex(worldHitPoint);
            if (nearestVertex < 0)
            {
                _connectedVerticesCache?.Clear();
                _connectedCacheStartVertex = -1;
                return;
            }

            float radius = s_brushRadius;
            Matrix4x4 matrix = _activeDeformer != null && _activeDeformer.MeshTransform != null
                ? _activeDeformer.MeshTransform.localToWorldMatrix
                : Matrix4x4.identity;
            if (nearestVertex == _connectedCacheStartVertex &&
                Mathf.Approximately(radius, _connectedCacheRadius) &&
                _connectedCacheGeometryRevision == _distanceGeometryRevision &&
                _connectedCacheMatrix == matrix)
            {
                return;
            }

            _connectedCacheStartVertex = nearestVertex;
            _connectedCacheRadius = radius;
            _connectedCacheGeometryRevision = _distanceGeometryRevision;
            _connectedCacheMatrix = matrix;
            _connectedVerticesCache = FindConnectedVertices(
                nearestVertex, radius, _connectedVerticesCache);
        }

        private void ClearConnectedVerticesCache()
        {
            _connectedVerticesCache?.Clear();
            _mirrorConnectedVerticesCache?.Clear();
            _connectedCacheStartVertex = -1;
            _connectedCacheRadius = -1f;
            _connectedCacheGeometryRevision = -1;
        }

        private void UpdateGeodesicDistanceCache(Vector3 worldHitPoint)
        {
            if (!s_useSurfaceDistance)
            {
                _hasGeodesicDistanceCache = false;
                _geodesicCacheStartVertex = -1;
                return;
            }

            EnsureAdjacencyBuilt();
            int nearest = FindNearestVertex(worldHitPoint);
            float radius = s_brushRadius;
            if (nearest < 0)
            {
                return;
            }

            Matrix4x4 matrix = _activeDeformer != null && _activeDeformer.MeshTransform != null
                ? _activeDeformer.MeshTransform.localToWorldMatrix
                : Matrix4x4.identity;
            if (_hasGeodesicDistanceCache &&
                nearest == _geodesicCacheStartVertex &&
                Mathf.Approximately(radius, _geodesicCacheRadius) &&
                _geodesicCacheGeometryRevision == _distanceGeometryRevision &&
                _geodesicCacheMatrix == matrix)
            {
                return;
            }

            _geodesicCacheStartVertex = nearest;
            _geodesicCacheRadius = radius;
            _geodesicCacheGeometryRevision = _distanceGeometryRevision;
            _geodesicCacheMatrix = matrix;
            using (s_geodesicMarker.Auto())
            {
                _hasGeodesicDistanceCache = GeodesicDistanceCalculator.ComputeDistances(
                    nearest, radius, _adjacency, GetDistanceWorldPositions(), _geodesicWorkspace);
            }
        }

        private Vector3[] GetDistanceWorldPositions()
        {
            if (_meshVertices == null) return null;
            if (_worldPositions != null && _worldPositions.Length == _meshVertices.Length)
            {
                return _worldPositions;
            }

            int count = _meshVertices.Length;
            if (_distanceWorldPositions == null || _distanceWorldPositions.Length != count)
            {
                _distanceWorldPositions = new Vector3[count];
            }

            Matrix4x4 localToWorld = _activeDeformer != null
                ? _activeDeformer.MeshTransform.localToWorldMatrix
                : Matrix4x4.identity;
            for (int i = 0; i < count; i++)
            {
                _distanceWorldPositions[i] = localToWorld.MultiplyPoint3x4(_meshVertices[i]);
            }

            return _distanceWorldPositions;
        }

        private void ClearGeodesicDistanceCache()
        {
            _hasGeodesicDistanceCache = false;
            _geodesicCacheStartVertex = -1;
            _geodesicCacheRadius = -1f;
            _geodesicCacheGeometryRevision = -1;
        }

        private Vector3[] GetSmoothDisplacementBuffer(int vertexCount)
        {
            if (_smoothDisplacements == null || _smoothDisplacements.Length != vertexCount)
                _smoothDisplacements = new Vector3[vertexCount];
            return _smoothDisplacements;
        }

        internal static void ClearAllDisplacements(LatticeDeformer deformer)
        {
            if (deformer == null) return;
            if (deformer.ActiveLayerType != MeshDeformerLayerType.Brush) return;
            int previousStateHash = deformer.ComputeLayeredStateHash();
            Undo.RecordObject(deformer, LatticeLocalization.Tr(LocKey.ClearAll));
            deformer.ClearDisplacements();
            if (deformer.ComputeLayeredStateHash() != previousStateHash)
            {
                LatticePrefabUtility.MarkModified(deformer);
            }
            bool assignToRenderer = LatticePreviewUtility.ShouldAssignRuntimeMesh();
            deformer.Deform(assignToRenderer);
            LatticePreviewUtility.RequestSceneRepaint();
        }

        internal static void ClearActiveMask(LatticeDeformer deformer)
        {
            if (deformer == null) return;
            if (!TryGetActiveLayer(deformer, out var layer)) return;
            int previousStateHash = deformer.ComputeLayeredStateHash();
            Undo.RecordObject(deformer, LatticeLocalization.Tr(LocKey.ClearMask));
            layer.ClearVertexMask();
            if (deformer.ComputeLayeredStateHash() != previousStateHash)
            {
                LatticePrefabUtility.MarkModified(deformer);
            }
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
            // Brush Mode toolbar (icon + text)
            var modeContent = new GUIContent[]
            {
                ToolIcons.Content(ToolIcons.Normal, LocKey.Normal),
                ToolIcons.Content(ToolIcons.Move, LocKey.Move),
                ToolIcons.Content(ToolIcons.Smooth, LocKey.Smooth),
                ToolIcons.Content(ToolIcons.Mask, LocKey.Mask),
            };
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
                radiusCm, 0f, 10f);
            if (EditorGUI.EndChangeCheck())
                BrushToolHandler.BrushRadius = radiusCm / 100f;

            // Display strength as 0-100%, store internally as 0-1
            float strengthPercent = BrushToolHandler.BrushStrength * 100f;
            strengthPercent = EditorGUILayout.Slider(
                new GUIContent(LatticeLocalization.Tr(LocKey.BrushStrength) + " (%)", LatticeLocalization.Tooltip(LocKey.BrushStrength)),
                strengthPercent, 0f, 100f);
            BrushToolHandler.BrushStrength = strengthPercent / 100f;

            if (BrushToolHandler.CurrentBrushMode == BrushToolHandler.BrushMode.Move &&
                deformer != null && deformer.GetComponent<SkinnedMeshRenderer>() != null)
            {
                SkinnedVertexHelper.StoreMovesInRestSpace = EditorGUILayout.Toggle(
                    LatticeLocalization.Content(LocKey.StoreMoveInRestSpace),
                    SkinnedVertexHelper.StoreMovesInRestSpace);
            }

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
                    ToolIcons.Content(ToolIcons.Invert, LocKey.InvertBrush));
                BrushToolHandler.BackfaceCulling = GUILayout.Toggle(
                    BrushToolHandler.BackfaceCulling,
                    ToolIcons.Content(ToolIcons.BackfaceCull, LocKey.BackfaceCulling));
            }

            // --- Advanced section (foldout) ---
            s_showAdvancedSection = EditorGUILayout.Foldout(s_showAdvancedSection, LatticeLocalization.Tr(LocKey.Advanced), true);
            if (s_showAdvancedSection)
            {
                EditorGUI.indentLevel++;
                BrushToolHandler.ConnectedOnly = GUILayout.Toggle(
                    BrushToolHandler.ConnectedOnly,
                    ToolIcons.Content(ToolIcons.Connected, LocKey.ConnectedOnly));
                BrushToolHandler.UseSurfaceDistance = GUILayout.Toggle(
                    BrushToolHandler.UseSurfaceDistance,
                    ToolIcons.Content(ToolIcons.SurfaceDistance, LocKey.SurfaceDistance));
                EditorGUI.indentLevel--;
            }

            // --- Mirror section (foldout) ---
            s_showMirrorSection = EditorGUILayout.Foldout(s_showMirrorSection, LatticeLocalization.Tr(LocKey.EnableMirror), true);
            if (s_showMirrorSection)
            {
                EditorGUI.indentLevel++;
                BrushToolHandler.MirrorEditing = GUILayout.Toggle(
                    BrushToolHandler.MirrorEditing,
                    ToolIcons.Content(ToolIcons.Mirror, LocKey.EnableMirror));

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
                s_showWireframe = GUILayout.Toggle(s_showWireframe,
                    ToolIcons.Content(ToolIcons.Eye, LocKey.ShowWireframe));
                BrushToolHandler.ShowAffectedVertices = GUILayout.Toggle(
                    BrushToolHandler.ShowAffectedVertices,
                    ToolIcons.Content(ToolIcons.Eye, LocKey.ShowAffectedVertices));
                BrushToolHandler.ShowDisplacementHeatmap = GUILayout.Toggle(
                    BrushToolHandler.ShowDisplacementHeatmap,
                    ToolIcons.Content(ToolIcons.Eye, LocKey.ShowDisplacementHeatmap));
                BrushToolHandler.VertexDotSize = EditorGUILayout.Slider(
                    LatticeLocalization.Content(LocKey.DotSize),
                    BrushToolHandler.VertexDotSize, 1f, 8f);

                BrushToolHandler.ShowPenetration = GUILayout.Toggle(
                    BrushToolHandler.ShowPenetration,
                    ToolIcons.Content(ToolIcons.Eye, LocKey.ShowPenetration));
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
                if (GUILayout.Button(ToolIcons.Content(ToolIcons.Clear, LocKey.ClearAll)))
                {
                    if (deformer != null && deformer.ActiveLayerType == MeshDeformerLayerType.Brush)
                    {
                        BrushToolHandler.ClearAllDisplacements(deformer);
                    }
                }

                if (GUILayout.Button(ToolIcons.Content(ToolIcons.Clear, LocKey.ClearMask)))
                {
                    if (deformer != null && deformer.ActiveLayerType == MeshDeformerLayerType.Brush)
                    {
                        BrushToolHandler.ClearActiveMask(deformer);
                    }
                }
            }

            GUILayout.Space(2f);
            GUILayout.Label(LatticeLocalization.Tr(LocKey.AltScrollHint), EditorStyles.miniLabel);
        }
    }
}
#endif
