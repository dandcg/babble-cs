using System.Threading.Tasks;
using Babble.Core.Common;
using Nito.AsyncEx;

namespace Babble.Core.PeersImpl
{
    public class StaticPeers : IPeerStore
    {


        public async  Task<(Peer[], StoreError)> Peers()
        {

            using (await l.LockAsync())
            {
                return (peers, null);
            }
            
        }

        public async Task<StoreError> SetPeers(Peer[] p)
        {
            using (await l.LockAsync())
            {
                this.peers = p;
            }

            return null;
        }

        private readonly AsyncLock l = new AsyncLock();
        private Peer[] peers;
    }
}