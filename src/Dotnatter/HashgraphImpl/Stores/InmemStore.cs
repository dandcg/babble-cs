using System.Collections.Generic;
using System.Threading.Tasks;
using Dotnatter.Common;
using Dotnatter.HashgraphImpl.Model;
using Dotnatter.Util;
using Serilog;

namespace Dotnatter.HashgraphImpl.Stores
{
    public class InmemStore : IStore
    {
        private readonly int cacheSize;
        private readonly ILogger logger;
        private readonly Dictionary<string, int> participants;
        private LruCache<string, Event> eventCache;
        private LruCache<int, RoundInfo> roundCache;
        private RollingIndex<string> consensusCache;
        private int totConsensusEvents;
        private readonly ParticipantEventsCache participantEventsCache;
        private Dictionary<string, Root> roots;
        private int lastRound;
        
        public InmemStore(Dictionary<string, int> participants, int cacheSize, ILogger logger)
        {
            var rts = new Dictionary<string, Root>();

            foreach (var p in participants)
            {
                rts.Add(p.Key,  Root.NewBaseRoot());
            }

            this.participants = participants;
            this.cacheSize = cacheSize;
            this.logger = logger.AddNamedContext("InmemStore");
            eventCache = new LruCache<string, Event>(cacheSize, null, logger,"EventCache");
            roundCache = new LruCache<int, RoundInfo>(cacheSize, null, logger,"RoundCache");
            consensusCache = new RollingIndex<string>(cacheSize);
            participantEventsCache = new ParticipantEventsCache(cacheSize, participants,logger);
            roots = rts;
            lastRound = -1;
        }

        public Dictionary<string, Root> Roots
        {
            get { return roots; }
        }

        public int CacheSize()
        {
            return cacheSize;
        }

        public (Dictionary<string, int> participants, StoreError err) Participants()
        {
            return (participants,null);
        }

        public Task<(Event evt, StoreError err)> GetEvent(string key)
        {
            bool ok=false;
            Event res=null;
            if (!string.IsNullOrEmpty(key))
            {
                (res,ok ) = eventCache.Get(key);
                logger.Verbose("GetEvent found={ok}; key={key}",ok,key);
            }
            
            if (!ok)
            {
                return Task.FromResult<(Event,StoreError)>((new Event(), new StoreError(StoreErrorType.KeyNotFound, key)));
                
            }
            
            return Task.FromResult<(Event,StoreError)>((res,null));
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
                err =AddParticpantEvent(ev.Creator(), key, ev.Index());
                if (err != null)
                {
                    return err;
                }
            }
            
            eventCache.Add(key, ev);

            return null;

        }

        private StoreError AddParticpantEvent(string participant, string hash, int index)
        {
          return  participantEventsCache.Add(participant, hash, index);
        }

        public Task<(string[] evts, StoreError err)> ParticipantEvents(string participant, int skip)
        {
            return Task.FromResult<(string[], StoreError)>(participantEventsCache.Get(participant, skip));
        }

        public Task<(string ev, StoreError err)> ParticipantEvent(string particant, int index)
        {
            return Task.FromResult(participantEventsCache.GetItem(particant, index));
        }

        public (string last, bool isRoot, StoreError err) LastFrom(string participant)
        {
            
            //try to get the last event from this participant
            var (last, err) = participantEventsCache.GetLast(participant);
            
            var isRoot = false;
            if (err != null)
            {
                return (last, isRoot, err);
            }

            //if there is none, grab the root
            if (last =="")
            {
                var ok = Roots.TryGetValue(participant, out var root);

                if (ok)
                {
                    last = root.X;
                    isRoot = true;
                }
                else
                {
                    err=new  StoreError(StoreErrorType.NoRoot, participant);
                }
            }

            return (last, isRoot,err);
        }

        public Task<Dictionary<int, int>> Known()
        {
            return Task.FromResult(participantEventsCache.Known());
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

        public StoreError AddConsensusEvent(string key)
        {
            consensusCache.Add(key, totConsensusEvents);
            totConsensusEvents++;
            return null;
        }

        public Task<(RoundInfo roundInfo, StoreError err)> GetRound(int r)
        {
            var (res, ok) = roundCache.Get(r);

            if (!ok)
            {
                return Task.FromResult<(RoundInfo,StoreError)>((new RoundInfo(), new StoreError(StoreErrorType.KeyNotFound, r.ToString()))); ;
            }
            return Task.FromResult<(RoundInfo,StoreError)>((res,null));
        }

        public  Task<StoreError>  SetRound(int r, RoundInfo round)
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
            var (round,err) = await GetRound(r);

            if (err != null)
            {
                return new string[] { };
            }
            return round.Witnesses();
        }

        public async Task<int> RoundEvents(int i)
        {
            var (round,err) = await GetRound(i);
            if (err != null)
            {
                return 0;
            }
            return round.Events.Count;
        }

        public Task<(Root root, StoreError err)> GetRoot(string participant)
        {
            var ok = (Roots.TryGetValue(participant, out var res));

            if (!ok)
            {
                return Task.FromResult<(Root,StoreError)>((new Root(), new StoreError(StoreErrorType.KeyNotFound, participant)));
            }

            return Task.FromResult<(Root,StoreError)>((res,null));
        }

        public StoreError Reset(Dictionary<string, Root> newRoots)
        {
            roots = newRoots;

            eventCache = new LruCache<string, Event>(cacheSize, null, logger,"EventCache");

            roundCache = new LruCache<int, RoundInfo>(cacheSize, null, logger,"RoundCache");

            consensusCache = new RollingIndex<string>(cacheSize);

            var err = participantEventsCache.Reset();

            lastRound = -1;

            return err;
        }

        public StoreError Close()
        {
            return null;

        }
    }
}