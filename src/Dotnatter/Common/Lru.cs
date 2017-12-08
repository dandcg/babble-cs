using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Dotnatter.Common
{
    public class LruCache<TKey, TValue>
    {
        private readonly Dictionary<TKey, LinkedListNode<LruCacheItem>> items = new Dictionary<TKey, LinkedListNode<LruCacheItem>>();
        private readonly LinkedList<LruCacheItem> evictList = new LinkedList<LruCacheItem>();

        private readonly int size;
        private readonly Action<TKey, TValue> evictAction;


        // NewLRU constructs an LRU of the given size
        public LruCache(int size, Action<TKey, TValue> evictAction)
        {
            this.size = size;
            this.evictAction = evictAction;
        }

        // Purge is used to completely clear the cache
        [MethodImpl(MethodImplOptions.Synchronized)]
        public void Purge()
        {
            foreach (var i in items.ToArray())
            {
                evictAction?.Invoke(i.Key, i.Value.Value.Value);
                items.Remove(i.Key, out var _);
            }

            evictList.Clear();
        }


        // Add adds a value to the cache.  Returns true if an eviction occurred.
        [MethodImpl(MethodImplOptions.Synchronized)]
        public bool Add(TKey key, TValue value)
        {
            var flag = false;
            if (items.Count >= size)
            {
                RemoveFirst();
                flag = true;
            }

            var cacheItem = new LruCacheItem(key, value);
            var node = new LinkedListNode<LruCacheItem>(cacheItem);
            evictList.AddLast(node);
            items.TryAdd(key, node);
            return flag;
        }


        public (TValue value, bool success) Get(TKey key)
        {
            if (items.TryGetValue(key, out var node))
            {
                var value = node.Value.Value;
                evictList.Remove(node);
                evictList.AddLast(node);
                return (value, true);
            }
            return (default(TValue), false);
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
            return (default(TValue), false);
        }


        // Remove removes the provided key from the cache, returning if the
        // key was contained.
        [MethodImpl(MethodImplOptions.Synchronized)]
        public bool Remove(TKey key)
        {
            var node = evictList.FirstOrDefault(w => w.Key.Equals(key));
            evictList.Remove(node);

            // Remove from cache
            return items.Remove(key);
        }


        // RemoveOldest removes the oldest item from the cache.
        private void RemoveFirst()
        {
            // Remove from LRUPriority
            var node = evictList.FirstOrDefault();

            if (node != null)
            {
                evictList.RemoveFirst();
                items.Remove(node.Key);
                evictAction?.Invoke(node.Key, node.Value);
            }
        }


        // GetOldest returns the oldest entry
        [MethodImpl(MethodImplOptions.Synchronized)]
        public (TKey key, TValue value, bool success) GetOldest()
        {
            // Remove from LRUPriority
            var node = evictList.FirstOrDefault();

            if (node != null)
            {
                return (node.Key, node.Value, true);
            }
            return (default(TKey), default(TValue), false);
        }


        // Len returns the number of items in the cache.
        [MethodImpl(MethodImplOptions.Synchronized)]
        public int Len()
        {
            return evictList.Count;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public IEnumerable<TKey> Keys()
        {
            return items.Keys;
        }


        private class LruCacheItem
        {
            public LruCacheItem(TKey k, TValue v)
            {
                Key = k;
                Value = v;
            }

            public TKey Key { get; }
            public TValue Value { get; }
        }

    }


}