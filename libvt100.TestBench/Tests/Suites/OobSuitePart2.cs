using System;
using System.Collections.Generic;
using System.Text;
using libVT100;
using libVT100.Oob;
using libVT100.TestBench.Harness;

namespace libVT100.TestBench.Tests.Suites
{
    public static class OobSuitePart2
    {
        public static IEnumerable<ITestCase> GetTests()
        {
            // 3. Unit Test - Interleaved Traffic
            yield return new SimpleTestCase(
                "OOB_03_Interleaved_Traffic", "OOB",
                "Processes mixed text, cursor jumps, and OOB packets without canvas corruption",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 40, height: 5);
                    OobPacket? packet = null;
                    session.Decoder.OobPacketReceived += (s, e) => packet = e.Packet;

                    // QUJD is Base64 for "ABC"
                    session.Feed("Hello\x1B[2J\x1B_Va=p;QUJD\x1B\\World");

                    ctx.Assert(packet != null, "OOB packet must be received");
                    ctx.Assert(packet!.GetPayloadAsText() == "ABC", $"Expected 'ABC', got '{packet.GetPayloadAsText()}'");

                    string screenText = string.Join("\n", session.GetAllScreenRows());
                    ctx.Assert(!screenText.Contains("QUJD"), "Base64 leaked to canvas");
                    ctx.Assert(!screenText.Contains("ABC"), "Decoded payload leaked to canvas");
                    ctx.Assert(!screenText.Contains("_V"), "Header leaked to canvas");
                    ctx.Assert(screenText.Contains("World"), "Screen canvas must contain 'World'");
                });

            // 4. Unit Test - Upstream Injection
            yield return new SimpleTestCase(
                "OOB_04_Upstream_Injection_10KB_AutoChunking", "OOB",
                "Sends 10KB payload upstream via SendOobPacket with automatic multi-chunk slicing",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 40, height: 5);

                    byte[] payload10k = new byte[10000];
                    for (int i = 0; i < payload10k.Length; i++) payload10k[i] = (byte)(i & 0xFF);

                    session.Decoder.SendOobPacket("s", "up10k", "bop", payload10k, maxChunkSize: 4096);

                    byte[] emitted = session.EmittedOutput.ToArray();
                    ctx.Assert(emitted.Length > 0, "Emitted stream must not be empty");

                    string emittedText = Encoding.ASCII.GetString(emitted);
                    ctx.Assert(emittedText.Contains("\x1B_Va=s,i=up10k"), "Emitted stream must contain OOB APC prefix");
                    ctx.Assert(emittedText.Contains("m=1"), "Emitted stream must contain m=1 chunks");

                    var receiver = new AnsiDecoder();
                    OobPacket? reassembled = null;
                    receiver.OobPacketReceived += (s, e) => reassembled = e.Packet;

                    ((IDecoder)receiver).Input(emitted);

                    ctx.Assert(reassembled != null, "Emitted chunks must reassemble completely in receiver");
                    ctx.Assert(reassembled!.Header.Id == "up10k", "Reassembled ID must be 'up10k'");
                    ctx.Assert(reassembled.Payload.Length == payload10k.Length, 
                        $"Length mismatch: expected {payload10k.Length}, got {reassembled.Payload.Length}");

                    for (int i = 0; i < payload10k.Length; i++)
                    {
                        ctx.Assert(reassembled.Payload[i] == payload10k[i], $"Byte mismatch at {i}");
                    }
                });

            // 5. Unit Test - CAN/SUB Sequence Abort & Recovery
            yield return new SimpleTestCase(
                "OOB_05_CAN_SUB_Cancellation_Recovery", "OOB",
                "Cancels in-flight APC sequence on CAN (0x18) and restores ground state without leakage",
                ctx =>
                {
                    using var session = new HeadlessTerminalSession(width: 40, height: 5);
                    session.Decoder.dvt = new StringBuilder();
                    bool oobTriggered = false;
                    session.Decoder.OobPacketReceived += (s, e) => oobTriggered = true;

                    session.Feed("\x1B_Va=p,i=999\u0018Cancelled\x1B\\NormalText");

                    ctx.Assert(!oobTriggered, "OOB packet must not trigger after CAN cancellation. Trace: " + session.Decoder.dvt.ToString());

                    string screenText = string.Join("\n", session.GetAllScreenRows());
                    ctx.Assert(screenText.Contains("NormalText"), "Screen must recover and render text following cancellation");
                });

            // 6. Unit Test - Z85 Codec and OOB e=b85
            yield return new SimpleTestCase(
                "OOB_06_Z85_Encoding_And_Decoding", "OOB",
                "Encodes and decodes binary payloads using ZeroMQ RFC 32 Z85 representation",
                ctx =>
                {
                    byte[] rfc32 = new byte[8] { 0x86, 0x4F, 0xD2, 0x6F, 0xB5, 0x59, 0xF7, 0x5B };
                    string encoded = Z85.Encode(rfc32);
                    ctx.Assert(encoded == "HelloWorld", $"Expected 'HelloWorld', got '{encoded}'");

                    byte[] decoded = Z85.Decode(encoded);
                    ctx.Assert(decoded.Length == 8, "Decoded length must be 8");
                    for (int i = 0; i < 8; i++) ctx.Assert(decoded[i] == rfc32[i], $"Mismatch at {i}");

                    using var session = new HeadlessTerminalSession(width: 40, height: 5);
                    OobPacket? received = null;
                    session.Decoder.OobPacketReceived += (s, e) => received = e.Packet;

                    session.Feed($"\x1B_Va=p,i=z85test,e=b85;{encoded}\x1B\\");

                    ctx.Assert(received != null, "Z85 packet must be received");
                    ctx.Assert(received!.Payload.Length == 8, "Payload length must be 8");
                    for (int i = 0; i < 8; i++) ctx.Assert(received.Payload[i] == rfc32[i], $"OOB Z85 mismatch at {i}");
                });
            // 7. Unit Test - Sliding Timeout Assembly Cleanup
            yield return new SimpleTestCase(
                "OOB_07_SlidingTimeout_StreamCleanup", "OOB",
                "Evicts orphaned in-flight chunk assemblies exceeding the sliding timeout threshold",
                ctx =>
                {
                    var reassembler = new OobChunkReassembler(maxPayloadSize: 1024, timeout: TimeSpan.FromMilliseconds(50));
                    var header = new OobHeader { Action = "s", Id = "abandoned", IsMore = true };

                    bool ok = reassembler.ProcessChunk(header, "Chunk1Data", out var packet, out var err);
                    ctx.Assert(ok && packet == null, "Chunk 1 buffered");
                    ctx.Assert(reassembler.ActiveAssembliesCount == 1, "Should have 1 active assembly");

                    System.Threading.Thread.Sleep(70);
                    reassembler.CleanupExpired();
                    ctx.Assert(reassembler.ActiveAssembliesCount == 0, "Abandoned assembly must be evicted after timeout");
                });

            // 8. Unit Test - Max Payload Size Enforcement
            yield return new SimpleTestCase(
                "OOB_08_MaxPayloadSize_Enforcement", "OOB",
                "Rejects and purges chunk assemblies that exceed configured memory quota to prevent DoS",
                ctx =>
                {
                    var reassembler = new OobChunkReassembler(maxPayloadSize: 50);
                    var header = new OobHeader { Action = "s", Id = "oversize", IsMore = true };

                    string bigChunk = new string('A', 60);
                    bool ok = reassembler.ProcessChunk(header, bigChunk, out var packet, out var error);

                    ctx.Assert(!ok, "Oversized chunk must be rejected");
                    ctx.Assert(packet == null, "No packet emitted");
                    ctx.Assert(error != null && error.Contains("exceeded"), $"Error message must state limit exceeded: '{error}'");
                    ctx.Assert(reassembler.ActiveAssembliesCount == 0, "Active assembly must be purged on overflow");
                });

        }
    }
}
