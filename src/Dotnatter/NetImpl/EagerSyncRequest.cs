using Dotnatter.HashgraphImpl.Model;

namespace Dotnatter.NetImpl
{
    public class EagerSyncRequest
    {
        public int FromId { get; set; }
        public WireEvent[] Events { get; set; }

    }
}