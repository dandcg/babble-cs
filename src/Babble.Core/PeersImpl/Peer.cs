using System;
using System.Collections.Generic;
using System.Linq;
using Babble.Core.Common;
using Babble.Core.Util;

namespace Babble.Core.PeersImpl
{
    public class Peer
    {
        public int ID { get; set; }
        public string NetAddr { get; set; }
        public string PubKeyHex { get; set; }

        public static Peer New(string pubKeyHex, string netAddr)
        {
            var peer = new Peer {PubKeyHex = pubKeyHex, NetAddr = netAddr};
            peer.ComputeId();

            return peer;
        }

        private byte[] PubKeyBytes()
        {
            return PubKeyHex.FromHex();
        }

        public void ComputeId()
        {
            // TODO: Use the decoded bytes from hex
            var pubKey = PubKeyBytes();

            var hash = new Fnv1a32();
            var res = hash.ComputeHash(pubKey);
            ID = hash.GetHashCode();
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