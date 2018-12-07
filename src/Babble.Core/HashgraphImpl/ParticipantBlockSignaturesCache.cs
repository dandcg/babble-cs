using System.Collections.Generic;
using System.Threading.Tasks;
using Babble.Core.Common;
using Babble.Core.HashgraphImpl.Model;
using Babble.Core.PeersImpl;

namespace Babble.Core.HashgraphImpl
{
    public class ParticipantBlockSignaturesCache
    {
        public Peers Participants { get; private set; }
        public RollingIndexMap<BlockSignature> Rim { get; private set; }

        public static async Task<ParticipantBlockSignaturesCache> NewParticipantBlockSignaturesCache(int size, Peers participants)
        {
            return new ParticipantBlockSignaturesCache
            {
                Participants = participants,
                Rim = new RollingIndexMap<BlockSignature>(size, await participants.ToIdSlice())
            };
        }

        public (int, StoreError) ParticipantId(string participant)
        {
            var ok = Participants.ByPubKey.TryGetValue(participant, out var peer);
            if (!ok)
            {
                return (-1, new StoreError(StoreErrorType.UnknownParticipant, participant));
            }

            return (peer.ID, null);
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
            var (last, err) = Rim.GetLast(Participants.ByPubKey[participant].ID);
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