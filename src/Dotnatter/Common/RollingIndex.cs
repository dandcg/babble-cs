using System;
using System.Collections.Generic;
using System.Linq;

namespace Dotnatter.Common
{
    public class RollingIndex<T>
    {
        private int size { get; }
        private int lastIndex { get; set; }
        private List<T> items { get; } = new List<T>();

        public RollingIndex(int size)
        {
            this.size = size;
            items.Capacity = 2 * size;
            lastIndex = -1;
        }

        public (T[] lastWindow, int lastIndex) GetLastWindow()
        {
            return (items.ToArray(), lastIndex);
        }

        public T[] Get(int skipIndex)
        {
            var res = new T[0];

            if (skipIndex > lastIndex)
            {
                return res;
            }

            var cachedItems = items.Count;
            //assume there are no gaps between indexes
            var oldestCachedIndex = lastIndex - cachedItems + 1;
            if (skipIndex + 1 < oldestCachedIndex)
            {
                throw new StoreError(StoreErrorType.TooLate);
            }

            //index of 'skipped' in RollingIndex
            var start = skipIndex - oldestCachedIndex + 1;

            return items.Skip(start).ToArray();
        }

        public T GetItem(int index)
        {
            var itemCount = items.Count;

            var oldestCached = lastIndex - itemCount + 1;

            if (index < oldestCached)
            {
                throw new StoreError(StoreErrorType.TooLate);
            }
            var findex = index - oldestCached;
            if (findex >= itemCount)
            {
                throw new StoreError(StoreErrorType.KeyNotFound);
         }
            return items[findex];
        }

        public void Add(T item, int index)
        {
            //Console.WriteLine("{0},{1}", item, index);

            if (index <= lastIndex)
            {
                throw new StoreError(StoreErrorType.PassedIndex, $"{index}");
            }


            if (lastIndex >= 0 && index > lastIndex + 1)
            {
                throw new StoreError(StoreErrorType.SkippedIndex, $"{index}");
            }

            if (items.Count >= 2 * size)
            {
                Roll();
            }

            items.Add(item);

            lastIndex = index;
        }

        public void Roll()
        {
            //Console.WriteLine("Roll");

            items.RemoveRange(0, size);
        }
    }
}