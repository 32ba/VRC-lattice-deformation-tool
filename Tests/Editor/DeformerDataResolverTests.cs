#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class DeformerDataResolverTests
    {
        [Test]
        public void EmbeddedResolution_BorrowsRawDataWithoutNormalizingNullOrSelection()
        {
            var resolver = new DeformerDataResolver();
            var groups = new List<DeformerGroup> { new DeformerGroup(), null };
            var result = resolver.Resolve(DeformerDataSource.Embedded, groups, 7, null, null);
            Assert.That(result.Groups, Is.SameAs(groups));
            Assert.That(result.ActiveGroupIndex, Is.EqualTo(7));
            Assert.That(result.Groups[1], Is.Null);
            Assert.That(resolver.Resolve(DeformerDataSource.Embedded, null, -2, null, null).Groups, Is.Null);
            Assert.That(resolver.ProfileGroups, Is.Null);
        }

        [Test]
        public void ProfileQuery_DoesNotClearEmbeddedDataOrChangeSelectionAndDirtyState()
        {
            using var fixture = new Fixture();
            var target = fixture.Target;
            SetField(target, "_profile", fixture.Profile);
            SetField(target, "_dataSource", DeformerDataSource.Profile);
            var embedded = SerializedDeformerReader.Read(target).EmbeddedGroups;
            string before = EditorJsonUtility.ToJson(target);
            string profileBefore = EditorJsonUtility.ToJson(fixture.Profile);
            int dirty = EditorUtility.GetDirtyCount(target);
            int profileDirty = EditorUtility.GetDirtyCount(fixture.Profile);

            var first = target.ReadResolvedData();
            var second = target.ReadResolvedData();

            Assert.That(first.Status, Is.EqualTo(DeformerDataResolutionStatus.Profile));
            Assert.That(first.Groups.Count, Is.EqualTo(2));
            Assert.That(first.ActiveGroupIndex, Is.EqualTo(1));
            Assert.That(second.Groups, Is.SameAs(first.Groups));
            Assert.That(second.ProfileRevision, Is.EqualTo(first.ProfileRevision));
            Assert.That(SerializedDeformerReader.Read(target).EmbeddedGroups, Is.SameAs(embedded));
            Assert.That(embedded.Count, Is.EqualTo(1));
            Assert.That(EditorJsonUtility.ToJson(target), Is.EqualTo(before));
            Assert.That(EditorJsonUtility.ToJson(fixture.Profile), Is.EqualTo(profileBefore));
            Assert.That(EditorUtility.GetDirtyCount(target), Is.EqualTo(dirty));
            Assert.That(EditorUtility.GetDirtyCount(fixture.Profile), Is.EqualTo(profileDirty));
        }

        [Test]
        public void IndependentOwnersAndIdenticalProfileReplacement_DoNotShareInstanceEdits()
        {
            using var fixture = new Fixture();
            var firstOwner = new DeformerDataResolver();
            var secondOwner = new DeformerDataResolver();
            var first = firstOwner.Resolve(DeformerDataSource.Profile, null, 0, fixture.Profile, fixture.Mesh);
            var second = secondOwner.Resolve(DeformerDataSource.Profile, null, 0, fixture.Profile, fixture.Mesh);
            string profileBefore = EditorJsonUtility.ToJson(fixture.Profile);
            first.Groups[0].Name = "Instance edit";
            first.Groups[0].Layers[0].SetBrushDisplacement(0, Vector3.one * 9f);
            Assert.That(second.Groups[0].Name, Is.Not.EqualTo("Instance edit"));
            Assert.That(EditorJsonUtility.ToJson(fixture.Profile), Is.EqualTo(profileBefore));

            var replacement = Object.Instantiate(fixture.Profile);
            try
            {
                var next = firstOwner.Resolve(DeformerDataSource.Profile, null, 0, replacement, fixture.Mesh);
                Assert.That(next.Groups, Is.Not.SameAs(first.Groups));
                Assert.That(next.Groups[0].Name, Is.EqualTo(fixture.Profile.Groups[0].Name));
                Assert.That(next.ProfileRevision, Is.GreaterThan(first.ProfileRevision));
                Assert.That(first.Groups[0].Name, Is.EqualTo("Instance edit"));
            }
            finally { Object.DestroyImmediate(replacement); }
        }

        [Test]
        public void SourceMutation_InvalidatesProfileCopyAndRecoveryBuildsANewCopy()
        {
            using var fixture = new Fixture();
            var resolver = new DeformerDataResolver();
            var first = resolver.Resolve(DeformerDataSource.Profile, null, 0, fixture.Profile, fixture.Mesh);
            var vertices = fixture.Mesh.vertices;
            var changed = (Vector3[])vertices.Clone();
            changed[0] += Vector3.up;
            fixture.Mesh.vertices = changed;
            var incompatible = resolver.Resolve(DeformerDataSource.Profile, null, 0, fixture.Profile, fixture.Mesh);
            Assert.That(incompatible.Status, Is.EqualTo(DeformerDataResolutionStatus.IncompatibleProfile));
            Assert.That(incompatible.Groups[0].Enabled, Is.False);
            Assert.That(resolver.ProfileGroups, Is.Null);
            fixture.Mesh.vertices = vertices;
            var recovered = resolver.Resolve(DeformerDataSource.Profile, null, 0, fixture.Profile, fixture.Mesh);
            Assert.That(recovered.Status, Is.EqualTo(DeformerDataResolutionStatus.Profile));
            Assert.That(recovered.Groups, Is.Not.SameAs(first.Groups));
        }

        [TestCase("null-groups")]
        [TestCase("null-group")]
        [TestCase("null-layers")]
        [TestCase("null-layer")]
        [TestCase("selection")]
        [TestCase("future-lattice")]
        [TestCase("nonfinite")]
        [TestCase("count")]
        public void InvalidProfile_IsRejectedBeforeChangingAuthoredData(string corruption)
        {
            using var fixture = new Fixture();
            var profile = fixture.Profile;
            var groups = (List<DeformerGroup>)profile.SerializedGroups;
            var layer = groups[0].SerializedLayers[0];
            switch (corruption)
            {
                case "null-groups": SetField(profile, "_groups", null); break;
                case "null-group": groups[0] = null; break;
                case "null-layers": SetField(groups[0], "_layers", null); break;
                case "null-layer": groups[0].SerializedLayers[0] = null; break;
                case "selection": SetField(profile, "_activeGroupIndex", 10); break;
                case "future-lattice": SetField(layer.SerializedSettings, "_serializationVersion", 999); break;
                case "nonfinite": layer.BrushDisplacements[0] = new Vector3(float.NaN, 0f, 0f); break;
                case "count": layer.BrushDisplacements = new Vector3[1]; break;
            }
            string beforeApply = EditorJsonUtility.ToJson(fixture.Target);
            Assert.That(fixture.Target.UseProfile(profile), Is.False);
            Assert.That(EditorJsonUtility.ToJson(fixture.Target), Is.EqualTo(beforeApply));
            SetField(fixture.Target, "_profile", profile);
            SetField(fixture.Target, "_dataSource", DeformerDataSource.Profile);
            string before = EditorJsonUtility.ToJson(fixture.Target);
            // Unity's serializer materializes null inline class entries itself.
            // Observe those raw slots directly, without serializing the Profile.
            bool nullEntry = corruption == "null-group" || corruption == "null-layer";
            string profileBefore = nullEntry ? null : EditorJsonUtility.ToJson(profile);
            int profileDirty = EditorUtility.GetDirtyCount(profile);
            if (corruption == "null-group") Assert.That(profile.SerializedGroups[0], Is.Null);
            if (corruption == "null-layer") Assert.That(profile.SerializedGroups[0].SerializedLayers[0], Is.Null);
            var resolved = fixture.Target.ReadResolvedData();
            Assert.That(resolved.Status, Is.EqualTo(DeformerDataResolutionStatus.InvalidProfile));
            Assert.That(resolved.Groups, Is.Null);
            Assert.That(fixture.Target.Deform(false), Is.Null);
            Assert.That(fixture.Target.CopyProfileToEmbedded(), Is.False);
            Assert.That(EditorJsonUtility.ToJson(fixture.Target), Is.EqualTo(before));
            if (!nullEntry) Assert.That(EditorJsonUtility.ToJson(profile), Is.EqualTo(profileBefore));
            if (corruption == "null-group") Assert.That(profile.SerializedGroups[0], Is.Null);
            if (corruption == "null-layer") Assert.That(profile.SerializedGroups[0].SerializedLayers[0], Is.Null);
            Assert.That(EditorUtility.GetDirtyCount(profile), Is.EqualTo(profileDirty));
            Assert.That(fixture.Target.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(fixture.Mesh));
        }

        [TestCase(0)]
        [TestCase(1)]
        public void ProfileSelection_PersistsThroughInactivePrefabSaveAndReload(int selection)
        {
            string folder = "Assets/__ProfileResolver_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            var mesh = DeformationOutputBaselineFixture.CreateMesh(2);
            var profile = CreateProfile(mesh);
            var root = new GameObject("Profile selection");
            GameObject loaded = null;
            try
            {
                AssetDatabase.CreateAsset(mesh, folder + "/source.asset");
                AssetDatabase.CreateAsset(profile, folder + "/profile.asset");
                root.SetActive(false);
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                root.AddComponent<MeshRenderer>();
                var target = root.AddComponent<LatticeDeformer>();
                target.Reset();
                Assert.That(target.UseProfile(profile), Is.True);
                Assert.That(target.ActiveGroupIndex, Is.EqualTo(1));
                Assert.That(target.GroupCount, Is.EqualTo(2));
                target.ActiveGroupIndex = selection;
                var expected = DeformationOutputBaselineFixture.CaptureMesh(target.Deform(false));
                string before = EditorJsonUtility.ToJson(profile);
                string prefabPath = folder + "/target.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Object.DestroyImmediate(root);
                root = null;
                loaded = PrefabUtility.LoadPrefabContents(prefabPath);
                var restored = loaded.GetComponent<LatticeDeformer>();
                Assert.That(restored.ActiveGroupIndex, Is.EqualTo(selection));
                Assert.That(restored.GroupCount, Is.EqualTo(2));
                Assert.That(SerializedDeformerReader.Read(restored).EmbeddedGroups.Count, Is.Zero);
                DeformationOutputCompatibilityTests.CompareMesh(expected,
                    DeformationOutputBaselineFixture.CaptureMesh(restored.Deform(false)), "Profile save/reload");
                Assert.That(EditorJsonUtility.ToJson(profile), Is.EqualTo(before));
                Assert.That(loaded.activeSelf, Is.False);
            }
            finally
            {
                if (loaded != null) PrefabUtility.UnloadPrefabContents(loaded);
                if (root != null) Object.DestroyImmediate(root);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void ProfileUndoRedo_RebuildsOwnedEvaluationCopy()
        {
            using var fixture = new Fixture();
            Assert.That(fixture.Target.UseProfile(fixture.Profile), Is.True);
            var before = fixture.Target.Deform(false).vertices;
            Undo.RegisterCompleteObjectUndo(fixture.Profile, "Edit Profile displacement");
            fixture.Profile.Groups[0].Layers[0].SetBrushDisplacement(0, Vector3.one * 2f);
            EditorUtility.SetDirty(fixture.Profile);
            Undo.FlushUndoRecordObjects();
            var edited = fixture.Target.Deform(false).vertices;
            Assert.That(edited, Is.Not.EqualTo(before));
            Undo.PerformUndo();
            Assert.That(fixture.Target.Deform(false).vertices, Is.EqualTo(before));
            Undo.PerformRedo();
            Assert.That(fixture.Target.Deform(false).vertices, Is.EqualTo(edited));
        }

        [TestCase("deform")]
        [TestCase("renderer")]
        [TestCase("preview")]
        public void ConsecutiveEvaluations_RecheckProfileAndInPlaceSourceChanges(string mode)
        {
            using var fixture = new Fixture();
            Assert.That(fixture.Target.UseProfile(fixture.Profile), Is.True);
            Vector3[] Evaluate()
            {
                var output = mode == "preview" ? fixture.Target.CreatePreviewMeshFromInput(fixture.Mesh)
                    : fixture.Target.Deform(mode == "renderer");
                if (output == null) return null;
                try { return output.vertices; }
                finally { if (mode == "preview") Object.DestroyImmediate(output); }
            }

            var original = Evaluate();
            Assert.That(original, Is.Not.Null);
            var layer = fixture.Profile.Groups[0].Layers[0];
            var originalDelta = layer.BrushDisplacements[0];
            layer.SetBrushDisplacement(0, originalDelta + Vector3.up);
            var edited = Evaluate();
            Assert.That(Vector3.Distance(edited[0], original[0] + Vector3.up), Is.LessThan(1e-5f));

            // An early return must close the same scope as a successful evaluation.
            layer.BrushDisplacements[0] = new Vector3(float.NaN, 0f, 0f);
            Assert.That(Evaluate(), Is.Null);
            layer.SetBrushDisplacement(0, originalDelta);
            Assert.That(Evaluate(), Is.EqualTo(original));

            var source = fixture.Mesh.vertices;
            var moved = (Vector3[])source.Clone();
            moved[0] += Vector3.right;
            fixture.Mesh.vertices = moved;
            var incompatible = Evaluate();
            // Existing compatibility policy may reject evaluation or return the
            // source through a disabled group; neither may reuse Profile deltas.
            if (incompatible != null) Assert.That(incompatible, Is.EqualTo(moved));
            fixture.Mesh.vertices = source;
            Assert.That(Evaluate(), Is.EqualTo(original));
        }

        [Test]
        public void EvaluationScope_ExceptionReleasesNestedReadResults()
        {
            using var fixture = new Fixture();
            var resolver = new DeformerDataResolver();
            ResolvedDeformerData first = default;
            Assert.Throws<InvalidOperationException>(() =>
            {
                using var outer = resolver.BeginEvaluation();
                using var inner = resolver.BeginEvaluation();
                first = resolver.Resolve(DeformerDataSource.Profile, null, 0, fixture.Profile, fixture.Mesh);
                throw new InvalidOperationException("Evaluation aborted");
            });
            fixture.Profile.Groups[0].Layers[0].SetBrushDisplacement(0, Vector3.one * 4f);
            using var nextScope = resolver.BeginEvaluation();
            var next = resolver.Resolve(DeformerDataSource.Profile, null, 0, fixture.Profile, fixture.Mesh);
            Assert.That(next.ProfileRevision, Is.GreaterThan(first.ProfileRevision));
            Assert.That(next.Groups, Is.Not.SameAs(first.Groups));
            Assert.That(next.Groups[0].Layers[0].BrushDisplacements[0], Is.EqualTo(Vector3.one * 4f));
        }

        private static MeshDeformerProfile CreateProfile(Mesh mesh)
        {
            var profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            var groups = new List<DeformerGroup>();
            for (int g = 0; g < 2; g++)
            {
                var group = new DeformerGroup { Name = "Group " + g };
                var layer = new LatticeLayer();
                layer.SetType(MeshDeformerLayerType.Brush);
                layer.EnsureBrushDisplacementCapacity(mesh.vertexCount);
                for (int i = 0; i < mesh.vertexCount; i++) layer.SetBrushDisplacement(i, Vector3.up * (g + 1) * 0.1f);
                group.LayersList.Add(layer);
                groups.Add(group);
            }
            profile.Capture(groups, 1, mesh);
            return profile;
        }

        private static void SetField(object value, string field, object replacement)
        {
            var info = value.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(info, Is.Not.Null, field);
            info.SetValue(value, replacement);
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly Mesh Mesh = DeformationOutputBaselineFixture.CreateMesh(2);
            internal readonly MeshDeformerProfile Profile;
            internal readonly LatticeDeformer Target;
            internal Fixture()
            {
                var root = new GameObject("Data resolver");
                root.AddComponent<MeshFilter>().sharedMesh = Mesh;
                root.AddComponent<MeshRenderer>();
                Target = root.AddComponent<LatticeDeformer>();
                Target.Reset();
                Profile = CreateProfile(Mesh);
            }
            public void Dispose()
            {
                Undo.ClearAll();
                Object.DestroyImmediate(Target.gameObject);
                Object.DestroyImmediate(Profile);
                Object.DestroyImmediate(Mesh);
            }
        }
    }
}
#endif
