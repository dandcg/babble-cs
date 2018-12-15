using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Babble.Core.Util;
using Serilog;

namespace Babble.Core.Common
{
    public class LruCache<TKey, TValue>
    {
        private readonly Dictionary<TKey, LinkedListNode<LruCacheItem>> items = new Dictionary<TKey, LinkedListNode<LruCacheItem>>();
        private readonly LinkedList<LruCacheItem> evictList = new LinkedList<LruCacheItem>();
        private readonly int size;
        private readonly Action<TKey, TValue> evictAction;
        private readonly ILogger logger;

        // NewLRU constructs an LRU of the given size
        public LruCache(int size, Action<TKey, TValue> evictAction, ILogger logger, string instanceName = null)
        {
            this.size = size;
            this.evictAction = evictAction;
            this.logger = logger.AddNamedContext("LruCache", instanceName);
        }

        // Purge is used to completely clear the cache
        [MethodImpl(MethodImplOptions.Synchronized)]
        public void Purge()
        {
            foreach (var i in items.ToArray())
            {
                evictAction?.Invoke(i.Key, i.Value.Value.Value);
                items.Remove(i.Key);
            }

            evictList.Clear();
        }

        // Set adds a value to the cache.  Returns true if an eviction occurred.
        [MethodImpl(MethodImplOptions.Synchronized)]
        public bool Add(TKey key, TValue value)
        {

            var ok = items.TryGetValue(key, out var ent1);

            if (ok)
            {
                evictList.Remove(ent1);
                evictList.AddFirst(ent1);
                ent1.Value.Value = value;
            }

            // Add new item
            var ent = new LruCacheItem(key, value);
            var node = new LinkedListNode<LruCacheItem>(ent);
            evictList.AddFirst(node);
            items[key] = node;

            //logger.Debug("add key={key}, value={value}", key,value);

            var evict = evictList.Count() > size;
            // Verify size not exceeded
            if (evict)
            {
                removeOldest();
            }

            return evict;
        }

        public (TValue value, bool success) Get(TKey key)
        {
            var ok = items.TryGetValue(key, out var ent);
            if (ok)
            {
                evictList.Remove(ent);
                evictList.AddFirst(ent);
                return (ent.Value.Value, true);
            }

            return (default, false);
        }

        // Check if a key is in the cache, without updating the recent-ness
        // or deleting it for being stale.
        [MethodImpl(MethodImplOptions.Synchronized)]
        public bool Contains(TKey key)
        {
            return items.ContainsKey(key);
        }

        // Returns the key value (or undefined if not found) without updating
        // the "recently used"-ness of the key.
        [MethodImpl(MethodImplOptions.Synchronized)]
        public (TValue, bool success) Peek(TKey key)
        {
            if (items.TryGetValue(key, out var node))
            {
                var value = node.Value.Value;
                return (value, true);
            }

            return (default, false);
        }

        // Remove removes the provided key from the cache, returning if the
        // key was contained.
        [MethodImpl(MethodImplOptions.Synchronized)]
        public bool Remove(TKey key)
        {
            var node = evictList.LastOrDefault(w => w.Key.Equals(key));
            evictList.Remove(node);

            // Remove from cache
            return items.Remove(key);
        }


        // RemoveOldest removes the oldest item from the cache.
        [MethodImpl(MethodImplOptions.Synchronized)]
        public (TKey, TValue, bool success) RemoveOldest()
        {
            var ent = evictList.Last;
            if (ent != null)
            {
                RemoveElement(ent);
                var kv = ent.Value;
                return (kv.Key, kv.Value, true);
            }

            return (default, default, false);
        }

        // GetOldest returns the oldest entry
        [MethodImpl(MethodImplOptions.Synchronized)]
        public (TKey key, TValue value, bool success) GetOldest()
        {
            // Remove from LRUPriority
            var node = evictList.LastOrDefault();

            if (node != null)
            {
                return (node.Key, node.Value, true);
            }

            return (default, default, false);
        }

        // Len returns the number of items in the cache.
        [MethodImpl(MethodImplOptions.Synchronized)]
        public int Len()
        {
            return evictList.Count;
        }

        // removeOldest removes the oldest item from the cache.
        private void removeOldest()
        {
            var ent = evictList.Last;

            
            //logger.Debug("remove key={key}, value={value}", ent.Value.Key,ent.Value.Value);

            if (ent != null)
            {
                RemoveElement(ent);
            }
        }

        // removeElement is used to remove a given list element from the cache
        private void RemoveElement(LinkedListNode<LruCacheItem> e)
        {
            evictList.Remove(e);
            var kv = e.Value;
            
            items.Remove(kv.Key);
            evictAction?.Invoke(kv.Key, kv.Value);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public IEnumerable<TKey> Keys()
        {
            var keys = new TKey[items.Count];
            var i = 0;
            var ent = evictList.Last;
            while (ent!=null)
            {
                keys[i] = ent.Value.Key;
                ent = ent.Previous;
                i++;
            }

            return keys;
        }

        private class LruCacheItem
        {
            public LruCacheItem(TKey k, TValue v)
            {
                Key = k;
                Value = v;
            }

            public TKey Key { get; }
            public TValue Value { get; set; }
        }
    }
}