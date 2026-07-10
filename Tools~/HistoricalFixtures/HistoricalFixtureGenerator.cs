#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batch-mode generator for real Unity-serialized historical deformation fixtures.
///
/// This source is copied, together with its fixed .meta GUID, into an isolated Unity
/// project's Assets/Editor directory. The historical tag's Runtime directory is copied
/// into that project unchanged. All interaction with tag code is reflection-based so the
/// same generator compiles against every published release from 0.0.1 through 1.4.0.
/// </summary>
public static class HistoricalFixtureGenerator
{
    private const string RuntimeAssemblyName = "net.32ba.lattice-deformation-tool";
    private const string Namespace = "Net._32Ba.LatticeDeformationTool";
    private const string GeneratorPath = "Tools~/HistoricalFixtures/HistoricalFixtureGenerator.cs";
    private const string GenerationMode = "unity-batchmode-tag-checkout";
    private const string GoldenOutputSource = "historical-runtime-deform";
    private const float Tolerance = 0.00005f;

    public static void Generate()
    {
        try
        {
            string tag = RequireArgument("-fixtureTag");
            string commitSha = RequireArgument("-fixtureCommit");
            string packageVersion = RequireArgument("-fixturePackageVersion");
            string generatorSha = RequireArgument("-fixtureGeneratorSha");

            if (!string.Equals(Application.unityVersion, "2022.3.22f1", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Historical fixtures must be generated with Unity 2022.3.22f1, not {Application.unityVersion}.");
            }

            EditorSettings.serializationMode = SerializationMode.ForceText;
            GenerateRelease(tag, commitSha, packageVersion, generatorSha);
            Debug.Log($"HISTORICAL_FIXTURE_GENERATION_SUCCEEDED tag={tag} commit={commitSha}");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            throw;
        }
    }

    private static void GenerateRelease(
        string tag,
        string commitSha,
        string packageVersion,
        string generatorSha)
    {
        string outputRoot = $"Assets/Generated/HistoricalReleases/{tag}";
        if (AssetDatabase.IsValidFolder(outputRoot))
        {
            if (!AssetDatabase.DeleteAsset(outputRoot))
            {
                throw new IOException($"Could not clear existing output folder: {outputRoot}");
            }
        }

        Directory.CreateDirectory(ToAbsolutePath(outputRoot));
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        string sourcePath = $"{outputRoot}/source.asset";
        Mesh source = CreateSourceMesh(tag);
        AssetDatabase.CreateAsset(source, sourcePath);
        AssetDatabase.SaveAssets();

        var fixtureEntries = new List<ManifestFixture>();
        var artifactPaths = new HashSet<string>(StringComparer.Ordinal);
        AddArtifactWithMeta(artifactPaths, sourcePath);

        string localPrefabPath = $"{outputRoot}/fixture.prefab";
        string localExpectedPath = $"{outputRoot}/expected.json";
        ExpectedDocument localExpected = CreateLatticeFixture(tag, source, localPrefabPath, worldSpace: false);
        WriteJsonAsset(localExpectedPath, localExpected);
        fixtureEntries.Add(new ManifestFixture
        {
            kind = "lattice",
            prefab = "fixture.prefab",
            expected = "expected.json",
            source = "source.asset",
            goldenOutputSource = "LatticeDeformer.Deform(false)",
        });
        AddArtifactWithMeta(artifactPaths, localPrefabPath);
        AddArtifactWithMeta(artifactPaths, localExpectedPath);

        if (tag == "0.0.1")
        {
            string worldPrefabPath = $"{outputRoot}/fixture-world.prefab";
            string worldExpectedPath = $"{outputRoot}/expected-world.json";
            ExpectedDocument worldExpected = CreateLatticeFixture(tag, source, worldPrefabPath, worldSpace: true);
            WriteJsonAsset(worldExpectedPath, worldExpected);
            fixtureEntries.Add(new ManifestFixture
            {
                kind = "lattice-world",
                prefab = "fixture-world.prefab",
                expected = "expected-world.json",
                source = "source.asset",
                goldenOutputSource = "LatticeDeformer.Deform(false)",
            });
            AddArtifactWithMeta(artifactPaths, worldPrefabPath);
            AddArtifactWithMeta(artifactPaths, worldExpectedPath);
        }

        if (HasPublishedLegacyBrush(tag))
        {
            string brushPrefabPath = $"{outputRoot}/legacy-brush.prefab";
            string brushExpectedPath = $"{outputRoot}/legacy-brush-expected.json";
            ExpectedDocument brushExpected = CreateLegacyBrushFixture(tag, source, brushPrefabPath);
            WriteJsonAsset(brushExpectedPath, brushExpected);
            fixtureEntries.Add(new ManifestFixture
            {
                kind = "legacy-brush",
                prefab = "legacy-brush.prefab",
                expected = "legacy-brush-expected.json",
                source = "source.asset",
                goldenOutputSource = "BrushDeformer.Deform(false)",
            });
            AddArtifactWithMeta(artifactPaths, brushPrefabPath);
            AddArtifactWithMeta(artifactPaths, brushExpectedPath);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        var files = artifactPaths
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new ManifestFile
            {
                path = Path.GetFileName(path),
                sha256 = ComputeSha256(ToAbsolutePath(path)),
            })
            .ToArray();

        var manifest = new ManifestDocument
        {
            tag = tag,
            commitSha = commitSha,
            packageVersion = packageVersion,
            unityVersion = Application.unityVersion,
            generator = GeneratorPath,
            generatorSha256 = generatorSha,
            generationMode = GenerationMode,
            goldenOutputSource = GoldenOutputSource,
            fixtures = fixtureEntries.ToArray(),
            files = files,
        };

        string manifestPath = $"{outputRoot}/manifest.json";
        WriteJsonAsset(manifestPath, manifest);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        AssertFileAndMetaExist(manifestPath);
        foreach (string path in artifactPaths)
        {
            if (!File.Exists(ToAbsolutePath(path)))
            {
                throw new FileNotFoundException("Generated artifact is missing.", path);
            }
        }
    }

    private static ExpectedDocument CreateLatticeFixture(
        string tag,
        Mesh source,
        string prefabPath,
        bool worldSpace)
    {
        Type deformerType = RequireHistoricalType("LatticeDeformer");
        var root = new GameObject(worldSpace ? "Historical Lattice World Fixture" : "Historical Lattice Fixture");
        root.SetActive(false);

        if (worldSpace)
        {
            root.transform.SetPositionAndRotation(
                new Vector3(3.25f, -1.5f, 2.75f),
                Quaternion.Euler(23f, -37f, 14f));
            root.transform.localScale = new Vector3(1.75f, 0.45f, 2.2f);
        }
        else
        {
            root.transform.SetPositionAndRotation(
                new Vector3(0.35f, -0.2f, 0.15f),
                Quaternion.Euler(7f, -11f, 5f));
            root.transform.localScale = new Vector3(1.1f, 0.9f, 1.2f);
        }

        var filter = root.AddComponent<MeshFilter>();
        root.AddComponent<MeshRenderer>();
        filter.sharedMesh = source;

        Component component = root.AddComponent(deformerType);
        Call(component, "Reset");

        bool groupSchema = HasProperty(deformerType, "Groups");
        if (groupSchema)
        {
            // Exercise the release exactly as an Inspector/public-API access would. The
            // first group releases can legitimately retain a conceptual-v2 marker after
            // eagerly creating their default group; that historical state is intentional.
            Call(component, "EnsureLayerModelReady");
            ConfigurePublishedGroupSchema(component, source);
        }
        else
        {
            ConfigurePublishedSingleSettings(component, worldSpace);
        }

        SetFieldIfPresent(component, "_recalculateNormals", false);
        SetFieldIfPresent(component, "_recalculateTangents", true);
        SetFieldIfPresent(component, "_recalculateBounds", false);
        SetFieldIfPresent(component, "_recalculateBoneWeights", true);
        ConfigureWeightTransferIfPresent(component);
        Call(component, "InvalidateCache");

        Mesh result = (Mesh)Call(component, "Deform", false);
        if (result == null)
        {
            throw new InvalidOperationException($"{tag} LatticeDeformer.Deform(false) returned null.");
        }

        Vector3[] expectedVertices = result.vertices.ToArray();
        OutputBlendShapeExpected[] outputBlendShapes = BuildOutputBlendShapes(result);
        GroupExpected[] serializedGroups = groupSchema
            ? BuildNormalizedGroups(component, groupSchema: true)
            : Array.Empty<GroupExpected>();
        LayerExpected[] serializedFlatLayers = groupSchema
            ? BuildSerializedFlatLayers(component)
            : Array.Empty<LayerExpected>();
        int serializedActiveFlatLayerIndex = groupSchema
            ? Convert.ToInt32(GetField(component, "_activeLayerIndex"), CultureInfo.InvariantCulture)
            : -1;
        string serializedFlatBlendShapeOutput = groupSchema
            ? EnumName(GetField(component, "_blendShapeOutput"))
            : string.Empty;
        string serializedFlatBlendShapeName = groupSchema
            ? Convert.ToString(GetField(component, "_blendShapeName"), CultureInfo.InvariantCulture)
            : string.Empty;
        CurveExpected serializedFlatBlendShapeCurve = groupSchema
            ? CurveExpected.From((AnimationCurve)GetField(component, "_blendShapeCurve"))
            : CurveExpected.From(AnimationCurve.Linear(0f, 0f, 1f, 1f));
        GroupExpected[] expectedGroups = groupSchema
            ? BuildProjectedFinalGroups(
                serializedGroups,
                serializedFlatLayers,
                serializedActiveFlatLayerIndex,
                serializedFlatBlendShapeOutput,
                serializedFlatBlendShapeName,
                serializedFlatBlendShapeCurve)
            : BuildNormalizedGroups(component, groupSchema: false);
        ComponentSettingsExpected componentSettings = BuildComponentSettingsExpected(component);
        int activeGroupIndex = groupSchema ? Convert.ToInt32(GetProperty(component, "ActiveGroupIndex")) : 0;
        int serializedLayerModelVersion = GetSerializedIntOrDefault(component, "_layerModelVersion", -1);
        int serializedFlatLayerCount = GetSerializedArraySizeOrDefault(component, "_layers", -1);

        Call(component, "RestoreOriginalMesh");
        Call(component, "InvalidateCache");

        var transformProbes = new List<TransformProbeExpected>();
        if (worldSpace)
        {
            SettingsExpected settingsBeforeProbe = BuildSettingsExpected(GetProperty(component, "Settings"));
            Vector3[] sourceVerticesBeforeProbe = source.vertices.ToArray();
            Vector3 savedPosition = root.transform.position;
            Quaternion savedRotation = root.transform.rotation;
            Vector3 savedScale = root.transform.localScale;
            Vector3 probePosition = new Vector3(-2.1f, 3.4f, -1.7f);
            Quaternion probeRotation = Quaternion.Euler(-31f, 58f, -19f);
            Vector3 probeScale = new Vector3(0.55f, 1.85f, 1.3f);
            AssertTransformProbeIsValid(probePosition, probeRotation, probeScale);

            root.transform.SetPositionAndRotation(probePosition, probeRotation);
            root.transform.localScale = probeScale;
            Call(component, "InvalidateCache");

            Mesh probeResult = (Mesh)Call(component, "Deform", false);
            if (probeResult == null)
            {
                throw new InvalidOperationException($"{tag} world transform probe returned null.");
            }

            var probe = new TransformProbeExpected
            {
                position = probePosition,
                rotation = probeRotation,
                scale = probeScale,
                expectedVertices = probeResult.vertices.ToArray(),
                outputBlendShapes = BuildOutputBlendShapes(probeResult),
            };
            transformProbes.Add(probe);

            AssertSettingsBitwiseEqual(
                settingsBeforeProbe,
                BuildSettingsExpected(GetProperty(component, "Settings")),
                "World probe mutated raw lattice settings");
            AssertVectorArrayBitwiseEqual(
                sourceVerticesBeforeProbe,
                source.vertices,
                "World probe mutated source.asset vertices");
            if (!HasAnyVectorBitDifference(expectedVertices, probe.expectedVertices))
            {
                throw new InvalidOperationException("World transform probe did not differ from the primary golden output.");
            }

            Call(component, "RestoreOriginalMesh");
            root.transform.SetPositionAndRotation(savedPosition, savedRotation);
            root.transform.localScale = savedScale;
            Call(component, "InvalidateCache");

            Mesh restoredPrimary = (Mesh)Call(component, "Deform", false);
            if (restoredPrimary == null)
            {
                throw new InvalidOperationException($"{tag} restored primary world evaluation returned null.");
            }

            AssertVectorArrayBitwiseEqual(
                expectedVertices,
                restoredPrimary.vertices,
                "Restored primary world vertices differ from their golden output");
            AssertOutputBlendShapesBitwiseEqual(
                outputBlendShapes,
                BuildOutputBlendShapes(restoredPrimary),
                "Restored primary world BlendShapes differ from their golden output");
            AssertSettingsBitwiseEqual(
                settingsBeforeProbe,
                BuildSettingsExpected(GetProperty(component, "Settings")),
                "Restored primary world evaluation mutated raw lattice settings");
            AssertVectorArrayBitwiseEqual(
                sourceVerticesBeforeProbe,
                source.vertices,
                "Restored primary world evaluation mutated source.asset vertices");

            Call(component, "RestoreOriginalMesh");
            Call(component, "InvalidateCache");

            AssertVector3BitwiseEqual(savedPosition, root.transform.position, "World fixture position was not restored");
            AssertQuaternionBitwiseEqual(savedRotation, root.transform.rotation, "World fixture rotation was not restored");
            AssertVector3BitwiseEqual(savedScale, root.transform.localScale, "World fixture scale was not restored");
        }

        if (!ReferenceEquals(filter.sharedMesh, source))
        {
            throw new InvalidOperationException("Lattice fixture did not restore the source mesh before serialization.");
        }

        ValidateHistoricalComponentBeforeSave(component, source, groupSchema);
        ((Behaviour)component).enabled = false;
        EditorUtility.SetDirty(component);
        EditorUtility.SetDirty(root);
        SavePrefab(root, prefabPath, deformerType);
        UnityEngine.Object.DestroyImmediate(root);

        return new ExpectedDocument
        {
            tag = tag,
            kind = worldSpace ? "lattice-world" : "lattice",
            deformerPath = string.Empty,
            classifiedVersion = ClassifySerializedShape(tag, groupSchema),
            serializedLayerModelVersion = serializedLayerModelVersion,
            serializedFlatLayerCount = serializedFlatLayerCount,
            serializedGroups = serializedGroups,
            serializedFlatLayers = serializedFlatLayers,
            serializedActiveFlatLayerIndex = serializedActiveFlatLayerIndex,
            serializedFlatBlendShapeOutput = serializedFlatBlendShapeOutput,
            serializedFlatBlendShapeName = serializedFlatBlendShapeName,
            serializedFlatBlendShapeCurve = serializedFlatBlendShapeCurve,
            legacyAbsoluteEvaluation = true,
            tolerance = Tolerance,
            activeGroupIndex = activeGroupIndex,
            groups = expectedGroups,
            expectedVertices = expectedVertices,
            outputBlendShapes = outputBlendShapes,
            componentSettings = componentSettings,
            legacyBrush = null,
            transformProbes = transformProbes.ToArray(),
        };
    }

    private static ExpectedDocument CreateLegacyBrushFixture(string tag, Mesh source, string prefabPath)
    {
        Type brushType = RequireHistoricalType("BrushDeformer");
        var root = new GameObject("Historical Standalone Brush Fixture");
        root.SetActive(false);
        root.transform.SetPositionAndRotation(
            new Vector3(-0.4f, 0.25f, -0.1f),
            Quaternion.Euler(-4f, 8f, 3f));

        var filter = root.AddComponent<MeshFilter>();
        root.AddComponent<MeshRenderer>();
        filter.sharedMesh = source;

        Component brush = root.AddComponent(brushType);
        Call(brush, "Reset");
        Call(brush, "CacheSourceMesh");
        Call(brush, "EnsureDisplacementCapacity");

        Vector3[] displacements = CreateStandaloneBrushDisplacements(source.vertexCount);
        for (int i = 0; i < displacements.Length; i++)
        {
            Call(brush, "SetDisplacement", i, displacements[i]);
        }

        SetField(brush, "_recalculateNormals", false);
        SetField(brush, "_recalculateTangents", true);
        SetField(brush, "_recalculateBounds", false);
        SetField(brush, "_recalculateBoneWeights", true);

        object weightTransfer = GetProperty(brush, "WeightTransferSettings");
        SetField(weightTransfer, "maxTransferDistance", 0.17f);
        SetField(weightTransfer, "normalAngleThreshold", 123f);
        SetField(weightTransfer, "enableInpainting", false);
        SetField(weightTransfer, "maxIterations", 321);
        SetField(weightTransfer, "tolerance", 0.00042f);

        Mesh result = (Mesh)Call(brush, "Deform", false);
        if (result == null)
        {
            throw new InvalidOperationException($"{tag} BrushDeformer.Deform(false) returned null.");
        }

        Vector3[] expectedVertices = result.vertices.ToArray();
        OutputBlendShapeExpected[] outputBlendShapes = BuildOutputBlendShapes(result);
        Call(brush, "RestoreOriginalMesh");

        if (!ReferenceEquals(filter.sharedMesh, source))
        {
            throw new InvalidOperationException("Brush fixture did not restore the source mesh before serialization.");
        }

        ValidateHistoricalComponentBeforeSave(brush, source, groupSchema: false);
        ((Behaviour)brush).enabled = false;
        EditorUtility.SetDirty(brush);
        EditorUtility.SetDirty(root);
        SavePrefab(root, prefabPath, brushType);
        UnityEngine.Object.DestroyImmediate(root);

        return new ExpectedDocument
        {
            tag = tag,
            kind = "legacy-brush",
            deformerPath = string.Empty,
            classifiedVersion = "V1_2_1",
            serializedLayerModelVersion = -1,
            serializedFlatLayerCount = -1,
            serializedGroups = Array.Empty<GroupExpected>(),
            serializedFlatLayers = Array.Empty<LayerExpected>(),
            serializedActiveFlatLayerIndex = -1,
            serializedFlatBlendShapeOutput = string.Empty,
            serializedFlatBlendShapeName = string.Empty,
            serializedFlatBlendShapeCurve = CurveExpected.From(AnimationCurve.Linear(0f, 0f, 1f, 1f)),
            legacyAbsoluteEvaluation = false,
            tolerance = Tolerance,
            activeGroupIndex = 0,
            groups = Array.Empty<GroupExpected>(),
            expectedVertices = expectedVertices,
            outputBlendShapes = outputBlendShapes,
            componentSettings = null,
            legacyBrush = new LegacyBrushExpected
            {
                displacements = displacements,
                recalculateNormals = false,
                recalculateTangents = true,
                recalculateBounds = false,
                recalculateBoneWeights = true,
                weightTransfer = BuildWeightTransferExpected(weightTransfer),
            },
            transformProbes = Array.Empty<TransformProbeExpected>(),
        };
    }

    private static void ConfigurePublishedSingleSettings(Component component, bool worldSpace)
    {
        object settings = GetProperty(component, "Settings");
        PropertyInfo applySpace = settings.GetType().GetProperty("ApplySpace", BindingFlags.Instance | BindingFlags.Public);
        if (worldSpace)
        {
            if (applySpace == null || !applySpace.CanWrite)
            {
                throw new InvalidOperationException("World fixture requested for a release without LatticeApplySpace.");
            }

            applySpace.SetValue(settings, Enum.ToObject(applySpace.PropertyType, 1));
        }

        Call(component, "InitializeFromSource", true);
        settings = GetProperty(component, "Settings");

        SetProperty(settings, "GridSize", new Vector3Int(2, 2, 2));
        if (!worldSpace)
        {
            SetProperty(settings, "LocalBounds", new Bounds(
                new Vector3(0.05f, -0.08f, 0.04f),
                new Vector3(2.4f, 1.8f, 1.6f)));
        }

        SetEnumProperty(settings, "Interpolation", 0);
        PropertyInfo useJobs = settings.GetType().GetProperty("UseJobsAndBurst", BindingFlags.Instance | BindingFlags.Public);
        if (useJobs != null && useJobs.CanWrite)
        {
            useJobs.SetValue(settings, true);
        }

        Call(settings, "ResetControlPoints");
        ApplyControlPointEdits(settings, 0.19f);
        Call(component, "InvalidateCache");
    }

    private static void ConfigurePublishedGroupSchema(Component component, Mesh source)
    {
        IList groups = AsList(GetProperty(component, "Groups"));
        if (groups.Count == 0)
        {
            Call(component, "AddGroup", "Historical Primary Group");
            groups = AsList(GetProperty(component, "Groups"));
        }

        SetProperty(component, "ActiveGroupIndex", 0);
        object primaryGroup = groups[0];
        SetProperty(primaryGroup, "Name", "Historical Primary Group");
        SetProperty(primaryGroup, "Enabled", true);
        SetEnumProperty(primaryGroup, "BlendShapeOutput", 1);
        SetProperty(primaryGroup, "BlendShapeName", "Historical Primary Shape");
        SetProperty(primaryGroup, "BlendShapeCurve", CreateHistoricalCurve(0.31f));

        IList primaryLayers = AsList(GetProperty(component, "Layers"));
        if (primaryLayers.Count == 0)
        {
            CallAddLayer(component, "Historical Lattice Layer", 0);
            primaryLayers = AsList(GetProperty(component, "Layers"));
        }

        object latticeLayer = primaryLayers[0];
        SetProperty(latticeLayer, "Name", "Historical Lattice Layer");
        SetProperty(latticeLayer, "Enabled", true);
        SetProperty(latticeLayer, "Weight", 0.72f);
        SetEnumProperty(latticeLayer, "BlendShapeOutput", 1);
        SetProperty(latticeLayer, "BlendShapeName", "Historical Lattice Layer Shape");
        object latticeSettings = GetProperty(latticeLayer, "Settings");
        ConfigureGroupLatticeSettings(latticeSettings, 0.16f, new Vector3(0.02f, -0.04f, 0.03f));

        int brushIndex = Convert.ToInt32(CallAddLayer(component, "Historical Brush Layer", 1));
        primaryLayers = AsList(GetProperty(component, "Layers"));
        object brushLayer = primaryLayers[brushIndex];
        SetProperty(brushLayer, "Enabled", true);
        SetProperty(brushLayer, "Weight", 0.58f);
        SetEnumProperty(brushLayer, "BlendShapeOutput", 0);
        SetProperty(brushLayer, "BlendShapeName", "Historical Brush Layer Shape");
        Call(brushLayer, "EnsureBrushDisplacementCapacity", source.vertexCount);
        Call(brushLayer, "EnsureVertexMaskCapacity", source.vertexCount);

        Vector3[] brushDisplacements = CreateLayerBrushDisplacements(source.vertexCount);
        float[] mask = CreateLayerMask(source.vertexCount);
        for (int i = 0; i < source.vertexCount; i++)
        {
            Call(brushLayer, "SetBrushDisplacement", i, brushDisplacements[i]);
            Call(brushLayer, "SetVertexMask", i, mask[i]);
        }

        SetProperty(primaryGroup, "ActiveLayerIndex", brushIndex);

        int secondaryGroupIndex = Convert.ToInt32(Call(component, "AddGroup", "Historical Secondary Group"));
        SetProperty(component, "ActiveGroupIndex", secondaryGroupIndex);
        groups = AsList(GetProperty(component, "Groups"));
        object secondaryGroup = groups[secondaryGroupIndex];
        SetProperty(secondaryGroup, "Name", "Historical Secondary Group");
        SetProperty(secondaryGroup, "Enabled", true);
        SetEnumProperty(secondaryGroup, "BlendShapeOutput", 1);
        SetProperty(secondaryGroup, "BlendShapeName", "Historical Secondary Shape");
        SetProperty(secondaryGroup, "BlendShapeCurve", CreateHistoricalCurve(0.67f));

        int secondaryLayerIndex = Convert.ToInt32(CallAddLayer(component, "Historical Secondary Lattice", 0));
        IList secondaryLayers = AsList(GetProperty(component, "Layers"));
        object secondaryLayer = secondaryLayers[secondaryLayerIndex];
        SetProperty(secondaryLayer, "Enabled", true);
        SetProperty(secondaryLayer, "Weight", 0.33f);
        SetEnumProperty(secondaryLayer, "BlendShapeOutput", 0);
        SetProperty(secondaryLayer, "BlendShapeName", "Historical Secondary Layer Shape");
        ConfigureGroupLatticeSettings(
            GetProperty(secondaryLayer, "Settings"),
            0.085f,
            new Vector3(-0.06f, 0.05f, -0.02f));
        SetProperty(secondaryGroup, "ActiveLayerIndex", secondaryLayerIndex);

        SetProperty(component, "ActiveGroupIndex", 0);
        Call(component, "InvalidateCache");
    }

    private static void ConfigureGroupLatticeSettings(object settings, float editScale, Vector3 center)
    {
        SetProperty(settings, "GridSize", new Vector3Int(2, 2, 2));
        SetProperty(settings, "LocalBounds", new Bounds(center, new Vector3(2.2f, 1.9f, 1.7f)));
        SetEnumProperty(settings, "Interpolation", 0);
        Call(settings, "ResetControlPoints");
        ApplyControlPointEdits(settings, editScale);
    }

    private static void ApplyControlPointEdits(object settings, float scale)
    {
        int count = Convert.ToInt32(GetProperty(settings, "ControlPointCount"));
        for (int i = 0; i < count; i++)
        {
            Vector3 point = (Vector3)Call(settings, "GetControlPointLocal", i);
            float x = ((i & 1) == 0 ? -1f : 1f) * scale * (0.35f + i * 0.04f);
            float y = ((i & 2) == 0 ? 1f : -1f) * scale * (0.2f + i * 0.03f);
            float z = ((i & 4) == 0 ? -1f : 1f) * scale * (0.15f + i * 0.02f);
            Call(settings, "SetControlPointLocal", i, point + new Vector3(x, y, z));
        }
    }

    private static GroupExpected[] BuildNormalizedGroups(Component component, bool groupSchema)
    {
        if (!groupSchema)
        {
            object settings = GetProperty(component, "Settings");
            return new[]
            {
                new GroupExpected
                {
                    name = "Group",
                    enabled = true,
                    activeLayerIndex = 0,
                    blendShapeOutput = "Disabled",
                    blendShapeName = string.Empty,
                    blendShapeCurve = CurveExpected.From(AnimationCurve.Linear(0f, 0f, 1f, 1f)),
                    layers = new[]
                    {
                        new LayerExpected
                        {
                            name = "Lattice Layer",
                            type = "Lattice",
                            enabled = true,
                            weight = 1f,
                            blendShapeOutput = "Disabled",
                            blendShapeName = string.Empty,
                            settings = BuildSettingsExpected(settings),
                            brushDisplacements = Array.Empty<Vector3>(),
                            vertexMask = Array.Empty<float>(),
                        },
                    },
                },
            };
        }

        IList groups = AsList(GetProperty(component, "Groups"));
        var expected = new GroupExpected[groups.Count];
        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            object group = groups[groupIndex];
            IList layers = AsList(GetProperty(group, "LayersList"));
            var layerExpected = new LayerExpected[layers.Count];
            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                layerExpected[layerIndex] = BuildLayerExpected(layers[layerIndex]);
            }

            expected[groupIndex] = new GroupExpected
            {
                name = Convert.ToString(GetProperty(group, "Name"), CultureInfo.InvariantCulture),
                enabled = Convert.ToBoolean(GetProperty(group, "Enabled")),
                activeLayerIndex = Convert.ToInt32(GetProperty(group, "ActiveLayerIndex")),
                blendShapeOutput = EnumName(GetProperty(group, "BlendShapeOutput")),
                blendShapeName = Convert.ToString(GetProperty(group, "BlendShapeName"), CultureInfo.InvariantCulture),
                blendShapeCurve = CurveExpected.From((AnimationCurve)GetProperty(group, "BlendShapeCurve")),
                layers = layerExpected,
            };
        }

        return expected;
    }

    private static LayerExpected[] BuildSerializedFlatLayers(Component component)
    {
        IList layers = AsList(GetField(component, "_layers"));
        var expected = new LayerExpected[layers.Count];
        for (int i = 0; i < layers.Count; i++)
        {
            expected[i] = BuildLayerExpected(layers[i]);
        }
        return expected;
    }

    private static LayerExpected BuildLayerExpected(object layer)
    {
        if (layer == null)
        {
            throw new InvalidOperationException("Historical fixture contains a null layer that cannot be normalized.");
        }

        string type = EnumName(GetProperty(layer, "Type"));
        bool isBrush = string.Equals(type, "Brush", StringComparison.Ordinal);
        return new LayerExpected
        {
            name = Convert.ToString(GetProperty(layer, "Name"), CultureInfo.InvariantCulture),
            type = type,
            enabled = Convert.ToBoolean(GetProperty(layer, "Enabled")),
            weight = Convert.ToSingle(GetProperty(layer, "Weight"), CultureInfo.InvariantCulture),
            blendShapeOutput = EnumName(GetProperty(layer, "BlendShapeOutput")),
            blendShapeName = Convert.ToString(GetProperty(layer, "BlendShapeName"), CultureInfo.InvariantCulture),
            settings = BuildSettingsExpected(GetProperty(layer, "Settings")),
            brushDisplacements = isBrush
                ? ((Vector3[])GetProperty(layer, "BrushDisplacements")).ToArray()
                : Array.Empty<Vector3>(),
            vertexMask = isBrush
                ? ((float[])GetProperty(layer, "VertexMask")).ToArray()
                : Array.Empty<float>(),
        };
    }

    private static GroupExpected[] BuildProjectedFinalGroups(
        GroupExpected[] serializedGroups,
        LayerExpected[] serializedFlatLayers,
        int serializedActiveFlatLayerIndex,
        string serializedFlatBlendShapeOutput,
        string serializedFlatBlendShapeName,
        CurveExpected serializedFlatBlendShapeCurve)
    {
        if (serializedFlatLayers.Length == 0)
        {
            return serializedGroups.ToArray();
        }

        var projected = new GroupExpected[serializedGroups.Length + 1];
        Array.Copy(serializedGroups, projected, serializedGroups.Length);
        projected[projected.Length - 1] = new GroupExpected
        {
            name = "Recovered Legacy Flat Layers",
            enabled = false,
            activeLayerIndex = Math.Max(0, Math.Min(serializedActiveFlatLayerIndex, serializedFlatLayers.Length - 1)),
            blendShapeOutput = serializedFlatBlendShapeOutput,
            blendShapeName = serializedFlatBlendShapeName,
            blendShapeCurve = serializedFlatBlendShapeCurve,
            layers = serializedFlatLayers,
        };
        return projected;
    }

    private static SettingsExpected BuildSettingsExpected(object settings)
    {
        int controlPointCount = Convert.ToInt32(GetProperty(settings, "ControlPointCount"));
        var controlPoints = new Vector3[controlPointCount];
        for (int i = 0; i < controlPointCount; i++)
        {
            controlPoints[i] = (Vector3)Call(settings, "GetControlPointLocal", i);
        }

        PropertyInfo applySpace = settings.GetType().GetProperty("ApplySpace", BindingFlags.Instance | BindingFlags.Public);
        return new SettingsExpected
        {
            gridSize = (Vector3Int)GetProperty(settings, "GridSize"),
            boundsCenter = ((Bounds)GetProperty(settings, "LocalBounds")).center,
            boundsSize = ((Bounds)GetProperty(settings, "LocalBounds")).size,
            interpolation = EnumName(GetProperty(settings, "Interpolation")),
            legacyApplySpaceValue = applySpace != null ? Convert.ToInt32(applySpace.GetValue(settings)) : 0,
            controlPoints = controlPoints,
        };
    }

    private static WeightTransferExpected BuildWeightTransferExpected(object settings)
    {
        return new WeightTransferExpected
        {
            maxTransferDistance = Convert.ToSingle(GetField(settings, "maxTransferDistance"), CultureInfo.InvariantCulture),
            normalAngleThreshold = Convert.ToSingle(GetField(settings, "normalAngleThreshold"), CultureInfo.InvariantCulture),
            enableInpainting = Convert.ToBoolean(GetField(settings, "enableInpainting")),
            maxIterations = Convert.ToInt32(GetField(settings, "maxIterations")),
            tolerance = Convert.ToSingle(GetField(settings, "tolerance"), CultureInfo.InvariantCulture),
        };
    }

    private static void ConfigureWeightTransferIfPresent(object component)
    {
        PropertyInfo property = component.GetType().GetProperty(
            "WeightTransferSettings",
            BindingFlags.Instance | BindingFlags.Public);
        if (property == null)
        {
            return;
        }

        object settings = property.GetValue(component);
        SetField(settings, "maxTransferDistance", 0.19f);
        SetField(settings, "normalAngleThreshold", 117f);
        SetField(settings, "enableInpainting", false);
        SetField(settings, "maxIterations", 432);
        SetField(settings, "tolerance", 0.00031f);
    }

    private static ComponentSettingsExpected BuildComponentSettingsExpected(object component)
    {
        PropertyInfo weightProperty = component.GetType().GetProperty(
            "WeightTransferSettings",
            BindingFlags.Instance | BindingFlags.Public);
        WeightTransferExpected weightTransfer = weightProperty != null
            ? BuildWeightTransferExpected(weightProperty.GetValue(component))
            : new WeightTransferExpected
            {
                maxTransferDistance = 0.05f,
                normalAngleThreshold = 60f,
                enableInpainting = true,
                maxIterations = 1000,
                tolerance = 0.000001f,
            };

        return new ComponentSettingsExpected
        {
            recalculateNormals = GetBooleanFieldOrDefault(component, "_recalculateNormals", true),
            recalculateTangents = GetBooleanFieldOrDefault(component, "_recalculateTangents", false),
            recalculateBounds = GetBooleanFieldOrDefault(component, "_recalculateBounds", true),
            recalculateBoneWeights = GetBooleanFieldOrDefault(component, "_recalculateBoneWeights", false),
            weightTransfer = weightTransfer,
        };
    }

    private static OutputBlendShapeExpected[] BuildOutputBlendShapes(Mesh mesh)
    {
        var shapes = new OutputBlendShapeExpected[mesh.blendShapeCount];
        int vertexCount = mesh.vertexCount;
        for (int shapeIndex = 0; shapeIndex < mesh.blendShapeCount; shapeIndex++)
        {
            int frameCount = mesh.GetBlendShapeFrameCount(shapeIndex);
            var frames = new OutputBlendShapeFrameExpected[frameCount];
            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                var deltaVertices = new Vector3[vertexCount];
                var deltaNormals = new Vector3[vertexCount];
                var deltaTangents = new Vector3[vertexCount];
                mesh.GetBlendShapeFrameVertices(
                    shapeIndex,
                    frameIndex,
                    deltaVertices,
                    deltaNormals,
                    deltaTangents);
                frames[frameIndex] = new OutputBlendShapeFrameExpected
                {
                    weight = mesh.GetBlendShapeFrameWeight(shapeIndex, frameIndex),
                    deltaVertices = deltaVertices,
                    deltaNormals = deltaNormals,
                    deltaTangents = deltaTangents,
                };
            }

            shapes[shapeIndex] = new OutputBlendShapeExpected
            {
                name = mesh.GetBlendShapeName(shapeIndex),
                frames = frames,
            };
        }

        return shapes;
    }

    private static void AssertSettingsBitwiseEqual(SettingsExpected expected, SettingsExpected actual, string message)
    {
        if (expected.gridSize != actual.gridSize ||
            !string.Equals(expected.interpolation, actual.interpolation, StringComparison.Ordinal) ||
            expected.legacyApplySpaceValue != actual.legacyApplySpaceValue)
        {
            throw new InvalidOperationException(message);
        }

        AssertVector3BitwiseEqual(expected.boundsCenter, actual.boundsCenter, message);
        AssertVector3BitwiseEqual(expected.boundsSize, actual.boundsSize, message);
        AssertVectorArrayBitwiseEqual(expected.controlPoints, actual.controlPoints, message);
    }

    private static void AssertOutputBlendShapesBitwiseEqual(
        OutputBlendShapeExpected[] expected,
        OutputBlendShapeExpected[] actual,
        string message)
    {
        if (expected.Length != actual.Length)
        {
            throw new InvalidOperationException(message);
        }

        for (int shapeIndex = 0; shapeIndex < expected.Length; shapeIndex++)
        {
            OutputBlendShapeExpected expectedShape = expected[shapeIndex];
            OutputBlendShapeExpected actualShape = actual[shapeIndex];
            if (!string.Equals(expectedShape.name, actualShape.name, StringComparison.Ordinal) ||
                expectedShape.frames.Length != actualShape.frames.Length)
            {
                throw new InvalidOperationException(message);
            }

            for (int frameIndex = 0; frameIndex < expectedShape.frames.Length; frameIndex++)
            {
                OutputBlendShapeFrameExpected expectedFrame = expectedShape.frames[frameIndex];
                OutputBlendShapeFrameExpected actualFrame = actualShape.frames[frameIndex];
                if (!FloatBitsEqual(expectedFrame.weight, actualFrame.weight))
                {
                    throw new InvalidOperationException(message);
                }

                AssertVectorArrayBitwiseEqual(expectedFrame.deltaVertices, actualFrame.deltaVertices, message);
                AssertVectorArrayBitwiseEqual(expectedFrame.deltaNormals, actualFrame.deltaNormals, message);
                AssertVectorArrayBitwiseEqual(expectedFrame.deltaTangents, actualFrame.deltaTangents, message);
            }
        }
    }

    private static bool HasAnyVectorBitDifference(Vector3[] first, Vector3[] second)
    {
        if (first.Length != second.Length) return true;
        for (int i = 0; i < first.Length; i++)
        {
            if (!Vector3BitsEqual(first[i], second[i])) return true;
        }
        return false;
    }

    private static void AssertVectorArrayBitwiseEqual(Vector3[] expected, Vector3[] actual, string message)
    {
        if (expected.Length != actual.Length)
        {
            throw new InvalidOperationException(message);
        }

        for (int i = 0; i < expected.Length; i++)
        {
            if (!Vector3BitsEqual(expected[i], actual[i]))
            {
                throw new InvalidOperationException($"{message} (index {i}).");
            }
        }
    }

    private static void AssertVector3BitwiseEqual(Vector3 expected, Vector3 actual, string message)
    {
        if (!Vector3BitsEqual(expected, actual))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertQuaternionBitwiseEqual(Quaternion expected, Quaternion actual, string message)
    {
        if (!FloatBitsEqual(expected.x, actual.x) ||
            !FloatBitsEqual(expected.y, actual.y) ||
            !FloatBitsEqual(expected.z, actual.z) ||
            !FloatBitsEqual(expected.w, actual.w))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static bool Vector3BitsEqual(Vector3 first, Vector3 second)
    {
        return FloatBitsEqual(first.x, second.x) &&
               FloatBitsEqual(first.y, second.y) &&
               FloatBitsEqual(first.z, second.z);
    }

    private static bool FloatBitsEqual(float first, float second)
    {
        return BitConverter.SingleToInt32Bits(first) == BitConverter.SingleToInt32Bits(second);
    }

    private static void AssertTransformProbeIsValid(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        float[] values =
        {
            position.x, position.y, position.z,
            rotation.x, rotation.y, rotation.z, rotation.w,
            scale.x, scale.y, scale.z,
        };
        if (values.Any(value => float.IsNaN(value) || float.IsInfinity(value)) ||
            scale.x == 0f || scale.y == 0f || scale.z == 0f ||
            (FloatBitsEqual(scale.x, scale.y) && FloatBitsEqual(scale.y, scale.z)))
        {
            throw new InvalidOperationException("World transform probe must be finite, invertible, and nonuniform.");
        }
    }

    private static void ValidateHistoricalComponentBeforeSave(Component component, Mesh source, bool groupSchema)
    {
        var serialized = new SerializedObject(component);
        if (serialized.FindProperty("_deformationDataVersion") != null ||
            serialized.FindProperty("_deformationDataSourceVersion") != null)
        {
            throw new InvalidOperationException("A historical tag unexpectedly contains current deformation version markers.");
        }

        if (groupSchema)
        {
            SerializedProperty modelVersion = serialized.FindProperty("_layerModelVersion");
            if (modelVersion == null || modelVersion.intValue < 0)
            {
                throw new InvalidOperationException("Published group fixture must serialize its actual _layerModelVersion.");
            }
        }

        var filter = component.GetComponent<MeshFilter>();
        if (filter == null || !ReferenceEquals(filter.sharedMesh, source))
        {
            throw new InvalidOperationException("Historical component prefab is not linked to source.asset.");
        }
    }

    private static void SavePrefab(GameObject root, string prefabPath, Type expectedComponentType)
    {
        if (root.activeSelf)
        {
            throw new InvalidOperationException("Historical fixture roots must be inactive.");
        }

        Component[] components = root.GetComponents(expectedComponentType);
        if (components.Length != 1 || !components[0] || ((Behaviour)components[0]).enabled)
        {
            throw new InvalidOperationException("Historical fixture must contain exactly one disabled target component.");
        }

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
        if (!success || prefab == null)
        {
            throw new IOException($"PrefabUtility failed to save {prefabPath}.");
        }

        AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceSynchronousImport);
        AssertFileAndMetaExist(prefabPath);
    }

    private static Mesh CreateSourceMesh(string tag)
    {
        var mesh = new Mesh { name = $"Historical Source {tag}" };
        mesh.vertices = new[]
        {
            new Vector3(-0.72f, -0.48f, -0.22f),
            new Vector3(0.68f, -0.36f, 0.12f),
            new Vector3(0.46f, 0.71f, -0.16f),
            new Vector3(-0.57f, 0.43f, 0.51f),
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f),
        };
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Vector3[] CreateLayerBrushDisplacements(int count)
    {
        var canonical = new[]
        {
            new Vector3(0.11f, -0.03f, 0.02f),
            new Vector3(-0.05f, 0.09f, -0.04f),
            new Vector3(0.03f, 0.02f, 0.08f),
            new Vector3(-0.07f, -0.01f, 0.05f),
        };
        return Enumerable.Range(0, count).Select(i => canonical[i % canonical.Length]).ToArray();
    }

    private static float[] CreateLayerMask(int count)
    {
        float[] canonical = { 0.2f, 0.55f, 0.85f, 1f };
        return Enumerable.Range(0, count).Select(i => canonical[i % canonical.Length]).ToArray();
    }

    private static Vector3[] CreateStandaloneBrushDisplacements(int count)
    {
        var canonical = new[]
        {
            new Vector3(0.14f, -0.06f, 0.03f),
            new Vector3(-0.08f, 0.12f, -0.05f),
            new Vector3(0.04f, 0.01f, 0.11f),
            new Vector3(-0.09f, -0.02f, 0.07f),
        };
        return Enumerable.Range(0, count).Select(i => canonical[i % canonical.Length]).ToArray();
    }

    private static AnimationCurve CreateHistoricalCurve(float middleValue)
    {
        var curve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0.8f, 0.15f, 0.25f),
            new Keyframe(0.45f, middleValue, 0.7f, 1.1f, 0.3f, 0.4f),
            new Keyframe(1f, 1f, 0.9f, 0f, 0.2f, 0.2f))
        {
            preWrapMode = WrapMode.ClampForever,
            postWrapMode = WrapMode.ClampForever,
        };
        return curve;
    }

    private static object CallAddLayer(object component, string name, int typeValue)
    {
        MethodInfo method = component.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(candidate => candidate.Name == "AddLayer" && candidate.GetParameters().Length == 2);
        Type enumType = method.GetParameters()[1].ParameterType;
        return method.Invoke(component, new[] { (object)name, Enum.ToObject(enumType, typeValue) });
    }

    private static Type RequireHistoricalType(string simpleName)
    {
        string fullName = $"{Namespace}.{simpleName}";
        Type type = Type.GetType($"{fullName}, {RuntimeAssemblyName}");
        if (type == null)
        {
            type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, throwOnError: false))
                .FirstOrDefault(candidate => candidate != null);
        }

        return type ?? throw new TypeLoadException($"Could not load historical type {fullName}.");
    }

    private static bool HasProperty(Type type, string name)
    {
        return type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public) != null;
    }

    private static object Call(object target, string methodName, params object[] arguments)
    {
        MethodInfo[] methods = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => method.Name == methodName && method.GetParameters().Length == arguments.Length)
            .ToArray();
        foreach (MethodInfo method in methods)
        {
            ParameterInfo[] parameters = method.GetParameters();
            bool compatible = true;
            var converted = new object[arguments.Length];
            for (int i = 0; i < arguments.Length; i++)
            {
                object argument = arguments[i];
                Type parameterType = parameters[i].ParameterType;
                if (argument == null || parameterType.IsInstanceOfType(argument))
                {
                    converted[i] = argument;
                }
                else if (parameterType.IsEnum && argument is int enumValue)
                {
                    converted[i] = Enum.ToObject(parameterType, enumValue);
                }
                else
                {
                    try
                    {
                        converted[i] = Convert.ChangeType(argument, parameterType, CultureInfo.InvariantCulture);
                    }
                    catch
                    {
                        compatible = false;
                        break;
                    }
                }
            }

            if (compatible)
            {
                return method.Invoke(target, converted);
            }
        }

        throw new MissingMethodException(target.GetType().FullName, methodName);
    }

    private static object GetProperty(object target, string name)
    {
        PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property == null)
        {
            throw new MissingMemberException(target.GetType().FullName, name);
        }
        return property.GetValue(target);
    }

    private static void SetProperty(object target, string name, object value)
    {
        PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property == null || !property.CanWrite)
        {
            throw new MissingMemberException(target.GetType().FullName, name);
        }
        property.SetValue(target, value);
    }

    private static void SetEnumProperty(object target, string name, int value)
    {
        PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property == null || !property.CanWrite || !property.PropertyType.IsEnum)
        {
            throw new MissingMemberException(target.GetType().FullName, name);
        }
        property.SetValue(target, Enum.ToObject(property.PropertyType, value));
    }

    private static object GetField(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null)
        {
            throw new MissingFieldException(target.GetType().FullName, name);
        }
        return field.GetValue(target);
    }

    private static void SetField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null)
        {
            throw new MissingFieldException(target.GetType().FullName, name);
        }
        field.SetValue(target, value);
    }

    private static void SetFieldIfPresent(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        field?.SetValue(target, value);
    }

    private static bool GetBooleanFieldOrDefault(object target, string name, bool fallback)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field != null ? Convert.ToBoolean(field.GetValue(target)) : fallback;
    }

    private static int GetSerializedIntOrDefault(UnityEngine.Object target, string propertyName, int fallback)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        return property != null ? property.intValue : fallback;
    }

    private static int GetSerializedArraySizeOrDefault(UnityEngine.Object target, string propertyName, int fallback)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        return property != null && property.isArray ? property.arraySize : fallback;
    }

    private static IList AsList(object value)
    {
        return value as IList ?? throw new InvalidCastException($"{value?.GetType().FullName ?? "null"} is not IList.");
    }

    private static string EnumName(object value)
    {
        return value?.ToString() ?? string.Empty;
    }

    private static string ClassifySerializedShape(string tag, bool groupSchema)
    {
        // Published 1.2.0 still serialized only the single-settings shape and contains
        // no provenance marker that distinguishes it from earlier releases.
        return groupSchema ? "V1_2_1" : "V0_0_1";
    }

    private static bool HasPublishedLegacyBrush(string tag)
    {
        return tag == "1.2.1" || tag == "1.3.0" || tag == "1.3.1" || tag == "1.4.0";
    }

    private static string RequireArgument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }

            string prefix = name + "=";
            if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return args[i].Substring(prefix.Length);
            }
        }

        throw new ArgumentException($"Missing required command-line argument {name}.");
    }

    private static void WriteJsonAsset(string assetPath, object value)
    {
        File.WriteAllText(ToAbsolutePath(assetPath), JsonUtility.ToJson(value, prettyPrint: true) + "\n");
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        AssertFileAndMetaExist(assetPath);
    }

    private static void AddArtifactWithMeta(ISet<string> paths, string assetPath)
    {
        AssertFileAndMetaExist(assetPath);
        paths.Add(assetPath);
        paths.Add(assetPath + ".meta");
    }

    private static void AssertFileAndMetaExist(string assetPath)
    {
        string absolute = ToAbsolutePath(assetPath);
        string meta = absolute + ".meta";
        if (!File.Exists(absolute) || !File.Exists(meta))
        {
            throw new FileNotFoundException($"Unity did not save both artifact and meta: {assetPath}");
        }
    }

    private static string ToAbsolutePath(string assetPath)
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
            ?? throw new InvalidOperationException("Could not resolve the Unity project root from Application.dataPath.");
        return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
    }

    private static string ComputeSha256(string absolutePath)
    {
        using var stream = File.OpenRead(absolutePath);
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
    }

    [Serializable]
    private sealed class ExpectedDocument
    {
        public string tag;
        public string kind;
        public string deformerPath;
        public string classifiedVersion;
        public int serializedLayerModelVersion;
        public int serializedFlatLayerCount;
        public GroupExpected[] serializedGroups;
        public LayerExpected[] serializedFlatLayers;
        public int serializedActiveFlatLayerIndex;
        public string serializedFlatBlendShapeOutput;
        public string serializedFlatBlendShapeName;
        public CurveExpected serializedFlatBlendShapeCurve;
        public bool legacyAbsoluteEvaluation;
        public float tolerance;
        public int activeGroupIndex;
        public GroupExpected[] groups;
        public Vector3[] expectedVertices;
        public OutputBlendShapeExpected[] outputBlendShapes;
        public ComponentSettingsExpected componentSettings;
        public LegacyBrushExpected legacyBrush;
        public TransformProbeExpected[] transformProbes;
    }

    [Serializable]
    private sealed class TransformProbeExpected
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public Vector3[] expectedVertices;
        public OutputBlendShapeExpected[] outputBlendShapes;
    }

    [Serializable]
    private sealed class GroupExpected
    {
        public string name;
        public bool enabled;
        public int activeLayerIndex;
        public string blendShapeOutput;
        public string blendShapeName;
        public CurveExpected blendShapeCurve;
        public LayerExpected[] layers;
    }

    [Serializable]
    private sealed class LayerExpected
    {
        public string name;
        public string type;
        public bool enabled;
        public float weight;
        public string blendShapeOutput;
        public string blendShapeName;
        public SettingsExpected settings;
        public Vector3[] brushDisplacements;
        public float[] vertexMask;
    }

    [Serializable]
    private sealed class SettingsExpected
    {
        public Vector3Int gridSize;
        public Vector3 boundsCenter;
        public Vector3 boundsSize;
        public string interpolation;
        public int legacyApplySpaceValue;
        public Vector3[] controlPoints;
    }

    [Serializable]
    private sealed class CurveExpected
    {
        public int preWrapMode;
        public int postWrapMode;
        public CurveKeyExpected[] keys;

        public static CurveExpected From(AnimationCurve curve)
        {
            curve ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
            return new CurveExpected
            {
                preWrapMode = (int)curve.preWrapMode,
                postWrapMode = (int)curve.postWrapMode,
                keys = curve.keys.Select(key => new CurveKeyExpected
                {
                    time = key.time,
                    value = key.value,
                    inTangent = key.inTangent,
                    outTangent = key.outTangent,
                    inWeight = key.inWeight,
                    outWeight = key.outWeight,
                    weightedMode = (int)key.weightedMode,
                }).ToArray(),
            };
        }
    }

    [Serializable]
    private sealed class CurveKeyExpected
    {
        public float time;
        public float value;
        public float inTangent;
        public float outTangent;
        public float inWeight;
        public float outWeight;
        public int weightedMode;
    }

    [Serializable]
    private sealed class LegacyBrushExpected
    {
        public Vector3[] displacements;
        public bool recalculateNormals;
        public bool recalculateTangents;
        public bool recalculateBounds;
        public bool recalculateBoneWeights;
        public WeightTransferExpected weightTransfer;
    }

    [Serializable]
    private sealed class ComponentSettingsExpected
    {
        public bool recalculateNormals;
        public bool recalculateTangents;
        public bool recalculateBounds;
        public bool recalculateBoneWeights;
        public WeightTransferExpected weightTransfer;
    }

    [Serializable]
    private sealed class OutputBlendShapeExpected
    {
        public string name;
        public OutputBlendShapeFrameExpected[] frames;
    }

    [Serializable]
    private sealed class OutputBlendShapeFrameExpected
    {
        public float weight;
        public Vector3[] deltaVertices;
        public Vector3[] deltaNormals;
        public Vector3[] deltaTangents;
    }

    [Serializable]
    private sealed class WeightTransferExpected
    {
        public float maxTransferDistance;
        public float normalAngleThreshold;
        public bool enableInpainting;
        public int maxIterations;
        public float tolerance;
    }

    [Serializable]
    private sealed class ManifestDocument
    {
        public string tag;
        public string commitSha;
        public string packageVersion;
        public string unityVersion;
        public string generator;
        public string generatorSha256;
        public string generationMode;
        public string goldenOutputSource;
        public ManifestFixture[] fixtures;
        public ManifestFile[] files;
    }

    [Serializable]
    private sealed class ManifestFixture
    {
        public string kind;
        public string prefab;
        public string expected;
        public string source;
        public string goldenOutputSource;
    }

    [Serializable]
    private sealed class ManifestFile
    {
        public string path;
        public string sha256;
    }
}
#endif
