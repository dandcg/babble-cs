using System.Collections.Generic;
using System.Linq;
using Dotnatter.Common;

namespace Dotnatter.HashgraphImpl
{
    public class ParticipantEventsCache
    {
        public int Size { get; set; }
        public Dictionary<string, int> Participants { get; set; } //[public key] => id
        public Dictionary<string, RollingIndex<string>> ParticipantEvents { get; set; }
        
        public static ParticipantEventsCache NewParticipantEventsCache(int size, Dictionary<string, int> participants)
        {
            var items = new Dictionary<string, RollingIndex<string>>();

            foreach (var k in participants.Keys)
            {
                items.Add(k, new RollingIndex<string>(size));
            }

            return new ParticipantEventsCache
            {
                Size = size,
                Participants = participants,
                ParticipantEvents = items
            };
        }

        //return participant events with index > skip
        public string[] Get(string participant, int skipIndex)
        {
            if (ParticipantEvents.TryGetValue(participant, out var pe))
            {
                var cached = pe.Get(skipIndex);
                return cached;
            }

            throw new StoreError(StoreErrorType.KeyNotFound, participant);
        }

        public string GetItem(string participant, int index)
        {
            var res = ParticipantEvents[participant].GetItem(index);
            return res;
        }

        public string GetLast(string participant)
        {
            if (ParticipantEvents.TryGetValue(participant, out var pe))
            {
                var (cached, _) = pe.GetLastWindow();

                if (cached.Length == 0)
                {
                    return "";
                }

                var last = cached[cached.Length - 1];
                return last;
            }
            return "";
           //throw new StoreError(StoreErrorType.KeyNotFound, participant);
        }

        public void Add(string participant, string hash, int index)
        {
            if (ParticipantEvents.TryGetValue(participant, out var pe))
            {
                pe.Add(hash, index);
            }
            else
            {
                var npe = new RollingIndex<string>(Size);
                ParticipantEvents.Add(participant, npe);
            }
        }

        //returns [participant id] => lastKnownIndex
        public Dictionary<int, int> Known()
        {
            var kn = new Dictionary<int, int>();

            foreach (var p in ParticipantEvents)
            {
                var (_, lastIndex) = p.Value.GetLastWindow();
                kn.Add(Participants[p.Key], lastIndex);
            }

            return kn;
        }

        public void Reset()
        {
            {
                var items = Participants.Keys.ToDictionary(k => k, k => new RollingIndex<string>(Size));

                ParticipantEvents = items;
            }
        }
    }
}