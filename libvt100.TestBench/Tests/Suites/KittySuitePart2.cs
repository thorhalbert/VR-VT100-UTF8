using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using libVT100;
using libVT100.TestBench.Harness;

namespace libVT100.TestBench.Tests.Suites
{
    public static class KittySuitePart2
    {
        public static IEnumerable<ITestCase> GetTests()
        {
            yield return new SimpleTestCase(
                "Kitty_05_ExplicitHyperlinks_OSC8", "Kitty", "OSC 8 explicit hyperlink URL interned registry",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 25, height: 2);
                    session.Feed("\e]8;id=link1;https://kitty.terminal\e\\KittyLink\e]8;;\e\\ PlainText");

                    var linkCell = session.Buffer[0, 0];
                    ctx.Assert(linkCell.Attributes.HyperlinkId > 0, "Link cell must have non-zero HyperlinkId");

                    string? url = session.Buffer.GetHyperlinkUrl(linkCell.Attributes.HyperlinkId);
                    ctx.Assert(url == "https://kitty.terminal", $"Expected URL 'https://kitty.terminal', got '{url}'");

                    var plainCell = session.Buffer[10, 0];
                    ctx.Assert(plainCell.Attributes.HyperlinkId == 0, "Plain cell must have HyperlinkId 0");
                });

            yield return new SimpleTestCase(
                "Kitty_06_SynchronizedOutput_Mode2026", "Kitty", "CSI ? 2026 h/l atomic synchronized output mode",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 20, height: 2);

                    session.Feed("\e[?2026h");
                    ctx.Assert(TerminalFrameBuffer.UIActions.SynchronizedOutputActive, "Synchronized output must be active");

                    session.Feed("BufferedText\e[?2026l");
                    ctx.Assert(!TerminalFrameBuffer.UIActions.SynchronizedOutputActive, "Synchronized output must be inactive after reset");
                });

            yield return new SimpleTestCase(
                "Kitty_07_KeyboardProtocolQuery_Probe", "Kitty", "CSI ? u probes Kitty keyboard protocol, responds with 0 flags",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 20, height: 2);
                    session.Feed("\e[?u");

                    string resp = session.GetEmittedOutputString();
                    ctx.Assert(resp == "\\e[?0u", $"Expected '\\e[?0u', got '{resp}'");
                });

            yield return new SimpleTestCase(
                "Kitty_08_TraceLogging_PrefixJargon", "Kitty", "Trace log emits standardized [KITTY:...] tokens",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 20, height: 2);
                    session.Decoder.dvt = new StringBuilder();

                    session.Feed("\e[4:3m\e[58;2;10;20;30m\e[2 q\e]8;id=1;http://test\e\\\e]8;;\e\\\e[?2026h\e[?2026l\e[?u");
                    string trace = session.Decoder.dvt.ToString();

                    ctx.Assert(trace.Contains("[KITTY:UNDERLINE_STYLE(Curly)]"), "Trace must contain [KITTY:UNDERLINE_STYLE(Curly)]");
                    ctx.Assert(trace.Contains("[KITTY:UNDERLINE_COLOR_RGB(10,20,30)]"), "Trace must contain [KITTY:UNDERLINE_COLOR_RGB(10,20,30)]");
                    ctx.Assert(trace.Contains("[KITTY:DECSCUSR(2)]"), "Trace must contain [KITTY:DECSCUSR(2)]");
                    ctx.Assert(trace.Contains("[KITTY:OSC_HYPERLINK:"), "Trace must contain [KITTY:OSC_HYPERLINK:");
                    ctx.Assert(trace.Contains("[KITTY:SYNC_OUTPUT_START]"), "Trace must contain [KITTY:SYNC_OUTPUT_START]");
                    ctx.Assert(trace.Contains("[KITTY:SYNC_OUTPUT_END]"), "Trace must contain [KITTY:SYNC_OUTPUT_END]");
                    ctx.Assert(trace.Contains("[KITTY:KEYBOARD_QUERY]"), "Trace must contain [KITTY:KEYBOARD_QUERY]");
                });
            yield return new SimpleTestCase(
                "Kitty_09_DesktopNotification_And_ShellIntegration", "Kitty", "OSC 99 desktop notifications and OSC 133 shell integration",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 20, height: 2);
                    session.Decoder.dvt = new StringBuilder();

                    session.Feed("\e]99;i=1:d=5;Build Completed\e\\\e]133;A\e\\\e]133;B\e\\\e]133;C\e\\\e]133;D;0\e\\");
                    string trace = session.Decoder.dvt.ToString();

                    ctx.Assert(trace.Contains("[KITTY:OSC_NOTIFICATION:i=1:d=5;Build Completed]"), "Must handle OSC 99 notification");
                    ctx.Assert(trace.Contains("[KITTY:OSC_SHELL_INTEGRATION:A]"), "Must handle OSC 133;A");
                    ctx.Assert(trace.Contains("[KITTY:OSC_SHELL_INTEGRATION:D;0]"), "Must handle OSC 133;D;0");
                });

        }
    }
}
