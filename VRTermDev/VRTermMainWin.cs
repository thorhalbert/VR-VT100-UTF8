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

        int charWidth;
        int charHeight;

        int termWidth = 0;
        int termHeight = 0;
        private Bitmap terminalFrameBitmap;
        private Graphics terminalGraphicsContext;

        // Connection (SSH)
        private SshClient client;
        private IAnsiDecoder vt100;
        private libVT100.Screen screen;

        public VRTermMain()
        {
            InitializeComponent();

            screenFont = new Font(fontName, fontSize);

            VRTermMain_SizeChanged(this, new EventArgs());  // After this, we'll have a bitmap

            checkLoginState();
        }

        private void checkLoginState()
        {
            if (client!=null && client.IsConnected)
            {
                connectButton.Enabled = false;
                disconnectButton.Enabled = true;

                statusLabel.Text = client.ConnectionInfo.Username + "@" + client.ConnectionInfo.Host + " [" + client.ConnectionInfo.ServerVersion+"]";

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

        // Recompute the terminal size - this will cause our initial bitmap to be built
        private void terminalFrameBuffer_SizeChanged(object sender, EventArgs e)
        {
            var tSize = terminalFrameBuffer.Size;

            charWidth = screenFont.Height;    // HACK HACK - figure out how big an M is - this are fixed fonts
            charHeight = screenFont.Height;

            termWidth = tSize.Width / charWidth;
            termHeight = tSize.Height / charHeight;

            screenInfo.Text = termHeight + " x " + termWidth;

            resizeTerminal();
        }

        private void resizeTerminal()
        {
            terminalFrameBitmap = new Bitmap(charWidth * termWidth, charHeight * termHeight);
            terminalGraphicsContext = Graphics.FromImage(terminalFrameBitmap);

            terminalFrameBuffer.Image = (Image)terminalFrameBitmap;
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

            client = new SshClient(host_textbox.Text, user_textbox.Text, pass_textbox.Text);

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

            var keyS = Console.OpenStandardInput();
            var screenS = new ScreenStream();

            vt100 = new AnsiDecoder();
            screen = new libVT100.Screen(termWidth, termHeight);

            vt100.Encoding = Encoding.GetEncoding("utf8");
            vt100.Subscribe(screen);

            screenS.AnsiDecoder = vt100;

            vt100.Output += Vt100_Output;

            var shell = client.CreateShell(keyS, screenS, screenS);  // stdin, stdout, stderr

            shell.Start();
        }

        private void Vt100_Output(IDecoder _decoder, byte[] _output)
        {
          
        }

        private void disconnectButton_Click(object sender, EventArgs e)
        {
            if (client != null && client.IsConnected)
                client.Disconnect();

            if (client != null)
                client.Dispose();

            client = null;

            checkLoginState();
        }
    }
}
