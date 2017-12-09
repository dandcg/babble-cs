using System.Collections.Generic;
using System.Linq;
using Dotnatter.Common;
using Dotnatter.Test.Helpers;
using Xunit;

namespace Dotnatter.Test.Common
{
    public class RollingIndexTests
    {
        [Fact]
        public void TestRollingIndex()
        {
            const int size = 10;

            const int testSize = 3 * size;

            var rollingIndex = new RollingIndex<string>(size);

            var items = new List<string>();
            int i;

            string item;
            for (i = 0; i < testSize; i++)
            {
                item = $"item {i}";

                rollingIndex.Add(item, i);

                items.Add(item);
            }
            var (cached, lastIndex) = rollingIndex.GetLastWindow();

            var expectedLastIndex = testSize - 1;

            // last index
            Assert.Equal(expectedLastIndex, lastIndex);

            var start = testSize / (2 * size) * size;
            var count = testSize - start;

            for (i = 0; i < count; i++)
            {
                //Console.WriteLine("{0},{1}", items[start + i], cached[i]);
                Assert.Equal(items[start + i], cached[i]);
            }


            var ex = Assert.Throws<StoreError>(() => rollingIndex.Add("PassedIndex", expectedLastIndex - 1));
            Assert.Equal(StoreErrorType.PassedIndex, ex.StoreErrorType);


            ex = Assert.Throws<StoreError>(() => rollingIndex.Add("PassedIndex", expectedLastIndex + 2));
            Assert.Equal(StoreErrorType.SkippedIndex, ex.StoreErrorType);


            ex = Assert.Throws<StoreError>(() => rollingIndex.GetItem(9));
            Assert.Equal(StoreErrorType.TooLate, ex.StoreErrorType);


            var indexes = new[] {10, 17, 29};
            foreach (var id in indexes)
            {
                item = rollingIndex.GetItem(id);

                item.ShouldCompareTo(items[id]);
            }


            ex = Assert.Throws<StoreError>(() => rollingIndex.GetItem(lastIndex + 1));
            Assert.Equal(StoreErrorType.KeyNotFound, ex.StoreErrorType);
        }


        [Fact]
        public void TestRollingIndexSkip()
        {
            const int size = 10;

            const int testSize = 25;

            var rollingIndex = new RollingIndex<string>(size);

            var items = new List<string>();
            int i;

            for (i = 0; i < testSize; i++)
            {
                var item = $"item {i}";

                rollingIndex.Add(item, i);

                items.Add(item);
            }


            var ex = Assert.Throws<StoreError>(() => rollingIndex.Get(0));
            Assert.Equal(StoreErrorType.TooLate, ex.StoreErrorType);

            // 1

            var skipIndex1 = 9;

            var expected1 = items.Skip(skipIndex1 + 1).ToArray();

            var cached1 = rollingIndex.Get(skipIndex1);

            var convertedItems = new List<string>();

            foreach (var it in cached1)
            {
                convertedItems.Add(it);
            }
            convertedItems.ToArray().ShouldCompareTo(expected1);

            // 2

            var skipIndex2 = 15;

            var expected2 = items.Skip(skipIndex2 + 1).ToArray();

            var cached2 = rollingIndex.Get(skipIndex2);
            
            convertedItems = new List<string>();
            foreach (var it in cached2)
            {
                convertedItems.Add(it);
            }
            convertedItems.ToArray().ShouldCompareTo(expected2);

            // 3

            var skipIndex3 = 27;

            string[] expected3 = { };
            var cached3 = rollingIndex.Get(skipIndex3);
            
            convertedItems = new List<string>();
            foreach (var it in cached3)
            {
                convertedItems.Add(it);
            }
            convertedItems.ToArray().ShouldCompareTo(expected3);
        }
    }
}