using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace Babble.Core.NetImpl.TransportImpl
{
    public class InMemTransport : ILoopbackTransport
    {
        private readonly AsyncLock sync;
        public Dictionary<string, ITransport> Peers { get; private set; }
        public TimeSpan Timeout { get; set; }

        public InMemTransport(string addr)
        {
            if (addr == "")
            {
                addr = GenerateUuid();
            }

            sync = new AsyncLock();
            Consumer = new BufferBlock<Rpc>(16);
            LocalAddr = addr;
            Peers = new Dictionary<string, ITransport>();
            Timeout = TimeSpan.FromMilliseconds(5000);
        }

        public BufferBlock<Rpc> Consumer { get; }

        public string LocalAddr { get; }

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
            bool ok;
            ITransport peer;

            using (await sync.LockAsync())
            {
                ok = Peers.TryGetValue(target, out peer);
            }

            if (!ok || peer == null)
            {
                return (null, new NetError($"Failed to connect to peer: {target}"));
            }

            var rpc = new Rpc {Command = args, RespChan = new BufferBlock<RpcResponse>()};

            await peer.Consumer.EnqueueAsync(rpc);

            var responseTask =  rpc.RespChan.DequeueAsync();

            var timeoutTask = Task.Delay(tmout);

            var resultTask =await Task.WhenAny(responseTask, timeoutTask);

            if (resultTask==timeoutTask)
            {
                return (null, new NetError("command timed out"));
            }

            var rpcResp = responseTask.Result;

            return (rpcResp, rpcResp.Error);
        }

        // Connect is used to connect this transport to another transport for
        // a given peer name. This allows for local routing.
        public async Task ConnectAsync(string peer, ITransport trans)
        {
            using (await sync.LockAsync())
            {
                Peers[peer] = trans;
            }
        }

        // Disconnect is used to remove the ability to route to a given peer.
        public async Task DisconnectAsync(string peer)
        {
            using (await sync.LockAsync())
            {
                Peers.Remove(peer);
            }
        }

        // DisconnectAll is used to remove all routes to peers.
        public async Task DisconnectAllAsync()
        {
            using (await sync.LockAsync())
            {
                Peers = new Dictionary<string, ITransport>();
            }
        }

        public NetError Close()
        {
            using (sync.Lock())
            {
                Peers = new Dictionary<string, ITransport>();
            }

            return null;
        }

        public async Task<NetError> CloseAsync()
        {
            using (await sync.LockAsync())
            {
                Peers = new Dictionary<string, ITransport>();
            }

            return null;
        }
    }
}