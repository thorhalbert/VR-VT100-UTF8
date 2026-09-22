using System;
using System.Collections.Generic;
using System.Text;

namespace libVT100.Oob
{
    /// <summary>
    /// Defragments multi-part OOB chunk streams ('m=1' -> 'm=0') and decodes final payloads.
    /// Enforces maximum payload limits and sliding timeouts to prevent resource exhaustion.
    /// </summary>
    public sealed class OobChunkReassembler
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, InFlightAssembly> _assemblies = new(StringComparer.OrdinalIgnoreCase);
        private readonly long _maxPayloadSize;
        private readonly TimeSpan _timeout;

        public long MaxPayloadSize => _maxPayloadSize;
        public TimeSpan Timeout => _timeout;
        public int ActiveAssembliesCount { get { lock (_lock) return _assemblies.Count; } }

        public OobChunkReassembler(long maxPayloadSize = 64 * 1024 * 1024, TimeSpan? timeout = null)
        {
            _maxPayloadSize = maxPayloadSize;
            _timeout = timeout ?? TimeSpan.FromSeconds(5);
        }

        public bool ProcessChunk(OobHeader header, string payloadChunk, out OobPacket? completePacket, out string? error)
        {
            completePacket = null;
            error = null;

            lock (_lock)
            {
                CleanupExpiredInternal(DateTime.UtcNow);
                string key = GetAssemblyKey(header);

                if (header.IsMore)
                {
                    if (!_assemblies.TryGetValue(key, out var assembly))
                    {
                        assembly = new InFlightAssembly(key, header);
                        _assemblies[key] = assembly;
                    }

                    if (assembly.TotalLength + payloadChunk.Length > _maxPayloadSize)
                    {
                        _assemblies.Remove(key);
                        error = $"Maximum payload size ({_maxPayloadSize} bytes) exceeded for key '{key}'.";
                        return false;
                    }

                    assembly.Append(payloadChunk);
                    assembly.LastActivityUtc = DateTime.UtcNow;
                    return true;
                }
                else
                {
                    string fullPayloadString;
                    if (_assemblies.TryGetValue(key, out var assembly))
                    {
                        if (assembly.TotalLength + payloadChunk.Length > _maxPayloadSize)
                        {
                            _assemblies.Remove(key);
                            error = $"Maximum payload size ({_maxPayloadSize} bytes) exceeded for key '{key}'.";
                            return false;
                        }
                        assembly.Append(payloadChunk);
                        fullPayloadString = assembly.GetCombinedString();
                        _assemblies.Remove(key);
                    }
                    else
                    {
                        fullPayloadString = payloadChunk;
                    }

                    try
                    {
                        byte[] decodedBytes = DecodePayload(fullPayloadString, header);
                        completePacket = new OobPacket(header, decodedBytes);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        error = $"Failed to decode OOB payload: {ex.Message}";
                        return false;
                    }
                }
            }
        }

        public void CleanupExpired()
        {
            lock (_lock) CleanupExpiredInternal(DateTime.UtcNow);
        }

        private void CleanupExpiredInternal(DateTime nowUtc)
        {
            var expiredKeys = new List<string>();
            foreach (var kvp in _assemblies)
            {
                if (nowUtc - kvp.Value.LastActivityUtc > _timeout) expiredKeys.Add(kvp.Key);
            }
            foreach (var key in expiredKeys) _assemblies.Remove(key);
        }

        private static string GetAssemblyKey(OobHeader header)
        {
            if (header.TryGetParameter("sender", out var sender) && !string.IsNullOrEmpty(sender))
                return $"{sender}:{header.Id}";
            return string.IsNullOrEmpty(header.Id) ? "_unkeyed_stream_" : header.Id;
        }

        private static byte[] DecodePayload(string payloadString, OobHeader header)
        {
            if (string.IsNullOrEmpty(payloadString)) return Array.Empty<byte>();

            string enc = header.Encoding.ToLowerInvariant();
            byte[] bytes = enc switch
            {
                "b85" => Z85.Decode(payloadString),
                "raw" => Encoding.UTF8.GetBytes(payloadString),
                _ => Convert.FromBase64String(payloadString)
            };

            if (header.TryGetParameter("pad", out var padStr) && int.TryParse(padStr, out int pad) && pad > 0 && pad < bytes.Length)
            {
                var trimmed = new byte[bytes.Length - pad];
                Array.Copy(bytes, trimmed, trimmed.Length);
                return trimmed;
            }
            return bytes;
        }

        private sealed class InFlightAssembly
        {
            public string Key { get; }
            public OobHeader InitialHeader { get; }
            private readonly StringBuilder _sb = new();
            public DateTime LastActivityUtc { get; set; }
            public long TotalLength => _sb.Length;

            public InFlightAssembly(string key, OobHeader initialHeader)
            {
                Key = key;
                InitialHeader = initialHeader;
                LastActivityUtc = DateTime.UtcNow;
            }

            public void Append(string chunk) => _sb.Append(chunk);
            public string GetCombinedString() => _sb.ToString();
        }
    }
}
