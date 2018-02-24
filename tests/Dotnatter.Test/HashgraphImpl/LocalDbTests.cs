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
        public async Task TestDBEventMethods()
        {
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

                var itemsA=items.ToArray();
                events[p.Hex] = itemsA; 

                var err = await store.DbSetEvents(itemsA);
                Assert.Null(err);

            }

            //check events where correctly inserted and can be retrieved
            foreach (var evsd in events)
            {
                var p = evsd.Key;
                var evs = evsd.Value;

                var k = 0;
                foreach (var ev in evs)
                {
       

          
                    logger.Debug($"Testing events[{p}][{ev.Hex()}]");


                    var (rev, err) = await store.DbGetEvent(ev.Hex());
                    Assert.Null(err);

                    //if !reflect.DeepEqual(ev.Body, rev.Body) {
                    //	t.Fatalf("events[%s][%d].Body should be %#v, not %#v", p, k, ev.Body, rev.Body)
                    //}
                    //if !reflect.DeepEqual(ev.S, rev.S) {
                    //	t.Fatalf("events[%s][%d].S should be %#v, not %#v", p, k, ev.S, rev.S)
                    //}
                    //if !reflect.DeepEqual(ev.R, rev.R) {
                    //	t.Fatalf("events[%s][%d].R should be %#v, not %#v", p, k, ev.R, rev.R)
                    //}
                    //if ver, err := rev.Verify(); err != nil && !ver {
                    //	t.Fatalf("failed to verify signature. err: %s", err)
                    //}

                    k++;
                }
            }

            ////check topological order of events was correctly created
            //dbTopologicalEvents, err := store.dbTopologicalEvents()
            //if err != nil {
            //	t.Fatal(err)
            //}
            //if len(dbTopologicalEvents) != len(topologicalEvents) {
            //	t.Fatalf("Length of dbTopologicalEvents should be %d, not %d",
            //		len(topologicalEvents), len(dbTopologicalEvents))
            //}
            //for i, dte := range dbTopologicalEvents {
            //	te := topologicalEvents[i]

            //	if dte.Hex() != te.Hex() {
            //		t.Fatalf("dbTopologicalEvents[%d].Hex should be %s, not %s", i,
            //			te.Hex(),
            //			dte.Hex())
            //	}
            //	if !reflect.DeepEqual(te.Body, dte.Body) {
            //		t.Fatalf("dbTopologicalEvents[%d].Body should be %#v, not %#v", i,
            //			te.Body,
            //			dte.Body)
            //	}
            //	if !reflect.DeepEqual(te.R, dte.R) {
            //		t.Fatalf("dbTopologicalEvents[%d].R should be %#v, not %#v", i,
            //			te.R,
            //			dte.R)
            //	}
            //	if !reflect.DeepEqual(te.S, dte.S) {
            //		t.Fatalf("dbTopologicalEvents[%d].S should be %#v, not %#v", i,
            //			te.S,
            //			dte.S)
            //	}

            //	if ver, err := dte.Verify(); err != nil && !ver {
            //		t.Fatalf("failed to verify signature. err: %s", err)
            //	}
            //}

            ////check that participant events where correctly added
            //skipIndex := -1 //do not skip any indexes
            //for _, p := range participants {
            //	pEvents, err := store.dbParticipantEvents(p.hex, skipIndex)
            //	if err != nil {
            //		t.Fatal(err)
            //	}
            //	if l := len(pEvents); l != testSize {
            //		t.Fatalf("%s should have %d events, not %d", p.hex, testSize, l)
            //	}

            //	expectedEvents := events[p.hex][skipIndex+1:]
            //	for k, e := range expectedEvents {
            //		if e.Hex() != pEvents[k] {
            //			t.Fatalf("ParticipantEvents[%s][%d] should be %s, not %s",
            //				p.hex, k, e.Hex(), pEvents[k])
            //		}
            //	}
            //}
        }
    }
}