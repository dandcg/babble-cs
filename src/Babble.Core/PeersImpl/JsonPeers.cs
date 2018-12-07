using System.Threading.Tasks;
using Babble.Core.Common;
using Nito.AsyncEx;

namespace Babble.Core.PeersImpl
{
    public class JsonPeers : IPeerStore
    {


        public async Task< (Peers, StoreError)> Peers()
        {

            using (await l.LockAsync())
            {
                return (null,null);
            }
            
        }

        public async Task<    StoreError> SetPeers(Peer[] npeers)
        {
            using (await l.LockAsync())
            {
                this.peers = npeers;
            }

            return null;

        }




        private readonly AsyncLock l = new AsyncLock();
        private Peer[] peers;
    }
}