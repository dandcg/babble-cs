using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DBreeze;
using DBreeze.Transactions;
using DBreeze.Utils;
using Dotnatter.Common;
using Dotnatter.HashgraphImpl.Model;
using Serilog;
using Serilog.Data;

namespace Dotnatter.HashgraphImpl.Stores
{
    public class LocalDbStore : IStore
    {
        private Dictionary<string, int> participants;
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

        private LocalDbStore(Dictionary<string, int> participants, InmemStore inMemStore, DBreezeEngine db, string path, ILogger logger)
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
        public static async Task<(IStore store, StoreError err)> New(Dictionary<string, int> participants, int cacheSize, string path, ILogger logger)
        {
            var inmemStore = new InmemStore(participants, cacheSize, logger);
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

            store.BeginTx();

            var err = await store.DbSetParticipants(participants);
            if (err != null)
            {
                return (null, err);
            }

            err = await store.DbSetRoots(inmemStore.Roots);
            if (err != null)
            {
                return (null, err);
            }

            store.CommitTx();

            return (store, null);
        }

        public static async Task<(IStore store, StoreError err)> Load(int cacheSize, string path, ILogger logger)
        {
            var db = new DBreezeEngine(new DBreezeConfiguration
            {
                //Storage =  DBreezeConfiguration.eStorage.MEMORY, 

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

                var inmemStore = new InmemStore(participants, cacheSize, logger);

                //read roots from db and put them in InmemStore
                var roots = new Dictionary<string, Root>();
                foreach (var p in participants)
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

        private const string ParticipantPrefix = "participant";
        private const string RootSuffix = "root";
        private const string RoundPrefix = "round";
        private const string TopoPrefix = "topo";

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

        public int CacheSize()
        {
            return InMemStore.CacheSize();
        }

        public (Dictionary<string, int> participants, StoreError err) Participants()
        {
            return (participants, null);
        }

        public async Task<(Event evt, StoreError err)> GetEvent(string key)
        {
            //try to get it from cache
            var (ev, err) = await InMemStore.GetEvent(key);
            //try to get it from db
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

        public (string last, bool isRoot, StoreError err) LastFrom(string participant)
        {
            return InMemStore.LastFrom(participant);
        }

        public async Task<Dictionary<int, int>> Known()
        {
            var known = new Dictionary<int, int>();

            foreach (var p in participants)
            {
                var index = -1;
                var (last, isRoot, err) = LastFrom(p.Key);
                if (err == null)
                {
                    if (isRoot)
                    {
                        Root root;
                        (root, err) = await GetRoot(p.Key);
                        if (err != null)
                        {
                            //last = root.X;
                            index = root.Index;
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

                known[p.Value] = index;
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

        public StoreError AddConsensusEvent(string key)
        {
            return InMemStore.AddConsensusEvent(key);
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
            if (txLevel == 0)
            {
            
                logger.Verbose("Begin Transaction");
                tx = db.GetTransaction();
                stx = new StoreTx(() =>
                    {
                       
                        tx.Commit();
                        logger.Verbose("Comitted Transaction");
                    },
                    () =>
                    {
                        txLevel--;

                        if (txLevel == 0)
                        {
                            tx.Dispose();
                            tx = null;
                        }
                    }
                );
            }

            txLevel++;
            return stx;
        }

        public void CommitTx()
        {
        }

        public Task<(Event ev, StoreError err)> DbGetEvent(string key)
        {
            var evRes = tx.Select<string, Event>(EventStore, key);

            if (!evRes.Exists)
            {
                return Task.FromResult((new Event(), new StoreError(StoreErrorType.KeyNotFound, key)));
            }

            return Task.FromResult<(Event, StoreError)>((evRes.Value, null));
        }

        public Task<StoreError> DbSetEvents(Event[] events)
        {
            foreach (var ev in events)
            {
                var eventHex = ev.Hex();
                logger.Debug($"Writing event[{eventHex}]");
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

                tx.Insert(ParticipantPrefix, peKey, eventHex);
            }

            return Task.FromResult<StoreError>(null);
        }

        public Task<(Event[] events, StoreError error)> DbTopologicalEvents()
        {
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
            var events = new List<string>();

            var i = skip + 1;

            while (true)
            {
                var key = ParticipantEventKey(participant, i);
                var result = tx.Select<string, string>(ParticipantPrefix, key);
                logger.Debug(key);
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
            var key = ParticipantEventKey(participant, index);
            var ev = tx.Select<string, string>(ParticipantPrefix, key).Value;
            return Task.FromResult<(string, StoreError)>((ev, null));
        }

        public Task<StoreError> DbSetRoots(Dictionary<string, Root> roots)
        {
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
            var key = ParticipantRootKey(participant);

            var root = tx.Select<string, Root>(RootSuffix, key).Value;

            return Task.FromResult<(Root, StoreError)>((root, null));
        }

        public Task<(RoundInfo round, StoreError err)> DbGetRound(int index)
        {
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
            var key = RoundKey(index);

            //insert [round_index] => [round bytes]
            tx.Insert(RoundPrefix, key, round);

            return Task.FromResult<StoreError>(null);
        }

        public Task<(Dictionary<string, int> participants, StoreError err)> DbGetParticipants()
        {
            var p = tx.SelectDictionary<string, int>(ParticipantPrefix).ToDictionary(k => string.Join(string.Empty, k.Key.Skip(ParticipantPrefix.Length + 1)), v => v.Value);
            return Task.FromResult<(Dictionary<string, int>, StoreError)>((p, null));
        }

        public Task<StoreError> DbSetParticipants(Dictionary<string, int> ps)
        {
            foreach (var participant in ps)
            {
                var key = ParticipantKey(participant.Key);
                var val = participant.Value;

                //insert [participant_participant] => [id]
                tx.Insert(ParticipantPrefix, key, val);
            }

            return Task.FromResult<StoreError>(null);
        }
    }
}