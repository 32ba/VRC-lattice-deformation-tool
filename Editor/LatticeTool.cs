#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.Rendering;
using Net._32Ba.LatticeDeformationTool;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    [EditorTool("Lattice Tool", typeof(LatticeDeformer))]
    public sealed class LatticeDeformerTool : EditorTool
    {
        internal enum MirrorAxis
        {
            X = 0,
            Y = 1,
            Z = 2
        }

        internal enum MirrorBehavior
        {
            Identical = 0,
            Mirrored = 1,
            Antisymmetric = 2
        }

        private static GUIContent s_icon;
        private static bool s_showIndices = false;
        private static bool s_includeInteriorControls = false;
        private static readonly HashSet<int> s_selectedControls = new HashSet<int>();
        private static bool s_mirrorEditing = false;
        private static MirrorAxis s_mirrorAxis = MirrorAxis.X;
        private static MirrorBehavior s_mirrorBehavior = MirrorBehavior.Mirrored;
        private static bool s_occludeWithSceneGeometry = true;
        private static PivotRotation? s_previousPivotRotation;
        private static Vector3Int s_lastGridSize = Vector3Int.one;

        // Constants for bounds comparison and validation
        private const float k_BoundsTolerance = 0.02f;
        private const float k_MaxVolumeRatio = 4f;
        private const float k_HandleSizeMultiplier = 0.08f;

        static LatticeDeformerTool()
        {
            LatticeLocalization.LanguageChanged += OnLanguageChanged;
        }

        private static void OnLanguageChanged()
        {
            if (s_icon != null)
            {
                s_icon.tooltip = LatticeLocalization.Tr("Lattice Tool");
            }

            SceneView.RepaintAll();
        }

        internal static bool ShowIndices
        {
            get => s_showIndices;
            set
            {
                if (s_showIndices == value)
                {
                    return;
                }

                s_showIndices = value;
                SceneView.RepaintAll();
            }
        }

        internal static bool OccludeWithSceneGeometry
        {
            get => s_occludeWithSceneGeometry;
            set
            {
                if (s_occludeWithSceneGeometry == value)
                {
                    return;
                }

                s_occludeWithSceneGeometry = value;
                SceneView.RepaintAll();
            }
        }

        internal static bool MirrorEditing
        {
            get => s_mirrorEditing;
            set
            {
                if (s_mirrorEditing == value)
                {
                    return;
                }

                s_mirrorEditing = value;
                SceneView.RepaintAll();
            }
        }

        internal static bool IncludeInteriorControls
        {
            get => s_includeInteriorControls;
            set
            {
                if (s_includeInteriorControls == value)
                {
                    return;
                }

                s_includeInteriorControls = value;
                if (!s_includeInteriorControls)
                {
                    FilterSelectionToBoundary(s_lastGridSize);
                }

                SceneView.RepaintAll();
            }
        }

        internal static MirrorAxis CurrentMirrorAxis
        {
            get => s_mirrorAxis;
            set
            {
                if (s_mirrorAxis == value)
                {
                    return;
                }

                s_mirrorAxis = value;
                SceneView.RepaintAll();
            }
        }

        internal static MirrorBehavior CurrentMirrorBehavior
        {
            get => s_mirrorBehavior;
            set
            {
                if (s_mirrorBehavior == value)
                {
                    return;
                }

                s_mirrorBehavior = value;
                SceneView.RepaintAll();
            }
        }

        internal static GUIContent[] AxisOptions => new[]
        {
            LatticeLocalization.Content("X"),
            LatticeLocalization.Content("Y"),
            LatticeLocalization.Content("Z")
        };

        internal static GUIContent[] BehaviorOptions => new[]
        {
            LatticeLocalization.Content("Copy"),
            LatticeLocalization.Content("Mirror"),
            LatticeLocalization.Content("Antisymmetric")
        };

        public override GUIContent toolbarIcon
        {
            get
            {
                if (s_icon == null)
                {
                    s_icon = EditorGUIUtility.IconContent("EditCollider");
                }

                if (s_icon != null)
                {
                    s_icon.tooltip = LatticeLocalization.Tr("Lattice Tool");
                }

                return s_icon;
            }
        }

        public override void OnActivated()
        {
            s_previousPivotRotation = Tools.pivotRotation;
            Tools.pivotRotation = PivotRotation.Local;
            Undo.undoRedoPerformed += OnUndoRedo;
            SceneView.RepaintAll();
        }

        public override void OnWillBeDeactivated()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            ClearSelection();
            if (s_previousPivotRotation.HasValue)
            {
                Tools.pivotRotation = s_previousPivotRotation.Value;
                s_previousPivotRotation = null;
            }
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (Event.current != null && Event.current.commandName == "UndoRedoPerformed")
            {
                return;
            }

            if (Tools.pivotRotation != PivotRotation.Local)
            {
                Tools.pivotRotation = PivotRotation.Local;
            }

            if (target is not LatticeDeformer deformer)
            {
                return;
            }

            var settings = deformer.Settings;
            if (settings == null)
            {
                return;
            }

            int controlCount = settings.ControlPointCount;
            if (controlCount == 0)
            {
                return;
            }

            // Prevent selection from switching away from the current lattice while editing.
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            DrawControlHandles(deformer, settings, controlCount);
        }

        private void DrawControlHandles(LatticeDeformer deformer, LatticeAsset settings, int controlCount)
        {
            var sourceTransform = deformer.MeshTransform;
            Renderer proxyRenderer = null;
            deformer.TryGetComponent<Renderer>(out var srcRenderer);

            var useProxy = LatticePreviewUtility.UsePreviewAlignedCage &&
                           srcRenderer != null &&
                           NDMFPreviewProxyUtility.TryGetProxyRenderer(srcRenderer, out proxyRenderer) &&
                           proxyRenderer != null;

            var proxyTransform = useProxy ? proxyRenderer.transform : sourceTransform;

            var sourceToWorld = sourceTransform != null ? sourceTransform.localToWorldMatrix : Matrix4x4.identity;
            var worldToSource = sourceTransform != null ? sourceTransform.worldToLocalMatrix : Matrix4x4.identity;
            var proxyToWorld = proxyTransform != null ? proxyTransform.localToWorldMatrix : Matrix4x4.identity;
            var worldToProxy = proxyTransform != null ? proxyTransform.worldToLocalMatrix : Matrix4x4.identity;
            var sourceToProxy = worldToProxy * sourceToWorld;
            var proxyToSource = worldToSource * proxyToWorld;

            var sourceBounds = settings.LocalBounds;
            var proxyBoundsMesh = useProxy ? LatticePreviewUtility.GetMeshLocalBounds(proxyRenderer) : sourceBounds;
            var proxyBoundsRenderer = useProxy ? LatticePreviewUtility.GetRendererLocalBounds(proxyRenderer) : sourceBounds;
            var proxyBounds = useProxy ? LatticeBoundsUtility.Encapsulate(proxyBoundsMesh, proxyBoundsRenderer) : sourceBounds;
            var proxyBoundsUnscaled = useProxy
                ? LatticeBoundsUtility.DivideByScale(proxyBounds, proxyTransform != null ? proxyTransform.lossyScale : Vector3.one)
                : proxyBounds;
            var proxyVolume = proxyBoundsUnscaled.size.x * proxyBoundsUnscaled.size.y * proxyBoundsUnscaled.size.z;
            var sourceVolume = sourceBounds.size.x * sourceBounds.size.y * sourceBounds.size.z;
            bool proxyTooBig = proxyVolume > 0f && sourceVolume > 0f && (proxyVolume / sourceVolume) > k_MaxVolumeRatio;

            var mode = LatticePreviewUtility.GetAlignMode(deformer);
            // Bounds remap is applied only when user has not provided manual offset/scale (to avoid double application).
            var manualOffset = LatticePreviewUtility.GetManualOffsetProxy(deformer);
            var manualScale = LatticePreviewUtility.GetManualScaleProxy(deformer);
            bool hasManualAdjust = manualOffset != Vector3.zero || manualScale != Vector3.one;

            bool useBoundsRemap =
                useProxy &&
                mode == LatticeDeformer.LatticeAlignMode.Mode3_BoundsRemap &&
                !proxyTooBig &&
                !hasManualAdjust &&
                !LatticeBoundsUtility.AreApproximatelyEqual(sourceBounds, proxyBoundsUnscaled, k_BoundsTolerance);

            var needBoundsMap = useBoundsRemap;

            if (proxyTooBig && LatticePreviewUtility.DebugAlignLogs)
            {
                LatticePreviewUtility.LogAlign("Bounds",
                    $"Proxy bounds skipped (too large). sourceVol={sourceVolume:F3}, proxyVol={proxyVolume:F3}, ratio={(proxyVolume/sourceVolume):F2}");
            }

            // Center offset: apply position delta (mode-dependent), clamp to avoid runaway
            Vector3 centerOffsetProxyLocal = Vector3.zero;
            if (useProxy && mode == LatticeDeformer.LatticeAlignMode.Mode2_TransformPlusCenter)
            {
                // Use renderer local bounds center (already scaled) if available for better accuracy
                var proxyCenterLocal = proxyBoundsUnscaled.center;
                var sourceCenterProxy = sourceToProxy.MultiplyPoint3x4(sourceBounds.center);
                centerOffsetProxyLocal = proxyCenterLocal - sourceCenterProxy;

                var clampMul = LatticePreviewUtility.GetCenterClampMulXY(deformer);
                var clampMin = LatticePreviewUtility.GetCenterClampMinXY(deformer);
                var clampVec = sourceBounds.extents * clampMul;
                clampVec.x = Mathf.Max(clampVec.x, clampMin);
                clampVec.y = Mathf.Max(clampVec.y, clampMin);
                var clampMulZ = LatticePreviewUtility.GetCenterClampMulZ(deformer);
                var clampMinZ = LatticePreviewUtility.GetCenterClampMinZ(deformer);
                clampVec.z = Mathf.Max(sourceBounds.extents.z * clampMulZ, clampMinZ);

                centerOffsetProxyLocal = new Vector3(
                    Mathf.Clamp(centerOffsetProxyLocal.x, -clampVec.x, clampVec.x),
                    Mathf.Clamp(centerOffsetProxyLocal.y, -clampVec.y, clampVec.y),
                    Mathf.Clamp(centerOffsetProxyLocal.z, -clampVec.z, clampVec.z));
            }

            // Apply manual offset (proxy local)
            centerOffsetProxyLocal += manualOffset;

            // Root bone/world offset (affects Skinned meshes when armature position differs)
            Vector3 rootOffsetWorld = Vector3.zero;
            if (useProxy && srcRenderer is SkinnedMeshRenderer srcSkinned && proxyRenderer is SkinnedMeshRenderer proxySkinned)
            {
                var srcRoot = srcSkinned.rootBone != null ? srcSkinned.rootBone : srcRenderer.transform;
                var proxyRoot = proxySkinned.rootBone != null ? proxySkinned.rootBone : proxyRenderer.transform;
                rootOffsetWorld = proxyRoot.position - srcRoot.position;
            }
            var rootOffsetProxyLocal = useProxy ? worldToProxy.MultiplyVector(rootOffsetWorld) : Vector3.zero;

            // Auto-initialize clamp values once per instance based on observed offset
            // Auto alignment is now manual (via button); no automatic recalculation here.

            if (LatticePreviewUtility.DebugAlignLogs && useProxy)
            {
                LatticePreviewUtility.LogAlign("Bounds",
                    $"mode={mode}, sourceBounds={FormatBounds(sourceBounds)}, proxyBounds={FormatBounds(proxyBounds)}, proxyBoundsUnscaled={FormatBounds(proxyBoundsUnscaled)}, needBoundsMap={needBoundsMap}, proxyTooBig={proxyTooBig}, hasManualAdjust={hasManualAdjust}, srcScale={(sourceTransform != null ? sourceTransform.lossyScale : Vector3.one)}, proxyScale={(proxyTransform != null ? proxyTransform.lossyScale : Vector3.one)}, rootOffsetWorld={rootOffsetWorld}, centerOffsetProxyLocal=({centerOffsetProxyLocal.x:F4},{centerOffsetProxyLocal.y:F4},{centerOffsetProxyLocal.z:F4})");
            }
            var gridSize = settings.GridSize;
            int nx = Mathf.Max(1, gridSize.x);
            int ny = Mathf.Max(1, gridSize.y);
            int nz = Mathf.Max(1, gridSize.z);
            s_lastGridSize = new Vector3Int(nx, ny, nz);

            int Index(int x, int y, int z) => x + y * nx + z * nx * ny;

            var worldPositions = new Vector3[controlCount];
            for (int z = 0; z < nz; z++)
            {
                for (int y = 0; y < ny; y++)
                {
                    for (int x = 0; x < nx; x++)
                    {
                        int index = Index(x, y, z);
                        var local = settings.GetControlPointLocal(index);
                        var mappedLocal = (useProxy && needBoundsMap) ? LatticeBoundsUtility.MapPointBetweenBounds(local, sourceBounds, proxyBoundsUnscaled) : local;
                        var proxyLocal = sourceToProxy.MultiplyPoint3x4(mappedLocal) + rootOffsetProxyLocal + centerOffsetProxyLocal;
                        proxyLocal = Vector3.Scale(proxyLocal, LatticePreviewUtility.GetManualScaleProxy(deformer));

                        worldPositions[index] = proxyTransform != null ? proxyTransform.TransformPoint(proxyLocal) : proxyLocal;
                    }
                }
            }

            var previousZTest = Handles.zTest;
            Handles.zTest = OccludeWithSceneGeometry ? CompareFunction.LessEqual : CompareFunction.Always;

            if (MirrorEditing)
            {
                // Include manual scale and offsets in mirror plane bounds
                var mirrorBounds = (useProxy && needBoundsMap) ? proxyBoundsUnscaled : sourceBounds;
                var mirrorScale = LatticePreviewUtility.GetManualScaleProxy(deformer);
                mirrorBounds.size = Vector3.Scale(mirrorBounds.size, mirrorScale);
                mirrorBounds.center += centerOffsetProxyLocal;
                DrawMirrorPlane(mirrorBounds, proxyTransform);
            }

            var cageColor = new Color(1f, 1f, 1f, 0.8f);
            Handles.color = cageColor;

            for (int z = 0; z < nz; z++)
            {
                for (int y = 0; y < ny; y++)
                {
                    for (int x = 0; x < nx; x++)
                    {
                        int index = Index(x, y, z);
                        var from = worldPositions[index];

                        if (x + 1 < nx)
                        {
                            Handles.DrawAAPolyLine(3f, from, worldPositions[Index(x + 1, y, z)]);
                        }

                        if (y + 1 < ny)
                        {
                            Handles.DrawAAPolyLine(3f, from, worldPositions[Index(x, y + 1, z)]);
                        }

                        if (z + 1 < nz)
                        {
                            Handles.DrawAAPolyLine(3f, from, worldPositions[Index(x, y, z + 1)]);
                        }
                    }
                }
            }

            var handleColor = new Color(0.2f, 0.8f, 1f, 0.9f);
            var mirrorPartnerColor = new Color(1f, 0.5f, 0.2f, 0.9f);

            s_selectedControls.RemoveWhere(idx => idx < 0 || idx >= controlCount);

            for (int index = 0; index < controlCount; index++)
            {
                int ix = index % nx;
                int iy = (index / nx) % ny;
                int iz = index / (nx * ny);

                bool onBoundary = IsBoundaryIndex(ix, iy, iz, nx, ny, nz);
                if (!onBoundary && !IncludeInteriorControls)
                {
                    continue;
                }

                var worldPosition = worldPositions[index];
                float handleSize = HandleUtility.GetHandleSize(worldPosition) * k_HandleSizeMultiplier;

                bool isSelected = s_selectedControls.Contains(index);
                bool isMirrorPartner = false;
                if (!isSelected && MirrorEditing && TryGetSymmetryIndex(index, gridSize, CurrentMirrorBehavior, CurrentMirrorAxis, out var symmetryOfIndex))
                {
                    isMirrorPartner = s_selectedControls.Contains(symmetryOfIndex);
                }

                Handles.color = isSelected ? Color.yellow : isMirrorPartner ? mirrorPartnerColor : handleColor;

                bool additive = false;
                var currentEvent = Event.current;
                if (currentEvent != null)
                {
                    additive = currentEvent.shift || currentEvent.control || currentEvent.command;
                }

                if (Handles.Button(worldPosition, Quaternion.identity, handleSize, handleSize, Handles.CubeHandleCap))
                {
                    if (additive)
                    {
                        if (!s_selectedControls.Add(index))
                        {
                            s_selectedControls.Remove(index);
                        }
                    }
                    else
                    {
                        s_selectedControls.Clear();
                        s_selectedControls.Add(index);
                    }

                    SceneView.RepaintAll();
                }

                if (ShowIndices)
                {
                    Handles.Label(worldPosition, $" {index}");
                }
            }

            if (s_selectedControls.Count > 0)
            {
                Vector3 pivot = Vector3.zero;
                foreach (var selectedIndex in s_selectedControls)
                {
                    pivot += worldPositions[selectedIndex];
                }

                pivot /= s_selectedControls.Count;

                if (Tools.pivotRotation == PivotRotation.Global)
                {
                    Handles.Label(pivot, " " + LatticeLocalization.Tr("Global-space editing disabled"));
                }
                else
                {
                    EditorGUI.BeginChangeCheck();
                    var handleRotation = proxyTransform != null ? proxyTransform.rotation : Quaternion.identity;
                    var newPivot = Handles.PositionHandle(pivot, handleRotation);
                    if (EditorGUI.EndChangeCheck())
                    {
                        var delta = newPivot - pivot;
                        if (delta != Vector3.zero)
                        {
                            Undo.RecordObject(deformer, LatticeLocalization.Tr("Move Lattice Controls"));

                            var deltaProxy = proxyTransform != null
                                ? proxyTransform.InverseTransformVector(delta)
                                : delta;
                            var processedIndices = new HashSet<int>();

                            foreach (var selectedIndex in s_selectedControls)
                            {
                                if (!processedIndices.Add(selectedIndex))
                                {
                                    continue;
                                }

                                var newWorldPosition = worldPositions[selectedIndex] + delta;
                                var proxyLocal = proxyTransform != null
                                ? proxyTransform.InverseTransformPoint(newWorldPosition)
                                : newWorldPosition;
                                // remove manual scale before mapping back
                                var scaleProxy = LatticePreviewUtility.GetManualScaleProxy(deformer);
                                proxyLocal = new Vector3(
                                    scaleProxy.x != 0f ? proxyLocal.x / scaleProxy.x : proxyLocal.x,
                                    scaleProxy.y != 0f ? proxyLocal.y / scaleProxy.y : proxyLocal.y,
                                    scaleProxy.z != 0f ? proxyLocal.z / scaleProxy.z : proxyLocal.z);

                                Vector3 storedLocal;
                                if (useProxy)
                                {
                                    var proxyLocalAdjusted = proxyLocal - centerOffsetProxyLocal;
                                    var mappedSource = needBoundsMap
                                        ? LatticeBoundsUtility.MapPointBetweenBounds(proxyLocalAdjusted, proxyBoundsUnscaled, sourceBounds)
                                        : proxyLocalAdjusted;
                                    mappedSource -= rootOffsetProxyLocal;
                                    storedLocal = proxyToSource.MultiplyPoint3x4(mappedSource);
                                    settings.SetControlPointLocal(selectedIndex, storedLocal);
                                }
                                else
                                {
                                    storedLocal = proxyToSource.MultiplyPoint3x4(proxyLocal);
                                    settings.SetControlPointLocal(selectedIndex, storedLocal);
                                }

                                if (MirrorEditing && TryGetSymmetryIndex(selectedIndex, gridSize, CurrentMirrorBehavior, CurrentMirrorAxis, out var mirrorIndex))
                                {
                                    if (!processedIndices.Add(mirrorIndex))
                                    {
                                        continue;
                                    }

                                    Vector3 mirrorLocal;

                                    Vector3 deltaSource;
                                    if (useProxy)
                                    {
                                        deltaSource = needBoundsMap
                                            ? LatticeBoundsUtility.MapDeltaBetweenBounds(deltaProxy, proxyBoundsUnscaled, sourceBounds)
                                            : deltaProxy;
                                        // Remove root offset contribution before mapping back
                                        deltaSource = proxyToSource.MultiplyVector(deltaSource);
                                    }
                                    else
                                    {
                                        deltaSource = proxyToSource.MultiplyVector(deltaProxy);
                                    }

                                    switch (CurrentMirrorBehavior)
                                    {
                                        case MirrorBehavior.Identical:
                                        {
                                            var original = settings.GetControlPointLocal(mirrorIndex);
                                            mirrorLocal = original + deltaSource;
                                            break;
                                        }
                                        case MirrorBehavior.Mirrored:
                                            mirrorLocal = MirrorPointAxis(storedLocal, sourceBounds, CurrentMirrorAxis);
                                            break;
                                        case MirrorBehavior.Antisymmetric:
                                        {
                                            var original = settings.GetControlPointLocal(mirrorIndex);
                                            mirrorLocal = original - deltaSource;
                                            break;
                                        }
                                        default:
                                            mirrorLocal = storedLocal;
                                            break;
                                    }

                                    settings.SetControlPointLocal(mirrorIndex, mirrorLocal);
                                }
                            }

                            if (!IncludeInteriorControls)
                            {
                                settings.RelaxInteriorControlPoints(2);
                            }

                            deformer.InvalidateCache();
                            deformer.Deform(false);
                            LatticePrefabUtility.MarkModified(deformer);
                            LatticePreviewUtility.RequestSceneRepaint();
                        }
                    }
                }
            }

            Handles.zTest = previousZTest;
        }

        private void OnUndoRedo()
        {
            if (target is not LatticeDeformer deformer)
            {
                return;
            }

            deformer.Deform(false);

            LatticePreviewUtility.RequestSceneRepaint();
        }

        private static void DrawMirrorPlane(Bounds bounds, Transform meshTransform)
        {
            var size = bounds.size;
            if (size == Vector3.zero)
            {
                return;
            }

            var centerLocal = bounds.center;
            Vector3 axisA;
            Vector3 axisB;

            switch (CurrentMirrorAxis)
            {
                case MirrorAxis.X:
                    axisA = Vector3.up * (size.y * 0.5f);
                    axisB = Vector3.forward * (size.z * 0.5f);
                    break;
                case MirrorAxis.Y:
                    axisA = Vector3.right * (size.x * 0.5f);
                    axisB = Vector3.forward * (size.z * 0.5f);
                    break;
                case MirrorAxis.Z:
                default:
                    axisA = Vector3.right * (size.x * 0.5f);
                    axisB = Vector3.up * (size.y * 0.5f);
                    break;
            }

            var localCorners = new Vector3[4];
            localCorners[0] = centerLocal + axisA + axisB;
            localCorners[1] = centerLocal + axisA - axisB;
            localCorners[2] = centerLocal - axisA - axisB;
            localCorners[3] = centerLocal - axisA + axisB;

            if (meshTransform != null)
            {
                for (int i = 0; i < localCorners.Length; i++)
                {
                    localCorners[i] = meshTransform.TransformPoint(localCorners[i]);
                }
            }

            var fillColor = new Color(0.3f, 0.6f, 1f, 0.3f);
            var outlineColor = new Color(0.3f, 0.6f, 1f, 0.6f);
            Handles.DrawSolidRectangleWithOutline(localCorners, fillColor, outlineColor);
        }

        private static bool TryGetSymmetryIndex(int index, Vector3Int gridSize, MirrorBehavior behavior, MirrorAxis axis, out int symmetryIndex)
        {
            int nx = Mathf.Max(1, gridSize.x);
            int ny = Mathf.Max(1, gridSize.y);
            int nz = Mathf.Max(1, gridSize.z);

            symmetryIndex = index;

            if (behavior != MirrorBehavior.Identical &&
                behavior != MirrorBehavior.Mirrored &&
                behavior != MirrorBehavior.Antisymmetric)
            {
                return false;
            }

            if ((axis == MirrorAxis.X && nx <= 1) ||
                (axis == MirrorAxis.Y && ny <= 1) ||
                (axis == MirrorAxis.Z && nz <= 1))
            {
                return false;
            }

            int ix = index % nx;
            int iy = (index / nx) % ny;
            int iz = index / (nx * ny);

            int mirrorX = ix;
            int mirrorY = iy;
            int mirrorZ = iz;

            switch (axis)
            {
                case MirrorAxis.X:
                    mirrorX = nx - 1 - ix;
                    break;
                case MirrorAxis.Y:
                    mirrorY = ny - 1 - iy;
                    break;
                case MirrorAxis.Z:
                    mirrorZ = nz - 1 - iz;
                    break;
            }

            symmetryIndex = mirrorX + mirrorY * nx + mirrorZ * nx * ny;
            return symmetryIndex != index;
        }


        private static string FormatBounds(Bounds b)
        {
            return $"center=({b.center.x:F3},{b.center.y:F3},{b.center.z:F3}), size=({b.size.x:F3},{b.size.y:F3},{b.size.z:F3})";
        }

        private static Mesh GetRendererMesh(Renderer renderer)
        {
            switch (renderer)
            {
                case SkinnedMeshRenderer skinned:
                    return skinned.sharedMesh;
                case MeshRenderer meshRenderer:
                    var mf = meshRenderer.GetComponent<MeshFilter>();
                    return mf != null ? mf.sharedMesh : null;
                default:
                    return null;
            }
        }

        private static void AutoInitAlignment(LatticeDeformer deformer, Bounds sourceBounds, Vector3 centerOffsetProxyLocal, bool computeOffset, bool computeScale)
        {
            if (deformer == null)
            {
                return;
            }

            const float eps = 1e-4f;
            var ext = sourceBounds.extents;
            if (computeOffset)
            {
                deformer.ManualOffsetProxy = centerOffsetProxyLocal;
                deformer.AllowCenterOffsetWhenBoundsSkipped = true;
            }

            if (computeScale)
            {
                // Compute scale ratio from proxy vs source bounds sizes if available
                // Here we reuse centerOffsetProxyLocal magnitude relative to bounds as heuristic fallback
                float absX = Mathf.Abs(centerOffsetProxyLocal.x);
                float absY = Mathf.Abs(centerOffsetProxyLocal.y);
                float absZ = Mathf.Abs(centerOffsetProxyLocal.z);

                float sx = ext.x > eps ? (absX / (ext.x + eps) + 1f) : 1f;
                float sy = ext.y > eps ? (absY / (ext.y + eps) + 1f) : 1f;
                float sz = ext.z > eps ? (absZ / (ext.z + eps) + 1f) : 1f;

                deformer.ManualScaleProxy = new Vector3(sx, sy, sz);
            }

            deformer.AlignAutoInitialized = true;
            EditorUtility.SetDirty(deformer);
        }

        private static Vector3 MirrorPointAxis(Vector3 localPoint, Bounds bounds, MirrorAxis axis)
        {
            var mirrored = localPoint;
            var center = bounds.center;

            switch (axis)
            {
                case MirrorAxis.X:
                    mirrored.x = center.x - (localPoint.x - center.x);
                    break;
                case MirrorAxis.Y:
                    mirrored.y = center.y - (localPoint.y - center.y);
                    break;
                case MirrorAxis.Z:
                    mirrored.z = center.z - (localPoint.z - center.z);
                    break;
            }

            return mirrored;
        }

        internal static void ClearSelection()
        {
            if (s_selectedControls.Count == 0)
            {
                return;
            }

            s_selectedControls.Clear();
            SceneView.RepaintAll();
        }

        private static bool IsBoundaryIndex(int ix, int iy, int iz, int nx, int ny, int nz)
        {
            return ix == 0 || ix == nx - 1 || iy == 0 || iy == ny - 1 || iz == 0 || iz == nz - 1;
        }

        private static void FilterSelectionToBoundary(Vector3Int gridSize)
        {
            if (s_selectedControls.Count == 0)
            {
                return;
            }

            int nx = Mathf.Max(1, gridSize.x);
            int ny = Mathf.Max(1, gridSize.y);
            int nz = Mathf.Max(1, gridSize.z);

            s_selectedControls.RemoveWhere(index =>
            {
                int ix = index % nx;
                int iy = (index / nx) % ny;
                int iz = index / (nx * ny);
                return !IsBoundaryIndex(ix, iy, iz, nx, ny, nz);
            });
        }

        internal static string GetSelectionLabel()
        {
            if (s_selectedControls.Count == 0)
            {
                return LatticeLocalization.Tr("Selected: None");
            }

            if (s_selectedControls.Count == 1)
            {
                foreach (var index in s_selectedControls)
                {
                    return string.Format(LatticeLocalization.Tr("Selected: {0}"), index);
                }
            }

            return string.Format(LatticeLocalization.Tr("Selected: {0} controls"), s_selectedControls.Count);
        }
    }

    [Overlay(typeof(SceneView), "Lattice Tool", defaultDisplay = true)]
    internal sealed class LatticeDeformerToolOverlay : IMGUIOverlay, ITransientOverlay
    {
        public bool visible => ToolManager.activeToolType == typeof(LatticeDeformerTool);

        public override void OnGUI()
        {
            displayName = LatticeLocalization.Tr("Lattice Tool");

            if (ToolManager.activeToolType != typeof(LatticeDeformerTool))
            {
                GUILayout.Label(LatticeLocalization.Content("Activate the Lattice Tool to access settings."), EditorStyles.miniLabel);
                return;
            }

            using (new GUILayout.VerticalScope(GUILayout.MinWidth(260f)))
            {
                GUILayout.Label(LatticeLocalization.Content("Lattice Tool"), EditorStyles.boldLabel);

                DrawLanguageSelector();
                GUILayout.Space(4f);

                LatticeDeformerTool.ShowIndices = GUILayout.Toggle(LatticeDeformerTool.ShowIndices, LatticeLocalization.Content("Show Control IDs"));

                GUILayout.Label(LatticeLocalization.Content("Control Point Scope"), EditorStyles.miniLabel);
                int scopeSelection = GUILayout.Toolbar(
                    LatticeDeformerTool.IncludeInteriorControls ? 1 : 0,
                    new[]
                    {
                        LatticeLocalization.Content("Boundary Only"),
                        LatticeLocalization.Content("All Controls")
                    });
                bool includeInterior = scopeSelection == 1;
                LatticeDeformerTool.IncludeInteriorControls = includeInterior;
                GUILayout.Space(2f);

                bool keepControlsVisible = GUILayout.Toggle(
                    !LatticeDeformerTool.OccludeWithSceneGeometry,
                    LatticeLocalization.Content("Keep control points visible through objects"));
                LatticeDeformerTool.OccludeWithSceneGeometry = !keepControlsVisible;

                GUILayout.Space(2f);

                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(LatticeLocalization.Content("Clear Selection"), GUILayout.Width(110f)))
                    {
                        LatticeDeformerTool.ClearSelection();
                    }

                GUILayout.Label(LatticeDeformerTool.GetSelectionLabel());
            }

            GUILayout.Space(4f);

                LatticeDeformerTool.MirrorEditing = GUILayout.Toggle(LatticeDeformerTool.MirrorEditing, LatticeLocalization.Content("Enable Symmetry Editing"));

                using (new EditorGUI.DisabledScope(!LatticeDeformerTool.MirrorEditing))
                {
                    int modeSelection = EditorGUILayout.Popup(
                        LatticeLocalization.Content("Symmetry Mode"),
                        (int)LatticeDeformerTool.CurrentMirrorBehavior,
                        LatticeDeformerTool.BehaviorOptions);
                    modeSelection = Mathf.Clamp(modeSelection, 0, LatticeDeformerTool.BehaviorOptions.Length - 1);
                    LatticeDeformerTool.CurrentMirrorBehavior = (LatticeDeformerTool.MirrorBehavior)modeSelection;

                    GUILayout.Label(LatticeLocalization.Content("Symmetry Axis"), EditorStyles.miniLabel);
                    int axisSelection = GUILayout.Toolbar((int)LatticeDeformerTool.CurrentMirrorAxis, LatticeDeformerTool.AxisOptions);
                    axisSelection = Mathf.Clamp(axisSelection, 0, LatticeDeformerTool.AxisOptions.Length - 1);
                    LatticeDeformerTool.CurrentMirrorAxis = (LatticeDeformerTool.MirrorAxis)axisSelection;
                }

                GUILayout.Label(LatticeLocalization.Content("Hold Shift/Ctrl to add/remove controls."), EditorStyles.miniLabel);
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
