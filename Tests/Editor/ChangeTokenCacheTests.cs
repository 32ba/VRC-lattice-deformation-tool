#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    /// <summary>
    /// Cross-frame caches keyed by ObjectChangeToken: reuse while unchanged, and
    /// recompute after Mesh API writes, SerializedObject edits, SetDirty, Undo/Redo,
    /// SaveToProfile, renderer mesh swaps and asset reimports.
    /// </summary>
    public sealed class ChangeTokenCacheTests
    {
        [TearDown]
        public void TearDown()
        {
            DeformerAuthoringSource.InvalidateRowInputMemo();
            Undo.ClearAll();
        }

        [Test]
        public void EditorAdapter_SuppliesTokens()
        {
            Assert.That(DeformerPlatformServices.DirtyCountReader, Is.Not.Null);
            Assert.That(DeformerChangeTokens.IsAvailable, Is.True);
            Assert.That(DeformerChangeTokens.Capture(null).IsValid, Is.False);
            Assert.That(default(ObjectChangeToken).Equals(default(ObjectChangeToken)), Is.False, "Invalid tokens never match.");
        }

        [Test]
        public void MeshToken_ChangesOnInteriorVertexWriteTrianglesAndUndo()
        {
            var mesh = CreateQuad();
            try
            {
                var token = DeformerChangeTokens.Capture(mesh);
                Assert.That(token.IsValid, Is.True);
                Assert.That(DeformerChangeTokens.Capture(mesh).Equals(token), Is.True, "Reading must not change the token.");
                _ = mesh.vertices; _ = mesh.triangles;
                Assert.That(DeformerChangeTokens.Capture(mesh).Equals(token), Is.True);

                // (1,0,0) -> (1,0,1) keeps the bounds: only the dirty count can reveal it.
                var vertices = mesh.vertices;
                vertices[1] += Vector3.forward;
                mesh.vertices = vertices;
                var moved = DeformerChangeTokens.Capture(mesh);
                Assert.That(moved.Equals(token), Is.False, "A Mesh API vertex write must change the token.");

                mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                var reordered = DeformerChangeTokens.Capture(mesh);
                Assert.That(reordered.Equals(moved), Is.False, "An index write must change the token.");

                int generation = DeformerChangeTokens.Generation;
                PerformUnrelatedUndoRedo();
                Assert.That(DeformerChangeTokens.Generation, Is.Not.EqualTo(generation));
                Assert.That(DeformerChangeTokens.Capture(mesh).Equals(reordered), Is.False);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void ReadableSourceCache_ReusesCopyUntilTokenChangesAndNeverDestroysABorrowedCopy()
        {
            var source = DeformationOutputBaselineFixture.CreateMesh(3);
            var cache = new ReadableSourceCache();
            try
            {
                var before = DeformationOutputBaselineFixture.CaptureMesh(source);
                source.UploadMeshData(true);
                int created = ReadableSourceCache.CopiesCreated;
                var lease = cache.Acquire(source);
                Mesh copy = lease.Mesh;
                Assert.That(lease.OwnsMesh, Is.True);
                Assert.That(copy, Is.Not.SameAs(source));
                Assert.That(copy.isReadable, Is.True);
                DeformationOutputCompatibilityTests.CompareMesh(before,
                    DeformationOutputBaselineFixture.CaptureMesh(copy), "cached readable copy");
                lease.Dispose();
                Assert.That(copy != null, Is.True, "Disposing a cached lease only ends its borrow.");

                lease = cache.Acquire(source);
                Assert.That(lease.Mesh, Is.SameAs(copy), "An unchanged source reuses its copy.");
                Assert.That(ReadableSourceCache.CopiesCreated, Is.EqualTo(created + 1));

                // A change while borrowed hands out an uncached copy and keeps the borrowed one.
                EditorUtility.SetDirty(source);
                var replacement = cache.Acquire(source);
                Mesh uncached = replacement.Mesh;
                Assert.That(uncached, Is.Not.SameAs(copy));
                Assert.That(cache.CachedCopy, Is.SameAs(copy));
                replacement.Dispose();
                Assert.That(uncached == null, Is.True, "An uncached copy belongs to its lease.");

                cache.Release();
                Assert.That(copy != null, Is.True, "Release is deferred while a lease borrows the copy.");
                lease.Dispose();
                Assert.That(copy == null, Is.True);
                Assert.That(cache.CachedCopy, Is.Null);

                using var next = cache.Acquire(source);
                Assert.That(next.Mesh, Is.Not.Null);
                Assert.That(ReadableSourceCache.CopiesCreated, Is.EqualTo(created + 2));
                Assert.That(source != null && !source.isReadable, Is.True, "The source is never modified.");
            }
            finally
            {
                cache.Release();
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ReadableSourceCache_ReleaseAllDestroysCopiesBeforeDomainReload()
        {
            var source = DeformationOutputBaselineFixture.CreateMesh(2);
            var cache = new ReadableSourceCache();
            try
            {
                source.UploadMeshData(true);
                Mesh copy;
                using (var lease = cache.Acquire(source)) copy = lease.Mesh;
                Assert.That(copy != null, Is.True);
                ReadableSourceCache.ReleaseAll();
                Assert.That(copy == null, Is.True);
                Assert.That(cache.CachedCopy, Is.Null);
                using var next = cache.Acquire(source);
                Assert.That(next.Mesh != null && next.Mesh.isReadable, Is.True);
            }
            finally
            {
                cache.Release();
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ReadableSourceCache_WithoutTokens_DoesNotCache()
        {
            var source = DeformationOutputBaselineFixture.CreateMesh(2);
            var cache = new ReadableSourceCache();
            var reader = DeformerPlatformServices.DirtyCountReader;
            try
            {
                source.UploadMeshData(true);
                DeformerPlatformServices.DirtyCountReader = null;
                var first = cache.Acquire(source);
                var second = cache.Acquire(source);
                Mesh firstMesh = first.Mesh, secondMesh = second.Mesh;
                Assert.That(firstMesh, Is.Not.SameAs(secondMesh));
                Assert.That(cache.CachedCopy, Is.Null);
                first.Dispose(); second.Dispose();
                Assert.That(firstMesh == null && secondMesh == null, Is.True);
            }
            finally
            {
                DeformerPlatformServices.DirtyCountReader = reader;
                cache.Release();
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void UnreadableSource_DeformReusesCopyAndRefreshesOnChangeUndoAndDisable()
        {
            using var f = new DeformerFixture(active: true);
            var expected = f.Owner.Deform(false).vertices;
            f.Source.UploadMeshData(true);
            int created = ReadableSourceCache.CopiesCreated;

            Assert.That(f.Owner.Deform(false).vertices, Is.EqualTo(expected));
            Mesh first = f.Owner.CachedReadableSourceCopyForTests;
            Assert.That(first, Is.Not.Null);
            Assert.That(f.Owner.Deform(false).vertices, Is.EqualTo(expected));
            Assert.That(f.Owner.CachedReadableSourceCopyForTests, Is.SameAs(first));
            Assert.That(ReadableSourceCache.CopiesCreated, Is.EqualTo(created + 1));
            Assert.That(f.Owner.SourceMesh, Is.SameAs(f.Source), "The copy is only borrowed during evaluation.");

            EditorUtility.SetDirty(f.Source);
            Assert.That(f.Owner.Deform(false).vertices, Is.EqualTo(expected));
            Mesh second = f.Owner.CachedReadableSourceCopyForTests;
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(first == null, Is.True, "A replaced copy is destroyed.");

            PerformUnrelatedUndoRedo();
            Assert.That(f.Owner.Deform(false).vertices, Is.EqualTo(expected));
            Assert.That(f.Owner.CachedReadableSourceCopyForTests, Is.Not.SameAs(second));
            Assert.That(ReadableSourceCache.CopiesCreated, Is.EqualTo(created + 3));

            Mesh third = f.Owner.CachedReadableSourceCopyForTests;
            f.Owner.InvalidateCache();
            Assert.That(third == null, Is.True, "InvalidateCache drops the copy.");
            Assert.That(f.Owner.Deform(false).vertices, Is.EqualTo(expected));
            Mesh fourth = f.Owner.CachedReadableSourceCopyForTests;
            f.Owner.enabled = false;
            Assert.That(fourth == null, Is.True, "OnDisable drops the copy.");
            Assert.That(f.Owner.Deform(false).vertices, Is.EqualTo(expected));
            Assert.That(f.Owner.CachedReadableSourceCopyForTests, Is.Null,
                "An inactive owner releases its copy with its native scratch.");
            Assert.That(f.Source != null && !f.Source.isReadable, Is.True);
        }

        [Test]
        public void UnreadablePreviewInput_ReusesCopyUntilInputChangesOrIsReplaced()
        {
            using var f = new DeformerFixture(active: true);
            var expected = f.Owner.Deform(false).vertices;
            var other = Object.Instantiate(f.Source);
            f.Source.UploadMeshData(true);
            other.UploadMeshData(true);
            var outputs = new List<Mesh>();
            try
            {
                Vector3[] Preview(Mesh input)
                {
                    var output = f.Owner.CreatePreviewMeshFromInput(input);
                    Assert.That(output, Is.Not.Null);
                    outputs.Add(output);
                    return output.vertices;
                }

                int created = ReadableSourceCache.CopiesCreated;
                Assert.That(Preview(f.Source), Is.EqualTo(expected));
                Mesh first = f.Owner.CachedReadablePreviewInputCopyForTests;
                Assert.That(Preview(f.Source), Is.EqualTo(expected));
                Assert.That(f.Owner.CachedReadablePreviewInputCopyForTests, Is.SameAs(first));
                Assert.That(ReadableSourceCache.CopiesCreated, Is.EqualTo(created + 1));

                EditorUtility.SetDirty(f.Source);
                Assert.That(Preview(f.Source), Is.EqualTo(expected));
                Assert.That(first == null, Is.True);
                Mesh second = f.Owner.CachedReadablePreviewInputCopyForTests;

                Assert.That(Preview(other), Is.EqualTo(expected));
                Assert.That(second == null, Is.True, "A different input Mesh replaces the copy.");
                Assert.That(ReadableSourceCache.CopiesCreated, Is.EqualTo(created + 3));
            }
            finally
            {
                foreach (var output in outputs) if (output != null) Object.DestroyImmediate(output);
                Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void ProfileFingerprints_ReusedUntilSerializedEditUndoOrSaveToProfile()
        {
            using var f = new DeformerFixture(active: false);
            var profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            try
            {
                Assert.That(f.Owner.SaveToProfile(profile), Is.True);
                int computed = MeshDeformerProfile.ContentFingerprintsComputed;
                string initial = profile.GetContentFingerprint();
                string compatibility = profile.GetCompatibilityFingerprint();
                Assert.That(profile.GetContentFingerprint(), Is.SameAs(initial));
                Assert.That(profile.GetCompatibilityFingerprint(), Is.SameAs(compatibility));
                Assert.That(MeshDeformerProfile.ContentFingerprintsComputed, Is.EqualTo(computed + 1));

                using (var serialized = new SerializedObject(profile))
                {
                    serialized.FindProperty("_groups.Array.data[0]._name").stringValue = "Inspector edit";
                    serialized.ApplyModifiedProperties();
                }
                string inspected = profile.GetContentFingerprint();
                Assert.That(inspected, Is.Not.EqualTo(initial));
                Assert.That(MeshDeformerProfile.ContentFingerprintsComputed, Is.EqualTo(computed + 2));

                Undo.IncrementCurrentGroup();
                Undo.RegisterCompleteObjectUndo(profile, "Rename Profile group");
                profile.Groups[0].Name = "Undoable edit";
                EditorUtility.SetDirty(profile);
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
                string undoable = profile.GetContentFingerprint();
                Assert.That(undoable, Is.Not.EqualTo(inspected));
                Undo.PerformUndo();
                Assert.That(profile.GetContentFingerprint(), Is.EqualTo(inspected));
                Undo.PerformRedo();
                Assert.That(profile.GetContentFingerprint(), Is.EqualTo(undoable));

                int beforeSave = MeshDeformerProfile.ContentFingerprintsComputed;
                f.Owner.SetDisplacement(0, Vector3.one * 0.3f);
                Assert.That(f.Owner.SaveToProfile(profile), Is.True);
                string saved = profile.GetContentFingerprint();
                Assert.That(saved, Is.Not.EqualTo(undoable));
                Assert.That(MeshDeformerProfile.ContentFingerprintsComputed, Is.EqualTo(beforeSave + 1));
                Assert.That(profile.GetContentFingerprint(), Is.SameAs(saved));
                Assert.That(MeshDeformerProfile.ContentFingerprintsComputed, Is.EqualTo(beforeSave + 1));
            }
            finally { Object.DestroyImmediate(profile); }
        }

        [Test]
        public void ProfileCompatibility_ReusesCaptureUntilMeshWriteUndoOrSourceSwap()
        {
            var mesh = CreateQuad();
            var reordered = CreateQuad();
            reordered.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            var profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            try
            {
                profile.Capture(new List<DeformerGroup>(), 0, mesh);
                int computed = MeshCompatibilityMetadata.ComparisonCapturesComputed;
                Assert.That(profile.EvaluateCompatibility(mesh), Is.EqualTo(ProfileCompatibilityStatus.CompatibleSourceDiffers));
                Assert.That(profile.EvaluateCompatibility(mesh), Is.EqualTo(ProfileCompatibilityStatus.CompatibleSourceDiffers));
                Assert.That(MeshCompatibilityMetadata.ComparisonCapturesComputed, Is.EqualTo(computed + 1));

                var vertices = mesh.vertices;
                var moved = (Vector3[])vertices.Clone();
                moved[1] += Vector3.forward;
                mesh.vertices = moved;
                Assert.That(profile.EvaluateCompatibility(mesh), Is.EqualTo(ProfileCompatibilityStatus.TopologyMismatch));
                mesh.vertices = vertices;
                Assert.That(profile.EvaluateCompatibility(mesh), Is.EqualTo(ProfileCompatibilityStatus.CompatibleSourceDiffers));
                Assert.That(MeshCompatibilityMetadata.ComparisonCapturesComputed, Is.EqualTo(computed + 3));

                PerformUnrelatedUndoRedo();
                Assert.That(profile.EvaluateCompatibility(mesh), Is.EqualTo(ProfileCompatibilityStatus.CompatibleSourceDiffers));
                Assert.That(MeshCompatibilityMetadata.ComparisonCapturesComputed, Is.EqualTo(computed + 4));

                var resolver = new DeformerDataResolver();
                Assert.That(resolver.Resolve(DeformerDataSource.Profile, null, 0, profile, mesh).Status,
                    Is.EqualTo(DeformerDataResolutionStatus.Profile));
                Assert.That(resolver.Resolve(DeformerDataSource.Profile, null, 0, profile, reordered).Status,
                    Is.EqualTo(DeformerDataResolutionStatus.IncompatibleProfile));
                Assert.That(resolver.Resolve(DeformerDataSource.Profile, null, 0, profile, mesh).Status,
                    Is.EqualTo(DeformerDataResolutionStatus.Profile));
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(reordered);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void AssetReimport_InvalidatesProfileAndMeshTokens()
        {
            string folder = "Assets/__ChangeTokens_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            var mesh = CreateQuad();
            var profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            try
            {
                profile.Capture(new List<DeformerGroup> { new DeformerGroup { Name = "Imported" } }, 0, mesh);
                AssetDatabase.CreateAsset(mesh, folder + "/source.asset");
                AssetDatabase.CreateAsset(profile, folder + "/profile.asset");
                AssetDatabase.SaveAssets();
                profile.GetContentFingerprint();
                int computed = MeshDeformerProfile.ContentFingerprintsComputed;
                profile.GetContentFingerprint();
                Assert.That(MeshDeformerProfile.ContentFingerprintsComputed, Is.EqualTo(computed));
                var meshToken = DeformerChangeTokens.Capture(mesh);
                int compatibility = MeshCompatibilityMetadata.ComparisonCapturesComputed;
                profile.EvaluateCompatibility(mesh);
                profile.EvaluateCompatibility(mesh);
                Assert.That(MeshCompatibilityMetadata.ComparisonCapturesComputed, Is.EqualTo(compatibility + 1));

                int generation = DeformerChangeTokens.Generation;
                AssetDatabase.ImportAsset(folder + "/profile.asset", ImportAssetOptions.ForceUpdate);
                AssetDatabase.ImportAsset(folder + "/source.asset", ImportAssetOptions.ForceUpdate);
                Assert.That(DeformerChangeTokens.Generation, Is.Not.EqualTo(generation), "Imports advance the generation.");

                var reloadedProfile = AssetDatabase.LoadAssetAtPath<MeshDeformerProfile>(folder + "/profile.asset");
                var reloadedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(folder + "/source.asset");
                Assert.That(DeformerChangeTokens.Capture(reloadedMesh).Equals(meshToken), Is.False);
                reloadedProfile.GetContentFingerprint();
                Assert.That(MeshDeformerProfile.ContentFingerprintsComputed, Is.EqualTo(computed + 1));
                Assert.That(reloadedProfile.EvaluateCompatibility(reloadedMesh),
                    Is.EqualTo(ProfileCompatibilityStatus.CompatibleSourceDiffers));
                Assert.That(MeshCompatibilityMetadata.ComparisonCapturesComputed, Is.EqualTo(compatibility + 2));
            }
            finally { AssetDatabase.DeleteAsset(folder); }
        }

        [Test]
        public void RowInputPreflight_ReusesResultUntilAnyInputChanges()
        {
            using var f = new DeformerFixture(active: false);
            var owner = f.Owner;
            int preflights = DeformerAuthoringSource.RowInputPreflightsComputed;
            int hashes = DeformerAuthoringSource.RowInputTopologyHashesComputed;
            Assert.That(DeformerAuthoringSource.TryReadForRowInput(owner), Is.True);
            Assert.That(DeformerAuthoringSource.TryReadForRowInput(owner), Is.True);
            Assert.That(DeformerAuthoringSource.RowInputPreflightsComputed, Is.EqualTo(preflights + 1));
            Assert.That(DeformerAuthoringSource.RowInputTopologyHashesComputed, Is.EqualTo(hashes + 1));

            // A SerializedProperty edit recomputes the preflight; the unchanged Mesh keeps its hash.
            using (var serialized = new SerializedObject(owner))
            {
                var property = serialized.FindProperty("_recalculateNormals");
                property.boolValue = !property.boolValue;
                serialized.ApplyModifiedProperties();
            }
            Assert.That(DeformerAuthoringSource.TryReadForRowInput(owner), Is.True);
            Assert.That(DeformerAuthoringSource.RowInputPreflightsComputed, Is.EqualTo(preflights + 2));
            Assert.That(DeformerAuthoringSource.RowInputTopologyHashesComputed, Is.EqualTo(hashes + 1));

            PerformUnrelatedUndoRedo();
            Assert.That(DeformerAuthoringSource.TryReadForRowInput(owner), Is.True);
            Assert.That(DeformerAuthoringSource.RowInputPreflightsComputed, Is.EqualTo(preflights + 3));

            // Renderer mesh swap: the displayed Mesh no longer is the bound source.
            var other = Object.Instantiate(f.Source);
            try
            {
                f.Filter.sharedMesh = other;
                Assert.That(DeformerAuthoringSource.TryReadForRowInput(owner), Is.False);
                f.Filter.sharedMesh = f.Source;
                Assert.That(DeformerAuthoringSource.TryReadForRowInput(owner), Is.True);
            }
            finally { Object.DestroyImmediate(other); }

            // In-place topology drift of the source Mesh.
            var triangles = f.Source.triangles;
            f.Source.triangles = new[] { 0, 2, 1 };
            Assert.That(DeformerAuthoringSource.TryReadForRowInput(owner), Is.False);
            Assert.That(DeformerAuthoringSource.TryRead(owner, out _, out _, out _), Is.False);
            f.Source.triangles = triangles;
            Assert.That(DeformerAuthoringSource.TryReadForRowInput(owner), Is.True);

            // Dirty-marked payload corruption.
            var raw = SerializedDeformerReader.Read(owner);
            var brush = raw.ActiveLayers[raw.ActiveLayerIndex];
            var displacements = brush.BrushDisplacements;
            brush.BrushDisplacements = new[] { new Vector3(float.NaN, 0f, 0f), Vector3.zero, Vector3.zero };
            EditorUtility.SetDirty(owner);
            Assert.That(DeformerAuthoringSource.TryReadForRowInput(owner), Is.False);
            brush.BrushDisplacements = displacements;
            EditorUtility.SetDirty(owner);
            Assert.That(DeformerAuthoringSource.TryReadForRowInput(owner), Is.True);
        }

        private static void PerformUnrelatedUndoRedo()
        {
            var probe = new GameObject("__ChangeTokenUndoProbe");
            try
            {
                Undo.IncrementCurrentGroup();
                Undo.RecordObject(probe.transform, "Change token probe");
                probe.transform.position = Vector3.one;
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
                Undo.PerformUndo();
                Undo.PerformRedo();
            }
            finally
            {
                Undo.ClearUndo(probe.transform);
                Object.DestroyImmediate(probe);
            }
        }

        private static Mesh CreateQuad()
        {
            var mesh = new Mesh { name = "__ChangeTokenQuad" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.one, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private sealed class DeformerFixture : IDisposable
        {
            internal readonly GameObject Root;
            internal readonly Mesh Source;
            internal readonly MeshFilter Filter;
            internal readonly LatticeDeformer Owner;

            internal DeformerFixture(bool active)
            {
                Root = new GameObject("__ChangeTokenFixture");
                Root.SetActive(false);
                Source = new Mesh
                {
                    name = "__ChangeTokenSource",
                    vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                    triangles = new[] { 0, 1, 2 }
                };
                Source.RecalculateNormals();
                Filter = Root.AddComponent<MeshFilter>();
                Root.AddComponent<MeshRenderer>();
                Filter.sharedMesh = Source;
                Owner = Root.AddComponent<LatticeDeformer>();
                Owner.Reset();
                Owner.ActiveLayerIndex = Owner.AddLayer("Brush", MeshDeformerLayerType.Brush);
                Owner.EnsureDisplacementCapacity();
                Owner.SetDisplacement(0, Vector3.up * 0.17f);
                Root.SetActive(active);
            }

            public void Dispose()
            {
                if (Owner != null)
                {
                    Undo.ClearUndo(Owner);
                    var runtime = Owner.RuntimeMesh;
                    Owner.InvalidateCache();
                    Owner.RestoreOriginalMesh();
                    if (runtime != null) Object.DestroyImmediate(runtime);
                }
                Object.DestroyImmediate(Root);
                if (Source != null) Object.DestroyImmediate(Source);
            }
        }
    }
}
#endif
