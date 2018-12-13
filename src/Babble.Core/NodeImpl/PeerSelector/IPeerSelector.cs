using Babble.Core.PeersImpl;

namespace Babble.Core.NodeImpl.PeerSelector
{
    public interface IPeerSelector

    {
        Peers Peers();
        Peer Next();
        void UpdateLast(string peerAddr);
    }
}