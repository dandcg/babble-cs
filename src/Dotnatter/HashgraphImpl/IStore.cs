using System.Collections.Generic;

namespace Dotnatter.HashgraphImpl
{
    public interface IStore
    {
        int CacheSize();
        Dictionary<string, int> Participants();
        Event GetEvent(string str);
        void SetEvent(Event ev);
        string[] ParticipantEvents(string str, int i);
        string ParticipantEvent(string str, int i);
        (string, bool) LastFrom(string str);
        Dictionary<int, int> Known();
        string[] ConsensusEvents();

        int ConsensusEventsCount();
        void AddConsensusEvent(string str);
        RoundInfo GetRound(int i);
        void SetRound(int i , RoundInfo ri);
        int LastRound();
        string[] RoundWitnesses(int i);

        int RoundEvents(int i );
        Root GetRoot(string str);
        void Reset(Dictionary<string, Root> d);
        void Close();

    }
}