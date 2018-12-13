using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Babble.Core.Crypto;
using Babble.Core.HashgraphImpl;
using Babble.Core.HashgraphImpl.Model;
using Babble.Core.HashgraphImpl.Stores;
using Babble.Core.PeersImpl;
using Babble.Core.Util;
using Nito.AsyncEx;
using Serilog;

namespace Babble.Core.NodeImpl
{
    public class Controller
    {
        private readonly int id;
        public CngKey Key { get; }
        private byte[] pubKey;
        private string hexId;
        public readonly Hashgraph Hg;
        private readonly Peers participants;
        private readonly AsyncProducerConsumerQueue<Block> commitCh;
        public string Head { get; private set; }
        public int Seq { get; private set; }
        public List<byte[]> TransactionPool { get; } = new List<byte[]>();
        public List<BlockSignature> BlockSignaturePool { get; } = new List<BlockSignature>();

        private readonly ILogger logger;

        public Controller(int id, CngKey key, Peers participants, IStore store, AsyncProducerConsumerQueue<Block> commitCh, ILogger loggerIn)
        {
            logger = loggerIn.AddNamedContext("Controller", id.ToString());

            this.id = id;
            Key = key;
            this.participants = participants;
            this.commitCh = commitCh;

            Hg = new Hashgraph(participants, store, commitCh, logger);
        }

        public int Id()
        {
            return id;
        }

        public byte[] PubKey()
        {
            return pubKey ?? (pubKey = CryptoUtils.FromEcdsaPub(Key));
        }

        public string HexId()
        {
            if (!string.IsNullOrEmpty(hexId))
            {
                return hexId;
            }

            pubKey = PubKey();
            hexId = pubKey.ToHex();

            return hexId;
        }

        public async Task<BabbleError> SetHeadAndSeq()
        {
            string head = null;
            int seq = 0;

            var (last, isRoot, err1) = Hg.Store.LastEventFrom(HexId());

            if (err1 != null)
            {
                return err1;
            }

            if (isRoot)
            {
                var (root, err2) = await Hg.Store.GetRoot(HexId());
                if (err2 != null)
                {
                    return err2;
                }

                head = root.SelfParent.Hash;
                seq = root.SelfParent.Index;
            }
            else
            {
                var (lastEvent, err3) = await GetEvent(last);
                if (err3 != null)
                {
                    return err3;
                }

                head = last;
                seq = lastEvent.Index();
            }

            Head = head;
            Seq = seq;

            logger.Debug("SetHeadAndSeq: core.Head = {Head}, core.Seq = {Seq}, is_root = {isRoot}", Head, Seq, isRoot);

            return null;
        }

      public Task<BabbleError>  Bootstrap()
      {
          return Hg.Bootstrap();
      }




        public async Task<BabbleError> SignAndInsertSelfEvent(Event ev)
        {
            BabbleError err = ev.Sign(Key);

            if (err != null)
            {
                return err;
            }

            err = await InsertEvent(ev, true);

            return err;
        }

        public async Task<BabbleError> InsertEvent(Event ev, bool setWireInfo)
        {
            var err = await Hg.InsertEvent(ev, setWireInfo);

            if (err != null)
            {
                return err;
            }

            if (ev.Creator() == HexId())
            {
                Head = ev.Hex();
                Seq = ev.Index();
            }

            return null;
        }

        public Task<Dictionary<int, int>> KnownEvents()
        {
            return Hg.Store.KnownEvents();
        }

        //++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

        public async Task<(BlockSignature bs, BabbleError err)> SignBlock(Block block)
        {
            var (sig, err) = block.Sign(Key);
            if (err != null)
            {
                return (new BlockSignature(), err);
            }

            err = block.SetSignature(sig);

            if (err != null)
            {
                return (new BlockSignature(), err);
            }

            return (sig, await Hg.Store.SetBlock(block));
        }

        //++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

        public async Task<bool> OverSyncLimit(Dictionary<int, int> known, int syncLimit)
        {
            var totUnknown = 0;
            var myKnownEvents = await KnownEvents();

            foreach (var kn in myKnownEvents)
            {
                var i = kn.Key;
                var li = kn.Value;

                if (li > known[i])
                {
                    totUnknown += li - known[i];
                }
            }

            if (totUnknown > syncLimit)
            {
                return true;
            }

            return false;
        }

        public Task<(Block block, Frame frame, BabbleError err)> GetAnchorBlockWithFrame()
        {
            return Hg.GetAnchorBlockWithFrame();
        }

        //returns events that c knowns about that are not in 'known'
        public async Task<(Event[] events, BabbleError err)> EventDiff(Dictionary<int, int> known)
        {
            var unknown = new List<Event>();

            //known represents the index of the last event known for every participant
            //compare this to our view of events and fill unknownEvents with events that we know of
            // and the other doesnt

            foreach (var kn in known)
            {
                var idl = kn.Key;
                var ct = kn.Value;

                var peer = participants.ById[idl];

                //get participant Events with index > ct
                var (participantEvents, err) = await Hg.Store.ParticipantEvents(peer.PubKeyHex, ct);

                if (err != null)
                {
                    return (new Event[] { }, err);
                }

                foreach (var e in participantEvents)
                {
                    Event ev;
                    (ev, err) = await Hg.Store.GetEvent(e);
                    if (err != null)
                    {
                        return (new Event[] { }, err);
                    }

                    unknown.Add(ev);
                }
            }

            unknown.Sort(new Event.EventByTopologicalOrder());

            return (unknown.ToArray(), null);
        }

        public async Task<BabbleError> Sync(WireEvent[] unknownEvents)
        {
            logger.Debug("Sync unknownEvents={@unknownEvents}; transactionPool={transactionPoolCount}; blockSignaturePool={blockSignaturePoolCount}", unknownEvents.Length, TransactionPool.Count, BlockSignaturePool.Count);

            using (var tx = Hg.Store.BeginTx())
            {
                string otherHead = "";

                //add unknownEvents events
                int k = 0;

                foreach (var we in unknownEvents)

                {
                    //logger.Debug("wev={wev}",we.Body.CreatorId);

                    var (ev, err1) = await Hg.ReadWireInfo(we);

                    if (err1 != null)
                    {
                        return err1;
                    }

                    //logger.Debug("ev={ev}",ev.Creator());

                    var err2 = await InsertEvent(ev, false);

                    if (err2 != null)
                    {
                        return err2;
                    }

                    //assume last event corresponds to other-head
                    if (k == unknownEvents.Length - 1)
                    {
                        otherHead = ev.Hex();
                    }

                    k++;
                }

                //create new event with self head and other head only if there are pending
                //loaded events or the pools are not empty
                return await AddSelfEvent(otherHead);
            }
        }

        public async Task<BabbleError> FastForward(string peer, Block block, Frame frame)
        {
            //Check Block Signatures
            var err1 = Hg.CheckBlock(block);
            if (err1 != null)
            {
                return err1;
            }

            //Check Frame Hash
            var frameHash = frame.Hash();

            if (block.FrameHash().GetHashCode() != frameHash.GetHashCode())
            {
                return new CoreError("Invalid Frame Hash");
            }

            var err3 = await Hg.Reset(block, frame);
            if (err3 != null)
            {
                return err3;
            }

            var err4 = await SetHeadAndSeq();
            if (err4 != null)
            {
                return err4;
            }

            // lastEventFromPeer, _, err := c.hg.Store.LastEventFrom(peer)
            // if err != nil {
            // 	return err
            // }

            // err = c.AddSelfEvent(lastEventFromPeer)
            // if err != nil {
            // 	return err
            // }

            var err5 = await RunConsensus();
            if (err5 != null)
            {
                return err5;
            }

            return null;
        }

        public async Task<BabbleError> AddSelfEvent(string otherHead)
        {
            if (otherHead == "" && TransactionPool.Count == 0 && BlockSignaturePool.Count == 0)
            {
                logger.Debug("Empty transaction pool and block signature pool");
                return null;
            }

            //create new event with self head and empty other parent
            //empty transaction pool in its payload
            var newHead = new Event(TransactionPool.ToArray(), BlockSignaturePool.ToArray(),
                new[] {Head, otherHead},
                PubKey(), Seq + 1);

            var err1 = await SignAndInsertSelfEvent(newHead);

            if (err1 != null)
            {
                return new CoreError($"Error inserting new head: {err1.Message}");
            }

            logger.Debug("Created Self-Event Transactions={TransactionCount}; BlockSignatures={BlockSignatureCount}", TransactionPool.Count, BlockSignaturePool.Count);

            TransactionPool.Clear();
            BlockSignaturePool.Clear();

            return null;
        }

        public async Task<(Event[] events, BabbleError err)> FromWire(WireEvent[] wireEvents)
        {
            var events = new List<Event>(wireEvents.Length);

            foreach (var w in wireEvents)
            {
                var (ev, err) = await Hg.ReadWireInfo(w);
                if (err != null)
                {
                    return (null, err);
                }

                events.Add(ev);
            }

            return (events.ToArray(), null);
        }

        public (WireEvent[] wireEvents, BabbleError err) ToWire(Event[] events)
        {
            var wireEvents = new List<WireEvent>(events.Length);
            foreach (var e in events)
            {
                wireEvents.Add(e.ToWire());
            }

            return (wireEvents.ToArray(), null);
        }

        public async Task<BabbleError> RunConsensus()
        {
            using (var tx = Hg.Store.BeginTx())
            {
                // DivideRounds

                var watch1 = Stopwatch.StartNew();
                var err1 = await Hg.DivideRounds();
                watch1.Stop();

                logger.Debug("DivideRounds() Duration={DivideRoundsDuration}", watch1.Nanoseconds());

                if (err1 != null)
                {
                    logger.Error("DivideRounds Error={@err}", err1);
                    return err1;
                }

                // DecideFrame

                var watch2 = Stopwatch.StartNew();
                var err2 = await Hg.DecideFame();
                watch2.Stop();

                logger.Debug("DecideFame() Duration={DecideFameDuration}", watch2.Nanoseconds());

                if (err2 != null)
                {
                    logger.Error("DecideFame Error={@err}", err2);
                    return err2;
                }

                var watch3 = Stopwatch.StartNew();

                var err3 = await Hg.DecideRoundReceived();

                watch3.Stop();

                logger.Debug("DecideRoundReceived() Duration={DecideFameDuration}", watch3.Nanoseconds());

                if (err3 != null)
                {
                    logger.Error("DecideRoundReceived Error={@err}", err3);
                    return err3;
                }

                var watch4 = Stopwatch.StartNew();

                var err4 = await Hg.ProcessDecidedRounds();

                watch4.Stop();

                logger.Debug("ProcessDecidedRounds() Duration={DecideFameDuration}", watch3.Nanoseconds());

                if (err4 != null)
                {
                    logger.Error("ProcessDecidedRounds Error={@err}", err4);
                    return err4;
                }

                var watch5 = Stopwatch.StartNew();

                var err5 = await Hg.ProcessSigPool();

                watch5.Stop();

                logger.Debug("ProcessSigPool() Duration={DecideFameDuration}", watch5.Nanoseconds());

                if (err5 != null)
                {
                    logger.Error("ProcessSigPool Error={@err}", err5);
                    return err5;
                }

                return null;
            }
        }

        public void AddTransactions(byte[][] txs)
        {
            TransactionPool.AddRange(txs);
        }

        public void AddBlockSignature(BlockSignature bs)
        {
            BlockSignaturePool.Add(bs);
        }

        public async Task<(Event ev, BabbleError err)> GetHead()
        {
            return await Hg.Store.GetEvent(Head);
        }

        public async Task<(Event ev, BabbleError err)> GetEvent(string hash)
        {
            return await Hg.Store.GetEvent(hash);
        }

        public async Task<(byte[][] txs, BabbleError err)> GetEventTransactions(string hash)
        {
            var (ex, err) = await GetEvent(hash);
            if (err != null)
            {
                return (new byte[][] { }, err);
            }

            var txs = ex.Transactions();
            return (txs, null);
        }

        public string[] GetConsensusEvents()
        {
            return Hg.Store.ConsensusEvents();
        }

        public int GetConsensusEventsCount()
        {
            return Hg.Store.ConsensusEventsCount();
        }

        public string[] GetUndeterminedEvents()
        {
            return Hg.UndeterminedEvents.ToArray();
        }

        public int GetPendingLoadedEvents()
        {
            return Hg.PendingLoadedEvents;
        }

        public async Task<(byte[][] txs, BabbleError err)> GetConsensusTransactions()
        {
            var txs = new List<byte[]>();
            foreach (var e in GetConsensusEvents())
            {
                var (eTxs, err) = await GetEventTransactions(e);
                if (err != null)
                {
                    return (txs.ToArray(), new CoreError($"Consensus event not found: {e}"));
                }

                txs.AddRange(eTxs);
            }

            return (txs.ToArray(), null);
        }

        public int? GetLastConsensusRoundIndex()
        {
            return Hg.LastConsensusRound;
        }

        public int GetConsensusTransactionsCount()
        {
            return Hg.ConsensusTransactions;
        }

        public int GetLastCommitedRoundEventsCount()
        {
            return Hg.LastCommitedRoundEvents;
        }

        public Task<int> GetLastBlockIndex()
        {
            return Hg.Store.LastBlockIndex();
        }

        public bool NeedGossip()
        {
            return Hg.PendingLoadedEvents > 0 || TransactionPool.Count > 0 || BlockSignaturePool.Count > 0;
        }
    }
}