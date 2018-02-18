using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Dotnatter.HashgraphImpl;
using Dotnatter.HashgraphImpl.Model;
using Dotnatter.HashgraphImpl.Stores;
using Dotnatter.NetImpl;
using Dotnatter.NetImpl.PeerImpl;
using Dotnatter.NetImpl.TransportImpl;
using Dotnatter.NodeImpl.PeerSelector;
using Dotnatter.ProxyImpl;
using Dotnatter.Util;
using Nito.AsyncEx;
using Serilog;

namespace Dotnatter.NodeImpl
{
    public class Node
    {
        private readonly NodeState nodeState;
        public Config Conf { get; }
        public int Id { get; }
        public IStore Store { get; }
        private readonly Peer[] participants;
        public ITransport Trans { get; }
        private readonly AsyncProducerConsumerQueue<Rpc> netCh;
        private readonly IAppProxy proxy;
        public Core Core { get; }
        private readonly AsyncLock coreLock;
        public string LocalAddr { get; }
        private readonly ILogger logger;
        public IPeerSelector PeerSelector { get; }
        private readonly AsyncLock selectorLock;
        private readonly ControlTimer controlTimer;
        private readonly AsyncProducerConsumerQueue<byte[]> submitCh;
        private readonly AsyncProducerConsumerQueue<Event[]> commitCh;
        private CancellationTokenSource cts;

        public Node(Config conf, int id, CngKey key, Peer[] participants, IStore store, ITransport trans, IAppProxy proxy, ILogger logger)

        {
            LocalAddr = trans.LocalAddr;

            var (pmap, _) = store.Participants();

            commitCh = new AsyncProducerConsumerQueue<Event[]>(400);

            Core = new Core(id, key, pmap, store, commitCh, logger);
            coreLock = new AsyncLock();
            PeerSelector = new RandomPeerSelector(participants, LocalAddr);
            selectorLock = new AsyncLock();
            Id = id;
            Store = store;
            Conf = conf;

            this.logger = logger.ForContext("node", LocalAddr);

            this.Trans = trans;
            netCh = trans.Consumer;
            this.proxy = proxy;
            this.participants = participants;
            submitCh = proxy.SubmitCh();
            controlTimer = ControlTimer.NewRandomControlTimer(conf.HeartbeatTimeout);

            nodeState = new NodeState();

            //Initialize as Babbling
            nodeState.SetStarting(true);
            nodeState.SetState(NodeStateEnum.Babbling);
        }

        public Exception Init(bool bootstrap)
        {
            var peerAddresses = new List<string>();
            foreach (var p in PeerSelector.Peers())
            {
                peerAddresses.Add(p.NetAddr);
            }

            logger.ForContext("peers", peerAddresses).Debug("Init Node");

            if (bootstrap)
            {
                return Core.Bootstrap();
            }

            return Core.Init();
        }

        public async Task RunAsync(bool gossip, CancellationToken ct = default)
        {
            cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            //The ControlTimer allows the background routines to control the
            //heartbeat timer when the node is in the Babbling state. The timer should
            //only be running when there are uncommitted transactions in the system.

            var timer = controlTimer.RunAsync(cts.Token);

            //Execute some background work regardless of the state of the node.
            //Process RPC requests as well as SumbitTx and CommitTx requests

            var backgroundWork = BackgroundWorkRunAsync(cts.Token);

            //Execute Node State Machine

            var stateMachine = StateMachineRunAsync(gossip, cts.Token);

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
                        await Babble(gossip, ct);
                        break;

                    case NodeStateEnum.CatchingUp:
                        await FastForward();
                        break;

                    case NodeStateEnum.Shutdown:
                        return;
                }
            }
        }

        public async Task BackgroundWorkRunAsync(CancellationToken ct)
        {
            async Task ProcessingRpc()

            {
                while (!ct.IsCancellationRequested)
                {
                    await netCh.OutputAvailableAsync(ct);
                    var rpc = await netCh.DequeueAsync(ct);
                    logger.Debug("Processing RPC");
                    await ProcessRpc(rpc, ct);
                }
            }

            ;

            async Task AddingTransactions()

            {
                while (!ct.IsCancellationRequested)
                {
                    await submitCh.OutputAvailableAsync(ct);
                    var tx = await submitCh.DequeueAsync(ct);
                    logger.Debug("Adding Transaction");
                    await AddTransaction(tx, ct);
                }
            }

            ;

            async Task CommitEvents()

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

            ;

            await Task.WhenAll(ProcessingRpc(), AddingTransactions(), CommitEvents());
        }

        private async Task Babble(bool gossip, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var oldState = nodeState.GetState();

                await controlTimer.TickCh.OutputAvailableAsync(ct);
                await controlTimer.TickCh.DequeueAsync(ct);

                if (gossip)
                {
                    var (proceed, err) = await PreGossip();
                    if (proceed && err == null)
                    {
                        logger.Debug("Time to gossip!");
                        var peer = PeerSelector.Next();
                        await Gossip(peer.NetAddr);
                    }

                    if (!Core.NeedGossip())
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
        }

        public async Task ProcessRpc(Rpc rpc, CancellationToken ct)
        {
            var s = nodeState.GetState();

            if (s != NodeStateEnum.Babbling)
            {
                logger.Debug("Discarding RPC Request {state}", s);
                var resp = new RpcResponse {Error = new NetError($"not ready: {s}"), Response = new SyncResponse {From = LocalAddr}};
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

        private async Task ProcessSyncRequest(Rpc rpc, SyncRequest cmd)
        {
            logger.ForContext("Fields", new
            {
                cmd.From,
                cmd.Known
            }).Debug("Process SyncRequest");

            var resp = new SyncResponse
            {
                From = LocalAddr
            };

            Exception respErr = null;

            //Check sync limit
            bool overSyncLimit;
            using (await coreLock.LockAsync())
            {
                overSyncLimit = Core.OverSyncLimit(cmd.Known, Conf.SyncLimit);
            }

            if (overSyncLimit)
            {
                logger.Debug("SyncLimit");
                resp.SyncLimit = true;
            }
            else
            {
                //Compute Diff
                var start = Stopwatch.StartNew();
                Event[] diff;
                Exception err;
                using (await coreLock.LockAsync())
                {
                    (diff, err) = Core.Diff(cmd.Known);
                }

                logger.Debug("Diff() duration={duration}", start.Nanoseconds());
                if (err != null)
                {
                    logger.Error("Calculating Diff {err}", err);
                    respErr = err;
                }

                //Convert to WireEvents
                WireEvent[] wireEvents;
                (wireEvents, err) = Core.ToWire(diff);
                if (err != null)
                {
                    logger.Debug("Converting to WireEvent {err}", err);
                    respErr = err;
                }
                else
                {
                    resp.Events = wireEvents;
                }
            }

            //Get Self Known
            Dictionary<int, int> known;
            using (await coreLock.LockAsync())
            {
                known = Core.Known().Clone();
            }

            resp.Known = known;

            logger.ForContext("Fields", new
            {
                Events = resp.Events.Length,
                resp.Known,
                resp.SyncLimit,
                Error = respErr
            }).Debug("Responding to SyncRequest");

            await rpc.RespondAsync(resp, respErr != null ? new NetError(resp.From, respErr) : null);
        }

        private async Task ProcessEagerSyncRequest(Rpc rpc, EagerSyncRequest cmd)
        {
            logger.ForContext("Fields", new
            {
                cmd.From,
                Events = cmd.Events.Length
            }).Debug("EagerSyncRequest");

            var success = true;

            Exception respErr;
            using (await coreLock.LockAsync())
            {
                respErr = await Sync(cmd.Events);
            }

            if (respErr != null)
            {
                logger.ForContext("error", respErr).Error("sync()");
                success = false;
            }

            var resp = new EagerSyncResponse
            {
                From = LocalAddr,
                Success = success
            };

            await rpc.RespondAsync(resp, respErr != null ? new NetError(resp.From, respErr) : null);
        }

        private async Task<(bool proceed, Exception error)> PreGossip()
        {
            using (await coreLock.LockAsync())
            {
                //Check if it is necessary to gossip
                var needGossip = Core.NeedGossip() || nodeState.IsStarting();
                if (!needGossip)
                {
                    logger.Debug("Nothing to gossip");
                    return (false, null);
                }

                //If the transaction pool is not empty, create a new self-event and empty the
                //transaction pool in its payload
                var err = Core.AddSelfEvent();
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
                PeerSelector.UpdateLast(peerAddr);
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
                known = Core.Known();
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
                err = await Sync(resp.Events);
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
                overSyncLimit = Core.OverSyncLimit(known, Conf.SyncLimit);
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
                (diff, err) = Core.Diff(known);
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
            (wireEvents, err) = Core.ToWire(diff);
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

        private Task FastForward()
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
                From = LocalAddr,
                Known = known
            };

            var (resp, err) = await Trans.Sync(target, args);
            return (resp, err);
        }

        private async Task<(EagerSyncResponse resp, Exception err)> requestEagerSync(string target, WireEvent[] events)
        {
            var args = new EagerSyncRequest
            {
                From = LocalAddr,
                Events = events
            };

            var (resp, err) = await Trans.EagerSync(target, args);
            return (resp, err);
        }

        public Task<Exception> Sync(WireEvent[] events)
        {
            //Insert Events in Hashgraph and create new Head if necessary
            var start = new Stopwatch();
            var err = Core.Sync(events);

            var elapsed = start.Nanoseconds();

            logger.Debug("Processed Sync() {duration}", elapsed);
            if (err != null)
            {
                return Task.FromResult(err);
            }

            //Run consensus methods
            start = new Stopwatch();
            err = Core.RunConsensus();

            elapsed = start.Nanoseconds();
            logger.Debug("Processed RunConsensus() {duration}", elapsed);
            return Task.FromResult(err);
        }

        private  Task<Exception> Commit(Event[] events, CancellationToken ct)
        {
            foreach (var ev in events)
            {
                foreach (var tx in ev.Transactions())
                {
                    var err = proxy.CommitTx(tx);
                    if (err != null)
                    {
                        return Task.FromResult<Exception>(err);
                    }
                }
            }

            return Task.FromResult<Exception>(null);
        }

        private async Task AddTransaction(byte[] tx, CancellationToken ct)
        {
            using (await coreLock.LockAsync())
            {
                Core.AddTransactions(new[] {tx});
            }
        }

        public void Shutdown()

        {
            if (nodeState.GetState() != NodeStateEnum.Shutdown)
            {
                logger.Debug("Shutdown");

                //Exit any non-shutdown state immediately
                nodeState.SetState(NodeStateEnum.Shutdown);

                //Stop and wait for concurrent operations
                cts.Cancel();

                //transport and store should only be closed once all concurrent operations
                //are finished otherwise they will panic trying to use close objects
                Trans.Close();
                Core.hg.Store.Close();
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