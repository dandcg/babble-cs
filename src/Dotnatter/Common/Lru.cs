using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Dotnatter.Common
{
    public class LruCache<TKey, TValue>
    {
        private readonly ConcurrentDictionary<TKey, LruNode> items = new ConcurrentDictionary<TKey, LruNode>();
        private readonly ConcurrentQueue<KeyValuePair<TKey, long>> evictList = new ConcurrentQueue<KeyValuePair<TKey, long>>();
        private readonly int size;
        private readonly Action<TKey, TValue> evictAction;
        private long ticks;


        // NewLRU constructs an LRU of the given size
        public LruCache(int size, Action<TKey, TValue> evictAction)
        {
            this.size = size;
            this.evictAction = evictAction;
        }

        // Purge is used to completely clear the cache
        public void Purge()
        {
            foreach (var k in items)
            {
                evictAction?.Invoke(k.Key, k.Value.Value);
                items.Remove(k.Key, out var _);
            }

            evictList.Clear();
        }


        // Add adds a value to the cache.  Returns true if an eviction occurred.
        public bool Add(TKey key, TValue value)
        {
            var flag = false;
            if (items.ContainsKey(key))
            {
                items[key].Value = value;
            }
            else if (items.Count >= size)
            {
                while (evictList.Count > 0)
                {
                    
                    if (evictList.TryDequeue(out var lastEntry))
                    {

                        if (!items.ContainsKey(lastEntry.Key))
                        {
                            continue;
                        }

                        if (lastEntry.Value != items[lastEntry.Key].TimeStamp)
                        {
                            continue;
                        }

                        items.Remove(lastEntry.Key, out var _);
                        items.TryAdd(key, new LruNode(value));
                        evictAction?.Invoke(key, value);
                        flag = true;
                        break;
                    }
                }
            }
            else
            {
                items.TryAdd(key, new LruNode(value));
            }
            items[key].TimeStamp = ticks++;
            evictList.Enqueue(new KeyValuePair<TKey, long>(key, items[key].TimeStamp));
            return flag;
        }


        public (TValue value, bool success) Get(TKey key)
        {
            if (items.ContainsKey(key))
            {
                items[key].TimeStamp = ticks++;
                evictList.Enqueue(new KeyValuePair<TKey, long>(key, items[key].TimeStamp));
                return (items[key].Value,true);
            }
            return (default(TValue),false);
        }


        // Check if a key is in the cache, without updating the recent-ness
        // or deleting it for being stale.
        public bool Contains(TKey key)
        {
            return items.ContainsKey(key);
        }

        // Returns the key value (or undefined if not found) without updating
        // the "recently used"-ness of the key.
        public (TValue,bool success) Peek(TKey key)
        {
            if (items.ContainsKey(key))
            {
                return (items[key].Value,true);
            }
            return (default(TValue),false);
        }


        // Remove removes the provided key from the cache, returning if the
        // key was contained.
        public bool Remove(TKey key)
        {
            return items.TryRemove(key, out var _);
        }


        // RemoveOldest removes the oldest item from the cache.
        public (TKey key, TValue value, bool success) RemoveOldest()
        {
            if (evictList.TryDequeue(out var ent))
            {
                items.TryRemove(ent.Key, out var r);

                return (ent.Key, r.Value, true);
            }


            return (default(TKey), default(TValue), false);
        }

        // GetOldest returns the oldest entry
        public (TKey key, TValue value, bool success) GetOldest()
        {
            if (evictList.TryPeek(out var ent))
            {
                if (items.TryGetValue(ent.Key, out var r))
                {
                    return (ent.Key, r.Value, true);
                }
                return (ent.Key, default(TValue), true);
            }


            return (default(TKey), default(TValue), false);
        }


        // Len returns the number of items in the cache.
        public int Len()
        {
            return evictList.Count;
        }


        public IEnumerable<TKey> Keys => items.Keys;


        public IEnumerable<TValue> Values
        {
            get
            {
                IEnumerable<LruNode> valueList = items.Values;
                foreach (var node in valueList)
                {
                    yield return node.Value;
                }
            }
        }


        private class LruNode
        {
            public TValue Value;
            public long TimeStamp;

            public LruNode(TValue value)
            {
                Value = value;
            }
        }
    }
}