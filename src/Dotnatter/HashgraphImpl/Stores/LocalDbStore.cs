using System.Collections.Generic;
using System.Threading.Tasks;
using DBreeze;
using DBreeze.DataTypes;
using Dotnatter.Common;
using Dotnatter.HashgraphImpl.Model;
using Serilog;

namespace Dotnatter.HashgraphImpl.Stores
{
    public class LocalDbStore : IStore
    {
        private Dictionary<string, int> participants;
        private InmemStore inMemStore;
        private readonly DBreezeEngine db;
        private string path;
        private readonly ILogger logger;

        private LocalDbStore(Dictionary<string, int> participants, InmemStore inMemStore, DBreezeEngine db, string path, ILogger logger)
        {
            this.participants = participants;
            this.inMemStore = inMemStore;
            this.db = db;
            this.path = path;
            this.logger = logger;
        }

        //LoadBadgerStore creates a Store from an existing database
        public static async Task<(IStore store, StoreError err)> New(Dictionary<string, int> participants, int cacheSize, string path, ILogger logger)
        {
            var inmemStore = new InmemStore(participants, cacheSize, logger);
            var db = new DBreezeEngine(path);
            var store = new LocalDbStore(
                participants,
                inmemStore,
                db,
                path,
                logger
            );

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

            return (store, null);
        }

        public static async Task<(IStore store, StoreError err)> Load(int cacheSize, string path, ILogger logger)
        {
            var db = new DBreezeEngine(path);
            var store = new LocalDbStore(
                null,
                null,
                db,
                path,
                logger
            );

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
            store.inMemStore = inmemStore;

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
            return string.Format("{0}_{1}", ParticipantPrefix, participant);
        }

        public string ParticipantEventKey(string participant, int index)
        {
            return string.Format("%s_%09d", participant, index);
        }

        public string ParticipantRootKey(string participant)
        {
            return string.Format("%s_%s", participant, RootSuffix);
        }

        public string RoundKey(int index)
        {
            return string.Format("%s_%09d", RoundPrefix, index);
        }

        public int CacheSize()
        {
            return inMemStore.CacheSize();
        }

        public (Dictionary<string, int> participants, StoreError err) Participants()
        {
            return (participants, null);
        }

        public async Task<(Event evt, StoreError err)> GetEvent(string key)
        {
            //try to get it from cache
            var (ev, err) = await inMemStore.GetEvent(key);
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
            var err = await inMemStore.SetEvent(ev);

            if (err != null)
            {
                return err;
            }

            //try to add it to the db
            return await DbSetEvents(new[] {ev});
        }

        public async Task<(string[] evts, StoreError err)> ParticipantEvents(string participant, int skip)
        {
            var (res, err) = await inMemStore.ParticipantEvents(participant, skip);
            if (err != null)
            {
                (res, err) = await DbParticipantEvents(participant, skip);
            }

            return (res, err);
        }

        public async Task<(string ev, StoreError err)> ParticipantEvent(string participant, int index)
        {
            var (result, err) = await inMemStore.ParticipantEvent(participant, index);
            if (err != null)
            {
                (result, err) = await DbParticipantEvent(participant, index);
            }

            return (result, err);
        }

        public (string last, bool isRoot, StoreError err) LastFrom(string participant)
        {
            return inMemStore.LastFrom(participant);
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
                            last = root.X;
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
            return inMemStore.ConsensusEvents();
        }

        public int ConsensusEventsCount()
        {
            return inMemStore.ConsensusEventsCount();
        }

        public StoreError AddConsensusEvent(string key)
        {
            return inMemStore.AddConsensusEvent(key);
        }

        public async Task<(RoundInfo roundInfo, StoreError err)> GetRound(int r)
        {
            var (res, err) = await inMemStore.GetRound(r);
            if (err != null)
            {
                (res, err) = await DbGetRound(r);
            }

            return (res, err);
        }

        public async Task<StoreError> SetRound(int r, RoundInfo round)
        {
            var err = await inMemStore.SetRound(r, round);
            if (err != null)
            {
                return err;
            }

            return await DbSetRound(r, round);
        }

        public int LastRound()
        {
            return inMemStore.LastRound();
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
            var (root, err) = await inMemStore.GetRoot(participant);
            if (err != null)
            {
                (root, err) = await DbGetRoot(participant);
            }

            return (root, err);
        }

        public StoreError Reset(Dictionary<string, Root> roots)
        {
            return inMemStore.Reset(roots);
        }

        public StoreError Close()
        {
            var err = inMemStore.Close();

            if (err != null)
            {
                return err;
            }

            db.Dispose();
            return null;
        }

        //++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
//DB Methods

        public Task<(Event ev, StoreError err)> DbGetEvent(string key)
        {
            Event ev;

            using (var t = db.GetTransaction())
            {
                ev = t.Select<string, Event>("Event", key).Value;
            }

            return Task.FromResult<(Event, StoreError)>((ev, null));
        }

        public Task<StoreError> DbSetEvents(Event[] events)
        {
            using (var t = db.GetTransaction())
            {
                foreach (var ev in events)
                {
                    var eventHex = ev.Hex();

                    //check if it already exists
                    var isnew = !t.Select<string, Event>(EventStore, eventHex).Exists;

                    //insert [event hash] => [event bytes]
                    t.Insert("Event", eventHex, ev);

                    if (isnew)
                    {
                        //insert [topo_index] => [event hash]
                        var topoKey = TopologicalEventKey(ev.GetTopologicalIndex());

                        t.Insert(TopoPrefix, topoKey, eventHex);
                    }

                    //insert [participant_index] => [event hash]
                    var peKey = ParticipantEventKey(ev.Creator(), ev.Index());

                    t.Insert(ParticipantPrefix, peKey, eventHex);
                }

                t.Commit();
            }

            return Task.FromResult<StoreError>(null);
        }

        //func (s *BadgerStore) dbTopologicalEvents() ([]Event, error) {
        //	res := []Event{}
        //	t := 0
        //	err := s.db.View(func(txn *badger.Txn) error {
        //		key := topologicalEventKey(t)
        //		item, errr := txn.Get(key)
        //		for errr == nil {
        //			v, errrr := item.Value()
        //			if errrr != nil {
        //				break
        //			}

        //			evKey := string(v)
        //			eventItem, err := txn.Get([]byte(evKey))
        //			if err != nil {
        //				return err
        //			}
        //			eventBytes, err := eventItem.Value()
        //			if err != nil {
        //				return err
        //			}

        //			event := new(Event)
        //			if err := event.Unmarshal(eventBytes); err != nil {
        //				return err
        //			}
        //			res = append(res, *event)

        //			t++
        //			key = topologicalEventKey(t)
        //			item, errr = txn.Get(key)
        //		}

        //		if !isDBKeyNotFound(errr) {
        //			return errr
        //		}

        //		return nil
        //	})

        //	return res, err
        //}

        public Task<(string[] events, StoreError err)> DbParticipantEvents(string participant, int skip)

        {
            var events = new List<string>();

            using (var tx = db.GetTransaction())
            {
                var i = skip + 1;

                while (true)
                {
                    var key = ParticipantEventKey(participant, i);
                    var result = tx.Select<string, string>(ParticipantPrefix, key);

                    if (!result.Exists)
                    {
                        break;
                    }

                    events.Add(result.Value);
                }
            }

            return Task.FromResult<(string[], StoreError)>((events.ToArray(), null));
        }

        public Task<(string ev, StoreError err)> DbParticipantEvent(string participant, int index)

        {
            string ev;
            using (var tx = db.GetTransaction())
            {
                var key = ParticipantEventKey(participant, index);

                ev = tx.Select<string, string>(ParticipantPrefix, key).Value;
            }

            return Task.FromResult<(string, StoreError)>((ev, null));
        }

        public Task<StoreError> DbSetRoots(Dictionary<string, Root> roots)
        {
            using (var tx = db.GetTransaction())
            {
                foreach (var pr in roots)

                {
                    var participant = pr.Key;
                    var root = pr.Value;

                    var key = ParticipantRootKey(participant);
                    //insert [participant_root] => [root bytes]
                    tx.Insert(RootSuffix, key, root);
                }

                tx.Commit();
            }

            return Task.FromResult<StoreError>(null);
        }

        public Task<(Root, StoreError)> DbGetRoot(string participant)
        {
            var key = ParticipantRootKey(participant);

            Root root;
            using (var tx = db.GetTransaction())
            {
                root = tx.Select<string, Root>(RootSuffix, key).Value;
            }

            return Task.FromResult<(Root, StoreError)>((root, null));
        }

        public Task<(RoundInfo round, StoreError err)> DbGetRound(int index)
        {
            Row<string, RoundInfo> result;
            using (var tx = db.GetTransaction())
            {
                var key = RoundKey(index);
                result = tx.Select<string, RoundInfo>(RoundPrefix, key);
            }

            if (result.Exists)
            {
                return Task.FromResult<(RoundInfo, StoreError)>((result.Value, null));
            }

            return Task.FromResult<(RoundInfo, StoreError)>((null, new StoreError(StoreErrorType.KeyNotFound)));
        }

        public Task<StoreError> DbSetRound(int index, RoundInfo round)
        {
            using (var tx = db.GetTransaction())
            {
                var key = RoundKey(index);

                //insert [round_index] => [round bytes]
                tx.Insert(RoundPrefix, key, round);
                tx.Commit();
            }

            return null;
        }

        public Task<(Dictionary<string, int> participants, StoreError err)> DbGetParticipants()

        {
            Dictionary<string, int> p = null;
            using (var tx = db.GetTransaction())
            {
                p = tx.SelectDictionary<string, int>(ParticipantPrefix);
            }

            return Task.FromResult<(Dictionary<string, int>, StoreError)>((p, null));
        }

        public Task<StoreError> DbSetParticipants(Dictionary<string, int> ps)
        {
            using (var tx = db.GetTransaction())
            {
                foreach (var participant in ps)
                {
                    var key = ParticipantKey(participant.Key);
                    var val = participant.Value;

                    //insert [participant_participant] => [id]
                    tx.Insert(ParticipantPrefix, key, val);
                }

                tx.Commit();
            }

            return Task.FromResult<StoreError>(null);
        }
    }
}