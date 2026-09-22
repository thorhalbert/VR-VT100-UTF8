using System;
using System.Collections.Generic;
using libVT100;
using libVT100.Oob;
using libVT100.TestBench.Harness;

namespace libVT100.TestBench.Tests.Suites
{
    public static class OobSuite
    {
        public static IEnumerable<ITestCase> GetTests()
        {
            // 1. Unit Test - Single Packet
            yield return new SimpleTestCase(
                "OOB_01_SinglePacket_JsonPush_ZeroVisualLeakage", "OOB",
                "Parses single unchunked JSON push packet with zero visual leakage on terminal grid",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 40, height: 5);
                    OobPacket? receivedPacket = null;

                    session.Decoder.OobPacketReceived += (s, e) => receivedPacket = e.Packet;

                    // Send: \x1b_Va=p,i=101,t=json;eyJoZWxsbyI6IndvcmxkIn0=\x1b\
                    session.Feed("\x1B_Va=p,i=101,t=json;eyJoZWxsbyI6IndvcmxkIn0=\x1B\\");

                    ctx.Assert(receivedPacket != null, "OOB packet must be received");
                    ctx.Assert(receivedPacket!.Header.Action == "p", $"Expected action 'p', got '{receivedPacket.Header.Action}'");
                    ctx.Assert(receivedPacket.Header.Id == "101", $"Expected id '101', got '{receivedPacket.Header.Id}'");
                    ctx.Assert(receivedPacket.Header.PayloadType == "json", $"Expected type 'json', got '{receivedPacket.Header.PayloadType}'");
                    ctx.Assert(!receivedPacket.Header.IsMore, "IsMore must be false");

                    string payloadText = receivedPacket.GetPayloadAsText();
                    ctx.Assert(payloadText == "{\"hello\":\"world\"}", $"Expected '{{\"hello\":\"world\"}}', got '{payloadText}'");

                    // Verify zero visual leakage: every cell in screen buffer must remain blank
                    for (int y = 0; y < session.Buffer.Height; y++)
                    {
                        for (int x = 0; x < session.Buffer.Width; x++)
                        {
                            char c = session.Buffer[x, y].Char;
                            ctx.Assert(c == ' ' || c == '\0', $"Screen canvas leaked glyph '{c}' at ({x},{y})");
                        }
                    }
                });

            // 2. Unit Test - Multi-Chunk Reassembly
            yield return new SimpleTestCase(
                "OOB_02_MultiChunk_Reassembly_BitForBit", "OOB",
                "Reassembles 3 sequential chunks (m=1, m=1, m=0) matching original binary stream bit-for-bit",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 40, height: 5);
                    OobPacket? receivedPacket = null;

                    session.Decoder.OobPacketReceived += (s, e) => receivedPacket = e.Packet;

                    // Generate a 120-byte test binary stream
                    byte[] originalBinary = new byte[120];
                    for (int i = 0; i < originalBinary.Length; i++)
                    {
                        originalBinary[i] = (byte)((i * 7 + 13) % 256);
                    }

                    // Base64 encode whole payload then slice string into 3 chunks
                    string fullBase64 = Convert.ToBase64String(originalBinary);
                    int chunkLen = fullBase64.Length / 3;
                    string chunk1 = fullBase64.Substring(0, chunkLen);
                    string chunk2 = fullBase64.Substring(chunkLen, chunkLen);
                    string chunk3 = fullBase64.Substring(chunkLen * 2);

                    // Send chunk 1 with m=1
                    session.Feed($"\x1B_Va=p,i=stream-42,m=1,t=bop;{chunk1}\x1B\\");
                    ctx.Assert(receivedPacket == null, "Packet must not complete after chunk 1 (m=1)");

                    // Send chunk 2 with m=1
                    session.Feed($"\x1B_Va=p,i=stream-42,m=1,t=bop;{chunk2}\x1B\\");
                    ctx.Assert(receivedPacket == null, "Packet must not complete after chunk 2 (m=1)");

                    // Send chunk 3 with m=0
                    session.Feed($"\x1B_Va=p,i=stream-42,m=0,t=bop;{chunk3}\x1B\\");
                    ctx.Assert(receivedPacket != null, "Packet must complete after chunk 3 (m=0)");

                    ctx.Assert(receivedPacket!.Header.Id == "stream-42", "Correlation ID must match");
                    ctx.Assert(receivedPacket.Payload.Length == originalBinary.Length, 
                        $"Payload length mismatch: expected {originalBinary.Length}, got {receivedPacket.Payload.Length}");

                    // Bit-for-bit comparison
                    for (int i = 0; i < originalBinary.Length; i++)
                    {
                        ctx.Assert(receivedPacket.Payload[i] == originalBinary[i], 
                            $"Byte mismatch at offset {i}: expected {originalBinary[i]}, got {receivedPacket.Payload[i]}");
                    }
                });
        }
    }
}
