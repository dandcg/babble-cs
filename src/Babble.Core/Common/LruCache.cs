using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Babble.Core.Util;
using Serilog;

namespace Babble.Core.Common
{

    public class LruCache<TKey, TValue>
    {
        private readonly Dictionary<TKey, CacheNode> entries;
        private readonly int capacity;
        private CacheNode head;
        private CacheNode tail;
        private int count;
        private readonly bool refreshEntries;
        private readonly Action<TKey, TValue> evictAction;
        private object logger;
        
        public LruCache(int size, Action<TKey, TValue> evictAction, ILogger logger, string instanceName = null)
        {
            this.capacity = size;
            this.evictAction = evictAction;
            this.logger = logger.AddNamedContext("LruCache", instanceName);
            this.entries = new Dictionary<TKey, CacheNode>(this.capacity);
            this.head = null;
            this.tail = null;
            this.count = 0;
            this.refreshEntries = true;
        }

        private class CacheNode
        {
            public CacheNode Next { get; set; }
            public CacheNode Prev { get; set; }
            public TKey Key { get; set; }
            public TValue Value { get; set; }
            public DateTime LastAccessed { get; set; }
        }

        /// <summary>
        /// Gets the current number of entries in the cache.
        /// </summary>
        public int Count => entries.Count;

        public int Len()
        {
            return entries.Count;

        }

        /// <summary>
        /// Gets the maximum number of entries in the cache.
        /// </summary>
        public int Capacity => this.capacity;

        /// <summary>
        /// Gets whether or not the cache is full.
        /// </summary>
        public bool IsFull => this.count == this.capacity;

        /// <summary>
        /// Gets the item being stored.
        /// </summary>
        /// <returns>The cached value at the given key.</returns>
        public (TValue value, bool success) Get(TKey key)
        {
            CacheNode entry;
            var value = default(TValue);

            if (!this.entries.TryGetValue(key, out entry))
            {
                return (value,false);
            }

            if (this.refreshEntries)
            {
                MoveToHead(entry);
            }

            lock (entry)
            {
                value = entry.Value;
            }

            return (value,true);
        }


        /// <summary>
        /// Sets the item being stored to the supplied value.
        /// </summary>
        /// <param name="key">The cache key.</param>
        /// <param name="value">The value to set in the cache.</param>
        /// <returns>True if the set was successful. False otherwise.</returns>
        public bool Add(TKey key, TValue value)
        {
            var evict = false;
            CacheNode entry;
            if (!this.entries.TryGetValue(key, out entry))
            {
                // Add the entry
                lock (this)
                {
                    if (!this.entries.TryGetValue(key, out entry))
                    {
                        if (this.IsFull)
                        {
                            // Re-use the CacheNode entry
                            entry = this.tail;
                            entries.Remove(this.tail.Key);

                            evictAction?.Invoke(entry.Key, entry.Value);

                            // Reset with new values
                            entry.Key = key;
                            entry.Value = value;
                            entry.LastAccessed = DateTime.UtcNow;

                        evict=true;

                            // Next and Prev don't need to be reset.
                            // Move to front will do the right thing.
                        }
                        else
                        {
                            this.count++;
                            entry = new CacheNode()
                            {
                                Key = key,
                                Value = value,
                                LastAccessed = DateTime.UtcNow
                            };
                        }
                        entries.Add(key, entry);
                    }
                }
            }
            else
            {
                // If V is a nonprimitive Value type (struct) then sets are
                // not atomic, therefore we need to lock on the entry.
                lock (entry)
                {
                    entry.Value = value;
                }
            }

            MoveToHead(entry);

            // We don't need to lock here because two threads at this point
            // can both happily perform this check and set, since they are
            // both atomic.
            if (null == this.tail)
            {
                this.tail = this.head;
            }

            return evict;
        }

        /// <summary>
        /// Removes the stored data.
        /// </summary>
        /// <returns>True if the removal was successful. False otherwise.</returns>
        public bool Purge()
        {
            lock (this)
            {
                this.entries.Clear();
                this.head = null;
                this.tail = null;
                return true;
            }
        }

        /// <summary>
        /// Moved the provided entry to the head of the list.
        /// </summary>
        /// <param name="entry">The CacheNode entry to move up.</param>
        private void MoveToHead(CacheNode entry)
        {
            if (entry == this.head)
            {
                return;
            }

            // We need to lock here because we're modifying the entry
            // which is not thread safe by itself.
            lock (this)
            {
                RemoveFromLl(entry);
                AddToHead(entry);
            }
        }

  
        private void AddToHead(CacheNode entry)
        {
            entry.Prev = null;
            entry.Next = this.head;

            if (null != this.head)
            {
                this.head.Prev = entry;
            }

            this.head = entry;
        }

        private void RemoveFromLl(CacheNode entry)
        {
            var next = entry.Next;
            var prev = entry.Prev;

            if (null != next)
            {
                next.Prev = entry.Prev;
            }
            if (null != prev)
            {
                prev.Next = entry.Next;
            }

            if (this.head == entry)
            {
                this.head = next;
            }

            if (this.tail == entry)
            {
                this.tail = prev;
            }

        }

        private void Remove(CacheNode entry)
        {
            lock (this)
            {
                // Only to be called while locked from Purge
                RemoveFromLl(entry);
                entries.Remove(entry.Key);

                evictAction?.Invoke(entry.Key, entry.Value);
                this.count--;
            }
        }

        public IEnumerable<TKey> Keys()
        {
            var keys = new TKey[entries.Count];
            var i = 0;
            var ent = tail;
            while (ent!=null)
            {
                keys[i] = ent.Key;
                ent = ent.Prev;
                i++;
            }

            return keys;
        }

        public bool Remove(TKey key)
        {
            lock (this)
            {
                if (entries.TryGetValue(key, out var cn))
                {
                    RemoveFromLl(cn);

                    // Remove from cache
                    return entries.Remove(key);
                }

                return false;
            }
        }


        // RemoveOldest removes the oldest item from the cache.

        public (TKey, TValue, bool success) RemoveOldest()
        {
            lock (this)
            {
                var ent = tail;
                if (ent != null)
                {
                    RemoveFromLl(ent);
                    return (ent.Key, ent.Value, true);
                }

                return (default(TKey), default(TValue), false);
            }
        }

    
        public (TKey key, TValue value, bool success) GetOldest()
        {
            // Remove from LRUPriority
            var ent = tail;

            if (ent != null)
            {
                return (ent.Key,ent.Value, true);
            }

            return (default, default, false);
        }


        public bool Contains(TKey key)
        {
            return entries.ContainsKey(key);
        }



        public (TValue, bool success) Peek(TKey key)
        {
            if (entries.TryGetValue(key, out var node))
            {
                var value = node.Value;
                return (value, true);
            }

            return (default(TValue), false);
        }


    }
}