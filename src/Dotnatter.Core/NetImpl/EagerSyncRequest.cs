using Dotnatter.Core.HashgraphImpl.Model;

namespace Dotnatter.Core.NetImpl
{
    public class EagerSyncRequest
    {
        public int FromId { get; set; }
        public WireEvent[] Events { get; set; }

    }
}