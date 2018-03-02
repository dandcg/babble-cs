using System.Collections.Generic;

namespace Dotnatter.Common
{
    public class RollingIndexMap<T>
    {
        public int Size { get; }
        public int[] Keys { get; }

        public Dictionary<int, RollingIndex<T>> Mapping { get; private set; }

        public RollingIndexMap(int size, int[] keys)
        {
            var items = new Dictionary<int, RollingIndex<T>>();
            foreach (var key in keys)
            {
                items[key] = new RollingIndex<T>(size);
            }

            Size = size;
            Keys = keys;
            Mapping = items;
        }

        //return key items with index > skip
        public (T[], StoreError) Get(int key, int skipIndex)

        {
            var ok = Mapping.TryGetValue(key, out var items);
            if (!ok)
            {
                return (null, new StoreError(StoreErrorType.KeyNotFound, $"{key}"));
            }

            var (cached, err) = items.Get(skipIndex);
            if (err != null)
            {
                return (null, err);
            }

            return (cached, null);
        }

        public (T, StoreError) GetItem(int key, int index)
        {
            return Mapping[key].GetItem(index);
        }

        public (T, StoreError) GetLast(int key)
        {
            var ok = Mapping.TryGetValue(key, out var pe);

            if (!ok)
            {
                return (default, new StoreError(StoreErrorType.KeyNotFound, $"{key}"));
            }

            var (cached, _) = pe.GetLastWindow();
            if (cached.Length == 0)
            {
                return (default, null);
            }

            return (cached[cached.Length - 1], null);
        }

        public StoreError Set(int key, T item, int index)

        {
            var ok = Mapping.TryGetValue(key, out var items);

            if (!ok)
            {
                items = new RollingIndex<T>(Size);
                Mapping[key] = items;
            }

            return items.Set(item, index);
        }

        //returns [key] => lastKnownIndex
        public Dictionary<int, int> Known()
        {
            var known = new Dictionary<int, int>();

            foreach (var mp in Mapping)
            {
                var k = mp.Key;
                var items = mp.Value;

                var (_, lastIndex) = items.GetLastWindow();
                known[k] = lastIndex;
            }

            return known;
        }

        public StoreError Reset()
        {
            var items = new Dictionary<int, RollingIndex<T>>();
            foreach (var key in Keys)
            {
                items[key] = new RollingIndex<T>(Size);
            }

            Mapping = items;
            return null;
        }
    }
}