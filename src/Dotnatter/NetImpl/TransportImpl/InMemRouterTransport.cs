using System;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace Dotnatter.NetImpl.TransportImpl
{
    public class InMemRouterTransport : ITransport
    {
        public TimeSpan Timeout { get; set; }

        public InMemRouterTransport(InMemRouter router, string addr)
        {
            if (addr == "")
            {
                addr = GenerateUuid();
            }

            Consumer = new AsyncProducerConsumerQueue<Rpc>(16);
            LocalAddr = addr;
            Router = router;
            Timeout = TimeSpan.FromMilliseconds(500);
        }

        public AsyncProducerConsumerQueue<Rpc> Consumer { get; }

        public string LocalAddr { get; }
        public InMemRouter Router { get; }

        public async Task<(SyncResponse resp, NetError err)> Sync(string target, SyncRequest args)
        {
            var (rpcResp, err) = await MakeRpc(target, args, Timeout);

            if (err != null)
            {
                return (null, err);
            }

            var syncResp = (SyncResponse) rpcResp.Response;

            return (syncResp, null);
        }

        public async Task<(EagerSyncResponse resp, NetError err)> EagerSync(string target, EagerSyncRequest args)
        {
            var (rpcResp, err) = await MakeRpc(target, args, Timeout);

            if (err != null)
            {
                return (null, err);
            }

            var syncResp = (EagerSyncResponse) rpcResp.Response;

            return (syncResp, null);
        }

        private string GenerateUuid()
        {
            return Guid.NewGuid().ToString();
        }

        private async Task<(RpcResponse rpcResp, NetError err)> MakeRpc(string target, object args, TimeSpan tmout)
        {
            var (peer, err) = await Router.GetPeer(target);

            var rpc = new Rpc {Command = args, RespChan = new AsyncProducerConsumerQueue<RpcResponse>()};

            await peer.Consumer.EnqueueAsync(rpc);

            var responseTask = rpc.RespChan.OutputAvailableAsync();

            var timeoutTask = Task.Delay(tmout);

            await Task.WhenAny(responseTask, timeoutTask);

            if (!responseTask.IsCompleted) return (null, new NetError("command timed out"));

            var rpcResp = await rpc.RespChan.DequeueAsync();

            return (rpcResp, rpcResp.Error);
        }

        // Disconnect is used to remove the ability to route to a given peer.
        public async Task DisconnectAsync()
        {
            await Router.DisconnectAsync(LocalAddr);
        }

        public NetError Close()
        {
            Router.Disconnect(LocalAddr);
            return null;
        }
    }
}