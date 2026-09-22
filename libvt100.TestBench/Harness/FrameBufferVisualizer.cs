using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using libVT100;

namespace libVT100.TestBench.Harness
{
    public class VisualizerOptions
    {
        public bool ShowRulers { get; set; } = true;
        public bool ShowCursorMarker { get; set; } = true;
        public bool ShowAttributesSummary { get; set; } = true;
        public int MaxRows { get; set; } = 0; // 0 = all
    }

    public static class FrameBufferVisualizer
    {
        public static string RenderToString(TerminalFrameBuffer buffer, VisualizerOptions? options = null)
        {
            var sb = new StringBuilder();
            using (var writer = new StringWriter(sb))
            {
                Render(buffer, writer, options);
            }
            return sb.ToString();
        }

        public static void Render(TerminalFrameBuffer buffer, TextWriter writer, VisualizerOptions? options = null)
        {
            options ??= new VisualizerOptions();
            int width = buffer.Width;
            int height = buffer.Height;
            Point cursor = buffer.CursorPosition;
            int rowsToRender = options.MaxRows > 0 && options.MaxRows < height ? options.MaxRows : height;

            // Box header
            string title = $" VT FrameBuffer [{width}x{height}] | Cursor: ({cursor.X},{cursor.Y}) ";
            int innerWidth = Math.Max(width + 6, title.Length + 4);
            int paddingRight = Math.Max(0, innerWidth - title.Length - 3);

            writer.WriteLine($"┌─{title}{new string('─', paddingRight)}┐");

            // Top ruler
            if (options.ShowRulers)
            {
                var tensSb = new StringBuilder("│     ");
                for (int c = 0; c < width; c++)
                    tensSb.Append(c % 10 == 0 ? ((c / 10) % 10).ToString() : " ");
                while (tensSb.Length < innerWidth + 1) tensSb.Append(' ');
                tensSb.Append('│');
                writer.WriteLine(tensSb.ToString());

                var unitsSb = new StringBuilder("│Col: ");
                for (int c = 0; c < width; c++)
                    unitsSb.Append(c % 10);
                while (unitsSb.Length < innerWidth + 1) unitsSb.Append(' ');
                unitsSb.Append('│');
                writer.WriteLine(unitsSb.ToString());

                writer.WriteLine($"├─────{new string('─', width)}{new string('─', Math.Max(0, innerWidth - width - 5))}┤");
            }

            // Screen rows
            var nonDefaultAttrs = new List<string>();

            for (int r = 0; r < rowsToRender; r++)
            {
                var rowSb = new StringBuilder($"│ {r:D2} │");

                for (int c = 0; c < width; c++)
                {
                    TerminalFrameBuffer.Glyph glyph = buffer[c, r];
                    char ch = glyph?.Char ?? ' ';

                    bool isCursor = options.ShowCursorMarker && (c == cursor.X && r == cursor.Y);
                    if (isCursor && ch == ' ') ch = '█';

                    rowSb.Append(ch);

                    if (glyph != null)
                    {
                        var attrs = glyph.Attributes;
                        bool hasAttr = attrs.Bold || attrs.Italic 
                            || attrs.Underline != TerminalFrameBuffer.Underline.None
                            || attrs.Foreground != TerminalFrameBuffer.TextColor.White
                            || attrs.Background != TerminalFrameBuffer.TextColor.Black
                            || attrs.CHARSET == TerminalFrameBuffer.GraphicAttributes.CHARSETs.G1;

                        if (hasAttr)
                        {
                            var desc = new List<string>();
                            if (attrs.Bold) desc.Add("Bold");
                            if (attrs.Italic) desc.Add("Italic");
                            if (attrs.Underline != TerminalFrameBuffer.Underline.None) desc.Add($"Underline={attrs.Underline}");
                            if (attrs.Foreground != TerminalFrameBuffer.TextColor.White) desc.Add($"Fg={attrs.Foreground}");
                            if (attrs.Background != TerminalFrameBuffer.TextColor.Black) desc.Add($"Bg={attrs.Background}");
                            if (attrs.CHARSET == TerminalFrameBuffer.GraphicAttributes.CHARSETs.G1) desc.Add("ACS");

                            nonDefaultAttrs.Add($"({c},{r}): '{ch}' [{string.Join(", ", desc)}]");
                        }
                    }
                }

                rowSb.Append('│');
                writer.WriteLine(rowSb.ToString());
            }

            // Bottom border
            writer.WriteLine($"└─────{new string('─', width)}{new string('─', Math.Max(0, innerWidth - width - 5))}┘");

            if (options.ShowAttributesSummary && nonDefaultAttrs.Count > 0)
            {
                writer.WriteLine("Attributes Breakdown:");
                int maxToShow = Math.Min(nonDefaultAttrs.Count, 10);
                for (int i = 0; i < maxToShow; i++)
                    writer.WriteLine($"  • {nonDefaultAttrs[i]}");
                if (nonDefaultAttrs.Count > maxToShow)
                    writer.WriteLine($"  ... and {nonDefaultAttrs.Count - maxToShow} more attributed cells.");
            }
        }
    }
}
