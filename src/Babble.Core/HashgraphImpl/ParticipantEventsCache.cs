using System.Collections.Generic;
using System.Threading.Tasks;
using Babble.Core.Common;
using Babble.Core.PeersImpl;
using Babble.Core.Util;
using Serilog;

namespace Babble.Core.HashgraphImpl
{
    public class ParticipantEventsCache
    {

        public Peers Participants { get; private set; }
        public RollingIndexMap<string> Rim { get; private set; }

        public static async Task<ParticipantEventsCache> NewParticipantEventsCache(int size, Peers participants)
        {
            return new ParticipantEventsCache()
            {
                Participants = participants,
                Rim = new RollingIndexMap<string>(size, await participants.ToIdSlice())
            };
        }

        public (int,StoreError) ParticipantId(string participant)
        {
            var ok = Participants.ByPubKey.TryGetValue(participant, out var peer);
            if (!ok)
            {
                return (-1, new StoreError(StoreErrorType.UnknownParticipant, participant));
            }

            return (peer.ID, null);
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
            for (var k = 0; k < pe.Length; k++)
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


        public (string item, StoreError err) GetLastConsensus(string participant)
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