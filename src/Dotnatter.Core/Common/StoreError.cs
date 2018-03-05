using System;
using System.Runtime.Serialization;

namespace Dotnatter.Common
{
    public class StoreError : ApplicationException
    {
        public StoreErrorType StoreErrorType { get; }

        public StoreError(StoreErrorType storeErrorType) : base($"{storeErrorType.ToString()}")
        {
            StoreErrorType = storeErrorType;
        }

        public StoreError(StoreErrorType storeErrorType, string message) : base($"{storeErrorType.ToString()}: {message}")
        {
            StoreErrorType = storeErrorType;
        }

        public StoreError(StoreErrorType storeErrorType, string message, Exception innerException) : base($"{storeErrorType.ToString()}", innerException)
        {
            StoreErrorType = storeErrorType;
        }

        protected StoreError(StoreErrorType storeErrorType, SerializationInfo info, StreamingContext context) : base(info, context)
        {
            StoreErrorType = storeErrorType;
        }
    }
}
