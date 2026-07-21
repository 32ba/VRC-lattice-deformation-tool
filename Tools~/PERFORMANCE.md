# Interactive Performance Baseline

Performance-sensitive changes must be measured in Unity Editor with representative editing data. Record the scenario, Unity version, sample count, p50, p95, maximum time, and managed allocation. EditMode correctness tests remain a separate gate.

## 2026-07-21 codebase health cleanup

- Unity: 2022.3.22f1
- Project: `Plugin-dev-playground`
- Target size: 70,225 vertices
- Measurements: Stopwatch distributions after warm-up; the same operations were captured separately with the Editor Profiler markers listed below

| Scenario | Samples | p50 | p95 | Max | Managed allocation |
| --- | ---: | ---: | ---: | ---: | ---: |
| Brush-only `LatticeDeformer.Deform(false)` | 60 | 7.776 ms | 10.056 ms | 10.449 ms | 0 B/op |
| Proportional influence rebuild, 1,004 selected vertices | 40 | 11.874 ms | 13.033 ms | 13.749 ms | 0 B/op |

Profiler evidence led to the final radius-sized dense-grid/spatial-hash implementation. It performed 141,586 exact candidate distance checks in this scenario, compared with 70,505,900 checks for the previous vertex-by-selection pairwise bound, and the warm rebuild allocated no managed memory.

Profiler markers used for the capture:

- `Audit.LatticeDeformer.Deform70k.Warm`
- `LatticeDeformer.Deform`
- `Audit.VertexSelection.ProportionalInfluence70k`
- existing `Brush.*` markers for BakeMesh, raycast, adjacency, geodesic distance, visualization, and deform work

Repeat this baseline after changes to deformation buffers, Scene View cache invalidation, brush distance calculations, or proportional editing. A release dispatch must link or describe equivalent evidence in its required `performance-report` input.
