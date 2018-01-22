using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using Dotnatter.Crypto;
using Dotnatter.HashgraphImpl;
using Dotnatter.HashgraphImpl.Model;
using Dotnatter.Util;
using Serilog;

namespace Dotnatter.NodeImpl
{
    public class Core
    {
        private readonly int id;
        private readonly CngKey key;
        private byte[] pubKey;
        private string hexId;
        private readonly Hashgraph hg;
        //private readonly Dictionary<string, int> participants;
        private readonly Dictionary<int, string> reverseParticipants;
        //private readonly IStore store;
        //private readonly Channel<Event> commitCh;

        public string Head { get; private set; }
        public int Seq { get; private set; }

        private readonly List<byte[]> transactionPool = new List<byte[]>();
        private readonly ILogger logger;

        public Core(int id, CngKey key, Dictionary<string, int> participants, IStore store, Channel<Event> commitCh, ILogger logger)
        {
            this.id = id;
            this.key = key;
            //this.participants = participants;
            //this.store = store;
            //this.commitCh = commitCh;
            this.logger = logger.ForContext("SourceContext", "Core");

            reverseParticipants= new Dictionary<int, string>();
            foreach (var p in participants)
            {
                reverseParticipants.Add(p.Value, p.Key);
            }

            hg = new Hashgraph(participants, store, commitCh, logger);
        }

        public int Id()
        {
            return id;
        }

        public byte[] PubKey()
        {
            return pubKey ?? (pubKey = CryptoUtils.FromEcdsaPub(key));
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

        public Exception Init()
        {
            var initialEvent = new Event(new byte[][] { }, new[] {"", ""},
                PubKey(),
                Seq);

            return SignAndInsertSelfEvent(initialEvent);
        }

        public Exception Bootstrap()
        {
            var err = hg.Bootstrap();

            if (err != null)
            {
                return err;
            }

            string head = null;
            int seq = 0;

            string last;
            bool isRoot;
            (last, isRoot, err) = hg.Store.LastFrom(HexId());

            if (err != null)
            {
                return err;
            }

            if (isRoot)
            {
                Root root;
                (root, err) = hg.Store.GetRoot(HexId());
                if (err != null)
                {
                    head = root.X;
                    seq = root.Index;
                }
            }
            else
            {
                Event lastEvent;
                (lastEvent, err) = GetEvent(last);
                if (err != null)
                {
                    return err;
                }

                head = last;
                seq = lastEvent.Index();
            }

            Head = head;
            Seq = seq;

            return null;
        }

        public Exception SignAndInsertSelfEvent(Event ev)
        {
            Exception err = ev.Sign(key);

            if (err != null)
            {
                return err;
            }

            err = InsertEvent(ev, true);

            return err;
        }

        public Exception InsertEvent(Event ev, bool setWireInfo)
        {
            var err = hg.InsertEvent(ev, setWireInfo);

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

        public Dictionary<int, int> Known()
        {
            return hg.Known();
        }

        public bool OverSyncLimit(Dictionary<int, int> known, int syncLimit)
        {
            var totUnknown = 0;
            var myKnown = Known();

            foreach (var kn in myKnown)
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

        public (Frame frame, Exception err) GetFrame()
        {
            return hg.GetFrame();
        }

//returns events that c knowns about that are not in 'known'
        public (Event[] events, Exception err ) Diff(Dictionary<int, int> known)
        {
            var unknown = new List<Event>();

            //known represents the number of events known for every participant
            //compare this to our view of events and fill unknown with events that we know of
            // and the other doesnt

            foreach (var kn in known)
            {
                var ct = kn.Value;

                var pk = reverseParticipants[kn.Key];
                var (participantEvents, err) = hg.Store.ParticipantEvents(pk, ct);

                if (err != null)
                {
                    return (new Event[] { }, err);
                }

                foreach (var e in participantEvents)
                {
                    Event ev;
                    (ev, err) = hg.Store.GetEvent(e);
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

        public Exception Sync(WireEvent[] unknown)
        {
            logger.Debug("Sync unknown={unknown}; txPool={txPool}", unknown, transactionPool);

            string otherHead = "";

            //add unknown events
            int k = 0;
            Exception err;
            foreach (var we in unknown)

            {
                Event ev;
                (ev, err) = hg.ReadWireInfo(we);

                if (err != null)
                {
                    return err;
                }

                err = InsertEvent(ev, false);

                if (err != null)
                {
                    return err;
                }

                //assume last event corresponds to other-head
                if (k == unknown.Length - 1)
                {
                    otherHead = ev.Hex();
                }
            }

            //create new event with self head and other head
            //only if there are pending loaded events or the transaction pool is not empty
            if (unknown.Length > 0 || transactionPool.Count > 0)
            {
                var newHead = new Event(transactionPool.ToArray(),
                    new[] {Head, otherHead},
                    PubKey(),
                    Seq + 1);

                err = SignAndInsertSelfEvent(newHead);

                if (err != null)
                {
                    return new CoreError($"Error inserting new head: {err.Message}", err);
                }

                //empty the transaction pool
                transactionPool.Clear();
            }

            return null;
        }

        public Exception AddSelfEvent()
        {
            if (transactionPool.Count == 0)
            {
                logger.Debug("Empty TxPool");
                return null;
            }

            //create new event with self head and empty other parent
            //empty transaction pool in its payload
            var newHead = new Event(transactionPool.ToArray(),
                new[] {Head, ""},
                PubKey(), Seq + 1);

            var err = SignAndInsertSelfEvent(newHead);

            if (err != null)
            {
                return new CoreError($"Error inserting new head: {err.Message}", err);
            }

            logger.Debug("Created Self-Event Transactions={TransactionCount}", transactionPool.Count);

            transactionPool.Clear();

            return null;
        }

        public (Event[] events, Exception err) FromWire(WireEvent[] wireEvents)
        {
            var events = new List<Event>(wireEvents.Length);

            foreach (var w in wireEvents)
            {
                var (ev, err) = hg.ReadWireInfo(w);
                if (err != null)
                {
                    return (null, err);
                }

                events.Add(ev);
            }

            return (events.ToArray(), null);
        }

        public (WireEvent[] wireEvents, Exception err) ToWire(Event[] events)
        {
            var wireEvents = new List<WireEvent>(events.Length);
            foreach (var e in events)
            {
                wireEvents.Add(e.ToWire());
            }

            return (wireEvents.ToArray(), null);
        }

        public Exception RunConsensus()
        {
            // DivideRounds

            var watch = Stopwatch.StartNew();
            var err = hg.DivideRounds();
            watch.Stop();

            logger.Debug("DivideRounds() Duration={DivideRoundsDuration}", watch.Nanoseconds());

            if (err != null)
            {
                logger.Error("DivideRounds Error={@err}", err);
                return err;
            }

            // DecideFrame

            watch = Stopwatch.StartNew();
            err = hg.DecideFame();
            watch.Stop();

            logger.Debug("DecideFame() Duration={DecideFameDuration}", watch.Nanoseconds());

            if (err != null)
            {
                logger.Error("DecideFame Error={@err}", err);
                return err;
            }

            // FindOrder

            watch = Stopwatch.StartNew();
            err = hg.FindOrder();
            watch.Stop();

            logger.Debug("FindOrder() Duration={FindOrderDuration}", watch.Nanoseconds());

            if (err != null)
            {
                logger.Error("FindOrder Error={@err}", err);
                return err;
            }

            return null;
        }

        public void AddTransactions(byte[][] txs)
        {
            transactionPool.AddRange(txs);
        }

        public (Event ev, Exception err) GetHead()
        {
            return hg.Store.GetEvent(Head);
        }

        public (Event ev, Exception err) GetEvent(string hash)
        {
            return hg.Store.GetEvent(hash);
        }

        public (byte[][] txs, Exception err) GetEventTransactions(string hash)
        {
            var (ex, err) = GetEvent(hash);
            if (err != null)
            {
                return (new byte[][] { }, err);
            }

            var txs = ex.Transactions();
            return (txs, null);
        }

        public string[] GetConsensusEvents() => hg.ConsensusEvents();

        public int GetConsensusEventsCount() => hg.Store.ConsensusEventsCount();

        public string[] GetUndeterminedEvents() => hg.UndeterminedEvents.ToArray();

        public int GetPendingLoadedEvents()
        {
            return hg.PendingLoadedEvents;
        }

        public (byte[][] txs, Exception err) GetConsensusTransactions()
        {
            var txs = new List<byte[]>();
            foreach (var e in GetConsensusEvents())
            {
                var (eTxs, err) = GetEventTransactions(e);
                if (err != null)
                {
                    return (txs.ToArray(), new CoreError($"Consensus event not found: {e}"));
                }

                txs.AddRange(eTxs);
            }

            return (txs.ToArray(), null);
        }

        public int? GetLastConsensusRoundIndex() => hg.LastConsensusRound;

        public int GetConsensusTransactionsCount() => hg.ConsensusTransactions;

        public int GetLastCommitedRoundEventsCount() => hg.LastCommitedRoundEvents;

        public bool NeedGossip() => hg.PendingLoadedEvents > 0 || transactionPool.Count > 0;
    }
}