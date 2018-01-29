using System.Collections.Generic;

namespace Dotnatter.NetImpl
{
    public class SyncRequest
    {

        public string From { get; set; }
        public Dictionary<int, int> Known { get; set; }
    }
}