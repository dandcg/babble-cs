using System;
using System.Runtime.Serialization;

namespace Dotnatter.HashgraphImpl
{
    public class NodeError : ApplicationException
    {
        public NodeError()
        {
        }

        public NodeError(string message) : base(message)
        {
        }

        public NodeError(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected NodeError(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
