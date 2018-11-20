using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using libVT100;

namespace VRTermDev
{
    class ScreenStream : Stream
    {
        public override bool CanRead { get { return false; } }

        public override bool CanSeek { get { return false; } }

        public override bool CanWrite { get { return true; } }

        public override long Length { get { return 0; } }

        public override long Position { get { return 0; } set => throw new NotImplementedException(); }

        public IAnsiDecoder AnsiDecoder { get; internal set; }

        public override void Flush()
        {

        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {

        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (count == 1)  // How often is this one?  This is probably cheaper
            {
                AnsiDecoder.Input(new byte[1] { buffer[offset] });
                return;
            }

            var retA = new byte[count];
            Array.Copy(buffer, offset, retA, 0, count);

            // var str = System.Text.Encoding.UTF8.GetString(buffer, offset, count);

            AnsiDecoder.Input(retA);

            // Console.WriteLine("Buffer=/" + str + "/");
        }
    }
}
