# Lattice Deformation Tool

A Unity editor extension for lattice deformation, compatible with Unity 2022.3 and later.  
It works with NDMF’s preview feature and provides a **non-destructive workflow**, allowing you to edit without altering the original mesh.

## Features
- Enables lattice deformation with a **non-destructive workflow** using NDMF’s preview feature. During editing, the original mesh is not replaced; deformation results are displayed on a proxy instead.
- In the Scene view, you can edit the lattice by selecting and moving boundary control points with the **Lattice Tool**.
- The Inspector provides mesh update options (recalculate normals/tangents/bounds) and a `(NDMF) Enable Lattice Preview` toggle to switch preview ON/OFF.

## Usage (Overview)
1. Install the package via the [VPM repository](https://vpm.32ba.net), or place the repository inside the `Packages` folder of your VCC workspace.
2. Add `LatticeDeformer` to a GameObject that has either a `MeshFilter` or a `SkinnedMeshRenderer`.
3. In the Inspector, set the target renderer and adjust grid size and bounds under **Lattice Settings** (detailed options are available in Advanced Settings).
4. Press **Activate Lattice Tool** to select boundary control points in the Scene view and edit them using the PositionHandle.
5. Once editing is complete, run NDMF’s build pipeline to bake the deformed mesh (the original component will be automatically removed during baking).

## Controls and Tips
- **Selection**: Boundary control points are displayed as small cubes. Clicking one highlights it and shows a PositionHandle.
- **Toggle Preview**: Use the `(NDMF) Enable Lattice Preview` button in the Inspector to instantly switch the proxy display ON/OFF.
- **Undo/Redo**: Fully supports Unity’s standard Undo/Redo. After each operation, the preview is automatically recalculated.

## Requirements
- Unity 2022.3 LTS or later
- VRChat Creator Companion (recommended)
- NDMF (`nadena.dev.ndmf`) version 1.9.0 or later

## License
This package is provided under the MIT License. See `LICENSE` for details.