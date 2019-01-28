using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Babble.Core.Util
{
    public static class IterationHelpers
    {
        public static void ForRange<T>(IEnumerable<T> collection, Action<int, T> action)
        {
            int i = 0;
            foreach (var t in collection)
            {
                action(i, t);
                i++;
            }
        }

        public static async Task ForRange<T>(IEnumerable<T> collection, Func<int, T, Task> action)
        {
            int i = 0;
            foreach (var t in collection)
            {
                await action(i, t);
                i++;
            }
        }

        public static void ForRangeMap<TKey, TValue>(Dictionary<TKey, TValue> dictionary, Action<TKey, TValue> action)
        {
            foreach (var t in dictionary)
            {
                action(t.Key, t.Value);
            }
        }

        public static async Task ForRangeMap<TKey, TValue>(Dictionary<TKey, TValue> dictionary, Func<TKey, TValue, Task> action)
        {
            foreach (var t in dictionary)
            {
                await action(t.Key, t.Value);
            }
        }
    }
}