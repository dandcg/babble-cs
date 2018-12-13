using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Nito.AsyncEx;

namespace Babble.Core.NetImpl.TransportImpl
{
    public interface ITransport
    {
        // Consumer returns a channel that can be used to
        // consume and respond to RPC requests.
        BufferBlock<Rpc> Consumer { get; }

        // LocalAddr is used to return our local address to distinguish from our peers.
        string LocalAddr { get; }

        // Sync sends the appropriate RPC to the target node.
        Task<(SyncResponse resp, NetError err)> Sync(string target, SyncRequest args);

        Task<(EagerSyncResponse resp, NetError err)> EagerSync(string target, EagerSyncRequest args);

        // Close permanently closes a transport, stopping
        // any associated goroutines and freeing other resources.
        NetError Close();

        Task<NetError> CloseAsync();
    }
}