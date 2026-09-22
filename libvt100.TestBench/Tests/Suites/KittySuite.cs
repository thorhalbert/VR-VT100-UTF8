using System;
using System.Collections.Generic;
using System.Drawing;
using libVT100;
using libVT100.TestBench.Harness;

namespace libVT100.TestBench.Tests.Suites
{
    public static class KittySuite
    {
        public static IEnumerable<ITestCase> GetTests()
        {
            yield return new SimpleTestCase(
                "Kitty_01_ExtendedUnderlineStyles", "Kitty", "Kitty SGR 4:x extended underlines (Curly, Dotted, Dashed)",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 25, height: 2);
                    session.Feed("\e[4:3mC\e[4:0m \e[4:4mD\e[4:0m \e[4:5mS\e[4:0m");

                    var cellC = session.Buffer[0, 0];
                    var cellD = session.Buffer[2, 0];
                    var cellS = session.Buffer[4, 0];

                    ctx.Assert(cellC.Attributes.Underline == TerminalFrameBuffer.Underline.Curly, "Cell C must be Curly underline");
                    ctx.Assert(cellD.Attributes.Underline == TerminalFrameBuffer.Underline.Dotted, "Cell D must be Dotted underline");
                    ctx.Assert(cellS.Attributes.Underline == TerminalFrameBuffer.Underline.Dashed, "Cell S must be Dashed underline");
                });

            yield return new SimpleTestCase(
                "Kitty_02_UnderlineColor", "Kitty", "Kitty SGR 58 (Underline Color) and SGR 59 (Reset)",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 25, height: 2);
                    session.Feed("\e[4m\e[58;2;255;50;100mUnderlineColored\e[59mNormal");

                    var cell = session.Buffer[0, 0];
                    ctx.Assert(cell.Attributes.Underline == TerminalFrameBuffer.Underline.Single, "Must have underline");
                    ctx.Assert(cell.Attributes.UnderlineColor == Color.FromArgb(255, 50, 100), 
                        $"Expected UnderlineColor (255,50,100), got {cell.Attributes.UnderlineColor}");

                    var cellAfter = session.Buffer[16, 0];
                    ctx.Assert(cellAfter.Attributes.UnderlineColor == null, "Underline color must be reset");
                });

            yield return new SimpleTestCase(
                "Kitty_03_CursorShape_DECSCUSR", "Kitty", "CSI Ps SP q (DECSCUSR) cursor shape and blinking",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 20, height: 2);

                    session.Feed("\e[1 q");
                    ctx.Assert(session.Buffer.CursorStyle == TerminalFrameBuffer.CursorShape.Block && session.Buffer.CursorBlink, 
                        "Expected Blinking Block");

                    session.Feed("\e[4 q");
                    ctx.Assert(session.Buffer.CursorStyle == TerminalFrameBuffer.CursorShape.Underline && !session.Buffer.CursorBlink, 
                        "Expected Steady Underline");

                    session.Feed("\e[5 q");
                    ctx.Assert(session.Buffer.CursorStyle == TerminalFrameBuffer.CursorShape.Bar && session.Buffer.CursorBlink, 
                        "Expected Blinking Bar");
                });

            yield return new SimpleTestCase(
                "Kitty_04_CursorColor_OSC12_OSC112", "Kitty", "OSC 12 (Set cursor color) and OSC 112 (Reset cursor color)",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 20, height: 2);

                    session.Feed("\e]12;rgb:ff00/8000/0000\x07");
                    ctx.Assert(session.Buffer.CursorColor == Color.FromArgb(255, 128, 0), 
                        $"Expected CursorColor (255,128,0), got {session.Buffer.CursorColor}");

                    session.Feed("\e]112\x07");
                    ctx.Assert(session.Buffer.CursorColor == null, "CursorColor must be reset to null");
                });
        }
    }
}
