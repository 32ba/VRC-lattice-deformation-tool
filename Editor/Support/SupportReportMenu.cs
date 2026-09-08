#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class SupportReportMenu
    {
        [MenuItem("Tools/Lattice Deformation Tool/Decode Support Information Image...")]
        private static void DecodeSupportImageFromMenu()
        {
            string imagePath = EditorUtility.OpenFilePanel("Open Support Information Image", "", "png");
            if (string.IsNullOrEmpty(imagePath)) return;
            try
            {
                string json = MeshDeformerSupportReport.DecodePng(File.ReadAllBytes(imagePath));
                string outputPath = EditorUtility.SaveFilePanel(
                    "Save Decoded Support Information", Path.GetDirectoryName(imagePath),
                    Path.GetFileNameWithoutExtension(imagePath) + ".json", "json");
                if (!string.IsNullOrEmpty(outputPath)) SupportReportFiles.Write(outputPath, new UTF8Encoding(false).GetBytes(json));
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Mesh Deformer", exception.Message, "OK");
            }
        }

    }
}
#endif
