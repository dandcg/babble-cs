using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Babble.Core.Common;
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

        public IAppProxy Proxy { get; }
        public Controller Controller { get; }

        public string LocalAddr { get; }
        private readonly ILogger logger;
        public IPeerSelector PeerSelector { get; }

        private readonly AsyncLock coreLock;
        private readonly AsyncLock selectorLock;
        private readonly ControlTimer controlTimer;


        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private Task nodeTask;


        private readonly AsyncProducerConsumerQueue<byte[]> submitCh;
        private readonly AsyncMonitor submitChMonitor;
        private readonly AsyncProducerConsumerQueue<Rpc> netCh;
        private readonly AsyncMonitor netChMonitor;
        private readonly AsyncProducerConsumerQueue<Block> commitCh;
        private readonly AsyncMonitor commitChMonitor;

        private Stopwatch nodeStart;
        private int syncRequests;
        private int syncErrors;

        public Node(Config conf, int id, CngKey key, Peer[] participants, IStore store, ITransport trans, IAppProxy proxy, ILogger loggerIn)

        {
            logger = loggerIn.AddNamedContext("Node", id.ToString());

            LocalAddr = trans.LocalAddr;

            var (pmap, _) = store.Participants();

            commitCh = new AsyncProducerConsumerQueue<Block>(400);
            commitChMonitor = new AsyncMonitor();

            Controller = new Controller(id, key, pmap, store, commitCh, logger);
            coreLock = new AsyncLock();
            PeerSelector = new RandomPeerSelector(participants, LocalAddr);
            selectorLock = new AsyncLock();
            Id = id;
            Store = store;
            Conf = conf;



            Trans = trans;

            netCh = trans.Consumer;
            netChMonitor = new AsyncMonitor();

            Proxy = proxy;

            this.participants = participants;

            submitCh = proxy.SubmitCh();
            submitChMonitor = new AsyncMonitor();

            controlTimer = ControlTimer.NewRandomControlTimer(conf.HeartbeatTimeout);

            nodeState = new NodeState();

            //Initialize as Babbling
            nodeState.SetStarting(true);
            nodeState.SetState(NodeStateEnum.Babbling);
        }

        public Task<Exception> Init(bool bootstrap)
        {
            var peerAddresses = new List<string>();
            foreach (var p in PeerSelector.Peers())
            {
                peerAddresses.Add(p.NetAddr);
            }

            logger.Debug("Init Node Peers={@peerAddresses}", peerAddresses);

            if (bootstrap)
            {
                return Controller.Bootstrap();
            }

            return Controller.Init();
        }

        public Task StartAsync(bool gossip, CancellationToken ct = default)
        {
            var tcsInit = new TaskCompletionSource<bool>();

            nodeStart = Stopwatch.StartNew();

            nodeTask = Task.Run(async () =>
            {
                //The ControlTimer allows the background routines to control the
                //heartbeat timer when the node is in the Babbling state. The timer should
                //only be running when there are uncommitted transactions in the system.

                var controlTimerTask = controlTimer.RunAsync(cts.Token);

                //Execute some background work regardless of the state of the node.
                //Process RPC requests as well as SumbitTx and CommitBlock requests

                var processingRpcTask = ProcessingRpc(cts.Token);
                var addingTransactions = AddingTransactions(cts.Token);
                var commitBlocks = CommitBlocks(cts.Token);

                //Execute Node State Machine

                var stateMachineTask = StateMachineRunAsync(gossip, cts.Token);

                // await all

                var runTask = Task.WhenAll(controlTimerTask, stateMachineTask, processingRpcTask, addingTransactions, commitBlocks);

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

        // Background work

        private async Task ProcessingRpc(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var rpc = await netCh.DequeueAsync(ct);
                logger.Debug("Processing RPC");
                await ProcessRpcAsync(rpc, ct);

                using (await netChMonitor.EnterAsync())
                {
                    netChMonitor.Pulse();
                }
            }
        }

        public async Task ProcessingRpcCompleted()
        {
            using (await netChMonitor.EnterAsync())
            {
                await netChMonitor.WaitAsync();
            }
        }


        private async Task AddingTransactions(CancellationToken ct)

        {
            while (!ct.IsCancellationRequested)
            {
                var tx = await submitCh.DequeueAsync(ct);

                logger.Debug("Adding Transaction");
                await AddTransaction(tx, ct);

                using (await submitChMonitor.EnterAsync())
                {
                    submitChMonitor.Pulse();
                }
            }
        }


        public async Task AddingTransactionsCompleted()
        {
            using (await submitChMonitor.EnterAsync())
            {
                await submitChMonitor.WaitAsync();
            }
        }

        private async Task CommitBlocks(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var block = await commitCh.DequeueAsync(ct);
                logger.Debug("Committing Block Index={Index}; RoundReceived={RoundReceived}; TxCount={TxCount}", block.Index(), block.RoundReceived(), block.Transactions().Length);

                var err = await Commit(block);
                if (err != null)
                {
                    logger.Error("Committing Block", err);
                }

                using (await commitChMonitor.EnterAsync())
                {
                    commitChMonitor.Pulse();
                }
            }
        }


        public async Task CommitBlocksCompleted()
        {
            using (await commitChMonitor.EnterAsync())
            {
                await commitChMonitor.WaitAsync();
            }
        }

        private async Task BabbleAsync(bool gossip, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var oldState = nodeState.GetState();

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

            logger.Debug("Responding to SyncRequest {@SyncRequest}", new
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
            using (await selectorLock.LockAsync())
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
            var start = Stopwatch.StartNew();
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

        private async Task<Exception> Commit(Block block)
        {
            Exception err;
            byte[] stateHash;
            (stateHash, err) = await Proxy.CommitBlock(block);

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
                    logger.Error(e, "Application termination ");
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

            var timeElapsed = nodeStart.Elapsed;

            var consensusEvents = Controller.GetConsensusEventsCount();

            var consensusEventsPerSecond = (decimal) (consensusEvents) / timeElapsed.Seconds;

            var lastConsensusRound = Controller.GetLastConsensusRoundIndex();
            decimal consensusRoundsPerSecond = 0;


            if (lastConsensusRound != null)
            {
                consensusRoundsPerSecond = (decimal) (lastConsensusRound) / timeElapsed.Seconds;

            }

            var s = new Dictionary<string, string>
            {

                {"last_consensus_round", lastConsensusRound.ToString()},
                {"consensus_events", consensusEvents.ToString()},
                {"consensus_transactions", Controller.GetConsensusTransactionsCount().ToString()},
                {"undetermined_events", Controller.GetUndeterminedEvents().Length.ToString()},
                {"transaction_pool", Controller.TransactionPool.Count.ToString()},
                {"num_peers", PeerSelector.Peers().Length.ToString()},
                {"sync_rate", SyncRate().ToString("0.00")},
                {"events_per_second", consensusEventsPerSecond.ToString("0.00")},
                {"rounds_per_second", consensusRoundsPerSecond.ToString("0.00")},
                {"round_events", Controller.GetLastCommitedRoundEventsCount().ToString()},
                {"id", Id.ToString()},
                {"state", nodeState.GetState().ToString()},
            };
            return s;
        }

        private void LogStats()
        {
            var stats = GetStats();
            logger.Debug("Stats {@stats}", stats);
        }

        public decimal SyncRate()
        {
            decimal syncErrorRate = 0;
            if (syncRequests != 0)
            {
                syncErrorRate = (decimal) syncErrors / (decimal) syncRequests;
            }

            return (1 - syncErrorRate);


        }


        public Task<(Block block, StoreError err )> GetBlock(int blockIndex)
        {
            return Controller.Hg.Store.GetBlock(blockIndex);


        }

    }
}