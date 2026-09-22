using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using libVT100;
using libVT100.KittyGraphics;
using libVT100.TestBench.Harness;

namespace libVT100.TestBench.Tests.Suites
{
    public static class KittyGraphicsSuitePart2
    {
        public static IEnumerable<ITestCase> GetTests()
        {
            // 4. Raw 24-bit RGB Format Conversion (f=24)
            yield return new SimpleTestCase(
                "KittyGfx_04_RawRGB_Conversion", "KittyGraphics",
                "Converts raw 24-bit RGB pixel data to 32-bit RGBA with full alpha opacity",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 40, height: 10);
                    byte[] rgb = new byte[6] { 255, 0, 0, 0, 0, 255 };
                    string b64 = Convert.ToBase64String(rgb);

                    session.Feed($"\x1B_Ga=t,i=24,f=24,s=2,v=1;{b64}\x1B\\");

                    ctx.Assert(session.Buffer.StoredImages.ContainsKey(24), "Image 24 must be stored");
                    var img = session.Buffer.StoredImages[24];
                    ctx.Assert(img.GetPixel(0, 0) == Color.FromArgb(255, 255, 0, 0), "Pixel (0,0) must be opaque Red");
                    ctx.Assert(img.GetPixel(1, 0) == Color.FromArgb(255, 0, 0, 255), "Pixel (1,0) must be opaque Blue");
                });

            // 5. Multi-Chunk Transmission (m=1 -> m=0)
            yield return new SimpleTestCase(
                "KittyGfx_05_MultiChunk_Transmission", "KittyGraphics",
                "Reassembles multi-chunk image transmissions across sequential m=1/m=0 slices",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 40, height: 10);

                    byte[] rgba = new byte[16] { 10, 20, 30, 255, 40, 50, 60, 255, 70, 80, 90, 255, 100, 110, 120, 255 };
                    string fullB64 = Convert.ToBase64String(rgba);
                    int half = fullB64.Length / 2;
                    string chunk1 = fullB64.Substring(0, half);
                    string chunk2 = fullB64.Substring(half);

                    session.Feed($"\x1B_Ga=t,i=55,f=32,s=2,v=2,m=1;{chunk1}\x1B\\");
                    ctx.Assert(!session.Buffer.StoredImages.ContainsKey(55), "Incomplete after chunk 1");

                    session.Feed($"\x1B_Ga=t,i=55,m=0;{chunk2}\x1B\\");
                    ctx.Assert(session.Buffer.StoredImages.ContainsKey(55), "Stored after chunk 2");

                    var img = session.Buffer.StoredImages[55];
                    ctx.Assert(img.GetPixel(0, 0) == Color.FromArgb(255, 10, 20, 30), "Pixel match");
                });

            // 6. ZLib Decompression (o=z)
            yield return new SimpleTestCase(
                "KittyGfx_06_ZLib_Decompression", "KittyGraphics",
                "Decompresses zlib-compressed image payloads before decoding",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 40, height: 10);

                    byte[] rgba = new byte[16] { 200, 100, 50, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255 };
                    byte[] compressed;
                    using (var ms = new MemoryStream())
                    {
                        using (var zlib = new ZLibStream(ms, CompressionLevel.Optimal, true))
                        {
                            zlib.Write(rgba, 0, rgba.Length);
                        }
                        compressed = ms.ToArray();
                    }

                    string b64 = Convert.ToBase64String(compressed);
                    session.Feed($"\x1B_Ga=t,i=77,f=32,s=2,v=2,o=z;{b64}\x1B\\");

                    ctx.Assert(session.Buffer.StoredImages.ContainsKey(77), "Image 77 must be stored");
                    var img = session.Buffer.StoredImages[77];
                    ctx.Assert(img.GetPixel(0, 0) == Color.FromArgb(255, 200, 100, 50), "Pixel (0,0) must match decompressed data");
                });

            // 7. Query Terminal Support (a=q)
            yield return new SimpleTestCase(
                "KittyGfx_07_Query_TerminalSupport", "KittyGraphics",
                "Responds to capability queries (a=q) with OK acknowledgment",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 40, height: 10);
                    session.Feed("\x1B_Gi=42,a=q;AAAA\x1B\\");

                    string emitted = session.GetEmittedOutputString();
                    ctx.Assert(emitted.Contains("\\e_Gi=42;OK\\e\\"), $"Expected query OK response, got '{emitted}'");
                });
        }
    }
}
