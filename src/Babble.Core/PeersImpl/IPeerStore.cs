using System.Threading.Tasks;
using Babble.Core.Common;

namespace Babble.Core.PeersImpl
{
    // PeerStore provides an interface for persistent storage and
// retrieval of peers.
    public interface IPeerStore
    {
        // Peers returns the list of known peers.
        Task<(Peer[], StoreError)> Peers();


        // SetPeers sets the list of known peers. This is invoked when a peer is
        // added or removed.
        Task<StoreError> SetPeers(Peer[] peers);
    }
}