using System;
using System.Collections.Generic;
using System.Linq;
using Dotnatter.Common;
using Dotnatter.Util;

namespace Dotnatter.HashgraphImpl
{
    public class Hashgraph
    {
        public Dictionary<string, int> Participants { get; set; } //[public key] => id
        public Dictionary<int, string> ReverseParticipants { get; set; } //[id] => public key
        public IStore Store { get; set; } //store of Events and Rounds
        public List<string> UndeterminedEvents { get; set; } //[index] => hash
        public Queue<int> UndecidedRounds { get; set; } //queue of Rounds which have undecided witnesses
        public int? LastConsensusRound { get; set; } //index of last round where the fame of all witnesses has been decided
        public int LastCommitedRoundEvents { get; set; } //number of evs in round before LastConsensusRound
        public int ConsensusTransactions { get; set; } //number of consensus transactions
        public int PendingLoadedEvents { get; set; } //number of loaded evs that are not yet committed
        public Channel<Event> CommitChannel { get; set; } //channel for committing evs
        public int TopologicalIndex { get; set; } //counter used to order evs in topological order
        public int SuperMajority { get; set; }

        public LruCache<string, bool> AncestorCache { get; set; }
        public LruCache<string, bool> SelfAncestorCache { get; set; }
        public LruCache<string, string> OldestSelfAncestorCache { get; set; }
        public LruCache<string, bool> StronglySeeCache { get; set; }
        public LruCache<string, ParentRoundInfo> ParentRoundCache { get; set; }
        public LruCache<string, int> RoundCache { get; set; }

        public Hashgraph(Dictionary<string, int> participants, IStore store, Channel<Event> commitCh)
        {
            var reverseParticipants = participants.ToDictionary(p => p.Value, p => p.Key);
            var cacheSize = store.CacheSize();

            Participants = participants;
            ReverseParticipants = reverseParticipants;
            Store = store;
            CommitChannel = commitCh;
            AncestorCache = new LruCache<string, bool>(cacheSize, null);
            SelfAncestorCache = new LruCache<string, bool>(cacheSize, null);
            OldestSelfAncestorCache = new LruCache<string, string>(cacheSize, null);
            StronglySeeCache = new LruCache<string, bool>(cacheSize, null);
            ParentRoundCache = new LruCache<string, ParentRoundInfo>(cacheSize, null);
            RoundCache = new LruCache<string, int>(cacheSize, null);
            UndeterminedEvents = new List<string>();
            SuperMajority = 2 * participants.Count / 3 + 1;
            UndecidedRounds = new Queue<int>(); //initialize
        }

     
        //true if y is an ancestor of x
        public bool Ancestor(string x, string y)
        {
            var (c, ok) = AncestorCache.Get(Key.New(x, y));

            if (ok)
            {
                return c;
            }

            var a = AncestorInternal(x, y);
            AncestorCache.Add(Key.New(x, y), a);
            return a;
        }

        private bool AncestorInternal(string x, string y)
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
        public bool SelfAncestor(string x, string y)
        {
            var (c, ok) = SelfAncestorCache.Get(Key.New(x, y));

            if (ok)
            {
                return c;
            }
            var a = SelfAncestorInternal(x, y);
            SelfAncestorCache.Add(Key.New(x, y), a);
            return a;
        }

        private bool SelfAncestorInternal(string x, string y)
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

        //true if x sees y
        public bool See(string x, string y)
        {
            return Ancestor(x, y);
            //it is not necessary to detect forks because we assume that with our
            //implementations, no two evs can be added by the same creator at the
            //same height (cf InsertEvent)
        }

        //oldest self-ancestor of x to see y
        public string OldestSelfAncestorToSee(string x, string y)
        {
            var ( c, ok) = OldestSelfAncestorCache.Get(Key.New(x, y));

            if (ok)
            {
                return c;
            }

            var res = OldestSelfAncestorToSeeInternal(x, y);
            OldestSelfAncestorCache.Add(Key.New(x, y), res);
            return res;
        }

        private string OldestSelfAncestorToSeeInternal(string x, string y)
        {
            var (ex, successx) = Store.GetEvent(x);

            if (!successx)
            {
                return "";
            }

            var (ey, successy) = Store.GetEvent(y);

            if (!successy)
            {
                return "";
            }

            var a = ey.FirstDescendants[Participants[ex.Creator]];

            if (a.Index <= ex.Index())
            {
                return a.Hash;
            }
            return "";
        }

        //true if x strongly sees y
        public bool StronglySee(string x, string y)
        {
            var (c, ok) = StronglySeeCache.Get(Key.New(x, y));
            if (ok)
            {
                return c;
            }
            var ss = StronglySeeInternal(x, y);
            StronglySeeCache.Add(Key.New(x, y), ss);
            return ss;
        }

        public bool StronglySeeInternal(string x, string y)
        {
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

            var c = 0;

            for (var i = 0; i < ex.LastAncestors.Length; i++)
            {
                if (ex.LastAncestors[i].Index >= ey.FirstDescendants[i].Index)
                {
                    c++;
                }
            }
            return c >= SuperMajority;
        }

        //PRI.round: max of parent rounds
        //PRI.isRoot: true if round is taken from a Root
        public ParentRoundInfo ParentRound(string x)
        {
            var (c, ok) = ParentRoundCache.Get(x);
            if (ok)
            {
                return c;
            }
            var pr = ParentRoundInternal(x);
            ParentRoundCache.Add(x, pr);

            return pr;
        }

        public ParentRoundInfo ParentRoundInternal(string x)
        {
            var res = new ParentRoundInfo();

            var (ex, successx) = Store.GetEvent(x);

            if (!successx)
            {
                return res;
            }

            //We are going to need the Root later
            var root = Store.GetRoot(ex.Creator);

            if (root == null)
            {
                return res;
            }

            var spRound = -1;

            var spRoot = false;
            //If it is the creator's first Event, use the corresponding Root
            if (ex.SelfParent() == root.X)
            {
                spRound = root.Round;
                spRoot = true;
            }
            else
            {
                spRound = Round(ex.SelfParent());

                spRoot = false;
            }

            var opRound = -1;

            var opRoot = false;

            var (_, success) = Store.GetEvent(ex.OtherParent());
            if (success)
            {
                //if we known the other-parent, fetch its Round directly
                opRound = Round(ex.OtherParent());
            }
            else if (ex.OtherParent() == root.Y)
            {
                //we do not know the other-parent but it is referenced in Root.Y
                opRound = root.Round;
                opRoot = true;
            }
            else if (root.Others.TryGetValue(x, out var other))
            {
                if (other == ex.OtherParent())
                {
                    //we do not know the other-parent but it is referenced  in Root.Others
                    //we use the Root's Round
                    //in reality the OtherParent Round is not necessarily the same as the
                    //Root's but it is necessarily smaller. Since We are intererest in the
                    //max between self-parent and other-parent rounds, this shortcut is
                    //acceptable.
                    opRound = root.Round;
                }
            }

            res.Round = spRound;
            res.IsRoot = spRoot;

            if (spRound < opRound)
            {
                res.Round = opRound;
                res.IsRoot = opRoot;
            }
            return res;
        }

        ////true if x is a witness (first ev of a round for the owner)
        public bool Witness(string x)
        {
            var (ex, successx) = Store.GetEvent(x);

            if (!successx)
            {
                return false;
            }

            var root = Store.GetRoot(ex.Creator);

            if (root == null)
            {
                return false;
            }

            //If it is the creator's first Event, return true
            if (ex.SelfParent() == root.X && ex.OtherParent() == root.Y)
            {
                return true;
            }

            return Round(x) > Round(ex.SelfParent());
        }

        //true if round of x should be incremented
        public bool RoundInc(string x)
        {
            var parentRound = ParentRound(x);

            //If parent-round was obtained from a Root, then x is the Event that sits
            //right on top of the Root. RoundInc is true.
            if (parentRound.IsRoot)
            {
                return true;
            }

            //If parent-round was obtained from a regulare Event, then we need to check
            //if x strongly-sees a strong majority of withnesses from parent-round.
            var c = 0;

            foreach (var w in Store.RoundWitnesses(parentRound.Round))
            {
                if (StronglySee(x, w))
                {
                    c++;
                }
            }

            return c >= SuperMajority;
        }

        public int RoundReceived(string x)
        {
            var (ex, successx) = Store.GetEvent(x);

            if (!successx)
            {
                return -1;
            }

            return ex.RoundReceived ?? -1;
        }

        public int Round(string x)
        {
            var (c, ok) = RoundCache.Get(x);
            if (ok)
            {
                return c;
            }
            var r = RoundInternal(x);
            RoundCache.Add(x, r);

            return r;
        }

        private int RoundInternal(string x)
        {
            var round = ParentRound(x).Round;

            var inc = RoundInc(x);

            if (inc)
            {
                round++;
            }
            return round;
        }

        //round(x) - round(y)
        public int RoundDiff(string x, string y)
        {
            var xRound = Round(x);

            if (xRound < 0)
            {
                throw new ApplicationException($"ev {x} has negative round");
            }
            var yRound = Round(y);

            if (yRound < 0)
            {
                throw new ApplicationException($"ev {y} has negative round");
            }

            return xRound - yRound;
        }

        public void InsertEvent(Event ev, bool setWireInfo)
        {
            //verify signature
            if (!ev.Verify())
            {
                throw new ApplicationException($"Invalid signature");
            }

            CheckSelfParent(ev);

            CheckOtherParent(ev);

            ev.TopologicalIndex = TopologicalIndex;
            TopologicalIndex++;

            if (setWireInfo)
            {
                SetWireInfo(ev);
            }

            InitEventCoordinates(ev);

            Store.SetEvent(ev);

            UpdateAncestorFirstDescendant(ev);

            UndeterminedEvents.Add(ev.Hex());

            if (ev.IsLoaded())
            {
                PendingLoadedEvents++;
            }
        }

        //Check the SelfParent is the Creator's last known Event
        public void CheckSelfParent(Event ev)
        {
            var selfParent = ev.SelfParent();

            var creator = ev.Creator;

            var (creatorLastKnown, _) = Store.LastFrom(creator);

            var selfParentLegit = selfParent == creatorLastKnown;

            if (!selfParentLegit)
            {
                throw new ApplicationException($"Self-parent not last known ev by creator");
            }
        }

        //Check if we know the OtherParent
        public void CheckOtherParent(Event ev)
        {
            var otherParent = ev.OtherParent();

            if (!string.IsNullOrEmpty(otherParent))
            {
                //Check if we have it
                var (_, ok) = Store.GetEvent(otherParent);

                if (!ok)
                {
                    //it might still be in the Root
                    var root = Store.GetRoot(ev.Creator);

                    if (root == null)
                    {
                        return;
                    }
                    if (root.X == ev.SelfParent() && root.Y == otherParent)
                    {
                        return;
                    }
                    var other = root.Others[ev.Hex()];

                    if (other == ev.OtherParent())
                    {
                        return;
                    }
                    throw new ApplicationException("Other-parent not known");
                }
            }
        }

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
            var fakeCreatorId = Participants[ev.Creator];

            var index = ev.Index();
            var hash = ev.Hex();

            for (var i = 0; i < ev.LastAncestors.Length; i++)
            {
                var ah = ev.LastAncestors[i]?.Hash;

                while (!string.IsNullOrEmpty(ah))
                {
                    var (a, success) = Store.GetEvent(ah);

                    if (a.FirstDescendants[fakeCreatorId].Index == int.MaxValue)
                    {
                        a.FirstDescendants[fakeCreatorId] = new EventCoordinates
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

        public void SetWireInfo(Event ev)
        {
            var selfParentIndex = -1;

            var otherParentCreatorId = -1;

            var otherParentIndex = -1;

            var (lf, isRoot) = Store.LastFrom(ev.Creator);
            //could be the first Event inserted for this creator. In this case, use Root
            if (isRoot && lf == ev.SelfParent())
            {
                var root = Store.GetRoot(ev.Creator);

                if (root == null)
                {
                    return;
                }
                selfParentIndex = root.Index;
            }
            else
            {
                var (selfParent, ok) = Store.GetEvent(ev.SelfParent());

                if (!ok)
                {
                    return;
                }
                selfParentIndex = selfParent.Index();
            }

            if (!string.IsNullOrEmpty(ev.OtherParent()))
            {
                var (otherParent, ok) = Store.GetEvent(ev.OtherParent());

                if (!ok)
                {
                    return;
                }
                otherParentCreatorId = Participants[otherParent.Creator];

                otherParentIndex = otherParent.Index();
            }

            ev.SetWireInfo(selfParentIndex,
                otherParentCreatorId,
                otherParentIndex,
                Participants[ev.Creator]);
        }

        public Event ReadWireInfo(WireEvent wev)
        {
            var selfParent = "";
            var otherParent = "";

            var creator = ReverseParticipants[wev.Body.CreatorId];
            var creatorBytes = creator.Substring(2).StringToBytes();

            if (wev.Body.SelfParentIndex >= 0)
            {
                selfParent = Store.ParticipantEvent(creator, wev.Body.SelfParentIndex);
            }

            if (wev.Body.OtherParentIndex >= 0)
            {
                var otherParentCreator = ReverseParticipants[wev.Body.OtherParentCreatorId];
                otherParent = Store.ParticipantEvent(otherParentCreator, wev.Body.OtherParentIndex);
            }

            var body = new EventBody
            {
                Transactions = wev.Body.Transactions,
                Parents = new[] {selfParent, otherParent},
                Creator = creatorBytes,
                Timestamp = wev.Body.Timestamp,
                Index = wev.Body.Index,
                SelfParentIndex = wev.Body.SelfParentIndex,
                OtherParentCreatorId = wev.Body.OtherParentCreatorId,
                OtherParentIndex = wev.Body.OtherParentIndex,
                CreatorId = wev.Body.CreatorId
            };

            var ev = new Event
            {
                Body = body,
                Signiture = wev.Signiture
            };

            return ev;
        }

        public void DivideRounds()
        {
            foreach (var hash in UndeterminedEvents)

            {
                var roundNumber = Round(hash);

                var witness = Witness(hash);

                var roundInfo = Store.GetRound(roundNumber);

                //If the RoundInfo is not found in the Store's Cache, then the Hashgraph
                //is not aware of it yet. We need to add the roundNumber to the queue of
                //undecided rounds so that it will be processed in the other consensus
                //methods

                //If the RoundInfo is actually taken from the Store's DB, then it still
                //has not been processed by the Hashgraph consensus methods (The 'queued'
                //field is not exported and therefore not persisted in the DB).
                //RoundInfos taken from the DB directly will always have this field set
                //to false
                if (!roundInfo.Queued)
                {
                    UndecidedRounds.Enqueue(roundNumber);

                    roundInfo.Queued = true;
                }

                roundInfo.AddEvent(hash, witness);

                Store.SetRound(roundNumber, roundInfo);

                return;
            }

            //decide if witnesses are famous
            void DecideFame()
            {
                var votes = new Dictionary<string, Dictionary<string, bool>>(); //[x][y]=>vote(x,y)

                var decidedRounds = new Dictionary<int, int>(); // [round number] => index in UndecidedRounds
                //
                //defer UpdateUndecidedRounds(decidedRounds)

                foreach (var i in UndecidedRounds)
                {
                    var roundInfo = Store.GetRound(i);

                    foreach (var x in roundInfo.Witnesses())
                    {
                        if (roundInfo.IsDecided(x))
                        {
                            continue;
                        }
                        //X:

                        for (var j = i + 1; j <= Store.LastRound(); j++)

                        {
                            foreach (var y in Store.RoundWitnesses(j))
                            {
                                var diff = j - i;

                                if (diff == 1)
                                {
                                    SetVote(votes, y, x, See(y, x));
                                }
                                else
                                {
                                    //count votes
                                    var ssWitnesses = new List<string>();
                                    foreach (var w in Store.RoundWitnesses(j - 1))
                                    {
                                        if (StronglySee(y, w))
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
                                    if ((float) diff % Participants.Count > 0)
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

                        X:
                        ;
                    }
                    //Update decidedRounds and LastConsensusRound if all witnesses have been decided
                    if (roundInfo.WitnessesDecided())
                    {
                        decidedRounds[i] = i;

                        if (LastConsensusRound == null || i > LastConsensusRound)
                        {
                            SetLastConsensusRound(i);
                        }
                    }

                    Store.SetRound(i, roundInfo);
                }
            }
        }

        //remove items from UndecidedRounds
        public void UpdateUndecidedRounds(Dictionary<int, int> decidedRounds)
        {
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

        public void SetLastConsensusRound(int i)
        {
            LastConsensusRound = i;
            LastCommitedRoundEvents = Store.RoundEvents(i - 1);
        }

        //assign round received and timestamp to all evs
        public void DecideRoundReceived()
        {
            foreach (var x in UndeterminedEvents)
            {
                var r = Round(x);

                for (var i = r + 1; i <= Store.LastRound(); i++)

                {
                    var tr = Store.GetRound(i);

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
                        if (See(w, x))
                        {
                            s.Add(w);
                        }
                    }

                    if (s.Count > fws.Length / 2)
                    {
                        var (ex, ok) = Store.GetEvent(x);

                        if (!ok)
                        {
                            throw new ApplicationException("Event not found");
                        }

                        ex.SetRoundReceived(i);

                        var t = new List<string>();
                        foreach (var a in s)
                        {
                            t.Add(OldestSelfAncestorToSee(a, x));
                        }

                        ex.ConsensusTimestamp = MedianTimestamp(t);

                        Store.SetEvent(ex);

                        break;
                    }
                }
            }
        }

        public void FindOrder()
        {
            DecideRoundReceived();

            var newConsensusEvents = new List<Event>();
            var newUndeterminedEvents = new List<string>();
            foreach (var x in UndeterminedEvents)
            {
                var(ex, ok) = Store.GetEvent(x);

                if (!ok)
                {
                    throw new ApplicationException("Event not found");
                }

                if (ex.RoundReceived != null)
                {
                    newConsensusEvents.Add(ex);
                }
                else
                {
                    newUndeterminedEvents.Add(x);
                }
            }

            UndeterminedEvents = newUndeterminedEvents;

            newConsensusEvents.Sort(new EventByConsensus());

            foreach (var e in newConsensusEvents)
            {
                Store.AddConsensusEvent(e.Hex());

                ConsensusTransactions += e.Transactions().Length;

                if (e.IsLoaded())
                {
                    PendingLoadedEvents--;
                }
            }

            if (CommitChannel != null && newConsensusEvents.Count > 0)
            {
                foreach (var nce in newConsensusEvents)
                {
                    CommitChannel.Send(nce);
                }
            }
        }

        public DateTime MedianTimestamp(List<string> evHashes)
        {
            var evs = new List<Event>();
            foreach (var x in evHashes)
            {
                var (ex, _) = Store.GetEvent(x);
                evs.Add(ex);
            }

            evs.Sort(new EventByTimeStamp());

            return evs[evs.Count / 2].Body.Timestamp;
        }

        public string[] ConsensusEvents()
        {
            return Store.ConsensusEvents();
        }

        //number of evs per participants
        public Dictionary<int, int> Known()
        {
            return Store.Known();
        }

        public void Reset(Dictionary<string, Root> roots)
        {
            Store.Reset(roots);

            UndeterminedEvents = new List<string>();
            UndecidedRounds = new Queue<int>();
            PendingLoadedEvents = 0;
            TopologicalIndex = 0;

            var cacheSize = Store.CacheSize();

            AncestorCache = new LruCache<string, bool>(cacheSize, null);
            SelfAncestorCache = new LruCache<string, bool>(cacheSize, null);
            OldestSelfAncestorCache = new LruCache<string, string>(cacheSize, null);
            StronglySeeCache = new LruCache<string, bool>(cacheSize, null);
            ParentRoundCache = new LruCache<string, ParentRoundInfo>(cacheSize, null);
            RoundCache = new LruCache<string, int>(cacheSize, null);
        }

        public Frame GetFrame()
        {
            var lastConsensusRoundIndex = 0;
            var lcr = LastConsensusRound;
            if (lcr != null)
            {
                lastConsensusRoundIndex = (int) lcr;
            }

            var lastConsensusRound = Store.GetRound(lastConsensusRoundIndex);

            if (lastConsensusRound == null)
            {
                return new Frame();
            }

            var witnessHashes = lastConsensusRound.Witnesses();

            var evs = new List<Event>();
            var roots = new Dictionary<string, Root>();

            foreach (var wh in witnessHashes)

            {
                var (w, ok) = Store.GetEvent(wh);

                if (!ok)
                {
                    return new Frame();
                }

                evs.Add(w);

                roots.Add(w.Creator, new Root
                {
                    X = w.SelfParent(),
                    Y = w.OtherParent(),
                    Index = w.Index() - 1,
                    Round = Round(w.SelfParent())
                });

                var participantEvents = Store.ParticipantEvents(w.Creator, w.Index());

                if (participantEvents == null)
                {
                    return new Frame();
                }

                foreach (var e in participantEvents)
                {
                    var (ev, okv) = Store.GetEvent(e);

                    if (!okv)
                    {
                        return new Frame();
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
                    var (last, isRoot) = Store.LastFrom(p.Key);

                    if (last == null)
                    {
                        return new Frame();
                    }
                    Root root;
                    if (isRoot)
                    {
                        root = Store.GetRoot(p.Key);

                        if (root == null)
                        {
                            return new Frame();
                        }
                    }
                    else
                    {
                        var (ev, ok) = Store.GetEvent(last);

                        if (!ok)
                        {
                            return new Frame();
                        }
                        evs.Add(ev);

                        root = new Root
                        {
                            X = ev.SelfParent(),
                            Y = ev.OtherParent(),
                            Index = ev.Index() - 1,
                            Round = Round(ev.SelfParent())
                        };
                    }
                    roots.Add(p.Key, root);
                }
            }

            evs.Sort(new EventByTopologicalOrder());

            //Some Events in the Frame might have other-parents that are outside of the
            //Frame (cf root.go ex 2)
            //When inserting these Events in a newly reset hashgraph, the CheckOtherParent
            //method would return an error because the other-parent would not be found.
            //So we make it possible to also look for other-parents in the creator's Root.

            var treated = new Dictionary<string, bool>();
            foreach (var ev in evs)
            {
                treated.Add(ev.Hex(), true);

                var otherParent = ev.OtherParent();

                if (!string.IsNullOrEmpty(otherParent))
                {
                    var ok = treated.TryGetValue(otherParent, out var opt);

                    if (!opt || !ok)
                    {
                        if (ev.SelfParent() != roots[ev.Creator].X)
                        {
                            roots[ev.Creator].Others[ev.Hex()] = otherParent;
                        }
                    }
                }
            }

            var frame = new Frame
            {
                Roots = roots,
                Events = evs.ToArray()
            };

            return frame;
        }

        //Bootstrap loads all Events from the Store's DB (if there is one) and feeds
        //them to the Hashgraph (in topological order) for consensus ordering. After this
        //method call, the Hashgraph should be in a state coeherent with the 'tip' of the
        //Hashgraph
        public void Bootstrap()
        {
            //if badgerStore, ok = Store.(*BadgerStore); ok {
            //    //Retreive the Events from the underlying DB. They come out in topological
            //    //order
            //    topologicalEvents, err = badgerStore.dbTopologicalEvents()

            //                if err != nil {
            //        return err

            //                }

            //    //Insert the Events in the Hashgraph
            //    for _, e = range topologicalEvents
            //        {
            //        if err = InsertEvent(e, true); err != nil
            //            {
            //            return err

            //                    }
            //    }

            //    //Compute the consensus order of Events
            //    if err = DivideRounds(); err != nil {
            //        return err

            //                }
            //    if err = DecideFame(); err != nil {
            //        return err

            //                }
            //    if err = FindOrder(); err != nil {
            //        return err

            //                }
            //}

            //return nil
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
                v.Add(y, vote);
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