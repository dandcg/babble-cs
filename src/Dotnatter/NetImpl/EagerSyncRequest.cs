using Dotnatter.HashgraphImpl.Model;

namespace Dotnatter.NetImpl
{
    public class EagerSyncRequest
    {
        public string From { get; set; }
        public WireEvent[] Events { get; set; }

    }
}