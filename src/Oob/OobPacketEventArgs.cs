using System;

namespace libVT100.Oob
{
    /// <summary>
    /// Event arguments for received Out-of-Band (OOB) packets.
    /// </summary>
    public sealed class OobPacketEventArgs : EventArgs
    {
        public OobPacket Packet { get; }
        public OobHeader Header => Packet.Header;
        public byte[] Payload => Packet.Payload;

        public OobPacketEventArgs(OobPacket packet)
        {
            Packet = packet ?? throw new ArgumentNullException(nameof(packet));
        }
    }
}
