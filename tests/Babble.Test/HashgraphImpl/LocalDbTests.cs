using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Babble.Core.Common;
using Babble.Core.Crypto;
using Babble.Core.HashgraphImpl.Model;
using Babble.Core.HashgraphImpl.Stores;
using Babble.Core.PeersImpl;
using Babble.Core.Util;
using Babble.Test.Helpers;
using Serilog;
using Xunit;
using Xunit.Abstractions;

namespace Babble.Test.HashgraphImpl
{
    public class LocalDbTests
    {
        private readonly ILogger logger;
        private ITestOutputHelper output;
        private string GetPath() => $"localdb/{Guid.NewGuid():D}";

        public LocalDbTests(ITestOutputHelper output)
        {
            this.output = output;
            logger = output.SetupLogging().ForContext("SourceContext", "LocalDbTests");

        }

        private static async Task<(LocalDbStore store, Pub[] pubs)> InitBadgerStore(int cacheSize, string dbPath, ILogger logger)
        {
            var n = 3;
            var participantPubs = new List<Pub>();
            var participants = Peers.NewPeers();
            for (var i = 0; i < n; i++)
            {
                var key = CryptoUtils.GenerateEcdsaKey();
                var pubKey = CryptoUtils.FromEcdsaPub(key);
                var peer = Peer.New(pubKey.ToHex(), "");
                await participants.AddPeer(peer);
                participantPubs.Add(new Pub {Id = peer.ID, PrivKey = key, PubKey = pubKey, Hex = pubKey.ToHex()});
     
            }

            var (storeBase, err) = await LocalDbStore.New(participants, cacheSize, dbPath, logger);
            Assert.Null(err);

            var store = storeBase as LocalDbStore;
            Assert.NotNull(store);

            return (store, participantPubs.ToArray());
        }

        //func removeBadgerStore(store *BadgerStore, t *testing.T) {
        //    if err = store.Close(); err != nil {
        //        t.Fatal(err)
        //    }
        //    if err = os.RemoveAll(store.path); err != nil {
        //        t.Fatal(err)
        //    }
        //}

        private static async Task<LocalDbStore> CreateTestDb(string dir, ILogger logger)

        {
            var participants =Peers.NewPeersFromSlice(new Peer[]{
                Peer.New(
                    "0xaa", ""
                ),
                Peer.New(
                    "0xbb", ""
                ),          Peer.New(
                    "0xcc", ""
                )
            });

            var cacheSize = 100;

            var (storeBase, err) = await LocalDbStore.New(participants, cacheSize, dir, logger);
            Assert.Null(err);

            var store = storeBase as LocalDbStore;
            Assert.NotNull(store);

            return store;
        }

        [Fact]
        public async Task TestNewStore()
        {
            var dbPath = GetPath();
            logger.Information(Directory.GetCurrentDirectory());

            var store = await CreateTestDb(dbPath, logger);

            Assert.NotNull(store);
            Assert.Equal(store.Path, dbPath);

            StoreError err;

            //check roots
            using (var tx = store.BeginTx())
            {
  
                var inmemRoots = store.InMemStore.Roots;
                foreach (var pr in inmemRoots)

                {
                    var participant = pr.Key;
                    var root = pr.Value;

                    Root dbRoot;
                    (dbRoot, err) = await store.DbGetRoot(participant);
                    Assert.Null(err);

                    dbRoot.ShouldCompareTo(root);
                }
            }


            err = store.Close();
            Assert.Null(err);
        }

        [Fact]
        public async Task TestLoadStore()
        {
            var dbPath = GetPath();
            //Create the test db
            var tempStore = await CreateTestDb(dbPath, logger);

            Assert.NotNull(tempStore);
            tempStore.Close();

            var cacheSize = 100;

            var (badgerStoreres, err) = await LocalDbStore.Load(cacheSize, tempStore.Path, logger);
            var badgerStore = badgerStoreres as LocalDbStore;

            Assert.NotNull(badgerStore);
            Assert.Null(err);

            Peers dbParticipants;
            using (var tx = badgerStore.BeginTx())
            {
                (dbParticipants, err) = await badgerStore.DbGetParticipants();
            }

            Assert.Null(err);
            Assert.Equal(badgerStore.Participants().participants.Len(), dbParticipants.Len());

            foreach (var p in dbParticipants.ByPubKey)
            {
                var dbP = p.Key;
                var dbId = p.Value;

                var ok = badgerStore.Participants().participants.ByPubKey.TryGetValue(dbP, out var id);
                Assert.True(ok);
                // Assert.Equal(id, dbId);
            }
        }

//        //++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
//        //Call DB methods directly

//        [Fact]
//        public async Task TestDbEventMethods()
//        {
//            var dbPath = GetPath();
//            Exception err;
//            var cacheSize = 0;
//            var testSize = 100;
//            var (store, participants) = await InitBadgerStore(cacheSize, dbPath, logger);

//            //inset events in db directly
//            var events = new Dictionary<string, Event[]>();

//            var topologicalIndex = 0;
//            var topologicalEvents = new List<Event>();
//            foreach (var p in participants)
//            {
//                using (var tx = store.BeginTx())
//                {
//                    var items = new List<Event>();
//                    for (var k = 0; k < testSize; k++)
//                    {
//                        var ev = new Event(new[] {$"{p.Hex.Take(5)}_{k}".StringToBytes()}, new[] {new BlockSignature {Validator = "validator".StringToBytes(), Index = 0, Signature = "r|s".StringToBytes()}},
//                            new[] {"", ""},
//                            p.PubKey,
//                            k);

//                        ev.Sign(p.PrivKey);
//                        ev.SetTopologicalIndex(topologicalIndex);
//                        topologicalIndex++;
//                        topologicalEvents.Add(ev);

//                        items.Add(ev);

//                        err = await store.DbSetEvents(new[] {ev});
//                        Assert.Null(err);
//                    }

//                    events[p.Hex] = items.ToArray();

//                    tx.Commit();
//                }
//            }

//            bool ver;

//            using (var tx = store.BeginTx())
//            {
//                //check events where correctly inserted and can be retrieved
//                foreach (var evsd in events)
//                {
//                    var p = evsd.Key;
//                    var evs = evsd.Value;

//                    foreach (var ev in evs)
//                    {
//                        logger.Debug($"Testing events[{p}][{ev.Hex()}]");

//                        Event rev;
//                        (rev, err) = await store.DbGetEvent(ev.Hex());
//                        Assert.Null(err);

//                        ev.Body.ShouldCompareTo(rev.Body);

//                        rev.ShouldCompareTo(ev);

//                        Assert.Equal(ev.Signiture, rev.Signiture);

//                        (ver, err) = rev.Verify();
//                        Assert.Null(err);
//                        Assert.True(ver);
//                    }
//                }

//                //check topological order of events was correctly created
//                Event[] dbTopologicalEvents;
//                (dbTopologicalEvents, err) = await store.DbTopologicalEvents();
//                Assert.Null(err);

//                Assert.Equal(topologicalEvents.Count, dbTopologicalEvents.Length);

//                int i = 0;
//                foreach (var dte in dbTopologicalEvents)

//                {
//                    var te = topologicalEvents[i];

//                    Assert.Equal(te.Hex(), dte.Hex());

//                    dte.Body.ShouldCompareTo(te.Body);

//                    Assert.Equal(te.Signiture, dte.Signiture);

//                    (ver, err) = dte.Verify();
//                    Assert.Null(err);
//                    Assert.True(ver);

//                    i++;
//                }

//                //check that participant events where correctly added
//                var skipIndex = -1; //do not skip any indexes
//                foreach (var p in participants)
//                {
//                    string[] pEvents;
//                    (pEvents, err) = await store.DbParticipantEvents(p.Hex, skipIndex);
//                    Assert.Null(err);

//                    Assert.Equal(testSize, pEvents.Length);

//                    var expectedEvents = events[p.Hex].Skip(skipIndex + 1);

//                    int k = 0;
//                    foreach (var e in expectedEvents)
//                    {
//                        Assert.Equal(e.Hex(), pEvents[k]);

//                        k++;
//                    }
//                }
//            }
//        }

//        [Fact]
//        public async Task TestDbRoundMethods()
//        {
//            var dbPath = GetPath();
//            var cacheSize = 0;
//            var (store, participants) = await InitBadgerStore(cacheSize, dbPath, logger);

//            var round = new RoundInfo();
//            var events = new Dictionary<string, Event>();
//            foreach (var p in participants)
//            {
//                var ev = new Event(new[] {new byte[] { }},new BlockSignature[] {} ,
//                    new[] {"", ""},
//                    p.PubKey,
//                    0);

//                events[p.Hex] = ev;
//                round.AddEvent(ev.Hex(), true);
//            }

//            StoreError err;
//            using (var tx = store.BeginTx())
//            {
//                err = await store.DbSetRound(0, round);
//                Assert.Null(err);
//                tx.Commit();

//                RoundInfo storedRound;
//                (storedRound, err) = await store.DbGetRound(0);

//                Assert.Null(err);

//                storedRound.ShouldCompareTo(round);

//                var witnesses = await store.RoundWitnesses(0);
//                var expectedWitnesses = round.Witnesses();

//                Assert.Equal(expectedWitnesses.Length, witnesses.Length);

//                foreach (var w in expectedWitnesses)
//                {
//                    Assert.Contains(w, witnesses);
//                }
//            }
//        }

//        [Fact]
//        public async Task TestDbParticipantMethods()
//        {
//            var dbPath = GetPath();
//            var cacheSize = 0;
//            var (store, _ ) = await InitBadgerStore(cacheSize, dbPath, logger);

//            var (participants, err) = store.Participants();
//            Assert.Null(err);

//            using (var tx = store.BeginTx())
//            {
//                err = await store.DbSetParticipants(participants);
//                Assert.Null(err);
//                tx.Commit();

//                Dictionary<string, int> participantsFromDb;
//                (participantsFromDb, err) = await store.DbGetParticipants();

//                foreach (var pp in participantsFromDb)
//                {
//                    logger.Debug(pp.Key);
//                }

//                Assert.Null(err);

//                foreach (var pp in participants)
//                {
//                    logger.Debug(pp.Key);

//                    var p = pp.Key;
//                    var id = pp.Value;
//                    var ok = participantsFromDb.TryGetValue(p, out var dbId);
//                    Assert.True(ok);
//                    Assert.Equal(id, dbId);
//                }
//            }
//        }

//        [Fact]

//        public async Task TestDbBlockMethods()
//        {
//            var dbPath = GetPath();
//            var cacheSize = 0;
//            var (store, participants) = await InitBadgerStore(cacheSize, dbPath,logger);
     
//            var index = 0;
//            var roundReceived = 5;
//            var transactions = new[]
//            {
//                "tx1".StringToBytes(),
//                "tx2".StringToBytes(),
//                "tx3".StringToBytes(),
//                "tx4".StringToBytes(),
//                "tx5".StringToBytes()
//            };

//            var block = new Block(index, roundReceived, transactions);

//            Exception err;
//            BlockSignature sig1;

//            (sig1, err) = block.Sign(participants[0].PrivKey);
//            Assert.Null(err);

//            BlockSignature sig2;
//            (sig2, err) = block.Sign(participants[1].PrivKey);
//            Assert.Null(err);

//            block.SetSignature(sig1);
//            block.SetSignature(sig2);



//            using (var tx = store.BeginTx())
//            {
//                // Store Block


//                err = await store.DbSetBlock(block);
//                Assert.Null(err);

//                Block storedBlock;
//                (storedBlock, err) = await store.DbGetBlock(index);

//                Assert.Null(err);

//                storedBlock.ShouldCompareTo(block);


//                // Check signatures in stored Block

//                (storedBlock, err) = await store.DbGetBlock(index);
//                Assert.Null(err);

//                var ok = storedBlock.Signatures.TryGetValue(participants[0].Hex, out var val1Sig);
//                Assert.True(ok, "Validator1 signature not stored in block");

//                Assert.Equal(sig1.Signature, val1Sig);

//                ok = storedBlock.Signatures.TryGetValue(participants[1].Hex, out var val2Sig);
//                Assert.True(ok, "Validator2 signature not stored in block");

//                Assert.Equal(sig2.Signature, val2Sig);

//            }
//        }



//        //++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
////Check that the wrapper methods work
////These methods use the inmemStore as a cache on top of the DB

//        [Fact]
//        public async Task TestBadgerEvents()
//        {
//            var dbPath = GetPath();

//            //Insert more events than can fit in cache to test retrieving from db.
//            var cacheSize = 10;
//            var testSize = 10;
//            var (store, participants) = await InitBadgerStore(cacheSize, dbPath, logger);

//            //insert event
//            var events = new Dictionary<string, Event[]>();
//            StoreError err;
//            foreach (var p in participants)
//            {
//                using (var tx = store.BeginTx())
//                {
//                    var items = new List<Event>();
//                    for (var k = 0; k < testSize; k++)
//                    {
//                        var ev = new Event(new[] {$"{p.Hex}_{k}".StringToBytes()},new[] {new BlockSignature {Validator = "validator".StringToBytes(), Index = 0, Signature = "r|s".StringToBytes()}},
                            
//                            new[] {"", ""},
//                            p.PubKey,
//                            k);

//                        items.Add(ev);
//                        err = await store.SetEvent(ev);
//                        Assert.Null(err);
//                    }

//                    events[p.Hex] = items.ToArray();

//                    tx.Commit();
//                }
//            }

//            using (var tx = store.BeginTx())
//            {
//                // check that events were correclty inserted
//                foreach (var evd in events)
//                {
//                    var p = evd.Key;
//                    var evs = evd.Value;

//                    int k = 0;
//                    foreach (var ev in evs)
//                    {
//                        Event rev;
//                        (rev, err) = await store.GetEvent(ev.Hex());
//                        Assert.Null(err);

//                        ev.Body.ShouldCompareTo(rev.Body);

//                        ev.Signiture.ShouldCompareTo(rev.Signiture);
//                        k++;
//                    }
//                }

//                //check retrieving events per participant
//                var skipIndex = -1; //do not skip any indexes
//                foreach (var p in participants)
//                {
//                    string[] pEvents;
//                    (pEvents, err) = await store.ParticipantEvents(p.Hex, skipIndex);
//                    Assert.Null(err);

//                    var l = pEvents.Length;
//                    Assert.Equal(testSize, l);

//                    var expectedEvents = events[p.Hex].Skip(skipIndex + 1);

//                    int k = 0;
//                    foreach (var e in expectedEvents)
//                    {
//                        Assert.Equal(pEvents[k], e.Hex());
//                        k++;
//                    }
//                }

//                //check retrieving participant last
//                foreach (var p in participants)
//                {
//                    string last;
//                    (last, _, err) = store.LastEventFrom(p.Hex);
//                    Assert.Null(err);

//                    var evs = events[p.Hex];
//                    var expectedLast = evs[evs.Length - 1];

//                    Assert.Equal(expectedLast.Hex(), last);
//                }

//                var expectedKnown = new Dictionary<int, int>();
//                foreach (var p in participants)
//                {
//                    expectedKnown[p.Id] = testSize - 1;
//                }

//                var known = await store.KnownEvents();

//                known.ShouldCompareTo(expectedKnown);

//                foreach (var p in participants)
//                {
//                    var evs = events[p.Hex];
//                    foreach (var ev in evs)
//                    {
//                        err = store.AddConsensusEvent(ev.Hex());

//                        Assert.Null(err);
//                    }
//                }
//            }
//        }

//        [Fact]
//        public async Task TestBadgerRounds()
//        {
//            var dbPath = GetPath();
//            var cacheSize = 0;
//            var ( store, participants ) = await InitBadgerStore(cacheSize, dbPath, logger);

//            var round = new RoundInfo();
//            var events = new Dictionary<string, Event>();

//            foreach (var p in participants)
//            {
//                var ev = new Event(new[] {new byte[] { }}, new BlockSignature[]{},
//                    new[] {"", ""},
//                    p.PubKey,
//                    0);

//                events[p.Hex] = ev;
//                round.AddEvent(ev.Hex(), true);
//            }

//            StoreError err;
//            using (var tx = store.BeginTx())
//            {
//                err = await store.SetRound(0, round);
//                Assert.Null(err);
//                tx.Commit();
//            }

//            var c = store.LastRound();
//            Assert.Equal(0, c);

//            RoundInfo storedRound;
//            (storedRound, err) = await store.GetRound(0);
//            Assert.Null(err);

//            storedRound.ShouldCompareTo(round);

//            var witnesses = await store.RoundWitnesses(0);
//            var expectedWitnesses = round.Witnesses();

//            Assert.Equal(expectedWitnesses.Length, witnesses.Length);

//            foreach (var w in expectedWitnesses)
//            {
//                Assert.Contains(w, witnesses);
//            }
//        }

//        [Fact]
//        public async Task TestBadgerBlocks()
//        {
//            var dbPath = GetPath();
//            var cacheSize = 0;
//            var (store, participants) = await InitBadgerStore(cacheSize, dbPath, logger);

//            var index = 0;
//            var roundReceived = 5;
//            var transactions = new[]
//            {
//                "tx1".StringToBytes(),
//                "tx2".StringToBytes(),
//                "tx3".StringToBytes(),
//                "tx4".StringToBytes(),
//                "tx5".StringToBytes()
//            };

//            var block = new Block(index, roundReceived, transactions);

//            Exception err;
//            BlockSignature sig1;

//            (sig1, err) = block.Sign(participants[0].PrivKey);
//            Assert.Null(err);

//            BlockSignature sig2;
//            (sig2, err) = block.Sign(participants[1].PrivKey);
//            Assert.Null(err);

//            block.SetSignature(sig1);
//            block.SetSignature(sig2);

//            using (var tx = store.BeginTx())
//            {

//                // Store Block

//                err = await store.SetBlock(block);
//                Assert.Null(err);

//                Block storedBlock;
//                (storedBlock, err) = await store.GetBlock(index);
//                Assert.Null(err);

//                storedBlock.ShouldCompareTo(block);

//                // Check signatures in stored Block

//                (storedBlock, err) = await store.GetBlock(index);
//                Assert.Null(err);

//                var ok = storedBlock.Signatures.TryGetValue(participants[0].Hex, out var val1Sig);
//                Assert.True(ok, "Validator1 signature not stored in block");

//                Assert.Equal(sig1.Signature, val1Sig);

//                ok = storedBlock.Signatures.TryGetValue(participants[1].Hex, out var val2Sig);
//                Assert.True(ok, "Validator2 signature not stored in block");

//                Assert.Equal(sig2.Signature, val2Sig);

//            }

//        }

    }
}