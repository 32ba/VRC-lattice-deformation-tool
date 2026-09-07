#if UNITY_EDITOR
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Net._32Ba.LatticeDeformationTool;
using Net._32Ba.LatticeDeformationTool.Editor;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class MeshDeformerClipboardTests
    {
        private object _previousLayerClipboard;
        private object _previousGroupClipboard;
        private object _previousClipboardResult;
        private object _previousClipboardTarget;

        [SetUp]
        public void SetUp()
        {
            _previousLayerClipboard = ReadClipboardField("s_layer");
            _previousGroupClipboard = ReadClipboardField("s_group");
            _previousClipboardResult = ReadClipboardField("<LastResult>k__BackingField");
            _previousClipboardTarget = ReadClipboardField("<LastTarget>k__BackingField");
            MeshDeformerClipboard.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            SetClipboardField("s_layer", _previousLayerClipboard);
            SetClipboardField("s_group", _previousGroupClipboard);
            SetClipboardField("<LastResult>k__BackingField", _previousClipboardResult);
            SetClipboardField("<LastTarget>k__BackingField", _previousClipboardTarget);
        }

        [Test]
        public void BrushLayer_SameContentClone_PastesAndKeepsIndependentPayload()
        {
            var sourceMesh = CreateMesh("source");
            var targetMesh = Object.Instantiate(sourceMesh);
            var source = CreateDeformer(sourceMesh, "source-deformer");
            var target = CreateDeformer(targetMesh, "target-deformer");
            try
            {
                source.AddLayer("Brush", MeshDeformerLayerType.Brush);
                source.SetDisplacement(0, Vector3.up * 0.25f);
                Assert.That(MeshDeformerClipboard.CopyLayer(source, 1).Succeeded, Is.True);
                source.SetDisplacement(0, Vector3.right);

                var result = MeshDeformerClipboard.PasteLayer(target);
                Assert.That(result.Succeeded, Is.True, result.Code);
                Assert.That(target.Layers.Count, Is.EqualTo(2));
                Assert.That(target.GetDisplacement(0), Is.EqualTo(Vector3.up * 0.25f));

                Assert.That(target.GetDisplacement(0), Is.EqualTo(Vector3.up * 0.25f));

                target.Deform(false);
                Assert.That(target.RuntimeMesh, Is.Not.Null);
                Assert.That(target.RuntimeMesh.vertices[0], Is.EqualTo(targetMesh.vertices[0] + Vector3.up * 0.25f));

                Undo.PerformUndo();
                target.InvalidateCache();
                target.Deform(false);
                Assert.That(target.Layers.Count, Is.EqualTo(1));
                Undo.PerformRedo();
                target.InvalidateCache();
                target.Deform(false);
                Assert.That(target.Layers.Count, Is.EqualTo(2));
                Assert.That(target.RuntimeMesh.vertices[0], Is.EqualTo(targetMesh.vertices[0] + Vector3.up * 0.25f));
            }
            finally
            {
                Destroy(source.gameObject, target.gameObject, sourceMesh, targetMesh);
            }
        }

        [TestCase("vertex-count")]
        [TestCase("vertex-order")]
        [TestCase("index-order")]
        public void BrushLayer_IncompatibleMesh_IsRejectedWithoutChangingTarget(string variant)
        {
            var sourceMesh = CreateMesh("source");
            var targetMesh = Object.Instantiate(sourceMesh);
            if (variant == "vertex-count")
            {
                Object.DestroyImmediate(targetMesh);
                targetMesh = CreateMesh("different-count",
                    new[] { Vector3.zero, Vector3.right }, new[] { 0, 1, 1 });
            }
            else if (variant == "vertex-order")
                targetMesh.vertices = new[] { Vector3.zero, Vector3.up, Vector3.right };
            else
                targetMesh.triangles = new[] { 0, 2, 1 };

            var source = CreateDeformer(sourceMesh, "source-deformer");
            var target = CreateDeformer(targetMesh, "target-deformer");
            try
            {
                source.AddLayer("Brush", MeshDeformerLayerType.Brush);
                source.SetDisplacement(0, Vector3.up * 0.25f);
                Assert.That(MeshDeformerClipboard.CopyLayer(source, 1).Succeeded, Is.True);
                int layerCount = target.Layers.Count;
                int active = target.ActiveLayerIndex;
                string before = EditorJsonUtility.ToJson(target);
                Renderer targetRenderer = target.TargetRenderer;
                Mesh targetAssignedMesh = targetRenderer.GetComponent<MeshFilter>().sharedMesh;

                var result = MeshDeformerClipboard.PasteLayer(target);

                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Code, Is.EqualTo(
                    variant == "vertex-count"
                        ? "LDT_CLIPBOARD_VERTEX_COUNT_MISMATCH"
                        : "LDT_CLIPBOARD_TOPOLOGY_MISMATCH"));
                Assert.That(target.Layers.Count, Is.EqualTo(layerCount));
                Assert.That(target.ActiveLayerIndex, Is.EqualTo(active));
                Assert.That(EditorJsonUtility.ToJson(target), Is.EqualTo(before));
                Assert.That(target.TargetRenderer, Is.SameAs(targetRenderer));
                Assert.That(targetRenderer.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(targetAssignedMesh));

                if (variant == "index-order")
                {
                    // The target mesh is intentionally left untouched after the
                    // refusal. Choose a new compatible source from that same mesh
                    // content, then perform the successful copy/paste operation.
                    var compatibleMesh = Object.Instantiate(targetMesh);
                    var compatibleSource = CreateDeformer(compatibleMesh, "compatible-source-deformer");
                    try
                    {
                        compatibleSource.AddLayer("Brush", MeshDeformerLayerType.Brush);
                        compatibleSource.SetDisplacement(0, Vector3.up * 0.25f);
                        Assert.That(MeshDeformerClipboard.CopyLayer(compatibleSource, 1).Succeeded, Is.True);
                        Assert.That(MeshDeformerClipboard.PasteLayer(target).Succeeded, Is.True);
                        Assert.That(MeshDeformerClipboard.LastResult.Succeeded, Is.True);
                    }
                    finally
                    {
                        Destroy(compatibleSource.gameObject, compatibleMesh);
                    }
                }
            }
            finally
            {
                Destroy(source.gameObject, target.gameObject, sourceMesh, targetMesh);
            }
        }

        [Test]
        public void PureLatticeLayer_DifferentMesh_IsStillAllowed()
        {
            var sourceMesh = CreateMesh("source");
            var targetMesh = CreateMesh("different", new[] { Vector3.zero, Vector3.right * 2f, Vector3.up * 3f }, new[] { 0, 1, 2 });
            var source = CreateDeformer(sourceMesh, "source-deformer");
            var target = CreateDeformer(targetMesh, "target-deformer");
            try
            {
                Assert.That(MeshDeformerClipboard.CopyLayer(source, 0).Succeeded, Is.True);
                Assert.That(MeshDeformerClipboard.PasteLayer(target).Succeeded, Is.True);
                Assert.That(target.Layers.Count, Is.EqualTo(2));
            }
            finally
            {
                Destroy(source.gameObject, target.gameObject, sourceMesh, targetMesh);
            }
        }

        [Test]
        public void ProfileTarget_IsRejectedBeforeUndoOrMutation()
        {
            var sourceMesh = CreateMesh("source");
            var targetMesh = Object.Instantiate(sourceMesh);
            var source = CreateDeformer(sourceMesh, "source-deformer");
            var target = CreateDeformer(targetMesh, "target-deformer");
            var profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            try
            {
                source.AddLayer("Brush", MeshDeformerLayerType.Brush);
                source.SetDisplacement(0, Vector3.up);
                Assert.That(MeshDeformerClipboard.CopyLayer(source, 1).Succeeded, Is.True);
                target.Profile = profile;
                target.DataSource = DeformerDataSource.Profile;
                int count = target.Layers.Count;
                var result = MeshDeformerClipboard.PasteLayer(target);
                Assert.That(result.Code, Is.EqualTo("LDT_CLIPBOARD_PROFILE_READ_ONLY"));
                Assert.That(target.Layers.Count, Is.EqualTo(count));
            }
            finally
            {
                Destroy(profile, source.gameObject, target.gameObject, sourceMesh, targetMesh);
            }
        }

        [Test]
        public void BrushLayer_SourceMutationAfterCopy_IsRejected()
        {
            var sourceMesh = CreateMesh("source");
            var source = CreateDeformer(sourceMesh, "source-deformer");
            var target = CreateDeformer(sourceMesh, "target-deformer");
            try
            {
                source.AddLayer("Brush", MeshDeformerLayerType.Brush);
                source.SetDisplacement(0, Vector3.up * 0.25f);
                Assert.That(MeshDeformerClipboard.CopyLayer(source, 1).Succeeded, Is.True);
                var changed = sourceMesh.vertices;
                changed[0] += Vector3.forward * 0.1f;
                sourceMesh.vertices = changed;
                string before = EditorJsonUtility.ToJson(target);

                var result = MeshDeformerClipboard.PasteLayer(target);

                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Code, Is.EqualTo("LDT_CLIPBOARD_TOPOLOGY_MISMATCH"));
                Assert.That(EditorJsonUtility.ToJson(target), Is.EqualTo(before));
            }
            finally
            {
                Destroy(source.gameObject, target.gameObject, sourceMesh);
            }
        }

        [Test]
        public void BrushLayer_IndependentClone_SurvivesSourceMeshMutation()
        {
            var sourceMesh = CreateMesh("source");
            var targetMesh = Object.Instantiate(sourceMesh);
            var source = CreateDeformer(sourceMesh, "source-deformer");
            var target = CreateDeformer(targetMesh, "target-deformer");
            try
            {
                source.AddLayer("Brush", MeshDeformerLayerType.Brush);
                source.SetDisplacement(0, Vector3.up * 0.25f);
                Assert.That(MeshDeformerClipboard.CopyLayer(source, 1).Succeeded, Is.True);
                var changed = sourceMesh.vertices;
                changed[0] += Vector3.forward * 0.1f;
                sourceMesh.vertices = changed;

                Assert.That(MeshDeformerClipboard.PasteLayer(target).Succeeded, Is.True);
                Assert.That(target.GetDisplacement(0), Is.EqualTo(Vector3.up * 0.25f));
            }
            finally
            {
                Destroy(source.gameObject, target.gameObject, sourceMesh, targetMesh);
            }
        }

        [Test]
        public void BrushLayer_InvalidArrayLengthsAndNonFinitePayload_AreRejectedAtCopy()
        {
            var mesh = CreateMesh("source");
            var deformer = CreateDeformer(mesh, "source-deformer");
            try
            {
                deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
                deformer.EnsureDisplacementCapacity();
                var brush = deformer.Layers[1];
                brush.Enabled = false;
                SetPrivateField(brush, "_brushDisplacements", new[] { Vector3.zero });
                var lengthResult = MeshDeformerClipboard.CopyLayer(deformer, 1);
                Assert.That(lengthResult.Succeeded, Is.False);

                deformer.EnsureDisplacementCapacity();
                brush = deformer.Layers[1];
                brush.Enabled = true;
                SetPrivateField(brush, "_brushDisplacements", new[]
                {
                    new Vector3(float.NaN, 0f, 0f), Vector3.zero, Vector3.zero
                });
                var finiteResult = MeshDeformerClipboard.CopyLayer(deformer, 1);
                Assert.That(finiteResult.Succeeded, Is.False);
                Assert.That(finiteResult.Code, Is.EqualTo("LDT_CLIPBOARD_NONFINITE_PAYLOAD"));

                brush.BrushDisplacements = new[] { Vector3.zero, Vector3.zero, Vector3.zero };
                brush.VertexMask = new[] { 1f, 1f };
                var maskResult = MeshDeformerClipboard.CopyLayer(deformer, 1);
                Assert.That(maskResult.Succeeded, Is.False);

                var lattice = deformer.Layers[0];
                lattice.VertexMask = new[] { 1f, 1f };
                var latticeMaskResult = MeshDeformerClipboard.CopyLayer(deformer, 0);
                Assert.That(latticeMaskResult.Succeeded, Is.False);
            }
            finally
            {
                Destroy(deformer.gameObject, mesh);
            }
        }

        [Test]
        public void GroupWithOneIncompatibleLayer_IsRejectedAsWhole()
        {
            var sourceMesh = CreateMesh("source");
            var targetMesh = Object.Instantiate(sourceMesh);
            var source = CreateDeformer(sourceMesh, "source-deformer");
            var target = CreateDeformer(targetMesh, "target-deformer");
            try
            {
                source.AddLayer("Brush", MeshDeformerLayerType.Brush);
                source.SetDisplacement(0, Vector3.up * 0.25f);
                Assert.That(MeshDeformerClipboard.CopyGroup(source, 0).Succeeded, Is.True);
                var changed = targetMesh.vertices;
                changed[1] += Vector3.forward * 0.1f;
                targetMesh.vertices = changed;
                string before = EditorJsonUtility.ToJson(target);

                var result = MeshDeformerClipboard.PasteGroup(target);

                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Code, Is.EqualTo("LDT_CLIPBOARD_TOPOLOGY_MISMATCH"));
                Assert.That(EditorJsonUtility.ToJson(target), Is.EqualTo(before));
                Assert.That(target.Groups.Count, Is.EqualTo(1));
            }
            finally
            {
                Destroy(source.gameObject, target.gameObject, sourceMesh, targetMesh);
            }
        }

        [Test]
        public void GroupPaste_SuccessUndoRedo_RestoresOneOperation()
        {
            var sourceMesh = CreateMesh("source");
            var targetMesh = Object.Instantiate(sourceMesh);
            var source = CreateDeformer(sourceMesh, "source-deformer");
            var target = CreateDeformer(targetMesh, "target-deformer");
            try
            {
                source.AddLayer("Brush", MeshDeformerLayerType.Brush);
                source.SetDisplacement(0, Vector3.up * 0.25f);
                Assert.That(MeshDeformerClipboard.CopyGroup(source, 0).Succeeded, Is.True);
                Assert.That(MeshDeformerClipboard.PasteGroup(target).Succeeded, Is.True);
                Assert.That(target.Groups.Count, Is.EqualTo(2));
                Undo.PerformUndo();
                Assert.That(target.Groups.Count, Is.EqualTo(1));
                Undo.PerformRedo();
                Assert.That(target.Groups.Count, Is.EqualTo(2));
            }
            finally
            {
                Destroy(source.gameObject, target.gameObject, sourceMesh, targetMesh);
            }
        }

        [Test]
        public void InvalidTargetStructure_IsRejectedBeforeClipboardMutation()
        {
            var sourceMesh = CreateMesh("source");
            var targetMesh = Object.Instantiate(sourceMesh);
            var source = CreateDeformer(sourceMesh, "source-deformer");
            var target = CreateDeformer(targetMesh, "target-deformer");
            try
            {
                source.AddLayer("Brush", MeshDeformerLayerType.Brush);
                source.SetDisplacement(0, Vector3.up);
                Assert.That(MeshDeformerClipboard.CopyLayer(source, 1).Succeeded, Is.True);
                SetPrivateField(target, "_groups", null);
                string before = EditorJsonUtility.ToJson(target);

                var result = MeshDeformerClipboard.PasteLayer(target);

                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Code, Is.EqualTo("LDT_CLIPBOARD_INVALID_TARGET_STRUCTURE"));
                Assert.That(EditorJsonUtility.ToJson(target), Is.EqualTo(before));
            }
            finally
            {
                Destroy(source.gameObject, target.gameObject, sourceMesh, targetMesh);
            }
        }

        [Test]
        public void ExistingTargetBrushLengthError_IsRejectedWithoutChangingTarget()
        {
            var sourceMesh = CreateMesh("source");
            var targetMesh = Object.Instantiate(sourceMesh);
            var source = CreateDeformer(sourceMesh, "source-deformer");
            var target = CreateDeformer(targetMesh, "target-deformer");
            try
            {
                source.AddLayer("Brush", MeshDeformerLayerType.Brush);
                source.SetDisplacement(0, Vector3.up);
                Assert.That(MeshDeformerClipboard.CopyLayer(source, 1).Succeeded, Is.True);
                target.AddLayer("Existing invalid Brush", MeshDeformerLayerType.Brush);
                SetPrivateField(target.Layers[1], "_brushDisplacements", new[]
                {
                    Vector3.zero, Vector3.zero
                });
                int layerCount = target.Layers.Count;
                string before = EditorJsonUtility.ToJson(target);
                Renderer renderer = target.TargetRenderer;
                Mesh assignedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;

                var result = MeshDeformerClipboard.PasteLayer(target);

                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Code, Is.EqualTo("LDT_CLIPBOARD_INVALID_TARGET_STRUCTURE"));
                Assert.That(target.Layers.Count, Is.EqualTo(layerCount));
                Assert.That(EditorJsonUtility.ToJson(target), Is.EqualTo(before));
                Assert.That(target.TargetRenderer, Is.SameAs(renderer));
                Assert.That(renderer.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(assignedMesh));
            }
            finally
            {
                Destroy(source.gameObject, target.gameObject, sourceMesh, targetMesh);
            }
        }

        [Test]
        public void ReadWriteDisabledMesh_UsesEditorReadableCopy()
        {
            var sourceMesh = CreateMesh("source");
            var targetMesh = Object.Instantiate(sourceMesh);
            var source = CreateDeformer(sourceMesh, "source-deformer");
            var target = CreateDeformer(targetMesh, "target-deformer");
            try
            {
                sourceMesh.UploadMeshData(true);
                targetMesh.UploadMeshData(true);
                source.AddLayer("Brush", MeshDeformerLayerType.Brush);
                source.SetDisplacement(0, Vector3.up * 0.25f);

                Assert.That(MeshDeformerClipboard.CopyLayer(source, 1).Succeeded, Is.True);
                Assert.That(MeshDeformerClipboard.PasteLayer(target).Succeeded, Is.True);
                Assert.That(sourceMesh.isReadable, Is.False);
                Assert.That(targetMesh.isReadable, Is.False);
            }
            finally
            {
                Destroy(source.gameObject, target.gameObject, sourceMesh, targetMesh);
            }
        }

        [Test]
        public void EditingVisibilityReason_ExplainsEachNonVisibleState()
        {
            var mesh = CreateMesh("source");
            var deformer = CreateDeformer(mesh, "deformer");
            try
            {
                Assert.That(LatticeDeformerEditor.GetEditingVisibilityReasonKey(deformer), Is.EqualTo(string.Empty));
                deformer.enabled = false;
                Assert.That(LatticeDeformerEditor.GetEditingVisibilityReasonKey(deformer), Is.EqualTo(LocKey.EditUnavailableComponent));
                deformer.enabled = true;
                deformer.ActiveGroup.Enabled = false;
                Assert.That(LatticeDeformerEditor.GetEditingVisibilityReasonKey(deformer), Is.EqualTo(LocKey.EditUnavailableGroup));
                deformer.ActiveGroup.Enabled = true;
                deformer.Layers[0].Enabled = false;
                Assert.That(LatticeDeformerEditor.GetEditingVisibilityReasonKey(deformer), Is.EqualTo(LocKey.EditUnavailableLayer));
                deformer.Layers[0].Enabled = true;
                deformer.Layers[0].Weight = 0f;
                Assert.That(LatticeDeformerEditor.GetEditingVisibilityReasonKey(deformer), Is.EqualTo(LocKey.EditUnavailableWeight));
                deformer.Layers[0].Weight = 1f;
                deformer.ActiveGroup.BlendShapeOutput = BlendShapeOutputMode.OutputAsBlendShape;
                Assert.That(LatticeDeformerEditor.GetEditingVisibilityReasonKey(deformer), Is.EqualTo(LocKey.EditBlendShapeOutputInfo));
            }
            finally
            {
                Destroy(deformer.gameObject, mesh);
            }
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }

        private static object ReadClipboardField(string name)
        {
            var field = typeof(MeshDeformerClipboard).GetField(
                name, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            return field.GetValue(null);
        }

        private static void SetClipboardField(string name, object value)
        {
            var field = typeof(MeshDeformerClipboard).GetField(
                name, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(null, value);
        }

        private static LatticeDeformer CreateDeformer(Mesh mesh, string name)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshRenderer>();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var deformer = go.AddComponent<LatticeDeformer>();
            deformer.Reset();
            return deformer;
        }

        private static Mesh CreateMesh(string name)
        {
            return CreateMesh(name,
                new[] { Vector3.zero, Vector3.right, Vector3.up },
                new[] { 0, 1, 2 });
        }

        private static Mesh CreateMesh(string name, Vector3[] vertices, int[] triangles)
        {
            var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles };
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        private static void Destroy(params Object[] objects)
        {
            foreach (var item in objects)
            {
                if (item != null) Object.DestroyImmediate(item);
            }
        }
    }
}
#endif
