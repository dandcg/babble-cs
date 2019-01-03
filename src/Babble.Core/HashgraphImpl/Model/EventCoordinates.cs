using System;
using System.Runtime.InteropServices;

namespace Babble.Core.HashgraphImpl.Model
{
    public class EventCoordinates:ICloneable
    {
        public EventCoordinates()
        {
            Hash = "";
        }

        public EventCoordinates(string hash, int index)
        {
            Hash = hash;
            Index = index;
        }


        public string Hash { get; set; }
        public int Index { get; set; }
        public object Clone()
        {
            return new EventCoordinates(Hash,Index);
        }
    }
}