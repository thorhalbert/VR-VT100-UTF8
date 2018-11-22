using System;
using System.Drawing;
using System.Text;
using System.Collections;
using System.Collections.Generic;

namespace libVT100
{
    public class TerminalFrameBuffer: IAnsiDecoderClient, IEnumerable<TerminalFrameBuffer.Character>
    {
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
                throw new ArgumentOutOfRangeException("_textColor", "Unknown color value.");
                //return Color.Transparent;
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

        public class Character
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

            public Character()
                : this(' ')
            {
            }

            public Character(char _char)
            {
                m_char = _char;
                m_graphicAttributes = new GraphicAttributes();
            }

            public Character(char _char, GraphicAttributes _attribs)
            {
                m_char = _char;
                m_graphicAttributes = _attribs;
            }

        }

        protected Point m_cursorPosition;
        protected Point m_savedCursorPosition;
        protected bool m_showCursor;
        protected Character[,] m_screen;
        protected GraphicAttributes m_currentAttributes;

        public Size Size
        {
            get
            {
                return new Size(Width, Height);
            }
            set
            {
                if (m_screen == null || value.Width != Width || value.Height != Height)
                {
                    m_screen = new Character[value.Width, value.Height];
                    for (int x = 0; x < Width; ++x)
                    {
                        for (int y = 0; y < Height; ++y)
                        {
                            m_screen[x, y] = new Character();  // Don't let this do callbacks
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
                return m_screen.GetLength(0);
            }
        }

        public int Height
        {
            get
            {
                return m_screen.GetLength(1);
            }
        }

        public event Action<Point, bool> OnCursorChanged;

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

                    OnCursorChanged?.Invoke(m_cursorPosition, m_showCursor);
                }
            }
        }

        public event Action<int, int, Character> OnScreenChanged;

        public Character this[int _column, int _row]
        {
            get
            {
                CheckColumnRow(_column, _row);

                return m_screen[_column, _row];
            }
            set
            {
                CheckColumnRow(_column, _row);

                m_screen[_column, _row] = value;

                OnScreenChanged?.Invoke(_column, _row, value);
            }
        }

        public Character this[Point _position]
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
            m_screen = null;
            Size = new Size(w, h);

            m_showCursor = true;
            m_savedCursorPosition = Point.Empty;
            m_currentAttributes.Reset();

            DoRefresh(true);
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
                        OnScreenChanged?.Invoke(x, y, c);
                    else
                    {
                        var white = (c.Char == ' ');
                        if (c.Attributes.Background != TextColor.Black)
                            white = false;
                        if (!white)
                            OnScreenChanged?.Invoke(x, y, c);
                    }
                }
            }
        }

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
                    ScrollDownOne();
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

        // Get the client to scroll - thrashing the buffers is too slow
        public event Action<Character[]> ScreenScrollsUp;

        private void ScrollDownOne()
        {
            // Save the top line

            var keepScroll = new Character[Width];
            for (var col = 0; col < Width; col++)
                keepScroll[col] = m_screen[col, 0];

            // Move all lines up - top line goes away, bottom line is blank

            for (var row = 1; row < Height; row++)
                for (var col = 0; col < Width; col++)
                    m_screen[col, row - 1] = m_screen[col, row];

            for (var col = 0; col < Width; col++)
                m_screen[col, Height - 1] = new Character();

            ScreenScrollsUp?.Invoke(keepScroll);
        }

        public void CursorDown()
        {
            if (m_cursorPosition.Y + 1 >= Height)
            {
                // Hit bottom.  Must scroll

                ScrollDownOne();
                return;  // We scrolled instead of moving cursor down, so no change there

                //throw new Exception("Can not move further down!");
            }
            CursorPosition = new Point(m_cursorPosition.X, m_cursorPosition.Y + 1);
        }

        public void CursorUp()
        {
            if (m_cursorPosition.Y - 1 < 0)
            {
                throw new Exception("Can not move further up!");
            }
            CursorPosition = new Point(m_cursorPosition.X, m_cursorPosition.Y - 1);
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

        IEnumerator<TerminalFrameBuffer.Character> IEnumerable<TerminalFrameBuffer.Character>.GetEnumerator()
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
            return (this as IEnumerable<TerminalFrameBuffer.Character>).GetEnumerator();
        }

        void IAnsiDecoderClient.Characters(IAnsiDecoder _sender, char[] _chars)
        {
            foreach (char ch in _chars)
            {
                if (ch == '\n')
                {
                    (this as IAnsiDecoderClient).MoveCursorToBeginningOfLineBelow(_sender, 1);
                }
                else if (ch == '\r')
                {
                    (this as IAnsiDecoderClient).MoveCursorToColumn(_sender, 0);
                }
                else if (ch == '\x08')
                    CursorBackward();
                else
                {
                    this[CursorPosition] = new Character(ch, m_currentAttributes); ;

                    CursorForward();
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

        void IAnsiDecoderClient.MoveCursor(IAnsiDecoder _sender, Direction _direction, int _amount)
        {
            switch (_direction)
            {
                case Direction.Up:
                    while (_amount > 0)
                    {
                        CursorUp();
                        _amount--;
                    }
                    break;

                case Direction.Down:
                    while (_amount > 0)
                    {
                        CursorDown();
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

        void IAnsiDecoderClient.MoveCursorToBeginningOfLineBelow(IAnsiDecoder _sender, int _lineNumberRelativeToCurrentLine)
        {
            m_cursorPosition.X = 0;
            while (_lineNumberRelativeToCurrentLine > 0)
            {
                CursorDown();
                _lineNumberRelativeToCurrentLine--;
            }
        }

        void IAnsiDecoderClient.MoveCursorToBeginningOfLineAbove(IAnsiDecoder _sender, int _lineNumberRelativeToCurrentLine)
        {
            m_cursorPosition.X = 0;
            while (_lineNumberRelativeToCurrentLine > 0)
            {
                CursorUp();
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
                        this[x, m_cursorPosition.Y] = new Character(' ', this[x, m_cursorPosition.Y].Attributes);
                    }
                    break;

                case ClearDirection.Backward:
                    for (int x = m_cursorPosition.X; x >= 0; --x)
                    {
                        this[x, m_cursorPosition.Y] = new Character(' ', this[x, m_cursorPosition.Y].Attributes);
                    }
                    break;

                case ClearDirection.Both:
                    for (int x = 0; x < Width; ++x)
                    {
                        this[x, m_cursorPosition.Y] = new Character(' ', this[x, m_cursorPosition.Y].Attributes);
                    }
                    break;
            }
        }

        void IAnsiDecoderClient.ScrollPageUpwards(IAnsiDecoder _sender, int _linesToScroll)
        {
            for (var i = 0; i < _linesToScroll; i++)
                ScrollDownOne();
        }

        void IAnsiDecoderClient.ScrollPageDownwards(IAnsiDecoder _sender, int _linesToScroll)
        {
          
        }

        void IAnsiDecoderClient.ModeChanged(IAnsiDecoder _sender, AnsiMode _mode)
        {
            switch (_mode)
            {
                case AnsiMode.HideCursor:
                    m_showCursor = false;
                    break;

                case AnsiMode.ShowCursor:
                    m_showCursor = true;
                    break;
            }

            OnCursorChanged?.Invoke(m_cursorPosition, m_showCursor);
        }

        Point IAnsiDecoderClient.GetCursorPosition(IAnsiDecoder _sender)
        {
            return new Point(m_cursorPosition.X + 1, m_cursorPosition.Y + 1);
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

                        throw new Exception("Unknown rendition command");
                }
            }
        }

        void IDisposable.Dispose()
        {
            m_screen = null;
        }
    }
}
