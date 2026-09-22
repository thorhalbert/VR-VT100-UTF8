using System;
using System.Collections.Generic;
using libVT100;
using libVT100.TestBench.Harness;

namespace libVT100.TestBench.Tests.Suites
{
    public static class HelloWorldSuite
    {
        public static IEnumerable<ITestCase> GetTests()
        {
            yield return new SimpleTestCase(
                "HW_01_PlainText", "HelloWorld", "Basic ASCII text rendering and cursor progression",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 30, height: 3);
                    session.Feed("Hello, VT100 World!");
                    ctx.AssertCursor(session.Buffer, 19, 0);
                    ctx.AssertScreen(session.Buffer, new[] {
                        "Hello, VT100 World!           ",
                        "                              ",
                        "                              "
                    });
                });

            yield return new SimpleTestCase(
                "HW_02_CRLF_MultiLine", "HelloWorld", "CR and LF positioning across rows",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 20, height: 4);
                    session.Feed("Line One\r\nLine Two\r\nThree");
                    ctx.AssertCursor(session.Buffer, 5, 2);
                    ctx.AssertScreen(session.Buffer, new[] {
                        "Line One            ",
                        "Line Two            ",
                        "Three               ",
                        "                    "
                    });
                });

            yield return new SimpleTestCase(
                "HW_03_CursorHome_And_ClearScreen", "HelloWorld", "Cursor Home (CUP) and Erase in Display (ED 2)",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 25, height: 3);
                    session.Feed("Old Garbage Everywhere");
                    session.Feed("\e[H\e[2JHello Clean!");
                    ctx.AssertCursor(session.Buffer, 12, 0);
                    ctx.AssertScreen(session.Buffer, new[] {
                        "Hello Clean!             ",
                        "                         ",
                        "                         "
                    });
                });

            yield return new SimpleTestCase(
                "HW_04_DirectCursorPositioning_CUP", "HelloWorld", "Direct cursor positioning via CSI row;col H",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 25, height: 4);
                    // 1-based (3, 8) -> 0-based col 7, row 2
                    session.Feed("\e[3;8HTarget");
                    ctx.AssertCursor(session.Buffer, 13, 2);
                    ctx.AssertScreen(session.Buffer, new[] {
                        "                         ",
                        "                         ",
                        "       Target            ",
                        "                         "
                    });
                });

            yield return new SimpleTestCase(
                "HW_05_CursorRelativeMovement", "HelloWorld", "CUU, CUD, CUF, CUB relative cursor movement",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 20, height: 4);
                    // Start (1,1), Down 2, Forward 5, Write 'X'
                    session.Feed("\e[1;1H\e[2B\e[5CX");
                    ctx.AssertCursor(session.Buffer, 6, 2);
                    ctx.AssertScreen(session.Buffer, new[] {
                        "                    ",
                        "                    ",
                        "     X              ",
                        "                    "
                    });
                });

            yield return new SimpleTestCase(
                "HW_06_GraphicRendition_SGR", "HelloWorld", "SGR codes for Bold, Underline, and Colors",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 20, height: 2);
                    session.Feed("\e[1mA\e[0m\e[4mB\e[0m\e[31mC\e[0m");
                    ctx.AssertScreen(session.Buffer, new[] {
                        "ABC                 ",
                        "                    "
                    });

                    ctx.Assert(session.Buffer[0, 0].Attributes.Bold, "Cell A must be Bold");
                    ctx.Assert(session.Buffer[1, 0].Attributes.Underline == TerminalFrameBuffer.Underline.Single, "Cell B must be Underlined");
                    ctx.Assert(session.Buffer[2, 0].Attributes.Foreground == TerminalFrameBuffer.TextColor.Red, "Cell C must be Red");
                });

            yield return new SimpleTestCase(
                "HW_07_ClearLine_EL", "HelloWorld", "Erase in Line (CSI K) from cursor to end of line",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 20, height: 2);
                    session.Feed("KeepMeDeleteMe\e[8D\e[K");
                    ctx.AssertCursor(session.Buffer, 6, 0);
                    ctx.AssertScreen(session.Buffer, new[] {
                        "KeepMe              ",
                        "                    "
                    });
                });

            yield return new SimpleTestCase(
                "HW_08_CursorPositionReport_DSR", "HelloWorld", "DSR (CSI 6 n) triggers CPR (CSI row;col R)",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 40, height: 10);
                    session.Feed("\e[4;15H\e[6n");
                    string response = session.GetEmittedOutputString();
                    ctx.Assert(response == "\\e[4;15R", $"Expected CPR '\\e[4;15R', but got '{response}'");
                });
        }
    }
}
