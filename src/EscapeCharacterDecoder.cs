using System;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;

namespace libVT100
{
    /// <summary>
    /// Base stream decoder that consumes raw bytes and parses C0/C1 control characters and escape sequences.
    /// Handles fragmentation across packet boundaries without data loss.
    /// </summary>
    public abstract class EscapeCharacterDecoder : IDecoder
    {
        /// <summary>ASCII Escape character (0x1B).</summary>
        public const byte ESC = 0x1B;
        /// <summary>ASCII Left Bracket '[' (0x5B), CSI introducer character.</summary>
        public const byte LBRACK = 0x5B;
        /// <summary>ASCII Right Bracket ']' (0x5D), OSC introducer character.</summary>
        public const byte RBRACK = 0x5D;
        /// <summary>ASCII Backslash '\' (0x5C), ST string terminator character.</summary>
        public const byte BACKSLASH = 0x5C;

        /// <summary>XON software flow control character (DC1, 17).</summary>
        public const byte XonCharacter = 17;
        /// <summary>XOFF software flow control character (DC3, 19).</summary>
        public const byte XoffCharacter = 19;

        /// <summary>7-bit CSI introducer character '['.</summary>
        public const byte COMMAND_CSI = LBRACK;
        /// <summary>7-bit String Terminator '\'.</summary>
        public const byte COMMAND_ST = BACKSLASH;
        /// <summary>Single Shift 2 'N'.</summary>
        public const byte COMMAND_SS2 = (int)'N';
        /// <summary>Single Shift 3 'O'.</summary>
        public const byte COMMAND_SS3 = (int)'O';
        /// <summary>Device Control String introducer 'P'.</summary>
        public const byte COMMAND_DCS = (int)'P';
        /// <summary>Operating System Command introducer ']'.</summary>
        public const byte COMMAND_OSC = RBRACK;
        /// <summary>Application Program Command introducer '_'.</summary>
        public const byte COMMAND_APC = (int)'_';

        // 8-bit versions of the commands
        /// <summary>8-bit Single Shift 2 (0x8E).</summary>
        public const byte C1_SS2 = 0x8e;
        /// <summary>8-bit Single Shift 3 (0x8F).</summary>
        public const byte C1_SS3 = 0x8f;
        /// <summary>8-bit Device Control String (0x90).</summary>
        public const byte C1_DCS = 0x90;
        /// <summary>8-bit Control Sequence Introducer (0x9B).</summary>
        public const byte C1_CSI = 0x9b;
        /// <summary>8-bit String Terminator (0x9C).</summary>
        public const byte C1_ST = 0x9c;
        /// <summary>8-bit Operating System Command (0x9D).</summary>
        public const byte C1_OSC = 0x9d;
        /// <summary>8-bit Start of String (0x98).</summary>
        public const byte C1_SOS = 0x98;
        /// <summary>8-bit Privacy Message (0x9E).</summary>
        public const byte C1_PM = 0x9e;
        /// <summary>8-bit Application Program Command (0x9F).</summary>
        public const byte C1_APC = 0x9f;

        // 1 byte 8-bit commands
        /// <summary>8-bit Index (0x84).</summary>
        public const byte C1_IND = 0x84;
        /// <summary>8-bit Next Line (0x85).</summary>
        public const byte C1_NEL = 0x85;
        /// <summary>8-bit Horizontal Tab Set (0x88).</summary>
        public const byte C1_HTS = 0x88;
        /// <summary>8-bit Reverse Index (0x8D).</summary>
        public const byte C1_RI = 0x8d;
       
        protected enum State
        {
            Normal,
            CommandCSI,
            CommandTwo,
            CommandThree,
            CommandOSC,
            CommandDCS,
            CommandAPC
        }
        protected State m_state;
        protected Encoding m_encoding = Encoding.ASCII;
        protected Decoder m_decoder = Encoding.ASCII.GetDecoder();
        protected Encoder m_encoder = Encoding.ASCII.GetEncoder();
        private List<byte> m_commandBuffer = new List<byte>();
        protected bool m_supportXonXoff;
        protected bool m_xOffReceived;
        protected List<byte[]> m_outBuffer = new List<byte[]>();

        /// <summary>Maximum duration an incomplete escape sequence will wait for input before timing out and resuming normal parsing.</summary>
        public TimeSpan SequenceTimeout { get; set; } = TimeSpan.FromSeconds(3.0);
        private DateTime m_sequenceStartTime = DateTime.MinValue;

        Encoding IDecoder.Encoding
        {
            get
            {
                return m_encoding;
            }
            set
            {
                if (m_encoding != value)
                {
                    m_encoding = value;
                    m_decoder = m_encoding.GetDecoder();
                    m_encoder = m_encoding.GetEncoder();
                }
            }
        }

        public EscapeCharacterDecoder()
        {
            m_state = State.Normal;
            m_encoding = Encoding.ASCII;
            m_decoder = m_encoding.GetDecoder();
            m_encoder = m_encoding.GetEncoder();
            m_commandBuffer = new List<byte>();
            m_supportXonXoff = false;
            m_xOffReceived = false;
            m_outBuffer = new List<byte[]>();
        }

        virtual protected bool IsValidParameterCharacter(char _c)
        {
            //var interMed = "0123456789 ;!\"#$%&'()*+,-./";
            const string interMed = "0123456789;?>=!#";
            return interMed.IndexOf(_c) >= 0;

            //return (Char.IsNumber( _c ) || _c == '(' || _c == ')' || _c == ';' || _c == '"' || _c == '?');
            //return (Char.IsNumber(_c) || _c == ';' || _c == '"' || _c == '?');
        }

        protected void AddToCommandBuffer(byte _byte)
        {
            if (m_supportXonXoff)
                if (_byte == XonCharacter || _byte == XoffCharacter)
                    return;

            m_commandBuffer.Add(_byte);
        }

        protected void AddToCommandBuffer(byte[] _bytes)
        {
            if (m_supportXonXoff)
            {
                foreach (byte b in _bytes)
                    if (!(b == XonCharacter || b == XoffCharacter))
                        m_commandBuffer.Add(b);
            }
            else
                m_commandBuffer.AddRange(_bytes);
        }

        protected virtual bool IsValidOneCharacterCommand(char _command)
        {
            return false;
        }

        private enum InternalState
        {
            C1Command,
            Command,
            Parameters,
            Terminator,
            Complete
        }

        private enum Terminators
        {
            Unknown,
            Third,      // The third letter
            CSITerm,    // Typical CSI, terminated by non-intermediate char
            OSC_ST,     // Terminated by $\ (ST)
            OSC_ST_BEL  // Terminaled by an ST ($\) or a BEL (0x07)
        }

        // Decoding state machine (at least for CSI codes)
        protected void ProcessCommandBuffer()
        {
            // Parser saw and escape so sent here

            var phase = InternalState.Command;
            var term = Terminators.Unknown;

            var intermediates = string.Empty;
            var parameters = string.Empty;
            var terminator = string.Empty;

            int cursor = 0;
            const string interParts = " !\"#$%&'()*+,-./";
            const string paramParts = "0123456789;:?>=<";
            const string twoLetter = "DEHMNOPVWXZ\\&_6789=>Fclmno|}~";
            const string threeLetter = " #%()*+-./";
            bool inEsc = false;

            // See if we should be here    

            var count = m_commandBuffer.Count;

            if (count < 1) return;  // Not enough data
            if (m_commandBuffer[0] == ESC && count < 2) return;

            if (m_sequenceStartTime == DateTime.MinValue)
            {
                m_sequenceStartTime = DateTime.UtcNow;
            }

            if ((DateTime.UtcNow - m_sequenceStartTime) > SequenceTimeout)
            {
                Console.Error.WriteLine($"[libvt100:WARN] Escape sequence timed out after {SequenceTimeout.TotalSeconds:F1}s in state {m_state}. Resuming ground state.");
                m_state = State.Normal;
                m_sequenceStartTime = DateTime.MinValue;
                m_commandBuffer.Clear();
                return;
            }

            bool isLargeCommand = false;
            if (m_commandBuffer[0] == ESC && count >= 2)
            {
                byte c2 = m_commandBuffer[1];
                isLargeCommand = (c2 == COMMAND_APC || c2 == COMMAND_OSC || c2 == COMMAND_DCS);
            }
            else if (m_commandBuffer[0] == C1_APC || m_commandBuffer[0] == C1_OSC || m_commandBuffer[0] == C1_DCS)
            {
                isLargeCommand = true;
            }

            int maxLimit = isLargeCommand ? (16 * 1024 * 1024) : 4096;
            if (count > maxLimit)
            {
                Console.Error.WriteLine($"[libvt100:WARN] Escape sequence buffer overflow ({count} bytes in state {m_state}). Resetting to ground state.");
                m_state = State.Normal;
                m_sequenceStartTime = DateTime.MinValue;
                m_commandBuffer.Clear();
                return;
            }

            // Allow the full 8 bit commands too
            byte skipFirst = 0;
            var first = m_commandBuffer[cursor++];
            switch (first)
            {
                case ESC:
                    skipFirst = 0;
                    break;
                case C1_CSI:
                    skipFirst = COMMAND_CSI;
                    break;
                case C1_OSC:
                    skipFirst = COMMAND_OSC;
                    break;
                case C1_DCS:
                    skipFirst = COMMAND_DCS;
                    break;
                case C1_APC:
                    skipFirst = COMMAND_APC;
                    break;

                // 1 byte commands
                case C1_IND:
                    m_state = State.CommandTwo;
                    phase = InternalState.Complete;
                    terminator = "D";
                    break;
                case C1_NEL:
                    m_state = State.CommandTwo;
                    phase = InternalState.Complete;
                    terminator = "E";
                    break;
                case C1_HTS:
                    m_state = State.CommandTwo;
                    phase = InternalState.Complete;
                    terminator = "H";
                    break;
                case C1_RI:
                    m_state = State.CommandTwo;
                    phase = InternalState.Complete;
                    terminator = "M";
                    break;


                default:
                    Console.Error.WriteLine($"[libvt100:ERROR] First command character (0x{first:X2}) was not an escape introducer. Resetting parser.");
                    m_state = State.Normal;
                    m_sequenceStartTime = DateTime.MinValue;
                    m_commandBuffer.Clear();
                    return;
            }

            // Start the state machine

            while (true)
            {
                if (cursor == count) return;   // Need more buffer

                switch (phase)
                {
                    case InternalState.Command:
                        byte cmd = 0;
                        if (skipFirst == 0)
                            cmd = m_commandBuffer[cursor++];
                        else cmd = skipFirst;

                        switch (cmd)
                        {
                            case COMMAND_CSI:  // $[ CSI
                                m_state = State.CommandCSI;
                                term = Terminators.CSITerm;
                                phase = InternalState.Parameters;
                                break;

                            case COMMAND_OSC: // $] OSC
                                m_state = State.CommandOSC;
                                term = Terminators.OSC_ST_BEL;
                                intermediates = null;
                                phase = InternalState.Terminator;
                                break;

                            case COMMAND_DCS:
                                m_state = State.CommandDCS;
                                term = Terminators.OSC_ST;
                                intermediates = null;
                                phase = InternalState.Terminator;
                                break;

                            case COMMAND_APC: // $_ APC (Application Program Command)
                                m_state = State.CommandAPC;
                                term = Terminators.OSC_ST_BEL;
                                intermediates = null;
                                phase = InternalState.Terminator;
                                break;

                            // The other two letter command types will get caught below

                            default:
                                // A Two Letter Escape Sequene
                                if (twoLetter.IndexOf((char)cmd) >= 0)
                                {
                                    m_state = State.CommandTwo;
                                    terminator = new String(new char[] { (char)cmd });
                                    phase = InternalState.Complete;

                                    break;
                                }

                                if (threeLetter.IndexOf((char)cmd) > 0)
                                {
                                    m_state = State.CommandThree;
                                    parameters = new String(new char[] { (char)cmd });
                                    term = Terminators.Third;
                                    phase = InternalState.Terminator;

                                    break;
                                }

                                // Something Unknown! Discard invalid escape sequence and recover
                                m_state = State.Normal;
                                phase = InternalState.Complete;
                                break;


                                // Other escape types (+VT52 types)
                                // $N SS2
                                // $O SS3
                                // $P DCS
                                // $\ ST
                                // $X SOS
                                // $^ PM
                                // $_ APC
                                // $c RIS
                        }
                        break;

                    case InternalState.Parameters:
                        cmd = m_commandBuffer[cursor];
                        if (cmd == 0x18 || cmd == 0x1A) // CAN / SUB cancel
                        {
                            cursor++;
                            m_state = State.Normal;
                            phase = InternalState.Complete;
                            break;
                        }
                        if (interParts.IndexOf((char)cmd) >= 0)
                        {
                            intermediates += (char)cmd;
                            cursor++;
                            break;
                        }
                        if (paramParts.IndexOf((char)cmd) >= 0)
                        {
                            parameters += (char)cmd;
                            cursor++;
                            break;
                        }

                        phase = InternalState.Terminator;
                        break;
                    case InternalState.Terminator:
                        cmd = m_commandBuffer[cursor++];
                        if (cmd == 0x18 || cmd == 0x1A) // CAN / SUB cancel
                        {
                            m_state = State.Normal;
                            phase = InternalState.Complete;
                            break;
                        }
                        switch (term)
                        {
                            case Terminators.Third:
                                terminator = new String(new char[] { (char)cmd });
                                phase = InternalState.Complete;
                                break;
                            case Terminators.CSITerm:
                                terminator = new String(new char[] { (char)cmd });
                                phase = InternalState.Complete;
                                break;
                            case Terminators.OSC_ST_BEL:
                                if (cmd == 0x07)
                                {
                                    terminator = new String(new char[] { (char)cmd });
                                    phase = InternalState.Complete;
                                    break;
                                }
                                goto case Terminators.OSC_ST;
                            case Terminators.OSC_ST:
                                if (cmd == ESC)
                                {
                                    inEsc = true;
                                    break;
                                }
                                if (cmd == C1_ST)  // Just fake it if high ST
                                {
                                    inEsc = true;
                                    cmd = (int)'\\';
                                }
                                if (inEsc && cmd == '\\')
                                {
                                    terminator = "\0x1b\\";
                                    phase = InternalState.Complete;
                                    break;
                                }
                                inEsc = false;
                                parameters += (char)cmd;  // Eat the inner into the parameters
                                break;
                        }
                        break;
                }

                if (phase == InternalState.Complete)  // State machine ends
                    break;
            }

            if (phase != InternalState.Complete)
            {
                // Incomplete sequence - wait for more data to arrive
                return;
            }

            // Pass our command to the processor
            try
            {
                switch (m_state)
                {
                    case State.CommandCSI:
                        ProcessCommandCSI((byte)terminator[0], intermediates + parameters);
                        break;
                    case State.CommandOSC:
                        ProcessCommandOSC(parameters, terminator);
                        break;
                    case State.CommandTwo:
                        ProcessCommandTwo(terminator);
                        break;
                    case State.CommandThree:
                        ProcessCommandThree(parameters, terminator);
                        break;
                    case State.CommandDCS:
                        ProcessCommandDCS(parameters);
                        break;
                    case State.CommandAPC:
                        ProcessCommandAPC(parameters, terminator);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[libvt100:ERROR] Error processing command in state {m_state}: {ex.Message}");
                Debug.WriteLine("Unsupported: " + ex.Message);
            }

            int bytesConsumed = cursor;
            m_commandBuffer.RemoveRange(0, bytesConsumed);
            m_sequenceStartTime = DateTime.MinValue;

            if (m_commandBuffer.Count == 0)
            {
                m_state = State.Normal;
            }
            else
            {
                int nextCmdIdx = -1;
                for (int i = 0; i < m_commandBuffer.Count; i++)
                {
                    if (isCMD(m_commandBuffer[i]))
                    {
                        nextCmdIdx = i;
                        break;
                    }
                }

                if (nextCmdIdx == -1)
                {
                    for (int i = 0; i < m_commandBuffer.Count; i++)
                    {
                        ProcessNormalInput(m_commandBuffer[i]);
                    }
                    m_commandBuffer.Clear();
                    m_state = State.Normal;
                }
                else if (nextCmdIdx == 0)
                {
                    ProcessCommandBuffer();
                }
                else
                {
                    for (int i = 0; i < nextCmdIdx; i++)
                    {
                        ProcessNormalInput(m_commandBuffer[i]);
                    }
                    m_commandBuffer.RemoveRange(0, nextCmdIdx);
                    ProcessCommandBuffer();
                }
            }
        }

        private bool isCMD(byte c)
        {
            switch (c)
            {
                case ESC:
                    return true;

                case C1_CSI:
                case C1_OSC:
                case C1_DCS:
                case C1_APC:
                    return true;

                case C1_IND:
                case C1_NEL:
                case C1_HTS:
                case C1_RI:
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Invoked when an Application Program Command (APC, ESC _ or 0x9F) sequence is decoded.
        /// </summary>
        /// <param name="parameters">The raw string payload contained within the APC envelope.</param>
        /// <param name="terminator">The string terminator (ST, ESC \ or BEL).</param>
        protected virtual void ProcessCommandAPC(string parameters, string terminator)
        {
        }

        protected void ProcessNormalInput(byte _data)
        {
            if (isCMD(_data))
            {
                Console.Error.WriteLine($"[libvt100:WARN] ProcessNormalInput was passed an escape character (0x{_data:X2}). Redirecting to command buffer.");
                AddToCommandBuffer(_data);
                ProcessCommandBuffer();
                return;
            }
            if (m_supportXonXoff)
            {
                if (_data == XonCharacter || _data == XoffCharacter)
                {
                    return;
                }
            }

            byte[] data = new byte[] { _data };
            char[] characters = new char[4];
            int charCount = m_decoder.GetChars(data, 0, 1, characters, 0, false);

            if (charCount > 0)
            {
                if (charCount == 1)
                {
                    OnCharacters(new char[] { characters[0] });
                }
                else
                {
                    char[] result = new char[charCount];
                    Array.Copy(characters, result, charCount);
                    OnCharacters(result);
                }
            }
        }

        void IDecoder.Input(byte[] _data)
        {
            /*
            System.Console.Write ( "Input[{0}]: ", m_state );
            foreach ( byte b in _data )
            {
                System.Console.Write ( "{0:X2} ", b );
            }
            System.Console.WriteLine ( "" );
            */

            //var SI = 0; var SO = 0;
            //var sb = new StringBuilder();
            //foreach (var b in _data)
            //{
            //    if (b < 32) 
            //        sb.Append($"<{b}>");
            //    else
            //        sb.Append(Convert.ToChar(b));
            //    switch (b)
            //    {
            //        case 14:
            //            SO++;
            //            break;
            //        case 15:
            //            SI++;
            //            break;
            //    }
            //}
            //var rawDump = sb.ToString();

            if (_data == null || _data.Length == 0)
            {
                return;
            }

            if (m_state != State.Normal && m_sequenceStartTime != DateTime.MinValue && (DateTime.UtcNow - m_sequenceStartTime) > SequenceTimeout)
            {
                Console.Error.WriteLine($"[libvt100:WARN] Escape sequence timed out after {SequenceTimeout.TotalSeconds:F1}s in state {m_state}. Resuming ground state.");
                m_state = State.Normal;
                m_sequenceStartTime = DateTime.MinValue;
                m_commandBuffer.Clear();
            }

            if (m_supportXonXoff)
            {
                foreach (byte b in _data)
                {
                    if (b == XoffCharacter)
                    {
                        m_xOffReceived = true;
                    }
                    else if (b == XonCharacter)
                    {
                        m_xOffReceived = false;
                        if (m_outBuffer.Count > 0)
                        {
                            foreach (byte[] output in m_outBuffer)
                            {
                                OnOutput(output);
                            }
                        }
                    }
                }
            }

            switch (m_state)
            {
                case State.Normal:
                    if (isCMD(_data[0]))
                    {
                        AddToCommandBuffer(_data);
                        ProcessCommandBuffer();
                    }
                    else
                    {
                        int i = 0;
                        while (i < _data.Length && !isCMD(_data[i]))
                        {
                            ProcessNormalInput(_data[i]);
                            i++;
                        }
                        if (i != _data.Length)
                        {
                            while (i < _data.Length)
                            {
                                AddToCommandBuffer(_data[i]);
                                i++;
                            }
                            ProcessCommandBuffer();
                        }
                    }
                    break;

                case State.CommandCSI:
                case State.CommandOSC:
                case State.CommandDCS:
                case State.CommandAPC:
                    AddToCommandBuffer(_data);
                    ProcessCommandBuffer();
                    break;
            }
        }

        void IDecoder.CharacterTyped(char _character)
        {
            byte[] data = m_encoding.GetBytes(new char[] { _character });
            OnOutput(data);
        }

        bool IDecoder.KeyPressed(Keys _modifiers, Keys _key)
        {
            return false;
        }

        void IDisposable.Dispose()
        {
            m_commandBuffer?.Clear();
            m_outBuffer?.Clear();
        }

        /// <summary>Invoked when non-escape text characters have been decoded from the input stream.</summary>
        abstract protected void OnCharacters(char[] _characters);

        /// <summary>Invoked when a complete Control Sequence Introducer (CSI) sequence has been decoded.</summary>
        /// <param name="command">The terminating command byte.</param>
        /// <param name="_parameter">Intermediate and numeric parameter string.</param>
        abstract protected void ProcessCommandCSI(byte command, String _parameter);

        /// <summary>Invoked when an Operating System Command (OSC) sequence has been decoded.</summary>
        abstract protected void ProcessCommandOSC(string parameters, string terminator);

        /// <summary>Invoked when a 2-character escape sequence (ESC + character) has been decoded.</summary>
        abstract protected void ProcessCommandTwo(string terminator);

        /// <summary>Invoked when a 3-character escape sequence (ESC + intermediate + final) has been decoded.</summary>
        abstract protected void ProcessCommandThree(string parameters, string terminator);

        /// <summary>Invoked when a Device Control String (DCS) sequence has been decoded.</summary>
        abstract protected void ProcessCommandDCS(string parameters);

        /// <summary>Event raised when output is emitted by the terminal emulator back to the host.</summary>
        virtual public event DecoderOutputDelegate? Output;

        /// <summary>Fires the <see cref="Output"/> event with raw bytes for host transmission.</summary>
        virtual protected void OnOutput(byte[] _output)
        {
            if (Output != null)
            {
                if (m_supportXonXoff && m_xOffReceived)
                {
                    m_outBuffer.Add(_output);
                }
                else
                {
                    Output(this, _output);
                }
            }
        }
    }
}
