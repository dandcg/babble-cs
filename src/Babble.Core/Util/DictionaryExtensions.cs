using System.Collections.Generic;
using System.Linq;

namespace Babble.Core.Util
{
    public static class DictionaryExtensions
    {
        public static Dictionary<T1, T2> Clone<T1, T2>(this Dictionary<T1, T2> dic)
        {
            return dic.ToDictionary(entry => entry.Key,entry => entry.Value);
        }

        public static string GetOrEmpty<T>(this Dictionary<T,string> dic, T key)
        {
            dic.TryGetValue(key, out var val);
            return val ?? "";
        }
    }
}
