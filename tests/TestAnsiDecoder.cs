using System;
using System.Text;
using System.Collections.Generic;
using System.Drawing;
using NUnit.Framework;
using libVT100;

namespace libVT100.Tests
{
    [TestFixture]
    public class TestAnsiDecoder : IAnsiDecoderClient
    {
        private List<char[]> m_chars;
        private List<byte[]> m_output;
        private IAnsiDecoder m_vt100;
        
        private Point m_cursorPosition;
        private bool m_showCursor;
        private bool m_hideCursor;
        private int m_scrollPageDownwards;
        private int m_scrollPageUpwards;
        private ClearDirection m_clearLine;
        private ClearDirection m_clearScreen;
        private Point m_moveCursorTo;
        private int m_moveCursorToColumn;
        private int m_moveCursorToBeginningOfLineAbove;
        private int m_moveCursorToBeginningOfLineBelow;
        private Direction m_moveCursorDirection;
        private int m_moveCursorAmount;
        private Size m_size;
        private bool m_restoreCursor;
        private bool m_saveCursor;
        
        [SetUp]
        public void SetUp ()
        {
            Reset ();
            
            m_cursorPosition = new Point(0,0);
            m_size = new Size(10,10);
              
            m_vt100 = new AnsiDecoder ();
            
            m_chars = new List<char[]>();
            m_output = new List<byte[]>();

            m_vt100.Subscribe ( this );
            m_vt100.Output += new DecoderOutputDelegate(m_vt100_Output);
        }
        
        [TearDown]
        public void TearDown ()
        {
            m_vt100.Output -= new DecoderOutputDelegate(m_vt100_Output);
            m_vt100.UnSubscribe ( this );
            m_vt100.Dispose();
            m_chars = null;
            m_output = null;
            m_vt100 = null;
            m_chars = null;
        }
        
        [Test]
        public void TestMoveCursorTo ()
        {
            Input ( "AB\x001B[2;5fCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( new Point(4,1), m_moveCursorTo );

            Reset();
            
            Input ( "AB\x001B2;5fCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( new Point(4,1), m_moveCursorTo );
            
            Reset();
            
            Input ( "AB\x001B[;4fCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( new Point(3,0), m_moveCursorTo );
            
            Reset();
            
            Input ( "AB\x001B[3;fCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( new Point(0,2), m_moveCursorTo );
            
            Reset();
            
            Input ( "AB\x001B[;fCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( new Point(0,0), m_moveCursorTo );

            Reset();
            
            Input ( "AB\x001B[fCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( new Point(0,0), m_moveCursorTo );
        }

        [Test]
        public void TestGetCursorPosition ()
        {
            Input ( "AB\x001B[6nCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( "\x001B[1;1R", Output );
            
            Reset();
            
            m_cursorPosition = new Point(7,2);
            Input ( "AB\x001B[6nCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( "\x001B[3;8R", Output );
        }

        [Test]
        public void TestShowCursor ()
        {
            Input ( "AB\x001B[?25hCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.That ( m_showCursor );
        }
        
        [Test]
        public void TestHideCursor ()
        {
            Input ( "AB\x001B[?25lCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.That ( m_hideCursor );
        }

        [Test]
        public void TestSaveCursorPosition ()
        {
            Input ( "AB\x001B[sCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.That ( m_saveCursor );
        }

        [Test]
        public void TestRestoreCursorPosition ()
        {
            Input ( "AB\x001B[uCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.That ( m_restoreCursor );
        }

        [Test]
        public void TestScrollPageDownwards ()
        {
            Input ( "AB\x001B[TCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( 1, m_scrollPageDownwards );
            
            Reset();
            
            Input ( "AB\x001B[6TCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( 6, m_scrollPageDownwards );
            Reset();
        }

        [Test]
        public void TestScrollPageUpwards ()
        {
            Input ( "AB\x001B[SCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( 1, m_scrollPageUpwards );
            
            Reset();
            
            Input ( "AB\x001B[4SCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( 4, m_scrollPageUpwards );
            Reset();
        }

        [Test]
        public void TestClearScreen ()
        {
            Input ( "AB\x001B[JCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( ClearDirection.Forward, m_clearScreen );
            
            Reset();

            Input ( "AB\x001B[0JCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( ClearDirection.Forward, m_clearScreen );
            
            Reset();

            Input ( "AB\x001B[1JCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( ClearDirection.Backward, m_clearScreen );
            
            Reset();

            Input ( "AB\x001B[2JCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( ClearDirection.Both, m_clearScreen );
        }

        [Test]
        public void TestClearLine ()
        {
            Input ( "AB\x001B[KCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( ClearDirection.Forward, m_clearLine );
            
            Reset();

            Input ( "AB\x001B[0KCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( ClearDirection.Forward, m_clearLine );
            
            Reset();

            Input ( "AB\x001B[1KCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( ClearDirection.Backward, m_clearLine );
            
            Reset();

            Input ( "AB\x001B[2KCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( ClearDirection.Both, m_clearLine );
        }

        [Test]
        public void TestMoveCursor ()
        {
            Input ( "AB\x001B[ACDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( Direction.Up, m_moveCursorDirection );
            Assert.AreEqual ( 1, m_moveCursorAmount );
            
            Reset ();
            
            Input ( "AB\x001B[2ACDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( Direction.Up, m_moveCursorDirection );
            Assert.AreEqual ( 2, m_moveCursorAmount );
            
            Reset ();

            Input ( "AB\x001B[BCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( Direction.Down, m_moveCursorDirection );
            Assert.AreEqual ( 1, m_moveCursorAmount );
            
            Reset ();
            
            Input ( "AB\x001B[3BCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( Direction.Down, m_moveCursorDirection );
            Assert.AreEqual ( 3, m_moveCursorAmount );
            
            Reset ();

            Input ( "AB\x001B[CCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( Direction.Forward, m_moveCursorDirection );
            Assert.AreEqual ( 1, m_moveCursorAmount );
            
            Reset ();
            
            Input ( "AB\x001B[6CCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( Direction.Forward, m_moveCursorDirection );
            Assert.AreEqual ( 6, m_moveCursorAmount );
            
            Reset ();

            Input ( "AB\x001B[DCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( Direction.Backward, m_moveCursorDirection );
            Assert.AreEqual ( 1, m_moveCursorAmount );
            
            Reset ();
            
            Input ( "AB\x001B[4DCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( Direction.Backward, m_moveCursorDirection );
            Assert.AreEqual ( 4, m_moveCursorAmount );
        }

        [Test]
        public void TestMoveCursorToBeginningOfLineAbove ()
        {
            Input ( "AB\x001B[FCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( 1, m_moveCursorToBeginningOfLineAbove );
            
            Reset ();

            Input ( "AB\x001B[4FCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( 4, m_moveCursorToBeginningOfLineAbove );
        }
        
        [Test]
        public void TestMoveCursorToBeginningOfLineBelow ()
        {
            Input ( "AB\x001B[ECDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( 1, m_moveCursorToBeginningOfLineBelow );
            
            Reset ();

            Input ( "AB\x001B[3ECDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( 3, m_moveCursorToBeginningOfLineBelow );
        }

        [Test]
        public void TestMoveCursorToColumn ()
        {
            Input ( "AB\x001B[1GCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( 0, m_moveCursorToColumn );
            
            Reset ();

            Input ( "AB\x001B[7GCDE" );
            Assert.AreEqual ( "ABCDE", ReceivedCharacters );
            Assert.AreEqual ( 6, m_moveCursorToColumn );
        }
        
        public void Reset ()
        {
            m_showCursor = false;
            m_hideCursor = false;
            m_scrollPageDownwards = -1;
            m_scrollPageUpwards = -1;
            m_clearLine = (ClearDirection) (-1);
            m_clearScreen = (ClearDirection) (-1);
            m_moveCursorTo = Point.Empty;
            m_moveCursorToColumn = -1;
            m_moveCursorToBeginningOfLineAbove = -1;
            m_moveCursorToBeginningOfLineBelow = -1;
            m_moveCursorDirection = (Direction) (-1);
            m_moveCursorAmount = -1;
            m_restoreCursor = false;
            m_saveCursor = false;
        }
        
        private void Input ( String _input )
        {
            byte[] data = new byte[_input.Length];
            int i = 0;
            foreach ( char c in _input )
            {
                data[i] = (byte) c;
                i++;
            }
            m_vt100.Input ( data );
        }
        
        private String ReceivedCharacters
        {
            get
            {
                StringBuilder builder = new StringBuilder();
                foreach ( char[] chars in m_chars )
                {
                    builder.Append ( chars );
                }
                m_chars.Clear();
                return builder.ToString();
            }
        }
        
        private String Output
        {
            get
            {
                StringBuilder builder = new StringBuilder();
                foreach ( byte[] chars in m_output )
                {
                    foreach ( byte b in chars )
                    {
                        builder.Append ( (char) b );
                    }
                }
                m_output.Clear();
                return builder.ToString();
            }
        }

        private void m_vt100_Output ( IDecoder _decoder, byte[] _output )
        {
            m_output.Add ( _output );
        }
        
        void IAnsiDecoderClient.Characters ( IAnsiDecoder _sender, char[] _chars )
        {
            m_chars.Add ( _chars );
        }
        
        void IAnsiDecoderClient.SaveCursor ( IAnsiDecoder _sernder )
        {
            m_saveCursor = true;
        }
        
        void IAnsiDecoderClient.RestoreCursor ( IAnsiDecoder _sender )
        {
            m_restoreCursor = true;
        }
        
        Size IAnsiDecoderClient.GetSize ( IAnsiDecoder _sender )
        {
            return m_size;
        }
        
        void IAnsiDecoderClient.MoveCursor ( IAnsiDecoder _sender, Direction _direction, int _amount )
        {
            m_moveCursorDirection = _direction;
            m_moveCursorAmount = _amount;
        }
        
        void IAnsiDecoderClient.MoveCursorToBeginningOfLineBelow ( IAnsiDecoder _sender, int _lineNumberRelativeToCurrentLine )
        {
            m_moveCursorToBeginningOfLineBelow = _lineNumberRelativeToCurrentLine;
        }
        
        void IAnsiDecoderClient.MoveCursorToBeginningOfLineAbove ( IAnsiDecoder _sender, int _lineNumberRelativeToCurrentLine )
        {
            m_moveCursorToBeginningOfLineAbove = _lineNumberRelativeToCurrentLine;
        }
        
        void IAnsiDecoderClient.MoveCursorToColumn ( IAnsiDecoder _sender, int _columnNumber )
        {
            m_moveCursorToColumn = _columnNumber;
        }
        
        void IAnsiDecoderClient.MoveCursorTo ( IAnsiDecoder _sender, Point _position )
        {
            m_moveCursorTo = _position;
        }
        
        void IAnsiDecoderClient.ClearScreen ( IAnsiDecoder _sender, ClearDirection _direction )
        {
            m_clearScreen = _direction;
        }
        
        void IAnsiDecoderClient.ClearLine ( IAnsiDecoder _sender, ClearDirection _direction )
        {
            m_clearLine = _direction;
        }
        
        void IAnsiDecoderClient.ScrollPageUpwards ( IAnsiDecoder _sender, int _linesToScroll )
        {
            m_scrollPageUpwards = _linesToScroll;
        }
        
        void IAnsiDecoderClient.ScrollPageDownwards ( IAnsiDecoder _sender, int _linesToScroll )
        {
            m_scrollPageDownwards = _linesToScroll;
        }

       void IAnsiDecoderClient.ModeChanged( IAnsiDecoder _sender, AnsiMode _mode )
        {
           switch ( _mode )
           {
              case AnsiMode.HideCursor:
                 m_hideCursor = true;
                 break;

              case AnsiMode.ShowCursor:
                 m_showCursor = true;
                 break;
           }
        }
        
        Point IAnsiDecoderClient.GetCursorPosition ( IAnsiDecoder _sender )
        {
            return m_cursorPosition;
        }

        void IAnsiDecoderClient.SetGraphicRendition ( IAnsiDecoder _sender, GraphicRendition[] _commands )
        {
            
        }
        
        void IDisposable.Dispose ()
        {
        }

        public void SetProperty(IAnsiDecoder _sender, PropertyTypes type, string value)
        {
            throw new NotImplementedException();
        }
    }
}
