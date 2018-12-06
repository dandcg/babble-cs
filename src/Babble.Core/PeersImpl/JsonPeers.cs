using System.Threading.Tasks;
using Babble.Core.Common;
using Babble.Core.PeersImpl;
using Nito.AsyncEx;

namespace Babble.Core.NetImpl.PeerImpl
{
    public class JsonPeers : IPeerStore
    {


        public async Task< (Peers, StoreError)> Peers()
        {

            using (await l.LockAsync())
            {
                return peers;
            }
            
        }

        public async Task<    StoreError> SetPeers(Peer[] npeers)
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