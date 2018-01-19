using Dotnatter.HashgraphImpl;
using Dotnatter.Util;

namespace Dotnatter.NetImpl
{
    public interface ITransport
    {
        // Consumer returns a channel that can be used to
        // consume and respond to RPC requests.
        Channel<Rpc> Consumer();

        // LocalAddr is used to return our local address to distinguish from our peers.
        string LocalAddr();

        // Sync sends the appropriate RPC to the target node.
        NetError Sync(string target, SyncRequest args, SyncResponse resp);

        NetError EagerSync(string target, EagerSyncRequest args, EagerSyncResponse resp);

        // Close permanently closes a transport, stopping
        // any associated goroutines and freeing other resources.
        NetError Close();
    }
}