#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class BrushInfluenceContractTests
    {
        private const string BaselinePath = "Packages/net.32ba.lattice-deformation-tool/Tests/Editor/Fixtures/ArchitectureBaseline/brush-influence.json";
        private static readonly string[] Scenarios = { "plain", "pose", "connected", "surface", "backface", "gaussian" };
        private static readonly string[] Modes = { "Normal", "Move", "Smooth", "Mask" };

        [Serializable] public sealed class Sample
        {
            public string key;
            public bool modified;
            public Vector3[] displacements;
            public float[] mask;
        }
        [Serializable] public sealed class Document
        {
            public string sourceCommit;
            public string unity;
            public Sample[] samples;
        }

        public static IEnumerable Cases()
        {
            foreach (string mode in Modes)
                foreach (string scenario in Scenarios)
                    foreach (bool mirror in new[] { false, true })
                        yield return new TestCaseData(mode, scenario, mirror);
        }

        [TestCaseSource(nameof(Cases))]
        public void InfluenceAndModeOutput_MatchesPreExtraction(string mode, string scenario, bool mirror)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(BaselinePath);
            Assert.That(asset, Is.Not.Null);
            var baseline = JsonUtility.FromJson<Document>(asset.text);
            Assert.That(baseline.sourceCommit, Is.EqualTo("e193acf52293e9e986036d9257bdc93df75f143c"));
            Assert.That(baseline.samples.Length, Is.EqualTo(48));
            Sample actual = Capture(mode, scenario, mirror);
            Sample expected = Array.Find(baseline.samples, s => s.key == actual.key);
            Assert.That(expected, Is.Not.Null);
            Assert.That(actual.modified, Is.EqualTo(expected.modified));
            Assert.That(actual.displacements.Length, Is.EqualTo(expected.displacements.Length));
            Assert.That(actual.mask.Length, Is.EqualTo(expected.mask.Length));
            for (int i = 0; i < actual.displacements.Length; i++)
            {
                Assert.That(Vector3.Distance(actual.displacements[i], expected.displacements[i]), Is.LessThan(2e-6f), "delta " + i);
                Assert.That(actual.mask[i], Is.EqualTo(expected.mask[i]).Within(2e-6f), "mask " + i);
            }
        }


        [Test]
        public void Query_UsesWorldRadiusUnderNonuniformScale()
        {
            var query = new BrushInfluenceQuery(null, Matrix4x4.Scale(new Vector3(2f, 3f, 4f)),
                Vector3.zero, 2f, BrushFalloffType.Linear, null, null, Vector3.forward, null);
            Assert.That(query.TryGetFalloff(0, Vector3.right * .5f, out float half), Is.True);
            Assert.That(half, Is.EqualTo(.5f));
            Assert.That(query.TryGetFalloff(0, Vector3.right, out float edge), Is.True);
            Assert.That(edge, Is.Zero);
            Assert.That(query.TryGetFalloff(0, Vector3.right * 1.01f, out _), Is.False);
        }

        [Test]
        public void Query_PosedWorldCoordinatesTakePrecedenceWithoutMutation()
        {
            var pose = new[] { Vector3.right * .5f };
            var query = new BrushInfluenceQuery(pose, Matrix4x4.Scale(Vector3.one * 100f),
                Vector3.zero, 1f, BrushFalloffType.Linear, null, null, Vector3.forward, null);
            Assert.That(query.TryGetFalloff(0, Vector3.one * 100f, out float half), Is.True);
            Assert.That(half, Is.EqualTo(.5f));
            Assert.That(pose[0], Is.EqualTo(Vector3.right * .5f));
        }

        [Test]
        public void Query_SurfaceUsesSparseVisitedOrderAndRejectsUnreachableVertices()
        {
            var surface = new GeodesicDistanceCalculator.Workspace();
            surface.Begin(4);
            surface.SetDistance(3, .5f);
            surface.SetDistance(1, 1.5f);
            var query = new BrushInfluenceQuery(null, Matrix4x4.identity, Vector3.zero, 2f,
                BrushFalloffType.Linear, null, null, Vector3.forward, surface);
            Assert.That(query.CandidateCount(4), Is.EqualTo(2));
            Assert.That(query.CandidateAt(0), Is.EqualTo(3));
            Assert.That(query.CandidateAt(1), Is.EqualTo(1));
            Assert.That(query.TryGetFalloff(3, Vector3.one * 100f, out float near), Is.True);
            Assert.That(near, Is.EqualTo(.75f));
            Assert.That(query.TryGetFalloff(1, Vector3.one * 100f, out float far), Is.True);
            Assert.That(far, Is.EqualTo(.25f));
            Assert.That(query.TryGetFalloff(0, Vector3.zero, out _), Is.False);
        }

        [Test]
        public void Query_ConnectedAndBackfaceFiltersCombineWithoutChangingTheirInputs()
        {
            var connected = new HashSet<int> { 0, 1, 2 };
            var normals = new[] { Vector3.forward, Vector3.back, Vector3.zero, Vector3.back };
            var query = new BrushInfluenceQuery(null, Matrix4x4.identity, Vector3.zero, 1f,
                BrushFalloffType.Constant, connected, normals, Vector3.forward, null);
            Assert.That(query.TryGetFalloff(0, Vector3.zero, out _), Is.False);
            Assert.That(query.TryGetFalloff(1, Vector3.zero, out float back), Is.True);
            Assert.That(back, Is.EqualTo(1f));
            Assert.That(query.TryGetFalloff(2, Vector3.zero, out _), Is.True);
            Assert.That(query.TryGetFalloff(3, Vector3.zero, out _), Is.False);
            Assert.That(connected, Is.EquivalentTo(new[] { 0, 1, 2 }));
            Assert.That(normals[0], Is.EqualTo(Vector3.forward));
        }

        private static Sample Capture(string mode, string scenario, bool mirror)
        {
            var root = new GameObject("__BrushInfluenceContract");
            var vertices = new[] { Vector3.left, Vector3.zero, Vector3.right, Vector3.up, Vector3.up * 4f };
            var mesh = new Mesh { vertices = vertices, triangles = new[] { 0, 1, 3, 1, 2, 3, 3, 2, 4 } };
            var handler = new BrushToolHandler();
            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            var settings = new Dictionary<FieldInfo, object>();
            string[] names = { "s_brushMode", "s_brushStrength", "s_brushFalloff", "s_invertBrush", "s_mirrorAxis",
                "s_connectedOnly", "s_backfaceCulling", "s_useSurfaceDistance" };
            foreach (string name in names)
            {
                var field = typeof(BrushToolHandler).GetField(name, flags);
                settings.Add(field, field.GetValue(null));
            }
            bool oldRest = SkinnedVertexHelper.StoreMovesInRestSpace;
            var camera = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
            Quaternion cameraRotation = camera != null ? camera.transform.rotation : Quaternion.identity;
            try
            {
                if (camera != null) camera.transform.rotation = Quaternion.identity;
                root.transform.localScale = new Vector3(2f, 1f, 1f);
                mesh.RecalculateNormals();
                root.AddComponent<MeshRenderer>();
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                var owner = root.AddComponent<LatticeDeformer>();
                owner.Reset();
                owner.ActiveLayerIndex = owner.AddLayer("Brush", MeshDeformerLayerType.Brush);
                owner.EnsureDisplacementCapacity();
                var layer = owner.Layers[owner.ActiveLayerIndex];
                layer.EnsureVertexMaskCapacity(vertices.Length);
                float[] masks = { .5f, .25f, .8f, 0f, 1f };
                for (int i = 0; i < vertices.Length; i++)
                {
                    owner.Displacements[i] = Vector3.forward * (.2f + .1f * i);
                    layer.SetVertexMask(i, masks[i]);
                }
                handler.Activate(owner);
                handler.RebuildCacheIfNeeded(mesh, owner);
                var world = new Vector3[vertices.Length];
                for (int i = 0; i < vertices.Length; i++) world[i] = root.transform.TransformPoint(vertices[i]);
                if (scenario == "pose") world[2] += Vector3.forward * 5f;
                Set(handler, "_worldPositions", world);
                Set(handler, "_meshNormals", new[] { Vector3.forward, Vector3.back, Vector3.forward, Vector3.back, Vector3.forward });
                Set(handler, "_connectedVerticesCache", new HashSet<int> { 0, 1, 3 });
                Set(handler, "_hasLastMoveBrushLocalDelta", true);
                Set(handler, "_lastMoveBrushLocalDelta", new Vector3(.1f, .2f, .3f));
                Set(handler, "_currentHitTriangleIndex", -1);
                Set(handler, "_hasGeodesicDistanceCache", scenario == "surface");
                var workspace = (GeodesicDistanceCalculator.Workspace)typeof(BrushToolHandler)
                    .GetField("_geodesicWorkspace", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(handler);
                workspace.Begin(vertices.Length);
                workspace.SetDistance(0, .4f);
                workspace.SetDistance(2, .8f);
                Set(null, "s_brushMode", Enum.Parse(typeof(BrushToolHandler.BrushMode), mode));
                Set(null, "s_brushStrength", .4f);
                Set(null, "s_brushFalloff", scenario == "gaussian" ? BrushFalloffType.Gaussian : BrushFalloffType.Linear);
                Set(null, "s_invertBrush", false);
                Set(null, "s_mirrorAxis", BrushToolHandler.MirrorAxis.X);
                Set(null, "s_connectedOnly", scenario == "connected");
                Set(null, "s_backfaceCulling", scenario == "backface");
                Set(null, "s_useSurfaceDistance", scenario == "surface");
                SkinnedVertexHelper.StoreMovesInRestSpace = false;
                Vector3 localHit = Vector3.right * .5f;
                Vector3 worldHit = root.transform.TransformPoint(localHit);
                object result;
                if (mirror) result = Invoke(handler, "ApplyMirror", owner, localHit, worldHit, 2f, .1f, 1f);
                else if (mode == "Normal") result = Invoke(handler, "ApplyNormalBrush", owner, worldHit, 2f, .1f, 1f);
                else if (mode == "Move") result = Invoke(handler, "ApplyMoveBrushLocalDelta", owner, worldHit, 2f, .1f, new Vector3(.1f, .2f, .3f), Vector3.forward);
                else if (mode == "Smooth") result = Invoke(handler, "ApplySmoothBrush", owner, worldHit, 2f, .1f);
                else result = Invoke(handler, "ApplyMaskBrush", owner, worldHit, 2f);
                var mask = new float[vertices.Length];
                for (int i = 0; i < mask.Length; i++) mask[i] = layer.GetVertexMask(i);
                Assert.That(mesh.vertices, Is.EqualTo(vertices), "Source asset must remain unchanged.");
                return new Sample { key = mode + "/" + scenario + "/" + mirror, modified = result is bool changed && changed,
                    displacements = (Vector3[])owner.Displacements.Clone(), mask = mask };
            }
            finally
            {
                handler.Deactivate();
                foreach (var setting in settings) setting.Key.SetValue(null, setting.Value);
                SkinnedVertexHelper.StoreMovesInRestSpace = oldRest;
                if (camera != null) camera.transform.rotation = cameraRotation;
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
            }
        }

        private static void Set(BrushToolHandler handler, string name, object value) =>
            typeof(BrushToolHandler).GetField(name, BindingFlags.NonPublic | (handler == null ? BindingFlags.Static : BindingFlags.Instance))
                .SetValue(handler, value);

        private static object Invoke(BrushToolHandler handler, string name, params object[] arguments) =>
            typeof(BrushToolHandler).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(handler, arguments);
    }
}
#endif
