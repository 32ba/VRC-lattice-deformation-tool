# Local Test and Coverage Gate

Unity tests for this package run locally. GitHub Actions does not start Unity, run
EditMode tests, or generate coverage because the repository has no supported
Unity-capable runner. The release workflow only packages a revision after its
dispatcher supplies local test and Profiler evidence.

## Required local run

With `Plugin-dev-playground` open in Unity 2022.3.22f1 or a compatible 2022.3+
Editor, run these EditMode assemblies through UnityMCP or Unity Test Runner:

- `net.32ba.lattice-deformation-tool.tests.editor`
- `net.32ba.lattice-deformation-tool.tests.editor.vrchat` when the VRChat SDK is present

Record the date, exact Unity version, passed/failed/skipped counts, and the result
file path. Supply that record as the release workflow's required `test-report`
input. A release candidate requires zero failed, skipped, or inconclusive tests.

## Local coverage

Coverage is an additional local diagnostic. It is not an Actions status check.
The instrumented subset can be run from PowerShell:

```powershell
./Tools~/Run-Coverage.ps1 -ProjectPath D:\VRC\Projects\Plugin-dev-playground -MinimumTestTotal 900
```

To require every instrumented line in the generated report:

```powershell
./Tools~/Run-Coverage.ps1 -ProjectPath D:\VRC\Projects\Plugin-dev-playground -MinimumTestTotal 900 -EnforceLineCoverage
```

Run `-PreflightOnly` first to validate paths, filters, the Unity executable, and
the Code Coverage package without launching Unity. Keep Unity closed for the
PowerShell batchmode command.

For an open Editor, use the UnityMCP coverage runner in
`Tests/Editor/Coverage/LatticeCoverageMcpRunner.cs`, restart the Editor after
enabling instrumentation, then run the EditMode assemblies above. Verify the
artifacts locally:

```powershell
./Tools~/Assert-CoverageArtifacts.ps1 -ResultsPath D:\VRC\Projects\Plugin-dev-playground\Temp\LatticeCoverage
./Tools~/Assert-Coverage.ps1 -ResultsPath D:\VRC\Projects\Plugin-dev-playground\Temp\LatticeCoverage -MinimumLineCoverage 100 -TargetAssemblies @('net.32ba.lattice-deformation-tool','net.32ba.lattice-deformation-tool.editor','net.32ba.lattice-deformation-tool.vrchat')
```

Omit the VRChat assembly when the SDK is absent. Clean stale coverage XML before
each enforced run; ReportGenerator can otherwise merge old sequence points.

## Scope of the percentage

The 100% threshold applies to instrumented production lines, not every physical
line in the repository. Unity GUI/event routing, SceneView drawing, NDMF callback
glue, and Burst job wrappers are explicitly excluded where deterministic unit
coverage is not meaningful. Their extracted calculation helpers and observable
behaviour still require focused EditMode tests.

The principal class-level exclusions are `LatticeDeformerEditor`,
`MeshDeformerTool`, `BrushToolHandler`, `VertexSelectionHandler`,
`LatticeToolHandler`, `LatticeDeformerPreviewFilter`, and NDMF registration glue.
Do not describe the resulting percentage as whole-codebase coverage.

The last recorded local baseline before the current audit was 900 EditMode tests
across the main suite plus 3 VRChat tests, with 100% of the configured
instrumented subset. Treat it as historical evidence; every release candidate
must produce a fresh local result.

## Release evidence

The release workflow requires both:

- `test-report`: local EditMode result counts, Unity version, date, and report path or URL.
- `performance-report`: local Unity Profiler scenario, sample count, p50, p95, max,
  GC allocation, and capture path or URL.

Neither input causes GitHub Actions to execute Unity.
