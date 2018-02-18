using System.Collections.Generic;
using Dotnatter.Common;
using Dotnatter.HashgraphImpl.Model;
using Serilog;

namespace Dotnatter.HashgraphImpl.Stores
{
    public class LocalDbStore : IStore
    {
        public LocalDbStore(Dictionary<string, int> pmap, int confCacheSize, string confStorePath, ILogger logger)
        {
            throw new System.NotImplementedException();
        }

        public int CacheSize()
        {
            throw new System.NotImplementedException();
        }

        public (Dictionary<string, int> participants, StoreError err) Participants()
        {
            throw new System.NotImplementedException();
        }

        public (Event evt, StoreError err) GetEvent(string str)
        {
            throw new System.NotImplementedException();
        }

        public StoreError SetEvent(Event ev)
        {
            throw new System.NotImplementedException();
        }

        public (string[] evts, StoreError err) ParticipantEvents(string str, int i)
        {
            throw new System.NotImplementedException();
        }

        public (string ev, StoreError err) ParticipantEvent(string str, int i)
        {
            throw new System.NotImplementedException();
        }

        public (string last, bool isRoot, StoreError err) LastFrom(string str)
        {
            throw new System.NotImplementedException();
        }

        public Dictionary<int, int> Known()
        {
            throw new System.NotImplementedException();
        }

        public string[] ConsensusEvents()
        {
            throw new System.NotImplementedException();
        }

        public int ConsensusEventsCount()
        {
            throw new System.NotImplementedException();
        }

        public StoreError AddConsensusEvent(string str)
        {
            throw new System.NotImplementedException();
        }

        public (RoundInfo roundInfo, StoreError err) GetRound(int i)
        {
            throw new System.NotImplementedException();
        }

        public StoreError SetRound(int i, RoundInfo ri)
        {
            throw new System.NotImplementedException();
        }

        public int LastRound()
        {
            throw new System.NotImplementedException();
        }

        public string[] RoundWitnesses(int i)
        {
            throw new System.NotImplementedException();
        }

        public int RoundEvents(int i)
        {
            throw new System.NotImplementedException();
        }

        public (Root root, StoreError err) GetRoot(string str)
        {
            throw new System.NotImplementedException();
        }

        public StoreError Reset(Dictionary<string, Root> d)
        {
            throw new System.NotImplementedException();
        }

        public StoreError Close()
        {
            throw new System.NotImplementedException();
        }
    }
}