using System;
using System.Collections.Generic;
using libVT100.TestBench.Harness;

namespace libVT100.TestBench.Tests.Suites
{
    public static class VT220Suite
    {
        public static IEnumerable<ITestCase> GetTests()
        {
            yield return new SimpleTestCase(
                "VT220_01_DECSTBM_Margins_ScrollUp", "VT220", "Scrolling constrained within DECSTBM margins",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 15, height: 6);
                    session.Feed("Row 0\r\nRow 1\r\nRow 2\r\nRow 3\r\nRow 4\r\nRow 5");
                    session.Feed("\e[2;5r");
                    session.Feed("\e[5;1H\nNEW LINE");

                    ctx.AssertScreen(session.Buffer, new[] {
                        "Row 0          ",
                        "Row 2          ",
                        "Row 3          ",
                        "Row 4          ",
                        "NEW LINE       ",
                        "Row 5          "
                    });
                });

            yield return new SimpleTestCase(
                "VT220_02_ReverseIndex_RI", "VT220", "ESC M (RI) scrolls down at top margin",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 15, height: 5);
                    session.Feed("Row 0\r\nRow 1\r\nRow 2\r\nRow 3\r\nRow 4");
                    session.Feed("\e[2;4r");
                    session.Feed("\e[2;1H\eMINSERTED");

                    ctx.AssertScreen(session.Buffer, new[] {
                        "Row 0          ",
                        "INSERTED       ",
                        "Row 1          ",
                        "Row 2          ",
                        "Row 4          "
                    });
                });

            yield return new SimpleTestCase(
                "VT220_03_InsertLine_IL", "VT220", "CSI L (IL) inserts lines within margins and shifts lines down",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 15, height: 5);
                    session.Feed("Row 0\r\nRow 1\r\nRow 2\r\nRow 3\r\nRow 4");
                    session.Feed("\e[2;1H\e[2L");

                    ctx.AssertScreen(session.Buffer, new[] {
                        "Row 0          ",
                        "               ",
                        "               ",
                        "Row 1          ",
                        "Row 2          "
                    });
                });

            yield return new SimpleTestCase(
                "VT220_04_DeleteLine_DL", "VT220", "CSI M (DL) deletes lines within margins and shifts lines up",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 15, height: 5);
                    session.Feed("Row 0\r\nRow 1\r\nRow 2\r\nRow 3\r\nRow 4");
                    session.Feed("\e[2;1H\e[2M");

                    ctx.AssertScreen(session.Buffer, new[] {
                        "Row 0          ",
                        "Row 3          ",
                        "Row 4          ",
                        "               ",
                        "               "
                    });
                });

            yield return new SimpleTestCase(
                "VT220_05_InsertCharacter_ICH", "VT220", "CSI @ (ICH) inserts blank characters and shifts text right",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 15, height: 2);
                    session.Feed("ABCDEFGHIJ");
                    session.Feed("\e[1;4H\e[3@");

                    ctx.AssertScreen(session.Buffer, new[] {
                        "ABC   DEFGHIJ  ",
                        "               "
                    });
                });
        }
    }
}
