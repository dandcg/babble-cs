using System.Collections.Generic;
using Dotnatter.Common;

namespace Dotnatter.HashgraphImpl
{
    public class InmemStore : IStore
    {
        private readonly int cacheSize;
        private readonly Dictionary<string, int> participants;
        private LruCache<string, Event> eventCache;
        private LruCache<int, RoundInfo> roundCache;
        private RollingIndex<string> consensusCache;
        private int totConsensusEvents;
        private readonly ParticipantEventsCache participantEventsCache;
        private Dictionary<string, Root> roots;
        private int lastRound;
        
        public InmemStore(Dictionary<string, int> participants, int cacheSize)
        {
            var rts = new Dictionary<string, Root>();

            foreach (var p in participants)
            {
                rts.Add(p.Key,  Root.NewBaseRoot());
            }

            this.participants = participants;
            this.cacheSize = cacheSize;
            eventCache = new LruCache<string, Event>(cacheSize, null);
            roundCache = new LruCache<int, RoundInfo>(cacheSize, null);
            consensusCache = new RollingIndex<string>(cacheSize);
            participantEventsCache = ParticipantEventsCache.NewParticipantEventsCache(cacheSize, participants);
            roots = rts;
            lastRound = -1;
        }

        public int CacheSize()
        {
            return cacheSize;
        }

        public Dictionary<string, int> Participants()
        {
            return participants;
        }

        public (Event evt, bool success) GetEvent(string key)
        {
            var res = eventCache.Get(key);
            return res;
        }

        public void SetEvent(Event ev)
        {
            var key = ev.Hex();
            var (_, success) = GetEvent(key);

            if (!success)
            {
                AddParticpantEvent(ev.Creator, key, ev.Index());
    
            }
            
            eventCache.Add(key, ev);
        }

        private void AddParticpantEvent(string participant, string hash, int index)
        {
            participantEventsCache.Add(participant, hash, index);
        }

        public string[] ParticipantEvents(string participant, int skip)
        {
            return participantEventsCache.Get(participant, skip);
        }

        public string ParticipantEvent(string particant, int index)
        {
            return participantEventsCache.GetItem(particant, index);
        }

        public (string last, bool isRoot) LastFrom(string participant)
        {
            //try to get the last event from this participant
            var last = participantEventsCache.GetLast(participant);

            //if there is none, grab the root
            if (last == null)
            {
                if (roots.TryGetValue(participant, out var root))
                {
                    return (root.X, true);
                }
                throw new StoreError(StoreErrorType.NoRoot, participant);
            }
            return (last, true);
        }

        public Dictionary<int, int> Known()
        {
            return participantEventsCache.Known();
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

        public void AddConsensusEvent(string key)
        {
            consensusCache.Add(key, totConsensusEvents);
            totConsensusEvents++;
        }

        public RoundInfo GetRound(int r)
        {
            var (res, ok) = roundCache.Get(r);

            if (!ok)
            {
                return new RoundInfo();
            }
            return res;
        }

        public void SetRound(int r, RoundInfo round)
        {
            roundCache.Add(r, round);

            if (r > lastRound)
            {
                lastRound = r;
            }
        }

        public int LastRound()
        {
            return lastRound;
        }

        public string[] RoundWitnesses(int r)
        {
            var round = GetRound(r);

            if (round == null)
            {
                return new string[] { };
            }
            return round.Witnesses();
        }

        public int RoundEvents(int i)
        {
            var round = GetRound(i);
            if (round == null)
            {
                return 0;
            }
            return round.Events.Count;
        }

        public Root GetRoot(string participant)
        {
            if (roots.TryGetValue(participant, out var res))
            {
                return res;
            }

            return null;
        }

        public void Reset(Dictionary<string, Root> newRoots)
        {
            roots = newRoots;

            eventCache = new LruCache<string, Event>(cacheSize, null);

            roundCache = new LruCache<int, RoundInfo>(cacheSize, null);

            consensusCache = new RollingIndex<string>(cacheSize);

            participantEventsCache.Reset();

            lastRound = -1;
        }

        public void Close()
        {
        }
    }
}