using System.Threading.Tasks;
using Nito.AsyncEx;

namespace Dotnatter.NetImpl.PeerImpl
{
    public class StaticPeers : IPeerStore
    {


        public async Task<Peer[]> Peers()
        {

            using (await l.LockAsync())
            {
                return peers;
            }
            
        }

        public async Task SetPeers(Peer[] npeers)
        {
            using (await l.LockAsync())
            {
                this.peers = npeers;
            }
        }

        private readonly AsyncLock l = new AsyncLock();
        private Peer[] peers;
    }
}