#if UNITY_EDITOR
namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class LocKey
    {
        // General
        internal const string ToolLanguage = "net.32ba.lattice-deformation-tool.localization.tool-language";
        internal const string MeshDeformer = "net.32ba.lattice-deformation-tool.localization.mesh-deformer";
        internal const string Yes = "net.32ba.lattice-deformation-tool.localization.yes";
        internal const string No = "net.32ba.lattice-deformation-tool.localization.no";
        internal const string Apply = "net.32ba.lattice-deformation-tool.localization.apply";
        internal const string Revert = "net.32ba.lattice-deformation-tool.localization.revert";
        internal const string Settings = "net.32ba.lattice-deformation-tool.localization.settings";
        internal const string Copy = "net.32ba.lattice-deformation-tool.localization.copy";
        internal const string Paste = "net.32ba.lattice-deformation-tool.localization.paste";
        internal const string Duplicate = "net.32ba.lattice-deformation-tool.localization.duplicate";
        internal const string Select = "net.32ba.lattice-deformation-tool.localization.select";

        // Mesh Source
        internal const string SkinnedMeshSource = "net.32ba.lattice-deformation-tool.localization.skinned-mesh-source";
        internal const string StaticMeshSource = "net.32ba.lattice-deformation-tool.localization.static-mesh-source";

        // Groups & Layers
        internal const string DeformationGroups = "net.32ba.lattice-deformation-tool.localization.deformation-groups";
        internal const string Layers = "net.32ba.lattice-deformation-tool.localization.layers";
        internal const string AddLatticeLayer = "net.32ba.lattice-deformation-tool.localization.add-lattice-layer";
        internal const string AddBrushLayer = "net.32ba.lattice-deformation-tool.localization.add-brush-layer";
        internal const string ActiveLayer = "net.32ba.lattice-deformation-tool.localization.active-layer";
        internal const string DuplicateLayer = "net.32ba.lattice-deformation-tool.localization.duplicate-layer";
        internal const string CopyLayer = "net.32ba.lattice-deformation-tool.localization.copy-layer";
        internal const string PasteLayer = "net.32ba.lattice-deformation-tool.localization.paste-layer";
        internal const string DeleteLayer = "net.32ba.lattice-deformation-tool.localization.delete-layer";
        internal const string DuplicateGroup = "net.32ba.lattice-deformation-tool.localization.duplicate-group";
        internal const string CopyGroup = "net.32ba.lattice-deformation-tool.localization.copy-group";
        internal const string PasteGroup = "net.32ba.lattice-deformation-tool.localization.paste-group";
        internal const string DeleteGroup = "net.32ba.lattice-deformation-tool.localization.delete-group";
        internal const string ResetActiveLayer = "net.32ba.lattice-deformation-tool.localization.reset-active-layer";
        internal const string NoDeformationLayers = "net.32ba.lattice-deformation-tool.localization.no-deformation-layers";

        // L/R Operations
        internal const string LROperations = "net.32ba.lattice-deformation-tool.localization.lr-operations";
        internal const string SplitL = "net.32ba.lattice-deformation-tool.localization.split-l";
        internal const string SplitR = "net.32ba.lattice-deformation-tool.localization.split-r";
        internal const string SplitLayerLeft = "net.32ba.lattice-deformation-tool.localization.split-layer-left";
        internal const string SplitLayerRight = "net.32ba.lattice-deformation-tool.localization.split-layer-right";
        internal const string FlipX = "net.32ba.lattice-deformation-tool.localization.flip-x";
        internal const string FlipY = "net.32ba.lattice-deformation-tool.localization.flip-y";
        internal const string FlipZ = "net.32ba.lattice-deformation-tool.localization.flip-z";
        internal const string FlipLayerX = "net.32ba.lattice-deformation-tool.localization.flip-layer-x";
        internal const string FlipLayerY = "net.32ba.lattice-deformation-tool.localization.flip-layer-y";
        internal const string FlipLayerZ = "net.32ba.lattice-deformation-tool.localization.flip-layer-z";

        // Lattice Tool
        internal const string LatticeTool = "net.32ba.lattice-deformation-tool.localization.lattice-tool";
        internal const string ActiveLayerNotLattice = "net.32ba.lattice-deformation-tool.localization.active-layer-not-lattice";
        internal const string MoveLatticeControls = "net.32ba.lattice-deformation-tool.localization.move-lattice-controls";
        internal const string ResetLatticeCage = "net.32ba.lattice-deformation-tool.localization.reset-lattice-cage";
        internal const string ControlPointScope = "net.32ba.lattice-deformation-tool.localization.control-point-scope";
        internal const string AllControls = "net.32ba.lattice-deformation-tool.localization.all-controls";
        internal const string BoundaryOnly = "net.32ba.lattice-deformation-tool.localization.boundary-only";
        internal const string KeepControlPointsVisible = "net.32ba.lattice-deformation-tool.localization.keep-control-points-visible";
        internal const string ShowControlIds = "net.32ba.lattice-deformation-tool.localization.show-control-ids";
        internal const string HandleOrientation = "net.32ba.lattice-deformation-tool.localization.handle-orientation";
        internal const string Local = "net.32ba.lattice-deformation-tool.localization.local";
        internal const string Global = "net.32ba.lattice-deformation-tool.localization.global";
        internal const string EnableSymmetryEditing = "net.32ba.lattice-deformation-tool.localization.enable-symmetry-editing";
        internal const string SymmetryAxis = "net.32ba.lattice-deformation-tool.localization.symmetry-axis";
        internal const string SymmetryMode = "net.32ba.lattice-deformation-tool.localization.symmetry-mode";
        internal const string Antisymmetric = "net.32ba.lattice-deformation-tool.localization.antisymmetric";
        internal const string Mirror = "net.32ba.lattice-deformation-tool.localization.mirror";
        internal const string X = "net.32ba.lattice-deformation-tool.localization.x";
        internal const string Y = "net.32ba.lattice-deformation-tool.localization.y";
        internal const string Z = "net.32ba.lattice-deformation-tool.localization.z";
        internal const string ShiftClickHint = "net.32ba.lattice-deformation-tool.localization.shift-click-hint";
        internal const string SelectedNone = "net.32ba.lattice-deformation-tool.localization.selected-none";
        internal const string SelectedFormat = "net.32ba.lattice-deformation-tool.localization.selected-format";
        internal const string SelectedControlsFormat = "net.32ba.lattice-deformation-tool.localization.selected-controls-format";
        internal const string GlobalSpaceDisabled = "net.32ba.lattice-deformation-tool.localization.global-space-disabled";

        // Lattice Grid
        internal const string CurrentGridDivisions = "net.32ba.lattice-deformation-tool.localization.current-grid-divisions";
        internal const string PendingGridDivisions = "net.32ba.lattice-deformation-tool.localization.pending-grid-divisions";
        internal const string ChangeLatticeDivisions = "net.32ba.lattice-deformation-tool.localization.change-lattice-divisions";
        internal const string NoLatticeDeformerSelected = "net.32ba.lattice-deformation-tool.localization.no-lattice-deformer-selected";
        internal const string NoLatticeAssetAssigned = "net.32ba.lattice-deformation-tool.localization.no-lattice-asset-assigned";

        // Brush Tool
        internal const string BrushTool = "net.32ba.lattice-deformation-tool.localization.brush-tool";
        internal const string ActiveLayerNotBrush = "net.32ba.lattice-deformation-tool.localization.active-layer-not-brush";
        internal const string BrushLayerInfo = "net.32ba.lattice-deformation-tool.localization.brush-layer-info";
        internal const string BrushRadius = "net.32ba.lattice-deformation-tool.localization.brush-radius";
        internal const string BrushStrength = "net.32ba.lattice-deformation-tool.localization.brush-strength";
        internal const string BrushFalloff = "net.32ba.lattice-deformation-tool.localization.brush-falloff";
        internal const string Normal = "net.32ba.lattice-deformation-tool.localization.normal";
        internal const string Move = "net.32ba.lattice-deformation-tool.localization.move";
        internal const string BrushSmooth = "net.32ba.lattice-deformation-tool.localization.brush-smooth";
        internal const string BrushMask = "net.32ba.lattice-deformation-tool.localization.brush-mask";
        internal const string BrushDeform = "net.32ba.lattice-deformation-tool.localization.brush-deform";
        internal const string Smooth = "net.32ba.lattice-deformation-tool.localization.smooth";
        internal const string Linear = "net.32ba.lattice-deformation-tool.localization.linear";
        internal const string Constant = "net.32ba.lattice-deformation-tool.localization.constant";
        internal const string Sphere = "net.32ba.lattice-deformation-tool.localization.sphere";
        internal const string Gaussian = "net.32ba.lattice-deformation-tool.localization.gaussian";
        internal const string Falloff = "net.32ba.lattice-deformation-tool.localization.falloff";
        internal const string Invert = "net.32ba.lattice-deformation-tool.localization.invert";
        internal const string InvertBrush = "net.32ba.lattice-deformation-tool.localization.invert-brush";
        internal const string EnableMirror = "net.32ba.lattice-deformation-tool.localization.enable-mirror";
        internal const string MirrorAxis = "net.32ba.lattice-deformation-tool.localization.mirror-axis";
        internal const string SurfaceDistance = "net.32ba.lattice-deformation-tool.localization.surface-distance";
        internal const string ClearAllDisplacements = "net.32ba.lattice-deformation-tool.localization.clear-all-displacements";
        internal const string ClearActiveLayerDisplacements = "net.32ba.lattice-deformation-tool.localization.clear-active-layer-displacements";
        internal const string BackfaceCulling = "net.32ba.lattice-deformation-tool.localization.backface-culling";
        internal const string AltScrollHint = "net.32ba.lattice-deformation-tool.localization.alt-scroll-hint";
        internal const string Mask = "net.32ba.lattice-deformation-tool.localization.mask";
        internal const string ClearMask = "net.32ba.lattice-deformation-tool.localization.clear-mask";
        internal const string ClearAll = "net.32ba.lattice-deformation-tool.localization.clear-all";
        internal const string Visualization = "net.32ba.lattice-deformation-tool.localization.visualization";
        internal const string ShowAffectedVertices = "net.32ba.lattice-deformation-tool.localization.show-affected-vertices";
        internal const string ShowDisplacementHeatmap = "net.32ba.lattice-deformation-tool.localization.show-displacement-heatmap";
        internal const string ShowPenetration = "net.32ba.lattice-deformation-tool.localization.show-penetration";
        internal const string ReferenceMesh = "net.32ba.lattice-deformation-tool.localization.reference-mesh";
        internal const string DotSize = "net.32ba.lattice-deformation-tool.localization.dot-size";

        // Vertex Selection Tool
        internal const string VertexTool = "net.32ba.lattice-deformation-tool.localization.vertex-tool";
        internal const string VertexSelection = "net.32ba.lattice-deformation-tool.localization.vertex-selection";
        internal const string VertexMove = "net.32ba.lattice-deformation-tool.localization.vertex-move";
        internal const string VertexRotate = "net.32ba.lattice-deformation-tool.localization.vertex-rotate";
        internal const string VertexScale = "net.32ba.lattice-deformation-tool.localization.vertex-scale";
        internal const string VertexTransform = "net.32ba.lattice-deformation-tool.localization.vertex-transform";
        internal const string TransformMode = "net.32ba.lattice-deformation-tool.localization.transform-mode";
        internal const string Rotate = "net.32ba.lattice-deformation-tool.localization.rotate";
        internal const string Scale = "net.32ba.lattice-deformation-tool.localization.scale";
        internal const string SelectAll = "net.32ba.lattice-deformation-tool.localization.select-all";
        internal const string SelectNone = "net.32ba.lattice-deformation-tool.localization.select-none";
        internal const string ClearSelection = "net.32ba.lattice-deformation-tool.localization.clear-selection";
        internal const string ConnectedOnly = "net.32ba.lattice-deformation-tool.localization.connected-only";
        internal const string ResetSelectedVertices = "net.32ba.lattice-deformation-tool.localization.reset-selected-vertices";
        internal const string ResetAllVertices = "net.32ba.lattice-deformation-tool.localization.reset-all-vertices";
        internal const string ProportionalEditing = "net.32ba.lattice-deformation-tool.localization.proportional-editing";
        internal const string ProportionalRadius = "net.32ba.lattice-deformation-tool.localization.proportional-radius";
        internal const string Pivot = "net.32ba.lattice-deformation-tool.localization.pivot";
        internal const string Center = "net.32ba.lattice-deformation-tool.localization.center";
        internal const string LastSelected = "net.32ba.lattice-deformation-tool.localization.last-selected";
        internal const string SelectedVerticesFormat = "net.32ba.lattice-deformation-tool.localization.selected-vertices-format";
        internal const string WERHint = "net.32ba.lattice-deformation-tool.localization.wer-hint";
        internal const string AltScrollProportionalHint = "net.32ba.lattice-deformation-tool.localization.alt-scroll-proportional-hint";
        internal const string ShiftDragPrecision = "net.32ba.lattice-deformation-tool.localization.shift-drag-precision";
        internal const string ZTogglePivot = "net.32ba.lattice-deformation-tool.localization.z-toggle-pivot";

        // Mesh Rebuild Options
        internal const string MeshRebuildOptions = "net.32ba.lattice-deformation-tool.localization.mesh-rebuild-options";
        internal const string Normals = "net.32ba.lattice-deformation-tool.localization.normals";
        internal const string Tangents = "net.32ba.lattice-deformation-tool.localization.tangents";
        internal const string Bounds = "net.32ba.lattice-deformation-tool.localization.bounds";
        internal const string RecalculateBoneWeights = "net.32ba.lattice-deformation-tool.localization.recalculate-bone-weights";
        internal const string BoneWeightRequiresSMR = "net.32ba.lattice-deformation-tool.localization.bone-weight-requires-smr";

        // Weight Transfer
        internal const string WeightTransferSettings = "net.32ba.lattice-deformation-tool.localization.weight-transfer-settings";
        internal const string Stage1InitialTransfer = "net.32ba.lattice-deformation-tool.localization.stage1-initial-transfer";
        internal const string Stage2WeightInpainting = "net.32ba.lattice-deformation-tool.localization.stage2-weight-inpainting";
        internal const string MaxTransferDistance = "net.32ba.lattice-deformation-tool.localization.max-transfer-distance";
        internal const string NormalAngleThreshold = "net.32ba.lattice-deformation-tool.localization.normal-angle-threshold";
        internal const string EnableInpainting = "net.32ba.lattice-deformation-tool.localization.enable-inpainting";
        internal const string MaxIterations = "net.32ba.lattice-deformation-tool.localization.max-iterations";
        internal const string Tolerance = "net.32ba.lattice-deformation-tool.localization.tolerance";
        internal const string VertexCount = "net.32ba.lattice-deformation-tool.localization.vertex-count";
        internal const string HasDisplacements = "net.32ba.lattice-deformation-tool.localization.has-displacements";

        // BlendShape
        internal const string BlendShapeOutput = "net.32ba.lattice-deformation-tool.localization.blendshape-output";
        internal const string BlendShapeName = "net.32ba.lattice-deformation-tool.localization.blendshape-name";
        internal const string Curve = "net.32ba.lattice-deformation-tool.localization.curve";
        internal const string EnterTestMode = "net.32ba.lattice-deformation-tool.localization.enter-test-mode";
        internal const string ExitTestMode = "net.32ba.lattice-deformation-tool.localization.exit-test-mode";
        internal const string BlendShapeTestMode = "net.32ba.lattice-deformation-tool.localization.blendshape-test-mode";
        internal const string TestWeight = "net.32ba.lattice-deformation-tool.localization.test-weight";
        internal const string ImportBlendShape = "net.32ba.lattice-deformation-tool.localization.import-blendshape";

        // NDMF Preview
        internal const string NDMFDisableMeshPreview = "net.32ba.lattice-deformation-tool.localization.ndmf-disable-mesh-preview";
        internal const string NDMFEnableMeshPreview = "net.32ba.lattice-deformation-tool.localization.ndmf-enable-mesh-preview";

        // Lattice Cage Alignment
        internal const string LatticeCageAlignment = "net.32ba.lattice-deformation-tool.localization.lattice-cage-alignment";
        internal const string AlignmentCageInfo = "net.32ba.lattice-deformation-tool.localization.alignment-cage-info";
        internal const string Offset = "net.32ba.lattice-deformation-tool.localization.offset";
        internal const string OffsetTooltip = "net.32ba.lattice-deformation-tool.localization.offset-tooltip";
        internal const string DebugLogAlignment = "net.32ba.lattice-deformation-tool.localization.debug-log-alignment";

        // Editor buttons
        internal const string OpenBrushEditor = "net.32ba.lattice-deformation-tool.localization.open-brush-editor";
        internal const string OpenLatticeEditor = "net.32ba.lattice-deformation-tool.localization.open-lattice-editor";
        internal const string SelectMeshDeformer = "net.32ba.lattice-deformation-tool.localization.select-mesh-deformer";
        internal const string Brush = "net.32ba.lattice-deformation-tool.localization.brush";
        internal const string Advanced = "net.32ba.lattice-deformation-tool.localization.advanced";

        // Legacy Brush Deformer
        internal const string BrushDeformer = "net.32ba.lattice-deformation-tool.localization.brush-deformer";
        internal const string BrushDeformation = "net.32ba.lattice-deformation-tool.localization.brush-deformation";
    }
}
#endif
