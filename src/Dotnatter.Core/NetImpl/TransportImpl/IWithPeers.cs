using System.Threading.Tasks;

namespace Dotnatter.NetImpl.TransportImpl
{
    public interface IWithPeers
    {
        Task ConnectAsync(string peer, ITransport t); // Connect a peer
        Task DisconnectAsync(string peer); // Disconnect a given peer
        Task DisconnectAllAsync(); // Disconnect all peers, possibly to reconnect them later
    }
}