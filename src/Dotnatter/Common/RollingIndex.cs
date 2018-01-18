using System.Collections.Generic;
using System.Linq;

namespace Dotnatter.Common
{
    public class RollingIndex<T>
    {
        public int Size { get; }
        public int LastIndex { get; private set; }
        public List<T> Items { get; private set; } = new List<T>();

        public RollingIndex(int size)
        {
            Size = size;
            Items.Capacity = 2 * size;
            LastIndex = -1;
        }

        public (T[] lastWindow, int lastIndex) GetLastWindow()
        {
            return (Items.ToArray(), LastIndex);
        }

        public (T[] items, StoreError err) Get(int skipIndex)
        {
            var res = new T[] { };

            if (skipIndex > LastIndex)
            {
                return (res, null);
            }

            var cachedItems = Items.Count;

            //assume there are no gaps between indexes
            var oldestCachedIndex = LastIndex - cachedItems + 1;
            if (skipIndex + 1 < oldestCachedIndex)
            {
                return (res, new StoreError(StoreErrorType.TooLate));
            }

            //index of 'skipped' in RollingIndex
            var start = skipIndex - oldestCachedIndex + 1;

            return (Items.Skip(start).ToArray(), null);
        }

        public (T item, StoreError err) GetItem(int index)
        {
            var itemCount = Items.Count;

            var oldestCached = LastIndex - itemCount + 1;

            if (index < oldestCached)
            {
                return (default, new StoreError(StoreErrorType.TooLate));
            }

            var findex = index - oldestCached;
            if (findex >= itemCount)
            {
                return (default, new StoreError(StoreErrorType.KeyNotFound));
            }

            return (Items[findex], null);
        }

        public StoreError Add(T item, int index)
        {
            if (index <= LastIndex)
            {
                return new StoreError(StoreErrorType.PassedIndex, $"{index}");
            }

            if (LastIndex >= 0 && index > LastIndex + 1)
            {
                return new StoreError(StoreErrorType.SkippedIndex, $"{index}");
            }

            if (Items.Count >= 2 * Size)
            {
                Roll();
            }

            Items.Add(item);

            LastIndex = index;

            return null;
        }

        public void Roll()
        {
            var newLsit = new List<T>(2 * Size);
            newLsit.AddRange(Items.Skip(Size));
            Items = newLsit;
        }
    }
}