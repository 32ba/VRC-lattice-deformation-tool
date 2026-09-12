#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Saves real later-release Prefabs and Profile assets using only the selected tag's
/// Runtime. The original 14-tag generator is an unchanged, hash-verified helper for
/// public authoring operations and deterministic Unity identities; its corpus is not touched.
/// </summary>
public static class LaterReleaseFixtureGenerator
{
    private const string RuntimeAssembly = "net.32ba.lattice-deformation-tool";
    private const string ProductNamespace = "Net._32Ba.LatticeDeformationTool.";
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    [Serializable] public sealed class FileRecord { public string path, sha256; }
    [Serializable] public sealed class FixtureRecord { public string kind, prefab, expected, source, profile; }
    [Serializable] public sealed class Manifest
    {
        public int schemaVersion = 1;
        public string tag, commitSha, packageVersion, unityVersion;
        public string generationMode = "unity-batchmode-tag-checkout";
        public string goldenOutputSource = "historical-runtime-deform";
        public string metaGuidScheme = "sha256-v1:tag/relative-asset-path";
        public string prefabFileIdScheme = "sha256-v1:tag/relative-prefab/class/ordinal";
        public FileRecord[] tools, files;
        public FixtureRecord[] fixtures;
    }
    [Serializable] public sealed class SavedValue { public string path, type, value; }
    [Serializable] public sealed class Expected
    {
        public string tag, kind;
        public int rawVersion, rawSourceVersion, rawLayerModelVersion, rawActiveGroupIndex;
        public int rawEmbeddedGroupCount, rawFlatLayerCount;
        public SavedValue[] componentValues, profileValues;
        public LaterReleaseMeshCapture.MeshSnapshot sourceBefore, sourceAfter, output;
        public bool rendererRetainedSource, inactivePrefab, disabledComponent;
    }
    [Serializable] private sealed class CurveValue { public CurveKey[] keys; public int preWrapMode, postWrapMode; }
    [Serializable] private sealed class CurveKey
    {
        public float time, value, inTangent, outTangent, inWeight, outWeight;
        public int weightedMode;
    }

    public static void Generate()
    {
        string tag = Argument("-fixtureTag");
        string commit = Argument("-fixtureCommit");
        if (Application.unityVersion != "2022.3.22f1") throw new InvalidOperationException("Unexpected Unity version.");
        var toolRecords = JsonUtility.FromJson<ToolInput>(File.ReadAllText("Assets/Editor/tool-input.json")).tools;
        foreach (var record in toolRecords)
            if (Sha("Assets/Editor/" + Path.GetFileName(record.path)) != record.sha256)
                throw new InvalidDataException("Fixture helper source changed: " + record.path);

        EditorSettings.serializationMode = SerializationMode.ForceText;
        string outputRoot = "Assets/Generated/LaterReleases/" + tag;
        if (Directory.Exists(outputRoot)) throw new IOException("Output already exists; use a fresh generation project.");
        Directory.CreateDirectory(outputRoot);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        string sourcePath = outputRoot + "/source.asset";
        Mesh source = LaterReleaseMeshCapture.CreateMesh(3);
        source.name = "Later release full-channel source " + tag;
        AssetDatabase.CreateAsset(source, sourcePath);
        AssetDatabase.SaveAssets();

        var artifacts = new HashSet<string>(StringComparer.Ordinal);
        AddArtifact(artifacts, sourcePath);
        var fixtures = new List<FixtureRecord>();
        foreach (string kind in new[] { "embedded-preserve", "embedded-rebuild", "profile" })
        {
            if (kind == "profile" && FindType("MeshDeformerProfile") == null) continue;
            string prefab = outputRoot + "/" + kind + ".prefab";
            string profilePath = kind == "profile" ? outputRoot + "/profile.asset" : null;
            var expected = CreateFixture(tag, kind, source, prefab, profilePath);
            string expectedPath = outputRoot + "/" + kind + ".json";
            WriteJson(expectedPath, expected);
            AddArtifact(artifacts, prefab);
            AddArtifact(artifacts, expectedPath);
            if (profilePath != null) AddArtifact(artifacts, profilePath);
            fixtures.Add(new FixtureRecord
            {
                kind = kind, prefab = Path.GetFileName(prefab), expected = Path.GetFileName(expectedPath),
                source = "source.asset", profile = profilePath == null ? "" : Path.GetFileName(profilePath)
            });
        }
        AssetDatabase.SaveAssets();
        Helper("NormalizeGeneratedPrefabFileIds", outputRoot, tag, artifacts);
        Helper("NormalizeGeneratedMetaGuids", outputRoot, tag, artifacts);
        var manifest = new Manifest
        {
            tag = tag, commitSha = commit, packageVersion = Argument("-fixturePackageVersion"),
            unityVersion = Application.unityVersion, tools = toolRecords, fixtures = fixtures.ToArray(),
            files = artifacts.OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => new FileRecord { path = Path.GetFileName(p), sha256 = Sha(p) }).ToArray()
        };
        string manifestPath = outputRoot + "/manifest.json";
        WriteJson(manifestPath, manifest);
        // Existing helper rewrites only the supplied manifest identity here; previously
        // normalized payload GUIDs and file hashes stay unchanged.
        Helper("NormalizeGeneratedMetaGuids", outputRoot, tag, new[] { manifestPath, manifestPath + ".meta" });
        foreach (var file in manifest.files)
            if (Sha(outputRoot + "/" + file.path) != file.sha256)
                throw new InvalidDataException("Manifest normalization changed a payload: " + file.path);
        Debug.Log($"LATER_RELEASE_FIXTURES_SUCCEEDED tag={tag} commit={commit} fixtures={fixtures.Count}");
    }

    private static Expected CreateFixture(string tag, string kind, Mesh source, string prefabPath, string profilePath)
    {
        var root = new GameObject("Later release " + kind);
        root.SetActive(false);
        root.transform.SetPositionAndRotation(new Vector3(0.35f, -0.2f, 0.15f), Quaternion.Euler(7f, -11f, 5f));
        root.transform.localScale = new Vector3(1.1f, 0.9f, 1.2f);
        var filter = root.AddComponent<MeshFilter>();
        root.AddComponent<MeshRenderer>();
        filter.sharedMesh = source;
        var component = root.AddComponent(FindType("LatticeDeformer") ?? throw new TypeLoadException("LatticeDeformer"));
        try
        {
            Call(component, "Reset");
            Call(component, "EnsureLayerModelReady");
            Helper("ConfigurePublishedGroupSchema", component, source);
            // Use a non-linear 3x3x3 field to distinguish true Bernstein evaluation
            // from the eight-corner behavior in earlier releases.
            IList groups = (IList)Get(component, "Groups");
            object primary = groups[0];
            object lattice = ((IList)Get(primary, "LayersList"))[0];
            object settings = Get(lattice, "Settings");
            Set(settings, "GridSize", new Vector3Int(3, 3, 3));
            Call(settings, "ResetControlPoints");
            Helper("ApplyControlPointEdits", settings, 0.16f);
            // Keep both generated output and a direct contribution in every fixture.
            SetEnum(groups[1], "BlendShapeOutput", 0);
            // The first later release throws on a name collision with source frames.
            // Use its supported distinct-name contract; P0 independently fixes modern
            // collision behavior against the integrated baseline.
            bool rebuild = kind != "embedded-preserve";
            SetField(component, "_recalculateNormals", rebuild);
            SetField(component, "_recalculateTangents", rebuild);
            SetField(component, "_recalculateBounds", rebuild);
            SetField(component, "_recalculateBoneWeights", false);
            ScriptableObject profile = null;
            if (profilePath != null)
            {
                // Old Profile evaluation has a separately documented non-zero-selection
                // defect. This fixture records supported active-0 Profile output.
                Set(component, "ActiveGroupIndex", 0);
                profile = ScriptableObject.CreateInstance(FindType("MeshDeformerProfile"));
                profile.name = "Historical shared Profile";
                AssetDatabase.CreateAsset(profile, profilePath);
                if (!(bool)Call(component, "SaveToProfile", profile) || !(bool)Call(component, "UseProfile", profile))
                    throw new InvalidOperationException("The published Profile API rejected its own saved source.");
            }
            Call(component, "InvalidateCache");
            var before = LaterReleaseMeshCapture.CaptureMesh(source);
            Mesh output = (Mesh)Call(component, "Deform", false);
            if (output == null) throw new InvalidOperationException(tag + " returned no " + kind + " deformation.");
            var captured = LaterReleaseMeshCapture.CaptureMesh(output);
            Call(component, "RestoreOriginalMesh");
            Call(component, "InvalidateCache");
            var after = LaterReleaseMeshCapture.CaptureMesh(source);
            if (JsonUtility.ToJson(before) != JsonUtility.ToJson(after))
                throw new InvalidDataException("Published evaluation mutated its source Mesh.");
            if (filter.sharedMesh != source) throw new InvalidDataException("Published evaluation did not restore its renderer.");
            ((Behaviour)component).enabled = false;
            EditorUtility.SetDirty(component);
            if (profile != null) EditorUtility.SetDirty(profile);
            var expected = new Expected
            {
                tag = tag, kind = kind, rawVersion = RawInt(component, "_deformationDataVersion", -1),
                rawSourceVersion = RawInt(component, "_deformationDataSourceVersion", -1),
                rawLayerModelVersion = RawInt(component, "_layerModelVersion", -1),
                rawActiveGroupIndex = RawInt(component, "_activeGroupIndex", -1),
                rawEmbeddedGroupCount = RawCount(component, "_groups"), rawFlatLayerCount = RawCount(component, "_layers"),
                componentValues = CaptureSavedValues(component), profileValues = profile == null ? Array.Empty<SavedValue>() : CaptureSavedValues(profile),
                sourceBefore = before, sourceAfter = after, output = captured,
                rendererRetainedSource = true, inactivePrefab = true, disabledComponent = true
            };
            Helper("SavePrefab", root, prefabPath, component.GetType());
            AssetDatabase.SaveAssets();
            return expected;
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static SavedValue[] CaptureSavedValues(Object target)
    {
        var values = new List<SavedValue>();
        using var serialized = new SerializedObject(target);
        var property = serialized.GetIterator();
        bool enterChildren = true;
        while (property.Next(enterChildren))
        {
            // ObjectReference children expose volatile live m_FileID values, not
            // the persistent GUID/local-file-ID pair saved in the asset.
            enterChildren = property.propertyType != SerializedPropertyType.ObjectReference;
            string path = property.propertyPath;
            // Engine identity fields are verified through Prefab/source references.
            if (!path.StartsWith("_", StringComparison.Ordinal)) continue;
            if (property.propertyType == SerializedPropertyType.Generic) continue;
            string value;
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer: case SerializedPropertyType.ArraySize:
                case SerializedPropertyType.Enum: value = property.longValue.ToString(CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.Boolean: value = property.boolValue ? "true" : "false"; break;
                case SerializedPropertyType.Float: value = property.floatValue.ToString("R", CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.String: value = property.stringValue; break;
                case SerializedPropertyType.Vector2: value = JsonUtility.ToJson(property.vector2Value); break;
                case SerializedPropertyType.Vector3: value = JsonUtility.ToJson(property.vector3Value); break;
                case SerializedPropertyType.Vector4: value = JsonUtility.ToJson(property.vector4Value); break;
                case SerializedPropertyType.Vector2Int: value = JsonUtility.ToJson(property.vector2IntValue); break;
                case SerializedPropertyType.Vector3Int: value = JsonUtility.ToJson(property.vector3IntValue); break;
                case SerializedPropertyType.Bounds: value = JsonUtility.ToJson(property.boundsValue); break;
                case SerializedPropertyType.Quaternion: value = JsonUtility.ToJson(property.quaternionValue); break;
                case SerializedPropertyType.Color: value = JsonUtility.ToJson(property.colorValue); break;
                case SerializedPropertyType.AnimationCurve:
                    var curve = property.animationCurveValue;
                    value = JsonUtility.ToJson(new CurveValue
                    {
                        keys = curve.keys.Select(k => new CurveKey
                        {
                            time = k.time, value = k.value, inTangent = k.inTangent, outTangent = k.outTangent,
                            inWeight = k.inWeight, outWeight = k.outWeight, weightedMode = (int)k.weightedMode
                        }).ToArray(), preWrapMode = (int)curve.preWrapMode, postWrapMode = (int)curve.postWrapMode
                    }); break;
                case SerializedPropertyType.ObjectReference:
                    Object reference = property.objectReferenceValue;
                    if (reference == null) value = "null";
                    else if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(reference, out string guid, out long localId))
                        value = guid + ":" + localId.ToString(CultureInfo.InvariantCulture);
                    else if (reference is Component local && target is Component owner && local.gameObject == owner.gameObject)
                        value = "local:self:" + local.GetType().FullName;
                    else throw new InvalidDataException("Unexpected unsaved reference at " + path);
                    break;
                default: throw new NotSupportedException("Uncaptured saved property " + property.propertyType + " at " + path);
            }
            values.Add(new SavedValue { path = path, type = property.propertyType.ToString(), value = value });
        }
        return values.ToArray();
    }

    private static int RawInt(Object target, string name, int fallback)
    { using var value = new SerializedObject(target); return value.FindProperty(name)?.intValue ?? fallback; }
    private static int RawCount(Object target, string name)
    { using var value = new SerializedObject(target); return value.FindProperty(name)?.arraySize ?? -1; }
    private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .First(a => a.GetName().Name == RuntimeAssembly).GetType(ProductNamespace + name, false);
    private static object Helper(string name, params object[] args) => typeof(HistoricalFixtureGenerator)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
    private static object Call(object target, string name, params object[] args) => Helper("Call", target, name, args);
    private static object Get(object target, string name) => target.GetType().GetProperty(name, Instance).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetProperty(name, Instance).SetValue(target, value);
    private static void SetEnum(object target, string name, int value)
    { var property = target.GetType().GetProperty(name, Instance); property.SetValue(target, Enum.ToObject(property.PropertyType, value)); }
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, Instance).SetValue(target, value);
    private static void AddArtifact(ISet<string> paths, string path) { paths.Add(path); paths.Add(path + ".meta"); }
    private static void WriteJson(string path, object value)
    { File.WriteAllText(path, JsonUtility.ToJson(value, true) + "\n", new UTF8Encoding(false)); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport); }
    private static string Sha(string path)
    { using var sha = SHA256.Create(); using var stream = File.OpenRead(path); return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2"))); }
    private static string Argument(string name)
    { var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : throw new ArgumentException(name); }
    [Serializable] private sealed class ToolInput { public FileRecord[] tools; }
}
#endif
