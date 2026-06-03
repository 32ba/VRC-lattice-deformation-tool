#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Net._32Ba.LatticeDeformationTool;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class RuntimeCoreTests
    {
        [Test]
        public void LatticeNativeArrayUtility_CreateCopy_Vector3ArrayCopiesValues()
        {
            using var copy = LatticeNativeArrayUtility.CreateCopy(
                new[] { new Vector3(1f, 2f, 3f), new Vector3(-1f, 0.5f, 4f) },
                Allocator.TempJob);

            Assert.That(copy.Length, Is.EqualTo(2));
            Assert.That(copy[0], Is.EqualTo(new float3(1f, 2f, 3f)));
            Assert.That(copy[1], Is.EqualTo(new float3(-1f, 0.5f, 4f)));
        }

        [Test]
        public void LatticeNativeArrayUtility_CreateCopy_NullSourcesReturnEmptyArrays()
        {
            using var vectors = LatticeNativeArrayUtility.CreateCopy((Vector3[])null, Allocator.TempJob);
            using var ints = LatticeNativeArrayUtility.CreateCopy((int[])null, Allocator.TempJob);

            Assert.That(vectors.Length, Is.EqualTo(0));
            Assert.That(ints.Length, Is.EqualTo(0));
        }

        [Test]
        public void LatticeNativeArrayUtility_CreateFloat3Array_ClampsNegativeLengthToZero()
        {
            using var array = LatticeNativeArrayUtility.CreateFloat3Array(-10, Allocator.TempJob);

            Assert.That(array.Length, Is.EqualTo(0));
        }

        [Test]
        public void LatticeNativeArrayUtility_CopyFromManaged_CopiesAndValidatesInputs()
        {
            using var array = LatticeNativeArrayUtility.CreateFloat3Array(2, Allocator.TempJob);

            array.CopyFromManaged(new[] { Vector3.right, Vector3.up });

            Assert.That(array[0], Is.EqualTo(new float3(1f, 0f, 0f)));
            Assert.That(array[1], Is.EqualTo(new float3(0f, 1f, 0f)));
            Assert.That(() => array.CopyFromManaged(null), Throws.ArgumentNullException);
            Assert.That(
                () => array.CopyFromManaged(new[] { Vector3.one }),
                Throws.TypeOf<ArgumentException>().With.Message.EqualTo("Source and destination lengths must match for copy operations."));
        }

        [Test]
        public void LatticeNativeArrayUtility_CopyToManaged_CopiesVectorAndGenericArrays()
        {
            using var vectors = new NativeArray<float3>(
                new[] { new float3(1f, 2f, 3f), new float3(4f, 5f, 6f) },
                Allocator.TempJob);
            var vectorDest = new Vector3[2];

            vectors.CopyToManaged(vectorDest);

            Assert.That(vectorDest[0], Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(vectorDest[1], Is.EqualTo(new Vector3(4f, 5f, 6f)));

            using var ints = new NativeArray<int>(new[] { 1, 2, 3 }, Allocator.TempJob);
            var intDest = new int[3];
            ints.CopyToManaged(intDest);

            Assert.That(intDest, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(() => vectors.CopyToManaged((Vector3[])null), Throws.ArgumentNullException);
            Assert.That(
                () => ints.CopyToManaged(new int[2]),
                Throws.TypeOf<ArgumentException>().With.Message.EqualTo("Source and destination lengths must match for copy operations."));
        }

        [Test]
        public void LatticeNativeArrayUtility_CopyToManaged_GenericArrayValidatesNullDestination()
        {
            using var source = new NativeArray<int>(new[] { 1 }, Allocator.TempJob);

            Assert.That(() => source.CopyToManaged((int[])null), Throws.ArgumentNullException);
        }

        [Test]
        public void LatticeLayer_DefaultsClampAndNullBackFields()
        {
            var layer = new LatticeLayer();
            var type = typeof(LatticeLayer);

            layer.Name = " ";
            layer.Weight = 2f;
            layer.Settings = null;
            layer.BrushDisplacements = null;
            layer.VertexMask = null;

            Assert.That(layer.Name, Is.EqualTo("Layer"));
            Assert.That(layer.Weight, Is.EqualTo(1f));
            Assert.That(layer.Settings, Is.Not.Null);
            Assert.That(layer.EffectiveBlendShapeName, Is.EqualTo("Layer"));
            Assert.That(layer.BrushDisplacements, Is.Empty);
            Assert.That(layer.VertexMask, Is.Empty);
            Assert.That(layer.HasBrushDisplacements(), Is.False);
            Assert.That(layer.HasVertexMask(), Is.False);

            type.GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(layer, null);
            type.GetField("_brushDisplacements", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(layer, null);
            type.GetField("_vertexMask", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(layer, null);

            Assert.That(layer.Settings, Is.Not.Null);
            Assert.That(layer.BrushDisplacementCount, Is.EqualTo(0));
            Assert.That(layer.GetBrushDisplacement(-1), Is.EqualTo(Vector3.zero));
            Assert.That(layer.GetVertexMask(-1), Is.EqualTo(1f));
            layer.ClearBrushDisplacements();
            layer.AddBrushDisplacement(0, Vector3.one);
            layer.SetVertexMask(0, 0.5f);
            layer.ClearVertexMask();
        }

        [Test]
        public void LatticeLayer_BrushAndMaskOperations_PreserveAndClampValues()
        {
            var layer = new LatticeLayer();

            layer.EnsureBrushDisplacementCapacity(2);
            layer.SetBrushDisplacement(0, Vector3.right);
            layer.AddBrushDisplacement(1, Vector3.up);
            Assert.That(layer.HasBrushDisplacements(), Is.True);
            Assert.That(layer.GetBrushDisplacement(1), Is.EqualTo(Vector3.up));

            layer.EnsureBrushDisplacementCapacity(3);
            Assert.That(layer.GetBrushDisplacement(0), Is.EqualTo(Vector3.right));
            layer.ClearBrushDisplacements();
            Assert.That(layer.HasBrushDisplacements(), Is.False);

            layer.EnsureVertexMaskCapacity(2);
            layer.SetVertexMask(0, -1f);
            layer.SetVertexMask(1, 0.25f);
            Assert.That(layer.GetVertexMask(0), Is.EqualTo(0f));
            Assert.That(layer.HasVertexMask(), Is.True);

            layer.EnsureVertexMaskCapacity(3);
            Assert.That(layer.GetVertexMask(1), Is.EqualTo(0.25f));
            Assert.That(layer.GetVertexMask(2), Is.EqualTo(1f));
            layer.ClearVertexMask();
            Assert.That(layer.HasVertexMask(), Is.False);
        }

        [Test]
        public void LatticeDeformerCache_IsCompatibleWith_RejectsMismatchedInputs()
        {
            var asset = new LatticeAsset();
            asset.EnsureInitialized();
            var mesh = new Mesh { vertices = new[] { Vector3.zero } };
            var cache = new LatticeDeformerCache();
            var bounds = asset.LocalBounds;
            try
            {
                Assert.That(cache.IsCompatibleWith(null, mesh), Is.False);
                Assert.That(cache.IsCompatibleWith(asset, null), Is.False);
                Assert.That(cache.IsCompatibleWith(asset, mesh), Is.False);

                cache.Populate(
                    asset.GridSize,
                    bounds,
                    asset.Interpolation,
                    mesh.vertexCount + 1,
                    new[] { new LatticeCacheEntry() },
                    mesh.vertices);
                Assert.That(cache.IsCompatibleWith(asset, mesh), Is.False);

                cache.Populate(
                    asset.GridSize + Vector3Int.one,
                    bounds,
                    asset.Interpolation,
                    mesh.vertexCount,
                    new[] { new LatticeCacheEntry() },
                    mesh.vertices);
                Assert.That(cache.IsCompatibleWith(asset, mesh), Is.False);

                cache.Populate(
                    asset.GridSize,
                    bounds,
                    asset.Interpolation == LatticeInterpolationMode.Trilinear
                        ? LatticeInterpolationMode.CubicBernstein
                        : LatticeInterpolationMode.Trilinear,
                    mesh.vertexCount,
                    new[] { new LatticeCacheEntry() },
                    mesh.vertices);
                Assert.That(cache.IsCompatibleWith(asset, mesh), Is.False);

                cache.Populate(
                    asset.GridSize,
                    new Bounds(bounds.center + Vector3.one, bounds.size),
                    asset.Interpolation,
                    mesh.vertexCount,
                    new[] { new LatticeCacheEntry() },
                    mesh.vertices);
                Assert.That(cache.IsCompatibleWith(asset, mesh), Is.False);

                cache.Populate(
                    asset.GridSize,
                    bounds,
                    asset.Interpolation,
                    mesh.vertexCount,
                    new[] { new LatticeCacheEntry() },
                    mesh.vertices);
                Assert.That(cache.IsCompatibleWith(asset, mesh), Is.True);
                cache.Clear();
                Assert.That(cache.Entries, Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void WeightTransferSettingsData_DefaultAndClone_ReturnIndependentCopies()
        {
            var settings = WeightTransferSettingsData.Default;
            settings.maxTransferDistance = 0.25f;
            settings.normalAngleThreshold = 120f;
            settings.enableInpainting = false;
            settings.maxIterations = 222;
            settings.tolerance = 1e-4f;

            var clone = settings.Clone();

            Assert.That(clone, Is.Not.SameAs(settings));
            Assert.That(clone.maxTransferDistance, Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(clone.normalAngleThreshold, Is.EqualTo(120f).Within(1e-6f));
            Assert.That(clone.enableInpainting, Is.False);
            Assert.That(clone.maxIterations, Is.EqualTo(222));
            Assert.That(clone.tolerance, Is.EqualTo(1e-4f).Within(1e-8f));
        }

        [Test]
        public void LatticeAsset_EnsureInitialized_PopulatesDefaultGridControlPoints()
        {
            var asset = new LatticeAsset();

            asset.EnsureInitialized();

            Assert.That(asset.UseJobsAndBurst, Is.True);
            Assert.That(asset.ControlPointCount, Is.EqualTo(27));
            Assert.That(asset.ControlPointsLocal.Length, Is.EqualTo(27));
            Assert.That(asset.GetControlPointLocal(0), Is.EqualTo(new Vector3(-0.5f, -0.5f, -0.5f)));
            Assert.That(asset.GetControlPointLocal(26), Is.EqualTo(new Vector3(0.5f, 0.5f, 0.5f)));
            Assert.That(asset.GetControlPointLocal(-1), Is.EqualTo(Vector3.zero));
            Assert.That(asset.GetControlPointLocal(999), Is.EqualTo(Vector3.zero));
            Assert.That(asset.HasCustomizedControlPoints(), Is.False);
        }

        [Test]
        public void LatticeAsset_SetControlPointAndReset_TracksCustomization()
        {
            var asset = new LatticeAsset();
            asset.EnsureInitialized();

            asset.SetControlPointLocal(13, Vector3.one * 10f);
            asset.SetControlPointLocal(-1, Vector3.one);
            asset.SetControlPointLocal(999, Vector3.one);

            Assert.That(asset.HasCustomizedControlPoints(), Is.True);

            asset.ResetControlPoints();

            Assert.That(asset.HasCustomizedControlPoints(), Is.False);
            Assert.That(asset.GetControlPointLocal(13), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void LatticeAsset_ResizeGrid_ClampsAndResamplesExistingPoints()
        {
            var asset = new LatticeAsset();
            asset.EnsureInitialized();
            asset.SetControlPointLocal(26, new Vector3(2f, 2f, 2f));

            asset.GridSize = new Vector3Int(1, 4, 2);

            Assert.That(asset.GridSize, Is.EqualTo(new Vector3Int(2, 4, 2)));
            Assert.That(asset.ControlPointCount, Is.EqualTo(16));
            Assert.That(asset.ControlPointsLocal.Length, Is.EqualTo(16));
            Assert.That(asset.HasCustomizedControlPoints(1e-8f), Is.True);
        }

        [Test]
        public void LatticeAsset_RelaxInteriorControlPoints_HandlesEarlyReturnsAndMovesInterior()
        {
            var asset = new LatticeAsset();

            asset.RelaxInteriorControlPoints();

            asset.EnsureInitialized();
            asset.RelaxInteriorControlPoints(0);

            asset = new LatticeAsset();
            asset.EnsureInitialized();
            asset.SetControlPointLocal(12, Vector3.left);
            asset.SetControlPointLocal(14, Vector3.right);
            asset.SetControlPointLocal(13, Vector3.up * 10f);

            asset.RelaxInteriorControlPoints(1);

            Assert.That(asset.GetControlPointLocal(13).y, Is.LessThan(10f));
        }

        [Test]
        public void LatticeAsset_SerializationCallbacks_EnsureValidState()
        {
            var asset = new LatticeAsset();
            asset.GridSize = new Vector3Int(-10, 0, 1);

            ((ISerializationCallbackReceiver)asset).OnBeforeSerialize();
            ((ISerializationCallbackReceiver)asset).OnAfterDeserialize();

            Assert.That(asset.GridSize, Is.EqualTo(new Vector3Int(2, 2, 2)));
            Assert.That(asset.ControlPointsLocal.Length, Is.EqualTo(8));
        }

        [Test]
        public void LatticeAsset_PrivateHelpers_HandleDefensiveBranches()
        {
            var asset = new LatticeAsset();
            var type = typeof(LatticeAsset);

            type.GetField("_controlPointsLocal", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(asset, null);
            Assert.That(asset.HasCustomizedControlPoints(), Is.False);

            type.GetField("_gridSize", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(asset, Vector3Int.zero);
            type.GetField("_controlPointsLocal", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(asset, Array.Empty<Vector3>());
            Assert.That(asset.HasCustomizedControlPoints(), Is.False);

            type.GetField("_controlPointsLocal", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(asset, null);
            asset.RelaxInteriorControlPoints();

            Assert.That(
                () => InvokePrivate(
                    asset,
                    "ResampleControlPointsWithJobs",
                    null,
                    new Vector3Int(1, 1, 1),
                    new Vector3Int(1, 1, 1),
                    new Vector3[1]),
                Throws.TargetInvocationException.With.InnerException.TypeOf<ArgumentNullException>());

            Assert.That(
                () => InvokePrivate(
                    asset,
                    "ResampleControlPointsWithJobs",
                    new[] { Vector3.zero },
                    new Vector3Int(1, 1, 1),
                    new Vector3Int(1, 1, 1),
                    null),
                Throws.TargetInvocationException.With.InnerException.TypeOf<ArgumentNullException>());

            Assert.That(
                () => InvokePrivate(
                    asset,
                    "ResampleControlPointsWithJobs",
                    Array.Empty<Vector3>(),
                    new Vector3Int(1, 1, 1),
                    new Vector3Int(1, 1, 1),
                    new Vector3[1]),
                Throws.TargetInvocationException.With.InnerException.TypeOf<ArgumentException>());

            Assert.That(
                () => InvokePrivate(
                    asset,
                    "ResampleControlPointsWithJobs",
                    new[] { Vector3.zero },
                    new Vector3Int(1, 1, 1),
                    new Vector3Int(1, 1, 1),
                    Array.Empty<Vector3>()),
                Throws.TargetInvocationException.With.InnerException.TypeOf<ArgumentException>());

            Assert.That(
                () => InvokePrivate(
                    asset,
                    "PopulateControlPointsWithJobs",
                    Vector3.zero,
                    Vector3.one,
                    new Vector3Int(2, 2, 2),
                    new Vector3[1]),
                Throws.TargetInvocationException.With.InnerException.TypeOf<ArgumentException>());
        }

        [Test]
        public void LatticeAsset_PrivateSamplingAndPopulateHelpers_ReturnExpectedValues()
        {
            var asset = new LatticeAsset();
            var type = typeof(LatticeAsset);
            var points = new[]
            {
                Vector3.zero,
                Vector3.right,
                Vector3.up,
                Vector3.one,
                Vector3.forward,
                Vector3.right + Vector3.forward,
                Vector3.up + Vector3.forward,
                Vector3.one + Vector3.forward
            };

            var index = (int)type.GetMethod("Index", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { new Vector3Int(2, 2, 2), 1, 1, 1 });
            var sampled = (Vector3)type.GetMethod("SampleControlPoints", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { points, new Vector3Int(2, 2, 2), 0.5f, 0.5f, 0.5f });

            Assert.That(index, Is.EqualTo(7));
            Assert.That(sampled.x, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(sampled.y, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(sampled.z, Is.EqualTo(0.75f).Within(1e-5f));

            var target = new Vector3[8];
            InvokePrivate(
                asset,
                "PopulateControlPointsWithJobs",
                new Vector3(-1f, -1f, -1f),
                Vector3.one * 2f,
                new Vector3Int(2, 2, 2),
                target);

            Assert.That(target[0], Is.EqualTo(new Vector3(-1f, -1f, -1f)));
            Assert.That(target[7], Is.EqualTo(new Vector3(1f, 1f, 1f)));
        }

        [Test]
        public void LatticeAsset_EdgeBranches_HandleEmptySmallAndUninitializedStates()
        {
            var asset = new LatticeAsset();
            var type = typeof(LatticeAsset);

            type.GetField("_controlPointsLocal", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(asset, null);
            Assert.That(asset.HasCustomizedControlPoints(), Is.False);
            asset.RelaxInteriorControlPoints();

            asset.GridSize = new Vector3Int(2, 2, 2);
            asset.EnsureInitialized();
            asset.RelaxInteriorControlPoints(2);

            var emptyTarget = Array.Empty<Vector3>();
            InvokePrivate(asset, "PopulateControlPointsWithJobs", emptyTarget);

            type.GetField("_gridSize", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(asset, Vector3Int.zero);
            InvokePrivate(asset, "EnsureControlPointCapacity");
            InvokePrivate(asset, "PopulateControlPoints");
            Assert.That(asset.ControlPointCount, Is.EqualTo(0));

            type.GetField("_gridSize", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(asset, new Vector3Int(2, 2, 2));
            type.GetField("_controlPointsLocal", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(asset, new Vector3[8]);
            InvokePrivate(asset, "EnsureControlPointCapacity");
            Assert.That(asset.ControlPointsLocal[7], Is.EqualTo(new Vector3(0.5f, 0.5f, 0.5f)));

            Assert.That(
                () => InvokePrivate(
                    asset,
                    "ResampleControlPointsWithJobs",
                    Array.Empty<Vector3>(),
                    Vector3Int.zero,
                    Vector3Int.zero,
                    Array.Empty<Vector3>()),
                Throws.Nothing);
        }

        [Test]
        public void LatticeDeformer_GroupAndLayerApi_HandlesInvalidAndActiveIndexTransitions()
        {
            var go = new GameObject("runtime-core-deformer");
            var mesh = CreateSymmetricQuadMesh("RuntimeCoreQuad");
            try
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var deformer = go.AddComponent<LatticeDeformer>();

                Assert.That(deformer.GroupCount, Is.GreaterThanOrEqualTo(0));
                Assert.That(deformer.RemoveGroup(-1), Is.False);

                int initialGroups = deformer.GroupCount;
                int secondGroup = deformer.AddGroup("Group");
                int thirdGroup = deformer.AddGroup("Group");
                Assert.That(secondGroup, Is.EqualTo(initialGroups));
                Assert.That(thirdGroup, Is.EqualTo(initialGroups + 1));
                Assert.That(deformer.GroupCount, Is.EqualTo(initialGroups + 2));

                deformer.ActiveGroupIndex = 99;
                Assert.That(deformer.ActiveGroupIndex, Is.EqualTo(deformer.GroupCount - 1));
                Assert.That(deformer.RemoveGroup(secondGroup), Is.True);
                Assert.That(deformer.ActiveGroupIndex, Is.EqualTo(deformer.GroupCount - 1));

                int brushLayer = deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
                Assert.That(brushLayer, Is.GreaterThanOrEqualTo(0));
                Assert.That(deformer.ActiveLayerType, Is.EqualTo(MeshDeformerLayerType.Brush));
                deformer.SetDisplacement(0, Vector3.right);
                deformer.AddDisplacement(1, Vector3.up);
                Assert.That(deformer.HasDisplacements(), Is.True);
                Assert.That(deformer.GetDisplacement(1), Is.EqualTo(Vector3.up));

                int duplicateLayer = deformer.DuplicateLayer(brushLayer);
                Assert.That(duplicateLayer, Is.GreaterThan(brushLayer));
                Assert.That(deformer.MoveLayer(duplicateLayer, 0), Is.True);
                Assert.That(deformer.RemoveLayer(brushLayer), Is.True);
                Assert.That(deformer.InsertLayer(null), Is.EqualTo(-1));
                Assert.That(deformer.DuplicateLayer(-1), Is.EqualTo(-1));
                Assert.That(deformer.ComputeLayeredStateHash(), Is.Not.EqualTo(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LatticeDeformer_BrushLayerSplitAndFlip_ModifySymmetricDisplacements()
        {
            var go = new GameObject("runtime-core-brush-symmetry");
            var mesh = CreateSymmetricQuadMesh("RuntimeCoreSymmetricQuad");
            try
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var deformer = go.AddComponent<LatticeDeformer>();
                int layerIndex = deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);

                deformer.SetDisplacement(0, Vector3.left);
                deformer.SetDisplacement(1, Vector3.right);
                deformer.SetDisplacement(2, Vector3.up);
                deformer.SetDisplacement(3, Vector3.down);

                deformer.SplitLayerByAxis(layerIndex, 0, true);
                Assert.That(deformer.GetDisplacement(0), Is.EqualTo(Vector3.zero));
                Assert.That(deformer.GetDisplacement(1), Is.EqualTo(Vector3.right));

                deformer.FlipLayerByAxis(layerIndex, 0);
                Assert.That(deformer.GetDisplacement(0), Is.EqualTo(Vector3.left));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LatticeDeformer_ImportBlendShapeAndNames_UsesSourceMeshFrames()
        {
            var go = new GameObject("runtime-core-blendshape");
            var mesh = CreateSymmetricQuadMesh("RuntimeCoreBlendShapeQuad");
            try
            {
                var deltas = Enumerable.Repeat(Vector3.forward, mesh.vertexCount).ToArray();
                mesh.AddBlendShapeFrame(
                    "Smile",
                    100f,
                    deltas,
                    new Vector3[mesh.vertexCount],
                    new Vector3[mesh.vertexCount]);

                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var deformer = go.AddComponent<LatticeDeformer>();

                Assert.That(deformer.GetSourceBlendShapeNames(), Is.EqualTo(new[] { "Smile" }));
                Assert.That(deformer.ImportBlendShapeAsLayer(-1), Is.EqualTo(-1));
                int layer = deformer.ImportBlendShapeAsLayer(0);
                Assert.That(layer, Is.GreaterThanOrEqualTo(0));
                deformer.ActiveLayerIndex = layer;
                Assert.That(deformer.ActiveLayerType, Is.EqualTo(MeshDeformerLayerType.Brush));
                Assert.That(deformer.GetDisplacement(0), Is.EqualTo(Vector3.forward));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LatticeDeformer_LightweightPropertiesAndGuards_ReturnExpectedDefaults()
        {
            var go = new GameObject("runtime-core-lightweight");
            try
            {
                var deformer = go.AddComponent<LatticeDeformer>();

                deformer.Settings = null;
                Assert.That(deformer.Settings, Is.Not.Null);
                Assert.That(deformer.RuntimeMesh, Is.Null);
                Assert.That(deformer.SourceMesh, Is.Null);
                Assert.That(deformer.RecalculateBoneWeights, Is.False);
                deformer.RecalculateBoneWeights = true;
                Assert.That(deformer.RecalculateBoneWeights, Is.True);
                Assert.That(deformer.IsEditingBaseLayer, Is.False);
                Assert.That(deformer.EffectiveBlendShapeName, Is.EqualTo(go.name));

                deformer.WeightTransferSettings = null;
                Assert.That(deformer.WeightTransferSettings, Is.Not.Null);

                Assert.That(deformer.Displacements, Is.Empty);
                Assert.That(deformer.DisplacementCount, Is.EqualTo(0));
                Assert.That(deformer.HasDisplacements(), Is.False);
                Assert.That(deformer.GetDisplacement(0), Is.EqualTo(Vector3.zero));
                Assert.That(deformer.GetSourceBlendShapeNames(), Is.Empty);
                Assert.That(deformer.IsLayerStructurallyCompatible(-1), Is.False);
                deformer.SyncLayerStructuresToBase(resetControlPoints: true);

                deformer.AlignAutoInitialized = true;
                Assert.That(deformer.AlignAutoInitialized, Is.True);
                deformer.ManualScaleProxy = new Vector3(-1f, 0f, 2f);
                Assert.That(deformer.ManualScaleProxy.x, Is.EqualTo(0.0001f).Within(1e-7f));
                Assert.That(deformer.ManualScaleProxy.y, Is.EqualTo(0.0001f).Within(1e-7f));
                Assert.That(deformer.ManualScaleProxy.z, Is.EqualTo(2f).Within(1e-7f));

                Assert.That(deformer.Deform(assignToRenderer: false), Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LatticeDeformer_MeshTransform_ReturnsRendererTransforms()
        {
            var meshGo = new GameObject("mesh-transform-mesh");
            var skinnedGo = new GameObject("mesh-transform-skinned");
            try
            {
                var meshDeformer = meshGo.AddComponent<LatticeDeformer>();
                var meshFilter = meshGo.AddComponent<MeshFilter>();
                meshDeformer.Reset();
                Assert.That(meshDeformer.MeshTransform, Is.SameAs(meshFilter.transform));

                var skinnedDeformer = skinnedGo.AddComponent<LatticeDeformer>();
                var skinned = skinnedGo.AddComponent<SkinnedMeshRenderer>();
                skinnedDeformer.Reset();
                Assert.That(skinnedDeformer.MeshTransform, Is.SameAs(skinned.transform));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(meshGo);
                UnityEngine.Object.DestroyImmediate(skinnedGo);
            }
        }

        [Test]
        public void LatticeDeformer_LayerIndexTransitions_CoverRemoveAndMoveBranches()
        {
            var go = new GameObject("runtime-core-layer-transitions");
            try
            {
                var deformer = go.AddComponent<LatticeDeformer>();
                deformer.AddLayer("A", MeshDeformerLayerType.Lattice);
                deformer.AddLayer("B", MeshDeformerLayerType.Lattice);
                deformer.AddLayer("C", MeshDeformerLayerType.Lattice);

                deformer.ActiveLayerIndex = 2;
                Assert.That(deformer.RemoveLayer(0), Is.True);
                Assert.That(deformer.ActiveLayerIndex, Is.EqualTo(1));

                deformer.AddLayer("D", MeshDeformerLayerType.Lattice);
                deformer.ActiveLayerIndex = 0;
                Assert.That(deformer.MoveLayer(2, 0), Is.True);
                Assert.That(deformer.ActiveLayerIndex, Is.EqualTo(1));

                deformer.ActiveLayerIndex = 2;
                Assert.That(deformer.MoveLayer(0, 2), Is.True);
                Assert.That(deformer.ActiveLayerIndex, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LatticeDeformer_LatticeSplitAndFlip_CoverYAxisAndZAxis()
        {
            var go = new GameObject("runtime-core-lattice-axis");
            try
            {
                var deformer = go.AddComponent<LatticeDeformer>();
                int layer = deformer.AddLayer("Lattice", MeshDeformerLayerType.Lattice);
                deformer.ActiveLayerIndex = layer;
                deformer.EditingSettings.SetControlPointLocal(0, deformer.EditingSettings.GetControlPointLocal(0) + Vector3.one);

                deformer.SplitLayerByAxis(layer, 1, keepPositiveSide: true);
                deformer.FlipLayerByAxis(layer, 1);
                deformer.FlipLayerByAxis(layer, 2);

                Assert.That(deformer.EditingSettings.ControlPointCount, Is.GreaterThan(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LatticeDeformer_PrivateContributionGuards_ReturnWithoutMutating()
        {
            var go = new GameObject("runtime-core-private-guards");
            try
            {
                var deformer = go.AddComponent<LatticeDeformer>();
                var source = new[] { Vector3.zero };
                var deformed = new[] { Vector3.one };
                var brushLayer = new LatticeLayer();
                brushLayer.SetType(MeshDeformerLayerType.Brush);

                typeof(LatticeDeformer)
                    .GetMethod("TryApplyBrushLayerContribution", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { null, source, deformed });
                Assert.That(deformed[0], Is.EqualTo(Vector3.one));

                typeof(LatticeDeformer)
                    .GetMethod("TryApplyBrushLayerContribution", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { brushLayer, source, deformed });
                Assert.That(deformed[0], Is.EqualTo(Vector3.one));

                InvokePrivate(
                    deformer,
                    "TryApplyLatticeLayerContribution",
                    null,
                    source,
                    deformed);
                Assert.That(deformed[0], Is.EqualTo(Vector3.one));

                var latticeLayer = new LatticeLayer();
                InvokePrivate(
                    deformer,
                    "TryApplyLatticeLayerContribution",
                    latticeLayer,
                    source,
                    deformed);
                Assert.That(deformed[0], Is.EqualTo(Vector3.one));

                typeof(LatticeDeformer)
                    .GetField("_cache", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, new LatticeDeformerCache());
                InvokePrivate(
                    deformer,
                    "TryApplyLatticeLayerContribution",
                    latticeLayer,
                    source,
                    deformed);
                Assert.That(deformed[0], Is.EqualTo(Vector3.one));

                var sourceMesh = new Mesh { name = "RuntimeCoreCacheMismatch" };
                sourceMesh.vertices = source;
                sourceMesh.triangles = Array.Empty<int>();
                sourceMesh.RecalculateBounds();
                try
                {
                    var cache = new LatticeDeformerCache();
                    var settings = latticeLayer.Settings;
                    cache.Populate(
                        settings.GridSize,
                        settings.LocalBounds,
                        settings.Interpolation,
                        sourceMesh.vertexCount,
                        new[] { new LatticeCacheEntry(), new LatticeCacheEntry() },
                        source);
                    typeof(LatticeDeformer)
                        .GetField("_sourceMesh", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(deformer, sourceMesh);
                    typeof(LatticeDeformer)
                        .GetField("_cache", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(deformer, cache);
                    InvokePrivate(
                        deformer,
                        "TryApplyLatticeLayerContribution",
                        latticeLayer,
                        source,
                        deformed);
                    Assert.That(deformed[0], Is.EqualTo(Vector3.one));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(sourceMesh);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LatticeDeformer_PrivateNamingCloneHashAndBufferHelpers_CoverPureBranches()
        {
            var go = new GameObject("runtime-core-private-pure");
            try
            {
                var deformer = go.AddComponent<LatticeDeformer>();
                deformer.AddLayer(null, MeshDeformerLayerType.Lattice);
                deformer.AddLayer(null, MeshDeformerLayerType.Brush);
                deformer.AddGroup(null);

                Assert.That(
                    InvokePrivate(deformer, "GenerateNextLayerName", MeshDeformerLayerType.Lattice),
                    Is.TypeOf<string>());
                Assert.That(
                    InvokePrivate(deformer, "GenerateNextLayerName", MeshDeformerLayerType.Brush),
                    Is.TypeOf<string>());
                Assert.That(
                    InvokePrivate(deformer, "GenerateNextGroupName"),
                    Is.TypeOf<string>());

                var clone = InvokeStaticPrivate<LatticeAsset>("CloneSettings", new object[] { null });
                Assert.That(clone.ControlPointCount, Is.GreaterThan(0));
                var neutral = InvokeStaticPrivate<LatticeAsset>("CreateNeutralLayerSettings", clone);
                Assert.That(neutral.HasCustomizedControlPoints(), Is.False);

                Assert.That(InvokeStaticPrivate<int>("HashAssetState", new object[] { null }), Is.EqualTo(0));
                Assert.That(InvokeStaticPrivate<int>("HashDisplacementState", new object[] { null }), Is.EqualTo(0));
                Assert.That(InvokeStaticPrivate<int>("HashMaskState", new object[] { null }), Is.EqualTo(0));
                Assert.That(InvokeStaticPrivate<int>("HashMaskState", Array.Empty<float>()), Is.EqualTo(0));
                Assert.That(InvokeStaticPrivate<int>("HashDisplacementState", new[] { Vector3.one }), Is.Not.EqualTo(0));
                Assert.That(InvokeStaticPrivate<int>("HashMaskState", new[] { 0.25f, 1f }), Is.Not.EqualTo(0));

                InvokePrivate(deformer, "EnsureControlBuffer", 0);
                InvokePrivate(deformer, "EnsureControlBuffer", 3);

                LatticeDeformer.CollectControlPointsLocal(null, Span<Vector3>.Empty);
                LatticeDeformer.CollectControlPointsLocal(clone, Span<Vector3>.Empty);

                var controlPoints = new Vector3[clone.ControlPointCount];
                LatticeDeformer.CollectControlPointsLocal(clone, controlPoints);
                Assert.That(controlPoints[0], Is.EqualTo(clone.GetControlPointLocal(0)));
                Assert.That(
                    () => LatticeDeformer.CollectControlPointsLocal(clone, new Vector3[controlPoints.Length + 1]),
                    Throws.InvalidOperationException);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LatticeDeformer_PrivateRuntimeMeshHelpers_HandleNullAndAssignRestore()
        {
            var emptyGo = new GameObject("runtime-core-runtime-mesh-empty");
            var go = new GameObject("runtime-core-runtime-mesh");
            var mesh = CreateSymmetricQuadMesh("RuntimeCoreRuntimeMesh");
            try
            {
                var emptyDeformer = emptyGo.AddComponent<LatticeDeformer>();
                Assert.That(InvokePrivate(emptyDeformer, "AcquireRuntimeMesh", false), Is.Null);

                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var deformer = go.AddComponent<LatticeDeformer>();
                deformer.Reset();

                InvokePrivate(deformer, "CacheSourceMesh");
                var runtime = InvokePrivate(deformer, "AcquireRuntimeMesh", true) as Mesh;
                Assert.That(runtime, Is.Not.Null);
                Assert.That(filter.sharedMesh, Is.SameAs(runtime));

                deformer.RestoreOriginalMesh();
                Assert.That(filter.sharedMesh, Is.SameAs(mesh));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(emptyGo);
            }
        }

        [Test]
        public void LatticeDeformer_PrivateCacheAndDeformHelpers_CoverManagedValidationAndResults()
        {
            var go = new GameObject("runtime-core-cache-helpers");
            try
            {
                var deformer = go.AddComponent<LatticeDeformer>();

                Assert.That(
                    () => InvokePrivate(deformer, "DeformWithJobs", null, new[] { Vector3.zero }),
                    Throws.TargetInvocationException.With.InnerException.TypeOf<ArgumentException>());
                Assert.That(
                    () => InvokePrivate(deformer, "DeformWithJobs", Array.Empty<LatticeCacheEntry>(), new[] { Vector3.zero }),
                    Throws.TargetInvocationException.With.InnerException.TypeOf<ArgumentException>());
                Assert.That(
                    () => InvokePrivate(deformer, "DeformWithJobs", new[] { new LatticeCacheEntry() }, null),
                    Throws.TargetInvocationException.With.InnerException.TypeOf<ArgumentException>());

                var entry = new LatticeCacheEntry
                {
                    Corner0 = 0,
                    Corner1 = 1,
                    Corner2 = 2,
                    Corner3 = 3,
                    Corner4 = 4,
                    Corner5 = 5,
                    Corner6 = 6,
                    Corner7 = 7,
                    Weights0 = new Unity.Mathematics.float4(0f, 0f, 0f, 0f),
                    Weights1 = new Unity.Mathematics.float4(0f, 0f, 0f, 1f)
                };
                var controlPoints = new[]
                {
                    Vector3.zero,
                    Vector3.right,
                    Vector3.up,
                    Vector3.one,
                    Vector3.forward,
                    Vector3.right + Vector3.forward,
                    Vector3.up + Vector3.forward,
                    Vector3.one + Vector3.forward
                };

                var deformed = (Vector3[])InvokePrivate(deformer, "DeformWithJobs", new[] { entry }, controlPoints);
                Assert.That(deformed[0], Is.EqualTo(Vector3.one + Vector3.forward));

                Assert.That(
                    () => InvokePrivate(
                        deformer,
                        "BuildCacheWithJobs",
                        new Vector3Int(2, 2, 2),
                        new Bounds(Vector3.zero, Vector3.one),
                        null),
                    Throws.TargetInvocationException.With.InnerException.TypeOf<ArgumentException>());

                var entries = (LatticeCacheEntry[])InvokePrivate(
                    deformer,
                    "BuildCacheWithJobs",
                    new Vector3Int(2, 2, 2),
                    new Bounds(Vector3.zero, Vector3.one),
                    new[] { Vector3.zero, Vector3.one });
                Assert.That(entries.Length, Is.EqualTo(2));
                Assert.That(entries[0].Corner0, Is.EqualTo(0));
                Assert.That(entries[1].Corner7, Is.EqualTo(7));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LatticeDeformer_PrivateMathHelpers_ReturnExpectedValues()
        {
            var normalized = InvokeStaticPrivate<Vector3>(
                "CalculateNormalizedCoordinate",
                new Bounds(Vector3.zero, new Vector3(2f, 0f, 4f)),
                new Vector3(1f, 5f, -2f));
            Assert.That(normalized.x, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(normalized.y, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(normalized.z, Is.EqualTo(0f).Within(1e-6f));

            var trilinear = InvokeStaticPrivate<LatticeCacheEntry>(
                "BuildTrilinearEntry",
                new Vector3Int(2, 2, 2),
                new Vector3(1f, 1f, 1f));
            Assert.That(trilinear.Corner7, Is.EqualTo(7));
            Assert.That(trilinear.Weights1.w, Is.EqualTo(1f).Within(1e-6f));

            var bounds = InvokeStaticPrivate<Bounds>(
                "TransformBounds",
                Matrix4x4.TRS(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 0f, 90f), new Vector3(2f, 3f, 4f)),
                new Bounds(Vector3.zero, new Vector3(2f, 4f, 6f)));
            Assert.That(bounds.center, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(bounds.size.x, Is.EqualTo(12f).Within(1e-5f));
            Assert.That(bounds.size.y, Is.EqualTo(4f).Within(1e-5f));
            Assert.That(bounds.size.z, Is.EqualTo(24f).Within(1e-5f));
        }

        [Test]
        public void LatticeDeformer_GroupSettingsAndCacheGuards_CoverRemainingPureBranches()
        {
            var go = new GameObject("runtime-core-more-branches");
            var mesh = new Mesh { name = "EmptyCacheMesh" };
            try
            {
                var deformer = go.AddComponent<LatticeDeformer>();

                deformer.AddGroup("empty");
                deformer.Settings = new LatticeAsset();
                Assert.That(deformer.Layers.Count, Is.EqualTo(1));

                deformer.AddGroup("remove-active");
                int active = deformer.ActiveGroupIndex;
                Assert.That(deformer.RemoveGroup(active), Is.True);

                deformer.AddGroup("empty-type");
                Assert.That(deformer.ActiveLayerType, Is.EqualTo(MeshDeformerLayerType.Lattice));

                typeof(LatticeDeformer)
                    .GetField("_weightTransferSettings", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, null);
                Assert.That(deformer.WeightTransferSettings, Is.Not.Null);

                InvokePrivate(deformer, "EnsureCache", new object[] { null });
                Assert.That(
                    InvokePrivate(deformer, "RebuildCache", null, mesh),
                    Is.EqualTo(false));

                var invalidGrid = new LatticeAsset();
                typeof(LatticeAsset)
                    .GetField("_gridSize", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(invalidGrid, Vector3Int.one);
                Assert.That(InvokePrivate(deformer, "RebuildCache", invalidGrid, mesh), Is.EqualTo(false));

                var validSettings = new LatticeAsset();
                validSettings.EnsureInitialized();
                Assert.That(InvokePrivate(deformer, "RebuildCache", validSettings, mesh), Is.EqualTo(false));

                typeof(LatticeDeformer)
                    .GetField("_cache", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, null);
                Assert.That(InvokePrivate(deformer, "EnsureCache", validSettings), Is.EqualTo(false));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LatticeDeformer_SplitFlipAndMigrationGuards_CoverLegacyBranches()
        {
            var go = new GameObject("runtime-core-legacy-branches");
            var mesh = CreateSymmetricQuadMesh("RuntimeCoreLegacyBranches");
            var emptyMesh = new Mesh { name = "RuntimeCoreEmptyMesh" };
            try
            {
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var deformer = go.AddComponent<LatticeDeformer>();
                int brushIndex = deformer.AddLayer("brush", MeshDeformerLayerType.Brush);
                int latticeIndex = deformer.AddLayer("lattice", MeshDeformerLayerType.Lattice);

                filter.sharedMesh = null;
                deformer.SplitLayerByAxis(brushIndex, 0, true);
                deformer.FlipLayerByAxis(brushIndex, 0);
                filter.sharedMesh = mesh;

                deformer.SplitLayerByAxis(brushIndex, 0, true);
                deformer.FlipLayerByAxis(brushIndex, 0);
                deformer.Layers[brushIndex].BrushDisplacements = new[] { Vector3.one };
                typeof(LatticeDeformer)
                    .GetField("_runtimeMesh", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, mesh);
                deformer.SplitLayerByAxis(brushIndex, 0, true);
                deformer.FlipLayerByAxis(brushIndex, 0);
                typeof(LatticeDeformer)
                    .GetField("_runtimeMesh", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, null);

                deformer.Layers[brushIndex].BrushDisplacements = new Vector3[mesh.vertexCount];
                deformer.SplitLayerByAxis(brushIndex, 2, false);
                deformer.FlipLayerByAxis(brushIndex, 2);
                deformer.SplitLayerByAxis(latticeIndex, 0, true);
                deformer.FlipLayerByAxis(latticeIndex, 0);

                filter.sharedMesh = emptyMesh;
                Assert.That(deformer.Deform(assignToRenderer: false), Is.Null);

                filter.sharedMesh = mesh;
                Assert.That(deformer.Deform(assignToRenderer: false), Is.Not.Null);
                typeof(LatticeDeformer)
                    .GetField("_lastBlendShapeHash", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, 123);
                Assert.That(deformer.Deform(assignToRenderer: false), Is.Not.Null);

                typeof(LatticeDeformer)
                    .GetField("_cache", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, null);
                deformer.InvalidateCache();

                typeof(LatticeDeformer)
                    .GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, null);
                InvokePrivate(deformer, "EnsureLayers");
                Assert.That(deformer.GroupCount, Is.GreaterThan(0));

                typeof(LatticeDeformer)
                    .GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, null);
                InvokePrivate(deformer, "EnsureSettings");
                Assert.That(deformer.Settings, Is.Not.Null);

                deformer.Settings.SetControlPointLocal(0, deformer.Settings.GetControlPointLocal(0) + Vector3.right);
                typeof(LatticeDeformer)
                    .GetField("_sourceMesh", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, mesh);
                typeof(LatticeDeformer)
                    .GetField("_hasInitializedFromSource", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, false);
                InvokePrivate(deformer, "TryAutoConfigureSettings");

                var legacyLayer = new LatticeLayer { Name = "" };
                var legacyLayers = new List<LatticeLayer> { null, legacyLayer };
                typeof(LatticeDeformer)
                    .GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, new List<DeformerGroup>());
                typeof(LatticeDeformer)
                    .GetField("_layers", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, legacyLayers);
                typeof(LatticeDeformer)
                    .GetField("_layerModelVersion", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, 0);
                typeof(LatticeDeformer)
                    .GetField("_activeLayerIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, 0);

                Assert.That(InvokePrivate(deformer, "TryMigrateLegacyBaseToLayerStructure"), Is.EqualTo(true));
                Assert.That(InvokePrivate(deformer, "TryMigrateLayersToGroupStructure"), Is.EqualTo(true));
                Assert.That(deformer.GroupCount, Is.EqualTo(1));

                typeof(LatticeDeformer)
                    .GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, new List<DeformerGroup>());
                typeof(LatticeDeformer)
                    .GetField("_layers", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, new List<LatticeLayer>());
                typeof(LatticeDeformer)
                    .GetField("_layerModelVersion", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, 2);
                Assert.That(InvokePrivate(deformer, "TryMigrateLayersToGroupStructure"), Is.EqualTo(false));

                deformer.AddLayer("Lattice Layer", MeshDeformerLayerType.Lattice);
                deformer.AddLayer("Lattice Layer 1", MeshDeformerLayerType.Lattice);
                Assert.That(
                    InvokePrivate(deformer, "GenerateNextLayerName", MeshDeformerLayerType.Lattice),
                    Is.EqualTo("Lattice Layer 2"));

                typeof(LatticeDeformer)
                    .GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, new List<DeformerGroup>());
                typeof(LatticeDeformer)
                    .GetField("_layers", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, new List<LatticeLayer>());
                typeof(LatticeDeformer)
                    .GetField("_layerModelVersion", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, 0);

                Assert.That(InvokePrivate(deformer, "TryMigrateLegacyBaseToLayerStructure"), Is.EqualTo(true));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(emptyMesh);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static object InvokePrivate(object target, string methodName, params object[] args)
        {
            var method = target.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .First(m => m.Name == methodName &&
                            m.GetParameters().Length == args.Length &&
                            m.GetParameters()
                                .Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i]))
                                .All(match => match));
            return method.Invoke(target, args);
        }

        private static T InvokeStaticPrivate<T>(string methodName, params object[] args)
        {
            var method = typeof(LatticeDeformer)
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .First(m => m.Name == methodName &&
                            m.GetParameters().Length == args.Length &&
                            m.GetParameters()
                                .Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i]))
                                .All(match => match));
            return (T)method.Invoke(null, args);
        }

        private static Mesh CreateSymmetricQuadMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = new[]
            {
                new Vector3(-1f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(-1f, 1f, 0f),
                new Vector3(1f, 1f, 0f)
            };
            mesh.normals = Enumerable.Repeat(Vector3.forward, 4).ToArray();
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
#endif
