using System;
using System.Runtime.Serialization;

namespace libVT100
{
    /// <summary>
    /// Exception thrown when an unrecognized terminal escape command is parsed.
    /// </summary>
    [global::System.Serializable]
    public class InvalidCommandException : InvalidByteException
    {
        protected string m_parameter = string.Empty;

        /// <summary>
        /// Gets the final command character code.
        /// </summary>
        public byte Command
        {
            get
            {
                return base.Byte;
            }
        }

        /// <summary>
        /// Gets the command parameter string associated with the unrecognized command.
        /// </summary>
        public string Paramter
        {
            get
            {
                return m_parameter;
            }
        }

        /// <summary>
        /// Initializes a new instance of <see cref="InvalidCommandException"/>.
        /// </summary>
        /// <param name="_command">Command byte.</param>
        /// <param name="_parameter">Parameter string.</param>
        public InvalidCommandException(byte _command, string _parameter)
           : base(_command, String.Format("Invalid command {0:X2} '{1}', parameter = \"{2}\"", _command, (char)_command, _parameter))
        {
            m_parameter = _parameter;
        }

#pragma warning disable SYSLIB0051
        protected InvalidCommandException(SerializationInfo info,
                                           StreamingContext context)
           : base(info, context)
        {
            info.AddValue("Paramter", m_parameter);
        }
#pragma warning restore SYSLIB0051
    }
}

