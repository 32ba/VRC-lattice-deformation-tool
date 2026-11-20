# Third Party Notices

This package includes the third-party assets listed under "Bundled Assets", which are governed by the licenses shown there. It does not include third-party source code. The work listed under "Algorithm Reference" is credited as background for an algorithm written for this package.

---

## Bundled Assets

### Tabler Icons

The tool icons in `Editor/Icons/` (PNG files) are converted from [Tabler Icons](https://github.com/tabler/tabler-icons).

```
MIT License

Copyright (c) 2020-2026 Paweł Kuna

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## Algorithm Reference

The bone weight recalculation feature is inspired by the following paper. It is not an implementation of the paper's method: the package uses its own cotangent harmonic approximation instead of the paper's full mixed-energy formulation.

**Robust Skin Weights Transfer via Weight Inpainting**
Rinat Abdrashitov, Kim Raichstat, Jared Monsen, David Hill
SIGGRAPH Asia 2023

- Paper: https://www.dgp.toronto.edu/~rinat/projects/RobustSkinWeightsTransfer/
- The authors also publish a reference implementation (MIT License) at https://github.com/rin-23/RobustSkinWeightsTransferCode. Its code is not included in this package.

The closest-point-on-triangle routine in `Editor/MeshDeformer/WeightTransfer/MeshSpatialQuery.cs` follows the region-based test described in Christer Ericson, *Real-Time Collision Detection* (2005), section 5.1.5.
