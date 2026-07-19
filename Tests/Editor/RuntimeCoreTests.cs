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
        public void BrushCapacity_InvalidMaskDoesNotAllocateDisplacements()
        {
            var layer = new LatticeLayer();
            layer.SetType(MeshDeformerLayerType.Brush);
            layer.BrushDisplacements = Array.Empty<Vector3>();
            layer.VertexMask = new[] { 0.25f };

            Assert.That(layer.TryEnsureBrushDataCapacityPreservingExisting(3), Is.False);
            Assert.That(layer.BrushDisplacements, Is.Empty,
                "Fail-closed validation must not replace the serialized empty payload.");
            Assert.That(layer.VertexMask, Is.EqualTo(new[] { 0.25f }));
        }

        [Test]
        public void NeutralLayerClone_ClearsLegacyWorldSpaceMarker()
        {
            var source = new LatticeAsset();
            source.EnsureInitialized();
            typeof(LatticeAsset)
                .GetField("_applySpace", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(source, 1);

            var neutral = InvokeStaticPrivate<LatticeAsset>("CreateNeutralLayerSettings", source);

            Assert.That(neutral.LegacyApplySpaceValue, Is.Zero,
                "New neutral settings are local data and must not inherit a historical World marker.");
            var evaluated = new Vector3[neutral.ControlPointCount];
            Assert.That(
                neutral.TryCopyLegacyEvaluationControlPoints(
                    Matrix4x4.TRS(new Vector3(3f, -2f, 5f), Quaternion.Euler(10f, 20f, 30f), Vector3.one).inverse,
                    evaluated),
                Is.True);
            Assert.That(evaluated, Is.EqualTo(neutral.ControlPointsLocal.ToArray()),
                "A non-identity owner transform must not move a newly created neutral lattice.");
        }

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
                Assert.That(cache.IsCompatibleWith(null, mesh, 0), Is.False);
                Assert.That(cache.IsCompatibleWith(asset, null, 0), Is.False);
                Assert.That(cache.IsCompatibleWith(asset, mesh, 0), Is.False);

                cache.Populate(
                    asset.GridSize,
                    bounds,
                    asset.Interpolation,
                    mesh.vertexCount + 1,
                    0,
                    new[] { new LatticeCacheEntry() },
                    mesh.vertices);
                Assert.That(cache.IsCompatibleWith(asset, mesh, 0), Is.False);

                cache.Populate(
                    asset.GridSize + Vector3Int.one,
                    bounds,
                    asset.Interpolation,
                    mesh.vertexCount,
                    0,
                    new[] { new LatticeCacheEntry() },
                    mesh.vertices);
                Assert.That(cache.IsCompatibleWith(asset, mesh, 0), Is.False);

                cache.Populate(
                    asset.GridSize,
                    bounds,
                    asset.Interpolation == LatticeInterpolationMode.Trilinear
                        ? LatticeInterpolationMode.CubicBernstein
                        : LatticeInterpolationMode.Trilinear,
                    mesh.vertexCount,
                    0,
                    new[] { new LatticeCacheEntry() },
                    mesh.vertices);
                Assert.That(cache.IsCompatibleWith(asset, mesh, 0), Is.False);

                cache.Populate(
                    asset.GridSize,
                    new Bounds(bounds.center + Vector3.one, bounds.size),
                    asset.Interpolation,
                    mesh.vertexCount,
                    0,
                    new[] { new LatticeCacheEntry() },
                    mesh.vertices);
                Assert.That(cache.IsCompatibleWith(asset, mesh, 0), Is.False);

                cache.Populate(
                    asset.GridSize,
                    bounds,
                    asset.Interpolation,
                    mesh.vertexCount,
                    0,
                    new[] { new LatticeCacheEntry() },
                    mesh.vertices);
                Assert.That(cache.IsCompatibleWith(asset, mesh, 0), Is.True);
                Assert.That(cache.IsCompatibleWith(asset, mesh, 1), Is.False);

                asset.Interpolation = LatticeInterpolationMode.CubicBernstein;
                cache.Populate(
                    asset.GridSize,
                    bounds,
                    asset.Interpolation,
                    mesh.vertexCount,
                    0,
                    new[] { new LatticeCacheEntry() },
                    mesh.vertices);
                Assert.That(cache.IsCompatibleWith(asset, mesh, 0), Is.False,
                    "Bernstein caches without per-axis basis weights must be rejected.");

                int bernsteinWeightCount =
                    (asset.GridSize.x + asset.GridSize.y + asset.GridSize.z) * mesh.vertexCount;
                cache.Populate(
                    asset.GridSize,
                    bounds,
                    asset.Interpolation,
                    mesh.vertexCount,
                    0,
                    new[] { new LatticeCacheEntry() },
                    mesh.vertices,
                    new float[bernsteinWeightCount]);
                Assert.That(cache.IsCompatibleWith(asset, mesh, 0), Is.True);
                Assert.That(cache.HasValidBernsteinWeights(-1), Is.False);

                typeof(LatticeDeformerCache)
                    .GetField("_bernsteinWeights", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(cache, null);
                Assert.That(cache.HasValidBernsteinWeights(mesh.vertexCount), Is.False);

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
        public void LatticeAsset_ResizeGrid_RebuildsNeutralPointsFromMalformedOldPayload()
        {
            var asset = new LatticeAsset();
            asset.EnsureInitialized();
            typeof(LatticeAsset)
                .GetField("_controlPointsLocal", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(asset, new[] { Vector3.one });

            asset.GridSize = new Vector3Int(2, 2, 2);

            Assert.That(asset.ControlPointsLocal.Length, Is.EqualTo(8));
            Assert.That(asset.GetControlPointLocal(0), Is.EqualTo(new Vector3(-0.5f, -0.5f, -0.5f)));
            Assert.That(asset.GetControlPointLocal(7), Is.EqualTo(new Vector3(0.5f, 0.5f, 0.5f)));
        }

        [Test]
        public void LatticeAsset_LegacyEvaluation_RejectsMalformedWorldPayloadsWithoutMutation()
        {
            var asset = new LatticeAsset();
            asset.EnsureInitialized();
            var type = typeof(LatticeAsset);
            var applySpace = type.GetField("_applySpace", BindingFlags.Instance | BindingFlags.NonPublic);
            var points = type.GetField("_controlPointsLocal", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(asset.CanEvaluateLegacyWorldSpace(Matrix4x4.identity), Is.False);
            Assert.That(asset.TryCopyLegacyEvaluationControlPoints(Matrix4x4.identity, new Vector3[1]), Is.False);

            applySpace.SetValue(asset, 1);
            var singular = Matrix4x4.Scale(new Vector3(0f, 1f, 1f));
            Assert.That(asset.CanEvaluateLegacyWorldSpace(singular), Is.False);

            var nonFinite = Matrix4x4.identity;
            nonFinite.m00 = float.NaN;
            Assert.That(asset.CanEvaluateLegacyWorldSpace(nonFinite), Is.False);

            points.SetValue(asset, new[] { Vector3.zero });
            Assert.That(asset.CanEvaluateLegacyWorldSpace(Matrix4x4.identity), Is.False);

            var validPoints = new Vector3[asset.ControlPointCount];
            validPoints[0] = new Vector3(float.MaxValue, 0f, 0f);
            points.SetValue(asset, validPoints);
            var overflowingTransform = Matrix4x4.Scale(new Vector3(2f, 1f, 1f));
            Assert.That(asset.CanEvaluateLegacyWorldSpace(overflowingTransform), Is.False);
            Assert.That(
                asset.TryCopyLegacyEvaluationControlPoints(
                    overflowingTransform,
                    new Vector3[asset.ControlPointCount]),
                Is.False);

            Assert.That(() => asset.CopyLegacySerializationStateFrom(null), Throws.Nothing);
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
        public void LatticeAsset_CurrentSerializedVersion_PreservesIntentionalAllZeroControlPoints()
        {
            var asset = new LatticeAsset();
            asset.EnsureInitialized();
            for (int i = 0; i < asset.ControlPointCount; i++)
            {
                asset.SetControlPointLocal(i, Vector3.zero);
            }

            var versionField = typeof(LatticeAsset)
                .GetField("_serializationVersion", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(versionField, Is.Not.Null);
            int currentVersion = (int)versionField.GetValue(asset);
            Assert.That(currentVersion, Is.GreaterThan(0), "New instances must start at the current serialized model version.");

            ((ISerializationCallbackReceiver)asset).OnAfterDeserialize();

            Assert.That((int)versionField.GetValue(asset), Is.EqualTo(currentVersion));
            Assert.That(asset.ControlPointsLocal.ToArray(), Is.All.EqualTo(Vector3.zero));
            Assert.That(asset.HasCustomizedControlPoints(), Is.True);

            string json = JsonUtility.ToJson(asset);
            var roundTrip = JsonUtility.FromJson<LatticeAsset>(json);
            ((ISerializationCallbackReceiver)roundTrip).OnAfterDeserialize();

            Assert.That((int)versionField.GetValue(roundTrip), Is.EqualTo(currentVersion));
            Assert.That(roundTrip.ControlPointsLocal.ToArray(), Is.All.EqualTo(Vector3.zero));
        }

        [Test]
        public void LatticeAsset_LegacyMissingVersion_AllZeroControlPointsMigrateToNeutral()
        {
            var asset = new LatticeAsset();
            asset.GridSize = new Vector3Int(2, 2, 2);
            for (int i = 0; i < asset.ControlPointCount; i++)
            {
                asset.SetControlPointLocal(i, Vector3.zero);
            }

            var versionField = typeof(LatticeAsset)
                .GetField("_serializationVersion", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(versionField, Is.Not.Null);
            int currentVersion = (int)versionField.GetValue(asset);

            string json = JsonUtility.ToJson(asset);
            Assert.That(json, Does.Contain("_serializationVersion"));
            string versionAtStart = $"\"_serializationVersion\":{currentVersion},";
            string versionAfterField = $",\"_serializationVersion\":{currentVersion}";
            string legacyJson = json
                .Replace(versionAtStart, "")
                .Replace(versionAfterField, "");
            Assert.That(legacyJson, Does.Not.Contain("_serializationVersion"));

            var legacy = JsonUtility.FromJson<LatticeAsset>(legacyJson);
            ((ISerializationCallbackReceiver)legacy).OnAfterDeserialize();

            Assert.That((int)versionField.GetValue(legacy), Is.EqualTo(currentVersion));
            Assert.That(legacy.HasCustomizedControlPoints(), Is.False);
            Assert.That(legacy.GetControlPointLocal(0), Is.EqualTo(new Vector3(-0.5f, -0.5f, -0.5f)));
            Assert.That(legacy.GetControlPointLocal(7), Is.EqualTo(new Vector3(0.5f, 0.5f, 0.5f)));
        }

        [Test]
        public void LatticeAsset_ExplicitLegacyVersion_AllZeroControlPointsMigrateOnlyOnce()
        {
            var asset = new LatticeAsset();
            asset.GridSize = new Vector3Int(2, 2, 2);
            for (int i = 0; i < asset.ControlPointCount; i++)
            {
                asset.SetControlPointLocal(i, Vector3.zero);
            }

            var versionField = typeof(LatticeAsset)
                .GetField("_serializationVersion", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(versionField, Is.Not.Null);
            versionField.SetValue(asset, 0);

            ((ISerializationCallbackReceiver)asset).OnAfterDeserialize();
            Assert.That(asset.HasCustomizedControlPoints(), Is.False);

            for (int i = 0; i < asset.ControlPointCount; i++)
            {
                asset.SetControlPointLocal(i, Vector3.zero);
            }

            ((ISerializationCallbackReceiver)asset).OnAfterDeserialize();
            Assert.That(asset.ControlPointsLocal.ToArray(), Is.All.EqualTo(Vector3.zero));
            Assert.That(asset.HasCustomizedControlPoints(), Is.True);
        }

        [Test]
        public void LatticeAsset_PrivateHelpers_HandleDefensiveBranches()
        {
            var asset = new LatticeAsset();
            var type = typeof(LatticeAsset);

            Assert.That(asset.HasCustomizedControlPoints(), Is.False);

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
                    "PopulateNeutralControlPoints",
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
                "PopulateNeutralControlPoints",
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

            type.GetField("_gridSize", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(asset, Vector3Int.zero);
            InvokePrivate(asset, "EnsureControlPointCapacity");
            InvokePrivate(asset, "PopulateControlPoints");
            Assert.That(asset.ControlPointCount, Is.EqualTo(8));

            type.GetField("_gridSize", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(asset, new Vector3Int(2, 2, 2));
            type.GetField("_controlPointsLocal", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(asset, new Vector3[8]);
            InvokePrivate(asset, "EnsureControlPointCapacity");
            Assert.That(asset.ControlPointsLocal.ToArray(), Is.All.EqualTo(Vector3.zero));

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
        public void LatticeAsset_DefensiveSerializationHelpers_RejectFutureOverflowAndEmptyPayloads()
        {
            var asset = new LatticeAsset();
            var type = typeof(LatticeAsset);
            var version = type.GetField("_serializationVersion", BindingFlags.Instance | BindingFlags.NonPublic);
            var grid = type.GetField("_gridSize", BindingFlags.Instance | BindingFlags.NonPublic);
            var points = type.GetField("_controlPointsLocal", BindingFlags.Instance | BindingFlags.NonPublic);

            version.SetValue(asset, int.MaxValue);
            points.SetValue(asset, new[] { Vector3.one });
            InvokePrivate(asset, "EnsureControlPointCapacity");
            Assert.That(asset.ControlPointsLocal.ToArray(), Is.EqualTo(new[] { Vector3.one }));

            grid.SetValue(asset, new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue));
            var expectedArgs = new object[] { 0 };
            var expectedMethod = type.GetMethod("TryGetExpectedControlPointCount", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That((bool)expectedMethod.Invoke(asset, expectedArgs), Is.False);

            grid.SetValue(asset, new Vector3Int(2, 2, int.MaxValue));
            points.SetValue(asset, null);
            InvokePrivate(asset, "PopulateControlPoints");

            version.SetValue(asset, 0);
            InvokePrivate(asset, "EnsureControlPointCapacity");
            Assert.That(asset.ControlPointsLocal.ToArray(), Is.Empty);

            grid.SetValue(asset, new Vector3Int(3, 3, 3));
            points.SetValue(asset, null);
            asset.GridSize = new Vector3Int(4, 2, 2);
            Assert.That(asset.ControlPointsLocal.Length, Is.EqualTo(16));
            Assert.That(asset.GetControlPointLocal(0), Is.EqualTo(new Vector3(-0.5f, -0.5f, -0.5f)));
            Assert.That(asset.GetControlPointLocal(15), Is.EqualTo(new Vector3(0.5f, 0.5f, 0.5f)));

            var allZero = type.GetMethod("AreAllControlPointsZero", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That((bool)allZero.Invoke(null, new object[] { null }), Is.False);
            Assert.That((bool)allZero.Invoke(null, new object[] { Array.Empty<Vector3>() }), Is.False);

            version.SetValue(asset, 1);
            Assert.That(() => asset.EnsureInitialized(), Throws.Nothing);
            Assert.That(asset.HasMalformedSerializedShape, Is.False);

            var malformed = new LatticeAsset();
            malformed.EnsureInitialized();
            points.SetValue(malformed, new[] { Vector3.one });
            malformed.EnsureInitialized();
            Assert.That(malformed.ControlPointsLocal.ToArray(), Is.EqualTo(new[] { Vector3.one }));
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
                Assert.That(deformer.ActiveLayerIndex, Is.EqualTo(layer));
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
                    int sourceHash = InvokeStaticPrivate<int>("HashVertices", source);
                    cache.Populate(
                        settings.GridSize,
                        settings.LocalBounds,
                        settings.Interpolation,
                        sourceMesh.vertexCount,
                        sourceHash,
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
                LatticeDeformer.CollectControlPointOffsetsLocal(null, Span<Vector3>.Empty);
                LatticeDeformer.CollectControlPointOffsetsLocal(clone, Span<Vector3>.Empty);

                var controlPoints = new Vector3[clone.ControlPointCount];
                LatticeDeformer.CollectControlPointsLocal(clone, controlPoints);
                Assert.That(controlPoints[0], Is.EqualTo(clone.GetControlPointLocal(0)));
                Assert.That(
                    () => LatticeDeformer.CollectControlPointsLocal(clone, new Vector3[controlPoints.Length + 1]),
                    Throws.InvalidOperationException);

                var offsets = new Vector3[clone.ControlPointCount];
                LatticeDeformer.CollectControlPointOffsetsLocal(clone, offsets);
                Assert.That(offsets, Is.All.EqualTo(Vector3.zero));

                clone.SetControlPointLocal(0, clone.GetControlPointLocal(0) + Vector3.right);
                LatticeDeformer.CollectControlPointOffsetsLocal(clone, offsets);
                Assert.That(offsets[0], Is.EqualTo(Vector3.right));
                Assert.That(
                    () => LatticeDeformer.CollectControlPointOffsetsLocal(clone, new Vector3[offsets.Length + 1]),
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

                var emptyBernsteinWeights = InvokeStaticPrivate<float[]>(
                    "BuildBernsteinWeightsWithJobs",
                    new Vector3Int(2, 2, 2),
                    Array.Empty<LatticeCacheEntry>());
                Assert.That(emptyBernsteinWeights, Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LatticeDeformer_PublishedCubicCompatibility_RestoresFlagsOnRollback()
        {
            var go = new GameObject("runtime-core-cubic-migration-rollback");
            try
            {
                var deformer = go.AddComponent<LatticeDeformer>();
                var settings = new LatticeAsset
                {
                    Interpolation = LatticeInterpolationMode.CubicBernstein
                };
                settings.EnsureInitialized();

                var flatLayer = new LatticeLayer { Name = "flat cubic" };
                flatLayer.Settings.Interpolation = LatticeInterpolationMode.CubicBernstein;
                typeof(LatticeDeformer)
                    .GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, settings);
                typeof(LatticeDeformer)
                    .GetField("_layers", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, new List<LatticeLayer> { null, flatLayer });

                object snapshots = InvokePrivate(
                    deformer,
                    "PreservePublishedCubicInterpolationSemantics");
                Assert.That(settings.UsesLegacyTrilinearInterpolation, Is.True);
                Assert.That(flatLayer.Settings.UsesLegacyTrilinearInterpolation, Is.True);

                typeof(LatticeDeformer)
                    .GetMethod(
                        "RestoreLatticeInterpolationCompatibility",
                        BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new[] { snapshots });
                Assert.That(settings.UsesLegacyTrilinearInterpolation, Is.False);
                Assert.That(flatLayer.Settings.UsesLegacyTrilinearInterpolation, Is.False);

                typeof(LatticeDeformer)
                    .GetField("_deformationDataVersion", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, DeformationDataVersion.CurrentDevelopment);
                Assert.That(
                    InvokePrivate(deformer, "TryUpgradeV1_4_0ToCurrent"),
                    Is.EqualTo(false));
                Assert.That(settings.UsesLegacyTrilinearInterpolation, Is.False,
                    "A failed final migration must restore the published interpolation flag.");
                Assert.That(flatLayer.Settings.UsesLegacyTrilinearInterpolation, Is.False);
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
        public void LatticeDeformer_DefensiveVersionAndPureHelperBranches_AreStable()
        {
            Assert.That(
                LatticeDeformer.CurrentDeformationDataVersion,
                Is.EqualTo(DeformationDataVersion.CurrentDevelopment));

            var go = new GameObject("runtime-core-defensive-version");
            var mesh = CreateSymmetricQuadMesh("RuntimeCorePureHelpers");
            try
            {
                var deformer = go.AddComponent<LatticeDeformer>();
                var type = typeof(LatticeDeformer);
                var version = type.GetField("_deformationDataVersion", BindingFlags.Instance | BindingFlags.NonPublic);
                var layerVersion = type.GetField("_layerModelVersion", BindingFlags.Instance | BindingFlags.NonPublic);
                var migrationStatus = type.GetField("_migrationStatus", BindingFlags.Instance | BindingFlags.NonPublic);

                version.SetValue(deformer, (DeformationDataVersion)99);
                deformer.Reset();
                Assert.That(deformer.MigrationStatus, Is.EqualTo(DeformationDataMigrationStatus.UnsupportedFutureVersion));

                version.SetValue(deformer, DeformationDataVersion.CurrentDevelopment);
                layerVersion.SetValue(deformer, 99);
                deformer.Reset();
                Assert.That(deformer.MigrationStatus, Is.EqualTo(DeformationDataMigrationStatus.UnsupportedFutureVersion));

                layerVersion.SetValue(deformer, 3);
                version.SetValue(deformer, (DeformationDataVersion)(-1));
                deformer.Reset();
                Assert.That(deformer.MigrationStatus, Is.EqualTo(DeformationDataMigrationStatus.InvalidData));

                version.SetValue(deformer, DeformationDataVersion.CurrentDevelopment);
                layerVersion.SetValue(deformer, 3);
                type.GetField("_isEnsuringLayerModelReady", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, true);
                Assert.That(InvokePrivate(deformer, "EnsureLayerModelReady"), Is.EqualTo(true));
                type.GetField("_hasIncompatibleBrushData", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, true);
                Assert.That(InvokePrivate(deformer, "EnsureLayerModelReady"), Is.EqualTo(false));
                type.GetField("_isEnsuringLayerModelReady", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, false);

                version.SetValue(deformer, (DeformationDataVersion)(-1));
                Assert.That(InvokePrivate(deformer, "TryUpgradeDeformationDataOneRelease"), Is.EqualTo(false));
                Assert.That(deformer.MigrationStatus, Is.EqualTo(DeformationDataMigrationStatus.InvalidData));

                Assert.That(InvokePrivate(deformer, "RestoreSourceNormals", (object)null), Is.Null);
                Assert.That(InvokePrivate(deformer, "RestoreSourceTangents", (object)null), Is.Null);
                Assert.That(InvokePrivate(deformer, "TryApplyLayerContribution", null, null, null), Is.Null);
                Assert.That(InvokePrivate(deformer, "HasMeaningfulBaseSettings"), Is.TypeOf<bool>());

                type.GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(deformer, null);
                Assert.That(InvokePrivate(deformer, "HasMeaningfulBaseSettings"), Is.EqualTo(false));

                Assert.That(InvokeStaticPrivate<bool>("HasNonNullGroups", new List<DeformerGroup> { null }), Is.False);
                Assert.That(InvokeStaticPrivate<bool>("HasNonNullLayers", new List<LatticeLayer> { null }), Is.False);
                Assert.That(
                    InvokeStaticPrivate<bool>("TryBuildDeltas", null, null, null),
                    Is.False);
                Assert.That(InvokeStaticPrivate<HashSet<string>>("CollectBlendShapeNames", (object)null), Is.Empty);
                Assert.That(
                    InvokeStaticPrivate<string>(
                        "MakeUniqueBlendShapeName",
                        "Shape",
                        new HashSet<string> { "Shape", "Shape 1" }),
                    Is.EqualTo("Shape 2"));
                Assert.That(InvokeStaticPrivate<AnimationCurve>("CloneCurve", (object)null), Is.Not.Null);
                Assert.That(InvokeStaticPrivate<int>("HashCurveState", (object)null), Is.Zero);
                Assert.That(InvokeStaticPrivate<Bounds>("CalculateReferencedBounds", null, null, new Bounds(Vector3.one, Vector3.one)).center,
                    Is.EqualTo(Vector3.one));
                Assert.That(InvokeStaticPrivate<object>("DestroyTemporaryMesh", (object)null), Is.Null);

                type.GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(deformer, null);
                Assert.That(InvokePrivate(deformer, "EnsureAllBrushLayerDisplacementCapacity", 0), Is.EqualTo(true));
                Assert.That(
                    type.GetField("_hasIncompatibleBrushData", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(deformer),
                    Is.EqualTo(false));

                var surfaceArgs = new object[]
                {
                    null,
                    Array.Empty<Vector3>(),
                    Array.Empty<Vector3>(),
                    true,
                    true,
                    null,
                    null
                };
                type.GetMethod("CalculateGeneratedSurfaceDeltas", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, surfaceArgs);
                Assert.That(surfaceArgs[5], Is.Null);
                Assert.That(surfaceArgs[6], Is.Null);

                mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
                var tangentArgs = new object[]
                {
                    mesh,
                    mesh.vertices,
                    new[] { Vector3.forward, Vector3.zero, Vector3.zero, Vector3.zero },
                    false,
                    true,
                    null,
                    null
                };
                type.GetMethod("CalculateGeneratedSurfaceDeltas", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, tangentArgs);
                Assert.That(tangentArgs[6], Is.TypeOf<Vector3[]>());

                migrationStatus.SetValue(deformer, DeformationDataMigrationStatus.Ready);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LatticeDeformer_MigrationFailureBoundaries_RollBackDeterministically()
        {
            var go = new GameObject("runtime-core-migration-failures");
            try
            {
                var deformer = go.AddComponent<LatticeDeformer>();
                var type = typeof(LatticeDeformer);
                var settingsField = type.GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic);
                var versionField = type.GetField("_deformationDataVersion", BindingFlags.Instance | BindingFlags.NonPublic);
                var sourceVersionField = type.GetField("_deformationDataSourceVersion", BindingFlags.Instance | BindingFlags.NonPublic);
                var layerVersionField = type.GetField("_layerModelVersion", BindingFlags.Instance | BindingFlags.NonPublic);
                var groupsField = type.GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic);
                var layersField = type.GetField("_layers", BindingFlags.Instance | BindingFlags.NonPublic);
                var activeGroupField = type.GetField("_activeGroupIndex", BindingFlags.Instance | BindingFlags.NonPublic);

                settingsField.SetValue(deformer, null);
                Assert.That(InvokePrivate(deformer, "TryUpgradeV0_0_1ToV0_0_2"), Is.EqualTo(false));

                var settings = new LatticeAsset();
                settings.EnsureInitialized();
                settingsField.SetValue(deformer, settings);
                typeof(LatticeAsset).GetField("_applySpace", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(settings, 2);
                Assert.That(InvokePrivate(deformer, "TryUpgradeV0_0_1ToV0_0_2"), Is.EqualTo(false));

                typeof(LatticeAsset).GetField("_applySpace", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(settings, 1);
                typeof(LatticeAsset).GetField("_controlPointsLocal", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(settings, new[] { Vector3.zero });
                Assert.That(InvokePrivate(deformer, "TryUpgradeV0_0_1ToV0_0_2"), Is.EqualTo(false));

                settings = new LatticeAsset();
                settings.EnsureInitialized();
                typeof(LatticeAsset).GetField("_applySpace", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(settings, 1);
                settingsField.SetValue(deformer, settings);
                go.transform.localScale = new Vector3(0f, 1f, 1f);
                Assert.That(InvokePrivate(deformer, "TryUpgradeV0_0_1ToV0_0_2"), Is.EqualTo(false));
                go.transform.localScale = Vector3.one;

                var authoritative = new DeformerGroup();
                authoritative.LayersList.Add(new LatticeLayer());
                groupsField.SetValue(deformer, new List<DeformerGroup> { authoritative });
                layersField.SetValue(deformer, new List<LatticeLayer>());
                layerVersionField.SetValue(deformer, 2);
                versionField.SetValue(deformer, DeformationDataVersion.V1_2_0);
                sourceVersionField.SetValue(deformer, DeformationDataVersion.V1_2_0);
                Assert.That(InvokePrivate(deformer, "TryUpgradeV1_2_0ToV1_2_1"), Is.EqualTo(true));
                Assert.That((int)layerVersionField.GetValue(deformer), Is.EqualTo(3));

                groupsField.SetValue(deformer, new List<DeformerGroup>());
                layersField.SetValue(deformer, new List<LatticeLayer>());
                layerVersionField.SetValue(deformer, 3);
                versionField.SetValue(deformer, DeformationDataVersion.V1_2_0);
                Assert.That(InvokePrivate(deformer, "TryUpgradeV1_2_0ToV1_2_1"), Is.EqualTo(false));
                Assert.That(deformer.MigrationStatus, Is.EqualTo(DeformationDataMigrationStatus.InvalidData));

                groupsField.SetValue(deformer, new List<DeformerGroup>());
                layersField.SetValue(deformer, new List<LatticeLayer>());
                versionField.SetValue(deformer, DeformationDataVersion.V1_2_1);
                sourceVersionField.SetValue(deformer, DeformationDataVersion.V1_2_1);
                Assert.That(InvokePrivate(deformer, "TryUpgradeV1_2_1ToV1_3_0"), Is.EqualTo(false));

                var invalidActive = new DeformerGroup();
                invalidActive.LayersList.Add(new LatticeLayer());
                groupsField.SetValue(deformer, new List<DeformerGroup> { invalidActive });
                activeGroupField.SetValue(deformer, 4);
                versionField.SetValue(deformer, DeformationDataVersion.V1_2_1);
                Assert.That(InvokePrivate(deformer, "TryUpgradeV1_2_1ToV1_3_0"), Is.EqualTo(false));

                groupsField.SetValue(deformer, new List<DeformerGroup> { invalidActive });
                activeGroupField.SetValue(deformer, 0);
                versionField.SetValue(deformer, DeformationDataVersion.CurrentDevelopment);
                Assert.That(InvokePrivate(deformer, "TryUpgradeV1_2_1ToV1_3_0"), Is.EqualTo(false));

                versionField.SetValue(deformer, DeformationDataVersion.CurrentDevelopment);
                Assert.That(
                    InvokePrivate(
                        deformer,
                        "TryNormalizePublishedGroupSelectionAndCommit",
                        DeformationDataVersion.V1_3_1),
                    Is.EqualTo(false));

                invalidActive.SetSerializedActiveLayerIndex(invalidActive.LayersList.Count);
                groupsField.SetValue(deformer, new List<DeformerGroup> { invalidActive });
                versionField.SetValue(deformer, DeformationDataVersion.V1_3_0);
                var snapshots = InvokePrivate(deformer, "CanonicalizePublishedRemoveLastSelections");
                Assert.That(invalidActive.SerializedActiveLayerIndex, Is.Zero);
                type.GetMethod("RestoreGroupSelections", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new[] { snapshots });
                Assert.That(invalidActive.SerializedActiveLayerIndex, Is.EqualTo(1));

                groupsField.SetValue(deformer, new List<DeformerGroup> { new DeformerGroup() });
                layersField.SetValue(deformer, new List<LatticeLayer>());
                layerVersionField.SetValue(deformer, 2);
                InvokePrivate(deformer, "NormalizeAuthoritativeGroupShapeVersion");
                Assert.That((int)layerVersionField.GetValue(deformer), Is.EqualTo(3));

                var first = new LatticeLayer();
                var second = new LatticeLayer();
                var filterArgs = new object[]
                {
                    new List<LatticeLayer> { first, null, second },
                    1,
                    0
                };
                var filtered = (List<LatticeLayer>)type
                    .GetMethod("FilterLayersAndRemapActive", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, filterArgs);
                Assert.That(filtered, Is.EqualTo(new[] { first, second }));
                Assert.That((int)filterArgs[2], Is.Zero);

                layerVersionField.SetValue(deformer, 2);
                Assert.That(InvokePrivate(deformer, "TryMigrateLegacyBaseToLayerStructure"), Is.EqualTo(false));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LatticeDeformer_SerializedValidators_RejectFutureMetadataAndSelections()
        {
            var go = new GameObject("runtime-core-serialized-validators");
            try
            {
                var deformer = go.AddComponent<LatticeDeformer>();
                var type = typeof(LatticeDeformer);
                var settingsField = type.GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic);
                var groupsField = type.GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic);
                var layersField = type.GetField("_layers", BindingFlags.Instance | BindingFlags.NonPublic);

                var future = new LatticeAsset();
                typeof(LatticeAsset).GetField("_serializationVersion", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(future, int.MaxValue);
                settingsField.SetValue(deformer, future);
                Assert.That(InvokePrivate(deformer, "HasUnsupportedFutureLatticeAsset"), Is.EqualTo(true));

                settingsField.SetValue(deformer, new LatticeAsset());
                var futureLayer = new LatticeLayer();
                typeof(LatticeLayer).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(futureLayer, future);
                layersField.SetValue(deformer, new List<LatticeLayer> { futureLayer });
                Assert.That(InvokePrivate(deformer, "HasUnsupportedFutureLatticeAsset"), Is.EqualTo(true));

                layersField.SetValue(deformer, new List<LatticeLayer>());
                var group = new DeformerGroup();
                group.LayersList.Add(futureLayer);
                groupsField.SetValue(deformer, new List<DeformerGroup> { group });
                Assert.That(InvokePrivate(deformer, "HasUnsupportedFutureLatticeAsset"), Is.EqualTo(true));

                typeof(DeformerGroup).GetField("_blendShapeOutput", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(group, (BlendShapeOutputMode)99);
                Assert.That(InvokePrivate(deformer, "HasMalformedLatticeAsset"), Is.EqualTo(true));

                groupsField.SetValue(deformer, new List<DeformerGroup>());
                layersField.SetValue(deformer, new List<LatticeLayer>());
                type.GetField("_activeGroupIndex", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(deformer, 1);
                Assert.That(InvokePrivate(deformer, "HasMalformedSerializedSelection"), Is.EqualTo(true));

                group = new DeformerGroup();
                group.SetSerializedActiveLayerIndex(1);
                groupsField.SetValue(deformer, new List<DeformerGroup> { group });
                type.GetField("_activeGroupIndex", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(deformer, 0);
                Assert.That(InvokePrivate(deformer, "HasMalformedSerializedSelection"), Is.EqualTo(true));

                typeof(LatticeDeformer).GetField("_blendShapeOutput", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, (BlendShapeOutputMode)99);
                groupsField.SetValue(deformer, new List<DeformerGroup>());
                type.GetField("_activeGroupIndex", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(deformer, 0);
                Assert.That(InvokePrivate(deformer, "HasMalformedLatticeAsset"), Is.EqualTo(true));

                var customLayer = new LatticeLayer();
                customLayer.Settings.EnsureInitialized();
                customLayer.Settings.SetControlPointLocal(
                    0,
                    customLayer.Settings.GetControlPointLocal(0) + Vector3.right);
                layersField.SetValue(deformer, new List<LatticeLayer> { null, customLayer });
                Assert.That(InvokePrivate(deformer, "HasMeaningfulSerializedLatticeData"), Is.EqualTo(true));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LatticeDeformer_RemainingRuntimeGuardsAndBlendShapeHelpers_ReturnSafely()
        {
            var go = new GameObject("runtime-core-remaining-guards");
            var mesh = CreateSymmetricQuadMesh("RuntimeCoreRemainingGuards");
            try
            {
                mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
                var deformer = go.AddComponent<LatticeDeformer>();
                var type = typeof(LatticeDeformer);
                var version = type.GetField("_deformationDataVersion", BindingFlags.Instance | BindingFlags.NonPublic);
                var layerVersion = type.GetField("_layerModelVersion", BindingFlags.Instance | BindingFlags.NonPublic);

                version.SetValue(deformer, DeformationDataVersion.CurrentDevelopment);
                layerVersion.SetValue(deformer, 99);
                Assert.That(InvokePrivate(deformer, "EnsureLayerModelReady"), Is.EqualTo(false));
                layerVersion.SetValue(deformer, 3);
                version.SetValue(deformer, (DeformationDataVersion)(-1));
                Assert.That(InvokePrivate(deformer, "EnsureLayerModelReady"), Is.EqualTo(false));

                var future = new LatticeAsset();
                typeof(LatticeAsset).GetField("_serializationVersion", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(future, int.MaxValue);
                type.GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(deformer, future);
                version.SetValue(deformer, DeformationDataVersion.V1_2_0);
                Assert.That(InvokePrivate(deformer, "TryUpgradeDeformationDataOneRelease"), Is.EqualTo(false));

                Assert.That(InvokePrivate(deformer, "AddGeneratedBlendShapeFrames", null, "Shape", null, null, null), Is.Null);
                var baseVertices = mesh.vertices;
                var deltas = new[] { Vector3.forward, Vector3.zero, Vector3.zero, Vector3.zero };
                Assert.That(
                    InvokePrivate(deformer, "AddGeneratedBlendShapeFrames", mesh, "Shape", new Vector3[1], deltas, null),
                    Is.Null);

                type.GetField("_sourceMesh", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(deformer, mesh);
                type.GetField("_recalculateTangents", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(deformer, true);
                type.GetField("_recalculateNormals", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(deformer, false);
                Assert.That(
                    InvokePrivate(
                        deformer,
                        "AddGeneratedBlendShapeFrames",
                        mesh,
                        "Tangent Shape",
                        baseVertices,
                        deltas,
                        AnimationCurve.Linear(0f, 0f, 1f, 1f)),
                    Is.Null);
                Assert.That(mesh.GetBlendShapeIndex("Tangent Shape"), Is.GreaterThanOrEqualTo(0));

                var generatedType = type.GetNestedType("GeneratedBlendShape", BindingFlags.NonPublic);
                var generated = Activator.CreateInstance(
                    generatedType,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new object[] { "Null Deltas", null, null },
                    null);
                var listType = typeof(List<>).MakeGenericType(generatedType);
                var list = (System.Collections.IList)Activator.CreateInstance(listType);
                list.Add(generated);
                Assert.That((int)InvokePrivate(deformer, "ComputeBlendShapeOutputHash", list), Is.Not.Zero);

                var emptyObject = new GameObject("runtime-core-empty-deform");
                var emptyMesh = new Mesh();
                try
                {
                    emptyObject.AddComponent<MeshFilter>().sharedMesh = emptyMesh;
                    var emptyDeformer = emptyObject.AddComponent<LatticeDeformer>();
                    Assert.That(emptyDeformer.Deform(false), Is.Null);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(emptyObject);
                    UnityEngine.Object.DestroyImmediate(emptyMesh);
                }

                layerVersion.SetValue(deformer, 4);
                Assert.That(InvokePrivate(deformer, "TryMigrateLayersToGroupStructure"), Is.EqualTo(false));

                layerVersion.SetValue(deformer, 3);
                type.GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, new List<DeformerGroup> { new DeformerGroup() });
                type.GetField("_layers", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, new List<LatticeLayer>());
                Assert.That(InvokePrivate(deformer, "TryMigrateLayersToGroupStructure"), Is.EqualTo(false));

                layerVersion.SetValue(deformer, 2);
                Assert.That(InvokePrivate(deformer, "TryMigrateLayersToGroupStructure"), Is.EqualTo(false));
                Assert.That((int)layerVersion.GetValue(deformer), Is.EqualTo(3));

                type.GetField("_sourceMesh", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(deformer, mesh);

                var worldLayer = new LatticeLayer();
                worldLayer.Settings.EnsureInitialized();
                typeof(LatticeAsset).GetField("_applySpace", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(worldLayer.Settings, 1);
                type.GetField("_legacyAbsoluteLatticeEvaluation", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, true);
                go.transform.localScale = new Vector3(0f, 1f, 1f);
                var source = mesh.vertices;
                InvokePrivate(
                    deformer,
                    "TryApplyLatticeLayerContribution",
                    worldLayer,
                    source,
                    (Vector3[])source.Clone());
                go.transform.localScale = Vector3.one;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LatticeDeformer_SourceAndRecoveryGuards_DoNotMutateMalformedPayloads()
        {
            var go = new GameObject("runtime-core-source-guards");
            var mesh = CreateBlendShapeMesh("RuntimeCoreBakedHashBranch");
            try
            {
                var deformer = go.AddComponent<LatticeDeformer>();
                int brush = deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
                deformer.SplitLayerByAxis(brush, 0, true);
                deformer.FlipLayerByAxis(brush, 0);

                var type = typeof(LatticeDeformer);
                var group = new DeformerGroup();
                group.LayersList.Add(new LatticeLayer());
                typeof(DeformerGroup).GetField("_blendShapeOutput", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(group, (BlendShapeOutputMode)99);
                type.GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, new List<DeformerGroup> { group });
                type.GetField("_layers", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, new List<LatticeLayer>());
                type.GetField("_deformationDataVersion", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, DeformationDataVersion.CurrentDevelopment);
                type.GetField("_layerModelVersion", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, 3);
                deformer.Reset();
                Assert.That(deformer.MigrationStatus, Is.EqualTo(DeformationDataMigrationStatus.InvalidData));

                type.GetField("_deformationDataVersion", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, DeformationDataVersion.V1_2_1);
                type.GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, new List<DeformerGroup>());
                Assert.That(InvokePrivate(deformer, "EnsureLayerModelReady"), Is.EqualTo(false));

                var recovery = new DeformerGroup();
                recovery.LayersList.Add(new LatticeLayer { Name = "Inspectable" });
                typeof(DeformerGroup).GetField("_blendShapeOutput", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(recovery, (BlendShapeOutputMode)99);
                type.GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, new List<DeformerGroup> { recovery });
                type.GetField("_activeGroupIndex", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(deformer, 0);
                type.GetField("_deformationDataVersion", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, DeformationDataVersion.CurrentDevelopment);
                Assert.That(deformer.Layers, Has.Count.EqualTo(1));

                var renderObject = new GameObject("runtime-core-baked-hash-renderer");
                try
                {
                    var renderer = renderObject.AddComponent<SkinnedMeshRenderer>();
                    renderer.sharedMesh = mesh;
                    var renderDeformer = renderObject.AddComponent<LatticeDeformer>();
                    Assert.That(renderDeformer.Deform(false), Is.Not.Null);
                    type.GetField("_blendShapeOutputDirty", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(renderDeformer, false);
                    type.GetField("_lastBlendShapeHash", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(renderDeformer, 0);
                    type.GetField("_lastBakedBlendShapeHash", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(renderDeformer, int.MinValue);
                    Assert.That(renderDeformer.Deform(false), Is.Not.Null);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(renderObject);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(go);
            }
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

                InvokePrivate(deformer, "EnsureCache", null, Array.Empty<Vector3>());
                Assert.That(
                    InvokePrivate(deformer, "RebuildCache", null, mesh, Array.Empty<Vector3>(), 0),
                    Is.EqualTo(false));

                var invalidGrid = new LatticeAsset();
                typeof(LatticeAsset)
                    .GetField("_gridSize", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(invalidGrid, Vector3Int.one);
                Assert.That(InvokePrivate(deformer, "RebuildCache", invalidGrid, mesh, Array.Empty<Vector3>(), 0), Is.EqualTo(false));

                var validSettings = new LatticeAsset();
                validSettings.EnsureInitialized();
                Assert.That(InvokePrivate(deformer, "RebuildCache", validSettings, mesh, Array.Empty<Vector3>(), 0), Is.EqualTo(false));

                typeof(LatticeDeformer)
                    .GetField("_cache", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, null);
                Assert.That(InvokePrivate(deformer, "EnsureCache", validSettings, Array.Empty<Vector3>()), Is.EqualTo(false));
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

                var groupsField = typeof(LatticeDeformer)
                    .GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic);
                groupsField.SetValue(deformer, null);
                InvokePrivate(deformer, "EnsureLayers");
                Assert.That(deformer.GroupCount, Is.Zero);
                Assert.That(deformer.MigrationStatus,
                    Is.EqualTo(DeformationDataMigrationStatus.InvalidData));
                Assert.That(groupsField.GetValue(deformer), Is.Null,
                    "A malformed explicit-null payload must remain untouched.");

                groupsField.SetValue(deformer, new List<DeformerGroup>());
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

        [Test]
        public void LatticeDeformer_BlendShapeSourceHelpers_CoverRuntimeBranches()
        {
            var go = new GameObject("runtime-core-blendshape-source");
            var deformer = go.AddComponent<LatticeDeformer>();
            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            var mesh = CreateBlendShapeMesh("RuntimeCoreBlendShape");
            var emptyMesh = new Mesh { name = "RuntimeCoreEmptyBlendShape" };
            try
            {
                typeof(LatticeDeformer)
                    .GetField("_sourceMesh", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, null);
                var nullArgs = new object[] { null, null, null };
                Assert.That(InvokePrivate(deformer, "BuildCurrentSourceVertices", nullArgs), Is.Null);

                typeof(LatticeDeformer)
                    .GetField("_sourceMesh", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, emptyMesh);
                var emptyArgs = new object[] { null, null, null };
                Assert.That((Vector3[])InvokePrivate(deformer, "BuildCurrentSourceVertices", emptyArgs), Is.Empty);

                typeof(LatticeDeformer)
                    .GetField("_sourceMesh", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, mesh);
                typeof(LatticeDeformer)
                    .GetField("_skinnedMeshRenderer", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, null);
                var noRendererArgs = new object[] { null, null, null };
                Assert.That(((Vector3[])InvokePrivate(deformer, "BuildCurrentSourceVertices", noRendererArgs))[0], Is.EqualTo(mesh.vertices[0]));

                renderer.sharedMesh = mesh;
                renderer.SetBlendShapeWeight(0, 0f);
                typeof(LatticeDeformer)
                    .GetField("_skinnedMeshRenderer", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, renderer);
                var zeroWeightArgs = new object[] { null, null, null };
                Assert.That(((Vector3[])InvokePrivate(deformer, "BuildCurrentSourceVertices", zeroWeightArgs))[0], Is.EqualTo(mesh.vertices[0]));

                renderer.SetBlendShapeWeight(0, 75f);
                var bakedArgs = new object[] { null, null, null };
                var bakedVertices = (Vector3[])InvokePrivate(deformer, "BuildCurrentSourceVertices", bakedArgs);
                Assert.That(bakedVertices[0].x, Is.EqualTo(-0.25f).Within(1e-6f));
                Assert.That(bakedArgs[0], Is.TypeOf<Vector3[][]>());
                Assert.That(bakedArgs[1], Is.TypeOf<float[]>());
                Assert.That((int)bakedArgs[2], Is.Not.EqualTo(0));

                var interpolated = InvokeStaticPrivate<Vector3[]>("EvaluateBlendShapeVertexDelta", mesh, 0, 75f);
                Assert.That(interpolated[0].x, Is.EqualTo(0.75f).Within(1e-6f));
                var lastFrame = InvokeStaticPrivate<Vector3[]>("EvaluateBlendShapeVertexDelta", mesh, 0, 150f);
                Assert.That(lastFrame[0].x, Is.EqualTo(1f).Within(1e-6f));

                Assert.That(InvokeStaticPrivate<int>("HashVertices", new object[] { null }), Is.EqualTo(0));
                Assert.That(InvokeStaticPrivate<int>("HashVertices", Array.Empty<Vector3>()), Is.EqualTo(0));
                Assert.That(InvokeStaticPrivate<int>("HashVertices", new[] { Vector3.one }), Is.Not.EqualTo(0));
                InvokeStaticPrivate<object>("ScaleDeltas", null, 1f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(emptyMesh);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LatticeDeformer_DeformAndBoundsHelpers_CoverMergeBranches()
        {
            var go = new GameObject("runtime-core-merge-branches");
            var deformer = go.AddComponent<LatticeDeformer>();
            var mesh = CreateSymmetricQuadMesh("RuntimeCoreMergeBounds");
            var noSubMesh = new Mesh { name = "RuntimeCoreNoSubMesh" };
            try
            {
                typeof(LatticeDeformer)
                    .GetField("_sourceMesh", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deformer, mesh);
                Assert.That(deformer.Deform(), Is.Null);

                var truncatedVertices = new[] { new Vector3(5f, 6f, 7f) };
                var truncatedBounds = InvokeStaticPrivate<Bounds>(
                    "CalculateReferencedBounds",
                    mesh,
                    truncatedVertices,
                    new Bounds(Vector3.one, Vector3.one));
                Assert.That(truncatedBounds.center, Is.EqualTo(truncatedVertices[0]));

                mesh.subMeshCount = 1;
                mesh.SetIndices(Array.Empty<int>(), MeshTopology.Triangles, 0);
                var fallbackBounds = InvokeStaticPrivate<Bounds>(
                    "CalculateReferencedBounds",
                    mesh,
                    mesh.vertices,
                    new Bounds(Vector3.one, Vector3.one));
                Assert.That(fallbackBounds.center.x, Is.EqualTo(0f).Within(1e-6f));

                noSubMesh.vertices = new[] { new Vector3(-2f, 0f, 0f), new Vector3(4f, 2f, 0f) };
                noSubMesh.subMeshCount = 0;
                var noSubMeshBounds = InvokeStaticPrivate<Bounds>(
                    "CalculateReferencedBounds",
                    noSubMesh,
                    noSubMesh.vertices,
                    new Bounds(Vector3.one, Vector3.one));
                Assert.That(noSubMeshBounds.center, Is.EqualTo(new Vector3(1f, 1f, 0f)));
                Assert.That(noSubMeshBounds.size, Is.EqualTo(new Vector3(6f, 2f, 0f)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(noSubMesh);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LatticeDeformer_OnDestroy_RestoresSourceAndReleasesRuntimeMeshIdempotently()
        {
            var go = new GameObject("runtime-core-destroy-runtime");
            var mesh = CreateSymmetricQuadMesh("RuntimeCoreDestroyRuntimeMesh");
            try
            {
                LatticeDeformer.SuppressRestoreOnDisable = false;
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var deformer = go.AddComponent<LatticeDeformer>();
                var runtimeMesh = deformer.Deform();
                Assert.That(runtimeMesh, Is.Not.Null);
                Assert.That(filter.sharedMesh, Is.SameAs(runtimeMesh));

                InvokePrivate(deformer, "OnDestroy");

                Assert.That(filter.sharedMesh, Is.SameAs(mesh));
                Assert.That(deformer.RuntimeMesh, Is.Null);
                Assert.That(runtimeMesh == null, Is.True, "The owned runtime mesh should be destroyed.");
                Assert.DoesNotThrow(() => InvokePrivate(deformer, "OnDestroy"));
                Assert.That(filter.sharedMesh, Is.SameAs(mesh));
            }
            finally
            {
                LatticeDeformer.SuppressRestoreOnDisable = false;
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void LatticeDeformer_OnDestroy_WhenRestoreSuppressed_PreservesExportMesh()
        {
            var go = new GameObject("runtime-core-destroy-suppressed");
            var sourceMesh = CreateSymmetricQuadMesh("RuntimeCoreDestroySuppressedSource");
            var exportMesh = CreateSymmetricQuadMesh("RuntimeCoreDestroySuppressedExport");
            try
            {
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = sourceMesh;
                var deformer = go.AddComponent<LatticeDeformer>();
                var runtimeMesh = deformer.Deform();
                Assert.That(runtimeMesh, Is.Not.Null);
                Assert.That(filter.sharedMesh, Is.SameAs(runtimeMesh));

                filter.sharedMesh = exportMesh;
                LatticeDeformer.SuppressRestoreOnDisable = true;

                InvokePrivate(deformer, "OnDestroy");

                Assert.That(filter.sharedMesh, Is.SameAs(exportMesh));
                Assert.That(deformer.RuntimeMesh, Is.Null);
                Assert.That(runtimeMesh == null, Is.True, "The owned runtime mesh should be destroyed.");
            }
            finally
            {
                LatticeDeformer.SuppressRestoreOnDisable = false;
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(exportMesh);
                UnityEngine.Object.DestroyImmediate(sourceMesh);
            }
        }

        private static object InvokePrivate(object target, string methodName, params object[] args)
        {
            var method = target.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .First(m => m.Name == methodName &&
                            m.GetParameters().Length == args.Length &&
                            m.GetParameters()
                                .Select((p, i) => ParameterMatches(p.ParameterType, args[i]))
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
                                .Select((p, i) => ParameterMatches(p.ParameterType, args[i]))
                                .All(match => match));
            return (T)method.Invoke(null, args);
        }

        private static bool ParameterMatches(Type parameterType, object arg)
        {
            if (arg == null)
            {
                return true;
            }

            if (parameterType.IsByRef)
            {
                parameterType = parameterType.GetElementType();
            }

            return parameterType != null && parameterType.IsInstanceOfType(arg);
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

        private static Mesh CreateBlendShapeMesh(string name)
        {
            var mesh = CreateSymmetricQuadMesh(name);
            var delta50 = Enumerable.Repeat(new Vector3(0.5f, 0f, 0f), mesh.vertexCount).ToArray();
            var delta100 = Enumerable.Repeat(Vector3.right, mesh.vertexCount).ToArray();
            var zeroNormals = new Vector3[mesh.vertexCount];
            var zeroTangents = new Vector3[mesh.vertexCount];
            mesh.AddBlendShapeFrame("Smile", 50f, delta50, zeroNormals, zeroTangents);
            mesh.AddBlendShapeFrame("Smile", 100f, delta100, zeroNormals, zeroTangents);
            return mesh;
        }
    }
}
#endif
