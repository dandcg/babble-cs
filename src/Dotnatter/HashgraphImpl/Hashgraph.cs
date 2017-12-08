using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Dotnatter.HashgraphImpl
{
    public class Hashgraph
    {
        public Dictionary<string, int> Participants { get; set; } //[public key] => id
        public Dictionary<int, string> ReverseParticipants { get; set; } //[id] => public key
        public IStore Store { get; set; } //store of Events and Rounds
        public string[] UndeterminedEvents { get; set; } //[index] => hash
        public int[] UndecidedRounds { get; set; } //queue of Rounds which have undecided witnesses
        public int LastConsensusRound { get; set; } //index of last round where the fame of all witnesses has been decided
        public int LastCommitedRoundEvents { get; set; } //number of events in round before LastConsensusRound
        public int ConsensusTransactions { get; set; } //number of consensus transactions
        public int PendingLoadedEvents { get; set; } //number of loaded events that are not yet committed

        public ConcurrentQueue<Event> commitCh { get; set; } //channel for committing events

        public int topologicalIndex { get; set; } //counter used to order events in topological order
        public int superMajority { get; set; }
        
        //ancestorCache* common.LRU
        //selfAncestorCache* common.LRU
        //oldestSelfAncestorCache *common.LRU
        //stronglySeeCache        *common.LRU
        //parentRoundCache        *common.LRU
        //roundCache              *common.LRU
    }


    public interface IStore
    {
    }
}