using System.Collections.Generic;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace Dotnatter.NetImpl.TransportImpl
{
    public class InMemRouter
    {

        private readonly AsyncLock sync;
        public Dictionary<string, ITransport> Peers { get; private set; }

        public InMemRouter()
        {
            sync = new AsyncLock();
            Peers = new Dictionary<string, ITransport>();
        }


        public async Task<ITransport> Register(string addr=null)
        {
            var trans = new InMemRouterTransport(this, addr);
            var peer = trans.LocalAddr;
            using (await sync.LockAsync())
            {
                Peers.Add(peer, trans);
            }
            return trans;
        }

        public async Task<(ITransport trans, NetError err)> GetPeer(string target)
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

            return (peer,null);

        }

        // Disconnect is used to remove the ability to route to a given peer.
        public async Task DisconnectAsync(string peer)
        {
            using (await sync.LockAsync())
            {
                Peers.Remove(peer);
            }
        }

        public void Disconnect(string peer)
        {
            using (sync.Lock())
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


    }
}