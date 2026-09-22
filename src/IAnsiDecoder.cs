using System;
using System.Collections.Generic;
using System.Text;
using libVT100.Oob;

namespace libVT100
{
    /// <summary>
    /// Specialization of <see cref="IDecoder"/> providing subscription hooks for ANSI/VT100/VT220/xterm
    /// client consumers (such as <see cref="TerminalFrameBuffer"/>), Out-of-Band (OOB) APC packet handlers,
    /// and trace logging facilities.
    /// </summary>
    public interface IAnsiDecoder : IDecoder
    {
        /// <summary>
        /// Subscribes an <see cref="IAnsiDecoderClient"/> to receive parsed terminal commands and text.
        /// </summary>
        /// <param name="_client">The terminal client listener to subscribe.</param>
        void Subscribe(IAnsiDecoderClient _client);

        /// <summary>
        /// Unsubscribes an <see cref="IAnsiDecoderClient"/> from receiving terminal commands.
        /// </summary>
        /// <param name="_client">The terminal client listener to unsubscribe.</param>
        void UnSubscribe(IAnsiDecoderClient _client);

        /// <summary>
        /// Gets or sets an optional debug buffer for recording human-readable escape sequences using
        /// standard ANSI, DEC, and xterm jargon. Set to null to disable logging.
        /// </summary>
        StringBuilder? dvt { get; set; }

        /// <summary>
        /// Emits a debug string token into the active trace log (<see cref="dvt"/>).
        /// </summary>
        /// <param name="s">Formatted debug text token.</param>
        void deb(string s);

        /// <summary>
        /// Emits a single character into the active trace log (<see cref="dvt"/>).
        /// </summary>
        /// <param name="c">Character token.</param>
        void deb(char c);

        /// <summary>
        /// Event fired when a complete Out-of-Band (OOB) APC packet is received and reassembled.
        /// </summary>
        event EventHandler<OobPacketEventArgs>? OobPacketReceived;

        /// <summary>
        /// Registers a general handler for all incoming OOB packets.
        /// </summary>
        void RegisterOobHandler(IOobPacketHandler handler);

        /// <summary>
        /// Registers a handler for OOB packets with a specific action code ('p', 'q', 'r', 'e', 's', etc.).
        /// </summary>
        void RegisterOobHandler(string action, IOobPacketHandler handler);

        /// <summary>
        /// Registers a delegate callback for OOB packets with a specific action code.
        /// </summary>
        void RegisterOobHandler(string action, Action<OobHeader, byte[]> callback);

        /// <summary>
        /// Unregisters an OOB packet handler.
        /// </summary>
        void UnregisterOobHandler(IOobPacketHandler handler);

        /// <summary>
        /// Serializes and sends an OOB packet upstream via the PTY output stream.
        /// Automatically handles chunking when the payload exceeds <paramref name="maxChunkSize"/>.
        /// </summary>
        void SendOobPacket(
            string action,
            string? id = null,
            string? type = null,
            ReadOnlySpan<byte> payload = default,
            int maxChunkSize = 4096,
            string encoding = "b64",
            int status = 0,
            IDictionary<string, string>? customHeaders = null);

        /// <summary>
        /// Gets the OOB chunk reassembly and defragmentation engine.
        /// </summary>
        OobChunkReassembler OobReassembler { get; }
        /// <summary>
        /// Gets or sets the maximum duration an incomplete escape sequence will wait for input before timing out and resuming normal parsing.
        /// </summary>
        TimeSpan SequenceTimeout { get; set; }

    }
}


