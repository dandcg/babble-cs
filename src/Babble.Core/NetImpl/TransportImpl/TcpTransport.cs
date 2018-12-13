using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace Babble.Core.NetImpl.TransportImpl
{
    public class TcpTransport:ITransport
    {

        private readonly AsyncLock sync;
        private Dictionary<string, ITransport> peers;
        private readonly TimeSpan timeout;

        public TcpTransport()
        {
            sync = new AsyncLock();
            Consumer = new BufferBlock<Rpc>(16);
            //LocalAddr = GenerateUuid();
            peers = new Dictionary<string, ITransport>();
            timeout = new TimeSpan(0, 0, 0, 50);
   
        }

        public BufferBlock<Rpc> Consumer { get; }
        public string LocalAddr { get; }
        public Task<(SyncResponse resp, NetError err)> Sync(string target, SyncRequest args)
        {
            throw new NotImplementedException();
        }

        public Task<(EagerSyncResponse resp, NetError err)> EagerSync(string target, EagerSyncRequest args)
        {
            throw new NotImplementedException();
        }

        public NetError Close()
        {
            throw new NotImplementedException();
        }

        public Task<NetError> CloseAsync()
        {
            throw new NotImplementedException();
        }
    }
}
