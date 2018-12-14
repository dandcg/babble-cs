﻿using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Nito.AsyncEx;

namespace Babble.Core.NetImpl.TransportImpl
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

            Consumer = new BufferBlock<Rpc>(new DataflowBlockOptions()
                {BoundedCapacity = 16});
            LocalAddr = addr;
            Router = router;
            Timeout = TimeSpan.FromMilliseconds(2000);
        }

        public BufferBlock<Rpc> Consumer { get; }

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

            if (err != null)
            {
                return (null, err);
            }

            var rpc = new Rpc {Command = args, RespChan = new BufferBlock<RpcResponse>()};

            await peer.Consumer.SendAsync(rpc);

            var responseTask=  rpc.RespChan.ReceiveAsync();

            var timeoutTask = Task.Delay(tmout);

            var resultTask = await Task.WhenAny(responseTask, timeoutTask);

            if (resultTask == timeoutTask)
            {
                return (null, new NetError("command timed out"));
            }

            var rpcResp = responseTask.Result;

            return (rpcResp, rpcResp.Error);
        }

        // Disconnect is used to remove the ability to route to a given peer.
        public Task DisconnectAsync()
        {
            return Router.DisconnectAsync(LocalAddr);
        }

        public NetError Close()
        {
            Router.Disconnect(LocalAddr);
            return null;
        }

        public async Task<NetError> CloseAsync()
        {
            await Router.DisconnectAsync(LocalAddr);
            return null;
        }
    }
}