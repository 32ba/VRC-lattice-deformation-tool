#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    /// <summary>
    /// Practical scenario tests using realistic avatar/clothing-like mesh geometry.
    /// Tests workflows that VRChat users actually perform.
    /// </summary>
    public sealed class PracticalScenarioTests
    {
        private const float Epsilon = 1e-4f;
        private static readonly BindingFlags s_privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        // ========================================================================
        // Scenario 1: Clothing penetration fix (body + clothing layers)
        // ========================================================================

        [Test]
        public void ClothingPushInward_OnlyAffectsOuterIsland()
        {
            // Simulates: user pushes clothing inward to fix penetration,
            // inner body mesh should remain completely untouched.
            var fixture = CreateConcentricFixture("ClothingPushInward_OnlyAffectsOuterIsland");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;

                TestMeshFactory.GetConcentricCylinderRanges(16, 8,
                    out int innerStart, out int innerEnd, out int outerStart, out int outerEnd);

                // Create brush layer and push outer (clothing) vertices inward
                int brushIdx = deformer.AddLayer("Fix Penetration", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();

                var sourceVerts = deformer.SourceMesh.vertices;
                for (int i = outerStart; i < outerEnd; i++)
                {
                    // Push inward along radial direction
                    var radial = new Vector3(sourceVerts[i].x, 0f, sourceVerts[i].z).normalized;
                    deformer.SetDisplacement(i, -radial * 0.005f);
                }

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);
                var deformed = runtimeMesh.vertices;

                // Inner (body) vertices must be unchanged
                for (int i = innerStart; i < innerEnd; i++)
                {
                    AssertApproximately(sourceVerts[i], deformed[i], Epsilon);
                }

                // Outer (clothing) vertices should have moved inward
                bool anyMoved = false;
                for (int i = outerStart; i < outerEnd; i++)
                {
                    if ((deformed[i] - sourceVerts[i]).sqrMagnitude > Epsilon * Epsilon)
                    {
                        anyMoved = true;
                        break;
                    }
                }
                Assert.That(anyMoved, Is.True, "Outer clothing vertices should have been displaced");
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void MaskProtectsBodyWhileEditingClothing()
        {
            // Simulates: user masks body vertices to protect them,
            // then applies displacement to ALL vertices — body should be unaffected.
            var fixture = CreateConcentricFixture("MaskProtectsBodyWhileEditingClothing");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;

                TestMeshFactory.GetConcentricCylinderRanges(16, 8,
                    out int innerStart, out int innerEnd, out int outerStart, out int outerEnd);

                int brushIdx = deformer.AddLayer("Masked Edit", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[brushIdx];
                layer.EnsureVertexMaskCapacity(vertexCount);

                // Protect inner (body) vertices, leave outer (clothing) editable
                for (int i = innerStart; i < innerEnd; i++)
                {
                    layer.SetVertexMask(i, 0f); // Protected
                }
                for (int i = outerStart; i < outerEnd; i++)
                {
                    layer.SetVertexMask(i, 1f); // Editable
                }

                // Apply uniform displacement to ALL vertices
                var displacement = new Vector3(0f, 0.02f, 0f);
                for (int i = 0; i < vertexCount; i++)
                {
                    deformer.SetDisplacement(i, displacement);
                }

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);
                var sourceVerts = deformer.SourceMesh.vertices;
                var deformed = runtimeMesh.vertices;

                // Body: untouched
                for (int i = innerStart; i < innerEnd; i++)
                {
                    AssertApproximately(sourceVerts[i], deformed[i], 2e-3f);
                }

                // Clothing: displaced
                for (int i = outerStart; i < outerEnd; i++)
                {
                    var expected = sourceVerts[i] + displacement;
                    AssertApproximately(expected, deformed[i], 2e-3f);
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        // ========================================================================
        // Scenario 2: Symmetric avatar L/R workflow
        // ========================================================================

        [Test]
        public void SymmetricAvatar_EditLeft_FlipToRight()
        {
            // Simulates: user edits left arm, then flips X to apply to right arm.
            var fixture = CreateHumanoidFixture("SymmetricAvatar_EditLeft_FlipToRight");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;
                var sourceVerts = deformer.SourceMesh.vertices;

                int brushIdx = deformer.AddLayer("Left Edit", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[brushIdx];

                // Apply displacement only to vertices on negative X (left side)
                var displacement = new Vector3(0f, 0.01f, 0f);
                int leftCount = 0;
                for (int i = 0; i < vertexCount; i++)
                {
                    if (sourceVerts[i].x < -0.05f)
                    {
                        deformer.SetDisplacement(i, displacement);
                        leftCount++;
                    }
                }
                Assert.That(leftCount, Is.GreaterThan(0), "Should have vertices on left side");

                // Split: keep left side only
                deformer.SplitLayerByAxis(brushIdx, 0, false);

                // Duplicate and flip for right side
                int dupIdx = deformer.DuplicateLayer(brushIdx);
                deformer.FlipLayerByAxis(dupIdx, 0);

                // Both layers should produce symmetric deformation
                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);
                var deformed = runtimeMesh.vertices;

                // Find mirror pairs and verify symmetry
                int mirrorPairsFound = 0;
                for (int i = 0; i < vertexCount; i++)
                {
                    if (sourceVerts[i].x < -0.05f)
                    {
                        // Find mirror vertex on positive X
                        var mirrorPos = new Vector3(-sourceVerts[i].x, sourceVerts[i].y, sourceVerts[i].z);
                        int mirrorIdx = FindNearestVertex(sourceVerts, mirrorPos, 0.002f);
                        if (mirrorIdx >= 0)
                        {
                            // Both should have same Y displacement
                            float leftDY = deformed[i].y - sourceVerts[i].y;
                            float rightDY = deformed[mirrorIdx].y - sourceVerts[mirrorIdx].y;
                            Assert.That(Mathf.Abs(leftDY - rightDY), Is.LessThan(2e-3f),
                                $"Mirror pair {i}/{mirrorIdx} should have symmetric Y displacement");
                            mirrorPairsFound++;
                        }
                    }
                }
                Assert.That(mirrorPairsFound, Is.GreaterThan(0), "Should find mirror vertex pairs");
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void SymmetricAvatar_SplitR_KeepsOnlyRightSide()
        {
            var fixture = CreateHumanoidFixture("SymmetricAvatar_SplitR_KeepsOnlyRightSide");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;
                var sourceVerts = deformer.SourceMesh.vertices;

                int brushIdx = deformer.AddLayer("Full Edit", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();

                var displacement = new Vector3(0.01f, 0.02f, 0f);
                for (int i = 0; i < vertexCount; i++)
                {
                    deformer.SetDisplacement(i, displacement);
                }

                // Split: keep positive X (right side)
                deformer.SplitLayerByAxis(brushIdx, 0, true);

                var layer = deformer.Layers[brushIdx];
                for (int i = 0; i < vertexCount; i++)
                {
                    var d = layer.GetBrushDisplacement(i);
                    if (sourceVerts[i].x >= 0f)
                    {
                        AssertApproximately(displacement, d, Epsilon);
                    }
                    else
                    {
                        AssertApproximately(Vector3.zero, d, Epsilon);
                    }
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        // ========================================================================
        // Scenario 3: BlendShape workflow on realistic mesh
        // ========================================================================

        [Test]
        public void BlendShapeImportModifyExport_OnCylinder()
        {
            // Simulates: import existing "Shrink" BlendShape, modify it, output as new BlendShape.
            var fixture = CreateBlendShapeCylinderFixture("BlendShapeImportModifyExport_OnCylinder");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;

                // Verify source has expected BlendShapes
                var names = deformer.GetSourceBlendShapeNames();
                Assert.That(names, Contains.Item("Shrink"));
                Assert.That(names, Contains.Item("Expand"));
                Assert.That(names, Contains.Item("MoveUp"));

                // Import "Shrink" BlendShape
                int shrinkIdx = System.Array.IndexOf(names, "Shrink");
                int layerIdx = deformer.ImportBlendShapeAsLayer(shrinkIdx);

                var layer = deformer.Layers[layerIdx];
                Assert.That(layer.Name, Is.EqualTo("Shrink"));
                Assert.That(layer.BrushDisplacementCount, Is.EqualTo(vertexCount));

                // Modify: add extra displacement to upper half (adjust shrink shape)
                var sourceVerts = deformer.SourceMesh.vertices;
                var extraDelta = new Vector3(0f, 0.01f, 0f);
                for (int i = 0; i < vertexCount; i++)
                {
                    if (sourceVerts[i].y > 0f)
                    {
                        layer.AddBrushDisplacement(i, extraDelta);
                    }
                }

                // Output as new BlendShape
                layer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                layer.BlendShapeName = "ShrinkModified";

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);

                // Find the output
                int outputIdx = -1;
                for (int i = 0; i < runtimeMesh.blendShapeCount; i++)
                {
                    if (runtimeMesh.GetBlendShapeName(i) == "ShrinkModified")
                    {
                        outputIdx = i;
                        break;
                    }
                }
                Assert.That(outputIdx, Is.GreaterThanOrEqualTo(0));

                // Verify deltas: upper half should have original + extra
                var outputDeltas = new Vector3[vertexCount];
                runtimeMesh.GetBlendShapeFrameVertices(outputIdx, 0, outputDeltas, null, null);

                var originalDeltas = new Vector3[vertexCount];
                deformer.SourceMesh.GetBlendShapeFrameVertices(shrinkIdx, 0, originalDeltas, null, null);

                for (int i = 0; i < vertexCount; i++)
                {
                    if (sourceVerts[i].y > 0f)
                    {
                        AssertApproximately(originalDeltas[i] + extraDelta, outputDeltas[i], 2e-3f);
                    }
                    else
                    {
                        AssertApproximately(originalDeltas[i], outputDeltas[i], 2e-3f);
                    }
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void MultipleBlendShapeImport_EachAsLayer_ThenOutputAll()
        {
            // Simulates: import all 3 BlendShapes, each as a separate layer,
            // then output each as BlendShape.
            var fixture = CreateBlendShapeCylinderFixture(
                "MultipleBlendShapeImport_EachAsLayer_ThenOutputAll");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;
                var names = deformer.GetSourceBlendShapeNames();

                // Import all 3 and set to BlendShape output
                var layerIndices = new int[names.Length];
                for (int s = 0; s < names.Length; s++)
                {
                    layerIndices[s] = deformer.ImportBlendShapeAsLayer(s);
                    var layer = deformer.Layers[layerIndices[s]];
                    layer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                    layer.BlendShapeName = $"Modified_{names[s]}";
                }

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);

                // Should have original BlendShapes + 3 new ones
                int originalBSCount = deformer.SourceMesh.blendShapeCount;
                Assert.That(runtimeMesh.blendShapeCount, Is.EqualTo(originalBSCount + names.Length));

                // Each output should have non-zero deltas
                for (int s = 0; s < names.Length; s++)
                {
                    int bsIdx = originalBSCount + s;
                    Assert.That(runtimeMesh.GetBlendShapeName(bsIdx),
                        Is.EqualTo($"Modified_{names[s]}"));

                    var deltas = new Vector3[vertexCount];
                    runtimeMesh.GetBlendShapeFrameVertices(bsIdx, 0, deltas, null, null);

                    bool anyNonZero = false;
                    for (int v = 0; v < vertexCount; v++)
                    {
                        if (deltas[v].sqrMagnitude > Epsilon * Epsilon)
                        {
                            anyNonZero = true;
                            break;
                        }
                    }
                    Assert.That(anyNonZero, Is.True,
                        $"BlendShape '{names[s]}' should have non-zero deltas");
                }

                // Vertices should be unchanged (all layers are BlendShape output)
                var sourceVerts = deformer.SourceMesh.vertices;
                var deformedVerts = runtimeMesh.vertices;
                for (int i = 0; i < vertexCount; i++)
                {
                    AssertApproximately(sourceVerts[i], deformedVerts[i], 2e-3f);
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        // ========================================================================
        // Scenario 4: Multi-island mesh operations
        // ========================================================================

        [Test]
        public void MultiIslandMesh_DeformOneIsland_OthersUnchanged()
        {
            // Simulates: mesh has separate clothing parts (collar, sleeve, body),
            // editing one should not affect others.
            var fixture = CreateMultiIslandFixture("MultiIslandMesh_DeformOneIsland_OthersUnchanged");
            try
            {
                var deformer = fixture.Deformer;
                var sourceVerts = deformer.SourceMesh.vertices;
                int vertexCount = sourceVerts.Length;

                // First island: 8 segments × 4 rings = 32 vertices
                int islandSize = 8 * 4;

                int brushIdx = deformer.AddLayer("Island Edit", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();

                // Displace only first island
                var displacement = new Vector3(0.05f, 0f, 0f);
                for (int i = 0; i < islandSize && i < vertexCount; i++)
                {
                    deformer.SetDisplacement(i, displacement);
                }

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);
                var deformed = runtimeMesh.vertices;

                // First island: displaced
                for (int i = 0; i < islandSize && i < vertexCount; i++)
                {
                    AssertApproximately(sourceVerts[i] + displacement, deformed[i], 2e-3f);
                }

                // Other islands: untouched
                for (int i = islandSize; i < vertexCount; i++)
                {
                    AssertApproximately(sourceVerts[i], deformed[i], 2e-3f);
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        // ========================================================================
        // Scenario 5: High vertex count correctness
        // ========================================================================

        [Test]
        public void HighVertexCount_DeformationIsCorrect()
        {
            // Test with a mesh similar in scale to real avatars (~2000+ vertices)
            var fixture = CreateHighDensityFixture("HighVertexCount_DeformationIsCorrect",
                segments: 32, rings: 64);
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;
                Assert.That(vertexCount, Is.GreaterThanOrEqualTo(2000),
                    "Should have a realistic vertex count");

                int brushIdx = deformer.AddLayer("Dense Brush", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();

                // Apply gradient displacement (stronger near center)
                var sourceVerts = deformer.SourceMesh.vertices;
                for (int i = 0; i < vertexCount; i++)
                {
                    float heightFactor = 1f - Mathf.Abs(sourceVerts[i].y) / 0.2f;
                    heightFactor = Mathf.Clamp01(heightFactor);
                    deformer.SetDisplacement(i, new Vector3(0.01f * heightFactor, 0f, 0f));
                }

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);
                Assert.That(runtimeMesh, Is.Not.Null);
                Assert.That(runtimeMesh.vertexCount, Is.EqualTo(vertexCount));

                // Verify no NaN/Infinity
                var deformed = runtimeMesh.vertices;
                for (int i = 0; i < vertexCount; i++)
                {
                    Assert.That(float.IsNaN(deformed[i].x) || float.IsNaN(deformed[i].y)
                        || float.IsNaN(deformed[i].z), Is.False, $"NaN at vertex {i}");
                    Assert.That(float.IsInfinity(deformed[i].x) || float.IsInfinity(deformed[i].y)
                        || float.IsInfinity(deformed[i].z), Is.False, $"Inf at vertex {i}");
                }

                // Verify vertices near center (y≈0) have more displacement than edges
                int centerVertex = -1;
                int edgeVertex = -1;
                for (int i = 0; i < vertexCount; i++)
                {
                    if (Mathf.Abs(sourceVerts[i].y) < 0.01f && centerVertex < 0) centerVertex = i;
                    if (Mathf.Abs(sourceVerts[i].y) > 0.18f && edgeVertex < 0) edgeVertex = i;
                    if (centerVertex >= 0 && edgeVertex >= 0) break;
                }
                if (centerVertex >= 0 && edgeVertex >= 0)
                {
                    float centerDisp = (deformed[centerVertex] - sourceVerts[centerVertex]).magnitude;
                    float edgeDisp = (deformed[edgeVertex] - sourceVerts[edgeVertex]).magnitude;
                    Assert.That(centerDisp, Is.GreaterThan(edgeDisp),
                        "Center vertices should have larger displacement than edge vertices");
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void HighVertexCount_BlendShapeOutput_AllVerticesProcessed()
        {
            var fixture = CreateHighDensityFixture(
                "HighVertexCount_BlendShapeOutput_AllVerticesProcessed",
                segments: 32, rings: 64);
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;

                int brushIdx = deformer.AddLayer("Dense BS", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();

                // Set displacement on every vertex
                var displacement = new Vector3(0.005f, 0f, 0f);
                for (int i = 0; i < vertexCount; i++)
                {
                    deformer.SetDisplacement(i, displacement);
                }

                var layer = deformer.Layers[brushIdx];
                layer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);

                int shapeIdx = runtimeMesh.blendShapeCount - 1;
                var deltas = new Vector3[vertexCount];
                runtimeMesh.GetBlendShapeFrameVertices(shapeIdx, 0, deltas, null, null);

                // Every vertex should have the displacement in the BlendShape
                int nonZeroCount = 0;
                for (int i = 0; i < vertexCount; i++)
                {
                    if (deltas[i].sqrMagnitude > Epsilon * Epsilon)
                        nonZeroCount++;
                }
                Assert.That(nonZeroCount, Is.EqualTo(vertexCount),
                    "All vertices should have non-zero BlendShape deltas");
            }
            finally
            {
                fixture.Dispose();
            }
        }

        // ========================================================================
        // Scenario 6: Layered editing workflow on clothing
        // ========================================================================

        [Test]
        public void LayeredClothingEdit_LatticeThenBrushRefinement()
        {
            // Simulates typical workflow:
            // 1. Lattice layer for coarse adjustment (move clothing)
            // 2. Brush layer for fine-tuning (fix individual vertices)
            var fixture = CreateCylinderFixture("LayeredClothingEdit_LatticeThenBrushRefinement");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;
                var sourceVerts = deformer.SourceMesh.vertices;

                // Lattice: shift the whole mesh slightly
                var latticeSettings = deformer.Layers[0].Settings;
                for (int i = 0; i < latticeSettings.ControlPointCount; i++)
                {
                    var cp = latticeSettings.GetControlPointLocal(i);
                    latticeSettings.SetControlPointLocal(i, cp + new Vector3(0.01f, 0f, 0f));
                }

                // Brush: push specific vertex further
                int brushIdx = deformer.AddLayer("Refinement", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();
                var brushDelta = new Vector3(0f, 0.02f, 0f);
                deformer.SetDisplacement(0, brushDelta);

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);
                var deformed = runtimeMesh.vertices;

                // Vertex 0 should have both lattice + brush contribution
                float totalDisp = (deformed[0] - sourceVerts[0]).magnitude;
                Assert.That(totalDisp, Is.GreaterThan(0.01f),
                    "Vertex 0 should have combined lattice + brush displacement");

                // A non-brush vertex should have only lattice contribution
                float otherDisp = (deformed[vertexCount / 2] - sourceVerts[vertexCount / 2]).magnitude;
                Assert.That(otherDisp, Is.GreaterThan(0f),
                    "Other vertices should have lattice displacement");
                Assert.That(totalDisp, Is.GreaterThan(otherDisp),
                    "Vertex 0 should have more displacement than lattice-only vertices");
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void MaskLowerHalf_ThenBrushUpperHalf()
        {
            // Simulates: user masks skirt hem to protect it, then adjusts waist area
            var fixture = CreateCylinderFixture("MaskLowerHalf_ThenBrushUpperHalf");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;
                var sourceVerts = deformer.SourceMesh.vertices;

                int brushIdx = deformer.AddLayer("Upper Edit", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[brushIdx];
                layer.EnsureVertexMaskCapacity(vertexCount);

                // Mask lower half (y < 0)
                for (int i = 0; i < vertexCount; i++)
                {
                    layer.SetVertexMask(i, sourceVerts[i].y >= 0f ? 1f : 0f);
                }

                // Apply displacement to all vertices
                var displacement = new Vector3(0.01f, 0f, 0f);
                for (int i = 0; i < vertexCount; i++)
                {
                    deformer.SetDisplacement(i, displacement);
                }

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);
                var deformed = runtimeMesh.vertices;

                for (int i = 0; i < vertexCount; i++)
                {
                    if (sourceVerts[i].y >= 0f)
                    {
                        // Upper: should be displaced
                        AssertApproximately(sourceVerts[i] + displacement, deformed[i], 2e-3f);
                    }
                    else
                    {
                        // Lower: protected by mask
                        AssertApproximately(sourceVerts[i], deformed[i], 2e-3f);
                    }
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        // ========================================================================
        // Scenario 7: BlendShape toggleable outfit adjustments
        // ========================================================================

        [Test]
        public void MultipleOutfitAdjustments_AsBlendShapes()
        {
            // Simulates: user creates multiple outfit adjustments as separate layers,
            // each output as BlendShape for in-game toggling via Avatar Menu Creator.
            // E.g., "SleevesRolled", "CollarOpen", "WaistTightened"
            var fixture = CreateCylinderFixture("MultipleOutfitAdjustments_AsBlendShapes");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;
                var sourceVerts = deformer.SourceMesh.vertices;

                // Adjustment 1: upper half inward (simulates sleeves rolled)
                int layer1 = deformer.AddLayer("SleevesRolled", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = layer1;
                deformer.EnsureDisplacementCapacity();
                for (int i = 0; i < vertexCount; i++)
                {
                    if (sourceVerts[i].y > 0.1f)
                    {
                        var radial = new Vector3(sourceVerts[i].x, 0f, sourceVerts[i].z).normalized;
                        deformer.SetDisplacement(i, -radial * 0.008f);
                    }
                }
                deformer.Layers[layer1].BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;

                // Adjustment 2: lower half tightened (simulates waist cinched)
                int layer2 = deformer.AddLayer("WaistTightened", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = layer2;
                deformer.EnsureDisplacementCapacity();
                for (int i = 0; i < vertexCount; i++)
                {
                    if (sourceVerts[i].y < -0.05f && sourceVerts[i].y > -0.15f)
                    {
                        var radial = new Vector3(sourceVerts[i].x, 0f, sourceVerts[i].z).normalized;
                        deformer.SetDisplacement(i, -radial * 0.005f);
                    }
                }
                deformer.Layers[layer2].BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);

                // Both should produce separate BlendShapes
                int bsCount = runtimeMesh.blendShapeCount;
                Assert.That(bsCount, Is.GreaterThanOrEqualTo(2));

                bool foundSleeves = false;
                bool foundWaist = false;
                for (int i = 0; i < bsCount; i++)
                {
                    string name = runtimeMesh.GetBlendShapeName(i);
                    if (name == "SleevesRolled") foundSleeves = true;
                    if (name == "WaistTightened") foundWaist = true;
                }
                Assert.That(foundSleeves, Is.True, "SleevesRolled BlendShape should exist");
                Assert.That(foundWaist, Is.True, "WaistTightened BlendShape should exist");

                // Vertices should be unchanged (all output as BlendShape)
                var deformed = runtimeMesh.vertices;
                for (int i = 0; i < vertexCount; i++)
                {
                    AssertApproximately(sourceVerts[i], deformed[i], 2e-3f);
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        // ========================================================================
        // Scenario 8: Geodesic distance with realistic topology
        // ========================================================================

        [Test]
        public void GeodesicDistance_CylinderTopology_RespectsWraparound()
        {
            // On a cylinder, the shortest path between two points may go around the wrap,
            // not through the mesh interior. Verify geodesic respects this.
            int segments = 12;
            int rings = 6;
            var mesh = TestMeshFactory.CreateCylinder(segments, rings, 1f, 2f);
            var vertices = mesh.vertices;

            // Build adjacency from triangle data (same as BrushLayerTool)
            var adjacency = BuildAdjacencyFromMesh(mesh);

            // Start at first vertex (ring 0, segment 0)
            // The diametrically opposite vertex is at ring 0, segment segments/2
            int startIdx = 0;
            int oppositeIdx = segments / 2;

            var distances = GeodesicDistanceCalculator.ComputeDistances(
                startIdx, 100f, adjacency, vertices);

            Assert.That(distances.ContainsKey(oppositeIdx), Is.True);

            // The geodesic distance around the cylinder should be π*r (half circumference)
            // since it goes through segments/2 edges, each of length 2*π*r/segments
            float expectedDist = Mathf.PI * 1f; // π * radius
            float actualDist = distances[oppositeIdx];

            // Allow some tolerance due to polygon approximation
            Assert.That(actualDist, Is.EqualTo(expectedDist).Within(expectedDist * 0.15f),
                "Geodesic distance around cylinder should approximate half circumference");

            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void GeodesicDistance_ConcentricCylinders_DoNotCross()
        {
            // Two concentric cylinders: geodesic from inner should NEVER reach outer
            int segments = 12;
            int rings = 6;
            var mesh = TestMeshFactory.CreateConcentricCylinders(segments, rings,
                0.04f, 0.06f, 0.4f);
            var vertices = mesh.vertices;
            var adjacency = BuildAdjacencyFromMesh(mesh);

            TestMeshFactory.GetConcentricCylinderRanges(segments, rings,
                out int innerStart, out int innerEnd, out int outerStart, out int outerEnd);

            // Compute geodesic from first inner vertex
            var distances = GeodesicDistanceCalculator.ComputeDistances(
                innerStart, 100f, adjacency, vertices);

            // No outer vertex should be reachable
            for (int i = outerStart; i < outerEnd; i++)
            {
                Assert.That(distances.ContainsKey(i), Is.False,
                    $"Outer vertex {i} should not be reachable from inner island");
            }

            // All inner vertices should be reachable
            for (int i = innerStart; i < innerEnd; i++)
            {
                Assert.That(distances.ContainsKey(i), Is.True,
                    $"Inner vertex {i} should be reachable from inner island start");
            }

            Object.DestroyImmediate(mesh);
        }

        // ========================================================================
        // Scenario 9: Lattice grid resize mid-workflow
        // ========================================================================

        [Test]
        public void LatticeGridResize_DeformStillCorrect()
        {
            var fixture = CreateCylinderFixture("LatticeGridResize_DeformStillCorrect");
            try
            {
                var deformer = fixture.Deformer;
                var settings = deformer.Layers[0].Settings;

                // Move a control point with default grid
                settings.SetControlPointLocal(0,
                    settings.GetControlPointLocal(0) + new Vector3(0f, 0.05f, 0f));

                ReleaseRuntimeMesh(deformer);
                deformer.InvalidateCache();
                var mesh1 = deformer.Deform(false);
                Assert.That(mesh1, Is.Not.Null);

                // Resize grid to larger
                settings.ResizeGrid(new Vector3Int(4, 4, 4));
                // Move a new control point
                settings.SetControlPointLocal(0,
                    settings.GetControlPointLocal(0) + new Vector3(0f, 0.03f, 0f));

                ReleaseRuntimeMesh(deformer);
                deformer.InvalidateCache();
                var mesh2 = deformer.Deform(false);
                Assert.That(mesh2, Is.Not.Null);
                Assert.That(mesh2.vertexCount, Is.EqualTo(deformer.SourceMesh.vertexCount));
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Scenario 10: Radial operations on cylinder (clothing tightening/loosening)
        // ========================================================================

        [Test]
        public void RadialShrink_ReducesCylinderRadius()
        {
            var fixture = CreateCylinderFixture("RadialShrink_ReducesCylinderRadius");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;
                var sourceVerts = deformer.SourceMesh.vertices;

                int brushIdx = deformer.AddLayer("Shrink", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();

                // Push all vertices inward (tighten clothing)
                for (int i = 0; i < vertexCount; i++)
                {
                    var radial = new Vector3(sourceVerts[i].x, 0f, sourceVerts[i].z).normalized;
                    deformer.SetDisplacement(i, -radial * 0.01f);
                }

                ReleaseRuntimeMesh(deformer);
                var mesh = deformer.Deform(false);
                var deformed = mesh.vertices;

                // Every vertex should be closer to Y axis
                for (int i = 0; i < vertexCount; i++)
                {
                    float srcRadius = new Vector2(sourceVerts[i].x, sourceVerts[i].z).magnitude;
                    float defRadius = new Vector2(deformed[i].x, deformed[i].z).magnitude;
                    Assert.That(defRadius, Is.LessThan(srcRadius + Epsilon),
                        $"Vertex {i}: deformed radius should be smaller");
                }
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void RadialExpand_OutputAsBlendShape_CorrectDelta()
        {
            var fixture = CreateCylinderFixture("RadialExpand_OutputAsBlendShape_CorrectDelta");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;
                var sourceVerts = deformer.SourceMesh.vertices;

                int brushIdx = deformer.AddLayer("Expand", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[brushIdx];
                layer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                layer.BlendShapeName = "Puff";

                for (int i = 0; i < vertexCount; i++)
                {
                    var radial = new Vector3(sourceVerts[i].x, 0f, sourceVerts[i].z).normalized;
                    deformer.SetDisplacement(i, radial * 0.02f);
                }

                ReleaseRuntimeMesh(deformer);
                var mesh = deformer.Deform(false);

                // Vertices should be unchanged (BS output)
                for (int i = 0; i < vertexCount; i++)
                {
                    AssertApproximately(sourceVerts[i], mesh.vertices[i], 2e-3f);
                }

                // BS deltas should all point outward
                int shapeIdx = mesh.blendShapeCount - 1;
                Assert.That(mesh.GetBlendShapeName(shapeIdx), Is.EqualTo("Puff"));

                var deltas = new Vector3[vertexCount];
                mesh.GetBlendShapeFrameVertices(shapeIdx, 0, deltas, null, null);

                for (int i = 0; i < vertexCount; i++)
                {
                    if (deltas[i].sqrMagnitude > Epsilon * Epsilon)
                    {
                        var radialDir = new Vector3(sourceVerts[i].x, 0f, sourceVerts[i].z).normalized;
                        float dot = Vector3.Dot(deltas[i].normalized, radialDir);
                        Assert.That(dot, Is.GreaterThan(0.9f),
                            $"Delta at vertex {i} should point outward");
                    }
                }
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Scenario 11: Gradient mask on cylinder
        // ========================================================================

        [Test]
        public void GradientMask_SmoothTransition()
        {
            // Simulates: mask that gradually transitions from protected (bottom)
            // to editable (top), like protecting a skirt hem while adjusting waist
            var fixture = CreateCylinderFixture("GradientMask_SmoothTransition");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;
                var sourceVerts = deformer.SourceMesh.vertices;
                float halfHeight = 0.2f;

                int brushIdx = deformer.AddLayer("Gradient", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[brushIdx];
                layer.EnsureVertexMaskCapacity(vertexCount);

                var displacement = new Vector3(0.02f, 0f, 0f);
                for (int i = 0; i < vertexCount; i++)
                {
                    deformer.SetDisplacement(i, displacement);
                    // Gradient mask: 0 at bottom (-halfHeight), 1 at top (+halfHeight)
                    float maskVal = Mathf.InverseLerp(-halfHeight, halfHeight, sourceVerts[i].y);
                    layer.SetVertexMask(i, maskVal);
                }

                ReleaseRuntimeMesh(deformer);
                var mesh = deformer.Deform(false);
                var deformed = mesh.vertices;

                // Find a bottom and top vertex
                int bottomIdx = -1, topIdx = -1;
                for (int i = 0; i < vertexCount; i++)
                {
                    if (sourceVerts[i].y < -halfHeight + 0.01f && bottomIdx < 0) bottomIdx = i;
                    if (sourceVerts[i].y > halfHeight - 0.01f && topIdx < 0) topIdx = i;
                }

                if (bottomIdx >= 0 && topIdx >= 0)
                {
                    float bottomDisp = (deformed[bottomIdx] - sourceVerts[bottomIdx]).magnitude;
                    float topDisp = (deformed[topIdx] - sourceVerts[topIdx]).magnitude;
                    Assert.That(topDisp, Is.GreaterThan(bottomDisp),
                        "Top (unmasked) should have more displacement than bottom (masked)");
                    Assert.That(bottomDisp, Is.LessThan(0.005f),
                        "Bottom should be nearly zero displacement");
                }
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Scenario 12: Import BlendShape then Split L/R
        // ========================================================================

        [Test]
        public void ImportBlendShape_ThenSplitLR_PerSideData()
        {
            var fixture = CreateBlendShapeCylinderFixture(
                "ImportBlendShape_ThenSplitLR_PerSideData");
            try
            {
                var deformer = fixture.Deformer;
                var sourceVerts = deformer.SourceMesh.vertices;
                int vertexCount = sourceVerts.Length;

                // Import "Expand" BlendShape
                int expandIdx = 1; // "Expand" is the second BS
                int layerIdx = deformer.ImportBlendShapeAsLayer(expandIdx);
                var layer = deformer.Layers[layerIdx];

                // Split: keep left (negative X) side
                deformer.SplitLayerByAxis(layerIdx, 0, false);

                // Verify: right side should be zeroed, left side should retain data
                for (int i = 0; i < vertexCount; i++)
                {
                    var d = layer.GetBrushDisplacement(i);
                    if (sourceVerts[i].x >= 0f)
                    {
                        AssertApproximately(Vector3.zero, d, Epsilon);
                    }
                    // Left side should have non-zero displacement (from Expand BS)
                }

                // Check some left-side vertices actually have data
                bool anyLeftNonZero = false;
                for (int i = 0; i < vertexCount; i++)
                {
                    if (sourceVerts[i].x < 0f &&
                        layer.GetBrushDisplacement(i).sqrMagnitude > Epsilon * Epsilon)
                    {
                        anyLeftNonZero = true;
                        break;
                    }
                }
                Assert.That(anyLeftNonZero, Is.True,
                    "Left side should retain BlendShape displacement data");
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Scenario 13: Many layers on high-density mesh
        // ========================================================================

        [Test]
        public void ManyLayersOnDenseMesh_AllCompose()
        {
            var fixture = CreateHighDensityFixture("ManyLayersOnDenseMesh_AllCompose",
                segments: 32, rings: 32);
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;
                var sourceVerts = deformer.SourceMesh.vertices;

                Assert.That(vertexCount, Is.GreaterThanOrEqualTo(1000));

                const int layerCount = 5;
                var expectedAccum = new Vector3[vertexCount];

                for (int n = 0; n < layerCount; n++)
                {
                    int idx = deformer.AddLayer($"Dense{n}", MeshDeformerLayerType.Brush);
                    deformer.ActiveLayerIndex = idx;
                    deformer.EnsureDisplacementCapacity();

                    float weight = 1f / (n + 1);
                    deformer.Layers[idx].Weight = weight;

                    var disp = new Vector3(0.001f * (n + 1), 0f, 0f);
                    for (int v = 0; v < vertexCount; v++)
                    {
                        deformer.SetDisplacement(v, disp);
                        expectedAccum[v] += disp * weight;
                    }
                }

                ReleaseRuntimeMesh(deformer);
                var mesh = deformer.Deform(false);
                var deformed = mesh.vertices;

                // Spot check several vertices
                for (int i = 0; i < vertexCount; i += vertexCount / 10)
                {
                    AssertApproximately(sourceVerts[i] + expectedAccum[i], deformed[i], 2e-3f);
                }
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Scenario 14: Concentric cylinders with mask + BlendShape output
        // ========================================================================

        [Test]
        public void ConcentricCylinders_MaskInner_BlendShapeOnOuter()
        {
            var fixture = CreateConcentricFixture(
                "ConcentricCylinders_MaskInner_BlendShapeOnOuter");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;

                TestMeshFactory.GetConcentricCylinderRanges(16, 8,
                    out int innerStart, out int innerEnd, out int outerStart, out int outerEnd);

                int brushIdx = deformer.AddLayer("MaskedBS", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[brushIdx];
                layer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                layer.EnsureVertexMaskCapacity(vertexCount);

                // Mask inner (body) vertices
                for (int i = innerStart; i < innerEnd; i++)
                    layer.SetVertexMask(i, 0f);
                for (int i = outerStart; i < outerEnd; i++)
                    layer.SetVertexMask(i, 1f);

                // Set displacement on ALL vertices
                var disp = new Vector3(0.01f, 0f, 0f);
                for (int i = 0; i < vertexCount; i++)
                    deformer.SetDisplacement(i, disp);

                ReleaseRuntimeMesh(deformer);
                var mesh = deformer.Deform(false);

                int shapeIdx = mesh.blendShapeCount - 1;
                var deltas = new Vector3[vertexCount];
                mesh.GetBlendShapeFrameVertices(shapeIdx, 0, deltas, null, null);

                // Inner (masked): zero delta
                for (int i = innerStart; i < innerEnd; i++)
                    AssertApproximately(Vector3.zero, deltas[i], Epsilon);

                // Outer (unmasked): full delta
                for (int i = outerStart; i < outerEnd; i++)
                    AssertApproximately(disp, deltas[i], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Scenario 15: Split Y on cylinder (upper/lower body separation)
        // ========================================================================

        [Test]
        public void SplitY_ThenFlipY_OnCylinder()
        {
            var fixture = CreateCylinderFixture("SplitY_ThenFlipY_OnCylinder");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;
                var sourceVerts = deformer.SourceMesh.vertices;

                int brushIdx = deformer.AddLayer("YOps", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[brushIdx];
                var disp = new Vector3(0.01f, 0f, 0f);
                for (int i = 0; i < vertexCount; i++)
                    deformer.SetDisplacement(i, disp);

                // Split: keep upper half (positive Y)
                deformer.SplitLayerByAxis(brushIdx, 1, true);

                // Lower half should be zeroed
                for (int i = 0; i < vertexCount; i++)
                {
                    if (sourceVerts[i].y < 0f)
                        AssertApproximately(Vector3.zero, layer.GetBrushDisplacement(i), Epsilon);
                }

                // Flip Y
                deformer.FlipLayerByAxis(brushIdx, 1);

                // After flip, lower half should have displacement, upper should be zeroed
                bool anyLowerNonZero = false;
                for (int i = 0; i < vertexCount; i++)
                {
                    if (sourceVerts[i].y < -0.05f &&
                        layer.GetBrushDisplacement(i).sqrMagnitude > Epsilon * Epsilon)
                    {
                        anyLowerNonZero = true;
                        break;
                    }
                }
                Assert.That(anyLowerNonZero, Is.True,
                    "After Flip Y, lower half should have data from upper half");
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Scenario 16: Copy layer between concentric cylinders
        // ========================================================================

        [Test]
        public void DuplicateLayer_OnConcentricMesh_IndependentEditing()
        {
            var fixture = CreateConcentricFixture(
                "DuplicateLayer_OnConcentricMesh_IndependentEditing");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;

                TestMeshFactory.GetConcentricCylinderRanges(16, 8,
                    out int innerStart, out int innerEnd, out int outerStart, out int outerEnd);

                int originalIdx = deformer.AddLayer("Original", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = originalIdx;
                deformer.EnsureDisplacementCapacity();

                // Edit outer vertices only
                var disp = new Vector3(0f, 0.01f, 0f);
                for (int i = outerStart; i < outerEnd; i++)
                    deformer.SetDisplacement(i, disp);

                // Duplicate
                int dupIdx = deformer.DuplicateLayer(originalIdx);
                var dupLayer = deformer.Layers[dupIdx];

                // Modify duplicate: clear outer, set inner
                for (int i = outerStart; i < outerEnd; i++)
                    dupLayer.SetBrushDisplacement(i, Vector3.zero);
                for (int i = innerStart; i < innerEnd; i++)
                    dupLayer.SetBrushDisplacement(i, disp * 2f);

                // Original should be unaffected
                var origLayer = deformer.Layers[originalIdx];
                for (int i = outerStart; i < Mathf.Min(outerStart + 5, outerEnd); i++)
                    AssertApproximately(disp, origLayer.GetBrushDisplacement(i), Epsilon);
                for (int i = innerStart; i < Mathf.Min(innerStart + 5, innerEnd); i++)
                    AssertApproximately(Vector3.zero, origLayer.GetBrushDisplacement(i), Epsilon);
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Scenario 17: Humanoid symmetric with multiple layer types
        // ========================================================================

        [Test]
        public void Humanoid_LatticePlusBrush_SymmetricSplitFlip()
        {
            var fixture = CreateHumanoidFixture(
                "Humanoid_LatticePlusBrush_SymmetricSplitFlip");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;
                var sourceVerts = deformer.SourceMesh.vertices;

                // Lattice layer: move everything slightly
                var latticeSettings = deformer.Layers[0].Settings;
                for (int i = 0; i < latticeSettings.ControlPointCount; i++)
                {
                    latticeSettings.SetControlPointLocal(i,
                        latticeSettings.GetControlPointLocal(i) + new Vector3(0f, 0.005f, 0f));
                }

                // Brush layer: edit left arm only
                int brushIdx = deformer.AddLayer("Left Arm", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();

                for (int i = 0; i < vertexCount; i++)
                {
                    if (sourceVerts[i].x < -0.1f)
                        deformer.SetDisplacement(i, new Vector3(0f, 0.01f, 0f));
                }

                // Split L (keep negative X)
                deformer.SplitLayerByAxis(brushIdx, 0, false);

                // Verify right side is zero in brush layer
                for (int i = 0; i < vertexCount; i++)
                {
                    if (sourceVerts[i].x > 0.05f)
                    {
                        AssertApproximately(Vector3.zero,
                            deformer.Layers[brushIdx].GetBrushDisplacement(i), Epsilon);
                    }
                }

                // Deform should work with both layers
                ReleaseRuntimeMesh(deformer);
                var mesh = deformer.Deform(false);
                Assert.That(mesh, Is.Not.Null);
                Assert.That(mesh.vertexCount, Is.EqualTo(vertexCount));
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Helpers
        // ========================================================================

        private static TestFixture CreateCylinderFixture(string name)
        {
            return CreateFixtureFromMesh(name,
                TestMeshFactory.CreateCylinder(16, 8, 0.05f, 0.4f));
        }

        private static TestFixture CreateConcentricFixture(string name)
        {
            return CreateFixtureFromMesh(name,
                TestMeshFactory.CreateConcentricCylinders(16, 8, 0.04f, 0.055f, 0.4f));
        }

        private static TestFixture CreateHumanoidFixture(string name)
        {
            return CreateFixtureFromMesh(name,
                TestMeshFactory.CreateSymmetricHumanoid(8, 6));
        }

        private static TestFixture CreateMultiIslandFixture(string name)
        {
            return CreateFixtureFromMesh(name,
                TestMeshFactory.CreateMultiIslandMesh(3, 8, 4));
        }

        private static TestFixture CreateBlendShapeCylinderFixture(string name)
        {
            return CreateFixtureFromMesh(name,
                TestMeshFactory.CreateCylinderWithBlendShapes(16, 8, 0.05f, 0.4f));
        }

        private static TestFixture CreateHighDensityFixture(string name,
            int segments = 32, int rings = 64)
        {
            return CreateFixtureFromMesh(name,
                TestMeshFactory.CreateCylinder(segments, rings, 0.05f, 0.4f));
        }

        private static TestFixture CreateFixtureFromMesh(string name, Mesh sourceMesh)
        {
            var root = new GameObject(name);
            var filter = root.AddComponent<MeshFilter>();
            root.AddComponent<MeshRenderer>();
            filter.sharedMesh = sourceMesh;

            var deformer = root.AddComponent<LatticeDeformer>();
            deformer.Reset();
            var warmupMesh = deformer.Deform(false);
            Assert.That(warmupMesh, Is.Not.Null);

            return new TestFixture(root, sourceMesh, deformer);
        }

        private static List<HashSet<int>> BuildAdjacencyFromMesh(Mesh mesh)
        {
            int vertexCount = mesh.vertexCount;
            var adjacency = new List<HashSet<int>>(vertexCount);
            for (int i = 0; i < vertexCount; i++)
            {
                adjacency.Add(new HashSet<int>());
            }

            var triangles = mesh.triangles;
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int a = triangles[t];
                int b = triangles[t + 1];
                int c = triangles[t + 2];
                adjacency[a].Add(b); adjacency[a].Add(c);
                adjacency[b].Add(a); adjacency[b].Add(c);
                adjacency[c].Add(a); adjacency[c].Add(b);
            }

            return adjacency;
        }

        private static int FindNearestVertex(Vector3[] vertices, Vector3 target, float maxDist)
        {
            float bestDistSq = maxDist * maxDist;
            int bestIdx = -1;
            for (int i = 0; i < vertices.Length; i++)
            {
                float distSq = (vertices[i] - target).sqrMagnitude;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        private static void ReleaseRuntimeMesh(LatticeDeformer deformer)
        {
            var field = typeof(LatticeDeformer).GetField("_runtimeMesh", s_privateInstance);
            Assert.That(field, Is.Not.Null, "Private field not found: _runtimeMesh");
            var mesh = field.GetValue(deformer) as Mesh;
            if (mesh != null)
            {
                Object.DestroyImmediate(mesh);
            }
            field.SetValue(deformer, null);
        }

        private static void AssertApproximately(Vector3 expected, Vector3 actual,
            float tolerance = Epsilon)
        {
            float toleranceSq = tolerance * tolerance;
            Assert.That((expected - actual).sqrMagnitude, Is.LessThanOrEqualTo(toleranceSq),
                $"Expected {expected} but got {actual}");
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
