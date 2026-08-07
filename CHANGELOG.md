# Changelog

All notable changes to Lattice Deformation Tool are recorded in this file.

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

## 1.4.3 - 2026-07-31

### Changed

- Reduced Avatar Optimizer preview update costs during interactive editing.
- Stabilized preview behavior when Avatar Optimizer removes meshes.
