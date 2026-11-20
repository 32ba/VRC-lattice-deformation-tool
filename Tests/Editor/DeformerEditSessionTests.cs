#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class DeformerEditSessionTests
    {
        [UnityTest]
        public IEnumerator SeparateFrames_AreOneUndoAndRedo()
        {
            using var fixture = new Fixture();
            var d = fixture.Deformer;
            using var session = Begin(d);
            Write(session, d, 0, Vector3.forward);
            yield return null;
            Write(session, d, 2, Vector3.up);
            yield return null;
            session.Dispose();
            Undo.PerformUndo();
            Assert.That(d.Displacements, Is.All.EqualTo(Vector3.zero));
            Undo.PerformRedo();
            Assert.That(d.Displacements, Is.EqualTo(new[] { Vector3.forward, Vector3.zero, Vector3.up }));
            Assert.That(fixture.Mesh.vertices, Is.EqualTo(Fixture.Vertices));
        }

        [TestCase("future")]
        [TestCase("null-groups")]
        [TestCase("selection")]
        [TestCase("missing-profile")]
        [TestCase("short-brush")]
        [TestCase("nonfinite-disabled")]
        [TestCase("topology")]
        [TestCase("source-replaced")]
        public void RejectedBegin_DoesNotWriteOrRecordUndo(string fault)
        {
            using var fixture = new Fixture();
            var d = fixture.Deformer;
            switch (fault)
            {
                case "future": Set(d, "_migrationReleaseIndex", int.MaxValue); break;
                case "null-groups": Set(d, "_groups", null); break;
                case "selection": Set(d, "_activeGroupIndex", -1); break;
                case "missing-profile": Set(d, "_dataSource", DeformerDataSource.Profile); break;
                case "short-brush": d.ActiveGroup.Layers[d.ActiveLayerIndex].BrushDisplacements = new Vector3[1]; break;
                case "nonfinite-disabled": d.enabled = false; d.Displacements[0] = new Vector3(float.NaN, 0, 0); break;
                case "topology": fixture.Mesh.triangles = new[] { 0, 2, 1 }; break;
                case "source-replaced": fixture.Filter.sharedMesh = fixture.OtherMesh; break;
            }
            string before = EditorJsonUtility.ToJson(d);
            int dirty = EditorUtility.GetDirtyCount(d), undo = Undo.GetCurrentGroup();
            Assert.That(DeformerEditSession.TryBegin(d, MeshDeformerLayerType.Brush, "Rejected gesture"), Is.Null);
            Assert.That(EditorJsonUtility.ToJson(d), Is.EqualTo(before));
            Assert.That(EditorUtility.GetDirtyCount(d), Is.EqualTo(dirty));
            Assert.That(Undo.GetCurrentGroup(), Is.EqualTo(undo));
        }

        [Test]
        public void Begin_CurrentShapeWithOlderJournal_DoesNotAdvanceTheJournal()
        {
            using var fixture = new Fixture();
            Set(fixture.Deformer, "_migrationReleaseIndex", 16);
            string before = EditorJsonUtility.ToJson(fixture.Deformer);
            using var session = Begin(fixture.Deformer);
            Assert.That(EditorJsonUtility.ToJson(fixture.Deformer), Is.EqualTo(before));
        }

        [TestCase("layer")]
        [TestCase("group")]
        [TestCase("source")]
        [TestCase("topology")]
        [TestCase("settings")]
        [TestCase("future")]
        [TestCase("corrupt")]
        public void InterruptedGesture_RejectsNextWriteWithoutRepairingData(string change)
        {
            using var fixture = new Fixture();
            var d = fixture.Deformer;
            using var session = Begin(d);
            Write(session, d, 0, Vector3.forward);
            switch (change)
            {
                case "layer": d.ActiveLayerIndex = 0; break;
                case "group": d.ActiveGroupIndex = d.AddGroup("Other"); break;
                case "source": fixture.Filter.sharedMesh = fixture.OtherMesh; break;
                case "topology": fixture.Mesh.triangles = new[] { 0, 2, 1 }; break;
                case "settings": d.ActiveGroup.Layers[d.ActiveLayerIndex].Settings = new LatticeAsset(); break;
                case "future": Set(d, "_migrationReleaseIndex", int.MaxValue); break;
                case "corrupt": Set(d, "_activeGroupIndex", -17); break;
            }
            string before = EditorJsonUtility.ToJson(d);
            Assert.That(session.TryPrepareWrite(d), Is.False);
            Assert.That(session.IsActive, Is.False);
            Assert.That(EditorJsonUtility.ToJson(d), Is.EqualTo(before));
            session.Dispose();
            Assert.That(EditorJsonUtility.ToJson(d), Is.EqualTo(before));
        }

        [Test]
        public void TargetSwitch_CannotApplyTheOldGestureToTheNewComponent()
        {
            using var a = new Fixture();
            using var b = new Fixture();
            using var session = Begin(a.Deformer);
            Write(session, a.Deformer, 0, Vector3.forward);
            string before = EditorJsonUtility.ToJson(b.Deformer);
            Assert.That(session.TryPrepareWrite(b.Deformer), Is.False);
            Assert.That(EditorJsonUtility.ToJson(b.Deformer), Is.EqualTo(before));
        }

        [Test]
        public void RuntimeMeshAssignment_IsAcceptedWithoutRetargetingTheSource()
        {
            using var fixture = new Fixture();
            var d = fixture.Deformer;
            d.Deform(true);
            using var session = Begin(d);
            Write(session, d, 0, Vector3.forward);
            d.Deform(true);
            Assert.That(session.TryPrepareWrite(d), Is.True);
            Assert.That(d.SourceMesh, Is.SameAs(fixture.Mesh));
            Assert.That(fixture.Mesh.vertices, Is.EqualTo(Fixture.Vertices));
        }

        [Test]
        public void Cancel_RestoresPayloadAndVisibleRuntimeWithoutRemovingEarlierUndo()
        {
            using var fixture = new Fixture();
            Undo.RegisterCompleteObjectUndo(fixture.Root.transform, "Earlier move");
            fixture.Root.transform.localPosition = Vector3.right;
            var d = fixture.Deformer;
            using var session = Begin(d);
            Write(session, d, 0, Vector3.forward);
            d.Deform(true);
            Assert.That(session.TryCancel(), Is.True);
            Assert.That(d.Displacements, Is.All.EqualTo(Vector3.zero));
            Assert.That(d.RuntimeMesh.vertices, Is.EqualTo(Fixture.Vertices));
            Assert.That(fixture.Root.transform.localPosition, Is.EqualTo(Vector3.right));
            Undo.PerformUndo();
            Assert.That(fixture.Root.transform.localPosition, Is.EqualTo(Vector3.zero));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InterveningUndoOperation_IsNeverCollapsedOrCancelled(bool cancel)
        {
            using var fixture = new Fixture();
            var d = fixture.Deformer;
            using var session = Begin(d);
            Write(session, d, 0, Vector3.forward);
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(fixture.Root.transform, "Unrelated move");
            fixture.Root.transform.localPosition = Vector3.right;
            if (cancel) Assert.That(session.TryCancel(), Is.False); else session.Dispose();
            Assert.That(d.Displacements[0], Is.EqualTo(Vector3.forward));
            Assert.That(fixture.Root.transform.localPosition, Is.EqualTo(Vector3.right));
            Undo.PerformUndo();
            Assert.That(fixture.Root.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(d.Displacements[0], Is.EqualTo(Vector3.forward));
            Undo.PerformUndo();
            Assert.That(d.Displacements, Is.All.EqualTo(Vector3.zero));
        }

        [Test]
        public void UndoWhileOpen_AbandonsTheSessionAndPreservesRedo()
        {
            using var fixture = new Fixture();
            var d = fixture.Deformer;
            using var session = Begin(d);
            Write(session, d, 0, Vector3.forward);
            Undo.PerformUndo();
            Assert.That(session.IsActive, Is.False);
            Assert.That(session.TryPrepareWrite(d), Is.False);
            Assert.That(d.Displacements, Is.All.EqualTo(Vector3.zero));
            Undo.PerformRedo();
            Assert.That(d.Displacements[0], Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void DestroyedOwner_AndRepeatedTerminationReleaseTheSession()
        {
            using var fixture = new Fixture();
            using var session = Begin(fixture.Deformer);
            Object.DestroyImmediate(fixture.Root);
            Assert.That(session.TryPrepareWrite(fixture.Deformer), Is.False);
            Assert.DoesNotThrow(() => { session.Dispose(); session.Abandon(); session.Dispose(); });
            Assert.That(fixture.Mesh.vertices, Is.EqualTo(Fixture.Vertices));
        }

        [UnityTest]
        public IEnumerator PrefabVariant_MultiFrameEditSurvivesUndoRedoApplyAndReload()
        {
            using var fixture = new Fixture();
            string folder = "Assets/__GestureVariant_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            GameObject instance = null, loaded = null;
            try
            {
                AssetDatabase.CreateAsset(fixture.Mesh, folder + "/Source.asset");
                var prefab = PrefabUtility.SaveAsPrefabAsset(fixture.Root, folder + "/Base.prefab");
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var variant = PrefabUtility.SaveAsPrefabAsset(instance, folder + "/Variant.prefab");
                Object.DestroyImmediate(instance);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(variant);
                var d = instance.GetComponent<LatticeDeformer>();
                using (var session = Begin(d))
                {
                    Write(session, d, 0, Vector3.forward);
                    yield return null;
                    Write(session, d, 2, Vector3.up);
                }
                Assert.That(PrefabUtility.GetPropertyModifications(d).Any(p => p.propertyPath.Contains("_brushDisplacements")), Is.True);
                Undo.PerformUndo();
                Assert.That(d.Displacements, Is.All.EqualTo(Vector3.zero));
                Undo.PerformRedo();
                Assert.That(d.Displacements[2], Is.EqualTo(Vector3.up));
                PrefabUtility.ApplyPrefabInstance(instance, InteractionMode.AutomatedAction);
                Object.DestroyImmediate(instance); instance = null;
                loaded = PrefabUtility.LoadPrefabContents(folder + "/Variant.prefab");
                Assert.That(loaded.GetComponent<LatticeDeformer>().Displacements,
                    Is.EqualTo(new[] { Vector3.forward, Vector3.zero, Vector3.up }));
                Assert.That(prefab.GetComponent<LatticeDeformer>().Displacements, Is.All.EqualTo(Vector3.zero));
                Assert.That(fixture.Mesh.vertices, Is.EqualTo(Fixture.Vertices));
            }
            finally
            {
                if (loaded != null) PrefabUtility.UnloadPrefabContents(loaded);
                if (instance != null) Object.DestroyImmediate(instance);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private static DeformerEditSession Begin(LatticeDeformer d)
        {
            var session = DeformerEditSession.TryBegin(d, MeshDeformerLayerType.Brush, "Gesture");
            Assert.That(session, Is.Not.Null);
            return session;
        }

        private static void Write(DeformerEditSession session, LatticeDeformer d, int index, Vector3 delta)
        {
            Assert.That(session.TryPrepareWrite(d), Is.True);
            d.AddDisplacement(index, delta);
            session.RecordChange();
        }

        private static void Set(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private sealed class Fixture : IDisposable
        {
            internal static readonly Vector3[] Vertices = { Vector3.zero, Vector3.right, Vector3.up };
            internal readonly GameObject Root;
            internal readonly Mesh Mesh, OtherMesh;
            internal readonly MeshFilter Filter;
            internal readonly LatticeDeformer Deformer;
            private readonly int _undo;

            internal Fixture()
            {
                Undo.IncrementCurrentGroup(); _undo = Undo.GetCurrentGroup();
                Mesh = new Mesh { vertices = Vertices, triangles = new[] { 0, 1, 2 } };
                Mesh.RecalculateBounds(); Mesh.RecalculateNormals();
                OtherMesh = Object.Instantiate(Mesh);
                Root = new GameObject("Edit session fixture");
                Root.AddComponent<MeshRenderer>();
                Filter = Root.AddComponent<MeshFilter>(); Filter.sharedMesh = Mesh;
                Deformer = Root.AddComponent<LatticeDeformer>(); Deformer.Reset();
                Deformer.ActiveLayerIndex = Deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
                Deformer.EnsureDisplacementCapacity();
                Deformer.Deform(false);
            }

            public void Dispose()
            {
                Undo.RevertAllDownToGroup(_undo);
                if (Root != null) Object.DestroyImmediate(Root);
                if (Mesh != null && !AssetDatabase.Contains(Mesh)) Object.DestroyImmediate(Mesh);
                if (OtherMesh != null) Object.DestroyImmediate(OtherMesh);
            }
        }
    }
}
#endif
