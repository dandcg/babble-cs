using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Dotnatter.Util
{
    public class Channel<T>
    {
        private readonly BlockingCollection<T> buffer;

        public Channel() : this(1) { }
        public Channel(int size)
        {
            buffer = new BlockingCollection<T>(new ConcurrentQueue<T>(), size);
        }

        public bool Send(T t)
        {
            try
            {
                buffer.Add(t);
            }
            catch (InvalidOperationException)
            {
                // will be thrown when the collection gets closed
                return false;
            }
            return true;
        }

        public bool Receive(out T val)
        {
            try
            {
                val = buffer.Take();
            }
            catch (InvalidOperationException)
            {
                // will be thrown when the collection is empty and got closed
                val = default(T);
                return false;
            }
            return true;
        }

        public void Close()
        {
            buffer.CompleteAdding();
        }

        public IEnumerable<T> Range()
        {
            while (Receive(out var val))
            {
                yield return val;
            }
        }
    }
}
