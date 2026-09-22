using System;
using System.Drawing;

namespace libVT100.KittyGraphics
{
    /// <summary>
    /// Represents a stored bitmap image in the Kitty Graphics Protocol image store.
    /// Pixels are stored as standard 32-bit RGBA (Red, Green, Blue, Alpha).
    /// </summary>
    public sealed class KittyImage
    {
        public uint Id { get; }
        public int Width { get; }
        public int Height { get; }
        public byte[] RgbaData { get; }

        public KittyImage(uint id, int width, int height, byte[] rgbaData)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);

            Id = id;
            Width = width;
            Height = height;

            int expectedLen = width * height * 4;
            if (rgbaData == null)
            {
                rgbaData = new byte[expectedLen];
            }
            else if (rgbaData.Length < expectedLen)
            {
                byte[] padded = new byte[expectedLen];
                Array.Copy(rgbaData, padded, rgbaData.Length);
                rgbaData = padded;
            }
            RgbaData = rgbaData;
        }

        /// <summary>
        /// Gets the 32-bit RGBA color of a pixel at coordinates (x, y).
        /// </summary>
        public Color GetPixel(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
            {
                return Color.Transparent;
            }

            int offset = (y * Width + x) * 4;
            byte r = RgbaData[offset];
            byte g = RgbaData[offset + 1];
            byte b = RgbaData[offset + 2];
            byte a = RgbaData[offset + 3];
            return Color.FromArgb(a, r, g, b);
        }

        /// <summary>
        /// Creates a <see cref="KittyImage"/> from a 24-bit RGB pixel buffer, converting to 32-bit RGBA.
        /// </summary>
        public static KittyImage FromRgb(uint id, int width, int height, ReadOnlySpan<byte> rgbData)
        {
            int pixelCount = width * height;
            byte[] rgba = new byte[pixelCount * 4];

            int rgbIdx = 0;
            int rgbaIdx = 0;

            for (int i = 0; i < pixelCount && rgbIdx + 2 < rgbData.Length; i++)
            {
                rgba[rgbaIdx++] = rgbData[rgbIdx++]; // R
                rgba[rgbaIdx++] = rgbData[rgbIdx++]; // G
                rgba[rgbaIdx++] = rgbData[rgbIdx++]; // B
                rgba[rgbaIdx++] = 255;                // A (opaque)
            }

            return new KittyImage(id, width, height, rgba);
        }
    }
}
