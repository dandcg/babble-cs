using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace Dotnatter.NetImpl.TransportImpl
{
    public class WampTransport:ITransport
    {

        private readonly AsyncLock sync;
        private Dictionary<string, ITransport> peers;
        private readonly TimeSpan timeout;

        public WampTransport()
        {
            sync = new AsyncLock();
            Consumer = new AsyncProducerConsumerQueue<Rpc>(16);
            //LocalAddr = GenerateUuid();
            peers = new Dictionary<string, ITransport>();
            timeout = new TimeSpan(0, 0, 0, 50);
   
        }

        public AsyncProducerConsumerQueue<Rpc> Consumer { get; }
        public string LocalAddr { get; }
        public Task<(SyncResponse resp, NetError err)> Sync(string target, SyncRequest args)
        {
            throw new NotImplementedException();
        }

        public Task<(EagerSyncResponse resp, NetError err)> EagerSync(string target, EagerSyncRequest args)
        {
            throw new NotImplementedException();
        }

        public Task<NetError> Close()
        {
            throw new NotImplementedException();
        }
    }
}
