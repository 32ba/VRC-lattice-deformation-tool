# Lattice Deformation Tool

Unity editor extension that provides free-form lattice deformation utilities for meshes and skinned meshes. The package is formatted for VRChat Creator Companion (VCC) distribution and targets Unity 2022.3 or newer.

## Features
- Embedded lattice settings on each `LatticeDeformer` with configurable grid resolution, bounds, and interpolation mode (no ScriptableObjects required).
- `LatticeDeformer` component that applies trilinear lattice deformation to a referenced mesh or skinned mesh renderer.
- Scene view editor tool for interactive control point manipulation and bounds editing.
- Inspector utilities for rebuilding caches, baking deformations, and re-initialising from the source mesh.
- Menu item for quickly creating a `LatticeDeformer` GameObject.

## Getting Started
1. Import the package through VCC or copy this repository into your VCC workspace Packages directory.
2. Add a `LatticeDeformer` (`GameObject > 32ba > Lattice Deformer`) to an object with a `MeshFilter` or `SkinnedMeshRenderer`.
3. Assign your target renderer on the component. Leave the lattice settings as-is to use the automatically generated data, or tweak grid/bounds directly in the inspector.
4. Press **Deform Mesh** or enable *Deform On Enable* to preview the deformation.
5. Click **Activate Lattice Tool** in the `LatticeDeformer` inspector (or choose it from the toolbar) to move lattice control points directly in the scene view using on-screen handles.

## Folder Overview
- `Runtime/` – Lattice settings container, deformation component, and math/cache logic.
- `Editor/` – Custom inspectors, editor tool implementation, and creation menus.
- `docs/` – Planning notes and design documents.

## Requirements
- Unity 2022.3 LTS or newer (tested in HDRP/URP/Standard pipelines).
- VRChat Creator Companion for package management (optional but recommended).

## License
This project is distributed under the MIT License. Refer to `LICENSE` for details.
