using System.Threading.Tasks;

namespace Babble.Core.NetImpl.PeerImpl
{
    public interface IPeerStore
    {
        Task<Peer[]> Peers();
        Task SetPeers(Peer[] npeers);
    }
}