using System.Runtime.InteropServices;

namespace Babble.Core.HashgraphImpl.Model
{
    public class EventCoordinates
    {
        public EventCoordinates()
        {
            
        }

        public EventCoordinates(string hash, int index)
        {
            Hash = hash;
            Index = index;
        }


        public string Hash { get; set; }
        public int Index { get; set; }
    }
}