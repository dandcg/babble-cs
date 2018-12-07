using System;
using System.Collections.Generic;
using System.Text;

namespace Babble.Core
{
    public class BabbleError
    {
        public string Message { get; protected set; }
        public Exception InnerException { get; protected set; }

        public BabbleError(string message = null, Exception innerException = null)
        {
            Message = message;
            InnerException = innerException;
        }
    }
}
