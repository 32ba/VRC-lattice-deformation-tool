using System;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    /// <summary>
    /// Identifies one observed state of a Unity object for cross-frame caches.
    /// Two valid tokens are equal only when the object identity, its Editor dirty
    /// count, the global change generation and (for meshes) a cheap structural
    /// signature all match. A default token is invalid and never equal to anything,
    /// so callers without the Editor adapter (players) must not cache.
    /// </summary>
    internal readonly struct ObjectChangeToken : IEquatable<ObjectChangeToken>
    {
        private readonly int _instanceId;
        private readonly int _dirtyCount;
        private readonly int _generation;
        private readonly int _structure;
        internal readonly bool IsValid;

        internal ObjectChangeToken(int instanceId, int dirtyCount, int generation, int structure)
        {
            _instanceId = instanceId;
            _dirtyCount = dirtyCount;
            _generation = generation;
            _structure = structure;
            IsValid = true;
        }

        public bool Equals(ObjectChangeToken other) =>
            IsValid && other.IsValid && _instanceId == other._instanceId && _dirtyCount == other._dirtyCount &&
            _generation == other._generation && _structure == other._structure;

        public override bool Equals(object obj) => obj is ObjectChangeToken other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(IsValid, _instanceId, _dirtyCount, _generation, _structure);
    }

    /// <summary>
    /// Shared change detection for caches that outlive one synchronous evaluation.
    /// The Editor adapter supplies EditorUtility.GetDirtyCount and advances
    /// <see cref="Generation"/> after every asset import/delete/move and every
    /// Undo/Redo, so reimports and restored states never match an older token.
    /// Dirty counts: SerializedObject.ApplyModifiedProperties and EditorUtility.SetDirty
    /// advance them by contract. Native Mesh writes do too; this package already relies
    /// on that for in-place preview meshes (LatticeControlPointSkinning: "their vertices
    /// and dirty count change every frame") and observed that Mesh Instantiate dirties
    /// its source. Tokens are therefore captured after the cached work, never before.
    /// Mesh tokens also include counts, bounds and per-submesh index ranges so a
    /// structural change is seen even if a write did not dirty the object. Mutating
    /// public managed payload objects from script without SetDirty/Undo is not observed;
    /// such callers must mark the object dirty (as Unity requires for saving anyway).
    /// </summary>
    internal static class DeformerChangeTokens
    {
        private static int s_generation;

        internal static int Generation => s_generation;

        internal static bool IsAvailable => DeformerPlatformServices.DirtyCountReader != null;

        internal static void AdvanceGeneration()
        {
            unchecked { s_generation++; }
        }

        internal static ObjectChangeToken Capture(UnityEngine.Object target)
        {
            var reader = DeformerPlatformServices.DirtyCountReader;
            if (reader == null || target == null) return default;
            int structure = target is Mesh mesh ? HashMeshStructure(mesh) : 0;
            return new ObjectChangeToken(target.GetInstanceID(), reader(target), s_generation, structure);
        }

        private static int HashMeshStructure(Mesh mesh)
        {
            var hash = new HashCode();
            int subMeshCount = mesh.subMeshCount;
            hash.Add(mesh.vertexCount);
            hash.Add(subMeshCount);
            hash.Add(mesh.blendShapeCount);
            hash.Add(mesh.isReadable);
            hash.Add(mesh.bounds);
            for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
            {
                hash.Add(mesh.GetIndexStart(subMesh));
                hash.Add(mesh.GetIndexCount(subMesh));
                hash.Add(mesh.GetBaseVertex(subMesh));
                hash.Add((int)mesh.GetTopology(subMesh));
            }
            return hash.ToHashCode();
        }
    }
}
