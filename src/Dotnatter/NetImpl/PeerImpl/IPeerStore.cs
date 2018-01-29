using System.Threading.Tasks;

namespace Dotnatter.NetImpl.PeerImpl
{
    public interface IPeerStore
    {
        Task<Peer[]> Peers();
        Task SetPeers(Peer[] npeers);
    }
}