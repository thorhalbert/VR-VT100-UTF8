# Out-of-Band (OOB) APC Streaming Protocol Specification

## 1. Overview & Motivation

Modern terminal applications and remote graphical terminals (such as WebAssembly, SSH, and cloud-hosted framebuffer consoles) frequently require exchanging structured, high-throughput binary or typed metadata alongside standard terminal text. Examples include:
- Binary state sync (e.g., Bebop serialization, Protobuf).
- Large framebuffer diffs or pixel slice transmissions (`fb`).
- JSON RPC command/response and structured telemetry.
- Terminal-to-app queries (capabilities, client metrics, window state).

Traditionally, mixing binary or structured data with terminal streams required dedicated sidecar channels (e.g. secondary TCP/WebSocket connections, unix domain sockets) or risked corrupting the terminal visual grid if escape sequences leaked into stdout.

The **Velocity Out-of-Band (OOB) Protocol** embeds structured, bidirectional packet streams directly within standard terminal stdin/stdout channels using standard ECMA-48 / ANSI **APC (Application Program Command)** escape sequences (`\x1b_ ... \x1b\`).

### Key Design Principles
1. **Zero Visual Leakage**: The terminal parser intercepts all `\x1b_V` sequences before normal character decoding; no visual glyphs are rendered, and the cursor position is undisturbed.
2. **Channel Multiplexing**: OOB packets are interleaved synchronously with ANSI escape sequences (CUP, SGR, DECSTBM, etc.) without requiring secondary sockets.
3. **Multi-Chunk Streaming**: Payloads exceeding transport MTU (e.g., > 4096 bytes) are split into sequential slices (`m=1`) and reassembled atomically on receipt (`m=0`).
4. **DoS Resilience**: Reassembly enforces strict maximum memory limits (64 MiB) and sliding timeouts (5 seconds).
5. **Bidirectional**: Supports downstream delivery (App stdout -> Terminal) and upstream injection (Terminal -> App stdin).
6. **Cancellation Safety**: Immediate parser recovery upon encountering `CAN` (0x18), `SUB` (0x1A), or `ST`.

---

## 2. Protocol Specification

### A. Envelope Syntax

```text
\x1b_V<HeaderControls>;<PayloadChunk>\x1b\
```

- `\x1b_` (`0x1B 0x5F`): 7-bit APC (Application Program Command) Initiator. (Also supports 8-bit C1 APC `0x9F`).
- `V`: Protocol namespace identifier (**Velocity** / Virtual Framebuffer / Genius-Loci).
- `<HeaderControls>`: Comma-separated `key=value` ASCII metadata pairs (case-insensitive keys).
- `;` (`0x3B`): Semicolon delimiter separating header controls from the payload chunk.
- `<PayloadChunk>`: Encoded payload string (Base64, Z85, or UTF-8 text).
- `\x1b\` (`0x1B 0x5C` or `0x9C`) or `\x07` (`BEL`): String Terminator (ST).

### B. Standard Header Keys

| Key | Name | Type | Description |
|---|---|---|---|
| `a` | **Action** | `string` | Operation type: `p` (Push), `q` (Query), `r` (Response), `e` (Event), `s` (Stream). |
| `i` | **Correlation ID** | `string` | Unique request/stream ID used for async request-response matching and multi-chunk defragmentation. |
| `m` | **More Chunks** | `0` or `1` | `1` indicates more chunks follow in this stream; `0` indicates the last/final chunk. |
| `t` | **Payload Type** | `string` | MIME/type hint: `bop` (Bebop binary), `json` (JSON), `raw` (text), `fb` (framebuffer slice). |
| `e` | **Encoding** | `string` | Payload encoding: `b64` (Base64, default), `b85` (Z85 / Base-85), `raw` (unencoded UTF-8). |
| `s` | **Status** | `int` | Execution status: `0` = Success/OK, `>0` = Error code. |
| `*` | **Extensions** | `string` | Arbitrary custom metadata key-value pairs (e.g. `sender=node1`, `pad=2`, `ver=1`). |

### C. Example Packets

#### 1. Single Unchunked JSON Push (`a=p`, `t=json`)
```text
\x1b_Va=p,i=101,t=json;eyJoZWxsbyI6IndvcmxkIn0=\x1b\
```
- Action: `p` (push)
- ID: `101`
- Type: `json`
- Payload: `{"hello":"world"}` (Base64 encoded)

#### 2. Control Query without Payload (`a=q`)
```text
\x1b_Va=q,i=202,t=fb;\x1b\
```
- Action: `q` (query)
- ID: `202`
- Type: `fb`
- Payload: empty

#### 3. Error Response with Status Code (`a=r`, `s=404`)
```text
\x1b_Va=r,i=101,s=404,t=raw;Tm90IEZvdW5k\x1b\
```
- Action: `r` (response)
- ID: `101`
- Status: `404`
- Payload: `Not Found`

---

## 3. Multi-Chunk Streaming Pattern (`m=1` / `m=0`)

When a serialized payload exceeds transport or buffer MTU (typically 4096 bytes), the sender splits the encoded string or binary payload into consecutive slices sharing identical correlation IDs `i`:

```text
Chunk 1: \x1b_Va=p,i=stream99,m=1,t=bop;AAAA...AAAA\x1b\
Chunk 2: \x1b_Va=p,i=stream99,m=1,t=bop;BBBB...BBBB\x1b\
Chunk 3: \x1b_Va=p,i=stream99,m=0,t=bop;CCCC...CCCC\x1b\
```

### Reassembly Rules
1. **Correlation Key**: Assemblies are keyed by `sender:id` (or `id` alone if sender is unstated).
2. **Buffer Accumulation**: The receiver appends incoming chunks in order until `m=0` is received.
3. **Atomic Completion**: Upon arrival of the `m=0` terminating chunk, the payload buffer is decoded and dispatched as an atomic `OobPacket` to subscribed handlers.
4. **Sliding Timeout**: Active assemblies are discarded if no subsequent chunk is received within the timeout window (default 5.0 seconds).
5. **Memory Limit**: Assemblies exceeding `MaxPayloadSize` (default 64 MiB) are immediately aborted to prevent denial-of-service memory exhaustion.

---

## 4. Encodings

| Code | Name | Description |
|---|---|---|
| `b64` | **Base64** | Standard RFC 4648 Base64 encoding. Default if `e=` is omitted. |
| `b85` | **Z85** | ZeroMQ RFC 32 Base-85 encoding. 4 binary octets map to 5 printable ASCII characters (25% overhead vs Base64's 33%). |
| `raw` | **Raw Text** | Direct UTF-8 string pass-through for human-readable ASCII/JSON control messages. |

---

## 5. Downstream Parser Architecture (App stdout -> Terminal)

The terminal stream decoder processes input bytes through an internal state machine:
```text
[GROUND] --(ESC)--> [ESCAPE] --('_')--> [APC_STRING]
                                             |
                                             +--(Prefix 'V')--> Buffer header & payload
                                             +--(ST or BEL)  --> Emit OobPacketReceived -> [GROUND]
                                             +--(CAN or SUB) --> Abort to [GROUND]
```

1. **APC Detection**: When `ESC _` (`0x1B 0x5F`) or C1 `0x9F` arrives, the decoder enters `State.CommandAPC`.
2. **Zero Visual Leakage**: While in `CommandAPC`, incoming bytes are diverted into an internal buffer. No glyphs are placed into the `TerminalFrameBuffer`, and the cursor is not repositioned.
3. **Punctuation Safety**: Semicolon `;` separates `<HeaderControls>` from `<PayloadChunk>`. Whitespace around keys or values is trimmed, and keys are parsed case-insensitively.
4. **Malformed Packet & Cancellation Recovery**: If `CAN` (`0x18`) or `SUB` (`0x1A`) is encountered, the in-flight APC sequence is canceled immediately, returning the decoder cleanly to `State.Normal` (`GROUND`) without stream desynchronization.

---

## 6. Upstream Dispatcher Architecture (Terminal -> App stdin)

The terminal emulator or client application can inject OOB packets upstream to the host PTY via `SendOobPacket`:
```csharp
decoder.SendOobPacket(
    action: "r",
    id: "req101",
    type: "json",
    payload: Encoding.UTF8.GetBytes("{\"result\":\"ok\"}")
);
```

For large payloads (e.g. 100 KB), `OobPacketEncoder` automatically slices the payload into `maxChunkSize` blocks (default 4096 bytes) and applies `m=1` to all intermediate slices and `m=0` to the final slice.

---

## 7. C# API & Consumer Integration

### A. Subscribing to OOB Packets
```csharp
// 1. General event subscription
decoder.OobPacketReceived += (sender, e) =>
{
    Console.WriteLine($"Received OOB Action: {e.Header.Action}, ID: {e.Header.Id}");
    if (e.Header.PayloadType == "json")
    {
        var model = e.Packet.GetPayloadAsJson<MyTelemetry>();
    }
};

// 2. Strongly-typed action handler
decoder.RegisterOobHandler("p", (header, payload) =>
{
    // Handle Push packets
});

// 3. Class-based handler interface
public class FramebufferSyncHandler : IOobPacketHandler
{
    public void HandlePacket(OobHeader header, ReadOnlySpan<byte> payload)
    {
        // Process binary framebuffer slice
    }
}
decoder.RegisterOobHandler("fb", new FramebufferSyncHandler());
```

### B. Sending OOB Packets Upstream
```csharp
byte[] binaryPayload = GetSerializedBebopData();

decoder.SendOobPacket(
    action: "s",
    id: "stream-01",
    type: "bop",
    payload: binaryPayload,
    maxChunkSize: 4096,
    encoding: "b85"
);
```

