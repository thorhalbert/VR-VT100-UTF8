using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using libVT100;

namespace libVT100.TestBench.Harness
{
    public class DiffResult
    {
        public bool Success { get; set; }
        public List<string> FailureMessages { get; } = new List<string>();
        public string VisualDiff { get; set; } = string.Empty;
    }

    public static class FrameBufferDiff
    {
        public static DiffResult Compare(TerminalFrameBuffer actual, string[] expectedRows, Point? expectedCursor = null)
        {
            var result = new DiffResult { Success = true };
            int width = actual.Width;
            int height = actual.Height;

            var mismatches = new List<int>();

            // Validate dimensions
            if (expectedRows.Length > height)
            {
                result.Success = false;
                result.FailureMessages.Add($"Expected {expectedRows.Length} rows, but terminal height is {height}.");
            }

            int rowsToCompare = Math.Min(expectedRows.Length, height);

            for (int r = 0; r < rowsToCompare; r++)
            {
                string expectedRow = expectedRows[r].PadRight(width);
                if (expectedRow.Length > width)
                    expectedRow = expectedRow.Substring(0, width);

                var actualSb = new StringBuilder(width);
                for (int c = 0; c < width; c++)
                {
                    actualSb.Append(actual[c, r]?.Char ?? ' ');
                }
                string actualRow = actualSb.ToString();

                if (!string.Equals(expectedRow, actualRow, StringComparison.Ordinal))
                {
                    result.Success = false;
                    mismatches.Add(r);
                    result.FailureMessages.Add($"Row {r:D2} mismatch:\n  Expected: \"{expectedRow}\"\n  Actual:   \"{actualRow}\"");
                }
            }

            if (expectedCursor.HasValue)
            {
                Point actualCursor = actual.CursorPosition;
                if (actualCursor != expectedCursor.Value)
                {
                    result.Success = false;
                    result.FailureMessages.Add($"Cursor mismatch: Expected ({expectedCursor.Value.X},{expectedCursor.Value.Y}), Actual ({actualCursor.X},{actualCursor.Y})");
                }
            }

            // Build side-by-side diff table
            var diffSb = new StringBuilder();
            diffSb.AppendLine("┌── Visual Diff Comparison ─────────────────────────────────────────────────────────┐");
            diffSb.AppendLine($"│ Row │ {"EXPECTED".PadRight(width)} │ {"ACTUAL".PadRight(width)} │ Status   │");
            diffSb.AppendLine($"├─────┼─{new string('─', width)}─┼─{new string('─', width)}─┼──────────┤");

            int maxRows = Math.Max(expectedRows.Length, height);
            for (int r = 0; r < maxRows; r++)
            {
                string exp = r < expectedRows.Length ? expectedRows[r].PadRight(width).Substring(0, width) : new string(' ', width);
                string act = r < height ? GetRowString(actual, r, width) : new string(' ', width);

                bool isMismatch = mismatches.Contains(r) || (r >= expectedRows.Length) || (r >= height);
                string status = isMismatch ? "<<< FAIL " : "  OK     ";

                diffSb.AppendLine($"│ {r:D2}  │ {exp} │ {act} │ {status}│");
            }

            diffSb.AppendLine($"└─────┴─{new string('─', width)}─┴─{new string('─', width)}─┴──────────┘");
            result.VisualDiff = diffSb.ToString();

            return result;
        }

        private static string GetRowString(TerminalFrameBuffer actual, int row, int width)
        {
            var sb = new StringBuilder(width);
            for (int c = 0; c < width; c++)
            {
                sb.Append(actual[c, row]?.Char ?? ' ');
            }
            return sb.ToString();
        }
    }
}
