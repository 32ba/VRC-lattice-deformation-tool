#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using UnityEditor;
using Unity.Profiling;
using UnityEngine;
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
        private readonly SkinnedPoseSnapshot _poseSnapshot = new SkinnedPoseSnapshot("Brush Posed Surface");
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
        internal const int MaxAffectedVertexDots = BrushVertexVisualization.MaxAffectedVertexDots;
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
        private DeformerEditSession _editSession;

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
            _editSession = DeformerEditSession.TryBegin(deformer, MeshDeformerLayerType.Brush, GetUndoLabel());
            _isStrokeActive = _editSession != null;
        }

        private void EndStroke()
        {
            _editSession?.Dispose();
            ResetStrokeState();
        }

        private void ResetStrokeState()
        {
            _isStrokeActive = false;
            _editSession?.Abandon();
            _editSession = null;
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

                if (_isStrokeActive && !_editSession.MatchesTarget(deformer)) EndStroke();
                if (_isStrokeActive && evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
                {
                    _editSession.TryCancel();
                    ResetStrokeState();
                    ClearConnectedVerticesCache();
                    ClearGeodesicDistanceCache();
                    evt.Use();
                    return;
                }

                // Layout only registers control ownership. Baking a posed mesh and
                // raycasting here duplicates the following Repaint event in the same
                // Scene GUI frame without producing any visible or editable result.
                if (evt.type == EventType.Layout)
                {
                    HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
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
                    if (!_editSession.TryPrepareWrite(deformer)) { EndStroke(); return; }
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
                    if (!_isStrokeActive) return;
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
            float strength = s_brushStrength * 0.01f;
            float direction = s_invertBrush ? -1f : 1f;
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

                _editSession?.RecordChange();
                using (s_deformMarker.Auto())
                    LatticePreviewUtility.RefreshInteractiveDeformation(deformer);
            }

            _lastMousePosition = evt.mousePosition;
        }

        private bool ApplyNormalBrush(LatticeDeformer deformer, Vector3 worldHitPoint, float worldRadius, float strength, float direction)
        {
            if (!TryGetBrushBuffers(
                    deformer, out _, out var displacements, out var vertexMask)) return false;
            Transform meshTransform = deformer.MeshTransform;
            Matrix4x4 localToWorld = meshTransform.localToWorldMatrix;

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

            var query = CreateBrushInfluenceQuery(worldHitPoint, worldRadius, localToWorld, localCameraForward);
            return BrushDisplacementApplication.Normal(_meshVertices, _meshNormals, displacements, vertexMask, query, strength, direction);
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

            SkinnedVertexHelper.RestSpaceDeltaConverter restSpaceConverter = null;
            if (SkinnedVertexHelper.StoreMovesInRestSpace)
            {
                using (s_restSpaceMarker.Auto())
                    restSpaceConverter = _restSpaceConverterCache.Get(deformer);
            }

            var query = CreateBrushInfluenceQuery(worldHitPoint, worldRadius, localToWorld, localCameraForward);
            return BrushDisplacementApplication.Move(_meshVertices, displacements, vertexMask, query, strength, localDelta, restSpaceConverter);
        }

        private bool ApplySmoothBrush(LatticeDeformer deformer, Vector3 worldHitPoint, float worldRadius, float strength)
        {
            if (!TryGetBrushBuffers(
                    deformer, out _, out var displacements, out var vertexMask)) return false;
            Matrix4x4 localToWorld = deformer.MeshTransform.localToWorldMatrix;
            EnsureAdjacencyBuilt();

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

            var query = CreateBrushInfluenceQuery(worldHitPoint, worldRadius, localToWorld, localCameraForward);
            return BrushDisplacementApplication.Smooth(_meshVertices, displacements, currentDisplacements, vertexMask, _adjacency, query, smoothFactor);
        }

        private bool ApplyMaskBrush(LatticeDeformer deformer, Vector3 worldHitPoint, float worldRadius)
        {
            Matrix4x4 localToWorld = deformer.MeshTransform.localToWorldMatrix;
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

            var query = CreateBrushInfluenceQuery(worldHitPoint, worldRadius, localToWorld, localCameraForward);
            return BrushDisplacementApplication.Mask(_meshVertices, displacements, layer, query, targetValue, s_brushStrength);
        }

        private BrushInfluenceQuery CreateBrushInfluenceQuery(Vector3 center, float radius,
            Matrix4x4 localToWorld, Vector3 localCameraForward) =>
            new BrushInfluenceQuery(_worldPositions, localToWorld, center, radius, s_brushFalloff,
                s_connectedOnly ? _connectedVerticesCache : null,
                s_backfaceCulling ? _meshNormals : null, localCameraForward,
                s_useSurfaceDistance && _hasGeodesicDistanceCache ? _geodesicWorkspace : null);

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

            // Preserve the existing mirror policy: Euclidean distance, no backface filter.
            var query = new BrushInfluenceQuery(_worldPositions, localToWorld, mirroredWorldCenter, worldRadius,
                s_brushFalloff, mirrorConnected, null, Vector3.forward, null);

            switch (s_brushMode)
            {
                case BrushMode.Normal:
                {
                    BrushDisplacementApplication.Normal(_meshVertices, _meshNormals, displacements, vertexMask,
                        query, strength, direction, mirrorMap, (int)s_mirrorAxis);
                    break;
                }

                case BrushMode.Smooth:
                {
                    EnsureAdjacencyBuilt();
                    var currentDisplacements = GetSmoothDisplacementBuffer(vertexCount);
                    Array.Copy(displacements, currentDisplacements, vertexCount);
                    float smoothFactor = Mathf.Clamp01(strength * 10f);

                    BrushDisplacementApplication.Smooth(_meshVertices, displacements, currentDisplacements, vertexMask,
                        _adjacency, query, smoothFactor, mirrorMap);
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
                    BrushDisplacementApplication.Move(_meshVertices, displacements, vertexMask,
                        query, strength, mirroredDelta, restSpaceConverter);
                    break;
                }

                case BrushMode.Mask:
                {
                    if (!TryGetBrushLayerFast(deformer, out var layer)) break;
                    layer.EnsureVertexMaskCapacity(vertexCount);
                    float targetValue = s_invertBrush ? 1f : 0f;

                    BrushDisplacementApplication.Mask(_meshVertices, displacements, layer,
                        query, targetValue, s_brushStrength, mirrorMap);
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
                _poseSnapshot.Reset();
                _worldPositions = null;
                _raycastMesh = null;
                _hasBakedRaycastMesh = false;
                _hasBrushSnapshotState = false;
                return;
            }

            Renderer targetRenderer = ResolveBrushRenderer(deformer);
            if (targetRenderer == null)
            {
                _poseSnapshot.Reset();
                _worldPositions = null;
                _raycastMesh = null;
                _hasBakedRaycastMesh = false;
                _hasBrushSnapshotState = false;
                return;
            }

            SkinnedMeshRenderer renderer = targetRenderer as SkinnedMeshRenderer;
            if (renderer == null)
            {
                _poseSnapshot.Reset();
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

            // The final proxy already includes all layers. Capture its posed surface
            // once for both visualization and raycasting, owned by this handler.
            using (s_bakeMeshMarker.Auto())
            {
                _hasBakedRaycastMesh = _poseSnapshot.TryCapture(renderer, _meshVertices.Length);
                _worldPositions = _poseSnapshot.CopyWorldPositions(_worldPositions);
                _raycastMesh = _poseSnapshot.Mesh;
                _raycastMatrix = _poseSnapshot.LocalToWorld;
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

        private void InvalidateCache()
        {
            _poseSnapshot.Reset();
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
                _cachedBrushProxyMappingRevision == mappingRevision &&
                (_cachedBrushTargetRenderer != null || sourceRenderer == null))
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

        private void DrawAffectedVertices(LatticeDeformer deformer, Vector3 worldHitPoint, Transform meshTransform)
        {
            if (!TryGetBrushBuffers(deformer, out _, out var displacements, out _)) return;
            var geometry = new VertexDisplayGeometry(_meshVertices, _worldPositions, displacements,
                meshTransform.localToWorldMatrix);
            BrushVertexVisualization.DrawAffected(geometry, worldHitPoint, s_brushRadius, s_brushFalloff,
                s_vertexDotSize, HandleUtility.GetHandleSize(meshTransform.position) * 0.004f,
                s_connectedOnly ? _connectedVerticesCache : null,
                s_useSurfaceDistance && _hasGeodesicDistanceCache ? _geodesicWorkspace : null);
        }

        internal static int GetVisualizationSampleStride(int candidateCount)
        {
            return BrushVertexVisualization.GetVisualizationSampleStride(candidateCount);
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
            var geometry = new VertexDisplayGeometry(_meshVertices, _worldPositions, deformer.Displacements,
                meshTransform.localToWorldMatrix);
            BrushVertexVisualization.DrawDisplacements(geometry,
                HandleUtility.GetHandleSize(meshTransform.position) * 0.003f);
        }

        private void DrawVertexMaskVisualization(LatticeDeformer deformer, Transform meshTransform)
        {
            if (_meshVertices == null ||
                !TryGetBrushBuffers(deformer, out var layer, out var displacements, out _) ||
                !layer.HasVertexMask()) return;
            var geometry = new VertexDisplayGeometry(_meshVertices, _worldPositions, displacements,
                meshTransform.localToWorldMatrix);
            BrushVertexVisualization.DrawMask(geometry, layer.VertexMask,
                HandleUtility.GetHandleSize(meshTransform.position) * 0.004f);
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
            LatticePreviewUtility.RefreshInteractiveDeformation(deformer);
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
            LatticePreviewUtility.RefreshInteractiveDeformation(deformer);
        }

        internal static bool ShowWireframe
        {
            get => s_showWireframe;
            set => s_showWireframe = value;
        }

        internal static void DrawOverlayGUI(LatticeDeformer deformer) => BrushToolOverlay.Draw(deformer);

    }
}
#endif
