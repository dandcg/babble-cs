using System.Threading.Tasks;

namespace Dotnatter.Core.NetImpl.PeerImpl
{
    public interface IPeerStore
    {
        Task<Peer[]> Peers();
        Task SetPeers(Peer[] npeers);
    }
}