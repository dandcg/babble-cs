using Babble.Core.HashgraphImpl.Model;

namespace Babble.Core.NetImpl
{
    public class EagerSyncRequest
    {
        public int FromId { get; set; }
        public WireEvent[] Events { get; set; }

    }
}