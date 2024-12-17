using System;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;

namespace libVT100
{
    public abstract class EscapeCharacterDecoder : IDecoder
    {
        public const byte ESC = 0x1B;
        public const byte LBRACK = 0x5B;
        public const byte RBRACK = 0x5D;
        public const byte BACKSLASH = 0x5C;

        public const byte XonCharacter = 17;
        public const byte XoffCharacter = 19;

        public const byte COMMAND_CSI = LBRACK;
        public const byte COMMAND_ST = BACKSLASH;
        public const byte COMMAND_SS2 = (int)'N';
        public const byte COMMAND_SS3 = (int)'O';
        public const byte COMMAND_DCS = (int)'P';
        public const byte COMMAND_OSC = RBRACK;
        public const byte COMMAND_APC = (int)'_';

        // 8-bit versions of the commands
        public const byte C1_SS2 = 0x8e;
        public const byte C1_SS3 = 0x8f;
        public const byte C1_DCS = 0x90;
        public const byte C1_CSI = 0x9b;
        public const byte C1_ST = 0x9c;
        public const byte C1_OSC = 0x9d;
        public const byte C1_SOS = 0x98;
        public const byte C1_PM = 0x9e;
        public const byte C1_APC = 0x9f;

        // 1 byte 8-bit commands
        public const byte C1_IND = 0x84;
        public const byte C1_NEL = 0x85;
        public const byte C1_HTS = 0x88;
        public const byte C1_RI = 0x8d;
       
        protected enum State
        {
            Normal,
            CommandCSI,
            CommandTwo,
            CommandThree,
            CommandOSC,
            CommandDCS
        }
        protected State m_state;
        protected Encoding m_encoding;
        protected Decoder m_decoder;
        protected Encoder m_encoder;
        private List<byte> m_commandBuffer;
        protected bool m_supportXonXoff;
        protected bool m_xOffReceived;
        protected List<byte[]> m_outBuffer;

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
            (this as IDecoder).Encoding = Encoding.ASCII;
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
            m_state = State.CommandCSI;  // We're guessing (it's one or the other)
            const string interParts = " !\"#$%&'()*+,-./?";
            const string paramParts = "0123456789;";
            const string twoLetter = "DEHMNOPVWXZ\\&_6789=>Fclmno|}~";
            const string threeLetter = " #%()*+-./";
            bool inEsc = false;

            // See if we should be here    

            var count = m_commandBuffer.Count;

            if (count < 2) return;  // Not enough data

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
                    throw new Exception("Internal error, first command character _MUST_ be the escape character, please report this bug to the author.");
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

                                // Something Unknown!  Unwind
                                m_state = State.Normal;  // Don't try to execute this
                                phase = InternalState.Complete;
                                return;


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
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Unsupported: " + ex.Message);
            }

            cursor--;

            // IO State machine needs work

            // Remove the processed commands
            if (m_commandBuffer.Count == cursor - 1)
            {
                // All command bytes processed, we can go back to normal handling
                m_commandBuffer.Clear();
                m_state = State.Normal;
            }
            else
            {
                bool returnToNormalState = true;
                for (int i = cursor + 1; i < m_commandBuffer.Count; i++)
                {
                    if (isCMD(m_commandBuffer[i]))
                    {
                        m_commandBuffer.RemoveRange(0, i);
                        ProcessCommandBuffer();
                        returnToNormalState = false;
                    }
                    else
                    {
                        ProcessNormalInput(m_commandBuffer[i]);
                    }
                }
                if (returnToNormalState)
                {
                    m_commandBuffer.Clear();

                    m_state = State.Normal;
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
                    return true;

                case C1_IND:
                case C1_NEL:
                case C1_HTS:
                case C1_RI:
                    return true;
            }
            return false;
        }

        protected void ProcessNormalInput(byte _data)
        {
            //System.Console.WriteLine ( "ProcessNormalInput: {0:X2}", _data );
            if (isCMD(_data ))
            {
                throw new Exception("Internal error, ProcessNormalInput was passed an escape character, please report this bug to the author.");
            }
            if (m_supportXonXoff)
            {
                if (_data == XonCharacter || _data == XoffCharacter)
                {
                    return;
                }
            }

            byte[] data = new byte[] { _data };
            int charCount = m_decoder.GetCharCount(data, 0, 1);
            char[] characters = new char[charCount];
            m_decoder.GetChars(data, 0, 1, characters, 0);

            if (charCount > 0)
            {
                OnCharacters(characters);
            }
            else
            {
                //System.Console.WriteLine ( "char count was zero" );
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

            if (_data.Length == 0)
            {
                throw new ArgumentException("Input can not process an empty array.");
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
            m_encoding = null;
            m_decoder = null;
            m_encoder = null;
            m_commandBuffer = null;
        }

        abstract protected void OnCharacters(char[] _characters);
        abstract protected void ProcessCommandCSI(byte command, String _parameter);
        abstract protected void ProcessCommandOSC(string parameters, string terminator);
        abstract protected void ProcessCommandTwo(string terminator);
        abstract protected void ProcessCommandThree(string parameters, string terminator);
        abstract protected void ProcessCommandDCS(string parameters);
      

        virtual public event DecoderOutputDelegate Output;
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
