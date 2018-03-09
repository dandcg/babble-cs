using System.Collections.Generic;

namespace Dotnatter.Core.NetImpl
{
    public class SyncRequest
    {

        public int FromId { get; set; }
        public Dictionary<int, int> Known { get; set; }
    }
}