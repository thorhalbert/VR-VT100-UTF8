﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VRTermDev
{
    class KeyboardStream: Stream
    {
        private List<byte> keyboardFifo = new List<byte>();

        public override bool CanRead { get { return true; } }

        public override bool CanSeek => throw new NotImplementedException();

        public override bool CanWrite => throw new NotImplementedException();

        public override long Length => throw new NotImplementedException();

        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var retCount = 0;

            while (keyboardFifo.Count > 0 && offset<buffer.Length && count > 0)
            {
                // Pop the first item
                var pop = keyboardFifo[0];
                keyboardFifo.RemoveAt(0);

                buffer[offset++] = pop;   // We can return more than one
                retCount++;
                count--;
            }
     
            return retCount;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        internal void Inject(byte[] keyValue)
        {
            foreach (var b in keyValue)
                keyboardFifo.Add(b);
        }
    }
}
