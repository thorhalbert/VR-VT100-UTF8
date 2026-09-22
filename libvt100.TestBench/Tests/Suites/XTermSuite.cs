using System;
using System.Collections.Generic;
using System.Drawing;
using libVT100;
using libVT100.TestBench.Harness;

namespace libVT100.TestBench.Tests.Suites
{
    public static class XTermSuite
    {
        public static IEnumerable<ITestCase> GetTests()
        {
            yield return new SimpleTestCase(
                "XTerm_01_AltScreen_1049_Lifecycle", "xterm", "1049 Alternate Screen Buffer enter/exit with cursor and content preservation",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 20, height: 4);
                    session.Feed("Primary Content\e[2;10H");
                    Point savedCursor = session.Buffer.CursorPosition;

                    // Enter alternate screen buffer (CSI ? 1049 h)
                    session.Feed("\e[?1049h");
                    // Alternate screen should be clear and homed
                    ctx.AssertCursor(session.Buffer, 0, 0);
                    session.Feed("Alternate Content");
                    ctx.AssertScreen(session.Buffer, new[] {
                        "Alternate Content   ",
                        "                    ",
                        "                    ",
                        "                    "
                    });

                    // Exit alternate screen buffer (CSI ? 1049 l)
                    session.Feed("\e[?1049l");
                    // Primary content and cursor must be restored
                    ctx.AssertCursor(session.Buffer, savedCursor.X, savedCursor.Y);
                    ctx.AssertScreen(session.Buffer, new[] {
                        "Primary Content     ",
                        "                    ",
                        "                    ",
                        "                    "
                    });
                });

            yield return new SimpleTestCase(
                "XTerm_02_Color256_Foreground_Background", "xterm", "256-color palette parsing (38;5;n and 48;5;n)",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 20, height: 2);
                    // 196 = bright red, 28 = green
                    session.Feed("\e[38;5;196mRed\e[0m\e[48;5;28mGreenBg\e[0m");

                    var cellRed = session.Buffer[0, 0];
                    var cellGreenBg = session.Buffer[3, 0];

                    Color expectedRed = TerminalFrameBuffer.Get256Color(196);
                    Color expectedGreen = TerminalFrameBuffer.Get256Color(28);

                    ctx.Assert(cellRed.Attributes.ForegroundColor.ToArgb() == expectedRed.ToArgb(), 
                        $"Expected Red FG {expectedRed}, but got {cellRed.Attributes.ForegroundColor}");
                    ctx.Assert(cellGreenBg.Attributes.BackgroundColor.ToArgb() == expectedGreen.ToArgb(), 
                        $"Expected Green BG {expectedGreen}, but got {cellGreenBg.Attributes.BackgroundColor}");
                });

            yield return new SimpleTestCase(
                "XTerm_03_TrueColor_RGB_Semicolon_And_Colon", "xterm", "24-bit TrueColor RGB (38;2;r;g;b and 48:2::r:g:b)",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 20, height: 2);
                    // Semicolon FG (120, 200, 80) and Colon BG (50, 100, 150)
                    session.Feed("\e[38;2;120;200;80m\e[48:2::50:100:150mRGB\e[0m");

                    var cell = session.Buffer[0, 0];
                    ctx.Assert(cell.Attributes.ForegroundColor == Color.FromArgb(120, 200, 80), 
                        $"Expected FG (120,200,80), got {cell.Attributes.ForegroundColor}");
                    ctx.Assert(cell.Attributes.BackgroundColor == Color.FromArgb(50, 100, 150), 
                        $"Expected BG (50,100,150), got {cell.Attributes.BackgroundColor}");
                });

            yield return new SimpleTestCase(
                "XTerm_04_Back_Color_Erase_BCE", "xterm", "Back Color Erase (BCE) on EL and ECH",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 10, height: 2);
                    // Set Blue background, clear line (EL 2)
                    session.Feed("\e[44m\e[2K");

                    for (int c = 0; c < 10; c++)
                    {
                        var cell = session.Buffer[c, 0];
                        ctx.Assert(cell.Attributes.Background == TerminalFrameBuffer.TextColor.Blue, 
                            $"Cell ({c},0) must have Blue background due to BCE");
                    }
                });

            yield return new SimpleTestCase(
                "XTerm_05_RepeatCharacter_REP", "xterm", "CSI count b (REP) repeats preceding character",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 15, height: 2);
                    // Write 'Z', then repeat 5 times
                    session.Feed("Z\e[5b");

                    ctx.AssertCursor(session.Buffer, 6, 0);
                    ctx.AssertScreen(session.Buffer, new[] {
                        "ZZZZZZ         ",
                        "               "
                    });
                });

            yield return new SimpleTestCase(
                "XTerm_06_CursorBackwardTabulation_CBT", "xterm", "CSI count Z (CBT) moves backward to preceding tab stops",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 25, height: 2);
                    // Standard tabs at 8, 16. Move to col 18, CBT 1 -> col 16, CBT 1 -> col 8
                    session.Feed("\e[1;19H\e[Z");
                    ctx.AssertCursor(session.Buffer, 16, 0);

                    session.Feed("\e[Z");
                    ctx.AssertCursor(session.Buffer, 8, 0);
                });
        }
    }
}
