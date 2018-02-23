using System.Collections.Generic;
using System.Threading.Tasks;
using Dotnatter.Common;
using Dotnatter.HashgraphImpl.Model;

namespace Dotnatter.HashgraphImpl.Stores
{
    public interface IStore
    {
        int CacheSize();
        (Dictionary<string, int> participants, StoreError err) Participants();
        Task<(Event evt, StoreError err)> GetEvent(string str);
        Task<StoreError> SetEvent(Event ev);
        Task<(string[] evts, StoreError err)> ParticipantEvents(string str, int i);
        Task<(string ev, StoreError err)> ParticipantEvent(string str, int i);
        (string last, bool isRoot, StoreError err) LastFrom(string str);
        Task<Dictionary<int, int>> Known();
        string[] ConsensusEvents();

        int ConsensusEventsCount();
        StoreError AddConsensusEvent(string str);
        Task<(RoundInfo roundInfo, StoreError err)> GetRound(int i);
       Task<StoreError> SetRound(int i , RoundInfo ri);
        int LastRound();
        Task<string[]> RoundWitnesses(int i);

        Task<int> RoundEvents(int i );
        Task<(Root root, StoreError err)> GetRoot(string str);
        StoreError Reset(Dictionary<string, Root> d);
        StoreError Close();

    }
}