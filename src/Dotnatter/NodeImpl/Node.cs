using System.Security.Cryptography;
using System.Threading.Tasks;
using Dotnatter.HashgraphImpl;
using Dotnatter.HashgraphImpl.Model;
using Dotnatter.NetImpl;
using Dotnatter.ProxyImpl;
using Dotnatter.Util;
using Serilog;

namespace Dotnatter.NodeImpl
{
    public class Node
    {
        private readonly NodeState nodeState;
        private readonly Config conf;
        private readonly int id;
        private readonly Peer[] participants;
        private readonly ITransport trans;
        private readonly IAppProxy proxy;
        private Core core;
        private readonly string localAddr;
        private ILogger logger;
        private RandomPeerSelector peerSelector;
        private Channel<Shutdown> shutdownCh;
        private object netCh;
        private Channel<byte[]> submitCh;
        private readonly ControlTimer controlTimer;

        public Node(Config conf, int id, CngKey key, Peer[] participants, IStore store, ITransport trans, IAppProxy proxy, ILogger logger)

        {
            localAddr = trans.LocalAddr();

            var (pmap, _) = store.Participants();

            var commitCh = new Channel<Event>(); //400 

            core = new Core(id, key, pmap, store, commitCh, logger);

            peerSelector = new RandomPeerSelector(participants, localAddr);

            this.id = id;
            this.conf = conf;

            this.logger = logger.ForContext("node", localAddr);

            this.trans = trans;
            netCh = trans.Consumer();
            this.proxy = proxy;
            submitCh = proxy.SubmitCh();
            shutdownCh = new Channel<Shutdown>();
            controlTimer = ControlTimer.NewRandomControlTimer(conf.HeartbeatTimeout);

            //Initialize as Babbling
            nodeState.SetStarting(true);
            nodeState.SetState(NodeStateEnum.Babbling);
        }

        public async Task RunAsync(bool gossip)
        {
            //The ControlTimer allows the background routines to control the
            //heartbeat timer when the node is in the Babbling state. The timer should
            //only be running when there are uncommitted transactions in the system.

            var timer = controlTimer.RunAsync();

            //Execute some background work regardless of the state of the node.
            //Process RPC requests as well as SumbitTx and CommitTx requests

            var backgroundWork = DoBackgroundWork();

            //n.goFunc(n.doBackgroundWork)

            //Execute Node State Machine
            //for {
            //    // Run different routines depending on node state
            //    state := n.getState()
            //    n.logger.WithField("state", state.String()).Debug("Run loop")

            //    switch state {
            //        case Babbling:
            //        n.babble(gossip)
            //        case CatchingUp:
            //        n.fastForward()
            //        case Shutdown:
            //        return
            //    }
            //}

            Task.WaitAll(timer, backgroundWork);
        }

        public async Task DoBackgroundWork()
        {
        }
    }
}