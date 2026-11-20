#if UNITY_EDITOR
namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>Shipping compatibility marker. Nonshipping authoring code has been removed.</summary>
    internal static class LatticeDeformationFeatureFlags
    {
        internal static bool NextReleaseFeatures => false;

        internal static bool DeformerProfiles => NextReleaseFeatures;
        internal static bool AdvancedBlendShapes => NextReleaseFeatures;
        internal static bool ClearanceTools => NextReleaseFeatures;
        internal static bool RestSpaceEditing => NextReleaseFeatures;
        internal static bool VertexMaskEditing => NextReleaseFeatures;
        internal static bool SymmetricVertexSelection => NextReleaseFeatures;
        internal static bool ValidationDiagnostics => NextReleaseFeatures;
    }
}
#endif
