﻿using System.Collections.Generic;
using System.Threading.Tasks;
using Babble.Core.Common;
using Babble.Core.HashgraphImpl.Model;

namespace Babble.Core.HashgraphImpl.Stores
{
    public interface IStore
    {
        int CacheSize();
        (Dictionary<string, int> participants, StoreError err) Participants();
        Task<(Event evt, StoreError err)> GetEvent(string str);
        Task<StoreError> SetEvent(Event ev);
        Task<(string[] evts, StoreError err)> ParticipantEvents(string str, int i);
        Task<(string ev, StoreError err)> ParticipantEvent(string str, int i);
        (string last, bool isRoot, StoreError err) LastEventFrom(string str);
        (string last, bool isRoot, StoreError err) LastConsensusEventFrom(string str); 
        Task<Dictionary<int, int>> KnownEvents();
        string[] ConsensusEvents();

        int ConsensusEventsCount();
        StoreError AddConsensusEvent(Event ev);
        Task<(RoundInfo roundInfo, StoreError err)> GetRound(int i);
       Task<StoreError> SetRound(int i , RoundInfo ri);
        int LastRound();
        Task<string[]> RoundWitnesses(int i);

        Task<int> RoundEvents(int i );
        Task<(Root root, StoreError err)> GetRoot(string str);
        
        Task<(Block block, StoreError err)> GetBlock(int index);
        Task<StoreError> SetBlock(Block block);

        Task<int> LastBlockIndex();
        Task<(Frame frame, StoreError err)> GetFrame(int index);
        
        Task<StoreError> SetFrame(Frame frame);
        StoreError Reset(Dictionary<string, Root> d);
        StoreError Close();

        StoreTx BeginTx();

        object RootsBySelfParent();
    }
}