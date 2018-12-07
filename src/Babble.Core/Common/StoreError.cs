using System;

namespace Babble.Core.Common
{
    public class StoreError : BabbleError
    {
        public StoreErrorType StoreErrorType { get; }

        public StoreError(StoreErrorType storeErrorType, string message = null, Exception innerException = null)
        {
            StoreErrorType = storeErrorType;
            Message = message;
            InnerException = innerException;
        }
    }
}