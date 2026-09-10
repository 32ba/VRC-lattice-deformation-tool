// Copy into Assets/Editor of an isolated project. Run with graphics enabled.
// This measures edit notification to observable final proxy data, not presentation latency.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using nadena.dev.ndmf.preview;
using Net._32Ba.LatticeDeformationTool;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class AsyncPreviewBenchmark
{
    [Serializable] public sealed class Sample
    {
        public int vertices, ordinal, polls;
        public double milliseconds;
        public bool warmup;
    }
    [Serializable] public sealed class Report
    {
        public string unity, graphics, error;
        public string scope = "Brush payload write and interactive refresh to full final-proxy vertex match; Editor update polling included; no OS input, presentation or GC measurement";
        public bool complete;
        public List<Sample> samples = new List<Sample>();
    }
    private delegate bool ProxyLookup(Renderer original, out Renderer proxy);
    private static ProxyLookup s_lookup;
    private static Action<LatticeDeformer> s_refresh;
    private static readonly Report s_report = new Report();
    private static readonly List<Vector3> s_vertices = new List<Vector3>(200000);
    private static readonly Stopwatch s_clock = new Stopwatch();
    private static string s_output;
    private static GameObject s_owner;
    private static Mesh s_source;
    private static Vector3[] s_original;
    private static Material s_material;
    private static Renderer s_renderer;
    private static LatticeDeformer s_deformer;
    private static SceneView s_view;
    private static int s_count, s_ordinal, s_polls, s_stage;
    private static float s_expectedZ;
    private static double s_deadline;

    public static void Export()
    {
        if (!Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            throw new InvalidOperationException("Use an isolated batch Editor with D3D11.");
        var args = Environment.GetCommandLineArgs();
        s_output = args[Array.IndexOf(args, "-asyncPreviewOutput") + 1];
        s_report.unity = Application.unityVersion;
        s_report.graphics = SystemInfo.graphicsDeviceName;
        var types = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).ToArray();
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        s_lookup = (ProxyLookup)Delegate.CreateDelegate(typeof(ProxyLookup),
            types.Single(t => t.Name == "NDMFPreviewProxyUtility").GetMethod("TryGetProxyRenderer", flags,
                null, new[] { typeof(Renderer), typeof(Renderer).MakeByRefType() }, null));
        s_refresh = (Action<LatticeDeformer>)Delegate.CreateDelegate(typeof(Action<LatticeDeformer>),
            types.Single(t => t.Name == "LatticePreviewUtility").GetMethod("RefreshInteractiveDeformation", flags));
        types.Single(t => t.Name == "LatticeDeformerPreviewFilter").GetMethod("ForcePreviewState", flags).Invoke(null, new object[] { true });
        NDMFPreview.DisablePreviewDepth = 0;
        // Menu checkmarks are populated by NDMF's delayed initialization and can
        // still be false at executeMethod entry even when previews are enabled.
        if (!(bool)typeof(NDMFPreview).GetProperty("EnablePreviewsUI", flags).GetValue(null))
            EditorApplication.ExecuteMenuItem("Tools/NDM Framework/Enable Previews");
        s_view = EditorWindow.GetWindow<SceneView>();
        s_view.Show();
        s_view.pivot = Vector3.zero;
        s_view.size = 2;
        s_view.rotation = Quaternion.identity;
        s_view.orthographic = true;
        s_deadline = EditorApplication.timeSinceStartup + 60;
        EditorApplication.update += Tick;
    }

    private static void Create(int count)
    {
        s_count = count;
        int width = 500, height = count / width;
        s_original = new Vector3[count];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                s_original[y * width + x] = new Vector3(x / 500f - 0.5f, y / 500f - 0.5f, 0);
        var triangles = new int[(width - 1) * (height - 1) * 6];
        int j = 0;
        for (int y = 0; y < height - 1; y++)
            for (int x = 0; x < width - 1; x++)
            {
                int n = y * width + x;
                triangles[j++] = n; triangles[j++] = n + width; triangles[j++] = n + 1;
                triangles[j++] = n + 1; triangles[j++] = n + width; triangles[j++] = n + width + 1;
            }
        s_source = new Mesh { name = "Async preview source", indexFormat = IndexFormat.UInt32 };
        s_source.vertices = s_original; s_source.triangles = triangles;
        s_source.RecalculateNormals(); s_source.RecalculateBounds();
        var shader = Shader.Find("Standard");
        if (shader == null || !shader.isSupported) throw new Exception("Standard shader unavailable");
        s_material = new Material(shader);
        s_owner = new GameObject("Async preview fixture");
        s_owner.AddComponent<MeshFilter>().sharedMesh = s_source;
        s_renderer = s_owner.AddComponent<MeshRenderer>(); s_renderer.sharedMaterial = s_material;
        s_deformer = s_owner.AddComponent<LatticeDeformer>(); s_deformer.Reset();
        s_deformer.ActiveLayerIndex = s_deformer.AddLayer("Async brush", MeshDeformerLayerType.Brush);
        s_deformer.EnsureDisplacementCapacity();
        Selection.activeGameObject = s_owner;
        s_expectedZ = 0;
        s_ordinal = -1;
        PreviewSession.Current.ForceRebuild();
        s_deadline = EditorApplication.timeSinceStartup + 60;
    }

    private static bool Matches()
    {
        if (!s_lookup(s_renderer, out var proxy) || proxy == null || proxy == s_renderer ||
            NDMFPreview.GetOriginalObjectForProxy(proxy.gameObject) != s_owner) return false;
        var filter = proxy.GetComponent<MeshFilter>();
        var mesh = filter != null ? filter.sharedMesh : null;
        if (mesh == null || mesh == s_source || mesh.vertexCount != s_count) return false;
        mesh.GetVertices(s_vertices);
        for (int i = 0; i < s_count; i++)
            if ((s_vertices[i] - s_original[i] - new Vector3(0, 0, s_expectedZ)).sqrMagnitude > 1e-12f) return false;
        return true;
    }

    private static void Tick()
    {
        try
        {
            s_view.Repaint(); EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup > s_deadline) throw new TimeoutException("No matching proxy at stage " + s_stage);
            if (s_stage == 0)
            {
                if (PreviewSession.Current == null) return;
                Create(70000); s_stage = 1; return;
            }
            s_polls++;
            if (!Matches()) return;
            if (s_ordinal >= 0)
            {
                s_clock.Stop();
                s_report.samples.Add(new Sample { vertices = s_count, ordinal = s_ordinal,
                    warmup = s_ordinal > 0 && s_ordinal < 4, polls = s_polls, milliseconds = s_clock.Elapsed.TotalMilliseconds });
            }
            if (++s_ordinal == 19)
            {
                if (!s_source.vertices.SequenceEqual(s_original)) throw new Exception("Source mutated");
                DisposeFixture();
                if (s_count == 70000) { Create(200000); return; }
                s_report.complete = true; Finish(0); return;
            }
            s_polls = 0;
            s_expectedZ = (s_ordinal + 1) * 0.0001f;
            s_deadline = EditorApplication.timeSinceStartup + 60;
            s_clock.Restart();
            var displacements = s_deformer.Displacements;
            for (int i = 0; i < displacements.Length; i++) displacements[i] = new Vector3(0, 0, s_expectedZ);
            s_refresh(s_deformer);
        }
        catch (Exception e) { s_report.error = e.ToString(); Finish(1); }
    }

    private static void DisposeFixture()
    {
        Selection.activeGameObject = null;
        if (s_owner != null) Object.DestroyImmediate(s_owner);
        if (s_source != null) Object.DestroyImmediate(s_source);
        if (s_material != null) Object.DestroyImmediate(s_material);
    }
    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        DisposeFixture();
        File.WriteAllText(s_output, JsonUtility.ToJson(s_report, true));
        EditorApplication.Exit(code);
    }
}
