#if UNITY_EDITOR
using System.Reflection;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    /// <summary>
    /// User-operation stories that exercise realistic editor sessions end to end.
    /// </summary>
    public sealed class UserOperationStoryTests
    {
        private const float Epsilon = 1e-4f;
        private static readonly BindingFlags s_privateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void Story_UserCreatesTwoOutfitVariants_AsSeparateBlendShapes()
        {
            var fixture = CreateFixtureFromMesh("Story_TwoOutfitVariants",
                TestMeshFactory.CreateConcentricCylinders(16, 8, 0.04f, 0.055f, 0.4f));
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;
                var src = deformer.SourceMesh.vertices;
                TestMeshFactory.GetConcentricCylinderRanges(16, 8,
                    out _, out _, out int outerStart, out int outerEnd);

                deformer.ActiveGroupIndex = 0;
                deformer.ActiveGroup.Name = "Tight";
                AddRadialBrush(deformer, "Tighten", outerStart, outerEnd, -0.006f);
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "Outfit_Tight";

                int looseGroup = deformer.AddGroup("Loose");
                deformer.ActiveGroupIndex = looseGroup;
                AddRadialBrush(deformer, "Loosen", outerStart, outerEnd, 0.008f);
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "Outfit_Loose";

                ReleaseRuntimeMesh(deformer);
                var result = deformer.Deform(false);

                Assert.That(FindBlendShape(result, "Outfit_Tight"), Is.GreaterThanOrEqualTo(0));
                Assert.That(FindBlendShape(result, "Outfit_Loose"), Is.GreaterThanOrEqualTo(0));
                for (int i = 0; i < vertexCount; i++)
                    AssertApproximately(src[i], result.vertices[i], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Story_UserComparesDirectEditWithToggleExport_ThenReturnsToPreview()
        {
            var fixture = CreateCylinderFixture("Story_CompareDirectAndToggle");
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;
                int vertexCount = src.Length;

                int brush = deformer.AddLayer("Sleeve Push", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brush;
                deformer.EnsureDisplacementCapacity();

                var disp = new Vector3(0f, 0.012f, 0f);
                for (int i = 0; i < vertexCount; i++)
                    if (src[i].y > 0f) deformer.SetDisplacement(i, disp);

                ReleaseRuntimeMesh(deformer);
                var direct = deformer.Deform(false);
                AssertApproximately(src[vertexCount - 1] + disp,
                    direct.vertices[vertexCount - 1], 2e-3f);

                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "SleevePush";
                ReleaseRuntimeMesh(deformer);
                var toggle = deformer.Deform(false);

                Assert.That(FindBlendShape(toggle, "SleevePush"), Is.GreaterThanOrEqualTo(0));
                for (int i = 0; i < vertexCount; i++)
                    AssertApproximately(src[i], toggle.vertices[i], 2e-3f);

                deformer.BlendShapeOutput = BlendShapeOutputMode.Disabled;
                ReleaseRuntimeMesh(deformer);
                var previewAgain = deformer.Deform(false);
                AssertApproximately(src[vertexCount - 1] + disp,
                    previewAgain.vertices[vertexCount - 1], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Story_UserTemporarilyDisablesProblemLayer_ThenRestoresIt()
        {
            var fixture = CreateCylinderFixture("Story_DisableRestoreLayer");
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;

                int baseFix = AddUniformBrush(deformer, "Base Fix", Vector3.right * 0.01f);
                int problem = AddUniformBrush(deformer, "Experimental", Vector3.up * 0.02f);

                ReleaseRuntimeMesh(deformer);
                var both = deformer.Deform(false);
                AssertApproximately(src[0] + Vector3.right * 0.01f + Vector3.up * 0.02f,
                    both.vertices[0], 2e-3f);

                deformer.Layers[problem].Enabled = false;
                ReleaseRuntimeMesh(deformer);
                var onlyBase = deformer.Deform(false);
                AssertApproximately(src[0] + Vector3.right * 0.01f,
                    onlyBase.vertices[0], 2e-3f);

                deformer.Layers[problem].Enabled = true;
                deformer.Layers[baseFix].Weight = 0.5f;
                ReleaseRuntimeMesh(deformer);
                var restored = deformer.Deform(false);
                AssertApproximately(src[0] + Vector3.right * 0.005f + Vector3.up * 0.02f,
                    restored.vertices[0], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Story_UserEditsOneIslandThenMovesWorkToAnotherIsland()
        {
            var fixture = CreateFixtureFromMesh("Story_MoveIslandWork",
                TestMeshFactory.CreateMultiIslandMesh(3, 8, 4));
            try
            {
                var deformer = fixture.Deformer;
                int islandSize = 8 * 4;
                var src = deformer.SourceMesh.vertices;

                int first = deformer.AddLayer("Collar Draft", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = first;
                deformer.EnsureDisplacementCapacity();
                for (int i = 0; i < islandSize; i++)
                    deformer.SetDisplacement(i, new Vector3(0.015f, 0f, 0f));

                int second = deformer.DuplicateLayer(first);
                var secondLayer = deformer.Layers[second];
                for (int i = 0; i < islandSize; i++)
                    secondLayer.SetBrushDisplacement(i, Vector3.zero);
                for (int i = islandSize; i < islandSize * 2; i++)
                    secondLayer.SetBrushDisplacement(i, new Vector3(0f, 0.02f, 0f));
                deformer.Layers[first].Enabled = false;

                ReleaseRuntimeMesh(deformer);
                var result = deformer.Deform(false);
                AssertApproximately(src[0], result.vertices[0], 2e-3f);
                AssertApproximately(src[islandSize] + new Vector3(0f, 0.02f, 0f),
                    result.vertices[islandSize], 2e-3f);
                AssertApproximately(src[islandSize * 2], result.vertices[islandSize * 2], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Story_UserBuildsAsymmetricLeftRightAdjustment_WithDifferentWeights()
        {
            var fixture = CreateFixtureFromMesh("Story_AsymmetricLR",
                TestMeshFactory.CreateSymmetricHumanoid(8, 6));
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;
                int vertexCount = src.Length;

                int left = deformer.AddLayer("Left Sleeve", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = left;
                deformer.EnsureDisplacementCapacity();
                for (int i = 0; i < vertexCount; i++)
                    deformer.SetDisplacement(i, new Vector3(0f, 0.01f, 0f));

                deformer.SplitLayerByAxis(left, 0, false);
                int right = deformer.DuplicateLayer(left);
                deformer.FlipLayerByAxis(right, 0);
                deformer.Layers[right].Weight = 0.25f;

                ReleaseRuntimeMesh(deformer);
                var result = deformer.Deform(false);

                float leftSum = 0f, rightSum = 0f;
                int leftCount = 0, rightCount = 0;
                for (int i = 0; i < vertexCount; i++)
                {
                    float deltaY = result.vertices[i].y - src[i].y;
                    if (src[i].x < -0.05f) { leftSum += deltaY; leftCount++; }
                    if (src[i].x > 0.05f) { rightSum += deltaY; rightCount++; }
                }

                Assert.That(leftCount, Is.GreaterThan(0));
                Assert.That(rightCount, Is.GreaterThan(0));
                Assert.That(leftSum / leftCount, Is.GreaterThan(rightSum / rightCount));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Story_UserProtectsGradientHemWhileTighteningUpperClothing()
        {
            var fixture = CreateCylinderFixture("Story_GradientHemProtection");
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;
                int vertexCount = src.Length;
                var bounds = deformer.SourceMesh.bounds;

                int brush = deformer.AddLayer("Upper Tighten", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brush;
                deformer.EnsureDisplacementCapacity();
                var layer = deformer.Layers[brush];
                layer.EnsureVertexMaskCapacity(vertexCount);

                for (int i = 0; i < vertexCount; i++)
                {
                    float mask = Mathf.InverseLerp(bounds.min.y, bounds.max.y, src[i].y);
                    layer.SetVertexMask(i, mask);
                    var radial = new Vector3(src[i].x, 0f, src[i].z).normalized;
                    deformer.SetDisplacement(i, -radial * 0.02f);
                }

                ReleaseRuntimeMesh(deformer);
                var result = deformer.Deform(false);
                int bottom = FindByY(src, bounds.min.y);
                int top = FindByY(src, bounds.max.y);
                float bottomMove = (result.vertices[bottom] - src[bottom]).magnitude;
                float topMove = (result.vertices[top] - src[top]).magnitude;

                Assert.That(topMove, Is.GreaterThan(bottomMove));
                Assert.That(bottomMove, Is.LessThan(0.003f));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Story_UserImportsBlendShapeThenClearsLowerHalfForPartialToggle()
        {
            var fixture = CreateFixtureFromMesh("Story_ImportClearLower",
                TestMeshFactory.CreateCylinderWithBlendShapes(16, 8, 0.05f, 0.4f));
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;
                int vertexCount = src.Length;

                int layerIndex = deformer.ImportBlendShapeAsLayer(1);
                var layer = deformer.Layers[layerIndex];
                for (int i = 0; i < vertexCount; i++)
                    if (src[i].y < 0f) layer.SetBrushDisplacement(i, Vector3.zero);

                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "UpperExpand";

                ReleaseRuntimeMesh(deformer);
                var result = deformer.Deform(false);
                int shape = FindBlendShape(result, "UpperExpand");
                Assert.That(shape, Is.GreaterThanOrEqualTo(0));

                var deltas = GetBlendShapeDeltas(result, shape, vertexCount);
                for (int i = 0; i < vertexCount; i++)
                {
                    if (src[i].y < 0f)
                        AssertApproximately(Vector3.zero, deltas[i], Epsilon);
                }
                Assert.That(HasNonZeroDelta(deltas, src, v => v.y > 0f), Is.True);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Story_UserRemovesRedoLayerAfterComparingTwoSculptAttempts()
        {
            var fixture = CreateCylinderFixture("Story_RemoveRedoLayer");
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;

                int attemptA = AddUniformBrush(deformer, "Attempt A", Vector3.right * 0.01f);
                int attemptB = AddUniformBrush(deformer, "Attempt B", Vector3.up * 0.02f);

                ReleaseRuntimeMesh(deformer);
                var beforeRemove = deformer.Deform(false);
                AssertApproximately(src[0] + Vector3.right * 0.01f + Vector3.up * 0.02f,
                    beforeRemove.vertices[0], 2e-3f);

                Assert.That(deformer.RemoveLayer(attemptB), Is.True);
                Assert.That(deformer.Layers.Count, Is.GreaterThan(attemptA));
                ReleaseRuntimeMesh(deformer);
                var afterRemove = deformer.Deform(false);
                AssertApproximately(src[0] + Vector3.right * 0.01f,
                    afterRemove.vertices[0], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Story_UserKeepsDirectFixAndAddsIndependentToggleGroup()
        {
            var fixture = CreateCylinderFixture("Story_DirectAndToggleGroups");
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;

                deformer.ActiveGroupIndex = 0;
                AddUniformBrush(deformer, "Always On Fix", Vector3.right * 0.01f);

                int toggleGroup = deformer.AddGroup("Toggle");
                deformer.ActiveGroupIndex = toggleGroup;
                AddUniformBrush(deformer, "Toggle Lift", Vector3.up * 0.02f);
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "ToggleLift";

                ReleaseRuntimeMesh(deformer);
                var result = deformer.Deform(false);

                AssertApproximately(src[0] + Vector3.right * 0.01f,
                    result.vertices[0], 2e-3f);
                Assert.That(FindBlendShape(result, "ToggleLift"), Is.GreaterThanOrEqualTo(0));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Story_UserAssignsPreviewRestoresThenExportsWithoutChangingRendererMesh()
        {
            var fixture = CreateCylinderFixture("Story_AssignRestoreExport");
            try
            {
                var deformer = fixture.Deformer;
                var filter = fixture.Root.GetComponent<MeshFilter>();
                var sourceMesh = deformer.SourceMesh;
                var src = sourceMesh.vertices;

                AddUniformBrush(deformer, "Preview Fix", Vector3.right * 0.01f);

                deformer.Deform(true);
                Assert.That(filter.sharedMesh, Is.Not.SameAs(sourceMesh));

                deformer.RestoreOriginalMesh();
                Assert.That(filter.sharedMesh, Is.SameAs(sourceMesh));

                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "PreviewFix";
                ReleaseRuntimeMesh(deformer);
                var result = deformer.Deform(false);

                Assert.That(filter.sharedMesh, Is.SameAs(sourceMesh));
                AssertApproximately(src[0], result.vertices[0], 2e-3f);
                Assert.That(FindBlendShape(result, "PreviewFix"), Is.GreaterThanOrEqualTo(0));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Story_UserResizesLatticeRelaxesThenAddsBrushCorrection()
        {
            var fixture = CreateCylinderFixture("Story_ResizeRelaxBrush");
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;
                int vertexCount = src.Length;

                var lattice = deformer.Layers[0].Settings;
                lattice.ResizeGrid(new Vector3Int(5, 4, 3));
                lattice.SetControlPointLocal(0,
                    lattice.GetControlPointLocal(0) + new Vector3(0f, 0.02f, 0f));
                Assert.That(lattice.HasCustomizedControlPoints(), Is.True);
                lattice.RelaxInteriorControlPoints(2);

                int brush = deformer.AddLayer("Top Cleanup", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brush;
                deformer.EnsureDisplacementCapacity();
                for (int i = 0; i < vertexCount; i++)
                    if (src[i].y > 0f) deformer.SetDisplacement(i, Vector3.right * 0.004f);

                ReleaseRuntimeMesh(deformer);
                var result = deformer.Deform(false);
                Assert.That(result.vertexCount, Is.EqualTo(vertexCount));
                Assert.That(AnyVertexMoved(src, result.vertices), Is.True);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Story_UserDisablesEntireVariantGroup_LeavesOtherGroupsIntact()
        {
            var fixture = CreateCylinderFixture("Story_DisableVariantGroup");
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;

                AddUniformBrush(deformer, "Base", Vector3.right * 0.01f);
                int variant = deformer.AddGroup("Variant");
                deformer.ActiveGroupIndex = variant;
                AddUniformBrush(deformer, "Variant Lift", Vector3.up * 0.02f);

                ReleaseRuntimeMesh(deformer);
                var enabled = deformer.Deform(false);
                AssertApproximately(src[0] + Vector3.right * 0.01f + Vector3.up * 0.02f,
                    enabled.vertices[0], 2e-3f);

                deformer.ActiveGroup.Enabled = false;
                ReleaseRuntimeMesh(deformer);
                var disabled = deformer.Deform(false);
                AssertApproximately(src[0] + Vector3.right * 0.01f,
                    disabled.vertices[0], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Story_UserDeletesRejectedVariantGroup_ReturnsToBaseOnly()
        {
            var fixture = CreateCylinderFixture("Story_DeleteVariantGroup");
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;

                deformer.ActiveGroupIndex = 0;
                AddUniformBrush(deformer, "Base Fit", Vector3.right * 0.01f);

                int rejected = deformer.AddGroup("Rejected Variant");
                deformer.ActiveGroupIndex = rejected;
                AddUniformBrush(deformer, "Too Much Lift", Vector3.up * 0.03f);

                ReleaseRuntimeMesh(deformer);
                var withVariant = deformer.Deform(false);
                AssertApproximately(src[0] + Vector3.right * 0.01f + Vector3.up * 0.03f,
                    withVariant.vertices[0], 2e-3f);

                Assert.That(deformer.RemoveGroup(rejected), Is.True);
                ReleaseRuntimeMesh(deformer);
                var afterDelete = deformer.Deform(false);

                Assert.That(deformer.GroupCount, Is.EqualTo(1));
                AssertApproximately(src[0] + Vector3.right * 0.01f,
                    afterDelete.vertices[0], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Story_UserClearsMaskAfterProtectingHem_AppliesExistingStrokeEverywhere()
        {
            var fixture = CreateCylinderFixture("Story_ClearMaskReapplyStroke");
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;
                int vertexCount = src.Length;

                int brush = deformer.AddLayer("Hem Protected Stroke", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brush;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[brush];
                layer.EnsureVertexMaskCapacity(vertexCount);
                for (int i = 0; i < vertexCount; i++)
                {
                    layer.SetVertexMask(i, src[i].y > 0f ? 1f : 0f);
                    deformer.SetDisplacement(i, Vector3.up * 0.01f);
                }

                ReleaseRuntimeMesh(deformer);
                var masked = deformer.Deform(false);
                int bottom = FindByY(src, deformer.SourceMesh.bounds.min.y);
                int top = FindByY(src, deformer.SourceMesh.bounds.max.y);
                AssertApproximately(src[bottom], masked.vertices[bottom], 2e-3f);
                AssertApproximately(src[top] + Vector3.up * 0.01f, masked.vertices[top], 2e-3f);

                layer.ClearVertexMask();
                ReleaseRuntimeMesh(deformer);
                var unmasked = deformer.Deform(false);
                AssertApproximately(src[bottom] + Vector3.up * 0.01f,
                    unmasked.vertices[bottom], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Story_UserDuplicatesImportedBlendShape_ToLayerWeightVariant()
        {
            var fixture = CreateFixtureFromMesh("Story_DuplicateImportedShape",
                TestMeshFactory.CreateCylinderWithBlendShapes(16, 8, 0.05f, 0.4f));
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;

                int shrink = deformer.ImportBlendShapeAsLayer(0);
                int stronger = deformer.DuplicateLayer(shrink);
                deformer.Layers[stronger].Weight = 0.5f;

                ReleaseRuntimeMesh(deformer);
                var result = deformer.Deform(false);

                float srcRadius = new Vector2(src[0].x, src[0].z).magnitude;
                float resultRadius = new Vector2(result.vertices[0].x, result.vertices[0].z).magnitude;
                Assert.That(resultRadius, Is.LessThan(srcRadius));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Story_UserSwitchesGroupsBetweenEdits_EachEditStaysInSelectedGroup()
        {
            var fixture = CreateCylinderFixture("Story_GroupSwitching");
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;

                deformer.ActiveGroupIndex = 0;
                int groupA = deformer.ActiveGroupIndex;
                AddUniformBrush(deformer, "Group A Edit", Vector3.right * 0.01f);

                int groupB = deformer.AddGroup("Group B");
                AddUniformBrush(deformer, "Group B Edit", Vector3.up * 0.02f);

                deformer.ActiveGroupIndex = groupA;
                Assert.That(deformer.Layers.Count, Is.EqualTo(2));
                deformer.ActiveGroup.Enabled = false;

                ReleaseRuntimeMesh(deformer);
                var onlyB = deformer.Deform(false);
                AssertApproximately(src[0] + Vector3.up * 0.02f, onlyB.vertices[0], 2e-3f);

                deformer.ActiveGroupIndex = groupB;
                deformer.ActiveGroup.Enabled = false;
                deformer.ActiveGroupIndex = groupA;
                deformer.ActiveGroup.Enabled = true;

                ReleaseRuntimeMesh(deformer);
                var onlyA = deformer.Deform(false);
                AssertApproximately(src[0] + Vector3.right * 0.01f, onlyA.vertices[0], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Story_UserCreatesSeparateLeftAndRightToggleNames()
        {
            var fixture = CreateFixtureFromMesh("Story_SeparateLRToggles",
                TestMeshFactory.CreateSymmetricHumanoid(8, 6));
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;
                int vertexCount = src.Length;

                deformer.ActiveGroup.Name = "Left";
                int left = deformer.AddLayer("Left Lift", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = left;
                deformer.EnsureDisplacementCapacity();
                for (int i = 0; i < vertexCount; i++)
                    deformer.SetDisplacement(i, Vector3.up * 0.01f);
                deformer.SplitLayerByAxis(left, 0, false);
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "LeftLift";

                int rightGroup = deformer.AddGroup("Right");
                deformer.ActiveGroupIndex = rightGroup;
                int right = deformer.AddLayer("Right Lift", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = right;
                deformer.EnsureDisplacementCapacity();
                for (int i = 0; i < vertexCount; i++)
                    deformer.SetDisplacement(i, Vector3.up * 0.02f);
                deformer.SplitLayerByAxis(right, 0, true);
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "RightLift";

                ReleaseRuntimeMesh(deformer);
                var result = deformer.Deform(false);
                Assert.That(FindBlendShape(result, "LeftLift"), Is.GreaterThanOrEqualTo(0));
                Assert.That(FindBlendShape(result, "RightLift"), Is.GreaterThanOrEqualTo(0));
                AssertApproximately(src[0], result.vertices[0], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Story_UserMutesAllButOneLayerToIsolateContribution()
        {
            var fixture = CreateCylinderFixture("Story_IsolateLayerContribution");
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;

                int x = AddUniformBrush(deformer, "X", Vector3.right * 0.01f);
                int y = AddUniformBrush(deformer, "Y", Vector3.up * 0.02f);
                int z = AddUniformBrush(deformer, "Z", Vector3.forward * 0.03f);

                deformer.Layers[x].Enabled = false;
                deformer.Layers[z].Enabled = false;

                ReleaseRuntimeMesh(deformer);
                var isolated = deformer.Deform(false);
                AssertApproximately(src[0] + Vector3.up * 0.02f,
                    isolated.vertices[0], 2e-3f);

                deformer.Layers[x].Enabled = true;
                deformer.Layers[y].Enabled = false;
                deformer.Layers[z].Enabled = true;

                ReleaseRuntimeMesh(deformer);
                var xz = deformer.Deform(false);
                AssertApproximately(src[0] + Vector3.right * 0.01f + Vector3.forward * 0.03f,
                    xz.vertices[0], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Story_UserChangesLatticeInterpolationAndBounds_ForSmootherCage()
        {
            var fixture = CreateCylinderFixture("Story_LatticeInterpolationBounds");
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;
                var lattice = deformer.Layers[0].Settings;

                int beforeHash = deformer.ComputeLayeredStateHash();
                lattice.Interpolation = LatticeInterpolationMode.CubicBernstein;
                lattice.LocalBounds = new Bounds(Vector3.zero, new Vector3(0.2f, 0.6f, 0.2f));
                lattice.ResetControlPoints();
                lattice.SetControlPointLocal(0,
                    lattice.GetControlPointLocal(0) + new Vector3(0f, 0.03f, 0f));

                Assert.That(deformer.ComputeLayeredStateHash(), Is.Not.EqualTo(beforeHash));

                ReleaseRuntimeMesh(deformer);
                var result = deformer.Deform(false);
                Assert.That(result.vertexCount, Is.EqualTo(src.Length));
                Assert.That(AnyVertexMoved(src, result.vertices), Is.True);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Story_UserResetsLatticeCageAfterBadControlPointEdit()
        {
            var fixture = CreateCylinderFixture("Story_ResetLatticeCage");
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;
                var lattice = deformer.Layers[0].Settings;

                lattice.SetControlPointLocal(0,
                    lattice.GetControlPointLocal(0) + new Vector3(0f, 0.05f, 0f));
                Assert.That(lattice.HasCustomizedControlPoints(), Is.True);

                ReleaseRuntimeMesh(deformer);
                var edited = deformer.Deform(false);
                Assert.That(AnyVertexMoved(src, edited.vertices), Is.True);

                lattice.ResetControlPoints();
                Assert.That(lattice.HasCustomizedControlPoints(), Is.False);

                ReleaseRuntimeMesh(deformer);
                var reset = deformer.Deform(false);
                for (int i = 0; i < src.Length; i++)
                    AssertApproximately(src[i], reset.vertices[i], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Story_UserSetsBlendShapeCurve_ForPartialFinalEffect()
        {
            var fixture = CreateCylinderFixture("Story_BlendShapeCurve");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;

                AddUniformBrush(deformer, "Curve Controlled", Vector3.right * 0.02f);
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "HalfStrength";
                deformer.BlendShapeCurve = AnimationCurve.Linear(0f, 0f, 1f, 0.5f);

                ReleaseRuntimeMesh(deformer);
                var result = deformer.Deform(false);
                int shape = FindBlendShape(result, "HalfStrength");
                Assert.That(shape, Is.GreaterThanOrEqualTo(0));
                Assert.That(result.GetBlendShapeFrameCount(shape), Is.EqualTo(100));

                var lastDeltas = GetBlendShapeDeltas(result, shape, vertexCount);
                AssertApproximately(Vector3.right * 0.01f, lastDeltas[0], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Story_UserResetsSelectedVerticesThenClearsAllVertexEdits()
        {
            var fixture = CreateCylinderFixture("Story_VertexSelectionReset");
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;
                int brush = deformer.AddLayer("Vertex Selection Edits", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brush;
                deformer.EnsureDisplacementCapacity();
                var layer = deformer.Layers[brush];

                layer.SetBrushDisplacement(0, Vector3.right * 0.01f);
                layer.SetBrushDisplacement(1, Vector3.up * 0.02f);
                layer.SetBrushDisplacement(2, Vector3.forward * 0.03f);

                layer.SetBrushDisplacement(1, Vector3.zero);
                ReleaseRuntimeMesh(deformer);
                var selectedReset = deformer.Deform(false);
                AssertApproximately(src[0] + Vector3.right * 0.01f,
                    selectedReset.vertices[0], 2e-3f);
                AssertApproximately(src[1], selectedReset.vertices[1], 2e-3f);
                AssertApproximately(src[2] + Vector3.forward * 0.03f,
                    selectedReset.vertices[2], 2e-3f);

                layer.ClearBrushDisplacements();
                ReleaseRuntimeMesh(deformer);
                var allReset = deformer.Deform(false);
                for (int i = 0; i < src.Length; i++)
                    AssertApproximately(src[i], allReset.vertices[i], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Story_UserUsesClearAllBrushOperation_RemovesMaskAndDisplacement()
        {
            var fixture = CreateCylinderFixture("Story_BrushClearAll");
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;
                int vertexCount = src.Length;

                int brush = deformer.AddLayer("Brush Stroke And Mask", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brush;
                deformer.EnsureDisplacementCapacity();
                var layer = deformer.Layers[brush];
                layer.EnsureVertexMaskCapacity(vertexCount);

                for (int i = 0; i < vertexCount; i++)
                {
                    deformer.SetDisplacement(i, Vector3.up * 0.01f);
                    layer.SetVertexMask(i, i % 2 == 0 ? 0f : 1f);
                }

                Assert.That(layer.HasBrushDisplacements(), Is.True);
                Assert.That(layer.HasVertexMask(), Is.True);

                layer.ClearBrushDisplacements();
                layer.ClearVertexMask();

                Assert.That(layer.HasBrushDisplacements(), Is.False);
                Assert.That(layer.HasVertexMask(), Is.False);

                ReleaseRuntimeMesh(deformer);
                var result = deformer.Deform(false);
                for (int i = 0; i < src.Length; i++)
                    AssertApproximately(src[i], result.vertices[i], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Story_UserTogglesMeshRebuildOptions_BoundsCanStayManual()
        {
            var fixture = CreateCylinderFixture("Story_MeshRebuildOptions");
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;

                SetPrivateField(deformer, "_recalculateBounds", false);
                SetPrivateField(deformer, "_recalculateNormals", false);
                AddUniformBrush(deformer, "Move Outside Bounds", Vector3.right * 0.5f);

                ReleaseRuntimeMesh(deformer);
                var noRecalc = deformer.Deform(false);
                AssertApproximately(src[0] + Vector3.right * 0.5f,
                    noRecalc.vertices[0], 2e-3f);
                Assert.That(noRecalc.bounds, Is.EqualTo(deformer.SourceMesh.bounds));

                SetPrivateField(deformer, "_recalculateBounds", true);
                ReleaseRuntimeMesh(deformer);
                var recalculated = deformer.Deform(false);
                Assert.That(recalculated.bounds.max.x,
                    Is.GreaterThan(deformer.SourceMesh.bounds.max.x + 0.1f));
            }
            finally { fixture.Dispose(); }
        }

        private static TestFixture CreateCylinderFixture(string name)
        {
            return CreateFixtureFromMesh(name,
                TestMeshFactory.CreateCylinder(16, 8, 0.05f, 0.4f));
        }

        private static TestFixture CreateFixtureFromMesh(string name, Mesh sourceMesh)
        {
            var root = new GameObject(name);
            var filter = root.AddComponent<MeshFilter>();
            root.AddComponent<MeshRenderer>();
            filter.sharedMesh = sourceMesh;

            var deformer = root.AddComponent<LatticeDeformer>();
            deformer.Reset();
            Assert.That(deformer.Deform(false), Is.Not.Null);

            return new TestFixture(root, sourceMesh, deformer);
        }

        private static int AddUniformBrush(LatticeDeformer deformer, string name, Vector3 displacement)
        {
            int idx = deformer.AddLayer(name, MeshDeformerLayerType.Brush);
            deformer.ActiveLayerIndex = idx;
            deformer.EnsureDisplacementCapacity();
            deformer.SetDisplacement(0, displacement);
            return idx;
        }

        private static void AddRadialBrush(LatticeDeformer deformer, string name,
            int start, int end, float amount)
        {
            int idx = deformer.AddLayer(name, MeshDeformerLayerType.Brush);
            deformer.ActiveLayerIndex = idx;
            deformer.EnsureDisplacementCapacity();
            var src = deformer.SourceMesh.vertices;
            for (int i = start; i < end; i++)
            {
                var radial = new Vector3(src[i].x, 0f, src[i].z).normalized;
                deformer.SetDisplacement(i, radial * amount);
            }
        }

        private static int FindBlendShape(Mesh mesh, string name)
        {
            for (int i = 0; i < mesh.blendShapeCount; i++)
                if (mesh.GetBlendShapeName(i) == name) return i;
            return -1;
        }

        private static Vector3[] GetBlendShapeDeltas(Mesh mesh, int blendShapeIndex,
            int vertexCount)
        {
            var deltas = new Vector3[vertexCount];
            int frameCount = mesh.GetBlendShapeFrameCount(blendShapeIndex);
            mesh.GetBlendShapeFrameVertices(blendShapeIndex, frameCount - 1,
                deltas, null, null);
            return deltas;
        }

        private static bool HasNonZeroDelta(Vector3[] deltas, Vector3[] vertices,
            System.Predicate<Vector3> predicate)
        {
            for (int i = 0; i < deltas.Length; i++)
                if (predicate(vertices[i]) && deltas[i].sqrMagnitude > Epsilon * Epsilon)
                    return true;
            return false;
        }

        private static int FindByY(Vector3[] vertices, float y)
        {
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < vertices.Length; i++)
            {
                float distance = Mathf.Abs(vertices[i].y - y);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
            return best;
        }

        private static bool AnyVertexMoved(Vector3[] expected, Vector3[] actual)
        {
            for (int i = 0; i < expected.Length; i++)
                if ((expected[i] - actual[i]).sqrMagnitude > Epsilon * Epsilon)
                    return true;
            return false;
        }

        private static void ReleaseRuntimeMesh(LatticeDeformer deformer)
        {
            var field = typeof(LatticeDeformer).GetField("_runtimeMesh", s_privateInstance);
            Assert.That(field, Is.Not.Null, "Private field not found: _runtimeMesh");
            var mesh = field.GetValue(deformer) as Mesh;
            if (mesh != null) Object.DestroyImmediate(mesh);
            field.SetValue(deformer, null);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, s_privateInstance);
            Assert.That(field, Is.Not.Null, $"Private field not found: {fieldName}");
            field.SetValue(target, value);
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
