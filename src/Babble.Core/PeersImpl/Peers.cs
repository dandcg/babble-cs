using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Babble.Core.PeersImpl;
using Nito.AsyncEx;

namespace go
{
    public class Peers
    {
        public AsyncLock RWMutex = new AsyncLock();
        public List<Peer> Sorted;
        public Dictionary<string, Peer> ByPubKey;
        public Dictionary<int, Peer> ById;

        /* Constructors */

        public static Peers NewPeers()
        {
            return new Peers {ById = new Dictionary<int, Peer>(), ByPubKey = new Dictionary<string, Peer>()};
        }

        public static Peers NewPeersFromSlice(Peer[] source)
        {
            var peers = NewPeers();

            foreach (var peer in source)
            {
                peers.addPeerRaw(peer);
            }

            peers.internalSort();

            return peers;
        }

        /* Add Methods */

        // Add a peer without sorting the set.
        // Useful for adding a bunch of peers at the same time
        // This method is private and is not protected by mutex.
        // Handle with care
        private void addPeerRaw(Peer peer)
        {
            if (peer.ID == 0)
            {
                peer.ComputeId();
            }

            ByPubKey[peer.PubKeyHex] = peer;
            ById[peer.ID] = peer;
        }

        private async Task AddPeer(Peer peer)
        {
            using (await RWMutex.LockAsync())
            {
                addPeerRaw(peer);

                internalSort();
            }
        }

        private void internalSort()
        {
            Sorted = ById.Values.ToList(); //Todo: Ordering here
        }

        /* Remove Methods */

        private async Task RemovePeer(Peer peer)
        {
            using (await RWMutex.LockAsync())
            {
                var ok = ByPubKey.ContainsKey(peer.PubKeyHex);

                if (!ok)
                {
                    return;
                }

                ByPubKey.Remove(peer.PubKeyHex);
                ById.Remove(peer.ID);

                internalSort();
            }
        }

        private Task RemovePeerByPubKey(string pubKey)
        {
            return RemovePeer(ByPubKey[pubKey]);
        }

        private Task RemovePeerById(int id)
        {
            return RemovePeer(ById[id]);
        }

        /* ToSlice Methods */

        private Peer[] ToPeerSlice()
        {
            return Sorted.ToArray();
        }

        private async Task<string[]> ToPubKeySlice(Peers p)
        {
            using (await RWMutex.LockAsync())
            {
                return Sorted.Select(s => s.PubKeyHex).ToArray();
            }
        }

        private async Task<int[]> ToIdSlice(Peers p)
        {
            using (await RWMutex.LockAsync())
            {
                return Sorted.Select(s => s.ID).ToArray();
            }
        }

        /* Utilities */

        private int Len()
        {
            using (RWMutex.Lock())
            {
                return ByPubKey.Count;
            }
        }
    }

    public class PeerByPubHex : IComparer<Peer>
    {
        public int Compare(Peer x, Peer y)
        {
            return string.Compare(x.PubKeyHex, y.PubKeyHex, StringComparison.Ordinal);
        }
    }

    public class PeerById : IComparer<Peer>
    {
        public int Compare(Peer x, Peer y)
        {
            return x.ID.CompareTo(y.ID);
        }
    }
}