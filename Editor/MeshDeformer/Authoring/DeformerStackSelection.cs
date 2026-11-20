#if UNITY_EDITOR
namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class DeformerStackSelection
    {
        internal static bool SelectGroup(LatticeDeformer owner, int index, string undoLabel)
        {
            if (!DeformerAuthoringSource.TryRead(owner, out var raw, out _, out _) ||
                raw.EmbeddedGroups == null || index < 0 || index >= raw.EmbeddedGroups.Count ||
                index == raw.EmbeddedActiveGroupIndex) return false;
            return DeformerEditService.Execute(owner, undoLabel, target =>
            {
                target.ActiveGroupIndex = index;
                return true;
            });
        }

        internal static bool SelectLayer(LatticeDeformer owner, int index, string undoLabel)
        {
            if (!DeformerAuthoringSource.TryRead(owner, out var raw, out _, out _) ||
                raw.ActiveLayers == null || index < 0 || index >= raw.ActiveLayers.Count ||
                index == raw.ActiveLayerIndex) return false;
            return DeformerEditService.Execute(owner, undoLabel, target =>
            {
                target.ActiveLayerIndex = index;
                return true;
            });
        }
    }
}
#endif
