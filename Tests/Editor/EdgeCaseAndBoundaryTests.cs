#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    /// <summary>
    /// Edge case, boundary value, and error handling tests.
    /// Covers clamping, invalid inputs, falloff evaluation, LatticeAsset operations,
    /// and code paths not exercised by scenario tests.
    /// </summary>
    public sealed class EdgeCaseAndBoundaryTests
    {
        private const float Epsilon = 1e-4f;
        private static readonly BindingFlags s_privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        // ========================================================================
        // Weight Clamping
        // ========================================================================

        [Test]
        public void Weight_NegativeValue_ClampedToZero()
        {
            var fixture = CreateFixture("Weight_NegativeValue_ClampedToZero");
            try
            {
                var deformer = fixture.Deformer;
                int idx = deformer.AddLayer("Neg", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(1f, 0f, 0f));
                deformer.Layers[idx].Weight = -0.5f;

                ReleaseRuntimeMesh(deformer);
                var mesh = deformer.Deform(false);
                var src = deformer.SourceMesh.vertices;

                // Negative weight should contribute nothing (clamped to 0)
                AssertApproximately(src[0], mesh.vertices[0], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Weight_AboveOne_ClampedToOne()
        {
            var fixture = CreateFixture("Weight_AboveOne_ClampedToOne");
            try
            {
                var deformer = fixture.Deformer;
                int idx = deformer.AddLayer("Over", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();
                var disp = new Vector3(0.1f, 0f, 0f);
                deformer.SetDisplacement(0, disp);
                deformer.Layers[idx].Weight = 1.5f;

                ReleaseRuntimeMesh(deformer);
                var mesh = deformer.Deform(false);
                var src = deformer.SourceMesh.vertices;

                // Weight >1 should clamp to 1
                var expected = src[0] + disp; // weight=1
                AssertApproximately(expected, mesh.vertices[0], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Vertex Mask Clamping & Boundary
        // ========================================================================

        [Test]
        public void VertexMask_NegativeValue_ClampedToZero()
        {
            var layer = new LatticeLayer();
            layer.EnsureVertexMaskCapacity(4);
            layer.SetVertexMask(0, -0.5f);
            Assert.That(layer.GetVertexMask(0), Is.EqualTo(0f).Within(Epsilon));
        }

        [Test]
        public void VertexMask_AboveOne_ClampedToOne()
        {
            var layer = new LatticeLayer();
            layer.EnsureVertexMaskCapacity(4);
            layer.SetVertexMask(0, 2.0f);
            Assert.That(layer.GetVertexMask(0), Is.EqualTo(1f).Within(Epsilon));
        }

        [Test]
        public void VertexMask_OutOfBoundsIndex_NoException()
        {
            var layer = new LatticeLayer();
            layer.EnsureVertexMaskCapacity(4);

            // Negative index — should silently return
            Assert.DoesNotThrow(() => layer.SetVertexMask(-1, 0.5f));
            // Index beyond capacity — should silently return
            Assert.DoesNotThrow(() => layer.SetVertexMask(100, 0.5f));
            // GetVertexMask on out-of-bounds returns 1.0 (default editable)
            Assert.That(layer.GetVertexMask(100), Is.EqualTo(1f).Within(Epsilon));
        }

        [Test]
        public void VertexMask_DefaultIsOne()
        {
            var layer = new LatticeLayer();
            // Without capacity, mask should return default 1.0
            Assert.That(layer.GetVertexMask(0), Is.EqualTo(1f).Within(Epsilon));

            layer.EnsureVertexMaskCapacity(4);
            // After init, should also be 1.0
            for (int i = 0; i < 4; i++)
            {
                Assert.That(layer.GetVertexMask(i), Is.EqualTo(1f).Within(Epsilon));
            }
        }

        [Test]
        public void HasVertexMask_FalseWhenAllOne()
        {
            var layer = new LatticeLayer();
            layer.EnsureVertexMaskCapacity(4);
            // All default 1.0 — no mask
            Assert.That(layer.HasVertexMask(), Is.False);

            layer.SetVertexMask(2, 0.5f);
            Assert.That(layer.HasVertexMask(), Is.True);

            layer.ClearVertexMask();
            Assert.That(layer.HasVertexMask(), Is.False);
        }

        [Test]
        public void EnsureVertexMaskCapacity_GrowPreservesValues()
        {
            var layer = new LatticeLayer();
            layer.EnsureVertexMaskCapacity(4);
            layer.SetVertexMask(0, 0.3f);
            layer.SetVertexMask(1, 0.7f);

            // Grow capacity
            layer.EnsureVertexMaskCapacity(8);

            // Old values preserved
            Assert.That(layer.GetVertexMask(0), Is.EqualTo(0.3f).Within(Epsilon));
            Assert.That(layer.GetVertexMask(1), Is.EqualTo(0.7f).Within(Epsilon));
            // New slots default to 1.0
            Assert.That(layer.GetVertexMask(4), Is.EqualTo(1f).Within(Epsilon));
        }

        // ========================================================================
        // BrushDeformer.EvaluateFalloff Boundary Values
        // ========================================================================

        [Test]
        public void Falloff_Linear_BoundaryValues()
        {
            var type = BrushFalloffType.Linear;
            Assert.That(BrushDeformer.EvaluateFalloff(type, 0f), Is.EqualTo(1f).Within(Epsilon));
            Assert.That(BrushDeformer.EvaluateFalloff(type, 0.5f), Is.EqualTo(0.5f).Within(Epsilon));
            Assert.That(BrushDeformer.EvaluateFalloff(type, 1f), Is.EqualTo(0f).Within(Epsilon));
        }

        [Test]
        public void Falloff_Smooth_BoundaryValues()
        {
            var type = BrushFalloffType.Smooth;
            Assert.That(BrushDeformer.EvaluateFalloff(type, 0f), Is.EqualTo(1f).Within(Epsilon));
            Assert.That(BrushDeformer.EvaluateFalloff(type, 1f), Is.EqualTo(0f).Within(Epsilon));
            // Smooth at 0.5: hermite smoothstep with s=0.5 → 0.5*0.5*(3-2*0.5) = 0.5
            float mid = BrushDeformer.EvaluateFalloff(type, 0.5f);
            Assert.That(mid, Is.EqualTo(0.5f).Within(0.01f));
        }

        [Test]
        public void Falloff_Constant_AlwaysOne()
        {
            var type = BrushFalloffType.Constant;
            Assert.That(BrushDeformer.EvaluateFalloff(type, 0f), Is.EqualTo(1f).Within(Epsilon));
            Assert.That(BrushDeformer.EvaluateFalloff(type, 0.5f), Is.EqualTo(1f).Within(Epsilon));
            Assert.That(BrushDeformer.EvaluateFalloff(type, 1f), Is.EqualTo(1f).Within(Epsilon));
        }

        [Test]
        public void Falloff_Sphere_TransitionRegion()
        {
            var type = BrushFalloffType.Sphere;
            // Before transition (t < 0.9): should be 1.0
            Assert.That(BrushDeformer.EvaluateFalloff(type, 0f), Is.EqualTo(1f).Within(Epsilon));
            Assert.That(BrushDeformer.EvaluateFalloff(type, 0.5f), Is.EqualTo(1f).Within(Epsilon));
            Assert.That(BrushDeformer.EvaluateFalloff(type, 0.89f), Is.EqualTo(1f).Within(0.02f));

            // Transition region (0.9 → 1.0): linear drop from 1 to 0
            float atTransitionStart = BrushDeformer.EvaluateFalloff(type, 0.9f);
            Assert.That(atTransitionStart, Is.EqualTo(1f).Within(0.05f));

            float atTransitionEnd = BrushDeformer.EvaluateFalloff(type, 1f);
            Assert.That(atTransitionEnd, Is.EqualTo(0f).Within(0.05f));

            // Mid-transition
            float atMid = BrushDeformer.EvaluateFalloff(type, 0.95f);
            Assert.That(atMid, Is.GreaterThan(0f));
            Assert.That(atMid, Is.LessThan(1f));
        }

        [Test]
        public void Falloff_Gaussian_Curve()
        {
            var type = BrushFalloffType.Gaussian;
            // At center: exp(0) = 1
            Assert.That(BrushDeformer.EvaluateFalloff(type, 0f), Is.EqualTo(1f).Within(Epsilon));

            // At edge: exp(-3) ≈ 0.0498
            float atEdge = BrushDeformer.EvaluateFalloff(type, 1f);
            Assert.That(atEdge, Is.EqualTo(Mathf.Exp(-3f)).Within(0.01f));

            // Monotonically decreasing
            float prev = 1f;
            for (float t = 0.1f; t <= 1f; t += 0.1f)
            {
                float val = BrushDeformer.EvaluateFalloff(type, t);
                Assert.That(val, Is.LessThanOrEqualTo(prev + Epsilon),
                    $"Gaussian should decrease monotonically at t={t}");
                prev = val;
            }
        }

        [Test]
        public void Falloff_NegativeT_ClampedToZero()
        {
            // math.saturate should clamp negative t to 0, giving max falloff
            foreach (var type in new[] {
                BrushFalloffType.Linear, BrushFalloffType.Smooth,
                BrushFalloffType.Gaussian, BrushFalloffType.Sphere })
            {
                float val = BrushDeformer.EvaluateFalloff(type, -0.5f);
                Assert.That(val, Is.EqualTo(1f).Within(0.01f),
                    $"{type}: negative t should clamp to 0, giving full falloff");
            }
        }

        [Test]
        public void Falloff_TGreaterThanOne_ClampedToOne()
        {
            // math.saturate should clamp t>1 to 1
            Assert.That(BrushDeformer.EvaluateFalloff(BrushFalloffType.Linear, 2f),
                Is.EqualTo(0f).Within(Epsilon));
            Assert.That(BrushDeformer.EvaluateFalloff(BrushFalloffType.Smooth, 2f),
                Is.EqualTo(0f).Within(Epsilon));
            Assert.That(BrushDeformer.EvaluateFalloff(BrushFalloffType.Constant, 2f),
                Is.EqualTo(1f).Within(Epsilon));
        }

        // ========================================================================
        // AddDisplacement Accumulation
        // ========================================================================

        [Test]
        public void AddDisplacement_AccumulatesMultipleCalls()
        {
            var fixture = CreateFixture("AddDisplacement_AccumulatesMultipleCalls");
            try
            {
                var deformer = fixture.Deformer;
                int idx = deformer.AddLayer("Accum", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();

                var d1 = new Vector3(0.1f, 0f, 0f);
                var d2 = new Vector3(0f, 0.2f, 0f);
                var d3 = new Vector3(0f, 0f, 0.3f);
                deformer.AddDisplacement(0, d1);
                deformer.AddDisplacement(0, d2);
                deformer.AddDisplacement(0, d3);

                AssertApproximately(d1 + d2 + d3, deformer.GetDisplacement(0), Epsilon);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void SetThenAdd_AddAccumulatesOnTopOfSet()
        {
            var fixture = CreateFixture("SetThenAdd_AddAccumulatesOnTopOfSet");
            try
            {
                var deformer = fixture.Deformer;
                int idx = deformer.AddLayer("SetAdd", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();

                var setVal = new Vector3(0.5f, 0f, 0f);
                var addVal = new Vector3(0f, 0.1f, 0f);
                deformer.SetDisplacement(0, setVal);
                deformer.AddDisplacement(0, addVal);

                AssertApproximately(setVal + addVal, deformer.GetDisplacement(0), Epsilon);

                // Set again should overwrite
                deformer.SetDisplacement(0, setVal);
                AssertApproximately(setVal, deformer.GetDisplacement(0), Epsilon);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void HasBrushDisplacements_FalseWhenEmpty_TrueWhenSet()
        {
            var layer = new LatticeLayer();
            Assert.That(layer.HasBrushDisplacements(), Is.False);

            layer.EnsureBrushDisplacementCapacity(4);
            // All zeros — still "no displacements"
            Assert.That(layer.HasBrushDisplacements(), Is.False);

            layer.SetBrushDisplacement(1, new Vector3(0.01f, 0f, 0f));
            Assert.That(layer.HasBrushDisplacements(), Is.True);

            layer.ClearBrushDisplacements();
            Assert.That(layer.HasBrushDisplacements(), Is.False);
        }

        // ========================================================================
        // InvalidateCache
        // ========================================================================

        [Test]
        public void InvalidateCache_DeformStillWorks()
        {
            var fixture = CreateFixture("InvalidateCache_DeformStillWorks");
            try
            {
                var deformer = fixture.Deformer;
                var settings = deformer.Layers[0].Settings;
                settings.SetControlPointLocal(0,
                    settings.GetControlPointLocal(0) + new Vector3(0f, 0.1f, 0f));

                var mesh1 = deformer.Deform(false);
                var v1 = mesh1.vertices[0];

                deformer.InvalidateCache();

                ReleaseRuntimeMesh(deformer);
                var mesh2 = deformer.Deform(false);
                var v2 = mesh2.vertices[0];

                AssertApproximately(v1, v2, 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void InvalidateCache_AfterBlendShapeOutputDisabled_RemovesGeneratedBlendShape()
        {
            var fixture = CreateFixture("InvalidateCache_BlendShapeOutputDisabled");
            try
            {
                var deformer = fixture.Deformer;
                int brushLayer = deformer.AddLayer("Generated", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushLayer;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.25f, 0f, 0f));
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "GeneratedShape";

                var blendShapeMesh = deformer.Deform(false);
                Assert.That(blendShapeMesh.GetBlendShapeIndex("GeneratedShape"), Is.GreaterThanOrEqualTo(0));

                deformer.InvalidateCache();
                deformer.BlendShapeOutput = BlendShapeOutputMode.Disabled;
                var directMesh = deformer.Deform(false);

                Assert.That(directMesh.GetBlendShapeIndex("GeneratedShape"), Is.EqualTo(-1));
                AssertApproximately(
                    fixture.SourceMesh.vertices[0] + new Vector3(0.25f, 0f, 0f),
                    directMesh.vertices[0],
                    2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // ComputeLayeredStateHash
        // ========================================================================

        [Test]
        public void StateHash_SameState_SameHash()
        {
            var fixture = CreateFixture("StateHash_SameState_SameHash");
            try
            {
                var deformer = fixture.Deformer;
                int hash1 = deformer.ComputeLayeredStateHash();
                int hash2 = deformer.ComputeLayeredStateHash();
                Assert.That(hash1, Is.EqualTo(hash2));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void StateHash_DifferentState_DifferentHash()
        {
            var fixture = CreateFixture("StateHash_DifferentState_DifferentHash");
            try
            {
                var deformer = fixture.Deformer;
                int hashBefore = deformer.ComputeLayeredStateHash();

                // Change a control point
                var settings = deformer.Layers[0].Settings;
                settings.SetControlPointLocal(0,
                    settings.GetControlPointLocal(0) + new Vector3(0.1f, 0f, 0f));

                int hashAfter = deformer.ComputeLayeredStateHash();
                Assert.That(hashAfter, Is.Not.EqualTo(hashBefore),
                    "Hash should change when control point moves");
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void StateHash_WeightChange_ChangesHash()
        {
            var fixture = CreateFixture("StateHash_WeightChange_ChangesHash");
            try
            {
                var deformer = fixture.Deformer;
                int hash1 = deformer.ComputeLayeredStateHash();

                deformer.Layers[0].Weight = 0.5f;
                int hash2 = deformer.ComputeLayeredStateHash();

                Assert.That(hash1, Is.Not.EqualTo(hash2));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void StateHash_AddLayer_ChangesHash()
        {
            var fixture = CreateFixture("StateHash_AddLayer_ChangesHash");
            try
            {
                var deformer = fixture.Deformer;
                int hash1 = deformer.ComputeLayeredStateHash();

                deformer.AddLayer("Extra", MeshDeformerLayerType.Brush);
                int hash2 = deformer.ComputeLayeredStateHash();

                Assert.That(hash1, Is.Not.EqualTo(hash2));
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Layer Management Error Handling
        // ========================================================================

        [Test]
        public void InsertLayer_Null_ReturnsNegativeOne()
        {
            var fixture = CreateFixture("InsertLayer_Null_ReturnsNegativeOne");
            try
            {
                int result = fixture.Deformer.InsertLayer(null);
                Assert.That(result, Is.EqualTo(-1));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void RemoveLayer_InvalidIndex_ReturnsFalse()
        {
            var fixture = CreateFixture("RemoveLayer_InvalidIndex_ReturnsFalse");
            try
            {
                var deformer = fixture.Deformer;
                Assert.That(deformer.RemoveLayer(-1), Is.False);
                Assert.That(deformer.RemoveLayer(999), Is.False);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void MoveLayer_SamePosition_NoOp()
        {
            var fixture = CreateFixture("MoveLayer_SamePosition_NoOp");
            try
            {
                var deformer = fixture.Deformer;
                deformer.AddLayer("Extra", MeshDeformerLayerType.Brush);

                string name0 = deformer.Layers[0].Name;
                string name1 = deformer.Layers[1].Name;

                bool result = deformer.MoveLayer(0, 0);
                Assert.That(result, Is.True);
                Assert.That(deformer.Layers[0].Name, Is.EqualTo(name0));
                Assert.That(deformer.Layers[1].Name, Is.EqualTo(name1));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void MoveLayer_InvalidSourceIndex_ReturnsFalse()
        {
            var fixture = CreateFixture("MoveLayer_InvalidSourceIndex_ReturnsFalse");
            try
            {
                var deformer = fixture.Deformer;
                // Source index clearly out of range
                Assert.That(deformer.MoveLayer(-1, 0), Is.False);
                Assert.That(deformer.MoveLayer(999, 0), Is.False);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void DuplicateLayer_InvalidIndex_ReturnsNegativeOne()
        {
            var fixture = CreateFixture("DuplicateLayer_InvalidIndex_ReturnsNegativeOne");
            try
            {
                var deformer = fixture.Deformer;
                Assert.That(deformer.DuplicateLayer(-1), Is.EqualTo(-1));
                Assert.That(deformer.DuplicateLayer(999), Is.EqualTo(-1));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void ImportBlendShapeAsLayer_InvalidIndex_ReturnsNegativeOne()
        {
            var fixture = CreateFixture("ImportBlendShapeAsLayer_InvalidIndex_ReturnsNegativeOne");
            try
            {
                var deformer = fixture.Deformer;
                // Source mesh has 0 blend shapes
                Assert.That(deformer.ImportBlendShapeAsLayer(0), Is.EqualTo(-1));
                Assert.That(deformer.ImportBlendShapeAsLayer(-1), Is.EqualTo(-1));
                Assert.That(deformer.ImportBlendShapeAsLayer(999), Is.EqualTo(-1));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void ActiveLayerIndex_ClampedToValidRange()
        {
            var fixture = CreateFixture("ActiveLayerIndex_ClampedToValidRange");
            try
            {
                var deformer = fixture.Deformer;
                deformer.AddLayer("Extra", MeshDeformerLayerType.Brush);
                Assert.That(deformer.Layers.Count, Is.EqualTo(2));

                deformer.ActiveLayerIndex = -5;
                Assert.That(deformer.ActiveLayerIndex, Is.GreaterThanOrEqualTo(0));

                deformer.ActiveLayerIndex = 999;
                Assert.That(deformer.ActiveLayerIndex, Is.LessThan(deformer.Layers.Count));
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // LatticeAsset Operations
        // ========================================================================

        [Test]
        public void ResizeGrid_ChangesControlPointCount()
        {
            var fixture = CreateFixture("ResizeGrid_ChangesControlPointCount");
            try
            {
                var settings = fixture.Deformer.Layers[0].Settings;
                var originalGrid = settings.GridSize;
                int originalCount = settings.ControlPointCount;

                var newGrid = new Vector3Int(4, 4, 4);
                settings.ResizeGrid(newGrid);

                Assert.That(settings.GridSize, Is.EqualTo(newGrid));
                Assert.That(settings.ControlPointCount, Is.EqualTo(4 * 4 * 4));
                Assert.That(settings.ControlPointCount, Is.Not.EqualTo(originalCount));

                // Deform should still work after resize
                ReleaseRuntimeMesh(fixture.Deformer);
                fixture.Deformer.InvalidateCache();
                var mesh = fixture.Deformer.Deform(false);
                Assert.That(mesh, Is.Not.Null);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void ResizeGrid_SameSize_IsNoOp()
        {
            var fixture = CreateFixture("ResizeGrid_SameSize_IsNoOp");
            try
            {
                var settings = fixture.Deformer.Layers[0].Settings;
                var grid = settings.GridSize;

                // Move a control point
                var original = settings.GetControlPointLocal(0);
                var delta = new Vector3(0.1f, 0f, 0f);
                settings.SetControlPointLocal(0, original + delta);

                // Resize to same size — should be no-op, preserving custom point
                settings.ResizeGrid(grid);
                AssertApproximately(original + delta, settings.GetControlPointLocal(0), Epsilon);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void GetControlPointLocal_OutOfBounds_ReturnsZero()
        {
            var asset = new LatticeAsset();
            asset.EnsureInitialized();

            Assert.That(asset.GetControlPointLocal(-1), Is.EqualTo(Vector3.zero));
            Assert.That(asset.GetControlPointLocal(999), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void SetControlPointLocal_OutOfBounds_NoException()
        {
            var asset = new LatticeAsset();
            asset.EnsureInitialized();

            Assert.DoesNotThrow(() => asset.SetControlPointLocal(-1, Vector3.one));
            Assert.DoesNotThrow(() => asset.SetControlPointLocal(999, Vector3.one));
        }

        [Test]
        public void InterpolationMode_SwitchBetweenModes_BothProduceValidResults()
        {
            var fixture = CreateInterpolationFixture("InterpolationMode_SwitchBetweenModes_BothProduceValidResults");
            try
            {
                var deformer = fixture.Deformer;
                var settings = deformer.Layers[0].Settings;
                int vertexCount = deformer.SourceMesh.vertexCount;
                int centerVertex = vertexCount - 1;
                var delta = new Vector3(0f, 0.2f, 0f);

                settings.SetControlPointLocal(0,
                    settings.GetControlPointLocal(0) + delta);

                var srcVerts = deformer.SourceMesh.vertices;

                // Trilinear
                settings.Interpolation = LatticeInterpolationMode.Trilinear;
                ReleaseRuntimeMesh(deformer);
                deformer.InvalidateCache();
                var triMesh = deformer.Deform(false);
                Assert.That(triMesh, Is.Not.Null);
                Assert.That(triMesh.vertexCount, Is.EqualTo(vertexCount));
                var triVerts = triMesh.vertices.Clone() as Vector3[];

                foreach (var v in triVerts)
                    AssertFinite(v);

                AssertApproximately(srcVerts[centerVertex], triVerts[centerVertex]);

                // CubicBernstein
                settings.Interpolation = LatticeInterpolationMode.CubicBernstein;
                ReleaseRuntimeMesh(deformer);
                deformer.InvalidateCache();
                var cubicMesh = deformer.Deform(false);
                Assert.That(cubicMesh, Is.Not.Null);
                Assert.That(cubicMesh.vertexCount, Is.EqualTo(vertexCount));
                var cubicVerts = cubicMesh.vertices;

                foreach (var v in cubicVerts)
                    AssertFinite(v);

                // The center has no contribution from corner 0 in the local trilinear cell.
                // A 3x3x3 Bernstein lattice uses degree two per axis, so corner 0 has
                // B0(0.5)^3 = (0.25)^3 = 1/64 influence at the center.
                var expectedCubicCenter = srcVerts[centerVertex] + delta / 64f;
                AssertApproximately(expectedCubicCenter, cubicVerts[centerVertex]);
                Assert.That(
                    (cubicVerts[centerVertex] - triVerts[centerVertex]).sqrMagnitude,
                    Is.GreaterThan(Epsilon * Epsilon));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void InterpolationMode_TwoPointGrid_CubicBernsteinMatchesTrilinear()
        {
            var fixture = CreateInterpolationFixture("InterpolationMode_TwoPointGrid_CubicBernsteinMatchesTrilinear");
            try
            {
                var deformer = fixture.Deformer;
                var settings = deformer.Layers[0].Settings;
                settings.GridSize = new Vector3Int(2, 2, 2);
                settings.ResetControlPoints();
                settings.SetControlPointLocal(0, settings.GetControlPointLocal(0) + Vector3.up * 0.2f);

                settings.Interpolation = LatticeInterpolationMode.Trilinear;
                ReleaseRuntimeMesh(deformer);
                deformer.InvalidateCache();
                var trilinear = deformer.Deform(false).vertices;

                settings.Interpolation = LatticeInterpolationMode.CubicBernstein;
                ReleaseRuntimeMesh(deformer);
                deformer.InvalidateCache();
                var bernstein = deformer.Deform(false).vertices;

                Assert.That(bernstein.Length, Is.EqualTo(trilinear.Length));
                for (int vertex = 0; vertex < trilinear.Length; vertex++)
                {
                    AssertFinite(bernstein[vertex]);
                    AssertApproximately(trilinear[vertex], bernstein[vertex]);
                }
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void InterpolationMode_AsymmetricHighOrderGrid_UsesAllAxisDegrees()
        {
            var fixture = CreateInterpolationFixture("InterpolationMode_AsymmetricHighOrderGrid_UsesAllAxisDegrees");
            try
            {
                var deformer = fixture.Deformer;
                var settings = deformer.Layers[0].Settings;
                settings.GridSize = new Vector3Int(4, 3, 5);
                settings.ResetControlPoints();
                var delta = Vector3.up * 0.2f;
                settings.SetControlPointLocal(0, settings.GetControlPointLocal(0) + delta);
                settings.Interpolation = LatticeInterpolationMode.CubicBernstein;

                ReleaseRuntimeMesh(deformer);
                deformer.InvalidateCache();
                var result = deformer.Deform(false).vertices;

                foreach (var vertex in result)
                    AssertFinite(vertex);

                // At t=0.5 the first basis values for degrees 3, 2 and 4 are
                // 1/8, 1/4 and 1/16. Their tensor product is 1/512.
                int centerVertex = deformer.SourceMesh.vertexCount - 1;
                AssertApproximately(
                    deformer.SourceMesh.vertices[centerVertex] + delta / 512f,
                    result[centerVertex]);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void HasCustomizedControlPoints_DetectsMovement()
        {
            var fixture = CreateFixture("HasCustomizedControlPoints_DetectsMovement");
            try
            {
                var settings = fixture.Deformer.Layers[0].Settings;

                // Fresh lattice — no customization
                // (Might already have initialized control points, so just add delta)
                var delta = new Vector3(0.05f, 0f, 0f);
                settings.SetControlPointLocal(0, settings.GetControlPointLocal(0) + delta);

                Assert.That(settings.HasCustomizedControlPoints(), Is.True);
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // BlendShapeName Edge Cases
        // ========================================================================

        [Test]
        public void EffectiveBlendShapeName_WhitespaceOnly_FallsBackToLayerName()
        {
            var layer = new LatticeLayer();
            layer.Name = "MyLayer";
            layer.BlendShapeName = "   ";
            Assert.That(layer.EffectiveBlendShapeName, Is.EqualTo("MyLayer"));
        }

        [Test]
        public void EffectiveBlendShapeName_Null_FallsBackToLayerName()
        {
            var layer = new LatticeLayer();
            layer.Name = "TestLayer";
            layer.BlendShapeName = null;
            Assert.That(layer.EffectiveBlendShapeName, Is.EqualTo("TestLayer"));
        }

        [Test]
        public void EffectiveBlendShapeName_ValidName_UsesCustomName()
        {
            var layer = new LatticeLayer();
            layer.Name = "LayerName";
            layer.BlendShapeName = "CustomBS";
            Assert.That(layer.EffectiveBlendShapeName, Is.EqualTo("CustomBS"));
        }

        // ========================================================================
        // Layer Enabled/Disabled Interactions
        // ========================================================================

        [Test]
        public void DisabledLayer_BlendShapeOutput_NotProduced()
        {
            var fixture = CreateFixture("DisabledLayer_BlendShapeOutput_NotProduced");
            try
            {
                var deformer = fixture.Deformer;
                int idx = deformer.AddLayer("DisabledBS", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.1f, 0f, 0f));

                var layer = deformer.Layers[idx];
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                layer.Enabled = false;

                ReleaseRuntimeMesh(deformer);
                var mesh = deformer.Deform(false);

                int sourceBSCount = deformer.SourceMesh.blendShapeCount;
                Assert.That(mesh.blendShapeCount, Is.EqualTo(sourceBSCount),
                    "Disabled layer should not produce BlendShape");
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void ReenableLayer_ContributesAgain()
        {
            var fixture = CreateFixture("ReenableLayer_ContributesAgain");
            try
            {
                var deformer = fixture.Deformer;
                int idx = deformer.AddLayer("Toggle", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();
                var disp = new Vector3(0.1f, 0f, 0f);
                deformer.SetDisplacement(0, disp);

                var src = deformer.SourceMesh.vertices;

                // Disable
                deformer.Layers[idx].Enabled = false;
                ReleaseRuntimeMesh(deformer);
                var meshOff = deformer.Deform(false);
                AssertApproximately(src[0], meshOff.vertices[0], 2e-3f);

                // Re-enable
                deformer.Layers[idx].Enabled = true;
                ReleaseRuntimeMesh(deformer);
                var meshOn = deformer.Deform(false);
                AssertApproximately(src[0] + disp, meshOn.vertices[0], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Multiple Deform Consistency
        // ========================================================================

        [Test]
        public void Deform_ThreeTimes_AllConsistent()
        {
            var fixture = CreateFixture("Deform_ThreeTimes_AllConsistent");
            try
            {
                var deformer = fixture.Deformer;
                int idx = deformer.AddLayer("Consistent", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.1f, 0.2f, 0.3f));
                deformer.SetDisplacement(1, new Vector3(-0.05f, 0f, 0.1f));

                Vector3[][] results = new Vector3[3][];
                for (int i = 0; i < 3; i++)
                {
                    ReleaseRuntimeMesh(deformer);
                    var mesh = deformer.Deform(false);
                    results[i] = mesh.vertices.Clone() as Vector3[];
                }

                for (int i = 1; i < 3; i++)
                {
                    Assert.That(results[i].Length, Is.EqualTo(results[0].Length));
                    for (int v = 0; v < results[0].Length; v++)
                    {
                        AssertApproximately(results[0][v], results[i][v], Epsilon);
                    }
                }
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // SplitLayerByAxis / FlipLayerByAxis Error Handling
        // ========================================================================

        [Test]
        public void SplitLayerByAxis_InvalidIndex_NoException()
        {
            var fixture = CreateFixture("SplitLayerByAxis_InvalidIndex_NoException");
            try
            {
                var deformer = fixture.Deformer;
                Assert.DoesNotThrow(() => deformer.SplitLayerByAxis(-1, 0, true));
                Assert.DoesNotThrow(() => deformer.SplitLayerByAxis(999, 0, true));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void FlipLayerByAxis_InvalidIndex_NoException()
        {
            var fixture = CreateFixture("FlipLayerByAxis_InvalidIndex_NoException");
            try
            {
                var deformer = fixture.Deformer;
                Assert.DoesNotThrow(() => deformer.FlipLayerByAxis(-1, 0));
                Assert.DoesNotThrow(() => deformer.FlipLayerByAxis(999, 0));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void SplitLayerByAxis_AllThreeAxes_BrushLayer()
        {
            // Verify Z axis split works (X and Y are already tested)
            var fixture = CreateFixture("SplitLayerByAxis_AllThreeAxes_BrushLayer");
            try
            {
                var deformer = fixture.Deformer;
                int idx = deformer.AddLayer("ZSplit", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[idx];
                var verts = deformer.SourceMesh.vertices;
                var disp = new Vector3(0.1f, 0.1f, 0.1f);
                for (int i = 0; i < verts.Length; i++)
                    deformer.SetDisplacement(i, disp);

                deformer.SplitLayerByAxis(idx, 2, true); // Z axis, keep positive

                for (int i = 0; i < verts.Length; i++)
                {
                    var d = layer.GetBrushDisplacement(i);
                    if (verts[i].z >= 0f)
                        AssertApproximately(disp, d, Epsilon);
                    else
                        AssertApproximately(Vector3.zero, d, Epsilon);
                }
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Brush Displacement Edge Cases
        // ========================================================================

        [Test]
        public void BrushDisplacement_OutOfBoundsIndex_NoException()
        {
            var layer = new LatticeLayer();
            layer.EnsureBrushDisplacementCapacity(4);

            Assert.DoesNotThrow(() => layer.SetBrushDisplacement(-1, Vector3.one));
            Assert.DoesNotThrow(() => layer.SetBrushDisplacement(100, Vector3.one));
            Assert.That(layer.GetBrushDisplacement(-1), Is.EqualTo(Vector3.zero));
            Assert.That(layer.GetBrushDisplacement(100), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void AddBrushDisplacement_AccumulatesOnLayer()
        {
            var layer = new LatticeLayer();
            layer.EnsureBrushDisplacementCapacity(4);

            var d1 = new Vector3(0.1f, 0f, 0f);
            var d2 = new Vector3(0f, 0.2f, 0f);
            layer.AddBrushDisplacement(0, d1);
            layer.AddBrushDisplacement(0, d2);

            AssertApproximately(d1 + d2, layer.GetBrushDisplacement(0), Epsilon);
        }

        // ========================================================================
        // Layer Type and Settings
        // ========================================================================

        [Test]
        public void BrushLayer_SettingsProperty_ReturnsNonNull()
        {
            var fixture = CreateFixture("BrushLayer_SettingsProperty_ReturnsNonNull");
            try
            {
                var deformer = fixture.Deformer;
                int idx = deformer.AddLayer("BrushSettings", MeshDeformerLayerType.Brush);
                var layer = deformer.Layers[idx];

                // Even brush layers should have non-null Settings (for grid info)
                Assert.That(layer.Settings, Is.Not.Null);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void LatticeLayer_Type_IsLattice()
        {
            var fixture = CreateFixture("LatticeLayer_Type_IsLattice");
            try
            {
                Assert.That(fixture.Deformer.Layers[0].Type, Is.EqualTo(MeshDeformerLayerType.Lattice));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void BrushLayer_Type_IsBrush()
        {
            var fixture = CreateFixture("BrushLayer_Type_IsBrush");
            try
            {
                var deformer = fixture.Deformer;
                int idx = deformer.AddLayer("TypeCheck", MeshDeformerLayerType.Brush);
                Assert.That(deformer.Layers[idx].Type, Is.EqualTo(MeshDeformerLayerType.Brush));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void ActiveLayerType_ReflectsActiveLayer()
        {
            var fixture = CreateFixture("ActiveLayerType_ReflectsActiveLayer");
            try
            {
                var deformer = fixture.Deformer;
                Assert.That(deformer.ActiveLayerType, Is.EqualTo(MeshDeformerLayerType.Lattice));

                int brushIdx = deformer.AddLayer("B", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                Assert.That(deformer.ActiveLayerType, Is.EqualTo(MeshDeformerLayerType.Brush));
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // GetSourceBlendShapeNames Edge Cases
        // ========================================================================

        [Test]
        public void GetSourceBlendShapeNames_NoBlendShapes_ReturnsEmpty()
        {
            var fixture = CreateFixture("GetSourceBlendShapeNames_NoBlendShapes_ReturnsEmpty");
            try
            {
                var names = fixture.Deformer.GetSourceBlendShapeNames();
                Assert.That(names, Is.Not.Null);
                Assert.That(names.Length, Is.EqualTo(0));
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // SkinnedMeshRenderer Support
        // ========================================================================

        [Test]
        public void SkinnedMeshRenderer_DeformWorks()
        {
            var root = new GameObject("SkinnedMeshRenderer_DeformWorks");
            var smr = root.AddComponent<SkinnedMeshRenderer>();

            var sourceMesh = CreateCubeMesh();
            smr.sharedMesh = sourceMesh;

            // Need at least one bone for SkinnedMeshRenderer
            var boneObj = new GameObject("Bone");
            boneObj.transform.parent = root.transform;
            smr.bones = new[] { boneObj.transform };
            smr.rootBone = boneObj.transform;

            var deformer = root.AddComponent<LatticeDeformer>();
            try
            {
                deformer.Reset();
                Assert.That(deformer.SourceMesh, Is.Not.Null);

                var mesh = deformer.Deform(false);
                Assert.That(mesh, Is.Not.Null);
                Assert.That(mesh.vertexCount, Is.EqualTo(sourceMesh.vertexCount));

                // Add displacement and verify
                int idx = deformer.AddLayer("SMR Brush", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();
                var disp = new Vector3(0.1f, 0f, 0f);
                deformer.SetDisplacement(0, disp);

                ReleaseRuntimeMesh(deformer);
                var deformed = deformer.Deform(false);
                var srcVerts = deformer.SourceMesh.vertices;
                AssertApproximately(srcVerts[0] + disp, deformed.vertices[0], 2e-3f);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(sourceMesh);
            }
        }

        // ========================================================================
        // Helpers
        // ========================================================================

        private static TestFixture CreateFixture(string name)
        {
            var root = new GameObject(name);
            var filter = root.AddComponent<MeshFilter>();
            root.AddComponent<MeshRenderer>();

            var sourceMesh = CreateCubeMesh();
            filter.sharedMesh = sourceMesh;

            var deformer = root.AddComponent<LatticeDeformer>();
            deformer.Reset();
            deformer.Deform(false);

            return new TestFixture(root, sourceMesh, deformer);
        }

        private static TestFixture CreateInterpolationFixture(string name)
        {
            var root = new GameObject(name);
            var filter = root.AddComponent<MeshFilter>();
            root.AddComponent<MeshRenderer>();

            var sourceMesh = new Mesh
            {
                name = "InterpolationTestMesh",
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, -0.5f),
                    new Vector3( 0.5f, -0.5f, -0.5f),
                    new Vector3( 0.5f,  0.5f, -0.5f),
                    new Vector3(-0.5f,  0.5f, -0.5f),
                    new Vector3(-0.5f, -0.5f,  0.5f),
                    new Vector3( 0.5f, -0.5f,  0.5f),
                    new Vector3( 0.5f,  0.5f,  0.5f),
                    new Vector3(-0.5f,  0.5f,  0.5f),
                    Vector3.zero
                },
                triangles = new[]
                {
                    0, 2, 1, 0, 3, 2,
                    4, 5, 6, 4, 6, 7,
                    0, 1, 5, 0, 5, 4,
                    2, 3, 7, 2, 7, 6,
                    0, 4, 7, 0, 7, 3,
                    1, 2, 6, 1, 6, 5
                }
            };
            sourceMesh.RecalculateNormals();
            sourceMesh.RecalculateBounds();
            filter.sharedMesh = sourceMesh;

            var deformer = root.AddComponent<LatticeDeformer>();
            deformer.Reset();
            deformer.Deform(false);

            return new TestFixture(root, sourceMesh, deformer);
        }

        private static Mesh CreateCubeMesh()
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var source = temp.GetComponent<MeshFilter>().sharedMesh;
            var mesh = Object.Instantiate(source);
            mesh.name = "EdgeCaseTestMesh";
            Object.DestroyImmediate(temp);
            return mesh;
        }

        private static void ReleaseRuntimeMesh(LatticeDeformer deformer)
        {
            var field = typeof(LatticeDeformer).GetField("_runtimeMesh", s_privateInstance);
            var mesh = field?.GetValue(deformer) as Mesh;
            if (mesh != null) Object.DestroyImmediate(mesh);
            field?.SetValue(deformer, null);
        }

        private static void AssertApproximately(Vector3 expected, Vector3 actual,
            float tolerance = Epsilon)
        {
            Assert.That((expected - actual).sqrMagnitude,
                Is.LessThanOrEqualTo(tolerance * tolerance),
                $"Expected {expected} but got {actual}");
        }

        private static void AssertFinite(Vector3 value)
        {
            Assert.That(
                float.IsNaN(value.x) || float.IsInfinity(value.x) ||
                float.IsNaN(value.y) || float.IsInfinity(value.y) ||
                float.IsNaN(value.z) || float.IsInfinity(value.z),
                Is.False,
                $"Expected a finite vector but got {value}");
        }

        private sealed class TestFixture
        {
            public GameObject Root { get; }
            public Mesh SourceMesh { get; }
            public LatticeDeformer Deformer { get; }

            public TestFixture(GameObject root, Mesh sourceMesh, LatticeDeformer deformer)
            {
                Root = root;
                SourceMesh = sourceMesh;
                Deformer = deformer;
            }

            public void Dispose()
            {
                if (Root != null) Object.DestroyImmediate(Root);
                if (SourceMesh != null) Object.DestroyImmediate(SourceMesh);
            }
        }
    }
}
#endif
