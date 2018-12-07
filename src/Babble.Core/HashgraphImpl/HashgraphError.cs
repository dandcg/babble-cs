using System;
using System.Runtime.Serialization;

namespace Babble.Core.HashgraphImpl
{
    public class HashgraphError : BabbleError
    {
        public HashgraphError(string message = null, Exception innerException = null)
        {
            Message = message;
            InnerException = innerException;
        }
    }
}
