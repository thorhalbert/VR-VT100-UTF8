using System;
using System.Drawing;

namespace libVT100.KittyGraphics
{
    /// <summary>
    /// Represents an on-screen placement of a stored <see cref="KittyImage"/> anchored to grid coordinates.
    /// Supports sub-cell pixel offsets, source cropping rectangles, and multi-layer Z-ordering.
    /// </summary>
    public sealed class KittyImagePlacement
    {
        public uint ImageId { get; set; }
        public uint PlacementId { get; set; }

        public int AnchorCol { get; set; }
        public int AnchorRow { get; set; }
        public int ColSpan { get; set; }
        public int RowSpan { get; set; }

        public int SrcX { get; set; }
        public int SrcY { get; set; }
        public int SrcWidth { get; set; }
        public int SrcHeight { get; set; }

        public int SubCellX { get; set; }
        public int SubCellY { get; set; }

        /// <summary>
        /// Z-index layering:
        /// z &lt; 0: Under text (behind glyphs)
        /// z == 0: Cell background layer
        /// z &gt; 0: Over text (above glyphs and cursor)
        /// </summary>
        public int ZIndex { get; set; }

        public Rectangle CellBounds => new(AnchorCol, AnchorRow, ColSpan, RowSpan);

        public bool IntersectsCell(int col, int row)
        {
            return col >= AnchorCol && col < AnchorCol + ColSpan &&
                   row >= AnchorRow && row < AnchorRow + RowSpan;
        }

        public bool IntersectsColumn(int col)
        {
            return col >= AnchorCol && col < AnchorCol + ColSpan;
        }

        public bool IntersectsRow(int row)
        {
            return row >= AnchorRow && row < AnchorRow + RowSpan;
        }
    }
}
