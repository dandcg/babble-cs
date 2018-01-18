using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Dotnatter.Common;
using Dotnatter.Util;
using Serilog;

namespace Dotnatter.HashgraphImpl
{
    public class ParticipantEventsCache
    {
        private readonly ILogger logger;

        public int Size { get; set; }
        public Dictionary<string, int> Participants { get; set; } //[public key] => id
        public Dictionary<string, RollingIndex<string>> ParticipantEvents { get; set; }
        
        public ParticipantEventsCache(int size, Dictionary<string, int> participants, ILogger logger, string instanceName = null)
        {
            var items = new Dictionary<string, RollingIndex<string>>();

            foreach (var k in participants.Keys)
            {
                items.Add(k, new RollingIndex<string>(size));
            }
            
            Size = size;
            Participants = participants;
            this.logger = logger.AddNamedContext("ParticipantEventsCache");
            ParticipantEvents = items;
        }

        //return participant events with index > skip
        public (string[] items, StoreError err) Get(string participant, int skipIndex)
        {
            var ok = ParticipantEvents.TryGetValue(participant, out var pe);
            if (!ok)
            {
                return (new string[] { }, new StoreError(StoreErrorType.KeyNotFound, participant));
            }

            var (cached,err) = pe.Get(skipIndex);

            if (err!=null)
            {

                return (new string[] { }, err);
            }

            var res = new List<string>();
            for (var k = 0; k < cached.Count(); k++)
            {
                res.Add(cached[k]);
            }

            return ( res.ToArray(), null);

        }

        public (string item, StoreError err) GetItem(string participant, int index)
        {
            var (res, err) = ParticipantEvents[participant].GetItem(index);

            if (err != null)
            {
                return ("", err);
            }
        
            return (res,null);
        }

        public (string item, StoreError err) GetLast(string participant)
        {
            var ok = ParticipantEvents.TryGetValue(participant, out var pe);

            if (!ok)
            {
                return ("", new StoreError(StoreErrorType.KeyNotFound, participant));
            }

            var (cached, _) = pe.GetLastWindow();

                if (cached.Length == 0)
                {
                    return ("",null);
                }

                var last = cached[cached.Length - 1];
                return (last,null);

 
        }

        public StoreError Add(string participant, string hash, int index)
        {
           var ok = ParticipantEvents.TryGetValue(participant, out var pe);

            if (!ok)
            {
                pe = new RollingIndex<string>(Size);
                ParticipantEvents.Add(participant, pe);
            }

            return pe.Add(hash, index);
      
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

        public StoreError Reset()
        {
            
                var items = new Dictionary<string, RollingIndex<string>>();
                foreach (var key in Participants.Keys)
                {
                    items.Add(key, new RollingIndex<string>(Size));
                }

                ParticipantEvents = items;

                return null;
            }
    }
}