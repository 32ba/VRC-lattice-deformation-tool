# Changelog

All notable changes to Lattice Deformation Tool are recorded in this file.

Dates are the GitHub release publication dates in UTC. Release candidates superseded by a stable release are consolidated into that stable release. `1.4.4-rc.1` remains listed because no stable `1.4.4` release exists yet.

## Unreleased

### Added

- Added multi-slot interpolation caches for components with differently configured Lattice Layers.
- Added user documentation for Clearance Heatmap, multi-condition Scan, Fit Correction, QA Report, Profiles, and Source BlendShapes above their last frame.

### Changed

- Reused temporary BlendShape vertex, normal, and tangent buffers during Preview and Bake generation to reduce managed allocations.
- Cached the source `SkinnedMeshRenderer` in NDMF Preview and removed a per-frame target lookup allocation.
- Updated the declared `com.unity.collections` dependency to 2.1.4, matching the validated VRChat SDK environment.
- Split migration and BlendShape implementation details into partial `LatticeDeformer` source files without changing serialized type identity.

### Fixed

- Silenced routine update-check messages when no check is needed or the package is current.
- Stored update-check timestamps in UTC.
- Removed an orphaned Unity folder metadata file.

## 1.4.4-rc.1 - 2026-08-02

### Fixed

- Stabilized the preview-proxy handoff used to align the lattice cage after downstream preview processors replace a Renderer or Mesh.
- Preserved the last committed cage alignment while a preview proxy is temporarily unavailable or an edit interaction is active.
- Added deterministic proxy registration and restoration so stale preview callbacks cannot replace a newer cage proxy.

## 1.4.3 - 2026-07-31

### Changed

- Added a post-Avatar Optimizer preview stage and reused preview geometry during interactive edits to reduce update cost.

### Fixed

- Kept the deformation preview operational when Avatar Optimizer removes or replaces meshes in its downstream preview output.
- Preserved rest-space conversion and proxy lookup behavior across Avatar Optimizer preview rebuilds.

## 1.4.2 - 2026-07-28

### Added

- Added true Cubic Bernstein interpolation across all control points for newly created Lattice Layers.
- Added per-layer BlendShape output.
- Added automatic migration from legacy `BrushDeformer` components to the current Group and Brush Layer structure while retaining the disabled legacy component as a backup.

### Changed

- Improved Brush, Vertex Selection, and Lattice editing performance on high-density meshes.
- Updated NDMF Preview meshes in place to reduce duplicate deformation work and BlendShape flicker.
- Made lattice cages and Brush hit testing follow the current skin pose, BlendShape weights, parent transforms, and NDMF Preview geometry.
- Added release-by-release deformation-data migration for public versions from `0.0.1` through `1.4.1`.
- Preserved the published Cubic interpolation and BlendShape output behavior of migrated assets.
- Made migration, validation, Bake, Preview, Editor tools, and bone-weight recalculation reject incompatible or invalid data without partially modifying the original payload.

### Fixed

- Fixed preview geometry briefly returning to its undeformed state while editing a mesh with active BlendShapes.
- Fixed lattice cage alignment under bone poses and scaled parent transforms.
- Fixed proportional editing rebuilding its influence cache from stale positions after Undo.
- Fixed posed vertex rotation and scaling storing the current skin pose in deformation data.
- Accepted valid planar lattice bounds.
- Matched the package's source-geometry evaluation beyond the final BlendShape frame.
- Completed missing Korean and Chinese UI translations.

## 1.4.1 - 2026-07-20

### Fixed

- Hid the legacy `BrushDeformer` from Add Component.
- Removed the legacy Inspector action that activated an incompatible EditorTool and repeatedly raised exceptions.
- Added migration guidance for existing legacy components.

## 1.4.0 - 2026-07-07

### Changed

- Applied Lattice and Brush deformation on top of the current `SkinnedMeshRenderer` BlendShape state.
- Updated cage bounds and interpolation caches from the current source shape, including partial weights and multi-frame BlendShapes.
- Copied source BlendShape frames to NDMF Preview proxy meshes.
- Excluded `Tests/` and `Tools~/` from release archives.
- Expanded EditMode regression and coverage tooling across Runtime, Preview, Weight Transfer, Editor utilities, VRChat integration, and user workflows.

## 1.3.1 - 2026-05-24

### Fixed

- Excluded disabled `LatticeDeformer` components from NDMF Bake.
- Kept enabled deformers under inactive GameObjects eligible for Bake so avatar toggle workflows continue to work.

## 1.3.0 - 2026-03-30

### Added

- Added Normal, Move, Smooth, and Mask Brush modes with configurable falloff, surface-distance falloff, and mirrored editing.
- Added click and rectangle Vertex Selection with Move, Rotate, Scale, and proportional editing.
- Added per-vertex masks with Scene view visualization.
- Added composable Lattice and Brush Layer stacks, multiple DeformerGroups, and backward-compatible facade APIs.
- Added Group BlendShape output, BlendShape import, AnimationCurve sampling, and live weight preview.
- Added axis-based Layer split and flip operations.
- Added reference-mesh penetration detection and Scene view highlighting.

### Changed

- Unified the three editing tools under `MeshDeformerTool` with submode switching.
- Added Group and Layer context menus, UI Toolkit lists, tooltips, icons, wireframe display, and backface culling.
- Reorganized the source tree around Mesh Deformer modules and migrated localization to namespaced keys.

### Fixed

- Fixed Lattice initialization when stored Mesh bounds are stale.
- Aligned Brush preview, hit testing, vertex handles, and proportional radius with the geometry and coordinate space shown in the Scene view.

## 1.2.1 - 2026-03-30

### Fixed

- Computed initial Lattice bounds directly from vertex positions so stale Mesh bounds cannot offset the cage.

## 1.2.0 - 2026-03-05

### Fixed

- Corrected Lattice cage display when Modular Avatar resets a Renderer Transform by deriving the visual correction from bone bind poses.
- Kept the correction limited to cage visualization so stored control points and Bake output remain unchanged.

## 1.1.0 - 2026-01-22

### Added

- Added automatic bone-weight recalculation after deformation.
- Added closest-point weight transfer with barycentric interpolation followed by cotangent-Laplacian inpainting for unresolved vertices.
- Added a Burst and Jobs implementation using CSR sparse matrices and a BiCGStab solver.
- Added Inspector settings and NDMF Bake integration for weight transfer.

### Fixed

- Corrected the default cage alignment so new cages center on the Mesh.
- Corrected null validation in baked Mesh processing.

## 1.0.1 - 2025-12-30

### Added

- Added three NDMF Preview cage-alignment modes: Transform, Transform with center offset, and bounds remapping.
- Added manual cage offset and scale controls that do not alter deformation output.
- Added a Scene view occlusion toggle for control points.

### Fixed

- Prevented other GameObjects from being selected accidentally while editing a Lattice cage.

## 1.0.0 - 2025-10-04

### Added

- Published the first stable release of non-destructive Lattice deformation for `SkinnedMeshRenderer` and `MeshFilter`.
- Added Scene view control-point editing, mirrored editing, interior-point visibility, and occlusion controls.
- Added NDMF real-time Preview and build-time Bake without modifying the source Mesh.
- Added Burst and Jobs deformation, per-axis grid resolution with resampling, five-language UI localization, and Prefab support.

## 0.0.6 - 2025-09-28

### Changed

- Improved Prefab modification tracking so Lattice edits inside Prefab instances are saved.
- Centralized NativeArray allocation, copying, and disposal.
- Used bulk control-point copies to reduce deformation overhead.

### Fixed

- Prevented customized control points from being reset when the source Mesh reference has not changed.

## 0.0.5 - 2025-09-26

### Added

- Added Reset Lattice Cage to refit the cage to Mesh bounds.
- Added automatic Renderer discovery for `SkinnedMeshRenderer` and `MeshFilter` on the same GameObject.

### Changed

- Made deformation consistently use Jobs and Burst and removed the managed fallback path.
- Refined UI terminology across all supported languages.

### Fixed

- Reduced Undo overhead when changing grid divisions.

## 0.0.4 - 2025-09-26

### Added

- Added English, Japanese, Korean, Simplified Chinese, and Traditional Chinese Inspector localization.
- Added interior control-point editing and automatic interior smoothing when editing only the cage surface.

## 0.0.3 - 2025-09-25

### Added

- Added Unity Jobs and Burst parallel processing for control-point initialization, resampling, interpolation-cache construction, and vertex deformation.
- Added `Unity.Mathematics`, `Unity.Burst`, and `Unity.Collections` dependencies.
- Added a managed fallback for environments where Jobs or Burst are unavailable.

## 0.0.2 - 2025-09-24

### Added

- Added X, Y, and Z mirrored control-point editing with copy, mirrored, and antisymmetric modes.
- Added multi-selection for control points.
- Moved editing settings to a Scene view Overlay.

### Changed

- Preserved deformation through grid-size changes by resampling existing control points.
- Added Apply and Revert confirmation for grid-size changes.
- Removed `LatticeApplySpace` and standardized new operations on local space.

## 0.0.1 - 2025-09-24

### Added

- Added the initial `LatticeDeformer` component for non-destructive deformation of `SkinnedMeshRenderer` and `MeshFilter` meshes.
- Added per-axis grid resolution, control-point resampling, and optional normal, tangent, and bounds recalculation.
- Added Scene view control-point selection and movement.
- Added NDMF Preview and build-time deformation without modifying the source Mesh asset.
- Added VRChat SDK3 component-whitelist registration.
