using System;

namespace libVT100.KittyGraphics
{
    /// <summary>
    /// Parsed command parameters for a Kitty Graphics Protocol sequence (\x1b_G...;...\x1b\).
    /// </summary>
    public sealed class KittyGraphicsCommand
    {
        public char Action { get; set; } = 'T'; // 't' = transmit, 'T' = transmit & put, 'p' = put, 'd' = delete, 'q' = query
        public uint ImageId { get; set; } = 0;
        public uint PlacementId { get; set; } = 0;
        public int Format { get; set; } = 32; // 32 = RGBA, 24 = RGB, 100 = PNG
        public char TransmissionMedium { get; set; } = 'd'; // 'd' = direct payload
        public int PixelWidth { get; set; } = 0;
        public int PixelHeight { get; set; } = 0;
        public int? ColSpan { get; set; } = null;
        public int? RowSpan { get; set; } = null;
        public int SrcX { get; set; } = 0;
        public int SrcY { get; set; } = 0;
        public int SrcWidth { get; set; } = 0;
        public int SrcHeight { get; set; } = 0;
        public int SubCellX { get; set; } = 0;
        public int SubCellY { get; set; } = 0;
        public int ZIndex { get; set; } = 0;
        public int CursorPolicy { get; set; } = 1; // 1 = advance cursor below image
        public int QuietMode { get; set; } = 0; // 0 = normal, 1 = quiet on success, 2 = quiet always
        public char DeleteTarget { get; set; } = '\0'; // 'a', 'i', 'p', 'c', 'z', etc.
        public char Compression { get; set; } = '\0'; // 'z' = zlib / deflate
        public bool IsMore { get; set; } = false;

        public KittyImage? Image { get; set; }

        public static bool TryParse(ReadOnlySpan<char> span, out KittyGraphicsCommand cmd, out string? error)
        {
            cmd = new KittyGraphicsCommand();
            error = null;

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
                char key = keySpan[0];

                switch (key)
                {
                    case 'a':
                        if (!valSpan.IsEmpty) cmd.Action = valSpan[0];
                        break;
                    case 'i':
                    case 'I':
                        if (uint.TryParse(valSpan, out uint id)) cmd.ImageId = id;
                        break;
                    case 'p':
                        if (uint.TryParse(valSpan, out uint pid)) cmd.PlacementId = pid;
                        break;
                    case 'f':
                        if (int.TryParse(valSpan, out int fmt)) cmd.Format = fmt;
                        break;
                    case 't':
                        if (!valSpan.IsEmpty) cmd.TransmissionMedium = valSpan[0];
                        break;
                    case 's':
                        if (int.TryParse(valSpan, out int w)) cmd.PixelWidth = w;
                        break;
                    case 'v':
                        if (int.TryParse(valSpan, out int h)) cmd.PixelHeight = h;
                        break;
                    case 'c':
                        if (int.TryParse(valSpan, out int c)) cmd.ColSpan = c;
                        break;
                    case 'r':
                        if (int.TryParse(valSpan, out int r)) cmd.RowSpan = r;
                        break;
                    case 'x':
                        if (int.TryParse(valSpan, out int x)) cmd.SrcX = x;
                        break;
                    case 'y':
                        if (int.TryParse(valSpan, out int y)) cmd.SrcY = y;
                        break;
                    case 'w':
                        if (int.TryParse(valSpan, out int sw)) cmd.SrcWidth = sw;
                        break;
                    case 'h':
                        if (int.TryParse(valSpan, out int sh)) cmd.SrcHeight = sh;
                        break;
                    case 'X':
                        if (int.TryParse(valSpan, out int scx)) cmd.SubCellX = scx;
                        break;
                    case 'Y':
                        if (int.TryParse(valSpan, out int scy)) cmd.SubCellY = scy;
                        break;
                    case 'z':
                        if (int.TryParse(valSpan, out int z)) cmd.ZIndex = z;
                        break;
                    case 'C':
                        if (int.TryParse(valSpan, out int cp)) cmd.CursorPolicy = cp;
                        break;
                    case 'q':
                        if (int.TryParse(valSpan, out int qm)) cmd.QuietMode = qm;
                        break;
                    case 'd':
                        if (!valSpan.IsEmpty) cmd.DeleteTarget = valSpan[0];
                        break;
                    case 'o':
                        if (!valSpan.IsEmpty) cmd.Compression = valSpan[0];
                        break;
                    case 'm':
                        cmd.IsMore = (!valSpan.IsEmpty && valSpan[0] == '1');
                        break;
                }
            }

            return true;
        }
    }
}
