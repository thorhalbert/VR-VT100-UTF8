using System;
using System.Drawing;
using System.Text;
using System.Collections;
using System.Collections.Generic;

namespace libVT100
{
    public class TerminalFrameBuffer : IAnsiDecoderClient, IEnumerable<TerminalFrameBuffer.Glyph>
    {
        #region Define Enums

        public enum Blink
        {
            None = 0,
            Slow = 1,
            Rapid = 2,
        }

        public enum Underline
        {
            None = 0,
            Single = 1,
            Double = 2,
        }

        public enum TextColor
        {
            Black,
            Red,
            Green,
            Yellow,
            Blue,
            Magenta,
            Cyan,
            White,
            BrightBlack,
            BrightRed,
            BrightGreen,
            BrightYellow,
            BrightBlue,
            BrightMagenta,
            BrightCyan,
            BrightWhite,
        }

        [Flags]
        public enum GraphicAttributeElements
        {
            None = 0,
            Bold = 1,
            Faint = 2,
            Italic = 4,
            Underline_Single = 8,
            Underline_Double = 16,
            Blink_Slow = 32,
            Blink_Rapid = 64,
            Conceal = 128,
        }

        #endregion
        #region Define Class Variables

        // Settings/Debug
        public static bool DoAsserts { get; set; }

        // Protected
        protected Point m_cursorPosition;
        protected Point m_savedCursorPosition;
        protected bool m_showCursor;

        // Manage Alternate Buffer
        protected bool screenBufferIsPrimary = true;
        protected Glyph[,] currentScreenBuffer;
        protected Point savePrimaryCursor;
        protected Glyph[,] primaryScreenBuffer;
        protected Glyph[,] alternateScreenBuffer;
        protected Point saveAlternativeCursor;

        protected GraphicAttributes m_currentAttributes;

        #endregion
        #region Saved Scrolling Buffer Management
        protected List<Glyph[]> UpperBuffer = new List<Glyph[]>();
        protected List<Glyph[]> LowerBuffer = new List<Glyph[]>();

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
        private bool[] TabStops = null;

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
                SetTabStop(CursorPosition.Y, false);
        }

        #endregion
        #region UI Actions Messages
        public event Action<List<UIActions>> OnUIAction;
        private static List<UIActions> UIActionQueue = new List<UIActions>();
        private static object _actionLock = new object();   // We're likely multithreaded here from the inputs coming in from sshshell

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

        public class UIAction_CursorMoved : UIActions
        {
            public UIAction_CursorMoved(int column, int row, bool showCursor) : base(ActionTypes.CursorMoved)
            {
                this.Column = column;
                this.Row = row;
                this.ShowCursor = showCursor;
            }

            public int Column { get; }
            public int Row { get; }
            public bool ShowCursor { get; }
        }

        public void ActionFlush()
        {
            if (UIActionQueue.Count < 1) return;

            List<UIActions> queue = null;
            lock (_actionLock)
            {
                queue = UIActionQueue;
                UIActionQueue = new List<UIActions>();
            }
            OnUIAction?.Invoke(queue);
            queue.Clear();
        }

        #endregion
        #region GraphicsAttributes
        public struct GraphicAttributes
        {
            public GraphicAttributeElements Elements { get; private set; }

            public bool Bold
            {
                get
                {
                    return _getElement(GraphicAttributeElements.Bold);
                }
                set
                {
                    _setElement(GraphicAttributeElements.Bold, value);
                }
            }

            public bool Faint
            {
                get
                {
                    return _getElement(GraphicAttributeElements.Faint);
                }
                set
                {
                    _setElement(GraphicAttributeElements.Faint, value);
                }
            }

            public bool Italic
            {
                get
                {
                    return _getElement(GraphicAttributeElements.Italic);
                }
                set
                {
                    _setElement(GraphicAttributeElements.Italic, value);
                }
            }

            public Underline Underline
            {
                get
                {
                    if ((Elements & GraphicAttributeElements.Underline_Single) != 0)
                        return Underline.Single;
                    if ((Elements & GraphicAttributeElements.Underline_Double) != 0)
                        return Underline.Double;
                    return Underline.None;

                }
                set
                {
                    _setElement(GraphicAttributeElements.Underline_Single, false);
                    _setElement(GraphicAttributeElements.Underline_Double, false);
                    switch (value)
                    {
                        case Underline.Single:
                            _setElement(GraphicAttributeElements.Underline_Single, true);
                            break;
                        case Underline.Double:
                            _setElement(GraphicAttributeElements.Underline_Double, true);
                            break;
                    }

                }
            }

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

            public bool Conceal
            {
                get
                {
                    return _getElement(GraphicAttributeElements.Conceal);
                }
                set
                {
                    _setElement(GraphicAttributeElements.Conceal, value);
                }
            }

            public TextColor Foreground { get; set; }

            public TextColor Background { get; set; }

            public Color ForegroundColor
            {
                get
                {
                    return TextColorToColor(Foreground);
                }
            }

            public Color BackgroundColor
            {
                get
                {
                    return TextColorToColor(Background);
                }
            }

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
                    throw new ArgumentOutOfRangeException("_textColor", "Unknown color value.");
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
            }
        }
        #endregion
        #region Subclass Glyph (Cell entity)

        public class Glyph
        {
            private char m_char;
            private GraphicAttributes m_graphicAttributes;

            public char Char
            {
                get
                {
                    return m_char;
                }
                set
                {
                    m_char = value;
                }
            }

            public GraphicAttributes Attributes
            {
                get
                {
                    return m_graphicAttributes;
                }
                set
                {
                    m_graphicAttributes = value;
                }
            }

            public Glyph()
                : this(' ')
            {
            }

            public Glyph(char _char)
            {
                m_char = _char;
                m_graphicAttributes = new GraphicAttributes();
            }

            public Glyph(char _char, GraphicAttributes _attribs)
            {
                m_char = _char;
                m_graphicAttributes = _attribs;
            }

            public void Invert()
            {
                var back = m_graphicAttributes.Background;
                m_graphicAttributes.Background = m_graphicAttributes.Foreground;
                m_graphicAttributes.Foreground = back;
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
                    for (int x = 0; x < Width; ++x)
                    {
                        for (int y = 0; y < Height; ++y)
                        {
                            currentScreenBuffer[x, y] = new Glyph();  // Don't let this do callbacks
                        }
                    }
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
                    CheckColumnRow(value.X, value.Y);

                    m_cursorPosition = value;

                    new UIAction_CursorMoved(value.X, value.Y, m_showCursor);
                }
            }
        }

        public Glyph this[int _column, int _row]
        {
            get
            {
                CheckColumnRow(_column, _row);

                return currentScreenBuffer[_column, _row];
            }
            set
            {
                CheckColumnRow(_column, _row);

                currentScreenBuffer[_column, _row] = value;

                new UIAction_UpdateScreen(_column, _row, value);

                new UIAction_UpdateScreen(_column, _row, value);
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
            Size = new Size(_width, _height);
            m_showCursor = true;
            m_savedCursorPosition = Point.Empty;
            m_currentAttributes.Reset();
        }

        public void ReSize(int termWidth, int termHeight)
        {
            // For now we just start over, later we clip
            //  Someday I'd like to do really clever things with line wrapping

            Size = new Size(termWidth, termHeight);
            m_showCursor = true;
            m_savedCursorPosition = Point.Empty;
            m_currentAttributes.Reset();
        }

        public void DoScreenClear()
        {
            var w = Width;
            var h = Height;

            // Jettison Screen Buffer and regen
            currentScreenBuffer = null;
            Size = new Size(w, h);

            m_showCursor = true;
            m_savedCursorPosition = Point.Empty;
            m_currentAttributes.Reset();

            new UIAction_ClearScreen();
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
                        new UIAction_UpdateScreen(x, y, c);
                    else
                    {
                        var white = (c.Char == ' ');
                        if (c.Attributes.Background != TextColor.Black)
                            white = false;
                        if (!white)
                            new UIAction_UpdateScreen(x, y, c);
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

        protected void CheckColumnRow(int _column, int _row)
        {
            if (_column >= Width)
            {
                throw new ArgumentOutOfRangeException(String.Format("The column number ({0}) is larger than the screen width ({1})", _column, Width));
            }
            if (_row >= Height)
            {
                throw new ArgumentOutOfRangeException(String.Format("The row number ({0}) is larger than the screen height ({1})", _row, Height));
            }
        }

        public void CursorForward()
        {
            if (m_cursorPosition.X + 1 >= Width)
            {
                if (m_cursorPosition.Y + 1 < Height)  // This can make us scroll
                    CursorPosition = new Point(0, m_cursorPosition.Y + 1);
                else
                {
                    ScrollScreen(1);
                    CursorPosition = new Point(0, Height - 1);
                }
            }
            else
            {
                CursorPosition = new Point(m_cursorPosition.X + 1, m_cursorPosition.Y);
            }
        }

        public void CursorBackward()
        {
            if (m_cursorPosition.X - 1 < 0)
            {
                CursorPosition = new Point(Width - 1, m_cursorPosition.Y - 1);
            }
            else
            {
                CursorPosition = new Point(m_cursorPosition.X - 1, m_cursorPosition.Y);
            }
        }

        private void ScrollScreen(int lines)
        {
            if (lines < 0)
                throw new Exception("Can't scroll up yet");

            // Save the top line

            var keepScroll = new Glyph[Width];
            for (var col = 0; col < Width; col++)
                keepScroll[col] = currentScreenBuffer[col, 0];

            // Move all lines up - top line goes away, bottom line is blank

            for (var row = 1; row < Height; row++)
                for (var col = 0; col < Width; col++)
                    currentScreenBuffer[col, row - 1] = currentScreenBuffer[col, row];

            for (var col = 0; col < Width; col++)
                currentScreenBuffer[col, Height - 1] = new Glyph();

            new UIAction_ScrollScreenUp(keepScroll);
        }

        public void CursorDown(bool scroll)
        {
            if (m_cursorPosition.Y + 1 >= Height)
            {
                if (!scroll) return;

                // Hit bottom.  Must scroll

                ScrollScreen(1);
                return;  // We scrolled instead of moving cursor down, so no change there
            }

            CursorPosition = new Point(m_cursorPosition.X, m_cursorPosition.Y + 1);
        }

        public void CursorUp(bool scroll)
        {
            if (m_cursorPosition.Y - 1 < 0)
            {
                if (!scroll) return;

                // Have to reverse scroll - which we don't know how to do yet

                ScrollScreen(-1);

            }
            CursorPosition = new Point(m_cursorPosition.X, m_cursorPosition.Y - 1);
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

                    this[CursorPosition] = new Glyph(ch, m_currentAttributes); ;
                    CursorForward();
                    return;
                }
                switch (ch)
                {
                    case '\n':      // Linefeed, FF, VT
                        (this as IAnsiDecoderClient).MoveCursorToBeginningOfLineBelow(_sender, 1, true);
                        return;
                    case '\r':      // Return
                        (this as IAnsiDecoderClient).MoveCursorToColumn(_sender, 0);
                        return;
                    case '\x08':    // Backspace
                        CursorBackward();
                        return;
                    case '\t':      // Tab
                        // Need real tab stops
                        var nextT = NextTabStop(CursorPosition.X, Width);
                        for (var i = 0; i < nextT; i++)
                        {
                            this[CursorPosition] = new Glyph(ch, m_currentAttributes); ;
                            CursorForward();
                        }
                        return;
                    case '\x07':    // BEL
                        return;
                        // SI
                        // SO
                        // ENQ
                        // BEL
                }

            }
        }

        void IAnsiDecoderClient.SaveCursor(IAnsiDecoder _sernder)
        {
            m_savedCursorPosition = m_cursorPosition;
        }

        void IAnsiDecoderClient.RestoreCursor(IAnsiDecoder _sender)
        {
            CursorPosition = m_savedCursorPosition;
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
            m_cursorPosition.X = 0;
            while (_lineNumberRelativeToCurrentLine > 0)
            {
                CursorDown(scroll);
                _lineNumberRelativeToCurrentLine--;
            }
        }

        void IAnsiDecoderClient.MoveCursorToBeginningOfLineAbove(IAnsiDecoder _sender, int _lineNumberRelativeToCurrentLine, bool scroll)
        {
            m_cursorPosition.X = 0;
            while (_lineNumberRelativeToCurrentLine > 0)
            {
                CursorUp(scroll);
                _lineNumberRelativeToCurrentLine--;
            }
        }

        void IAnsiDecoderClient.MoveCursorToColumn(IAnsiDecoder _sender, int _columnNumber)
        {
            CheckColumnRow(_columnNumber, m_cursorPosition.Y);

            CursorPosition = new Point(_columnNumber, m_cursorPosition.Y);
        }

        void IAnsiDecoderClient.MoveCursorTo(IAnsiDecoder _sender, Point _position)
        {
            CheckColumnRow(_position.X, _position.Y);

            CursorPosition = _position;
        }

        void IAnsiDecoderClient.ClearScreen(IAnsiDecoder _sender, ClearDirection _direction)
        {
            DoScreenClear();
        }

        void IAnsiDecoderClient.ClearLine(IAnsiDecoder _sender, ClearDirection _direction)
        {
            switch (_direction)
            {
                case ClearDirection.Forward:
                    for (int x = m_cursorPosition.X; x < Width; ++x)
                    {
                        this[x, m_cursorPosition.Y] = new Glyph(' ', this[x, m_cursorPosition.Y].Attributes);
                    }
                    break;

                case ClearDirection.Backward:
                    for (int x = m_cursorPosition.X; x >= 0; --x)
                    {
                        this[x, m_cursorPosition.Y] = new Glyph(' ', this[x, m_cursorPosition.Y].Attributes);
                    }
                    break;

                case ClearDirection.Both:
                    for (int x = 0; x < Width; ++x)
                    {
                        this[x, m_cursorPosition.Y] = new Glyph(' ', this[x, m_cursorPosition.Y].Attributes);
                    }
                    break;
            }
        }

        void IAnsiDecoderClient.ScrollPageUpwards(IAnsiDecoder _sender, int _linesToScroll)
        {
            ScrollScreen(_linesToScroll);
        }

        void IAnsiDecoderClient.ScrollPageDownwards(IAnsiDecoder _sender, int _linesToScroll)
        {
            ScrollScreen(-_linesToScroll);
        }

        void IAnsiDecoderClient.ModeChanged(IAnsiDecoder _sender, AnsiMode _mode)
        {
            Size cur;
            switch (_mode)
            {
                case AnsiMode.HideCursor:
                    m_showCursor = false;
                    new UIAction_CursorMoved(m_cursorPosition.X, m_cursorPosition.Y, m_showCursor);
                    break;

                case AnsiMode.ShowCursor:
                    m_showCursor = true;
                    new UIAction_CursorMoved(m_cursorPosition.X, m_cursorPosition.Y, m_showCursor);
                    break;
                case AnsiMode.SwitchToMainBuffer:
                    if (screenBufferIsPrimary) return;
                    alternateScreenBuffer = currentScreenBuffer;
                    saveAlternativeCursor = m_cursorPosition;
                    cur = Size;
                    currentScreenBuffer = primaryScreenBuffer;
                    m_cursorPosition = savePrimaryCursor;
                    Size = cur;
                    DoRefresh(true);
                    screenBufferIsPrimary = true;
                    break;
                case AnsiMode.SwitchToAlternateBuffer:
                    if (!screenBufferIsPrimary) return;
                    primaryScreenBuffer = currentScreenBuffer;
                    savePrimaryCursor = m_cursorPosition;
                    cur = Size;
                    currentScreenBuffer = primaryScreenBuffer;
                    m_cursorPosition = savePrimaryCursor;
                    Size = cur;
                    DoRefresh(true);
                    screenBufferIsPrimary = false;
                    break;
            }


        }

        Point IAnsiDecoderClient.GetCursorPosition(IAnsiDecoder _sender)
        {
            return new Point(m_cursorPosition.X + 1, m_cursorPosition.Y + 1);
        }

        void IAnsiDecoderClient.SetProperty(IAnsiDecoder _sender, PropertyTypes type, string value)
        {
            new UIAction_SetProperty(type, value);
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
                            throw new Exception("Unknown rendition command");
                        break;
                }
            }
        }
        #endregion
        #region Misc
        void IDisposable.Dispose()
        {
            currentScreenBuffer = null;
        }

        #endregion
    }
}
