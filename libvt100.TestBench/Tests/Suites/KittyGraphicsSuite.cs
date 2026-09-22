using System;
using System.Collections.Generic;
using System.Drawing;
using libVT100;
using libVT100.KittyGraphics;
using libVT100.TestBench.Harness;

namespace libVT100.TestBench.Tests.Suites
{
    public static class KittyGraphicsSuite
    {
        public static IEnumerable<ITestCase> GetTests()
        {
            // 1. Transmit and Put Raw RGBA (a=T, f=32)
            yield return new SimpleTestCase(
                "KittyGfx_01_TransmitAndPut_RawRGBA", "KittyGraphics",
                "Transmits and places a 2x2 raw RGBA32 bitmap on the terminal grid",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 40, height: 10);

                    byte[] rgba = new byte[16]
                    {
                        255, 0, 0, 255,     // Red
                        0, 255, 0, 255,     // Green
                        0, 0, 255, 255,     // Blue
                        255, 255, 255, 255  // White
                    };
                    string b64 = Convert.ToBase64String(rgba);

                    session.Feed($"\x1B_Ga=T,i=1,f=32,s=2,v=2;{b64}\x1B\\");

                    ctx.Assert(session.Buffer.StoredImages.ContainsKey(1), "Image ID 1 must be stored");
                    var img = session.Buffer.StoredImages[1];
                    ctx.Assert(img.Width == 2 && img.Height == 2, $"Dimensions expected 2x2, got {img.Width}x{img.Height}");
                    ctx.Assert(img.GetPixel(0, 0) == Color.FromArgb(255, 255, 0, 0), "Pixel (0,0) must be Red");
                    ctx.Assert(img.GetPixel(1, 0) == Color.FromArgb(255, 0, 255, 0), "Pixel (1,0) must be Green");

                    ctx.Assert(session.Buffer.ImagePlacements.Count == 1, "Must have 1 placement");
                    var placement = session.Buffer.ImagePlacements[0];
                    ctx.Assert(placement.ImageId == 1, "Placement image ID must match");
                    ctx.Assert(placement.AnchorCol == 0 && placement.AnchorRow == 0, "Anchor must be at (0,0)");
                });

            // 2. Transmit only (a=t) then Put multiple placements (a=p)
            yield return new SimpleTestCase(
                "KittyGfx_02_Transmit_Then_PutMultiplePlacements", "KittyGraphics",
                "Transmits image without display, then creates multiple independent placements",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 40, height: 10);

                    byte[] rgba = new byte[16] { 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255 };
                    string b64 = Convert.ToBase64String(rgba);

                    session.Feed($"\x1B_Ga=t,i=10,f=32,s=2,v=2;{b64}\x1B\\");

                    ctx.Assert(session.Buffer.StoredImages.ContainsKey(10), "Image 10 must be stored");
                    ctx.Assert(session.Buffer.ImagePlacements.Count == 0, "No placements should be created for a=t");

                    session.Feed("\x1B_Ga=p,i=10,p=1,z=-1;\x1B\\");
                    session.Feed("\x1B[4;6H\x1B_Ga=p,i=10,p=2,z=2;\x1B\\");

                    ctx.Assert(session.Buffer.ImagePlacements.Count == 2, "Must have 2 placements");
                    var p1 = session.Buffer.ImagePlacements[0];
                    var p2 = session.Buffer.ImagePlacements[1];

                    ctx.Assert(p1.PlacementId == 1 && p1.ZIndex == -1, "Placement 1 properties");
                    ctx.Assert(p2.PlacementId == 2 && p2.ZIndex == 2, "Placement 2 properties");
                    ctx.Assert(p2.AnchorCol == 5 && p2.AnchorRow == 3, $"Placement 2 anchor expected (5,3), got ({p2.AnchorCol},{p2.AnchorRow})");
                });

            // 3. Z-Index Layering (Below text vs Above text)
            yield return new SimpleTestCase(
                "KittyGfx_03_ZIndex_Layering_Below_And_Above", "KittyGraphics",
                "Partitions placements into negative, background, and positive Z-index layers",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 40, height: 10);
                    byte[] rgba = new byte[16];
                    string b64 = Convert.ToBase64String(rgba);

                    session.Feed($"\x1B_Ga=t,i=1,f=32,s=2,v=2;{b64}\x1B\\");
                    session.Feed("\x1B_Ga=p,i=1,p=1,z=-5;\x1B\\");
                    session.Feed("\x1B_Ga=p,i=1,p=2,z=0;\x1B\\");
                    session.Feed("\x1B_Ga=p,i=1,p=3,z=3;\x1B\\");

                    var below = session.Buffer.GetPlacementsBelowText();
                    var bg = session.Buffer.GetPlacementsBackground();
                    var above = session.Buffer.GetPlacementsAboveText();

                    ctx.Assert(below.Count == 1 && below[0].PlacementId == 1, "Below text layer must contain z=-5");
                    ctx.Assert(bg.Count == 1 && bg[0].PlacementId == 2, "Background layer must contain z=0");
                    ctx.Assert(above.Count == 1 && above[0].PlacementId == 3, "Above text layer must contain z=3");
                });
        }
    }
}
