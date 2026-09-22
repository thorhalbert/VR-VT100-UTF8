using System;
using System.Drawing;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static libVT100.TerminalFrameBuffer.GraphicAttributes;
using System.Data.Common;
using libVT100.KittyGraphics;

using System.Runtime.CompilerServices;

namespace libVT100
{
    /// <summary>
    /// In-memory character-cell framebuffer supporting VT100, VT220, and xterm terminal emulation.
    /// Manages primary and alternate screen grids, scrolling regions, text rendition attributes, and cursor state.
    /// </summary>
    public class TerminalFrameBuffer : IAnsiDecoderClient, IEnumerable<TerminalFrameBuffer.Glyph>, IDisposable
    {
        #region Define Enums

        /// <summary>Text blinking rate.</summary>
        public enum Blink
        {
            /// <summary>No blinking.</summary>
            None = 0,
            /// <summary>Slow blinking (less than 150 per minute).</summary>
            Slow = 1,
            /// <summary>Rapid blinking (150 per minute or more).</summary>
            Rapid = 2,
        }

        /// <summary>Text underline style (standard SGR 4, 21 and Kitty extended SGR 4:x).</summary>
        public enum Underline
        {
            /// <summary>No underline.</summary>
            None = 0,
            /// <summary>Single straight underline (SGR 4 or 4:1).</summary>
            Single = 1,
            /// <summary>Double underline (SGR 21 or 4:2).</summary>
            Double = 2,
            /// <summary>Curly / wavy underline (Kitty SGR 4:3).</summary>
            Curly = 3,
            /// <summary>Dotted underline (Kitty SGR 4:4).</summary>
            Dotted = 4,
            /// <summary>Dashed underline (Kitty SGR 4:5).</summary>
            Dashed = 5,
        }

        /// <summary>Text cursor shape styles (DECSCUSR).</summary>
        public enum CursorShape
        {
            /// <summary>Full character block cursor.</summary>
            Block,
            /// <summary>Underline cursor below character.</summary>
            Underline,
            /// <summary>Vertical bar / I-beam cursor.</summary>
            Bar
        }

        /// <summary>Standard 16 ANSI system text colors.</summary>
        public enum TextColor
        {
            /// <summary>Black (Color 0).</summary>
            Black,
            /// <summary>Red (Color 1).</summary>
            Red,
            /// <summary>Green (Color 2).</summary>
            Green,
            /// <summary>Yellow (Color 3).</summary>
            Yellow,
            /// <summary>Blue (Color 4).</summary>
            Blue,
            /// <summary>Magenta (Color 5).</summary>
            Magenta,
            /// <summary>Cyan (Color 6).</summary>
            Cyan,
            /// <summary>White (Color 7).</summary>
            White,
            /// <summary>Bright Black / Dark Gray (Color 8).</summary>
            BrightBlack,
            /// <summary>Bright Red (Color 9).</summary>
            BrightRed,
            /// <summary>Bright Green (Color 10).</summary>
            BrightGreen,
            /// <summary>Bright Yellow (Color 11).</summary>
            BrightYellow,
            /// <summary>Bright Blue (Color 12).</summary>
            BrightBlue,
            /// <summary>Bright Magenta (Color 13).</summary>
            BrightMagenta,
            /// <summary>Bright Cyan (Color 14).</summary>
            BrightCyan,
            /// <summary>Bright White (Color 15).</summary>
            BrightWhite,
        }

        /// <summary>Bitmask flags representing combined video rendition attributes.</summary>
        [Flags]
        public enum GraphicAttributeElements
        {
            /// <summary>No video attributes enabled.</summary>
            None = 0,
            /// <summary>Bold / high-intensity video.</summary>
            Bold = 1,
            /// <summary>Faint / decreased intensity / dim.</summary>
            Faint = 2,
            /// <summary>Italic font rendition.</summary>
            Italic = 4,
            /// <summary>Single underline.</summary>
            Underline_Single = 8,
            /// <summary>Double underline.</summary>
            Underline_Double = 16,
            /// <summary>Slow blink.</summary>
            Blink_Slow = 32,
            /// <summary>Rapid blink.</summary>
            Blink_Rapid = 64,
            /// <summary>Concealed text.</summary>
            Conceal = 128,
            /// <summary>Crossed-out / strikethrough text.</summary>
            Strikethrough = 256,
            /// <summary>Overlined text.</summary>
            Overline = 512,
        }

        #endregion
        #region Define Class Variables

        /// <summary>Enables strict runtime validation assertions.</summary>
        public static bool DoAsserts { get; set; }

        // Protected
        protected Point m_cursorPosition;
        protected Point m_savedCursorPosition;
        protected bool m_showCursor;

        // VT220 Scrolling Margins
        protected int m_topMargin;
        protected int m_bottomMargin;

        // VT220 Modes
        protected bool m_autoWrap = true;
        protected bool m_pendingWrap = false;
        protected bool m_insertMode = false;

        // VT220 Saved Cursor State (DECSC / DECRC)
        protected Point m_savedCursorPos = Point.Empty;
        protected GraphicAttributes m_savedAttrs;
        protected CharSetType m_savedG0 = CharSetType.US_ASCII;
        protected CharSetType m_savedG1 = CharSetType.LineDrawing;
        protected CharSetSlot m_savedActiveCharset = CharSetSlot.G0;
        protected bool m_savedPendingWrap = false;

        /// <summary>Designatable character set types under ISO 2022.</summary>
        public enum CharSetType
        {
            /// <summary>Standard US-ASCII character set.</summary>
            US_ASCII,
            /// <summary>United Kingdom national replacement character set (pound symbol replaces hash).</summary>
            UK,
            /// <summary>DEC Special Graphics / Alternate Character Set (line drawing characters).</summary>
            LineDrawing
        }

        /// <summary>Active ISO 2022 character set designation slot.</summary>
        public enum CharSetSlot
        {
            /// <summary>Primary G0 character set slot (invoked via SI, 0x0F).</summary>
            G0,
            /// <summary>Secondary G1 character set slot (invoked via SO, 0x0E).</summary>
            G1
        }

        protected CharSetType m_designatedG0 = CharSetType.US_ASCII;
        protected CharSetType m_designatedG1 = CharSetType.LineDrawing;
        protected CharSetSlot m_activeCharset = CharSetSlot.G0;

        // Manage Alternate Buffer
        protected bool screenBufferIsPrimary = true;
        protected Glyph[,] currentScreenBuffer = new Glyph[0, 0];
        protected Point savePrimaryCursor;
        protected Glyph[,] primaryScreenBuffer = new Glyph[0, 0];
        protected Glyph[,] alternateScreenBuffer = new Glyph[0, 0];
        protected Point saveAlternativeCursor;

        protected GraphicAttributes m_currentAttributes;

        // xterm Extensions
        protected char m_lastGraphicChar = '\0';
        protected GraphicAttributes m_lastGraphicAttrs;
        protected int m_leftMargin;
        protected int m_rightMargin;
        protected Stack<string> m_titleStack = new Stack<string>();
        protected string m_windowTitle = string.Empty;

        // Kitty Non-Bitmap Extensions
        /// <summary>Active cursor shape style (DECSCUSR).</summary>
        public CursorShape CursorStyle { get; set; } = CursorShape.Block;
        /// <summary>Whether the cursor is currently set to blink (DECSCUSR).</summary>
        public bool CursorBlink { get; set; } = false;
        /// <summary>Custom cursor color override (OSC 12 / OSC 112).</summary>
        public Color? CursorColor { get; set; } = null;
        /// <summary>Whether synchronized output (atomic frame updates) is active (Mode ?2026).</summary>
        protected bool m_synchronizedOutput = false;
        /// <summary>Interned table of explicit hyperlink URLs (OSC 8).</summary>
        protected readonly List<string> m_hyperlinkUrls = new();
        /// <summary>Kitty Graphics Protocol image store keyed by ImageId.</summary>
        public Dictionary<uint, KittyImage> StoredImages { get; } = new();

        /// <summary>Active on-screen Kitty Graphics Protocol image placements.</summary>
        public List<KittyImagePlacement> ImagePlacements { get; } = new();

        /// <summary>Configured cell pixel width for grid sizing and cropping (default 10).</summary>
        public int CellPixelWidth { get; set; } = 10;

        /// <summary>Configured cell pixel height for grid sizing and cropping (default 20).</summary>
        public int CellPixelHeight { get; set; } = 20;


        #endregion
        #region Saved Scrolling Buffer Management
        protected List<Glyph[]> UpperBuffer = [];
        protected List<Glyph[]> LowerBuffer = [];

        // Always will succeed even if we push stuff of top of lists
        private void pushOntoBuffer(Glyph[] line, bool lower)
        {
            var outLine = squashLine(line);

            if (lower) LowerBuffer.Add(outLine);
            else UpperBuffer.Add(outLine);

        }

        private Glyph[] squashLine(Glyph[] line)
        {
            return line;
            // var buf = new List<Glyph>();
            // return buf.ToArray();
        }
        #endregion
        #region Tab Handling
        private bool[] TabStops = new bool[1024];

        public void ClearTabStops()
        {
            for (var i = 0; i < TabStops.Length; i++)
                TabStops[i] = false;
        }

        public void SetStdTabStops()
        {
            for (var i = 1; i < 1024; i++)
                TabStops[i] = (i % 8) == 0;
        }

        public void SetTabStop(int column, bool value)
        {
            TabStops[column] = value;
        }

        public int NextTabStop(int column, int max)
        {
            for (int i = column + 1; i < max; i++)
                if (TabStops[i]) return i;

            return max - 1;  // No stop between here and the end
        }

        public void SetTab(IAnsiDecoder _sender)
        {
            SetTabStop(CursorPosition.X, true);
        }

        public void ClearTab(IAnsiDecoder _sender, bool ClearAll)
        {
            if (ClearAll)
                ClearTabStops();
            else
                SetTabStop(CursorPosition.X, false);
        }

        #endregion
        #region UI Actions Messages
        public static event Action<List<UIActions>>? OnUIAction;
        private static List<UIActions> UIActionQueue = [];
        private static object _actionLock = new();   // We're likely multithreaded here from the inputs coming in from sshshell
        private bool disposedValue;

        public abstract class UIActions
        {
            public enum ActionTypes
            {
                ClearScreen,
                SrollScreenUp,
                UpdateScreen,
                CursorMoved,
                SetProperty,
            }
            
            public ActionTypes Action { get; }

            public UIActions(ActionTypes _type)
            {
                Action = _type;
                lock (_actionLock)
                {
                    UIActionQueue.Add(this);


                }
            }

            public static bool SynchronizedOutputActive { get; set; } = false;

            public void Fire()
            {
                if (SynchronizedOutputActive)
                    return;

                List<UIActions>? queue = null;
                lock (_actionLock)
                {
                    queue = UIActionQueue;
                    UIActionQueue = [];
                }

                OnUIAction?.Invoke(queue);
                queue.Clear();
            }
        }

        public class UIAction_ClearScreen : UIActions
        {
            public UIAction_ClearScreen() : base(ActionTypes.ClearScreen)
            {
            }
        }

        public class UIAction_ScrollScreenUp : UIActions
        {
            public Glyph[] SaveRow { get; }

            public UIAction_ScrollScreenUp(Glyph[] saveRow) : base(ActionTypes.SrollScreenUp)
            {
                SaveRow = saveRow;
            }
        }

        public class UIAction_UpdateScreen : UIActions
        {
            public UIAction_UpdateScreen(int column, int row, Glyph glyph) : base(ActionTypes.UpdateScreen)
            {
                this.Column = column;
                this.Row = row;
                this.Glyph = glyph;
            }

            public int Column { get; }
            public int Row { get; }
            public Glyph Glyph { get; }
        }

        public class UIAction_SetProperty : UIActions
        {
            public PropertyTypes PropertyType { get; }
            public string PropertyValue { get; }

            public UIAction_SetProperty(PropertyTypes type, string value) : base(ActionTypes.SetProperty)
            {
                PropertyType = type;
                PropertyValue = value;
            }
        }

        public Glyph GetGlyph(int x, int y)
        {
            return this[x, y];
        }

        public Glyph[] GetGlyphRow(int x, int y, int length)
        {
            try
            {
                if (currentScreenBuffer is null) return Array.Empty<Glyph>();

                var len = length;
                if (length < 1 || len > Width - x)
                    length = Width - x;

                var ret = new Glyph[length];

                for (var i = 0; i < length; i++)
                    ret[i] = this.currentScreenBuffer[x + i, y];

                return ret;
            }
            catch { }

            return [];
        }

        public string GetGlyphString(int x, int y, int length=0)
        {
            try
            {
                if (currentScreenBuffer is null) return "";
                var sb = new StringBuilder();

                if (length < 1 || length > Width - x)
                    length = Width - x;
                              
                for (var i = 0; i < length; i++)
                {
                    var c = currentScreenBuffer[x + i, y];

                    // For result sets
                    if (c.Char == '±') c.Char = '•';
                    if (c.Char == '▒') c.Char = '•';
                    if (c.Char == '�') c.Char = '•';

                    if (c is not null)
                        sb.Append(c.Char);
                }

                return sb.ToString();
            }
            catch (Exception ex) {
                Console.WriteLine($"GetGlyphString({x},{y},{length}) - {ex.Message}");
            }

            return "";
        }

        public class UIAction_CursorMoved : UIActions
        {

            private static int oldColumn = -1;
            private static int oldRow = -1;

            private static bool debugMoves = false;

            public UIAction_CursorMoved(int column, int row, bool showCursor) : base(ActionTypes.CursorMoved)
            {
                this.Column = column;
                this.Row = row;
                this.ShowCursor = showCursor;
                                  
                if (debugMoves && 
                    (oldColumn != Column || oldRow != Row))
                    Console.WriteLine($"[Cursor Moved From [{oldColumn},{oldRow}] to [{Column},{Row}]");

                oldColumn = Column;
                oldRow = Row;
            }

        public int Column { get; }
            public int Row { get; }
            public bool ShowCursor { get; }
        }

        public void ActionFlush()
        {
            if (UIActionQueue.Count < 1) return;

            List<UIActions>? queue = null;
            lock (_actionLock)
            {
                queue = UIActionQueue;
                UIActionQueue = [];
            }

            OnUIAction?.Invoke(queue);
            queue.Clear();
        }

        #endregion
        #region GraphicsAttributes
        /// <summary>
        /// Represents the complete graphical rendition and color state of a terminal cell.
        /// </summary>
        public struct GraphicAttributes
        {
            /// <summary>Initializes a new default instance of <see cref="GraphicAttributes"/>.</summary>
            public GraphicAttributes()
            {
            }

            /// <summary>Bitmask elements of active rendition styles (bold, italic, underline, etc.).</summary>
            public GraphicAttributeElements Elements { get; private set; }

            /// <summary>Gets or sets bold / high-intensity rendition (SGR 1).</summary>
            public bool Bold
            {
                get => _getElement(GraphicAttributeElements.Bold);
                set => _setElement(GraphicAttributeElements.Bold, value);
            }

            /// <summary>Gets or sets faint / decreased intensity rendition (SGR 2).</summary>
            public bool Faint
            {
                get => _getElement(GraphicAttributeElements.Faint);
                set => _setElement(GraphicAttributeElements.Faint, value);
            }

            /// <summary>Gets or sets italic rendition (SGR 3).</summary>
            public bool Italic
            {
                get => _getElement(GraphicAttributeElements.Italic);
                set => _setElement(GraphicAttributeElements.Italic, value);
            }

            private Underline _underlineStyle;

            /// <summary>Gets or sets underline rendition (None, Single, Double, Curly, Dotted, Dashed).</summary>
            public Underline Underline
            {
                get => _underlineStyle;
                set
                {
                    _underlineStyle = value;
                    _setElement(GraphicAttributeElements.Underline_Single, value == Underline.Single);
                    _setElement(GraphicAttributeElements.Underline_Double, value == Underline.Double);
                }
            }

            /// <summary>Gets or sets optional distinct underline color (Kitty SGR 58 / 59).</summary>
            public Color? UnderlineColor { get; set; }

            /// <summary>Gets or sets active interned hyperlink ID (OSC 8, 0 = no link).</summary>
            public ushort HyperlinkId { get; set; }

            /// <summary>Gets or sets blinking text rendition (None, Slow, Rapid).</summary>
            public Blink Blink
            {
                get
                {
                    if ((Elements & GraphicAttributeElements.Blink_Slow) != 0)
                        return Blink.Slow;
                    if ((Elements & GraphicAttributeElements.Blink_Rapid) != 0)
                        return Blink.Rapid;
                    return Blink.None;
                }
                set
                {
                    _setElement(GraphicAttributeElements.Blink_Slow, false);
                    _setElement(GraphicAttributeElements.Blink_Rapid, false);
                    switch (value)
                    {
                        case Blink.Slow:
                            _setElement(GraphicAttributeElements.Blink_Slow, true);
                            break;
                        case Blink.Rapid:
                            _setElement(GraphicAttributeElements.Blink_Rapid, true);
                            break;
                    }
                }
            }

            /// <summary>Gets or sets conceal / hidden rendition (SGR 8).</summary>
            public bool Conceal
            {
                get => _getElement(GraphicAttributeElements.Conceal);
                set => _setElement(GraphicAttributeElements.Conceal, value);
            }

            /// <summary>Gets or sets crossed-out / strikethrough rendition (SGR 9).</summary>
            public bool Strikethrough
            {
                get => _getElement(GraphicAttributeElements.Strikethrough);
                set => _setElement(GraphicAttributeElements.Strikethrough, value);
            }

            /// <summary>Gets or sets overline text decoration (SGR 53).</summary>
            public bool Overline
            {
                get => _getElement(GraphicAttributeElements.Overline);
                set => _setElement(GraphicAttributeElements.Overline, value);
            }

            /// <summary>Gets or sets standard ANSI foreground color index.</summary>
            public TextColor Foreground { get; set; } = TextColor.White;

            /// <summary>Gets or sets standard ANSI background color index.</summary>
            public TextColor Background { get; set; } = TextColor.Black;

            /// <summary>Gets or sets optional 24-bit TrueColor or 256-color RGB foreground override.</summary>
            public Color? ForegroundRgb { get; set; }

            /// <summary>Gets or sets optional 24-bit TrueColor or 256-color RGB background override.</summary>
            public Color? BackgroundRgb { get; set; }

            /// <summary>Gets effective foreground color, honoring RGB overrides or ANSI fallbacks.</summary>
            public Color ForegroundColor
            {
                get => ForegroundRgb ?? TextColorToColor(Foreground);
            }

            /// <summary>Gets effective background color, honoring RGB overrides or ANSI fallbacks.</summary>
            public Color BackgroundColor
            {
                get => BackgroundRgb ?? TextColorToColor(Background);
            }

            public enum CHARSETs
            {
                G0,
                G1
            }

            public CHARSETs CHARSET { get; set; } = CHARSETs.G0;

            // We eventually have to get rid of the system.drawing elements and move them to the consumer (UI stuff)

            public Color TextColorToColor(TextColor _textColor)
            {
                switch (_textColor)
                {
                    case TextColor.Black:
                        return Color.Black;
                    case TextColor.Red:
                        return Color.DarkRed;
                    case TextColor.Green:
                        return Color.Green;
                    case TextColor.Yellow:
                        return Color.Yellow;
                    case TextColor.Blue:
                        return Color.Blue;
                    case TextColor.Magenta:
                        return Color.DarkMagenta;
                    case TextColor.Cyan:
                        return Color.Cyan;
                    case TextColor.White:
                        return Color.White;
                    case TextColor.BrightBlack:
                        return Color.Gray;
                    case TextColor.BrightRed:
                        return Color.Red;
                    case TextColor.BrightGreen:
                        return Color.LightGreen;
                    case TextColor.BrightYellow:
                        return Color.LightYellow;
                    case TextColor.BrightBlue:
                        return Color.LightBlue;
                    case TextColor.BrightMagenta:
                        return Color.DarkMagenta;
                    case TextColor.BrightCyan:
                        return Color.LightCyan;
                    case TextColor.BrightWhite:
                        return Color.Gray;
                }
                if (TerminalFrameBuffer.DoAsserts)
                    Console.Error.WriteLine($"[libvt100:WARN] Unknown color value: {_textColor}");
                return Color.Transparent;
            }

            private bool _getElement(GraphicAttributeElements type)
            {
                return (Elements & type) != 0;
            }

            private void _setElement(GraphicAttributeElements type, bool value)
            {
                if (value)
                    Elements |= type;
                else
                    Elements &= (~type);
            }

            public void Reset()
            {
                Elements = GraphicAttributeElements.None;

                Foreground = TextColor.White;
                Background = TextColor.Black;
                ForegroundRgb = null;
                BackgroundRgb = null;
                _underlineStyle = Underline.None;
                UnderlineColor = null;
                HyperlinkId = 0;

                CHARSET = CHARSETs.G0;
            }
        }
        #endregion
        #region Subclass Glyph (Cell entity)

        /// <summary>
        /// Represents an individual cell within the terminal display grid containing a character and video rendition attributes.
        /// </summary>
        public class Glyph
        {
            private char m_char;
            private GraphicAttributes m_graphicAttributes;

            /// <summary>
            /// Translates an ASCII character code (0x60..0x7E) to its DEC Special Graphics (ACS) Unicode Box Drawing equivalent.
            /// </summary>
            /// <param name="ch">The raw ASCII character.</param>
            /// <returns>The translated Unicode box drawing character.</returns>
            public static char TranslateAcs(char ch)
            {
                switch (ch)
                {
                    case '`': return '◆'; // U+25C6
                    case 'a': return '▒'; // U+2592
                    case 'b': return '␉'; // U+2409
                    case 'c': return '␌'; // U+240C
                    case 'd': return '␍'; // U+240D
                    case 'e': return '␊'; // U+240A
                    case 'f': return '°'; // U+00B0
                    case 'g': return '±'; // U+00B1
                    case 'h': return '␤'; // U+2424
                    case 'i': return '␋'; // U+240B
                    case 'j': return '┘'; // U+2518
                    case 'k': return '┐'; // U+2510
                    case 'l': return '┌'; // U+250C
                    case 'm': return '└'; // U+2514
                    case 'n': return '┼'; // U+253C
                    case 'o': return '⎺'; // U+23BA
                    case 'p': return '⎻'; // U+23BB
                    case 'q': return '─'; // U+2500
                    case 'r': return '⎼'; // U+23BC
                    case 's': return '⎽'; // U+23BD
                    case 't': return '├'; // U+251C
                    case 'u': return '┤'; // U+2524
                    case 'v': return '┴'; // U+2534
                    case 'w': return '┬'; // U+252C
                    case 'x': return '│'; // U+2502
                    case 'y': return '≤'; // U+2264
                    case 'z': return '≥'; // U+2265
                    case '{': return 'π'; // U+03C0
                    case '|': return '≠'; // U+2260
                    case '}': return '£'; // U+00A3
                    case '~': return '·'; // U+00B7
                    default: return ch;
                }
            }

            /// <summary>
            /// Gets or sets the character displayed by the cell, honoring active ACS line-drawing character sets.
            /// </summary>
            public char Char
            {
                get
                {
                    switch (m_graphicAttributes.CHARSET)
                    {
                        case CHARSETs.G0:
                            return m_char;
                        case CHARSETs.G1:
                            return TranslateAcs(m_char);
                    }
                    return m_char;
                }
                set
                {
                    m_char = value;
                }
            }

            /// <summary>
            /// Gets the raw stored character without character set translation.
            /// </summary>
            public char RawChar => m_char;

            /// <summary>
            /// Gets or sets the cell's video rendition attributes and colors.
            /// </summary>
            public GraphicAttributes Attributes
            {
                get => m_graphicAttributes;
                set => m_graphicAttributes = value;
            }

            /// <summary>Initializes a blank cell with a space character.</summary>
            public Glyph()
                : this(' ')
            {
            }

            /// <summary>Initializes a cell with the given character and default attributes.</summary>
            /// <param name="_char">Character value.</param>
            public Glyph(char _char)
            {
                m_char = _char;
                m_graphicAttributes = new GraphicAttributes();
                m_graphicAttributes.Reset();
            }

            /// <summary>Initializes a cell with the given character and custom video attributes.</summary>
            /// <param name="_char">Character value.</param>
            /// <param name="_attribs">Video rendition attributes.</param>
            public Glyph(char _char, GraphicAttributes _attribs)
            {
                m_char = _char;
                m_graphicAttributes = _attribs;
            }

            /// <summary>Swaps foreground and background colors (inverse video).</summary>
            public void Invert()
            {
                var back = m_graphicAttributes.Background;
                m_graphicAttributes.Background = m_graphicAttributes.Foreground;
                m_graphicAttributes.Foreground = back;

                var backRgb = m_graphicAttributes.BackgroundRgb;
                m_graphicAttributes.BackgroundRgb = m_graphicAttributes.ForegroundRgb;
                m_graphicAttributes.ForegroundRgb = backRgb;
            }
        }

        #endregion
        #region Misc Frame Parameters

        public Size Size
        {
            get
            {
                return new Size(Width, Height);
            }
            set
            {
                // We need a separate hard and soft reset - this is sort of a wierd constructor
                if (TabStops == null)
                {
                    TabStops = new bool[1024];
                    SetStdTabStops();
                }

                if (currentScreenBuffer == null || value.Width != Width || value.Height != Height)
                {
                    currentScreenBuffer = new Glyph[value.Width, value.Height];
                    for (int x = 0; x < value.Width; ++x)
                    {
                        for (int y = 0; y < value.Height; ++y)
                        {
                            currentScreenBuffer[x, y] = new Glyph();  // Don't let this do callbacks
                        }
                    }
                    m_topMargin = 0;
                    m_bottomMargin = value.Height - 1;
                    CursorPosition = new Point(0, 0);
                }
            }
        }

        public int Width
        {
            get
            {
                return currentScreenBuffer.GetLength(0);
            }
        }

        public int Height
        {
            get
            {
                return currentScreenBuffer.GetLength(1);
            }
        }

      

        public Point CursorPosition
        {
            get
            {


                return m_cursorPosition;
            }
            set
            {
                if (m_cursorPosition != value)
                {
                    if (!CheckColumnRow(value.X, value.Y)) return;

                    m_cursorPosition = value;

                    new UIAction_CursorMoved(value.X, value.Y, m_showCursor).Fire();
                }
            }
        }

        public Glyph this[int _column, int _row]
        {
            get
            {
              

                return currentScreenBuffer[_column, _row];
            }
            set
            {
                if (!CheckColumnRow(_column, _row)) return;

                currentScreenBuffer[_column, _row] = value;

                new UIAction_UpdateScreen(_column, _row, value).Fire();

            }
        }

        public Glyph this[Point _position]
        {
            get
            {
                return this[_position.X, _position.Y];
            }
            set
            {
                this[_position.X, _position.Y] = value;
            }
        }

        #endregion
        #region Constructor and Maintenance Functions

        public TerminalFrameBuffer(int _width, int _height)
        {
            SetStdTabStops();
            Size = new Size(_width, _height);
            m_showCursor = true;
            m_savedCursorPosition = Point.Empty;
            m_currentAttributes.Reset();
        }

        public void ReSize(int termWidth, int termHeight)
        {
            // For now we just start over, later we clip
            //  Someday I'd like to do really clever things with line wrapping

            SetStdTabStops();
            Size = new Size(termWidth, termHeight);
            m_showCursor = true;
            m_savedCursorPosition = Point.Empty;
            m_currentAttributes.Reset();
        }

        public void DoScreenClear()
        {
            var w = Width;
            var h = Height;

            currentScreenBuffer = new Glyph[w, h];
            for (int x = 0; x < w; ++x)
            {
                for (int y = 0; y < h; ++y)
                {
                    currentScreenBuffer[x, y] = new Glyph();
                }
            }

            m_showCursor = true;
            m_savedCursorPosition = Point.Empty;
            m_currentAttributes.Reset();

            new UIAction_ClearScreen().Fire();
        }

        public void DoRefresh(bool sendWhite)
        {
            // Go through the array and send everything back
            //  If !sendwhite, don't send spaces with black background

            for (int x = 0; x < Width; ++x)
            {
                for (int y = 0; y < Height; ++y)
                {
                    var c = this[x, y];
                    if (sendWhite)
                        new UIAction_UpdateScreen(x, y, c).Fire();
                    else
                    {
                        var white = (c.Char == ' ');
                        if (c.Attributes.Background != TextColor.Black)
                            white = false;
                        if (!white)
                            new UIAction_UpdateScreen(x, y, c).Fire();
                    }
                }
            }
        }

        public override String ToString()
        {
            StringBuilder builder = new StringBuilder();
            for (int y = 0; y < Height; ++y)
            {
                for (int x = 0; x < Width; ++x)
                {
                    if (this[x, y].Char > 127)
                    {
                        builder.Append('!');
                    }
                    else
                    {
                        builder.Append(this[x, y].Char);
                    }
                }
                builder.Append(Environment.NewLine);
            }
            return builder.ToString();
        }

        #endregion
        #region Basic Cursor Movement and Scrolling

        protected bool CheckColumnRow(int _column, int _row)
        {
            if (_column >= Width)
            {
                Console.WriteLine($"The column number ({_column}) is larger than the screen width ({Width})");
                return false;
            }
            if (_row >= Height)
            {
                Console.WriteLine($"The row number ({_row}) is larger than the screen height ({Height})");
                return false;
            }

            return true;
        }

        public void CursorForward()
        {
            if (m_cursorPosition.X + 1 < Width)
            {
                CursorPosition = new Point(m_cursorPosition.X + 1, m_cursorPosition.Y);
            }
        }

        public void CursorBackward()
        {
            if (m_cursorPosition.X > 0)
            {
                CursorPosition = new Point(m_cursorPosition.X - 1, m_cursorPosition.Y);
            }
        }

        public void ScrollScreen(int lines)
        {
            if (lines == 0) return;
            if (m_bottomMargin <= m_topMargin) return;

            if (lines > 0)
            {
                int count = Math.Min(lines, m_bottomMargin - m_topMargin + 1);
                var keepScroll = new Glyph[Width];
                for (var col = 0; col < Width; col++)
                    keepScroll[col] = currentScreenBuffer[col, m_topMargin];

                for (var row = m_topMargin + count; row <= m_bottomMargin; row++)
                    for (var col = 0; col < Width; col++)
                        currentScreenBuffer[col, row - count] = currentScreenBuffer[col, row];

                for (var row = m_bottomMargin - count + 1; row <= m_bottomMargin; row++)
                    for (var col = 0; col < Width; col++)
                        currentScreenBuffer[col, row] = new Glyph(' ', m_currentAttributes);

                ShiftImagePlacementsVertically(m_topMargin, m_bottomMargin, -count);
                new UIAction_ScrollScreenUp(keepScroll).Fire();
            }
            else
            {
                int count = Math.Min(-lines, m_bottomMargin - m_topMargin + 1);
                for (var row = m_bottomMargin - count; row >= m_topMargin; row--)
                    for (var col = 0; col < Width; col++)
                        currentScreenBuffer[col, row + count] = currentScreenBuffer[col, row];

                for (var row = m_topMargin; row < m_topMargin + count; row++)
                    for (var col = 0; col < Width; col++)
                        currentScreenBuffer[col, row] = new Glyph(' ', m_currentAttributes);

                ShiftImagePlacementsVertically(m_topMargin, m_bottomMargin, count);
            }
        }

        private void ShiftImagePlacementsVertically(int topRow, int bottomRow, int deltaRows)
        {
            if (ImagePlacements.Count == 0 || deltaRows == 0) return;

            for (int i = ImagePlacements.Count - 1; i >= 0; i--)
            {
                var p = ImagePlacements[i];
                if (p.AnchorRow >= topRow && p.AnchorRow <= bottomRow)
                {
                    p.AnchorRow += deltaRows;
                    if (p.AnchorRow + p.RowSpan <= 0 || p.AnchorRow >= Height)
                    {
                        ImagePlacements.RemoveAt(i);
                    }
                }
            }
        }

        public void CursorDown(bool scroll)
        {
            if (m_cursorPosition.Y >= m_bottomMargin)
            {
                if (!scroll) return;
                ScrollScreen(1);
                return;
            }

            if (m_cursorPosition.Y + 1 < Height)
                CursorPosition = new Point(m_cursorPosition.X, m_cursorPosition.Y + 1);
        }

        public void CursorUp(bool scroll)
        {
            if (m_cursorPosition.Y <= m_topMargin)
            {
                if (!scroll) return;
                ScrollScreen(-1);
                return;
            }

            if (m_cursorPosition.Y - 1 >= 0)
                CursorPosition = new Point(m_cursorPosition.X, m_cursorPosition.Y - 1);
        }
        public void InsertLine(int count)
        {
            if (count <= 0) return;
            if (m_cursorPosition.Y < m_topMargin || m_cursorPosition.Y > m_bottomMargin)
                return;

            int linesToShift = Math.Min(count, m_bottomMargin - m_cursorPosition.Y + 1);
            for (int r = m_bottomMargin - linesToShift; r >= m_cursorPosition.Y; r--)
                for (int c = 0; c < Width; c++)
                    currentScreenBuffer[c, r + linesToShift] = currentScreenBuffer[c, r];

            for (int r = m_cursorPosition.Y; r < m_cursorPosition.Y + linesToShift; r++)
                for (int c = 0; c < Width; c++)
                    currentScreenBuffer[c, r] = new Glyph(' ', m_currentAttributes);

            ShiftImagePlacementsVertically(m_cursorPosition.Y, m_bottomMargin, linesToShift);
        }

        public void DeleteLine(int count)
        {
            if (count <= 0) return;
            if (m_cursorPosition.Y < m_topMargin || m_cursorPosition.Y > m_bottomMargin)
                return;

            int linesToShift = Math.Min(count, m_bottomMargin - m_cursorPosition.Y + 1);
            for (int r = m_cursorPosition.Y + linesToShift; r <= m_bottomMargin; r++)
                for (int c = 0; c < Width; c++)
                    currentScreenBuffer[c, r - linesToShift] = currentScreenBuffer[c, r];

            for (int r = m_bottomMargin - linesToShift + 1; r <= m_bottomMargin; r++)
                for (int c = 0; c < Width; c++)
                    currentScreenBuffer[c, r] = new Glyph(' ', m_currentAttributes);

            ShiftImagePlacementsVertically(m_cursorPosition.Y, m_bottomMargin, -linesToShift);
        }

        public void InsertCharacter(int count)
        {
            if (count <= 0) return;
            int charsToShift = Math.Min(count, Width - m_cursorPosition.X);
            int r = m_cursorPosition.Y;
            for (int c = Width - 1 - charsToShift; c >= m_cursorPosition.X; c--)
                currentScreenBuffer[c + charsToShift, r] = currentScreenBuffer[c, r];

            for (int c = m_cursorPosition.X; c < m_cursorPosition.X + charsToShift; c++)
                currentScreenBuffer[c, r] = new Glyph(' ', m_currentAttributes);
        }

        public void DeleteCharacter(int count)
        {
            if (count <= 0) return;
            int charsToShift = Math.Min(count, Width - m_cursorPosition.X);
            int r = m_cursorPosition.Y;
            for (int c = m_cursorPosition.X + charsToShift; c < Width; c++)
                currentScreenBuffer[c - charsToShift, r] = currentScreenBuffer[c, r];

            for (int c = Width - charsToShift; c < Width; c++)
                currentScreenBuffer[c, r] = new Glyph(' ', m_currentAttributes);
        }

        public void SetScrollingRegion(int top, int bottom)
        {
            m_topMargin = Math.Max(0, Math.Min(top - 1, Height - 1));
            m_bottomMargin = bottom <= 0 ? Height - 1 : Math.Max(m_topMargin, Math.Min(bottom - 1, Height - 1));
            CursorPosition = new Point(0, 0);
        }

        public void ClearScreen(ClearDirection direction)
        {
            switch (direction)
            {
                case ClearDirection.Forward: // 0: cursor to end
                    for (int c = m_cursorPosition.X; c < Width; c++)
                        this[c, m_cursorPosition.Y] = new Glyph(' ', m_currentAttributes);
                    for (int r = m_cursorPosition.Y + 1; r < Height; r++)
                        for (int c = 0; c < Width; c++)
                            this[c, r] = new Glyph(' ', m_currentAttributes);
                    break;

                case ClearDirection.Backward: // 1: start to cursor
                    for (int r = 0; r < m_cursorPosition.Y; r++)
                        for (int c = 0; c < Width; c++)
                            this[c, r] = new Glyph(' ', m_currentAttributes);
                    for (int c = 0; c <= m_cursorPosition.X && c < Width; c++)
                        this[c, m_cursorPosition.Y] = new Glyph(' ', m_currentAttributes);
                    break;

                case ClearDirection.Both: // 2: entire screen
                    for (int r = 0; r < Height; r++)
                        for (int c = 0; c < Width; c++)
                            this[c, r] = new Glyph(' ', m_currentAttributes);
                    break;
            }
        }

        public void Reset(bool soft)
        {
            m_currentAttributes.Reset();
            m_topMargin = 0;
            m_bottomMargin = Height - 1;
            m_autoWrap = true;
            m_pendingWrap = false;
            m_insertMode = false;
            m_showCursor = true;
            m_designatedG0 = CharSetType.US_ASCII;
            m_designatedG1 = CharSetType.LineDrawing;
            m_activeCharset = CharSetSlot.G0;

            if (!soft)
            {
                ClearScreen(ClearDirection.Both);
                CursorPosition = new Point(0, 0);
                SetStdTabStops();
            }
        }

        public static Color Get256Color(int index)
        {
            if (index < 0) return Color.Black;
            if (index < 16)
            {
                switch (index)
                {
                    case 0: return Color.FromArgb(0, 0, 0);
                    case 1: return Color.FromArgb(128, 0, 0);
                    case 2: return Color.FromArgb(0, 128, 0);
                    case 3: return Color.FromArgb(128, 128, 0);
                    case 4: return Color.FromArgb(0, 0, 128);
                    case 5: return Color.FromArgb(128, 0, 128);
                    case 6: return Color.FromArgb(0, 128, 128);
                    case 7: return Color.FromArgb(192, 192, 192);
                    case 8: return Color.FromArgb(128, 128, 128);
                    case 9: return Color.FromArgb(255, 0, 0);
                    case 10: return Color.FromArgb(0, 255, 0);
                    case 11: return Color.FromArgb(255, 255, 0);
                    case 12: return Color.FromArgb(0, 0, 255);
                    case 13: return Color.FromArgb(255, 0, 255);
                    case 14: return Color.FromArgb(0, 255, 255);
                    case 15: return Color.FromArgb(255, 255, 255);
                }
            }
            if (index <= 231)
            {
                int val = index - 16;
                int b = (val % 6) == 0 ? 0 : 55 + (val % 6) * 40;
                val /= 6;
                int g = (val % 6) == 0 ? 0 : 55 + (val % 6) * 40;
                val /= 6;
                int r = (val % 6) == 0 ? 0 : 55 + (val % 6) * 40;
                return Color.FromArgb(r, g, b);
            }
            if (index <= 255)
            {
                int gray = 8 + (index - 232) * 10;
                return Color.FromArgb(gray, gray, gray);
            }
            return Color.White;
        }

        public void SetColor256(bool isForeground, int colorIndex)
        {
            Color c = Get256Color(colorIndex);
            if (isForeground)
                m_currentAttributes.ForegroundRgb = c;
            else
                m_currentAttributes.BackgroundRgb = c;
        }

        public void SetColorRgb(bool isForeground, Color color)
        {
            if (isForeground)
                m_currentAttributes.ForegroundRgb = color;
            else
                m_currentAttributes.BackgroundRgb = color;
        }

        public void ResetColor(bool isForeground)
        {
            if (isForeground)
            {
                m_currentAttributes.Foreground = TextColor.White;
                m_currentAttributes.ForegroundRgb = null;
            }
            else
            {
                m_currentAttributes.Background = TextColor.Black;
                m_currentAttributes.BackgroundRgb = null;
            }
        }

        public void RepeatCharacter(int count)
        {
            if (m_lastGraphicChar == '\0' || count <= 0) return;
            for (int i = 0; i < count; i++)
            {
                if (m_pendingWrap && m_autoWrap)
                {
                    m_pendingWrap = false;
                    CursorPosition = new Point(0, CursorPosition.Y);
                    CursorDown(true);
                }

                if (m_insertMode)
                {
                    InsertCharacter(1);
                }

                this[CursorPosition] = new Glyph(m_lastGraphicChar, m_lastGraphicAttrs);

                if (CursorPosition.X >= Width - 1)
                {
                    if (m_autoWrap)
                    {
                        m_pendingWrap = true;
                    }
                }
                else
                {
                    CursorForward();
                }
            }
        }

        public void MoveCursorToRow(int rowNumber)
        {
            m_pendingWrap = false;
            int targetRow = Math.Clamp(rowNumber, 0, Height - 1);
            CursorPosition = new Point(m_cursorPosition.X, targetRow);
        }

        public void MoveCursorBackTab(int count)
        {
            m_pendingWrap = false;
            while (count > 0 && m_cursorPosition.X > 0)
            {
                int prev = 0;
                for (int c = m_cursorPosition.X - 1; c >= 0; c--)
                {
                    if (TabStops != null && c < TabStops.Length && TabStops[c])
                    {
                        prev = c;
                        break;
                    }
                }
                CursorPosition = new Point(prev, m_cursorPosition.Y);
                count--;
            }
        }

        public void SetLeftRightMargins(int left, int right)
        {
            m_leftMargin = Math.Max(0, Math.Min(left - 1, Width - 1));
            m_rightMargin = right <= 0 ? Width - 1 : Math.Max(m_leftMargin, Math.Min(right - 1, Width - 1));
            CursorPosition = new Point(0, 0);
        }

        public void ClearSavedLines()
        {
            UpperBuffer.Clear();
            LowerBuffer.Clear();
        }

        public void PushTitle()
        {
            m_titleStack.Push(m_windowTitle);
        }

        public void PopTitle()
        {
            if (m_titleStack.Count > 0)
            {
                m_windowTitle = m_titleStack.Pop();
                new UIAction_SetProperty(PropertyTypes.WindowTitle, m_windowTitle).Fire();
            }
        }

        public void SetUnderlineStyle(Underline style)
        {
            m_currentAttributes.Underline = style;
        }

        public void SetUnderlineColor(Color? color)
        {
            m_currentAttributes.UnderlineColor = color;
        }

        public void SetCursorShape(CursorShape shape, bool blinking)
        {
            CursorStyle = shape;
            CursorBlink = blinking;
            new UIAction_CursorMoved(m_cursorPosition.X, m_cursorPosition.Y, m_showCursor).Fire();
        }

        public void SetCursorColor(Color? color)
        {
            CursorColor = color;
        }

        public ushort RegisterHyperlink(string url)
        {
            if (string.IsNullOrEmpty(url)) return 0;
            int idx = m_hyperlinkUrls.IndexOf(url);
            if (idx >= 0) return (ushort)(idx + 1);
            m_hyperlinkUrls.Add(url);
            return (ushort)m_hyperlinkUrls.Count;
        }

        public string? GetHyperlinkUrl(ushort id)
        {
            if (id == 0 || id > m_hyperlinkUrls.Count) return null;
            return m_hyperlinkUrls[id - 1];
        }

        public void SetHyperlink(string? url, string? id)
        {
            if (string.IsNullOrEmpty(url))
            {
                m_currentAttributes.HyperlinkId = 0;
            }
            else
            {
                m_currentAttributes.HyperlinkId = RegisterHyperlink(url);
            }
        }

        public void DesktopNotification(string notificationParams, string body)
        {
            // Dispatches notification metadata
        }

        public void ShellIntegration(string command, string? args)
        {
            // Dispatches shell integration semantic marker
        }

        public List<KittyImagePlacement> GetPlacementsBelowText()
        {
            var list = ImagePlacements.FindAll(p => p.ZIndex < 0);
            list.Sort((a, b) => a.ZIndex.CompareTo(b.ZIndex));
            return list;
        }

        public List<KittyImagePlacement> GetPlacementsBackground()
        {
            return ImagePlacements.FindAll(p => p.ZIndex == 0);
        }

        public List<KittyImagePlacement> GetPlacementsAboveText()
        {
            var list = ImagePlacements.FindAll(p => p.ZIndex > 0);
            list.Sort((a, b) => a.ZIndex.CompareTo(b.ZIndex));
            return list;
        }

        public void HandleKittyGraphicsCommand(KittyGraphicsCommand cmd)
        {
            if (cmd == null) return;

            switch (cmd.Action)
            {
                case 't': // Transmit only
                    if (cmd.Image != null)
                    {
                        uint id = cmd.ImageId != 0 ? cmd.ImageId : (uint)(StoredImages.Count + 1);
                        StoredImages[id] = cmd.Image;
                    }
                    break;

                case 'T': // Transmit and put
                    if (cmd.Image != null)
                    {
                        uint id = cmd.ImageId != 0 ? cmd.ImageId : (uint)(StoredImages.Count + 1);
                        StoredImages[id] = cmd.Image;
                        PlaceImage(id, cmd, cmd.Image);
                    }
                    break;

                case 'p': // Put existing image
                    if (cmd.ImageId != 0 && StoredImages.TryGetValue(cmd.ImageId, out var existingImg))
                    {
                        PlaceImage(cmd.ImageId, cmd, existingImg);
                    }
                    break;

                case 'd': // Delete
                    DeleteKittyImages(cmd);
                    break;
            }
        }

        private void PlaceImage(uint imgId, KittyGraphicsCommand cmd, KittyImage img)
        {
            int colSpan = cmd.ColSpan ?? Math.Max(1, (int)Math.Ceiling((double)img.Width / CellPixelWidth));
            int rowSpan = cmd.RowSpan ?? Math.Max(1, (int)Math.Ceiling((double)img.Height / CellPixelHeight));

            var placement = new KittyImagePlacement
            {
                ImageId = imgId,
                PlacementId = cmd.PlacementId,
                AnchorCol = m_cursorPosition.X,
                AnchorRow = m_cursorPosition.Y,
                ColSpan = colSpan,
                RowSpan = rowSpan,
                SrcX = cmd.SrcX,
                SrcY = cmd.SrcY,
                SrcWidth = cmd.SrcWidth > 0 ? cmd.SrcWidth : img.Width,
                SrcHeight = cmd.SrcHeight > 0 ? cmd.SrcHeight : img.Height,
                SubCellX = cmd.SubCellX,
                SubCellY = cmd.SubCellY,
                ZIndex = cmd.ZIndex
            };

            ImagePlacements.Add(placement);

            if (cmd.CursorPolicy != 0)
            {
                CursorPosition = new Point(m_cursorPosition.X, Math.Min(Height - 1, m_cursorPosition.Y + rowSpan));
            }
        }

        private void DeleteKittyImages(KittyGraphicsCommand cmd)
        {
            char target = cmd.DeleteTarget != '\0' ? cmd.DeleteTarget : 'a';

            switch (target)
            {
                case 'a':
                case 'A':
                    ImagePlacements.Clear();
                    StoredImages.Clear();
                    break;

                case 'i':
                case 'I':
                    if (cmd.ImageId != 0)
                    {
                        ImagePlacements.RemoveAll(p => p.ImageId == cmd.ImageId);
                        StoredImages.Remove(cmd.ImageId);
                    }
                    break;

                case 'p':
                case 'P':
                    if (cmd.PlacementId != 0)
                    {
                        ImagePlacements.RemoveAll(p => p.PlacementId == cmd.PlacementId);
                    }
                    break;

                case 'c':
                case 'C':
                    ImagePlacements.RemoveAll(p => p.IntersectsCell(m_cursorPosition.X, m_cursorPosition.Y));
                    break;

                case 'z':
                case 'Z':
                    ImagePlacements.RemoveAll(p => p.ZIndex == cmd.ZIndex);
                    break;

                case 'x':
                case 'X':
                    ImagePlacements.RemoveAll(p => p.IntersectsColumn(cmd.SrcX));
                    break;

                case 'y':
                case 'Y':
                    ImagePlacements.RemoveAll(p => p.IntersectsRow(cmd.SrcY));
                    break;
            }
        }


        #endregion
        #region Buffer Enumerators
        IEnumerator<TerminalFrameBuffer.Glyph> IEnumerable<TerminalFrameBuffer.Glyph>.GetEnumerator()
        {
            for (int y = 0; y < Height; ++y)
            {
                for (int x = 0; x < Width; ++x)
                {
                    yield return this[x, y];
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return (this as IEnumerable<TerminalFrameBuffer.Glyph>).GetEnumerator();
        }
        #endregion
        #region Implement IAnsiDecoderClient

      

        void IAnsiDecoderClient.Characters(IAnsiDecoder _sender, char[] _chars)
        {
            foreach (char ch in _chars)
            {
                if (ch >= 32)
                {
                    _sender.deb(ch);

                    if (m_pendingWrap && m_autoWrap)
                    {
                        m_pendingWrap = false;
                        CursorPosition = new Point(0, CursorPosition.Y);
                        CursorDown(true);
                    }

                    char displayChar = ch;
                    var activeType = m_activeCharset == CharSetSlot.G0 ? m_designatedG0 : m_designatedG1;
                    if (activeType == CharSetType.LineDrawing)
                    {
                        displayChar = Glyph.TranslateAcs(ch);
                    }

                    if (m_insertMode)
                    {
                        InsertCharacter(1);
                    }

                    this[CursorPosition] = new Glyph(displayChar, m_currentAttributes);
                    m_lastGraphicChar = displayChar;
                    m_lastGraphicAttrs = m_currentAttributes;

                    if (CursorPosition.X >= Width - 1)
                    {
                        if (m_autoWrap)
                        {
                            m_pendingWrap = true;
                        }
                    }
                    else
                    {
                        CursorForward();
                    }
                    continue;
                }

                _sender.deb($"[0x{(int)ch:x}]");

                switch (ch)
                {
                    case '\n':      // Linefeed
                        m_pendingWrap = false;
                        _sender.deb("[LF]");
                        CursorDown(true);
                        break;

                    case '\r':      // Return
                        m_pendingWrap = false;
                        _sender.deb("[RET]\n");
                        CursorPosition = new Point(0, CursorPosition.Y);
                        break;

                    case '\x08':    // Backspace
                        m_pendingWrap = false;
                        _sender.deb("[BS]");
                        CursorBackward();
                        break;

                    case '\t':      // Tab
                        m_pendingWrap = false;
                        int nextT = NextTabStop(CursorPosition.X, Width);
                        CursorPosition = new Point(nextT, CursorPosition.Y);
                        break;

                    case '\x07':    // BEL
                        _sender.deb("[BEL]");
                        break;

                    case '\x0e':     // SO (Shift Out -> G1)
                        _sender.deb("[SO]");
                        m_activeCharset = CharSetSlot.G1;
                        break;

                    case '\x0f':     // SI (Shift In -> G0)
                        _sender.deb("[SI]");
                        m_activeCharset = CharSetSlot.G0;
                        break;
                }
            }
        }

        void IAnsiDecoderClient.SaveCursor(IAnsiDecoder _sender)
        {
            m_savedCursorPosition = m_cursorPosition;
            m_savedAttrs = m_currentAttributes;
            m_savedG0 = m_designatedG0;
            m_savedG1 = m_designatedG1;
            m_savedActiveCharset = m_activeCharset;
            m_savedPendingWrap = m_pendingWrap;
        }

        void IAnsiDecoderClient.RestoreCursor(IAnsiDecoder _sender)
        {
            CursorPosition = m_savedCursorPosition;
            m_currentAttributes = m_savedAttrs;
            m_designatedG0 = m_savedG0;
            m_designatedG1 = m_savedG1;
            m_activeCharset = m_savedActiveCharset;
            m_pendingWrap = m_savedPendingWrap;
        }

        Size IAnsiDecoderClient.GetSize(IAnsiDecoder _sender)
        {
            return Size;
        }

        void IAnsiDecoderClient.MoveCursor(IAnsiDecoder _sender, Direction _direction, int _amount, bool scroll)
        {
            switch (_direction)
            {
                case Direction.Up:
                    while (_amount > 0)
                    {
                        CursorUp(scroll);
                        _amount--;
                    }
                    break;

                case Direction.Down:
                    while (_amount > 0)
                    {
                        CursorDown(scroll);
                        _amount--;
                    }
                    break;

                case Direction.Forward:
                    while (_amount > 0)
                    {
                        CursorForward();
                        _amount--;
                    }
                    break;

                case Direction.Backward:
                    while (_amount > 0)
                    {
                        CursorBackward();
                        _amount--;
                    }
                    break;
            }
        }

        void IAnsiDecoderClient.MoveCursorToBeginningOfLineBelow(IAnsiDecoder _sender, int _lineNumberRelativeToCurrentLine, bool scroll)
        {
            m_pendingWrap = false;
            m_cursorPosition.X = 0;
            while (_lineNumberRelativeToCurrentLine > 0)
            {
                CursorDown(scroll);
                _lineNumberRelativeToCurrentLine--;
            }
        }

        void IAnsiDecoderClient.MoveCursorToBeginningOfLineAbove(IAnsiDecoder _sender, int _lineNumberRelativeToCurrentLine, bool scroll)
        {
            m_pendingWrap = false;
            m_cursorPosition.X = 0;
            while (_lineNumberRelativeToCurrentLine > 0)
            {
                CursorUp(scroll);
                _lineNumberRelativeToCurrentLine--;
            }
        }

        void IAnsiDecoderClient.MoveCursorToColumn(IAnsiDecoder _sender, int _columnNumber)
        {
            m_pendingWrap = false;
            if (!CheckColumnRow(_columnNumber, m_cursorPosition.Y)) return;

            CursorPosition = new Point(_columnNumber, m_cursorPosition.Y);
        }

        void IAnsiDecoderClient.MoveCursorTo(IAnsiDecoder _sender, Point _position)
        {
            m_pendingWrap = false;
            if (!CheckColumnRow(_position.X, _position.Y)) return;

            CursorPosition = _position;
        }

        void IAnsiDecoderClient.ClearScreen(IAnsiDecoder _sender, ClearDirection _direction)
        {
            ClearScreen(_direction);
        }

        void IAnsiDecoderClient.ClearLine(IAnsiDecoder _sender, ClearDirection _direction)
        {
            switch (_direction)
            {
                case ClearDirection.Forward:
                    for (int x = m_cursorPosition.X; x < Width; ++x)
                    {
                        this[x, m_cursorPosition.Y] = new Glyph(' ', m_currentAttributes);
                    }
                    break;

                case ClearDirection.Backward:
                    for (int x = m_cursorPosition.X; x >= 0; --x)
                    {
                        this[x, m_cursorPosition.Y] = new Glyph(' ', m_currentAttributes);
                    }
                    break;

                case ClearDirection.Both:
                    for (int x = 0; x < Width; ++x)
                    {
                        this[x, m_cursorPosition.Y] = new Glyph(' ', m_currentAttributes);
                    }
                    break;
            }
        }

        void IAnsiDecoderClient.ClearNext(AnsiDecoder ansiDecoder, int numChars)
        {
            for (int x = 0; x < numChars; ++x)
            {
                var sx = x + m_cursorPosition.X;
                if (sx < Width)
                    this[sx, m_cursorPosition.Y] = new Glyph(' ', this[sx, m_cursorPosition.Y].Attributes);
            }
        }

        void IAnsiDecoderClient.ScrollPageUpwards(IAnsiDecoder _sender, int _linesToScroll)
        {
            _sender.deb($"[ScrollUp:{_linesToScroll}]");
            ScrollScreen(_linesToScroll);
        }

        void IAnsiDecoderClient.ScrollPageDownwards(IAnsiDecoder _sender, int _linesToScroll)
        {
            _sender.deb($"[ScrollDn:{_linesToScroll}]");
            ScrollScreen(-_linesToScroll);
        }

        void IAnsiDecoderClient.SetScrollingRegion(IAnsiDecoder _sender, int _topRow, int _bottomRow)
        {
            SetScrollingRegion(_topRow, _bottomRow);
        }

        void IAnsiDecoderClient.InsertLine(IAnsiDecoder _sender, int _lineCount)
        {
            InsertLine(_lineCount);
        }

        void IAnsiDecoderClient.DeleteLine(IAnsiDecoder _sender, int _lineCount)
        {
            DeleteLine(_lineCount);
        }

        void IAnsiDecoderClient.InsertCharacter(IAnsiDecoder _sender, int _charCount)
        {
            InsertCharacter(_charCount);
        }

        void IAnsiDecoderClient.DeleteCharacter(IAnsiDecoder _sender, int _charCount)
        {
            DeleteCharacter(_charCount);
        }

        void IAnsiDecoderClient.Reset(IAnsiDecoder _sender, bool _soft)
        {
            Reset(_soft);
        }

        void IAnsiDecoderClient.ModeChanged(IAnsiDecoder _sender, AnsiMode _mode)
        {
            switch (_mode)
            {
                case AnsiMode.InsertMode:
                    m_insertMode = true;
                    break;
                case AnsiMode.ReplaceMode:
                    m_insertMode = false;
                    break;
                case AnsiMode.LineWrap:
                    m_autoWrap = true;
                    break;
                case AnsiMode.DisableLineWrap:
                    m_autoWrap = false;
                    m_pendingWrap = false;
                    break;

                case AnsiMode.SwitchG0toVT100_US:
                    m_designatedG0 = CharSetType.US_ASCII;
                    break;
                case AnsiMode.SwitchG0toVT100_UK:
                    m_designatedG0 = CharSetType.UK;
                    break;
                case AnsiMode.SwitchG0toVT100_LineDrawing:
                    m_designatedG0 = CharSetType.LineDrawing;
                    break;
                case AnsiMode.SwitchG1toVT100_US:
                    m_designatedG1 = CharSetType.US_ASCII;
                    break;
                case AnsiMode.SwitchG1toVT100_UK:
                    m_designatedG1 = CharSetType.UK;
                    break;
                case AnsiMode.SwitchG1toVT100_LineDrawing:
                    m_designatedG1 = CharSetType.LineDrawing;
                    break;

                case AnsiMode.HideCursor:
                    m_showCursor = false;
                    new UIAction_CursorMoved(m_cursorPosition.X, m_cursorPosition.Y, m_showCursor).Fire();
                    break;

                case AnsiMode.ShowCursor:
                    m_showCursor = true;
                    new UIAction_CursorMoved(m_cursorPosition.X, m_cursorPosition.Y, m_showCursor).Fire();
                    break;
                case AnsiMode.Strikethrough:
                    m_currentAttributes.Strikethrough = true;
                    break;
                case AnsiMode.NoStrikethrough:
                    m_currentAttributes.Strikethrough = false;
                    break;
                case AnsiMode.Overline:
                    m_currentAttributes.Overline = true;
                    break;
                case AnsiMode.NoOverline:
                    m_currentAttributes.Overline = false;
                    break;
                case AnsiMode.LeftRightMarginMode:
                    break;
                case AnsiMode.DisableLeftRightMarginMode:
                    m_leftMargin = 0;
                    m_rightMargin = Width - 1;
                    break;
                case AnsiMode.SynchronizedOutput:
                    m_synchronizedOutput = true;
                    UIActions.SynchronizedOutputActive = true;
                    break;
                case AnsiMode.DisableSynchronizedOutput:
                    m_synchronizedOutput = false;
                    UIActions.SynchronizedOutputActive = false;
                    ActionFlush();
                    break;
                case AnsiMode.SwitchToMainBuffer:
                    if (screenBufferIsPrimary) return;
                    currentScreenBuffer = primaryScreenBuffer ?? new Glyph[Width, Height];
                    screenBufferIsPrimary = true;
                    CursorPosition = savePrimaryCursor;
                    DoRefresh(true);
                    break;
                case AnsiMode.SwitchToAlternateBuffer:
                    if (!screenBufferIsPrimary) return;
                    primaryScreenBuffer = currentScreenBuffer;
                    savePrimaryCursor = m_cursorPosition;
                    if (alternateScreenBuffer == null || alternateScreenBuffer.GetLength(0) != Width || alternateScreenBuffer.GetLength(1) != Height)
                    {
                        alternateScreenBuffer = new Glyph[Width, Height];
                    }
                    for (int x = 0; x < Width; x++)
                        for (int y = 0; y < Height; y++)
                            alternateScreenBuffer[x, y] = new Glyph(' ', m_currentAttributes);
                    currentScreenBuffer = alternateScreenBuffer;
                    screenBufferIsPrimary = false;
                    CursorPosition = new Point(0, 0);
                    DoRefresh(true);
                    break;
            }
        }

        Point IAnsiDecoderClient.GetCursorPosition(IAnsiDecoder _sender)
        {
            return new Point(m_cursorPosition.X + 1, m_cursorPosition.Y + 1);
        }

        void IAnsiDecoderClient.SetProperty(IAnsiDecoder _sender, PropertyTypes type, string value)
        {
            new UIAction_SetProperty(type, value).Fire();
        }

        void IAnsiDecoderClient.SetGraphicRendition(IAnsiDecoder _sender, GraphicRendition[] _commands)
        {
            foreach (GraphicRendition command in _commands)
            {
                switch (command)
                {
                    case GraphicRendition.Reset:
                        m_currentAttributes.Reset();
                        break;
                    case GraphicRendition.Bold:
                        m_currentAttributes.Bold = true;
                        break;
                    case GraphicRendition.Faint:
                        m_currentAttributes.Faint = true;
                        break;
                    case GraphicRendition.Italic:
                        m_currentAttributes.Italic = true;
                        break;
                    case GraphicRendition.Underline:
                        m_currentAttributes.Underline = Underline.Single;
                        break;
                    case GraphicRendition.BlinkSlow:
                        m_currentAttributes.Blink = Blink.Slow;
                        break;
                    case GraphicRendition.BlinkRapid:
                        m_currentAttributes.Blink = Blink.Rapid;
                        break;
                    case GraphicRendition.Positive:
                    case GraphicRendition.Inverse:
                        TextColor tmp = m_currentAttributes.Foreground;
                        m_currentAttributes.Foreground = m_currentAttributes.Background;
                        m_currentAttributes.Background = tmp;

                        break;
                    case GraphicRendition.Conceal:
                        m_currentAttributes.Conceal = true;
                        break;
                    case GraphicRendition.UnderlineDouble:
                        m_currentAttributes.Underline = Underline.Double;
                        break;
                    case GraphicRendition.NormalIntensity:
                        m_currentAttributes.Bold = false;
                        m_currentAttributes.Faint = false;
                        break;
                    case GraphicRendition.NoUnderline:
                        m_currentAttributes.Underline = Underline.None;
                        break;
                    case GraphicRendition.NoBlink:
                        m_currentAttributes.Blink = Blink.None;
                        break;
                    case GraphicRendition.Reveal:
                        m_currentAttributes.Conceal = false;
                        break;
                    //case GraphicRendition.Faint:
                    //var fg = m_currentAttributes.Foreground;
                    //break;
                    case GraphicRendition.ForegroundNormalBlack:
                        m_currentAttributes.Foreground = TextColor.Black;
                        break;
                    case GraphicRendition.ForegroundNormalRed:
                        m_currentAttributes.Foreground = TextColor.Red;
                        break;
                    case GraphicRendition.ForegroundNormalGreen:
                        m_currentAttributes.Foreground = TextColor.Green;
                        break;
                    case GraphicRendition.ForegroundNormalYellow:
                        m_currentAttributes.Foreground = TextColor.Yellow;
                        break;
                    case GraphicRendition.ForegroundNormalBlue:
                        m_currentAttributes.Foreground = TextColor.Blue;
                        break;
                    case GraphicRendition.ForegroundNormalMagenta:
                        m_currentAttributes.Foreground = TextColor.Magenta;
                        break;
                    case GraphicRendition.ForegroundNormalCyan:
                        m_currentAttributes.Foreground = TextColor.Cyan;
                        break;
                    case GraphicRendition.ForegroundNormalWhite:
                        m_currentAttributes.Foreground = TextColor.White;
                        break;
                    case GraphicRendition.ForegroundNormalReset:
                        m_currentAttributes.Foreground = TextColor.White;
                        break;

                    case GraphicRendition.BackgroundNormalBlack:
                        m_currentAttributes.Background = TextColor.Black;
                        break;
                    case GraphicRendition.BackgroundNormalRed:
                        m_currentAttributes.Background = TextColor.Red;
                        break;
                    case GraphicRendition.BackgroundNormalGreen:
                        m_currentAttributes.Background = TextColor.Green;
                        break;
                    case GraphicRendition.BackgroundNormalYellow:
                        m_currentAttributes.Background = TextColor.Yellow;
                        break;
                    case GraphicRendition.BackgroundNormalBlue:
                        m_currentAttributes.Background = TextColor.Blue;
                        break;
                    case GraphicRendition.BackgroundNormalMagenta:
                        m_currentAttributes.Background = TextColor.Magenta;
                        break;
                    case GraphicRendition.BackgroundNormalCyan:
                        m_currentAttributes.Background = TextColor.Cyan;
                        break;
                    case GraphicRendition.BackgroundNormalWhite:
                        m_currentAttributes.Background = TextColor.White;
                        break;
                    case GraphicRendition.BackgroundNormalReset:
                        m_currentAttributes.Background = TextColor.Black;
                        break;

                    case GraphicRendition.ForegroundBrightBlack:
                        m_currentAttributes.Foreground = TextColor.BrightBlack;
                        break;
                    case GraphicRendition.ForegroundBrightRed:
                        m_currentAttributes.Foreground = TextColor.BrightRed;
                        break;
                    case GraphicRendition.ForegroundBrightGreen:
                        m_currentAttributes.Foreground = TextColor.BrightGreen;
                        break;
                    case GraphicRendition.ForegroundBrightYellow:
                        m_currentAttributes.Foreground = TextColor.BrightYellow;
                        break;
                    case GraphicRendition.ForegroundBrightBlue:
                        m_currentAttributes.Foreground = TextColor.BrightBlue;
                        break;
                    case GraphicRendition.ForegroundBrightMagenta:
                        m_currentAttributes.Foreground = TextColor.BrightMagenta;
                        break;
                    case GraphicRendition.ForegroundBrightCyan:
                        m_currentAttributes.Foreground = TextColor.BrightCyan;
                        break;
                    case GraphicRendition.ForegroundBrightWhite:
                        m_currentAttributes.Foreground = TextColor.BrightWhite;
                        break;
                    case GraphicRendition.ForegroundBrightReset:
                        m_currentAttributes.Foreground = TextColor.White;
                        break;

                    case GraphicRendition.BackgroundBrightBlack:
                        m_currentAttributes.Background = TextColor.BrightBlack;
                        break;
                    case GraphicRendition.BackgroundBrightRed:
                        m_currentAttributes.Background = TextColor.BrightRed;
                        break;
                    case GraphicRendition.BackgroundBrightGreen:
                        m_currentAttributes.Background = TextColor.BrightGreen;
                        break;
                    case GraphicRendition.BackgroundBrightYellow:
                        m_currentAttributes.Background = TextColor.BrightYellow;
                        break;
                    case GraphicRendition.BackgroundBrightBlue:
                        m_currentAttributes.Background = TextColor.BrightBlue;
                        break;
                    case GraphicRendition.BackgroundBrightMagenta:
                        m_currentAttributes.Background = TextColor.BrightMagenta;
                        break;
                    case GraphicRendition.BackgroundBrightCyan:
                        m_currentAttributes.Background = TextColor.BrightCyan;
                        break;
                    case GraphicRendition.BackgroundBrightWhite:
                        m_currentAttributes.Background = TextColor.BrightWhite;
                        break;
                    case GraphicRendition.BackgroundBrightReset:
                        m_currentAttributes.Background = TextColor.Black;
                        break;

                    case GraphicRendition.Font1:
                        break;

                    default:
                        if (TerminalFrameBuffer.DoAsserts)
                            Console.Error.WriteLine($"[libvt100:WARN] Unknown rendition command: {command}");
                        break;
                }
            }
        }
        #endregion
        void IAnsiDecoderClient.MoveCursorToRow(IAnsiDecoder _sender, int _rowNumber) => MoveCursorToRow(_rowNumber);
        void IAnsiDecoderClient.MoveCursorBackTab(IAnsiDecoder _sender, int _tabCount) => MoveCursorBackTab(_tabCount);
        void IAnsiDecoderClient.RepeatCharacter(IAnsiDecoder _sender, int _count) => RepeatCharacter(_count);
        void IAnsiDecoderClient.SetLeftRightMargins(IAnsiDecoder _sender, int _leftColumn, int _rightColumn) => SetLeftRightMargins(_leftColumn, _rightColumn);
        void IAnsiDecoderClient.ClearSavedLines(IAnsiDecoder _sender) => ClearSavedLines();
        void IAnsiDecoderClient.SetColor256(IAnsiDecoder _sender, bool _isForeground, int _colorIndex) => SetColor256(_isForeground, _colorIndex);
        void IAnsiDecoderClient.SetColorRgb(IAnsiDecoder _sender, bool _isForeground, Color _color) => SetColorRgb(_isForeground, _color);
        void IAnsiDecoderClient.ResetColor(IAnsiDecoder _sender, bool _isForeground) => ResetColor(_isForeground);
        void IAnsiDecoderClient.PushTitle(IAnsiDecoder _sender) => PushTitle();
        void IAnsiDecoderClient.PopTitle(IAnsiDecoder _sender) => PopTitle();
        public void LockMemory(bool lockAbove)
        {
            if (lockAbove)
            {
                m_topMargin = Math.Clamp(m_cursorPosition.Y, 0, Height - 1);
            }
            else
            {
                m_topMargin = 0;
            }
        }

        void IAnsiDecoderClient.LockMemory(IAnsiDecoder _sender, bool _lockAbove) => LockMemory(_lockAbove);
        void IAnsiDecoderClient.SetUnderlineStyle(IAnsiDecoder _sender, Underline _style) => SetUnderlineStyle(_style);
        void IAnsiDecoderClient.SetUnderlineColor(IAnsiDecoder _sender, Color? _color) => SetUnderlineColor(_color);
        void IAnsiDecoderClient.SetCursorShape(IAnsiDecoder _sender, CursorShape _shape, bool _blinking) => SetCursorShape(_shape, _blinking);
        void IAnsiDecoderClient.SetCursorColor(IAnsiDecoder _sender, Color? _color) => SetCursorColor(_color);
        void IAnsiDecoderClient.SetHyperlink(IAnsiDecoder _sender, string? _url, string? _id) => SetHyperlink(_url, _id);
        void IAnsiDecoderClient.DesktopNotification(IAnsiDecoder _sender, string _notificationParams, string _body) => DesktopNotification(_notificationParams, _body);
        void IAnsiDecoderClient.ShellIntegration(IAnsiDecoder _sender, string _command, string? _args) => ShellIntegration(_command, _args);
        void IAnsiDecoderClient.KittyGraphicsCommand(IAnsiDecoder _sender, KittyGraphicsCommand _command) => HandleKittyGraphicsCommand(_command);





        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    OnUIAction = null;  // Clear events -- these leak easily
                    currentScreenBuffer = new Glyph[0, 0];
                    primaryScreenBuffer = new Glyph[0, 0];
                    alternateScreenBuffer = new Glyph[0, 0];
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~TerminalFrameBuffer()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
