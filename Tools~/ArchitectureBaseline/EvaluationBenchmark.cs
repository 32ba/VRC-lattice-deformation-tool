// Copy into Assets/Editor of the isolated baseline/candidate project.
// This harness measures synchronous evaluation, not Scene View interaction.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Net._32Ba.LatticeDeformationTool;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

public static class EvaluationBenchmark
{
    [Serializable] public sealed class Measurement
    {
        public double milliseconds;
        public long allocatedBytes;
        public int allocationSamples;
        public bool allocationSamplesComplete;
        public int profilerFrame;
    }
    [Serializable] public sealed class Scenario
    {
        public string name;
        public int vertices, groups;
        public bool generatedBlendShape;
        public bool profileSource;
        public bool upstreamPreview;
        public int clearanceReferenceVertices;
        public int scanConditionCount;
        public string brushDisplacementHash;
        public string proportionalInfluenceHash;
        public bool recalculateNormals, recalculateTangents;
        public bool recalculateBounds = true;
        public Measurement firstEvaluation;
        public Measurement[] unchanged, edited;
        public double unchangedP50, unchangedP95, editedP50, editedP95;
        public int meshCountDeltaAfterDispose;
    }
    [Serializable] public sealed class Document
    {
        public string commit, unityVersion, utc, processor, operatingSystem, scenarioFilter;
        public int processorCount, memoryMb, sampleCount;
        public int maxPrivateMemoryMb;
        public bool gcProfilerSupported;
        public long allocationCalibrationBytes;
        public bool complete;
        public string error;
        public string allocationScope = "GC.Alloc size metadata under the named CPU Profiler sample; Editor main thread";
        public string limitation = "Headless evaluation benchmark. Does not measure rendering, drag input latency, or total native allocations.";
        public List<Scenario> scenarios = new List<Scenario>();
    }

    private sealed class Work
    {
        public Action action;
        public Action<Measurement> accept;
        public string marker;
        public string captureName;
    }
    private static Document s_document;
    private static string s_output;
    private static IEnumerator<Work> s_work;
    private static Work s_pending;
    private static int s_frameBefore;
    private static CustomSampler s_sampler;
    private static double s_startedWaiting;
    private static bool s_actionScheduled;
    private static bool s_previousEnabled, s_previousProfileEditor, s_previousCpu, s_previousMemory;

    public static void Export()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run only in an isolated batch Editor.");
        s_output = Argument("-latticeBenchmarkOutput");
        s_sampler = CustomSampler.Create("Lattice.Architecture.Measure");
        string commit = Argument("-latticeBenchmarkCommit");
        int samples = int.Parse(Argument("-latticeBenchmarkSamples", "15"));
        if (samples < 3 || samples > 200) throw new ArgumentOutOfRangeException(nameof(samples));
        s_document = new Document
        {
            commit = commit, unityVersion = Application.unityVersion, utc = DateTime.UtcNow.ToString("O"),
            processor = SystemInfo.processorType, processorCount = SystemInfo.processorCount,
            operatingSystem = SystemInfo.operatingSystem, memoryMb = SystemInfo.systemMemorySize,
            sampleCount = samples, scenarioFilter = Argument("-latticeBenchmarkFilter", ""),
            maxPrivateMemoryMb = int.Parse(Argument("-latticeBenchmarkMaxPrivateMb", "6144"))
        };
        s_previousEnabled = ProfilerDriver.enabled;
        s_previousProfileEditor = ProfilerDriver.profileEditor;
        s_previousCpu = ProfilerDriver.IsAreaEnabled(ProfilerArea.CPU);
        s_previousMemory = ProfilerDriver.IsAreaEnabled(ProfilerArea.Memory);
        ProfilerDriver.ClearAllFrames();
        ProfilerDriver.profileEditor = true;
        ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, true);
        // The CPU stream contains GC.Alloc metadata. The Memory module is not
        // needed for this measurement and can retain large native object dumps.
        ProfilerDriver.SetAreaEnabled(ProfilerArea.Memory, false);
        ProfilerDriver.enabled = true;
        s_work = WorkItems(samples, Argument("-latticeBenchmarkCalibrationOnly", "false") == "true").GetEnumerator();
        AssemblyReloadEvents.beforeAssemblyReload += AbortForAssemblyReload;
        EditorApplication.update += Tick;
        EditorApplication.QueuePlayerLoopUpdate();
    }

    private static IEnumerable<Work> WorkItems(int samples, bool calibrationOnly)
    {
        yield return new Work
        {
            captureName = "calibration",
            action = () => GC.KeepAlive(new byte[16384]),
            accept = measurement =>
            {
                s_document.allocationCalibrationBytes = measurement.allocatedBytes;
                s_document.gcProfilerSupported = measurement.allocatedBytes >= 16384 && measurement.allocationSamplesComplete;
                if (!s_document.gcProfilerSupported) throw new InvalidOperationException("GC.Alloc size calibration failed.");
            }
        };
        if (calibrationOnly) yield break;
        foreach (int vertices in new[] { 70000, 200000 })
            foreach (string kind in new[] { "direct", "groups", "generated", "profile", "preview", "clearance", "fit", "scan", "brush-normal", "proportional" })
            {
                string scenarioName = kind + "-" + vertices;
                if (!string.IsNullOrEmpty(s_document.scenarioFilter) &&
                    !s_document.scenarioFilter.Split(';').Contains(scenarioName)) continue;
                foreach (var work in kind == "scan" ? RunScan(vertices, samples) : kind == "fit" ? RunFit(vertices, samples) : kind == "clearance" ? RunClearance(vertices, samples) :
                    kind == "proportional" ? RunProportional(vertices, samples) : Run(vertices, kind, samples)) yield return work;
                SaveDocument();
                UnityEngine.Debug.Log("Completed evaluation benchmark: " + kind + " " + vertices);
            }
    }

    private static IEnumerable<Work> Run(int vertexCount, string kind, int samples)
    {
        int meshCountBefore = Resources.FindObjectsOfTypeAll<Mesh>().Length;
        var root = new GameObject("Evaluation benchmark");
        var mesh = CreateMesh(vertexCount);
        MeshDeformerProfile profile = null;
        Mesh upstream = null;
        Action releaseBrush = null;
        var result = new Scenario
        {
            name = kind + "-" + vertexCount, vertices = vertexCount, groups = kind == "groups" ? 4 : 1,
            generatedBlendShape = kind == "generated", profileSource = kind == "profile",
            upstreamPreview = kind == "preview",
            unchanged = new Measurement[samples], edited = new Measurement[samples]
        };
        try
        {
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>();
            var deformer = root.AddComponent<LatticeDeformer>();
            deformer.Reset();
            for (int group = 0; group < result.groups; group++)
            {
                if (group > 0)
                {
                    deformer.AddGroup("Group " + group);
                    deformer.AddLayer("Lattice " + group);
                }
                var settings = deformer.EditingSettings;
                settings.SetControlPointLocal(0, settings.GetControlPointLocal(0) + Vector3.up * 0.1f);
                if (kind == "groups")
                {
                    int brush = deformer.AddLayer("Brush " + group, MeshDeformerLayerType.Brush);
                    var layer = deformer.Layers[brush];
                    for (int i = 0; i < vertexCount; i += 13) layer.SetBrushDisplacement(i, Vector3.forward * 0.01f);
                }
            }
            deformer.ActiveGroupIndex = 0;
            deformer.ActiveLayerIndex = 0;
            if (result.generatedBlendShape)
            {
                deformer.ActiveGroup.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                deformer.ActiveGroup.BlendShapeName = "Generated";
            }
            using (var serialized = new SerializedObject(deformer))
            {
                serialized.FindProperty("_recalculateNormals").boolValue = false;
                serialized.FindProperty("_recalculateTangents").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            if (result.profileSource)
            {
                profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
                profile.Capture(deformer.Groups, 0, mesh);
                if (!deformer.UseProfile(profile)) throw new InvalidOperationException("Profile was rejected.");
            }
            if (result.upstreamPreview)
            {
                upstream = Object.Instantiate(mesh);
                var points = upstream.vertices;
                for (int i = 0; i < points.Length; i++) points[i] += Vector3.forward * 0.02f;
                upstream.vertices = points;
                var deltas = new Vector3[vertexCount];
                for (int i = 0; i < deltas.Length; i++) deltas[i] = Vector3.up * 0.01f;
                upstream.AddBlendShapeFrame("Upstream", 100f, deltas, null, null);
            }
            Action<bool> applyBrush = null;
            if (kind == "brush-normal")
            {
                deformer.ActiveLayerIndex = deformer.AddLayer("Benchmark brush", MeshDeformerLayerType.Brush);
                deformer.EnsureDisplacementCapacity();
                var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(
                    "Net._32Ba.LatticeDeformationTool.Editor.BrushToolHandler")).First(t => t != null);
                object handler = Activator.CreateInstance(type, true);
                var instance = Expression.Constant(handler, type);
                var fields = new Dictionary<FieldInfo, object>();
                foreach (string name in new[] { "s_connectedOnly", "s_backfaceCulling", "s_useSurfaceDistance" })
                {
                    var field = type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
                    fields.Add(field, field.GetValue(null)); field.SetValue(null, false);
                }
                var falloff = type.GetField("s_brushFalloff", BindingFlags.Static | BindingFlags.NonPublic);
                fields.Add(falloff, falloff.GetValue(null));
                falloff.SetValue(null, Enum.Parse(falloff.FieldType, "Linear"));
                releaseBrush = () =>
                {
                    type.GetMethod("Deactivate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(handler, null);
                    foreach (var field in fields) field.Key.SetValue(null, field.Value);
                };
                type.GetMethod("Activate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(handler, new object[] { deformer });
                var altered = Expression.Parameter(typeof(bool), "altered");
                var hit = Expression.Condition(altered,
                    Expression.Constant(new Vector3(0.25f, 0.25f, 0f)),
                    Expression.Constant(new Vector3(0.2f, 0.2f, 0f)));
                var rebuild = type.GetMethod("RebuildCacheIfNeeded", BindingFlags.Instance | BindingFlags.NonPublic);
                var apply = type.GetMethod("ApplyNormalBrush", BindingFlags.Instance | BindingFlags.NonPublic);
                applyBrush = Expression.Lambda<Action<bool>>(Expression.Block(
                    Expression.Call(instance, rebuild, Expression.Constant(mesh), Expression.Constant(deformer)),
                    Expression.Call(instance, apply, Expression.Constant(deformer), hit,
                        Expression.Constant(0.1f), Expression.Constant(0.001f), Expression.Constant(1f)),
                    Expression.Empty()), altered).Compile();
            }
            // Profile edits change the shared asset, exercising external change
            // detection and replacement of the owner's independent evaluation copy.
            var lattice = result.profileSource ? profile.Groups[0].Layers[0].Settings : deformer.Layers[0].Settings;
            var startPoint = lattice.GetControlPointLocal(0);
            // Bind once outside measurement; avoid reflection allocation per sample.
            var notify = (Action)Delegate.CreateDelegate(typeof(Action), deformer,
                typeof(LatticeDeformer).GetMethod("NotifyDeformationDataChanged", BindingFlags.Instance | BindingFlags.NonPublic));
            bool alternateBrushHit = false;
            Action evaluate = () =>
            {
                if (applyBrush != null) { applyBrush(alternateBrushHit); notify(); }
                Mesh output = null;
                try
                {
                    output = result.upstreamPreview ? deformer.CreatePreviewMeshFromInput(upstream) : deformer.Deform(false);
                    if (output == null || output.vertexCount != vertexCount)
                        throw new InvalidOperationException("Evaluation was rejected or changed vertex count.");
                    if (result.upstreamPreview && (output == upstream || output == mesh ||
                        output.blendShapeCount != 1 || output.GetBlendShapeName(0) != "Upstream" ||
                        output.GetBlendShapeFrameCount(0) != 1))
                        throw new InvalidOperationException("Expected an independent upstream Preview with one source frame.");
                    if (result.generatedBlendShape &&
                        (output.blendShapeCount != 1 || output.GetBlendShapeFrameCount(0) != 100))
                        throw new InvalidOperationException("Expected the generated 100-frame BlendShape workload.");
                }
                finally
                {
                    // Preview returns a caller-owned clone; include its disposal in each operation.
                    if (result.upstreamPreview && output != null && output != upstream && output != mesh)
                        Object.DestroyImmediate(output);
                }
            };
            int edit = 0;
            Action editedEvaluation = () =>
            {
                if (applyBrush != null) alternateBrushHit = ++edit % 2 == 0;
                else lattice.SetControlPointLocal(0, startPoint + Vector3.up * ((++edit % 2 == 0) ? 0.01f : -0.01f));
                if (!result.profileSource) notify();
                evaluate();
            };
            yield return new Work { action = evaluate, accept = value => result.firstEvaluation = value,
                captureName = result.name + ".first" };
            for (int i = 0; i < 3; i++) yield return new Work { action = evaluate };
            for (int i = 0; i < samples; i++)
            {
                int index = i;
                yield return new Work { action = evaluate, accept = value => result.unchanged[index] = value,
                    captureName = i == samples - 1 ? result.name + ".unchanged-last" : null };
            }
            for (int i = 0; i < 3; i++) yield return new Work { action = editedEvaluation };
            for (int i = 0; i < samples; i++)
            {
                int index = i;
                yield return new Work { action = editedEvaluation, accept = value => result.edited[index] = value,
                    captureName = i == samples - 1 ? result.name + ".edited-last" : null };
            }
            if (applyBrush != null)
            {
                var displacements = deformer.Displacements;
                if (!displacements.Any(v => v.sqrMagnitude > 0f)) throw new InvalidOperationException("Brush changed no vertices.");
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream))
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    foreach (var v in displacements) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
                    writer.Flush();
                    result.brushDisplacementHash = BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
                }
            }
            result.unchangedP50 = Percentile(result.unchanged, 0.5);
            result.unchangedP95 = Percentile(result.unchanged, 0.95);
            result.editedP50 = Percentile(result.edited, 0.5);
            result.editedP95 = Percentile(result.edited, 0.95);
        }
        finally
        {
            releaseBrush?.Invoke();
            Object.DestroyImmediate(root);
            if (upstream != null) Object.DestroyImmediate(upstream);
            if (profile != null) Object.DestroyImmediate(profile);
            Object.DestroyImmediate(mesh);
        }
        result.meshCountDeltaAfterDispose = Resources.FindObjectsOfTypeAll<Mesh>().Length - meshCountBefore;
        s_document.scenarios.Add(result);
    }


    private static IEnumerable<Work> RunScan(int vertexCount, int samples)
    {
        int meshCountBefore = Resources.FindObjectsOfTypeAll<Mesh>().Length;
        var root = new GameObject("Scan benchmark root");
        var targetRoot = new GameObject("Target");
        targetRoot.transform.SetParent(root.transform, false);
        var referenceRoot = new GameObject("Reference");
        referenceRoot.transform.SetParent(root.transform, false);
        var mesh = CreateMesh(vertexCount);
        var referenceMesh = new Mesh { name = "Scan reference plane" };
        var scanSet = ScriptableObject.CreateInstance<ClearanceScanSet>();
        var depths = new[] { -0.002f, 0.003f, 0.015f };
        var scenario = new Scenario { name = "scan-" + vertexCount, vertices = vertexCount,
            clearanceReferenceVertices = 4, scanConditionCount = depths.Length,
            unchanged = new Measurement[samples], edited = new Measurement[samples] };
        LatticeDeformer deformer = null;
        Action clearCache = null;
        try
        {
            referenceMesh.vertices = new[] { new Vector3(-2,-2,0), new Vector3(2,-2,0),
                new Vector3(2,2,0), new Vector3(-2,2,0) };
            referenceMesh.triangles = new[] { 0,1,2,0,2,3 };
            referenceMesh.RecalculateNormals();
            referenceMesh.RecalculateBounds();
            targetRoot.AddComponent<MeshFilter>().sharedMesh = mesh;
            targetRoot.AddComponent<MeshRenderer>();
            referenceRoot.AddComponent<MeshFilter>().sharedMesh = referenceMesh;
            var reference = referenceRoot.AddComponent<MeshRenderer>();
            var originalPosition = new Vector3(0, 0, 0.037f);
            targetRoot.transform.localPosition = originalPosition;
            deformer = targetRoot.AddComponent<LatticeDeformer>();
            deformer.Reset();
            var sourceVertices = mesh.vertices;
            var sourceIndices = mesh.triangles;
            string originalPayload = EditorJsonUtility.ToJson(deformer);
            var referenceVertices = referenceMesh.vertices;
            var referenceIndices = referenceMesh.triangles;
            float expectedReferenceZ = 0f;
            foreach (float depth in depths)
            {
                var condition = new ClearanceScanCondition { Name = "Z=" + depth };
                condition.TransformOverrides.Add(new ClearanceTransformPoseOverride {
                    RelativePath = "Target", OverridePosition = true,
                    LocalPosition = Vector3.forward * depth, OverrideRotation = false, OverrideScale = false });
                scanSet.Conditions.Add(condition);
            }
            Func<string, Type> find = name => AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Net._32Ba.LatticeDeformationTool.Editor." + name)).First(t => t != null);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var type = find("ClearanceScanOperation");
            var ctor = type.GetConstructors(flags).Single();
            var cp = ctor.GetParameters();
            var args = new Expression[] { Expression.Constant(scanSet), Expression.Constant(deformer),
                Expression.Constant(reference, typeof(Renderer)), Expression.Constant(root.transform),
                Expression.Constant(Enum.Parse(cp[4].ParameterType, "ReferenceNormal")),
                Expression.Constant(0.005f), Expression.Constant(0.01f), Expression.Constant(true),
                Expression.Default(cp[8].ParameterType), Expression.Default(cp[9].ParameterType) };
            var operation = Expression.Variable(type, "operation");
            var run = type.GetMethod("RunToCompletion", flags);
            // Constructor, complete scan, restoration, and disposal are timed together.
            // Reflection and the independent numerical oracle remain outside the marker.
            var execute = Expression.Lambda<Func<object>>(Expression.Block(new[] { operation },
                Expression.Assign(operation, Expression.New(ctor, args)),
                Expression.TryFinally(Expression.Convert(Expression.Call(operation, run,
                    Expression.Default(run.GetParameters()[0].ParameterType)), typeof(object)),
                    Expression.Call(Expression.Convert(operation, typeof(IDisposable)),
                        typeof(IDisposable).GetMethod("Dispose"))))).Compile();
            clearCache = (Action)Delegate.CreateDelegate(typeof(Action), find("ClearanceQueryCache")
                .GetMethod("Clear", BindingFlags.Static | BindingFlags.NonPublic));
            clearCache();
            object report = null;
            Action action = () => report = execute();
            Action validate = () =>
            {
                var reportType = report.GetType();
                var conditions = (IList)reportType.GetField("Conditions", flags).GetValue(report);
                if ((bool)reportType.GetField("WasCancelled", flags).GetValue(report) || conditions.Count != depths.Length)
                    throw new InvalidOperationException("Scan did not complete every condition.");
                for (int c = 0; c < conditions.Count; c++)
                {
                    object result = conditions[c];
                    var rt = result.GetType();
                    if (!(bool)rt.GetProperty("IsSuccess", flags).GetValue(result) ||
                        (bool)rt.GetField("UsedNdmfPreviewProxy", flags).GetValue(result))
                        throw new InvalidOperationException("Unexpected Scan condition status or proxy.");
                    var values = (float[])rt.GetField("VertexClearances", flags).GetValue(result);
                    float expected = depths[c] - expectedReferenceZ;
                    if (values.Length != vertexCount || values.Any(v => !float.IsFinite(v) || Mathf.Abs(v - expected) > 1e-5f))
                        throw new InvalidOperationException("Scan condition differs from the plane oracle.");
                }
                var worst = (float[])reportType.GetField("WorstClearances", flags).GetValue(report);
                var indices = (int[])reportType.GetField("WorstConditionIndices", flags).GetValue(report);
                float expectedWorst = depths[0] - expectedReferenceZ;
                if (worst.Length != vertexCount || indices.Length != vertexCount || indices.Any(v => v != 0) ||
                    worst.Any(v => !float.IsFinite(v) || Mathf.Abs(v - expectedWorst) > 1e-5f) ||
                    targetRoot.transform.localPosition != originalPosition ||
                    referenceRoot.transform.localPosition != Vector3.forward * expectedReferenceZ ||
                    EditorJsonUtility.ToJson(deformer) != originalPayload ||
                    !sourceVertices.SequenceEqual(mesh.vertices) || !sourceIndices.SequenceEqual(mesh.triangles) ||
                    !referenceVertices.SequenceEqual(referenceMesh.vertices) ||
                    !referenceIndices.SequenceEqual(referenceMesh.triangles))
                    throw new InvalidOperationException("Scan worst-condition, restoration, or source preservation failed.");
            };
            yield return new Work { action = action, accept = m => scenario.firstEvaluation = m,
                captureName = scenario.name + ".first" };
            validate();
            for (int phase = 0; phase < 2; phase++)
                for (int i = -3; i < samples; i++)
                {
                    expectedReferenceZ = phase == 0 || (i & 1) == 0 ? 0f : 0.001f;
                    referenceRoot.transform.localPosition = Vector3.forward * expectedReferenceZ;
                    int index = i;
                    var values = phase == 0 ? scenario.unchanged : scenario.edited;
                    yield return new Work { action = action,
                        accept = i < 0 ? (Action<Measurement>)null : m => values[index] = m,
                        captureName = i == samples - 1 ? scenario.name + (phase == 0 ? ".unchanged-last" : ".edited-last") : null };
                    validate();
                }
            scenario.unchangedP50 = Percentile(scenario.unchanged, 0.5);
            scenario.unchangedP95 = Percentile(scenario.unchanged, 0.95);
            scenario.editedP50 = Percentile(scenario.edited, 0.5);
            scenario.editedP95 = Percentile(scenario.edited, 0.95);
        }
        finally
        {
            clearCache?.Invoke();
            if (deformer != null && deformer.RuntimeMesh != null) Object.DestroyImmediate(deformer.RuntimeMesh);
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(referenceMesh);
            Object.DestroyImmediate(scanSet);
        }
        scenario.meshCountDeltaAfterDispose = Resources.FindObjectsOfTypeAll<Mesh>().Length - meshCountBefore;
        s_document.scenarios.Add(scenario);
    }

    private static IEnumerable<Work> RunFit(int vertexCount, int samples)
    {
        int meshCountBefore = Resources.FindObjectsOfTypeAll<Mesh>().Length;
        var root = new GameObject("Fit benchmark target");
        var referenceRoot = new GameObject("Fit benchmark reference");
        var mesh = CreateMesh(vertexCount);
        var referenceMesh = new Mesh { name = "Fit reference plane" };
        var scenario = new Scenario { name = "fit-" + vertexCount, vertices = vertexCount,
            clearanceReferenceVertices = 4, unchanged = new Measurement[samples], edited = new Measurement[samples] };
        LatticeDeformer deformer = null;
        Action clearCache = null;
        try
        {
            referenceMesh.vertices = new[] { new Vector3(-2,-2,0), new Vector3(2,-2,0),
                new Vector3(2,2,0), new Vector3(-2,2,0) };
            referenceMesh.triangles = new[] { 0,1,2,0,2,3 };
            referenceMesh.RecalculateNormals();
            referenceMesh.RecalculateBounds();
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var target = root.AddComponent<MeshRenderer>();
            referenceRoot.AddComponent<MeshFilter>().sharedMesh = referenceMesh;
            var reference = referenceRoot.AddComponent<MeshRenderer>();
            root.transform.position = Vector3.back * 0.002f;
            deformer = root.AddComponent<LatticeDeformer>();
            deformer.Reset();
            var sourceVertices = mesh.vertices;
            var sourceIndices = mesh.triangles;
            int initialLayers = deformer.Layers.Count;

            Func<string, Type> find = name => AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Net._32Ba.LatticeDeformationTool.Editor." + name)).First(t => t != null);
            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            var evaluate = find("ClearanceHeatmapEvaluator").GetMethod("Evaluate", flags);
            var analyze = find("FitCorrectionGenerator").GetMethod("Analyze", flags);
            var generate = find("FitCorrectionGenerator").GetMethod("Generate", flags);
            clearCache = (Action)Delegate.CreateDelegate(typeof(Action), find("ClearanceQueryCache").GetMethod("Clear", flags));
            clearCache();
            var raw = Expression.Variable(evaluate.ReturnType, "raw");
            var plan = Expression.Variable(analyze.ReturnType, "plan");
            var ap = analyze.GetParameters();
            var sign = evaluate.GetParameters()[2].ParameterType;
            var queryMode = Expression.Constant(Enum.Parse(ap[3].ParameterType, "ReferenceNormal"));
            var scope = Expression.Constant(Enum.Parse(ap[4].ParameterType, "TargetClearance"));
            var owner = Expression.Constant(deformer);
            var referenceArg = Expression.Constant(reference, typeof(Renderer));
            // Build one direct-call delegate before timing: reflection and invocation arrays
            // are diagnostic setup only, not part of the measured product operation.
            var operation = Expression.Lambda<Func<object>>(Expression.Block(new[] { raw, plan },
                Expression.Assign(raw, Expression.Call(evaluate, Expression.Constant(target, typeof(Renderer)),
                    referenceArg, Expression.Constant(Enum.Parse(sign, "ReferenceNormal")))),
                Expression.Assign(plan, Expression.Call(analyze, owner, raw, referenceArg, queryMode, scope,
                    Expression.Constant(0.005f), Expression.Constant(0.01f), Expression.Constant(0.1f),
                    Expression.Default(ap[8].ParameterType))),
                Expression.Convert(Expression.Call(generate, owner, plan, referenceArg, queryMode, scope,
                    Expression.Constant(0.005f), Expression.Constant(0.01f), Expression.Constant(0.1f)), typeof(object)))).Compile();
            var reportType = generate.ReturnType;
            var reportFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            object report = null;
            Action operationAction = () => report = operation();
            Action validateAndReset = () =>
            {
                if (reportType.GetField("Status", reportFlags).GetValue(report).ToString() != "Success" ||
                    (int)reportType.GetField("MovedVertexCount", reportFlags).GetValue(report) != vertexCount ||
                    (int)reportType.GetField("ImprovedVertexCount", reportFlags).GetValue(report) != vertexCount ||
                    (int)reportType.GetField("UnresolvedVertexCount", reportFlags).GetValue(report) != 0 ||
                    deformer.Layers.Count != initialLayers + 1)
                    throw new InvalidOperationException("Fit result does not meet the independent plane oracle.");
                var layer = deformer.Layers[initialLayers];
                float expected = 0.01f - root.transform.position.z;
                for (int i = 0; i < vertexCount; i++)
                    if ((layer.GetBrushDisplacement(i) - Vector3.forward * expected).sqrMagnitude > 1e-10f)
                        throw new InvalidOperationException("Fit displacement mismatch at " + i);
                if (!sourceVertices.SequenceEqual(mesh.vertices) || !sourceIndices.SequenceEqual(mesh.triangles))
                    throw new InvalidOperationException("Fit modified source mesh.");
                deformer.RemoveLayer(initialLayers);
                deformer.ActiveLayerIndex = 0;
            };
            yield return new Work { action = operationAction, accept = m => scenario.firstEvaluation = m,
                captureName = scenario.name + ".first" };
            validateAndReset();
            for (int phase = 0; phase < 2; phase++)
            {
                for (int i = -3; i < samples; i++)
                {
                    root.transform.position = Vector3.back * (phase == 0 || (i & 1) == 0 ? 0.002f : 0.004f);
                    int index = i;
                    var values = phase == 0 ? scenario.unchanged : scenario.edited;
                    yield return new Work { action = operationAction,
                        accept = i < 0 ? (Action<Measurement>)null : m => values[index] = m,
                        captureName = i == samples - 1 ? scenario.name + (phase == 0 ? ".unchanged-last" : ".edited-last") : null };
                    validateAndReset();
                }
            }
            scenario.unchangedP50 = Percentile(scenario.unchanged, 0.5);
            scenario.unchangedP95 = Percentile(scenario.unchanged, 0.95);
            scenario.editedP50 = Percentile(scenario.edited, 0.5);
            scenario.editedP95 = Percentile(scenario.edited, 0.95);
        }
        finally
        {
            clearCache?.Invoke();
            if (deformer != null && deformer.RuntimeMesh != null) Object.DestroyImmediate(deformer.RuntimeMesh);
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(referenceRoot);
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(referenceMesh);
        }
        scenario.meshCountDeltaAfterDispose = Resources.FindObjectsOfTypeAll<Mesh>().Length - meshCountBefore;
        s_document.scenarios.Add(scenario);
    }

    private static IEnumerable<Work> RunClearance(int vertexCount, int samples)
    {
        int meshCountBefore = Resources.FindObjectsOfTypeAll<Mesh>().Length;
        var reference = CreateMesh(4096);
        var scenario = new Scenario
        {
            name = "clearance-" + vertexCount, vertices = vertexCount, clearanceReferenceVertices = 4096,
            unchanged = new Measurement[samples], edited = new Measurement[samples]
        };
        try
        {
            // Resolve internal API once in this external diagnostic harness. Compiled
            // delegates exclude reflection, boxing and invocation arrays from measurements.
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(
                "Net._32Ba.LatticeDeformationTool.Editor.ClearanceQuery")).First(t => t != null);
            var create = type.GetMethod("TryCreate", BindingFlags.Static | BindingFlags.NonPublic);
            var local = Expression.Variable(type, "query");
            var build = Expression.Lambda<Func<object>>(Expression.Block(new[] { local },
                Expression.Call(create, Expression.Constant(reference), Expression.Constant(Matrix4x4.identity), local),
                Expression.Convert(local, typeof(object)))).Compile();
            var method = type.GetMethod("QueryPoints", BindingFlags.Instance | BindingFlags.NonPublic);
            var parameters = method.GetParameters();
            var points = new Vector3[vertexCount];
            for (int i = 0; i < points.Length; i++)
                points[i] = new Vector3((i % 250 + 0.25f) * 0.002f, ((i / 250) % 14 + 0.25f) * 0.002f, 0.01f);
            var resultType = parameters[3].ParameterType.GetElementType();
            var results = Array.CreateInstance(resultType, vertexCount);
            var instance = Expression.Parameter(typeof(object), "instance");
            var matrix = Expression.Parameter(typeof(Matrix4x4), "matrix");
            var query = Expression.Lambda<Action<object, Matrix4x4>>(Expression.Call(
                Expression.Convert(instance, type), method, Expression.Constant(points), matrix,
                Expression.Constant(Enum.ToObject(parameters[2].ParameterType, 0)),
                Expression.Constant(results, parameters[3].ParameterType)), instance, matrix).Compile();
            object tree = null;
            Action evaluate = () => query(tree, Matrix4x4.identity);
            int edit = 0;
            Action edited = () => query(tree, Matrix4x4.Translate(Vector3.forward * (++edit % 2 == 0 ? 0.02f : 0.03f)));
            yield return new Work
            {
                action = () => { tree = build(); if (tree == null) throw new InvalidOperationException("BVH build failed."); evaluate(); },
                accept = value => scenario.firstEvaluation = value, captureName = scenario.name + ".first"
            };
            // Validate known plane distances outside the timed operation.
            var valid = resultType.GetField("IsValid", BindingFlags.Instance | BindingFlags.NonPublic);
            var distance = resultType.GetField("Distance", BindingFlags.Instance | BindingFlags.NonPublic);
            Action<float> validate = expected =>
            {
                for (int i = 0; i < vertexCount; i++)
                {
                    var value = results.GetValue(i);
                    if (!(bool)valid.GetValue(value) || Math.Abs((float)distance.GetValue(value) - expected) > 1e-5f)
                        throw new InvalidOperationException("Clearance plane distance mismatch at " + i);
                }
            };
            validate(0.01f);
            for (int i = 0; i < 3; i++) yield return new Work { action = evaluate };
            for (int i = 0; i < samples; i++)
            {
                int index = i;
                yield return new Work { action = evaluate, accept = value => scenario.unchanged[index] = value,
                    captureName = i == samples - 1 ? scenario.name + ".unchanged-last" : null };
            }
            validate(0.01f);
            for (int i = 0; i < 3; i++) yield return new Work { action = edited };
            for (int i = 0; i < samples; i++)
            {
                int index = i;
                yield return new Work { action = edited, accept = value => scenario.edited[index] = value,
                    captureName = i == samples - 1 ? scenario.name + ".edited-last" : null };
            }
            validate(edit % 2 == 0 ? 0.03f : 0.04f);
            scenario.unchangedP50 = Percentile(scenario.unchanged, 0.5);
            scenario.unchangedP95 = Percentile(scenario.unchanged, 0.95);
            scenario.editedP50 = Percentile(scenario.edited, 0.5);
            scenario.editedP95 = Percentile(scenario.edited, 0.95);
        }
        finally { Object.DestroyImmediate(reference); }
        scenario.meshCountDeltaAfterDispose = Resources.FindObjectsOfTypeAll<Mesh>().Length - meshCountBefore;
        s_document.scenarios.Add(scenario);
    }

    private static IEnumerable<Work> RunProportional(int count, int samples)
    {
        var root = new GameObject("Proportional benchmark");
        int meshes = Resources.FindObjectsOfTypeAll<Mesh>().Length;
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(
            "Net._32Ba.LatticeDeformationTool.Editor.VertexSelectionHandler")).First(t => t != null);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        var radius = type.GetField("s_proportionalRadius", flags);
        var falloff = type.GetField("s_proportionalFalloff", flags);
        var selected = (HashSet<int>)type.GetField("s_selectedVertices", flags).GetValue(null);
        var previousSelection = selected.ToArray();
        object oldRadius = radius.GetValue(null), oldFalloff = falloff.GetValue(null);
        var result = new Scenario { name = "proportional-" + count, vertices = count,
            unchanged = new Measurement[samples], edited = new Measurement[samples] };
        try
        {
            selected.Clear();
            for (int i = 0; i < 256; i++) selected.Add(i * (count / 256));
            radius.SetValue(null, 0.03f);
            falloff.SetValue(null, Enum.Parse(falloff.FieldType, "Linear"));
            object handler = Activator.CreateInstance(type, true);
            var points = new Vector3[count];
            for (int i = 0; i < count; i++) points[i] = new Vector3((i % 256) * 0.002f, (i / 256) * 0.002f, 0);
            type.GetField("_deformedVertices", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(handler, points);
            var ensure = Expression.Lambda<Action>(Expression.Call(Expression.Constant(handler, type),
                type.GetMethod("EnsureProportionalInfluences", BindingFlags.Instance | BindingFlags.NonPublic),
                Expression.Constant(root.transform))).Compile();
            Action warm = () => { for (int i = 0; i < 100; i++) ensure(); };
            int edits = 0;
            Action edited = () =>
            {
                root.transform.localScale = ++edits % 2 == 0 ? Vector3.one : new Vector3(1.1f, 1f, 1f);
                ensure();
            };
            yield return new Work { action = ensure, accept = m => result.firstEvaluation = m, captureName = result.name + ".first" };
            for (int i = 0; i < 3; i++) yield return new Work { action = warm };
            for (int i = 0; i < samples; i++)
            {
                int index = i;
                yield return new Work { action = warm, accept = m => result.unchanged[index] = m,
                    captureName = i == samples - 1 ? result.name + ".unchanged-last" : null };
            }
            for (int i = 0; i < 3; i++) yield return new Work { action = edited };
            for (int i = 0; i < samples; i++)
            {
                int index = i;
                yield return new Work { action = edited, accept = m => result.edited[index] = m,
                    captureName = i == samples - 1 ? result.name + ".edited-last" : null };
            }
            object cache = type.GetField("_proportionalInfluenceCache", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(handler);
            var getter = cache.GetType().GetMethod("GetInfluence", BindingFlags.Instance | BindingFlags.NonPublic);
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                for (int i = 0; i < count; i++)
                {
                    float value = (float)getter.Invoke(cache, new object[] { i });
                    if (float.IsNaN(value) || value < 0f || value > 1f || (selected.Contains(i) && value != 1f))
                        throw new InvalidOperationException("Invalid proportional influence.");
                    writer.Write(value);
                }
                writer.Flush();
                result.proportionalInfluenceHash = BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
            }
            result.unchangedP50 = Percentile(result.unchanged, 0.5); result.unchangedP95 = Percentile(result.unchanged, 0.95);
            result.editedP50 = Percentile(result.edited, 0.5); result.editedP95 = Percentile(result.edited, 0.95);
        }
        finally
        {
            selected.Clear(); foreach (int index in previousSelection) selected.Add(index);
            radius.SetValue(null, oldRadius); falloff.SetValue(null, oldFalloff);
            Object.DestroyImmediate(root);
        }
        result.meshCountDeltaAfterDispose = Resources.FindObjectsOfTypeAll<Mesh>().Length - meshes;
        s_document.scenarios.Add(result);
    }

    private static void Tick()
    {
        try
        {
            // Let the recorder become active at a frame boundary before invoking
            // an operation. Enabling it and sampling immediately can lose the
            // entire first operation after substantial fixture setup.
            if (s_actionScheduled)
            {
                s_actionScheduled = false;
                s_frameBefore = ProfilerDriver.lastFrameIndex;
                double actionStarted = EditorApplication.timeSinceStartup;
                s_sampler.Begin();
                try { s_pending.action(); }
                finally { s_sampler.End(); }
                s_startedWaiting = EditorApplication.timeSinceStartup;
                if (s_pending.captureName != null)
                    UnityEngine.Debug.Log("Benchmark action returned: " + s_pending.captureName +
                        "; wallSeconds=" + (s_startedWaiting - actionStarted));
                EditorApplication.QueuePlayerLoopUpdate();
                return;
            }
            // Reading sample names allocates managed strings. Do not profile the
            // Profiler reader itself: that would feed its allocations into the next
            // frame being scanned and grow the recording without bound.
            ProfilerDriver.enabled = false;
            using (var process = Process.GetCurrentProcess())
                if (process.PrivateMemorySize64 > s_document.maxPrivateMemoryMb * 1024L * 1024L)
                    throw new InvalidOperationException("Benchmark exceeded its private memory budget.");
            if (s_pending != null)
            {
                if (!TryReadSample(s_pending.marker, out var measurement))
                {
                    if (EditorApplication.timeSinceStartup - s_startedWaiting > 30)
                    {
                        ProfilerDriver.SaveProfile(s_output + ".missing-frame.raw");
                        var diagnostic = new List<string>();
                        for (int frame = ProfilerDriver.firstFrameIndex; frame <= ProfilerDriver.lastFrameIndex; frame++)
                        {
                            using var view = ProfilerDriver.GetRawFrameDataView(frame, 0);
                            if (!view.valid) continue;
                            for (int sample = 0; sample < view.sampleCount; sample++)
                            {
                                string name = view.GetSampleName(sample);
                                if (name.Contains("Lattice") || name.Contains("FitCorrection"))
                                    diagnostic.Add(frame + ": " + name);
                            }
                        }
                        File.WriteAllLines(s_output + ".missing-frame-markers.txt", diagnostic);
                        throw new TimeoutException("CPU Profiler did not provide the measurement frame: " +
                            s_pending.captureName + "; before=" + s_frameBefore +
                            "; first=" + ProfilerDriver.firstFrameIndex + "; last=" + ProfilerDriver.lastFrameIndex);
                    }
                    ProfilerDriver.enabled = true;
                    EditorApplication.QueuePlayerLoopUpdate();
                    return;
                }
                s_pending.accept?.Invoke(measurement);
                if (s_pending.captureName != null)
                    ProfilerDriver.SaveProfile(s_output + "." + s_pending.captureName + ".raw");
                ProfilerDriver.ClearAllFrames();
                s_pending = null;
            }
            if (!s_work.MoveNext()) { Finish(null); return; }
            s_pending = s_work.Current;
            s_pending.marker = "Lattice.Architecture.Measure";
            s_actionScheduled = true;
            ProfilerDriver.enabled = true;
            EditorApplication.QueuePlayerLoopUpdate();
        }
        catch (Exception exception) { Finish(exception); }
    }

    private static bool TryReadSample(string marker, out Measurement measurement)
    {
        measurement = null;
        int last = ProfilerDriver.lastFrameIndex;
        for (int frame = Math.Max(ProfilerDriver.firstFrameIndex, s_frameBefore + 1); frame <= last; frame++)
        {
            using var data = ProfilerDriver.GetRawFrameDataView(frame, 0);
            if (!data.valid) continue;
            int markerId = data.GetMarkerId(marker);
            if (markerId < 0) continue;
            int allocationMarkerId = data.GetMarkerId("GC.Alloc");
            for (int sample = 0; sample < data.sampleCount; sample++)
            {
                // Compare IDs: GetSampleName allocates a string per sample and
                // can feed the profiler's own allocation stream on this frame.
                if (data.GetSampleMarkerId(sample) != markerId) continue;
                var value = new Measurement
                {
                    milliseconds = data.GetSampleTimeMs(sample), profilerFrame = frame,
                    allocationSamplesComplete = true
                };
                int end = sample + data.GetSampleChildrenCountRecursive(sample);
                for (int child = sample + 1; child <= end; child++)
                {
                    if (data.GetSampleMarkerId(child) != allocationMarkerId) continue;
                    value.allocationSamples++;
                    if (data.GetSampleMetadataCount(child) < 1) value.allocationSamplesComplete = false;
                    else value.allocatedBytes += data.GetSampleMetadataAsLong(child, 0);
                }
                measurement = value;
                return true;
            }
        }
        return false;
    }

    private static void SaveDocument() => File.WriteAllText(s_output, JsonUtility.ToJson(s_document, true) + "\n");

    private static void AbortForAssemblyReload() =>
        Finish(new InvalidOperationException("Assembly reload interrupted measurement; settle project initialization before retrying."));

    private static void Finish(Exception error)
    {
        AssemblyReloadEvents.beforeAssemblyReload -= AbortForAssemblyReload;
        EditorApplication.update -= Tick;
        s_work?.Dispose();
        s_pending = null;
        s_document.complete = error == null;
        s_document.error = error?.ToString();
        SaveDocument();
        ProfilerDriver.enabled = s_previousEnabled;
        ProfilerDriver.profileEditor = s_previousProfileEditor;
        ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, s_previousCpu);
        ProfilerDriver.SetAreaEnabled(ProfilerArea.Memory, s_previousMemory);
        if (error != null) UnityEngine.Debug.LogException(error);
        EditorApplication.Exit(error == null ? 0 : 1);
    }

    private static double Percentile(Measurement[] samples, double quantile)
    {
        var sorted = samples.Select(m => m.milliseconds).OrderBy(v => v).ToArray();
        return sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * quantile) - 1)];
    }

    private static Mesh CreateMesh(int count)
    {
        var mesh = new Mesh { name = "Benchmark source", indexFormat = IndexFormat.UInt32 };
        var vertices = new Vector3[count];
        const int width = 256;
        for (int i = 0; i < count; i++) vertices[i] = new Vector3((i % width) * 0.002f, (i / width) * 0.002f, 0f);
        var triangles = new List<int>();
        for (int i = 0; i + width + 1 < count; i++)
            if (i % width != width - 1) triangles.AddRange(new[] { i, i + 1, i + width, i + 1, i + width + 1, i + width });
        mesh.vertices = vertices;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static string Argument(string name, string fallback = null)
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, name);
        if (index >= 0 && index + 1 < args.Length) return args[index + 1];
        return fallback ?? throw new ArgumentException("Missing argument: " + name);
    }
}
