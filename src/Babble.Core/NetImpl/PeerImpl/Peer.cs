using System.Collections.Generic;

namespace Dotnatter.Core.NetImpl.PeerImpl
{
    public class Peer
    {
        public string NetAddr { get; }
        public string PubKeyHex { get; }


        public Peer(string netAddr, string pubKeyHex)
        {
            this.NetAddr = netAddr;
            this.PubKeyHex = pubKeyHex;
     
        }

        // ExcludePeer is used to exclude a single peer from a list of peers.
        public static (int, Peer[]) ExcludePeer(Peer[] peers, string peer)
        {
            var index = -1;
            var otherPeers = new List<Peer>();

            int i = 0;
            foreach (var p in peers)
            {
                if (p.NetAddr != peer)
                {
                    otherPeers.Add(p);
                } else
                {
                    index = i;
                }

                i++;
            }

            return (index, otherPeers.ToArray());
        }
    }
}