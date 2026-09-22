using System;
using System.Runtime.Serialization;

namespace libVT100
{
    /// <summary>
    /// Exception thrown when an unexpected or malformed byte is encountered in the terminal stream.
    /// </summary>
    [global::System.Serializable]
    public class InvalidByteException : Exception
    {
        protected byte m_byte;

        /// <summary>
        /// Gets the invalid byte value that triggered the exception.
        /// </summary>
        public byte Byte
        {
            get
            {
                return m_byte;
            }
        }

        /// <summary>
        /// Initializes a new instance of <see cref="InvalidByteException"/>.
        /// </summary>
        /// <param name="_byte">The invalid byte.</param>
        /// <param name="_message">Descriptive error message.</param>
        public InvalidByteException(byte _byte, string _message)
           : base(_message)
        {
            m_byte = _byte;
        }

#pragma warning disable SYSLIB0051
        protected InvalidByteException(SerializationInfo info,
                                        StreamingContext context)
           : base(info, context)
        {
            info.AddValue("Byte", m_byte);
        }
#pragma warning restore SYSLIB0051
    }
}

