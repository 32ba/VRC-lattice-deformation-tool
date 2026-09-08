#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Net._32Ba.LatticeDeformationTool;
using Object = UnityEngine.Object;

// Runs in a disposable project first with the old package, then with its replacement.
// Copy LaterReleaseMeshCapture.cs alongside this helper; its frozen mesh oracle is shared.
public static class NormalUpgradeProbe
{
    const string Folder = "Assets/NormalUpgrade";
    [Serializable] public sealed class State
    {
        public string path, meshGuid, profileGuid, output;
        public int groups, activeGroup, activeLayer;
        public bool active, enabled, variant;
        public string[] overrides;
    }
    [Serializable] public sealed class Report
    {
        public string unity, package, source;
        public State[] objects;
    }
    static string Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        if (i < 0 || i + 1 >= args.Length) throw new ArgumentException(name);
        return args[i + 1];
    }
    public static void Create()
    {
        if (AssetDatabase.IsValidFolder(Folder)) throw new InvalidOperationException("Use a fresh project.");
        EditorSettings.serializationMode = SerializationMode.ForceText;
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        AssetDatabase.CreateFolder("Assets", "NormalUpgrade");
        var mesh = LaterReleaseMeshCapture.CreateMesh(3);
        AssetDatabase.CreateAsset(mesh, Folder + "/Source.asset");
        var go = new GameObject("Embedded"); go.SetActive(false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>();
        var d = go.AddComponent<LatticeDeformer>(); d.Reset(); d.enabled = false;
        int layer = d.AddLayer("Brush", MeshDeformerLayerType.Brush);
        d.Layers[layer].BrushDisplacements = Enumerable.Range(0, mesh.vertexCount)
            .Select(i => new Vector3(i * .013f, .025f, -.017f)).ToArray();
        d.ActiveLayerIndex = layer;
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, Folder + "/Base.prefab");
        Object.DestroyImmediate(go);
        var variant = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        variant.name = "Variant";
        d = variant.GetComponent<LatticeDeformer>();
        d.Layers[layer].Weight = .37f;
        PrefabUtility.RecordPrefabInstancePropertyModifications(d);
        var variantAsset = PrefabUtility.SaveAsPrefabAsset(variant, Folder + "/Variant.prefab");
        Object.DestroyImmediate(variant);
        var sceneInstance = (GameObject)PrefabUtility.InstantiatePrefab(variantAsset);
        sceneInstance.transform.localPosition = new Vector3(.12f, .23f, -.34f);
        PrefabUtility.RecordPrefabInstancePropertyModifications(sceneInstance.transform);
        var profileObject = Object.Instantiate(prefab); profileObject.name = "Profile";
        var profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
        AssetDatabase.CreateAsset(profile, Folder + "/Profile.asset");
        d = profileObject.GetComponent<LatticeDeformer>();
        if (!d.SaveToProfile(profile)) throw new InvalidOperationException("Profile save failed");
        d.Profile = profile; d.DataSource = DeformerDataSource.Profile;
        PrefabUtility.SaveAsPrefabAsset(profileObject, Folder + "/Profile.prefab");
        Object.DestroyImmediate(profileObject);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), Folder + "/Scene.unity");
        Capture();
    }
    public static void Capture()
    {
        CaptureInternal(false);
    }
    public static void SaveAndCapture()
    {
        CaptureInternal(true);
    }
    static void CaptureInternal(bool persist)
    {
        EditorSceneManager.OpenScene(Folder + "/Scene.unity");
        var states = new System.Collections.Generic.List<State>();
        foreach (string name in new[] { "Base", "Variant", "Profile" })
        {
            var root = PrefabUtility.LoadPrefabContents(Folder + "/" + name + ".prefab");
            try
            {
                states.Add(Read(root, name));
                if (persist) PrefabUtility.SaveAsPrefabAsset(root, Folder + "/" + name + ".prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (root.GetComponent<LatticeDeformer>() != null) states.Add(Read(root, "Scene/" + root.name));
        if (persist)
        {
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }
        var source = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Source.asset");
        var report = new Report { unity = Application.unityVersion,
            package = File.ReadAllText("Packages/net.32ba.lattice-deformation-tool/package.json"),
            source = JsonUtility.ToJson(LaterReleaseMeshCapture.CaptureMesh(source)), objects = states.ToArray() };
        File.WriteAllText(Arg("-upgradeReport"), JsonUtility.ToJson(report, true));
    }
    static State Read(GameObject root, string path)
    {
        if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(root) != 0)
            throw new InvalidOperationException("Missing script: " + path);
        var d = root.GetComponent<LatticeDeformer>();
        if (d == null) throw new InvalidOperationException("Missing deformer: " + path);
        var source = root.GetComponent<MeshFilter>().sharedMesh;
        var mesh = d.Deform(false);
        if (mesh == null) throw new InvalidOperationException("Missing output: " + path);
        // Legacy getters may rebuild caches and dispose the returned preview mesh.
        string output = JsonUtility.ToJson(LaterReleaseMeshCapture.CaptureMesh(mesh));
        var state = new State { path = path, meshGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source)),
            profileGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(d.Profile)),
            groups = d.Groups.Count, activeGroup = d.ActiveGroupIndex, activeLayer = d.ActiveLayerIndex,
            active = root.activeSelf, enabled = d.enabled, variant = PrefabUtility.GetPrefabAssetType(root) == PrefabAssetType.Variant,
            output = output,
            overrides = (PrefabUtility.GetPropertyModifications(root) ?? Array.Empty<PropertyModification>())
                .Select(p => p.propertyPath + "=" + p.value).OrderBy(p => p, StringComparer.Ordinal).ToArray() };
        if (root.GetComponent<MeshFilter>().sharedMesh != source) throw new InvalidOperationException("Source assignment changed");
        return state;
    }
}
#endif
