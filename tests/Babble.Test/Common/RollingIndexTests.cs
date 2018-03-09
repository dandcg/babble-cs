using System.Collections.Generic;
using System.Linq;
using Babble.Core.Common;
using Babble.Test.Helpers;
using Xunit;

namespace Babble.Test.Common
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

                rollingIndex.Set(item, i);

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

            var err = rollingIndex.Set("ErrSkippedIndex", expectedLastIndex + 2);
            Assert.Equal(StoreErrorType.SkippedIndex, err.StoreErrorType);

            (_, err) = rollingIndex.GetItem(9);
            Assert.Equal(StoreErrorType.TooLate, err.StoreErrorType);

            var indexes = new[] {10, 17, 29};
            foreach (var id in indexes)
            {
                (item, err) = rollingIndex.GetItem(id);

                Assert.Null(err);

                item.ShouldCompareTo(items[id]);
            }

            (_, err) = rollingIndex.GetItem(lastIndex + 1);
            Assert.Equal(StoreErrorType.KeyNotFound, err.StoreErrorType);

            //Test updating an item in place
            var updateIndex = 26;
            var updateValue = "Updated Item";

            err = rollingIndex.Set(updateValue, updateIndex);
            Assert.Null(err);

            (item, err) = rollingIndex.GetItem(updateIndex);
            Assert.Null(err);

            Assert.Equal(updateValue, item);
        }

        [Fact]
        public void TestRollingIndexSkip()
        {
            const int size = 10;

            const int testSize = 25;

            var rollingIndex = new RollingIndex<string>(size);

            var ( _, err) = rollingIndex.Get(-1);
            Assert.Null(err);

            var items = new List<string>();
            int i;

            for (i = 0; i < testSize; i++)
            {
                var item = $"item {i}";

                rollingIndex.Set(item, i);

                items.Add(item);
            }

            (_, err) = rollingIndex.Get(0);
            Assert.Equal(StoreErrorType.TooLate, err.StoreErrorType);

            // 1

            var skipIndex1 = 9;

            var expected1 = items.Skip(skipIndex1 + 1).ToArray();

            var (cached1, err1) = rollingIndex.Get(skipIndex1);
            Assert.Null(err1);

            var convertedItems = new List<string>();

            foreach (var it in cached1)
            {
                convertedItems.Add(it);
            }

            convertedItems.ToArray().ShouldCompareTo(expected1);

            // 2

            var skipIndex2 = 15;

            var expected2 = items.Skip(skipIndex2 + 1).ToArray();

            var (cached2, err2) = rollingIndex.Get(skipIndex2);
            Assert.Null(err2);

            convertedItems = new List<string>();
            foreach (var it in cached2)
            {
                convertedItems.Add(it);
            }

            convertedItems.ToArray().ShouldCompareTo(expected2);

            // 3

            var skipIndex3 = 27;

            string[] expected3 = { };
            var (cached3, err3) = rollingIndex.Get(skipIndex3);

            Assert.Null(err3);

            convertedItems = new List<string>();
            foreach (var it in cached3)
            {
                convertedItems.Add(it);
            }

            convertedItems.ToArray().ShouldCompareTo(expected3);
        }
    }
}