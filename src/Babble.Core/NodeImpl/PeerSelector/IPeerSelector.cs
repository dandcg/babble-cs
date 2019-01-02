using System.Threading.Tasks;
using Babble.Core.PeersImpl;

namespace Babble.Core.NodeImpl.PeerSelector
{
    public interface IPeerSelector

    {
        Peers Peers();
        Task<Peer> Next();
        void UpdateLast(string peerAddr);
    }
}