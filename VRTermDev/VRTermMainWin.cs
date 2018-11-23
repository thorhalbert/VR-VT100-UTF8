using libVT100;
using Renci.SshNet;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VRTermDev
{
    public partial class VRTermMain : Form
    {
        string fontName = "Consolas";
        int fontSize = 12;
        private Font screenFont;

        // Connection (SSH)
        private SshClient client;
        private IAnsiDecoder vt100;
        private libVT100.TerminalFrameBuffer screen;
        private KeyboardStream keyboardStream;

        int termColumns, termRows;

        public VRTermMain()
        {
            InitializeComponent();

            screenInfo.Text = statusLabel.Text = terminalLegend.Text = "";

            //SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            screenFont = new Font(fontName, fontSize);

            VRTermMain_SizeChanged(this, new EventArgs());  // After this, we'll have a bitmap

            terminalFrameBuffer.OnTerminalSizeChanged += TerminalFrameBuffer_OnTerminalSizeChanged;
            terminalFrameBuffer.TerminalFont = screenFont;

            checkLoginState();
        }

        private void TerminalFrameBuffer_OnTerminalSizeChanged(int columns, int rows)
        {
            termColumns = columns;
            termRows = rows;

            screenInfo.Text = "Size: " + columns + ", " + rows;
        }

        private void checkLoginState()
        {
            if (client != null && client.IsConnected)
            {
                connectButton.Enabled = false;
                disconnectButton.Enabled = true;

                statusLabel.Text = client.ConnectionInfo.Username + "@" + client.ConnectionInfo.Host + " [" + client.ConnectionInfo.ServerVersion + "]";

                return;
            }

            bool allGood = true;
            if (String.IsNullOrWhiteSpace(host_textbox.Text)) allGood = false;
            if (String.IsNullOrWhiteSpace(user_textbox.Text)) allGood = false;
            if (String.IsNullOrWhiteSpace(pass_textbox.Text)) allGood = false;

            disconnectButton.Enabled = false;

            connectButton.Enabled = allGood;

            statusLabel.Text = "disconnected";
        }

        // With flow layout's its hard to get the picturebox to autosize - some hackery here
        private void VRTermMain_SizeChanged(object sender, EventArgs e)
        {
            var left = terminalFrameBuffer.Left;
            var top = terminalFrameBuffer.Top;

            var fSize = this.Size;

            terminalFrameBuffer.Size = new Size(fSize.Width - 25, fSize.Height - top - 50);  // hacky offsets
        }

        private void host_textbox_TextChanged(object sender, EventArgs e)
        {
            checkLoginState();
        }

        private void user_textbox_TextChanged(object sender, EventArgs e)
        {
            checkLoginState();
        }

        private void pass_textbox_TextChanged(object sender, EventArgs e)
        {
            checkLoginState();
        }

        private void connectButton_Click(object sender, EventArgs e)
        {
            if (client != null && client.IsConnected) return;

            if (client != null)
                client.Dispose();

            host_textbox.Enabled =
                user_textbox.Enabled =
                pass_textbox.Enabled = false;

            client = new SshClient(host_textbox.Text, user_textbox.Text, pass_textbox.Text);

            screen = new libVT100.TerminalFrameBuffer(10, 10);  // This will get set to reality quickly
           
            keyboardStream = new KeyboardStream();
            var screenS = new ScreenStream();

            terminalFrameBuffer.BoundScreen = screen;
            terminalFrameBuffer.LegendLabel = terminalLegend;

            terminalFrameBuffer.Init();

            vt100 = new AnsiDecoder();
            vt100.Output += Vt100_Output;
            screenS.InjectTo = vt100;
            vt100.Encoding = new UTF8Encoding(); // Encoding.GetEncoding("utf8");
            vt100.Subscribe(screen);

            try
            {
                client.Connect();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Connect: " + ex.Message, "Connect Error", MessageBoxButtons.OK);
                client.Dispose();
                client = null;

                return;
            }

            checkLoginState();

            if (terminalFrameBuffer.CanFocus)
            {
                terminalFrameBuffer.Focus();
            }

            var shell = client.CreateShell(keyboardStream, screenS, screenS);  // stdin, stdout, stderr

            shell.Start();
        }

        // This is terminal output (like from report escape sequences - should be shunted back into the input buffer)
        private void Vt100_Output(IDecoder _decoder, byte[] _output)
        {
        
        }

        private void disconnectButton_Click(object sender, EventArgs e)
        {
            host_textbox.Enabled =
            user_textbox.Enabled =
            pass_textbox.Enabled = true;

            if (client != null && client.IsConnected)
                client.Disconnect();

            if (client != null)
                client.Dispose();

            client = null;

            checkLoginState();
        }

        private void pass_textbox_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                connectButton.PerformClick();
            }
        }

        private void terminalFrameBuffer_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            var value = KeyboardMaps.ConvertStroke(KeyboardMaps.KeyboardTypes.American, e);

            keyboardStream.Inject(value);
        }
    }
}
