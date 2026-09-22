using System;
using System.Collections.Generic;
using System.Text;

namespace libVT100.Oob
{
    /// <summary>
    /// Strongly-typed metadata header for Out-of-Band (OOB) APC packet envelopes.
    /// Envelope: \x1b_V<HeaderControls>;<PayloadChunk>\x1b\
    /// </summary>
    public sealed class OobHeader
    {
        public string Action { get; init; } = string.Empty;
        public string Id { get; init; } = string.Empty;
        public bool IsMore { get; init; } = false;
        public string PayloadType { get; init; } = string.Empty;
        public string Encoding { get; init; } = "b64";
        public int Status { get; init; } = 0;
        public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string? this[string key] => Parameters.TryGetValue(key, out var val) ? val : null;
        public bool TryGetParameter(string key, out string value) => Parameters.TryGetValue(key, out value!);

        public static bool TryParse(ReadOnlySpan<char> span, out OobHeader header, out string? error)
        {
            header = new OobHeader();
            error = null;

            string action = string.Empty, id = string.Empty, payloadType = string.Empty, encoding = "b64";
            bool isMore = false;
            int status = 0;
            var paramDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            while (!span.IsEmpty)
            {
                int commaIdx = span.IndexOf(',');
                ReadOnlySpan<char> token = commaIdx >= 0 ? span.Slice(0, commaIdx) : span;
                span = commaIdx >= 0 ? span.Slice(commaIdx + 1) : ReadOnlySpan<char>.Empty;

                token = token.Trim();
                if (token.IsEmpty) continue;

                int eqIdx = token.IndexOf('=');
                ReadOnlySpan<char> keySpan = eqIdx >= 0 ? token.Slice(0, eqIdx).Trim() : token;
                ReadOnlySpan<char> valSpan = eqIdx >= 0 ? token.Slice(eqIdx + 1).Trim() : ReadOnlySpan<char>.Empty;

                if (keySpan.IsEmpty) continue;

                string key = keySpan.ToString();
                string val = valSpan.ToString();
                paramDict[key] = val;

                if (keySpan.Equals("a", StringComparison.OrdinalIgnoreCase)) action = val;
                else if (keySpan.Equals("i", StringComparison.OrdinalIgnoreCase)) id = val;
                else if (keySpan.Equals("m", StringComparison.OrdinalIgnoreCase)) isMore = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
                else if (keySpan.Equals("t", StringComparison.OrdinalIgnoreCase)) payloadType = val;
                else if (keySpan.Equals("e", StringComparison.OrdinalIgnoreCase)) encoding = val.ToLowerInvariant();
                else if (keySpan.Equals("s", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, out int st)) status = st;
            }

            header = new OobHeader
            {
                Action = action,
                Id = id,
                IsMore = isMore,
                PayloadType = payloadType,
                Encoding = string.IsNullOrEmpty(encoding) ? "b64" : encoding,
                Status = status,
                Parameters = paramDict
            };
            return true;
        }

        public string ToHeaderString()
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(Action)) sb.Append("a=").Append(Action);

            if (!string.IsNullOrEmpty(Id))
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append("i=").Append(Id);
            }
            if (IsMore)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append("m=1");
            }
            if (!string.IsNullOrEmpty(PayloadType))
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append("t=").Append(PayloadType);
            }
            if (!string.IsNullOrEmpty(Encoding) && !Encoding.Equals("b64", StringComparison.OrdinalIgnoreCase))
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append("e=").Append(Encoding);
            }
            if (Status != 0)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append("s=").Append(Status);
            }
            foreach (var kvp in Parameters)
            {
                string k = kvp.Key;
                if (k.Equals("a", StringComparison.OrdinalIgnoreCase) || k.Equals("i", StringComparison.OrdinalIgnoreCase) ||
                    k.Equals("m", StringComparison.OrdinalIgnoreCase) || k.Equals("t", StringComparison.OrdinalIgnoreCase) ||
                    k.Equals("e", StringComparison.OrdinalIgnoreCase) || k.Equals("s", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (sb.Length > 0) sb.Append(',');
                sb.Append(k).Append('=').Append(kvp.Value);
            }
            return sb.ToString();
        }
    }
}
