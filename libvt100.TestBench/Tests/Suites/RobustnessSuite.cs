using System;
using System.Collections.Generic;
using System.Threading;
using libVT100;
using libVT100.TestBench.Harness;

namespace libVT100.TestBench.Tests.Suites
{
    public static class RobustnessSuite
    {
        public static IEnumerable<ITestCase> GetTests()
        {
            // 1. Incomplete sequence times out and resumes ground state
            yield return new SimpleTestCase(
                "Robustness_01_IncompleteEscapeSequence_TimesOutAndResumes", "Robustness",
                "Times out incomplete escape sequences without hanging and resumes normal text rendering",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 40, height: 5);

                    // Set a fast timeout for testing
                    session.Decoder.SequenceTimeout = TimeSpan.FromMilliseconds(50);

                    // Send incomplete CSI sequence without terminator
                    session.Feed("\x1B[99;123");

                    // Sleep past the timeout threshold
                    Thread.Sleep(75);

                    // Feed normal text
                    session.Feed("ResumedText");

                    string screenText = string.Join("\n", session.GetAllScreenRows());
                    ctx.Assert(screenText.Contains("ResumedText"), $"Screen canvas must contain 'ResumedText', got: '{screenText}'");
                });

            // 2. Malformed command bytes recover to ground state without throwing or asserting
            yield return new SimpleTestCase(
                "Robustness_02_MalformedCommandByte_RecoversWithoutThrowing", "Robustness",
                "Recovers from invalid first command bytes and continues decoding subsequent stream data",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 40, height: 5);

                    // Send malformed escape sequence (0xFF is not a valid 7-bit introducer)
                    session.Feed(new byte[] { 0x1B, 0xFF, 0xFE, (byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o' });

                    string screenText = string.Join("\n", session.GetAllScreenRows());
                    ctx.Assert(screenText.Contains("Hello"), $"Screen must render 'Hello' after malformed byte recovery, got: '{screenText}'");
                });

            // 3. Unknown keys and color values never assert or throw
            yield return new SimpleTestCase(
                "Robustness_03_UnknownKeysAndColors_NeverAssertOrThrow", "Robustness",
                "Handles invalid keys and unknown color indices safely without throwing or stopping",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 40, height: 5);
                    TerminalFrameBuffer.DoAsserts = true;

                    // Pass an unrecognized key to KeyPressed
                    bool handled = ((IDecoder)session.Decoder).KeyPressed(Keys.None, (Keys)99999);
                    ctx.Assert(!handled, "Unrecognized key must return false without throwing");

                    // Feed unknown SGR attribute and unknown text color
                    session.Feed("\x1B[999mText\x1B[0m");

                    string screenText = string.Join("\n", session.GetAllScreenRows());
                    ctx.Assert(screenText.Contains("Text"), "Screen must continue rendering normal text");
                });
        }
    }
}
