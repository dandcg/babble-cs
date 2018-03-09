using System;
using System.Runtime.Serialization;

namespace Babble.Core.NetImpl
{
    public class NetError : ApplicationException
    {
        public NetError()
        {
        }

        public NetError(string message) : base(message)
        {
        }

        public NetError(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected NetError(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
