#if UNITY_EDITOR
namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class LocKey
    {
        // General
        internal const string ShowTooltips = "net.32ba.lattice-deformation-tool.localization.show-tooltips";
        internal const string ToolLanguage = "net.32ba.lattice-deformation-tool.localization.tool-language";
        internal const string MeshDeformer = "net.32ba.lattice-deformation-tool.localization.mesh-deformer";
        internal const string MeshDeformerOverview = "net.32ba.lattice-deformation-tool.localization.mesh-deformer-overview";
        internal const string Yes = "net.32ba.lattice-deformation-tool.localization.yes";
        internal const string No = "net.32ba.lattice-deformation-tool.localization.no";
        internal const string Apply = "net.32ba.lattice-deformation-tool.localization.apply";
        internal const string Revert = "net.32ba.lattice-deformation-tool.localization.revert";
        internal const string InvalidMigrationJournal = "net.32ba.lattice-deformation-tool.localization.invalid-migration-journal";
        internal const string Settings = "net.32ba.lattice-deformation-tool.localization.settings";
        internal const string Copy = "net.32ba.lattice-deformation-tool.localization.copy";
        internal const string Select = "net.32ba.lattice-deformation-tool.localization.select";
        internal const string UpdateAvailable = "net.32ba.lattice-deformation-tool.localization.update-available";
        internal const string UpdateCurrentToLatest = "net.32ba.lattice-deformation-tool.localization.update-current-to-latest";
        internal const string UpdateViaVpm = "net.32ba.lattice-deformation-tool.localization.update-via-vpm";
        internal const string UpdateOpenGuide = "net.32ba.lattice-deformation-tool.localization.update-open-guide";

        // Guided inspector
        internal const string GuidedQuestion = "net.32ba.lattice-deformation-tool.localization.guided-question";
        internal const string GuidedIntro = "net.32ba.lattice-deformation-tool.localization.guided-intro";
        internal const string GuidedTarget = "net.32ba.lattice-deformation-tool.localization.guided-target";
        internal const string GuidedNoTarget = "net.32ba.lattice-deformation-tool.localization.guided-no-target";
        internal const string GuidedCurrentEdit = "net.32ba.lattice-deformation-tool.localization.guided-current-edit";
        internal const string GuidedAdjustShape = "net.32ba.lattice-deformation-tool.localization.guided-adjust-shape";
        internal const string GuidedAdjustShapeDescription = "net.32ba.lattice-deformation-tool.localization.guided-adjust-shape-description";
        internal const string GuidedSculptSurface = "net.32ba.lattice-deformation-tool.localization.guided-sculpt-surface";
        internal const string GuidedSculptSurfaceDescription = "net.32ba.lattice-deformation-tool.localization.guided-sculpt-surface-description";
        internal const string GuidedMoveVertices = "net.32ba.lattice-deformation-tool.localization.guided-move-vertices";
        internal const string GuidedMoveVerticesDescription = "net.32ba.lattice-deformation-tool.localization.guided-move-vertices-description";
        internal const string GuidedSingleSelectionRequired = "net.32ba.lattice-deformation-tool.localization.guided-single-selection-required";
        internal const string GuidedProfileReadOnly = "net.32ba.lattice-deformation-tool.localization.guided-profile-read-only";
        internal const string GuidedUnableToStart = "net.32ba.lattice-deformation-tool.localization.guided-unable-to-start";
        internal const string OpenDetailedInspector = "net.32ba.lattice-deformation-tool.localization.open-detailed-inspector";

        // Guided Scene View overlay
        internal const string GuidedOverlayAdjustShapeHint = "net.32ba.lattice-deformation-tool.localization.guided-overlay-adjust-shape-hint";
        internal const string GuidedOverlaySculptSurfaceHint = "net.32ba.lattice-deformation-tool.localization.guided-overlay-sculpt-surface-hint";
        internal const string GuidedOverlayMoveVerticesHint = "net.32ba.lattice-deformation-tool.localization.guided-overlay-move-vertices-hint";
        internal const string GuidedBrushPushPull = "net.32ba.lattice-deformation-tool.localization.guided-brush-push-pull";
        internal const string GuidedBrushMove = "net.32ba.lattice-deformation-tool.localization.guided-brush-move";
        internal const string GuidedBrushSmooth = "net.32ba.lattice-deformation-tool.localization.guided-brush-smooth";
        internal const string GuidedBrushRaise = "net.32ba.lattice-deformation-tool.localization.guided-brush-raise";
        internal const string GuidedBrushIndent = "net.32ba.lattice-deformation-tool.localization.guided-brush-indent";
        internal const string OpenDetailedOverlay = "net.32ba.lattice-deformation-tool.localization.open-detailed-overlay";
        internal const string ReturnToSimpleOverlay = "net.32ba.lattice-deformation-tool.localization.return-to-simple-overlay";
        internal const string FinishEditing = "net.32ba.lattice-deformation-tool.localization.finish-editing";

        // Mesh Source
        internal const string SkinnedMeshSource = "net.32ba.lattice-deformation-tool.localization.skinned-mesh-source";
        internal const string StaticMeshSource = "net.32ba.lattice-deformation-tool.localization.static-mesh-source";

        // Groups & Layers
        internal const string DeformationGroups = "net.32ba.lattice-deformation-tool.localization.deformation-groups";
        internal const string ProfileReadOnlyInfo = "net.32ba.lattice-deformation-tool.localization.profile-read-only-info";
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

        // Brush Tool
        internal const string ActiveLayerNotBrush = "net.32ba.lattice-deformation-tool.localization.active-layer-not-brush";
        internal const string BrushLayerInfo = "net.32ba.lattice-deformation-tool.localization.brush-layer-info";
        internal const string BrushRadius = "net.32ba.lattice-deformation-tool.localization.brush-radius";
        internal const string BrushStrength = "net.32ba.lattice-deformation-tool.localization.brush-strength";
        internal const string BrushFalloff = "net.32ba.lattice-deformation-tool.localization.brush-falloff";
        internal const string Normal = "net.32ba.lattice-deformation-tool.localization.normal";
        internal const string Move = "net.32ba.lattice-deformation-tool.localization.move";
        internal const string BrushSmooth = "net.32ba.lattice-deformation-tool.localization.brush-smooth";
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
        internal const string ClearAll = "net.32ba.lattice-deformation-tool.localization.clear-all";
        internal const string Visualization = "net.32ba.lattice-deformation-tool.localization.visualization";
        internal const string ShowWireframe = "net.32ba.lattice-deformation-tool.localization.show-wireframe";
        internal const string ShowAffectedVertices = "net.32ba.lattice-deformation-tool.localization.show-affected-vertices";
        internal const string ShowDisplacementHeatmap = "net.32ba.lattice-deformation-tool.localization.show-displacement-heatmap";
        internal const string ShowPenetration = "net.32ba.lattice-deformation-tool.localization.show-penetration";
        internal const string ReferenceMesh = "net.32ba.lattice-deformation-tool.localization.reference-mesh";
        internal const string DotSize = "net.32ba.lattice-deformation-tool.localization.dot-size";

        // Vertex Selection Tool
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
        // Mesh Rebuild Options
        internal const string MeshRebuildOptions = "net.32ba.lattice-deformation-tool.localization.mesh-rebuild-options";
        internal const string Normals = "net.32ba.lattice-deformation-tool.localization.normals";
        internal const string NormalsMode = "net.32ba.lattice-deformation-tool.localization.normals-mode";
        internal const string NormalsLegacyUnityRecalculate = "net.32ba.lattice-deformation-tool.localization.normals-legacy-unity-recalculate";
        internal const string NormalsPreserveSourceSmoothing = "net.32ba.lattice-deformation-tool.localization.normals-preserve-source-smoothing";
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
        internal const string SupportInformation = "net.32ba.lattice-deformation-tool.localization.support-information";
        internal const string SupportInformationDescription = "net.32ba.lattice-deformation-tool.localization.support-information-description";
        internal const string CopySupportInformation = "net.32ba.lattice-deformation-tool.localization.copy-support-information";
        internal const string SupportInformationCopied = "net.32ba.lattice-deformation-tool.localization.support-information-copied";

        // Legacy Brush Deformer
        internal const string BrushDeformation = "net.32ba.lattice-deformation-tool.localization.brush-deformation";
        internal const string LegacyBrushMigrationWarning = "net.32ba.lattice-deformation-tool.localization.legacy-brush-migration-warning";
        internal const string MigrateLegacyBrush = "net.32ba.lattice-deformation-tool.localization.migrate-legacy-brush";
        internal const string LegacyBrushMigrationSucceeded = "net.32ba.lattice-deformation-tool.localization.legacy-brush-migration-succeeded";
        internal const string LegacyBrushMigrationFailed = "net.32ba.lattice-deformation-tool.localization.legacy-brush-migration-failed";

        // Editor UI (design system)
        internal const string GuidedInspectorTab = "net.32ba.lattice-deformation-tool.localization.guided-inspector-tab";
        internal const string DetailedInspectorTab = "net.32ba.lattice-deformation-tool.localization.detailed-inspector-tab";
        internal const string GuidedAdjustShapeExample = "net.32ba.lattice-deformation-tool.localization.guided-adjust-shape-example";
        internal const string GuidedSculptSurfaceExample = "net.32ba.lattice-deformation-tool.localization.guided-sculpt-surface-example";
        internal const string GuidedMoveVerticesExample = "net.32ba.lattice-deformation-tool.localization.guided-move-vertices-example";
        internal const string MeshSource = "net.32ba.lattice-deformation-tool.localization.mesh-source";
        internal const string DeformationLayers = "net.32ba.lattice-deformation-tool.localization.deformation-layers";
        internal const string SelectedLayerSettings = "net.32ba.lattice-deformation-tool.localization.selected-layer-settings";
        internal const string ToolPreferences = "net.32ba.lattice-deformation-tool.localization.tool-preferences";
        internal const string AddGroup = "net.32ba.lattice-deformation-tool.localization.add-group";
        internal const string AddLayer = "net.32ba.lattice-deformation-tool.localization.add-layer";
        internal const string GroupLayerCount = "net.32ba.lattice-deformation-tool.localization.group-layer-count";
        internal const string LayerVisible = "net.32ba.lattice-deformation-tool.localization.layer-visible";
        internal const string LayerEditing = "net.32ba.lattice-deformation-tool.localization.layer-editing";
        internal const string LayerBadgeLattice = "net.32ba.lattice-deformation-tool.localization.layer-badge-lattice";
        internal const string LayerBadgeBrush = "net.32ba.lattice-deformation-tool.localization.layer-badge-brush";
        internal const string ScaleLinked = "net.32ba.lattice-deformation-tool.localization.scale-linked";
        internal const string ScaleUnlinked = "net.32ba.lattice-deformation-tool.localization.scale-unlinked";

        // Editor UI glossary
        internal const string SelectMirrorPartners = "net.32ba.lattice-deformation-tool.localization.select-mirror-partners";

        // Editor UI glossary
        internal const string LayerInfluence = "net.32ba.lattice-deformation-tool.localization.layer-influence";
    }
}
#endif
