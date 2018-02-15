using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace Dotnatter.NetImpl.TransportImpl
{
    public class InMemTransport : ILoopbackTransport
    {
        private readonly AsyncLock sync;
        private Dictionary<string, ITransport> peers;
        private readonly TimeSpan timeout;

        public InMemTransport()
        {
            sync = new AsyncLock();
            Consumer = new AsyncProducerConsumerQueue<Rpc>(16);
            LocalAddr = GenerateUuid();
            peers = new Dictionary<string, ITransport>();
            timeout = new TimeSpan(0, 0, 0, 50);
        }

        public AsyncProducerConsumerQueue<Rpc> Consumer { get; }

        public string LocalAddr { get; }

        public async Task<(SyncResponse resp, NetError err)> Sync(string target, SyncRequest args)
        {
            var (rpcResp, err) = await MakeRpc(target, args, timeout);

            var syncResp = (SyncResponse) rpcResp.Response;

            return (syncResp, err);
        }

        public async Task<(EagerSyncResponse resp, NetError err)> EagerSync(string target, EagerSyncRequest args)
        {
            var (rpcResp, err) = await MakeRpc(target, args, timeout);

            var syncResp = (EagerSyncResponse) rpcResp.Response;

            return (syncResp, err);
        }

        private string GenerateUuid()
        {
            return Guid.NewGuid().ToString();
        }

        private async Task<(RpcResponse rpcResp, NetError err)> MakeRpc(string target, object args, TimeSpan tmout)
        {
            bool ok;
            ITransport peer;

            using (await sync.LockAsync())
            {
                ok = peers.TryGetValue(target, out peer);
            }

            if (!ok || peer == null)
            {
                return (null, new NetError($"Failed to connect to peer: {target}"));
            }

            var rpc = new Rpc {Command = args, RespChan = new AsyncProducerConsumerQueue<RpcResponse>()};

            await peer.Consumer.EnqueueAsync(rpc);

            var responseTask = rpc.RespChan.OutputAvailableAsync();

            var timeoutTask = Task.Delay(tmout);

            await Task.WhenAny(responseTask, timeoutTask);

            if (!responseTask.IsCompleted) return (null, new NetError($"command timed out"));

            var rpcResp = await rpc.RespChan.DequeueAsync();

            return (rpcResp, rpcResp.Error);
        }

        // Connect is used to connect this transport to another transport for
        // a given peer name. This allows for local routing.
        public async Task ConnectAsync(string peer, ITransport trans)
        {
            using (await sync.LockAsync())
            {
                peers[peer] = trans;
            }
        }

        // Disconnect is used to remove the ability to route to a given peer.
        public async Task DisconnectAsync(string peer)
        {
            using (await sync.LockAsync())
            {
                peers.Remove(peer);
            }
        }

        // DisconnectAll is used to remove all routes to peers.
        public async Task DisconnectAllAsync()
        {
            using (await sync.LockAsync())
            {
                peers = new Dictionary<string, ITransport>();
            }
        }

        public async Task<NetError> Close()
        {
            await DisconnectAllAsync();
            return null;
        }
    }
}