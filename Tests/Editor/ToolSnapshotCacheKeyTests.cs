#if UNITY_EDITOR
using Net._32Ba.LatticeDeformationTool;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    /// <summary>
    /// Cache keys that let the Brush penetration display and the Lattice cage skinning
    /// snapshot skip redundant work while still tracking every input they read.
    /// </summary>
    public sealed class ToolSnapshotCacheKeyTests
    {
        private static Mesh CreateSkinnedTriangle(string name, bool withNormals)
        {
            var mesh = new Mesh
            {
                name = name,
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
                boneWeights = new[]
                {
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f }
                },
                bindposes = new[] { Matrix4x4.identity }
            };
            if (withNormals)
                mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.AddBlendShapeFrame(
                "Expand",
                100f,
                new[] { Vector3.left, Vector3.right, Vector3.up },
                new Vector3[3],
                new Vector3[3]);
            return mesh;
        }

        private static SkinnedMeshRenderer CreateSkinnedRenderer(GameObject owner, Mesh mesh, Transform bone)
        {
            var renderer = owner.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.rootBone = bone;
            renderer.bones = new[] { bone };
            return renderer;
        }

        [Test]
        public void PenetrationDetectionCacheKey_TracksDynamicStateAndDefaultsToZero()
        {
            Matrix4x4 target = Matrix4x4.Translate(Vector3.one);
            Matrix4x4 reference = Matrix4x4.Translate(Vector3.left);

            var legacy = new PenetrationDetectionCacheKey(1, 2, 3, 4, 5, 6, 7, 8, target, reference);
            var zero = new PenetrationDetectionCacheKey(1, 2, 3, 4, 5, 6, 7, 8, target, reference, 0);
            var pose = new PenetrationDetectionCacheKey(1, 2, 3, 4, 5, 6, 7, 8, target, reference, 99);
            var samePose = new PenetrationDetectionCacheKey(1, 2, 3, 4, 5, 6, 7, 8, target, reference, 99);

            Assert.That(zero, Is.EqualTo(legacy));
            Assert.That(zero.GetHashCode(), Is.EqualTo(legacy.GetHashCode()));
            Assert.That(pose, Is.Not.EqualTo(legacy));
            Assert.That(samePose, Is.EqualTo(pose));
            Assert.That(samePose.GetHashCode(), Is.EqualTo(pose.GetHashCode()));
        }

        [Test]
        public void SkinnedReferenceDynamicHash_TracksReferencePoseAndTargetSourceWeights()
        {
            var referenceObject = new GameObject("Reference");
            var referenceBone = new GameObject("ReferenceBone");
            var targetObject = new GameObject("Target");
            var targetBone = new GameObject("TargetBone");
            Mesh referenceMesh = CreateSkinnedTriangle("Reference", true);
            Mesh targetMesh = CreateSkinnedTriangle("Target", true);
            try
            {
                referenceBone.transform.SetParent(referenceObject.transform, false);
                targetBone.transform.SetParent(targetObject.transform, false);
                var reference = CreateSkinnedRenderer(referenceObject, referenceMesh, referenceBone.transform);
                var targetRenderer = CreateSkinnedRenderer(targetObject, targetMesh, targetBone.transform);
                var deformer = targetObject.AddComponent<LatticeDeformer>();

                Assert.That(BrushToolHandler.TryComputeSkinnedReferenceDynamicHash(
                    deformer, targetMesh, reference, out int initial), Is.True);
                Assert.That(BrushToolHandler.TryComputeSkinnedReferenceDynamicHash(
                    deformer, targetMesh, reference, out int repeated), Is.True);
                Assert.That(repeated, Is.EqualTo(initial), "An unchanged pose must reuse the cached result.");

                referenceBone.transform.localPosition = Vector3.forward;
                Assert.That(BrushToolHandler.TryComputeSkinnedReferenceDynamicHash(
                    deformer, targetMesh, reference, out int moved), Is.True);
                Assert.That(moved, Is.Not.EqualTo(initial), "A reference bone move must invalidate the result.");

                reference.SetBlendShapeWeight(0, 50f);
                Assert.That(BrushToolHandler.TryComputeSkinnedReferenceDynamicHash(
                    deformer, targetMesh, reference, out int referenceShape), Is.True);
                Assert.That(referenceShape, Is.Not.EqualTo(moved),
                    "A reference BlendShape weight change must invalidate the result.");

                targetRenderer.SetBlendShapeWeight(0, 25f);
                Assert.That(BrushToolHandler.TryComputeSkinnedReferenceDynamicHash(
                    deformer, targetMesh, reference, out int targetShape), Is.True);
                Assert.That(targetShape, Is.Not.EqualTo(referenceShape),
                    "A target source BlendShape weight change alters Deform and must invalidate the result.");

                Assert.That(BrushToolHandler.TryComputeSkinnedReferenceDynamicHash(
                    deformer, targetMesh, null, out _), Is.False);
                Assert.That(BrushToolHandler.TryComputeSkinnedReferenceDynamicHash(
                    null, targetMesh, reference, out _), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(referenceObject);
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(referenceMesh);
                Object.DestroyImmediate(targetMesh);
            }
        }

        [Test]
        public void PenetrationDetector_RendererOverload_RequiresAuthoredReferenceNormals()
        {
            var withNormals = new GameObject("WithNormals");
            var withoutNormals = new GameObject("WithoutNormals");
            var normalsMesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward },
                triangles = new[] { 0, 1, 2 }
            };
            var bareMesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 }
            };
            try
            {
                withNormals.AddComponent<MeshFilter>().sharedMesh = normalsMesh;
                var normalsRenderer = withNormals.AddComponent<MeshRenderer>();
                withoutNormals.AddComponent<MeshFilter>().sharedMesh = bareMesh;
                var bareRenderer = withoutNormals.AddComponent<MeshRenderer>();
                var points = new[]
                {
                    new Vector3(0.05f, 0.05f, -0.01f),
                    new Vector3(0.05f, 0.05f, 0.01f)
                };

                var detected = PenetrationDetector.DetectPenetration(
                    points, Matrix4x4.identity, normalsRenderer);
                Assert.That(detected.Contains(0), Is.True);
                Assert.That(detected.Contains(1), Is.False);

                Assert.That(
                    PenetrationDetector.DetectPenetration(points, Matrix4x4.identity, bareRenderer),
                    Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(withNormals);
                Object.DestroyImmediate(withoutNormals);
                Object.DestroyImmediate(normalsMesh);
                Object.DestroyImmediate(bareMesh);
                ClearanceQueryCache.PruneDeadEntries();
            }
        }

        [Test]
        public void SkinningDeformerInputHash_IgnoresControlPointWritesAndTracksSkinningInputs()
        {
            var go = new GameObject("Deformer");
            var bone = new GameObject("Bone");
            Mesh mesh = CreateSkinnedTriangle("Source", false);
            try
            {
                bone.transform.SetParent(go.transform, false);
                var renderer = CreateSkinnedRenderer(go, mesh, bone.transform);
                var deformer = go.AddComponent<LatticeDeformer>();
                var settings = new LatticeAsset();
                settings.EnsureInitialized();
                settings.LocalBounds = mesh.bounds;

                int initial = LatticeToolHandler.ComputeSkinningDeformerInputHash(deformer, settings);
                settings.SetControlPointLocal(0, settings.GetControlPointLocal(0) + Vector3.one);
                EditorUtility.SetDirty(deformer);
                Assert.That(
                    LatticeToolHandler.ComputeSkinningDeformerInputHash(deformer, settings),
                    Is.EqualTo(initial),
                    "Control points are not inputs of the cage skinning snapshot.");

                renderer.SetBlendShapeWeight(0, 40f);
                int weighted = LatticeToolHandler.ComputeSkinningDeformerInputHash(deformer, settings);
                Assert.That(weighted, Is.Not.EqualTo(initial),
                    "The BlendShape weight source feeds the control-point shape offsets.");

                settings.GridSize = new Vector3Int(4, 3, 3);
                Assert.That(
                    LatticeToolHandler.ComputeSkinningDeformerInputHash(deformer, settings),
                    Is.Not.EqualTo(weighted),
                    "The grid size defines the bound control points.");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(bone);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void SkinningSnapshot_WithEditingSettings_SkipsRefreshForControlPointWrites()
        {
            var go = new GameObject("Deformer");
            var bone = new GameObject("Bone");
            Mesh mesh = CreateSkinnedTriangle("Source", false);
            try
            {
                bone.transform.SetParent(go.transform, false);
                var renderer = CreateSkinnedRenderer(go, mesh, bone.transform);
                var deformer = go.AddComponent<LatticeDeformer>();
                var settings = new LatticeAsset();
                settings.EnsureInitialized();
                settings.LocalBounds = mesh.bounds;
                var handler = new LatticeToolHandler();
                Bounds sourceBounds = settings.LocalBounds;

                handler.UpdateSkinningSnapshotIfNeeded(
                    deformer, renderer, sourceBounds, Matrix4x4.identity, Matrix4x4.identity, settings);
                handler.UpdateSkinningSnapshotIfNeeded(
                    deformer, renderer, sourceBounds, Matrix4x4.identity, Matrix4x4.identity, settings);
                int warm = handler.SkinningRefreshCountForTests;

                // A drag frame: write a control point and dirty the component.
                settings.SetControlPointLocal(0, settings.GetControlPointLocal(0) + Vector3.one);
                EditorUtility.SetDirty(deformer);
                handler.UpdateSkinningSnapshotIfNeeded(
                    deformer, renderer, sourceBounds, Matrix4x4.identity, Matrix4x4.identity, settings);
                Assert.That(handler.SkinningRefreshCountForTests, Is.EqualTo(warm));

                bone.transform.localPosition = Vector3.forward;
                handler.UpdateSkinningSnapshotIfNeeded(
                    deformer, renderer, sourceBounds, Matrix4x4.identity, Matrix4x4.identity, settings);
                Assert.That(handler.SkinningRefreshCountForTests, Is.EqualTo(warm + 1),
                    "A pose change still refreshes the skinning snapshot.");

                settings.GridSize = new Vector3Int(4, 3, 3);
                handler.UpdateSkinningSnapshotIfNeeded(
                    deformer, renderer, sourceBounds, Matrix4x4.identity, Matrix4x4.identity, settings);
                Assert.That(handler.SkinningRefreshCountForTests, Is.EqualTo(warm + 2),
                    "A grid size change still refreshes the skinning snapshot.");

                // Without the caller's settings the component dirty count stays the key.
                handler.UpdateSkinningSnapshotIfNeeded(
                    deformer, renderer, sourceBounds, Matrix4x4.identity, Matrix4x4.identity);
                int legacyWarm = handler.SkinningRefreshCountForTests;
                EditorUtility.SetDirty(deformer);
                handler.UpdateSkinningSnapshotIfNeeded(
                    deformer, renderer, sourceBounds, Matrix4x4.identity, Matrix4x4.identity);
                Assert.That(handler.SkinningRefreshCountForTests, Is.EqualTo(legacyWarm + 1));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(bone);
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
#endif
