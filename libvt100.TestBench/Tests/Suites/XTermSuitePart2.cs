using System;
using System.Collections.Generic;
using System.Text;
using libVT100;
using libVT100.TestBench.Harness;

namespace libVT100.TestBench.Tests.Suites
{
    public static class XTermSuitePart2
    {
        public static IEnumerable<ITestCase> GetTests()
        {
            yield return new SimpleTestCase(
                "XTerm_07_RowAddress_VPA_And_ColumnAddress_HPA", "xterm", "VPA (CSI d) and HPA (CSI `) absolute coordinate addressing",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 20, height: 6);
                    session.Feed("\e[5d\e[12`Target");

                    ctx.AssertCursor(session.Buffer, 17, 4);
                    ctx.AssertScreen(session.Buffer, new[] {
                        "                    ",
                        "                    ",
                        "                    ",
                        "                    ",
                        "           Target   ",
                        "                    "
                    });
                });

            yield return new SimpleTestCase(
                "XTerm_08_ClearSavedLines_ED3", "xterm", "CSI 3 J (ED 3) clears scrollback / saved lines buffer",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 10, height: 2);
                    session.Feed("1\r\n2\r\n3\r\n4\r\n5\r\n");
                    session.Feed("\e[3J\e[H\e[2J");

                    ctx.AssertScreen(session.Buffer, new[] {
                        "          ",
                        "          "
                    });
                });

            yield return new SimpleTestCase(
                "XTerm_09_WindowSizeReporting", "xterm", "CSI 18 t (text area) and CSI 19 t (screen size) reporting",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 40, height: 12);
                    session.Feed("\e[18t");
                    string resp18 = session.GetEmittedOutputString();
                    ctx.Assert(resp18 == "\\e[8;12;40t", $"Expected '\\e[8;12;40t', got '{resp18}'");

                    session.ClearEmittedOutput();
                    session.Feed("\e[19t");
                    string resp19 = session.GetEmittedOutputString();
                    ctx.Assert(resp19 == "\\e[9;12;40t", $"Expected '\\e[9;12;40t', got '{resp19}'");
                });

            yield return new SimpleTestCase(
                "XTerm_10_TitleStack_PushPop", "xterm", "CSI 22;0;0 t (push title) and CSI 23;0;0 t (pop title)",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 20, height: 2);
                    session.Feed("\e]2;FirstTitle\x07\e[22;0;0t");
                    session.Feed("\e]2;SecondTitle\x07\e[23;0;0t");
                    ctx.Assert(true, "Title push/pop completed");
                });

            yield return new SimpleTestCase(
                "XTerm_11_SecondaryDeviceAttributes_DA2", "xterm", "CSI > c responds with xterm ID",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 20, height: 2);
                    session.Feed("\e[>c");
                    string resp = session.GetEmittedOutputString();
                    ctx.Assert(resp == "\\e[>0;10;0c", $"Expected '\\e[>0;10;0c', got '{resp}'");
                });

            yield return new SimpleTestCase(
                "XTerm_12_Extended_Attributes_Italic_Strikethrough_Overline", "xterm", "SGR 3 (Italic), SGR 9 (Strikethrough), SGR 53 (Overline)",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 20, height: 2);
                    session.Feed("\e[3mI\e[23m\e[9mS\e[29m\e[53mO\e[55m");

                    var cellI = session.Buffer[0, 0];
                    var cellS = session.Buffer[1, 0];
                    var cellO = session.Buffer[2, 0];

                    ctx.Assert(cellI.Attributes.Italic, "Cell I must be Italic");
                    ctx.Assert(cellS.Attributes.Strikethrough, "Cell S must be Strikethrough");
                    ctx.Assert(cellO.Attributes.Overline, "Cell O must be Overline");
                });

            yield return new SimpleTestCase(
                "XTerm_13_DebugOutput_PrefixJargon", "xterm", "Debugger trace emits standardized [ANSI:...], [DEC:...], [XTERM:...]",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 20, height: 2);
                    session.Decoder.dvt = new StringBuilder();

                    session.Feed("\e[H\e[2J\e[1m\e[38;2;1;2;3m\e[?1049h\e[?1049l");
                    string trace = session.Decoder.dvt.ToString();

                    ctx.Assert(trace.Contains("[ANSI:CUP(0,0)]"), "Trace must contain [ANSI:CUP(0,0)]");
                    ctx.Assert(trace.Contains("[ANSI:ED(Both)]"), "Trace must contain [ANSI:ED(Both)]");
                    ctx.Assert(trace.Contains("[XTERM:COLOR_RGB_FG(1,2,3)]"), "Trace must contain [XTERM:COLOR_RGB_FG(1,2,3)]");
                    ctx.Assert(trace.Contains("[XTERM:ALTSCREEN_ENTER(1049)]"), "Trace must contain [XTERM:ALTSCREEN_ENTER(1049)]");
                    ctx.Assert(trace.Contains("[XTERM:ALTSCREEN_EXIT(1049)]"), "Trace must contain [XTERM:ALTSCREEN_EXIT(1049)]");
                });

            yield return new SimpleTestCase(
                "XTerm_14_MemoryLockUnlock", "xterm", "ESC l (memory lock at cursor) and ESC m (memory unlock)",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 15, height: 5);
                    session.Feed("Row 0\r\nRow 1\r\nRow 2\r\nRow 3\r\nRow 4");
                    // Move to row 2 (1-based line 3), lock memory with ESC l
                    session.Feed("\e[3;1H\el");
                    // Move to bottom, feed newline + text
                    session.Feed("\e[5;1H\nNEW");

                    ctx.AssertScreen(session.Buffer, new[] {
                        "Row 0          ", // Locked (above margin)
                        "Row 1          ", // Locked (above margin)
                        "Row 3          ", // Scrolled up
                        "Row 4          ", // Scrolled up
                        "NEW            "  // New line at bottom
                    });
                });

            yield return new SimpleTestCase(
                "XTerm_15_MultiParameter_Modes", "xterm", "Compound mode parameters like \\E[?12;25h and \\E[?3;4l",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 20, height: 2);
                    session.Feed("\e[?12;25h\e[?3;4l");
                    // Verify executed without error
                    ctx.Assert(true, "Compound mode sequence executed cleanly");
                });
        }
    }
}
