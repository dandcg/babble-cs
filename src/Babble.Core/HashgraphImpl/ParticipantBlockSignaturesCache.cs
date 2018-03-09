using System.Collections.Generic;
using Babble.Core.Common;
using Babble.Core.HashgraphImpl.Model;

namespace Babble.Core.HashgraphImpl
{
    public class ParticipantBlockSignaturesCache
    {
        public Dictionary<string, int> Participants { get; set; }
        public RollingIndexMap<BlockSignature> Rim { get; set; }


        public ParticipantBlockSignaturesCache(int size, Dictionary<string, int> participants)
        {
            Participants = participants;
            Rim = new RollingIndexMap<BlockSignature>(size, participants.GetValues());
        }




        public (int, StoreError) ParticipantId(string participant)
        {
            var ok = Participants.TryGetValue(participant, out var id);
            if (!ok)
            {
                return (-1, new StoreError(StoreErrorType.UnknownParticipant, participant));
            }

            return (id, null);
        }




        //return participant BlockSignatures where index > skip
        public (BlockSignature[] items, StoreError err) Get(string participant, int skipIndex)
        {

            var (id, err) = ParticipantId(participant);
            if (err != null)
            {
                return (new BlockSignature[] { }, err);

            }

            BlockSignature[] ps;
            (ps, err) = Rim.Get(id, skipIndex);
            if (err != null)
            {
                return (new BlockSignature[] { }, err);

            }

            var res = new List<BlockSignature>();
            for (var k = 0; k < ps.Length; k++)
            {
                res.Add(ps[k]);
            }

            return (res.ToArray(), null);

        }


        public (BlockSignature item, StoreError err) GetItem(string participant, int index)
        {
            var (id, err) = ParticipantId(participant);
            if (err != null)
            {
                return (new BlockSignature(), err);

            }

            BlockSignature item;
            (item, err) = Rim.GetItem(id, index);
            if (err != null)
            {
                return (new BlockSignature(), err);

            }

            return (item, null);
        }

        public (BlockSignature item, StoreError err) GetLast(string participant)
        {

            var (id, err) = ParticipantId(participant);
            if (err != null)
            {
                return (new BlockSignature(), err);

            }

            BlockSignature last;
            (last, err) = Rim.GetLast(id);
            if (err != null)
            {
                return (new BlockSignature(), err);

            }

            return (last, null);


        }

        public StoreError Set(string participant, BlockSignature sig)
        {
            var (id, err) = ParticipantId(participant);
            if (err != null)
            {
                return err;

            }

            return Rim.Set(id, sig, sig.Index);

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