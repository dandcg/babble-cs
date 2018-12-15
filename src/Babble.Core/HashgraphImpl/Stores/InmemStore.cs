using System.Collections.Generic;
using System.Threading.Tasks;
using Babble.Core.Common;
using Babble.Core.HashgraphImpl.Model;
using Babble.Core.PeersImpl;
using Babble.Core.Util;
using Serilog;

namespace Babble.Core.HashgraphImpl.Stores
{
    public class InmemStore : IStore
    {
        private  ILogger logger;

        private  int cacheSize;
        private  Peers participants;
        
        private LruCache<string, Event> eventCache;
        private LruCache<int, RoundInfo> roundCache;
        private  LruCache<int, Block> blockCache;
        private  LruCache<int, Frame> frameCache;
        
        private RollingIndex<string> consensusCache;
        private int totConsensusEvents;
        private  ParticipantEventsCache participantEventsCache;

        private Dictionary<string, Root> rootsByParticipant; //[participant] => Root
        private Dictionary<string, Root> rootsBySelfParent; //[Root.SelfParent.Hash] => Root
        
        private int lastRound;
        private Dictionary<string,string> lastConsensusEvents;
        private int lastBlock;




        public static async Task<InmemStore> NewInmemStore(Peers participants, int cacheSize, ILogger logger)
        {
            
            var inmemStore = new InmemStore();

        var rootsByParticipant = new Dictionary<string, Root>();



            foreach (var p in participants.ByPubKey)
            {
                var pk = p.Key;
                var pid = p.Value;

                var root = Root.NewBaseRoot(pid.ID);
             rootsByParticipant[pk] = root;
            }

            inmemStore.logger = logger.AddNamedContext("InmemStore");

            inmemStore.cacheSize = cacheSize;
            inmemStore.participants = participants;
            inmemStore.eventCache = new LruCache<string, Event>(cacheSize, null, logger, "EventCache");
            inmemStore.roundCache = new LruCache<int, RoundInfo>(cacheSize, null, logger, "RoundCache");
            inmemStore.blockCache= new LruCache<int, Block>(cacheSize, null, logger,"BlockCache");
            inmemStore.frameCache= new LruCache<int, Frame>(cacheSize,null,logger, "FrameCache");
            inmemStore.consensusCache = new RollingIndex<string>("ConsensusCache",cacheSize);
            inmemStore.participantEventsCache =await ParticipantEventsCache.NewParticipantEventsCache(cacheSize, participants);
            inmemStore.rootsByParticipant = rootsByParticipant;
            inmemStore.lastRound = -1;
            inmemStore.lastBlock = -1;
            inmemStore.lastConsensusEvents = new Dictionary<string, string>();

            return inmemStore;
        }

        public Dictionary<string, Root> Roots { get; private set; }

        public int CacheSize()
        {
            return cacheSize;
        }

        public (Peers participants, StoreError err) Participants()
        {
            return (participants, null);
        }

        public (Dictionary<string,Root>,StoreError) RootsBySelfParent() 
        {
            if (rootsBySelfParent == null)
            {
                rootsBySelfParent = new Dictionary<string, Root>();
                foreach (var root in rootsByParticipant.Values)
                {
                    rootsBySelfParent[root.SelfParent.Hash] = root;
                }
            }

            return (rootsBySelfParent, null);
        }



        public Task<(Event evt, StoreError err)> GetEvent(string key)
        {
            var (res, ok ) = eventCache.Get(key);
            //logger.Debug("GetEvent found={ok}; key={key}; res={res}", ok, key,res);

            if (!ok)
            {
                return Task.FromResult((new Event(), new StoreError(StoreErrorType.KeyNotFound, $"EventCache Key={key}")));
            }

            return Task.FromResult<(Event, StoreError)>((res, null));
        }

        public async Task<StoreError> SetEvent(Event ev)
        {
            var key = ev.Hex();
            var (_, err) = await GetEvent(key);

            if (err != null && err.StoreErrorType != StoreErrorType.KeyNotFound)
            {
                return err;
            }

            if (err != null && err.StoreErrorType == StoreErrorType.KeyNotFound)
            {
                err = AddParticpantEvent(ev.Creator(), key, ev.Index());
                if (err != null)
                {
                    return err;
                }
            }

            eventCache.Add(key, ev);
            //logger.Debug("SetEvent key={key}; ev={ev}", key,ev);

            return null;
        }

        private StoreError AddParticpantEvent(string participant, string hash, int index)
        {
            return participantEventsCache.Set(participant, hash, index);
        }

        public Task<(string[] evts, StoreError err)> ParticipantEvents(string participant, int skip)
        {
            return Task.FromResult<(string[], StoreError)>(participantEventsCache.Get(participant, skip));
        }

        public Task<(string ev, StoreError err)> ParticipantEvent(string particant, int index)
        {
            var (ev, err1 ) = participantEventsCache.GetItem(particant, index);

            if (err1 != null)
            {
                var ok = rootsByParticipant.TryGetValue(particant, out var root);
                if (!ok)
                {
                    return Task.FromResult(("", new StoreError(StoreErrorType.NoRoot, $"InmemStore.Roots Participant={particant}")));
                }

            }


            return Task.FromResult(participantEventsCache.GetItem(particant, index));
        }

        public (string last, bool isRoot, StoreError err) LastEventFrom(string participant)
        {
            //try to get the last event from this participant
            var (last, err) = participantEventsCache.GetLast(participant);

            bool isRoot = false;

            if (err != null && err.StoreErrorType== StoreErrorType.Empty )
            {
                var ok = rootsByParticipant.TryGetValue(participant, out var root);
                if (ok)
                {
                    last = root.SelfParent.Hash;
                    isRoot = true;
                    err = null;


                }
                else
                {
                    err= new StoreError(StoreErrorType.NoRoot, $"InmemStore.Roots Participant={participant}") ;
                }

         
            }

   
            return (last, isRoot, err);
        }

        public (string last, bool isRoot, StoreError err) LastConsensusEventFrom(string participant)
        {
            //try to get the last consensus event from this participant
            var ok = lastConsensusEvents.TryGetValue(participant, out var last);
            bool isRoot = false;
            StoreError err = null;
            //if there is none, grab the root
            if (!ok)
            {
                ok = rootsByParticipant.TryGetValue(participant, out var root);
                if (ok)
                {
                    last = root.SelfParent.Hash;
                    isRoot = true;
                } else {
                    err= new StoreError(StoreErrorType.NoRoot, $"InmemStore.Roots Participant={participant}") ;
                }
            }

            return (last, isRoot, err);
        }

        public Task<Dictionary<int, int>> KnownEvents()
        {
            var known = participantEventsCache.Known();
            foreach (var p in participants.ByPubKey)
            {
                var pk = p.Key;
                var pid = p.Value;
                if (known[pid.ID] == -1)
                {
                    var ok = rootsByParticipant.TryGetValue(pk, out var root);
                    if (ok)
                    {
                        known[pid.ID] = root.SelfParent.Index;
                    }
                }
            }

            return Task.FromResult(known);
        }

        public string[] ConsensusEvents()
        {
            var (lastWindow, _) = consensusCache.GetLastWindow();

            var res = new List<string>();
            foreach (var item in lastWindow)
            {
                res.Add(item);
            }

            return res.ToArray();
        }

        public int ConsensusEventsCount()
        {
            return totConsensusEvents;
        }

        public StoreError AddConsensusEvent(Event ev)
        {
            consensusCache.Set(ev.Hex(), totConsensusEvents);
            totConsensusEvents++;
            lastConsensusEvents[ev.Creator()] = ev.Hex();
            return null;
        }

  

        public Task<(RoundInfo roundInfo, StoreError err)> GetRound(int r)
        {
            var (res, ok) = roundCache.Get(r);

            if (!ok)
            {
                return Task.FromResult((new RoundInfo(), new StoreError(StoreErrorType.KeyNotFound,$"RoundCache {r}" )));
                ;
            }

            return Task.FromResult<(RoundInfo, StoreError)>((res, null));
        }

        public Task<StoreError> SetRound(int r, RoundInfo round)
        {
            roundCache.Add(r, round);

            if (r > lastRound)
            {
                lastRound = r;
            }

            return Task.FromResult<StoreError>(null);
        }

        public int LastRound()
        {
            return lastRound;
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

        public async Task<int> RoundEvents(int i)
        {
            var (round, err) = await GetRound(i);
            if (err != null)
            {
                return 0;
            }

            return round.Events.Count;
        }

        public Task<(Root root, StoreError err)> GetRoot(string participant)
        {
            var ok = rootsByParticipant.TryGetValue(participant, out var res);

            if (!ok)
            {
                return Task.FromResult((new Root(), new StoreError(StoreErrorType.KeyNotFound, participant)));
            }

            return Task.FromResult<(Root, StoreError)>((res, null));
        }

        public Task<(Block block, StoreError err)> GetBlock(int index)
        {

            var (res, ok) = blockCache.Get(index);
            if (!ok)
            {
                return Task.FromResult((new Block(), new StoreError(StoreErrorType.KeyNotFound,$"{index}")));
            }

            return Task.FromResult<(Block, StoreError)>((res, null));
        }

        public async Task<StoreError> SetBlock(Block block)
        {

            var index = block.Index();

            var (_,err) = await GetBlock(index);
            if (err != null && err.StoreErrorType!=StoreErrorType.KeyNotFound)
            {
                return err;
            }

            blockCache.Add(index, block);

            if (index > lastBlock)
            {
                lastBlock = index;
            }


            return null;
        }

        public Task<int> LastBlockIndex()
        {
            return Task.FromResult(lastBlock);
        }

        public Task<(Frame frame, StoreError err)> GetFrame(int index)
        {
            var (res, ok) = frameCache.Get(index);
            if (!ok)
            {
                return Task.FromResult((new Frame { }, new StoreError(StoreErrorType.KeyNotFound, $"FrameCache: Index={index}")));
            }

            return Task.FromResult<(Frame,StoreError)>((res, null));
        }

        public async Task<StoreError> SetFrame(Frame frame)
        {
            var index = frame.Round;
            var (_, err) = await GetFrame(index);
            if (err != null && err.StoreErrorType!= StoreErrorType.KeyNotFound)
            {
                return err;
            }

            frameCache.Add(index, frame);
            return null;
        }

        public StoreError Reset(Dictionary<string, Root> roots)
        {
            rootsByParticipant = roots;
            rootsBySelfParent = null;

            eventCache = new LruCache<string, Event>(cacheSize, null, logger, "EventCache");

            roundCache = new LruCache<int, RoundInfo>(cacheSize, null, logger, "RoundCache");

            consensusCache = new RollingIndex<string>("ConsensusCache",cacheSize);

            var err = participantEventsCache.Reset();

            lastRound = -1;
            lastBlock = -1;

            return err;
        }

        public StoreError Close()
        {
            return null;
        }

        public bool NeedBoostrap()
        {
            return false;
        }

        public string StorePath()
        {
            return "";
        }

        public StoreTx BeginTx()
        {
            return new StoreTx(null, null);
        }
        
    }
}