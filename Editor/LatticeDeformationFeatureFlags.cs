#if UNITY_EDITOR
namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Gates user-facing features that are present in the package but are not ready
    /// to be exposed in the next public release.
    ///
    /// Add LATTICE_DEFORMATION_TOOL_ENABLE_NEXT_RELEASE_FEATURES to Unity's
    /// Scripting Define Symbols to expose these entry points during development.
    /// Runtime data and behavior remain available regardless of this flag so that
    /// already-authored assets continue to deserialize and build safely.
    /// </summary>
    internal static class LatticeDeformationFeatureFlags
    {
#if LATTICE_DEFORMATION_TOOL_ENABLE_NEXT_RELEASE_FEATURES
        internal static bool NextReleaseFeatures => true;
#else
        internal static bool NextReleaseFeatures => false;
#endif

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
