#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class BlendShapeTestSessionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void InspectorExit_RestoresSerializedWeightsAndExistingPrefabOverrides(bool existingOverrides)
        {
            using var fixture = new Fixture("Prefab test session");
            string folder = "Assets/__BlendShapeSession_" + Guid.NewGuid().ToString("N");
            GameObject instance = null;
            UnityEditor.Editor editor = null;
            try
            {
                AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
                AssetDatabase.CreateAsset(fixture.Source, folder + "/source.asset");
                var prefab = PrefabUtility.SaveAsPrefabAsset(fixture.Root, folder + "/base.prefab");
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var renderer = instance.GetComponent<SkinnedMeshRenderer>();
                var deformer = instance.GetComponent<LatticeDeformer>();
                if (existingOverrides)
                {
                    renderer.SetBlendShapeWeight(1, 31f);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
                editor = UnityEditor.Editor.CreateEditor(deformer, typeof(LatticeDeformerEditor));
                var weights = SerializedWeights(renderer);
                var modifications = RelevantModifications(renderer);
                Invoke(editor, "EnterBlendShapeTestMode", deformer, renderer);
                int generated = renderer.sharedMesh.GetBlendShapeIndex("Test");
                Assert.That(generated, Is.GreaterThanOrEqualTo(0));
                renderer.SetBlendShapeWeight(generated, 58f);
                renderer.receiveShadows = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                Invoke(editor, "ExitBlendShapeTestMode");

                Assert.That(renderer.sharedMesh, Is.SameAs(fixture.Source));
                Assert.That(SerializedWeights(renderer), Is.EqualTo(weights),
                    "Test mode must also restore the serialized weight array length.");
                Assert.That(RelevantModifications(renderer), Is.EqualTo(modifications),
                    "Existing mesh/weight overrides must survive without test-only array entries.");
                Assert.That(renderer.receiveShadows, Is.False, "A separate renderer edit must remain.");

                var variant = PrefabUtility.SaveAsPrefabAsset(instance, folder + "/variant.prefab");
                Object.DestroyImmediate(editor); editor = null;
                Object.DestroyImmediate(instance);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(variant);
                renderer = instance.GetComponent<SkinnedMeshRenderer>();
                Assert.That(renderer.sharedMesh, Is.SameAs(fixture.Source));
                Assert.That(SerializedWeights(renderer), Is.EqualTo(weights));
                Assert.That(renderer.receiveShadows, Is.False);
                Assert.That(SerializedWeights(prefab.GetComponent<SkinnedMeshRenderer>()),
                    Is.EqualTo(new[] {24f, 68f}), "Saving the variant changed the base prefab.");
            }
            finally
            {
                if (editor != null) Object.DestroyImmediate(editor);
                if (instance != null) Object.DestroyImmediate(instance);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void WeightAndRefresh_UseCurrentGeneratedShapeAndRestoreByOriginalNames()
        {
            using var fixture = new Fixture("Weight refresh");
            var sourceVertices = fixture.Source.vertices;
            using var session = BlendShapeTestSession.TryBegin(fixture.Deformer, fixture.Renderer);
            Assert.That(session, Is.Not.Null);
            session.SetWeight(63f);
            Assert.That(fixture.Renderer.GetBlendShapeWeight(fixture.Renderer.sharedMesh.GetBlendShapeIndex("Test")), Is.EqualTo(63f));
            fixture.Deformer.BlendShapeName = "Renamed Test";
            fixture.Deformer.InvalidateCache();
            fixture.Deformer.Deform(false);
            session.Refresh();
            Assert.That(fixture.Renderer.GetBlendShapeWeight(fixture.Renderer.sharedMesh.GetBlendShapeIndex("Renamed Test")), Is.EqualTo(63f));
            session.Dispose();
            session.Dispose();
            Assert.That(fixture.Renderer.sharedMesh, Is.SameAs(fixture.Source));
            Assert.That(SerializedWeights(fixture.Renderer), Is.EqualTo(new[] {24f, 68f}));
            Assert.That(fixture.Source.vertices, Is.EqualTo(sourceVertices));
            Assert.That(fixture.Deformer.RuntimeMesh != null, Is.True, "The component owns its runtime mesh.");
        }

        [TestCase("dispose")]
        [TestCase("weight")]
        [TestCase("refresh")]
        public void ExternalMeshAssignment_IsPreserved(string action)
        {
            using var fixture = new Fixture("External assignment");
            Mesh external = Object.Instantiate(fixture.Source);
            try
            {
                using var session = BlendShapeTestSession.TryBegin(fixture.Deformer, fixture.Renderer);
                Assert.That(session, Is.Not.Null);
                string raw = EditorJsonUtility.ToJson(fixture.Deformer);
                fixture.Renderer.sharedMesh = external;
                fixture.Renderer.SetBlendShapeWeight(0, 19f);
                if (action == "weight") session.SetWeight(80f);
                else if (action == "refresh") session.Refresh();
                else session.Dispose();
                Assert.That(session.IsActive, Is.False);
                Assert.That(fixture.Renderer.sharedMesh, Is.SameAs(external));
                Assert.That(fixture.Renderer.GetBlendShapeWeight(0), Is.EqualTo(19f));
                Assert.That(fixture.Deformer.SourceMesh, Is.SameAs(fixture.Source));
                Assert.That(EditorJsonUtility.ToJson(fixture.Deformer), Is.EqualTo(raw));
            }
            finally { Object.DestroyImmediate(external); }
        }

        [Test]
        public void NewSessionOnSameRenderer_EndsPriorOwnerBeforeCapturingRestoration()
        {
            using var fixture = new Fixture("Session replacement");
            using var first = BlendShapeTestSession.TryBegin(fixture.Deformer, fixture.Renderer);
            Assert.That(first, Is.Not.Null);
            first.SetWeight(39f);
            using var second = BlendShapeTestSession.TryBegin(fixture.Deformer, fixture.Renderer);
            Assert.That(second, Is.Not.Null);
            second.SetWeight(71f);
            Assert.That(first.IsActive, Is.False);
            first.Dispose();
            first.SetWeight(5f);
            Assert.That(fixture.Renderer.GetBlendShapeWeight(fixture.Renderer.sharedMesh.GetBlendShapeIndex("Test")), Is.EqualTo(71f));
            second.Dispose();
            Assert.That(fixture.Renderer.sharedMesh, Is.SameAs(fixture.Source));
            Assert.That(SerializedWeights(fixture.Renderer), Is.EqualTo(new[] {24f, 68f}));
        }

        [Test]
        public void SeparateRenderers_EndIndependently()
        {
            using var a = new Fixture("Session A");
            using var b = new Fixture("Session B");
            using var first = BlendShapeTestSession.TryBegin(a.Deformer, a.Renderer);
            using var second = BlendShapeTestSession.TryBegin(b.Deformer, b.Renderer);
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            first.SetWeight(14f); second.SetWeight(83f);
            first.Dispose();
            Assert.That(a.Renderer.sharedMesh, Is.SameAs(a.Source));
            Assert.That(second.IsActive, Is.True);
            Assert.That(b.Renderer.GetBlendShapeWeight(b.Renderer.sharedMesh.GetBlendShapeIndex("Test")), Is.EqualTo(83f));
        }

        [TestCase("renderer")]
        [TestCase("component")]
        [TestCase("object")]
        public void DestroyedTarget_DisposesWithoutAffectingAnotherSession(string destroy)
        {
            using var a = new Fixture("Destroyed session");
            using var b = new Fixture("Retained session");
            using var first = BlendShapeTestSession.TryBegin(a.Deformer, a.Renderer);
            using var second = BlendShapeTestSession.TryBegin(b.Deformer, b.Renderer);
            Assert.That(first, Is.Not.Null);
            if (destroy == "object") Object.DestroyImmediate(a.Root);
            else if (destroy == "component") Object.DestroyImmediate(a.Deformer);
            else Object.DestroyImmediate(a.Renderer);
            Assert.DoesNotThrow(first.Dispose);
            Assert.That(first.IsActive, Is.False);
            Assert.That(second.IsActive, Is.True);
            Assert.That(b.Renderer.sharedMesh, Is.SameAs(b.Deformer.RuntimeMesh));
            if (destroy == "component")
            {
                Assert.That(a.Renderer.sharedMesh, Is.SameAs(a.Source));
                Assert.That(SerializedWeights(a.Renderer), Is.EqualTo(new[] {24f, 68f}));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DisabledOwner_RestoresWeightsAfterComponentReleasedItsMesh(bool disableObject)
        {
            using var fixture = new Fixture("Disabled session");
            fixture.Root.SetActive(true);
            using var session = BlendShapeTestSession.TryBegin(fixture.Deformer, fixture.Renderer);
            Assert.That(session, Is.Not.Null);
            session.SetWeight(54f);
            var generated = fixture.Renderer.sharedMesh;
            if (disableObject) fixture.Root.SetActive(false);
            else fixture.Deformer.enabled = false;
            Assert.That(generated == null, Is.True, "Component lifecycle releases its generated mesh.");
            session.Dispose();
            Assert.That(fixture.Renderer.sharedMesh, Is.SameAs(fixture.Source));
            Assert.That(SerializedWeights(fixture.Renderer), Is.EqualTo(new[] {24f, 68f}));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExternalOriginalMeshAssignment_PreservesSubsequentWeightEdit(bool refresh)
        {
            using var fixture = new Fixture("External original assignment");
            using var session = BlendShapeTestSession.TryBegin(fixture.Deformer, fixture.Renderer);
            session.SetWeight(54f);
            fixture.Renderer.sharedMesh = fixture.Source;
            fixture.Renderer.SetBlendShapeWeight(0, 19f);
            var weights = SerializedWeights(fixture.Renderer);
            if (refresh) session.Refresh();
            else session.Dispose();
            Assert.That(session.IsActive, Is.False);
            Assert.That(fixture.Renderer.sharedMesh, Is.SameAs(fixture.Source));
            Assert.That(SerializedWeights(fixture.Renderer), Is.EqualTo(weights));
        }

        [Test]
        public void OriginallyEmptyWeightStorage_IsRestoredAsEmpty()
        {
            using var fixture = new Fixture("Empty weights");
            using (var serialized = new SerializedObject(fixture.Renderer))
            {
                serialized.FindProperty("m_BlendShapeWeights").arraySize = 0;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            using var session = BlendShapeTestSession.TryBegin(fixture.Deformer, fixture.Renderer);
            Assert.That(session, Is.Not.Null);
            session.SetWeight(43f);
            session.Dispose();
            Assert.That(SerializedWeights(fixture.Renderer), Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReorderedOriginalShapes_RestoreWeightsByNameWithoutGeneratedEntries(bool usePrefab)
        {
            using var fixture = new Fixture("Reordered original shapes");
            string folder = "Assets/__BlendShapeSession_" + Guid.NewGuid().ToString("N");
            GameObject instance = null;
            try
            {
                var deformer = fixture.Deformer;
                var renderer = fixture.Renderer;
                if (usePrefab)
                {
                    AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
                    AssetDatabase.CreateAsset(fixture.Source, folder + "/source.asset");
                    var prefab = PrefabUtility.SaveAsPrefabAsset(fixture.Root, folder + "/base.prefab");
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    deformer = instance.GetComponent<LatticeDeformer>();
                    renderer = instance.GetComponent<SkinnedMeshRenderer>();
                    renderer.SetBlendShapeWeight(1, 31f);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
                using var session = BlendShapeTestSession.TryBegin(deformer, renderer);
                Assert.That(session, Is.Not.Null);
                session.SetWeight(58f);
                if (usePrefab) PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                fixture.Source.ClearBlendShapes();
                fixture.Source.AddBlendShapeFrame("Blink", 100f, new Vector3[3], null, null);
                fixture.Source.AddBlendShapeFrame("Smile", 100f, new Vector3[3], null, null);
                if (usePrefab)
                {
                    EditorUtility.SetDirty(fixture.Source);
                    AssetDatabase.SaveAssetIfDirty(fixture.Source);
                }
                session.Dispose();
                var expected = new[] {usePrefab ? 31f : 68f, 24f};
                Assert.That(renderer.sharedMesh, Is.SameAs(fixture.Source));
                Assert.That(SerializedWeights(renderer), Is.EqualTo(expected));
                if (usePrefab)
                {
                    Assert.That(RelevantModifications(renderer).Any(s => s.Contains("Array.data[2]")), Is.False);
                    var variant = PrefabUtility.SaveAsPrefabAsset(instance, folder + "/variant.prefab");
                    Object.DestroyImmediate(instance);
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(variant);
                    Assert.That(SerializedWeights(instance.GetComponent<SkinnedMeshRenderer>()), Is.EqualTo(expected));
                }
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void MissingSource_DoesNotLeaveAnActiveSessionOrAssignment()
        {
            var root = new GameObject("Missing test source");
            root.SetActive(false);
            try
            {
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                var deformer = root.AddComponent<LatticeDeformer>();
                Assert.That(BlendShapeTestSession.TryBegin(deformer, renderer), Is.Null);
                Assert.That(renderer.sharedMesh, Is.Null);
                Assert.That(BlendShapeTestSession.TryBegin(deformer, renderer), Is.Null);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void InvalidTargetAndFutureData_AreRejectedWithoutChangingRendererOrPayload()
        {
            using var fixture = new Fixture("Rejected session");
            using var other = new Fixture("Wrong renderer");
            typeof(LatticeDeformer).GetField("_deformationDataVersion", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(fixture.Deformer, (DeformationDataVersion)999);
            string raw = EditorJsonUtility.ToJson(fixture.Deformer);
            var weights = SerializedWeights(fixture.Renderer);
            Assert.That(BlendShapeTestSession.TryBegin(null, fixture.Renderer), Is.Null);
            Assert.That(BlendShapeTestSession.TryBegin(fixture.Deformer, null), Is.Null);
            Assert.That(BlendShapeTestSession.TryBegin(fixture.Deformer, other.Renderer), Is.Null);
            Assert.That(BlendShapeTestSession.TryBegin(fixture.Deformer, fixture.Renderer), Is.Null);
            Assert.That(fixture.Renderer.sharedMesh, Is.SameAs(fixture.Source));
            Assert.That(SerializedWeights(fixture.Renderer), Is.EqualTo(weights));
            Assert.That(EditorJsonUtility.ToJson(fixture.Deformer), Is.EqualTo(raw));
        }

        private static void Invoke(UnityEditor.Editor editor, string name, params object[] args) =>
            typeof(LatticeDeformerEditor).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(editor, args);

        private static float[] SerializedWeights(SkinnedMeshRenderer renderer)
        {
            using var serialized = new SerializedObject(renderer);
            var weights = serialized.FindProperty("m_BlendShapeWeights");
            return Enumerable.Range(0, weights.arraySize).Select(i => weights.GetArrayElementAtIndex(i).floatValue).ToArray();
        }

        private static string[] RelevantModifications(SkinnedMeshRenderer renderer) =>
            (PrefabUtility.GetPropertyModifications(renderer) ?? Array.Empty<PropertyModification>())
            .Where(m => m.propertyPath == "m_Mesh" || m.propertyPath.StartsWith("m_BlendShapeWeights"))
            .Select(m => m.propertyPath + "=" + m.value + ":" + (m.objectReference != null ? m.objectReference.GetInstanceID() : 0))
            .OrderBy(s => s, StringComparer.Ordinal).ToArray();

        private sealed class Fixture : IDisposable
        {
            internal readonly GameObject Root;
            internal readonly Mesh Source;
            internal readonly SkinnedMeshRenderer Renderer;
            internal readonly LatticeDeformer Deformer;

            internal Fixture(string name)
            {
                Root = new GameObject(name);
                Root.SetActive(false);
                Source = new Mesh
                {
                    name = name + " source",
                    vertices = new[] {Vector3.zero, Vector3.right, Vector3.up},
                    triangles = new[] {0, 1, 2},
                    bindposes = new[] {Matrix4x4.identity},
                    boneWeights = Enumerable.Range(0,3).Select(i => new BoneWeight {boneIndex0=0,weight0=1}).ToArray()
                };
                Source.RecalculateNormals();
                Source.AddBlendShapeFrame("Smile", 100f, new[] {Vector3.forward * 0.1f, Vector3.zero, Vector3.zero}, null, null);
                Source.AddBlendShapeFrame("Blink", 100f, new[] {Vector3.zero, Vector3.forward * 0.2f, Vector3.zero}, null, null);
                Renderer = Root.AddComponent<SkinnedMeshRenderer>();
                Renderer.sharedMesh = Source;
                Renderer.bones = new[] {Root.transform};
                Renderer.rootBone = Root.transform;
                Renderer.SetBlendShapeWeight(0, 24f);
                Renderer.SetBlendShapeWeight(1, 68f);
                Deformer = Root.AddComponent<LatticeDeformer>();
                Deformer.Reset();
                Deformer.ActiveLayerIndex = Deformer.AddLayer("Test layer", MeshDeformerLayerType.Brush);
                Deformer.EnsureDisplacementCapacity();
                Deformer.Displacements[0] = Vector3.up * 0.15f;
                Deformer.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                Deformer.BlendShapeName = "Test";
                Deformer.Deform(false);
            }

            public void Dispose()
            {
                if (Root != null) Object.DestroyImmediate(Root);
                if (Source != null && !AssetDatabase.Contains(Source)) Object.DestroyImmediate(Source);
            }
        }
    }
}
#endif
