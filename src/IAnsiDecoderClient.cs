using System;
using System.Drawing;
using libVT100.KittyGraphics;


namespace libVT100
{
    /// <summary>
    /// Relative cursor movement direction.
    /// </summary>
    public enum Direction
    {
        /// <summary>Move cursor upward towards row 0 (CUU).</summary>
        Up,
        /// <summary>Move cursor downward towards bottom row (CUD).</summary>
        Down,
        /// <summary>Move cursor forward towards right column (CUF).</summary>
        Forward,
        /// <summary>Move cursor backward towards column 0 (CUB).</summary>
        Backward
    }

    /// <summary>
    /// Directional bounds for display (ED) and line (EL) clearing commands.
    /// </summary>
    public enum ClearDirection
    {
        /// <summary>Erase from the cursor to the end (Ps = 0).</summary>
        Forward = 0,
        /// <summary>Erase from the beginning to the cursor (Ps = 1).</summary>
        Backward = 1,
        /// <summary>Erase the entire display or line (Ps = 2).</summary>
        Both = 2
    }

    /// <summary>
    /// Select Graphic Rendition (SGR) character video rendition attributes and colors (ECMA-48 / ANSI X3.64 / aixterm).
    /// </summary>
    public enum GraphicRendition
    {
        /// <summary>All video attributes off (SGR 0).</summary>
        Reset = 0,
        /// <summary>Bold or increased intensity (SGR 1).</summary>
        Bold = 1,
        /// <summary>Faint, decreased intensity, or dim (SGR 2).</summary>
        Faint = 2,
        /// <summary>Italicized font (SGR 3).</summary>
        Italic = 3,
        /// <summary>Singly underlined text (SGR 4).</summary>
        Underline = 4,
        /// <summary>Slow blinking text, less than 150 per minute (SGR 5).</summary>
        BlinkSlow = 5,
        /// <summary>Rapid blinking text, 150 per minute or more (SGR 6).</summary>
        BlinkRapid = 6,
        /// <summary>Negative or inverse video; swaps foreground and background colors (SGR 7).</summary>
        Inverse = 7,
        /// <summary>Concealed / hidden text (SGR 8).</summary>
        Conceal = 8,
        /// <summary>Primary (default) font (SGR 10).</summary>
        Font1 = 10,
        /// <summary>Doubly underlined text (SGR 21).</summary>
        UnderlineDouble = 21,
        /// <summary>Normal intensity: neither bold nor faint (SGR 22).</summary>
        NormalIntensity = 22,
        /// <summary>Underline off: neither single nor double underline (SGR 24).</summary>
        NoUnderline = 24,
        /// <summary>Blink off (SGR 25).</summary>
        NoBlink = 25,
        /// <summary>Positive image: inverse off (SGR 27).</summary>
        Positive = 27,
        /// <summary>Reveal: conceal off (SGR 28).</summary>
        Reveal = 28,
        /// <summary>Foreground color: Black (SGR 30).</summary>
        ForegroundNormalBlack = 30,
        /// <summary>Foreground color: Red (SGR 31).</summary>
        ForegroundNormalRed = 31,
        /// <summary>Foreground color: Green (SGR 32).</summary>
        ForegroundNormalGreen = 32,
        /// <summary>Foreground color: Yellow (SGR 33).</summary>
        ForegroundNormalYellow = 33,
        /// <summary>Foreground color: Blue (SGR 34).</summary>
        ForegroundNormalBlue = 34,
        /// <summary>Foreground color: Magenta (SGR 35).</summary>
        ForegroundNormalMagenta = 35,
        /// <summary>Foreground color: Cyan (SGR 36).</summary>
        ForegroundNormalCyan = 36,
        /// <summary>Foreground color: White (SGR 37).</summary>
        ForegroundNormalWhite = 37,
        /// <summary>Reset foreground color to terminal default (SGR 39).</summary>
        ForegroundNormalReset = 39,
        /// <summary>Background color: Black (SGR 40).</summary>
        BackgroundNormalBlack = 40,
        /// <summary>Background color: Red (SGR 41).</summary>
        BackgroundNormalRed = 41,
        /// <summary>Background color: Green (SGR 42).</summary>
        BackgroundNormalGreen = 42,
        /// <summary>Background color: Yellow (SGR 43).</summary>
        BackgroundNormalYellow = 43,
        /// <summary>Background color: Blue (SGR 44).</summary>
        BackgroundNormalBlue = 44,
        /// <summary>Background color: Magenta (SGR 45).</summary>
        BackgroundNormalMagenta = 45,
        /// <summary>Background color: Cyan (SGR 46).</summary>
        BackgroundNormalCyan = 46,
        /// <summary>Background color: White (SGR 47).</summary>
        BackgroundNormalWhite = 47,
        /// <summary>Reset background color to terminal default (SGR 49).</summary>
        BackgroundNormalReset = 49,
        /// <summary>High-intensity foreground color: Bright Black / Dark Gray (aixterm SGR 90).</summary>
        ForegroundBrightBlack = 90,
        /// <summary>High-intensity foreground color: Bright Red (aixterm SGR 91).</summary>
        ForegroundBrightRed = 91,
        /// <summary>High-intensity foreground color: Bright Green (aixterm SGR 92).</summary>
        ForegroundBrightGreen = 92,
        /// <summary>High-intensity foreground color: Bright Yellow (aixterm SGR 93).</summary>
        ForegroundBrightYellow = 93,
        /// <summary>High-intensity foreground color: Bright Blue (aixterm SGR 94).</summary>
        ForegroundBrightBlue = 94,
        /// <summary>High-intensity foreground color: Bright Magenta (aixterm SGR 95).</summary>
        ForegroundBrightMagenta = 95,
        /// <summary>High-intensity foreground color: Bright Cyan (aixterm SGR 96).</summary>
        ForegroundBrightCyan = 96,
        /// <summary>High-intensity foreground color: Bright White (aixterm SGR 97).</summary>
        ForegroundBrightWhite = 97,
        /// <summary>Reset high-intensity foreground color (SGR 99).</summary>
        ForegroundBrightReset = 99,
        /// <summary>High-intensity background color: Bright Black / Dark Gray (aixterm SGR 100).</summary>
        BackgroundBrightBlack = 100,
        /// <summary>High-intensity background color: Bright Red (aixterm SGR 101).</summary>
        BackgroundBrightRed = 101,
        /// <summary>High-intensity background color: Bright Green (aixterm SGR 102).</summary>
        BackgroundBrightGreen = 102,
        /// <summary>High-intensity background color: Bright Yellow (aixterm SGR 103).</summary>
        BackgroundBrightYellow = 103,
        /// <summary>High-intensity background color: Bright Blue (aixterm SGR 104).</summary>
        BackgroundBrightBlue = 104,
        /// <summary>High-intensity background color: Bright Magenta (aixterm SGR 105).</summary>
        BackgroundBrightMagenta = 105,
        /// <summary>High-intensity background color: Bright Cyan (aixterm SGR 106).</summary>
        BackgroundBrightCyan = 106,
        /// <summary>High-intensity background color: Bright White (aixterm SGR 107).</summary>
        BackgroundBrightWhite = 107,
        /// <summary>Reset high-intensity background color (SGR 109).</summary>
        BackgroundBrightReset = 109,
    }

    /// <summary>
    /// Terminal mode states controlled by Set Mode (SM / DECSET) and Reset Mode (RM / DECRST).
    /// </summary>
    public enum AnsiMode
    {
        /// <summary>Make text cursor visible (DECTCEM set, CSI ? 25 h).</summary>
        ShowCursor,
        /// <summary>Make text cursor invisible (DECTCEM reset, CSI ? 25 l).</summary>
        HideCursor,
        /// <summary>Line feed without carriage return (LNM reset, CSI 20 l).</summary>
        LineFeed,
        /// <summary>Line feed performs carriage return and line feed (LNM set, CSI 20 h).</summary>
        NewLine,
        /// <summary>Normal cursor keys transmit ANSI cursor sequences (DECCKM reset, CSI ? 1 l).</summary>
        CursorKeyToCursor,
        /// <summary>Application cursor keys transmit SS3 cursor sequences (DECCKM set, CSI ? 1 h).</summary>
        CursorKeyToApplication,
        /// <summary>Select ANSI / VT100 mode (DECANM set).</summary>
        ANSI,
        /// <summary>Select legacy VT52 mode (DECANM reset, CSI ? 2 l).</summary>
        VT52,
        /// <summary>Set 80-column screen width (DECCOLM reset, CSI ? 3 l).</summary>
        Columns80,
        /// <summary>Set 132-column screen width (DECCOLM set, CSI ? 3 h).</summary>
        Columns132,
        /// <summary>Jump scrolling: scroll lines instantly (DECSCLM reset, CSI ? 4 l).</summary>
        JumpScrolling,
        /// <summary>Smooth scrolling: scroll lines at fixed pace (DECSCLM set, CSI ? 4 h).</summary>
        SmoothScrolling,
        /// <summary>Normal video screen background (DECSCNM reset, CSI ? 5 l).</summary>
        NormalVideo,
        /// <summary>Reverse video screen background (DECSCNM set, CSI ? 5 h).</summary>
        ReverseVideo,
        /// <summary>Cursor coordinates relative to upper-left screen origin (DECOM reset, CSI ? 6 l).</summary>
        OriginIsAbsolute,
        /// <summary>Cursor coordinates relative to top margin origin (DECOM set, CSI ? 6 h).</summary>
        OriginIsRelative,
        /// <summary>Enable automatic margin wrapping (DECAWM set, CSI ? 7 h).</summary>
        LineWrap,
        /// <summary>Disable automatic margin wrapping (DECAWM reset, CSI ? 7 l).</summary>
        DisableLineWrap,
        /// <summary>Enable keyboard auto-repeat (DECARM set, CSI ? 8 h).</summary>
        AutoRepeat,
        /// <summary>Disable keyboard auto-repeat (DECARM reset, CSI ? 8 l).</summary>
        DisableAutoRepeat,
        /// <summary>Enable display interlacing (CSI ? 9 h).</summary>
        Interlacing,
        /// <summary>Disable display interlacing (CSI ? 9 l).</summary>
        DisableInterlacing,
        /// <summary>Numeric keypad mode (DECKPNM, ESC >).</summary>
        NumericKeypad,
        /// <summary>Application keypad mode (DECKPAM, ESC =).</summary>
        AlternateKeypad,
        /// <summary>Switch to primary screen buffer (xterm CSI ? 1049 l / 1047 l / 47 l).</summary>
        SwitchToMainBuffer,
        /// <summary>Switch to alternate screen buffer (xterm CSI ? 1049 h / 1047 h / 47 h).</summary>
        SwitchToAlternateBuffer,
        /// <summary>Application Keypad mode (DECKPAM, ESC =).</summary>
        ApplicationKeypad_DECKPAM,
        /// <summary>Normal numeric keypad mode (DECKPNM, ESC >).</summary>
        NormalKeypad_DECKPNM,
        /// <summary>Designate G0 character set to UK national character set (ESC ( A).</summary>
        SwitchG0toVT100_UK,
        /// <summary>Designate G0 character set to US-ASCII (ESC ( B).</summary>
        SwitchG0toVT100_US,
        /// <summary>Designate G0 character set to DEC Special Graphics / Line Drawing (ESC ( 0).</summary>
        SwitchG0toVT100_LineDrawing,
        /// <summary>Designate G1 character set to UK national character set (ESC ) A).</summary>
        SwitchG1toVT100_UK,
        /// <summary>Designate G1 character set to US-ASCII (ESC ) B).</summary>
        SwitchG1toVT100_US,
        /// <summary>Designate G1 character set to DEC Special Graphics / Line Drawing (ESC ) 0).</summary>
        SwitchG1toVT100_LineDrawing,
        /// <summary>Enable character insert mode (IRM set, CSI 4 h).</summary>
        InsertMode,
        /// <summary>Disable character insert mode: replace mode (IRM reset, CSI 4 l).</summary>
        ReplaceMode,
        /// <summary>Enable left and right margin mode (DECLRMM set, CSI ? 69 h).</summary>
        LeftRightMarginMode,
        /// <summary>Disable left and right margin mode (DECLRMM reset, CSI ? 69 l).</summary>
        DisableLeftRightMarginMode,
        /// <summary>Enable crossed-out / strikethrough text (SGR 9).</summary>
        Strikethrough,
        /// <summary>Disable crossed-out / strikethrough text (SGR 29).</summary>
        NoStrikethrough,
        /// <summary>Enable overline text decoration (SGR 53).</summary>
        Overline,
        /// <summary>Disable overline text decoration (SGR 55).</summary>
        NoOverline,
        /// <summary>Enable synchronized update / atomic frame output (Mode ?2026 h).</summary>
        SynchronizedOutput,
        /// <summary>Disable synchronized update / flush atomic frame output (Mode ?2026 l).</summary>
        DisableSynchronizedOutput,

    }

    /// <summary>
    /// Target property for terminal title and configuration commands (OSC 0, 1, 2).
    /// </summary>
    public enum PropertyTypes
    {
        /// <summary>Both window title and icon title (OSC 0).</summary>
        IconAndTitle,
        /// <summary>Window title bar text (OSC 2).</summary>
        WindowTitle,
        /// <summary>Minimized window icon text (OSC 1).</summary>
        IconName,
    }

    /// <summary>
    /// Consumer interface for terminal rendering targets (such as <see cref="TerminalFrameBuffer"/>)
    /// receiving decoded VT100, VT220, ANSI, and xterm commands.
    /// </summary>
    public interface IAnsiDecoderClient : IDisposable
    {
        /// <summary>Sets a horizontal tab stop at the current cursor column (HTS, ESC H).</summary>
        void SetTab(IAnsiDecoder _sender);

        /// <summary>Clears tab stops (TBC, CSI 0 g or CSI 3 g).</summary>
        void ClearTab(IAnsiDecoder _sender, bool ClearAll);

        /// <summary>Writes an array of text characters into the terminal buffer.</summary>
        void Characters(IAnsiDecoder _sender, char[] _chars);

        /// <summary>Saves the cursor position, video attributes, and character sets (DECSC, ESC 7).</summary>
        void SaveCursor(IAnsiDecoder _sernder);

        /// <summary>Restores the saved cursor position, video attributes, and character sets (DECRC, ESC 8).</summary>
        void RestoreCursor(IAnsiDecoder _sender);

        /// <summary>Gets the active grid size (columns and rows) of the terminal buffer.</summary>
        Size GetSize(IAnsiDecoder _sender);

        /// <summary>Moves the cursor in the given direction by the specified number of cells.</summary>
        void MoveCursor(IAnsiDecoder _sender, Direction _direction, int _amount, bool scroll);

        /// <summary>Moves the cursor to column 0 of the line below (CNL / LF).</summary>
        void MoveCursorToBeginningOfLineBelow(IAnsiDecoder _sender, int _lineNumberRelativeToCurrentLine, bool scroll);

        /// <summary>Moves the cursor to column 0 of the line above (CPL).</summary>
        void MoveCursorToBeginningOfLineAbove(IAnsiDecoder _sender, int _lineNumberRelativeToCurrentLine, bool scroll);

        /// <summary>Moves the cursor to the specified 0-based column (CHA / HPA).</summary>
        void MoveCursorToColumn(IAnsiDecoder _sender, int _columnNumber);

        /// <summary>Moves the cursor to the specified 0-based coordinate (CUP / HVP).</summary>
        void MoveCursorTo(IAnsiDecoder _sender, Point _position);

        /// <summary>Erases portions of the display grid (ED, CSI J).</summary>
        void ClearScreen(IAnsiDecoder _sender, ClearDirection _direction);

        /// <summary>Erases portions of the current line (EL, CSI K).</summary>
        void ClearLine(IAnsiDecoder _sender, ClearDirection _direction);

        /// <summary>Scrolls the active scrolling region upwards by the specified number of lines (SU, CSI S).</summary>
        void ScrollPageUpwards(IAnsiDecoder _sender, int _linesToScroll);

        /// <summary>Scrolls the active scrolling region downwards by the specified number of lines (SD, CSI T).</summary>
        void ScrollPageDownwards(IAnsiDecoder _sender, int _linesToScroll);

        /// <summary>Gets the current 1-based cursor coordinate for CPR reporting (DSR 6).</summary>
        Point GetCursorPosition(IAnsiDecoder _sender);

        /// <summary>Applies standard Select Graphic Rendition (SGR) character video attributes.</summary>
        void SetGraphicRendition(IAnsiDecoder _sender, GraphicRendition[] _commands);

        /// <summary>Notifies the client of a terminal mode change (SM / RM / DECSET / DECRST).</summary>
        void ModeChanged(IAnsiDecoder _sender, AnsiMode _mode);

        /// <summary>Sets a terminal string property such as window title or icon name (OSC 0, 1, 2).</summary>
        void SetProperty(IAnsiDecoder _sender, PropertyTypes type, string value);

        /// <summary>Erases the next N characters on the current line without moving the cursor (ECH, CSI X).</summary>
        void ClearNext(AnsiDecoder ansiDecoder, int numChars);

        /// <summary>Sets top and bottom scrolling margins (DECSTBM, CSI r).</summary>
        void SetScrollingRegion(IAnsiDecoder _sender, int _topRow, int _bottomRow);

        /// <summary>Inserts N blank lines at the cursor within the active scrolling margins (IL, CSI L).</summary>
        void InsertLine(IAnsiDecoder _sender, int _lineCount);

        /// <summary>Deletes N lines at the cursor within the active scrolling margins (DL, CSI M).</summary>
        void DeleteLine(IAnsiDecoder _sender, int _lineCount);
        /// <summary>Inserts N blank characters at the cursor, shifting following text right (ICH, CSI @).</summary>
        void InsertCharacter(IAnsiDecoder _sender, int _charCount);

        /// <summary>Deletes N characters at the cursor, shifting following text left (DCH, CSI P).</summary>
        void DeleteCharacter(IAnsiDecoder _sender, int _charCount);

        /// <summary>Performs a soft (DECSTR) or hard (RIS) terminal reset.</summary>
        void Reset(IAnsiDecoder _sender, bool _soft);

        /// <summary>Moves the cursor to the specified 0-based row on the current line (VPA, CSI d).</summary>
        void MoveCursorToRow(IAnsiDecoder _sender, int _rowNumber);

        /// <summary>Moves the cursor backwards to preceding tab stops (CBT, CSI Z).</summary>
        void MoveCursorBackTab(IAnsiDecoder _sender, int _tabCount);

        /// <summary>Repeats the preceding graphic character N times (REP, CSI b).</summary>
        void RepeatCharacter(IAnsiDecoder _sender, int _count);

        /// <summary>Sets left and right scrolling margins when DECLRMM is enabled (DECSLRM, CSI s).</summary>
        void SetLeftRightMargins(IAnsiDecoder _sender, int _leftColumn, int _rightColumn);

        /// <summary>Clears saved lines from the scrollback history buffer (ED 3, CSI 3 J).</summary>
        void ClearSavedLines(IAnsiDecoder _sender);

        /// <summary>Sets the foreground or background color from the xterm 256-color palette (SGR 38;5 or 48;5).</summary>
        void SetColor256(IAnsiDecoder _sender, bool _isForeground, int _colorIndex);

        /// <summary>Sets the foreground or background color to a 24-bit TrueColor RGB value (SGR 38;2 or 48;2).</summary>
        void SetColorRgb(IAnsiDecoder _sender, bool _isForeground, Color _color);

        /// <summary>Resets the foreground or background color to the terminal default (SGR 39 or 49).</summary>
        void ResetColor(IAnsiDecoder _sender, bool _isForeground);

        /// <summary>Pushes the current window title onto the xterm title stack (CSI 22 t).</summary>
        void PushTitle(IAnsiDecoder _sender);

        /// <summary>Pops the window title from the xterm title stack (CSI 23 t).</summary>
        void PopTitle(IAnsiDecoder _sender);

        /// <summary>Locks display memory above the cursor or unlocks it (ESC l / ESC m).</summary>
        void LockMemory(IAnsiDecoder _sender, bool _lockAbove);

        /// <summary>Sets extended underline style (Kitty SGR 4:x).</summary>
        void SetUnderlineStyle(IAnsiDecoder _sender, TerminalFrameBuffer.Underline _style);

        /// <summary>Sets distinct underline color (Kitty SGR 58 / SGR 59).</summary>
        void SetUnderlineColor(IAnsiDecoder _sender, Color? _color);

        /// <summary>Sets cursor shape and blinking behavior (DECSCUSR / CSI q).</summary>
        void SetCursorShape(IAnsiDecoder _sender, TerminalFrameBuffer.CursorShape _shape, bool _blinking);

        /// <summary>Sets cursor color (OSC 12 / OSC 112).</summary>
        void SetCursorColor(IAnsiDecoder _sender, Color? _color);

        /// <summary>Sets an active explicit hyperlink URL on the terminal buffer (OSC 8).</summary>
        void SetHyperlink(IAnsiDecoder _sender, string? _url, string? _id);

        /// <summary>Dispatches a desktop notification (Kitty OSC 99).</summary>
        void DesktopNotification(IAnsiDecoder _sender, string _notificationParams, string _body);

        /// <summary>Dispatches a semantic shell integration marker (OSC 133 / FTCS).</summary>
        void ShellIntegration(IAnsiDecoder _sender, string _command, string? _args);
        /// <summary>Dispatches a Kitty Graphics Protocol command to the terminal framebuffer.</summary>
        void KittyGraphicsCommand(IAnsiDecoder _sender, KittyGraphicsCommand _command);


    }
}
