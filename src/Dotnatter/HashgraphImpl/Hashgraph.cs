using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dotnatter.Common;
using Dotnatter.HashgraphImpl.Model;
using Dotnatter.HashgraphImpl.Stores;
using Dotnatter.NodeImpl;
using Dotnatter.Util;
using Nito.AsyncEx;
using Serilog;

namespace Dotnatter.HashgraphImpl
{
    public class Hashgraph
    {
        private readonly ILogger logger;
        public Dictionary<string, int> Participants { get; set; } //[public key] => id
        public Dictionary<int, string> ReverseParticipants { get; set; } //[id] => public key
        public IStore Store { get; set; } //store of Events and Rounds
        public List<string> UndeterminedEvents { get; set; } //[index] => hash
        public Queue<int> UndecidedRounds { get; set; } //queue of Rounds which have undecided witnesses
        public int? LastConsensusRound { get; set; } //index of last round where the fame of all witnesses has been decided
        public int LastCommitedRoundEvents { get; set; } //number of evs in round before LastConsensusRound
        public int ConsensusTransactions { get; set; } //number of consensus transactions
        public int PendingLoadedEvents { get; set; } //number of loaded evs that are not yet committed
        public AsyncProducerConsumerQueue<Event[]> CommitChannel { get; set; } //channel for committing evs
        public int TopologicalIndex { get; set; } //counter used to order evs in topological order
        public int SuperMajority { get; set; }

        public LruCache<string, bool> AncestorCache { get; set; }
        public LruCache<string, bool> SelfAncestorCache { get; set; }
        public LruCache<string, string> OldestSelfAncestorCache { get; set; }
        public LruCache<string, bool> StronglySeeCache { get; set; }
        public LruCache<string, ParentRoundInfo> ParentRoundCache { get; set; }
        public LruCache<string, int> RoundCache { get; set; }

        public Hashgraph(Dictionary<string, int> participants, IStore store, AsyncProducerConsumerQueue<Event[]> commitCh, ILogger logger)
        {
            this.logger = logger.AddNamedContext("HashGraph");
            var reverseParticipants = participants.ToDictionary(p => p.Value, p => p.Key);
            var cacheSize = store.CacheSize();

            Participants = participants;
            ReverseParticipants = reverseParticipants;
            Store = store;
            CommitChannel = commitCh;
            AncestorCache = new LruCache<string, bool>(cacheSize, null, logger,"AncestorCache");
            SelfAncestorCache = new LruCache<string, bool>(cacheSize, null, logger,"SelfAncestorCache");
            OldestSelfAncestorCache = new LruCache<string, string>(cacheSize, null, logger,"OldestAncestorCache");
            StronglySeeCache = new LruCache<string, bool>(cacheSize, null, logger,"StronglySeeCache");
            ParentRoundCache = new LruCache<string, ParentRoundInfo>(cacheSize, null, logger,"ParentRoundCache");
            RoundCache = new LruCache<string, int>(cacheSize, null, logger,"RoundCache");
            UndeterminedEvents = new List<string>();
            SuperMajority = 2 * participants.Count / 3 + 1;
            UndecidedRounds = new Queue<int>(); //initialize
        }

        //true if y is an ancestor of x
        public async Task<bool> Ancestor(string x, string y)
        {
            var (c, ok) = AncestorCache.Get(Key.New(x, y));

            if (ok)
            {
                return c;
            }

            var a = await AncestorInternal(x, y);
            AncestorCache.Add(Key.New(x, y), a);
            return a;
        }

        private async Task<bool> AncestorInternal(string x, string y)
        {
            if (x == y)
            {
                return true;
            }

            var (ex, errx) = await Store.GetEvent(x);

            if (errx != null)
            {
                return false;
            }

            var (ey, erry) = await Store.GetEvent(y);

            if (erry != null)
            {
                return false;
            }

            var eyCreator = Participants[ey.Creator()];
            var lastAncestorKnownFromYCreator = ex.GetLastAncestors()[eyCreator].Index;

            return lastAncestorKnownFromYCreator >= ey.Index();
        }

        //true if y is a self-ancestor of x
        public async Task<bool> SelfAncestor(string x, string y)
        {
            var (c, ok) = SelfAncestorCache.Get(Key.New(x, y));

            if (ok)
            {
                return c;
            }

            var a =await  SelfAncestorInternal(x, y);
            SelfAncestorCache.Add(Key.New(x, y), a);
            return a;
        }

        private async Task<bool> SelfAncestorInternal(string x, string y)
        {
            if (x == y)
            {
                return true;
            }

            var (ex, errx) = await Store.GetEvent(x);

            if (errx != null)
            {
                return false;
            }

            var exCreator = Participants[ex.Creator()];

            var (ey, erry) =await  Store.GetEvent(y);
            if (erry != null)
            {
                return false;
            }

            var eyCreator = Participants[ey.Creator()];

            return exCreator == eyCreator && ex.Index() >= ey.Index();
        }

        //true if x sees y
        public Task<bool> See(string x, string y)
        {
            return Ancestor(x, y);
            //it is not necessary to detect forks because we assume that with our
            //implementations, no two evs can be added by the same creator at the
            //same height (cf InsertEvent)
        }

        //oldest self-ancestor of x to see y
        public async Task<string> OldestSelfAncestorToSee(string x, string y)
        {
            var (c, ok) = OldestSelfAncestorCache.Get(Key.New(x, y));

            if (ok)
            {
                return c;
            }

            var res =await OldestSelfAncestorToSeeInternal(x, y);
            OldestSelfAncestorCache.Add(Key.New(x, y), res);
            return res;
        }

        private async Task<string> OldestSelfAncestorToSeeInternal(string x, string y)
        {
            var (ex, errx) = await Store.GetEvent(x);

            if (errx != null)
            {
                return "";
            }

            var (ey, erry) = await Store.GetEvent(y);

            if (erry != null)
            {
                return "";
            }

            var a = ey.GetFirstDescendants()[Participants[ex.Creator()]];

            if (a.Index <= ex.Index())
            {
                return a.Hash;
            }

            return "";
        }

        //true if x strongly sees y
        public async Task<bool> StronglySee(string x, string y)
        {
            var (c, ok) = StronglySeeCache.Get(Key.New(x, y));
            if (ok)
            {
                return c;
            }

            var ss = await StronglySeeInternal(x, y);
            StronglySeeCache.Add(Key.New(x, y), ss);
            return ss;
        }

        public async Task<bool> StronglySeeInternal(string x, string y)
        {
            var (ex, errx) = await Store.GetEvent(x);

            if (errx != null)
            {
                return false;
            }

            var (ey, erry) = await Store.GetEvent(y);

            if (erry != null)
            {
                return false;
            }

            var c = 0;

            for (var i = 0; i < ex.GetLastAncestors().Length; i++)
            {
                if (ex.GetLastAncestors()[i].Index >= ey.GetFirstDescendants()[i].Index)
                {
                    c++;
                }
            }

            return c >= SuperMajority;
        }

        //PRI.round: max of parent rounds
        //PRI.isRoot: true if round is taken from a Root
        public async Task<ParentRoundInfo> ParentRound(string x)
        {
            var (c, ok) = ParentRoundCache.Get(x);
            if (ok)
            {
                return c;
            }

            var pr = await ParentRoundInternal(x);
            ParentRoundCache.Add(x, pr);

            return pr;
        }

        public async Task<ParentRoundInfo> ParentRoundInternal(string x)
        {
            var res = new ParentRoundInfo();

            var (ex, errx) =await Store.GetEvent(x);

            if (errx != null)
            {
                return res;
            }

            //We are going to need the Root later
            var (root, errr) =await Store.GetRoot(ex.Creator());

            if (errr != null)
            {
                return res;
            }

            var spRound = -1;

            var spRoot = false;
            //If it is the creator's first Event, use the corresponding Root
            if (ex.SelfParent == root.X)
            {
                spRound = root.Round;
                spRoot = true;
            }
            else
            {
                spRound = await Round(ex.SelfParent);

                spRoot = false;
            }

            var opRound = -1;

            var opRoot = false;

            var (_, erro) = await Store.GetEvent(ex.OtherParent);
            if (erro != null)
            {
                //if we known the other-parent, fetch its Round directly
                opRound = await Round(ex.OtherParent);
            }
            else if (ex.OtherParent == root.Y)
            {
                //we do not know the other-parent but it is referenced in Root.Y
                opRound = root.Round;
                opRoot = true;
            }
            else if (root.Others.TryGetValue(x, out var other))
            {
                if (other == ex.OtherParent)
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
        public async Task<bool> Witness(string x)
        {
            var (ex, errx) = await Store.GetEvent(x);

            if (errx != null)
            {
                return false;
            }

            var (root, err) = await Store.GetRoot(ex.Creator());

            if (err != null)
            {
                return false;
            }

            //If it is the creator's first Event, return true
            if (ex.SelfParent == root.X && ex.OtherParent == root.Y)
            {
                return true;
            }

            return await  Round(x) > await  Round(ex.SelfParent);
        }

        //true if round of x should be incremented
        public async Task<bool> RoundInc(string x)
        {
            var parentRound = await ParentRound(x);

            //If parent-round was obtained from a Root, then x is the Event that sits
            //right on top of the Root. RoundInc is true.
            if (parentRound.IsRoot)
            {
                return true;
            }

            //If parent-round was obtained from a regulare Event, then we need to check
            //if x strongly-sees a strong majority of withnesses from parent-round.
            var c = 0;

            foreach (var w in await Store.RoundWitnesses(parentRound.Round))
            {
                if (await StronglySee(x, w))
                {
                    c++;
                }
            }

            return c >= SuperMajority;
        }

        public async Task<int> RoundReceived(string x)
        {
            var (ex, errx) = await Store.GetEvent(x);

            if (errx != null)
            {
                return -1;
            }

            return ex.GetRoundReceived() ?? -1;
        }

        public async Task<int> Round(string x)
        {
            var (c, ok) = RoundCache.Get(x);
            if (ok)
            {
                return c;
            }

            var r = await RoundInternal(x);
            RoundCache.Add(x, r);

            return r;
        }

        private async Task<int> RoundInternal(string x)
        {
            var round = (await ParentRound(x)).Round;

            var inc = await RoundInc(x);

            if (inc)
            {
                round++;
            }

            return round;
        }

        //round(x) - round(y)
        public async Task<(int d, Exception err)> RoundDiff(string x, string y)
        {
            var xRound =await  Round(x);

            if (xRound < 0)
            {
                return (Int32.MinValue, new HashgraphError($"ev {x} has negative round"));
            }

            var yRound = await  Round(y);

            if (yRound < 0)
            {
                return (Int32.MinValue, new HashgraphError($"ev {y} has negative round"));
            }

            return (xRound - yRound,null);
        }

        public async Task< Exception> InsertEvent(Event ev, bool setWireInfo)
        {

            //verify signature
            var (ok, err) = ev.Verify();

            if (!ok)
            { 
            if (err != null)
            {
                return err;
            }

            return new HashgraphError($"Invalid signature");
            }

            err= CheckSelfParent(ev);
            if (err != null)
            {
                return new Exception($"CheckSelfParent: {err.Message}",err);
            }

            err = await CheckOtherParent(ev);
            if (err != null)
            {
                return new Exception($"CheckOtherParent: {err.Message}", err);
            }


            ev.SetTopologicalIndex(TopologicalIndex);
            TopologicalIndex++;

            if (setWireInfo)
            {
                err =await  SetWireInfo(ev);
                if (err != null)
                {
                    return new Exception($"SetWireInfo: {err.Message}", err);
                }

            }

            err= await InitEventCoordinates(ev);
            if (err != null)
            {
                return new Exception($"InitEventCoordinates: {err.Message}", err);
            }


            err=await Store.SetEvent(ev);
            if (err != null)
            {
                return new Exception($"SetEvent: {err.Message}", err);
            }


            err=await UpdateAncestorFirstDescendant(ev);
            if (err != null)
            {
                return new Exception($"UpdateAncestorFirstDescendant: {err.Message}", err);
            }


            UndeterminedEvents.Add(ev.Hex());

            if (ev.IsLoaded())
            {
                PendingLoadedEvents++;
            }

            return null;
        }

        //Check the SelfParent is the Creator's last known Event
        public Exception CheckSelfParent(Event ev)
        {
            var selfParent = ev.SelfParent;
            var creator = ev.Creator();

            var (creatorLastKnown, _, err) = Store.LastFrom(creator);
            if (err != null)
            {
                return err;
            }

            var selfParentLegit = selfParent == creatorLastKnown;

            if (!selfParentLegit)
            {
                return new HashgraphError($"Self-parent not last known ev by creator");
            }

            return null;
        }

        //Check if we know the OtherParent
        public async Task<Exception> CheckOtherParent(Event ev)
        {
            var otherParent = ev.OtherParent;

            if (!string.IsNullOrEmpty(otherParent))
            {
                //Check if we have it
                var (_, err) =await  Store.GetEvent(otherParent);

                if (err != null)
                {
                    //it might still be in the Root
                    var (root, errr) =await  Store.GetRoot(ev.Creator());

                    if (errr != null)
                    {
                        return errr;
                    }

                    if (root.X == ev.SelfParent && root.Y == otherParent)
                    {
                        return null;
                    }

                    var ok = root.Others.TryGetValue(ev.Hex(), out var other);

                    if (ok && other == ev.OtherParent)
                    {
                        return null;
                    }

                    return new HashgraphError("Other-parent not known");
                }
            }

            return null;
        }

        ////initialize arrays of last ancestors and first descendants
        public async Task<HashgraphError> InitEventCoordinates(Event ev)
        {
            var members = Participants.Count;

            ev.SetFirstDescendants(new EventCoordinates[members]);

            for (var fakeId = 0; fakeId < members; fakeId++)
            {
                ev.GetFirstDescendants()[fakeId] = new EventCoordinates
                {
                    Index = int.MaxValue
                };
            }

            ev.SetLastAncestors(new EventCoordinates[members]);

            var (selfParent, selfParentError) =await  Store.GetEvent(ev.SelfParent);
            var (otherParent, otherParentError) = await Store.GetEvent(ev.OtherParent);

            if (selfParentError != null && otherParentError != null)
            {
                for (var fakeId = 0; fakeId < members; fakeId++)
                {
                    ev.GetLastAncestors()[fakeId] = new EventCoordinates
                    {
                        Index = -1
                    };
                }
            }
            else if (selfParentError != null)
            {
                Array.Copy(otherParent.GetLastAncestors(), 0, ev.GetLastAncestors(), 0, members);
            }
            else if (otherParentError != null)
            {
                Array.Copy(selfParent.GetLastAncestors(), 0, ev.GetLastAncestors(), 0, members);
            }
            else
            {
                var selfParentLastAncestors = selfParent.GetLastAncestors();

                var otherParentLastAncestors = otherParent.GetLastAncestors();

                Array.Copy(selfParentLastAncestors, 0, ev.GetLastAncestors(), 0, members);

                for (var i = 0; i < members; i++)
                {
                    if (ev.GetLastAncestors()[i].Index < otherParentLastAncestors[i].Index)
                    {
                        {
                            ev.GetLastAncestors()[i] = new EventCoordinates();
                            ev.GetLastAncestors()[i].Index = otherParentLastAncestors[i].Index;
                            ev.GetLastAncestors()[i].Hash = otherParentLastAncestors[i].Hash;
                        }
                    }
                }
            }

            var index = ev.Index();

            var creator = ev.Creator();

            var ok = Participants.TryGetValue(creator, out var fakeCreatorId);

            if (!ok)
            {
                return new HashgraphError("Could not find fake creator id");
            }

            var hash = ev.Hex();

            ev.GetFirstDescendants()[fakeCreatorId] = new EventCoordinates { Index = index, Hash = hash };
            ev.GetLastAncestors()[fakeCreatorId] = new EventCoordinates { Index = index, Hash = hash };

            return null;
        }

        //update first decendant of each last ancestor to point to ev
        public async Task<Exception> UpdateAncestorFirstDescendant(Event ev)
        {
            var ok = Participants.TryGetValue(ev.Creator(), out int fakeCreatorId);

            if (!ok)
            {
                return new HashgraphError($"Could not find fake creator id {ev.Creator()}");
            }

            var index = ev.Index();
            var hash = ev.Hex();

            for (var i = 0; i < ev.GetLastAncestors().Length; i++)
            {
                var ah = ev.GetLastAncestors()[i].Hash;

                while (!string.IsNullOrEmpty(ah))
                {
                    var (a, err) = await Store.GetEvent(ah);

                    if (err != null)
                    {
                        break;
                    }

                    if (a.GetFirstDescendants()[fakeCreatorId].Index == int.MaxValue)
                    {
                        a.GetFirstDescendants()[fakeCreatorId] = new EventCoordinates
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

        public async Task<Exception> SetWireInfo(Event ev)
        {
            var selfParentIndex = -1;

            var otherParentCreatorId = -1;

            var otherParentIndex = -1;

            //could be the first Event inserted for this creator. In this case, use Root
            var (lf, isRoot, _) = Store.LastFrom(ev.Creator());

            if (isRoot && lf == ev.SelfParent)
            {
                var (root, err) = await Store.GetRoot(ev.Creator());

                if (err != null)
                {
                    return err;
                }

                selfParentIndex = root.Index;
            }
            else
            {
                var (selfParent, err) =await  Store.GetEvent(ev.SelfParent);

                if (err != null)
                {
                    return err;
                }

                selfParentIndex = selfParent.Index();
            }

            if (!string.IsNullOrEmpty(ev.OtherParent))
            {
                var (otherParent, err) =await  Store.GetEvent(ev.OtherParent);

                if (err != null)
                {
                    return err;
                }

                otherParentCreatorId = Participants[otherParent.Creator()];
                otherParentIndex = otherParent.Index();
            }

            ev.SetWireInfo(selfParentIndex,
                otherParentCreatorId,
                otherParentIndex,
                Participants[ev.Creator()]);

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
                (selfParent, err) =await  Store.ParticipantEvent(creator, wev.Body.SelfParentIndex);
                if (err != null)
                {
                    return (null, err);
                }
            }

            if (wev.Body.OtherParentIndex >= 0)
            {
                var otherParentCreator = ReverseParticipants[wev.Body.OtherParentCreatorId];
                (otherParent, err) =await  Store.ParticipantEvent(otherParentCreator, wev.Body.OtherParentIndex);
                if (err != null)
                {
                    return (null, err);
                }
            }

            var body = new EventBody
            {
                Transactions = wev.Body.Transactions,
                Parents = new[] { selfParent, otherParent },
                Creator = creatorBytes,
                Timestamp = wev.Body.Timestamp,
                Index = wev.Body.Index,
            
            };


            body.SetSelfParentIndex (wev.Body.SelfParentIndex);
            body.SetOtherParentCreatorId(wev.Body.OtherParentCreatorId);
            body.SetOtherParentIndex(wev.Body.OtherParentIndex);
            body.SetCreatorId(wev.Body.CreatorId);

            var ev = new Event
            {
                Body = body,
                Signiture = wev.Signiture,
            };
            return (ev, null);
        }

        public async Task<Exception> DivideRounds()
        {
            foreach (var hash in UndeterminedEvents)

            {
                var roundNumber = await Round(hash);

                var witness = await  Witness(hash);

                var (roundInfo, err) = await Store.GetRound(roundNumber);

                //If the RoundInfo is not found in the Store's Cache, then the Hashgraph
                //is not aware of it yet. We need to add the roundNumber to the queue of
                //undecided rounds so that it will be processed in the other consensus
                //methods
                if (err != null && err?.StoreErrorType != StoreErrorType.KeyNotFound)
                {
                    return err;
                }

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

                err = await Store.SetRound(roundNumber, roundInfo);

                if (err != null)
                {
                    return err;
                }
            }

            return null;
        }

        //decide if witnesses are famous
        public async Task<Exception> DecideFame()
        {
            var votes = new Dictionary<string, Dictionary<string, bool>>(); //[x][y]=>vote(x,y)

            var decidedRounds = new Dictionary<int, int>(); // [round number] => index in UndecidedRounds


            try
            {

                int pos = 0;

                foreach (var i in UndecidedRounds)
                {
                    var (roundInfo, err) =await  Store.GetRound(i);

                    if (err != null)
                    {
                        return err;
                    }

                    foreach (var x in roundInfo.Witnesses())
                    {
                        if (roundInfo.IsDecided(x))
                        {

                            continue;
                        }

                        //X:

                        for (var j = i + 1; j <= Store.LastRound(); j++)

                        {
                            foreach (var y in await Store.RoundWitnesses(j))
                            {
                                var diff = j - i;

                                if (diff == 1)
                                {
                                    SetVote(votes, y, x, await See(y, x));
                                }
                                else
                                {
                                    //count votes
                                    var ssWitnesses = new List<string>();
                                    foreach (var w in await Store.RoundWitnesses(j - 1))
                                    {
                                        if (await StronglySee(y, w))
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

                        X:;
                    }

                    //Update decidedRounds and LastConsensusRound if all witnesses have been decided
                    if (roundInfo.WitnessesDecided())
                    {
                        decidedRounds[i] = i;

                        if (LastConsensusRound == null || i > LastConsensusRound)
                        {
                            await SetLastConsensusRound(i);
                        }
                    }

                    err = await Store.SetRound(i, roundInfo);

                    if (err != null)
                    {
                        return err;
                    }

                    pos++;
                }

                return null;

            }
            finally
            {
                UpdateUndecidedRounds(decidedRounds);
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
                var r =await  Round(x);

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
                        var (ex, erre) =await Store.GetEvent(x);

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
            foreach (var x in UndeterminedEvents)
            {
                var (ex, err) = await Store.GetEvent(x);

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
                    CommitChannel.Enqueue(new Event[]{nce});
                }
            }

            return null;
        }
        public async Task<DateTime> MedianTimestamp(List<string> evHashes)
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
        public Task<Dictionary<int, int>> Known()
        {
            return Store.Known();
        }
        public Exception Reset(Dictionary<string, Root> roots)
        {
            Store.Reset(roots);
            UndeterminedEvents = new List<string>();
            UndecidedRounds = new Queue<int>();
            PendingLoadedEvents = 0;
            TopologicalIndex = 0;
            var cacheSize = Store.CacheSize();
            AncestorCache = new LruCache<string, bool>(cacheSize, null, logger,"AncestorCache");
            SelfAncestorCache = new LruCache<string, bool>(cacheSize, null, logger,"SelfAncestorCache");
            OldestSelfAncestorCache = new LruCache<string, string>(cacheSize, null, logger,"OldestAncestorCache");
            StronglySeeCache = new LruCache<string, bool>(cacheSize, null, logger,"StronglySeeCache");
            ParentRoundCache = new LruCache<string, ParentRoundInfo>(cacheSize, null, logger,"ParentRoundCache");
            RoundCache = new LruCache<string, int>(cacheSize, null, logger,"RoundCache");

            return null;
        }
        public async Task<(Frame frame, Exception err)> GetFrame()
        {
            Exception err;

            var lastConsensusRoundIndex = 0;
            var lcr = LastConsensusRound;
            if (lcr != null)
            {
                lastConsensusRoundIndex = (int)lcr;
            }

            RoundInfo lastConsensusRound;
            (lastConsensusRound,err) =await  Store.GetRound(lastConsensusRoundIndex);
            if (err!=null)
            {
                return (new Frame(),err);
            }
            var witnessHashes = lastConsensusRound.Witnesses();
            var evs = new List<Event>();
            var roots = new Dictionary<string, Root>();
            foreach (var wh in witnessHashes)
            {
                Event w;
                (w, err) =await  Store.GetEvent(wh);
                if (err!=null)
                {
                    return (new Frame(),err);
                }
                evs.Add(w);
                roots.Add(w.Creator(), new Root
                {
                    X = w.SelfParent,
                    Y = w.OtherParent,
                    Index = w.Index() - 1,
                    Round = await Round(w.SelfParent), Others = new Dictionary<string, string>()
                });
                string[] participantEvents;
                (participantEvents,err) =await  Store.ParticipantEvents(w.Creator(), w.Index());
                if (err != null)
                {
                    return (new Frame(),err);
                }
                foreach (var e in participantEvents)
                {
                    var (ev, errev) =await  Store.GetEvent(e);
                    if (errev!=null)
                    {
                        return (new Frame(),errev);
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
                    var (last, isRoot,errp) = Store.LastFrom(p.Key);
                    if (errp != null)
                    {
                        return( new Frame(), errp);
                    }
                    Root root;
                    if (isRoot)
                    {
                        (root,err) =await  Store.GetRoot(p.Key);
                        if (root == null)
                        {
                            return (new Frame(),err);
                        }
                    }
                    else
                    {
                        Event ev;
                        (ev, err) =await  Store.GetEvent(last);
                        if (err!=null)
                        {
                            return (new Frame(),err);
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
            return (frame,null);
        }

        //Bootstrap loads all Events from the Store's DB (if there is one) and feeds
        //them to the Hashgraph (in topological order) for consensus ordering. After this
        //method call, the Hashgraph should be in a state coeherent with the 'tip' of the
        //Hashgraph
        public async Task< Exception> Bootstrap()
        {
            Exception err;
            if (Store is LocalDbStore)
            
            {
                //Retreive the Events from the underlying DB. They come out in topological
                //order
                Event[] topologicalEvents;
                (topologicalEvents, err) = await ((LocalDbStore)Store).DbTopologicalEvents();

                            if (err != null)
                            {
                                return err;

                            }

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
            }

            return null;
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
                v[y]=vote;
                return;
            }
            votes.Add(x, new Dictionary<string, bool> { { y, vote } });
        }
    }
    public class Frame
    {
        public Dictionary<string, Root> Roots { get; set; }
        public Event[] Events { get; set; }
    }
}