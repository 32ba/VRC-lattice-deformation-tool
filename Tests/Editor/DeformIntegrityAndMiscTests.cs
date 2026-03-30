#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    /// <summary>
    /// Tests for mesh data integrity during deformation, lifecycle,
    /// remaining edge cases, and miscellaneous untested code paths.
    /// </summary>
    public sealed class DeformIntegrityAndMiscTests
    {
        private const float Epsilon = 1e-4f;
        private static readonly BindingFlags s_privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        // ========================================================================
        // Mesh Data Integrity: UVs, Colors, Submeshes, Topology
        // ========================================================================

        [Test]
        public void Deform_PreservesUVCoordinates()
        {
            var fixture = CreateFixture("Deform_PreservesUVCoordinates");
            try
            {
                var deformer = fixture.Deformer;
                var sourceUVs = deformer.SourceMesh.uv;
                Assert.That(sourceUVs.Length, Is.GreaterThan(0), "Source mesh should have UVs");

                // Apply displacement
                int idx = deformer.AddLayer("UV Test", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.5f, 0f, 0f));

                ReleaseRuntimeMesh(deformer);
                var mesh = deformer.Deform(false);
                var deformedUVs = mesh.uv;

                Assert.That(deformedUVs.Length, Is.EqualTo(sourceUVs.Length));
                for (int i = 0; i < sourceUVs.Length; i++)
                {
                    Assert.That(deformedUVs[i].x, Is.EqualTo(sourceUVs[i].x).Within(Epsilon),
                        $"UV.x mismatch at vertex {i}");
                    Assert.That(deformedUVs[i].y, Is.EqualTo(sourceUVs[i].y).Within(Epsilon),
                        $"UV.y mismatch at vertex {i}");
                }
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Deform_PreservesTriangleTopology()
        {
            var fixture = CreateFixture("Deform_PreservesTriangleTopology");
            try
            {
                var deformer = fixture.Deformer;
                var sourceTriangles = deformer.SourceMesh.triangles;

                int idx = deformer.AddLayer("Topo Test", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.2f, 0.3f, 0f));

                ReleaseRuntimeMesh(deformer);
                var mesh = deformer.Deform(false);
                var deformedTriangles = mesh.triangles;

                Assert.That(deformedTriangles.Length, Is.EqualTo(sourceTriangles.Length),
                    "Triangle count should be preserved");
                for (int i = 0; i < sourceTriangles.Length; i++)
                {
                    Assert.That(deformedTriangles[i], Is.EqualTo(sourceTriangles[i]),
                        $"Triangle index mismatch at {i}");
                }
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Deform_PreservesSubMeshCount()
        {
            var fixture = CreateFixture("Deform_PreservesSubMeshCount");
            try
            {
                var deformer = fixture.Deformer;
                int sourceSubMeshCount = deformer.SourceMesh.subMeshCount;

                int idx = deformer.AddLayer("SubMesh Test", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.1f, 0f, 0f));

                ReleaseRuntimeMesh(deformer);
                var mesh = deformer.Deform(false);

                Assert.That(mesh.subMeshCount, Is.EqualTo(sourceSubMeshCount));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Deform_PreservesVertexCount()
        {
            var fixture = CreateFixture("Deform_PreservesVertexCount");
            try
            {
                var deformer = fixture.Deformer;
                int sourceCount = deformer.SourceMesh.vertexCount;

                // Add multiple layers of different types
                var settings = deformer.Layers[0].Settings;
                settings.SetControlPointLocal(0,
                    settings.GetControlPointLocal(0) + new Vector3(0f, 0.1f, 0f));

                int brushIdx = deformer.AddLayer("B", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.1f, 0f, 0f));

                ReleaseRuntimeMesh(deformer);
                var mesh = deformer.Deform(false);
                Assert.That(mesh.vertexCount, Is.EqualTo(sourceCount));
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Deform with Null / Missing Mesh
        // ========================================================================

        [Test]
        public void Deform_NoMeshAssigned_ReturnsNull()
        {
            var root = new GameObject("Deform_NoMeshAssigned_ReturnsNull");
            root.AddComponent<MeshFilter>(); // No mesh assigned
            root.AddComponent<MeshRenderer>();
            var deformer = root.AddComponent<LatticeDeformer>();
            try
            {
                var result = deformer.Deform(false);
                Assert.That(result, Is.Null, "Deform should return null when no mesh is assigned");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ========================================================================
        // Minimal Mesh (Single Triangle)
        // ========================================================================

        [Test]
        public void SingleTriangleMesh_DeformWorks()
        {
            var mesh = new Mesh { name = "SingleTriangle" };
            mesh.vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0.5f, 1f, 0f)
            };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var fixture = CreateFixtureFromMesh("SingleTriangleMesh_DeformWorks", mesh);
            try
            {
                var deformer = fixture.Deformer;

                int idx = deformer.AddLayer("Tri", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();

                var disp = new Vector3(0.1f, 0f, 0f);
                deformer.SetDisplacement(0, disp);

                ReleaseRuntimeMesh(deformer);
                var deformed = deformer.Deform(false);
                Assert.That(deformed, Is.Not.Null);
                Assert.That(deformed.vertexCount, Is.EqualTo(3));

                var src = deformer.SourceMesh.vertices;
                AssertApproximately(src[0] + disp, deformed.vertices[0], 2e-3f);
                AssertApproximately(src[1], deformed.vertices[1], 2e-3f);
                AssertApproximately(src[2], deformed.vertices[2], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // RestoreOriginalMesh
        // ========================================================================

        [Test]
        public void RestoreOriginalMesh_RestoresSourceMesh()
        {
            var fixture = CreateFixture("RestoreOriginalMesh_RestoresSourceMesh");
            try
            {
                var deformer = fixture.Deformer;
                var filter = fixture.Root.GetComponent<MeshFilter>();
                var originalMesh = deformer.SourceMesh;

                // Deform and assign to renderer
                int idx = deformer.AddLayer("Restore", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.5f, 0f, 0f));
                deformer.Deform(true); // assignToRenderer = true

                // After deform, renderer should have runtime mesh (different from source)
                Assert.That(filter.sharedMesh, Is.Not.SameAs(originalMesh));

                // Restore
                deformer.RestoreOriginalMesh();

                // Should be back to original
                Assert.That(filter.sharedMesh, Is.SameAs(originalMesh));
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Deform with assignToRenderer=true
        // ========================================================================

        [Test]
        public void DeformAssignToRenderer_UpdatesFilter()
        {
            var fixture = CreateFixture("DeformAssignToRenderer_UpdatesFilter");
            try
            {
                var deformer = fixture.Deformer;
                var filter = fixture.Root.GetComponent<MeshFilter>();
                var originalMesh = deformer.SourceMesh;

                int idx = deformer.AddLayer("Assign", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.1f, 0f, 0f));

                var runtimeMesh = deformer.Deform(true);

                // Filter should now point to the runtime mesh, not source
                Assert.That(filter.sharedMesh, Is.Not.SameAs(originalMesh));
                Assert.That(runtimeMesh, Is.Not.Null);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void DeformAssignFalse_DoesNotChangeFilter()
        {
            var fixture = CreateFixture("DeformAssignFalse_DoesNotChangeFilter");
            try
            {
                var deformer = fixture.Deformer;
                var filter = fixture.Root.GetComponent<MeshFilter>();

                // Restore to make sure filter has original mesh
                deformer.RestoreOriginalMesh();
                var originalFilterMesh = filter.sharedMesh;

                int idx = deformer.AddLayer("NoAssign", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.1f, 0f, 0f));

                ReleaseRuntimeMesh(deformer);
                deformer.Deform(false);

                // Filter should still have original mesh
                Assert.That(filter.sharedMesh, Is.SameAs(originalFilterMesh));
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // FlipLayerByAxis on Y and Z axes
        // ========================================================================

        [Test]
        public void FlipLayerByAxis_YAxis_SwapsVerticalMirrors()
        {
            var mesh = CreateYSymmetricMesh();
            var fixture = CreateFixtureFromMesh("FlipLayerByAxis_YAxis_SwapsVerticalMirrors", mesh);
            try
            {
                var deformer = fixture.Deformer;
                int idx = deformer.AddLayer("FlipY", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[idx];
                // vertex 0 at (0, 1, 0), vertex 1 at (0, -1, 0) — Y mirror pair
                var disp = new Vector3(0.2f, 0.1f, 0f);
                layer.SetBrushDisplacement(0, disp);

                deformer.FlipLayerByAxis(idx, 1); // Y axis

                // vertex 0's displacement should have moved to vertex 1 with Y negated
                var expected = new Vector3(disp.x, -disp.y, disp.z);
                AssertApproximately(expected, layer.GetBrushDisplacement(1), 2e-3f);
                AssertApproximately(Vector3.zero, layer.GetBrushDisplacement(0), 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void FlipLayerByAxis_ZAxis_SwapsDepthMirrors()
        {
            var mesh = CreateZSymmetricMesh();
            var fixture = CreateFixtureFromMesh("FlipLayerByAxis_ZAxis_SwapsDepthMirrors", mesh);
            try
            {
                var deformer = fixture.Deformer;
                int idx = deformer.AddLayer("FlipZ", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[idx];
                // vertex 0 at (0, 0, 1), vertex 1 at (0, 0, -1) — Z mirror pair
                var disp = new Vector3(0.1f, 0.2f, 0.3f);
                layer.SetBrushDisplacement(0, disp);

                deformer.FlipLayerByAxis(idx, 2); // Z axis

                var expected = new Vector3(disp.x, disp.y, -disp.z);
                AssertApproximately(expected, layer.GetBrushDisplacement(1), 2e-3f);
                AssertApproximately(Vector3.zero, layer.GetBrushDisplacement(0), 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Split: All Vertices on One Side
        // ========================================================================

        [Test]
        public void Split_AllVerticesOnPositiveSide_KeepPositive_NoChange()
        {
            // Mesh where all X >= 0
            var mesh = new Mesh { name = "AllPositiveX" };
            mesh.vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0.5f, 1f, 0f),
                new Vector3(0.5f, 0f, 1f)
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var fixture = CreateFixtureFromMesh("Split_AllVerticesOnPositiveSide", mesh);
            try
            {
                var deformer = fixture.Deformer;
                int idx = deformer.AddLayer("AllPos", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[idx];
                var disp = new Vector3(0.1f, 0.2f, 0f);
                for (int i = 0; i < 4; i++)
                    deformer.SetDisplacement(i, disp);

                // Split keep positive X — all vertices are on positive side
                deformer.SplitLayerByAxis(idx, 0, true);

                // All should retain displacement
                for (int i = 0; i < 4; i++)
                    AssertApproximately(disp, layer.GetBrushDisplacement(i), Epsilon);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void Split_AllVerticesOnPositiveSide_KeepNegative_ZerosAll()
        {
            var mesh = new Mesh { name = "AllPositiveX2" };
            mesh.vertices = new[]
            {
                new Vector3(0.1f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0.5f, 1f, 0f),
                new Vector3(0.5f, 0f, 1f)
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var fixture = CreateFixtureFromMesh("Split_AllPos_KeepNeg", mesh);
            try
            {
                var deformer = fixture.Deformer;
                int idx = deformer.AddLayer("AllPosKeepNeg", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[idx];
                var disp = new Vector3(0.1f, 0.2f, 0f);
                for (int i = 0; i < 4; i++)
                    deformer.SetDisplacement(i, disp);

                // Keep negative X — but all vertices are positive
                deformer.SplitLayerByAxis(idx, 0, false);

                // All should be zeroed
                for (int i = 0; i < 4; i++)
                    AssertApproximately(Vector3.zero, layer.GetBrushDisplacement(i), Epsilon);
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // BlendShape Output (Component-Level)
        // ========================================================================

        [Test]
        public void BlendShapeOutput_CombinesAllLayerDeltas()
        {
            // BlendShape output is now component-level: all layers contribute
            // to a single combined BlendShape frame.
            var fixture = CreateFixture("BlendShapeOutput_CombinesAllLayerDeltas");
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;

                int layer1 = deformer.AddLayer("A", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = layer1;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.1f, 0f, 0f));

                int layer2 = deformer.AddLayer("B", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = layer2;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0f, 0.2f, 0f));

                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = "Combined";

                ReleaseRuntimeMesh(deformer);
                Mesh mesh = null;
                Assert.DoesNotThrow(() => mesh = deformer.Deform(false));
                Assert.That(mesh, Is.Not.Null);
                Assert.That(mesh.blendShapeCount, Is.EqualTo(1));
                Assert.That(mesh.GetBlendShapeName(0), Is.EqualTo("Combined"));

                // Vertices should be unchanged (all deformation goes to BlendShape)
                for (int i = 0; i < src.Length; i++)
                    AssertApproximately(src[i], mesh.vertices[i], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void BlendShapeOutput_FallbackName_UsesGameObjectName()
        {
            // EffectiveBlendShapeName falls back to gameObject.name
            var fixture = CreateFixture("BlendShapeOutput_FallbackName_UsesGameObjectName");
            try
            {
                var deformer = fixture.Deformer;

                int layer1 = deformer.AddLayer("A", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = layer1;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.1f, 0f, 0f));

                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.BlendShapeName = ""; // empty => fallback to gameObject.name

                ReleaseRuntimeMesh(deformer);
                Mesh mesh = null;
                Assert.DoesNotThrow(() => mesh = deformer.Deform(false));
                Assert.That(mesh, Is.Not.Null);
                Assert.That(mesh.blendShapeCount, Is.EqualTo(1));
                Assert.That(mesh.GetBlendShapeName(0), Is.EqualTo(deformer.gameObject.name));
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Modify Between Deform Calls
        // ========================================================================

        [Test]
        public void ModifyBetweenDeforms_ReflectsLatestChanges()
        {
            var fixture = CreateFixture("ModifyBetweenDeforms_ReflectsLatestChanges");
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;

                int idx = deformer.AddLayer("Modify", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();

                // First deform
                var disp1 = new Vector3(0.1f, 0f, 0f);
                deformer.SetDisplacement(0, disp1);
                ReleaseRuntimeMesh(deformer);
                var mesh1 = deformer.Deform(false);
                AssertApproximately(src[0] + disp1, mesh1.vertices[0], 2e-3f);

                // Modify displacement
                var disp2 = new Vector3(0f, 0.5f, 0f);
                deformer.SetDisplacement(0, disp2);
                ReleaseRuntimeMesh(deformer);
                var mesh2 = deformer.Deform(false);
                AssertApproximately(src[0] + disp2, mesh2.vertices[0], 2e-3f);

                // Modify weight
                deformer.Layers[idx].Weight = 0.5f;
                ReleaseRuntimeMesh(deformer);
                var mesh3 = deformer.Deform(false);
                AssertApproximately(src[0] + disp2 * 0.5f, mesh3.vertices[0], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void AddLayerBetweenDeforms_NewLayerContributes()
        {
            var fixture = CreateFixture("AddLayerBetweenDeforms_NewLayerContributes");
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;

                int idx1 = deformer.AddLayer("First", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx1;
                deformer.EnsureDisplacementCapacity();
                var disp1 = new Vector3(0.1f, 0f, 0f);
                deformer.SetDisplacement(0, disp1);

                ReleaseRuntimeMesh(deformer);
                var mesh1 = deformer.Deform(false);
                var v1 = mesh1.vertices[0];

                // Add new layer between deforms
                int idx2 = deformer.AddLayer("Second", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx2;
                deformer.EnsureDisplacementCapacity();
                var disp2 = new Vector3(0f, 0.2f, 0f);
                deformer.SetDisplacement(0, disp2);

                ReleaseRuntimeMesh(deformer);
                var mesh2 = deformer.Deform(false);
                var v2 = mesh2.vertices[0];

                AssertApproximately(src[0] + disp1 + disp2, v2, 2e-3f);
                Assert.That((v2 - v1).sqrMagnitude, Is.GreaterThan(Epsilon),
                    "Second deform should differ from first");
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // RelaxInteriorControlPoints
        // ========================================================================

        [Test]
        public void RelaxInteriorControlPoints_SmoothesCenterPoints()
        {
            var fixture = CreateFixture("RelaxInteriorControlPoints_SmoothesCenterPoints");
            try
            {
                var settings = fixture.Deformer.Layers[0].Settings;

                // Need at least 3x3x3 grid for interior points
                settings.ResizeGrid(new Vector3Int(4, 4, 4));

                // Displace an interior point significantly
                // Interior: (1,1,1) = 1*16 + 1*4 + 1 = 21
                int interiorIdx = 1 * 16 + 1 * 4 + 1;
                var neutral = settings.GetControlPointLocal(interiorIdx);
                var bigDelta = new Vector3(0.5f, 0f, 0f);
                settings.SetControlPointLocal(interiorIdx, neutral + bigDelta);

                var beforeRelax = settings.GetControlPointLocal(interiorIdx);

                settings.RelaxInteriorControlPoints(5);

                var afterRelax = settings.GetControlPointLocal(interiorIdx);

                // After relaxation, the interior point should have moved toward neighbors
                // (i.e., the extreme displacement should be reduced)
                float distBefore = (beforeRelax - neutral).magnitude;
                float distAfter = (afterRelax - neutral).magnitude;
                Assert.That(distAfter, Is.LessThan(distBefore),
                    "Relaxation should reduce the displacement of an outlier interior point");
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void RelaxInteriorControlPoints_ZeroIterations_NoChange()
        {
            var fixture = CreateFixture("RelaxInteriorControlPoints_ZeroIterations_NoChange");
            try
            {
                var settings = fixture.Deformer.Layers[0].Settings;
                settings.ResizeGrid(new Vector3Int(4, 4, 4));

                int interiorIdx = 1 * 16 + 1 * 4 + 1;
                var delta = new Vector3(0.3f, 0f, 0f);
                var neutral = settings.GetControlPointLocal(interiorIdx);
                settings.SetControlPointLocal(interiorIdx, neutral + delta);

                var before = settings.GetControlPointLocal(interiorIdx);
                settings.RelaxInteriorControlPoints(0);
                var after = settings.GetControlPointLocal(interiorIdx);

                AssertApproximately(before, after, Epsilon);
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Disabled Layer Preserves Data
        // ========================================================================

        [Test]
        public void DisabledLayer_PreservesDisplacementData()
        {
            var fixture = CreateFixture("DisabledLayer_PreservesDisplacementData");
            try
            {
                var deformer = fixture.Deformer;
                int idx = deformer.AddLayer("Preserve", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();

                var disp = new Vector3(0.1f, 0.2f, 0.3f);
                deformer.SetDisplacement(0, disp);

                // Disable
                deformer.Layers[idx].Enabled = false;

                // Data should still be there
                AssertApproximately(disp, deformer.Layers[idx].GetBrushDisplacement(0), Epsilon);

                // Re-enable — data should still be intact
                deformer.Layers[idx].Enabled = true;
                AssertApproximately(disp, deformer.Layers[idx].GetBrushDisplacement(0), Epsilon);

                // Deform should apply it
                ReleaseRuntimeMesh(deformer);
                var mesh = deformer.Deform(false);
                var src = deformer.SourceMesh.vertices;
                AssertApproximately(src[0] + disp, mesh.vertices[0], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // StateHash: Brush Displacement and Mask Sensitivity
        // ========================================================================

        [Test]
        public void StateHash_BrushDisplacementChange_ChangesHash()
        {
            var fixture = CreateFixture("StateHash_BrushDisplacementChange_ChangesHash");
            try
            {
                var deformer = fixture.Deformer;
                int idx = deformer.AddLayer("Hash", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();

                int hash1 = deformer.ComputeLayeredStateHash();

                deformer.SetDisplacement(0, new Vector3(0.1f, 0f, 0f));
                int hash2 = deformer.ComputeLayeredStateHash();

                Assert.That(hash2, Is.Not.EqualTo(hash1),
                    "Hash should change when brush displacement is set");
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void StateHash_MaskChange_ChangesHash()
        {
            var fixture = CreateFixture("StateHash_MaskChange_ChangesHash");
            try
            {
                var deformer = fixture.Deformer;
                int idx = deformer.AddLayer("MaskHash", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[idx];
                int hash1 = deformer.ComputeLayeredStateHash();

                layer.EnsureVertexMaskCapacity(deformer.SourceMesh.vertexCount);
                layer.SetVertexMask(0, 0f);
                int hash2 = deformer.ComputeLayeredStateHash();

                Assert.That(hash2, Is.Not.EqualTo(hash1),
                    "Hash should change when vertex mask is modified");
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void StateHash_EnabledToggle_ChangesHash()
        {
            var fixture = CreateFixture("StateHash_EnabledToggle_ChangesHash");
            try
            {
                var deformer = fixture.Deformer;
                int hash1 = deformer.ComputeLayeredStateHash();

                deformer.Layers[0].Enabled = false;
                int hash2 = deformer.ComputeLayeredStateHash();

                Assert.That(hash2, Is.Not.EqualTo(hash1));
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Reset Behavior
        // ========================================================================

        [Test]
        public void Reset_CreatesDefaultLayer()
        {
            var root = new GameObject("Reset_CreatesDefaultLayer");
            var filter = root.AddComponent<MeshFilter>();
            root.AddComponent<MeshRenderer>();
            filter.sharedMesh = CreateCubeMesh();
            var deformer = root.AddComponent<LatticeDeformer>();
            try
            {
                deformer.Reset();

                Assert.That(deformer.Layers.Count, Is.GreaterThanOrEqualTo(1));
                Assert.That(deformer.ActiveLayerIndex, Is.EqualTo(0));
                Assert.That(deformer.SourceMesh, Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Reset_AfterModification_CanStillDeform()
        {
            var fixture = CreateFixture("Reset_AfterModification_CanStillDeform");
            try
            {
                var deformer = fixture.Deformer;

                // Add layers and modify
                deformer.AddLayer("Extra1", MeshDeformerLayerType.Brush);
                deformer.AddLayer("Extra2", MeshDeformerLayerType.Brush);

                // Reset
                deformer.Reset();

                Assert.That(deformer.Layers.Count, Is.GreaterThanOrEqualTo(1));

                // Should still be able to deform
                ReleaseRuntimeMesh(deformer);
                var mesh = deformer.Deform(false);
                Assert.That(mesh, Is.Not.Null);
                Assert.That(mesh.vertexCount, Is.EqualTo(deformer.SourceMesh.vertexCount));
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // All Layers as BlendShape Output
        // ========================================================================

        [Test]
        public void AllLayersBlendShapeOutput_MeshUnchanged()
        {
            var fixture = CreateFixture("AllLayersBlendShapeOutput_MeshUnchanged");
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;
                var src = deformer.SourceMesh.vertices;

                // Lattice layer deformation
                var settings = deformer.Layers[0].Settings;
                settings.SetControlPointLocal(0,
                    settings.GetControlPointLocal(0) + new Vector3(0f, 0.2f, 0f));

                // Add brush layer
                int idx = deformer.AddLayer("BS", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.1f, 0f, 0f));

                // Set component-level BlendShape output
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;

                ReleaseRuntimeMesh(deformer);
                var mesh = deformer.Deform(false);

                // Vertices should be completely unchanged
                for (int i = 0; i < vertexCount; i++)
                    AssertApproximately(src[i], mesh.vertices[i], 2e-3f);

                // One combined BlendShape should exist
                Assert.That(mesh.blendShapeCount, Is.EqualTo(1));
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Mixed Weights Composition
        // ========================================================================

        [Test]
        public void MixedWeights_ComposeCorrectly()
        {
            var fixture = CreateFixture("MixedWeights_ComposeCorrectly");
            try
            {
                var deformer = fixture.Deformer;
                var src = deformer.SourceMesh.vertices;

                // Layer with weight=0 (should not contribute)
                int idx0 = deformer.AddLayer("W0", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx0;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(99f, 0f, 0f));
                deformer.Layers[idx0].Weight = 0f;

                // Layer with weight=0.25
                int idx1 = deformer.AddLayer("W025", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx1;
                deformer.EnsureDisplacementCapacity();
                var disp1 = new Vector3(0.4f, 0f, 0f);
                deformer.SetDisplacement(0, disp1);
                deformer.Layers[idx1].Weight = 0.25f;

                // Layer with weight=0.5
                int idx2 = deformer.AddLayer("W05", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx2;
                deformer.EnsureDisplacementCapacity();
                var disp2 = new Vector3(0f, 0.2f, 0f);
                deformer.SetDisplacement(0, disp2);
                deformer.Layers[idx2].Weight = 0.5f;

                // Layer with weight=1
                int idx3 = deformer.AddLayer("W1", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx3;
                deformer.EnsureDisplacementCapacity();
                var disp3 = new Vector3(0f, 0f, 0.1f);
                deformer.SetDisplacement(0, disp3);
                deformer.Layers[idx3].Weight = 1f;

                ReleaseRuntimeMesh(deformer);
                var mesh = deformer.Deform(false);

                var expected = src[0] + disp1 * 0.25f + disp2 * 0.5f + disp3 * 1f;
                AssertApproximately(expected, mesh.vertices[0], 2e-3f);
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Lattice on Multi-Island Mesh
        // ========================================================================

        [Test]
        public void Lattice_OnMultiIslandMesh_DeformsBothIslands()
        {
            var mesh = TestMeshFactory.CreateConcentricCylinders(12, 6, 0.04f, 0.06f, 0.3f);
            var fixture = CreateFixtureFromMesh("Lattice_OnMultiIslandMesh", mesh);
            try
            {
                var deformer = fixture.Deformer;
                var settings = deformer.Layers[0].Settings;
                var src = deformer.SourceMesh.vertices;

                // Move a control point — should affect both islands
                settings.SetControlPointLocal(0,
                    settings.GetControlPointLocal(0) + new Vector3(0f, 0.1f, 0f));

                ReleaseRuntimeMesh(deformer);
                var runtimeMesh = deformer.Deform(false);
                var deformed = runtimeMesh.vertices;

                TestMeshFactory.GetConcentricCylinderRanges(12, 6,
                    out int innerStart, out int innerEnd, out int outerStart, out int outerEnd);

                // At least some vertices in each island should be affected
                bool innerMoved = false, outerMoved = false;
                for (int i = innerStart; i < innerEnd; i++)
                    if ((deformed[i] - src[i]).sqrMagnitude > Epsilon) { innerMoved = true; break; }
                for (int i = outerStart; i < outerEnd; i++)
                    if ((deformed[i] - src[i]).sqrMagnitude > Epsilon) { outerMoved = true; break; }

                Assert.That(innerMoved, Is.True, "Inner island should be affected by lattice");
                Assert.That(outerMoved, Is.True, "Outer island should be affected by lattice");
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Lattice Bounds Edge Cases
        // ========================================================================

        [Test]
        public void Lattice_BoundsProperty_CanBeModified()
        {
            var fixture = CreateFixture("Lattice_BoundsProperty_CanBeModified");
            try
            {
                var settings = fixture.Deformer.Layers[0].Settings;
                var origBounds = settings.LocalBounds;

                var newBounds = new Bounds(origBounds.center + Vector3.up * 0.1f,
                    origBounds.size * 2f);
                settings.LocalBounds = newBounds;

                AssertApproximately(newBounds.center, settings.LocalBounds.center, Epsilon);
                AssertApproximately(newBounds.size, settings.LocalBounds.size, Epsilon);

                // Deform should still work
                ReleaseRuntimeMesh(fixture.Deformer);
                fixture.Deformer.InvalidateCache();
                var mesh = fixture.Deformer.Deform(false);
                Assert.That(mesh, Is.Not.Null);
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Layer Name Edge Cases
        // ========================================================================

        [Test]
        public void AddLayer_DuplicateNames_BothExist()
        {
            var fixture = CreateFixture("AddLayer_DuplicateNames_BothExist");
            try
            {
                var deformer = fixture.Deformer;
                int idx1 = deformer.AddLayer("Same", MeshDeformerLayerType.Brush);
                int idx2 = deformer.AddLayer("Same", MeshDeformerLayerType.Brush);

                Assert.That(idx1, Is.Not.EqualTo(idx2));
                Assert.That(deformer.Layers.Count, Is.GreaterThanOrEqualTo(3));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void LayerName_EmptyString_UsesDefault()
        {
            var layer = new LatticeLayer();
            layer.Name = "";
            // Getting name should return something non-empty (fallback to "Layer")
            Assert.That(string.IsNullOrEmpty(layer.Name), Is.False);
        }

        // ========================================================================
        // Many Layers Stress Test
        // ========================================================================

        [Test]
        public void FiftyLayers_NoErrors()
        {
            var fixture = CreateFixture("FiftyLayers_NoErrors");
            try
            {
                var deformer = fixture.Deformer;

                for (int i = 0; i < 50; i++)
                {
                    int idx = deformer.AddLayer($"L{i}", MeshDeformerLayerType.Brush);
                    deformer.ActiveLayerIndex = idx;
                    deformer.EnsureDisplacementCapacity();
                    deformer.SetDisplacement(0, new Vector3(0.001f, 0f, 0f));
                }

                Assert.That(deformer.Layers.Count, Is.GreaterThanOrEqualTo(51));

                ReleaseRuntimeMesh(deformer);
                var mesh = deformer.Deform(false);
                Assert.That(mesh, Is.Not.Null);

                // All layers should contribute
                var src = deformer.SourceMesh.vertices;
                float totalDisp = (mesh.vertices[0] - src[0]).magnitude;
                Assert.That(totalDisp, Is.GreaterThan(0.04f),
                    "50 layers × 0.001 should produce visible displacement");
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Practical: Deform on High-Density Cylinder Mesh then BS Export
        // ========================================================================

        [Test]
        public void HighDensity_LatticePlusMask_BlendShapeOutput()
        {
            var mesh = TestMeshFactory.CreateCylinder(24, 32, 0.05f, 0.4f);
            var fixture = CreateFixtureFromMesh("HighDensity_LatticePlusMask_BS", mesh);
            try
            {
                var deformer = fixture.Deformer;
                int vertexCount = deformer.SourceMesh.vertexCount;
                var src = deformer.SourceMesh.vertices;

                Assert.That(vertexCount, Is.GreaterThanOrEqualTo(700));

                // Lattice: shift
                var settings = deformer.Layers[0].Settings;
                settings.SetControlPointLocal(0,
                    settings.GetControlPointLocal(0) + new Vector3(0f, 0.05f, 0f));

                // Brush with mask and BS output
                int brushIdx = deformer.AddLayer("MaskedBS", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = brushIdx;
                deformer.EnsureDisplacementCapacity();

                var layer = deformer.Layers[brushIdx];
                deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                layer.EnsureVertexMaskCapacity(vertexCount);

                for (int i = 0; i < vertexCount; i++)
                {
                    var radial = new Vector3(src[i].x, 0f, src[i].z).normalized;
                    deformer.SetDisplacement(i, -radial * 0.01f);
                    // Gradient mask by height
                    float mask = Mathf.InverseLerp(-0.2f, 0.2f, src[i].y);
                    layer.SetVertexMask(i, mask);
                }

                ReleaseRuntimeMesh(deformer);
                var result = deformer.Deform(false);
                Assert.That(result, Is.Not.Null);

                // BlendShape should exist with combined delta from all layers
                Assert.That(result.blendShapeCount, Is.GreaterThanOrEqualTo(1));

                // Vertices should remain at source (BlendShape output mode)
                for (int i = 0; i < vertexCount; i++)
                {
                    Assert.That((result.vertices[i] - src[i]).sqrMagnitude, Is.LessThan(1e-6f));
                }

                // Combined BlendShape should contain non-zero deltas (lattice + brush)
                var deltas = new Vector3[vertexCount];
                int lastShapeIdx = result.blendShapeCount - 1;
                int frameCount = result.GetBlendShapeFrameCount(lastShapeIdx);
                result.GetBlendShapeFrameVertices(lastShapeIdx, frameCount - 1, deltas, new Vector3[vertexCount], new Vector3[vertexCount]);
                bool anyDelta = false;
                for (int i = 0; i < vertexCount; i++)
                {
                    if (deltas[i].sqrMagnitude > Epsilon)
                    {
                        anyDelta = true;
                        break;
                    }
                }
                Assert.That(anyDelta, Is.True);
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Flip on Asymmetric Mesh (No Mirror Pairs)
        // ========================================================================

        [Test]
        public void FlipOnAsymmetricMesh_NoMirrorPairs_NoException()
        {
            // A mesh with no mirror vertex pairs
            var mesh = new Mesh { name = "Asymmetric" };
            mesh.vertices = new[]
            {
                new Vector3(0.5f, 0.3f, 0.1f),
                new Vector3(0.8f, 0.7f, 0.2f),
                new Vector3(0.6f, 0.9f, 0.4f)
            };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var fixture = CreateFixtureFromMesh("FlipOnAsymmetricMesh", mesh);
            try
            {
                var deformer = fixture.Deformer;
                int idx = deformer.AddLayer("NoMirror", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.1f, 0.2f, 0f));

                // Flip should not crash even without mirror pairs
                Assert.DoesNotThrow(() => deformer.FlipLayerByAxis(idx, 0));
                Assert.DoesNotThrow(() => deformer.FlipLayerByAxis(idx, 1));
                Assert.DoesNotThrow(() => deformer.FlipLayerByAxis(idx, 2));
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Deform Preserves Normals Count
        // ========================================================================

        [Test]
        public void Deform_NormalsCountMatchesVertexCount()
        {
            var fixture = CreateFixture("Deform_NormalsCountMatchesVertexCount");
            try
            {
                var deformer = fixture.Deformer;
                int idx = deformer.AddLayer("Normals", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = idx;
                deformer.EnsureDisplacementCapacity();
                deformer.SetDisplacement(0, new Vector3(0.1f, 0f, 0f));

                ReleaseRuntimeMesh(deformer);
                var mesh = deformer.Deform(false);

                Assert.That(mesh.normals.Length, Is.EqualTo(mesh.vertexCount));
            }
            finally { fixture.Dispose(); }
        }

        // ========================================================================
        // Helpers
        // ========================================================================

        private static Mesh CreateYSymmetricMesh()
        {
            var mesh = new Mesh { name = "YSymmetric" };
            mesh.vertices = new[]
            {
                new Vector3(0f,  1f, 0f), // 0: positive Y
                new Vector3(0f, -1f, 0f), // 1: negative Y (mirror of 0)
                new Vector3(1f,  0f, 0f), // 2: on axis
                new Vector3(-1f, 0f, 0f)  // 3: on axis
            };
            mesh.triangles = new[] { 0, 2, 1, 1, 3, 0 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateZSymmetricMesh()
        {
            var mesh = new Mesh { name = "ZSymmetric" };
            mesh.vertices = new[]
            {
                new Vector3(0f, 0f,  1f), // 0: positive Z
                new Vector3(0f, 0f, -1f), // 1: negative Z (mirror of 0)
                new Vector3(1f, 0f,  0f), // 2: on axis
                new Vector3(0f, 1f,  0f)  // 3: on axis
            };
            mesh.triangles = new[] { 0, 2, 1, 1, 3, 0 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

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
            mesh.name = "IntegrityTestMesh";
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
