using System;
using System.Runtime.Serialization;

namespace Babble.Core.NodeImpl
{
    public class CoreError : BabbleError
    {
        public CoreError(string message = null, Exception innerException = null)
        {
            Message = message;
            InnerException = innerException;
        }
    }
}