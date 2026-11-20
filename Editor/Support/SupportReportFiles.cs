#if UNITY_EDITOR
using System;
using System.IO;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class SupportReportFiles
    {
        internal static void Write(string path, byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            string destination = Path.GetFullPath(path);
            // On Unix a writable parent directory permits replacing a read-only
            // file (and CI may run as root). Preserve the user's read-only flag
            // explicitly instead of relying on the platform's rename behavior.
            if (File.Exists(destination) &&
                (File.GetAttributes(destination) & FileAttributes.ReadOnly) != 0)
                throw new UnauthorizedAccessException("The report destination is read-only: " + destination);
            string temporary = Path.Combine(Path.GetDirectoryName(destination),
                "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
#endif
