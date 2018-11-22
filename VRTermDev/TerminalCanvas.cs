using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using libVT100;

namespace VRTermDev
{
    public partial class TerminalCanvas : Control, ISupportInitialize
    {
        public Font TerminalFont { get; set; }
        public TerminalFrameBuffer BoundScreen { get; set; }

        public event Action<int, int> OnTerminalSizeChanged;

        int charWidth;
        int charHeight;
        private Size charSize;
        int termWidth = 0;
        int termHeight = 0;
        private Rectangle terminalRectangle;
        private Bitmap terminalCanvasBitmap;
        private Graphics terminalCanvasBitmapGraphic;
        private Timer timer;

        public TerminalCanvas()
        {
            InitializeComponent();

            // SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            timer = new Timer
            {
                Interval = 200
            };
            timer.Tick += Timer_Tick;
            timer.Start();
        }

        #region Manage Control Changes
        private void Timer_Tick(object sender, EventArgs e)
        {
            try
            {
                if (BoundScreen != null)
                    BoundScreen.ActionFlush();
            }
            finally
            {
                timer.Start();
            }
        }

        private bool initCalled = false;

        public void Init()
        {
            if (DesignMode) return;

            if (TerminalFont == null)
                throw new Exception("Must set TerminalFont");

            if (BoundScreen == null)
                throw new Exception("Must set BoundScreen");

            if (!initCalled)
            {
                // Only do this once - Init can be called multiple times if needed
                BoundScreen.OnUIAction += BoundScreen_OnUIAction;
            }

            buildBuffer();

            initCalled = true;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            // Dump our frame buffer bitmap to the screen (double-buffered hopefully)

            if (terminalCanvasBitmap != null)
                pe.Graphics.DrawImage((Image)terminalCanvasBitmap, 0, 0);

            // Paint the cursor (if any)
            if (!cursorIsVisible) return;

            // For now, we just draw the character inverted

            var glyph = BoundScreen.GetGlyph(cursorCurrent.X, cursorCurrent.Y);

            glyph.Invert();

            var glyphMap = getGlyph(glyph.Attributes.BackgroundColor,
            glyph.Attributes.ForegroundColor,
            glyph.Attributes.Elements,
            glyph.Char);

            var cursorRect = new Rectangle(new Point(cursorCurrent.X * charWidth, cursorCurrent.Y * charHeight), charSize);
            pe.Graphics.DrawImage((Image)glyphMap, cursorRect);

            base.OnPaint(pe);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            if (DesignMode) return;

            buildBuffer();

            base.OnSizeChanged(e);
        }

        // Hopefully this lets us trap the tab
        protected override bool IsInputKey(Keys keyData)
        {
            return true; // base.IsInputKey(keyData);
        }

        private void buildBuffer()
        {
            var tSize = this.Size;

            if (tSize.Height < 1) return;
            if (TerminalFont == null) return;

            var tmpG = this.CreateGraphics();
            terminalCanvasBitmap = new Bitmap(tSize.Width, tSize.Height, tmpG);

            terminalCanvasBitmapGraphic = Graphics.FromImage((Image)terminalCanvasBitmap); ;
            var charS = terminalCanvasBitmapGraphic.MeasureString("M", TerminalFont);

            charWidth = Convert.ToInt32(charS.Width + .5) - 6;  // This is HACK!  Need to figure out how to deal with font margins
            charHeight = Convert.ToInt32(charS.Height + .5);
            charSize = new Size(charWidth, charHeight);

            termWidth = tSize.Width / charWidth;
            termHeight = tSize.Height / charHeight;

            terminalRectangle = new Rectangle(new Point(0, 0), new Size(termWidth * charWidth, termHeight * charHeight));

            if (BoundScreen.Width != termWidth ||
                BoundScreen.Height != termHeight)
            {
                OnTerminalSizeChanged?.Invoke(termWidth, termHeight);    // Need to resize the ansi-decoder/vt100 BoundScreen is attached to
                BoundScreen.ReSize(termWidth, termHeight);
            }

            BoundScreen.DoRefresh(false);
        }
        #endregion

        #region Action Handlers for Frame Buffer
        private void BoundScreen_OnUIAction(List<TerminalFrameBuffer.UIActions> actions)
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)(() => { BoundScreen_OnUIAction(actions); }));
                return;
            }

            this.SuspendLayout();

            // Eventually need to sort the action list

            TerminalFrameBuffer.UIAction_CursorMoved lastMove = null;

            foreach (var action in actions)
                switch (action.Action)
                {
                    case TerminalFrameBuffer.UIActions.ActionTypes.ClearScreen:
                        terminalCanvasBitmapGraphic.FillRectangle(_getSolid(Color.Black), terminalRectangle);
                        break;
                    case TerminalFrameBuffer.UIActions.ActionTypes.CursorMoved:
                        lastMove = (TerminalFrameBuffer.UIAction_CursorMoved)action;
                        break;
                    case TerminalFrameBuffer.UIActions.ActionTypes.SrollScreenUp:
                        var scroll = (TerminalFrameBuffer.UIAction_ScrollScreenUp)action;
                        BoundScreen_ScreenScrollsUp(scroll.SaveRow);
                        break;
                    case TerminalFrameBuffer.UIActions.ActionTypes.UpdateScreen:
                        var glyph = (TerminalFrameBuffer.UIAction_UpdateScreen)action;
                        BoundScreen_OnScreenChanged(glyph.Column, glyph.Row, glyph.Glyph);
                        break;
                    default:
                        throw new NotImplementedException();
                }

            if (lastMove != null)
            {
                var moved = (TerminalFrameBuffer.UIAction_CursorMoved)lastMove;
                BoundScreen_OnCursorChanged(new Point(moved.Column, moved.Row), moved.ShowCursor);
            }

            this.ResumeLayout();
            this.Invalidate();
        }

        private void BoundScreen_ScreenScrollsUp(TerminalFrameBuffer.Glyph[] obj)
        {
            // We have to clip our bitmap and repaint it - nearly impossible in base gdi+

            if (terminalCanvasBitmap == null) return;

            var scrollRect = new Rectangle(new Point(0, charHeight), new Size(termWidth * charWidth, (termHeight - 1) * charHeight));

            using (var tmpBit = terminalCanvasBitmap.Clone(scrollRect, System.Drawing.Imaging.PixelFormat.DontCare))
            {
                terminalCanvasBitmapGraphic.DrawImage((Image)tmpBit, new Point(0, 0));
            }

            var blankRect = new Rectangle(new Point(0, (termHeight - 1) * charHeight), new Size(termWidth * charWidth, charHeight));
            terminalCanvasBitmapGraphic.FillRectangle(_getSolid(Color.Black), blankRect);
        }

        private void BoundScreen_OnScreenChanged(int column, int row, TerminalFrameBuffer.Glyph inChar)
        {
            if (inChar.Char < 32)
                return;    // So far see BEL

            var glyphMap = getGlyph(inChar.Attributes.BackgroundColor,
                inChar.Attributes.ForegroundColor,
                inChar.Attributes.Elements,
                inChar.Char);

            Rectangle rect = new Rectangle(new Point(column * charWidth, row * charHeight), charSize);

            terminalCanvasBitmapGraphic.DrawImage((Image)glyphMap, rect);
        }

        bool cursorIsVisible = false;
        Point cursorCurrent;

        private void BoundScreen_OnCursorChanged(Point cursorPosition, bool showCursor)
        {
            cursorIsVisible = showCursor;
            cursorCurrent = cursorPosition;
        }
        #endregion

        #region Cache Managers
        private static Dictionary<Color, SolidBrush> _brushCache = new Dictionary<Color, SolidBrush>();
        public Dictionary<Tuple<Color, Color, TerminalFrameBuffer.GraphicAttributeElements, Char>, Bitmap> glyphCache = new Dictionary<Tuple<Color, Color, TerminalFrameBuffer.GraphicAttributeElements, char>, Bitmap>();
        public Dictionary<TerminalFrameBuffer.GraphicAttributeElements, Font> fontCache = new Dictionary<TerminalFrameBuffer.GraphicAttributeElements, Font>();

        private static SolidBrush _getSolid(Color c)
        {
            if (_brushCache.TryGetValue(c, out SolidBrush brush))
                return brush;

            brush = new SolidBrush(c);
            _brushCache.Add(c, brush);

            return brush;
        }

        public Bitmap getGlyph(Color BGColor, Color FGColor, TerminalFrameBuffer.GraphicAttributeElements Elements, Char glyph)
        {
            var key = new Tuple<Color, Color, TerminalFrameBuffer.GraphicAttributeElements, Char>(BGColor, FGColor, Elements, glyph);
            if (glyphCache.TryGetValue(key, out Bitmap bits))
                return bits;

            Rectangle rect = new Rectangle(new Point(0, 0), charSize);
            bits = new Bitmap(charSize.Width, charSize.Height, terminalCanvasBitmapGraphic);
            using (var gr = Graphics.FromImage((Image)bits))
            {
                gr.FillRectangle(_getSolid(BGColor), rect);

                if (!fontCache.TryGetValue(Elements, out Font procFont))
                    procFont = buildFont(Elements);

                String text = new String(glyph, 1);
                gr.DrawString(text, procFont, _getSolid(FGColor), rect, StringFormat.GenericTypographic);
            }

            glyphCache.Add(key, bits);
            return bits;
        }

        private Font buildFont(TerminalFrameBuffer.GraphicAttributeElements Elements)
        {
            if (Elements == TerminalFrameBuffer.GraphicAttributeElements.None) return TerminalFont;

            if (fontCache.TryGetValue(Elements, out Font font))
                return font;

            var bold = Elements.HasFlag(TerminalFrameBuffer.GraphicAttributeElements.Bold);
            var italic = Elements.HasFlag(TerminalFrameBuffer.GraphicAttributeElements.Italic);
            var under = Elements.HasFlag(TerminalFrameBuffer.GraphicAttributeElements.Underline_Single) ||
                Elements.HasFlag(TerminalFrameBuffer.GraphicAttributeElements.Underline_Double);

            if (bold && italic && under)
                return setFont(Elements, FontStyle.Bold | FontStyle.Italic | FontStyle.Underline);

            if (bold && italic)
                return setFont(Elements, FontStyle.Bold | FontStyle.Italic);

            if (bold && under)
                return setFont(Elements, FontStyle.Bold | FontStyle.Underline);

            if (italic && under)
                return setFont(Elements, FontStyle.Italic | FontStyle.Underline);

            if (bold)
                return setFont(Elements, FontStyle.Bold);

            if (italic)
                return setFont(Elements, FontStyle.Italic);

            if (under)
                return setFont(Elements, FontStyle.Underline);

            return setFont(Elements, FontStyle.Regular);
        }

        private Font setFont(TerminalFrameBuffer.GraphicAttributeElements elements, FontStyle style)
        {
            var _font = new Font(TerminalFont.FontFamily, TerminalFont.Size, style);

            fontCache.Add(elements, _font);

            return _font;
        }

        #endregion

        #region Misc Interfaces
        public void BeginInit()
        {

        }

        public void EndInit()
        {

        }
        #endregion
    }
}
