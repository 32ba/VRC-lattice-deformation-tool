using System;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    // Editor installs its platform adapter after each assembly load. Player code
    // keeps the readable-Mesh fallback and never links to an Editor assembly.
    internal static class DeformerPlatformServices
    {
        internal static Func<Mesh, Mesh.MeshDataArray> EditorMeshDataReader;
        internal static Action<UnityEngine.Object> RecordLegacyMigration;
        internal static Func<UnityEngine.Object, Action> CaptureLegacyMigrationRecordRollback;

        internal static bool CanReadMesh(Mesh mesh) =>
            mesh != null && (mesh.isReadable || EditorMeshDataReader != null);

        internal static Mesh.MeshDataArray AcquireReadOnlyMeshData(Mesh mesh) =>
            EditorMeshDataReader != null ? EditorMeshDataReader(mesh) : Mesh.AcquireReadOnlyMeshData(mesh);
    }
}
