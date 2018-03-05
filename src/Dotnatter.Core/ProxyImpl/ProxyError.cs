using System;
using System.Runtime.Serialization;

namespace Dotnatter.Core.ProxyImpl
{
    public class ProxyError : ApplicationException
    {
        public ProxyError()
        {
        }

        public ProxyError(string message) : base(message)
        {
        }

        public ProxyError(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected ProxyError(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
