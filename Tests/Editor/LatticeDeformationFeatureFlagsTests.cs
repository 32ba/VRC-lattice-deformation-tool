#if UNITY_EDITOR
using NUnit.Framework;

namespace Net._32Ba.LatticeDeformationTool.Editor.Tests
{
    public sealed class LatticeDeformationFeatureFlagsTests
    {
        [Test]
        public void NextReleaseFeatureFlags_AreConsistentWithMasterFlag()
        {
            bool expected = LatticeDeformationFeatureFlags.NextReleaseFeatures;

            Assert.That(LatticeDeformationFeatureFlags.DeformerProfiles, Is.EqualTo(expected));
            Assert.That(LatticeDeformationFeatureFlags.AdvancedBlendShapes, Is.EqualTo(expected));
            Assert.That(LatticeDeformationFeatureFlags.ClearanceTools, Is.EqualTo(expected));
            Assert.That(LatticeDeformationFeatureFlags.RestSpaceEditing, Is.EqualTo(expected));
            Assert.That(LatticeDeformationFeatureFlags.VertexMaskEditing, Is.EqualTo(expected));
            Assert.That(LatticeDeformationFeatureFlags.SymmetricVertexSelection, Is.EqualTo(expected));
            Assert.That(LatticeDeformationFeatureFlags.ValidationDiagnostics, Is.EqualTo(expected));
        }

#if !LATTICE_DEFORMATION_TOOL_ENABLE_NEXT_RELEASE_FEATURES
        [Test]
        public void NextReleaseFeatures_AreDisabledByDefault()
        {
            Assert.That(LatticeDeformationFeatureFlags.NextReleaseFeatures, Is.False);
        }
#endif
    }
}
#endif
