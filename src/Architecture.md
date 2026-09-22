# Architecture & Technical Reference Manual: `libvt100`

`libvt100` is a high-performance C# terminal emulator engine and character-cell framebuffer. It conforms to:
- **ECMA-48 / ISO 6429** (Control Functions for Coded Character Sets)
- **DEC STD 070 / EK-VT220-RM** (Digital Equipment Corporation VT100 & VT220 Specifications)
- **X11 xterm Control Sequences** (Thomas Dickey & X.Org Foundation)

---

## 1. System Architecture & Component Interaction

The library is organized into a clean layered architecture separating stream decoding, sequence parsing, event dispatching, and framebuffer rendering:

```
                            ┌───────────────────────────────┐
                            │      Raw Byte Stream          │
                            │   (TCP, SSH, PTY, or Test)    │
                            └───────────────┬───────────────┘
                                            │ byte[] Input
                                            ▼
                            ┌───────────────────────────────┐
                            │    EscapeCharacterDecoder     │
                            │   - C0/C1 Control Detection   │
                            │   - Parameter Accumulation    │
                            │   - Packet Reassembly         │
                            └───────────────┬───────────────┘
                                            │ Command Callbacks
                                            ▼
                            ┌───────────────────────────────┐
                            │          AnsiDecoder          │
                            │   - CSI / OSC / C2 Dispatch   │
                            │   - SGR Rendition Engine      │
                            │   - DA / CPR / Window Reports │
                            │   - Standard Trace Logging    │
                            └───────────────┬───────────────┘
                                            │ IAnsiDecoderClient
                                            ▼
                            ┌───────────────────────────────┐
                            │      TerminalFrameBuffer      │
                            │   - Primary & Alternate Grids │
                            │   - DECSTBM & DECSLRM Margins │
                            │   - Back Color Erase (BCE)    │
                            │   - Deferred Autowrap (xenl)  │
                            │   - ISO 2022 ACS Line Drawing │
                            └───────────────────────────────┘
```

### Component Roles

| Component | Interface / Base | Responsibility |
|---|---|---|
| `IDecoder` | Interface | Root stream interface consuming raw `byte[]` payloads and firing host `Output` events. |
| `EscapeCharacterDecoder` | Abstract Class | Low-level streaming tokenizer. Separates printable UTF-8 text from C0 controls, 7-bit (`ESC ...`), and 8-bit (`C1`) escape sequences without heap thrashing. Resilient to packet fragmentation. |
| `IAnsiDecoder` | Interface | Extends `IDecoder` with client subscription (`Subscribe`/`UnSubscribe`) and trace logging (`deb`/`dvt`). |
| `AnsiDecoder` | Class | Concrete parser that decodes Control Sequence Introducers (CSI), Operating System Commands (OSC), Two-Character sequences (C2), and Character Set Designations. Evaluates 256/TrueColor SGR attributes and responds to host queries. |
| `IAnsiDecoderClient` | Interface | The rendering contract decoupling the parser from storage. Exposes cursor addressing, display clearing, scrolling margins, line/character editing, and buffer modes. |
| `TerminalFrameBuffer` | Class | Concrete 2D character-cell framebuffer implementing `IAnsiDecoderClient`. Manages primary and alternate screen buffers (`1049`), top/bottom (`DECSTBM`) and left/right (`DECSLRM`) margins, Back Color Erase (BCE), deferred autowrap (`xenl`), and ACS line-drawing translation. |

---

## 2. The Streaming Pipeline

### A. Tokenization & Fragmentation Resilience
Network protocols (such as SSH, Telnet, or WebSockets) may fragment escape sequences across packet boundaries. For example, `\x1B[10;20H` may arrive as `\x1B[1` in packet 1 and `0;20H` in packet 2.

`EscapeCharacterDecoder.ProcessCommandBuffer()` maintains an internal state machine:
- **`InternalState.Command`**: Identifies CSI (`ESC [`), OSC (`ESC ]`), DCS (`ESC P`), or 2-character escape codes.
- **`InternalState.Parameters`**: Gathers decimal digits, semicolons (`;`), colons (`:`), and private parameter markers (`?`, `>`, `=`, `<`).
- **`InternalState.Terminator`**: Captures final command bytes (`A`..`Z`, `a`..`z`, `@`, etc.).
- **Incomplete Sequences**: If a stream ends mid-sequence, the buffer is preserved intact. When subsequent bytes arrive, parsing resumes seamlessly without throwing exceptions or corrupting text.

### B. C0 Controls vs. Printable Text
- Printable characters ($0\text{x}20..0\text{x}7\text{E}$ and multi-byte UTF-8 sequences) pass to `IAnsiDecoderClient.Characters`.
- C0 controls execute immediately:
  - `\r` (CR, 0x0D): Homes cursor to column 0; resets pending autowrap.
  - `\n` (LF, 0x0A): Line feed down (scrolls if at bottom margin); resets pending autowrap.
  - `\b` (BS, 0x08): Cursor backward 1 cell, strictly clamped to column 0.
  - `\t` (TAB, 0x09): Advances cursor to next tab stop without cell overwriting.
  - `^G` (BEL, 0x07): Bell event.
  - `\x0E` (SO) / `\x0F` (SI): Shift Out (G1) / Shift In (G0) character set invocation.

---

## 3. Framebuffer Core Mechanisms

### A. Dual Screen Buffers (Alternate Screen Buffer `1049`)
Full-screen TUI applications (such as `vim`, `nano`, `htop`, `tmux`, and `curses`) switch between normal and alternate screens:
- **Primary Screen Buffer (`primaryScreenBuffer`)**: Retains command-line history and shell output.
- **Alternate Screen Buffer (`alternateScreenBuffer`)**: A dedicated viewport without scrollback history.
- **Lifecycle on `CSI ? 1049 h`**:
  1. Saves active cursor position, video attributes, and character sets (DECSC).
  2. Swaps `currentScreenBuffer` to `alternateScreenBuffer`.
  3. Clears the alternate buffer using active BCE attributes and homes the cursor to (0,0).
  4. Preserves `primaryScreenBuffer` completely unaltered in memory.
- **Lifecycle on `CSI ? 1049 l`**:
  1. Swaps `currentScreenBuffer` back to `primaryScreenBuffer`.
  2. Restores saved primary cursor position and attributes (DECRC).
  3. Dispatches refresh events to synchronize the UI.

### B. Scrolling Margins (`DECSTBM` & `DECSLRM`)
- **Top/Bottom Margins (`DECSTBM`, `CSI top ; bottom r`)**:
  - Restricts vertical scrolling and line insertion/deletion between rows `top` and `bottom`.
  - Content above `top` (e.g. status headers) and below `bottom` (e.g. status bars) is completely protected from line scrolling.
- **Left/Right Margins (`DECSLRM`, `CSI left ; right s` with `CSI ? 69 h`)**:
  - Constrains horizontal wrapping and column editing between columns `left` and `right`.

### C. Back Color Erase (BCE)
Under modern xterm conventions, whenever cells are cleared or scrolled into view via `ED`, `EL`, `ECH`, `DL`, `IL`, `DCH`, or `ScrollScreen`, the new blank cells adopt the **currently active background color** (`m_currentAttributes.Background` or `BackgroundRgb`) rather than resetting to default black.

### D. Deferred Autowrap (`xenl` / "Eat Newline Glitch")
When characters reach the right edge:
1. Writing a character into column `Width - 1` places the character and leaves the cursor at column `Width - 1`, setting `m_pendingWrap = true`.
2. If the next character is printable, it wraps to column 0 on the next line (scrolling if at `bottomMargin`) and clears `m_pendingWrap`.
3. If the next character is `\r` (CR) or `\n` (LF), `m_pendingWrap` is canceled without causing double-line advancement.

### E. DEC Alternate Character Set (ACS / Line Drawing)
`libvt100` supports ISO 2022 G0 and G1 character set switching. When the active character set is designated as DEC Special Graphics (`\E(0` or `\E)0` + `SO` `\x0E`), character codes in the range $0\text{x}60..0\text{x}7\text{E}$ map directly to Unicode Box Drawing characters:
- `lqqk` $\to$ `┌──┐`, `x  x` $\to$ `│  │`, `mqqj` $\to$ `└──┘`
- `t` $\to$ `├`, `u` $\to$ `┤`, `v` $\to$ `┴`, `w` $\to$ `┬`, `n` $\to$ `┼`, `q` $\to$ `─`, `x` $\to$ `│`

---

## 4. Comprehensive Escape Sequence Catalog

### A. Cursor Movement & Positioning

| Sequence | Name | Standard | Description |
|---|---|---|---|
| `\E[H` / `\E[f` | `CUP` / `HVP` | ECMA-48 | Move cursor to Home (1, 1). |
| `\E[{r};{c}H` | `CUP` | ECMA-48 | Move cursor to row `r`, column `c` (1-based). |
| `\E[{n}A` | `CUU` | ECMA-48 | Move cursor up `n` rows (default 1). |
| `\E[{n}B` | `CUD` | ECMA-48 | Move cursor down `n` rows (default 1). |
| `\E[{n}C` | `CUF` | ECMA-48 | Move cursor forward `n` columns (default 1). |
| `\E[{n}D` | `CUB` | ECMA-48 | Move cursor backward `n` columns, clamped at column 0. |
| `\E[{n}E` | `CNL` | ECMA-48 | Move cursor to column 0 of line `n` rows below. |
| `\E[{n}F` | `CPL` | ECMA-48 | Move cursor to column 0 of line `n` rows above. |
| `\E[{c}G` / `\E[{c}\`` | `CHA` / `HPA` | ECMA-48 | Move cursor to absolute column `c` (1-based). |
| `\E[{r}d` | `VPA` | ECMA-48 | Move cursor to absolute row `r` (1-based). |
| `\E[{n}a` | `HPR` | ECMA-48 | Move cursor forward `n` columns (relative). |
| `\E[{n}e` | `VPR` | ECMA-48 | Move cursor downward `n` rows (relative). |
| `\E[{n}Z` | `CBT` | ECMA-48 | Move cursor backward `n` tab stops. |
| `\E7` / `\E[s` | `DECSC` / `SCOSC` | DEC / ANSI | Save cursor position, attributes, charset, and autowrap state. |
| `\E8` / `\E[u` | `DECRC` / `SCORC` | DEC / ANSI | Restore cursor position, attributes, charset, and autowrap state. |

### B. Display, Line, and Character Editing

| Sequence | Name | Standard | Description |
|---|---|---|---|
| `\E[J` / `\E[0J` | `ED 0` | ECMA-48 | Erase from cursor to end of screen (using BCE attributes). |
| `\E[1J` | `ED 1` | ECMA-48 | Erase from top of screen to cursor (using BCE attributes). |
| `\E[2J` | `ED 2` | ECMA-48 | Erase entire display (does not home cursor or reset attributes). |
| `\E[3J` | `ED 3` | xterm | Erase scrollback history (clears saved lines buffer). |
| `\E[K` / `\E[0K` | `EL 0` | ECMA-48 | Erase from cursor to end of line (using BCE attributes). |
| `\E[1K` | `EL 1` | ECMA-48 | Erase from start of line to cursor (using BCE attributes). |
| `\E[2K` | `EL 2` | ECMA-48 | Erase entire line (using BCE attributes). |
| `\E[{n}L` | `IL` | ECMA-48 | Insert `n` lines at cursor within scrolling margins; shifts lines down. |
| `\E[{n}M` | `DL` | ECMA-48 | Delete `n` lines at cursor within scrolling margins; shifts lines up. |
| `\E[{n}@` | `ICH` | ECMA-48 | Insert `n` blank characters at cursor; shifts line content right. |
| `\E[{n}P` | `DCH` | ECMA-48 | Delete `n` characters at cursor; shifts line content left. |
| `\E[{n}X` | `ECH` | ECMA-48 | Erase `n` characters starting at cursor without moving cursor. |
| `\E[{n}b` | `REP` | ECMA-48 | Repeat preceding graphic character `n` times. |

### C. Margins & Scrolling

| Sequence | Name | Standard | Description |
|---|---|---|---|
| `\E[{t};{b}r` | `DECSTBM` | DEC VT100 | Set top margin `t` and bottom margin `b` (1-based). Homes cursor. |
| `\E[?69h` / `\E[?69l`| `DECLRMM` | DEC / xterm | Enable / disable Left and Right Margin Mode. |
| `\E[{l};{r}s` | `DECSLRM` | DEC / xterm | Set left margin `l` and right margin `r` (when DECLRMM is set). |
| `\ED` | `IND` | DEC / ECMA-48 | Index: move down 1 line; scrolls upward if at bottom margin. |
| `\EM` | `RI` | DEC / ECMA-48 | Reverse Index: move up 1 line; scrolls downward if at top margin. |
| `\EE` | `NEL` | DEC / ECMA-48 | Next Line: move to column 0 of next line; scrolls if at bottom margin. |
| `\E[{n}S` | `SU` | ECMA-48 | Scroll up `n` lines within scrolling margins. |
| `\E[{n}T` | `SD` | ECMA-48 | Scroll down `n` lines within scrolling margins. |
| `\El` / `\Em` | `MEM_LOCK` | DEC / HP | Lock display above cursor as top margin (`\El`); unlock (`\Em`). |


### D. Select Graphic Rendition (SGR) Video Attributes & Colors

| SGR Code | Description |
|---|---|
| `0` | Reset all video attributes and colors to default. |
| `1` / `22` | Bold (increased intensity) on / off. |
| `2` / `22` | Faint (decreased intensity / dim) on / normal intensity. |
| `3` / `23` | Italic font on / off. |
| `4` / `24` | Single underline on / off. |
| `5` / `25` | Slow blink on / off. |
| `6` / `25` | Rapid blink on / off. |
| `7` / `27` | Inverse / reverse video on / off (swaps foreground and background). |
| `8` / `28` | Conceal / hidden on / reveal off. |
| `9` / `29` | Crossed-out / strikethrough on / off. |
| `21` / `24` | Double underline on / off. |
| `53` / `55` | Overline decoration on / off. |
| `30..37` | Standard foreground colors (Black, Red, Green, Yellow, Blue, Magenta, Cyan, White). |
| `39` | Reset foreground color to terminal default. |
| `40..47` | Standard background colors. |
| `49` | Reset background color to terminal default. |
| `90..97` | High-intensity / bright foreground colors (aixterm). |
| `100..107` | High-intensity / bright background colors (aixterm). |
| `38;5;{n}` / `48;5;{n}` | Extended 256-color palette foreground / background (`n` in 0..255). |
| `38;2;{r};{g};{b}` | 24-bit TrueColor RGB foreground (semicolon syntax). |
| `48;2;{r};{g};{b}` | 24-bit TrueColor RGB background (semicolon syntax). |
| `38:2::{r}:{g}:{b}` | 24-bit TrueColor RGB foreground (ITU-T colon syntax). |
| `48:2::{r}:{g}:{b}` | 24-bit TrueColor RGB background (ITU-T colon syntax). |

### E. Device Modes (SM / RM / DECSET / DECRST)

| Parameter | Mode | Standard | Description |
|---|---|---|---|
| `4` | `IRM` | ECMA-48 | Insert / Replace Mode (`h` = Insert mode, `l` = Replace mode). |
| `20` | `LNM` | ECMA-48 | Linefeed Mode (`h` = Newline / CRLF, `l` = pure LF). |
| `?1` | `DECCKM` | DEC VT100 | Cursor Key Mode (`h` = Application `\EOx`, `l` = Normal `\E[x`). |
| `?2` | `DECANM` | DEC VT100 | ANSI / VT52 Mode (`h` = ANSI mode, `l` = VT52 mode). |
| `?3` | `DECCOLM` | DEC VT100 | Column Mode (`h` = 132 columns, `l` = 80 columns). |
| `?4` | `DECSCLM` | DEC VT100 | Scrolling Mode (`h` = Smooth scroll, `l` = Jump scroll). |
| `?5` | `DECSCNM` | DEC VT100 | Screen Mode (`h` = Reverse video screen, `l` = Normal screen). |
| `?6` | `DECOM` | DEC VT100 | Origin Mode (`h` = Relative to top margin, `l` = Absolute 1,1). |
| `?7` | `DECAWM` | DEC VT100 | Autowrap Mode (`h` = Autowrap with `xenl`, `l` = Truncate at margin). |
| `?8` | `DECARM` | DEC VT100 | Auto-repeat Mode (`h` = Keys auto-repeat, `l` = No repeat). |
| `?25` | `DECTCEM` | DEC VT220 | Text Cursor Enable Mode (`h` = Show cursor, `l` = Hide cursor). |
| `?69` | `DECLRMM` | DEC / xterm | Left/Right Margin Mode (`h` = Enable DECSLRM, `l` = Disable). |
| `?47` / `?1047` | `ALTSCREEN` | xterm | Alternate Screen Buffer (`h` = Alternate screen, `l` = Primary screen). |
| `?1048` | `CURSOR_SAVE` | xterm | Save (`h`) / Restore (`l`) cursor position. |
| `?1049` | `ALTSCREEN_CA` | xterm | Alternate Screen Buffer with automatic cursor save/restore and screen clearing. |

### F. Terminal Reports & Window Manipulation

| Sequence | Response | Description |
|---|---|---|
| `\E[6n` (DSR 6) | `\E[{row};{col}R` | Cursor Position Report (CPR, 1-based coordinates). |
| `\E[5n` (DSR 5) | `\E[0n` | Device Status Report (0 = Terminal OK). |
| `\E[c` / `\E[0c` (DA1) | `\E[?62;1;2;6;7;8;9c` | Primary Device Attributes (Reports VT220 ID). |
| `\E[>c` / `\E[>0c` (DA2)| `\E[>0;10;0c` | Secondary Device Attributes (Reports xterm ID). |
| `\E[18t` | `\E[8;{Height};{Width}t` | Report text area size in characters. |
| `\E[19t` | `\E[9;{Height};{Width}t` | Report screen size in characters. |
| `\E[22;0;0t` | (none) | Push window title onto xterm title stack. |
| `\E[23;0;0t` | (none) | Pop window title from xterm title stack. |
| `\E[!p` | (none) | `DECSTR`: Soft Terminal Reset. |
| `\Ec` | (none) | `RIS`: Reset to Initial State (Hard reset). |

---

## 5. Standardized Debugger Jargon

When debug logging is enabled via `decoder.dvt = new StringBuilder()`, `AnsiDecoder` outputs standard-prefixed tokens:
- **`[ANSI:...]`**: ECMA-48 / ANSI controls (e.g. `[ANSI:CUP(r,c)]`, `[ANSI:ED(Both)]`, `[ANSI:IL(1)]`, `[ANSI:REP(5)]`).
- **`[DEC:...]`**: Digital Equipment Corporation controls (e.g. `[DEC:DECSTBM(2,20)]`, `[DEC:DECSC]`, `[DEC:DECRC]`, `[DEC:DECAWM_SET]`, `[DEC:DA1]`, `[DEC:DA2]`).
- **`[XTERM:...]`**: X11 xterm extensions (e.g. `[XTERM:ALTSCREEN_ENTER(1049)]`, `[XTERM:COLOR_RGB_FG(r,g,b)]`, `[XTERM:ED_SCROLLBACK(3)]`, `[XTERM:WIN_REPORT_TEXTAREA(80x24)]`).
- **`[KITTY:...]`**: Kitty terminal extensions (e.g. `[KITTY:UNDERLINE_STYLE(Curly)]`, `[KITTY:UNDERLINE_COLOR_RGB(r,g,b)]`, `[KITTY:DECSCUSR(style)]`, `[KITTY:OSC_HYPERLINK:...]`, `[KITTY:SYNC_OUTPUT_START]`, `[KITTY:KEYBOARD_QUERY]`).

These prefixes allow automated test verification and human diagnostics.

---

## 6. Kitty Non-Bitmap Extensions

The Kitty terminal emulator introduced several modern terminal protocol enhancements beyond standard xterm. `libvt100` implements the non-bitmap subset of these extensions:

### A. Extended Underline Styles (`SGR 4:x`)
Standard ANSI SGR 4 supports single straight underlines, and SGR 21 specifies double underlines. Kitty introduces colon-delimited subparameter syntax for extended underline styles:
- `\E[4:0m`: Underline None (`TerminalFrameBuffer.Underline.None`)
- `\E[4:1m`: Single Straight Underline (`TerminalFrameBuffer.Underline.Single`)
- `\E[4:2m`: Double Straight Underline (`TerminalFrameBuffer.Underline.Double`)
- `\E[4:3m`: Curly / Wavy Underline (`TerminalFrameBuffer.Underline.Curly`)
- `\E[4:4m`: Dotted Underline (`TerminalFrameBuffer.Underline.Dotted`)
- `\E[4:5m`: Dashed Underline (`TerminalFrameBuffer.Underline.Dashed`)

### B. Underline Colors (`SGR 58` / `SGR 59`)
Allows styling underline decorations in a distinct color independent of foreground text:
- `\E[58;2;{r};{g};{b}m` or `\E[58:2::{r}:{g}:{b}m`: 24-bit TrueColor RGB underline.
- `\E[58;5;{idx}m` or `\E[58:5:{idx}m`: 256-color palette underline.
- `\E[59m`: Reset underline color to match foreground text (`UnderlineColor = null`).

### C. Cursor Shape & Blinking (`DECSCUSR` / `OSC 12` / `OSC 112`)
Configures cursor rendering and blinking mode:
- `\E[0 q` / `\E[1 q`: Blinking character block cursor.
- `\E[2 q`: Steady character block cursor.
- `\E[3 q`: Blinking underline cursor.
- `\E[4 q`: Steady underline cursor.
- `\E[5 q`: Blinking bar / I-beam cursor.
- `\E[6 q`: Steady bar / I-beam cursor.
- `\E]12;{color}\x07` or `\E]12;{color}\E\`: Custom cursor color override.
- `\E]112\x07` or `\E]112\E\`: Reset cursor color to default (`CursorColor = null`).

### D. Explicit Hyperlinks (`OSC 8`)
Enables clickable hyperlinks in terminal text:
- `\E]8;id={id};{url}\E\{text}\E]8;;\E\`: Associates text with a URI target.
- Uses an **interned URL table** in `TerminalFrameBuffer` and a `ushort HyperlinkId` (0 = no link, 1..65535 = index) on `GraphicAttributes` to avoid string allocations per terminal cell.

### E. Synchronized Output / Atomic Frames (`Mode ?2026 h/l`)
Suppresses screen tearing by batching UI action events into atomic rendering frames:
- `\E[?2026h`: Enter synchronized update. Action events generated by buffer modifications are queued without firing.
- `\E[?2026l`: Exit synchronized update. Flushes queued UI actions atomically via `TerminalFrameBuffer.ActionFlush()`.

### F. Desktop Notifications (`OSC 99`)
Routes desktop notifications directly through terminal escape sequences:
- `\E]99;i={id}:d={duration}:p={priority};{title}\n{body}\E\`: Emits structured notification events to decoder listeners.

### G. Shell Integration (`OSC 133`)
Supports FinalTerm / Semantic Prompts shell integration:
- `\E]133;A\E\`: Prompt start.
- `\E]133;B\E\`: Command line start.
- `\E]133;C\E\`: Command execution start.
- `\E]133;D;{exit_code}\E\`: Command finished with exit code.

### H. Kitty Keyboard Protocol Probing (`CSI ? u`)
- TUIs querying terminal capabilities via `\E[?u` receive `\E[?0u` (standard flags active, no extended flags) to ensure full compatibility without hanging modern TUI applications (e.g. Helix, Neovim, Bubbletea).

---

## 7. Kitty Graphics Protocol (Bitmap Extensions)

The Kitty Graphics Protocol allows terminal applications to transmit and render arbitrary bitmap graphics directly in the terminal emulator window. `libvt100` implements the complete specification:

### A. Envelope Syntax
```text
\E_G<ControlKeys>;<Payload>\E\
```
- Introducer: APC `\E_G` (`0x1B 0x5F 0x47` or 8-bit `0x9F 0x47`).
- Controls: Comma-separated `key=value` ASCII metadata pairs.
- Payload: Base64-encoded pixel or image data.
- Terminator: ST `\E\` (`0x1B 0x5C`) or BEL (`0x07`).

### B. Core Action Verbs (`a=...`)
- `a=t`: Transmit image data only (buffered in `TerminalFrameBuffer.StoredImages` under ID `i`), without placing on screen.
- `a=T`: Transmit image data and immediately place on screen at cursor position (default if `a` is omitted).
- `a=p`: Put a previously transmitted image (`i=...`) onto screen with optional placement ID (`p=...`), Z-index (`z=...`), crop (`x`, `y`, `w`, `h`), and sub-cell offset (`X`, `Y`).
- `a=d`: Delete images and/or placements matching specific filters (`d=a` all, `d=i` by image ID, `d=p` by placement ID, `d=c` intersecting cursor, `d=z` by Z-index, `d=x` by column, `d=y` by row).
- `a=q`: Query terminal graphics support. Terminal responds with `\E_Gi={id};OK\E\`.

### C. Pixel Formats (`f=...`) & Compression (`o=...`)
- `f=32`: Raw 32-bit RGBA (Red, Green, Blue, Alpha). Requires dimensions `s` (width) and `v` (height).
- `f=24`: Raw 24-bit RGB (3 bytes per pixel). Automatically converted to 32-bit RGBA with full alpha opacity.
- `f=100`: PNG binary data. Decoded via pure C# `PngDecoder` supporting standard 8-bit RGBA, RGB, and Grayscale formats without native GDI+ dependencies.
- `o=z`: Decompressed via `ZLibStream` prior to pixel decoding.

### D. Z-Index Layering Strata (`z=...`)
1. **Negative Z-Index (`z < 0`) — Below Text**:
   - Rendered behind text glyphs. Transparent cell backgrounds expose the image beneath.
2. **Zero Z-Index (`z == 0`) — Cell Background Layer**:
   - Rendered above default background colors, but below foreground glyphs, underlines, and cursor.
3. **Positive Z-Index (`z > 0`) — Over Text**:
   - Rendered on top of text glyphs and cursor.

### E. Grid Anchoring & Scrolling Lifecycle
- Placements are anchored to grid coordinates `(AnchorCol, AnchorRow)` with cell spans `(ColSpan, RowSpan)`.
- When the screen scrolls up or down (within `DECSTBM` margins, reverse index, or via `IL`/`DL`), `TerminalFrameBuffer` shifts placement anchors vertically via `ShiftImagePlacementsVertically(...)`.
- Images scrolling past Row 0 are clipped or moved to scrollback history.

---

## 8. Fault Tolerance, Non-Blocking Timeouts & Resilience Model

A robust terminal emulator must maintain operational availability under all conditions, even when processing malformed escape sequences, fuzz-testing traffic, or network stalls:

### A. Non-Asserting & Non-Crashing Guarantee
- The parser never throws unhandled exceptions or terminates on invalid bytes, unknown parameters, or unrecognized escape sequences.
- Any unexpected or malformed commands emit non-fatal diagnostic warnings to `Console.Error` and debug trace logs, after which the parser recovers cleanly to the ground state.
- `TerminalFrameBuffer.DoAsserts` logs diagnostic warnings to stderr without crashing the host process.

### B. Incomplete Sequence Timeouts (`SequenceTimeout`)
- If an escape or control sequence begins (e.g. `ESC [ ...` or `ESC _ ...`) and the incoming stream pauses, stalls, or hangs waiting for a missing terminator, `EscapeCharacterDecoder.SequenceTimeout` (default 3.0 seconds) triggers.
- Upon timing out, the incomplete sequence is discarded, a warning is emitted to stderr, and the parser resumes the ground (`State.Normal`) state.

### C. Buffer Overflow Protections
- CSI sequences are bounded to 4,096 bytes; APC and OSC sequences are bounded to 16 MiB.
- If un-terminated garbage bytes exceed these thresholds, the buffer is purged and the parser resets to ground state, preventing denial-of-service memory exhaustion.

### D. Zero-Warning Type Safety
- All public and internal types comply with C# nullable reference types (`<Nullable>enable</Nullable>`) and obsolete API guidelines, maintaining a clean 0-warning build across the entire library.


