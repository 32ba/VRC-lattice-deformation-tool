# Coverage Plan

This package targets 100% line coverage for these production assemblies:

- `net.32ba.lattice-deformation-tool`
- `net.32ba.lattice-deformation-tool.editor`
- `net.32ba.lattice-deformation-tool.vrchat` when `com.vrchat.avatars` is present

The coverage command is:

```powershell
./Tools~/Run-Coverage.ps1 -ProjectPath D:\VRC\Projects\Plugin-dev-playground -MinimumTestTotal 900
```

To fail the run unless the generated XML reports 100% line coverage:

```powershell
./Tools~/Run-Coverage.ps1 -ProjectPath D:\VRC\Projects\Plugin-dev-playground -MinimumTestTotal 900 -EnforceLineCoverage
```

To validate the fixed paths, filters, Unity executable, package path, and Code Coverage package without launching Unity or cleaning previous artifacts:

```powershell
./Tools~/Run-Coverage.ps1 -ProjectPath D:\VRC\Projects\Plugin-dev-playground -MinimumTestTotal 900 -PreflightOnly
```

## MCP Execution

Batchmode is not required. The same Unity Code Coverage package settings can be fixed from the open Editor through UnityMCP:

```csharp
var runner = System.AppDomain.CurrentDomain.GetAssemblies()
    .Select(a => a.GetType("Net._32Ba.LatticeDeformationTool.Editor.LatticeCoverageMcpRunner"))
    .First(t => t != null);
runner.GetMethod("Configure").Invoke(null, null);
runner.GetMethod("GetStatus").Invoke(null, null);
```

`LatticeCoverageMcpRunner` lives under `Tests/Editor/Coverage` in the dedicated `net.32ba.lattice-deformation-tool.coverage` Editor test assembly, so the main package Editor assembly does not take a permanent dependency on the Code Coverage package and the runner stays out of the normal Editor distribution surface.

Then run the EditMode test assembly through UnityMCP:

```text
mode: EditMode
assembly_names: net.32ba.lattice-deformation-tool.tests.editor
```

When the VRChat SDK is present, also run `net.32ba.lattice-deformation-tool.tests.editor.vrchat`.

The package also exposes Unity menu items for MCP execution:

- `Tools/Lattice Deformation Tool/Coverage/Configure`
- `Tools/Lattice Deformation Tool/Coverage/Start Recording`
- `Tools/Lattice Deformation Tool/Coverage/Stop Recording`

After the test run, verify the generated artifacts from PowerShell:

```powershell
./Tools~/Assert-CoverageArtifacts.ps1 -ResultsPath D:\VRC\Projects\Plugin-dev-playground\Temp\LatticeCoverage
./Tools~/Assert-Coverage.ps1 -ResultsPath D:\VRC\Projects\Plugin-dev-playground\Temp\LatticeCoverage -MinimumLineCoverage 100 -TargetAssemblies @('net.32ba.lattice-deformation-tool','net.32ba.lattice-deformation-tool.editor','net.32ba.lattice-deformation-tool.vrchat')
```

Omit `net.32ba.lattice-deformation-tool.vrchat` from manual `Assert-Coverage.ps1` calls when `com.vrchat.avatars` is not present; `Run-Coverage.ps1` does this detection automatically.

If Unity logs `Visited sequence points not found`, Code Coverage was enabled after the current Editor process had already compiled or loaded the target assemblies without coverage instrumentation. Keep the MCP configuration, restart the Editor, confirm `GetStatus` reports `Coverage.enabled=True`, `CodeOptimization=Debug`, `ScriptDebugInfoEnabled=True`, `BurstCompilation=False`, and `CoverageXmlFiles=0` before the run, then rerun the UnityMCP test command. After the run, `CoverageXmlFiles` and `CoverageHtmlFiles` must be greater than zero before the line coverage gate is meaningful.

The current successful MCP baseline produced `897/897` passing EditMode tests before three final spatial-hash regression cases were added; those cases were then run directly and passed, bringing the enforced minimum to 900. The separate VRChat assembly also passed `3/3`. The latest coverage baseline generated XML/HTML artifacts under `Temp/LatticeCoverage`; after excluding explicit Unity/NDMF/GUI/Burst job glue and adding focused Runtime/WeightTransfer/Editor utility coverage, the current baseline line coverage is:

- `net.32ba.lattice-deformation-tool`: 100.0%
- `net.32ba.lattice-deformation-tool.editor`: 100.0%
- `net.32ba.lattice-deformation-tool.vrchat`: 100.0%
- combined target report: 100.0%

Unity Code Coverage `ReportGenerator.GenerateReport()` can intermittently fail with an internal `IndexOutOfRangeException` in the package logger after very large MCP runs. The validation scripts now fall back to aggregating OpenCover sequence points when `Report/Summary.xml` is absent, using an OR merge across test result XML files for the same assembly/file/line.

There are no remaining coverable line gaps in the current target report. If stale XML files are left under `Temp/LatticeCoverage`, ReportGenerator may merge old sequence points and show false gaps; clean the results directory before each enforced run.

The GitHub Actions workflow in `.github/workflows/coverage.yml` is intended for a self-hosted Windows runner that has this Unity project and Unity Editor installed. It checks out the package, mirrors that checkout into:

```text
<ProjectPath>/Packages/net.32ba.lattice-deformation-tool
```

then runs `-PreflightOnly` followed by the enforced coverage command from inside the Unity project package path.

Run it with the Unity Editor closed. The script fixes:

- `assemblyFilters:+net.32ba.lattice-deformation-tool,+net.32ba.lattice-deformation-tool.editor`, plus `+net.32ba.lattice-deformation-tool.vrchat` when `com.vrchat.avatars` is present
- `pathFilters:+<package>/Runtime/**,+<package>/Editor/**,-**/Tests/**`
- EditMode test assembly: `net.32ba.lattice-deformation-tool.tests.editor`, plus `net.32ba.lattice-deformation-tool.tests.editor.vrchat` when `com.vrchat.avatars` is present
- Unity Code Coverage package execution: `-enableCodeCoverage`
- coverage accuracy flags: `-debugCodeOptimization` and `-burst-disable-compilation`
- test result gate: `Tools~/Assert-TestResults.ps1` requires `result="Passed"` and total/passed with zero failed/skipped/inconclusive tests; current minimum total is 900
- artifact gate: `Tools~/Assert-CoverageArtifacts.ps1` requires generated coverage XML and HTML report files under `ResultsPath`
- optional line coverage gate: `Tools~/Assert-Coverage.ps1 -MinimumLineCoverage 100` requires each target assembly coverage entry to be present and at 100%
- preflight: Unity Code Coverage package (`com.unity.testtools.codecoverage`) must be present in `Packages/packages-lock.json` or `Library/PackageCache`
- `-PreflightOnly`: prints the resolved coverage configuration and exits before Unity launch, result cleanup, or open-editor checks
- stale-result protection: `ResultsPath` is cleaned before Unity runs; external result paths require `-AllowExternalResultsClean`

The workflow runs automatically for pull requests and pushes to `master`, and is also reused by the release workflow. It defaults to the self-hosted runner project at `D:\VRC\Projects\Plugin-dev-playground`; the `UNITY_PROJECT_PATH` repository variable or a manual input can override that path. A release cannot be built until this gate passes and the dispatcher supplies Profiler evidence including p50, p95, max, and GC allocation.

The gate accepts XML values such as `line-rate="1.0"` and rejects values below 100%, including `line-rate="0.999"`.

## Current Milestones

1. Generate the baseline HTML/XML report from the open Editor through UnityMCP, closed Unity batchmode, or the self-hosted Windows CI workflow.
2. Drive Runtime and WeightTransfer coverage to 100% first:
   - Runtime unreferenced branches.
   - `LatticeNativeArrayUtility` copy/null/length validation helpers.
   - `WeightTransferSettingsData` default/clone behavior.
   - `WeightTransfer`.
   - `BurstSolver`.
   - `MeshSpatialQuery`.
   - `RobustWeightTransfer`.
   - `WeightInpainting`.
3. Cover Editor utility and preview code:
   - version comparison and formatting.
   - localization PO parsing and missing-key fallback.
   - VPM API error message formatting.
   - icon lookup/content fallback.
   - bounds/proxy/preview state logic.
   - NDMF preview proxy pair extraction and reflection fallback.
   - release/version/VPM/localization/icon loading.
   - VRChat whitelist append/deduplication logic.
4. For UI/SceneView heavy layers, extract pure calculation helpers first:
   - `MeshDeformerEditor`.
   - `BrushLayerTool`.
   - `VertexSelectionTool`.
   - `LatticeLayerTool`.
5. Use `[ExcludeFromCodeCoverage]` only after deciding that remaining code is Unity GUI, drawing, or event-loop glue with no practical deterministic test surface.

## Explicit UI/SceneView Exclusions

These classes are excluded from line coverage because their remaining responsibilities are IMGUI layout, Unity `Handles` drawing, `SceneView` event routing, overlay rendering, or inspector serialized-property glue:

- `LatticeDeformerEditor`
- `BrushDeformerEditor`
- `MeshDeformerTool`
- `MeshDeformerToolOverlay`
- `BrushToolHandler`
- `VertexSelectionHandler`
- `LatticeToolHandler`
- `LatticeDeformerPreviewFilter`
- `LatticeDeformerNDMFPlugin`
- `ReleaseNotificationGUI`
- `WireframeRenderer`
- `LatticePrefabUtility`
- `EditorCoroutine`
- `MeshSpatialQuery.FindClosestPointJob`
- `LatticeAsset.ResampleControlPointsJob`
- `LatticeAsset.PopulateControlPointsJob`
- `LatticeDeformer.DeformVerticesJob`

Method-level exclusions are also used for `ReleaseChecker` delayed startup and VRChat whitelist reflection registration; their deterministic helpers remain covered. Pure logic that was practical to test has been covered outside these shells: weight transfer, sparse solvers, mesh spatial queries, preview proxy lookup, bounds helpers, version/release helpers, localization parsing, icon fallback, VPM error formatting, and VRChat whitelist append/deduplication. The excluded preview filters and NDMF plugin are package callback registration and render-filter glue; deterministic preview utility logic remains in `LatticePreviewUtility` and `NDMFPreviewProxyUtility`.

`LatticeLocalization.GetPackageRoot` is excluded as Unity PackageManager/AssetDatabase package-root discovery glue. Catalog parsing, path resolution with an injected package root, language preferences, tooltip fallback, and English fallback behavior remain covered.

Only `LatticeDeformerBakePass.Execute` and its `BuildContext` mutation routine are excluded as NDMF build-pipeline glue because their asset-save and object-replacement effects require a real `BuildContext`, `AssetSaver`, and `ObjectRegistry` cycle. The whole-build validation preflight, enabled-state routing, diagnostics, and target-mesh selection remain instrumented and tested. Burst `IJobParallelFor` worker structs are excluded as Burst/Job glue; their public callers, sequential equivalents, validation paths, and managed data conversion remain test targets, while the workers mirror those algorithms inside Unity's job runner and previously made Code Coverage report generation unstable in MCP-driven runs.

## Verification Gates

- `Tests/Editor` keeps passing as the suite grows; the enforced minimum is 900 tests.
- UnityMCP, batchmode, or CI generates coverage artifacts under `Temp/LatticeCoverage`.
- Final line coverage for the three target assemblies is 100%.
- Any excluded line has an explicit UI/SceneView/glue rationale.
