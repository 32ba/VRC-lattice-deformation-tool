using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    // Session-only cache identity, never persisted as compatibility metadata.
    // Large vertex arrays are streamed; all other serialized fields retain Unity's
    // JSON representation, including curves and Unity object references.
    internal static class ProfileContentFingerprint
    {
        internal static string Capture(IReadOnlyList<DeformerGroup> groups, int activeGroupIndex)
        {
            using var stream = new CompatibilityHashStream();
            using var writer = new BinaryWriter(stream);
            var metadata = new List<DeformerGroup>(groups?.Count ?? 0);
            writer.Write(groups?.Count ?? -1);
            if (groups != null)
                for (int i = 0; i < groups.Count; i++)
                {
                    writer.Write(groups[i] != null);
                    metadata.Add(groups[i]?.CreateFingerprintMetadata(writer));
                }
            writer.Write(JsonUtility.ToJson(DeformerProfilePayload.From(metadata, activeGroupIndex)));
            writer.Flush();
            return Convert.ToBase64String(stream.Finish());
        }

        internal static void Write(BinaryWriter writer, Vector3[] values)
        {
            writer.Write(values?.Length ?? -1);
            if (values == null) return;
            for (int i = 0; i < values.Length; i++)
            {
                writer.Write(BitConverter.SingleToInt32Bits(values[i].x));
                writer.Write(BitConverter.SingleToInt32Bits(values[i].y));
                writer.Write(BitConverter.SingleToInt32Bits(values[i].z));
            }
        }

        internal static void Write(BinaryWriter writer, float[] values)
        {
            writer.Write(values?.Length ?? -1);
            if (values == null) return;
            for (int i = 0; i < values.Length; i++)
                writer.Write(BitConverter.SingleToInt32Bits(values[i]));
        }
    }
}
