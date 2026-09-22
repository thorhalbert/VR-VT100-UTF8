using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using libVT100;

namespace libVT100.TestBench.Harness
{
    public class HeadlessTerminalSession : IDisposable
    {
        public TerminalFrameBuffer Buffer { get; }
        public IAnsiDecoder Decoder { get; }
        public List<byte> EmittedOutput { get; }

        public int Width => Buffer.Width;
        public int Height => Buffer.Height;
        public Point CursorPosition => Buffer.CursorPosition;

        public HeadlessTerminalSession(int width = 80, int height = 24)
        {
            Buffer = new TerminalFrameBuffer(width, height);
            Decoder = new AnsiDecoder();
            Decoder.Encoding = Encoding.UTF8;
            EmittedOutput = new List<byte>();

            Decoder.Output += (IDecoder sender, byte[] output) =>
            {
                if (output != null && output.Length > 0)
                {
                    EmittedOutput.AddRange(output);
                }
            };

            Decoder.Subscribe(Buffer);
        }

        /// <summary>
        /// Feeds an escape sequence or text string (which can include \e, \r, \n, etc.) to the terminal.
        /// </summary>
        public void Feed(string textWithEscapes)
        {
            byte[] bytes = SequenceEscaper.Unescape(textWithEscapes);
            Feed(bytes);
        }

        /// <summary>
        /// Feeds raw bytes to the terminal decoder.
        /// </summary>
        public void Feed(byte[] bytes)
        {
            if (bytes != null && bytes.Length > 0)
            {
                Decoder.Input(bytes);
            }
        }

        /// <summary>
        /// Feeds chunks of bytes with optional delays or inspection points, useful for testing packet fragmentation.
        /// </summary>
        public void FeedChunks(params byte[][] chunks)
        {
            foreach (var chunk in chunks)
            {
                Feed(chunk);
            }
        }

        /// <summary>
        /// Feeds individual bytes one by one to test byte-by-byte streaming and state machine resilience.
        /// </summary>
        public void FeedBytesOneByOne(string textWithEscapes)
        {
            byte[] bytes = SequenceEscaper.Unescape(textWithEscapes);
            foreach (byte b in bytes)
            {
                Decoder.Input(new byte[] { b });
            }
        }

        public string GetScreenRow(int row)
        {
            if (row < 0 || row >= Height)
                return string.Empty;

            StringBuilder sb = new StringBuilder(Width);
            for (int col = 0; col < Width; col++)
            {
                TerminalFrameBuffer.Glyph glyph = Buffer[col, row];
                sb.Append(glyph?.Char ?? ' ');
            }
            return sb.ToString();
        }

        public string[] GetAllScreenRows()
        {
            string[] rows = new string[Height];
            for (int r = 0; r < Height; r++)
            {
                rows[r] = GetScreenRow(r);
            }
            return rows;
        }

        public string GetEmittedOutputString()
        {
            return SequenceEscaper.Escape(EmittedOutput.ToArray());
        }

        public void ClearEmittedOutput()
        {
            EmittedOutput.Clear();
        }

        public void Dispose()
        {
            Decoder.UnSubscribe(Buffer);
            Buffer.Dispose();
            Decoder.Dispose();
        }
    }
}
