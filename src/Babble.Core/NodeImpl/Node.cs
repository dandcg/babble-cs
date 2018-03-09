using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Babble.Core.HashgraphImpl.Model;
using Babble.Core.HashgraphImpl.Stores;
using Babble.Core.NetImpl;
using Babble.Core.NetImpl.PeerImpl;
using Babble.Core.NetImpl.TransportImpl;
using Babble.Core.NodeImpl.PeerSelector;
using Babble.Core.ProxyImpl;
using Babble.Core.Util;
using Nito.AsyncEx;
using Serilog;

namespace Babble.Core.NodeImpl
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
        public IAppProxy Proxy { get; }
        public Controller Controller { get; }
        private readonly AsyncLock coreLock;
        public string LocalAddr { get; }
        private readonly ILogger logger;
        public IPeerSelector PeerSelector { get; }
        private readonly AsyncLock selectorLock;
        private readonly ControlTimer controlTimer;
        private readonly AsyncProducerConsumerQueue<byte[]> submitCh;
        private readonly AsyncProducerConsumerQueue<Block> commitCh;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private Task nodeTask;

        public Node(Config conf, int id, CngKey key, Peer[] participants, IStore store, ITransport trans, IAppProxy proxy, ILogger logger)

        {
            LocalAddr = trans.LocalAddr;

            var (pmap, _) = store.Participants();

            commitCh = new AsyncProducerConsumerQueue<Block>(400);

            Controller = new Controller(id, key, pmap, store, commitCh, logger);
            coreLock = new AsyncLock();
            PeerSelector = new RandomPeerSelector(participants, LocalAddr);
            selectorLock = new AsyncLock();
            Id = id;
            Store = store;
            Conf = conf;

            this.logger = logger.AddNamedContext("Node", Id.ToString());

            Trans = trans;
            netCh = trans.Consumer;
            Proxy = proxy;
            this.participants = participants;
            submitCh = proxy.SubmitCh();
            controlTimer = ControlTimer.NewRandomControlTimer(conf.HeartbeatTimeout);

            nodeState = new NodeState();

            //Initialize as Babbling
            nodeState.SetStarting(true);
            nodeState.SetState(NodeStateEnum.Babbling);
        }

        public async Task<Exception> Init(bool bootstrap)
        {
            var peerAddresses = new List<string>();
            foreach (var p in PeerSelector.Peers())
            {
                peerAddresses.Add(p.NetAddr);
            }

            logger.ForContext("peers", peerAddresses).Debug("Init Node");

            if (bootstrap)
            {
                return await Controller.Bootstrap();
            }

            return await Controller.Init();
        }

        public Task StartAsync(bool gossip, CancellationToken ct = default)
        {
            var tcsInit = new TaskCompletionSource<bool>();

            nodeTask = Task.Run(async () =>
            {
                //The ControlTimer allows the background routines to control the
                //heartbeat timer when the node is in the Babbling state. The timer should
                //only be running when there are uncommitted transactions in the system.

                var controlTimerTask = controlTimer.RunAsync(cts.Token);

                //Execute some background work regardless of the state of the node.
                //Process RPC requests as well as SumbitTx and CommitBlock requests
                var tcsBackgroundWork = new TaskCompletionSource<bool>();
                var backgroundWorkTask = BackgroundWorkRunAsync(cts.Token, tcsBackgroundWork);

                //Execute Node State Machine

                var stateMachineTask = StateMachineRunAsync(gossip, cts.Token);

                var runTask = Task.WhenAll(controlTimerTask, stateMachineTask, backgroundWorkTask);

                await tcsBackgroundWork.Task;

                tcsInit.SetResult(true);

                await runTask;
            }, ct);

            return tcsInit.Task;
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
                        await BabbleAsync(gossip, ct);
                        break;

                    case NodeStateEnum.CatchingUp:
                        await FastForward();
                        break;

                    case NodeStateEnum.Shutdown:
                        return;
                }
            }
        }

        public async Task BackgroundWorkRunAsync(CancellationToken ct, TaskCompletionSource<bool> tcs)
        {
            var tcsProcessingRpc = new TaskCompletionSource<bool>();

            async Task ProcessingRpc()
            {
                tcsProcessingRpc.SetResult(true);

                while (!ct.IsCancellationRequested)
                {
                    await netCh.OutputAvailableAsync(ct);
                    var rpc = await netCh.DequeueAsync(ct);
                    logger.Debug("Processing RPC");
                    await ProcessRpcAsync(rpc, ct);
                }
            }

            var tcsAddingTransactions = new TaskCompletionSource<bool>();

            async Task AddingTransactions()

            {
                tcsAddingTransactions.SetResult(true);

                while (!ct.IsCancellationRequested)
                {
                    await submitCh.OutputAvailableAsync(ct);
                    var tx = await submitCh.DequeueAsync(ct);
                    logger.Debug("Adding Transaction");
                    await AddTransaction(tx, ct);
                }
            }

            ;
            var tcsCommitBlocks = new TaskCompletionSource<bool>();

            async Task CommitBlocks()
            {
                tcsCommitBlocks.SetResult(true);

                while (!ct.IsCancellationRequested)
                {
                    await commitCh.OutputAvailableAsync(ct);
                    var block = await commitCh.DequeueAsync(ct);
                    logger.Debug("Committing Block Index={Index}; RoundReceived={RoundReceived}; TxCount={TxCount}", block.Index(), block.RoundReceived(), block.Transactions().Length);
                    var err = await Commit(block, ct);
                    if (err != null)
                    {
                        logger.Error("Committing Block", err);
                    }
                }
            }

            var task = Task.WhenAll(ProcessingRpc(), AddingTransactions(), CommitBlocks());

            await Task.WhenAll(tcsProcessingRpc.Task, tcsAddingTransactions.Task, tcsCommitBlocks.Task);

            tcs.SetResult(true);

            await task;
        }

        private async Task BabbleAsync(bool gossip, CancellationToken ct)
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

                    if (!Controller.NeedGossip())
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

        public async Task ProcessRpcAsync(Rpc rpc, CancellationToken ct)
        {
            var s = nodeState.GetState();

            if (s != NodeStateEnum.Babbling)
            {
                logger.Debug("Discarding RPC Request {state}", s);
                var resp = new RpcResponse {Error = new NetError($"not ready: {s}"), Response = new SyncResponse {FromId = Id}};
                await rpc.RespChan.EnqueueAsync(resp, ct);
            }
            else
            {
                switch (rpc.Command)
                {
                    case SyncRequest cmd:
                        await ProcessSyncRequestAsync(rpc, cmd);
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

        private async Task ProcessSyncRequestAsync(Rpc rpc, SyncRequest cmd)
        {
            logger.Debug("Process SyncRequest FromId={FromId}; Known={@Known};", cmd.FromId, cmd.Known);

            var resp = new SyncResponse
            {
                FromId = Id
            };

            Exception respErr = null;

            //Check sync limit
            bool overSyncLimit;
            using (await coreLock.LockAsync())
            {
                overSyncLimit = await Controller.OverSyncLimit(cmd.Known, Conf.SyncLimit);
            }

            if (overSyncLimit)
            {
                logger.Debug("SyncLimit");
                resp.SyncLimit = true;
            }
            else
            {
                //Compute EventDiff
                var start = Stopwatch.StartNew();
                Event[] diff;
                Exception err;
                using (await coreLock.LockAsync())
                {
                    (diff, err) = await Controller.EventDiff(cmd.Known);
                }

                logger.Debug("EventDiff() duration={duration}", start.Nanoseconds());
                if (err != null)
                {
                    logger.Error("Calculating EventDiff {err}", err);
                    respErr = err;
                }

                //Convert to WireEvents
                WireEvent[] wireEvents;
                (wireEvents, err) = Controller.ToWire(diff);
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

            //Get Self KnownEvents
            Dictionary<int, int> known;
            using (await coreLock.LockAsync())
            {
                known = (await Controller.KnownEvents()).Clone();
            }

            resp.Known = known;

            logger.Debug("Responding to SyncRequest {SyncRequest}", new
            {
                Events = resp.Events.Length,
                resp.Known,
                resp.SyncLimit,
                Error = respErr
            });

            await rpc.RespondAsync(resp, respErr != null ? new NetError(resp.FromId.ToString(), respErr) : null);
        }

        private async Task ProcessEagerSyncRequest(Rpc rpc, EagerSyncRequest cmd)
        {
            logger.Debug("EagerSyncRequest {EagerSyncRequest}", new
            {
                cmd.FromId,
                Events = cmd.Events.Length
            });

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
                FromId = Id,
                Success = success
            };

            await rpc.RespondAsync(resp, respErr != null ? new NetError(resp.FromId.ToString(), respErr) : null);
        }

        private async Task<(bool proceed, Exception error)> PreGossip()
        {
            using (await coreLock.LockAsync())
            {
                //Check if it is necessary to gossip
                var needGossip = Controller.NeedGossip() || nodeState.IsStarting();
                if (!needGossip)
                {
                    logger.Debug("Nothing to gossip");
                    return (false, null);
                }

                //If the transaction pool is not empty, create a new self-event and empty the
                //transaction pool in its payload
                var err = await Controller.AddSelfEvent();
                if (err != null)
                {
                    logger.Error("Adding SelfEvent", err);
                    return (false, err);
                }
            }

            return (true, null);
        }

        public async Task<Exception> Gossip(string peerAddr)
        {
            //Pull
            var (syncLimit, otherKnownEvents, err) = await Pull(peerAddr);
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

            //Push
            err = await Push(peerAddr, otherKnownEvents);
            if (err != null)
            {
                return err;
            }

            //update peer selector
            using (selectorLock.Lock())
            {
                PeerSelector.UpdateLast(peerAddr);
            }

            LogStats();

            nodeState.SetStarting(false);

            return null;
        }

        private async Task<( bool syncLimit, Dictionary<int, int> otherKnown, Exception err)> Pull(string peerAddr)
        {
            //Compute KnownEvents
            Dictionary<int, int> knownEvents;
            using (await coreLock.LockAsync())
            {
                knownEvents = await Controller.KnownEvents();
            }

            //Send SyncRequest
            var start = new Stopwatch();

            var (resp, err) = await RequestSync(peerAddr, knownEvents);
            var elapsed = start.Nanoseconds();
            logger.Debug("requestSync() Duration = {duration}", elapsed);
            if (err != null)
            {
                logger.Error("requestSync()", err);
                return (false, null, err);
            }

            logger.Debug("SyncResponse {@SyncResponse}", new {resp.FromId, resp.SyncLimit, Events = resp.Events.Length, resp.Known});

            if (resp.SyncLimit)
            {
                return (true, null, null);
            }

            //Set Events to Hashgraph and create new Head if necessary
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

        private async Task<Exception> Push(string peerAddr, Dictionary<int, int> knownEvents)
        {
            //Check SyncLimit
            bool overSyncLimit;
            using (await coreLock.LockAsync())
            {
                overSyncLimit = await Controller.OverSyncLimit(knownEvents, Conf.SyncLimit);
            }

            if (overSyncLimit)
            {
                logger.Debug("SyncLimit");
                return null;
            }

            //Compute EventDiff
            var start = new Stopwatch();

            Event[] diff;
            Exception err;
            using (await coreLock.LockAsync())
            {
                (diff, err) = await Controller.EventDiff(knownEvents);
            }

            var elapsed = start.Nanoseconds();
            logger.Debug("EventDiff() {duration}", elapsed);
            if (err != null)
            {
                logger.Error("Calculating EventDiff", err);
                return err;
            }

            //Convert to WireEvents
            WireEvent[] wireEvents;
            (wireEvents, err) = Controller.ToWire(diff);
            if (err != null)
            {
                logger.Debug("Converting to WireEvent", err);
                return err;
            }

            //Create and Send EagerSyncRequest
            start = new Stopwatch();

            EagerSyncResponse resp2;
            (resp2, err) = await RequestEagerSync(peerAddr, wireEvents);
            elapsed = start.Nanoseconds();
            logger.Debug("requestEagerSync() {duration}", elapsed);
            if (err != null)
            {
                logger.Error("requestEagerSync()", err);
                return err;
            }

            logger.Debug("EagerSyncResponse {@EagerSyncResponse}", new {resp2.FromId, resp2.Success});

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

        private async Task<(SyncResponse resp, Exception err)> RequestSync(string target, Dictionary<int, int> known)
        {
            var args = new SyncRequest
            {
                FromId = Id,
                Known = known
            };

            var (resp, err) = await Trans.Sync(target, args);
            return (resp, err);
        }

        private async Task<(EagerSyncResponse resp, Exception err)> RequestEagerSync(string target, WireEvent[] events)
        {
            var args = new EagerSyncRequest
            {
                FromId = Id,
                Events = events
            };

            var (resp, err) = await Trans.EagerSync(target, args);
            return (resp, err);
        }

        public async Task<Exception> Sync(WireEvent[] events)
        {
            //Insert Events in Hashgraph and create new Head if necessary
            var start = new Stopwatch();
            var err = await Controller.Sync(events);

            var elapsed = start.Nanoseconds();

            logger.Debug("Processed Sync() {duration}", elapsed);
            if (err != null)
            {
                return err;
            }

            //Run consensus methods
            start = Stopwatch.StartNew();
            err = await Controller.RunConsensus();

            elapsed = start.Nanoseconds();
            logger.Debug("Processed RunConsensus() {duration}", elapsed);
            return err;
        }

        private async Task<Exception> Commit(Block block, CancellationToken ct)
        {
            Exception err;
            byte[] stateHash;
            (stateHash, err) = Proxy.CommitBlock(block);

            logger.Debug("CommitBlockResponse {@CommitBlockResponse}", new {Index = block.Index(), StateHash = stateHash.ToHex(), Err = err});

            block.Body.StateHash = stateHash;

            using (await coreLock.LockAsync())
            {
                BlockSignature sig;
                (sig, err) = await Controller.SignBlock(block);
                if (err != null)
                {
                    return err;
                }

                Controller.AddBlockSignature(sig);

                return null;
            }
        }

        private async Task AddTransaction(byte[] tx, CancellationToken ct)
        {
            using (await coreLock.LockAsync())
            {
                Controller.AddTransactions(new[] {tx});
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

                try
                {
                    nodeTask?.Wait();
                }
                catch (AggregateException e) when (e.InnerException is OperationCanceledException)
                {           
                }
                catch (AggregateException e)
                {
                    logger.Error(e,"Application termination ");
                }
                finally
                {
                    cts.Dispose();
                }

                //transport and store should only be closed once all concurrent operations
                //are finished otherwise they will panic trying to use close objects
                Trans.Close();
                Controller.Hg.Store.Close();
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

        private void LogStats()
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