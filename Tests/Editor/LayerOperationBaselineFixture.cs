#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Net._32Ba.LatticeDeformationTool.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    // Shared input/operation driver. Expected results are exported by the fixed
    // baseline Editor, then checked in; replacement code never updates them.
    public static class LayerOperationBaselineFixture
    {
        public static readonly string[] Cases =
        {
            "delete-before-active", "delete-active-last", "delete-only-layer",
            "move-active-last", "duplicate-brush", "copy-paste-brush"
        };

        [Serializable] public sealed class Snapshot
        {
            public string operation;
            public int activeGroup;
            public int activeLayer;
            public string groupsJson;
            public bool hasOutput;
            public Vector3[] vertices;
            public Vector3[] sourceVertices;
        }

        [Serializable] public sealed class Document
        {
            public string baselineCommit;
            public Snapshot[] cases;
        }

        [Serializable] private sealed class GroupsPayload { public List<DeformerGroup> groups; }

        public static void Export()
        {
            var args = Environment.GetCommandLineArgs();
            int output = Array.IndexOf(args, "-latticeOperationOutput");
            int commit = Array.IndexOf(args, "-latticeBaselineCommit");
            if (output < 0 || commit < 0) throw new ArgumentException("Explicit output and baseline are required.");
            var snapshots = new List<Snapshot>();
            foreach (string operation in Cases) snapshots.Add(Run(operation));
            File.WriteAllText(args[output + 1], JsonUtility.ToJson(new Document
            {
                baselineCommit = args[commit + 1], cases = snapshots.ToArray()
            }, true) + "\n");
            Debug.Log($"Captured {snapshots.Count} baseline Inspector operation results.");
        }

        public static Snapshot Run(string operation)
        {
            var root = new GameObject("Layer operation baseline");
            var mesh = new Mesh { name = "Layer operation source" };
            UnityEditor.Editor editor = null;
            var clipboard = (typeof(LatticeDeformerEditor).Assembly.GetType("Net._32Ba.LatticeDeformationTool.Editor.DeformerStackInspectorSection") ?? typeof(LatticeDeformerEditor)).GetField("s_copiedLayerJson", BindingFlags.NonPublic | BindingFlags.Static);
            var previousClipboard = clipboard.GetValue(null);
            try
            {
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one };
                mesh.triangles = new[] { 0, 1, 2, 1, 3, 2 };
                mesh.RecalculateBounds();
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                root.AddComponent<MeshRenderer>();
                var d = root.AddComponent<LatticeDeformer>();
                d.Reset();
                if (operation != "delete-only-layer")
                {
                    d.AddLayer("Brush A", MeshDeformerLayerType.Brush);
                    d.Layers[1].SetBrushDisplacement(0, Vector3.up);
                    d.Layers[1].VertexMask = new[] { 0.5f, 1f, 1f, 1f };
                    d.AddLayer("Brush B", MeshDeformerLayerType.Brush);
                    d.Layers[2].SetBrushDisplacement(1, Vector3.forward);
                }
                d.ActiveLayerIndex = operation == "delete-before-active" ? 1 : d.Layers.Count - 1;
                editor = UnityEditor.Editor.CreateEditor(d);
                editor.CreateInspectorGUI();
                d.Deform(false);
                switch (operation)
                {
                    case "delete-before-active": Invoke(editor, "DeleteLayer", d, 0); break;
                    case "delete-active-last": Invoke(editor, "DeleteLayer", d, 2); break;
                    case "delete-only-layer": Invoke(editor, "DeleteLayer", d, 0); break;
                    case "move-active-last": Invoke(editor, "OnLayerReordered", 2, 0); break;
                    case "duplicate-brush":
                        Invoke(editor, "PerformSingleLayerOperation", d, "Duplicate Layer",
                            new Func<LatticeDeformer, bool>(target => target.DuplicateLayer(1) >= 0));
                        break;
                    case "copy-paste-brush":
                        Invoke(editor, "CopyLayer", d, 1);
                        Invoke(editor, "PasteLayer", d);
                        break;
                    default: throw new ArgumentOutOfRangeException(nameof(operation));
                }

                var outputMesh = d.Deform(false);
                using var serialized = new SerializedObject(d);
                int groupIndex = serialized.FindProperty("_activeGroupIndex").intValue;
                int layerIndex = serialized.FindProperty("_groups").GetArrayElementAtIndex(groupIndex)
                    .FindPropertyRelative("_activeLayerIndex").intValue;
                var groups = (List<DeformerGroup>)typeof(LatticeDeformer)
                    .GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(d);
                return new Snapshot
                {
                    operation = operation, activeGroup = groupIndex, activeLayer = layerIndex,
                    groupsJson = JsonUtility.ToJson(new GroupsPayload { groups = groups }),
                    hasOutput = outputMesh != null,
                    vertices = outputMesh != null ? outputMesh.vertices : Array.Empty<Vector3>(),
                    sourceVertices = mesh.vertices
                };
            }
            finally
            {
                clipboard.SetValue(null, previousClipboard);
                if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
                Undo.ClearAll();
            }
        }

        private static void Invoke(UnityEditor.Editor editor, string method, params object[] args) =>
            typeof(LatticeDeformerEditor).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(editor, args);
    }
}
#endif
