using System.Collections.Generic;

namespace Babble.Core.HashgraphImpl
{
    public static class ParticipantEventsCacheExtensions
    {
        public static int[] GetValues(this Dictionary<string, int> mapping)
        {
            var keys = new List<int>();
         
            foreach (var m in mapping)
            {
                keys.Add(m.Value);
               
            }

            return keys.ToArray();
        }
    }
}