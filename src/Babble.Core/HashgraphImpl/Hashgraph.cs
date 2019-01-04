using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Babble.Core.Common;
using Babble.Core.HashgraphImpl.Model;
using Babble.Core.HashgraphImpl.Stores;
using Babble.Core.PeersImpl;
using Babble.Core.Util;
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

        public int? AnchorBlock { get; set; } //index of last block with enough signatures

        public int LastCommitedRoundEvents { get; set; } //number of evs in round before LastConsensusRound

        public List<BlockSignature> SigPool { get; set; } = new List<BlockSignature>(); //Pool of Block signatures that need to be processed

        public int ConsensusTransactions { get; set; } //number of consensus transactions
        public int PendingLoadedEvents { get; set; } //number of loaded evs that are not yet committed
        public BufferBlock<Block> CommitCh { get; set; } //channel for committing evs
        public int TopologicalIndex { get; set; } //counter used to order evs in topological order
        public int SuperMajority { get; set; }
        public int TrustCount { get; set; }

        public LruCache<string, bool> AncestorCache { get; set; }
        public LruCache<string, bool> SelfAncestorCache { get; set; }
        public LruCache<string, bool> StronglySeeCache { get; set; }
        public LruCache<string, int> RoundCache { get; set; }
        public LruCache<string, int> TimestampCache { get; set; }

        private readonly ILogger logger;

        public Hashgraph(Peers participants, IStore store, BufferBlock<Block> commitCh, ILogger logger)
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
               return (c, null);
            }

            var (a, err) = await _ancestor(x, y);

            if (err != null)
            {
                return (false, err);
            }

            AncestorCache.Add(Key.New(x, y), a);

            //logger.Debug(a.ToString());
            return (a, null);
        }

        private async Task<(bool, BabbleError)> _ancestor(string x, string y)
        {
            if (x == y)
            {
                return (true, null);
            }

            var (ex, errx) = await Store.GetEvent(x);

            if (errx != null)
            {
                return (false, errx);
            }

            var (ey, erry) = await Store.GetEvent(y);

            if (erry != null)
            {
                return (false, erry);
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
                return (c, null);
            }

            var (a, err) = await _selfAncestor(x, y);

            if (err != null)
            {
                return (false, err);
            }

            SelfAncestorCache.Add(Key.New(x, y), a);

            return (a, null);
        }

        private async Task<(bool, BabbleError)> _selfAncestor(string x, string y)
        {
            if (x == y)
            {
                return (true, null);
            }

            var (ex, errx) = await Store.GetEvent(x);

            if (errx != null)
            {
                return (false, errx);
            }

            var exCreator = Participants.ByPubKey[ex.Creator()].ID;

            var (ey, erry) = await Store.GetEvent(y);
            if (erry != null)
            {
                return (false, erry);
            }

            var eyCreator = Participants.ByPubKey[ey.Creator()].ID;

            return (exCreator == eyCreator && ex.Index() >= ey.Index(), null);
        }

        //true if x sees y
        public Task<(bool, BabbleError)> See(string x, string y)
        {
            return Ancestor(x, y);
            //it is not necessary to detect forks because we assume that the InsertEvent
            //function makes it impossible to insert two Events at the same height for
            //the same participant.
        }

        //true if x strongly sees y
        public async Task<(bool, BabbleError)> StronglySee(string x, string y)
        {
            var (c, ok) = StronglySeeCache.Get(Key.New(x, y));
            if (ok)
            {
                return (c, null);
            }

            var (ss, err) = await _stronglySee(x, y);

            if (err != null)
            {
                return (false, err);
            }

            StronglySeeCache.Add(Key.New(x, y), ss);
            return (ss, null);
        }

        public async Task<(bool, BabbleError)> _stronglySee(string x, string y)
        {
            var (ex, errx) = await Store.GetEvent(x);

            if (errx != null)
            {
                return (false, errx);
            }

            var (ey, erry) = await Store.GetEvent(y);

            if (erry != null)
            {
                return (false, erry);
            }

            var c = 0;

            int i = 0;
            foreach (var entry in ex.LastAncestors.Values)
            {
                if (entry.Event.Index >= ey.FirstDescendants.Values[i].Event.Index)
                {
                    c++;
                }

                i++;
            }

            return (c >= SuperMajority, null);
        }

        public async Task<(int, BabbleError)> Round(string x)
        {
            var (c, ok) = RoundCache.Get(x);
            if (ok)
            {
                return (c, null);
            }

            var (r, err) = await _round(x);

            if (err != null)
            {
                return (-1, err);
            }

            RoundCache.Add(x, r);

            return (r, null);
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
                return (r.SelfParent.Round, null);
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

            var (parentRound, err3) = await Round(ex.SelfParent);

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
                    (opRound, err4) = await Round(ex.OtherParent);
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
                var (ss, err5) = await StronglySee(x, w);

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
        public async Task<(bool, BabbleError)> Witness(string x)
        {
            var (ex, errx) = await Store.GetEvent(x);

            if (errx != null)
            {
                return (false, errx);
            }

            var (xRound, err1) = await Round(x);

            if (err1 != null)
            {
                return (false, err1);
            }

            var (spRound, err2) = await Round(ex.SelfParent);

            if (err2 != null)
            {
                return (false, err2);
            }

            return (xRound > spRound, null);
        }

        public async Task<(int, BabbleError)> RoundReceived(string x)
        {
            var (ex, errx) = await Store.GetEvent(x);

            if (errx != null)
            {
                return (-1, errx);
            }

            return (ex.GetRoundReceived() ?? -1, null);
        }

        public async Task<(int, StoreError)> LamportTimestamp(string x)
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
            var (rootsBySelfParent, _) = Store.RootsBySelfParent();
            {
                var ok = rootsBySelfParent.TryGetValue(x, out var r);

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
            var (xRound, err1) = await Round(x);

            if (err1 != null)
            {
                return (int.MinValue, new HashgraphError($"ev {x} has negative round"));
            }

            var (yRound, err2) = await Round(y);

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
                ev.FirstDescendants.Values[i] = new Index
                {
                    ParticipantId = id,
                    Event = new EventCoordinates {Index = int.MaxValue}
                };
                i++;
            }

            ev.LastAncestors=new OrderedEventCoordinates(members);

            var (selfParent, selfParentError) = await Store.GetEvent(ev.SelfParent);
            var (otherParent, otherParentError) = await Store.GetEvent(ev.OtherParent);

            if (selfParentError != null && otherParentError != null)
            {
                i = 0;
                foreach (var entry in ev.FirstDescendants.Values)
                {
                    ev.LastAncestors.Values[i] = new Index
                    {
                        ParticipantId = entry.ParticipantId,
                        Event = new EventCoordinates
                        {
                            Index = -1
                        }
                    };

                    i++;
                }
            }
            else if (selfParentError != null)
            {
                Array.Copy(otherParent.LastAncestors.CloneValues(), 0, ev.LastAncestors.Values, 0, members);

            }
            else if (otherParentError != null)
            {
                Array.Copy(selfParent.LastAncestors.CloneValues(), 0, ev.LastAncestors.Values, 0, members);

            }
            else
            {
                var selfParentLastAncestors = selfParent.LastAncestors;

                var otherParentLastAncestors = otherParent.LastAncestors;
                
                Array.Copy(selfParentLastAncestors.CloneValues(), 0, ev.LastAncestors.Values, 0, members);

                i = 0;
                foreach (var la in ev.LastAncestors.Values)
                {
                    if (ev.LastAncestors.Values[i].Event.Index < otherParentLastAncestors.Values[i].Event.Index) 
                    {
                        var eventCoordinates = ev.LastAncestors.Values[i].Event;
                        eventCoordinates.Index = otherParentLastAncestors.Values[i].Event.Index;
                        eventCoordinates.Hash = otherParentLastAncestors.Values[i].Event.Hash;
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

            if (ii == -1)
            {
                return new HashgraphError($"Could not find first descendant from creator id ({creatorPeer.ID})");
            }

            if (jj == -1)
            {
                return new HashgraphError($"Could not find last ancestor from creator id ({creatorPeer.ID})");
            }

            ev.FirstDescendants.Values[ii].Event = new EventCoordinates {Index = index, Hash = hash};
            ev.LastAncestors.Values[jj].Event = new EventCoordinates {Index = index, Hash = hash};

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

            for (var i = 0; i < ev.LastAncestors.Values.Count(); i++)
            {
                var ah = ev.LastAncestors.Values[i].Event.Hash;

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

                    if (a.FirstDescendants.Values[idx].Event.Index == int.MaxValue)
                    {
                        a.FirstDescendants.Values[idx].Event = new EventCoordinates
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

        private async Task<(RootEvent, BabbleError)> createSelfParentRootEvent(Event ev)
        {
            var sp = ev.SelfParent;
            var (spLT, err1) = await LamportTimestamp(sp);
            if (err1 != null)
            {
                return (new RootEvent(), err1);
            }

            var (spRound, err2) = await Round(sp);
            if (err2 != null)
            {
                return (new RootEvent(), err2);
            }

            var selfParentRootEvent = new RootEvent
            {
                Hash = sp,
                CreatorId = Participants.ByPubKey[ev.Creator()].ID,
                Index = ev.Index() - 1,
                LamportTimestamp = spLT,
                Round = spRound
            };
            return (selfParentRootEvent, null);
        }

        private async Task<(RootEvent, BabbleError)> CreateOtherParentRootEvent(Event ev)
        {
            var op = ev.OtherParent;

            //it might still be in the Root
            var (root, err1) = await Store.GetRoot(ev.Creator());
            if (err1 != null)
            {
                return (new RootEvent(), err1);
            }

            {
                var ok = root.Others.TryGetValue(ev.Hex(), out var other);

                if (ok && other.Hash == op)
                {
                    return (other, null);
                }
            }
            var (otherParent, err2) = await Store.GetEvent(op);
            if (err2 != null)
            {
                return (new RootEvent(), err2);
            }

            var (opLT, err3) = await LamportTimestamp(op);
            if (err3 != null)
            {
                return (new RootEvent(), err3);
            }

            var (opRound, err4) = await Round(op);
            if (err4 != null)
            {
                return (new RootEvent(), err4);
            }

            var otherParentRootEvent = new RootEvent
            {
                Hash = op,
                CreatorId = Participants.ByPubKey[otherParent.Creator()].ID,
                Index = otherParent.Index(),
                LamportTimestamp = opLT,
                Round = opRound
            };

            return (otherParentRootEvent, null);
        }

        private async Task<(Root, BabbleError)> createRoot(Event ev)
        {
            var (evRound, err1) = await Round(ev.Hex());
            if (err1 != null)
            {
                return (new Root(), err1);
            }

            /*
                SelfParent
            */
            var (selfParentRootEvent, err2) = await createSelfParentRootEvent(ev);
            if (err2 != null)
            {
                return (new Root(), err2);
            }

            /*
                OtherParent
            */
            RootEvent otherParentRootEvent = null;

            if (ev.OtherParent != "")
            {
                var (opre, err3) = await CreateOtherParentRootEvent(ev);
                if (err3 != null)
                {
                    return (new Root(), err3);
                }

                otherParentRootEvent = opre;
            }

            var root = new Root
            {
                NextRound = evRound,
                SelfParent = selfParentRootEvent,
                Others = new Dictionary<string, RootEvent>()
            };

            if (otherParentRootEvent != null)
            {
                root.Others[ev.Hex()] = new RootEvent
                {
                    CreatorId = otherParentRootEvent.CreatorId,
                    Hash = otherParentRootEvent.Hash,
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
                }
                else
                {
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

        private void updatePendingRounds(Dictionary<int, int> decidedRounds)
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
        private void removeProcessedSignatures(Dictionary<int, bool> processedSignatures)
        {
            var newSigPool = new List<BlockSignature>();

            foreach (var bs in SigPool)

            {
                var ok = processedSignatures.ContainsKey(bs.Index);

                if (!ok)
                {
                    newSigPool.Add(bs);
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
                var err2 = CheckSelfParent(ev);
                if (err2 != null)
                {
                    return new HashgraphError($"CheckSelfParent: {err2}");
                }

                var err3 = await CheckOtherParent(ev);
                if (err3 != null)
                {
                    return new HashgraphError($"CheckOtherParent: {err3}");
                }

                ev.SetTopologicalIndex(TopologicalIndex);
                TopologicalIndex++;

                if (setWireInfo)
                {
                    var err4 = await SetWireInfo(ev);
                    if (err4 != null)
                    {
                        return new HashgraphError($"SetWireInfo: {err4}");
                    }
                }

                var err5 = await InitEventCoordinates(ev);
                if (err5 != null)
                {
                    return new HashgraphError($"InitEventCoordinates: {err5}");
                }

                var err6 = await Store.SetEvent(ev);
                if (err6 != null)
                {
                    return new HashgraphError($"SetEvent: {err6}");
                }

                var err7 = await UpdateAncestorFirstDescendant(ev);
                if (err7 != null)
                {
                    return new HashgraphError($"UpdateAncestorFirstDescendant: {err7}");
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
                var (ev, err1) = await Store.GetEvent(hash);

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

                    var (witness, err4) = await Witness(hash);

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

            void SetVote(Dictionary<string, Dictionary<string, bool>> vts, string x, string y, bool vote)
            {
                if (vts[x] == null)
                {
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

        ////remove items from UndecidedRounds
        //public void UpdateUndecidedRounds(Dictionary<int, int> decidedRounds)
        //{
        //    logger.Debug("Update Undecided Rounds");

        //    var newUndecidedRounds = new Queue<int>();
        //    foreach (var ur in UndecidedRounds)
        //    {
        //        if (!decidedRounds.ContainsKey(ur))

        //        {
        //            newUndecidedRounds.Enqueue(ur);
        //        }
        //    }

        //    UndecidedRounds = newUndecidedRounds;
        //}

        //public async Task SetLastConsensusRound(int i)
        //{
        //    LastConsensusRound = i;
        //    LastCommitedRoundEvents = await Store.RoundEvents(i - 1);
        //}

        //assign round received and timestamp to all evs
        public async Task<BabbleError> DecideRoundReceived()
        {
            var newUndeterminedEvents = new List<string>();

            /* From whitepaper - 18/03/18
	           "[...] An event is said to be “received” in the first round where all the
	           unique famous witnesses have received it, if all earlier rounds have the
	           fame of all witnesses decided"
	        */

            foreach (var x in UndeterminedEvents)
            {
                var received = false;
                var (r, err1) = await Round(x);

                if (err1 != null)
                {
                    return err1;
                }

                for (var i = r + 1; i <= Store.LastRound(); i++)

                {
                    var (tr, err2) = await Store.GetRound(i);
                    if (err2 != null)
                    {
                        //Can happen after a Reset/FastSync
                        if (LastConsensusRound != null &&
                            r < LastConsensusRound)
                        {
                            received = true;
                            break;
                        }

                        return err2;
                    }

                    //We are looping from earlier to later rounds; so if we encounter
                    //one round with undecided witnesses, we are sure that this event
                    //is not "received". Break out of i loop
                    if (!tr.WitnessesDecided())
                    {
                        break;
                    }

                    var fws = tr.FamousWitnesses();

                    //set of famous witnesses that see x
                    var s = new List<string>();
                    foreach (var w in fws)
                    {
                        var (see, err3) = await See(w, x);

                        if (err3 != null)
                        {
                            return err3;
                        }

                        if (see)
                        {
                            s.Add(w);
                        }
                    }

                    if (s.Count == fws.Length && s.Count > 0)
                    {
                        received = true;

                        var (ex, err4) = await Store.GetEvent(x);

                        if (err4 != null)
                        {
                            return err4;
                        }

                        ex.SetRoundReceived(i);

                        var err5 = await Store.SetEvent(ex);
                        if (err5 != null)
                        {
                            return err5;
                        }

                        tr.SetConsensusEvent(x);
                        var err6 = await Store.SetRound(i, tr);
                        if (err6 != null)
                        {
                            return err6;
                        }

                        //break out of i loop
                        break;
                    }
                }

                if (!received)
                {
                    newUndeterminedEvents.Add(x);
                }
            }

            UndeterminedEvents = newUndeterminedEvents;

            return null;
        }

        //ProcessDecidedRounds takes Rounds whose witnesses are decided, computes the
        //corresponding Frames, maps them into Blocks, and commits the Blocks via the
        //commit channel
        public async Task<BabbleError> ProcessDecidedRounds()
        {
            //Defer removing processed Rounds from the PendingRounds Queue
            var processedIndex = 0;
            //defer(() =>
            //{
            //    
            //}());

            try
            {
                foreach (var r in PendingRounds)
                {
                    //Although it is possible for a Round to be 'decided' before a previous
                    //round, we should NEVER process a decided round before all the previous
                    //rounds are processed.
                    if (!r.Decided)
                    {
                        break;
                    }

                    //This is similar to the lower bound introduced in DivideRounds; it is
                    //redundant in normal operations, but becomes necessary after a Reset.
                    //Indeed, after a Reset, LastConsensusRound is added to PendingRounds,
                    //but its ConsensusEvents (which are necessarily 'under' this Round) are
                    //already deemed committed. Hence, skip this Round after a Reset.
                    if (LastConsensusRound != null && r.Index == LastConsensusRound)
                    {
                        continue;
                    }

                    var (frame, err1) = await GetFrame(r.Index);
                    if (err1 != null)
                    {
                        return new HashgraphError($"Getting Frame {r.Index}: {err1.Message}");
                    }

                    var (round, err2) = await Store.GetRound(r.Index);
                    if (err2 != null)
                    {
                        return err2;
                    }

                    //LogContext.Push()
                    //logger.WithFields(logrus.Fields{"round_received":r.Index,"witnesses":round.FamousWitnesses(),"events":len(frame.Events),"roots":frame.Roots,}).Debugf("Processing Decided Round");

                    if (frame.Events.Any())
                    {
                        foreach (var e in frame.Events)
                        {
                            var err3 = Store.AddConsensusEvent(e);
                            if (err3 != null)
                            {
                                return err3;
                            }

                            ConsensusTransactions += e.Transactions().Count();
                            if (e.IsLoaded())
                            {
                                PendingLoadedEvents--;
                            }
                        }

                        var lastBlockIndex = await Store.LastBlockIndex();
                        var (block, err4) = Block.NewBlockFromFrame(lastBlockIndex + 1, frame);
                        if (err4 != null)
                        {
                            return err4;
                        }

                        var err5 = await Store.SetBlock(block);

                        if (err5 != null)
                        {
                            return err5;
                        }

                        if (CommitCh != null)
                        {
                            await CommitCh.SendAsync(block);
                        }
                        else
                        {
                            logger.Debug("No Events to commit for ConsensusRound {Index}", r.Index);
                        }

                        processedIndex++;

                        if (LastConsensusRound == null || r.Index > LastConsensusRound)
                        {
                            SetLastConsensusRound(r.Index);
                        }
                    }
                }

                return null;
            }
            finally
            {
                PendingRounds = new Queue<PendingRound>(PendingRounds.Take(processedIndex));
            }
        }

        //GetFrame computes the Frame corresponding to a RoundReceived.
        private async Task<(Frame, BabbleError)> GetFrame(int roundReceived)
        {
            //Try to get it from the Store first
            var (frame, err1) = await Store.GetFrame(roundReceived);
            if (err1 == null || err1.StoreErrorType != StoreErrorType.KeyNotFound)
            {
                return (frame, err1);
            }

            //Get the Round and corresponding consensus Events
            var (round, err2) = await Store.GetRound(roundReceived);
            if (err2 != null)
            {
                return (new Frame(), err2);
            }

            var events = new List<Event>();

            foreach (var eh in round.ConsensusEvents())

            {
                var (e, err3) = await Store.GetEvent(eh);
                if (err3 != null)
                {
                    return (new Frame(), err3);
                }

                events.Add(e);
            }

            events.Sort(new Event.EventByLamportTimeStamp());

            // Get/Create Roots
            var roots = new Dictionary<string, Root>();
            //The events are in topological order. Each time we run into the first Event
            //of a participant, we create a Root for it.

            foreach (var ev in events)
            {
                var p = ev.Creator();

                var ok = roots.ContainsKey(p);

                if (!ok)
                {
                    var (root, err4) = await createRoot(ev);
                    if (err4 != null)
                    {
                        return (new Frame(), err4);
                    }

                    roots[ev.Creator()] = root;
                }
            }

            //Every participant needs a Root in the Frame. For the participants that
            //have no Events in this Frame, we create a Root from their last consensus
            //Event, or their last known Root

            foreach (var p in await Participants.ToPubKeySlice())

            {
                var ok = roots.ContainsKey(p);

                if (!ok)
                {
                    Root root;

                    var (lastConsensusEventHash, isRoot, err5) = Store.LastConsensusEventFrom(p);
                    if (err5 != null)
                    {
                        return (new Frame(), err5);
                    }

                    if (isRoot)
                    {
                        (root, _) = await Store.GetRoot(p);
                    }
                    else
                    {
                        var (lastConsensusEvent, err6) = await Store.GetEvent(lastConsensusEventHash);
                        if (err6 != null)
                        {
                            return (new Frame(), err6);
                        }

                        BabbleError err7;
                        (root, err7) = await createRoot(lastConsensusEvent);
                        if (err7 != null)
                        {
                            return (new Frame(), err7);
                        }
                    }

                    roots[p] = root;
                }
            }

            //Some Events in the Frame might have other-parents that are outside of the
            //Frame (cf root.go ex 2)
            //When inserting these Events in a newly reset hashgraph, the CheckOtherParent
            //method would return an error because the other-parent would not be found.
            //So we make it possible to also look for other-parents in the creator's Root.
            var treated = new Dictionary<string, bool>();
            foreach (var ev in events)
            {
                treated[ev.Hex()] = true;
                var otherParent = ev.OtherParent;
                if (otherParent != "")
                {
                    var ok = treated.TryGetValue(otherParent, out var opt);
                    if (!opt || !ok)

                    {
                        if (ev.SelfParent != roots[ev.Creator()].SelfParent.Hash)
                        {
                            var (other, err7) = await CreateOtherParentRootEvent(ev);
                            if (err7 != null)
                            {
                                return (new Frame(), err7);
                            }

                            roots[ev.Creator()].Others[ev.Hex()] = other;
                        }
                    }
                }

                //order roots
            }

            //order roots
            var orderedRoots = new Root[Participants.Len()];
            {
                var i = 0;
                foreach (var peer in await Participants.ToPeerSlice())
                {
                    orderedRoots[i] = roots[peer.PubKeyHex];
                    i++;
                }
            }

            var res = new Frame {Round = roundReceived, Roots = orderedRoots, Events = events.ToArray()};

            var err8 = await Store.SetFrame(res);

            if (err8 != null)
            {
                return (new Frame(), err8);
            }

            return (res, null);
        }

        //ProcessSigPool runs through the SignaturePool and tries to map a Signature to
        //a known Block. If a Signature is found to be valid for a known Block, it is
        //appended to the block and removed from the SignaturePool
        public async Task<BabbleError> ProcessSigPool()
        {
            var processedSignatures = new Dictionary<int, bool>(); //index in SigPool => Processed?

            try
            {
                var i = 0;
                foreach (var bs in SigPool)

                {
                    //check if validator belongs to list of participants
                    var validatorHex = $"0x{bs.Validator.ToHex()}";
                    {
                        var ok = Participants.ByPubKey.ContainsKey(validatorHex);

                        if (!ok)
                        {
                            logger.Warning("Index={Index}; Validator={Validator} : Verifying Block signature. Unknown validator", bs.Index, validatorHex);

                            continue;
                        }
                    }
                    var (block, err1) = await Store.GetBlock(bs.Index);
                    if (err1 != null)
                    {
                        logger.Warning("Index={Index}; Msg={Msg} : Verifying Block signature. Could not fetch Block", bs.Index, err1.Message);

                        continue;
                    }

                    var (valid, err2) = block.Verify(bs);
                    if (err2 != null)
                    {
                        logger.Warning("Index={Index}; Msg={Msg} : Verifying Block signature", bs.Index, err2.Message);

                        return err2;
                    }

                    if (!valid)
                    {
                        logger.Warning("Index={Index}; Validator={Validator} : Verifying Block signature. Invalid signature", bs.Index, Participants.ByPubKey[validatorHex]);

                        continue;
                    }

                    block.SetSignature(bs);

                    {
                        var err3 = await Store.SetBlock(block);

                        if (err3 != null)
                        {
                            logger.Warning("Index={Index}; Msg={Msg} : Saving Block", bs.Index, err3.Message);
                        }
                    }
                    if (block.Signatures.Count > TrustCount && (AnchorBlock == null || block.Index() > AnchorBlock))
                    {
                        SetAnchorBlock(block.Index());

                        logger.Debug("BlockIndex={BlockIndex};Signatures={Signatures};TrustCount={TrustCount} : Setting AnchorBlock", block.Index(), block.Signatures.Count(), TrustCount);
                    }

                    processedSignatures[i] = true;

                    i++;
                }

                return null;
            }
            finally
            {
                removeProcessedSignatures(processedSignatures);
            }
        }

        //GetAnchorBlockWithFrame returns the AnchorBlock and the corresponding Frame.
        //This can be used as a base to Reset a Hashgraph
        public async Task<(Block, Frame, BabbleError)> GetAnchorBlockWithFrame()
        {
            if (AnchorBlock == null)
            {
                return (new Block(), new Frame(), new HashgraphError("No Anchor Block"));
            }

            var (block, err1) = await Store.GetBlock(AnchorBlock ?? 0);
            if (err1 != null)
            {
                return (new Block(), new Frame(), err1);
            }

            var (frame, err2) = await GetFrame(block.RoundReceived());
            if (err2 != null)
            {
                return (new Block(), new Frame(), err2);
            }

            return (block, frame, null);
        }

        //public async Task<BabbleError> FindOrder()
        //{
        //    await DecideRoundReceived();

        //    var newConsensusEvents = new List<Event>();

        //    var newUndeterminedEvents = new List<string>();
        //    Exception err;
        //    foreach (var x in UndeterminedEvents)
        //    {
        //        Event ex;
        //        (ex, err) = await Store.GetEvent(x);

        //        if (err != null)
        //        {
        //            return err;
        //        }

        //        if (ex.GetRoundReceived() != null)
        //        {
        //            newConsensusEvents.Add(ex);
        //        }
        //        else
        //        {
        //            newUndeterminedEvents.Add(x);
        //        }
        //    }

        //    UndeterminedEvents = newUndeterminedEvents;

        //    newConsensusEvents.Sort(new Event.EventByConsensus());

        //    err = await HandleNewConsensusEvents(newConsensusEvents);
        //    return err;
        //}

        //public async Task<BabbleError> HandleNewConsensusEvents(IEnumerable<Event> newConsensusEvents)
        //{
        //    var blockMap = new Dictionary<int, List<byte[]>>(); // [RoundReceived] => []Transactions
        //    var blockOrder = new List<int>(); // [index] => RoundReceived

        //    foreach (var e in newConsensusEvents)
        //    {
        //        Store.AddConsensusEvent(e.Hex());

        //        ConsensusTransactions += e.Transactions().Length;

        //        if (e.IsLoaded())
        //        {
        //            PendingLoadedEvents--;
        //        }

        //        var rr = e.GetRoundReceived() ?? -1;
        //        var ok = blockMap.TryGetValue(rr, out var btxs);
        //        if (!ok)
        //        {
        //            btxs = new List<byte[]>();
        //            blockOrder.Add(rr);
        //        }

        //        btxs.AddRange(e.Transactions());
        //        blockMap[rr] = btxs;
        //    }

        //    foreach (var rr in blockOrder)
        //    {
        //        var blockTxs = blockMap[rr];
        //        if (blockTxs.Count > 0)
        //        {
        //            var (block, err) = await CreateAndInsertBlock(rr, blockTxs.ToArray());
        //            if (err != null)
        //            {
        //                return err;
        //            }

        //            logger.Debug("Block created! Index={Index}", block.Index());

        //            if (CommitCh != null)
        //            {
        //                await CommitCh.EnqueueAsync(block);
        //            }
        //        }
        //    }

        //    return null;
        //}

        //public async Task<(Block, BabbleError)> CreateAndInsertBlock(int roundReceived, byte[][] txs)
        //{
        //    var block = new Block(LastBlockIndex + 1, roundReceived, txs);

        //    Exception err = await Store.SetBlock(block);

        //    if (err != null)
        //    {
        //        return (new Block(), new HashgraphError(err.Message, err));
        //    }

        //    LastBlockIndex++;
        //    return (block, null);
        //}

        //public async Task<DateTimeOffset> MedianTimestamp(List<string> evHashes)
        //{
        //    var evs = new List<Event>();
        //    foreach (var x in evHashes)
        //    {
        //        var (ex, _) = await Store.GetEvent(x);
        //        evs.Add(ex);
        //    }

        //    evs.Sort(new Event.EventByTimeStamp());
        //    return evs[evs.Count / 2].Body.Timestamp;
        //}

        //public string[] ConsensusEvents()
        //{
        //    return Store.ConsensusEvents();
        //}

        ////number of evs per participants
        //public Task<Dictionary<int, int>> KnownEvents()
        //{
        //    return Store.KnownEvents();
        //}

        //Reset clears the Hashgraph and resets it from a new base.
        public async Task<BabbleError> Reset(Block block, Frame frame)
        {
            //Clear all state

            LastConsensusRound = null;
            FirstConsensusRound = null;
            AnchorBlock = null;

            UndeterminedEvents = new List<string>();
            PendingRounds = new Queue<PendingRound>();
            PendingLoadedEvents = 0;
            TopologicalIndex = 0;

            var cacheSize = Store.CacheSize();
            AncestorCache = new LruCache<string, bool>(cacheSize, null, logger, "AncestorCache");
            SelfAncestorCache = new LruCache<string, bool>(cacheSize, null, logger, "SelfAncestorCache");
            StronglySeeCache = new LruCache<string, bool>(cacheSize, null, logger, "StronglySeeCache");
            RoundCache = new LruCache<string, int>(cacheSize, null, logger, "RoundCache");

            var participants = await Participants.ToPeerSlice();

            var rootMap = new Dictionary<string, Root>();
            var id = 0;
            foreach (var root in frame.Roots)
            {
                var p = participants[id];
                rootMap[p.PubKeyHex] = root;
                id++;
            }

            var err1 = Store.Reset(rootMap);
            if (err1 != null)
            {
                return err1;
            }

            var err2 = await Store.SetBlock(block);
            //Insert Block
            if (err2 != null)
            {
                return err2;
            }

            SetLastConsensusRound(block.RoundReceived());

            //Insert Frame Events
            foreach (var ev in frame.Events)
            {
                var err3 = await InsertEvent(ev, false);

                if (err3 != null)
                {
                    return err3;
                }
            }

            return null;
        }

        //public async Task<(Frame frame, Exception err)> GetFrame()
        //{
        //    Exception err;

        //    var lastConsensusRoundIndex = 0;
        //    var lcr = LastConsensusRound;
        //    if (lcr != null)
        //    {
        //        lastConsensusRoundIndex = (int) lcr;
        //    }

        //    RoundInfo lastConsensusRound;
        //    (lastConsensusRound, err) = await Store.GetRound(lastConsensusRoundIndex);
        //    if (err != null)
        //    {
        //        return (new Frame(), err);
        //    }

        //    var witnessHashes = lastConsensusRound.Witnesses();
        //    var evs = new List<Event>();
        //    var roots = new Dictionary<string, Root>();
        //    foreach (var wh in witnessHashes)
        //    {
        //        Event w;
        //        (w, err) = await Store.GetEvent(wh);
        //        if (err != null)
        //        {
        //            return (new Frame(), err);
        //        }

        //        evs.Add(w);
        //        roots.Add(w.Creator(), new Root
        //        {
        //            X = w.SelfParent,
        //            Y = w.OtherParent,
        //            Index = w.Index() - 1,
        //            Round = await Round(w.SelfParent),
        //            Others = new Dictionary<string, string>()
        //        });
        //        string[] participantEvents;
        //        (participantEvents, err) = await Store.ParticipantEvents(w.Creator(), w.Index());
        //        if (err != null)
        //        {
        //            return (new Frame(), err);
        //        }

        //        foreach (var e in participantEvents)
        //        {
        //            var (ev, errev) = await Store.GetEvent(e);
        //            if (errev != null)
        //            {
        //                return (new Frame(), errev);
        //            }

        //            evs.Add(ev);
        //        }
        //    }

        //    //Not every participant necessarily has a witness in LastConsensusRound.
        //    //Hence, there could be participants with no Root at this point.
        //    //For these partcipants, use their last known Event.
        //    foreach (var p in Participants)
        //    {
        //        if (!roots.ContainsKey(p.Key))
        //        {
        //            var (last, isRoot, errp) = Store.LastEventFrom(p.Key);
        //            if (errp != null)
        //            {
        //                return (new Frame(), errp);
        //            }

        //            Root root;
        //            if (isRoot)
        //            {
        //                (root, err) = await Store.GetRoot(p.Key);
        //                if (root == null)
        //                {
        //                    return (new Frame(), err);
        //                }
        //            }
        //            else
        //            {
        //                Event ev;
        //                (ev, err) = await Store.GetEvent(last);
        //                if (err != null)
        //                {
        //                    return (new Frame(), err);
        //                }

        //                evs.Add(ev);
        //                root = new Root
        //                {
        //                    X = ev.SelfParent,
        //                    Y = ev.OtherParent,
        //                    Index = ev.Index() - 1,
        //                    Round = await Round(ev.SelfParent)
        //                };
        //            }

        //            roots.Add(p.Key, root);
        //        }
        //    }

        //    evs.Sort(new Event.EventByTopologicalOrder());

        //    //Some Events in the Frame might have other-parents that are outside of the
        //    //Frame (cf root.go ex 2)
        //    //When inserting these Events in a newly reset hashgraph, the CheckOtherParent
        //    //method would return an error because the other-parent would not be found.
        //    //So we make it possible to also look for other-parents in the creator's Root.
        //    var treated = new Dictionary<string, bool>();
        //    foreach (var ev in evs)
        //    {
        //        treated.Add(ev.Hex(), true);
        //        var otherParent = ev.OtherParent;
        //        if (!string.IsNullOrEmpty(otherParent))
        //        {
        //            var ok = treated.TryGetValue(otherParent, out var opt);
        //            if (!opt || !ok)
        //            {
        //                if (ev.SelfParent != roots[ev.Creator()].X)
        //                {
        //                    roots[ev.Creator()].Others[ev.Hex()] = otherParent;
        //                }
        //            }
        //        }
        //    }

        //    var frame = new Frame
        //    {
        //        Roots = roots,
        //        Events = evs.ToArray()
        //    };
        //    return (frame, null);
        //}

        //Bootstrap loads all Events from the Store's DB (if there is one) and feeds
        //them to the Hashgraph (in topological order) for consensus ordering. After this
        //method call, the Hashgraph should be in a state coeherent with the 'tip' of the
        //Hashgraph
        public async Task<BabbleError> Bootstrap()
        {
            if (Store is LocalDbStore)

            {
                logger.Debug("Bootstrap");

                using (var tx = Store.BeginTx())
                {
                    //Retreive the Events from the underlying DB. They come out in topological
                    //order
                    var (topologicalEvents, err1) = await ((LocalDbStore) Store).DbTopologicalEvents();

                    if (err1 != null)
                    {
                        return err1;
                    }

                    logger.Debug("Topological Event Count {count}", topologicalEvents.Length);

                    //Insert the Events in the Hashgraph
                    foreach (var e in topologicalEvents)
                    {
                        var err2 = await InsertEvent(e, true);

                        if (err2 != null)
                        {
                            return err2;
                        }
                    }

                    //Compute the consensus order of Events
                    var err3 = await DivideRounds();
                    if (err3 != null)
                    {
                        return err3;
                    }

                    var err4 = await DecideFame();
                    if (err4 != null)
                    {
                        return err4;
                    }

                    var err5 = await DecideRoundReceived();
                    if (err5 != null)
                    {
                        return err5;
                    }

                    var err6 = await ProcessDecidedRounds();
                    if (err6 != null)
                    {
                        return err6;
                    }

                    var err7 = await ProcessSigPool();
                    if (err7 != null)
                    {
                        return err7;
                    }

                    tx.Commit();
                }
            }

            return null;
        }

        public async Task<(Event ev, BabbleError err)> ReadWireInfo(WireEvent wev)
        {
            var selfParent = Event.RootSelfParent(wev.Body.CreatorId);
            var otherParent = "";
            
            BabbleError err;

            var creator = Participants.ById[wev.Body.CreatorId];
            var creatorBytes = creator.PubKeyHex.FromHex();

            if (wev.Body.SelfParentIndex >= 0)
            {
                (selfParent, err) = await Store.ParticipantEvent(creator.PubKeyHex, wev.Body.SelfParentIndex);
                if (err != null)
                {
                    return (null, err);
                }
            }

            if (wev.Body.OtherParentIndex >= 0)
            {
                var otherParentCreator = Participants.ById[wev.Body.OtherParentCreatorId];
                (otherParent, err) = await Store.ParticipantEvent(otherParentCreator.PubKeyHex, wev.Body.OtherParentIndex);
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

        //CheckBlock returns an error if the Block does not contain valid signatures
        //from MORE than 1/3 of participants
        public BabbleError CheckBlock(Block block)
        {
            var validSignatures = 0;
            foreach (var s in block.GetSignatures())
            {
                var (ok, _) = block.Verify(s);
                if (ok)
                {
                    validSignatures++;
                }
            }

            if (validSignatures <= TrustCount)
            {
                return new HashgraphError(string.Format("Not enough valid signatures: got {0}, need {1}", validSignatures, TrustCount + 1));
            }

            logger.Debug("CheckBlock : ValidSignatures = {ValidSignatures}", validSignatures);
            return null;
        }

        /*******************************************************************************
        Setters
        *******************************************************************************/

        private void SetLastConsensusRound(int i)
        {
            if (LastConsensusRound == null)
            {
                LastConsensusRound = default(int);
            }

            LastConsensusRound = i;

            if (FirstConsensusRound == null)
            {
                FirstConsensusRound = default(int);
                FirstConsensusRound = i;
            }
        }

        private void SetAnchorBlock(int i)
        {
            if (AnchorBlock == null)
            {
                AnchorBlock = default(int);
            }

            AnchorBlock = i;
        }

        /*******************************************************************************
           Helpers
        *******************************************************************************/

        public bool MiddleBit(string ehex)
        {
            var hash = ehex.Substring(2).StringToBytes();
            if (hash.Length > 0 && hash[hash.Length / 2] == 0)
            {
                return false;
            }

            return true;
        }
    }
}