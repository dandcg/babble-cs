using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace Dotnatter.Util
{
    public class AsyncChannel<T>
    {
        private readonly AsyncProducerConsumerQueue<T> buffer;

        public AsyncChannel() : this(1)
        {
        }

        public AsyncChannel(int size)
        {
            buffer = new AsyncProducerConsumerQueue<T>(size);
        }

        public async Task<bool> Send(T t)
        {
            try
            {
                await buffer.EnqueueAsync(t);
            }
            catch (InvalidOperationException)
            {
                // will be thrown when the collection gets closed
                return false;
            }

            return true;
        }

        public async Task<(T val, bool ok)> Receive()
        {
            T val;
            try
            {
                val = await buffer.DequeueAsync();
            }
            catch (InvalidOperationException)
            {
                // will be thrown when the collection is empty and got closed
                val = default;
                return (val, false);
            }

            return (val, true);
        }

        public void Close()
        {
            buffer.CompleteAdding();
        }

        public async Task<IEnumerable<T>> Range()
        {
            var l = new List<T>();
            bool ok = true;
            while (ok)
            {
                T val;
                (val, ok) = await Receive();
                l.Add(val);
            }

            return l;
        }
    }
}