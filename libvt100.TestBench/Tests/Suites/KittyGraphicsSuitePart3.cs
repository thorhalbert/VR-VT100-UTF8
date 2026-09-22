using System;
using System.Collections.Generic;
using System.Drawing;
using libVT100;
using libVT100.KittyGraphics;
using libVT100.TestBench.Harness;

namespace libVT100.TestBench.Tests.Suites
{
    public static class KittyGraphicsSuitePart3
    {
        public static IEnumerable<ITestCase> GetTests()
        {
            // 8. Deletion Filters (a=d)
            yield return new SimpleTestCase(
                "KittyGfx_08_Deletion_Filters", "KittyGraphics",
                "Evicts placements and stored images using specific deletion target filters",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 40, height: 10);
                    byte[] rgba = new byte[16];
                    string b64 = Convert.ToBase64String(rgba);

                    session.Feed($"\x1B_Ga=T,i=1,p=10,f=32,s=2,v=2;{b64}\x1B\\");
                    session.Feed($"\x1B_Ga=T,i=2,p=20,f=32,s=2,v=2;{b64}\x1B\\");
                    ctx.Assert(session.Buffer.ImagePlacements.Count == 2, "2 placements active");

                    // Delete placement 10
                    session.Feed("\x1B_Ga=d,d=p,p=10;\x1B\\");
                    ctx.Assert(session.Buffer.ImagePlacements.Count == 1, "1 placement remaining");
                    ctx.Assert(session.Buffer.ImagePlacements[0].PlacementId == 20, "Placement 20 kept");

                    // Delete all
                    session.Feed("\x1B_Ga=d,d=a;\x1B\\");
                    ctx.Assert(session.Buffer.ImagePlacements.Count == 0, "All placements cleared");
                    ctx.Assert(session.Buffer.StoredImages.Count == 0, "All stored images cleared");
                });

            // 9. Scrolling Shifts Image Placements
            yield return new SimpleTestCase(
                "KittyGfx_09_Scrolling_Shifts_Placements", "KittyGraphics",
                "Vertically shifts anchored placements during text line scrolling",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 40, height: 5);
                    byte[] rgba = new byte[16];
                    string b64 = Convert.ToBase64String(rgba);

                    // Move to row 2 and place image with C=0 (no cursor advance)
                    session.Feed($"\x1B[3;1H\x1B_Ga=T,i=1,f=32,s=2,v=2,C=0;{b64}\x1B\\");
                    ctx.Assert(session.Buffer.ImagePlacements[0].AnchorRow == 2, "Anchor at row 2");

                    // Scroll up 1 line
                    session.Feed("\x1B[1S");
                    ctx.Assert(session.Buffer.ImagePlacements[0].AnchorRow == 1, "Anchor shifted to row 1 after scroll");
                });

            // 10. Cursor Movement Policy (C=0 vs C=1)
            yield return new SimpleTestCase(
                "KittyGfx_10_Cursor_Movement_Policy", "KittyGraphics",
                "Preserves cursor with C=0 and advances cursor below image footprint with C=1",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 40, height: 10);
                    byte[] rgba = new byte[16];
                    string b64 = Convert.ToBase64String(rgba);

                    // Place with C=0 at (0,0)
                    session.Feed($"\x1B[1;1H\x1B_Ga=T,i=1,f=32,s=2,v=2,r=3,C=0;{b64}\x1B\\");
                    ctx.Assert(session.Buffer.CursorPosition == new Point(0, 0), "Cursor should not move with C=0");

                    // Place with C=1 at (0,0) with r=3 (takes 3 rows)
                    session.Feed($"\x1B[1;1H\x1B_Ga=T,i=2,f=32,s=2,v=2,r=3,C=1;{b64}\x1B\\");
                    ctx.Assert(session.Buffer.CursorPosition.Y == 3, $"Cursor Y expected 3, got {session.Buffer.CursorPosition.Y}");
                });
        }
    }
}
