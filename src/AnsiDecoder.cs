using System;
using System.Text;
using System.Drawing;
using System.Collections.Generic;
using System.Diagnostics;
using libVT100.Oob;
using libVT100.KittyGraphics;


namespace libVT100
{
    /// <summary>
    /// Full VT100, VT220, ANSI, and xterm sequence decoder. Parses CSI, OSC, C2, and character sets,
    /// evaluates 256/TrueColor SGR styles, and dispatches rendering commands to subscribed <see cref="IAnsiDecoderClient"/> instances.
    /// </summary>
    public class AnsiDecoder : EscapeCharacterDecoder, IAnsiDecoder
    {
        protected List<IAnsiDecoderClient> m_listeners;

        protected bool m_leftRightMarginMode = false;

        private readonly OobChunkReassembler m_oobReassembler = new();
        /// <summary>Gets the OOB chunk reassembly and defragmentation engine.</summary>
        public OobChunkReassembler OobReassembler => m_oobReassembler;
        /// <summary>Fires when an Out-of-Band (OOB) APC packet is fully received and reassembled.</summary>
        public event EventHandler<OobPacketEventArgs>? OobPacketReceived;
        private readonly List<IOobPacketHandler> m_globalOobHandlers = new();
        private readonly KittyGraphicsReassembler m_kittyGfxReassembler = new();

        private readonly Dictionary<string, List<IOobPacketHandler>> m_actionOobHandlers = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Gets or sets the string builder used for recording trace log tokens with standard DEC/ANSI jargon.</summary>
        public StringBuilder? dvt { get; set; }

        Encoding IDecoder.Encoding
        {
            get
            {
                return m_encoding;
            }
            set
            {
                if (m_encoding != value)
                {
                    m_encoding = value;
                    m_decoder = m_encoding.GetDecoder();
                    m_encoder = m_encoding.GetEncoder();
                }
            }
        }

        /// <summary>Initializes a new instance of <see cref="AnsiDecoder"/>.</summary>
        public AnsiDecoder()
           : base()
        {
            m_listeners = [];
        }

        /// <summary>Emits a formatted debug token to <see cref="dvt"/> if enabled.</summary>
        public void deb(string s)
        {
            if (dvt is not null) dvt.Append(s);
        }

        /// <summary>Emits a character token to <see cref="dvt"/> if enabled.</summary>
        public void deb(char c)
        {
            if (dvt is not null) dvt.Append(c);
        }

        private int DecodeInt(String _value, int _default)
        {
            if (_value.Length == 0)
            {
                return _default;
            }
            int ret;
            if (Int32.TryParse(_value.TrimStart('0'), out ret))
            {
                return ret;
            }
            else
            {
                return _default;
            }
        }

        #region CSI Processor
        /// <summary>
        /// Processes Control Sequence Introducer (CSI) commands: ESC [ ... FinalChar.
        /// Conforms to ECMA-48, DEC STD 070 (VT100/VT220), and X11 xterm Control Sequences.
        /// </summary>
        protected override void ProcessCommandCSI(byte _command, String _parameter)
        {
            switch ((char)_command)
            {
                case '@':  // CSI Ps @  ICH - Insert Character(s) [default = 1] (ECMA-48 / VT420 / xterm)
                    deb($"[ANSI:ICH({DecodeInt(_parameter, 1)})]");
                    OnInsertCharacter(DecodeInt(_parameter, 1));
                    break;

                case 'A':  // CSI Ps A  CUU - Cursor Up [default = 1] (ECMA-48 / ANSI X3.64)
                    deb($"[ANSI:CUU({DecodeInt(_parameter, 1)})]");
                    OnMoveCursor(Direction.Up, DecodeInt(_parameter, 1), false);
                    break;

                case 'B':  // CSI Ps B  CUD - Cursor Down [default = 1] (ECMA-48 / ANSI X3.64)
                    deb($"[ANSI:CUD({DecodeInt(_parameter, 1)})]");
                    OnMoveCursor(Direction.Down, DecodeInt(_parameter, 1), false);
                    break;

                case 'C':  // CSI Ps C  CUF - Cursor Forward [default = 1] (ECMA-48 / ANSI X3.64)
                    deb($"[ANSI:CUF({DecodeInt(_parameter, 1)})]");
                    OnMoveCursor(Direction.Forward, DecodeInt(_parameter, 1), false);
                    break;

                case 'D':  // CSI Ps D  CUB - Cursor Backward [default = 1] (ECMA-48 / ANSI X3.64)
                    deb($"[ANSI:CUB({DecodeInt(_parameter, 1)})]");
                    OnMoveCursor(Direction.Backward, DecodeInt(_parameter, 1), false);
                    break;

                case 'E':  // CSI Ps E  CNL - Cursor Next Line [default = 1] (ECMA-48)
                    deb($"[ANSI:CNL({DecodeInt(_parameter, 1)})]");
                    OnMoveCursorToBeginningOfLineBelow(DecodeInt(_parameter, 1), false);
                    break;

                case 'F':  // CSI Ps F  CPL - Cursor Preceding Line [default = 1] (ECMA-48)
                    deb($"[ANSI:CPL({DecodeInt(_parameter, 1)})]");
                    OnMoveCursorToBeginningOfLineAbove(DecodeInt(_parameter, 1), false);
                    break;

                case 'G': // CSI Ps G  CHA - Cursor Character Absolute [default = 1] (ECMA-48 / xterm)
                case '`': // CSI Ps `  HPA - Horizontal Position Absolute [default = 1] (ECMA-48)
                    {
                        var col = DecodeInt(_parameter, 1) - 1;
                        deb($"[ANSI:CHA({col})]");
                        OnMoveCursorToColumn(col);
                    }
                    break;

                case 'H':  // CSI Ps ; Ps H  CUP - Cursor Position [row; column] [default = 1; 1] (ECMA-48 / DEC)
                case 'f':  // CSI Ps ; Ps f  HVP - Horizontal and Vertical Position (ECMA-48)
                    {
                        int separator = _parameter.IndexOf(';');
                        if (separator == -1)
                        {
                            deb("[ANSI:CUP(0,0)]");
                            OnMoveCursorTo(new Point(0, 0));
                        }
                        else
                        {
                            String rowStr = _parameter.Substring(0, separator);
                            String columnStr = _parameter.Substring(separator + 1);
                            var cl = DecodeInt(columnStr, 1) - 1;
                            var rl = DecodeInt(rowStr, 1) - 1;
                            deb($"[ANSI:CUP({rl},{cl})]");
                            OnMoveCursorTo(new Point(cl, rl));
                        }
                    }
                    break;

                case 'I':   // CSI Ps I  CHT - Cursor Forward Tabulation [default = 1] (ECMA-48)
                    deb($"[ANSI:CHT({DecodeInt(_parameter, 1)})]");
                    break;

                case 'J':   // CSI Ps J  ED - Erase in Display (ECMA-48 / xterm)
                    {
                        int edDir = DecodeInt(_parameter, 0);
                        if (edDir == 3)
                        {
                            deb("[XTERM:ED_SCROLLBACK(3)]");
                            OnClearSavedLines();
                        }
                        else
                        {
                            deb($"[ANSI:ED({(ClearDirection)edDir})]");
                            OnClearScreen((ClearDirection)edDir);
                        }
                    }
                    break;

                case 'K':   // CSI Ps K  EL - Erase in Line (ECMA-48)
                    deb($"[ANSI:EL({(ClearDirection)DecodeInt(_parameter, 0)})]");
                    OnClearLine((ClearDirection)DecodeInt(_parameter, 0));
                    break;

                case 'L':  // CSI Ps L  IL - Insert Line(s) [default = 1] (ECMA-48 / VT102)
                    deb($"[ANSI:IL({DecodeInt(_parameter, 1)})]");
                    OnInsertLine(DecodeInt(_parameter, 1));
                    break;

                case 'M':  // CSI Ps M  DL - Delete Line(s) [default = 1] (ECMA-48 / VT102)
                    deb($"[ANSI:DL({DecodeInt(_parameter, 1)})]");
                    OnDeleteLine(DecodeInt(_parameter, 1));
                    break;

                case 'P':  // CSI Ps P  DCH - Delete Character(s) [default = 1] (ECMA-48 / VT102)
                    deb($"[ANSI:DCH({DecodeInt(_parameter, 1)})]");
                    OnDeleteCharacter(DecodeInt(_parameter, 1));
                    break;

                case 'S':  // CSI Ps S  SU - Scroll Up / Pan Down [default = 1] (ECMA-48 / VT420)
                    deb($"[ANSI:SU({DecodeInt(_parameter, 1)})]");
                    OnScrollPageUpwards(DecodeInt(_parameter, 1));
                    break;

                case 'T':  // CSI Ps T  SD - Scroll Down / Pan Up [default = 1] (ECMA-48 / VT420)
                    deb($"[ANSI:SD({DecodeInt(_parameter, 1)})]");
                    OnScrollPageDownwards(DecodeInt(_parameter, 1));
                    break;

                case 'X':  // CSI Ps X  ECH - Erase Character(s) [default = 1] (ECMA-48)
                    deb($"[ANSI:ECH({DecodeInt(_parameter, 1)})]");
                    OnClearNext(DecodeInt(_parameter, 1));                
                    break;

                case 'Z':  // CSI Ps Z  CBT - Cursor Backward Tabulation [default = 1] (ECMA-48 / xterm)
                    deb($"[ANSI:CBT({DecodeInt(_parameter, 1)})]");
                    OnMoveCursorBackTab(DecodeInt(_parameter, 1));
                    break;

                case 'a':  // CSI Ps a  HPR - Horizontal Position Relative [default = 1] (ECMA-48)
                    deb($"[ANSI:HPR({DecodeInt(_parameter, 1)})]");
                    OnMoveCursor(Direction.Forward, DecodeInt(_parameter, 1), false);
                    break;

                case 'b':  // CSI Ps b  REP - Repeat Preceding Character [default = 1] (ECMA-48 / xterm)
                    deb($"[ANSI:REP({DecodeInt(_parameter, 1)})]");
                    OnRepeatCharacter(DecodeInt(_parameter, 1));
                    break;

                case 'd':  // CSI Ps d  VPA - Vertical Position Absolute [default = 1] (ECMA-48)
                    {
                        var row = DecodeInt(_parameter, 1) - 1;
                        deb($"[ANSI:VPA({row})]");
                        OnMoveCursorToRow(row);
                    }
                    break;

                case 'e':  // CSI Ps e  VPR - Vertical Position Relative [default = 1] (ECMA-48)
                    deb($"[ANSI:VPR({DecodeInt(_parameter, 1)})]");
                    OnMoveCursor(Direction.Down, DecodeInt(_parameter, 1), false);
                    break;

                case 'c':   // CSI Ps c  Send Device Attributes (Primary DA).
                    DoCSI_PrimaryDA(_parameter);
                    break;

                case 'h':  // CSI ? Pm h - DEC Private Mode Set(DECSET).
                    DoCSI_DECSET(_command, _parameter);
                    break;

                case 'g':  // CSI Ps g  TBC - Tab Clear [0 = current, 3 = all] (ECMA-48)
                    deb($"[ANSI:TBC({_parameter})]");
                    switch (_parameter)
                    {
                        case "":
                        case "0":
                            ClearTab(false);
                            break;
                        case "3":
                            ClearTab(true);
                            break;
                    }
                    break;
                    
                case 'l':  // CSI ? Pm l - Reset Mode (RM / DECRST)
                    DoCSI_DECRST(_command, _parameter);
                    break;

                case 'm':  // CSI Pm m  SGR - Select Graphic Rendition (ECMA-48 / xterm 256 / TrueColor)
                    DoCSI_SGR(_parameter);
                    break;

                case 'n':  // CSI Ps n  DSR - Device Status Report (ECMA-48 / DEC)
                    DoCSI_DSR(_parameter);
                    break;

                case 'p':  // CSI ! p   DECSTR - Soft Terminal Reset (DEC VT220)
                    if (_parameter == "!")
                    {
                        deb("[DEC:DECSTR]");
                        OnReset(true);
                    }
                    break;

                case 'r':  // CSI Ps ; Ps r  DECSTBM - Set Top and Bottom Margins (DEC VT100)
                    deb($"[DEC:DECSTBM({_parameter})]");
                    {
                        int sep = _parameter.IndexOf(';');
                        int top = 1;
                        int bottom = 0;
                        if (sep == -1)
                        {
                            if (!string.IsNullOrEmpty(_parameter))
                                top = DecodeInt(_parameter, 1);
                        }
                        else
                        {
                            top = DecodeInt(_parameter.Substring(0, sep), 1);
                            bottom = DecodeInt(_parameter.Substring(sep + 1), 0);
                        }
                        OnSetScrollingRegion(top, bottom);
                    }
                    break;

                case 's':  // CSI s / CSI Pl ; Pr s  (SCOSC or DECSLRM)
                    if (m_leftRightMarginMode)
                    {
                        deb($"[DEC:DECSLRM({_parameter})]");
                        int sep = _parameter.IndexOf(';');
                        int left = 1;
                        int right = 0;
                        if (sep != -1)
                        {
                            left = DecodeInt(_parameter.Substring(0, sep), 1);
                            right = DecodeInt(_parameter.Substring(sep + 1), 0);
                        }
                        else if (!string.IsNullOrEmpty(_parameter))
                        {
                            left = DecodeInt(_parameter, 1);
                        }
                        OnSetLeftRightMargins(left, right);
                    }
                    else
                    {
                        deb("[ANSI:SCOSC]");
                        OnSaveCursor();
                    }
                    break;

                case 't':  // Window manipulation (xterm / EWMH)
                    DoCSI_WindowManipulation(_parameter);
                    break;

                case 'q':  // CSI Ps SP q (DECSCUSR - Set Cursor Style)
                    {
                        int style = DecodeInt(_parameter.Trim(), 1);
                        deb($"[KITTY:DECSCUSR({style})]");
                        TerminalFrameBuffer.CursorShape shape;
                        bool blinking = false;
                        switch (style)
                        {
                            case 0:
                            case 1:
                                shape = TerminalFrameBuffer.CursorShape.Block;
                                blinking = true;
                                break;
                            case 2:
                                shape = TerminalFrameBuffer.CursorShape.Block;
                                blinking = false;
                                break;
                            case 3:
                                shape = TerminalFrameBuffer.CursorShape.Underline;
                                blinking = true;
                                break;
                            case 4:
                                shape = TerminalFrameBuffer.CursorShape.Underline;
                                blinking = false;
                                break;
                            case 5:
                                shape = TerminalFrameBuffer.CursorShape.Bar;
                                blinking = true;
                                break;
                            case 6:
                                shape = TerminalFrameBuffer.CursorShape.Bar;
                                blinking = false;
                                break;
                            default:
                                shape = TerminalFrameBuffer.CursorShape.Block;
                                break;
                        }
                        OnSetCursorShape(shape, blinking);
                    }
                    break;

                case 'u':  // CSI u / CSI ? u / CSI > 1 u
                    if (_parameter.StartsWith("?"))
                    {
                        deb("[KITTY:KEYBOARD_QUERY]");
                        OnOutput(Encoding.ASCII.GetBytes("\x1B[?0u"));
                    }
                    else if (_parameter.StartsWith(">"))
                    {
                        deb($"[KITTY:KEYBOARD_PUSH({_parameter})]");
                    }
                    else if (_parameter.StartsWith("<"))
                    {
                        deb($"[KITTY:KEYBOARD_POP({_parameter})]");
                    }
                    else
                    {
                        deb("[ANSI:SCORC]");
                        OnRestoreCursor();
                    }
                    break;

                case '>':  // ESC >  DECKPNM - Normal Keypad (DEC VT100)
                    deb("[DEC:DECKPNM]");
                    OnModeChanged(AnsiMode.NumericKeypad);
                    break;

                case '=':  // ESC =  DECKPAM - Application Keypad (DEC VT100)
                    deb("[DEC:DECKPAM]");
                    OnModeChanged(AnsiMode.AlternateKeypad);
                    break;

                default:
                    deb($"[UNHANDLED:CSI:{(char)_command},{_parameter}]");
                    Debug.WriteLine("Unimplemented CSI: Command=" + (char)_command + " Param=" + _parameter);
                    break;
            }
        }

        private void DoCSI_WindowManipulation(string parameter)
        {
            deb($"[XTERM:WIN_MANIP({parameter})]");

            switch (parameter)
            {
                case "18":  // Report size of text area in characters: CSI 8 ; height ; width t
                    Size sz = OnGetSize();
                    byte[] r18 = Encoding.ASCII.GetBytes($"\x1B[8;{sz.Height};{sz.Width}t");
                    OnOutput(r18);
                    deb($"[XTERM:WIN_REPORT_TEXTAREA({sz.Width}x{sz.Height})]");
                    return;

                case "19":  // Report size of screen in characters: CSI 9 ; height ; width t
                    Size sz19 = OnGetSize();
                    byte[] r19 = Encoding.ASCII.GetBytes($"\x1B[9;{sz19.Height};{sz19.Width}t");
                    OnOutput(r19);
                    deb($"[XTERM:WIN_REPORT_SCREEN({sz19.Width}x{sz19.Height})]");
                    return;

                case "22;0;0":
                case "22;1;0":
                case "22;2;0":
                case "22;0":
                case "22;1":
                case "22;2":
                case "22":
                    deb("[XTERM:TITLE_STACK_PUSH]");
                    OnPushTitle();
                    return;

                case "23;0;0":
                case "23;1;0":
                case "23;2;0":
                case "23;0":
                case "23;1":
                case "23;2":
                case "23":
                    deb("[XTERM:TITLE_STACK_POP]");
                    OnPopTitle();
                    return;
            }
        }

        private void DoCSI_DSR(string _parameter)
        {
            deb($"<DSR:{_parameter}>");

            switch (_parameter)
            {
                case "5":  //   Ps = 5  -> Status Report.
                    var resultOk = new byte[] { ESC, LBRACK, (int)'0', (int)'n' };
                    OnOutput(resultOk);
                    break;
                case "6":  //   Ps = 6  -> Report Cursor Position (CPR) [row;column].
                    Point cursorPosition = OnGetCursorPosition();
                    String row = cursorPosition.Y.ToString();
                    String column = cursorPosition.X.ToString();
                    byte[] output = Encoding.ASCII.GetBytes($"\x1B[{row};{column}R");
                    OnOutput(output);
                    break;

                    // Need to implement
                case "?6":  // Ps = 6  -> Report Cursor Position (DECXCPR) [row;column] as CSI ? r ; c R (assumes the default page, i.e., "1").
                case "?15": // Ps = 1 5  -> Report Printer status as CSI ? 1 0 n  (ready). or CSI ? 1 1 n  (not ready).
                case "?25": // Ps = 2 5  -> Report UDK status as CSI ? 2 0 n  (unlocked) or CSI ? 2 1 n(locked).
                case "?26": // Ps = 2 6  -> Report Keyboard status as CSI ? 2 7; 1; 0; 0 n(North American).
                case "?53": // Ps = 5 3  -> Report Locator status as CSI ? 5 3 n  Locator available, if compiled -in, or CSI ? 5 0 n No Locator, if not.
                case "?55": // Ps = 5 5  -> Report Locator status as CSI ? 5 3 n  Locator available, if compiled -in, or CSI ? 5 0 n No Locator, if not.
                case "?56": // Ps = 5 6  -> Report Locator type as CSI ? 5 7 ; 1 n  Mouse, if compiled -in, or CSI ? 5 7; 0 n Cannot identify, if not.
                case "?62": // Ps = 6 2  -> Report macro space (DECMSR) as CSI Pn *  { .
                case "?63": // Ps = 6 3  -> Report memory checksum (DECCKSR) as DCS Pt ! x x x x ST .  Pt is the request id (from an optional parameter to the request). The x's are hexadecimal digits 0-9 and A-F.
                case "?75": // Ps = 7 5  -> Report data integrity as CSI ? 7 0 n  (ready, no errors).
                case "?85": // Ps = 8 5  -> Report multi-session configuration as CSI ? 8 3 n (not configured for multiple - session operation).
                default:
                    deb("[BAD!]");
                    Debug.WriteLine("Unimplemented CSI Command=DSR, Param=" + _parameter);
                    break;
            }
        }

        private void DoCSI_DECRST(byte _command, string _parameter)
        {
            if (_parameter.Contains(';'))
            {
                bool isPriv = _parameter.StartsWith("?");
                string[] parts = _parameter.Split(';');
                foreach (string part in parts)
                {
                    string p = part;
                    if (isPriv && !p.StartsWith("?"))
                        p = "?" + p;
                    DoCSI_DECRST_Single(_command, p);
                }
                return;
            }
            DoCSI_DECRST_Single(_command, _parameter);
        }

        private void DoCSI_DECRST_Single(byte _command, string _parameter)
        {
            deb($"[DEC:DECRST({_parameter})]");

            switch (_parameter)
            {
                case "4":
                    deb("[ANSI:IRM_RESET]");
                    OnModeChanged(AnsiMode.ReplaceMode);
                    break;
                case "17":
                case "?17":
                    OnClearScreen(ClearDirection.Both);
                    break;
                case "20":  // Normal Linefeed (LNM)
                    deb("[ANSI:LNM_RESET]");
                    OnModeChanged(AnsiMode.LineFeed);
                    break;

                case "?1":  // Normal Cursor Keys (DECCKM)
                    deb("[DEC:DECCKM_RESET]");
                    OnModeChanged(AnsiMode.CursorKeyToCursor);
                    break;

                case "?2":  // Designate VT52 mode (DECANM)
                    deb("[DEC:DECANM_RESET]");
                    OnModeChanged(AnsiMode.VT52);
                    break;

                case "?3":  // 80 Column Mode (DECCOLM)
                    deb("[DEC:DECCOLM_RESET]");
                    OnModeChanged(AnsiMode.Columns80);
                    break;

                case "?4":  // Jump (Fast) Scroll (DECSCLM)
                    deb("[DEC:DECSCLM_RESET]");
                    OnModeChanged(AnsiMode.JumpScrolling);
                    break;

                case "?5":  // Normal Video (DECSCNM)
                    deb("[DEC:DECSCNM_RESET]");
                    OnModeChanged(AnsiMode.NormalVideo);
                    break;

                case "?6":  // Normal Cursor Mode (DECOM)
                    deb("[DEC:DECOM_RESET]");
                    OnModeChanged(AnsiMode.OriginIsAbsolute);
                    break;

                case "?7":  // No Auto-wrap Mode (DECAWM)
                    deb("[DEC:DECAWM_RESET]");
                    OnModeChanged(AnsiMode.DisableLineWrap);
                    break;

                case "?8":  // No Auto-repeat Keys (DECARM)
                    deb("[DEC:DECARM_RESET]");
                    OnModeChanged(AnsiMode.DisableAutoRepeat);
                    break;

                case "?9":  // Interlacing
                    deb("[DEC:INTERLACING_RESET]");
                    OnModeChanged(AnsiMode.DisableInterlacing);
                    break;

                case "?12":  // Stop Blinking Cursor (AT&T 610)
                    deb("[DEC:CURSOR_BLINK_RESET]");
                    break;

                case "?25":  // Hide Cursor (DECTCEM)
                    deb("[DEC:DECTCEM_RESET]");
                    OnModeChanged(AnsiMode.HideCursor);
                    break;

                case "?40":  // Disallow 80 -> 132 Mode
                    deb("[XTERM:132COLS_RESET]");
                    break;

                case "?69":  // Left and Right Margins (DECLRMM)
                    deb("[DEC:DECLRMM_RESET]");
                    m_leftRightMarginMode = false;
                    OnModeChanged(AnsiMode.DisableLeftRightMarginMode);
                    break;

                case "?47":
                case "?1047":
                    deb($"[XTERM:ALTSCREEN_EXIT({_parameter})]");
                    OnModeChanged(AnsiMode.SwitchToMainBuffer);
                    break;

                case "?1048":
                    deb("[XTERM:CURSOR_RESTORE(1048)]");
                    OnRestoreCursor();
                    break;

                case "?1049":
                    deb("[XTERM:ALTSCREEN_EXIT(1049)]");
                    OnModeChanged(AnsiMode.SwitchToMainBuffer);
                    OnRestoreCursor();
                    break;

                case "?2026":  // Kitty Synchronized Output End
                    deb("[KITTY:SYNC_OUTPUT_END]");
                    OnModeChanged(AnsiMode.DisableSynchronizedOutput);
                    break;

                case "?1000":
                case "?1001":
                case "?1002":
                case "?1003":
                case "?1004":
                case "?1005":
                case "?1006":
                    deb($"[XTERM:MOUSE_RESET({_parameter})]");
                    break;

                case "?1034":
                    deb("[XTERM:META_OFF]");
                    break;

                case "?2004":
                    deb("[XTERM:BRACKETED_PASTE_RESET]");
                    break;

                default:
                    deb($"[UNHANDLED:DECRST:{_parameter}]");
                    Debug.WriteLine("Unimplemented CSI: Command=DECRST, Param=" + _parameter);
                    break;
            }
        }

        private void DoCSI_DECSET(byte _command, string _parameter)
        {
            if (_parameter.Contains(';'))
            {
                bool isPriv = _parameter.StartsWith("?");
                string[] parts = _parameter.Split(';');
                foreach (string part in parts)
                {
                    string p = part;
                    if (isPriv && !p.StartsWith("?"))
                        p = "?" + p;
                    DoCSI_DECSET_Single(_command, p);
                }
                return;
            }
            DoCSI_DECSET_Single(_command, _parameter);
        }

        private void DoCSI_DECSET_Single(byte _command, string _parameter)
        {
            deb($"[DEC:DECSET({_parameter})]");

            switch (_parameter)
            {
                case "":
                    deb("[DEC:DECANM_SET]");
                    OnModeChanged(AnsiMode.ANSI);
                    break;

                case "4":
                    deb("[ANSI:IRM_SET]");
                    OnModeChanged(AnsiMode.InsertMode);
                    break;

                case "20":
                    deb("[ANSI:LNM_SET]");
                    OnModeChanged(AnsiMode.NewLine);
                    break;

                case "?1":
                    deb("[DEC:DECCKM_SET]");
                    OnModeChanged(AnsiMode.CursorKeyToApplication);
                    break;

                case "?3":
                    deb("[DEC:DECCOLM_SET]");
                    OnModeChanged(AnsiMode.Columns132);
                    break;

                case "?4":
                    deb("[DEC:DECSCLM_SET]");
                    OnModeChanged(AnsiMode.SmoothScrolling);
                    break;

                case "?5":
                    deb("[DEC:DECSCNM_SET]");
                    OnModeChanged(AnsiMode.ReverseVideo);
                    break;

                case "?6":
                    deb("[DEC:DECOM_SET]");
                    OnModeChanged(AnsiMode.OriginIsRelative);
                    break;

                case "?7":
                    deb("[DEC:DECAWM_SET]");
                    OnModeChanged(AnsiMode.LineWrap);
                    break;

                case "?8":
                    deb("[DEC:DECARM_SET]");
                    OnModeChanged(AnsiMode.AutoRepeat);
                    break;

                case "?9":
                    deb("[DEC:INTERLACING_SET]");
                    OnModeChanged(AnsiMode.Interlacing);
                    break;

                case "?12":  // Start Blinking Cursor (AT&T 610)
                    deb("[DEC:CURSOR_BLINK_SET]");
                    break;

                case "?25":
                    deb("[DEC:DECTCEM_SET]");
                    OnModeChanged(AnsiMode.ShowCursor);
                    break;

                case "?40":
                    deb("[XTERM:132COLS_SET]");
                    break;

                case "?69":
                    deb("[DEC:DECLRMM_SET]");
                    m_leftRightMarginMode = true;
                    OnModeChanged(AnsiMode.LeftRightMarginMode);
                    break;

                case "?47":
                case "?1047":
                    deb($"[XTERM:ALTSCREEN_ENTER({_parameter})]");
                    OnModeChanged(AnsiMode.SwitchToAlternateBuffer);
                    break;

                case "?1048":
                    deb("[XTERM:CURSOR_SAVE(1048)]");
                    OnSaveCursor();
                    break;

                case "?1049":
                    deb("[XTERM:ALTSCREEN_ENTER(1049)]");
                    OnSaveCursor();
                    OnModeChanged(AnsiMode.SwitchToAlternateBuffer);
                    break;

                case "?2026":  // Kitty Synchronized Output Start
                    deb("[KITTY:SYNC_OUTPUT_START]");
                    OnModeChanged(AnsiMode.SynchronizedOutput);
                    break;

                case "?1000":
                case "?1001":
                case "?1002":
                case "?1003":
                case "?1004":
                case "?1005":
                case "?1006":
                    deb($"[XTERM:MOUSE_SET({_parameter})]");
                    break;

                case "?1034":
                    deb("[XTERM:META_ON]");
                    break;

                case "?2004":
                    deb("[XTERM:BRACKETED_PASTE_SET]");
                    break;

                default:
                    deb($"[UNHANDLED:DECSET:{_parameter}]");
                    Debug.WriteLine("Unimplemented CSI: Command=DECSET, Param=" + _parameter);
                    break;
            }
        }

        private void DoCSI_PrimaryDA(string _parameter)
        {
            if (_parameter == ">" || _parameter == ">0" || _parameter.StartsWith(">"))
            {
                deb("[DEC:DA2]");
                var da2 = Encoding.ASCII.GetBytes("\x1B[>0;10;0c");
                OnOutput(da2);
                return;
            }

            deb($"[DEC:DA1({_parameter})]");
            switch (_parameter)
            {
                case "":
                case "0":
                    var da = Encoding.ASCII.GetBytes("\x1B[?62;1;2;6;7;8;9c");
                    OnOutput(da);
                    break;
                default:
                    Debug.WriteLine("Unhandled Primary DA: " + _parameter);
                    break;
            }
        }
        #endregion

        protected override void ProcessCommandOSC(string parameters, string terminator)
        {
            deb($"[XTERM:OSC({parameters})]");

            var parts = parameters.Split(new char[] { ';' }, 2);
            string oscCmd = parts[0];
            string oscParam = parts.Length > 1 ? parts[1] : "";

            switch (oscCmd)
            {
                case "0":
                    deb($"[XTERM:OSC_ICON_AND_TITLE:{oscParam}]");
                    foreach (IAnsiDecoderClient client in m_listeners)
                    {
                        client.SetProperty(this, PropertyTypes.IconAndTitle, oscParam);
                    }
                    break;
                case "1":
                    deb($"[XTERM:OSC_ICON:{oscParam}]");
                    foreach (IAnsiDecoderClient client in m_listeners)
                    {
                        client.SetProperty(this, PropertyTypes.IconName, oscParam);
                    }
                    break;
                case "2":
                    deb($"[XTERM:OSC_TITLE:{oscParam}]");
                    foreach (IAnsiDecoderClient client in m_listeners)
                    {
                        client.SetProperty(this, PropertyTypes.WindowTitle, oscParam);
                    }
                    break;
                case "4":
                    deb($"[XTERM:OSC_COLOR_PALETTE:{oscParam}]");
                    break;
                case "8":
                    deb($"[KITTY:OSC_HYPERLINK:{oscParam}]");
                    {
                        var linkParts = oscParam.Split(';', 2);
                        string linkParams = linkParts.Length > 0 ? linkParts[0] : "";
                        string linkUrl = linkParts.Length > 1 ? linkParts[1] : "";
                        string? linkId = null;
                        if (!string.IsNullOrEmpty(linkParams))
                        {
                            foreach (var p in linkParams.Split(':'))
                            {
                                if (p.StartsWith("id=")) linkId = p.Substring(3);
                            }
                        }
                        OnSetHyperlink(linkUrl, linkId);
                    }
                    break;
                case "10":
                    deb($"[XTERM:OSC_FG_COLOR:{oscParam}]");
                    if (oscParam == "?")
                    {
                        OnOutput(Encoding.ASCII.GetBytes("\x1B]10;rgb:ffff/ffff/ffff\x1B\\"));
                    }
                    break;
                case "11":
                    deb($"[XTERM:OSC_BG_COLOR:{oscParam}]");
                    if (oscParam == "?")
                    {
                        OnOutput(Encoding.ASCII.GetBytes("\x1B]11;rgb:0000/0000/0000\x1B\\"));
                    }
                    break;
                case "12":
                    deb($"[KITTY:OSC_CURSOR_COLOR:{oscParam}]");
                    if (oscParam == "?")
                    {
                        OnOutput(Encoding.ASCII.GetBytes("\x1B]12;rgb:ffff/ffff/ffff\x1B\\"));
                    }
                    else
                    {
                        OnSetCursorColor(ParseColor(oscParam));
                    }
                    break;
                case "112":
                    deb("[KITTY:OSC_CURSOR_COLOR_RESET]");
                    OnSetCursorColor(null);
                    break;
                case "52":
                    deb("[XTERM:OSC_CLIPBOARD]");
                    break;
                case "99":
                    deb($"[KITTY:OSC_NOTIFICATION:{oscParam}]");
                    {
                        var notifParts = oscParam.Split(';', 2);
                        string notifParams = notifParts.Length > 0 ? notifParts[0] : "";
                        string notifBody = notifParts.Length > 1 ? notifParts[1] : "";
                        OnDesktopNotification(notifParams, notifBody);
                    }
                    break;
                case "104":
                    deb("[XTERM:OSC_RESET_PALETTE]");
                    break;
                case "110":
                    deb("[XTERM:OSC_RESET_FG]");
                    break;
                case "111":
                    deb("[XTERM:OSC_RESET_BG]");
                    break;
                case "133":
                    deb($"[KITTY:OSC_SHELL_INTEGRATION:{oscParam}]");
                    {
                        var shellParts = oscParam.Split(';', 2);
                        string cmd = shellParts.Length > 0 ? shellParts[0] : "";
                        string? args = shellParts.Length > 1 ? shellParts[1] : null;
                        OnShellIntegration(cmd, args);
                    }
                    break;
            }
        }

        protected override void ProcessCommandTwo(string terminator)
        {
            deb($"[C2:{terminator}]");
            switch (terminator)
            {
                case "D":  // IND (Index down - with scroll)
                    deb("[ANSI:IND]");
                    OnMoveCursor(Direction.Down, 1, true);
                    return;
                case "M":  // RI (Reverse Index up - with scroll)
                    deb("[ANSI:RI]");
                    OnMoveCursor(Direction.Up, 1, true);
                    return;
                case "E":  // NEL (Next Line - with scroll)
                    deb("[ANSI:NEL]");
                    OnMoveCursorToBeginningOfLineBelow(1, true);
                    return;
                case "H":  // Tab Set (HTS)
                    deb("[ANSI:HTS]");
                    SetTab();
                    break;
                case "=":  // ESC = Application Keypad (DECKPAM)
                    deb("[DEC:DECKPAM]");
                    OnModeChanged(AnsiMode.ApplicationKeypad_DECKPAM);
                    break;
                case ">":  // ESC > Normal Keypad (DECKPNM)
                    deb("[DEC:DECKPNM]");
                    OnModeChanged(AnsiMode.NormalKeypad_DECKPNM);
                    break;
                case "c":  // ESC c Full Reset (RIS)
                    deb("[DEC:RIS]");
                    OnReset(false);
                    break;
                case "7":  // ESC 7 Save Cursor (DECSC)
                    deb("[DEC:DECSC]");
                    OnSaveCursor();
                    break;
                case "8":  // ESC 8 Restore Cursor (DECRC)
                    deb("[DEC:DECRC]");
                    OnRestoreCursor();
                    break;
                case "l":  // ESC l Memory Lock (locks display above cursor)
                    deb("[DEC:MEM_LOCK]");
                    OnLockMemory(true);
                    break;
                case "m":  // ESC m Memory Unlock (clears memory lock)
                    deb("[DEC:MEM_UNLOCK]");
                    OnLockMemory(false);
                    break;

                default:
                    deb($"[UNHANDLED:C2:{terminator}]");
                    Debug.WriteLine("Unimplemented TwoLetter: Term=" + terminator);
                    break;
            }
        }

        protected override void ProcessCommandThree(string parameters, string terminator)
        {
            deb($"<C3:{parameters},{terminator}>");

            switch (parameters)
            {
                case "(":  // Set G0 Character Set
                    switch (terminator)
                    {
                        case "A":
                            OnModeChanged(AnsiMode.SwitchG0toVT100_UK);
                            return;
                        case "B":
                            OnModeChanged(AnsiMode.SwitchG0toVT100_US);
                            return;
                        case "0":
                            OnModeChanged(AnsiMode.SwitchG0toVT100_LineDrawing);
                            return;
                        default:
                            deb("[BAD!]");
                            Debug.WriteLine("Unimplemented G0 Character set: " + terminator);
                            return;
                    }

                case ")":  // Set G1 Character Set
                    switch (terminator)
                    {
                        case "A":
                            OnModeChanged(AnsiMode.SwitchG1toVT100_UK);
                            return;
                        case "B":
                            OnModeChanged(AnsiMode.SwitchG1toVT100_US);
                            return;
                        case "0":
                            OnModeChanged(AnsiMode.SwitchG1toVT100_LineDrawing);
                            return;
                        default:
                            deb("[BAD!]");
                            Debug.WriteLine("Unimplemented G1 Character set: " + terminator);
                            return;
                    }
            }

            deb("[BAD!]");
            Debug.WriteLine("Unimplemented ThreeLetter: Param=" + parameters + " Term=" + terminator);
        }

        protected override void ProcessCommandDCS(string parameters)
        {
            deb($"<DCS:{parameters}>");

            switch (parameters)
            {
                // DECRQSS
                case "$q\"p":   // DECSCL  - Conformance Level - 63;0
                    var decscl = new byte[] 
                    { ESC, COMMAND_DCS, (int)'0',(int) '$',(int)'r',
                      (int)'6', (int)'4', (int)';',         // High level
                      (int)'2',
                      (int)'p' ,
                      ESC, COMMAND_ST
                    };
                    OnOutput(decscl);
                    break;
                case "$qm":     // SGR - Character Attributes
                case "$q q":    // DECSCUSR
                case "$q\"q":   // DECSCA
                case "$qr":     // DECSTBM
                case "$qs":     // DECSLRM
                case "$qt":     // DECSLPP
                case "$q$|":    // DECSCPP
                case "$q*|":    // DECSNLS
                default:
                    deb("[BAD!]");
                    Debug.WriteLine("Unimplemented DCS: Param=" + parameters);
                    return;
            }
            
        }
        protected override void ProcessCommandAPC(string parameters, string terminator)
        {
            deb($"[APC:{parameters}]");
            if (string.IsNullOrEmpty(parameters)) return;

            if (parameters.StartsWith("V"))
            {
                ProcessOobApc(parameters.Substring(1));
            }
            else if (parameters.StartsWith("G"))
            {
                ProcessKittyGraphicsApc(parameters.Substring(1));
            }
        }

        private void ProcessOobApc(string content)
        {
            int semiIdx = content.IndexOf(';');
            string headerPart = semiIdx >= 0 ? content.Substring(0, semiIdx) : content;
            string payloadChunk = semiIdx >= 0 ? content.Substring(semiIdx + 1) : string.Empty;

            if (!OobHeader.TryParse(headerPart, out var header, out var error))
            {
                deb($"[OOB:PARSE_ERROR:{error}]");
                return;
            }

            deb($"[OOB:CHUNK(a={header.Action},i={header.Id},m={header.IsMore},t={header.PayloadType})]");

            if (m_oobReassembler.ProcessChunk(header, payloadChunk, out var packet, out var reassemblyError))
            {
                if (packet != null)
                {
                    deb($"[OOB:PACKET_COMPLETE(a={packet.Header.Action},i={packet.Header.Id},len={packet.Payload.Length})]");
                    DispatchOobPacket(packet);
                }
            }
            else if (!string.IsNullOrEmpty(reassemblyError))
            {
                deb($"[OOB:REASSEMBLY_ERROR:{reassemblyError}]");
            }
        }

        private void ProcessKittyGraphicsApc(string content)
        {
            int semiIdx = content.IndexOf(';');
            string controlsPart = semiIdx >= 0 ? content.Substring(0, semiIdx) : content;
            string payloadChunk = semiIdx >= 0 ? content.Substring(semiIdx + 1) : string.Empty;

            if (!KittyGraphicsCommand.TryParse(controlsPart, out var cmd, out var error))
            {
                deb($"[KITTY:GFX_PARSE_ERROR:{error}]");
                return;
            }

            deb($"[KITTY:GFX_CHUNK(a={cmd.Action},i={cmd.ImageId},p={cmd.PlacementId},f={cmd.Format},m={cmd.IsMore})]");

            if (cmd.Action == 'q')
            {
                deb($"[KITTY:GFX_QUERY({cmd.ImageId})]");
                if (cmd.QuietMode == 0)
                {
                    OnOutput(Encoding.ASCII.GetBytes($"\x1B_Gi={cmd.ImageId};OK\x1B\\"));
                }
                foreach (IAnsiDecoderClient client in m_listeners)
                {
                    client.KittyGraphicsCommand(this, cmd);
                }
                return;
            }

            if (m_kittyGfxReassembler.ProcessChunk(cmd, payloadChunk, out var completeCmd, out var gfxError))
            {
                if (completeCmd != null)
                {
                    deb($"[KITTY:GFX_COMPLETE(a={completeCmd.Action},i={completeCmd.ImageId})]");
                    foreach (IAnsiDecoderClient client in m_listeners)
                    {
                        client.KittyGraphicsCommand(this, completeCmd);
                    }
                }
            }
            else if (!string.IsNullOrEmpty(gfxError))
            {
                deb($"[KITTY:GFX_ERROR:{gfxError}]");
                if (cmd.QuietMode != 2)
                {
                    OnOutput(Encoding.ASCII.GetBytes($"\x1B_Gi={cmd.ImageId};EINVAL:{gfxError}\x1B\\"));
                }
            }
        }

        private void DispatchOobPacket(OobPacket packet)
        {
            OobPacketReceived?.Invoke(this, new OobPacketEventArgs(packet));

            lock (m_globalOobHandlers)
            {
                foreach (var handler in m_globalOobHandlers)
                {
                    try { handler.HandlePacket(packet.Header, packet.Payload); }
                    catch (Exception ex) { Debug.WriteLine("OOB handler exception: " + ex.Message); }
                }
            }

            if (!string.IsNullOrEmpty(packet.Header.Action))
            {
                lock (m_actionOobHandlers)
                {
                    if (m_actionOobHandlers.TryGetValue(packet.Header.Action, out var list))
                    {
                        foreach (var handler in list)
                        {
                            try { handler.HandlePacket(packet.Header, packet.Payload); }
                            catch (Exception ex) { Debug.WriteLine("OOB handler exception: " + ex.Message); }
                        }
                    }
                }
            }
        }

        public void RegisterOobHandler(IOobPacketHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            lock (m_globalOobHandlers)
            {
                if (!m_globalOobHandlers.Contains(handler))
                    m_globalOobHandlers.Add(handler);
            }
        }

        public void RegisterOobHandler(string action, IOobPacketHandler handler)
        {
            if (string.IsNullOrEmpty(action)) throw new ArgumentNullException(nameof(action));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            lock (m_actionOobHandlers)
            {
                if (!m_actionOobHandlers.TryGetValue(action, out var list))
                {
                    list = new List<IOobPacketHandler>();
                    m_actionOobHandlers[action] = list;
                }
                if (!list.Contains(handler))
                    list.Add(handler);
            }
        }

        public void RegisterOobHandler(string action, Action<OobHeader, byte[]> callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            RegisterOobHandler(action, new DelegateOobHandler(callback));
        }

        public void UnregisterOobHandler(IOobPacketHandler handler)
        {
            if (handler == null) return;
            lock (m_globalOobHandlers)
            {
                m_globalOobHandlers.Remove(handler);
            }
            lock (m_actionOobHandlers)
            {
                foreach (var list in m_actionOobHandlers.Values)
                {
                    list.Remove(handler);
                }
            }
        }

        public void SendOobPacket(
            string action,
            string? id = null,
            string? type = null,
            ReadOnlySpan<byte> payload = default,
            int maxChunkSize = 4096,
            string encoding = "b64",
            int status = 0,
            IDictionary<string, string>? customHeaders = null)
        {
            OobPacketEncoder.SendOobPacket(
                chunk => OnOutput(chunk),
                action, id, type, payload, maxChunkSize, encoding, status, customHeaders);
        }

        private sealed class DelegateOobHandler : IOobPacketHandler
        {
            private readonly Action<OobHeader, byte[]> _callback;
            public DelegateOobHandler(Action<OobHeader, byte[]> callback) => _callback = callback;
            public void HandlePacket(OobHeader header, ReadOnlySpan<byte> payload) => _callback(header, payload.ToArray());
        }


        protected override bool IsValidOneCharacterCommand(char _command)
        {
            return _command == '=' || _command == '>';
        }

        protected virtual void OnSetGraphicRendition(GraphicRendition[] _commands)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.SetGraphicRendition(this, _commands);
            }
        }

        protected virtual void OnScrollPageUpwards(int _linesToScroll)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.ScrollPageUpwards(this, _linesToScroll);
            }
        }
       

        protected virtual void OnScrollPageDownwards(int _linesToScroll)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.ScrollPageDownwards(this, _linesToScroll);
            }
        }

        protected virtual void OnModeChanged(AnsiMode _mode)
        {
            switch (_mode)
            {
                case AnsiMode.SwitchG0toVT100_LineDrawing:
                    deb("[F:LD]");
                    break;
                case AnsiMode.SwitchG0toVT100_US:
                    deb("[F:US]");
                    break;
                case AnsiMode.SwitchG0toVT100_UK:
                    deb("[F:UK]");
                    break;
                default:
                    deb($"[Mode:{_mode}]");
                    break;
            }
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.ModeChanged(this, _mode);
            }
        }

        protected virtual void OnSaveCursor()
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.SaveCursor(this);
            }
        }

        protected virtual void OnRestoreCursor()
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.RestoreCursor(this);
            }
        }

        protected virtual Point OnGetCursorPosition()
        {
            Point ret;
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                ret = client.GetCursorPosition(this);
                if (!ret.IsEmpty)
                {
                    return ret;
                }
            }
            return Point.Empty;
        }

        protected virtual void OnClearScreen(ClearDirection _direction)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.ClearScreen(this, _direction);
            }
        }

        protected virtual void OnClearNext(int numChars)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.ClearNext(this, numChars);
            }
        }
        protected virtual void OnClearLine(ClearDirection _direction)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.ClearLine(this, _direction);
            }
        }

        protected virtual void OnMoveCursorTo(Point _position)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.MoveCursorTo(this, _position);
            }
        }

        protected virtual void OnMoveCursorToColumn(int _columnNumber)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.MoveCursorToColumn(this, _columnNumber);
            }
        }

        protected virtual void OnMoveCursor(Direction _direction, int _amount, bool scroll)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.MoveCursor(this, _direction, _amount, scroll);
            }
        }

        protected virtual void OnMoveCursorToBeginningOfLineBelow(int _lineNumberRelativeToCurrentLine, bool scroll)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.MoveCursorToBeginningOfLineBelow(this, _lineNumberRelativeToCurrentLine, scroll);
            }
        }

        protected virtual void OnMoveCursorToBeginningOfLineAbove(int _lineNumberRelativeToCurrentLine, bool scroll)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.MoveCursorToBeginningOfLineAbove(this, _lineNumberRelativeToCurrentLine, scroll);
            }
        }

        protected override void OnCharacters(char[] _characters)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.Characters(this, _characters);
            }
        }

        protected void ClearTab(bool ClearAll)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.ClearTab(this, ClearAll);
            }
        }

        protected virtual void OnSetScrollingRegion(int top, int bottom)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.SetScrollingRegion(this, top, bottom);
            }
        }

        protected virtual void OnInsertLine(int count)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.InsertLine(this, count);
            }
        }

        protected virtual void OnDeleteLine(int count)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.DeleteLine(this, count);
            }
        }

        protected virtual void OnInsertCharacter(int count)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.InsertCharacter(this, count);
            }
        }

        protected virtual void OnDeleteCharacter(int count)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.DeleteCharacter(this, count);
            }
        }

        protected virtual void OnReset(bool soft)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.Reset(this, soft);
            }
        }
        private void DoCSI_SGR(string parameter)
        {
            deb($"[ANSI:SGR({parameter})]");

            if (string.IsNullOrEmpty(parameter))
            {
                OnSetGraphicRendition(new[] { GraphicRendition.Reset });
                OnResetColor(true);
                OnResetColor(false);
                return;
            }

            string normalized = parameter.Replace(':', ';');
            string[] rawTokens = normalized.Split(';');
            List<string> tokens = new List<string>();
            foreach (var t in rawTokens)
            {
                if (!string.IsNullOrEmpty(t))
                    tokens.Add(t);
            }

            List<GraphicRendition> commands = new List<GraphicRendition>();

            for (int i = 0; i < tokens.Count; i++)
            {
                int val = DecodeInt(tokens[i], 0);

                if (val == 4 && parameter.Contains("4:"))
                {
                    if (parameter.Contains("4:3"))
                    {
                        deb("[KITTY:UNDERLINE_STYLE(Curly)]");
                        OnSetUnderlineStyle(TerminalFrameBuffer.Underline.Curly);
                    }
                    else if (parameter.Contains("4:4"))
                    {
                        deb("[KITTY:UNDERLINE_STYLE(Dotted)]");
                        OnSetUnderlineStyle(TerminalFrameBuffer.Underline.Dotted);
                    }
                    else if (parameter.Contains("4:5"))
                    {
                        deb("[KITTY:UNDERLINE_STYLE(Dashed)]");
                        OnSetUnderlineStyle(TerminalFrameBuffer.Underline.Dashed);
                    }
                    else if (parameter.Contains("4:2"))
                    {
                        deb("[ANSI:UNDERLINE_DOUBLE]");
                        OnSetUnderlineStyle(TerminalFrameBuffer.Underline.Double);
                    }
                    else if (parameter.Contains("4:0"))
                    {
                        deb("[ANSI:NO_UNDERLINE]");
                        OnSetUnderlineStyle(TerminalFrameBuffer.Underline.None);
                    }
                    else
                    {
                        deb("[ANSI:UNDERLINE_SINGLE]");
                        OnSetUnderlineStyle(TerminalFrameBuffer.Underline.Single);
                    }
                    if (i + 1 < tokens.Count && int.TryParse(tokens[i + 1], out int sub) && sub >= 0 && sub <= 5)
                        i++;
                    continue;
                }

                if (val == 38)
                {
                    if (i + 1 < tokens.Count)
                    {
                        int mode = DecodeInt(tokens[i + 1], 0);
                        if (mode == 5 && i + 2 < tokens.Count)
                        {
                            int colorIdx = DecodeInt(tokens[i + 2], 0);
                            deb($"[XTERM:COLOR_256_FG({colorIdx})]");
                            OnSetColor256(true, colorIdx);
                            i += 2;
                            continue;
                        }
                        else if (mode == 2 && i + 4 < tokens.Count)
                        {
                            int r = Math.Clamp(DecodeInt(tokens[i + 2], 0), 0, 255);
                            int g = Math.Clamp(DecodeInt(tokens[i + 3], 0), 0, 255);
                            int b = Math.Clamp(DecodeInt(tokens[i + 4], 0), 0, 255);
                            deb($"[XTERM:COLOR_RGB_FG({r},{g},{b})]");
                            OnSetColorRgb(true, Color.FromArgb(r, g, b));
                            i += 4;
                            continue;
                        }
                    }
                }
                else if (val == 48)
                {
                    if (i + 1 < tokens.Count)
                    {
                        int mode = DecodeInt(tokens[i + 1], 0);
                        if (mode == 5 && i + 2 < tokens.Count)
                        {
                            int colorIdx = DecodeInt(tokens[i + 2], 0);
                            deb($"[XTERM:COLOR_256_BG({colorIdx})]");
                            OnSetColor256(false, colorIdx);
                            i += 2;
                            continue;
                        }
                        else if (mode == 2 && i + 4 < tokens.Count)
                        {
                            int r = Math.Clamp(DecodeInt(tokens[i + 2], 0), 0, 255);
                            int g = Math.Clamp(DecodeInt(tokens[i + 3], 0), 0, 255);
                            int b = Math.Clamp(DecodeInt(tokens[i + 4], 0), 0, 255);
                            deb($"[XTERM:COLOR_RGB_BG({r},{g},{b})]");
                            OnSetColorRgb(false, Color.FromArgb(r, g, b));
                            i += 4;
                            continue;
                        }
                    }
                }
                else if (val == 58)
                {
                    if (i + 1 < tokens.Count)
                    {
                        int mode = DecodeInt(tokens[i + 1], 0);
                        if (mode == 5 && i + 2 < tokens.Count)
                        {
                            int colorIdx = DecodeInt(tokens[i + 2], 0);
                            Color c = TerminalFrameBuffer.Get256Color(colorIdx);
                            deb($"[KITTY:UNDERLINE_COLOR_256({colorIdx})]");
                            OnSetUnderlineColor(c);
                            i += 2;
                            continue;
                        }
                        else if (mode == 2 && i + 4 < tokens.Count)
                        {
                            int r = Math.Clamp(DecodeInt(tokens[i + 2], 0), 0, 255);
                            int g = Math.Clamp(DecodeInt(tokens[i + 3], 0), 0, 255);
                            int b = Math.Clamp(DecodeInt(tokens[i + 4], 0), 0, 255);
                            deb($"[KITTY:UNDERLINE_COLOR_RGB({r},{g},{b})]");
                            OnSetUnderlineColor(Color.FromArgb(r, g, b));
                            i += 4;
                            continue;
                        }
                    }
                }
                else if (val == 59)
                {
                    deb("[KITTY:UNDERLINE_COLOR_RESET]");
                    OnSetUnderlineColor(null);
                    continue;
                }
                else if (val == 39)
                {
                    deb("[ANSI:SGR_DEFAULT_FG]");
                    OnResetColor(true);
                    continue;
                }
                else if (val == 49)
                {
                    deb("[ANSI:SGR_DEFAULT_BG]");
                    OnResetColor(false);
                    continue;
                }
                else if (val == 9)
                {
                    deb("[ANSI:SGR_STRIKETHROUGH]");
                    OnModeChanged(AnsiMode.Strikethrough);
                    continue;
                }
                else if (val == 29)
                {
                    deb("[ANSI:SGR_NO_STRIKETHROUGH]");
                    OnModeChanged(AnsiMode.NoStrikethrough);
                    continue;
                }
                else if (val == 53)
                {
                    deb("[ANSI:SGR_OVERLINE]");
                    OnModeChanged(AnsiMode.Overline);
                    continue;
                }
                else if (val == 55)
                {
                    deb("[ANSI:SGR_NO_OVERLINE]");
                    OnModeChanged(AnsiMode.NoOverline);
                    continue;
                }
                else if (val == 0)
                {
                    deb("[ANSI:SGR_RESET]");
                    commands.Add(GraphicRendition.Reset);
                    OnResetColor(true);
                    OnResetColor(false);
                    OnSetUnderlineColor(null);
                    OnSetUnderlineStyle(TerminalFrameBuffer.Underline.None);
                    OnModeChanged(AnsiMode.NoStrikethrough);
                    OnModeChanged(AnsiMode.NoOverline);
                }
                else
                {
                    commands.Add((GraphicRendition)val);
                }
            }

            if (commands.Count > 0)
            {
                OnSetGraphicRendition(commands.ToArray());
            }
        }

        protected virtual void OnMoveCursorToRow(int rowNumber)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.MoveCursorToRow(this, rowNumber);
            }
        }

        protected virtual void OnMoveCursorBackTab(int tabCount)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.MoveCursorBackTab(this, tabCount);
            }
        }

        protected virtual void OnRepeatCharacter(int count)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.RepeatCharacter(this, count);
            }
        }

        protected virtual void OnSetLeftRightMargins(int left, int right)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.SetLeftRightMargins(this, left, right);
            }
        }

        protected virtual void OnClearSavedLines()
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.ClearSavedLines(this);
            }
        }

        protected virtual void OnSetColor256(bool isForeground, int colorIndex)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.SetColor256(this, isForeground, colorIndex);
            }
        }

        protected virtual void OnSetColorRgb(bool isForeground, Color color)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.SetColorRgb(this, isForeground, color);
            }
        }

        protected virtual void OnResetColor(bool isForeground)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.ResetColor(this, isForeground);
            }
        }

        protected virtual void OnPushTitle()
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.PushTitle(this);
            }
        }

        protected virtual void OnPopTitle()
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.PopTitle(this);
            }
        }

        protected virtual Size OnGetSize()
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                Size sz = client.GetSize(this);
                if (!sz.IsEmpty) return sz;
            }
            return new Size(80, 24);
        }
        protected virtual void OnLockMemory(bool lockAbove)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.LockMemory(this, lockAbove);
            }
        }
        protected virtual void OnSetUnderlineStyle(TerminalFrameBuffer.Underline style)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.SetUnderlineStyle(this, style);
            }
        }

        protected virtual void OnSetUnderlineColor(Color? color)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.SetUnderlineColor(this, color);
            }
        }

        protected virtual void OnSetCursorShape(TerminalFrameBuffer.CursorShape shape, bool blinking)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.SetCursorShape(this, shape, blinking);
            }
        }

        protected virtual void OnSetCursorColor(Color? color)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.SetCursorColor(this, color);
            }
        }

        protected virtual void OnSetHyperlink(string? url, string? id)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.SetHyperlink(this, url, id);
            }
        }

        protected virtual void OnDesktopNotification(string notificationParams, string body)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.DesktopNotification(this, notificationParams, body);
            }
        }

        protected virtual void OnShellIntegration(string command, string? args)
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.ShellIntegration(this, command, args);
            }
        }

        private static Color? ParseColor(string spec)
        {
            if (string.IsNullOrEmpty(spec)) return null;
            if (spec.StartsWith("rgb:") && spec.Length >= 18)
            {
                var rgbParts = spec.Substring(4).Split('/');
                if (rgbParts.Length == 3 &&
                    int.TryParse(rgbParts[0].Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out int r) &&
                    int.TryParse(rgbParts[1].Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out int g) &&
                    int.TryParse(rgbParts[2].Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out int b))
                {
                    return Color.FromArgb(r, g, b);
                }
            }
            if (spec.StartsWith("#") && spec.Length == 7 &&
                int.TryParse(spec.Substring(1, 2), System.Globalization.NumberStyles.HexNumber, null, out int hr) &&
                int.TryParse(spec.Substring(3, 2), System.Globalization.NumberStyles.HexNumber, null, out int hg) &&
                int.TryParse(spec.Substring(5, 2), System.Globalization.NumberStyles.HexNumber, null, out int hb))
            {
                return Color.FromArgb(hr, hg, hb);
            }
            try
            {
                return Color.FromName(spec);
            }
            catch
            {
                return null;
            }
        }



        protected void SetTab()
        {
            foreach (IAnsiDecoderClient client in m_listeners)
            {
                client.SetTab(this);
            }
        }

        private static string[] FUNCTIONKEY_MAP = { 
        //      F1     F2     F3     F4     F5     F6     F7     F8     F9     F10    F11  F12
            "11", "12", "13", "14", "15", "17", "18", "19", "20", "21", "23", "24",
        //      F13    F14    F15    F16    F17  F18    F19    F20    F21    F22
            "25", "26", "28", "29", "31", "32", "33", "34", "23", "24" };

        bool IDecoder.KeyPressed(Keys _modifiers, Keys _key)
        {
            if ((int)Keys.F1 <= (int)_key && (int)_key <= (int)Keys.F12)
            {
                byte[] r = new byte[5];
                r[0] = 0x1B;
                r[1] = (byte)'[';
                int n = (int)_key - (int)Keys.F1;
                if ((_modifiers & Keys.Shift) != Keys.None)
                    n += 10;
                char tail;
                if (n >= 20)
                    tail = (_modifiers & Keys.Control) != Keys.None ? '@' : '$';
                else
                    tail = (_modifiers & Keys.Control) != Keys.None ? '^' : '~';
                string f = FUNCTIONKEY_MAP[n];
                r[2] = (byte)f[0];
                r[3] = (byte)f[1];
                r[4] = (byte)tail;
                OnOutput(r);
                return true;
            }
            else if (_key == Keys.Left || _key == Keys.Right || _key == Keys.Up || _key == Keys.Down)
            {
                byte[] r = new byte[3];
                r[0] = 0x1B;
                //if ( _cursorKeyMode == TerminalMode.Normal )
                r[1] = (byte)'[';
                //else
                //    r[1] = (byte) 'O';

                switch (_key)
                {
                    case Keys.Up:
                        r[2] = (byte)'A';
                        break;
                    case Keys.Down:
                        r[2] = (byte)'B';
                        break;
                    case Keys.Right:
                        r[2] = (byte)'C';
                        break;
                    case Keys.Left:
                        r[2] = (byte)'D';
                        break;
                    default:
                        Console.Error.WriteLine($"[libvt100:WARN] Unknown cursor key code: {_key}");
                        return false;
                }
                OnOutput(r);
                return true;
            }
            else
            {
                byte[] r = new byte[4];
                r[0] = 0x1B;
                r[1] = (byte)'[';
                r[3] = (byte)'~';
                if (_key == Keys.Insert)
                {
                    r[2] = (byte)'1';
                }
                else if (_key == Keys.Home)
                {
                    r[2] = (byte)'2';
                }
                else if (_key == Keys.PageUp)
                {
                    r[2] = (byte)'3';
                }
                else if (_key == Keys.Delete)
                {
                    r[2] = (byte)'4';
                }
                else if (_key == Keys.End)
                {
                    r[2] = (byte)'5';
                }
                else if (_key == Keys.PageDown)
                {
                    r[2] = (byte)'6';
                }
                else if (_key == Keys.Enter)
                {
                    r = new byte[] { 13 };
                }
                else if (_key == Keys.Escape)
                {
                    r = new byte[] { 0x1B };
                }
                else if (_key == Keys.Tab)
                {
                    r = new byte[] { (byte)'\t' };
                }
                else
                {
                    return false;
                }
                OnOutput(r);
                return true;
            }
        }

        void IAnsiDecoder.Subscribe(IAnsiDecoderClient _client)
        {
            m_listeners.Add(_client);
        }

        void IAnsiDecoder.UnSubscribe(IAnsiDecoderClient _client)
        {
            m_listeners.Remove(_client);
        }

        void IDisposable.Dispose()
        {
            m_listeners.Clear();
        }
    }
}
