using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    internal static class DeformationModelCopy
    {
        internal static LatticeAsset CloneSettings(LatticeAsset source)
        {
            var cloned = new LatticeAsset();
            if (source == null)
            {
                cloned.EnsureInitialized();
                return cloned;
            }

            cloned.GridSize = source.GridSize;
            cloned.LocalBounds = source.LocalBounds;
            cloned.Interpolation = source.Interpolation;
            cloned.EnsureInitialized();

            int count = Mathf.Min(cloned.ControlPointCount, source.ControlPointCount);
            for (int i = 0; i < count; i++)
            {
                cloned.SetControlPointLocal(i, source.GetControlPointLocal(i));
            }

            cloned.CopyLegacySerializationStateFrom(source);

            return cloned;
        }

        internal static AnimationCurve CloneCurve(AnimationCurve source)
        {
            if (source == null)
            {
                return AnimationCurve.Linear(0f, 0f, 1f, 1f);
            }

            var clone = new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode
            };
            return clone;
        }
    }
}
