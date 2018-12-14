using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace Babble.Core.PeersImpl
{
    public class Peers
    {
        public AsyncLock RwMutex = new AsyncLock();
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
                peers.AddPeerRaw(peer);
            }

            peers.internalSort();

            return peers;
        }

        /* Add Methods */

        // Add a peer without sorting the set.
        // Useful for adding a bunch of peers at the same time
        // This method is private and is not protected by mutex.
        // Handle with care
        public void AddPeerRaw(Peer peer)
        {
            if (peer.ID == 0)
            {
                peer.ComputeId();
            }

            ByPubKey[peer.PubKeyHex] = peer;
            ById[peer.ID] = peer;
        }

        public  async Task AddPeer(Peer peer)
        {
            using (await RwMutex.LockAsync())
            {
                AddPeerRaw(peer);

                internalSort();
            }
        }

        private  void internalSort()
        {
            Sorted = ById.Values.OrderBy(o => o.ID).ToList();
        }

        /* Remove Methods */

        public  async Task RemovePeer(Peer peer)
        {
            using (await RwMutex.LockAsync())
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

        public  Task RemovePeerByPubKey(string pubKey)
        {
            return RemovePeer(ByPubKey[pubKey]);
        }

        public  Task RemovePeerById(int id)
        {
            return RemovePeer(ById[id]);
        }

        /* ToSlice Methods */

        public Peer[] ToPeerSlice()
        {
            return Sorted.ToArray();
        }

        public async Task<string[]> ToPubKeySlice()
        {
            using (await RwMutex.LockAsync())
            {
                return Sorted.Select(s => s.PubKeyHex).ToArray();
            }
        }

        public async Task<int[]> ToIdSlice()
        {
            using (await RwMutex.LockAsync())
            {
                return Sorted.Select(s => s.ID).ToArray();
            }
        }

        /* Utilities */

        public  int Len()
        {
            using (RwMutex.Lock())
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