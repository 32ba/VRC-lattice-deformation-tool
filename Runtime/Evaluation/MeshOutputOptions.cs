namespace Net._32Ba.LatticeDeformationTool
{
    internal readonly struct MeshOutputOptions
    {
        internal readonly bool RecalculateNormals, RecalculateTangents, RecalculateBounds;
        internal readonly NormalsRecalculationMode NormalsMode;
        internal readonly bool LegacyPublishedBlendShapes;

        internal MeshOutputOptions(bool normals, bool tangents, bool bounds,
            NormalsRecalculationMode normalsMode, bool legacyPublishedBlendShapes)
        {
            RecalculateNormals = normals;
            RecalculateTangents = tangents;
            RecalculateBounds = bounds;
            NormalsMode = normalsMode;
            LegacyPublishedBlendShapes = legacyPublishedBlendShapes;
        }
    }
}
