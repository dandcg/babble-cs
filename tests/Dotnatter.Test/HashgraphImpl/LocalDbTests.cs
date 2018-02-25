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

        private static async Task<(LocalDbStore store, Pub[] pubs)> initBadgerStore(int cacheSize, string dbPath, ILogger logger)
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
        //    if err := store.Close(); err != nil {
        //        t.Fatal(err)
        //    }
        //    if err := os.RemoveAll(store.path); err != nil {
        //        t.Fatal(err)
        //    }
        //}

        private static async Task<LocalDbStore> createTestDB(string dir, ILogger logger)

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
            //os.RemoveAll("test_data")
            //os.Mkdir("test_data", os.ModeDir|0777)

            var dbPath = $"localdb/{Guid.NewGuid():D}";
            var store = await createTestDB(dbPath, logger);
            //defer os.RemoveAll(store.path)

            Assert.NotNull(store);
            Assert.Equal(store.Path, dbPath);

            //if (store.path != dbPath) {
            //    t.Fatalf("unexpected path %q", store.path)
            //}
            //if _, err := os.Stat(dbPath); err != nil {
            //    t.Fatalf("err: %s", err)
            //}

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
            var dbPath = $"localdb/{Guid.NewGuid():D}";

            //Create the test db
            var tempStore = await createTestDB(dbPath, logger);

            Assert.NotNull(tempStore);
            tempStore.Close();

            var cacheSize = 100;

            var (badgerStoreres, err) = await LocalDbStore.Load(cacheSize, tempStore.Path, logger);
            var badgerStore = badgerStoreres as LocalDbStore;

            Assert.NotNull(badgerStore);
            Assert.Null(err);

            Dictionary<string, int> dbParticipants;
            (dbParticipants, err) = await badgerStore.DbGetParticipants();

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
            var testSize = 1;
            var (store, participants) = await initBadgerStore(cacheSize, dbPath, logger);

            //inset events in db directly
            var events = new Dictionary<string, Event[]>();

            var topologicalIndex = 0;
            var topologicalEvents = new List<Event>();
            foreach (var p in participants)
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
                }

                var itemsA = items.ToArray();
                events[p.Hex] = itemsA;

                err = await store.DbSetEvents(itemsA);
                Assert.Null(err);
            }

            bool ver;

            //check events where correctly inserted and can be retrieved
            foreach (var evsd in events)
            {
                var p = evsd.Key;
                var evs = evsd.Value;

                var k = 0;
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

                    k++;
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

                Assert.Equal(testSize,pEvents.Length);

                var expectedEvents = events[p.Hex].Skip(skipIndex + 1);

                int k = 0;
                foreach (var e in expectedEvents)
                {
                    Assert.Equal(e.Hex(), pEvents[k]);
               
                    k++;
                }
            }
        }
    }
}