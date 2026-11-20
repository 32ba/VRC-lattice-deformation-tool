namespace Net._32Ba.LatticeDeformationTool
{
    public enum MeshDeformerLayerType
    {
        Lattice = 0,
        Brush = 1
    }

    public enum BlendShapeOutputMode
    {
        Disabled = 0,
        OutputAsBlendShape = 1
    }

    public enum NormalsRecalculationMode
    {
        LegacyUnityRecalculate = 0,
        PreserveSourceSmoothing = 1
    }

    public enum ClearanceHeatmapDisplayMode
    {
        PenetrationOnly = 0,
        WarningAndPenetration = 1,
        FullDistribution = 2
    }

    public enum ClearanceQueryMode
    {
        ReferenceNormal = 0,
        ClosedMesh = 1
    }

    public enum FitCorrectionScope
    {
        PenetrationOnly = 0,
        WarningThreshold = 1,
        TargetClearance = 2
    }

    public enum BlendShapeCompositionMode
    {
        Single = 0,
        Progressive = 1,
        Crossfade = 2
    }

    /// <summary>
    /// Published deformation-data schemas in release order. Every value is retained in
    /// the migration dispatcher even when that release did not change serialized data,
    /// so an upgrade can be audited and resumed one published release at a time.
    /// </summary>
    public enum DeformationDataVersion
    {
        Unversioned = 0,
        V0_0_1 = 1,
        V0_0_2 = 2,
        V0_0_3 = 3,
        V0_0_4 = 4,
        V0_0_5 = 5,
        V0_0_6 = 6,
        V1_0_0 = 7,
        V1_0_1 = 8,
        V1_1_0 = 9,
        V1_2_0 = 10,
        V1_2_1 = 11,
        V1_3_0 = 12,
        V1_3_1 = 13,
        V1_4_0 = 14,
        CurrentDevelopment = 15
    }

    internal enum DeformationDataMigrationStatus
    {
        Uninitialized = 0,
        Ready = 1,
        InProgress = 2,
        PendingOwnerTransform = 3,
        InvalidData = 4,
        UnsupportedFutureVersion = 5
    }
}
