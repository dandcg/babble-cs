using System;
using System.Collections.Generic;
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
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Dotnatter.NodeImpl
{
    public class Node
    {
        private readonly NodeState nodeState;
        private readonly Config conf;
        private readonly int id;
        private readonly Peer[] participants;
        private readonly ITransport trans;
        private readonly AsyncProducerConsumerQueue<Rpc> netCh;

        private readonly IAppProxy proxy;
        private readonly Core core;
        private readonly AsyncLock coreLock;
        private readonly string localAddr;
        private readonly ILogger logger;
        private readonly RandomPeerSelector peerSelector;
        private readonly AsyncLock selectorLock;
        private readonly AsyncProducerConsumerQueue<bool> shutdownCh;
        private readonly ControlTimer controlTimer;
        private readonly AsyncProducerConsumerQueue<byte[]> submitCh;
        private AsyncProducerConsumerQueue<Event[]> commitCh;

        public Node(Config conf, int id, CngKey key, Peer[] participants, IStore store, ITransport trans, IAppProxy proxy, ILogger logger)

        {
            localAddr = trans.LocalAddr;

            var (pmap, _) = store.Participants();

            var commitCh = new AsyncProducerConsumerQueue<Event>(400);

            core = new Core(id, key, pmap, store, commitCh, logger);
            coreLock = new AsyncLock();
            peerSelector = new RandomPeerSelector(participants, localAddr);
            selectorLock = new AsyncLock();
            this.id = id;
            this.conf = conf;

            this.logger = logger.ForContext("node", localAddr);

            this.trans = trans;
            netCh = trans.Consumer;
            this.proxy = proxy;
            submitCh = proxy.SubmitCh();
            shutdownCh = new AsyncProducerConsumerQueue<bool>();
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

            var stateMachine = StateMachineRunAsync(gossip, ct);

            await Task.WhenAll(timer, backgroundWork, stateMachine);
        }

        private async Task StateMachineRunAsync(bool gossip, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                //    // Run different routines depending on node state
                var state = nodeState.GetState();
                logger.Debug("Run Loop {state}", state);

                switch (state)
                {
                    case NodeStateEnum.Babbling:
                        await babble(gossip, ct);
                        break;

                    case NodeStateEnum.CatchingUp:
                        await fastForward();
                        break;

                    case NodeStateEnum.Shutdown:
                        return;
                }
            }
        }

        public async Task BackgroundWorkRunAsync(CancellationToken ct)
        {
            var processingRpcTask = new Task(
                async () =>
                {
                    while (!ct.IsCancellationRequested)
                    {
                        await netCh.OutputAvailableAsync(ct);
                        var rpc = await netCh.DequeueAsync(ct);
                        logger.Debug("Processing RPC");
                        await ProcessRpc(rpc, ct);
                    }
                });

            var addingTransactionsTask = new Task(
                async () =>
                {
                    while (!ct.IsCancellationRequested)
                    {
                        await submitCh.OutputAvailableAsync(ct);
                        var tx = await submitCh.DequeueAsync(ct);
                        logger.Debug("Adding Transaction");
                        await AddTransaction(tx, ct);
                    }
                });

            var commitEventsTask = new Task(
                async () =>
                {
                    while (!ct.IsCancellationRequested)
                    {
                        await commitCh.OutputAvailableAsync(ct);
                        var events = await commitCh.DequeueAsync(ct);
                        logger.Debug("Committing Events {events}", events.Length);
                        var err = await Commit(events, ct);
                        if (err != null)
                        {
                            logger.Error("Committing Event", err);
                        }
                    }
                }
            );

            await Task.WhenAll(processingRpcTask);
        }

        private async Task babble(bool gossip, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var oldState = nodeState.GetState();

                var timerTask =
                    new Task(async () =>
                    {
                        while (!ct.IsCancellationRequested)
                        {
                            await controlTimer.TickCh.OutputAvailableAsync(ct);
                            await controlTimer.TickCh.DequeueAsync(ct);

                            if (gossip)
                            {
                                var (proceed, err) = await PreGossip();
                                if (proceed && err == null)
                                {
                                    logger.Debug("Time to gossip!");
                                    var peer = peerSelector.Next();
                                    await Gossip(peer.NetAddr);
                                }

                                if (!core.NeedGossip())
                                {
                                    await controlTimer.StopCh.EnqueueAsync(true, ct);
                                }
                                else if (!controlTimer.Set)
                                {
                                    await controlTimer.ResetCh.EnqueueAsync(true, ct);
                                }
                            }

                            var newState = nodeState.GetState();
                            if (newState != oldState)
                            {
                                break;
                            }
                        }
                    });

                var shutDownTask = new Task(async () =>
                {
                    await shutdownCh.OutputAvailableAsync(ct);
                    await shutdownCh.DequeueAsync(ct);
                });

                await Task.WhenAny(timerTask, shutDownTask, Task.Delay(Timeout.Infinite, ct));
            }
        }

        public async Task ProcessRpc(Rpc rpc, CancellationToken ct)
        {
            var s = nodeState.GetState();

            if (s != NodeStateEnum.Babbling)
            {
                logger.Debug("Discarding RPC Request {state}", s);
                var resp = new RpcResponse {Error = new NetError($"not ready: {s}"), Response = new SyncResponse {From = localAddr}};
                await rpc.RespChan.EnqueueAsync(resp, ct);
            }
            else
            {
                switch (rpc.Command)
                {
                    case SyncRequest cmd:
                        await ProcessSyncRequest(rpc, cmd);
                        break;

                    case EagerSyncRequest cmd:
                        await ProcessEagerSyncRequest(rpc, cmd);
                        break;

                    default:

                        logger.Error("Discarding RPC Request {@cmd}", rpc.Command);
                        var resp = new RpcResponse {Error = new NetError($"unexpected command"), Response = null};
                        await rpc.RespChan.EnqueueAsync(resp, ct);

                        break;
                }
            }
        }

        private Task ProcessSyncRequest(Rpc rpc, SyncRequest req)
        {
            throw new NotImplementedException();
        }

        private Task ProcessEagerSyncRequest(Rpc rpc, EagerSyncRequest cmd)
        {
            throw new NotImplementedException();
        }

        private async Task<(bool proceed, Exception error)> PreGossip()
        {
            using (await coreLock.LockAsync())
            {
                //Check if it is necessary to gossip
                var needGossip = core.NeedGossip() || nodeState.IsStarting();
                if (!needGossip)
                {
                    logger.Debug("Nothing to gossip");
                    return (false, null);
                }

                //If the transaction pool is not empty, create a new self-event and empty the
                //transaction pool in its payload
                var err = core.AddSelfEvent();
                if (err != null)
                {
                    logger.Error("Adding SelfEvent", err);
                    return (false, err);
                }
            }

            return (true, null);
        }

        private async Task<Exception> Gossip(string peerAddr)
        {
            //pull
            var (syncLimit, otherKnown, err) = await pull(peerAddr);
            if (err != null)
            {
                return err;
            }

            //check and handle syncLimit
            if (syncLimit)
            {
                logger.Debug("SyncLimit {from}", peerAddr);
                nodeState.SetState(NodeStateEnum.CatchingUp);
                return null;
            }

            //push
            err = await push(peerAddr, otherKnown);
            if (err != null)
            {
                return err;
            }

            //update peer selector
            using (selectorLock.Lock())
            {
                peerSelector.UpdateLast(peerAddr);
            }

            logStats();

            nodeState.SetStarting(false);

            return null;
        }

        private async Task<( bool syncLimit, Dictionary<int, int> otherKnown, Exception err)> pull(string peerAddr)
        {
            //Compute Known
            Dictionary<int, int> known;
            using (await coreLock.LockAsync())
            {
                known = core.Known();
            }

            //Send SyncRequest
            var start = new Stopwatch();

            var (resp, err) = await requestSync(peerAddr, known);
            var elapsed = start.Nanoseconds();
            logger.Debug("requestSync() Duration = {duration}", elapsed);
            if (err != null)
            {
                logger.Error("requestSync()", err);
                return (false, null, err);
            }

            var logFields = new {resp.SyncLimit, Events = resp.Events.Length, resp.Known};

            logger.Debug("SyncResponse {@fields}", logFields);

            if (resp.SyncLimit)
            {
                return (true, null, null);
            }

            //Add Events to Hashgraph and create new Head if necessary
            using (await coreLock.LockAsync())
            {
                err = await sync(resp.Events);
            }

            if (err != null)
            {
                logger.Error("sync()", err);
                return (false, null, err);
            }

            return (false, resp.Known, null);
        }

        private async Task<Exception> push(string peerAddr, Dictionary<int, int> known)
        {
            //Check SyncLimit
            bool overSyncLimit;
            using (await coreLock.LockAsync())
            {
                overSyncLimit = core.OverSyncLimit(known, conf.SyncLimit);
            }

            if (overSyncLimit)
            {
                logger.Debug("SyncLimit");
                return null;
            }

            //Compute Diff
            var start = new Stopwatch();

            Event[] diff;
            Exception err;
            using (await coreLock.LockAsync())
            {
                (diff, err) = core.Diff(known);
            }

            var elapsed = start.Nanoseconds();
            logger.Debug("Diff() {duration}", elapsed);
            if (err != null)
            {
                logger.Error("Calculating Diff", err);
                return err;
            }

            //Convert to WireEvents
            WireEvent[] wireEvents;
            (wireEvents, err) = core.ToWire(diff);
            if (err != null)
            {
                logger.Debug("Converting to WireEvent", err);
                return err;
            }

            //Create and Send EagerSyncRequest
            start = new Stopwatch();

            EagerSyncResponse resp2;
            (resp2, err) = await requestEagerSync(peerAddr, wireEvents);
            elapsed = start.Nanoseconds();
            logger.Debug("requestEagerSync() {duration}", elapsed);
            if (err != null)
            {
                logger.Error("requestEagerSync()", err);
                return err;
            }

            var logFields = new {Fromm = resp2.From, resp2.Success};

            logger.Debug("EagerSyncResponse {@fields}", logFields);

            return null;
        }

        private Task fastForward()
        {
            logger.Debug("IN CATCHING-UP STATE");
            logger.Debug("fast-sync not implemented yet");

            //XXX Work in Progress on fsync branch

            nodeState.SetState(NodeStateEnum.Babbling);

            return Task.FromResult(true);
        }

        private async Task<(SyncResponse resp, Exception err)> requestSync(string target, Dictionary<int, int> known)
        {
            var args = new SyncRequest
            {
                From = localAddr,
                Known = known
            };

            var (resp, err) = await trans.Sync(target, args);
            return (resp, err);
        }

        private async Task<(EagerSyncResponse resp, Exception err)> requestEagerSync(string target, WireEvent[] events)
        {
            var args = new EagerSyncRequest
            {
                From = localAddr,
                Events = events
            };

            var (resp, err) = await trans.EagerSync(target, args);
            return (resp, err);
        }

        private async Task<Exception> sync(WireEvent[] events)
        {
            //Insert Events in Hashgraph and create new Head if necessary
            var start = new Stopwatch();
            var err = core.Sync(events);

            var elapsed = start.Nanoseconds();

            logger.Debug("Processed Sync() {duration}", elapsed);
            if (err != null)
            {
                return err;
            }

            //Run consensus methods
            start = new Stopwatch();
            err = core.RunConsensus();

            elapsed = start.Nanoseconds();
            logger.Debug("Processed RunConsensus() {duration}", elapsed);
            return err;
        }

        private async Task<Exception> Commit(Event[] events, CancellationToken ct)
        {
            foreach (var ev in events)
            {
                foreach (var tx in ev.Transactions())
                {
                    var err = proxy.CommitTx(tx);
                    if (err != null)
                    {
                        return err;
                    }
                }
            }

            return null;
        }

        private async Task AddTransaction(byte[] tx, CancellationToken ct)
        {
            using (await coreLock.LockAsync())
            {
                core.AddTransactions(new[] {tx});
            }
        }

        public async Task Shutdown()

        {
            if (nodeState.GetState() != NodeStateEnum.Shutdown)
            {
                logger.Debug("Shutdown");

                //Exit any non-shutdown state immediately
                nodeState.SetState(NodeStateEnum.Shutdown);

                //Stop and wait for concurrent operations
                await shutdownCh.EnqueueAsync(true);

                //For some reason this needs to be called after closing the shutdownCh
                //Not entirely sure why...
                await controlTimer.Shutdown();

                //transport and store should only be closed once all concurrent operations
                //are finished otherwise they will panic trying to use close objects
                await trans.Close();
                core.hg.Store.Close();
            }
        }

        public Dictionary<string, string> GetStats()
        {
//	toString := func(i*int) string {
//		if i == nil {
//			return "nil"
//		}
//		return strconv.Itoa(* i)
//}

//timeElapsed := time.Since(n.start)

//consensusEvents := n.core.GetConsensusEventsCount()
//consensusEventsPerSecond := float64(consensusEvents) / timeElapsed.Seconds()

//lastConsensusRound := n.core.GetLastConsensusRoundIndex()
//var consensusRoundsPerSecond float64
//	if lastConsensusRound != nil {
//		consensusRoundsPerSecond = float64(*lastConsensusRound) / timeElapsed.Seconds()
//	}

//	s := map[string] string{
//		"last_consensus_round":   toString(lastConsensusRound),
//		"consensus_events":       strconv.Itoa(consensusEvents),
//		"consensus_transactions": strconv.Itoa(n.core.GetConsensusTransactionsCount()),
//		"undetermined_events":    strconv.Itoa(len(n.core.GetUndeterminedEvents())),
//		"transaction_pool":       strconv.Itoa(len(n.core.transactionPool)),
//		"num_peers":              strconv.Itoa(len(n.peerSelector.Peers())),
//		"sync_rate":              strconv.FormatFloat(n.SyncRate(), 'f', 2, 64),
//		"events_per_second":      strconv.FormatFloat(consensusEventsPerSecond, 'f', 2, 64),
//		"rounds_per_second":      strconv.FormatFloat(consensusRoundsPerSecond, 'f', 2, 64),
//		"round_events":           strconv.Itoa(n.core.GetLastCommitedRoundEventsCount()),
//		"id":                     strconv.Itoa(n.id),
//		"state":                  n.getState().String(),
//	}
//	return s
            throw new NotImplementedException();
        }

        private void logStats()
        {
            //stats := n.GetStats()
            //n.logger.WithFields(logrus.Fields{
            //	"last_consensus_round":   stats["last_consensus_round"],
            //	"consensus_events":       stats["consensus_events"],
            //	"consensus_transactions": stats["consensus_transactions"],
            //	"undetermined_events":    stats["undetermined_events"],
            //	"transaction_pool":       stats["transaction_pool"],
            //	"num_peers":              stats["num_peers"],
            //	"sync_rate":              stats["sync_rate"],
            //	"events/s":               stats["events_per_second"],
            //	"rounds/s":               stats["rounds_per_second"],
            //	"round_events":           stats["round_events"],
            //	"id":                     stats["id"],
            //	"state":                  stats["state"],
            //}).Debug("Stats")

            throw new NotImplementedException();
        }

        public decimal SyncRate()
        {
            //if (syncRequests != 0) {
            //	syncErrorRate = float64(n.syncErrors) / float64(n.syncRequests)
            //}

            //   return (1 - syncErrorRate);

            throw new NotImplementedException();
        }
    }
}