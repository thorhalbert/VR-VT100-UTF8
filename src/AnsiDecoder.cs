using System;
using System.Text;
using System.Drawing;
using System.Collections.Generic;
using System.Diagnostics;

namespace libVT100
{
    public class AnsiDecoder : EscapeCharacterDecoder, IAnsiDecoder
    {
        protected List<IAnsiDecoderClient> m_listeners;

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

        public AnsiDecoder()
           : base()
        {
            m_listeners = [];
        }

        public void deb(string s)
        {
            if (dvt is not null) dvt.Append(s);
        }
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
        protected override void ProcessCommandCSI(byte _command, String _parameter)
        {
            //System.Console.WriteLine ( "ProcessCommand: {0} {1}", (char) _command, _parameter );
            deb($"<CSI:{(char)_command},{_parameter}>");

            switch ((char)_command)
            {
                case 'A':  // CSI Ps A  Cursor Up Ps Times (default = 1) (CUU).
                    deb("[CUU]");
                    OnMoveCursor(Direction.Up, DecodeInt(_parameter, 1), false);
                    break;

                case 'B':  // CSI Ps B  Cursor Down Ps Times (default = 1) (CUD).
                    deb("[CUD]");
                    OnMoveCursor(Direction.Down, DecodeInt(_parameter, 1), false);
                    break;

                case 'C':  // CSI Ps C Cursor Forward Ps Times(default = 1)(CUF).
                    deb("[CUF]");
                    OnMoveCursor(Direction.Forward, DecodeInt(_parameter, 1), false);
                    break;

                case 'D':  // CSI Ps D  Cursor Backward Ps Times (default = 1) (CUB).
                    deb("[CUB]");
                    OnMoveCursor(Direction.Backward, DecodeInt(_parameter, 1), false);
                    break;

                case 'E':  // CSI Ps E  Cursor Next Line Ps Times (default = 1) (CNL).
                    deb("[CNL]");
                    OnMoveCursorToBeginningOfLineBelow(DecodeInt(_parameter, 1), false);
                    break;

                case 'F':  // CSI Ps F  Cursor Preceding Line Ps Times (default = 1) (CPL).
                    deb("[CPL]");
                    OnMoveCursorToBeginningOfLineAbove(DecodeInt(_parameter, 1), false);
                    break;

                case 'G': // CSI Ps G  Cursor Character Absolute  [column] (default = [row,1]) (CHA).                 
                    var dec = DecodeInt(_parameter, 1) - 1;
                    deb($"[CHA:{dec}]");
                    OnMoveCursorToColumn(dec);
                    break;

                case 'H':  //CSI Ps ; Ps H -  Cursor Position[row; column] (default = [1, 1])(CUP).
                case 'f':
                    {
                        int separator = _parameter.IndexOf(';');
                        if (separator == -1)
                        {
                            deb($"[CUP:0,0]");
                            OnMoveCursorTo(new Point(0, 0));
                        }
                        else
                        {
                            String row = _parameter.Substring(0, separator);
                            String column = _parameter.Substring(separator + 1, _parameter.Length - separator - 1);
                            var cl = DecodeInt(column, 1) - 1;
                            var rl = DecodeInt(row, 1) - 1;
                            deb($"[CUP:{cl},{rl}]");
                            OnMoveCursorTo(new Point(cl, rl));
                        }
                    }
                    break;

                case 'I':   // CSI Ps I  Cursor Forward Tabulation Ps tab stops (default = 1) (CHT).
                    deb("[cht]");
                    break;

                case 'J':
                    // CSI Ps J Erase in Display(ED), VT100.
                    //       Ps = 0->Erase Below(default).
                    //       Ps = 1->Erase Above.
                    //       Ps = 2->Erase All.
                    //       Ps = 3->Erase Saved Lines(xterm).
                    deb($"[ED:{_parameter}]");
                    OnClearScreen((ClearDirection)DecodeInt(_parameter, 0));
                    break;

                case 'K':
                    // CSI Ps K Erase in Line(EL), VT100.
                    //          Ps = 0->Erase to Right(default).
                    //          Ps = 1->Erase to Left.
                    //          Ps = 2->Erase All.
                    deb($"[EL:{_parameter}]");
                    OnClearLine((ClearDirection)DecodeInt(_parameter, 0));
                    break;

                case 'M':  // CSI Ps M  Delete Ps Line(s) (default = 1) (DL).
                    deb("[dl]");
                    break;

                case 'S':  // CSI Ps S  Scroll up Ps lines (default = 1) (SU), VT420, ECMA-48.
                    deb("[SU]");
                    OnScrollPageUpwards(DecodeInt(_parameter, 1));
                    break;

                case 'T':  // CSI Ps T  Scroll down Ps lines (default = 1) (SD), VT420.
                    deb("[SD]");
                    OnScrollPageDownwards(DecodeInt(_parameter, 1));
                    break;

                case 'X':  // CSI ps X  Erase Ps characters [ECH]  (is this an xterm? - we get this, but vt100 spec doesn't have this -- this might fix major bug)
                    // As I read on, this is a vt420/vt510 extension
                    deb("[ECH]");
                    OnClearNext(DecodeInt(_parameter, 0));                
                    break;

                case 'c':   // CSI Ps c  Send Device Attributes (Primary DA).
                    DoCSI_PrimaryDA(_parameter);
                    break;

                case 'h':  // CSI ? Pm h - DEC Private Mode Set(DECSET).
                    DoCSI_DECSET(_command, _parameter);
                    break;

                case 'g':  // CSI Ps g  Tab Clear (TBC).
                    deb("[TBC]");
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
                    
                case 'l':  // CSI ? Pm l - DEC Private Mode Reset(DECRST).
                    DoCSI_DECRST(_command, _parameter);
                    break;


                case 'm':  // CSI Pm m  Character Attributes (SGR).
                    {
                        deb("[SGR]");
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
                    deb("[CPL]");
                    break;

                case 's':   // CSI s     Save cursor, available only when DECLRMM is disabled (SCOSC, also ANSI.SYS).
                    deb("[SCOSC]");
                    OnSaveCursor();
                    break;

                case 't':  // Window manipulation EWMH
                    deb("[EWMH]");
                    DoCSI_WindowManipulation(_parameter);
                    break;


                case 'u':   // CSI u     Restore cursor (SCORC, also ANSI.SYS).
                    deb("[SCORC]");
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

                // Currently unimplemented vt510/vt420 sequences - https://vt100.net/docs/vt510-rm/chapter4.html
                // We may ultimately want to get these to be fully xterm compatible - some of them are a bit wierd - hard to know if curses would produce them, though we can check termcap

                case '~':   // Vt420/vt510 extension - delete column
                    deb("DECDC-unimplemented");
                    break;
                case '@':   // Vt420/vt510 extension - insert character
                    deb("ICH-unimplemented");
                    break;
                case '\'':   // Vt420/vt510 extension - insert column
                    deb("DECIC-unimplemented");
                    break;


                // Unknowns
                case '(':
                case 'j':
                case '\\':
                case ']':

                default:
                    deb("[BAD!]");
                    Debug.WriteLine("Unimplemented CSI: Command=" + _command + " Param=" + _parameter);
                    break;
            }
        }

        private void DoCSI_WindowManipulation(string parameter)
        {
            // These are all explicit commands with fixed parameters
            deb($"<WIN:{parameter}>");

            switch (parameter)
            {
                case "18":  // Report size of text area in characters
                    //var result18 = new byte[] { ESC, COMMAND_CSI, (int)'8', (int)'n' };
                    //return;
                case "19":  // Report the size of the screen in characters
                    //var result19 = new byte[] { ESC, COMMAND_CSI, (int)'9', (int)'n' };
                    //return;

                case "1":   // Deiconify Window
                case "2":   // Iconify window
                case "5":   // Raise to front of stack
                case "6":   // Lower to bottom of stack
                case "7":   // Refresh the window
                case "9;0": // Restore maximized window
                case "9;1": // Maximize Window
                case "9;2": // Maximize Window vertically
                case "9;3": // Maximize Window horizontally
                case "10;0": // Undo Full Screen Mode
                case "10;1": // Change to full-screen
                case "10;2": // Toggle-full Screen
                case "11":  // Report xterm Window State
                case "13":  // Report text area position
                case "14":  // Report text area size in pixels
                case "14;2": // Report size of window size in pixels
                case "15":  // Report size of the screen in pixels
                case "16":  // Report character size in pixels                           
                case "20":  // Report window's icon label
                case "21":  // Report Windows title
                case "22;0": // Save xterm icon and window on stack
                case "22;1": // Save xterm icon title on stack
                case "22;2": // Save window title on stack
                case "23;0": // Restore icon and window title from stack
                case "23;1": // Resore icon title from stack
                case "23;2": // Restore window title from stack
                case "24":   // >=24 is resize DECSLPP
                    deb("[BAD!]");
                    Debug.WriteLine("Unimplemented Window Manipulation: " + parameter);
                    return;
            }
            // While these have a fixed initial parameter and variable arguments in later parameters
            var parms = parameter.Split(';');
            switch (parms[0])
            {
                case "3":   // Move WIndow to x,y (parm1=X, parm2=Y)
                case "4":   // Resize window (parm1=height, parm2=width)
                case "8":   // Resize text area (parm1=height, parm2=width)
                    Debug.WriteLine("Unimplemented Window Manipulation: " + parameter);
                    return;
            }

            deb("[BAD!]");
            Debug.WriteLine("Unimplemented (and unknown) Window Manipulation: " + parameter);
            return;
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
                    deb("[BAD!]");
                    Debug.WriteLine("Unimplemented CSI Command=DSR, Param=" + _parameter);
                    break;
            }
        }

        private void DoCSI_DECRST(byte _command, string _parameter)
        {
            deb($"<DECRST:{_command},{_parameter}>");

            switch (_parameter)
            {
                case "4":
                    // CSI 4 l restores the DEC Private Mode 4, which is specifically related to application cursor keys.
                    break;
                case "17":
                case "?17":
                    OnClearScreen(ClearDirection.Both);
                    break;
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
                    deb("[BAD!]");
                    Debug.WriteLine("Unimplemented CSI: Command=DECRST, Param=" + _parameter);
                    break;
                   
            }
        }

        private void DoCSI_DECSET(byte _command, string _parameter)
        {
            deb($"<DECSET:{_command},{_parameter}>");

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
                    deb("[BAD!]");
                    Debug.WriteLine("Unimplemented CSI: Command=DECSET, Param=" + _parameter);
                    break;
            }
        }

        private void DoCSI_PrimaryDA(string _parameter)
        {
            deb($"<P_DA:{_parameter}>");

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
            deb($"<OSC:{parameters},{terminator}>");

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
            deb($"<C2:{terminator}>");
            switch (terminator)
            {
                case "D":  // IND (Index down - with scroll)
                    deb("[IND]");
                    OnMoveCursor(Direction.Down, 1, true);
                    return;
                case "M":  // RI (Reverse Index up - with scroll)
                    deb("[RI]");
                    OnMoveCursor(Direction.Up, 1, true);
                    return;
                case "E":  // NEL (Next Line - with scroll)
                    deb("[NEL]");
                    OnMoveCursorToBeginningOfLineBelow(1, true);
                    return;
                case "H":  // Tab Set(HTS  is 0x88). - need to handle the 8 bit version of this
                    deb("[HTS]");
                    SetTab();
                    break;
                case "=":  // ESC =     Application Keypad (DECKPAM).
                    deb("[DECKPAM]");
                    OnModeChanged(AnsiMode.ApplicationKeypad_DECKPAM);
                    break;
                case ">":  // ESC >     Normal Keypad (DECKPNM), VT100.
                    deb("[DECKPNM]");
                    OnModeChanged(AnsiMode.NormalKeypad_DECKPNM);
                    break;
                case "c": // ESC c     Full Reset (RIS), VT100.
                    deb("[RIS]");
                    OnClearScreen(ClearDirection.Both);
                    break;

                case "7":
                    // Save cursor position
                case "8":
                    // Restore cursor position

                default:
                    deb("[BAD!]");
                    Debug.WriteLine("Unimplemented TwoLetter: Term=" + terminator);
                    break;
            }
        }

        protected override void ProcessCommandThree(string parameters, string terminator)
        {
            deb($"<C3:{parameters},{terminator}>");

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
                            deb("[BAD!]");
                            Debug.WriteLine("Unimplemented G0 Character set: " + terminator);
                            return;
                    }

            }

            switch (parameters)
            {
                case ")":  // Set G0 Character Set (there are lots)
                    switch (terminator)
                    {
                        case "0":
                            OnModeChanged(AnsiMode.SwitchG0toVT100_US);
                            return;
                        default:
                            deb("[BAD!]");
                            Debug.WriteLine("Unimplemented G0 Character set: " + terminator);
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
