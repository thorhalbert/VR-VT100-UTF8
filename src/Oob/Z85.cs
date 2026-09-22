using System;
using System.Text;

namespace libVT100.Oob
{
    /// <summary>
    /// Implements ZeroMQ RFC 32 (Z85) Base-85 encoding and decoding.
    /// Operates on 4-byte binary blocks encoded into 5 printable ASCII characters.
    /// </summary>
    public static class Z85
    {
        private const string Encoder =
            "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ.-:+=^!/*?&<>()[]{}@%$#";

        private static readonly byte[] Decoder = new byte[96]
        {
            0x00, 0x44, 0x00, 0x54, 0x53, 0x52, 0x48, 0x00, 
            0x4B, 0x4C, 0x46, 0x41, 0x00, 0x3F, 0x3E, 0x45, 
            0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 
            0x08, 0x09, 0x40, 0x00, 0x49, 0x42, 0x4A, 0x47, 
            0x51, 0x24, 0x25, 0x26, 0x27, 0x28, 0x29, 0x2A, 
            0x2B, 0x2C, 0x2D, 0x2E, 0x2F, 0x30, 0x31, 0x32, 
            0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 
            0x3B, 0x3C, 0x3D, 0x4D, 0x00, 0x4E, 0x43, 0x00, 
            0x00, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 
            0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 
            0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20, 
            0x21, 0x22, 0x23, 0x4F, 0x00, 0x50, 0x00, 0x00
        };

        /// <summary>
        /// Encodes a binary buffer into a Z85 ASCII string.
        /// </summary>
        /// <param name="data">Binary data to encode. Length must be a multiple of 4.</param>
        /// <returns>Encoded Z85 string.</returns>
        public static string Encode(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty) return string.Empty;

            ReadOnlySpan<byte> toEncode = data;
            byte[]? padded = null;
            int rem = data.Length % 4;
            if (rem != 0)
            {
                int pad = 4 - rem;
                padded = new byte[data.Length + pad];
                data.CopyTo(padded);
                toEncode = padded;
            }

            int encodedSize = toEncode.Length * 5 / 4;
            char[] encoded = new char[encodedSize];

            int charIdx = 0;
            int byteIdx = 0;
            uint value = 0;

            while (byteIdx < toEncode.Length)
            {
                value = (value * 256) + toEncode[byteIdx++];
                if (byteIdx % 4 == 0)
                {
                    uint divisor = 85 * 85 * 85 * 85;
                    while (divisor > 0)
                    {
                        encoded[charIdx++] = Encoder[(int)((value / divisor) % 85)];
                        divisor /= 85;
                    }
                    value = 0;
                }
            }

            return new string(encoded);
        }

        public static byte[] Decode(ReadOnlySpan<char> text)
        {
            if (text.IsEmpty) return Array.Empty<byte>();

            text = text.Trim();
            if (text.IsEmpty) return Array.Empty<byte>();

            if (text.Length % 5 != 0)
            {
                Console.Error.WriteLine($"[libvt100:WARN] Z85 text length ({text.Length}) must be divisible by 5.");
                return Array.Empty<byte>();
            }

            int decodedSize = text.Length * 4 / 5;
            byte[] decoded = new byte[decodedSize];

            int byteIdx = 0;
            int charIdx = 0;
            uint value = 0;

            while (charIdx < text.Length)
            {
                char c = text[charIdx++];
                if (c < 32 || c >= 128)
                {
                    Console.Error.WriteLine($"[libvt100:WARN] Invalid character in Z85 string: '{(int)c}'");
                    return Array.Empty<byte>();
                }

                byte decodedChar = Decoder[c - 32];
                value = (value * 85) + decodedChar;

                if (charIdx % 5 == 0)
                {
                    uint divisor = 256 * 256 * 256;
                    while (divisor > 0)
                    {
                        decoded[byteIdx++] = (byte)((value / divisor) % 256);
                        divisor /= 256;
                    }
                    value = 0;
                }
            }

            return decoded;
        }
    }
}
