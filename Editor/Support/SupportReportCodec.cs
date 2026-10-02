#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class SupportReportCodec
    {
        internal const int FormatVersion = 1;
        internal const int MaximumAttachmentBytes = 8 * 1024 * 1024;
        private const int MaximumDecodedBytes = 2 * 1024 * 1024;
        private const int ImageWidth = 256;
        private const byte CodecJsonGzip = 1;
        private static readonly byte[] s_envelopeMagic = { 0x4C, 0x44, 0x54, 0x44, 0x42, 0x47 };
        internal static byte[] GeneratePng(string plainText)
        {
            byte[] envelope = GenerateEnvelope(plainText);
            int byteCount = sizeof(int) + envelope.Length;
            int pixelCount = (byteCount + 2) / 3;
            int height = Math.Max(ImageWidth, (pixelCount + ImageWidth - 1) / ImageWidth);
            var pixels = new Color32[ImageWidth * height];
            var packed = new byte[pixels.Length * 3];
            Buffer.BlockCopy(BitConverter.GetBytes(envelope.Length), 0, packed, 0, sizeof(int));
            Buffer.BlockCopy(envelope, 0, packed, sizeof(int), envelope.Length);
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(packed[i * 3], packed[i * 3 + 1], packed[i * 3 + 2], 255);
            var texture = new Texture2D(ImageWidth, height, TextureFormat.RGB24, false, true);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                byte[] png = texture.EncodeToPNG();
                if (png == null || png.Length == 0 || png.Length > MaximumAttachmentBytes)
                    throw new InvalidDataException("The support image exceeds the 8 MB attachment limit.");
                return png;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        internal static string DecodePng(byte[] png)
        {
            if (png == null || png.Length == 0 || png.Length > MaximumAttachmentBytes)
                throw new InvalidDataException("The support image is empty or exceeds the attachment limit.");
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false, true);
            try
            {
                if (!ImageConversion.LoadImage(texture, png, false))
                    throw new InvalidDataException("The support image could not be decoded.");
                Color32[] pixels = texture.GetPixels32();
                var packed = new byte[pixels.Length * 3];
                for (int i = 0; i < pixels.Length; i++)
                { packed[i * 3] = pixels[i].r; packed[i * 3 + 1] = pixels[i].g; packed[i * 3 + 2] = pixels[i].b; }
                if (packed.Length < sizeof(int)) throw new InvalidDataException("The support image is incomplete.");
                int length = BitConverter.ToInt32(packed, 0);
                if (length <= 0 || length > packed.Length - sizeof(int))
                    throw new InvalidDataException("The support image payload length is invalid.");
                var envelope = new byte[length];
                Buffer.BlockCopy(packed, sizeof(int), envelope, 0, length);
                return DecodeEnvelope(envelope);
            }
            catch (InvalidDataException) { throw; }
            catch (Exception exception)
            {
                throw new InvalidDataException("The support image is damaged or unsupported.", exception);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        internal static byte[] GenerateEnvelope(string plainText)
        {
            string json = ConvertPlainTextToJson(plainText);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            if (jsonBytes.Length > MaximumDecodedBytes)
                throw new InvalidDataException("The support report is too large.");
            byte[] compressed = Compress(jsonBytes);
            byte[] checksum = ComputeChecksum(compressed);
            using var envelope = new MemoryStream(
                s_envelopeMagic.Length + 2 + checksum.Length + compressed.Length);
            envelope.Write(s_envelopeMagic, 0, s_envelopeMagic.Length);
            envelope.WriteByte(FormatVersion);
            envelope.WriteByte(CodecJsonGzip);
            envelope.Write(checksum, 0, checksum.Length);
            envelope.Write(compressed, 0, compressed.Length);
            return envelope.ToArray();
        }

        internal static string Decode(string encodedReport)
        {
            if (string.IsNullOrEmpty(encodedReport))
                throw new FormatException("The Mesh Deformer support report is empty.");

            return DecodeEnvelope(FromBase64Url(encodedReport));
        }

        private static string DecodeEnvelope(byte[] envelope)
        {
            int headerLength = s_envelopeMagic.Length + 2 + 8;
            if (envelope.Length <= headerLength)
                throw new FormatException("The Mesh Deformer support report is incomplete.");
            for (int i = 0; i < s_envelopeMagic.Length; i++)
            {
                if (envelope[i] != s_envelopeMagic[i])
                    throw new FormatException("Unsupported Mesh Deformer support report envelope.");
            }
            if (envelope[s_envelopeMagic.Length] != FormatVersion ||
                envelope[s_envelopeMagic.Length + 1] != CodecJsonGzip)
            {
                throw new FormatException("Unsupported Mesh Deformer support report format.");
            }

            int checksumOffset = s_envelopeMagic.Length + 2;
            int payloadOffset = checksumOffset + 8;
            var compressed = new byte[envelope.Length - payloadOffset];
            Buffer.BlockCopy(envelope, payloadOffset, compressed, 0, compressed.Length);
            byte[] actualChecksum = ComputeChecksum(compressed);
            bool checksumMatches = true;
            for (int i = 0; i < actualChecksum.Length; i++)
                checksumMatches &= envelope[checksumOffset + i] == actualChecksum[i];
            if (!checksumMatches)
                throw new InvalidDataException("The Mesh Deformer support report checksum does not match.");

            using var input = new MemoryStream(compressed, false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > MaximumDecodedBytes)
                    throw new InvalidDataException("The support report expands beyond the safety limit.");
                output.Write(buffer, 0, read);
            }
            return Encoding.UTF8.GetString(output.ToArray());
        }

        private static byte[] Compress(byte[] input)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(
                       output,
                       System.IO.Compression.CompressionLevel.Optimal,
                       true))
                gzip.Write(input, 0, input.Length);
            return output.ToArray();
        }

        private static string ToBase64Url(byte[] value) =>
            Convert.ToBase64String(value)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

        private static byte[] FromBase64Url(string value)
        {
            string base64 = value.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2:
                    base64 += "==";
                    break;
                case 3:
                    base64 += "=";
                    break;
                case 1:
                    throw new FormatException("The support report Base64URL payload is invalid.");
            }
            return Convert.FromBase64String(base64);
        }

        private static byte[] ComputeChecksum(byte[] value)
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(value);
            var checksum = new byte[8];
            Buffer.BlockCopy(hash, 0, checksum, 0, checksum.Length);
            return checksum;
        }

        private static string ConvertPlainTextToJson(string plainText)
        {
            string[] lines = plainText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var json = new StringBuilder(plainText.Length + 256);
            json.Append('{');
            bool firstRootProperty = true;
            if (lines.Length > 0 && !string.IsNullOrEmpty(lines[0]))
                AppendJsonProperty(json, ref firstRootProperty, "report", lines[0]);

            string currentSection = null;
            var sectionProperties = new List<KeyValuePair<string, string>>();
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0) continue;
                if (line.Length >= 2 && line[0] == '[' && line[line.Length - 1] == ']')
                {
                    FlushJsonSection(json, ref firstRootProperty, currentSection, sectionProperties);
                    currentSection = line.Substring(1, line.Length - 2);
                    sectionProperties.Clear();
                    continue;
                }

                int separator = line.IndexOf('=');
                string key = separator >= 0 ? line.Substring(0, separator) : "unparsed";
                string value = separator >= 0 ? line.Substring(separator + 1) : line;
                if (currentSection == null)
                    AppendJsonProperty(json, ref firstRootProperty, key, value);
                else
                    sectionProperties.Add(new KeyValuePair<string, string>(key, value));
            }
            FlushJsonSection(json, ref firstRootProperty, currentSection, sectionProperties);
            json.Append('}');
            return json.ToString();
        }

        private static void FlushJsonSection(
            StringBuilder json,
            ref bool firstRootProperty,
            string section,
            IReadOnlyList<KeyValuePair<string, string>> properties)
        {
            if (section == null) return;
            if (!firstRootProperty) json.Append(',');
            firstRootProperty = false;
            AppendJsonString(json, section);
            json.Append(":{");
            bool firstSectionProperty = true;
            for (int i = 0; i < properties.Count; i++)
                AppendJsonProperty(
                    json,
                    ref firstSectionProperty,
                    properties[i].Key,
                    properties[i].Value);
            json.Append('}');
        }

        private static void AppendJsonProperty(
            StringBuilder json,
            ref bool firstProperty,
            string key,
            string value)
        {
            if (!firstProperty) json.Append(',');
            firstProperty = false;
            AppendJsonString(json, key);
            json.Append(':');
            AppendJsonString(json, value);
        }

        private static void AppendJsonString(StringBuilder json, string value)
        {
            json.Append('"');
            if (value != null)
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char character = value[i];
                    switch (character)
                    {
                        case '"': json.Append("\\\""); break;
                        case '\\': json.Append("\\\\"); break;
                        case '\b': json.Append("\\b"); break;
                        case '\f': json.Append("\\f"); break;
                        case '\n': json.Append("\\n"); break;
                        case '\r': json.Append("\\r"); break;
                        case '\t': json.Append("\\t"); break;
                        default:
                            if (character < 0x20)
                                json.Append("\\u").Append(((int)character).ToString("x4"));
                            else
                                json.Append(character);
                            break;
                    }
                }
            }
            json.Append('"');
        }

        internal static string Encode(string plainText) => ToBase64Url(GenerateEnvelope(plainText));
    }
}
#endif
