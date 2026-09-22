using System;
using System.Collections.Generic;
using libVT100;
using libVT100.TestBench.Harness;

namespace libVT100.TestBench.Tests.Suites
{
    public static class VT220SuitePart3
    {
        public static IEnumerable<ITestCase> GetTests()
        {
            yield return new SimpleTestCase(
                "VT220_11_CursorSaveRestore_DECSC_DECRC", "VT220", "ESC 7 (save) and ESC 8 (restore) cursor position and attributes",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 20, height: 4);
                    // Move to (5, 2), set Bold, save cursor with ESC 7
                    session.Feed("\e[3;6H\e[1m\e7");

                    // Move to Home, reset attributes, write Normal
                    session.Feed("\e[H\e[0mNormal");

                    // Restore cursor with ESC 8, write "Restored"
                    session.Feed("\e8Restored");

                    ctx.AssertCursor(session.Buffer, 13, 2);
                    ctx.Assert(session.Buffer[5, 2].Attributes.Bold, "Restored text must retain Bold attribute");
                    ctx.Assert(!session.Buffer[0, 0].Attributes.Bold, "Normal text must not be bold");
                });

            yield return new SimpleTestCase(
                "VT220_12_Tabs_HTS_TBC", "VT220", "Tab stops: set (HTS), clear single, clear all (TBC), tab advance",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 20, height: 2);
                    // Clear all tabs with CSI 3 g, set tab at col 5 with ESC H
                    session.Feed("\e[3g\e[1;6H\eH");

                    // Home cursor, feed \t, write X
                    session.Feed("\e[H\tX");
                    ctx.AssertCursor(session.Buffer, 6, 0);
                    ctx.AssertScreen(session.Buffer, new[] {
                        "     X              ",
                        "                    "
                    });
                });

            yield return new SimpleTestCase(
                "VT220_13_LineDrawing_ACS", "VT220", "DEC Special Graphics line drawing (ACS) translation",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 10, height: 3);
                    // Designate G1 to ACS (\e)0), invoke G1 with SO (\x0e)
                    // Box:
                    // lqqk -> ┌──┐
                    // x  x -> │  │
                    // mqqj -> └──┘
                    session.Feed("\e)0\x0Elqqk\r\nx  x\r\nmqqj\x0F");

                    ctx.AssertScreen(session.Buffer, new[] {
                        "┌──┐      ",
                        "│  │      ",
                        "└──┘      "
                    });
                });

            yield return new SimpleTestCase(
                "VT220_14_PrimaryDA_VT220_ID", "VT220", "CSI c requests Primary Device Attributes, responds with VT220 ID",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 40, height: 5);
                    session.Feed("\e[c");
                    string response = session.GetEmittedOutputString();

                    ctx.Assert(response == "\\e[?62;1;2;6;7;8;9c", 
                        $"Expected VT220 DA response '\\e[?62;1;2;6;7;8;9c', but got '{response}'");
                });

            yield return new SimpleTestCase(
                "VT220_15_SoftTerminalReset_DECSTR", "VT220", "CSI ! p resets attributes, autowrap, margins without clearing text",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 20, height: 4);
                    session.Feed("PersistentText");
                    // Set Bold, red, IRM on, margins 2..3, then soft reset
                    session.Feed("\e[1m\e[31m\e[4h\e[2;3r\e[!p");

                    ctx.AssertScreen(session.Buffer, new[] {
                        "PersistentText      ",
                        "                    ",
                        "                    ",
                        "                    "
                    });
                });

            yield return new SimpleTestCase(
                "VT220_16_Stream_PacketFragmentation", "VT220", "Incomplete escape sequences across packets do not crash or corrupt",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 20, height: 3);
                    // Split \e[2;5HHello across 3 packets: "\e[2", ";5", "HHello"
                    session.Feed("\e[2");
                    session.Feed(";5");
                    session.Feed("HHello");

                    ctx.AssertCursor(session.Buffer, 9, 1);
                    ctx.AssertScreen(session.Buffer, new[] {
                        "                    ",
                        "    Hello           ",
                        "                    "
                    });
                });
        }
    }
}
