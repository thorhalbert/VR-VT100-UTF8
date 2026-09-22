using System;
using System.Drawing;
using libVT100;
using libVT100.TestBench.Harness;

namespace libVT100.TestBench.Tests
{
    public interface ITestCase
    {
        string Id { get; }
        string Category { get; }
        string Description { get; }
        void Run(TestContext context);
    }

    public class TestContext
    {
        public bool Passed { get; private set; } = true;
        public string FailureMessage { get; private set; } = string.Empty;
        public string FailureDiff { get; private set; } = string.Empty;

        public void Fail(string message, string diff = "")
        {
            Passed = false;
            FailureMessage = message;
            FailureDiff = diff;
        }

        public void Assert(bool condition, string message)
        {
            if (!condition)
            {
                Fail(message);
                throw new TestAssertionException(message);
            }
        }

        public void AssertCursor(TerminalFrameBuffer buffer, int expectedCol, int expectedRow)
        {
            if (buffer.CursorPosition.X != expectedCol || buffer.CursorPosition.Y != expectedRow)
            {
                string msg = $"Cursor position mismatch: expected ({expectedCol},{expectedRow}), actual ({buffer.CursorPosition.X},{buffer.CursorPosition.Y})";
                Fail(msg);
                throw new TestAssertionException(msg);
            }
        }

        public void AssertScreen(TerminalFrameBuffer buffer, string[] expectedRows, Point? expectedCursor = null)
        {
            DiffResult diff = FrameBufferDiff.Compare(buffer, expectedRows, expectedCursor);
            if (!diff.Success)
            {
                string msg = string.Join(Environment.NewLine, diff.FailureMessages);
                Fail(msg, diff.VisualDiff);
                throw new TestAssertionException(msg);
            }
        }
    }

    public class TestAssertionException : Exception
    {
        public TestAssertionException(string message) : base(message) { }
    }
}
