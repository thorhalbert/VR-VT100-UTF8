using System;

namespace libVT100.Oob
{
    /// <summary>
    /// Consumer interface for receiving decoded Out-of-Band (OOB) packets.
    /// </summary>
    public interface IOobPacketHandler
    {
        /// <summary>
        /// Handles a received OOB packet.
        /// </summary>
        /// <param name="header">The strongly-typed metadata header.</param>
        /// <param name="payload">The binary payload span.</param>
        void HandlePacket(OobHeader header, ReadOnlySpan<byte> payload);
    }
}
