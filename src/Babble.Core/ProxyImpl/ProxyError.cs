using System;
using System.Runtime.Serialization;

namespace Babble.Core.ProxyImpl
{
    public class ProxyError :  BabbleError
    {
        public ProxyError(string message = null, Exception innerException = null)
        {
            Message = message;
            InnerException = innerException;
        }
    }
}
