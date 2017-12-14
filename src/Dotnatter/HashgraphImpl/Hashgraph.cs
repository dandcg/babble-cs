using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Dotnatter.Common;

namespace Dotnatter.HashgraphImpl
{
    public class Hashgraph
    {
        public Dictionary<string, int> Participants { get; set; } //[public key] => id
        public Dictionary<int, string> ReverseParticipants { get; set; } //[id] => public key
        public IStore Store { get; set; } //store of Events and Rounds
        public string[] UndeterminedEvents { get; set; } //[index] => hash
        public Queue<int> UndecidedRounds { get; set; } //queue of Rounds which have undecided witnesses
        public int LastConsensusRound { get; set; } //index of last round where the fame of all witnesses has been decided
        public int LastCommitedRoundEvents { get; set; } //number of evs in round before LastConsensusRound
        public int ConsensusTransactions { get; set; } //number of consensus transactions
        public int PendingLoadedEvents { get; set; } //number of loaded evs that are not yet committed

        public ConcurrentQueue<Event> CommitChannel { get; set; } //channel for committing evs

        public int topologicalIndex { get; set; } //counter used to order evs in topological order
        public int superMajority { get; set; }

        public LruCache<string, bool> ancestorCache { get; set; }
        public LruCache<string, bool> selfAncestorCache { get; set; }
        public LruCache<string, string> oldestSelfAncestorCache { get; set; }
        public LruCache<string, string> stronglySeeCache { get; set; }
        public LruCache<string, string> parentRoundCache { get; set; }
        public LruCache<string, string> roundCache { get; set; }

        public Hashgraph(Dictionary<string, int> participants, IStore store, ConcurrentQueue<Event> commitCh)
        {
            var reverseParticipants = participants.ToDictionary(p => p.Value, p => p.Key);

            var cacheSize = store.CacheSize();

            Participants = participants;
            ReverseParticipants = reverseParticipants;
            Store = store;
            CommitChannel = commitCh;
            ancestorCache = new LruCache<string, bool>(cacheSize, null);
            selfAncestorCache = new LruCache<string, bool>(cacheSize, null);
            oldestSelfAncestorCache = new LruCache<string, string>(cacheSize, null);
            stronglySeeCache = new LruCache<string, string>(cacheSize, null);
            parentRoundCache = new LruCache<string, string>(cacheSize, null);
            roundCache = new LruCache<string, string>(cacheSize, null);

            superMajority = 2 * participants.Count / 3 + 1;
            UndecidedRounds = new Queue<int>(); //initialize
        }


        public int SuperMajority()
        {
            return superMajority;
        }

        //true if y is an ancestor of x
        public bool Ancestor(string x, string y)
        {
        var (c, ok) = ancestorCache.Get(new Key(x,y).ToString());

            if (ok)
            {
                return c;
                
            }

            var a = AncestorInternal(x, y);
            ancestorCache.Add(new Key(x, y).ToString(), a);
            return a;
        }

        public bool AncestorInternal(string x, string y)
        {
            if (x == y)
            {
                return true;

            }

            var (ex, successx) = Store.GetEvent(x);

            if (!successx)
            {
                return false;
            }

            var (ey, successy) = Store.GetEvent(y);

            if (!successy)
            {
                return false;
            }

            var eyCreator = Participants[ey.Creator];
            var lastAncestorKnownFromYCreator = ex.LastAncestors[eyCreator].Index;

            return lastAncestorKnownFromYCreator >= ey.Index();
        }

        //true if y is a self-ancestor of x
        public bool SelfAncestor(string x, string  y )
        {
            var (c, ok) = selfAncestorCache.Get(new Key( x, y).ToString());

            if (ok)
            {
                return c;

            }
            var a = SelfAncestorInternal(x, y);
            selfAncestorCache.Add(new Key(x, y).ToString(), a);
            return a;
        }

        public bool SelfAncestorInternal(string x, string y)
        {
        	if (x == y)
	        {
	            return true;
	        }
            var (ex, successx) = Store.GetEvent(x);

        	if (!successx)
	        {
	            return false;
	        }

            var exCreator = Participants[ex.Creator];

            var (ey, successy) = Store.GetEvent(y);
            if (!successy)
            {
                return false;
            }

            var eyCreator = Participants[ey.Creator];

            return exCreator == eyCreator && ex.Index() >= ey.Index();
        }

        ////true if x sees y
        //public bool See(x, y string)  {
        //	return h.Ancestor(x, y)
        //	//it is not necessary to detect forks because we assume that with our
        //	//implementations, no two evs can be added by the same creator at the
        //	//same height (cf InsertEvent)
        //}

        ////oldest self-ancestor of x to see y
        //public string OldestSelfAncestorToSee(x, y string)  {
        //	if c, ok = h.oldestSelfAncestorCache.Get(Key{x, y}); ok {
        //		return c.(string)
        //	}
        //	res = h.oldestSelfAncestorToSee(x, y)
        //    h.oldestSelfAncestorCache.Add(Key{ x, y}, res)
        //	return res
        //}

        //public string oldestSelfAncestorToSee(x, y string) {
        //	ex, err = h.Store.GetEvent(x)
        //	if err != nil {
        //		return ""
        //	}
        //	ey, err = h.Store.GetEvent(y)
        //	if err != nil {
        //		return ""
        //	}

        //	a = ey.firstDescendants[h.Participants[ex.Creator()]]

        //	if a.index <= ex.Index() {
        //		return a.hash
        //	}

        //	return ""
        //}

        ////true if x strongly sees y
        //public bool StronglySee(x, y string) {
        //	if c, ok = h.stronglySeeCache.Get(Key{x, y}); ok {
        //		return c.(bool)
        //	}
        //	ss = h.stronglySee(x, y)
        //    h.stronglySeeCache.Add(Key{ x, y}, ss)
        //	return ss
        //}

        //public bool stronglySee(x, y string)  {

        //	ex, err = h.Store.GetEvent(x)
        //	if err != nil {
        //		return false
        //	}

        //	ey, err = h.Store.GetEvent(y)
        //	if err != nil {
        //		return false
        //	}

        //	c = 0
        //	for i = 0; i<len(ex.lastAncestors); i++ {
        //		if ex.lastAncestors[i].index >= ey.firstDescendants[i].index {
        //			c++
        //		}
        //	}
        //	return c >= h.SuperMajority()
        //}

        ////PRI.round: max of parent rounds
        ////PRI.isRoot: true if round is taken from a Root
        //public ParentRoundInfo ParentRound(x string)
        //{
        //	if c, ok = h.parentRoundCache.Get(x); ok {
        //		return c.(ParentRoundInfo)
        //	}
        //	pr = h.parentRound(x)
        //    h.parentRoundCache.Add(x, pr)
        //	return pr
        //}

        //public ParentRoundInfo parentRound(x string)  {
        //	res = NewBaseParentRoundInfo()


        //    ex, err = h.Store.GetEvent(x)
        //	if err != nil {
        //		return res
        //	}

        //	//We are going to need the Root later
        //	root, err = h.Store.GetRoot(ex.Creator())
        //	if err != nil {
        //		return res
        //	}

        //	spRound = -1
        //	spRoot = false
        //	//If it is the creator's first Event, use the corresponding Root
        //	if ex.SelfParent() == root.X {
        //		spRound = root.Round
        //        spRoot = true
        //	} else {
        //		spRound = h.Round(ex.SelfParent())
        //		spRoot = false
        //	}

        //	opRound = -1
        //	opRoot = false
        //	if _, err = h.Store.GetEvent(ex.OtherParent()); err == nil {
        //		//if we known the other-parent, fetch its Round directly
        //		opRound = h.Round(ex.OtherParent())
        //	} else if ex.OtherParent() == root.Y {
        //		//we do not know the other-parent but it is referenced in Root.Y
        //		opRound = root.Round
        //        opRoot = true
        //	} else if other, ok = root.Others[x]; ok && other == ex.OtherParent() {
        //		//we do not know the other-parent but it is referenced  in Root.Others
        //		//we use the Root's Round
        //		//in reality the OtherParent Round is not necessarily the same as the
        //		//Root's but it is necessarily smaller. Since We are intererest in the
        //		//max between self-parent and other-parent rounds, this shortcut is
        //		//acceptable.
        //		opRound = root.Round
        //	}

        //	res.round = spRound
        //    res.isRoot = spRoot
        //	if spRound<opRound {
        //    res.round = opRound

        //        res.isRoot = opRoot

        //    }
        //	return res
        //}

        ////true if x is a witness (first ev of a round for the owner)
        //public bool Witness(x string)  {
        //	ex, err = h.Store.GetEvent(x)
        //	if err != nil {
        //		return false
        //	}

        //	root, err = h.Store.GetRoot(ex.Creator())
        //	if err != nil {
        //		return false
        //	}

        //	//If it is the creator's first Event, return true
        //	if ex.SelfParent() == root.X && ex.OtherParent() == root.Y {
        //		return true
        //	}

        //	return h.Round(x) > h.Round(ex.SelfParent())
        //}

        ////true if round of x should be incremented
        //public bool RoundInc(x string)  {

        //	parentRound = h.ParentRound(x)

        //	//If parent-round was obtained from a Root, then x is the Event that sits
        //	//right on top of the Root. RoundInc is true.
        //	if parentRound.isRoot {
        //		return true
        //	}

        //	//If parent-round was obtained from a regulare Event, then we need to check
        //	//if x strongly-sees a strong majority of withnesses from parent-round.
        //	c = 0
        //	for _, w = range h.Store.RoundWitnesses(parentRound.round)
        //{
        //    if h.StronglySee(x, w) {
        //        c++

        //        }
        //}

        //	return c >= h.SuperMajority()
        //}

        //public int RoundReceived(x string) {

        //	ex, err = h.Store.GetEvent(x)
        //	if err != nil {
        //		return -1
        //	}
        //	if ex.roundReceived == nil {
        //		return -1
        //	}

        //	return * ex.roundReceived
        //}

        //public int Round(x string) {
        //	if c, ok = h.roundCache.Get(x); ok {
        //		return c.(int)
        //	}
        //	r = h.round(x)
        //    h.roundCache.Add(x, r)
        //	return r
        //}

        //public int round(x string)  {

        //	round = h.ParentRound(x).round

        //    inc = h.RoundInc(x)

        //	if inc {
        //		round++
        //	}
        //	return round
        //}

        ////round(x) - round(y)
        //public int RoundDiff(x, y string)  {

        //	xRound = h.Round(x)
        //	if xRound< 0 {
        //		return math.MinInt32, fmt.Errorf("ev %s has negative round", x)
        //	}
        //	yRound = h.Round(y)
        //	if yRound< 0 {
        //		return math.MinInt32, fmt.Errorf("ev %s has negative round", y)
        //	}

        //	return xRound - yRound, nil
        //}

        //public void InsertEvent(ev Event, setWireInfo bool)  {
        //	//verify signature
        //	if ok, err = ev.Verify(); !ok
        //{
        //    if err != nil {
        //        return err

        //        }
        //    return fmt.Errorf("Invalid signature")

        //    }

        //	if err = h.CheckSelfParent(ev); err != nil
        //{
        //    return fmt.Errorf("CheckSelfParent: %s", err)

        //    }

        //	if err = h.CheckOtherParent(ev); err != nil
        //{
        //    return fmt.Errorf("CheckOtherParent: %s", err)

        //    }

        //	ev.topologicalIndex = h.topologicalIndex
        //    h.topologicalIndex++

        //	if setWireInfo
        //{
        //    if err = h.SetWireInfo(&ev); err != nil {
        //        return fmt.Errorf("SetWireInfo: %s", err)

        //        }
        //}

        //	if err = h.InitEventCoordinates(&ev); err != nil
        //{
        //    return fmt.Errorf("InitEventCoordinates: %s", err)

        //    }

        //	if err = h.Store.SetEvent(ev); err != nil
        //{
        //    return fmt.Errorf("SetEvent: %s", err)

        //    }

        //	if err = h.UpdateAncestorFirstDescendant(ev); err != nil
        //{
        //    return fmt.Errorf("UpdateAncestorFirstDescendant: %s", err)

        //    }

        //h.UndeterminedEvents = append(h.UndeterminedEvents, ev.Hex())

        //	if ev.IsLoaded() {
        //    h.PendingLoadedEvents++

        //    }

        //	return nil
        //}

        ////Check the SelfParent is the Creator's last known Event
        //public void CheckSelfParent(ev Event)
        //{
        //	selfParent = ev.SelfParent()
        //	creator = ev.Creator()

        //	creatorLastKnown, _, err = h.Store.LastFrom(creator)
        //	if err != nil
        //{
        //    return err

        //    }

        //selfParentLegit = selfParent == creatorLastKnown

        //	if !selfParentLegit
        //{
        //    return fmt.Errorf("Self-parent not last known ev by creator")

        //    }

        //	return nil
        //}

        ////Check if we know the OtherParent
        //public void CheckOtherParent(ev Event)
        //{
        //	otherParent = ev.OtherParent()
        //	if otherParent != "" {
        //    //Check if we have it
        //    _, err= h.Store.GetEvent(otherParent)

        //        if err != nil {
        //        //it might still be in the Root
        //        root, err= h.Store.GetRoot(ev.Creator())

        //            if err != nil {
        //            return err

        //            }
        //        if root.X == ev.SelfParent() && root.Y == otherParent {
        //            return nil

        //            }
        //        other, ok= root.Others[ev.Hex()]

        //            if ok && other == ev.OtherParent() {
        //            return nil

        //            }
        //        return fmt.Errorf("Other-parent not known")

        //        }
        //}
        //	return nil
        //}

        ////initialize arrays of last ancestors and first descendants
        public void InitEventCoordinates(Event ev)
        {
            var members = Participants.Count;

            ev.FirstDescendants = new EventCoordinates[members];

            for (var fakeId = 0; fakeId < members; fakeId++)
            {
                ev.FirstDescendants[fakeId] = new EventCoordinates
                {
                    Index = int.MaxValue
                };
            }

            ev.LastAncestors = new EventCoordinates[members];

            var ( selfParent, selfParentSuccess) = Store.GetEvent(ev.SelfParent());
            var ( otherParent, otherParentSuccess) = Store.GetEvent(ev.OtherParent());

            if (!selfParentSuccess && !otherParentSuccess)
            {
                for (var fakeId = 0; fakeId < members; fakeId++)
                {
                    ev.LastAncestors[fakeId] = new EventCoordinates
                    {
                        Index = -1
                    };
                }
            }
            else if (!selfParentSuccess)
            {
                Array.Copy(otherParent.LastAncestors.Take(members).ToArray(), 0, ev.LastAncestors, 0, members);
            }
            else if (!otherParentSuccess)
            {
                Array.Copy(selfParent.LastAncestors.Take(members).ToArray(), 0, ev.LastAncestors, 0, members);
            }
            else
            {
                var selfParentLastAncestors = selfParent.LastAncestors;

                var otherParentLastAncestors = otherParent.LastAncestors;

                Array.Copy(selfParentLastAncestors.Take(members).ToArray(), 0, ev.LastAncestors, 0, members);
            
                for (var i = 0; i < members; i++)
                {
                    if (ev.LastAncestors[i].Index < otherParentLastAncestors[i].Index)
                    {
                        {
                            ev.LastAncestors[i].Index = otherParentLastAncestors[i].Index;
                            ev.LastAncestors[i].Hash = otherParentLastAncestors[i].Hash;
                        }
                    }
                }
            }
            var index = ev.Index();

                var creator = ev.Creator;

                if (Participants.TryGetValue(creator, out var fakeCreatorId))
                {
                    var hash = ev.Hex();

                    ev.FirstDescendants[fakeCreatorId] = new EventCoordinates {Index = index, Hash = hash};
                    ev.LastAncestors[fakeCreatorId] = new EventCoordinates {Index = index, Hash = hash};
                }
            
        }

//update first decendant of each last ancestor to point to ev
        public void UpdateAncestorFirstDescendant(Event ev)
        {
            var fakeCreatorID = Participants[ev.Creator];

            var index = ev.Index();
            var hash = ev.Hex();

            for (var i = 0; i < ev.LastAncestors.Length; i++)
            {
                var ah = ev.LastAncestors[i]?.Hash;

                while (!string.IsNullOrEmpty(ah) )
                {
                    var (a, success) = Store.GetEvent(ah);


                    if (a.FirstDescendants[fakeCreatorID].Index == int.MaxValue)
                    {
                        a.FirstDescendants[fakeCreatorID] = new EventCoordinates
                        {
                            Index = index,
                            Hash = hash
                        };

                        Store.SetEvent(a);

                        ah = a.SelfParent();
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        //public void SetWireInfo(ev *Event)
        //{
        //	selfParentIndex = -1
        //	otherParentCreatorID = -1
        //	otherParentIndex = -1

        //	//could be the first Event inserted for this creator. In this case, use Root
        //	if lf, isRoot, _ = h.Store.LastFrom(ev.Creator()); isRoot && lf == ev.SelfParent() {
        //    root, err= h.Store.GetRoot(ev.Creator())

        //        if err != nil {
        //        return err

        //        }
        //    selfParentIndex = root.Index

        //    } else {
        //    selfParent, err= h.Store.GetEvent(ev.SelfParent())

        //        if err != nil {
        //        return err

        //        }
        //    selfParentIndex = selfParent.Index()

        //    }

        //	if ev.OtherParent() != "" {
        //    otherParent, err= h.Store.GetEvent(ev.OtherParent())

        //        if err != nil {
        //        return err

        //        }
        //    otherParentCreatorID = h.Participants[otherParent.Creator()]

        //        otherParentIndex = otherParent.Index()

        //    }

        //	ev.SetWireInfo(selfParentIndex,
        //		otherParentCreatorID,
        //		otherParentIndex,
        //		h.Participants [ev.Creator()])

        //	return nil
        //}

        //public Event ReadWireInfo(wev WireEvent)
        //{
        //	selfParent = ""
        //	otherParent = ""
        //	var err error

        //    creator = h.ReverseParticipants[wev.Body.CreatorID]
        //    creatorBytes, err = hex.DecodeString(creator[2:])
        //	if err != nil {
        //		return nil, err
        //	}

        //	if wev.Body.SelfParentIndex >= 0 {
        //		selfParent, err = h.Store.ParticipantEvent(creator, wev.Body.SelfParentIndex)
        //		if err != nil {
        //			return nil, err
        //		}
        //	}
        //	if wev.Body.OtherParentIndex >= 0 {
        //		otherParentCreator = h.ReverseParticipants[wev.Body.OtherParentCreatorID]
        //        otherParent, err = h.Store.ParticipantEvent(otherParentCreator, wev.Body.OtherParentIndex)
        //		if err != nil {
        //			return nil, err
        //		}
        //	}

        //	body = EventBody{
        //		Transactions: wev.Body.Transactions,
        //		Parents:      [] string{selfParent, otherParent},
        //		Creator:      creatorBytes,

        //		Timestamp:            wev.Body.Timestamp,
        //		Index:                wev.Body.Index,
        //		selfParentIndex:      wev.Body.SelfParentIndex,
        //		otherParentCreatorID: wev.Body.OtherParentCreatorID,
        //		otherParentIndex:     wev.Body.OtherParentIndex,
        //		creatorID:            wev.Body.CreatorID,
        //	}

        //	ev = &Event
        //{
        //    Body: body,
        //		R: wev.R,
        //		S: wev.S,
        //	}

        //	return ev, nil
        //}

        //public void DivideRounds()
        //{
        //	for _, hash = range h.UndeterminedEvents
        //{
        //    roundNumber = h.Round(hash)
        //		witness = h.Witness(hash)
        //		roundInfo, err = h.Store.GetRound(roundNumber)

        //		//If the RoundInfo is not found in the Store's Cache, then the Hashgraph
        //		//is not aware of it yet. We need to add the roundNumber to the queue of
        //		//undecided rounds so that it will be processed in the other consensus
        //		//methods
        //		if err != nil && !common.Is(err, common.KeyNotFound) {
        //        return err

        //        }
        //		//If the RoundInfo is actually taken from the Store's DB, then it still
        //		//has not been processed by the Hashgraph consensus methods (The 'queued'
        //		//field is not exported and therefore not persisted in the DB).
        //		//RoundInfos taken from the DB directly will always have this field set
        //		//to false
        //		if !roundInfo.queued
        //    {
        //        h.UndecidedRounds = append(h.UndecidedRounds, roundNumber)

        //            roundInfo.queued = true

        //        }

        //    roundInfo.AddEvent(hash, witness)
        //		err = h.Store.SetRound(roundNumber, roundInfo)
        //		if err != nil
        //    {
        //        return err

        //        }
        //}
        //	return nil
        //}

        ////decide if witnesses are famous
        //public void DecideFame()
        //{
        //	votes = make(map[string](map[string]bool)) //[x][y]=>vote(x,y)

        //	decidedRounds = map[int] int{} // [round number] => index in h.UndecidedRounds
        //	defer h.updateUndecidedRounds(decidedRounds)

        //	for pos, i = range h.UndecidedRounds
        //{
        //    roundInfo, err = h.Store.GetRound(i)
        //		if err != nil
        //    {
        //        return err

        //        }
        //		for _, x = range roundInfo.Witnesses() {
        //        if roundInfo.IsDecided(x) {
        //            continue

        //            }
        //        X:
        //        for j = i + 1; j <= h.Store.LastRound(); j++ {
        //            for _, y = range h.Store.RoundWitnesses(j) {
        //                diff= j - i

        //                    if diff == 1 {
        //                    setVote(votes, y, x, h.See(y, x))

        //                    }
        //                else
        //                {
        //                    //count votes
        //                    ssWitnesses= []string{ }
        //                    for _, w = range h.Store.RoundWitnesses(j - 1) {
        //                        if h.StronglySee(y, w) {
        //                            ssWitnesses = append(ssWitnesses, w)

        //                            }
        //                    }
        //                    yays= 0

        //                        nays= 0

        //                        for _, w = range ssWitnesses {
        //                        if votes[w][x] {
        //                            yays++

        //                            }
        //                        else
        //                        {
        //                            nays++

        //                            }
        //                    }
        //                    v= false

        //                        t= nays

        //                        if yays >= nays {
        //                        v = true

        //                            t = yays

        //                        }

        //                    //normal round
        //                    if math.Mod(float64(diff), float64(len(h.Participants))) > 0 {
        //                        if t >= h.SuperMajority() {
        //                            roundInfo.SetFame(x, v)

        //                                setVote(votes, y, x, v)

        //                                break X //break out of j loop

        //                            }
        //                        else
        //                        {
        //                            setVote(votes, y, x, v)

        //                            }
        //                    }
        //                    else
        //                    { //coin round
        //                        if t >= h.SuperMajority() {
        //                            setVote(votes, y, x, v)

        //                            }
        //                        else
        //                        {
        //                            setVote(votes, y, x, middleBit(y)) //middle bit of y's hash

        //                            }
        //                    }
        //                }
        //            }
        //        }
        //    }

        //		//Update decidedRounds and LastConsensusRound if all witnesses have been decided
        //		if roundInfo.WitnessesDecided() {
        //        decidedRounds[i] = pos


        //            if h.LastConsensusRound == nil || i > *h.LastConsensusRound {
        //            h.setLastConsensusRound(i)

        //            }
        //    }

        //    err = h.Store.SetRound(i, roundInfo)
        //		if err != nil
        //    {
        //        return err

        //        }
        //}
        //	return nil
        //}

        ////remove items from UndecidedRounds
        //public void updateUndecidedRounds(decidedRounds map[int]int)
        //{
        //    newUndecidedRounds= []int{ }
        //    for _, ur = range h.UndecidedRounds {
        //        if _, ok= decidedRounds[ur]; !ok {
        //            newUndecidedRounds = append(newUndecidedRounds, ur)

        //        }
        //    }
        //    h.UndecidedRounds = newUndecidedRounds
        //}

        //public void setLastConsensusRound(i int)
        //{
        //    if h.LastConsensusRound == nil {
        //        h.LastConsensusRound = new(int)

        //    }
        //    *h.LastConsensusRound = i


        //    h.LastCommitedRoundEvents = h.Store.RoundEvents(i - 1)
        //}

        ////assign round received and timestamp to all evs
        //public void DecideRoundReceived()
        //{
        //	for _, x = range h.UndeterminedEvents
        //{
        //    r = h.Round(x)
        //		for i = r + 1; i <= h.Store.LastRound(); i++ {
        //        tr, err= h.Store.GetRound(i)

        //            if err != nil && !common.Is(err, common.KeyNotFound) {
        //            return err

        //            }

        //        //skip if some witnesses are left undecided
        //        if !(tr.WitnessesDecided() && h.UndecidedRounds[0] > i) {
        //            continue

        //            }

        //        fws= tr.FamousWitnesses()
        //            //set of famous witnesses that see x
        //        s= []string{ }
        //        for _, w = range fws {
        //            if h.See(w, x) {
        //                s = append(s, w)

        //                }
        //        }
        //        if len(s) > len(fws) / 2 {
        //            ex, err= h.Store.GetEvent(x)

        //                if err != nil {
        //                return err

        //                }
        //            ex.SetRoundReceived(i)


        //                t= []string{ }
        //            for _, a = range s {
        //                t = append(t, h.OldestSelfAncestorToSee(a, x))

        //                }

        //            ex.consensusTimestamp = h.MedianTimestamp(t)


        //                err = h.Store.SetEvent(ex)

        //                if err != nil {
        //                return err

        //                }

        //            break

        //            }
        //    }
        //}
        //	return nil
        //}

        //public void FindOrder()
        //{
        //	err = h.DecideRoundReceived()
        //	if err != nil {
        //		return err
        //	}

        //	newConsensusEvents = [] Event{}
        //	newUndeterminedEvents = [] string{}
        //	for _, x = range h.UndeterminedEvents
        //{
        //    ex, err = h.Store.GetEvent(x)
        //		if err != nil
        //    {
        //        return err

        //        }
        //		if ex.roundReceived != nil
        //    {
        //        newConsensusEvents = append(newConsensusEvents, ex)

        //        } else {
        //        newUndeterminedEvents = append(newUndeterminedEvents, x)

        //        }
        //}
        //h.UndeterminedEvents = newUndeterminedEvents

        //sorter = NewConsensusSorter(newConsensusEvents)

        //    sort.Sort(sorter)

        //	for _, e = range newConsensusEvents
        //{
        //    err = h.Store.AddConsensusEvent(e.Hex())
        //		if err != nil
        //    {
        //        return err

        //        }
        //    h.ConsensusTransactions += len(e.Transactions())
        //		if e.IsLoaded() {
        //        h.PendingLoadedEvents--

        //        }
        //}

        //	if h.commitCh != nil && len(newConsensusEvents) > 0 {
        //		h.commitCh<- newConsensusEvents
        //	}

        //	return nil
        //}

        //public DateTime MedianTimestamp(evHashes[]string)
        //{
        //	evs = [] Event{}
        //	for _, x = range evHashes
        //{
        //    ex, _ = h.Store.GetEvent(x)
        //		evs = append(evs, ex)
        //	}
        //sort.Sort(ByTimestamp(evs))
        //	return evs[len(evs) / 2].Body.Timestamp
        //}

        //public void ConsensusEvents() [] string {
        //	return h.Store.ConsensusEvents()
        //}

        ////number of evs per participants
        //public Dictionary<int,int> Known()
        //{
        //	return h.Store.Known()
        //}

        //public void Reset(roots map[string]Root)
        //{
        //	if err = h.Store.Reset(roots); err != nil {
        //		return err
        //	}

        //	h.UndeterminedEvents = [] string{}
        //	h.UndecidedRounds = [] int{}
        //	h.PendingLoadedEvents = 0
        //	h.topologicalIndex = 0

        //	cacheSize = h.Store.CacheSize()
        //    h.ancestorCache = common.NewLRU(cacheSize, nil)
        //    h.selfAncestorCache = common.NewLRU(cacheSize, nil)

        //    h.oldestSelfAncestorCache = common.NewLRU(cacheSize, nil)

        //    h.stronglySeeCache = common.NewLRU(cacheSize, nil)

        //    h.parentRoundCache = common.NewLRU(cacheSize, nil)

        //    h.roundCache = common.NewLRU(cacheSize, nil)

        //	return nil
        //}

        //public Frame GetFrame()
        //{
        //	lastConsensusRoundIndex = 0
        //	if lcr = h.LastConsensusRound; lcr != nil {
        //		lastConsensusRoundIndex = * lcr
        //	}

        //	lastConsensusRound, err = h.Store.GetRound(lastConsensusRoundIndex)
        //	if err != nil {
        //		return Frame{}, err
        //	}

        //	witnessHashes = lastConsensusRound.Witnesses()

        //    evs = [] Event{}
        //	roots = make(map[string] Root)
        //	for _, wh = range witnessHashes
        //{
        //    w, err = h.Store.GetEvent(wh)
        //		if err != nil
        //    {
        //        return Frame{ }, err

        //        }
        //    evs = append(evs, w)
        //		roots [w.Creator()] = Root
        //    {
        //        X: w.SelfParent(),
        //			Y: w.OtherParent(),
        //			Index: w.Index() - 1,
        //			Round: h.Round(w.SelfParent()),
        //			Others: map[string]string{ },
        //		}

        //    participantEvents, err = h.Store.ParticipantEvents(w.Creator(), w.Index())
        //		if err != nil
        //    {
        //        return Frame{ }, err

        //        }
        //		for _, e = range participantEvents
        //    {
        //        ev, err= h.Store.GetEvent(e)

        //            if err != nil {
        //            return Frame{ }, err

        //            }
        //        evs = append(evs, ev)

        //        }
        //}

        //	//Not every participant necessarily has a witness in LastConsensusRound.
        //	//Hence, there could be participants with no Root at this point.
        //	//For these partcipants, use their last known Event.
        //	for p = range h.Participants
        //{
        //		if _, ok = roots [p]; !ok
        //    {
        //        var root Root
        //        last, isRoot, err = h.Store.LastFrom(p)

        //            if err != nil {
        //            return Frame{ }, err

        //            }
        //        if isRoot {
        //            root, err = h.Store.GetRoot(p)

        //                if err != nil {
        //                return Frame{ }, err

        //                }
        //        }
        //        else
        //        {
        //            ev, err= h.Store.GetEvent(last)

        //                if err != nil {
        //                return Frame{ }, err

        //                }
        //            evs = append(evs, ev)

        //                root = Root{
        //                X: ev.SelfParent(),
        //					Y: ev.OtherParent(),
        //					Index: ev.Index() - 1,
        //					Round: h.Round(ev.SelfParent()),
        //					Others: map[string]string{ },
        //				}
        //        }
        //        roots[p] = root

        //        }
        //}

        //sort.Sort(ByTopologicalOrder(evs))

        //	//Some Events in the Frame might have other-parents that are outside of the
        //	//Frame (cf root.go ex 2)
        //	//When inserting these Events in a newly reset hashgraph, the CheckOtherParent
        //	//method would return an error because the other-parent would not be found.
        //	//So we make it possible to also look for other-parents in the creator's Root.
        //	treated = map[string] bool{}
        //	for _, ev = range evs
        //{
        //    treated [ev.Hex()] = true
        //		otherParent = ev.OtherParent()
        //		if otherParent != "" {
        //        opt, ok= treated[otherParent]

        //            if !opt || !ok {
        //            if ev.SelfParent() != roots[ev.Creator()].X {
        //                roots[ev.Creator()].Others[ev.Hex()] = otherParent

        //                }
        //        }
        //    }
        //}

        //frame = Frame{
        //		Roots:  roots,
        //		Events: evs,
        //	}

        //	return frame, nil
        //}

        ////Bootstrap loads all Events from the Store's DB (if there is one) and feeds
        ////them to the Hashgraph (in topological order) for consensus ordering. After this
        ////method call, the Hashgraph should be in a state coeherent with the 'tip' of the
        ////Hashgraph
        //public void Bootstrap()
        //{
        //	if badgerStore, ok = h.Store.(* BadgerStore); ok {
        //		//Retreive the Events from the underlying DB. They come out in topological
        //		//order
        //		topologicalEvents, err = badgerStore.dbTopologicalEvents()
        //		if err != nil {
        //			return err
        //		}

        //		//Insert the Events in the Hashgraph
        //		for _, e = range topologicalEvents
        //{
        //			if err = h.InsertEvent(e, true); err != nil
        //    {
        //        return err

        //            }
        //}

        //		//Compute the consensus order of Events
        //		if err = h.DivideRounds(); err != nil {
        //			return err
        //		}
        //		if err = h.DecideFame(); err != nil {
        //			return err
        //		}
        //		if err = h.FindOrder(); err != nil {
        //			return err
        //		}
        //	}

        //	return nil
        //}

        //public bool middleBit(ehex string) bool {
        //	hash, err = hex.DecodeString(ehex[2:])
        //	if err != nil {
        //		fmt.Printf("ERROR decoding hex string: %s\n", err)
        //	}
        //	if len(hash) > 0 && hash[len(hash) / 2] == 0 {
        //		return false
        //	}
        //	return true
        //}

        //public void setVote(votes map[string]map[string]bool, x, y string, vote bool)
        //{
        //    if votes[x] == nil {
        //        votes[x] = make(map[string]bool)

        //    }
        //    votes[x][y] = vote
        //}
    }
}