using Dotnatter.NetImpl.PeerImpl;

namespace Dotnatter.NodeImpl.PeerSelector
{
    public interface IPeerSelector

    {
        Peer[] Peers();
        Peer Next();
        void UpdateLast(string peerAddr);
    }
}