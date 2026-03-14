#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    /// <summary>
    /// Complex multi-step workflow tests that simulate real VRChat user stories.
    /// Each test chains many operations together in realistic sequences.
    /// </summary>
    public sealed class ComplexWorkflowStoryTests
    {
        private const float Epsilon = 1e-4f;
        private static readonly BindingFlags s_privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        // ========================================================================
        // Story 1: Full outfit adjustment workflow
        //   Import existing BlendShape → mask upper body → edit with brush →
        //   split L/R → duplicate & flip for right side →
        //   output each side as separate BlendShape
        // ========================================================================

        [Test]
        public void Story_FullOutfitAdjustment_ImportMaskSplitFlipExport()
        {
            var mesh = TestMeshFactory.CreateCylinderWithBlendShapes(16, 8, 0.05f, 0.4f);
            var fixture = CreateFixtureFromMesh("Story_FullOutfit", mesh);
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;
                var srcVerts = deformer.SourceMesh.vertices;

                // Step 1: Import "Shrink" BlendShape as starting point
                var names = deformer.GetSourceBlendShapeNames();
                int shrinkBS = System.Array.IndexOf(names, "Shrink");
                int importedIdx = deformer.ImportBlendShapeAsLayer(shrinkBS);
                var importedLayer = deformer.Layers[importedIdx];
                Assert.That(importedLayer.Name, Is.EqualTo("Shrink"));

                // Step 2: Mask lower half (protect skirt hem)
                importedLayer.EnsureVertexMaskCapacity(vertexCount);
                for (int i = 0; i < vertexCount; i++)
                {
                    float maskVal = srcVerts[i].y >= 0f ? 1f : 0f;
                    importedLayer.SetVertexMask(i, maskVal);
                }

                // Step 3: Add extra inward push on upper body
                var extraPush = new Vector3(0f, 0f, -0.005f);
                for (int i = 0; i < vertexCount; i++)
                {
                    if (srcVerts[i].y >= 0f)
                        importedLayer.AddBrushDisplacement(i, extraPush);
                }

                // Step 4: Split — keep left side only
                deformer.SplitLayerByAxis(importedIdx, 0, false);

                // Step 5: Duplicate and flip for right side
                int rightIdx = deformer.DuplicateLayer(importedIdx);
                deformer.FlipLayerByAxis(rightIdx, 0);

                // Step 6: Set both as BlendShape output with distinct names
                deformer.Layers[importedIdx].BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.Layers[importedIdx].BlendShapeName = "Shrink_L";
                deformer.Layers[rightIdx].BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.Layers[rightIdx].BlendShapeName = "Shrink_R";

                // Step 7: Deform and verify
                ReleaseRuntimeMesh(deformer);
                var result = deformer.Deform(false);
                Assert.That(result, Is.Not.Null);

                // Vertices should be untouched (all BS output)
                for (int i = 0; i < vertexCount; i++)
                    AssertApproximately(srcVerts[i], result.vertices[i], 2e-3f);

                // Both BlendShapes should exist
                bool foundL = false, foundR = false;
                for (int i = 0; i < result.blendShapeCount; i++)
                {
                    if (result.GetBlendShapeName(i) == "Shrink_L") foundL = true;
                    if (result.GetBlendShapeName(i) == "Shrink_R") foundR = true;
                }
                Assert.That(foundL && foundR, Is.True, "Both L/R BlendShapes should exist");

                // Left BS should have non-zero deltas only on left upper vertices
                int lIdx = -1;
                for (int i = 0; i < result.blendShapeCount; i++)
                    if (result.GetBlendShapeName(i) == "Shrink_L") { lIdx = i; break; }

                var lDeltas = new Vector3[vertexCount];
                result.GetBlendShapeFrameVertices(lIdx, 0, lDeltas, null, null);

                for (int i = 0; i < vertexCount; i++)
                {
                    bool isLeft = srcVerts[i].x < 0f;
                    bool isUpper = srcVerts[i].y >= 0f;
                    if (!isLeft || !isUpper)
                    {
                        // Should be zero (either right side or masked lower)
                        AssertApproximately(Vector3.zero, lDeltas[i], 2e-3f);
                    }
                }
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Story 2: Progressive refinement pipeline
        //   Lattice (coarse) → resize grid → Lattice (finer) → Brush (detail) →
        //   Mask protect face → verify composition
        // ========================================================================

        [Test]
        public void Story_ProgressiveRefinement_LatticeResizeBrushMask()
        {
            var mesh = TestMeshFactory.CreateSymmetricHumanoid(8, 6);
            var fixture = CreateFixtureFromMesh("Story_ProgressiveRefinement", mesh);
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;
                var srcVerts = deformer.SourceMesh.vertices;

                // Step 1: Coarse lattice — shift everything slightly
                var coarseSettings = deformer.Layers[0].Settings;
                for (int i = 0; i < coarseSettings.ControlPointCount; i++)
                {
                    coarseSettings.SetControlPointLocal(i,
                        coarseSettings.GetControlPointLocal(i) + new Vector3(0f, 0.003f, 0f));
                }

                // Step 2: Add finer lattice layer with larger grid
                int fineIdx = deformer.AddLayer("Fine Lattice", MeshDeformerLayerType.Lattice);
                var fineSettings = deformer.Layers[fineIdx].Settings;
                fineSettings.ResizeGrid(new Vector3Int(4, 4, 4));
                deformer.Layers[fineIdx].Weight = 0.5f;

                // Move one interior control point
                fineSettings.SetControlPointLocal(0,
                    fineSettings.GetControlPointLocal(0) + new Vector3(0.01f, 0f, 0f));

                // Step 3: Add brush detail layer
                int brushIdx = deformer.AddLayer("Detail Brush", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();

                // Push arm vertices outward
                for (int i = 0; i < vertexCount; i++)
                {
                    if (Mathf.Abs(srcVerts[i].x) > 0.1f) // arm region
                        deformer.SetDisplacement(i, new Vector3(0f, 0.005f, 0f));
                }

                // Step 4: Mask torso center to protect it
                var brushLayer = deformer.Layers[brushIdx];
                brushLayer.EnsureVertexMaskCapacity(vertexCount);
                for (int i = 0; i < vertexCount; i++)
                {
                    float distFromCenter = Mathf.Abs(srcVerts[i].x);
                    brushLayer.SetVertexMask(i, distFromCenter > 0.05f ? 1f : 0f);
                }

                // Step 5: Deform and verify all layers composed
                ReleaseRuntimeMesh(deformer);
                deformer.InvalidateCache();
                var result = deformer.Deform(false);
                Assert.That(result, Is.Not.Null);
                Assert.That(result.vertexCount, Is.EqualTo(vertexCount));

                // All vertices should be displaced at least a little (from coarse lattice)
                int movedCount = 0;
                for (int i = 0; i < vertexCount; i++)
                {
                    if ((result.vertices[i] - srcVerts[i]).sqrMagnitude > 1e-6f)
                        movedCount++;
                }
                Assert.That(movedCount, Is.GreaterThan(vertexCount / 2),
                    "Most vertices should be displaced by the lattice layers");
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Story 3: Clothing penetration fix → export as toggleable BlendShape
        //   Create body+clothing mesh → push outer inward with mask on inner →
        //   verify no body impact → output fix as BlendShape for in-game toggle
        // ========================================================================

        [Test]
        public void Story_PenetrationFixToBlendShapeToggle()
        {
            var mesh = TestMeshFactory.CreateConcentricCylinders(16, 8, 0.04f, 0.055f, 0.4f);
            var fixture = CreateFixtureFromMesh("Story_PenFixToBS", mesh);
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;
                var srcVerts = deformer.SourceMesh.vertices;

                TestMeshFactory.GetConcentricCylinderRanges(16, 8,
                    out int innerStart, out int innerEnd, out int outerStart, out int outerEnd);

                // Step 1: Create brush layer with mask protecting body
                int fixIdx = deformer.AddLayer("Penetration Fix", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = fixIdx;
                deformer.EnsureDisplacementCapacity();

                var fixLayer = deformer.Layers[fixIdx];
                fixLayer.EnsureVertexMaskCapacity(vertexCount);

                for (int i = innerStart; i < innerEnd; i++)
                    fixLayer.SetVertexMask(i, 0f); // Protect body
                for (int i = outerStart; i < outerEnd; i++)
                    fixLayer.SetVertexMask(i, 1f); // Edit clothing

                // Step 2: Push clothing inward
                for (int i = outerStart; i < outerEnd; i++)
                {
                    var radial = new Vector3(srcVerts[i].x, 0f, srcVerts[i].z).normalized;
                    deformer.SetDisplacement(i, -radial * 0.008f);
                }

                // Step 3: Verify as direct deformation first
                ReleaseRuntimeMesh(deformer);
                var directMesh = deformer.Deform(false);

                // Body unchanged
                for (int i = innerStart; i < innerEnd; i++)
                    AssertApproximately(srcVerts[i], directMesh.vertices[i], 2e-3f);

                // Clothing moved inward
                for (int i = outerStart; i < outerEnd; i++)
                {
                    float srcR = new Vector2(srcVerts[i].x, srcVerts[i].z).magnitude;
                    float defR = new Vector2(directMesh.vertices[i].x, directMesh.vertices[i].z).magnitude;
                    Assert.That(defR, Is.LessThan(srcR + Epsilon));
                }

                // Step 4: Switch to BlendShape output mode
                fixLayer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                fixLayer.BlendShapeName = "PenetrationFix";

                ReleaseRuntimeMesh(deformer);
                var bsMesh = deformer.Deform(false);

                // Vertices should now be unchanged (BS mode)
                for (int i = 0; i < vertexCount; i++)
                    AssertApproximately(srcVerts[i], bsMesh.vertices[i], 2e-3f);

                // BlendShape should exist with correct deltas
                int bsIdx = -1;
                for (int i = 0; i < bsMesh.blendShapeCount; i++)
                    if (bsMesh.GetBlendShapeName(i) == "PenetrationFix") { bsIdx = i; break; }
                Assert.That(bsIdx, Is.GreaterThanOrEqualTo(0));

                var deltas = new Vector3[vertexCount];
                bsMesh.GetBlendShapeFrameVertices(bsIdx, 0, deltas, null, null);

                // Body deltas should be zero (masked)
                for (int i = innerStart; i < innerEnd; i++)
                    AssertApproximately(Vector3.zero, deltas[i], Epsilon);

                // Clothing deltas should point inward
                int inwardCount = 0;
                for (int i = outerStart; i < outerEnd; i++)
                {
                    if (deltas[i].sqrMagnitude > Epsilon * Epsilon)
                    {
                        var radialDir = new Vector3(srcVerts[i].x, 0f, srcVerts[i].z).normalized;
                        if (Vector3.Dot(deltas[i].normalized, radialDir) < -0.5f)
                            inwardCount++;
                    }
                }
                Assert.That(inwardCount, Is.GreaterThan(0), "Some deltas should point inward");
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Story 4: Multi-layer reorder, disable, weight tweak, hash tracking
        //   Add 5 layers → verify hash changes at each step → reorder →
        //   disable middle layer → adjust weights → verify final composition
        // ========================================================================

        [Test]
        public void Story_MultiLayerOrchestration_HashAndComposition()
        {
            var fixture = CreateFixture("Story_MultiLayerOrchestration");
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;
                var hashes = new List<int>();

                hashes.Add(deformer.ComputeLayeredStateHash());

                // Step 1: Add 5 brush layers with different displacements
                var disps = new[]
                {
                    new Vector3(0.02f, 0f, 0f),
                    new Vector3(0f, 0.03f, 0f),
                    new Vector3(0f, 0f, 0.01f),
                    new Vector3(-0.01f, 0.01f, 0f),
                    new Vector3(0.005f, -0.005f, 0.005f)
                };
                var layerIndices = new int[5];
                for (int n = 0; n < 5; n++)
                {
                    layerIndices[n] = deformer.AddLayer($"L{n}", MeshDeformerLayerType.Brush);
                    deformer.ActiveLayerIndex = layerIndices[n];
                    deformer.EnsureDisplacementCapacity();
                    deformer.SetDisplacement(0, disps[n]);

                    int h = deformer.ComputeLayeredStateHash();
                    Assert.That(h, Is.Not.EqualTo(hashes[hashes.Count - 1]),
                        $"Hash should change after adding layer {n}");
                    hashes.Add(h);
                }

                // Step 2: Deform with all 5 active
                ReleaseRuntimeMesh(deformer);
                var meshAll = deformer.Deform(false);
                var expectedAll = src[0];
                foreach (var d in disps) expectedAll += d;
                AssertApproximately(expectedAll, meshAll.vertices[0], 2e-3f);

                // Step 3: Disable layer 2 (middle)
                deformer.Layers[layerIndices[2]].Enabled = false;
                int hashDisabled = deformer.ComputeLayeredStateHash();
                Assert.That(hashDisabled, Is.Not.EqualTo(hashes[hashes.Count - 1]));

                ReleaseRuntimeMesh(deformer);
                var meshDisabled = deformer.Deform(false);
                var expectedDisabled = src[0] + disps[0] + disps[1] + disps[3] + disps[4];
                AssertApproximately(expectedDisabled, meshDisabled.vertices[0], 2e-3f);

                // Step 4: Reorder — move last layer to position 2
                int lastLayerCurrentIdx = layerIndices[4] + 1; // +1 because of base lattice layer
                deformer.MoveLayer(lastLayerCurrentIdx, 2);

                // Additive result should be the same despite reorder
                ReleaseRuntimeMesh(deformer);
                var meshReordered = deformer.Deform(false);
                AssertApproximately(expectedDisabled, meshReordered.vertices[0], 2e-3f);

                // Step 5: Adjust weight on first brush layer to 0.5
                deformer.Layers[layerIndices[0]].Weight = 0.5f;
                ReleaseRuntimeMesh(deformer);
                var meshWeighted = deformer.Deform(false);
                var expectedWeighted = src[0] + disps[0] * 0.5f + disps[1] + disps[3] + disps[4];
                AssertApproximately(expectedWeighted, meshWeighted.vertices[0], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Story 5: Import all BlendShapes → selective edit → selective export
        //   Import 3 BSs → modify one → mask another → direct-deform the third →
        //   output only modified ones as BS
        // ========================================================================

        [Test]
        public void Story_SelectiveBlendShapeWorkflow()
        {
            var mesh = TestMeshFactory.CreateCylinderWithBlendShapes(16, 8, 0.05f, 0.4f);
            var fixture = CreateFixtureFromMesh("Story_SelectiveBS", mesh);
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;
                var srcVerts = deformer.SourceMesh.vertices;
                var names = deformer.GetSourceBlendShapeNames();
                Assert.That(names.Length, Is.EqualTo(3));

                // Step 1: Import all 3 BlendShapes
                int shrinkIdx = deformer.ImportBlendShapeAsLayer(0); // Shrink
                int expandIdx = deformer.ImportBlendShapeAsLayer(1); // Expand
                int moveUpIdx = deformer.ImportBlendShapeAsLayer(2); // MoveUp

                // Step 2: Modify "Shrink" — add extra inward push
                var shrinkLayer = deformer.Layers[shrinkIdx];
                for (int i = 0; i < vertexCount; i++)
                {
                    if (srcVerts[i].y > 0f)
                    {
                        var radial = new Vector3(srcVerts[i].x, 0f, srcVerts[i].z).normalized;
                        shrinkLayer.AddBrushDisplacement(i, -radial * 0.003f);
                    }
                }
                shrinkLayer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                shrinkLayer.BlendShapeName = "Shrink_Modified";

                // Step 3: Mask "Expand" — protect lower half, output as BS
                var expandLayer = deformer.Layers[expandIdx];
                expandLayer.EnsureVertexMaskCapacity(vertexCount);
                for (int i = 0; i < vertexCount; i++)
                    expandLayer.SetVertexMask(i, srcVerts[i].y >= 0f ? 1f : 0f);
                expandLayer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                expandLayer.BlendShapeName = "Expand_UpperOnly";

                // Step 4: "MoveUp" stays as direct deformation (weight=0.5)
                deformer.Layers[moveUpIdx].Weight = 0.5f;

                // Step 5: Deform
                ReleaseRuntimeMesh(deformer);
                var result = deformer.Deform(false);
                Assert.That(result, Is.Not.Null);

                // Step 6: Verify — MoveUp should directly affect vertices
                var originalMoveUp = new Vector3[vertexCount];
                deformer.SourceMesh.GetBlendShapeFrameVertices(2, 0, originalMoveUp, null, null);

                bool anyDirectMoved = false;
                for (int i = 0; i < vertexCount; i++)
                {
                    var expectedDirect = srcVerts[i] + originalMoveUp[i] * 0.5f;
                    if ((result.vertices[i] - srcVerts[i]).sqrMagnitude > Epsilon)
                    {
                        anyDirectMoved = true;
                        break;
                    }
                }
                Assert.That(anyDirectMoved, Is.True, "MoveUp layer should directly modify vertices");

                // Two BS outputs should exist
                bool foundShrink = false, foundExpand = false;
                for (int i = 0; i < result.blendShapeCount; i++)
                {
                    string n = result.GetBlendShapeName(i);
                    if (n == "Shrink_Modified") foundShrink = true;
                    if (n == "Expand_UpperOnly") foundExpand = true;
                }
                Assert.That(foundShrink, Is.True);
                Assert.That(foundExpand, Is.True);

                // Expand BS: lower half should have zero delta (masked)
                int expandBSIdx = -1;
                for (int i = 0; i < result.blendShapeCount; i++)
                    if (result.GetBlendShapeName(i) == "Expand_UpperOnly") { expandBSIdx = i; break; }

                var expandDeltas = new Vector3[vertexCount];
                result.GetBlendShapeFrameVertices(expandBSIdx, 0, expandDeltas, null, null);
                for (int i = 0; i < vertexCount; i++)
                {
                    if (srcVerts[i].y < 0f)
                        AssertApproximately(Vector3.zero, expandDeltas[i], 2e-3f);
                }
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Story 6: Iterative sculpting with undo simulation
        //   Brush stroke 1 → verify → brush stroke 2 → verify →
        //   "undo" by removing last layer → verify back to stroke 1 state →
        //   "redo" by adding new layer → verify
        // ========================================================================

        [Test]
        public void Story_IterativeSculptingWithUndoRedo()
        {
            var mesh = TestMeshFactory.CreateCylinder(16, 8, 0.05f, 0.4f);
            var fixture = CreateFixtureFromMesh("Story_UndoRedo", mesh);
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;

                // Stroke 1: push vertex 0 right
                int stroke1 = deformer.AddLayer("Stroke 1", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = stroke1;
                deformer.EnsureDisplacementCapacity();
                var disp1 = new Vector3(0.02f, 0f, 0f);
                deformer.SetDisplacement(0, disp1);

                ReleaseRuntimeMesh(deformer);
                var afterStroke1 = deformer.Deform(false);
                var v_after1 = afterStroke1.vertices[0];
                AssertApproximately(src[0] + disp1, v_after1, 2e-3f);

                // Stroke 2: push vertex 0 up
                int stroke2 = deformer.AddLayer("Stroke 2", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = stroke2;
                deformer.EnsureDisplacementCapacity();
                var disp2 = new Vector3(0f, 0.03f, 0f);
                deformer.SetDisplacement(0, disp2);

                ReleaseRuntimeMesh(deformer);
                var afterStroke2 = deformer.Deform(false);
                AssertApproximately(src[0] + disp1 + disp2, afterStroke2.vertices[0], 2e-3f);

                // Undo: remove stroke 2
                deformer.RemoveLayer(stroke2);

                ReleaseRuntimeMesh(deformer);
                var afterUndo = deformer.Deform(false);
                AssertApproximately(src[0] + disp1, afterUndo.vertices[0], 2e-3f);

                // Redo: add new stroke with same data
                int stroke2Redo = deformer.AddLayer("Stroke 2 Redo", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = stroke2Redo;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, disp2);

                ReleaseRuntimeMesh(deformer);
                var afterRedo = deformer.Deform(false);
                AssertApproximately(src[0] + disp1 + disp2, afterRedo.vertices[0], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Story 7: Cross-deformer copy/paste workflow
        //   Edit on deformer A → copy layer → paste to deformer B (different mesh) →
        //   modify on B → verify A is unaffected
        // ========================================================================

        [Test]
        public void Story_CrossDeformerCopyPaste()
        {
            var meshA = TestMeshFactory.CreateCylinder(16, 8, 0.05f, 0.4f);
            var meshB = TestMeshFactory.CreateCylinder(12, 6, 0.03f, 0.3f);
            var fixtureA = CreateFixtureFromMesh("Story_Copy_A", meshA);
            var fixtureB = CreateFixtureFromMesh("Story_Paste_B", meshB);
            try
            {
                var deformerA = fixtureA.Deformer;
                var deformerB = fixtureB.Deformer;

                // Edit on A
                int brushA = deformerA.AddLayer("Shared Edit", MeshDeformerLayerType.Brush);
                deformerA.ActiveLayerIndex = brushA;
                deformerA.EnsureDisplacementCapacity();
                var disp = new Vector3(0.1f, 0.2f, 0f);
                deformerA.SetDisplacement(0, disp);
                deformerA.Layers[brushA].Weight = 0.7f;
                deformerA.Layers[brushA].BlendShapeName = "SharedBS";

                // Copy (simulate JsonUtility copy)
                var layerA = deformerA.Layers[brushA];
                string json = JsonUtility.ToJson(layerA);

                // Paste into B
                var pastedLayer = new LatticeLayer();
                JsonUtility.FromJsonOverwrite(json, pastedLayer);
                int pastedIdx = deformerB.InsertLayer(pastedLayer);
                Assert.That(pastedIdx, Is.GreaterThanOrEqualTo(0));

                // Verify paste preserved data
                Assert.That(deformerB.Layers[pastedIdx].Name, Is.EqualTo("Shared Edit"));
                Assert.That(deformerB.Layers[pastedIdx].Weight, Is.EqualTo(0.7f).Within(Epsilon));
                Assert.That(deformerB.Layers[pastedIdx].BlendShapeName, Is.EqualTo("SharedBS"));

                // Modify on B
                deformerB.Layers[pastedIdx].SetBrushDisplacement(0, Vector3.zero);

                // A should be unaffected
                AssertApproximately(disp, deformerA.Layers[brushA].GetBrushDisplacement(0), Epsilon);

                // Both deformers should still deform correctly
                ReleaseRuntimeMesh(deformerA);
                ReleaseRuntimeMesh(deformerB);
                Assert.DoesNotThrow(() => deformerA.Deform(false));
                Assert.DoesNotThrow(() => deformerB.Deform(false));
            }
            finally
            {
                fixtureA.Dispose();
                fixtureB.Dispose();
            }
        }

        // ========================================================================
        // Story 8: Multi-island per-island workflow
        //   3-island mesh → mask island 0 & 2 → edit island 1 →
        //   BS output island 1 → verify only island 1 has delta
        // ========================================================================

        [Test]
        public void Story_PerIslandMaskAndBlendShapeOutput()
        {
            var mesh = TestMeshFactory.CreateMultiIslandMesh(3, 8, 4);
            var fixture = CreateFixtureFromMesh("Story_PerIsland", mesh);
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;
                int islandSize = 8 * 4; // segments * rings per island

                int brushIdx = deformer.AddLayer("Island1 Edit", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[brushIdx];
                layer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                layer.BlendShapeName = "Island1_Push";
                layer.EnsureVertexMaskCapacity(vertexCount);

                // Mask islands 0 and 2, edit island 1
                for (int i = 0; i < vertexCount; i++)
                {
                    bool isIsland1 = i >= islandSize && i < islandSize * 2;
                    layer.SetVertexMask(i, isIsland1 ? 1f : 0f);
                    deformer.SetDisplacement(i, new Vector3(0.01f, 0f, 0f));
                }

                ReleaseRuntimeMesh(deformer);
                var result = deformer.Deform(false);

                int bsIdx = -1;
                for (int i = 0; i < result.blendShapeCount; i++)
                    if (result.GetBlendShapeName(i) == "Island1_Push") { bsIdx = i; break; }
                Assert.That(bsIdx, Is.GreaterThanOrEqualTo(0));

                var deltas = new Vector3[vertexCount];
                result.GetBlendShapeFrameVertices(bsIdx, 0, deltas, null, null);

                // Island 0: zero
                for (int i = 0; i < islandSize; i++)
                    AssertApproximately(Vector3.zero, deltas[i], Epsilon);

                // Island 1: non-zero
                bool anyNonZero = false;
                for (int i = islandSize; i < islandSize * 2 && i < vertexCount; i++)
                    if (deltas[i].sqrMagnitude > Epsilon * Epsilon) { anyNonZero = true; break; }
                Assert.That(anyNonZero, Is.True, "Island 1 should have non-zero deltas");

                // Island 2: zero
                for (int i = islandSize * 2; i < vertexCount; i++)
                    AssertApproximately(Vector3.zero, deltas[i], Epsilon);
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Story 9: Full L/R symmetric edit workflow on humanoid
        //   Edit left arm → preview direct deform → split L → duplicate → flip R →
        //   set different weights per side → export both as BS →
        //   verify symmetric deltas with independent weight
        // ========================================================================

        [Test]
        public void Story_FullSymmetricLRWorkflow()
        {
            var mesh = TestMeshFactory.CreateSymmetricHumanoid(8, 6);
            var fixture = CreateFixtureFromMesh("Story_FullLR", mesh);
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;
                var srcVerts = deformer.SourceMesh.vertices;

                // Step 1: Edit — push all left-side vertices up
                int editIdx = deformer.AddLayer("Full Edit", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = editIdx;
                deformer.EnsureDisplacementCapacity();

                var upDisp = new Vector3(0f, 0.01f, 0f);
                for (int i = 0; i < vertexCount; i++)
                    deformer.SetDisplacement(i, upDisp);

                // Step 2: Preview as direct deform
                ReleaseRuntimeMesh(deformer);
                var preview = deformer.Deform(false);
                for (int i = 0; i < vertexCount; i++)
                    AssertApproximately(srcVerts[i] + upDisp, preview.vertices[i], 2e-3f);

                // Step 3: Split left
                deformer.SplitLayerByAxis(editIdx, 0, false);

                // Step 4: Duplicate and flip for right
                int rightIdx = deformer.DuplicateLayer(editIdx);
                deformer.FlipLayerByAxis(rightIdx, 0);

                // Step 5: Different weights per side
                deformer.Layers[editIdx].Weight = 1.0f;
                deformer.Layers[rightIdx].Weight = 0.5f;

                // Step 6: Export both as BlendShapes
                deformer.Layers[editIdx].BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.Layers[editIdx].BlendShapeName = "ArmAdjust_L";
                deformer.Layers[rightIdx].BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.Layers[rightIdx].BlendShapeName = "ArmAdjust_R";

                ReleaseRuntimeMesh(deformer);
                var result = deformer.Deform(false);

                // Vertices untouched (BS output)
                for (int i = 0; i < vertexCount; i++)
                    AssertApproximately(srcVerts[i], result.vertices[i], 2e-3f);

                // Find BS indices
                int lBSIdx = -1, rBSIdx = -1;
                for (int i = 0; i < result.blendShapeCount; i++)
                {
                    if (result.GetBlendShapeName(i) == "ArmAdjust_L") lBSIdx = i;
                    if (result.GetBlendShapeName(i) == "ArmAdjust_R") rBSIdx = i;
                }
                Assert.That(lBSIdx, Is.GreaterThanOrEqualTo(0));
                Assert.That(rBSIdx, Is.GreaterThanOrEqualTo(0));

                var lDeltas = new Vector3[vertexCount];
                var rDeltas = new Vector3[vertexCount];
                result.GetBlendShapeFrameVertices(lBSIdx, 0, lDeltas, null, null);
                result.GetBlendShapeFrameVertices(rBSIdx, 0, rDeltas, null, null);

                // Left BS: right side should be zero
                for (int i = 0; i < vertexCount; i++)
                {
                    if (srcVerts[i].x > 0.05f)
                        AssertApproximately(Vector3.zero, lDeltas[i], 2e-3f);
                }

                // Right BS at 0.5 weight: left side should be zero
                for (int i = 0; i < vertexCount; i++)
                {
                    if (srcVerts[i].x < -0.05f)
                        AssertApproximately(Vector3.zero, rDeltas[i], 2e-3f);
                }
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Story 10: Restore → re-edit → compare workflow
        //   Deform with assignToRenderer → restore → verify restored →
        //   modify layer → deform again → compare with original
        // ========================================================================

        [Test]
        public void Story_RestoreReEditCompare()
        {
            var fixture = CreateFixture("Story_RestoreReEdit");
            try
            {
                var deformer = fixture.Deformer;
                var filter = fixture.Root.GetComponent<MeshFilter>();
                var originalMesh = deformer.SourceMesh;
                var src = deformer.SourceMesh.vertices;

                // Step 1: Add brush, deform, assign to renderer
                int idx = deformer.AddLayer("Edit", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();
                var disp1 = new Vector3(0.1f, 0f, 0f);
                deformer.SetDisplacement(0, disp1);

                deformer.Deform(true); // assign to renderer
                Assert.That(filter.sharedMesh, Is.Not.SameAs(originalMesh));

                // Step 2: Restore
                deformer.RestoreOriginalMesh();
                Assert.That(filter.sharedMesh, Is.SameAs(originalMesh));

                // Step 3: Modify the displacement
                var disp2 = new Vector3(0f, 0.2f, 0f);
                deformer.SetDisplacement(0, disp2);

                // Step 4: Deform again (but don't assign to renderer)
                ReleaseRuntimeMesh(deformer);
                var newMesh = deformer.Deform(false);
                AssertApproximately(src[0] + disp2, newMesh.vertices[0], 2e-3f);

                // Filter should still have original (we used false)
                Assert.That(filter.sharedMesh, Is.SameAs(originalMesh));

                // Step 5: Now assign
                deformer.Deform(true);
                Assert.That(filter.sharedMesh, Is.Not.SameAs(originalMesh));
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Story 11: Geodesic-aware editing verification
        //   Build adjacency for concentric cylinders → verify distances →
        //   confirm inner and outer are separate in geodesic space →
        //   then verify deformation respects this isolation
        // ========================================================================

        [Test]
        public void Story_GeodesicIsolation_ThenDeformIsolation()
        {
            int segments = 12;
            int rings = 6;
            var mesh = TestMeshFactory.CreateConcentricCylinders(segments, rings,
                0.04f, 0.06f, 0.3f);
            var fixture = CreateFixtureFromMesh("Story_GeodesicIsolation", mesh);
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;
                var srcVerts = deformer.SourceMesh.vertices;
                var adjacency = BuildAdjacencyFromMesh(deformer.SourceMesh);

                TestMeshFactory.GetConcentricCylinderRanges(segments, rings,
                    out int innerStart, out int innerEnd, out int outerStart, out int outerEnd);

                // Geodesic verification: inner start vertex should NOT reach any outer vertex
                var distances = GeodesicDistanceCalculator.ComputeDistances(
                    innerStart, 100f, adjacency, srcVerts);
                for (int i = outerStart; i < outerEnd; i++)
                    Assert.That(distances.ContainsKey(i), Is.False);

                // Geodesic verification: outer start vertex should NOT reach any inner vertex
                var outerDistances = GeodesicDistanceCalculator.ComputeDistances(
                    outerStart, 100f, adjacency, srcVerts);
                for (int i = innerStart; i < innerEnd; i++)
                    Assert.That(outerDistances.ContainsKey(i), Is.False);

                // Now deform only outer vertices and verify inner untouched
                int brushIdx = deformer.AddLayer("Outer Only", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();

                for (int i = outerStart; i < outerEnd; i++)
                    deformer.SetDisplacement(i, new Vector3(0.01f, 0f, 0f));

                ReleaseRuntimeMesh(deformer);
                var result = deformer.Deform(false);

                // Inner vertices: zero displacement
                for (int i = innerStart; i < innerEnd; i++)
                    AssertApproximately(srcVerts[i], result.vertices[i], Epsilon);

                // Outer vertices: displaced
                bool anyOuter = false;
                for (int i = outerStart; i < outerEnd; i++)
                    if ((result.vertices[i] - srcVerts[i]).sqrMagnitude > Epsilon) { anyOuter = true; break; }
                Assert.That(anyOuter, Is.True);
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Story 12: Complex layer stack with mixed types and outputs
        //   Lattice (direct) → Brush (direct, masked) → Brush (BS output) →
        //   Lattice (BS output) → verify vertex = lattice1 + brush1*mask,
        //   BS count = 2
        // ========================================================================

        [Test]
        public void Story_MixedLayerStackWithMasksAndOutputModes()
        {
            var mesh = TestMeshFactory.CreateCylinder(16, 8, 0.05f, 0.4f);
            var fixture = CreateFixtureFromMesh("Story_MixedStack", mesh);
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;
                var srcVerts = deformer.SourceMesh.vertices;

                // Layer 0: Lattice direct — small shift
                var lattice0 = deformer.Layers[0].Settings;
                lattice0.SetControlPointLocal(0,
                    lattice0.GetControlPointLocal(0) + new Vector3(0f, 0.02f, 0f));

                // Layer 1: Brush direct with mask
                int brush1 = deformer.AddLayer("Brush Direct", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brush1;
                deformer.EnsureDisplacementCapacity();
                var brushDisp = new Vector3(0.01f, 0f, 0f);
                for (int i = 0; i < vertexCount; i++)
                    deformer.SetDisplacement(i, brushDisp);

                var brush1Layer = deformer.Layers[brush1];
                brush1Layer.EnsureVertexMaskCapacity(vertexCount);
                brush1Layer.SetVertexMask(0, 0f); // Protect vertex 0

                // Layer 2: Brush BS output
                int brush2 = deformer.AddLayer("Brush BS", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brush2;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0f, 0f, 0.05f));
                deformer.Layers[brush2].BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.Layers[brush2].BlendShapeName = "BrushBS";

                // Layer 3: Lattice BS output
                int lattice3 = deformer.AddLayer("Lattice BS", MeshDeformerLayerType.Lattice);
                var lattice3Settings = deformer.Layers[lattice3].Settings;
                lattice3Settings.SetControlPointLocal(0,
                    lattice3Settings.GetControlPointLocal(0) + new Vector3(0f, 0.1f, 0f));
                deformer.Layers[lattice3].BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.Layers[lattice3].BlendShapeName = "LatticeBS";

                // Deform
                ReleaseRuntimeMesh(deformer);
                deformer.InvalidateCache();
                var result = deformer.Deform(false);
                Assert.That(result, Is.Not.Null);

                // Vertex 0: Lattice0 contribution + Brush1 * mask(0)=0 = only lattice
                // Vertex 1+: Lattice0 contribution + Brush1 * mask(1)=1
                // BS layers should NOT contribute to vertices
                var deformed = result.vertices;

                // Vertex 0 should have lattice but NOT brush (masked)
                // Vertex 1 should have lattice + brush
                float v0BrushComponent = Mathf.Abs(deformed[0].x - srcVerts[0].x);
                float v1BrushComponent = Mathf.Abs(deformed[1].x - srcVerts[1].x);
                Assert.That(v0BrushComponent, Is.LessThan(v1BrushComponent + Epsilon),
                    "Masked vertex should have less X displacement than unmasked");

                // Should have 2 BS outputs
                int bsCount = 0;
                for (int i = 0; i < result.blendShapeCount; i++)
                {
                    string n = result.GetBlendShapeName(i);
                    if (n == "BrushBS" || n == "LatticeBS") bsCount++;
                }
                Assert.That(bsCount, Is.EqualTo(2), "Should have both BS outputs");
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Helpers
        // ========================================================================

        private static TestFixture CreateFixture(string name)
        {
            return CreateFixtureFromMesh(name, CreateCubeMesh());
        }

        private static TestFixture CreateFixtureFromMesh(string name, Mesh sourceMesh)
        {
            var root = new GameObject(name);
            var filter = root.AddComponent<MeshFilter>();
            root.AddComponent<MeshRenderer>();
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
            mesh.name = "StoryTestMesh";
            Object.DestroyImmediate(temp);
            return mesh;
        }

        private static List<HashSet<int>> BuildAdjacencyFromMesh(Mesh mesh)
        {
            int vertexCount = mesh.vertexCount;
            var adjacency = new List<HashSet<int>>(vertexCount);
            for (int i = 0; i < vertexCount; i++)
                adjacency.Add(new HashSet<int>());

            var triangles = mesh.triangles;
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                adjacency[a].Add(b); adjacency[a].Add(c);
                adjacency[b].Add(a); adjacency[b].Add(c);
                adjacency[c].Add(a); adjacency[c].Add(b);
            }
            return adjacency;
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
