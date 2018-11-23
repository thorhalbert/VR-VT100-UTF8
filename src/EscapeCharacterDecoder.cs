using System;
using System.Text;
using System.Collections.Generic;
using System.Windows.Forms;

namespace libVT100
{
    public abstract class EscapeCharacterDecoder : IDecoder
    {
        public const byte EscapeCharacter = 0x1B;
        public const byte LeftBracketCharacter = 0x5B;
        public const byte RightBracketCharacter = 0x5D;
        public const byte BackslashCharacter = 0x5C;
        public const byte XonCharacter = 17;
        public const byte XoffCharacter = 19;

        protected enum State
        {
            Normal,
            CommandCSI,
            CommandTwo,
            CommandThree,
            CommandOSC
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

            // Internal check

            if (m_commandBuffer[cursor++] != EscapeCharacter)  // Internal Assert
            {
                throw new Exception("Internal error, first command character _MUST_ be the escape character, please report this bug to the author.");
            }

            // Start the state machine

            while (true)
            {
                if (cursor == count) return;   // Need more buffer

                switch (phase)
                {
                    case InternalState.Command:
                        var cmd = m_commandBuffer[cursor++];
                        switch (cmd)
                        {
                            case LeftBracketCharacter:  // $[ CSI
                                m_state = State.CommandCSI;
                                term = Terminators.CSITerm;
                                phase = InternalState.Parameters;
                                break;

                            case RightBracketCharacter: // $] OSC
                                m_state = State.CommandOSC;
                                term = Terminators.OSC_ST_BEL;
                                intermediates = null;
                                phase = InternalState.Terminator;
                                break;

                            default:
                                // A Two Letter Escape Sequene
                                if (twoLetter.IndexOf((char)cmd)>=0)
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
                                if (cmd == EscapeCharacter)
                                {
                                    inEsc = true;
                                    break;
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









            //if (m_commandBuffer.Count > 1)
            //{

            //    int start = 1, end = 0;
            //    // Is this a one or two byte escape code (CSI)?
            //    if (m_commandBuffer[start] == LeftBracketCharacter)
            //    {
            //        m_state = State.CommandCSI;  // Definately
            //        start++;

            //        // It is a two byte escape code, but we still need more data
            //        if (m_commandBuffer.Count < 3)
            //            return;

            //         end = start;

            //        if (m_commandBuffer.Count == 2 &&
            //            IsValidOneCharacterCommand((char)m_commandBuffer[start]))
            //            end = m_commandBuffer.Count - 1;

            //        if (end == m_commandBuffer.Count)
            //            return;  // More data needed

            //        Decoder decoder = (this as IDecoder).Encoding.GetDecoder();
            //        byte[] parameterData = new byte[end - start];
            //        for (int i = 0; i < parameterData.Length; i++)
            //            parameterData[i] = m_commandBuffer[start + i];

            //        int parameterLength = decoder.GetCharCount(parameterData, 0, parameterData.Length);
            //        char[] parameterChars = new char[parameterLength];
            //        decoder.GetChars(parameterData, 0, parameterData.Length, parameterChars, 0);
            //        String parameter = new String(parameterChars);

            //        byte command = m_commandBuffer[end];

            //        try
            //        {
            //            ProcessCommandCSI(command, parameter);
            //        }
            //        catch (Exception ex)
            //        {
            //            throw;  // Give's me a place for a breakpoint
            //        }
            //    }

            // Is this an OSC (Operating System Command?)
            // Is this a one or two byte escape code (CSI)?
            //if (m_commandBuffer[start] == RightBracketCharacter)
            //{
            //    start++;

            //    // It is a two byte escape code, but we still need more data
            //    if (m_commandBuffer.Count < 3)
            //    {
            //        return;
            //    }
            //}


            //bool insideQuotes = false;

            //while (end < m_commandBuffer.Count && (IsValidParameterCharacter((char)m_commandBuffer[end]) || insideQuotes))
            //{
            //    if (m_commandBuffer[end] == '"')
            //    {
            //        insideQuotes = !insideQuotes;
            //    }
            //    end++;
            //}

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
                }
            }
            catch (Exception ex)
            {
                throw;
            }

            //if (count == cursor)
            //{
            //    m_commandBuffer.Clear();
            //    m_state = State.Normal;
            //}
            //else
            //{
            //    m_commandBuffer.RemoveRange(0, cursor);
            //    ProcessNormalInput(m_commandBuffer)
            //}

            cursor--;

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
                    if (m_commandBuffer[i] == EscapeCharacter)
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

        protected void ProcessNormalInput(byte _data)
        {
            //System.Console.WriteLine ( "ProcessNormalInput: {0:X2}", _data );
            if (_data == EscapeCharacter)
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
                    if (_data[0] == EscapeCharacter)
                    {
                        AddToCommandBuffer(_data);
                        ProcessCommandBuffer();
                    }
                    else
                    {
                        int i = 0;
                        while (i < _data.Length && _data[i] != EscapeCharacter)
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
