namespace Babble.Core.NodeImpl.PeerSelector
{
    public interface IPeerSelector

    {
        Peer[] Peers();
        Peer Next();
        void UpdateLast(string peerAddr);
    }
}