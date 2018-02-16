using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Dotnatter.Util
{
    public static class DictionaryExtensions
    {

        public static Dictionary<T1, T2> Clone<T1, T2>(this Dictionary<T1, T2> dic)
        {
            return dic.ToDictionary(entry => entry.Key,entry => entry.Value);
        }

    }
}
