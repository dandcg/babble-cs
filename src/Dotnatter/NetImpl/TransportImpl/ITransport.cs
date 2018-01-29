using System.Threading.Tasks;
using Nito.AsyncEx;

namespace Dotnatter.NetImpl.TransportImpl
{
    public interface ITransport
    {
        // Consumer returns a channel that can be used to
        // consume and respond to RPC requests.
        AsyncProducerConsumerQueue<Rpc> Consumer { get; }

        // LocalAddr is used to return our local address to distinguish from our peers.
        string LocalAddr { get; }

        // Sync sends the appropriate RPC to the target node.
        Task<(SyncResponse resp, NetError err)> Sync(string target, SyncRequest args);

        Task<(EagerSyncResponse resp, NetError err)> EagerSync(string target, EagerSyncRequest args);

        // Close permanently closes a transport, stopping
        // any associated goroutines and freeing other resources.
        Task<NetError> Close();
    }
}