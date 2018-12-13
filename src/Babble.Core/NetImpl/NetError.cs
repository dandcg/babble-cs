using System;
using System.Runtime.Serialization;

namespace Babble.Core.NetImpl
{
    public class NetError : BabbleError
    {
        public BabbleError InnerError { get; private set; }

        public NetError(string message = null, BabbleError innerError=null, Exception innerException = null)
        {
            this.InnerError = innerError;
            Message = message;
            InnerException = innerException;
        }
    }
}
