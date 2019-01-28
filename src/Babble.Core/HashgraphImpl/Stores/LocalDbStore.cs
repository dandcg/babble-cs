using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Babble.Core.Common;
using Babble.Core.HashgraphImpl.Model;
using Babble.Core.PeersImpl;
using Babble.Core.Util;
using DBreeze;
using DBreeze.Transactions;
using DBreeze.Utils;
using Serilog;
using Serilog.Events;

namespace Babble.Core.HashgraphImpl.Stores
{
    public class LocalDbStore : IStore
    {
        private Peers participants;
        private readonly DBreezeEngine db;
        private readonly ILogger logger;
        private Transaction tx;
        private int txLevel;
        private StoreTx stx;

        static LocalDbStore()
        {
            //Setting up NetJSON serializer (from NuGet) to be used by DBreeze
            CustomSerializator.ByteArraySerializator = o => NetJSON.NetJSON.Serialize(o).To_UTF8Bytes();
            CustomSerializator.ByteArrayDeSerializator = (bt, t) => NetJSON.NetJSON.Deserialize(t, bt.UTF8_GetString());
        }

        private LocalDbStore(Peers participants, InmemStore inMemStore, DBreezeEngine db, string path, ILogger logger)
        {
            this.participants = participants;
            InMemStore = inMemStore;
            this.db = db;
            Path = path;
            this.logger = logger;
        }

        public string Path { get; }
        public InmemStore InMemStore { get; private set; }

        //LoadBadgerStore creates a Store from an existing database
        public static async Task<(IStore store, StoreError err)> New(Peers participants, int cacheSize, string path, ILogger logger)
        {
            logger = logger.AddNamedContext("LocalDbStore");
            logger.Verbose("New store");

            var inmemStore = await InmemStore.NewInmemStore(participants, cacheSize, logger);
            var db = new DBreezeEngine(new DBreezeConfiguration
            {
                //Storage =  DBreezeConfiguration.eStorage.MEMORY, 
                DBreezeDataFolderName = path
            });

            var store = new LocalDbStore(
                participants,
                inmemStore,
                db,
                path,
                logger
            );

            using (var tx = store.BeginTx())
            {
                var err = await store.DbSetParticipants(participants);
                if (err != null)
                {
                    return (null, err);
                }

                err = await store.DbSetRoots(inmemStore.rootsByParticipant);
                if (err != null)
                {
                    return (null, err);
                }

                tx.Commit();
            }

            return (store, null);
        }

        public static async Task<(IStore store, StoreError err)> Load(int cacheSize, string path, ILogger logger)
        {
            logger = logger.AddNamedContext("LocalDbStore");
            logger.Verbose("Load store");

            var db = new DBreezeEngine(new DBreezeConfiguration
            {
                DBreezeDataFolderName = path
            });

            var store = new LocalDbStore(
                null,
                null,
                db,
                path,
                logger
            );

            using (var tx = store.BeginTx())
            {
                var (participants, err) = await store.DbGetParticipants();
                if (err != null)
                {
                    return (null, err);
                }

                var inmemStore = await InmemStore.NewInmemStore(participants, cacheSize, logger);

                //read roots from db and put them in InmemStore
                var roots = new Dictionary<string, Root>();
                foreach (var p in participants.ByPubKey)
                {
                    Root root;
                    (root, err) = await store.DbGetRoot(p.Key);
                    if (err != null)
                    {
                        return (null, err);
                    }

                    roots[p.Key] = root;
                }

                err = inmemStore.Reset(roots);
                if (err != null)
                {
                    return (null, err);
                }

                store.participants = participants;
                store.InMemStore = inmemStore;

                tx.Commit();
            }

            return (store, null);
        }

        private const string ParticipantEv = "participantEvent";
        private const string ParticipantPrefix = "participant";
        private const string RootSuffix = "root";
        private const string RoundPrefix = "round";
        private const string BlockPrefix = "block";
        private const string TopoPrefix = "topo";
        private const string FramePrefix = "frame";
        private const string EventStore = "event";

        //==============================================================================
        //Keys

        public string TopologicalEventKey(int index)
        {
            return $"{TopoPrefix}_{index}";
        }

        public string ParticipantKey(string participant)
        {
            return $"{ParticipantPrefix}_{participant}";
        }

        public string ParticipantEventKey(string participant, int index)
        {
            return $"{participant}_{index}";
        }

        public string ParticipantRootKey(string participant)
        {
            return $"{participant}_{RootSuffix}";
        }

        public string RoundKey(int index)
        {
            return $"{RoundPrefix}_{index}";
        }

        public string BlockKey(int index)
        {
            return $"{BlockPrefix}_{index}";
        }

        public string FrameKey(int index)
        {
            return $"{FramePrefix}_{index}";
        }

        //==============================================================================
        //Implement the Store interface

        public int CacheSize()
        {
            return InMemStore.CacheSize();
        }

        public (Peers participants, StoreError err) Participants()
        {
            return (participants, null);
        }

        public async Task<(Event evt, StoreError err)> GetEvent(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return (new Event(), new StoreError(StoreErrorType.KeyNotFound, key));
            }

            //try to get it from cache
            var (ev, err) = await InMemStore.GetEvent(key);

            //if not in cache, try to get it from db
            if (err != null)
            {
                (ev, err) = await DbGetEvent(key);
            }

            return (ev, err);
        }

        public async Task<StoreError> SetEvent(Event ev)
        {
            //try to add it to the cache
            var err = await InMemStore.SetEvent(ev);

            if (err != null)
            {
                return err;
            }

            //try to add it to the db

            return await DbSetEvents(new[] {ev});
        }

        public async Task<(string[] evts, StoreError err)> ParticipantEvents(string participant, int skip)
        {
            var (res, err) = await InMemStore.ParticipantEvents(participant, skip);
            if (err != null)
            {
                (res, err) = await DbParticipantEvents(participant, skip);
            }

            return (res, err);
        }

        public async Task<(string ev, StoreError err)> ParticipantEvent(string participant, int index)
        {
            var (result, err) = await InMemStore.ParticipantEvent(participant, index);
            if (err != null)
            {
                (result, err) = await DbParticipantEvent(participant, index);
            }

            return (result, err);
        }

        public (string last, bool isRoot, StoreError err) LastEventFrom(string participant)
        {
            return InMemStore.LastEventFrom(participant);
        }

        public (string last, bool isRoot, StoreError err) LastConsensusEventFrom(string participant)
        {
            return InMemStore.LastConsensusEventFrom(participant);
        }

        public async Task<Dictionary<int, int>> KnownEvents()
        {
            var known = new Dictionary<int, int>();

            foreach (var pp in participants.ByPubKey)
            {
                var p = pp.Key;
                var pid = pp.Value;

                var index = -1;
                var (last, isRoot, err) = LastEventFrom(p);
                if (err == null)
                {
                    if (isRoot)
                    {
                        Root root;
                        (root, err) = await GetRoot(p);
                        if (err != null)
                        {
                            last = root.SelfParent.Hash;
                            index = root.SelfParent.Index;
                        }
                    }
                    else
                    {
                        Event lastEvent;
                        (lastEvent, err) = await GetEvent(last);
                        if (err == null)
                        {
                            index = lastEvent.Index();
                        }
                    }
                }

                known[pid.ID] = index;
            }

            return known;
        }

        public string[] ConsensusEvents()
        {
            return InMemStore.ConsensusEvents();
        }

        public int ConsensusEventsCount()
        {
            return InMemStore.ConsensusEventsCount();
        }

        public StoreError AddConsensusEvent(Event ev)
        {
            return InMemStore.AddConsensusEvent(ev);
        }

        public async Task<(RoundInfo roundInfo, StoreError err)> GetRound(int r)
        {
            var (res, err) = await InMemStore.GetRound(r);
            if (err != null)
            {
                (res, err) = await DbGetRound(r);
            }

            return (res, err);
        }

        public async Task<StoreError> SetRound(int r, RoundInfo round)
        {
            var err = await InMemStore.SetRound(r, round);
            if (err != null)
            {
                return err;
            }

            err = await DbSetRound(r, round);

            return err;
        }

        public int LastRound()
        {
            return InMemStore.LastRound();
        }

        public async Task<string[]> RoundWitnesses(int r)
        {
            var (round, err) = await GetRound(r);
            if (err != null)
            {
                return new string[] { };
            }

            return round.Witnesses();
        }

        public async Task<int> RoundEvents(int r)
        {
            var (round, err) = await GetRound(r);
            if (err != null)
            {
                return 0;
            }

            return round.Events.Count;
        }

        public async Task<(Root root, StoreError err)> GetRoot(string participant)
        {
            var (root, err) = await InMemStore.GetRoot(participant);
            if (err != null)
            {
                (root, err) = await DbGetRoot(participant);
            }

            return (root, err);
        }

        public async Task<(Block block, StoreError err)> GetBlock(int index)
        {
            var (res, err) = await InMemStore.GetBlock(index);
            if (err != null)
            {
                (res, err) = await DbGetBlock(index);
            }

            return (res, err);
        }

        public async Task<StoreError> SetBlock(Block block)
        {
            var err = await InMemStore.SetBlock(block);
            if (err != null)
            {
                return err;
            }

            err = await DbSetBlock(block);

            return err;
        }

        public Task<int> LastBlockIndex()
        {
            return InMemStore.LastBlockIndex();
        }

        public async Task<(Frame frame, StoreError err)> GetFrame(int rr)
        {
            var (res, err) = await InMemStore.GetFrame(rr);
            if (err != null)
            {
                (res, err) = await DbGetFrame(rr);
            }

            return (res, err); //return res, mapError(err, string(frameKey(rr)))
        }

        public async Task<StoreError> SetFrame(Frame frame)
        {
            var err = await InMemStore.SetFrame(frame);

            if (err != null)
            {
                return err;
            }

            return await DbSetFrame(frame);
        }

        public StoreError Reset(Dictionary<string, Root> roots)
        {
            return InMemStore.Reset(roots);
        }

        public StoreError Close()
        {
            var err = InMemStore.Close();

            if (err != null)
            {
                return err;
            }

            db.Dispose();
            return null;
        }

        //++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
        //DB Methods
        //++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

        public StoreTx BeginTx()
        {
            MethodBase method = null;
            if (logger.IsEnabled(LogEventLevel.Debug))
            {
                StackTrace stackTrace = new StackTrace();

                method = stackTrace.GetFrame(1).GetMethod();
            }

            if (txLevel == 0)
            {
                logger.Verbose($"Begin Transaction {method?.DeclaringType.Name}/{method?.Name}");

                tx = db.GetTransaction();
                stx = new StoreTx(() =>
                    {
                        if (txLevel != 1) return;
                        tx.Commit();
                        logger.Verbose("Commit Transaction");
                    },
                    () =>
                    {
                        txLevel--;

                        if (txLevel == 0)
                        {
                            logger.Verbose("End Transaction");
                            tx.Dispose();
                            tx = null;
                        }
                        else
                        {
                            logger.Verbose($"Nested Transaction End {txLevel}");
                        }
                    }
                );
            }
            else
            {
                logger.Verbose($"Nested Transaction Begin {txLevel} {method?.DeclaringType.Name}/{method?.Name}");
            }

            txLevel++;
            return stx;
        }

        public (Dictionary<string, Root>, StoreError err) RootsBySelfParent()
        {
            return InMemStore.RootsBySelfParent();
        }

        public Task<(Event ev, StoreError err)> DbGetEvent(string key)
        {
            logger.Verbose("Db - GetEvent {eventKey}", key);

            var evRes = tx.Select<string, Event>(EventStore, key);

            if (!evRes.Exists)
            {
                return Task.FromResult((new Event(), new StoreError(StoreErrorType.KeyNotFound, key)));
            }

            return Task.FromResult<(Event, StoreError)>((evRes.Value, null));
        }

        public Task<StoreError> DbSetEvents(Event[] events)
        {
            logger.Verbose("Db - SetEvents");

            foreach (var ev in events)
            {
                var eventHex = ev.Hex();
                logger.Verbose($"Writing event[{eventHex}]");
                //check if it already exists
                var isNew = !tx.Select<string, Event>(EventStore, eventHex).Exists;

                //insert [event hash] => [event bytes]
                tx.Insert(EventStore, eventHex, ev);

                if (isNew)
                {
                    //insert [topo_index] => [event hash]
                    var topoKey = TopologicalEventKey(ev.GetTopologicalIndex());
                    tx.Insert(TopoPrefix, topoKey, eventHex);
                }

                //insert [participant_index] => [event hash]
                var peKey = ParticipantEventKey(ev.Creator(), ev.Index());
                tx.Insert(ParticipantEv, peKey, eventHex);
            }

            return Task.FromResult<StoreError>(null);
        }

        public Task<(Event[] events, StoreError error)> DbTopologicalEvents()
        {
            logger.Verbose("Db - TopologicalEvents");

            var res = new List<Event>();

            var t = 0;
            var key = TopologicalEventKey(t);

            var item = tx.Select<string, string>(TopoPrefix, key);

            while (true)
            {
                if (!item.Exists)
                {
                    break;
                }

                var evKey = item.Value;

                var eventItem = tx.Select<string, Event>(EventStore, evKey);

                res.Add(eventItem.Value);

                t++;
                key = TopologicalEventKey(t);
                item = tx.Select<string, string>(TopoPrefix, key);
            }

            return Task.FromResult<(Event[], StoreError)>((res.ToArray(), null));
        }

        public Task<(string[] events, StoreError err)> DbParticipantEvents(string participant, int skip)
        {
            logger.Verbose("Db - ParticipantEvents");

            var events = new List<string>();

            var i = skip + 1;

            while (true)
            {
                var key = ParticipantEventKey(participant, i);
                var result = tx.Select<string, string>(ParticipantEv, key);
                logger.Verbose(key);
                if (!result.Exists)
                {
                    break;
                }

                events.Add(result.Value);

                i++;
            }

            return Task.FromResult<(string[], StoreError)>((events.ToArray(), null));
        }

        public Task<(string ev, StoreError err)> DbParticipantEvent(string participant, int index)

        {
            logger.Verbose("Db - ParticipantEvent");

            var key = ParticipantEventKey(participant, index);
            var ev = tx.Select<string, string>(ParticipantEv, key).Value;
            return Task.FromResult<(string, StoreError)>((ev, null));
        }

        public Task<StoreError> DbSetRoots(Dictionary<string, Root> roots)
        {
            logger.Verbose("Db - SetRoots");

            foreach (var pr in roots)

            {
                var participant = pr.Key;
                var root = pr.Value;

                var key = ParticipantRootKey(participant);
                //insert [participant_root] => [root bytes]
                tx.Insert(RootSuffix, key, root);
            }

            return Task.FromResult<StoreError>(null);
        }

        public Task<(Root, StoreError)> DbGetRoot(string participant)
        {
            logger.Verbose("Db - GetRoot");

            var key = ParticipantRootKey(participant);
            var result = tx.Select<string, Root>(RootSuffix, key);
            if (!result.Exists)
            {
                return Task.FromResult((new Root(), new StoreError(StoreErrorType.KeyNotFound)));
            }

            return Task.FromResult<(Root, StoreError)>((result.Value, null));
        }

        public Task<(RoundInfo round, StoreError err)> DbGetRound(int index)
        {
            logger.Verbose("Db - GetRound");

            var key = RoundKey(index);
            var result = tx.Select<string, RoundInfo>(RoundPrefix, key);

            if (!result.Exists)
            {
                return Task.FromResult((new RoundInfo(), new StoreError(StoreErrorType.KeyNotFound)));
            }

            return Task.FromResult<(RoundInfo, StoreError)>((result.Value, null));
        }

        public Task<StoreError> DbSetRound(int index, RoundInfo round)
        {
            logger.Verbose("Db - SetRound");

            var key = RoundKey(index);

            //insert [round_index] => [round bytes]
            tx.Insert(RoundPrefix, key, round);

            return Task.FromResult<StoreError>(null);
        }

        public async Task<(Peers participants, StoreError err)> DbGetParticipants()
        {
            logger.Verbose("Db - GetParticipants");

            var res = Peers.NewPeers(); 
            
            var p = tx.SelectDictionary<string, Peer>(ParticipantPrefix).ToDictionary(k => string.Join(string.Empty, k.Key.Skip(ParticipantPrefix.Length + 1)), v => v.Value);
            
            foreach (var participant in p)
            {
                logger.Debug("Get Participant {key}", participant.Key);
                await res.AddPeer(Peer.New(participant.Value.PubKeyHex, ""));
            }

            return (res, null);
        }

        public Task<StoreError> DbSetParticipants(Peers ps)
        {
            logger.Verbose("Db - SetParticipants");

            foreach (var participant in ps.ByPubKey)
            {
                var key = ParticipantKey(participant.Key);
                var val = participant.Value;

                logger.Debug("Set Participant {key}", key);
                //insert [participant_participant] => [id]
                tx.Insert(ParticipantPrefix, key, val);
            }

            return Task.FromResult<StoreError>(null);
        }

        public Task<(Block block, StoreError error)> DbGetBlock(int index)
        {
            logger.Verbose("Db - GetBlock");

            var key = BlockKey(index);
            var result = tx.Select<string, Block>(BlockPrefix, key);

            if (!result.Exists)
            {
                return Task.FromResult((new Block(), new StoreError(StoreErrorType.KeyNotFound)));
            }

            return Task.FromResult<(Block, StoreError)>((result.Value, null));
        }

        public Task<StoreError> DbSetBlock(Block block)
        {
            logger.Verbose("Db - SetRound");

            var key = BlockKey(block.Index());

            tx.Insert(BlockPrefix, key, block);

            return Task.FromResult<StoreError>(null);
        }

        public Task<(Frame frame, StoreError err)> DbGetFrame(int index)
        {
            logger.Verbose("Db - GetFrame");

            var key = FrameKey(index);

            var result = tx.Select<string, Frame>(FramePrefix, key);

            if (!result.Exists)
            {
                return Task.FromResult((new Frame(), new StoreError(StoreErrorType.KeyNotFound)));
            }

            return Task.FromResult<(Frame, StoreError)>((result.Value, null));
        }

        public Task<StoreError> DbSetFrame(Frame frame)
        {
            logger.Verbose("Db - SetFrame");

            var key = FrameKey(frame.Round);

            tx.Insert(FramePrefix, key, frame);

            return Task.FromResult<StoreError>(null);
        }

        //++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

        //func isDBKeyNotFound(err error) bool {
        //    return err.Error() == badger.ErrKeyNotFound.Error()
        //}

        //func mapError(err error, key string) error {
        //    if err != nil {
        //        if isDBKeyNotFound(err) {
        //            return cm.NewStoreErr(cm.KeyNotFound, key)
        //        }
        //    }
        //    return err
        //}
    }
}