using Dotnatter.Core.NetImpl.PeerImpl;

namespace Dotnatter.Core.NodeImpl.PeerSelector
{
    public interface IPeerSelector

    {
        Peer[] Peers();
        Peer Next();
        void UpdateLast(string peerAddr);
    }
}