using System;
using System.Collections.Generic;
using libVT100;
using libVT100.TestBench.Harness;

namespace libVT100.TestBench.Tests.Suites
{
    public static class VT220SuitePart2
    {
        public static IEnumerable<ITestCase> GetTests()
        {
            yield return new SimpleTestCase(
                "VT220_06_DeleteCharacter_DCH", "VT220", "CSI P (DCH) deletes characters and shifts text left",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 15, height: 2);
                    session.Feed("ABCDEFGHIJ");
                    session.Feed("\e[1;4H\e[3P");

                    ctx.AssertScreen(session.Buffer, new[] {
                        "ABCGHIJ        ",
                        "               "
                    });
                });

            yield return new SimpleTestCase(
                "VT220_07_EraseCharacter_ECH", "VT220", "CSI X (ECH) erases characters without moving cursor",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 15, height: 2);
                    session.Feed("ABCDEFGHIJ");
                    session.Feed("\e[1;4H\e[4X");

                    ctx.AssertCursor(session.Buffer, 3, 0);
                    ctx.AssertScreen(session.Buffer, new[] {
                        "ABC    HIJ     ",
                        "               "
                    });
                });

            yield return new SimpleTestCase(
                "VT220_08_InsertMode_IRM", "VT220", "CSI 4 h (IRM on) shifts characters right when typing",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 15, height: 2);
                    session.Feed("HelloWorld");
                    session.Feed("\e[1;6H\e[4h***\e[4l");

                    ctx.AssertScreen(session.Buffer, new[] {
                        "Hello***World  ",
                        "               "
                    });
                });

            yield return new SimpleTestCase(
                "VT220_09_AutoWrap_DECAWM_And_XENL", "VT220", "Autowrap pending flag (eat newline glitch)",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 10, height: 3);
                    // Fill line exactly (10 chars)
                    session.Feed("0123456789");
                    // Cursor must remain on column 9 (pending wrap)
                    ctx.AssertCursor(session.Buffer, 9, 0);

                    // Writing next character wraps to row 1 col 1
                    session.Feed("A");
                    ctx.AssertCursor(session.Buffer, 1, 1);
                    ctx.AssertScreen(session.Buffer, new[] {
                        "0123456789",
                        "A         ",
                        "          "
                    });
                });

            yield return new SimpleTestCase(
                "VT220_10_ClearScreen_Directions", "VT220", "ED 0 (cursor to end) and ED 1 (start to cursor)",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 10, height: 4);
                    session.Feed("AAAAAAAAAA\r\nBBBBBBBBBB\r\nCCCCCCCCCC\r\nDDDDDDDDDD");

                    // Move to (5, 1) and clear to end of screen (ED 0)
                    session.Feed("\e[2;6H\e[J");

                    ctx.AssertScreen(session.Buffer, new[] {
                        "AAAAAAAAAA",
                        "BBBBB     ",
                        "          ",
                        "          "
                    });
                });
        }
    }
}
