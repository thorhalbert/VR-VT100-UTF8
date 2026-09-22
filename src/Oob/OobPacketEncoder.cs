using System;
using System.Collections.Generic;
using System.Text;

namespace libVT100.Oob
{
    /// <summary>
    /// Encodes Out-of-Band (OOB) packets into ANSI APC escape sequences for upstream injection.
    /// Handles automatic chunking (m=1/m=0) and payload serialization (Base64/Z85).
    /// </summary>
    public static class OobPacketEncoder
    {
        public const int DefaultMaxChunkSize = 4096;

        public static List<byte[]> EncodePacket(
            string action,
            string? id = null,
            string? type = null,
            ReadOnlySpan<byte> payload = default,
            int maxChunkSize = DefaultMaxChunkSize,
            string encoding = "b64",
            int status = 0,
            IDictionary<string, string>? customHeaders = null)
        {
            if (maxChunkSize <= 0) maxChunkSize = DefaultMaxChunkSize;

            string enc = string.IsNullOrEmpty(encoding) ? "b64" : encoding.ToLowerInvariant();
            string fullPayloadStr;
            int pad = 0;

            if (payload.IsEmpty)
            {
                fullPayloadStr = string.Empty;
            }
            else if (enc == "b85")
            {
                int rem = payload.Length % 4;
                if (rem != 0)
                {
                    pad = 4 - rem;
                    byte[] padded = new byte[payload.Length + pad];
                    payload.CopyTo(padded);
                    fullPayloadStr = Z85.Encode(padded);
                }
                else
                {
                    fullPayloadStr = Z85.Encode(payload);
                }
            }
            else if (enc == "raw")
            {
                fullPayloadStr = Encoding.UTF8.GetString(payload);
            }
            else
            {
                // Default Base64
                fullPayloadStr = Convert.ToBase64String(payload);
            }

            var result = new List<byte[]>();

            // If empty or fits in one chunk
            if (fullPayloadStr.Length <= maxChunkSize)
            {
                var headerDict = customHeaders != null 
                    ? new Dictionary<string, string>(customHeaders, StringComparer.OrdinalIgnoreCase) 
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (pad > 0) headerDict["pad"] = pad.ToString();

                var header = new OobHeader
                {
                    Action = action,
                    Id = id ?? string.Empty,
                    IsMore = false,
                    PayloadType = type ?? string.Empty,
                    Encoding = enc,
                    Status = status,
                    Parameters = headerDict
                };

                result.Add(BuildApcEnvelope(header, fullPayloadStr));
                return result;
            }

            // Multi-chunk sequence
            int offset = 0;
            int total = fullPayloadStr.Length;

            while (offset < total)
            {
                int length = Math.Min(maxChunkSize, total - offset);
                string chunkSlice = fullPayloadStr.Substring(offset, length);
                offset += length;
                bool isMore = offset < total;

                var headerDict = customHeaders != null 
                    ? new Dictionary<string, string>(customHeaders, StringComparer.OrdinalIgnoreCase) 
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (pad > 0) headerDict["pad"] = pad.ToString();

                var header = new OobHeader
                {
                    Action = action,
                    Id = id ?? string.Empty,
                    IsMore = isMore,
                    PayloadType = type ?? string.Empty,
                    Encoding = enc,
                    Status = status,
                    Parameters = headerDict
                };

                result.Add(BuildApcEnvelope(header, chunkSlice));
            }

            return result;
        }

        public static void SendOobPacket(
            Action<byte[]> outputSink,
            string action,
            string? id = null,
            string? type = null,
            ReadOnlySpan<byte> payload = default,
            int maxChunkSize = DefaultMaxChunkSize,
            string encoding = "b64",
            int status = 0,
            IDictionary<string, string>? customHeaders = null)
        {
            if (outputSink == null) throw new ArgumentNullException(nameof(outputSink));

            var chunks = EncodePacket(action, id, type, payload, maxChunkSize, encoding, status, customHeaders);
            foreach (var chunk in chunks)
            {
                outputSink(chunk);
            }
        }

        private static byte[] BuildApcEnvelope(OobHeader header, string payloadChunk)
        {
            // \x1b_V<HeaderControls>;<PayloadChunk>\x1b\
            string headerStr = header.ToHeaderString();
            var sb = new StringBuilder(headerStr.Length + payloadChunk.Length + 8);
            sb.Append("\x1B_V");
            sb.Append(headerStr);
            sb.Append(';');
            sb.Append(payloadChunk);
            sb.Append("\x1B\\");

            return Encoding.ASCII.GetBytes(sb.ToString());
        }
    }
}
