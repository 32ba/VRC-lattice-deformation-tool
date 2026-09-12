#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class VertexTransformOperationTests
    {
        [TestCase("Move", false, false)]
        [TestCase("Rotate", false, false)]
        [TestCase("Scale", false, false)]
        [TestCase("Move", true, false)]
        [TestCase("Rotate", true, false)]
        [TestCase("Scale", true, false)]
        [TestCase("Move", false, true)]
        [TestCase("Rotate", false, true)]
        [TestCase("Scale", false, true)]
        public void SelectedAndProportionalDelta_MatchesAnalyticGeometryAndUndo(string operation, bool scaled, bool restSpace)
        {
            var root = new GameObject("__VertexTransformOperation");
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.right, Vector3.right * 1.5f, new Vector3(1f, 3f, 0f) },
                triangles = new[] { 0, 1, 2 }
            };
            var handler = new VertexSelectionHandler();
            var oldRadius = VertexSelectionHandler.ProportionalRadius;
            var oldFalloff = VertexSelectionHandler.ProportionalFalloffType;
            var oldMode = VertexSelectionHandler.CurrentTransformMode;
            var oldRest = SkinnedVertexHelper.StoreMovesInRestSpace;
            LatticeDeformer owner = null;
            try
            {
                if (restSpace)
                {
                    var bone = new GameObject("Bone").transform;
                    bone.SetParent(root.transform, false);
                    bone.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    mesh.bindposes = new[] { Matrix4x4.identity };
                    mesh.boneWeights = new[] {
                        new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                        new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                        new BoneWeight { boneIndex0 = 0, weight0 = 1f } };
                    var renderer = root.AddComponent<SkinnedMeshRenderer>();
                    renderer.sharedMesh = mesh;
                    renderer.bones = new[] { bone };
                    renderer.rootBone = bone;
                }
                else
                {
                    root.AddComponent<MeshRenderer>();
                    root.AddComponent<MeshFilter>().sharedMesh = mesh;
                }
                if (scaled)
                {
                    root.transform.position = new Vector3(5f, 6f, 7f);
                    root.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                    root.transform.localScale = new Vector3(2f, 3f, 4f);
                }
                owner = root.AddComponent<LatticeDeformer>();
                owner.Reset();
                owner.ActiveLayerIndex = owner.AddLayer("Brush", MeshDeformerLayerType.Brush);
                owner.EnsureDisplacementCapacity();
                handler.Activate(owner);
                handler.RebuildCacheIfNeeded(mesh, owner);
                VertexSelectionHandler.ClearSelection();
                ((HashSet<int>)typeof(VertexSelectionHandler).GetField("s_selectedVertices",
                    BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Add(0);
                VertexSelectionHandler.ProportionalRadius = scaled ? 2f : 1f;
                VertexSelectionHandler.ProportionalFalloffType = VertexSelectionHandler.FalloffType.Linear;
                VertexSelectionHandler.CurrentTransformMode =
                    (VertexSelectionHandler.TransformMode)Enum.Parse(typeof(VertexSelectionHandler.TransformMode), operation);
                SkinnedVertexHelper.StoreMovesInRestSpace = restSpace;
                Vector3 pivot = scaled ? root.transform.position : restSpace ? Vector3.zero : Vector3.up * 2f;
                if (!scaled && !restSpace)
                {
                    // A borrowed posed snapshot takes precedence over local vertices.
                    typeof(VertexSelectionHandler).GetField("_worldPositions", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(handler, new[] { new Vector3(1f, 2f, 0f), new Vector3(1.5f, 2f, 0f), new Vector3(1f, 5f, 0f) });
                }
                Invoke(handler, "BeginTransform", owner);
                if (operation == "Move") Invoke(handler, "ApplyMoveDelta", owner, new Vector3(.25f, .5f, .75f));
                else if (operation == "Rotate") Invoke(handler, "ApplyRotationDelta", owner, root.transform, pivot, Quaternion.Euler(0f, 0f, 90f));
                else Invoke(handler, "ApplyScaleDelta", owner, root.transform, pivot, new Vector3(2f, .5f, 1f));
                Invoke(handler, "EndTransform");
                float diagonal = Mathf.Sqrt(.5f);
                Vector3 selected, proportional;
                if (operation == "Move")
                {
                    selected = restSpace ? new Vector3(.5f, -.25f, .75f) : new Vector3(.25f, .5f, .75f);
                    proportional = selected * .5f;
                }
                else if (operation == "Scale")
                {
                    selected = restSpace ? Vector3.left * .5f : Vector3.right;
                    proportional = restSpace ? Vector3.left * .375f : Vector3.right * .75f;
                }
                else
                {
                    selected = scaled ? new Vector3(-1f, 2f / 3f, 0f) : new Vector3(-1f, 1f, 0f);
                    proportional = scaled ? new Vector3((3f * diagonal - 3f) / 2f, diagonal, 0f)
                        : new Vector3(1.5f * diagonal - 1.5f, 1.5f * diagonal, 0f);
                }
                Assert.That(Vector3.Distance(owner.GetDisplacement(0), selected), Is.LessThan(2e-6f));
                Assert.That(Vector3.Distance(owner.GetDisplacement(1), proportional), Is.LessThan(2e-6f));
                Assert.That(owner.GetDisplacement(2), Is.EqualTo(Vector3.zero));
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.That(owner.Displacements, Is.All.EqualTo(Vector3.zero));
            }
            finally
            {
                handler.Deactivate();
                VertexSelectionHandler.ClearSelection();
                VertexSelectionHandler.ProportionalRadius = oldRadius;
                VertexSelectionHandler.ProportionalFalloffType = oldFalloff;
                VertexSelectionHandler.CurrentTransformMode = oldMode;
                SkinnedVertexHelper.StoreMovesInRestSpace = oldRest;
                if (owner != null) Undo.ClearUndo(owner);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
            }
        }

        private static void Invoke(VertexSelectionHandler handler, string name, params object[] arguments) =>
            typeof(VertexSelectionHandler).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(handler, arguments);
    }
}
#endif
