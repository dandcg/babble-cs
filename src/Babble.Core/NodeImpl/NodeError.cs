using System;
using System.Runtime.Serialization;

namespace Babble.Core.NodeImpl
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



    public class CoreError : ApplicationException
    {
        public CoreError()
        {
        }

        public CoreError(string message) : base(message)
        {
        }

        public CoreError(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected CoreError(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }


}
