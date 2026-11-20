#if UNITY_EDITOR

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class MeshDeformerSupportReport
    {
        internal const int FormatVersion = SupportReportCodec.FormatVersion;
        internal const int MaximumAttachmentBytes = SupportReportCodec.MaximumAttachmentBytes;
        internal static string Generate(LatticeDeformer deformer) => SupportReportCodec.Encode(SupportReportCollector.Collect(deformer));
        internal static byte[] GeneratePng(LatticeDeformer deformer) => SupportReportCodec.GeneratePng(SupportReportCollector.Collect(deformer));
        internal static string Decode(string report) => SupportReportCodec.Decode(report);
        internal static string DecodePng(byte[] png) => SupportReportCodec.DecodePng(png);
    }
}
#endif
