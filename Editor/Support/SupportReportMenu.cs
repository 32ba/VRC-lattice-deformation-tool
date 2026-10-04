#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class SupportReportMenu
    {
        internal static byte[] ReadImageFile(string path)
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return ReadImage(input);
        }

        internal static byte[] ReadImage(Stream input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            const int limit = SupportReportCodec.MaximumAttachmentBytes;
            if (input.CanSeek && input.Length > limit)
                throw new InvalidDataException("The support image exceeds the attachment limit.");
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                // Read at most one byte beyond the limit, even if the file grows
                // after opening or the input cannot report its length.
                int count = input.Read(buffer, 0, Math.Min(buffer.Length, limit - (int)bytes.Length + 1));
                if (count == 0) return bytes.ToArray();
                if (bytes.Length + count > limit)
                    throw new InvalidDataException("The support image exceeds the attachment limit.");
                bytes.Write(buffer, 0, count);
            }
        }

        [MenuItem("Tools/Lattice Deformation Tool/Decode Support Information Image...")]
        private static void DecodeSupportImageFromMenu()
        {
            string imagePath = EditorUtility.OpenFilePanel("Open Support Information Image", "", "png");
            if (string.IsNullOrEmpty(imagePath)) return;
            try
            {
                string json = MeshDeformerSupportReport.DecodePng(ReadImageFile(imagePath));
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
