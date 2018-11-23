using System;
using System.Text;
using System.Drawing;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Diagnostics;

namespace libVT100
{
    public class AnsiDecoder : EscapeCharacterDecoder, IAnsiDecoder
    {
        protected List<IAnsiDecoderClient> m_listeners;

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

        public AnsiDecoder()
           : base()
        {
            m_listeners = new List<IAnsiDecoderClient>();
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
        protected override void ProcessCommandCSI(byte _command, String _parameter)
        {
            //System.Console.WriteLine ( "ProcessCommand: {0} {1}", (char) _command, _parameter );
            switch ((char)_command)
            {
                case 'A':  // CSI Ps A  Cursor Up Ps Times (default = 1) (CUU).
                    OnMoveCursor(Direction.Up, DecodeInt(_parameter, 1), false);
                    break;

                case 'B':  // CSI Ps B  Cursor Down Ps Times (default = 1) (CUD).
                    OnMoveCursor(Direction.Down, DecodeInt(_parameter, 1), false);
                    break;

                case 'C':  // CSI Ps C Cursor Forward Ps Times(default = 1)(CUF).
                    OnMoveCursor(Direction.Forward, DecodeInt(_parameter, 1), false);
                    break;

                case 'D':  // CSI Ps D  Cursor Backward Ps Times (default = 1) (CUB).
                    OnMoveCursor(Direction.Backward, DecodeInt(_parameter, 1), false);
                    break;

                case 'E':  // CSI Ps E  Cursor Next Line Ps Times (default = 1) (CNL).
                    OnMoveCursorToBeginningOfLineBelow(DecodeInt(_parameter, 1), false);
                    break;

                case 'F':  // CSI Ps F  Cursor Preceding Line Ps Times (default = 1) (CPL).
                    OnMoveCursorToBeginningOfLineAbove(DecodeInt(_parameter, 1), false);
                    break;

                case 'G': // CSI Ps G  Cursor Character Absolute  [column] (default = [row,1]) (CHA).
                    OnMoveCursorToColumn(DecodeInt(_parameter, 1) - 1);
                    break;

                case 'H':  //CSI Ps ; Ps H -  Cursor Position[row; column] (default = [1, 1])(CUP).
                case 'f':
                    {
                        int separator = _parameter.IndexOf(';');
                        if (separator == -1)
                        {
                            OnMoveCursorTo(new Point(0, 0));
                        }
                        else
                        {
                            String row = _parameter.Substring(0, separator);
                            String column = _parameter.Substring(separator + 1, _parameter.Length - separator - 1);
                            OnMoveCursorTo(new Point(DecodeInt(column, 1) - 1, DecodeInt(row, 1) - 1));
                        }
                    }
                    break;

                case 'I':   // CSI Ps I  Cursor Forward Tabulation Ps tab stops (default = 1) (CHT).
                    break;

                case 'J':
                    // CSI Ps J Erase in Display(ED), VT100.
                    //       Ps = 0->Erase Below(default).
                    //       Ps = 1->Erase Above.
                    //       Ps = 2->Erase All.
                    //       Ps = 3->Erase Saved Lines(xterm).
                    OnClearScreen((ClearDirection)DecodeInt(_parameter, 0));
                    break;

                case 'K':
                    // CSI Ps K Erase in Line(EL), VT100.
                    //          Ps = 0->Erase to Right(default).
                    //          Ps = 1->Erase to Left.
                    //          Ps = 2->Erase All.
                    OnClearLine((ClearDirection)DecodeInt(_parameter, 0));
                    break;

                case 'M':  // CSI Ps M  Delete Ps Line(s) (default = 1) (DL).
                    break;

                case 'S':  // CSI Ps S  Scroll up Ps lines (default = 1) (SU), VT420, ECMA-48.
                    OnScrollPageUpwards(DecodeInt(_parameter, 1));
                    break;

                case 'T':  // CSI Ps T  Scroll down Ps lines (default = 1) (SD), VT420.
                    OnScrollPageDownwards(DecodeInt(_parameter, 1));
                    break;

                case 'c':   // CSI Ps c  Send Device Attributes (Primary DA).
                    DoCSI_PrimaryDA(_parameter);
                    break;

                case 'h':  // CSI ? Pm h - DEC Private Mode Set(DECSET).
                    DoCSI_DECSET(_command, _parameter);
                    break;

                case 'l':  // CSI ? Pm l - DEC Private Mode Reset(DECRST).
                    DoCSI_DECRST(_command, _parameter);
                    break;


                case 'm':  // CSI Pm m  Character Attributes (SGR).
                    {
                        String[] commands = _parameter.Split(';');
                        GraphicRendition[] renditionCommands = new GraphicRendition[commands.Length];
                        for (int i = 0; i < commands.Length; ++i)
                        {
                            renditionCommands[i] = (GraphicRendition)DecodeInt(commands[i], 0);
                            //System.Console.WriteLine ( "Rendition command: {0} = {1}", commands[i], renditionCommands[i]);
                        }
                        OnSetGraphicRendition(renditionCommands);
                    }
                    break;

                case 'n':  // CSI Ps n  Device Status Report (DSR).
                    DoCSI_DSR(_parameter);
                    break;

                case 'r':
                    // CSI Ps ; Ps r Set Scrolling Region [top;bottom] (default = full size of window) (DECSTBM), VT100.
                    // CSI ? Pm r  Restore DEC Private Mode Values.The value of Ps previously saved is restored.Ps values are the same as for DECSET.
                    // CSI Pt; Pl; Pb ; Pr; Ps $ r   Change Attributes in Rectangular Area(DECCARA), VT400 and up.   Pt; Pl; Pb; Pr denotes the rectangle. Ps denotes the SGR attributes to change: 0, 1, 4, 5, 7.
                    break;

                case 's':   // CSI s     Save cursor, available only when DECLRMM is disabled (SCOSC, also ANSI.SYS).
                    OnSaveCursor();
                    break;

                case 'u':   // CSI u     Restore cursor (SCORC, also ANSI.SYS).
                    OnRestoreCursor();
                    break;




                case '>':
                    // Set numeric keypad mode
                    OnModeChanged(AnsiMode.NumericKeypad);
                    break;

                case '=':
                    OnModeChanged(AnsiMode.AlternateKeypad);
                    // Set alternate keypad mode (rto: non-numeric, presumably)
                    break;

                    // Unknowns
                case '(':
                case 'j':
                case '\\':
                case ']':

                default:
                    Debug.WriteLine("Unimplemented CSI: Command=" + _command + " Param=" + _parameter);
                    break;
            }
        }

        private void DoCSI_DSR(string _parameter)
        {
            switch (_parameter)
            {
                case "5":  //   Ps = 5  -> Status Report.
                    var resultOk = new byte[] { ESC, LBRACK, (int)'0', (int)'n' };
                    OnOutput(resultOk);
                    break;
                case "6":  //   Ps = 6  -> Report Cursor Position (CPR) [row;column].
                    Point cursorPosition = OnGetCursorPosition();
                    cursorPosition.X++;
                    cursorPosition.Y++;
                    String row = cursorPosition.Y.ToString();
                    String column = cursorPosition.X.ToString();
                    byte[] output = new byte[2 + row.Length + 1 + column.Length + 1];
                    int i = 0;
                    output[i++] = ESC;
                    output[i++] = LBRACK;
                    foreach (char c in row)
                    {
                        output[i++] = (byte)c;
                    }
                    output[i++] = (byte)';';
                    foreach (char c in column)
                    {
                        output[i++] = (byte)c;
                    }
                    output[i++] = (byte)'R';
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
                    Debug.WriteLine("Unimplemented CSI Command=DSR, Param=" + _parameter);
                    break;
            }
        }

        private void DoCSI_DECRST(byte _command, string _parameter)
        {
            switch (_parameter)
            {
                case "20":  //  Ps = 2 0  -> Normal Linefeed (LNM).
                    OnModeChanged(AnsiMode.LineFeed);
                    break;

                case "?1":  // Ps = 1  -> Normal Cursor Keys (DECCKM), VT100.
                    OnModeChanged(AnsiMode.CursorKeyToCursor);
                    break;

                case "?2":  // Ps = 2  -> Designate VT52 mode (DECANM), VT100.
                    OnModeChanged(AnsiMode.VT52);
                    break;

                case "?3":  // Ps = 3  -> 80 Column Mode (DECCOLM), VT100.
                    OnModeChanged(AnsiMode.Columns80);
                    break;

                case "?4":  // Ps = 4  -> Jump (Fast) Scroll (DECSCLM), VT100.
                    OnModeChanged(AnsiMode.JumpScrolling);
                    break;

                case "?5":  // Ps = 5  -> Normal Video (DECSCNM), VT100.
                    OnModeChanged(AnsiMode.NormalVideo);
                    break;

                case "?6":  // Ps = 6  -> Normal Cursor Mode (DECOM), VT100.
                    OnModeChanged(AnsiMode.OriginIsAbsolute);
                    break;

                case "?7":  // Ps = 7  -> No Auto-wrap Mode (DECAWM), VT100.
                    OnModeChanged(AnsiMode.DisableLineWrap);
                    break;

                case "?8":  // Ps = 8  -> No Auto-repeat Keys (DECARM), VT100.
                    OnModeChanged(AnsiMode.DisableAutoRepeat);
                    break;

                case "?9":  // Ps = 9  -> Don't send Mouse X & Y on button press, xterm.
                    OnModeChanged(AnsiMode.DisableInterlacing);
                    break;

                case "?12":  //  Ps = 1 2  -> Stop Blinking Cursor (AT&T 610).
                    break;

                case "?25":  // Ps = 2 5  -> Hide Cursor (DECTCEM), VT220.
                    OnModeChanged(AnsiMode.HideCursor);
                    break;

                case "?40":  //  Ps = 4 0  -> Disallow 80 -> 132 Mode, xterm.
                    break;

                case "?1049":
                    // Ps = 1 0 4 9->Use Normal Screen Buffer and restore cursor
                    // as in DECRC, xterm.This may be disabled by the titeInhibit
                    // resource.This combines the effects of the 1 0 4 7  and 1 0 4
                    // 8  modes.Use this with terminfo-based applications rather
                    // than the 4 7  mode.
                    OnModeChanged(AnsiMode.SwitchToMainBuffer);
                    break;

                default:
                    Debug.WriteLine("Unimplemented CSI: Command=DECRST, Param=" + _parameter);
                    break;
                   
            }
        }

        private void DoCSI_DECSET(byte _command, string _parameter)
        {
            switch (_parameter)
            {
                case "":
                    //Set ANSI (versus VT52)  DECANM
                    OnModeChanged(AnsiMode.ANSI);
                    break;

                case "20":
                    // Set new line mode
                    OnModeChanged(AnsiMode.NewLine);
                    break;

                case "?1":
                    // Set cursor key to application  DECCKM
                    OnModeChanged(AnsiMode.CursorKeyToApplication);
                    break;

                case "?3":
                    // Set number of columns to 132  DECCOLM
                    OnModeChanged(AnsiMode.Columns132);
                    break;

                case "?4":
                    // Set smooth scrolling  DECSCLM
                    OnModeChanged(AnsiMode.SmoothScrolling);
                    break;

                case "?5":
                    // Set reverse video on screen  DECSCNM
                    OnModeChanged(AnsiMode.ReverseVideo);
                    break;

                case "?6":
                    // Set origin to relative  DECOM
                    OnModeChanged(AnsiMode.OriginIsRelative);
                    break;

                case "?7":
                    //  Set auto-wrap mode  DECAWM
                    // Enable line wrap
                    OnModeChanged(AnsiMode.LineWrap);
                    break;

                case "?8":
                    // Set auto-repeat mode  DECARM
                    OnModeChanged(AnsiMode.AutoRepeat);
                    break;

                case "?9":
                    /// Set interlacing mode 
                    OnModeChanged(AnsiMode.Interlacing);
                    break;

                case "?25":
                    OnModeChanged(AnsiMode.ShowCursor);
                    break;

                case "?40":    // XTERM Allow 80/132 mode
                    break;

                case "?1047":
                // Ps = 1 0 4 7->Use Normal Screen Buffer, xterm.Clear the
                // screen first if in the Alternate Screen Buffer.  This may be
                // disabled by the titeInhibit resource.
                case "?1049":
                    // Ps = 1 0 4 9->Save cursor as in DECSC, xterm.After sav-
                    // ing the cursor, switch to the Alternate Screen Buffer, clear-
                    // ing it first.  This may be disabled by the titeInhibit
                    // resource.This control combines the effects of the 1 0 4 7
                    // and 1 0 4 8  modes.Use this with terminfo-based applications
                    // rather than the 4 7  mode.
                    OnModeChanged(AnsiMode.SwitchToAlternateBuffer);
                    break;

                // Need to implement
                case "?12;25":  //   Ps = 1 2  -> Start Blinking Cursor (AT&T 610).
                
                default:
                    Debug.WriteLine("Unimplemented CSI: Command=DECSET, Param=" + _parameter);
                    break;
            }
        }

        private void DoCSI_PrimaryDA(string _parameter)
        {
            switch (_parameter)
            {
                case "0":   //    Ps = 0  or omitted -> request attributes from terminal.  The response depends on the decTerminalID resource setting.
                            // cygterm generates "63;1;2;4;6;22c"
                            // xfce generates "62;9;c"
                            // xterm generates "64;1;2;6;15;18;21;22c"
                    var da = new byte[] { ESC, LBRACK, (int)'?',
                                (int)'6', (int)'4', (int)';',
                                (int)'1', (int)';',
                                (int)'2', (int)';',
                                (int)'6', (int)';',
                                (int)'1', (int)'5', (int)';',
                                (int)'1', (int)'8', (int)';',
                                (int)'2', (int)'1', (int)';',
                                (int)'2', (int)'2', (int)';',
                                (int)'c' };   // Send the xterm string for now 
                    OnOutput(da);
                    break;
                case "=0":  //      Ps = 0  -> report Terminal Unit ID (default), VT400.  XTerm uses zeros for the site code and serial number in its DECRPTUI response.
                    break;
                case ">0":
                    // Send Device Attributes(Secondary DA).
                    //   Ps = 0  or omitted -> request the terminal's identification
                    //   code.The response depends on the decTerminalID resource set-
                    //   ting.It should apply only to VT220 and up, but xterm extends
                    //    this to VT100.
                    //       -> CSI > Pp; Pv; Pc c
                    //    where Pp denotes the terminal type
                    //        Pp = 0-> "VT100".
                    //        Pp = 1-> "VT220".
                    //        Pp = 2-> "VT240".
                    //        Pp = 1 8-> "VT330".
                    //        Pp = 1 9-> "VT340".
                    //        Pp = 2 4-> "VT320".
                    //        Pp = 4 1-> "VT420".
                    //        Pp = 6 1-> "VT510".
                    //        Pp = 6 4-> "VT520".
                    //        Pp = 6 5-> "VT525".
                    //
                    //    and Pv is the firmware version(for xterm, this was originally
                    //
                    //    the XFree86 patch number, starting with 95).In a DEC termi -
                    //    nal, Pc indicates the ROM cartridge registration number and is
                    //    always zero.
                    break;
                default:
                    Debug.WriteLine("See Unhandled: CSI - Command: DA, " + _parameter + " c");
                    break;
            }
        }
        #endregion

        protected override void ProcessCommandOSC(string parameters, string terminator)
        {
            var parts = parameters.Split(new char[] { ';' }, 2);
            switch (parts[0])
            {
                case "0":
                    foreach (IAnsiDecoderClient client in m_listeners)
                    {
                        client.SetProperty(this, PropertyTypes.IconAndTitle, parts[1]);
                    }
                    break;
                case "1":
                    foreach (IAnsiDecoderClient client in m_listeners)
                    {
                        client.SetProperty(this, PropertyTypes.IconName, parts[1]);
                    }
                    break;
                case "2":
                    foreach (IAnsiDecoderClient client in m_listeners)
                    {
                        client.SetProperty(this, PropertyTypes.WindowTitle, parts[1]);
                    }
                    break;
            }
        }

        protected override void ProcessCommandTwo(string terminator)
        {
            switch (terminator)
            {
                case "D":  // IND (Index down - with scroll)
                    OnMoveCursor(Direction.Down, 1, true);
                    return;
                case "M":  // RI (Reverse Index up - with scroll)
                    OnMoveCursor(Direction.Up, 1, true);
                    return;
                case "E":  // NEL (Next Line - with scroll)
                    OnMoveCursorToBeginningOfLineBelow(1, true);
                    return;

                case "=":  // ESC =     Application Keypad (DECKPAM).
                    OnModeChanged(AnsiMode.ApplicationKeypad_DECKPAM);
                    break;
                case ">":  // ESC >     Normal Keypad (DECKPNM), VT100.
                    OnModeChanged(AnsiMode.NormalKeypad_DECKPNM);
                    break;
                case "c": // ESC c     Full Reset (RIS), VT100.
                    OnClearScreen(ClearDirection.Both);
                    break;

                default:
                    Debug.WriteLine("Unimplemented TwoLetter: Term=" + terminator);
                    break;
            }
        }

        protected override void ProcessCommandThree(string parameters, string terminator)
        {
            switch (parameters)
            {
                case "(":  // Set G0 Character Set (there are lots)
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
                            Debug.WriteLine("Unimplemented G0 Character set: " + terminator);
                            return;
                    }

            }

            Debug.WriteLine("Unimplemented ThreeLetter: Param=" + parameters + " Term=" + terminator);
        }

        protected override void ProcessCommandDCS(string parameters)
        {
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
                    Debug.WriteLine("Unimplemented DCS: Param=" + parameters);
                    return;
            }
            
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
                        throw new ArgumentException("unknown cursor key code", "key");
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
                    //return new byte[] { 0x1B, (byte) 'M', (byte) '~' };
                    //r[1] = (byte) 'O';
                    //r[2] = (byte) 'M';
                    //return new byte[] { (byte) '\r', (byte) '\n' };
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
            m_listeners = null;
        }
    }
}
