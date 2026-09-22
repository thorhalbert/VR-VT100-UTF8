using System;
using System.Text;
using System.Text.Json;

namespace libVT100.Oob
{
    /// <summary>
    /// Represents a decoded and reassembled Out-of-Band (OOB) packet.
    /// </summary>
    public sealed class OobPacket
    {
        public OobHeader Header { get; }
        public byte[] Payload { get; }

        public OobPacket(OobHeader header, byte[] payload)
        {
            Header = header ?? throw new ArgumentNullException(nameof(header));
            Payload = payload ?? Array.Empty<byte>();
        }

        /// <summary>
        /// Decodes the payload as a UTF-8 text string.
        /// </summary>
        public string GetPayloadAsText()
        {
            if (Payload.Length == 0) return string.Empty;
            return Encoding.UTF8.GetString(Payload);
        }

        /// <summary>
        /// Deserializes the payload as a JSON object.
        /// </summary>
        public T? GetPayloadAsJson<T>(JsonSerializerOptions? options = null)
        {
            if (Payload.Length == 0) return default;
            return JsonSerializer.Deserialize<T>(Payload, options);
        }
    }
}
