using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace libVT100.TestBench.Harness
{
    public static class SequenceEscaper
    {
        /// <summary>
        /// Decodes a human-readable escape sequence string (e.g. "\e[2J\e[HHello\r\n") into raw UTF-8 bytes.
        /// Supports:
        ///   \e, \E -> 0x1B (ESC)
        ///   \x1b, \x1B, \xHH -> hex byte
        ///   \033 -> octal ESC
        ///   \r -> 0x0D (CR)
        ///   \n -> 0x0A (LF)
        ///   \t -> 0x09 (TAB)
        ///   \b -> 0x08 (BS)
        ///   \a -> 0x07 (BEL)
        ///   \\ -> \
        ///   ^[ -> 0x1B (caret notation for ESC)
        /// </summary>
        public static byte[] Unescape(string input)
        {
            if (string.IsNullOrEmpty(input))
                return Array.Empty<byte>();

            List<byte> bytes = new List<byte>(input.Length);

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (c == '^' && i + 1 < input.Length && input[i + 1] == '[')
                {
                    bytes.Add(0x1B);
                    i++;
                    continue;
                }

                if (c == '\\' && i + 1 < input.Length)
                {
                    char next = input[i + 1];

                    switch (next)
                    {
                        case 'e':
                        case 'E':
                            bytes.Add(0x1B);
                            i++;
                            continue;

                        case 'r':
                            bytes.Add(0x0D);
                            i++;
                            continue;

                        case 'n':
                            bytes.Add(0x0A);
                            i++;
                            continue;

                        case 't':
                            bytes.Add(0x09);
                            i++;
                            continue;

                        case 'b':
                            bytes.Add(0x08);
                            i++;
                            continue;

                        case 'a':
                            bytes.Add(0x07);
                            i++;
                            continue;

                        case '\\':
                            bytes.Add((byte)'\\');
                            i++;
                            continue;

                        case 'x':
                        case 'X':
                            // Hex byte \x1b or \xHH
                            if (i + 3 < input.Length &&
                                byte.TryParse(input.AsSpan(i + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte hexVal))
                            {
                                bytes.Add(hexVal);
                                i += 3;
                                continue;
                            }
                            break;

                        case '0':
                            // Octal \033
                            if (i + 3 < input.Length && input[i + 2] == '3' && input[i + 3] == '3')
                            {
                                bytes.Add(0x1B);
                                i += 3;
                                continue;
                            }
                            break;
                    }
                }

                // Normal character encoded as UTF-8
                byte[] charBytes = Encoding.UTF8.GetBytes(new char[] { c });
                bytes.AddRange(charBytes);
            }

            return bytes.ToArray();
        }

        /// <summary>
        /// Encodes a byte array back into a readable representation, showing control codes as \e, \r, \n, etc.
        /// </summary>
        public static string Escape(byte[] data)
        {
            if (data == null || data.Length == 0)
                return string.Empty;

            StringBuilder sb = new StringBuilder();
            foreach (byte b in data)
            {
                switch (b)
                {
                    case 0x1B:
                        sb.Append("\\e");
                        break;
                    case 0x0D:
                        sb.Append("\\r");
                        break;
                    case 0x0A:
                        sb.Append("\\n");
                        break;
                    case 0x09:
                        sb.Append("\\t");
                        break;
                    case 0x08:
                        sb.Append("\\b");
                        break;
                    case 0x07:
                        sb.Append("\\a");
                        break;
                    default:
                        if (b >= 32 && b < 127)
                            sb.Append((char)b);
                        else
                            sb.Append($"\\x{b:X2}");
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
