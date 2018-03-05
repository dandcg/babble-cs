using System.Collections.Generic;
using System.Linq;
using Dotnatter.Core.Common;
using Dotnatter.Core.Util;
using Serilog;

namespace Dotnatter.Core.HashgraphImpl
{
    public class ParticipantEventsCache
    {
        
        private readonly ILogger logger;


        public Dictionary<string, int> Participants { get; private set; } //[public key] => id
        public RollingIndexMap<string> Rim { get;private set; }

        

        public ParticipantEventsCache(int size, Dictionary<string, int> participants, ILogger logger, string instanceName = null)
        {
            this.logger = logger.AddNamedContext("ParticipantEventsCache", instanceName);
            
            Participants = participants;
            Rim= new RollingIndexMap<string>(size,participants.GetValues());
        }

        public (int,StoreError) ParticipantId(string participant)
        {
            var ok = Participants.TryGetValue(participant, out var id);
            if (!ok)
            {
                return (-1, new StoreError(StoreErrorType.UnknownParticipant, participant));
            }

            return (id, null);
        }



        //return participant events with index > skip
        public (string[] items, StoreError err) Get(string participant, int skipIndex)
        {

            var (id, err) = ParticipantId(participant);
            if (err != null)
            {
                return (new string[] { }, err);

            }

            string[] pe;
            (pe, err) = Rim.Get(id, skipIndex);
            if (err != null)
            {
                return (new string[] { }, err);

            }
            
            var res = new List<string>();
            for (var k = 0; k < pe.Count(); k++)
            {
                res.Add(pe[k]);
            }

            return ( res.ToArray(), null);

        }

        public (string item, StoreError err) GetItem(string participant, int index)
        {
            var (id, err) = ParticipantId(participant);
            if (err != null)
            {
                return ("", err);

            }

            string item;
            (item, err) = Rim.GetItem(id, index);
            if (err != null)
            {
                return ("", err);

            }
        
            return (item,null);
        }

        public (string item, StoreError err) GetLast(string participant)
        {

            var (id, err) = ParticipantId(participant);
            if (err != null)
            {
                return ("", err);

            }

            string last;
            (last, err) = Rim.GetLast(id);
            if (err != null)
            {
                return ("", err);

            }
        
            return (last,null);

 
        }

        public StoreError Set(string participant, string hash, int index)
        {
            var (id, err) = ParticipantId(participant);
            if (err != null)
            {
                return err;

            }

            return Rim.Set(id,hash, index);
      
        }

        //returns [participant id] => lastKnownIndex
        public Dictionary<int, int> Known()
        {
            return Rim.Known();
        }

        public StoreError Reset()
        {

            return Rim.Reset();
        }
    }
}