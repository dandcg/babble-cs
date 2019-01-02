using System;
using System.Threading.Tasks;
using Babble.Core.PeersImpl;

namespace Babble.Core.NodeImpl.PeerSelector
{
    public class RandomPeerSelector:IPeerSelector
    {
        private readonly Peers peers;
        private readonly string localAddr;
        private string last;

        public RandomPeerSelector(Peers peers, string localAddr)
        {
            this.peers = peers;
            this.localAddr = localAddr;

        }

        public Peers Peers()
        {
            return peers;
        }


        public async Task<Peer> Next()
        {
            var selectablePeers =await peers.ToPeerSlice();
            if (selectablePeers.Length > 1)
            {
                (_, selectablePeers) = Peer.ExcludePeer(selectablePeers, localAddr);

                if (selectablePeers.Length > 1)
                {
                    (_,selectablePeers) = Peer.ExcludePeer(selectablePeers, last);
                }
            }
            Random rnd = new Random();
            var i = rnd.Next(0, selectablePeers.Length); 
            var peer = selectablePeers[i];
            return peer;
        }

        public void UpdateLast(string peerAddr)
        {
            last = peerAddr;
        }
    }
}