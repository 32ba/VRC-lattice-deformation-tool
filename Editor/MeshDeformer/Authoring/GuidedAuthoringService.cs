#if UNITY_EDITOR
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal enum GuidedEditingIntent { AdjustShape, SculptSurface, MoveVertices }

    /// <summary>Display values only. No initializing getters or retained payload references.</summary>
    internal readonly struct GuidedInspectorState
    {
        internal readonly Mesh Source;
        internal readonly string ActiveLayerName;
        internal readonly bool IsProfile;

        private GuidedInspectorState(Mesh source, string activeLayerName, bool isProfile)
        { Source = source; ActiveLayerName = activeLayerName; IsProfile = isProfile; }

        internal static GuidedInspectorState Read(LatticeDeformer owner)
        {
            if (owner == null) return default;
            var data = SerializedDeformerReader.Read(owner);
            // Profile mode is read-only even when its asset reference is missing.
            bool profile = data.DataSource == DeformerDataSource.Profile;
            string name = null;
            if (!profile && data.ActiveLayers != null && data.ActiveLayerIndex >= 0 &&
                data.ActiveLayerIndex < data.ActiveLayers.Count)
                name = data.ActiveLayers[data.ActiveLayerIndex]?.Name;
            return new GuidedInspectorState(owner.SourceMesh, name, profile);
        }
    }

    internal static class GuidedAuthoringService
    {
        internal static int EnsureLayer(LatticeDeformer owner, MeshDeformerLayerType type, string undoLabel)
        {
            if ((type != MeshDeformerLayerType.Lattice && type != MeshDeformerLayerType.Brush) ||
                !DeformerAuthoringSource.TryRead(owner, out var data, out _, out _) ||
                data.ActiveLayers == null) return -1;

            var layers = data.ActiveLayers;
            int selected = data.ActiveLayerIndex;
            if (selected >= 0 && selected < layers.Count && layers[selected]?.Type == type) return selected;
            int matching = -1;
            for (int i = 0; i < layers.Count; i++)
                if (layers[i]?.Type == type) { matching = i; break; }

            int result = -1;
            bool changed = DeformerEditService.Execute(owner, undoLabel, target =>
            {
                if (matching >= 0) { target.ActiveLayerIndex = matching; result = matching; }
                else result = target.AddLayer(layerType: type);
                return result >= 0;
            });
            return changed ? result : -1;
        }
    }
}
#endif
