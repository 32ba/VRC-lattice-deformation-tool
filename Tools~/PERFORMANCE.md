# Interactive Performance Baseline

Performance-sensitive changes must be measured in Unity Editor with representative editing data. Record the scenario, Unity version, sample count, p50, p95, maximum time, and managed allocation. EditMode correctness tests remain a separate gate.

## 2026-07-21 codebase health cleanup

- Unity: 2022.3.22f1
- Project: `Plugin-dev-playground`
- Target size: 70,225 vertices
- Measurements: Stopwatch distributions after warm-up; the same operations were captured separately with the Editor Profiler markers listed below

| Scenario | Samples | p50 | p95 | Max | Managed allocation |
| --- | ---: | ---: | ---: | ---: | ---: |
| Brush-only `LatticeDeformer.Deform(false)` | 60 | 7.771 ms | 9.448 ms | 10.307 ms | Whole main-thread frame: p50 300 B, p95 600 B, max 4.3 KB |
| Proportional influence rebuild, 1,004 selected vertices | 40 | 11.874 ms | 13.033 ms | 13.749 ms | Not reliably captured |

The final Deform row was captured with `Audit.LatticeDeformer.Deform70k.Warm.Final` and a main-thread `GC.Alloc` `ProfilerRecorder`. The matching no-operation Editor frames measured p50 300 B, p95 500 B, and max 700 B. The allocation values therefore include Editor frame noise and must not be presented as operation-only allocation.

Profiler evidence led to the final radius-sized dense-grid/spatial-hash implementation. It performed 141,586 exact candidate distance checks in this scenario, compared with 70,505,900 checks for the previous vertex-by-selection pairwise bound. The earlier `0 B/op` values came from `GC.GetAllocatedBytesForCurrentThread`, which is a non-advancing stub in this Unity 2022 Mono Editor and therefore cannot prove allocation-free execution.

Capture managed allocation with the Unity Profiler's `GC.Alloc` samples and Memory module. Do not use `GC.GetAllocatedBytesForCurrentThread` as release evidence unless a calibration allocation first proves that the counter advances in that exact Editor runtime.

Profiler markers used for the capture:

- `Audit.LatticeDeformer.Deform70k.Warm.Final`
- `LatticeDeformer.Deform`
- `Audit.VertexSelection.ProportionalInfluence70k`
- existing `Brush.*` markers for BakeMesh, raycast, adjacency, geodesic distance, visualization, and deform work

Repeat this baseline after changes to deformation buffers, Scene View cache invalidation, brush distance calculations, or proportional editing. Measurements are local-only; GitHub Actions does not run Unity. A release dispatch must link or describe equivalent local evidence in its required `performance-report` input.
