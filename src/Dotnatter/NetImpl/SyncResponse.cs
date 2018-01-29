using System.Collections.Generic;
using Dotnatter.HashgraphImpl.Model;

namespace Dotnatter.NetImpl
{
    public class SyncResponse
    {

        public string From { get; set; }
        public bool SyncLimit{ get; set; }
        public WireEvent[] Events { get; set; }
        public Dictionary<int, int> Known { get; set; }

    }
}