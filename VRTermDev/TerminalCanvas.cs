using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VRTermDev
{
    public partial class TerminalCanvas : Control , ISupportInitialize
    {
        public Font TerminalFont { get; set; }
        public libVT100.Screen BoundScreen { get; set; }

        public event Action<int, int> OnTerminalSizeChanged;

        int charWidth;
        int charHeight;
        private Size charSize;
        int termWidth = 0;
        int termHeight = 0;
        private Bitmap terminalCanvasBitmap;
        private Graphics terminalCanvasBitmapGraphic;

        public TerminalCanvas()
        {
            InitializeComponent();

            // SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer, true);

            // Turns out that scrolling in gdi+ is really hard!

            // this.DoubleBuffered = true;
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
                BoundScreen.OnCursorChanged += BoundScreen_OnCursorChanged;
                BoundScreen.OnScreenChanged += BoundScreen_OnScreenChanged;
                BoundScreen.ScreenScrollsUp += BoundScreen_ScreenScrollsUp;
            }

            buildBuffer();

            initCalled = true;

          
        }

        private static Dictionary<Color, SolidBrush> _brushCache = new Dictionary<Color, SolidBrush>();

        private static SolidBrush _getSolid(Color c)
        {
            SolidBrush brush;
            if (_brushCache.TryGetValue(c, out brush))
            {
                return brush;
            }

            brush = new SolidBrush(c);
            _brushCache.Add(c, brush);

            return brush;
        }

        private void BoundScreen_ScreenScrollsUp(libVT100.Screen.Character[] obj)
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)(() => { BoundScreen_ScreenScrollsUp(obj); }));
                return;
            }

            // We have to clip our bitmap and repaint it - nearly impossible in base gdi+

            if (terminalCanvasBitmap == null) return;

            var scrollRect = new Rectangle(new Point(0, charHeight), new Size(termWidth * charWidth, (termHeight-1) * charHeight));

            using (var tmpBit = terminalCanvasBitmap.Clone(scrollRect, System.Drawing.Imaging.PixelFormat.DontCare))
            {
                terminalCanvasBitmapGraphic.DrawImage((Image)tmpBit, new Point(0, 0));
            }
            
            var blankRect = new Rectangle(new Point(0, (termHeight-1) * charHeight), new Size(termWidth * charWidth, charHeight));
            terminalCanvasBitmapGraphic.FillRectangle(_getSolid(Color.Black), blankRect);
        }

        private void BoundScreen_OnScreenChanged(int column, int row, libVT100.Screen.Character inChar)
        {
            if (this.InvokeRequired) {
                this.Invoke((MethodInvoker)(() => { BoundScreen_OnScreenChanged(column, row, inChar); }));
                return;
            }

            this.SuspendLayout();

            var cellPoint = new Point(column * charWidth, row * charHeight);

            // Ultimatlely I want to cache each glyph into a bitmap - though you'd need one for each background/foreground color 

            Rectangle rect = new Rectangle(cellPoint, charSize);
            terminalCanvasBitmapGraphic.FillRectangle(_getSolid(inChar.Attributes.BackgroundColor), rect);

            var _font = TerminalFont;
            if (inChar.Attributes.Bold)
            {
                if (inChar.Attributes.Italic)
                {
                    _font = new Font(_font.FontFamily, _font.Size, FontStyle.Bold | FontStyle.Italic);
                }
                else
                {
                    _font = new Font(_font.FontFamily, _font.Size, FontStyle.Bold);
                }
            }
            else if (inChar.Attributes.Italic)
            {
                _font = new Font(_font.FontFamily, _font.Size, FontStyle.Italic);
            }
            String text = new String(inChar.Char, 1);
            terminalCanvasBitmapGraphic.DrawString(text, _font, _getSolid(inChar.Attributes.ForegroundColor), rect, StringFormat.GenericTypographic);

            this.ResumeLayout();
            this.Refresh();
        }

        private void BoundScreen_OnCursorChanged(Point arg1, bool arg2)
        {
            // Not sure how to paint a cursor yet

            //var cellPoint = new Point(cursorPosition.X * charWidth, cursorPosition.Y * charHeight);

            //var curs = Cursors.IBeam;

            //Rectangle rectangle = new Rectangle(cellPoint, charSize);

            //curs.DrawStretched(terminalGraphicsContext, rectangle);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            base.OnPaint(pe);
            // Dump our frame buffer bitmap to the screen (double-buffered hopefully)

            if (terminalCanvasBitmap!=null)
                pe.Graphics.DrawImage((Image)terminalCanvasBitmap, 0, 0);     
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            if (DesignMode) return;

            buildBuffer();

            base.OnSizeChanged(e);
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

            if (BoundScreen.Width != termWidth ||
                BoundScreen.Height != termHeight)
            {
                OnTerminalSizeChanged?.Invoke(termWidth, termHeight);    // Need to resize the ansi-decoder/vt100 BoundScreen is attached to
                BoundScreen.ReSize(termWidth, termHeight); 
            }

            BoundScreen.DoRefresh(false);
        }

        public void BeginInit()
        {
            
        }

        public void EndInit()
        {
           
        }
    }
}
