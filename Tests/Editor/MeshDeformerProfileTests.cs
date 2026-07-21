#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class MeshDeformerProfileTests
    {
        private const string k_TemporaryAssetPath = "Assets/MeshDeformerProfileTests.asset";

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(k_TemporaryAssetPath);
        }

        [Test]
        public void PublicCollectionViews_DoNotExposeSerializedBackingLists()
        {
            var mesh = CreateMesh();
            var deformer = CreateDeformer("ReadOnlyViews", mesh);
            var profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            try
            {
                var firstGroupsView = deformer.Groups;
                Assert.That(firstGroupsView, Is.Not.InstanceOf<List<DeformerGroup>>());
                Assert.That(deformer.Groups, Is.SameAs(firstGroupsView));
                Assert.That(firstGroupsView[0].Layers, Is.Not.InstanceOf<List<LatticeLayer>>());
                Assert.That(firstGroupsView[0].Layers, Is.SameAs(firstGroupsView[0].Layers));

                var emptyProfileView = profile.Groups;
                profile.Capture(firstGroupsView, deformer.ActiveGroupIndex, mesh);
                Assert.That(profile.Groups, Is.Not.InstanceOf<List<DeformerGroup>>());
                Assert.That(profile.Groups, Is.Not.SameAs(emptyProfileView),
                    "Replacing serialized profile data must replace its read-only wrapper as well.");
                Assert.That(profile.Groups, Is.SameAs(profile.Groups));
            }
            finally
            {
                DestroyDeformer(deformer);
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void ProfileReference_ProducesSameDeformationAsEmbeddedData()
        {
            var mesh = CreateMesh();
            var embedded = CreateDeformer("Embedded", mesh);
            var referenced = CreateDeformer("Referenced", mesh);
            var profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            try
            {
                ConfigureBrushLayer(embedded, new[] { Vector3.up, Vector3.right, Vector3.forward });
                profile.Capture(embedded.Groups, embedded.ActiveGroupIndex);

                referenced.Profile = profile;
                referenced.DataSource = DeformerDataSource.Profile;

                var embeddedGroups = (IList)typeof(LatticeDeformer)
                    .GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(referenced);

                var embeddedResult = embedded.Deform(false);
                var profileResult = referenced.Deform(false);

                Assert.That(profileResult.vertices, Is.EqualTo(embeddedResult.vertices));
                Assert.That(referenced.GroupCount, Is.EqualTo(embedded.GroupCount));
                Assert.That(embeddedGroups.Count, Is.Zero, "Profile mode must not duplicate payload into the component.");
            }
            finally
            {
                DestroyDeformer(embedded);
                DestroyDeformer(referenced);
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void ProfileReference_InstanceEditsDoNotMutateProfile()
        {
            var mesh = CreateMesh();
            var source = CreateDeformer("Source", mesh);
            var referenced = CreateDeformer("Referenced", mesh);
            var profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            try
            {
                ConfigureBrushLayer(source, new[] { Vector3.one, Vector3.up, Vector3.right });
                profile.Capture(source.Groups, source.ActiveGroupIndex);
                string originalName = profile.Groups[0].Name;
                Vector3 originalDisplacement = profile.Groups[0].Layers[1].GetBrushDisplacement(0);

                referenced.Profile = profile;
                referenced.DataSource = DeformerDataSource.Profile;
                referenced.Groups[0].Name = "Instance Only";
                referenced.Groups[0].Layers[1].SetBrushDisplacement(0, Vector3.zero);

                Assert.That(profile.Groups[0].Name, Is.EqualTo(originalName));
                Assert.That(profile.Groups[0].Layers[1].GetBrushDisplacement(0), Is.EqualTo(originalDisplacement));
            }
            finally
            {
                DestroyDeformer(source);
                DestroyDeformer(referenced);
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void DuplicateProfile_CanBeEditedWithoutChangingOriginal()
        {
            var mesh = CreateMesh();
            var deformer = CreateDeformer("Source", mesh);
            var profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            MeshDeformerProfile duplicate = null;
            try
            {
                ConfigureBrushLayer(deformer, new[] { Vector3.one, Vector3.up, Vector3.right });
                profile.Capture(deformer.Groups, deformer.ActiveGroupIndex);
                duplicate = UnityEngine.Object.Instantiate(profile);

                duplicate.Groups[0].Name = "Duplicate";
                duplicate.Groups[0].Layers[1].SetBrushDisplacement(1, Vector3.zero);

                Assert.That(profile.Groups[0].Name, Is.Not.EqualTo("Duplicate"));
                Assert.That(profile.Groups[0].Layers[1].GetBrushDisplacement(1), Is.EqualTo(Vector3.up));
            }
            finally
            {
                DestroyDeformer(deformer);
                if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate);
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void CopyProfileToEmbedded_CreatesEditableIndependentData()
        {
            var mesh = CreateMesh();
            var source = CreateDeformer("Source", mesh);
            var target = CreateDeformer("Target", mesh);
            var profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            try
            {
                ConfigureBrushLayer(source, new[] { Vector3.one, Vector3.up, Vector3.right });
                profile.Capture(source.Groups, source.ActiveGroupIndex);
                target.Profile = profile;
                target.DataSource = DeformerDataSource.Profile;

                Assert.That(target.CopyProfileToEmbedded(), Is.True);
                target.Groups[0].Layers[1].SetBrushDisplacement(0, Vector3.zero);

                Assert.That(target.DataSource, Is.EqualTo(DeformerDataSource.Embedded));
                Assert.That(profile.Groups[0].Layers[1].GetBrushDisplacement(0), Is.EqualTo(Vector3.one));
            }
            finally
            {
                DestroyDeformer(source);
                DestroyDeformer(target);
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void SaveToProfile_RefreshesReferencedPreviewWhenProfileChanges()
        {
            var mesh = CreateMesh();
            var source = CreateDeformer("Source", mesh);
            var referenced = CreateDeformer("Referenced", mesh);
            var profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            try
            {
                ConfigureBrushLayer(source, new[] { Vector3.up, Vector3.zero, Vector3.zero });
                Assert.That(source.SaveToProfile(null), Is.False);
                Assert.That(source.SaveToProfile(profile), Is.True);
                referenced.Profile = profile;
                referenced.DataSource = DeformerDataSource.Profile;
                var before = referenced.Deform(false).vertices[0];

                source.Layers[1].SetBrushDisplacement(0, Vector3.up * 2f);
                source.SaveToProfile(profile);
                var after = referenced.Deform(false).vertices[0];

                Assert.That(after.y - before.y, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(referenced.CopyProfileToEmbedded(), Is.True);
                Assert.That(referenced.CopyProfileToEmbedded(), Is.True);
            }
            finally
            {
                DestroyDeformer(source);
                DestroyDeformer(referenced);
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void JsonAndUnityAssetSerialization_PreserveGroupsLayersMasksAndOutputSettings()
        {
            var mesh = CreateMesh();
            var deformer = CreateDeformer("Serialization", mesh);
            var profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            var jsonCopy = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            try
            {
                ConfigureBrushLayer(deformer, new[] { Vector3.one, Vector3.up, Vector3.right });
                var group = deformer.ActiveGroup;
                group.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                group.BlendShapeName = "ProfileShape";
                group.BlendShapeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
                var brush = group.Layers[1];
                brush.EnsureVertexMaskCapacity(mesh.vertexCount);
                brush.SetVertexMask(1, 0.25f);
                profile.Capture(deformer.Groups, deformer.ActiveGroupIndex);

                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(profile), jsonCopy);
                AssertProfilePayload(jsonCopy);

                AssetDatabase.CreateAsset(profile, k_TemporaryAssetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(k_TemporaryAssetPath, ImportAssetOptions.ForceUpdate);
                var reloaded = AssetDatabase.LoadAssetAtPath<MeshDeformerProfile>(k_TemporaryAssetPath);
                AssertProfilePayload(reloaded);
            }
            finally
            {
                DestroyDeformer(deformer);
                UnityEngine.Object.DestroyImmediate(jsonCopy);
                if (!AssetDatabase.Contains(profile)) UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void EmptyProfileAndMissingProfile_HaveSafeFallbacks()
        {
            var mesh = CreateMesh();
            var deformer = CreateDeformer("Fallback", mesh);
            var emptyProfile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            try
            {
                Assert.That(deformer.DataSource, Is.EqualTo(DeformerDataSource.Embedded));
                Assert.That(deformer.CopyProfileToEmbedded(), Is.False);
                Assert.That(deformer.UseProfile(null), Is.False);

                deformer.DataSource = DeformerDataSource.Profile;
                Assert.That(deformer.GroupCount, Is.GreaterThan(0));

                deformer.Profile = emptyProfile;
                Assert.That(deformer.GroupCount, Is.EqualTo(1));
                Assert.That(deformer.ActiveGroup, Is.Not.Null);
            }
            finally
            {
                DestroyDeformer(deformer);
                UnityEngine.Object.DestroyImmediate(emptyProfile);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static void AssertProfilePayload(MeshDeformerProfile profile)
        {
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.Groups.Count, Is.EqualTo(1));
            Assert.That(profile.Groups[0].Layers.Count, Is.EqualTo(2));
            Assert.That(profile.Groups[0].BlendShapeOutput, Is.EqualTo(BlendShapeOutputMode.OutputAsBlendShape));
            Assert.That(profile.Groups[0].BlendShapeName, Is.EqualTo("ProfileShape"));
            Assert.That(profile.Groups[0].BlendShapeCurve.length, Is.GreaterThan(1));
            Assert.That(profile.Groups[0].Layers[1].GetBrushDisplacement(2), Is.EqualTo(Vector3.right));
            Assert.That(profile.Groups[0].Layers[1].GetVertexMask(1), Is.EqualTo(0.25f));
        }

        private static void ConfigureBrushLayer(LatticeDeformer deformer, Vector3[] displacements)
        {
            int layerIndex = deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
            deformer.ActiveLayerIndex = layerIndex;
            deformer.EnsureDisplacementCapacity();
            for (int i = 0; i < displacements.Length; i++)
            {
                deformer.SetDisplacement(i, displacements[i]);
            }
        }

        private static LatticeDeformer CreateDeformer(string name, Mesh mesh)
        {
            var gameObject = new GameObject(name);
            gameObject.AddComponent<MeshRenderer>();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var deformer = gameObject.AddComponent<LatticeDeformer>();
            deformer.Reset();
            return deformer;
        }

        private static Mesh CreateMesh()
        {
            var mesh = new Mesh { name = "MeshDeformerProfileTestMesh" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void DestroyDeformer(LatticeDeformer deformer)
        {
            if (deformer == null) return;
            var runtimeMesh = deformer.RuntimeMesh;
            if (runtimeMesh != null) UnityEngine.Object.DestroyImmediate(runtimeMesh);
            UnityEngine.Object.DestroyImmediate(deformer.gameObject);
        }
    }
}
#endif
