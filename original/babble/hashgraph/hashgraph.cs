// package hashgraph -- go2cs converted at 2018 December 06 17:41:59 UTC
// import "hashgraph" ==> using hashgraph = go.hashgraph_package
// Original source: c:\source\diffused\babble\src\hashgraph\hashgraph.go
using hex = go.encoding.hex_package;
using errors = go.errors_package;
using fmt = go.fmt_package;
using math = go.math_package;
using sort = go.sort_package;
using strconv = go.strconv_package;

using logrus = go.github.com.sirupsen.logrus_package;

using common = go.github.com.mosaicnetworks.babble.src.common_package;
using peers = go.github.com.mosaicnetworks.babble.src.peers_package;
using static go.builtin;
using System.Collections.Generic;

namespace go
{
    public static partial class hashgraph_package
    {
        //Hashgraph is a DAG of Events. It also contains methods to extract a consensus
        //order of Events and map them onto a blockchain.
        public partial struct Hashgraph
        {
            public Ptr<peers.Peers> Participants; //[public key] => id
            public Store Store; //store of Events, Rounds, and Blocks
            public slice<@string> UndeterminedEvents; //[index] => hash . FIFO queue of Events whose consensus order is not yet determined
            public slice<ref pendingRound> PendingRounds; //FIFO queue of Rounds which have not attained consensus yet
            public Ptr<@int> LastConsensusRound; //index of last consensus round
            public Ptr<@int> FirstConsensusRound; //index of first consensus round (only used in tests)
            public Ptr<@int> AnchorBlock; //index of last block with enough signatures
            public @int LastCommitedRoundEvents; //number of events in round before LastConsensusRound
            public slice<BlockSignature> SigPool; //Pool of Block signatures that need to be processed
            public @int ConsensusTransactions; //number of consensus transactions
            public @int PendingLoadedEvents; //number of loaded events that are not yet committed
            public channel<Block> commitCh; //channel for committing Blocks
            public @int topologicalIndex; //counter used to order events in topological order (only local)
            public @int superMajority;
            public @int trustCount;
            public Ptr<common.LRU> ancestorCache;
            public Ptr<common.LRU> selfAncestorCache;
            public Ptr<common.LRU> stronglySeeCache;
            public Ptr<common.LRU> roundCache;
            public Ptr<common.LRU> timestampCache;
            public Ptr<logrus.Entry> logger;
        }

        //NewHashgraph instantiates a Hashgraph from a list of participants, underlying
        //data store and commit channel
        public static ref Hashgraph NewHashgraph(ref peers.Peers participants, Store store, channel<Block> commitCh, ref logrus.Entry logger)
        {
            if (logger == nil)
            {
                var log = logrus.New();
                log.Level = logrus.DebugLevel;
                logger = logrus.NewEntry(log);
            }
            var superMajority = 2 * participants.Len() / 3 + 1;
            var trustCount = @int(math.Ceil(float64(participants.Len()) / float64(3)));

            var cacheSize = store.CacheSize();
            var hashgraph = Hashgraph{Participants:participants,Store:store,commitCh:commitCh,ancestorCache:common.NewLRU(cacheSize,nil),selfAncestorCache:common.NewLRU(cacheSize,nil),stronglySeeCache:common.NewLRU(cacheSize,nil),roundCache:common.NewLRU(cacheSize,nil),timestampCache:common.NewLRU(cacheSize,nil),logger:logger,superMajority:superMajority,trustCount:trustCount,};

            return ref hashgraph;
        }

        /*******************************************************************************
        Private Methods
        *******************************************************************************/

        //true if y is an ancestor of x
        private static (@bool, error) ancestor(this ref Hashgraph h, @string x, @string y)
        {
            {
                var (c, ok) = h.ancestorCache.Get(Key{x,y});

                if (ok)
                {
                    return (c.TypeAssert<@bool>(), nil);
                }

            }
            var (a, err) = h._ancestor(x, y);
            if (err != nil)
            {
                return (@false, err);
            }
            h.ancestorCache.Add(Key{x,y}, a);
            return (a, nil);
        }

        private static (@bool, error) _ancestor(this ref Hashgraph h, @string x, @string y)
        {
            if (x == y)
            {
                return (@true, nil);
            }
            var (ex, err) = h.Store.GetEvent(x);
            if (err != nil)
            {
                return (@false, err);
            }
            var (ey, err) = h.Store.GetEvent(y);
            if (err != nil)
            {
                return (@false, err);
            }
            var eyCreator = h.Participants.ByPubKey[ey.Creator()].ID;
            var (entry, ok) = ex.lastAncestors.GetByID(eyCreator);

            if (!ok)
            {
                return (@false, errors.New("Unknown event id " + strconv.Itoa(eyCreator)));
            }
            var lastAncestorKnownFromYCreator = entry.@event.index;

            return (lastAncestorKnownFromYCreator >= ey.Index(), nil);
        }

        //true if y is a self-ancestor of x
        private static (@bool, error) selfAncestor(this ref Hashgraph h, @string x, @string y)
        {
            {
                var (c, ok) = h.selfAncestorCache.Get(Key{x,y});

                if (ok)
                {
                    return (c.TypeAssert<@bool>(), nil);
                }

            }
            var (a, err) = h._selfAncestor(x, y);
            if (err != nil)
            {
                return (@false, err);
            }
            h.selfAncestorCache.Add(Key{x,y}, a);
            return (a, nil);
        }

        private static (@bool, error) _selfAncestor(this ref Hashgraph h, @string x, @string y)
        {
            if (x == y)
            {
                return (@true, nil);
            }
            var (ex, err) = h.Store.GetEvent(x);
            if (err != nil)
            {
                return (@false, err);
            }
            var exCreator = h.Participants.ByPubKey[ex.Creator()].ID;

            var (ey, err) = h.Store.GetEvent(y);
            if (err != nil)
            {
                return (@false, err);
            }
            var eyCreator = h.Participants.ByPubKey[ey.Creator()].ID;

            return (exCreator == eyCreator && ex.Index() >= ey.Index(), nil);
        }

        //true if x sees y
        private static (@bool, error) see(this ref Hashgraph h, @string x, @string y)
        {
            return h.ancestor(x, y);
            //it is not necessary to detect forks because we assume that the InsertEvent
            //function makes it impossible to insert two Events at the same height for
            //the same participant.
        }

        //true if x strongly sees y
        private static (@bool, error) stronglySee(this ref Hashgraph h, @string x, @string y)
        {
            {
                var (c, ok) = h.stronglySeeCache.Get(Key{x,y});

                if (ok)
                {
                    return (c.TypeAssert<@bool>(), nil);
                }

            }
            var (ss, err) = h._stronglySee(x, y);
            if (err != nil)
            {
                return (@false, err);
            }
            h.stronglySeeCache.Add(Key{x,y}, ss);
            return (ss, nil);
        }

        private static (@bool, error) _stronglySee(this ref Hashgraph h, @string x, @string y)
        {
            var (ex, err) = h.Store.GetEvent(x);
            if (err != nil)
            {
                return (@false, err);
            }
            var (ey, err) = h.Store.GetEvent(y);
            if (err != nil)
            {
                return (@false, err);
            }
            var c = 0;
            {
                {
                    if (entry.@event.index >= ey.firstDescendants[i].@event.index)
                    {
                        c++;
                    }
                }

            }
            return (c >= h.superMajority, nil);
        }

        private static (@int, error) round(this ref Hashgraph h, @string x)
        {
            {
                var (c, ok) = h.roundCache.Get(x);

                if (ok)
                {
                    return (c.TypeAssert<@int>(), nil);
                }

            }
            var (r, err) = h._round(x);
            if (err != nil)
            {
                return (-1, err);
            }
            h.roundCache.Add(x, r);
            return (r, nil);
        }

        private static (@int, error) _round(this ref Hashgraph h, @string x)
        {
            /*
                x is the Root
                Use Root.SelfParent.Round
            */
            var (rootsBySelfParent, _) = h.Store.RootsBySelfParent();
            {
                var (r, ok) = rootsBySelfParent[x];

                if (ok)
                {
                    return (r.SelfParent.Round, nil);
                }

            }
            var (ex, err) = h.Store.GetEvent(x);
            if (err != nil)
            {
                return (math.MinInt32, err);
            }
            var (root, err) = h.Store.GetRoot(ex.Creator());
            if (err != nil)
            {
                return (math.MinInt32, err);
            }

            /*
                The Event is directly attached to the Root.
            */
            if (ex.SelfParent() == root.SelfParent.Hash)
            {
                //Root is authoritative EXCEPT if other-parent is not in the root
                {
                    var (other, ok) = root.Others[ex.Hex()];

                    if ((ex.OtherParent() == "") || (ok && other.Hash == ex.OtherParent()))
                    {
                        return (root.NextRound, nil);
                    }

                }
            }

            /*
                The Event's parents are "normal" Events.
                Use the whitepaper formula: parentRound + roundInc
            */
            var (parentRound, err) = h.round(ex.SelfParent());
            if (err != nil)
            {
                return (math.MinInt32, err);
            }
            if (ex.OtherParent() != "")
            {
                @int opRound;
                //XXX

                //XXX
                {
                    var (other, ok) = root.Others[ex.Hex()];

                    if (ok && other.Hash == ex.OtherParent())
                    {
                        opRound = root.NextRound;
                    }
                    else
                    {
                        (opRound, err) = h.round(ex.OtherParent());
                        if (err != nil)
                        {
                            return (math.MinInt32, err);
                        }
                    }

                }
                if (opRound > parentRound)
                {
                    parentRound = opRound;
                }
            }
            var c = 0;
            {
                {
                    var (ss, err) = h.stronglySee(x, w);
                    if (err != nil)
                    {
                        return (math.MinInt32, err);
                    }
                    if (ss)
                    {
                        c++;
                    }
                }

            }
            if (c >= h.superMajority)
            {
                parentRound++;
            }
            return (parentRound, nil);
        }

        //true if x is a witness (first event of a round for the owner)
        private static (@bool, error) witness(this ref Hashgraph h, @string x)
        {
            var (ex, err) = h.Store.GetEvent(x);
            if (err != nil)
            {
                return (@false, err);
            }
            var (xRound, err) = h.round(x);
            if (err != nil)
            {
                return (@false, err);
            }
            var (spRound, err) = h.round(ex.SelfParent());
            if (err != nil)
            {
                return (@false, err);
            }
            return (xRound > spRound, nil);
        }

        private static (@int, error) roundReceived(this ref Hashgraph h, @string x)
        {
            var (ex, err) = h.Store.GetEvent(x);
            if (err != nil)
            {
                return (-1, err);
            }
            var res = -1;
            if (ex.roundReceived != nil)
            {
                res = ex.roundReceived.Deref;
            }
            return (res, nil);
        }

        private static (@int, error) lamportTimestamp(this ref Hashgraph h, @string x)
        {
            {
                var (c, ok) = h.timestampCache.Get(x);

                if (ok)
                {
                    return (c.TypeAssert<@int>(), nil);
                }

            }
            var (r, err) = h._lamportTimestamp(x);
            if (err != nil)
            {
                return (-1, err);
            }
            h.timestampCache.Add(x, r);
            return (r, nil);
        }

        private static (@int, error) _lamportTimestamp(this ref Hashgraph h, @string x)
        {
            /*
                x is the Root
                User Root.SelfParent.LamportTimestamp
            */
            var (rootsBySelfParent, _) = h.Store.RootsBySelfParent();
            {
                var (r, ok) = rootsBySelfParent[x];

                if (ok)
                {
                    return (r.SelfParent.LamportTimestamp, nil);
                }

            }
            var (ex, err) = h.Store.GetEvent(x);
            if (err != nil)
            {
                return (math.MinInt32, err);
            }

            //We are going to need the Root later
            var (root, err) = h.Store.GetRoot(ex.Creator());
            if (err != nil)
            {
                return (math.MinInt32, err);
            }
            var plt = math.MinInt32;
            //If it is the creator's first Event, use the corresponding Root
            if (ex.SelfParent() == root.SelfParent.Hash)
            {
                plt = root.SelfParent.LamportTimestamp;
            }
            else
            {
                var (t, err) = h.lamportTimestamp(ex.SelfParent());
                if (err != nil)
                {
                    return (math.MinInt32, err);
                }
                plt = t;
            }
            if (ex.OtherParent() != "")
            {
                var opLT = math.MinInt32;
                {
                    var (_, err) = h.Store.GetEvent(ex.OtherParent());

                    if (err == nil)
                    {
                        //if we know the other-parent, fetch its Round directly
                        var (t, err) = h.lamportTimestamp(ex.OtherParent());
                        if (err != nil)
                        {
                            return (math.MinInt32, err);
                        }
                        opLT = t;
                    }                    {
                        var (other, ok) = root.Others[x];


                        else if (ok && other.Hash == ex.OtherParent())
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
            return (plt + 1, nil);
        }

        //round(x) - round(y)
        private static (@int, error) roundDiff(this ref Hashgraph h, @string x, @string y)
        {
            var (xRound, err) = h.round(x);
            if (err != nil)
            {
                return (math.MinInt32, fmt.Errorf("event %s has negative round", x));
            }
            var (yRound, err) = h.round(y);
            if (err != nil)
            {
                return (math.MinInt32, fmt.Errorf("event %s has negative round", y));
            }
            return (xRound - yRound, nil);
        }

        //Check the SelfParent is the Creator's last known Event
        private static error checkSelfParent(this ref Hashgraph h, Event @event)
        {
            var selfParent = @event.SelfParent();
            var creator = @event.Creator();

            var (creatorLastKnown, _, err) = h.Store.LastEventFrom(creator);
            if (err != nil)
            {
                return err;
            }
            var selfParentLegit = selfParent == creatorLastKnown;

            if (!selfParentLegit)
            {
                return fmt.Errorf("Self-parent not last known event by creator");
            }
            return nil;
        }

        //Check if we know the OtherParent
        private static error checkOtherParent(this ref Hashgraph h, Event @event)
        {
            var otherParent = @event.OtherParent();
            if (otherParent != "")
            {
                //Check if we have it
                var (_, err) = h.Store.GetEvent(otherParent);
                if (err != nil)
                {
                    //it might still be in the Root
                    var (root, err) = h.Store.GetRoot(@event.Creator());
                    if (err != nil)
                    {
                        return err;
                    }
                    var (other, ok) = root.Others[@event.Hex()];
                    if (ok && other.Hash == @event.OtherParent())
                    {
                        return nil;
                    }
                    return fmt.Errorf("Other-parent not known");
                }
            }
            return nil;
        }

        //initialize arrays of last ancestors and first descendants
        private static error initEventCoordinates(this ref Hashgraph h, ref Event @event)
        {
            var members = h.Participants.Len();

            @event.firstDescendants = make(OrderedEventCoordinates, members);
            {
                {
                    @event.firstDescendants[i] = Index{participantId:id,event:EventCoordinates{index:math.MaxInt32,},};
                }

            }

            @event.lastAncestors = make(OrderedEventCoordinates, members);

            var (selfParent, selfParentError) = h.Store.GetEvent(@event.SelfParent());
            var (otherParent, otherParentError) = h.Store.GetEvent(@event.OtherParent());

            if (selfParentError != nil && otherParentError != nil)
            {
                {
                    {
                        @event.lastAncestors[i] = Index{participantId:entry.participantId,event:EventCoordinates{index:-1,},};
                    }

                }
            }
            else if (selfParentError != nil)
            {
                copy(@event.lastAncestors.slice(high:members), otherParent.lastAncestors);
            }
            else if (otherParentError != nil)
            {
                copy(@event.lastAncestors.slice(high:members), selfParent.lastAncestors);
            }
            else
            {
                var selfParentLastAncestors = selfParent.lastAncestors;
                var otherParentLastAncestors = otherParent.lastAncestors;

                copy(@event.lastAncestors.slice(high:members), selfParentLastAncestors);
                {
                    {
                        if (@event.lastAncestors[i].@event.index < otherParentLastAncestors[i].@event.index)
                        {
                            @event.lastAncestors[i].@event.index = otherParentLastAncestors[i].@event.index;
                            @event.lastAncestors[i].@event.hash = otherParentLastAncestors[i].@event.hash;
                        }
                    }

                }
            }
            var index = @event.Index();

            var creator = @event.Creator();
            var (creatorPeer, ok) = h.Participants.ByPubKey[creator];
            if (!ok)
            {
                return fmt.Errorf("Could not find creator id (%s)", creator);
            }
            var hash = @event.Hex();

            var i = @event.firstDescendants.GetIDIndex(creatorPeer.ID);
            var j = @event.lastAncestors.GetIDIndex(creatorPeer.ID);

            if (i == -1)
            {
                return fmt.Errorf("Could not find first descendant from creator id (%d)", creatorPeer.ID);
            }
            if (j == -1)
            {
                return fmt.Errorf("Could not find last ancestor from creator id (%d)", creatorPeer.ID);
            }
            @event.firstDescendants[i].@event = EventCoordinates{index:index,hash:hash};
            @event.lastAncestors[j].@event = EventCoordinates{index:index,hash:hash};

            return nil;
        }

        //update first decendant of each last ancestor to point to event
        private static error updateAncestorFirstDescendant(this ref Hashgraph h, Event @event)
        {
            var (creatorPeer, ok) = h.Participants.ByPubKey[@event.Creator()];
            if (!ok)
            {
                return fmt.Errorf("Could not find creator id (%s)", @event.Creator());
            }
            var index = @event.Index();
            var hash = @event.Hex();

            {
                {
                    var ah = @event.lastAncestors[i].@event.hash;
                    while (ah != "")
                    {
                        var (a, err) = h.Store.GetEvent(ah);
                        if (err != nil)
                        {
                            break;
                        }
                        var idx = a.firstDescendants.GetIDIndex(creatorPeer.ID);

                        if (idx == -1)
                        {
                            return fmt.Errorf("Could not find first descendant by creator id (%s)", @event.Creator());
                        }
                        if (a.firstDescendants[idx].@event.index == math.MaxInt32)
                        {
                            a.firstDescendants[idx].@event = EventCoordinates{index:index,hash:hash};
                            {
                                var err = h.Store.SetEvent(a);

                                if (err != nil)
                                {
                                    return err;
                                }

                            }
                            ah = a.SelfParent();
                        }
                        else
                        {
                            break;
                        }
                    }

                }

            }

            return nil;
        }

        private static (RootEvent, error) createSelfParentRootEvent(this ref Hashgraph h, Event ev)
        {
            var sp = ev.SelfParent();
            var (spLT, err) = h.lamportTimestamp(sp);
            if (err != nil)
            {
                return (RootEvent{}, err);
            }
            var (spRound, err) = h.round(sp);
            if (err != nil)
            {
                return (RootEvent{}, err);
            }
            var selfParentRootEvent = RootEvent{Hash:sp,CreatorID:h.Participants.ByPubKey[ev.Creator()].ID,Index:ev.Index()-1,LamportTimestamp:spLT,Round:spRound,};
            return (selfParentRootEvent, nil);
        }

        private static (RootEvent, error) createOtherParentRootEvent(this ref Hashgraph h, Event ev)
        {
            var op = ev.OtherParent();

            //it might still be in the Root
            var (root, err) = h.Store.GetRoot(ev.Creator());
            if (err != nil)
            {
                return (RootEvent{}, err);
            }
            {
                var (other, ok) = root.Others[ev.Hex()];

                if (ok && other.Hash == op)
                {
                    return (other, nil);
                }

            }
            var (otherParent, err) = h.Store.GetEvent(op);
            if (err != nil)
            {
                return (RootEvent{}, err);
            }
            var (opLT, err) = h.lamportTimestamp(op);
            if (err != nil)
            {
                return (RootEvent{}, err);
            }
            var (opRound, err) = h.round(op);
            if (err != nil)
            {
                return (RootEvent{}, err);
            }
            var otherParentRootEvent = RootEvent{Hash:op,CreatorID:h.Participants.ByPubKey[otherParent.Creator()].ID,Index:otherParent.Index(),LamportTimestamp:opLT,Round:opRound,};
            return (otherParentRootEvent, nil);

        }

        private static (Root, error) createRoot(this ref Hashgraph h, Event ev)
        {
            var (evRound, err) = h.round(ev.Hex());
            if (err != nil)
            {
                return (Root{}, err);
            }

            /*
                SelfParent
            */
            var (selfParentRootEvent, err) = h.createSelfParentRootEvent(ev);
            if (err != nil)
            {
                return (Root{}, err);
            }

            /*
                OtherParent
            */
            ref RootEvent otherParentRootEvent;

            if (ev.OtherParent() != "")
            {
                var (opre, err) = h.createOtherParentRootEvent(ev);
                if (err != nil)
                {
                    return (Root{}, err);
                }
                otherParentRootEvent = ref opre;
            }
            var root = Root{NextRound:evRound,SelfParent:selfParentRootEvent,Others:map[string]RootEvent{},};

            if (otherParentRootEvent != nil)
            {
                root.Others[ev.Hex()] = otherParentRootEvent.Deref;
            }
            return (root, nil);
        }

        private static error setWireInfo(this ref Hashgraph h, ref Event @event)
        {
            var selfParentIndex = -1;
            var otherParentCreatorID = -1;
            var otherParentIndex = -1;

            //could be the first Event inserted for this creator. In this case, use Root
            {
                var (lf, isRoot, _) = h.Store.LastEventFrom(@event.Creator());

                if (isRoot && lf == @event.SelfParent())
                {
                    var (root, err) = h.Store.GetRoot(@event.Creator());
                    if (err != nil)
                    {
                        return err;
                    }
                    selfParentIndex = root.SelfParent.Index;
                }
                else
                {
                    var (selfParent, err) = h.Store.GetEvent(@event.SelfParent());
                    if (err != nil)
                    {
                        return err;
                    }
                    selfParentIndex = selfParent.Index();
                }

            }
            if (@event.OtherParent() != "")
            {
                //Check Root then regular Events
                var (root, err) = h.Store.GetRoot(@event.Creator());
                if (err != nil)
                {
                    return err;
                }
                {
                    var (other, ok) = root.Others[@event.Hex()];

                    if (ok && other.Hash == @event.OtherParent())
                    {
                        otherParentCreatorID = other.CreatorID;
                        otherParentIndex = other.Index;
                    }
                    else
                    {
                        var (otherParent, err) = h.Store.GetEvent(@event.OtherParent());
                        if (err != nil)
                        {
                            return err;
                        }
                        otherParentCreatorID = h.Participants.ByPubKey[otherParent.Creator()].ID;
                        otherParentIndex = otherParent.Index();
                    }

                }
            }
            @event.SetWireInfo(selfParentIndex, otherParentCreatorID, otherParentIndex, h.Participants.ByPubKey[@event.Creator()].ID);

            return nil;
        }

        private static void updatePendingRounds(this ref Hashgraph h, Dictionary<@int, @int> decidedRounds)
        {
            {
                {
                    {
                        var (_, ok) = decidedRounds[ur.Index];

                        if (ok)
                        {
                            ur.Decided = @true;
                        }

                    }
                }

            }
        }

        //Remove processed Signatures from SigPool
        private static void removeProcessedSignatures(this ref Hashgraph h, Dictionary<@int, @bool> processedSignatures)
        {
            var newSigPool = []BlockSignature{};
            {
                {
                    {
                        var (_, ok) = processedSignatures[bs.Index];

                        if (!ok)
                        {
                            newSigPool = append(newSigPool, bs);
                        }

                    }
                }

            }
            h.SigPool = newSigPool;
        }

        /*******************************************************************************
        Public Methods
        *******************************************************************************/

        //InsertEvent attempts to insert an Event in the DAG. It verifies the signature,
        //checks the ancestors are known, and prevents the introduction of forks.
        private static error InsertEvent(this ref Hashgraph h, Event @event, @bool setWireInfo)
        {
            //verify signature
            {
                var (ok, err) = @event.Verify();

                if (!ok)
                {
                    if (err != nil)
                    {
                        return err;
                    }
                    return fmt.Errorf("Invalid Event signature");
                }

            }
            {
                var err = h.checkSelfParent(@event);

                if (err != nil)
                {
                    return fmt.Errorf("CheckSelfParent: %s", err);
                }

            }
            {
                var err = h.checkOtherParent(@event);

                if (err != nil)
                {
                    return fmt.Errorf("CheckOtherParent: %s", err);
                }

            }
            @event.topologicalIndex = h.topologicalIndex;
            h.topologicalIndex++;

            if (setWireInfo)
            {
                {
                    var err = h.setWireInfo(ref @event);

                    if (err != nil)
                    {
                        return fmt.Errorf("SetWireInfo: %s", err);
                    }

                }
            }
            {
                var err = h.initEventCoordinates(ref @event);

                if (err != nil)
                {
                    return fmt.Errorf("InitEventCoordinates: %s", err);
                }

            }
            {
                var err = h.Store.SetEvent(@event);

                if (err != nil)
                {
                    return fmt.Errorf("SetEvent: %s", err);
                }

            }
            {
                var err = h.updateAncestorFirstDescendant(@event);

                if (err != nil)
                {
                    return fmt.Errorf("UpdateAncestorFirstDescendant: %s", err);
                }

            }
            h.UndeterminedEvents = append(h.UndeterminedEvents, @event.Hex());

            if (@event.IsLoaded())
            {
                h.PendingLoadedEvents++;
            }
            h.SigPool = append(h.SigPool, @event.BlockSignatures());

            return nil;
        }

        /*
        DivideRounds assigns a Round and LamportTimestamp to Events, and flags them as
        witnesses if necessary. Pushes Rounds in the PendingRounds queue if necessary.
        */
        private static error DivideRounds(this ref Hashgraph h)
        {
            {
                {
                    var (ev, err) = h.Store.GetEvent(hash);
                    if (err != nil)
                    {
                        return err;
                    }
                    var updateEvent = @false;

                    /*
                       Compute Event's round, update the corresponding Round object, and
                       add it to the PendingRounds queue if necessary.
                    */
                    if (ev.round == nil)
                    {
                        var (roundNumber, err) = h.round(hash);
                        if (err != nil)
                        {
                            return err;
                        }
                        ev.SetRound(roundNumber);
                        updateEvent = @true;

                        var (roundInfo, err) = h.Store.GetRound(roundNumber);
                        if (err != nil && !common.Is(err, common.KeyNotFound))
                        {
                            return err;
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
                        if (!roundInfo.queued && (h.LastConsensusRound == nil || roundNumber >= h.LastConsensusRound.Deref))
                        {
                            h.PendingRounds = append(h.PendingRounds, ref pendingRound{roundNumber,false});
                            roundInfo.queued = @true;
                        }
                        var (witness, err) = h.witness(hash);
                        if (err != nil)
                        {
                            return err;
                        }
                        roundInfo.AddEvent(hash, witness);

                        err = h.Store.SetRound(roundNumber, roundInfo);
                        if (err != nil)
                        {
                            return err;
                        }
                    }

                    /*
                        Compute the Event's LamportTimestamp
                    */
                    if (ev.lamportTimestamp == nil)
                    {
                        var (lamportTimestamp, err) = h.lamportTimestamp(hash);
                        if (err != nil)
                        {
                            return err;
                        }
                        ev.SetLamportTimestamp(lamportTimestamp);
                        updateEvent = @true;
                    }
                    if (updateEvent)
                    {
                        h.Store.SetEvent(ev);
                    }
                }

            }

            return nil;
        }

        //DecideFame decides if witnesses are famous
        private static error DecideFame(this ref Hashgraph h)
        {
            //Initialize the vote map
            var votes = make(typeof(Dictionary<@string, Dictionary<@string, @bool>>)); //[x][y]=>vote(x,y)
            var setVote = (votes, x, y, vote) =>
            {
                if (votes[x] == nil)
                {
                    votes[x] = make(typeof(Dictionary<@string, @bool>));
                }
                votes[x][y] = vote;
            }
;

            var decidedRounds = map[int]int{}; // [round number] => index in h.PendingRounds

            {
                {
                    var roundIndex = r.Index;
                    var (roundInfo, err) = h.Store.GetRound(roundIndex);
                    if (err != nil)
                    {
                        return err;
                    }
                    {
                        {
                            if (roundInfo.IsDecided(x))
                            {
                                continue;
                            }
VOTE_LOOP:
                            for (var j = roundIndex + 1; j <= h.Store.LastRound(); j++)
                            {
                                {
                                    {
                                        var diff = j - roundIndex;
                                        if (diff == 1)
                                        {
                                            var (ycx, err) = h.see(y, x);
                                            if (err != nil)
                                            {
                                                return err;
                                            }
                                            setVote(votes, y, x, ycx);
                                        }
                                        else
                                        {
                                            //count votes
                                            var ssWitnesses = []string{};
                                            {
                                                {
                                                    var (ss, err) = h.stronglySee(y, w);
                                                    if (err != nil)
                                                    {
                                                        return err;
                                                    }
                                                    if (ss)
                                                    {
                                                        ssWitnesses = append(ssWitnesses, w);
                                                    }
                                                }

                                            }
                                            var yays = 0;
                                            var nays = 0;
                                            {
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

                                            }
                                            var v = @false;
                                            var t = nays;
                                            if (yays >= nays)
                                            {
                                                v = @true;
                                                t = yays;
                                            }

                                            //normal round
                                            if (math.Mod(float64(diff), float64(h.Participants.Len())) > 0)
                                            {
                                                if (t >= h.superMajority)
                                                {
                                                    roundInfo.SetFame(x, v);
                                                    setVote(votes, y, x, v);
                                                    _breakVOTE_LOOP = true; //break out of j loop
                                                    break;
                                                }
                                                else
                                                {
                                                    setVote(votes, y, x, v);
                                                }
                                            }
                                            else
                                            { //coin round
                                                if (t >= h.superMajority)
                                                {
                                                    setVote(votes, y, x, v);
                                                }
                                                else
                                                {
                                                    setVote(votes, y, x, middleBit(y)); //middle bit of y's hash
                                                }
                                            }
                                        }
                                    }

                                }
                            }
                        }

                    }

                    err = h.Store.SetRound(roundIndex, roundInfo);
                    if (err != nil)
                    {
                        return err;
                    }
                    if (roundInfo.WitnessesDecided())
                    {
                        decidedRounds[roundIndex] = pos;
                    }
                }

            }

            h.updatePendingRounds(decidedRounds);
            return nil;
        }

        //DecideRoundReceived assigns a RoundReceived to undetermined events when they
        //reach consensus
        private static error DecideRoundReceived(this ref Hashgraph h)
        {
            var newUndeterminedEvents = []string{};

            /* From whitepaper - 18/03/18
               "[...] An event is said to be “received” in the first round where all the
               unique famous witnesses have received it, if all earlier rounds have the
               fame of all witnesses decided"
            */
            {
                {
                    var received = @false;
                    var (r, err) = h.round(x);
                    if (err != nil)
                    {
                        return err;
                    }
                    for (var i = r + 1; i <= h.Store.LastRound(); i++)
                    {
                        var (tr, err) = h.Store.GetRound(i);
                        if (err != nil)
                        {
                            //Can happen after a Reset/FastSync
                            if (h.LastConsensusRound != nil && r < h.LastConsensusRound.Deref)
                            {
                                received = @true;
                                break;
                            }
                            return err;
                        }

                        //We are looping from earlier to later rounds; so if we encounter
                        //one round with undecided witnesses, we are sure that this event
                        //is not "received". Break out of i loop
                        if (!(tr.WitnessesDecided()))
                        {
                            break;
                        }
                        var fws = tr.FamousWitnesses();
                        //set of famous witnesses that see x
                        var s = []string{};
                        {
                            {
                                var (see, err) = h.see(w, x);
                                if (err != nil)
                                {
                                    return err;
                                }
                                if (see)
                                {
                                    s = append(s, w);
                                }
                            }

                        }

                        if (len(s) == len(fws) && len(s) > 0)
                        {
                            received = @true;

                            var (ex, err) = h.Store.GetEvent(x);
                            if (err != nil)
                            {
                                return err;
                            }
                            ex.SetRoundReceived(i);

                            err = h.Store.SetEvent(ex);
                            if (err != nil)
                            {
                                return err;
                            }
                            tr.SetConsensusEvent(x);
                            err = h.Store.SetRound(i, tr);
                            if (err != nil)
                            {
                                return err;
                            }

                            //break out of i loop
                            break;
                        }
                    }


                    if (!received)
                    {
                        newUndeterminedEvents = append(newUndeterminedEvents, x);
                    }
                }

            }

            h.UndeterminedEvents = newUndeterminedEvents;

            return nil;
        }

        //ProcessDecidedRounds takes Rounds whose witnesses are decided, computes the
        //corresponding Frames, maps them into Blocks, and commits the Blocks via the
        //commit channel
        private static error ProcessDecidedRounds(this ref Hashgraph _h) => func(_h, (ref Hashgraph h, Defer defer, Panic _, Recover _) =>
        {
            //Defer removing processed Rounds from the PendingRounds Queue
            var processedIndex = 0;
            defer(() =>
            {
                h.PendingRounds = h.PendingRounds.slice(processedIndex);
            }());

            {
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
                    if (h.LastConsensusRound != nil && r.Index == h.LastConsensusRound.Deref)
                    {
                        continue;
                    }
                    var (frame, err) = h.GetFrame(r.Index);
                    if (err != nil)
                    {
                        return fmt.Errorf("Getting Frame %d: %v", r.Index, err);
                    }
                    var (round, err) = h.Store.GetRound(r.Index);
                    if (err != nil)
                    {
                        return err;
                    }
                    h.logger.WithFields(logrus.Fields{"round_received":r.Index,"witnesses":round.FamousWitnesses(),"events":len(frame.Events),"roots":frame.Roots,}).Debugf("Processing Decided Round");

                    if (len(frame.Events) > 0)
                    {
                        {
                            {
                                var err = h.Store.AddConsensusEvent(e);
                                if (err != nil)
                                {
                                    return err;
                                }
                                h.ConsensusTransactions += len(e.Transactions());
                                if (e.IsLoaded())
                                {
                                    h.PendingLoadedEvents--;
                                }
                            }
                    else


                        }

                        var lastBlockIndex = h.Store.LastBlockIndex();
                        var (block, err) = NewBlockFromFrame(lastBlockIndex + 1, frame);
                        if (err != nil)
                        {
                            return err;
                        }
                        {
                            var err = h.Store.SetBlock(block);

                            if (err != nil)
                            {
                                return err;
                            }

                        }
                        if (h.commitCh != nil)
                        {
                            h.commitCh.Send(block);
                        }
                    }                    {
                        h.logger.Debugf("No Events to commit for ConsensusRound %d", r.Index);
                    }
                    processedIndex++;

                    if (h.LastConsensusRound == nil || r.Index > h.LastConsensusRound.Deref)
                    {
                        h.setLastConsensusRound(r.Index);
                    }
                }

            }

            return nil;
        });

        //GetFrame computes the Frame corresponding to a RoundReceived.
        private static (Frame, error) GetFrame(this ref Hashgraph h, @int roundReceived)
        {
            //Try to get it from the Store first
            var (frame, err) = h.Store.GetFrame(roundReceived);
            if (err == nil || !common.Is(err, common.KeyNotFound))
            {
                return (frame, err);
            }

            //Get the Round and corresponding consensus Events
            var (round, err) = h.Store.GetRound(roundReceived);
            if (err != nil)
            {
                return (Frame{}, err);
            }
            var events = []Event{};
            {
                {
                    var (e, err) = h.Store.GetEvent(eh);
                    if (err != nil)
                    {
                        return (Frame{}, err);
                    }
                    events = append(events, e);
                }

            }

            sort.Sort(ByLamportTimestamp(events));

            // Get/Create Roots
            var roots = make(typeof(Dictionary<@string, Root>));
            //The events are in topological order. Each time we run into the first Event
            //of a participant, we create a Root for it.
            {
                {
                    var p = ev.Creator();
                    {
                        var (_, ok) = roots[p];

                        if (!ok)
                        {
                            var (root, err) = h.createRoot(ev);
                            if (err != nil)
                            {
                                return (Frame{}, err);
                            }
                            roots[ev.Creator()] = root;
                        }

                    }
                }

                //Every participant needs a Root in the Frame. For the participants that
                //have no Events in this Frame, we create a Root from their last consensus
                //Event, or their last known Root

            }

            //Every participant needs a Root in the Frame. For the participants that
            //have no Events in this Frame, we create a Root from their last consensus
            //Event, or their last known Root
            {
                {
                    {
                        var (_, ok) = roots[p];

                        if (!ok)
                        {
                            Root root;

                            var (lastConsensusEventHash, isRoot, err) = h.Store.LastConsensusEventFrom(p);
                            if (err != nil)
                            {
                                return (Frame{}, err);
                            }
                            if (isRoot)
                            {
                                (root, _) = h.Store.GetRoot(p);
                            }
                            else
                            {
                                var (lastConsensusEvent, err) = h.Store.GetEvent(lastConsensusEventHash);
                                if (err != nil)
                                {
                                    return (Frame{}, err);
                                }
                                (root, err) = h.createRoot(lastConsensusEvent);
                                if (err != nil)
                                {
                                    return (Frame{}, err);
                                }
                            }
                            roots[p] = root;
                        }

                    }
                }

                //Some Events in the Frame might have other-parents that are outside of the
                //Frame (cf root.go ex 2)
                //When inserting these Events in a newly reset hashgraph, the CheckOtherParent
                //method would return an error because the other-parent would not be found.
                //So we make it possible to also look for other-parents in the creator's Root.

            }

            //Some Events in the Frame might have other-parents that are outside of the
            //Frame (cf root.go ex 2)
            //When inserting these Events in a newly reset hashgraph, the CheckOtherParent
            //method would return an error because the other-parent would not be found.
            //So we make it possible to also look for other-parents in the creator's Root.
            var treated = map[string]bool{};
            {
                {
                    treated[ev.Hex()] = @true;
                    var otherParent = ev.OtherParent();
                    if (otherParent != "")
                    {
                        var (opt, ok) = treated[otherParent];
                        if (!opt || !ok)
                        {
                            if (ev.SelfParent() != roots[ev.Creator()].SelfParent.Hash)
                            {
                                var (other, err) = h.createOtherParentRootEvent(ev);
                                if (err != nil)
                                {
                                    return (Frame{}, err);
                                }
                                roots[ev.Creator()].Others[ev.Hex()] = other;
                            }
                        }
                    }
                }

                //order roots

            }

            //order roots
            var orderedRoots = make(typeof(slice<Root>), h.Participants.Len());
            {
                {
                    orderedRoots[i] = roots[peer.PubKeyHex];
                }

            }

            var res = Frame{Round:roundReceived,Roots:orderedRoots,Events:events,};

            {
                var err = h.Store.SetFrame(res);

                if (err != nil)
                {
                    return (Frame{}, err);
                }

            }
            return (res, nil);
        }

        //ProcessSigPool runs through the SignaturePool and tries to map a Signature to
        //a known Block. If a Signature is found to be valid for a known Block, it is
        //appended to the block and removed from the SignaturePool
        private static error ProcessSigPool(this ref Hashgraph _h) => func(_h, (ref Hashgraph h, Defer defer, Panic _, Recover _) =>
        {
            var processedSignatures = map[int]bool{}; //index in SigPool => Processed?
            defer(h.removeProcessedSignatures(processedSignatures));

            {
                {
                    //check if validator belongs to list of participants
                    var validatorHex = fmt.Sprintf("0x%X", bs.Validator);
                    {
                        var (_, ok) = h.Participants.ByPubKey[validatorHex];

                        if (!ok)
                        {
                            h.logger.WithFields(logrus.Fields{"index":bs.Index,"validator":validatorHex,}).Warning("Verifying Block signature. Unknown validator");
                            continue;
                        }

                    }
                    var (block, err) = h.Store.GetBlock(bs.Index);
                    if (err != nil)
                    {
                        h.logger.WithFields(logrus.Fields{"index":bs.Index,"msg":err,}).Warning("Verifying Block signature. Could not fetch Block");
                        continue;
                    }
                    var (valid, err) = block.Verify(bs);
                    if (err != nil)
                    {
                        h.logger.WithFields(logrus.Fields{"index":bs.Index,"msg":err,}).Error("Verifying Block signature");
                        return err;
                    }
                    if (!valid)
                    {
                        h.logger.WithFields(logrus.Fields{"index":bs.Index,"validator":h.Participants.ByPubKey[validatorHex],"block":block,}).Warning("Verifying Block signature. Invalid signature");
                        continue;
                    }
                    block.SetSignature(bs);

                    {
                        var err = h.Store.SetBlock(block);

                        if (err != nil)
                        {
                            h.logger.WithFields(logrus.Fields{"index":bs.Index,"msg":err,}).Warning("Saving Block");
                        }

                    }
                    if (len(block.Signatures) > h.trustCount && (h.AnchorBlock == nil || block.Index() > h.AnchorBlock.Deref))
                    {
                        h.setAnchorBlock(block.Index());
                        h.logger.WithFields(logrus.Fields{"block_index":block.Index(),"signatures":len(block.Signatures),"trustCount":h.trustCount,}).Debug("Setting AnchorBlock");
                    }
                    processedSignatures[i] = @true;
                }

            }

            return nil;
        });

        //GetAnchorBlockWithFrame returns the AnchorBlock and the corresponding Frame.
        //This can be used as a base to Reset a Hashgraph
        private static (Block, Frame, error) GetAnchorBlockWithFrame(this ref Hashgraph h)
        {
            if (h.AnchorBlock == nil)
            {
                return (Block{}, Frame{}, fmt.Errorf("No Anchor Block"));
            }
            var (block, err) = h.Store.GetBlock(h.AnchorBlock.Deref);
            if (err != nil)
            {
                return (Block{}, Frame{}, err);
            }
            var (frame, err) = h.GetFrame(block.RoundReceived());
            if (err != nil)
            {
                return (Block{}, Frame{}, err);
            }
            return (block, frame, nil);
        }

        //Reset clears the Hashgraph and resets it from a new base.
        private static error Reset(this ref Hashgraph h, Block block, Frame frame)
        {
            //Clear all state
            h.LastConsensusRound = nil;
            h.FirstConsensusRound = nil;
            h.AnchorBlock = nil;

            h.UndeterminedEvents = []string{};
            h.PendingRounds = []*pendingRound{};
            h.PendingLoadedEvents = 0;
            h.topologicalIndex = 0;

            var cacheSize = h.Store.CacheSize();
            h.ancestorCache = common.NewLRU(cacheSize, nil);
            h.selfAncestorCache = common.NewLRU(cacheSize, nil);
            h.stronglySeeCache = common.NewLRU(cacheSize, nil);
            h.roundCache = common.NewLRU(cacheSize, nil);

            var participants = h.Participants.ToPeerSlice();

            //Initialize new Roots
            var rootMap = map[string]Root{};
            {
                {
                    var p = participants[id];
                    rootMap[p.PubKeyHex] = root;
                }

            }
            {
                var err = h.Store.Reset(rootMap);

                if (err != nil)
                {
                    return err;
                }

                //Insert Block

            }
            {
                var err = h.Store.SetBlock(block);

                if (err != nil)
                {
                    return err;
                }

            }
            h.setLastConsensusRound(block.RoundReceived());

            //Insert Frame Events
            {
                {
                    {
                        var err = h.InsertEvent(ev, @false);

                        if (err != nil)
                        {
                            return err;
                        }

                    }
                }

            }

            return nil;
        }

        //Bootstrap loads all Events from the Store's DB (if there is one) and feeds
        //them to the Hashgraph (in topological order) for consensus ordering. After this
        //method call, the Hashgraph should be in a state coherent with the 'tip' of the
        //Hashgraph
        private static error Bootstrap(this ref Hashgraph h)
        {
            {
                var (badgerStore, ok) = h.Store.TypeAssert<ref BadgerStore>();

                if (ok)
                {
                    //Retreive the Events from the underlying DB. They come out in topological
                    //order
                    var (topologicalEvents, err) = badgerStore.dbTopologicalEvents();
                    if (err != nil)
                    {
                        return err;
                    }

                    //Insert the Events in the Hashgraph
                    {
                        {
                            {
                                var err = h.InsertEvent(e, @true);

                                if (err != nil)
                                {
                                    return err;
                                }

                            }
                        }

                        //Compute the consensus order of Events

                    }

                    //Compute the consensus order of Events
                    {
                        var err = h.DivideRounds();

                        if (err != nil)
                        {
                            return err;
                        }

                    }
                    {
                        var err = h.DecideFame();

                        if (err != nil)
                        {
                            return err;
                        }

                    }
                    {
                        var err = h.DecideRoundReceived();

                        if (err != nil)
                        {
                            return err;
                        }

                    }
                    {
                        var err = h.ProcessDecidedRounds();

                        if (err != nil)
                        {
                            return err;
                        }

                    }
                    {
                        var err = h.ProcessSigPool();

                        if (err != nil)
                        {
                            return err;
                        }

                    }
                }

            }
            return nil;
        }

        //ReadWireInfo converts a WireEvent to an Event by replacing int IDs with the
        //corresponding public keys.
        private static (ref Event, error) ReadWireInfo(this ref Hashgraph h, WireEvent wevent)
        {
            var selfParent = rootSelfParent(wevent.Body.CreatorID);
            var otherParent = "";
            error err;



            var creator = h.Participants.ById[wevent.Body.CreatorID];
            var (creatorBytes, err) = hex.DecodeString(creator.PubKeyHex.slice(2));
            if (err != nil)
            {
                return (nil, err);
            }
            if (wevent.Body.SelfParentIndex >= 0)
            {
                (selfParent, err) = h.Store.ParticipantEvent(creator.PubKeyHex, wevent.Body.SelfParentIndex);
                if (err != nil)
                {
                    return (nil, err);
                }
            }
            if (wevent.Body.OtherParentIndex >= 0)
            {
                var otherParentCreator = h.Participants.ById[wevent.Body.OtherParentCreatorID];
                (otherParent, err) = h.Store.ParticipantEvent(otherParentCreator.PubKeyHex, wevent.Body.OtherParentIndex);
                if (err != nil)
                {
                    //PROBLEM Check if other parent can be found in the root
                    //problem, we do not known the WireEvent's EventHash, and
                    //we do not know the creators of the roots RootEvents
                    var (root, err) = h.Store.GetRoot(creator.PubKeyHex);
                    if (err != nil)
                    {
                        return (nil, err);
                    }
                    //loop through others
                    var found = @false;
                    {
                        {
                            if (re.CreatorID == wevent.Body.OtherParentCreatorID && re.Index == wevent.Body.OtherParentIndex)
                            {
                                otherParent = re.Hash;
                                found = @true;
                                break;
                            }
                        }

                    }

                    if (!found)
                    {
                        return (nil, fmt.Errorf("OtherParent not found"));
                    }
                }
            }
            var body = EventBody{Transactions:wevent.Body.Transactions,BlockSignatures:wevent.BlockSignatures(creatorBytes),Parents:[]string{selfParent,otherParent},Creator:creatorBytes,Index:wevent.Body.Index,selfParentIndex:wevent.Body.SelfParentIndex,otherParentCreatorID:wevent.Body.OtherParentCreatorID,otherParentIndex:wevent.Body.OtherParentIndex,creatorID:wevent.Body.CreatorID,};

            var event = ref Event{Body:body,Signature:wevent.Signature,};

            return (@event, nil);
        }

        //CheckBlock returns an error if the Block does not contain valid signatures
        //from MORE than 1/3 of participants
        private static error CheckBlock(this ref Hashgraph h, Block block)
        {
            var validSignatures = 0;
            {
                {
                    var (ok, _) = block.Verify(s);
                    if (ok)
                    {
                        validSignatures++;
                    }
                }

            }
            if (validSignatures <= h.trustCount)
            {
                return fmt.Errorf("Not enough valid signatures: got %d, need %d", validSignatures, h.trustCount + 1);
            }
            h.logger.WithField("valid_signatures", validSignatures).Debug("CheckBlock");
            return nil;
        }

        /*******************************************************************************
        Setters
        *******************************************************************************/

        private static void setLastConsensusRound(this ref Hashgraph h, @int i)
        {
            if (h.LastConsensusRound == nil)
            {
                h.LastConsensusRound = @new(@int);
            }
            h.LastConsensusRound.Deref = i;

            if (h.FirstConsensusRound == nil)
            {
                h.FirstConsensusRound = @new(@int);
                h.FirstConsensusRound.Deref = i;
            }
        }

        private static void setAnchorBlock(this ref Hashgraph h, @int i)
        {
            if (h.AnchorBlock == nil)
            {
                h.AnchorBlock = @new(@int);
            }
            h.AnchorBlock.Deref = i;
        }

        /*******************************************************************************
           Helpers
        *******************************************************************************/

        private static @bool middleBit(@string ehex)
        {
            var (hash, err) = hex.DecodeString(ehex.slice(2));
            if (err != nil)
            {
                fmt.Printf("ERROR decoding hex string: %s\n", err);
            }
            if (len(hash) > 0 && hash[len(hash) / 2] == 0)
            {
                return @false;
            }
            return @true;
        }
    }
}
