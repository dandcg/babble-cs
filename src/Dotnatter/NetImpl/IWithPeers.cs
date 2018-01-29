using Dotnatter.NetImpl.TransportImpl;

namespace Dotnatter.NetImpl
{
    public interface IWithPeers
    {
        void Connect(string peer, ITransport t); // Connect a peer
        void Disconnect(string peer); // Disconnect a given peer
        void DisconnectAll(); // Disconnect all peers, possibly to reconnect them later
    }
}