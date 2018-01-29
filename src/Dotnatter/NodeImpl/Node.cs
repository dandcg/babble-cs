using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Dotnatter.HashgraphImpl;
using Dotnatter.HashgraphImpl.Model;
using Dotnatter.NetImpl;
using Dotnatter.NetImpl.PeerImpl;
using Dotnatter.NetImpl.TransportImpl;
using Dotnatter.ProxyImpl;
using Dotnatter.Util;
using Nito.AsyncEx;
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
        private readonly AsyncLock coreLock;
        private readonly string localAddr;
        private ILogger logger;
        private RandomPeerSelector peerSelector;
        private Channel<Shutdown> shutdownCh;
        private readonly ControlTimer controlTimer;

        public Node(Config conf, int id, CngKey key, Peer[] participants, IStore store, ITransport trans, IAppProxy proxy, ILogger logger)

        {
            localAddr = trans.LocalAddr;

            var (pmap, _) = store.Participants();

            var commitCh = new Channel<Event>(); //400 

            core = new Core(id, key, pmap, store, commitCh, logger);
            coreLock= new AsyncLock();
            peerSelector = new RandomPeerSelector(participants, localAddr);

            this.id = id;
            this.conf = conf;

            this.logger = logger.ForContext("node", localAddr);

            this.trans = trans;
   
            this.proxy = proxy;
            submitCh = proxy.SubmitCh();
            shutdownCh = new Channel<Shutdown>();
            controlTimer = ControlTimer.NewRandomControlTimer(conf.HeartbeatTimeout);

            //Initialize as Babbling
            nodeState.SetStarting(true);
            nodeState.SetState(NodeStateEnum.Babbling);
        }

        public async Task RunAsync(bool gossip, CancellationToken ct)
        {
            //The ControlTimer allows the background routines to control the
            //heartbeat timer when the node is in the Babbling state. The timer should
            //only be running when there are uncommitted transactions in the system.

            var timer = controlTimer.RunAsync(ct);

            //Execute some background work regardless of the state of the node.
            //Process RPC requests as well as SumbitTx and CommitTx requests

            var backgroundWork = BackgroundWorkRunAsync(ct);

            //Execute Node State Machine

            var stateMachine = StateMachineRunAsync(gossip,ct);
            
            await Task.WhenAll(timer, backgroundWork, stateMachine);
        }

        private async Task StateMachineRunAsync(bool gossip, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                //    // Run different routines depending on node state
                var state = nodeState.GetState();
                logger.Debug("Run Lopp {state}", state);

                switch (state)
                {
                       case NodeStateEnum.Babbling:
                           await babble(gossip,ct);
                           break;

                           case NodeStateEnum.CatchingUp:
                               await fastForward();
                               break;

                               case NodeStateEnum.Shutdown:
                                   return;
                                   break;

                }

      
            } ;
        }

        public async Task BackgroundWorkRunAsync(CancellationToken ct)
        {

            while (!ct.IsCancellationRequested)
            {

                //var rpc = netCh.





            }

        }








    }
}