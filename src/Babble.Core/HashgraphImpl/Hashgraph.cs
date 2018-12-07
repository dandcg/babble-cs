﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Babble.Core.Common;
using Babble.Core.HashgraphImpl.Model;
using Babble.Core.HashgraphImpl.Stores;
using Babble.Core.PeersImpl;
using Babble.Core.Util;
using Nito.AsyncEx;
using Serilog;

namespace Babble.Core.HashgraphImpl
{
    public class Hashgraph
    {

        public Peers Participants { get; set; } //[public key] => id
        public IStore Store { get; set; } //store of Events and Rounds
        public List<string> UndeterminedEvents { get; set; } //[index] => hash
        public Queue<PendingRound> PendingRounds { get; set; } //FIFO queue of Rounds which have not attained consensus yet

        public int? LastConsensusRound { get; set; } //index of last consensus round
        public int? FirstConsensusRound { get; set; } //index of first consensus round (only used in tests)

        public int AnchorBlock { get; set; } //index of last block with enough signatures

        public int LastCommitedRoundEvents { get; set; } //number of evs in round before LastConsensusRound

        public List<BlockSignature> SigPool { get; set; } //Pool of Block signatures that need to be processed

        public int ConsensusTransactions { get; set; } //number of consensus transactions
        public int PendingLoadedEvents { get; set; } //number of loaded evs that are not yet committed
        public AsyncProducerConsumerQueue<Block> CommitCh { get; set; } //channel for committing evs
        public int TopologicalIndex { get; set; } //counter used to order evs in topological order
        public int SuperMajority { get; set; }
        public int TrustCount { get; set; }

        public LruCache<string, bool> AncestorCache { get; set; }
        public LruCache<string, bool> SelfAncestorCache { get; set; }
        public LruCache<string, bool> StronglySeeCache { get; set; }
        public LruCache<string, int> RoundCache { get; set; }
        public LruCache<string, int> TimestampCache { get; set; }

        private readonly ILogger logger;

        public Hashgraph(Peers participants, IStore store, AsyncProducerConsumerQueue<Block> commitCh, ILogger logger)
        {
            this.logger = logger.AddNamedContext("Hashgraph");

           var superMajority = 2 * participants.Len() / 3 + 1;
            var trustCount = (int) (Math.Ceiling((float) participants.Len()) / 3);

            var cacheSize = store.CacheSize();

            Participants = participants;
            Store = store;
            CommitCh = commitCh;
            AncestorCache = new LruCache<string, bool>(cacheSize, null, logger, "AncestorCache");
            SelfAncestorCache = new LruCache<string, bool>(cacheSize, null, logger, "SelfAncestorCache");
            StronglySeeCache = new LruCache<string, bool>(cacheSize, null, logger, "StronglySeeCache");
            RoundCache = new LruCache<string, int>(cacheSize, null, logger, "RoundCache");
            TimestampCache = new LruCache<string, int>(cacheSize, null, logger, "TimestampCache");
            UndeterminedEvents = new List<string>();
            SuperMajority = superMajority;
            TrustCount = trustCount;
        }

        //true if y is an ancestor of x
        public async Task<(bool, BabbleError)> Ancestor(string x, string y)
        {
            var (c, ok) = AncestorCache.Get(Key.New(x, y));

            if (ok)
            {
                return (c,null);
            }

            var (a,err) = await _ancestor(x, y);

            if (err != null)
            {
                return (false, err);
            }
                
            AncestorCache.Add(Key.New(x, y), a);
            
            return (a,null);
        }

        private async Task<(bool, BabbleError)> _ancestor(string x, string y)
        {
            if (x == y)
            {
                return (true,null);
            }

            var (ex, errx) = await Store.GetEvent(x);

            if (errx != null)
            {
                return (false, errx);
            }

            var (ey, erry) = await Store.GetEvent(y);

            if (erry != null)
            {
                return (false,erry);
            }

            var eyCreator = Participants.ByPubKey[ey.Creator()].ID;
            var (entry, ok) = ex.LastAncestors.GetById(eyCreator);

            if (!ok)
            {
                return (false, new HashgraphError($"Unknown event id {eyCreator}"));
            }

            var lastAncestorKnownFromYCreator = entry.Event.Index;

            return (lastAncestorKnownFromYCreator >= ey.Index(), null);
        }

        //true if y is a self-ancestor of x
        public async Task<(bool, BabbleError)> SelfAncestor(string x, string y)
        {
            var (c, ok) = SelfAncestorCache.Get(Key.New(x, y));

            if (ok)
            {
                return (c,null);
            }
            
            var (a,err) = await _selfAncestor(x, y);

            if (err != null)
            {
                return (false, err);
            }
            
            SelfAncestorCache.Add(Key.New(x, y), a);

            return (a,null);
        }

        private async Task<(bool, BabbleError)> _selfAncestor(string x, string y)
        {
            if (x == y)
            {
                return (true,null);
            }

            var (ex, errx) = await Store.GetEvent(x);

            if (errx != null)
            {
                return (false,errx);
            }

            var exCreator = Participants.ByPubKey[ex.Creator()].ID;

            var (ey, erry) = await Store.GetEvent(y);
            if (erry != null)
            {
                return (false,erry);
            }

            var eyCreator = Participants.ByPubKey[ey.Creator()].ID;

            return (exCreator == eyCreator && ex.Index() >= ey.Index(),null);
        }

        //true if x sees y
        public Task<(bool,BabbleError)> See(string x, string y)
        {
            return Ancestor(x, y);
            //it is not necessary to detect forks because we assume that the InsertEvent
            //function makes it impossible to insert two Events at the same height for
            //the same participant.
        }

        //true if x strongly sees y
        public async Task<(bool,BabbleError)> StronglySee(string x, string y)
        {
            var (c, ok) = StronglySeeCache.Get(Key.New(x, y));
            if (ok)
            {
                return (c,null);
            }

            var (ss,err) = await _stronglySee(x, y);

            if (err != null)
            {
                return (false, err);
            }

            StronglySeeCache.Add(Key.New(x, y), ss);
            return (ss,null);
        }

        public async Task<(bool,BabbleError)> _stronglySee(string x, string y)
        {
            var (ex, errx) = await Store.GetEvent(x);

            if (errx != null)
            {
                return (false,errx);
            }

            var (ey, erry) = await Store.GetEvent(y);

            if (erry != null)
            {
                return (false,erry);
            }

            var c = 0;

            int i = 0;
            foreach (var entry in ex.LastAncestors)
            {
                if (entry.Event.Index >= ey.FirstDescendants[i].Event.Index)
                {
                    c ++ ;
                }

                i++;

            }
     

            return (c >= SuperMajority,null);
        }

        public async Task<(int,BabbleError)> Round(string x)
        {
            var (c, ok) = RoundCache.Get(x);
            if (ok)
            {
                return (c,null);
            }

            var (r,err) = await _round(x);

            if (err != null)
            {
                return (-1, err);
            }
            
            RoundCache.Add(x, r);

            return (r,null);
        }

        private async Task<(int round, BabbleError err)> _round(string x)
        {
            /*
    x is the Root
    Use Root.SelfParent.Round
*/

            var (rootsBySelfParent, _) = Store.RootsBySelfParent();

            var ok = rootsBySelfParent.TryGetValue(x, out var r);

            if (ok)
            {
                return (r.SelfParent.Round,null);
            }

       
          var (ex, err1) = await Store.GetEvent(x);

            if (err1 != null)
            {
                return (int.MinValue, err1);
            }

            //We are going to need the Root later
            
            var (root, err2) = await Store.GetRoot(ex.Creator());

            if (err2 != null)
            {
                return (int.MinValue, err2);
            }

            /*
              The Event is directly attached to the Root.
            */

            if (ex.SelfParent == root.SelfParent.Hash)
            {
                //Root is authoritative EXCEPT if other-parent is not in the root

                ok = root.Others.TryGetValue(ex.Hex(), out var other);

                if (ok && other.Hash == ex.OtherParent)
                {
                    return (root.NextRound, null);
                }
            }

            /*
                The Event's parents are "normal" Events.
                Use the whitepaper formula: parentRound + roundInc
            */

            var (parentRound,err3) = await Round(ex.SelfParent);

            if (err3 != null)
            {
                return (int.MinValue, err3);
            }


            if (ex.OtherParent != "")
            {
                int opRound;

                //XXX
                ok = root.Others.TryGetValue(ex.Hex(), out var other);

                if (ok && other.Hash == ex.OtherParent)
                {
                    opRound = root.NextRound;
                }
                else
                {
                    BabbleError err4;
                    (opRound,err4) = await Round(ex.OtherParent);
                    if (err4 != null)
                    {
                        return (int.MinValue, err4);
                    }
                }

                if (opRound > parentRound)
                {
                    parentRound = opRound;
                }
            }

            var c = 0;
            foreach (var w in await Store.RoundWitnesses(parentRound))
            {
                var (ss,err5) = await StronglySee(x, w);

                if (err5 != null)
                {
                    return (int.MinValue, err5);
                }
                
                if (ss)
                {
                    c++;
                }
            }

            if (c >= SuperMajority)
            {
                parentRound++;
            }

            return (parentRound, null);
        }

        ////true if x is a witness (first ev of a round for the owner)
        public async Task<(bool,BabbleError)>  Witness(string x)
        {
            var (ex, errx) = await Store.GetEvent(x);

            if (errx != null)
            {
                return (false,errx);
            }

            var (xRound,err1) = await Round(x);

            if (err1 != null)
            {
                return (false,err1);
            }

            var (spRound,err2) = await Round(ex.SelfParent);

            if (err2 != null)
            {
                return (false,err2);
            }



            return (xRound > spRound,null);
        }


        public async Task<(int,BabbleError)> RoundReceived(string x)
        {
            var (ex, errx) = await Store.GetEvent(x);

            if (errx != null)
            {
                return (-1,errx);
            }

            return (ex.GetRoundReceived() ?? -1,null);
        }

        private async Task<(int, StoreError)> LamportTimestamp(string x)
        {
            {
                var (c, ok) = TimestampCache.Get(x);

                if (ok)
                {
                    return (c, null);
                }
            }
            var (r, err) = await _lamportTimestamp(x);
            if (err != null)
            {
                return (-1, err);
            }

            TimestampCache.Add(x, r);
            return (r, null);
        }

        private async Task<(int, StoreError)> _lamportTimestamp(string x)
        {
            /*
                x is the Root
                User Root.SelfParent.LamportTimestamp
            */
            var (rootsBySelfParent, _) =  Store.RootsBySelfParent();
            {
                var ok = rootsBySelfParent.TryGetValue(x,out var r);

                if (ok)
                {
                    return (r.SelfParent.LamportTimestamp, null);
                }
            }
            var (ex, err) = await Store.GetEvent(x);
            if (err != null)
            {
                return (int.MinValue, err);
            }

            //We are going to need the Root later
            Root root;
            (root, err) = await Store.GetRoot(ex.Creator());
            if (err != null)
            {
                return (int.MinValue, err);
            }

            var plt = int.MinValue;
            //If it is the creator's first Event, use the corresponding Root
            if (ex.SelfParent == root.SelfParent.Hash)
            {
                plt = root.SelfParent.LamportTimestamp;
            }
            else
            {
                int t;
                (t, err) = await LamportTimestamp(ex.SelfParent);
                if (err != null)
                {
                    return (int.MinValue, err);
                }

                plt = t;
            }

            if (ex.OtherParent != "")
            {
                var opLT = int.MinValue;
                {
                    (_, err) = await Store.GetEvent(ex.OtherParent);

                    if (err == null)
                    {
                        //if we know the other-parent, fetch its Round directly
                        int t;
                        (t, err) = await LamportTimestamp(ex.OtherParent);
                        if (err != null)
                        {
                            return (int.MinValue, err);
                        }

                        opLT = t;
                    }

                    {
                        var ok = root.Others.TryGetValue(x, out var other);

                        if (ok && other.Hash == ex.OtherParent)
                        {
                            //we do not know the other-parent but it is referenced  in Root.Others
                            //we use the Root's LamportTimestamp
                            opLT = other.LamportTimestamp;
                        }
                    }
                }
                if (opLT > plt)
                {
                    plt = opLT;
                }
            }

            return (plt + 1, null);
        }

        //round(x) - round(y)
        public async Task<(int d, BabbleError err)> RoundDiff(string x, string y)
        {
            var (xRound,err1) = await Round(x);


            if (err1 != null)
            {
                return (int.MinValue, new HashgraphError($"ev {x} has negative round"));
            }

            var (yRound,err2) = await Round(y);

            if (err2 != null)
            {
                return (int.MinValue, new HashgraphError($"ev {y} has negative round"));
            }

            return (xRound - yRound, null);
        }



        //private async Task RecordBlockSignatures(BlockSignature[] blockSignatures)
        //{
        //    foreach (var bs in blockSignatures)
        //    {
        //        //check if validator belongs to list of participants
        //        var validatorHex = bs.Validator.ToHex();

        //        var ok = Participants.ContainsKey(validatorHex);
        //        if (!ok)
        //        {
        //            logger.Warning("Verifying Block signature. Unknown validator", new {bs.Index, Validator = validatorHex});
        //            continue;
        //        }

        //        Exception err;
        //        Block block;
        //        (block, err) = await Store.GetBlock(bs.Index);
        //        if (err != null)
        //        {
        //            logger.Warning("Verifying Block signature. Could not fetch Block", new {bs.Index, err.Message});
        //            continue;
        //        }

        //        bool valid;
        //        (valid, err) = block.Verify(bs);
        //        if (err != null)
        //        {
        //            logger.Warning("Verifying Block signature.", new {bs.Index, err.Message});
        //            continue;
        //        }

        //        if (!valid)
        //        {
        //            logger.Warning("Verifying Block signature. Invalid signature.", new {bs.Index});
        //            continue;
        //        }

        //        block.SetSignature(bs);

        //        err = await Store.SetBlock(block);

        //        if (err != null)
        //        {
        //            logger.Warning("Saving Block.", new {bs.Index, err.Message});
        //        }
        //    }
        //}

        //Check the SelfParent is the Creator's last known Event
        public BabbleError CheckSelfParent(Event ev)
        {
            var selfParent = ev.SelfParent;
            var creator = ev.Creator();

            var (creatorLastKnown, _, err) = Store.LastEventFrom(creator);
            if (err != null)
            {
                return err;
            }

            var selfParentLegit = selfParent == creatorLastKnown;

            return !selfParentLegit ? new HashgraphError("Self-parent not last known ev by creator") : null;
        }

        //Check if we know the OtherParent
        public async Task<BabbleError> CheckOtherParent(Event ev)
        {
            var otherParent = ev.OtherParent;

            if (!string.IsNullOrEmpty(otherParent))
            {
                //Check if we have it
                var (_, err) = await Store.GetEvent(otherParent);

                if (err != null)
                {
                    //it might still be in the Root
                    var (root, errr) = await Store.GetRoot(ev.Creator());

                    if (errr != null)
                    {
                        return errr;
                    }

         var ok = root.Others.TryGetValue(ev.Hex(), out var other);

                    if (ok && other.Hash == ev.OtherParent)
                    {
                        return null;
                    }

                    return new HashgraphError("Other-parent not known");
                }
            }

            return null;
        }

        ////initialize arrays of last ancestors and first descendants
        public async Task<BabbleError> InitEventCoordinates(Event ev)
        {
            var members = Participants.Len();

            ev.SetFirstDescendants(new OrderedEventCoordinates(members));

            int i = 0;
            foreach (var id in await Participants.ToIdSlice())
            {
                ev.FirstDescendants[i] = new Index
                {
                    ParticipantId = id, 
                    Event = new EventCoordinates(){Index=-1}

                };
                i++;

            }

            ev.SetLastAncestors(new OrderedEventCoordinates(members));

            var (selfParent, selfParentError) = await Store.GetEvent(ev.SelfParent);
            var (otherParent, otherParentError) = await Store.GetEvent(ev.OtherParent);

            if (selfParentError != null && otherParentError != null)
            {
                i = 0;
                foreach (var entry in ev.FirstDescendants)
                {
                    ev.LastAncestors[i] = new Index(){
                        ParticipantId = entry.ParticipantId, 
                        Event = new EventCoordinates()
                        {
                            Index = -1
                        }
                        };

                    i ++;
                }
            }
            else if (selfParentError != null)
            {
              ev.SetLastAncestors((OrderedEventCoordinates) otherParent.LastAncestors.ToList());
               
            }
            else if (otherParentError != null)
            {
            ev.SetLastAncestors((OrderedEventCoordinates)  selfParent.LastAncestors.ToList());
          
            }
            else
            {
                var selfParentLastAncestors = selfParent.LastAncestors;

                var otherParentLastAncestors = otherParent.LastAncestors;

                ev.SetLastAncestors((OrderedEventCoordinates)selfParentLastAncestors.ToList());

                i = 0;
                foreach (var la in ev.LastAncestors)
                {
                    if (ev.LastAncestors[i].Event.Index < otherParentLastAncestors[i].Event.Index) continue;
                    {

                        var laev=  ev.LastAncestors[i].Event;
                      laev.Index = otherParentLastAncestors[i].Event.Index;
                        laev.Hash = otherParentLastAncestors[i].Event.Hash;
                    }
                    i++;
                }
            }

            var index = ev.Index();

            var creator = ev.Creator();

            var ok = Participants.ByPubKey.TryGetValue(creator, out var creatorPeer);

            if (!ok)
            {
                return new HashgraphError($"Could not find fake creator id {creator}");
            }

            var hash = ev.Hex();

            var ii = ev.FirstDescendants.GetIdIndex(creatorPeer.ID);
            var jj = ev.LastAncestors.GetIdIndex(creatorPeer.ID);

            if (ii == -1) {
                return new HashgraphError($"Could not find first descendant from creator id ({ creatorPeer.ID})");
            }

            if (jj == -1)
            {
                return new HashgraphError($"Could not find last ancestor from creator id ({creatorPeer.ID})");
            }

            ev.FirstDescendants[ii].Event = new EventCoordinates {Index = index, Hash = hash};
            ev.LastAncestors[jj].Event =new  EventCoordinates {Index = index, Hash = hash};

            return null;
        }

        //update first decendant of each last ancestor to point to ev
        public async Task<BabbleError> UpdateAncestorFirstDescendant(Event ev)
        {
            var ok = Participants.ByPubKey.TryGetValue(ev.Creator(), out var creatorPeer);

            if (!ok)
            {
                return new HashgraphError($"Could not find fake creator id {ev.Creator()}");
            }

            var index = ev.Index();
            var hash = ev.Hex();

            for (var i = 0; i < ev.LastAncestors.Count; i++)
            {
                var ah = ev.LastAncestors[i].Event.Hash;

                while (!string.IsNullOrEmpty(ah))
                {
                    var (a, err) = await Store.GetEvent(ah);

                    if (err != null)
                    {
                        break;
                    }

                    var idx = a.FirstDescendants.GetIdIndex(creatorPeer.ID);


                    if (idx == -1)
                    {
                        return new HashgraphError($"Could not find first descendant by creator id ({ev.Creator()})");
                    }


                    if (a.FirstDescendants[idx].Event.Index == int.MaxValue)
                    {
                        a.FirstDescendants[idx].Event = new EventCoordinates
                        {
                            Index = index,
                            Hash = hash
                        };

                        err = await Store.SetEvent(a);

                        if (err != null)
                        {
                            return err;
                        }

                        ah = a.SelfParent;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            return null;
        }

     private async Task<(RootEvent, BabbleError)> createSelfParentRootEvent( Event ev)
        {
            var sp = ev.SelfParent;
            var (spLT, err1) =await LamportTimestamp(sp);
            if (err1 != null)
            {
                return (new RootEvent{}, err1);
            }
            var (spRound, err2) =await Round(sp);
            if (err2 != null)
            {
                return (new RootEvent{}, err2);
            }
            var selfParentRootEvent = new RootEvent
            {
                
                Hash=sp,
                CreatorId=Participants.ByPubKey[ev.Creator()].ID,
                Index=ev.Index()-1,
                LamportTimestamp=spLT,
                Round=spRound

            };
            return (selfParentRootEvent, null);
        }

        private async Task<(RootEvent, BabbleError)> CreateOtherParentRootEvent( Event ev)
        {
            var op = ev.OtherParent;

            //it might still be in the Root
            var (root, err1) = await Store.GetRoot(ev.Creator());
            if (err1 != null)
            {
                return (new RootEvent{}, err1);
            }
            {
                var ok = root.Others.TryGetValue(ev.Hex(), out var other);

                if (ok && other.Hash == op)
                {
                    return (other, null);
                }

            }
            var (otherParent, err2) =await Store.GetEvent(op);
            if (err2 != null)
            {
                return (new RootEvent{}, err2);
            }
            var (opLT, err3) = await LamportTimestamp(op);
            if (err3 != null)
            {
                return (new RootEvent{}, err3);
            }
            var (opRound, err4) =await Round(op);
            if (err4 != null)
            {
                return (new RootEvent{}, err4);
            }
            var otherParentRootEvent = new RootEvent
            {
                Hash=op,
                CreatorId=Participants.ByPubKey[otherParent.Creator()].ID,
                Index=otherParent.Index(),
                LamportTimestamp=opLT,
                Round=opRound
            };

            return (otherParentRootEvent, null);

        }

        private async Task<(Root, BabbleError)> createRoot( Event ev)
        {
            var (evRound, err1) = await Round(ev.Hex());
            if (err1 != null)
            {
                return (new Root{}, err1);
            }

            /*
                SelfParent
            */
            var (selfParentRootEvent, err2) = await createSelfParentRootEvent(ev);
            if (err2 != null)
            {
                return (new Root{}, err2);
            }

            /*
                OtherParent
            */
           RootEvent otherParentRootEvent=null;

            if (ev.OtherParent != "")
            {
                var (opre, err3) = await CreateOtherParentRootEvent(ev);
                if (err3 != null)
                {
                    return (new Root{}, err3);
                }
                otherParentRootEvent =  opre;
            }

            var root = new Root
            {
                NextRound = evRound,
                SelfParent = selfParentRootEvent,
                Others = new Dictionary<string, RootEvent>()
            };

            if (otherParentRootEvent != null)
            {
                root.Others[ev.Hex()] = new RootEvent()
                {
                    CreatorId = otherParentRootEvent.CreatorId,
                    Hash =otherParentRootEvent.Hash,
                    Index = otherParentRootEvent.Index,
                    LamportTimestamp = otherParentRootEvent.LamportTimestamp,
                    Round = otherParentRootEvent.Round


                };
            }
            return (root, null);
        }

        public async Task<BabbleError> SetWireInfo(Event ev)
        {
            var selfParentIndex = -1;

            var otherParentCreatorId = -1;

            var otherParentIndex = -1;

            //could be the first Event inserted for this creator. In this case, use Root
            var (lf, isRoot, _) = Store.LastEventFrom(ev.Creator());

            if (isRoot && lf == ev.SelfParent)
            {
                var (root, err) = await Store.GetRoot(ev.Creator());

                if (err != null)
                {
                    return err;
                }

                selfParentIndex = root.SelfParent.Index;
            }
            else
            {
                var (selfParent, err) = await Store.GetEvent(ev.SelfParent);

                if (err != null)
                {
                    return err;
                }

                selfParentIndex = selfParent.Index();
            }

            if (!string.IsNullOrEmpty(ev.OtherParent))
            {

                var (root, err1) = await Store.GetRoot(ev.Creator());

                if (err1 != null)
                {
                    return err1;
                }

                var ok = root.Others.TryGetValue(ev.Hex(), out var other);

                if (ok && other.Hash == ev.OtherParent)
                {
                    otherParentCreatorId = other.CreatorId;
                    otherParentIndex = other.Index;
                } else {

                    var (otherParent, err) = await Store.GetEvent(ev.OtherParent);

                    if (err != null)
                    {
                        return err;
                    }

                    otherParentCreatorId = Participants.ByPubKey[otherParent.Creator()].ID;
                    otherParentIndex = otherParent.Index();
                }
            }

            ev.SetWireInfo(selfParentIndex,
                otherParentCreatorId,
                otherParentIndex,
                Participants.ByPubKey[ev.Creator()].ID);

            return null;
        }


        private  void updatePendingRounds(Dictionary<int, int> decidedRounds)
        {
         foreach (var ur in PendingRounds)
                    {
                        var ok = decidedRounds.ContainsKey(ur.Index);

                        if (ok)
                        {
                            ur.Decided = true;
                        }

                    }
      
        }

        //Remove processed Signatures from SigPool
        private  void removeProcessedSignatures( Dictionary<int, bool> processedSignatures)
        {
            var newSigPool = new List <BlockSignature>();
            
            foreach (var bs in SigPool)

                    {
                        var ok = processedSignatures.ContainsKey(bs.Index);

                        if (!ok)
                        {
                            newSigPool.Add( bs);
                        }

               

            }
            SigPool = newSigPool;
        }

        /*******************************************************************************
        Public Methods
        *******************************************************************************/

        //InsertEvent attempts to insert an Event in the DAG. It verifies the signature,
        //checks the ancestors are known, and prevents the introduction of forks.


        public async Task<BabbleError> InsertEvent(Event ev, bool setWireInfo)
        {
            //verify signature

            var (ok, err) = ev.Verify();

            if (!ok)
            {
                return err ?? new HashgraphError("Invalid Event signature");
            }

            using (var tx = Store.BeginTx())

            {
               var  err2= CheckSelfParent(ev);
                if (err2 != null)
                {
                    return new HashgraphError($"CheckSelfParent: {err2.Message}");
                }

                var err3= await CheckOtherParent(ev);
                if (err3 != null)
                {
                    return new HashgraphError($"CheckOtherParent: {err3.Message}");
                }

                ev.SetTopologicalIndex(TopologicalIndex);
                TopologicalIndex++;

                if (setWireInfo)
                {
                    var err4 = await SetWireInfo(ev);
                    if (err4 != null)
                    {
                        return new HashgraphError($"SetWireInfo: {err4.Message}");
                    }
                }

                var err5 = await InitEventCoordinates(ev);
                if (err5 != null)
                {
                    return new HashgraphError($"InitEventCoordinates: {err5.Message}");
                }

                var err6 = await Store.SetEvent(ev);
                if (err6 != null)
                {
                    return new HashgraphError($"SetEvent: {err6.Message}");
                }

                var err7 = await UpdateAncestorFirstDescendant(ev);
                if (err7 != null)
                {
                    return new HashgraphError($"UpdateAncestorFirstDescendant: {err7.Message}");
                }

                UndeterminedEvents.Add(ev.Hex());

                if (ev.IsLoaded())
                {
                    PendingLoadedEvents++;
                }

                SigPool.AddRange(ev.BlockSignatures());

                tx.Commit();
            }

            return null;
        }

        /*
        DivideRounds assigns a Round and LamportTimestamp to Events, and flags them as
        witnesses if necessary. Pushes Rounds in the PendingRounds queue if necessary.
        */

        public async Task<BabbleError> DivideRounds()
        {
            logger.Debug("Divide Rounds {UndeterminedEvents}", UndeterminedEvents.Count);

            foreach (var hash in UndeterminedEvents)

            {

                var (ev,err1) = await Store.GetEvent(hash);

                if (err1 != null)
                {
                    return err1;
                }

                var updateEvent = false;

                /*
                Compute Event's round, update the corresponding Round object, and
                add it to the PendingRounds queue if necessary.
                */

                if (ev.Round == null)
                {

                    var (roundNumber, err2) = await Round(hash);

                    if (err2 != null)
                    {
                        return err1;
                    }

                    ev.SetRound(roundNumber);
                    updateEvent = true;


                    var (roundInfo, err3) = await Store.GetRound(roundNumber);

                    if (err3 != null && err3.StoreErrorType != StoreErrorType.KeyNotFound)
                    {
                        return err3;
                    }


                    /*
                    Why the lower bound?
                    Normally, once a Round has attained consensus, it is impossible for
                    new Events from a previous Round to be inserted; the lower bound
                    appears redundant. This is the case when the hashgraph grows
                    linearly, without jumps, which is what we intend by 'Normally'.
                    But the Reset function introduces a dicontinuity  by jumping
                    straight to a specific place in the hashgraph. This technique relies
                    on a base layer of Events (the corresponding Frame's Events) for
                    other Events to be added on top, but the base layer must not be
                    reprocessed.
                    */
                    
                    if (!roundInfo.Queued() && LastConsensusRound == null || roundNumber >= LastConsensusRound)
                    {

                        PendingRounds.Enqueue(new PendingRound {Index = roundNumber, Decided = false});
                        roundInfo.SetQueued();
                    }

                    var (witness, err4) =await Witness(hash);
                    
                    if (err4 != null)
                    {
                        return err4;
                    }

                    roundInfo.AddEvent(hash, witness);

                    var err5 = await Store.SetRound(roundNumber, roundInfo);
                    if (err5 != null)
                    {
                        return err5;
                    }

                }


                /*
               Compute the Event's LamportTimestamp
           */
                if (ev.LamportTimestamp == null)
                {

                    var (lamportTimestamp, err6) = await LamportTimestamp(hash);
                    if (err6 != null)
                    {
                        return err6;
                    }

                    ev.SetLamportTimestamp(lamportTimestamp);
                    updateEvent = true;
                }

                if (updateEvent)
                {
                    await Store.SetEvent(ev);
                }
            }

            return null;
        }

        //decide if witnesses are famous
        public async Task<BabbleError> DecideFame()
        {
            logger.Debug("Decide Fame");

            var votes = new Dictionary<string, Dictionary<string, bool>>(); //[x][y]=>vote(x,y)

            void SetVote(Dictionary<string, Dictionary<string, bool>> vts, string x, string y ,bool vote)
            {
                if (vts[x] == null) {
                    vts[x] = new Dictionary<string, bool>(); 
                }

                votes[x][y] = vote;
            }


            var decidedRounds = new Dictionary<int, int>(); // [round number] => index in UndecidedRounds

   
                var pos = 0;

                foreach (var r in PendingRounds)
                {
                    var roundIndex = r.Index;
                    var (roundInfo, err1) = await Store.GetRound(roundIndex);

                    if (err1 != null)
                    {
                        return err1;
                    }


                    foreach (var x in roundInfo.Witnesses())
                    {
                        if (roundInfo.IsDecided(x))
                        {
                            continue;
                        }

                        VOTE_LOOP:

                        //X:

                        for (var j = roundIndex + 1; j <= Store.LastRound(); j++)

                        {
                            foreach (var y in await Store.RoundWitnesses(j))
                            {
                                var diff = j - roundIndex;

                                if (diff == 1)
                                {

                                    var (ycx, err2) = await See(y, x);
                                    if (err2 != null)
                                    {
                                        return err2;
                                    }

                                    SetVote(votes, y, x, ycx);
                                }
                                else
                                {
                                    //count votes
                                    var ssWitnesses = new List<string>();
                                    foreach (var w in await Store.RoundWitnesses(j - 1))
                                    {
                                        var (ss, err3) = await StronglySee(y, w);
                                        if (err3 != null)
                                        {
                                            return err3;
                                        }

                                        if (ss)
                                        {
                                            ssWitnesses.Add(w);
                                        }
                                    }

                                    var yays = 0;

                                    var nays = 0;

                                    foreach (var w in ssWitnesses)
                                    {
                                        if (votes[w][x])
                                        {
                                            yays++;
                                        }
                                        else
                                        {
                                            nays++;
                                        }
                                    }

                                    var v = false;

                                    var t = nays;

                                    if (yays >= nays)
                                    {
                                        v = true;

                                        t = yays;
                                    }

                                    //normal round
                                    if ((float) diff % Participants.Len() > 0)
                                    {
                                        if (t >= SuperMajority)
                                        {
                                            roundInfo.SetFame(x, v);

                                            SetVote(votes, y, x, v);

                                            //break out of j loop

                                            goto X;
                                        }

                                        SetVote(votes, y, x, v);
                                    }
                                    else
                                    {
                                        //coin round
                                        if (t >= SuperMajority)
                                        {
                                            SetVote(votes, y, x, v);
                                        }
                                        else
                                        {
                                            SetVote(votes, y, x, MiddleBit(y)); //middle bit of y's hash
                                        }
                                    }
                                }
                            }
                        }

                        X: ;
                    }


                    var err4 = await Store.SetRound(roundIndex, roundInfo);
                    if (err4 != null)
                    {
                        return err4;
                    }

                    //Update decidedRounds and LastConsensusRound if all witnesses have been decided
                    if (roundInfo.WitnessesDecided())
                    {
                        decidedRounds[roundIndex] = pos;
                    
                    }

             

                    pos++;
                }

            updatePendingRounds(decidedRounds);
                return null;
  
        }

        //remove items from UndecidedRounds
        public void UpdateUndecidedRounds(Dictionary<int, int> decidedRounds)
        {
            logger.Debug("Update Undecided Rounds");

            var newUndecidedRounds = new Queue<int>();
            foreach (var ur in UndecidedRounds)
            {
                if (!decidedRounds.ContainsKey(ur))

                {
                    newUndecidedRounds.Enqueue(ur);
                }
            }

            UndecidedRounds = newUndecidedRounds;
        }

        public async Task SetLastConsensusRound(int i)
        {
            LastConsensusRound = i;
            LastCommitedRoundEvents = await Store.RoundEvents(i - 1);
        }

        //assign round received and timestamp to all evs
        public async Task<Exception> DecideRoundReceived()
        {
            foreach (var x in UndeterminedEvents)
            {
                var r = await Round(x);

                for (var i = r + 1; i <= Store.LastRound(); i++)

                {
                    var (tr, err) = await Store.GetRound(i);
                    if (err != null && err.StoreErrorType != StoreErrorType.KeyNotFound)
                    {
                        return err;
                    }

                    //skip if some witnesses are left undecided
                    if (!(tr.WitnessesDecided() && UndecidedRounds.Peek() > i))
                    {
                        continue;
                    }

                    var fws = tr.FamousWitnesses();

                    //set of famous witnesses that see x
                    var s = new List<string>();
                    foreach (var w in fws)
                    {
                        if (await See(w, x))
                        {
                            s.Add(w);
                        }
                    }

                    if (s.Count > fws.Length / 2)
                    {
                        var (ex, erre) = await Store.GetEvent(x);

                        if (erre != null)
                        {
                            return erre;
                        }

                        ex.SetRoundReceived(i);

                        var t = new List<string>();
                        foreach (var a in s)
                        {
                            t.Add(await OldestSelfAncestorToSee(a, x));
                        }

                        ex.SetConsensusTimestamp(await MedianTimestamp(t));

                        await Store.SetEvent(ex);

                        break;
                    }
                }
            }

            return null;
        }

        public async Task<Exception> FindOrder()
        {
            await DecideRoundReceived();

            var newConsensusEvents = new List<Event>();

            var newUndeterminedEvents = new List<string>();
            Exception err;
            foreach (var x in UndeterminedEvents)
            {
                Event ex;
                (ex, err) = await Store.GetEvent(x);

                if (err != null)
                {
                    return err;
                }

                if (ex.GetRoundReceived() != null)
                {
                    newConsensusEvents.Add(ex);
                }
                else
                {
                    newUndeterminedEvents.Add(x);
                }
            }

            UndeterminedEvents = newUndeterminedEvents;

            newConsensusEvents.Sort(new Event.EventByConsensus());

            err = await HandleNewConsensusEvents(newConsensusEvents);
            return err;
        }

        public async Task<HashgraphError> HandleNewConsensusEvents(IEnumerable<Event> newConsensusEvents)
        {
            var blockMap = new Dictionary<int, List<byte[]>>(); // [RoundReceived] => []Transactions
            var blockOrder = new List<int>(); // [index] => RoundReceived

            foreach (var e in newConsensusEvents)
            {
                Store.AddConsensusEvent(e.Hex());

                ConsensusTransactions += e.Transactions().Length;

                if (e.IsLoaded())
                {
                    PendingLoadedEvents--;
                }

                var rr = e.GetRoundReceived() ?? -1;
                var ok = blockMap.TryGetValue(rr, out var btxs);
                if (!ok)
                {
                    btxs = new List<byte[]>();
                    blockOrder.Add(rr);
                }

                btxs.AddRange(e.Transactions());
                blockMap[rr] = btxs;
            }

            foreach (var rr in blockOrder)
            {
                var blockTxs = blockMap[rr];
                if (blockTxs.Count > 0)
                {
                    var (block, err) = await CreateAndInsertBlock(rr, blockTxs.ToArray());
                    if (err != null)
                    {
                        return err;
                    }

                    logger.Debug("Block created! Index={Index}", block.Index());

                    if (CommitCh != null)
                    {
                        await CommitCh.EnqueueAsync(block);
                    }
                }
            }

            return null;
        }

        public async Task<(Block, HashgraphError)> CreateAndInsertBlock(int roundReceived, byte[][] txs)
        {
            var block = new Block(LastBlockIndex + 1, roundReceived, txs);

            Exception err = await Store.SetBlock(block);

            if (err != null)
            {
                return (new Block(), new HashgraphError(err.Message, err));
            }

            LastBlockIndex++;
            return (block, null);
        }

        public async Task<DateTimeOffset> MedianTimestamp(List<string> evHashes)
        {
            var evs = new List<Event>();
            foreach (var x in evHashes)
            {
                var (ex, _) = await Store.GetEvent(x);
                evs.Add(ex);
            }

            evs.Sort(new Event.EventByTimeStamp());
            return evs[evs.Count / 2].Body.Timestamp;
        }

        public string[] ConsensusEvents()
        {
            return Store.ConsensusEvents();
        }

        //number of evs per participants
        public Task<Dictionary<int, int>> KnownEvents()
        {
            return Store.KnownEvents();
        }

        public Exception Reset(Dictionary<string, Root> roots)
        {
            Store.Reset(roots);
            UndeterminedEvents = new List<string>();
            UndecidedRounds = new Queue<int>();
            PendingLoadedEvents = 0;
            TopologicalIndex = 0;
            var cacheSize = Store.CacheSize();
            AncestorCache = new LruCache<string, bool>(cacheSize, null, logger, "AncestorCache");
            SelfAncestorCache = new LruCache<string, bool>(cacheSize, null, logger, "SelfAncestorCache");
            OldestSelfAncestorCache = new LruCache<string, string>(cacheSize, null, logger, "OldestAncestorCache");
            StronglySeeCache = new LruCache<string, bool>(cacheSize, null, logger, "StronglySeeCache");
            ParentRoundCache = new LruCache<string, ParentRoundInfo>(cacheSize, null, logger, "ParentRoundCache");
            RoundCache = new LruCache<string, int>(cacheSize, null, logger, "RoundCache");

            return null;
        }

        public async Task<(Frame frame, Exception err)> GetFrame()
        {
            Exception err;

            var lastConsensusRoundIndex = 0;
            var lcr = LastConsensusRound;
            if (lcr != null)
            {
                lastConsensusRoundIndex = (int) lcr;
            }

            RoundInfo lastConsensusRound;
            (lastConsensusRound, err) = await Store.GetRound(lastConsensusRoundIndex);
            if (err != null)
            {
                return (new Frame(), err);
            }

            var witnessHashes = lastConsensusRound.Witnesses();
            var evs = new List<Event>();
            var roots = new Dictionary<string, Root>();
            foreach (var wh in witnessHashes)
            {
                Event w;
                (w, err) = await Store.GetEvent(wh);
                if (err != null)
                {
                    return (new Frame(), err);
                }

                evs.Add(w);
                roots.Add(w.Creator(), new Root
                {
                    X = w.SelfParent,
                    Y = w.OtherParent,
                    Index = w.Index() - 1,
                    Round = await Round(w.SelfParent),
                    Others = new Dictionary<string, string>()
                });
                string[] participantEvents;
                (participantEvents, err) = await Store.ParticipantEvents(w.Creator(), w.Index());
                if (err != null)
                {
                    return (new Frame(), err);
                }

                foreach (var e in participantEvents)
                {
                    var (ev, errev) = await Store.GetEvent(e);
                    if (errev != null)
                    {
                        return (new Frame(), errev);
                    }

                    evs.Add(ev);
                }
            }

            //Not every participant necessarily has a witness in LastConsensusRound.
            //Hence, there could be participants with no Root at this point.
            //For these partcipants, use their last known Event.
            foreach (var p in Participants)
            {
                if (!roots.ContainsKey(p.Key))
                {
                    var (last, isRoot, errp) = Store.LastEventFrom(p.Key);
                    if (errp != null)
                    {
                        return (new Frame(), errp);
                    }

                    Root root;
                    if (isRoot)
                    {
                        (root, err) = await Store.GetRoot(p.Key);
                        if (root == null)
                        {
                            return (new Frame(), err);
                        }
                    }
                    else
                    {
                        Event ev;
                        (ev, err) = await Store.GetEvent(last);
                        if (err != null)
                        {
                            return (new Frame(), err);
                        }

                        evs.Add(ev);
                        root = new Root
                        {
                            X = ev.SelfParent,
                            Y = ev.OtherParent,
                            Index = ev.Index() - 1,
                            Round = await Round(ev.SelfParent)
                        };
                    }

                    roots.Add(p.Key, root);
                }
            }

            evs.Sort(new Event.EventByTopologicalOrder());

            //Some Events in the Frame might have other-parents that are outside of the
            //Frame (cf root.go ex 2)
            //When inserting these Events in a newly reset hashgraph, the CheckOtherParent
            //method would return an error because the other-parent would not be found.
            //So we make it possible to also look for other-parents in the creator's Root.
            var treated = new Dictionary<string, bool>();
            foreach (var ev in evs)
            {
                treated.Add(ev.Hex(), true);
                var otherParent = ev.OtherParent;
                if (!string.IsNullOrEmpty(otherParent))
                {
                    var ok = treated.TryGetValue(otherParent, out var opt);
                    if (!opt || !ok)
                    {
                        if (ev.SelfParent != roots[ev.Creator()].X)
                        {
                            roots[ev.Creator()].Others[ev.Hex()] = otherParent;
                        }
                    }
                }
            }

            var frame = new Frame
            {
                Roots = roots,
                Events = evs.ToArray()
            };
            return (frame, null);
        }

        //Bootstrap loads all Events from the Store's DB (if there is one) and feeds
        //them to the Hashgraph (in topological order) for consensus ordering. After this
        //method call, the Hashgraph should be in a state coeherent with the 'tip' of the
        //Hashgraph
        public async Task<Exception> Bootstrap()
        {
            Exception err;
            if (Store is LocalDbStore)

            {
                logger.Debug("Bootstrap");

                using (var tx = Store.BeginTx())
                {
                    //Retreive the Events from the underlying DB. They come out in topological
                    //order
                    Event[] topologicalEvents;
                    (topologicalEvents, err) = await ((LocalDbStore) Store).DbTopologicalEvents();

                    if (err != null)
                    {
                        return err;
                    }

                    logger.Debug("Topological Event Count {count}", topologicalEvents.Length);

                    //Insert the Events in the Hashgraph
                    foreach (var e in topologicalEvents)
                    {
                        err = await InsertEvent(e, true);

                        if (err != null)
                        {
                            return err;
                        }
                    }

                    //Compute the consensus order of Events
                    err = await DivideRounds();
                    if (err != null)
                    {
                        return err;
                    }

                    err = await DecideFame();
                    if (err != null)
                    {
                        return err;
                    }

                    err = await FindOrder();

                    if (err != null)
                    {
                        return err;
                    }

                    tx.Commit();
                }
            }

            return null;
        }


        
        public async Task<(Event ev, Exception err)> ReadWireInfo(WireEvent wev)
        {
            var selfParent = "";
            var otherParent = "";
            Exception err;

            var creator = ReverseParticipants[wev.Body.CreatorId];
            var creatorBytes = creator.FromHex();

            if (wev.Body.SelfParentIndex >= 0)
            {
                (selfParent, err) = await Store.ParticipantEvent(creator, wev.Body.SelfParentIndex);
                if (err != null)
                {
                    return (null, err);
                }
            }

            if (wev.Body.OtherParentIndex >= 0)
            {
                var otherParentCreator = ReverseParticipants[wev.Body.OtherParentCreatorId];
                (otherParent, err) = await Store.ParticipantEvent(otherParentCreator, wev.Body.OtherParentIndex);
                if (err != null)
                {
                    return (null, err);
                }
            }

            var body = new EventBody
            {
                Transactions = wev.Body.Transactions,
                BlockSignatures = wev.BlockSignatures(creatorBytes),
                Parents = new[] {selfParent, otherParent},
                Creator = creatorBytes,
                Timestamp = wev.Body.Timestamp,
                Index = wev.Body.Index
            };

            body.SetSelfParentIndex(wev.Body.SelfParentIndex);
            body.SetOtherParentCreatorId(wev.Body.OtherParentCreatorId);
            body.SetOtherParentIndex(wev.Body.OtherParentIndex);
            body.SetCreatorId(wev.Body.CreatorId);

            var ev = new Event
            {
                Body = body,
                Signiture = wev.Signiture
            };
            return (ev, null);
        }









        public bool MiddleBit(string ehex)
        {
            var hash = ehex.Substring(2).StringToBytes();
            if (hash.Length > 0 && hash[hash.Length / 2] == 0)
            {
                return false;
            }

            return true;
        }

        public void SetVote(Dictionary<string, Dictionary<string, bool>> votes, string x, string y, bool vote)
        {
            if (votes.TryGetValue(x, out var v))
            {
                v[y] = vote;
                return;
            }

            votes.Add(x, new Dictionary<string, bool> {{y, vote}});
        }
    }

    public class Frame
    {
        public Dictionary<string, Root> Roots { get; set; }
        public Event[] Events { get; set; }
    }
}