using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dotnatter.Common;
using Dotnatter.Crypto;
using Dotnatter.HashgraphImpl.Model;
using Dotnatter.HashgraphImpl.Stores;
using Dotnatter.Test.Helpers;
using Dotnatter.Util;
using Serilog;
using Xunit;
using Xunit.Abstractions;

namespace Dotnatter.Test.HashgraphImpl
{
    public class LocalDbTests
    {
        private readonly ILogger logger;
        private ITestOutputHelper output;
        private readonly string dbPath;

        public LocalDbTests(ITestOutputHelper output)
        {
            this.output = output;
            logger = output.SetupLogging().ForContext("SourceContext", "LocalDbTests");
            dbPath = $"localdb/{Guid.NewGuid():D}";
        }

        private static async Task<(LocalDbStore store, Pub[] pubs)> InitBadgerStore(int cacheSize, string dbPath, ILogger logger)
        {
            var n = 3;
            var participantPubs = new List<Pub>();
            var participants = new Dictionary<string, int>();
            for (var i = 0; i < n; i++)
            {
                var key = CryptoUtils.GenerateEcdsaKey();
                var pubKey = CryptoUtils.FromEcdsaPub(key);
                participantPubs.Add(new Pub {Id = i, PrivKey = key, PubKey = pubKey, Hex = pubKey.ToHex()});
                participants[pubKey.ToHex()] = i;
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
            var participants = new Dictionary<string, int>
            {
                {
                    "alice", 0
                },
                {
                    "bob", 1
                },

                {
                    "charlie", 2
                }
            };

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
            logger.Information(Directory.GetCurrentDirectory());
            
            var store = await CreateTestDb(dbPath, logger);

            Assert.NotNull(store);
            Assert.Equal(store.Path, dbPath);
            
            //check roots

            StoreError err;
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

            err = store.Close();
            Assert.Null(err);
        }

        [Fact]
        public async Task TestLoadStore()
        {

            //Create the test db
            var tempStore = await CreateTestDb(dbPath, logger);

            Assert.NotNull(tempStore);
            tempStore.Close();

            var cacheSize = 100;

            var (badgerStoreres, err) = await LocalDbStore.Load(cacheSize, tempStore.Path, logger);
            var badgerStore = badgerStoreres as LocalDbStore;

            Assert.NotNull(badgerStore);
            Assert.Null(err);

            Dictionary<string, int> dbParticipants;
            using (var tx = badgerStore.BeginTx())
            {
                (dbParticipants, err) = await badgerStore.DbGetParticipants();
            }

            Assert.Null(err);
            Assert.Equal(badgerStore.Participants().participants.Count, dbParticipants.Count);

            foreach (var p in dbParticipants)
            {
                var dbP = p.Key;
                var dbId = p.Value;

                var ok = badgerStore.Participants().participants.TryGetValue(dbP, out int id);
                Assert.True(ok);
                Assert.Equal(id, dbId);
            }
        }

        //++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
        //Call DB methods directly

        [Fact]
        public async Task TestDbEventMethods()
        {
            Exception err;
            var cacheSize = 0;
            var testSize = 100;
            var (store, participants) = await InitBadgerStore(cacheSize, dbPath, logger);

            //inset events in db directly
            var events = new Dictionary<string, Event[]>();

            var topologicalIndex = 0;
            var topologicalEvents = new List<Event>();
            foreach (var p in participants)
            {
                using (var tx =store.BeginTx())
                {
                    var items = new List<Event>();
                    for (var k = 0; k < testSize; k++)
                    {
                        var ev = new Event(new[] {$"{p.Hex.Take(5)}_{k}".StringToBytes()},
                            new[] {"", ""},
                            p.PubKey,
                            k);

                        ev.Sign(p.PrivKey);
                        ev.SetTopologicalIndex(topologicalIndex);
                        topologicalIndex++;
                        topologicalEvents.Add(ev);

                        items.Add(ev);

                        err = await store.DbSetEvents(new[] {ev});
                        Assert.Null(err);

                    }


                    events[p.Hex] = items.ToArray();

                    tx.Commit();
                }

            }

            bool ver;

            //check events where correctly inserted and can be retrieved
            foreach (var evsd in events)
            {
                var p = evsd.Key;
                var evs = evsd.Value;

                foreach (var ev in evs)
                {
                    logger.Debug($"Testing events[{p}][{ev.Hex()}]");

                    Event rev;
                    (rev, err) = await store.DbGetEvent(ev.Hex());
                    Assert.Null(err);

                    ev.Body.ShouldCompareTo(rev.Body);

                    rev.ShouldCompareTo(ev);

                    Assert.Equal(ev.Signiture, rev.Signiture);

                    (ver, err) = rev.Verify();
                    Assert.Null(err);
                    Assert.True(ver);

                }
            }

            //check topological order of events was correctly created
            Event[] dbTopologicalEvents;
            (dbTopologicalEvents, err) = await store.DbTopologicalEvents();
            Assert.Null(err);

            Assert.Equal(topologicalEvents.Count, dbTopologicalEvents.Length);

            int i = 0;
            foreach (var dte in dbTopologicalEvents)

            {
                var te = topologicalEvents[i];

                Assert.Equal(te.Hex(), dte.Hex());

                dte.Body.ShouldCompareTo(te.Body);

                Assert.Equal(te.Signiture, dte.Signiture);

                (ver, err) = dte.Verify();
                Assert.Null(err);
                Assert.True(ver);

                i++;
            }

            //check that participant events where correctly added
            var skipIndex = -1; //do not skip any indexes
            foreach (var p in participants)
            {
                string[] pEvents;
                (pEvents, err) = await store.DbParticipantEvents(p.Hex, skipIndex);
                Assert.Null(err);

                Assert.Equal(testSize, pEvents.Length);

                var expectedEvents = events[p.Hex].Skip(skipIndex + 1);

                int k = 0;
                foreach (var e in expectedEvents)
                {
                    Assert.Equal(e.Hex(), pEvents[k]);

                    k++;
                }
            }
        }

        [Fact]
        public async Task TestDbRoundMethods()
        {
            var cacheSize = 0;
            var (store, participants) = await InitBadgerStore(cacheSize, dbPath, logger);

            var round = new RoundInfo();
            var events = new Dictionary<string, Event>();
            foreach (var p in participants)
            {
                var ev = new Event(new[] {new byte[] { }},
                    new[] {"", ""},
                    p.PubKey,
                    0);

                events[p.Hex] = ev;
                round.AddEvent(ev.Hex(), true);
            }

            StoreError err;
            using (var tx = store.BeginTx())
            {
             err = await store.DbSetRound(0, round);
                Assert.Null(err);
                tx.Commit();
            }

            RoundInfo storedRound;
            (storedRound, err) = await store.DbGetRound(0);

            Assert.Null(err);

            storedRound.ShouldCompareTo(round);

            var witnesses = await store.RoundWitnesses(0);
            var expectedWitnesses = round.Witnesses();

            Assert.Equal(expectedWitnesses.Length, witnesses.Length);

            foreach (var w in expectedWitnesses)
            {
                Assert.Contains(w, witnesses);
            }
        }

        [Fact]
        public async Task TestDbParticipantMethods()
        {
            var cacheSize = 0;
            var (store, _ ) = await InitBadgerStore(cacheSize, dbPath, logger);

            var (participants, err) = store.Participants();
            Assert.Null(err);

            using (var tx = store.BeginTx())
            {
                err = await store.DbSetParticipants(participants);
                Assert.Null(err);
                tx.Commit();
            }

            Dictionary<string, int> participantsFromDb;
            (participantsFromDb, err) = await store.DbGetParticipants();

            foreach (var pp in participantsFromDb)
            {
                logger.Debug(pp.Key);
            }

            Assert.Null(err);

            foreach (var pp in participants)
            {
                logger.Debug(pp.Key);

                var p = pp.Key;
                var id = pp.Value;
                var ok = participantsFromDb.TryGetValue(p, out var dbId);
                Assert.True(ok);
                Assert.Equal(id, dbId);
            }
        }

        //++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
//Check that the wrapper methods work
//These methods use the inmemStore as a cache on top of the DB

        [Fact]
        public async Task TestBadgerEvents()
        {
            //Insert more events than can fit in cache to test retrieving from db.
            var cacheSize = 10;
            var testSize = 10;
            var (store, participants) = await InitBadgerStore(cacheSize, dbPath, logger);

            //insert event
            var events = new Dictionary<string, Event[]>();
            StoreError err;
            foreach (var p in participants)
            {
                using (var tx = store.BeginTx())
                {
                    var items = new List<Event>();
                    for (var k = 0; k < testSize; k++)
                    {
                        var ev = new Event(new[] {$"{p.Hex}_{k}".StringToBytes()}
                            ,
                            new[] {"", ""},
                            p.PubKey,
                            k);

                        items.Add(ev);
                        err = await store.SetEvent(ev);
                        Assert.Null(err);
                    }

                    events[p.Hex] = items.ToArray();

                   tx.Commit();
                }
            }

            // check that events were correclty inserted
            foreach (var evd in events)
            {
                var p = evd.Key;
                var evs = evd.Value;

                int k = 0;
                foreach (var ev in evs)
                {
                    Event rev;
                    (rev, err) = await store.GetEvent(ev.Hex());
                    Assert.Null(err);

                    ev.Body.ShouldCompareTo(rev.Body);

                    ev.Signiture.ShouldCompareTo(rev.Signiture);
                    k++;
                }
            }

            //check retrieving events per participant
            var skipIndex = -1; //do not skip any indexes
            foreach (var p in participants)
            {
                string[] pEvents;
                (pEvents, err) = await store.ParticipantEvents(p.Hex, skipIndex);
                Assert.Null(err);

                var l = pEvents.Length;
                Assert.Equal(testSize, l);

                var expectedEvents = events[p.Hex].Skip(skipIndex + 1);

                int k = 0;
                foreach (var e in expectedEvents)
                {
                    Assert.Equal(pEvents[k], e.Hex());
                    k++;
                }
            }

            //check retrieving participant last
            foreach (var p in participants)
            {
                string last;
                (last, _, err) = store.LastFrom(p.Hex);
                Assert.Null(err);

                var evs = events[p.Hex];
                var expectedLast = evs[evs.Length - 1];

                Assert.Equal(expectedLast.Hex(), last);
            }

            var expectedKnown = new Dictionary<int, int>();
            foreach (var p in participants)
            {
                expectedKnown[p.Id] = testSize - 1;
            }

            var known = await store.Known();

            known.ShouldCompareTo(expectedKnown);

            foreach (var p in participants)
            {
                var evs = events[p.Hex];
                foreach (var ev in evs)
                {
                    err = store.AddConsensusEvent(ev.Hex());

                    Assert.Null(err);
                }
            }
        }

        [Fact]
        public async Task TestBadgerRounds()
        {
            var cacheSize = 0;
            var ( store, participants ) = await InitBadgerStore(cacheSize, dbPath, logger);

            var round = new RoundInfo();
            var events = new Dictionary<string, Event>();

            foreach (var p in participants)
            {
                var ev = new Event(new[] {new byte[] { }},
                    new[] {"", ""},
                    p.PubKey,
                    0);

                events[p.Hex] = ev;
                round.AddEvent(ev.Hex(), true);
            }

            StoreError err;
            using (var tx = store.BeginTx())
            {
                 err = await store.SetRound(0, round);
                Assert.Null(err);
                tx.Commit();

            }


            var c = store.LastRound();
            Assert.Equal(0, c);

            RoundInfo storedRound;
            (storedRound, err) = await store.GetRound(0);
            Assert.Null(err);

            storedRound.ShouldCompareTo(round);

            var witnesses = await store.RoundWitnesses(0);
            var expectedWitnesses = round.Witnesses();

            Assert.Equal(expectedWitnesses.Length, witnesses.Length);

            foreach (var w in expectedWitnesses)
            {
                Assert.Contains(w, witnesses);
            }
        }
    }
}