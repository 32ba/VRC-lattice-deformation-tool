#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class MeshDeformerProfileCompatibilityTests
    {
        private const string k_TemporaryAssetPath = "Assets/MeshDeformerProfileCompatibilityTests.asset";

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(k_TemporaryAssetPath);
        }

        [Test]
        public void CapturedMesh_WithMatchingIdentity_IsExactMatch()
        {
            var mesh = CreateMesh("Exact");
            var profile = CreateProfile(mesh, "mesh-guid", 42);
            try
            {
                Assert.That(profile.EvaluateCompatibility(mesh, "mesh-guid", 42),
                    Is.EqualTo(ProfileCompatibilityStatus.ExactMatch));
                Assert.That(profile.Compatibility.VertexCount, Is.EqualTo(mesh.vertexCount));
                Assert.That(profile.Compatibility.IndexCount, Is.EqualTo(6));
                Assert.That(profile.Compatibility.TriangleCount, Is.EqualTo(2));
            }
            finally
            {
                Destroy(profile, mesh);
            }
        }

        [Test]
        public void ClonedMesh_AndChangedGuid_AreCompatibleSourceDiffers()
        {
            var mesh = CreateMesh("Source");
            var clone = UnityEngine.Object.Instantiate(mesh);
            var profile = CreateProfile(mesh, "source-guid", 7);
            try
            {
                Assert.That(profile.EvaluateCompatibility(clone, "clone-guid", 8),
                    Is.EqualTo(ProfileCompatibilityStatus.CompatibleSourceDiffers));
                Assert.That(profile.EvaluateCompatibility(mesh),
                    Is.EqualTo(ProfileCompatibilityStatus.CompatibleSourceDiffers));
            }
            finally
            {
                Destroy(profile, clone, mesh);
            }
        }

        [Test]
        public void SameVertexCount_WithDifferentIndexOrder_IsTopologyMismatch()
        {
            var source = CreateMesh("Source");
            var reordered = CreateMesh("Reordered", new[] { 0, 2, 1, 0, 3, 2 });
            var profile = CreateProfile(source);
            try
            {
                Assert.That(reordered.vertexCount, Is.EqualTo(source.vertexCount));
                Assert.That(profile.EvaluateCompatibility(reordered),
                    Is.EqualTo(ProfileCompatibilityStatus.TopologyMismatch));
            }
            finally
            {
                Destroy(profile, reordered, source);
            }
        }

        [Test]
        public void ChangedVertexStructure_IsTopologyMismatch()
        {
            var source = CreateMesh("Source");
            var changed = CreateMesh("Changed");
            var vertices = changed.vertices;
            vertices[1] += Vector3.forward;
            changed.vertices = vertices;
            var profile = CreateProfile(source);
            try
            {
                Assert.That(profile.EvaluateCompatibility(changed),
                    Is.EqualTo(ProfileCompatibilityStatus.TopologyMismatch));
            }
            finally
            {
                Destroy(profile, changed, source);
            }
        }

        [Test]
        public void ComponentCompatibility_ReevaluatesWhenSameMeshInstanceMutates()
        {
            var mesh = CreateMesh("MutableSource");
            var profile = CreateProfile(mesh);
            var deformer = CreateDeformer("MutableTarget", mesh);
            try
            {
                Assert.That(deformer.EvaluateProfileCompatibility(profile),
                    Is.EqualTo(ProfileCompatibilityStatus.CompatibleSourceDiffers));

                var vertices = mesh.vertices;
                vertices[1] += Vector3.forward;
                mesh.vertices = vertices;

                Assert.That(deformer.EvaluateProfileCompatibility(profile),
                    Is.EqualTo(ProfileCompatibilityStatus.TopologyMismatch));
            }
            finally
            {
                DestroyDeformer(deformer);
                Destroy(profile, mesh);
            }
        }

        [Test]
        public void BlendShapeSignature_SubMeshCount_AndBindPoseCount_AreValidated()
        {
            var source = CreateMesh("Source");
            var blendShapeChanged = UnityEngine.Object.Instantiate(source);
            var subMeshChanged = UnityEngine.Object.Instantiate(source);
            var bindPoseChanged = UnityEngine.Object.Instantiate(source);
            var profile = CreateProfile(source);
            try
            {
                blendShapeChanged.AddBlendShapeFrame(
                    "Smile", 100f, new Vector3[source.vertexCount], null, null);

                subMeshChanged.subMeshCount = 2;
                subMeshChanged.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0);
                subMeshChanged.SetIndices(new[] { 0, 2, 3 }, MeshTopology.Triangles, 1);

                bindPoseChanged.bindposes = new[] { Matrix4x4.identity };

                Assert.That(profile.Compatibility.TopologyHash,
                    Is.EqualTo(MeshCompatibilityMetadata.Capture(blendShapeChanged).TopologyHash),
                    "BlendShape data is intentionally checked by its auxiliary signature.");
                Assert.That(profile.Compatibility.TopologyHash,
                    Is.EqualTo(MeshCompatibilityMetadata.Capture(bindPoseChanged).TopologyHash),
                    "Bind poses are intentionally checked by their auxiliary count.");
                Assert.That(profile.EvaluateCompatibility(blendShapeChanged),
                    Is.EqualTo(ProfileCompatibilityStatus.TopologyMismatch));
                Assert.That(profile.EvaluateCompatibility(subMeshChanged),
                    Is.EqualTo(ProfileCompatibilityStatus.TopologyMismatch));
                Assert.That(profile.EvaluateCompatibility(bindPoseChanged),
                    Is.EqualTo(ProfileCompatibilityStatus.TopologyMismatch));
            }
            finally
            {
                Destroy(profile, bindPoseChanged, subMeshChanged, blendShapeChanged, source);
            }
        }

        [Test]
        public void LegacyProfile_IsAllowedWithInsufficientMetadataStatus()
        {
            var mesh = CreateMesh("Legacy");
            var profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            var deformer = CreateDeformer("LegacyTarget", mesh);
            try
            {
                profile.Capture(new List<DeformerGroup>(), 0);

                Assert.That(profile.EvaluateCompatibility(mesh),
                    Is.EqualTo(ProfileCompatibilityStatus.InsufficientMetadata));
                Assert.That(deformer.UseProfile(profile), Is.True);
                Assert.That(deformer.DataSource, Is.EqualTo(DeformerDataSource.Profile));
            }
            finally
            {
                DestroyDeformer(deformer);
                Destroy(profile, mesh);
            }
        }

        [Test]
        public void Mismatch_IsRejectedWithoutMutatingComponentRendererLayersOrProfiles()
        {
            var source = CreateMesh("TargetSource");
            var incompatible = CreateMesh("Incompatible", new[] { 0, 2, 1, 0, 3, 2 });
            var deformer = CreateDeformer("Target", source);
            var currentProfile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            var incompatibleProfile = CreateProfile(incompatible);
            try
            {
                deformer.AddLayer("Existing Brush", MeshDeformerLayerType.Brush);
                deformer.Profile = currentProfile;
                var groupsBefore = deformer.Groups;
                int groupCountBefore = deformer.GroupCount;
                int layerCountBefore = deformer.Layers.Count;
                var rendererMeshBefore = deformer.GetComponent<MeshFilter>().sharedMesh;

                Assert.That(deformer.UseProfile(incompatibleProfile), Is.False);

                Assert.That(deformer.DataSource, Is.EqualTo(DeformerDataSource.Embedded));
                Assert.That(deformer.Profile, Is.SameAs(currentProfile));
                Assert.That(deformer.Groups, Is.SameAs(groupsBefore));
                Assert.That(deformer.GroupCount, Is.EqualTo(groupCountBefore));
                Assert.That(deformer.Layers.Count, Is.EqualTo(layerCountBefore));
                Assert.That(deformer.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(rendererMeshBefore));
                Assert.That(incompatibleProfile.Groups.Count, Is.Zero);
            }
            finally
            {
                DestroyDeformer(deformer);
                Destroy(incompatibleProfile, currentProfile, incompatible, source);
            }
        }

        [Test]
        public void CopyProfileToEmbedded_MismatchIsRejectedWithoutMutatingEmbeddedData()
        {
            var source = CreateMesh("CopyTarget");
            var incompatible = CreateMesh("CopyIncompatible", new[] { 0, 2, 1, 0, 3, 2 });
            var profile = CreateProfile(incompatible);
            var deformer = CreateDeformer("CopyTarget", source);
            try
            {
                deformer.AddLayer("Existing Brush", MeshDeformerLayerType.Brush);
                deformer.Profile = profile;
                var groupsBefore = deformer.Groups;
                int groupCountBefore = deformer.GroupCount;
                int layerCountBefore = deformer.Layers.Count;

                Assert.That(deformer.CopyProfileToEmbedded(), Is.False);
                Assert.That(deformer.DataSource, Is.EqualTo(DeformerDataSource.Embedded));
                Assert.That(deformer.Groups, Is.SameAs(groupsBefore));
                Assert.That(deformer.GroupCount, Is.EqualTo(groupCountBefore));
                Assert.That(deformer.Layers.Count, Is.EqualTo(layerCountBefore));
            }
            finally
            {
                DestroyDeformer(deformer);
                Destroy(profile, incompatible, source);
            }
        }

        [Test]
        public void CopyProfileToEmbedded_RendererMeshSwapRejectsWithoutMutatingCachedState()
        {
            var source = CreateMesh("CachedCopySource");
            var incompatible = CreateMesh("CachedCopyIncompatible", new[] { 0, 2, 1, 0, 3, 2 });
            var profile = CreateProfile(source);
            var deformer = CreateDeformer("CachedCopyTarget", source);
            try
            {
                deformer.AddLayer("Existing Brush", MeshDeformerLayerType.Brush);
                deformer.Profile = profile;
                Mesh runtimeBefore = deformer.Deform(false);
                Assert.That(runtimeBefore, Is.Not.Null);

                var serializedSourceField = typeof(LatticeDeformer).GetField(
                    "_serializedSourceMesh", BindingFlags.Instance | BindingFlags.NonPublic);
                var initializedField = typeof(LatticeDeformer).GetField(
                    "_hasInitializedFromSource", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(serializedSourceField, Is.Not.Null);
                Assert.That(initializedField, Is.Not.Null);

                Mesh serializedSourceBefore = (Mesh)serializedSourceField.GetValue(deformer);
                bool initializedBefore = (bool)initializedField.GetValue(deformer);
                Mesh sourceBefore = deformer.SourceMesh;
                var groupsBefore = deformer.Groups;
                int groupCountBefore = deformer.GroupCount;
                int layerCountBefore = deformer.Layers.Count;

                deformer.GetComponent<MeshFilter>().sharedMesh = incompatible;

                Assert.That(deformer.CopyProfileToEmbedded(), Is.False);
                Assert.That(deformer.SourceMesh, Is.SameAs(sourceBefore));
                Assert.That(deformer.RuntimeMesh, Is.SameAs(runtimeBefore));
                Assert.That(serializedSourceField.GetValue(deformer), Is.SameAs(serializedSourceBefore));
                Assert.That(initializedField.GetValue(deformer), Is.EqualTo(initializedBefore));
                Assert.That(deformer.Profile, Is.SameAs(profile));
                Assert.That(deformer.DataSource, Is.EqualTo(DeformerDataSource.Embedded));
                Assert.That(deformer.Groups, Is.SameAs(groupsBefore));
                Assert.That(deformer.GroupCount, Is.EqualTo(groupCountBefore));
                Assert.That(deformer.Layers.Count, Is.EqualTo(layerCountBefore));
                Assert.That(deformer.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(incompatible));
            }
            finally
            {
                DestroyDeformer(deformer);
                Destroy(profile, incompatible, source);
            }
        }

        [Test]
        public void SerializedMismatch_DeformDoesNotApplyProfileDisplacements()
        {
            var source = CreateMesh("TargetSource");
            var incompatible = CreateMesh("Incompatible", new[] { 0, 2, 1, 0, 3, 2 });
            var profileSource = CreateDeformer("ProfileSource", incompatible);
            var target = CreateDeformer("Target", source);
            var profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            try
            {
                int brushIndex = profileSource.AddLayer("Brush", MeshDeformerLayerType.Brush);
                profileSource.ActiveLayerIndex = brushIndex;
                profileSource.EnsureDisplacementCapacity();
                profileSource.SetDisplacement(0, Vector3.up * 10f);
                Assert.That(profileSource.SaveToProfile(profile), Is.True);

                var profileField = typeof(LatticeDeformer).GetField(
                    "_profile", BindingFlags.Instance | BindingFlags.NonPublic);
                var dataSourceField = typeof(LatticeDeformer).GetField(
                    "_dataSource", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(profileField, Is.Not.Null);
                Assert.That(dataSourceField, Is.Not.Null);
                profileField.SetValue(target, profile);
                dataSourceField.SetValue(target, DeformerDataSource.Profile);

                var result = target.Deform(false);

                Assert.That(result.vertices, Is.EqualTo(source.vertices));
                Assert.That(profile.Groups[0].Layers[1].GetBrushDisplacement(0),
                    Is.EqualTo(Vector3.up * 10f));
            }
            finally
            {
                DestroyDeformer(target);
                DestroyDeformer(profileSource);
                Destroy(profile, incompatible, source);
            }
        }

        [Test]
        public void DuplicateJsonAndAssetSerialization_PreserveCompatibilityAndOptionalMetadata()
        {
            var mesh = CreateMesh("SerializedSource");
            var profile = CreateProfile(mesh, "asset-guid", 99);
            var jsonCopy = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            MeshDeformerProfile duplicate = null;
            try
            {
                profile.DisplayName = "Outfit Fix";
                profile.Author = "Author";
                profile.Description = "Description";
                profile.TargetAsset = "Product 1.2";
                profile.Notes = "Terms and source URL";

                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(profile), jsonCopy);
                duplicate = UnityEngine.Object.Instantiate(profile);
                AssertSerializedMetadata(jsonCopy, mesh);
                AssertSerializedMetadata(duplicate, mesh);

                AssetDatabase.CreateAsset(profile, k_TemporaryAssetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(k_TemporaryAssetPath, ImportAssetOptions.ForceUpdate);
                AssertSerializedMetadata(
                    AssetDatabase.LoadAssetAtPath<MeshDeformerProfile>(k_TemporaryAssetPath), mesh);

                foreach (var field in typeof(MeshDeformerProfile).GetFields(
                             BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    Assert.That(typeof(Mesh).IsAssignableFrom(field.FieldType), Is.False,
                        $"Profile must not embed a source Mesh field: {field.Name}");
                }
            }
            finally
            {
                if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate);
                UnityEngine.Object.DestroyImmediate(jsonCopy);
                if (!AssetDatabase.Contains(profile)) UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static void AssertSerializedMetadata(MeshDeformerProfile profile, Mesh mesh)
        {
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.EvaluateCompatibility(mesh, "asset-guid", 99),
                Is.EqualTo(ProfileCompatibilityStatus.ExactMatch));
            Assert.That(profile.DisplayName, Is.EqualTo("Outfit Fix"));
            Assert.That(profile.Author, Is.EqualTo("Author"));
            Assert.That(profile.Description, Is.EqualTo("Description"));
            Assert.That(profile.TargetAsset, Is.EqualTo("Product 1.2"));
            Assert.That(profile.Notes, Is.EqualTo("Terms and source URL"));
        }

        private static MeshDeformerProfile CreateProfile(Mesh mesh, string guid = "", long localId = 0)
        {
            var profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            profile.Capture(new List<DeformerGroup>(), 0, mesh);
            profile.SetSourceAssetIdentity(guid, localId);
            return profile;
        }

        private static Mesh CreateMesh(string name, int[] indices = null)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = new[]
            {
                Vector3.zero,
                Vector3.right,
                Vector3.one,
                Vector3.up
            };
            mesh.triangles = indices ?? new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
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

        private static void DestroyDeformer(LatticeDeformer deformer)
        {
            if (deformer == null) return;
            var runtimeMesh = deformer.RuntimeMesh;
            if (runtimeMesh != null) UnityEngine.Object.DestroyImmediate(runtimeMesh);
            UnityEngine.Object.DestroyImmediate(deformer.gameObject);
        }

        private static void Destroy(params UnityEngine.Object[] objects)
        {
            foreach (var obj in objects)
            {
                if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            }
        }
    }
}
#endif
