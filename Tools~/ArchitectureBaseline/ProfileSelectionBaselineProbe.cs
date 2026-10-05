using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Net._32Ba.LatticeDeformationTool;
using UnityEngine;

public static class ProfileSelectionBaselineProbe
{
    [Serializable] private sealed class Result
    {
        public string baselineCommit = "c7f499c38e16f386fe6734e0f7937d50c502c529";
        public bool accepted;
        public int rawActive, publicActive, publicGroupCount;
        public bool producedMesh;
    }

    public static void Run()
    {
        var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
        var profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
        var root = new GameObject("Profile selection baseline");
        try
        {
            var groups = new List<DeformerGroup>();
            for (int i = 0; i < 2; i++)
            {
                var group = new DeformerGroup { Name = "Group " + i };
                group.LayersList.Add(new LatticeLayer());
                groups.Add(group);
            }
            profile.Capture(groups, 1, mesh);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>();
            var deformer = root.AddComponent<LatticeDeformer>();
            deformer.Reset();
            var result = new Result { accepted = deformer.UseProfile(profile) };
            result.rawActive = (int)typeof(LatticeDeformer).GetField("_activeGroupIndex", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(deformer);
            result.publicActive = deformer.ActiveGroupIndex;
            result.publicGroupCount = deformer.GroupCount;
            result.producedMesh = deformer.Deform(false) != null;
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                "profile-selection-baseline.json"), JsonUtility.ToJson(result, true));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(profile);
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }
}
