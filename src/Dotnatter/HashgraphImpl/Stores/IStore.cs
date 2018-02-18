using System.Collections.Generic;
using Dotnatter.Common;
using Dotnatter.HashgraphImpl.Model;

namespace Dotnatter.HashgraphImpl
{
    public interface IStore
    {
        int CacheSize();
        (Dictionary<string, int> participents, StoreError err) Participants();
        (Event evt, StoreError err) GetEvent(string str);
        StoreError SetEvent(Event ev);
        (string[] evts, StoreError err) ParticipantEvents(string str, int i);
        (string ev, StoreError err) ParticipantEvent(string str, int i);
        (string last, bool isRoot, StoreError err) LastFrom(string str);
        Dictionary<int, int> Known();
        string[] ConsensusEvents();

        int ConsensusEventsCount();
        StoreError AddConsensusEvent(string str);
        (RoundInfo roundInfo, StoreError err) GetRound(int i);
        StoreError SetRound(int i , RoundInfo ri);
        int LastRound();
        string[] RoundWitnesses(int i);

        int RoundEvents(int i );
        (Root root, StoreError err) GetRoot(string str);
        StoreError Reset(Dictionary<string, Root> d);
        StoreError Close();

    }
}