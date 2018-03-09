using System;
using Dotnatter.Core.NetImpl.PeerImpl;

namespace Dotnatter.Core.NodeImpl.PeerSelector
{
    public class RandomPeerSelector:IPeerSelector
    {

        private readonly Peer[] peers;
        private string last;

        public RandomPeerSelector(Peer[] participants, string localAddr)
        {
        
            (_,peers) = Peer.ExcludePeer(participants, localAddr);
        }

        public Peer[] Peers()
        {
            return peers;
        }


        public Peer Next()
        {
            var selectablePeers = peers;
            if (selectablePeers.Length > 1)
            {
                (_, selectablePeers) = Peer.ExcludePeer(selectablePeers, last);
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