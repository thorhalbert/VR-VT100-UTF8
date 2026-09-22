using System;
using System.IO;
using System.IO.Compression;

namespace libVT100.KittyGraphics
{
    /// <summary>
    /// Pure C# PNG (Portable Network Graphics) decoder.
    /// Supports standard 8-bit RGB, RGBA, and Grayscale PNG formats without native dependencies.
    /// </summary>
    public static class PngDecoder
    {
        private static readonly byte[] PngHeader = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };

        public static bool TryDecode(ReadOnlySpan<byte> pngBytes, out int width, out int height, out byte[] rgbaData, out string? error)
        {
            width = height = 0;
            rgbaData = Array.Empty<byte>();
            error = null;

            if (pngBytes.Length < 8 || !pngBytes.Slice(0, 8).SequenceEqual(PngHeader))
            {
                error = "Invalid PNG signature.";
                return false;
            }

            int offset = 8, bitDepth = 0, colorType = 0;
            using var idatStream = new MemoryStream();

            while (offset + 8 <= pngBytes.Length)
            {
                int chunkLen = (pngBytes[offset] << 24) | (pngBytes[offset + 1] << 16) | (pngBytes[offset + 2] << 8) | pngBytes[offset + 3];
                offset += 4;
                string chunkType = "" + (char)pngBytes[offset] + (char)pngBytes[offset + 1] + (char)pngBytes[offset + 2] + (char)pngBytes[offset + 3];
                offset += 4;

                if (offset + chunkLen > pngBytes.Length) { error = "Truncated PNG chunk."; return false; }
                var chunkData = pngBytes.Slice(offset, chunkLen);
                offset += chunkLen + 4; // Skip data + CRC

                if (chunkType == "IHDR" && chunkLen >= 13)
                {
                    width = (chunkData[0] << 24) | (chunkData[1] << 16) | (chunkData[2] << 8) | chunkData[3];
                    height = (chunkData[4] << 24) | (chunkData[5] << 16) | (chunkData[6] << 8) | chunkData[7];
                    bitDepth = chunkData[8];
                    colorType = chunkData[9];
                }
                else if (chunkType == "IDAT") idatStream.Write(chunkData);
                else if (chunkType == "IEND") break;
            }

            if (width <= 0 || height <= 0 || idatStream.Length == 0 || bitDepth != 8)
            {
                error = "Incomplete PNG or unsupported bit depth.";
                return false;
            }

            int bpp = colorType switch { 6 => 4, 2 => 3, 4 => 2, 0 => 1, _ => -1 };
            if (bpp == -1) { error = $"Unsupported color type: {colorType}."; return false; }

            try
            {
                idatStream.Position = 0;
                using var zlib = new ZLibStream(idatStream, CompressionMode.Decompress);
                using var decompressed = new MemoryStream();
                zlib.CopyTo(decompressed);

                byte[] raw = decompressed.ToArray();
                int stride = width * bpp;
                rgbaData = new byte[width * height * 4];
                byte[] prior = new byte[stride], cur = new byte[stride];
                int rawIdx = 0, rgbaIdx = 0;

                for (int y = 0; y < height; y++)
                {
                    byte filter = raw[rawIdx++];
                    for (int x = 0; x < stride; x++)
                    {
                        byte filt = raw[rawIdx++];
                        byte a = x >= bpp ? cur[x - bpp] : (byte)0;
                        byte b = prior[x];
                        byte c = x >= bpp ? prior[x - bpp] : (byte)0;

                        cur[x] = filter switch
                        {
                            1 => (byte)(filt + a),
                            2 => (byte)(filt + b),
                            3 => (byte)(filt + ((a + b) / 2)),
                            4 => (byte)(filt + Paeth(a, b, c)),
                            _ => filt
                        };
                    }

                    for (int p = 0; p < width; p++)
                    {
                        int srcP = p * bpp;
                        if (colorType == 6) // RGBA
                        {
                            rgbaData[rgbaIdx++] = cur[srcP];
                            rgbaData[rgbaIdx++] = cur[srcP + 1];
                            rgbaData[rgbaIdx++] = cur[srcP + 2];
                            rgbaData[rgbaIdx++] = cur[srcP + 3];
                        }
                        else if (colorType == 2) // RGB
                        {
                            rgbaData[rgbaIdx++] = cur[srcP];
                            rgbaData[rgbaIdx++] = cur[srcP + 1];
                            rgbaData[rgbaIdx++] = cur[srcP + 2];
                            rgbaData[rgbaIdx++] = 255;
                        }
                        else
                        {
                            byte g = cur[srcP];
                            rgbaData[rgbaIdx++] = g;
                            rgbaData[rgbaIdx++] = g;
                            rgbaData[rgbaIdx++] = g;
                            rgbaData[rgbaIdx++] = colorType == 4 ? cur[srcP + 1] : (byte)255;
                        }
                    }
                    Array.Copy(cur, prior, stride);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = $"PNG error: {ex.Message}";
                return false;
            }
        }

        private static byte Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc) return (byte)a;
            if (pb <= pc) return (byte)b;
            return (byte)c;
        }
    }
}
