#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>Profile commands and compatibility queries, independent of Inspector drawing.</summary>
    internal static class ProfileAuthoringService
    {
        internal static ProfileCompatibilityStatus ReadCompatibility(LatticeDeformer deformer,
            MeshDeformerProfile profile)
        {
            if (deformer == null || profile == null) return ProfileCompatibilityStatus.InsufficientMetadata;
            Mesh source = deformer.CompatibilitySourceMesh;
            TryGetSourceAssetIdentity(source, out string guid, out long localId);
            return profile.EvaluateCompatibility(source, guid, localId);
        }

        internal static bool ChangeSource(LatticeDeformer deformer, DeformerDataSource source,
            MeshDeformerProfile profile, string undoLabel)
        {
            if (deformer == null || !deformer.HasValidSerializedAuthoringData ||
                (source != DeformerDataSource.Embedded && source != DeformerDataSource.Profile)) return false;
            if (source == DeformerDataSource.Profile && profile != null && !CanUse(deformer, profile)) return false;
            if (deformer.DataSource == source && deformer.Profile == profile) return true;
            return DeformerEditService.ExecuteDataSourceChange(deformer, undoLabel, target =>
            {
                if (source == DeformerDataSource.Profile && profile != null) return target.UseProfile(profile);
                target.DataSource = source;
                target.Profile = profile;
                return target.DataSource == source && target.Profile == profile;
            });
        }

        internal static bool CopyToEmbedded(LatticeDeformer deformer, string undoLabel)
        {
            if (deformer == null || deformer.Profile == null || !CanUse(deformer, deformer.Profile)) return false;
            return DeformerEditService.ExecuteDataSourceChange(deformer, undoLabel,
                target => target.CopyProfileToEmbedded());
        }

        internal static bool SaveCurrent(LatticeDeformer deformer, MeshDeformerProfile destination,
            string undoLabel)
        {
            if (destination == null || !CanWriteAsset(destination)) return false;
            var prepared = PrepareProfile(deformer, destination);
            if (prepared == null) return false;
            try
            {
                string path = AssetDatabase.GetAssetPath(destination);
                string absolutePath = string.IsNullOrEmpty(path) ? null : AbsoluteAssetPath(path);
                byte[] savedFile = absolutePath == null ? null : File.ReadAllBytes(absolutePath);
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(undoLabel);
                Undo.RegisterCompleteObjectUndo(destination, undoLabel);
                try
                {
                    EditorUtility.CopySerialized(prepared, destination);
                    EditorUtility.SetDirty(destination);
                    // Saving one Profile must not flush unrelated dirty assets.
                    AssetDatabase.SaveAssetIfDirty(destination);
                    Undo.CollapseUndoOperations(group);
                    return true;
                }
                catch
                {
                    Undo.RevertAllDownToGroup(group);
                    if (savedFile != null) File.WriteAllBytes(absolutePath, savedFile);
                    throw;
                }
            }
            finally { Object.DestroyImmediate(prepared); }
        }

        internal static bool CreateAsset(LatticeDeformer deformer, string path, string undoLabel,
            out MeshDeformerProfile profile)
        {
            profile = null;
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                Array.Exists(path.Split('/'), part => part == ".." || part == ".") || path.Contains("\\") ||
                !string.Equals(Path.GetExtension(path), ".asset", StringComparison.OrdinalIgnoreCase) ||
                !AssetDatabase.IsValidFolder(Path.GetDirectoryName(path)?.Replace('\\', '/')) ||
                File.Exists(AbsoluteAssetPath(path)) || AssetDatabase.LoadMainAssetAtPath(path) != null) return false;
            var prepared = PrepareProfile(deformer, null);
            if (prepared == null) return false;
            bool committed = false;
            try
            {
                prepared.name = Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(prepared, path);
                if (!AssetDatabase.Contains(prepared)) return false;
                AssetDatabase.SaveAssetIfDirty(prepared);
                if (!ChangeSource(deformer, DeformerDataSource.Profile, prepared, undoLabel)) return false;
                profile = prepared;
                committed = true;
                return true;
            }
            finally
            {
                if (!committed)
                {
                    // Only remove the new asset owned by this operation.
                    if (AssetDatabase.LoadMainAssetAtPath(path) == prepared) AssetDatabase.DeleteAsset(path);
                    if (prepared != null && !AssetDatabase.Contains(prepared)) Object.DestroyImmediate(prepared);
                }
            }
        }

        private static bool CanUse(LatticeDeformer deformer, MeshDeformerProfile profile)
        {
            Mesh source = deformer.CompatibilitySourceMesh;
            return DeformerDataResolver.HasValidProfilePayload(profile, source != null ? source.vertexCount : -1) &&
                   ReadCompatibility(deformer, profile) != ProfileCompatibilityStatus.TopologyMismatch;
        }

        private static MeshDeformerProfile PrepareProfile(LatticeDeformer deformer, MeshDeformerProfile destination)
        {
            if (deformer == null || !deformer.HasValidSerializedAuthoringData) return null;
            var stored = SerializedDeformerReader.Read(deformer);
            if (stored.UsesProfile && !CanUse(deformer, stored.Profile)) return null;
            var resolved = deformer.ReadResolvedData();
            if (resolved.Status != DeformerDataResolutionStatus.Embedded &&
                resolved.Status != DeformerDataResolutionStatus.Profile) return null;
            Mesh source = deformer.CompatibilitySourceMesh;
            if (stored.SourceMesh != null && !ReferenceEquals(source, stored.SourceMesh)) return null;
            if (source != null && ((stored.SourceVertexCount > 0 && stored.SourceVertexCount != source.vertexCount) ||
                (stored.SourceTopologyHash != 0 && stored.SourceTopologyHash != SourceMeshTopology.Calculate(source)))) return null;
            var prepared = destination != null ? Object.Instantiate(destination) : ScriptableObject.CreateInstance<MeshDeformerProfile>();
            bool success = false;
            try
            {
                if (destination != null) prepared.name = destination.name;
                // Use the owner's selected group, including a selection different
                // from the shared Profile default. Capture clones the borrowed data.
                prepared.Capture(resolved.Groups, stored.EmbeddedActiveGroupIndex, source);
                if (!DeformerDataResolver.HasValidProfilePayload(prepared, source != null ? source.vertexCount : -1)) return null;
                if (TryGetSourceAssetIdentity(source, out string guid, out long localId))
                    prepared.SetSourceAssetIdentity(guid, localId);
                success = true;
                return prepared;
            }
            finally { if (!success) Object.DestroyImmediate(prepared); }
        }

        private static bool CanWriteAsset(MeshDeformerProfile profile)
        {
            string path = AssetDatabase.GetAssetPath(profile);
            if (string.IsNullOrEmpty(path)) return !EditorUtility.IsPersistent(profile);
            return path.StartsWith("Assets/", StringComparison.Ordinal) && File.Exists(AbsoluteAssetPath(path)) &&
                   (File.GetAttributes(AbsoluteAssetPath(path)) & FileAttributes.ReadOnly) == 0 &&
                   AssetDatabase.IsOpenForEdit(path, StatusQueryOptions.UseCachedIfPossible);
        }

        private static string AbsoluteAssetPath(string path) => Path.Combine(Application.dataPath, path.Substring("Assets/".Length));

        private static bool TryGetSourceAssetIdentity(Mesh mesh, out string guid, out long localId)
        {
            guid = "";
            localId = 0;
            return mesh != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out guid, out localId) &&
                   !string.IsNullOrEmpty(guid);
        }
    }
}
#endif
