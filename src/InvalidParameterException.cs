using System;
using System.Runtime.Serialization;

namespace libVT100
{
    /// <summary>
    /// Exception thrown when a command is accompanied by an invalid or unparseable parameter.
    /// </summary>
    [global::System.Serializable]
    public class InvalidParameterException : InvalidByteException
    {
        protected string m_parameter = string.Empty;

        /// <summary>
        /// Gets the command byte.
        /// </summary>
        public byte Command
        {
            get
            {
                return Byte;
            }
        }

        /// <summary>
        /// Gets the invalid parameter string.
        /// </summary>
        public string Paramter
        {
            get
            {
                return m_parameter;
            }
        }

        /// <summary>
        /// Initializes a new instance of <see cref="InvalidParameterException"/>.
        /// </summary>
        /// <param name="_command">Command byte.</param>
        /// <param name="_parameter">Invalid parameter string.</param>
        public InvalidParameterException(byte _command, string _parameter)
           : base(_command, String.Format("Invalid parameter for command {0:X2} '{1}', parameter = \"{2}\"", _command, (char)_command, _parameter))
        {
            m_parameter = _parameter;
        }

#pragma warning disable SYSLIB0051
        protected InvalidParameterException(SerializationInfo info,
                                             StreamingContext context)
           : base(info, context)
        {
            info.AddValue("Paramter", m_parameter);
        }
#pragma warning restore SYSLIB0051
    }
}

