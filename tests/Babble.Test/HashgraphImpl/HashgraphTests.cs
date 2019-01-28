using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Babble.Core;
using Babble.Core.Common;
using Babble.Core.Crypto;
using Babble.Core.HashgraphImpl;
using Babble.Core.HashgraphImpl.Model;
using Babble.Core.HashgraphImpl.Stores;
using Babble.Core.PeersImpl;
using Babble.Core.Util;
using Babble.Test.Utils;
using Serilog;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
using static Babble.Core.Util.IterationHelpers;

namespace Babble.Test.HashgraphImpl
{
    public class HashgraphTests

    {
        public HashgraphTests(ITestOutputHelper output)
        {
            this.output = output;
            logger = output.SetupLogging().ForContext("SourceContext", "HashGraphTests");
        }

        public const int CacheSize = 100;

        public const int N = 3;

        private readonly ILogger logger;
        private readonly ITestOutputHelper output;

        private string GetPath()
        {
            return $"localdb/{Guid.NewGuid():D}";
        }

        public class TestNode
        {
            public int Id { get; set; }
            public byte[] Pub { get; set; }
            public string PubHex { get; set; }
            public CngKey Key { get; set; }
            public List<Event> Events { get; set; }

            public TestNode(CngKey key, int id)
            {
                var pub = CryptoUtils.FromEcdsaPub(key);
                Id = id;
                Key = key;
                Pub = pub;
                PubHex = pub.ToHex();
                Events = new List<Event>();
            }

            public void SignAndAddEvent(Event ev, string name, Dictionary<string, string> index, List<Event> orderedEvents)
            {
                ev.Sign(Key);
                Events.Add(ev);
                index[name] = ev.Hex();
                orderedEvents.Add(ev);
            }
        }

        public class AncestryItem
        {
            public AncestryItem(string descendant, string ancestor, bool val, bool err)
            {
                Descendant = descendant;
                Ancestor = ancestor;
                Val = val;
                Err = err;
            }

            public string Descendant { get; set; }
            public string Ancestor { get; set; }
            public bool Val { get; set; }
            public bool Err { get; set; }
        }

        public class RoundItem
        {
            public RoundItem(string ev, int round)
            {
                Event = ev;
                Round = round;
            }

            public string Event { get; set; }
            public int Round { get; set; }
        }

        public class Play
        {
            public int To { get; }
            public int Index { get; }
            public string SelfParent { get; }
            public string OtherParent { get; }
            public string Name { get; }
            public byte[][] TxPayload { get; }
            public BlockSignature[] SigPayload { get; }

            public Play(
                int to,
                int index,
                string selfParent,
                string otherParent,
                string name,
                byte[][] txPayload,
                BlockSignature[] sigPayload
            )
            {
                To = to;
                Index = index;
                SelfParent = selfParent;
                OtherParent = otherParent;
                Name = name;
                TxPayload = txPayload;
                SigPayload = sigPayload;
            }
        }

        public async Task<(TestNode[], Dictionary<string, string>, List<Event>, Peers)> InitHashgraphNodes(int n)
        {
            var index = new Dictionary<string, string>();
            var nodes = new List<TestNode>();
            var orderedEvents = new List<Event>();
            var keys = new Dictionary<string, CngKey>();

            var participants = Peers.NewPeers();

            int i = 0;
            for (i = 0; i < n; i++)
            {
                var key = CryptoUtils.GenerateEcdsaKey();
                var pub = CryptoUtils.FromEcdsaPub(key);
                var pubHex = pub.ToHex();
                await participants.AddPeer(Peer.New(pubHex, ""));
                keys[pubHex] = key;
            }

            i = 0;

            foreach (var peer in await participants.ToPeerSlice())
            {
                nodes.Add(new TestNode(keys[peer.PubKeyHex], i));
            }

            return (nodes.ToArray(), index, orderedEvents, participants);
        }

        private void PlayEvents(Play[] plays, TestNode[] nodes, Dictionary<string, string> index, List<Event> orderedEvents)
        {
            foreach (var p in plays)
            {
                index.TryGetValue(p.SelfParent, out var selfParentIndex);
                selfParentIndex = selfParentIndex ?? "";

                index.TryGetValue(p.OtherParent, out var otherParentIndex);
                otherParentIndex = otherParentIndex ?? "";

                var e = new Event(p.TxPayload, p.SigPayload, new[] {selfParentIndex, otherParentIndex}, nodes[p.To].Pub, p.Index);

                nodes[p.To].SignAndAddEvent(e, p.Name, index, orderedEvents);
            }
        }

        private async Task<Hashgraph> CreateHashgraph(bool db, List<Event> orderedEvents, Peers participants, ILogger logger)
        {
            IStore store;

            if (db)
            {
                BabbleError err1;

                (store, err1) = await LocalDbStore.New(participants, CacheSize, GetPath(), logger);
                if (err1 != null)
                {
                    logger.Fatal(err1.Message);
                }
            }
            else
            {
                store = await InmemStore.NewInmemStore(participants, CacheSize, logger);
            }

            var hashgraph = new Hashgraph(participants, store, null, logger);

            int i = 0;

            foreach (var ev in orderedEvents)
            {
                //logger.Debug("dd {@e}",ev);
                var err2 = await hashgraph.InsertEvent(ev, true);

                if (err2 != null)
                {
                    logger.Debug($"ERROR inserting event {i}: {err2.Message}");
                }

                i++;
            }

            return hashgraph;
        }

        private async Task<(Hashgraph hashgraph, Dictionary<string, string> index, List<Event> events)> InitHashgraphFull(Play[] plays, bool db, int n, ILogger logger)
        {
            var (nodes, index, orderedEvents, participants) = await InitHashgraphNodes(n);

            // Needed to have sorted nodes based on participants hash32

            int i = 0;

            foreach (var peer in await participants.ToPeerSlice())

            {
                var ev = new Event(null, null, new[] {Event.RootSelfParent(peer.ID), ""}, nodes[i].Pub, 0);
                nodes[i].SignAndAddEvent(ev, $"e{i}", index, orderedEvents);
                i++;
            }

            PlayEvents(plays, nodes, index, orderedEvents);

            foreach (var j in index)
            {
                //logger.Debug("name={name}, hex={hex}", j.Key, j.Value);
            }

            var hashgraph = await CreateHashgraph(db, orderedEvents, participants, logger);

            return (hashgraph, index, orderedEvents);
        }

        /*
        |  e12  |
        |   | \ |
        |  s10   e20
        |   | / |
        |   /   |
        | / |   |
        s00 |  s20
        |   |   |
        e01 |   |
        | \ |   |
        e0  e1  e2
        0   1   2
        */

        private async Task<( Hashgraph hashgraph, Dictionary<string, string> index)> InitHashgraph()
        {
            var plays = new[]
            {
                new Play(0, 1, "e0", "e1", "e01", null, null),
                new Play(2, 1, "e2", "", "s20", null, null),
                new Play(1, 1, "e1", "", "s10", null, null),
                new Play(0, 2, "e01", "", "s00", null, null),
                new Play(2, 2, "s20", "s00", "e20", null, null),
                new Play(1, 2, "s10", "e20", "e12", null, null)
            };

            var (h, index, orderedEvents) = await InitHashgraphFull(plays, false, N, logger);

            int i = 0;

            foreach (var ev in orderedEvents)
            {
                var err1 = await h.InitEventCoordinates(ev);

                if (err1 != null)
                {
                    output.WriteLine($"{i}: {err1}");
                    Assert.Null(err1);
                }

                var err2 = await h.Store.SetEvent(ev);
                if (err2 != null)
                {
                    output.WriteLine($"{i}: {err2}");
                    Assert.Null(err2);
                }

                var err3 = await h.UpdateAncestorFirstDescendant(ev);

                if (err3 != null)
                {
                    output.WriteLine($"{i}: {err3}");
                    Assert.Null(err3);
                }

                i++;
            }

            return (h, index);
        }

        [Fact]
        public async Task TestInit()
        {
            var (h, index) = await InitHashgraph();
            Assert.NotNull(h);
            Assert.NotNull(index);
        }

        [Fact]
        public async Task TestAncestor()
        {
            var ( h, index) = await InitHashgraph();

            var expected = new[]
            {
                //first generation
                new AncestryItem("e01", "e0", true, false),
                new AncestryItem("e01", "e1", true, false),
                new AncestryItem("s00", "e01", true, false),
                new AncestryItem("s20", "e2", true, false),
                new AncestryItem("e20", "s00", true, false),
                new AncestryItem("e20", "s20", true, false),
                new AncestryItem("e12", "e20", true, false),
                new AncestryItem("e12", "s10", true, false),

                //second generation
                new AncestryItem("s00", "e0", true, false),
                new AncestryItem("s00", "e1", true, false),
                new AncestryItem("e20", "e01", true, false),
                new AncestryItem("e20", "e2", true, false),
                new AncestryItem("e12", "e1", true, false),
                new AncestryItem("e12", "s20", true, false),

                //third generation
                new AncestryItem("e20", "e0", true, false),
                new AncestryItem("e20", "e1", true, false),
                new AncestryItem("e20", "e2", true, false),
                new AncestryItem("e12", "e01", true, false),
                new AncestryItem("e12", "e0", true, false),
                new AncestryItem("e12", "e1", true, false),
                new AncestryItem("e12", "e2", true, false),
                //false positive
                new AncestryItem("e01", "e2", false, false),
                new AncestryItem("s00", "e2", false, false),
                new AncestryItem("e0", "", false, true),
                new AncestryItem("s00", "", false, true),
                new AncestryItem("e12", "", false, true)
            };

            foreach (var exp in expected)
            {
                var indexDescendant = index.GetOrEmpty(exp.Descendant);
                var indexAncestor = index.GetOrEmpty(exp.Ancestor);

                indexDescendant = indexDescendant ?? "";
                indexAncestor = indexAncestor ?? "";

                logger.Debug("{d} {di} {a} {ai}", exp.Descendant, indexDescendant, exp.Ancestor, indexAncestor);

                var (a, err) = await h.Ancestor(indexDescendant, indexAncestor);

                if (err != null && !exp.Err)
                {
                    logger.Error($"Error computing ancestor({exp.Descendant}, {exp.Ancestor}). Err: {err}");
                    Assert.NotNull(err);
                }

                if (a != exp.Val)
                {
                    logger.Error($"ancestor({exp.Descendant}, {exp.Ancestor}) should be {exp.Val}, not {a}");
                    Assert.Equal(exp.Val, a);
                }
            }

            foreach (var j in h.AncestorCache.Keys())
            {
                logger.Debug("k={k}, v={v}", j, h.AncestorCache.Get(j));
            }
        }

        [Fact]
        public async Task TestKey()
        {
            var key1 = Key.New("a", "b");

            var key2 = Key.New("a", "b");
            Assert.Equal(key1, key2);

            var ac = new LruCache<string, bool>(100, null, logger);
            ac.Add(key1, true);

            var (res, ok) = ac.Get(key2);

            Assert.True(ok);
            Assert.True(res);

            var key3 = Key.New("a", "c");

            var (res2, ok2) = ac.Get(key3);

            Assert.False(ok2);
            Assert.False(res2);
        }

        [Fact]
        public async Task TestSelfAncestor()
        {
            var ( h, index) = await InitHashgraph();

            var expected = new[]
            {
                //first generation
                new AncestryItem("e01", "e0", true, false),
                new AncestryItem("s00", "e01", true, false),
                //1 generation false negative
                new AncestryItem("e01", "e1", false, false),
                new AncestryItem("e12", "e20", false, false),
                new AncestryItem("s20", "e1", false, false),
                new AncestryItem("s20", "", false, true),
                //2 generations
                new AncestryItem("e20", "e2", true, false),
                new AncestryItem("e12", "e1", true, false),
                //2 generations false negatives
                new AncestryItem("e20", "e0", false, false),
                new AncestryItem("e12", "e2", false, false),
                new AncestryItem("e20", "e01", false, false)
            };

            foreach (var exp in expected)
            {
                var indexDescendant = index.GetOrEmpty(exp.Descendant);
                var indexAncestor = index.GetOrEmpty(exp.Ancestor);

                // logger.Debug("{d} {di} {a} {ai}", exp.Descendant, indexDescendant, exp.Ancestor, indexAncestor);

                var (a, err) = await h.SelfAncestor(indexDescendant, indexAncestor);

                if (err != null && !exp.Err)
                {
                    logger.Error($"Error computing self ancestor({exp.Descendant}, {exp.Ancestor}). Err: {err}");
                    Assert.NotNull(err);
                }

                if (a != exp.Val)
                {
                    logger.Error($"self ancestor({exp.Descendant}, {exp.Ancestor}) should be {exp.Val}, not {a}");
                    Assert.Equal(exp.Val, a);
                }
            }
        }

        [Fact]
        public async Task TestSee()
        {
            var ( h, index) = await InitHashgraph();

            var expected = new[]
            {
                new AncestryItem("e01", "e0", true, false),
                new AncestryItem("e01", "e1", true, false),
                new AncestryItem("e20", "e0", true, false),
                new AncestryItem("e20", "e01", true, false),
                new AncestryItem("e12", "e01", true, false),
                new AncestryItem("e12", "e0", true, false),
                new AncestryItem("e12", "e1", true, false),
                new AncestryItem("e12", "s20", true, false)
            };

            foreach (var exp in expected)
            {
                var indexDescendant = index.GetOrEmpty(exp.Descendant);
                var indexAncestor = index.GetOrEmpty(exp.Ancestor);

                // logger.Debug("{d} {di} {a} {ai}", exp.Descendant, indexDescendant, exp.Ancestor, indexAncestor);

                var (a, err) = await h.See(indexDescendant, indexAncestor);

                if (err != null && !exp.Err)
                {
                    logger.Error($"Error computing self ancestor({exp.Descendant}, {exp.Ancestor}). Err: {err}");
                    Assert.NotNull(err);
                }

                if (a != exp.Val)
                {
                    logger.Error($"self ancestor({exp.Descendant}, {exp.Ancestor}) should be {exp.Val}, not {a}");
                    Assert.Equal(exp.Val, a);
                }
            }
        }

        [Fact]
        public async Task TestLamportTimestamp()
        {
            var ( h, index) = await InitHashgraph();

            var expectedTimestamps = new Dictionary<string, int>
            {
                {"e0", 0},
                {"e1", 0},
                {"e2", 0},
                {"e01", 1},
                {"s10", 1},
                {"s20", 1},
                {"s00", 2},
                {"e20", 3},
                {"e12", 4}
            };

            foreach (var etsd in expectedTimestamps)
            {
                var e = etsd.Key;
                var ets = etsd.Value;

                var (ts, err) = await h.LamportTimestamp(index[e]);
                if (err != null)
                {
                    logger.Error($"Error computing LamportTimestamp({e}). Err: {err.Message}");
                    Assert.NotNull(err);
                }

                if (ts != ets)
                {
                    logger.Error($"{e} LamportTimestamp should be {ets}, not {ts}");
                    Assert.Equal(ets, ts);
                }
            }
        }

        //        /*
        //        |    |    e20
        //        |    |   / |
        //        |    | /   |
        //        |    /     |
        //        |  / |     |
        //        e01  |     |
        //        | \  |     |
        //        |   \|     |
        //        |    |\    |
        //        |    |  \  |
        //        e0   e1 (a)e2
        //        0    1     2

        // Node 2 Forks; events a and e2 are both created by node2, they are not self-parents
        // and yet they are both ancestors of event e20

        [Fact]
        public async Task TestFork()
        {
            var index = new Dictionary<string, string>();

            var nodes = new List<TestNode>();

            var participants = Peers.NewPeers();

            int i = 0;
            for (i = 0; i < N; i++)
            {
                var key = CryptoUtils.GenerateEcdsaKey();
                var node = new TestNode(key, i);
                nodes.Add(node);
                await participants.AddPeer(Peer.New(node.Pub.ToHex(), ""));
            }

            var store = await InmemStore.NewInmemStore(participants, CacheSize, logger);

            var hashgraph = new Hashgraph(participants, store, null, logger);

            i = 0;
            foreach (var node in nodes)
            {
                var ev = new Event(null, null, new[] {"", ""}, node.Pub, 0);

                ev.Sign(node.Key);

                index.Add($"e{i}", ev.Hex());

                await hashgraph.InsertEvent(ev, true);

                i++;
            }

// ---

//a and e2 need to have different hashes

            var eventA = new Event(new[] {"yo".StringToBytes()}, null, new[] {"", ""}, nodes[2].Pub, 0);
            eventA.Sign(nodes[2].Key);
            index["a"] = eventA.Hex();

            // "InsertEvent should return error for 'a'"
            var err = hashgraph.InsertEvent(eventA, true);
            Assert.NotNull(err);

            //// ---

            var event01 = new Event(null, null, new[] {index["e0"], index["a"]}, nodes[0].Pub, 1); //e0 and a
            event01.Sign(nodes[0].Key);
            index["e01"] = event01.Hex();

            // "InsertEvent should return error for e01";
            err = hashgraph.InsertEvent(event01, true);
            Assert.NotNull(err);

            // ---

            var event20 = new Event(null, null, new[] {index["e2"], index["e01"]}, nodes[2].Pub, 1); //e2 and e01
            event20.Sign(nodes[2].Key);
            index["e20"] = event20.Hex();

            //"InsertEvent should return error for e20"
            err = hashgraph.InsertEvent(event20, true);
            Assert.NotNull(err);
        }

        //        /*
        //        |  s11  |
        //        |   |   |
        //        |   f1  |
        //        |  /|   |
        //        | / s10 |
        //        |/  |   |
        //        e02 |   |
        //        | \ |   |
        //        |   \   |
        //        |   | \ |
        //        s00 |  e21
        //        |   | / |
        //        |  e10  s20
        //        | / |   |
        //        e0  e1  e2
        //        0   1    2
        //        */

        private async Task<(Hashgraph hashgraph, Dictionary<string, string> index)> InitRoundHashgraph()
        {
            var plays = new[]
            {
                new Play(1, 1, "e1", "e0", "e10", null, null),
                new Play(2, 1, "e2", "", "s20", null, null),
                new Play(0, 1, "e0", "", "s00", null, null),
                new Play(2, 2, "s20", "e10", "e21", null, null),
                new Play(0, 2, "s00", "e21", "e02", null, null),
                new Play(1, 2, "e10", "", "s10", null, null),
                new Play(1, 3, "s10", "e02", "f1", null, null),
                new Play(1, 4, "f1", "", "s11", new[] {"abc".StringToBytes()}, null)
            };

            var (h, index, _) = await InitHashgraphFull(plays, false, N, logger);

            return (h, index);
        }

        [Fact]
        public async Task TestInsertEvent()
        {
            var (h, index) = await InitRoundHashgraph();

            var participants = await h.Participants.ToPeerSlice();

            //e0
            var (e0, err) = await h.Store.GetEvent(index["e0"]);

            Assert.Null(err);

            Assert.True(e0.Body.GetSelfParentIndex() == -1 &&
                        e0.Body.GetOtherParentCreatorId() == -1 &&
                        e0.Body.GetOtherParentIndex() == -1 &&
                        e0.Body.GetCreatorId() == h.Participants.ByPubKey[e0.Creator()].ID, "Invalid wire info on e0");

            var expectedFirstDescendants = new OrderedEventCoordinates(new[]
            {
                new Index(participants[0].ID, new EventCoordinates(index["e0"], 0)),
                new Index(participants[1].ID, new EventCoordinates(index["e10"], 1)),
                new Index(participants[2].ID, new EventCoordinates(index["e21"], 2))
            });

            var expectedLastAncestors = new OrderedEventCoordinates(new[]
            {
                new Index(participants[0].ID, new EventCoordinates(index["e0"], 0)),
                new Index(participants[1].ID, new EventCoordinates("", -1)),
                new Index(participants[2].ID, new EventCoordinates("", -1))
            });

            e0.FirstDescendants.Values.ShouldCompareTo(expectedFirstDescendants.Values);
            e0.LastAncestors.Values.ShouldCompareTo(expectedLastAncestors.Values);

            //e21
            Event e21;
            (e21, err) = await h.Store.GetEvent(index["e21"]);

            Assert.Null(err);

            Event e10;
            (e10, err) = await h.Store.GetEvent(index["e10"]);

            Assert.Null(err);

            Assert.True(e21.Body.GetSelfParentIndex() == 1 &&
                        e21.Body.GetOtherParentCreatorId() == h.Participants.ByPubKey[e10.Creator()].ID &&
                        e21.Body.GetOtherParentIndex() == 1 &&
                        e21.Body.GetCreatorId() == h.Participants.ByPubKey[e21.Creator()].ID
                , "Invalid wire info on e21"
            );

            expectedFirstDescendants = new OrderedEventCoordinates(new[]
            {
                new Index(participants[0].ID, new EventCoordinates(index["e02"], 2)),
                new Index(participants[1].ID, new EventCoordinates(index["f1"], 3)),
                new Index(participants[2].ID, new EventCoordinates(index["e21"], 2))
            });

            expectedLastAncestors = new OrderedEventCoordinates(new[]
            {
                new Index(participants[0].ID, new EventCoordinates(index["e0"], 0)),
                new Index(participants[1].ID, new EventCoordinates(index["e10"], 1)),
                new Index(participants[2].ID, new EventCoordinates(index["e21"], 2))
            });

            // "e21 firstDescendants not good"
            e21.FirstDescendants.Values.ShouldCompareTo(expectedFirstDescendants.Values);

            //"e21 lastAncestors not good" 
            e21.LastAncestors.Values.ShouldCompareTo(expectedLastAncestors.Values);

            //f1
            Event f1;
            (f1, err) = await h.Store.GetEvent(index["f1"]);

            Assert.Null(err);

            Assert.True(f1.Body.GetSelfParentIndex() == 2 &&
                        f1.Body.GetOtherParentCreatorId() == h.Participants.ByPubKey[e0.Creator()].ID &&
                        f1.Body.GetOtherParentIndex() == 2 &&
                        f1.Body.GetCreatorId() == h.Participants.ByPubKey[f1.Creator()].ID, "Invalid wire info on f1");

            expectedFirstDescendants = new OrderedEventCoordinates(new[]
            {
                new Index(participants[0].ID, new EventCoordinates("", int.MaxValue)),
                new Index(participants[1].ID, new EventCoordinates(index["f1"], 3)),
                new Index(participants[2].ID, new EventCoordinates("", int.MaxValue))
            });

            expectedLastAncestors = new OrderedEventCoordinates(new[]
            {
                new Index(participants[0].ID, new EventCoordinates(index["e02"], 2)),
                new Index(participants[1].ID, new EventCoordinates(index["f1"], 3)),
                new Index(participants[2].ID, new EventCoordinates(index["e21"], 2))
            });

            // "f1 firstDescendants not good"
            f1.FirstDescendants.Values.ShouldCompareTo(expectedFirstDescendants.Values);

            // "f1 lastAncestors not good"
            f1.FirstDescendants.Values.ShouldCompareTo(expectedFirstDescendants.Values);

            //--------
            var expectedUndeterminedEvents = new[]
            {
                index["e0"],
                index["e1"],
                index["e2"],
                index["e10"],
                index["s20"],
                index["s00"],
                index["e21"],
                index["e02"],
                index["s10"],
                index["f1"],
                index["s11"]
            };

            var i = 0;

            foreach (var eue in expectedUndeterminedEvents)
            {
                var ue = h.UndeterminedEvents[i];

                if (ue != eue)
                {
                    logger.Fatal("UndeterminedEvents[{i}] should be {eue}, not {ue}", i, eue, ue);
                    Assert.Equal(eue, ue);
                }

                i++;
            }

            //Pending loaded Events
            // 3 Events with index 0,
            // 1 Event with non-empty Transactions
            //= 4 Loaded Events
            var ple = h.PendingLoadedEvents;
            Assert.Equal(4, ple);
        }

        [Fact]
        public async Task TestReadWireInfo()
        {
            var (h, index) = await InitRoundHashgraph();

            int k = 0;
            foreach (var evh in index)
            {
                var (ev, err1) = await h.Store.GetEvent(evh.Value);

                Assert.Null(err1);

                //logger.Debug("ev={ev}",ev.DumpToString());

                var evWire = ev.ToWire();

                //logger.Debug("evWire={evWire}",evWire.DumpToString());

                var (evFromWire, err2) = await h.ReadWireInfo(evWire);
                Assert.Null(err2);

                //"Error converting %s.Body.BlockSignatures from light wire"
                evFromWire.Body.BlockSignatures.ShouldCompareTo(ev.Body.BlockSignatures);

                //"Error converting %s.Body from light wire"
                evFromWire.Body.ShouldCompareTo(ev.Body);

                //"Error converting %s.Signature from light wire"
                evFromWire.Signiture.ShouldCompareTo(ev.Signiture);

                var (ok, err3) = ev.Verify();

                Assert.True(ok, $"Error verifying signature for {k} from light wire: {err3?.Message}");

                k++;
            }
        }

        [Fact]
        public async Task TestStronglySee()
        {
            var (h, index) = await InitRoundHashgraph();

            var expected = new[]
            {
                new AncestryItem("e21", "e0", true, false),
                new AncestryItem("e02", "e10", true, false),
                new AncestryItem("e02", "e0", true, false),
                new AncestryItem("e02", "e1", true, false),
                new AncestryItem("f1", "e21", true, false),
                new AncestryItem("f1", "e10", true, false),
                new AncestryItem("f1", "e0", true, false),
                new AncestryItem("f1", "e1", true, false),
                new AncestryItem("f1", "e2", true, false),
                new AncestryItem("s11", "e2", true, false),
                //false negatives
                new AncestryItem("e10", "e0", false, false),
                new AncestryItem("e21", "e1", false, false),
                new AncestryItem("e21", "e2", false, false),
                new AncestryItem("e02", "e2", false, false),
                new AncestryItem("s11", "e02", false, false),
                new AncestryItem("s11", "", false, true)
            };

            foreach (var exp in expected)
            {
                var (a, err) = await h.StronglySee(index.GetOrEmpty(exp.Descendant), index.GetOrEmpty(exp.Ancestor));
                if (err != null && !exp.Err)
                {
                    logger.Fatal("Error computing stronglySee({d}, {a}). Err: {err}", exp.Descendant, exp.Ancestor, err);
                    Assert.False(err != null && !exp.Err);
                }

                if (a != exp.Val)
                {
                    logger.Fatal("stronglySee(%s, %s) should be %v, not %v", exp.Descendant, exp.Ancestor, exp.Val, a);
                    Assert.Equal(exp.Val, a);
                }
            }
        }

        [Fact]
        public async Task TestWitness()
        {
            var (h, index) = await InitRoundHashgraph();

            var round0Witnesses = new Dictionary<string, RoundEvent>();

            round0Witnesses[index["e0"]] = new RoundEvent {Witness = true, Famous = null};
            round0Witnesses[index["e1"]] = new RoundEvent {Witness = true, Famous = null};
            round0Witnesses[index["e2"]] = new RoundEvent {Witness = true, Famous = null};

            await h.Store.SetRound(0, new RoundInfo {Events = round0Witnesses});

            var round1Witnesses = new Dictionary<string, RoundEvent>();

            round1Witnesses[index["f1"]] = new RoundEvent {Witness = true, Famous = null};

            await h.Store.SetRound(1, new RoundInfo {Events = round1Witnesses});

            var expected = new[]
            {
                new AncestryItem("", "e0", true, false),
                new AncestryItem("", "e1", true, false),
                new AncestryItem("", "e2", true, false),
                new AncestryItem("", "f1", true, false),
                new AncestryItem("", "e10", false, false),
                new AncestryItem("", "e21", false, false),
                new AncestryItem("", "e02", false, false)
            };

            foreach (var exp in expected)
            {
                var (a, err) = await h.Witness(index.GetOrEmpty(exp.Ancestor));
                if (err != null)
                {
                    logger.Fatal("Error computing witness({d}). Err: {err}", exp.Ancestor, err);
                    Assert.False(err != null && !exp.Err);
                }

                if (a != exp.Val)
                {
                    logger.Fatal("witness({d}) should be {val}, not {a}", exp.Ancestor, exp.Val, a);
                    Assert.Equal(exp.Val, a);
                }
            }
        }

        [Fact]
        public async Task TestRound()
        {
            var (h, index) = await InitRoundHashgraph();

            var round0Witnesses = new Dictionary<string, RoundEvent>();

            round0Witnesses[index["e0"]] = new RoundEvent {Witness = true, Famous = null};
            round0Witnesses[index["e1"]] = new RoundEvent {Witness = true, Famous = null};
            round0Witnesses[index["e2"]] = new RoundEvent {Witness = true, Famous = null};
            await h.Store.SetRound(0, new RoundInfo {Events = round0Witnesses});

            var expected = new[]
            {
                new RoundItem("e0", 0),
                new RoundItem("e1", 0),
                new RoundItem("e2", 0),
                new RoundItem("s00", 0),
                new RoundItem("e10", 0),
                new RoundItem("s20", 0),
                new RoundItem("e21", 0),
                new RoundItem("e02", 0),
                new RoundItem("s10", 0),
                new RoundItem("f1", 1),
                new RoundItem("s11", 1)
            };

            foreach (var exp in expected)
            {
                var (r, err) = await h.Round(index.GetOrEmpty(exp.Event));
                if (err != null)
                {
                    logger.Fatal("Error computing round({d}). Err: {err}", exp.Event, err);
                    Assert.False(err != null);
                }

                if (r != exp.Round)
                {
                    logger.Fatal("round({d}) should be {val}, not {a}", exp.Event, exp.Round, r);
                    Assert.Equal(exp.Round, r);
                }
            }
        }

        [Fact]
        public async Task TestRoundDiff()
        {
            var (h, index) = await InitRoundHashgraph();

            var round0Witnesses = new Dictionary<string, RoundEvent>();

            round0Witnesses[index["e0"]] = new RoundEvent {Witness = true, Famous = null};
            round0Witnesses[index["e1"]] = new RoundEvent {Witness = true, Famous = null};
            round0Witnesses[index["e2"]] = new RoundEvent {Witness = true, Famous = null};
            await h.Store.SetRound(0, new RoundInfo {Events = round0Witnesses});

            var (d, err) = await h.RoundDiff(index["f1"], index["e02"]);

            if (d != 1)
            {
                if (err != null)
                {
                    throw new AssertActualExpectedException(null, err, "RoundDiff(f1, e02) returned an error");
                }

                throw new AssertActualExpectedException(1, d, "RoundDiff(f1, e02) should be 1");
            }

            (d, err) = await h.RoundDiff(index["e02"], index["f1"]);

            if (d != -1)
            {
                if (err != null)
                {
                    throw new AssertActualExpectedException(null, err, "RoundDiff(e02, f1) returned an error");
                }

                throw new AssertActualExpectedException(-1, d, "RoundDiff(e02, f1) should be -1");
            }

            (d, err) = await h.RoundDiff(index["e02"], index["e21"]);

            if (d != 0)
            {
                if (err != null)
                {
                    throw new AssertActualExpectedException(null, err, "RoundDiff(e20, e21) returned an error");
                }

                throw new AssertActualExpectedException(0, d, "RoundDiff(e20, e21) should be 0");
            }
        }

        [Fact]
        public async Task TestDivideRounds()
        {
            var (h, index) = await InitRoundHashgraph();

            var err = await h.DivideRounds();

            Assert.Null(err);

            var l = h.Store.LastRound();

            Assert.Equal(1, l);

            RoundInfo round0;
            (round0, err) = await h.Store.GetRound(0);

            Assert.Null(err);

            l = round0.Witnesses().Length;
            Assert.Equal(3, l);

            Assert.Contains(index["e0"], round0.Witnesses());

            Assert.Contains(index["e1"], round0.Witnesses());

            Assert.Contains(index["e2"], round0.Witnesses());

            RoundInfo round1;
            (round1, err) = await h.Store.GetRound(1);

            Assert.Null(err);

            l = round1.Witnesses().Length;

            Assert.Equal(1, l);

            Assert.Contains(index["f1"], round1.Witnesses());

            var expectedPendingRounds = new[]
            {
                new PendingRound
                {
                    Index = 0,
                    Decided = false
                },
                new PendingRound
                {
                    Index = 1,
                    Decided = false
                }
            };

            var i = 0;
            foreach (var pd in h.PendingRounds)
            {
                pd.ShouldCompareTo(expectedPendingRounds[i]);
                i++;
            }

            //[event] => {lamportTimestamp, round}
            (int t, int r) tr;
            var expectedTimestamps = new Dictionary<string, (int t, int r)>
            {
                {"e0", (0, 0)},
                {"e1", (0, 0)},
                {"e2", (0, 0)},
                {"s00", (1, 0)},
                {"e10", (1, 0)},
                {"s20", (1, 0)},
                {"e21", (2, 0)},
                {"e02", (3, 0)},
                {"s10", (2, 0)},
                {"f1", (4, 1)},
                {"s11", (5, 1)}
            };

            foreach (var ets in expectedTimestamps)
            {
                var e = ets.Key;
                var et = ets.Value;

                var (ev, err1) = await h.Store.GetEvent(index[e]);
                if (err1 != null)
                {
                    logger.Fatal("Error = {err}", err1);
                    Assert.Null(err1);
                }

                var r = ev.Round;

                if (r == null || r != et.r)
                {
                    logger.Fatal($"{e} round should be {et.r}, not {r}");
                    Assert.False(r == null || r != et.r);
                }

                var ts = ev.LamportTimestamp;
                if (ts == null || ts != et.t)
                {
                    logger.Fatal($"{e} lamportTimestamp should be {et.t}, not {ts}");
                    Assert.False(ts == null || ts != et.t);
                }
            }
        }

        /*



     e01  e12
      |   |  \
      e0  R1  e2
      |       |
      R0      R2

     */
        private async Task<(Hashgraph h, Dictionary<string, string> index)> InitDentedHashgraph()
        {
            var (nodes, index, orderedEvents, participants) = await InitHashgraphNodes(N);

            var orderedPeers = await participants.ToPeerSlice();

            foreach (var peer in orderedPeers)
            {
                {
                    index[Event.RootSelfParent(peer.ID)] = Event.RootSelfParent(peer.ID);
                }
            }

            var plays = new[]
            {
                new Play(0, 0, Event.RootSelfParent(orderedPeers[0].ID), "", "e0", null, null),
                new Play(2, 0, Event.RootSelfParent(orderedPeers[2].ID), "", "e2", null, null),
                new Play(0, 1, "e0", "", "e01", null, null),
                new Play(1, 0, Event.RootSelfParent(orderedPeers[1].ID), "e2", "e12", null, null)
            };

            PlayEvents(plays, nodes, index, orderedEvents);

            var hashgraph = await CreateHashgraph(false, orderedEvents, participants, logger);

            return (hashgraph, index);
        }

        [Fact]
        public async Task TestCreateRootBis()
        {
            var (h, index) = await InitDentedHashgraph();

            var participants = await h.Participants.ToPeerSlice();

            var expected = new Dictionary<string, Root>
            {
                {
                    "e12", new Root
                    {
                        NextRound = 0,
                        SelfParent = RootEvent.NewBaseRootEvent(participants[1].ID),
                        Others = new Dictionary<string, RootEvent>
                        {
                            {index["e12"], new RootEvent {Hash = index["e2"], CreatorId = participants[2].ID, Index = 0, Round = 0, LamportTimestamp = 0}}
                        }
                    }
                }
            };

            foreach (var ex in expected)

            {
                var evh = ex.Key;
                var expRoot = ex.Value;

                var (ev, err1) = await h.Store.GetEvent(index[evh]);
                if (err1 != null)
                {
                    logger.Fatal(err1.ToString());
                    Assert.Null(err1);
                }

                var (root, err2) = await h.CreateRoot(ev);
                if (err2 != null)
                {
                    logger.Fatal($"Error creating {evh} Root: {err2}");

                    Assert.Null(err1);
                }

                root.ShouldCompareTo(expRoot);
            }
        }

        /*

        e0  e1  e2    Block (0, 1)
        0   1    2
        */
        private async Task<( Hashgraph hashgraph, TestNode[] nodes, Dictionary<string, string> index)> InitBlockHashgraph()
        {
            var (nodes, index, orderedEvents, participants) = await InitHashgraphNodes(N);

            var i = 0;

            foreach (var peer in await participants.ToPeerSlice())

            {
                var ev = new Event(null, null, new[] {Event.RootSelfParent(peer.ID), ""}, nodes[i].Pub, 0);
                nodes[i].SignAndAddEvent(ev, $"e{i}", index, orderedEvents);
                i++;
            }

            var hashgraph = new Hashgraph(participants, await InmemStore.NewInmemStore(participants, CacheSize, logger), null, logger);

            //create a block and signatures manually
            var block = new Block(0, 1, "framehash".StringToBytes(), new[] {"block tx".StringToBytes()});
            var err1 = await hashgraph.Store.SetBlock(block);
            if (err1 != null)
            {
                logger.Fatal($"Error setting block. Err: {err1}");
                Assert.Null(err1);
            }

            i = 0;

            foreach (var ev in orderedEvents)

            {
                var err2 = await hashgraph.InsertEvent(ev, true);

                if (err2 != null)
                {
                    logger.Error($"ERROR inserting event {i}: {err2}");
                    Assert.Null(err1);
                }

                i++;
            }

            return (hashgraph, nodes, index);
        }

        [Fact]
        public async Task TestInsertEventsWithBlockSignatures()
        {
            var (h, nodes, index) = await InitBlockHashgraph();

            var (block, err1) = await h.Store.GetBlock(0);
            if (err1 != null)
            {
                logger.Fatal("Error retrieving block 0. %s", err1);
                Assert.Null(err1);
            }

            var blockSigs = new BlockSignature[N];

            var k = 0;
            foreach (var n in nodes)
            {
                HashgraphError err2;
                (blockSigs[k], err2) = block.Sign(n.Key);
                if (err2 != null)
                {
                    logger.Fatal(err2.ToString());
                    Assert.Null(err2);
                }

                k++;
            }

            Play p;
            Event e;

            {
                logger.Debug("Inserting Events with valid signatures");

                /*
                    s00 |   |
                    |   |   |
                    |  e10  s20
                    | / |   |
                    e0  e1  e2
                    0   1    2
                */

                var plays = new[]
                {
                    new Play(1, 1, "e1", "e0", "e10", null, new[] {blockSigs[1]}),
                    new Play(2, 1, "e2", "", "s20", null, new[] {blockSigs[2]}),
                    new Play(0, 1, "e0", "", "s00", null, new[] {blockSigs[0]})
                };

                foreach (var p1 in plays)
                {
                    p = p1;
                    e = new Event(p.TxPayload, p.SigPayload, new[] {index.GetOrEmpty(p.SelfParent), index.GetOrEmpty(p.OtherParent)}, nodes[p.To].Pub, p.Index);
                    e.Sign(nodes[p.To].Key);
                    index[p.Name] = e.Hex();

                    var err3 = await h.InsertEvent(e, true);

                    if (err3 != null)
                    {
                        logger.Fatal($"ERROR inserting event {p.Name}:{err3}");
                        Assert.Null(err3);
                    }
                }

                //Check SigPool
                {
                    var l = h.SigPool.Count;

                    if (l != 3)
                    {
                        logger.Fatal("SigPool should contain 3 signatures, not %d", l);
                        Assert.Equal(3, l);
                    }
                }

                //Process SigPool
                await h.ProcessSigPool();

                //Check that the block contains 3 signatures
                {
                    (block, _) = await h.Store.GetBlock(0);

                    var l = block.Signatures.Count;

                    if (l != 3)
                    {
                        logger.Fatal($"Block 0 should contain 3 signatures, not {l}");
                        Assert.Equal(3, l);
                    }

                    //Check that SigPool was cleared
                }
                {
                    var l = h.SigPool.Count;

                    if (l != 0)
                    {
                        logger.Fatal($"SigPool should contain 0 signatures, not {l}");
                        Assert.Equal(3, l);
                    }
                }
            }

            {
                logger.Debug("Inserting Events with signature of unknown block");

                //The Event should be inserted
                //The block signature is simply ignored

                var block1 = new Block(1, 2, "framehash".StringToBytes(), new byte[][] { });
                var (sig, _) = block1.Sign(nodes[2].Key);

                //unknown block
                var unknownBlockSig = new BlockSignature
                {
                    Validator = nodes[2].Pub,
                    Index = 1,
                    Signature = sig.Signature
                };

                p = new Play(2, 2, "s20", "e10", "e21", null, new[] {unknownBlockSig});

                e = new Event(null, p.SigPayload,
                    new[]
                    {
                        index.GetOrEmpty(p.SelfParent),
                        index.GetOrEmpty(p.OtherParent)
                    },
                    nodes[p.To].Pub,
                    p.Index
                );

                e.Sign(nodes[p.To].Key);
                index[p.Name] = e.Hex();

                var err4 = await h.InsertEvent(e, true);

                if (err4 != null)
                {
                    logger.Fatal($"ERROR inserting event {p.Name}: {err4}");
                    Assert.Null(err4);
                }

                //check that the event was recorded

                var (_, err2) = await h.Store.GetEvent(index["e21"]);
                if (err2 != null)
                {
                    logger.Fatal($"ERROR fetching Event e21: {err2}");
                    Assert.Null(err2);
                }
            }

            {
                logger.Debug("Inserting Events with BlockSignature not from creator");

                //The Event should be inserted
                //The block signature is simply ignored

                //wrong validator
                //Validator should be same as Event creator (node 0)
                var key = CryptoUtils.GenerateEcdsaKey();
                var badNode = new TestNode(key, 666);
                var (badNodeSig, _) = block.Sign(badNode.Key);

                p = new Play(0, 2, "s00", "e21", "e02", null, new[] {badNodeSig});

                e = new Event(null,
                    p.SigPayload,
                    new[] {index.GetOrEmpty(p.SelfParent), index.GetOrEmpty(p.OtherParent)},
                    nodes[p.To].Pub,
                    p.Index);

                e.Sign(nodes[p.To].Key);
                index[p.Name] = e.Hex();
                {
                    var err = await h.InsertEvent(e, true);

                    if (err != null)
                    {
                        logger.Fatal($"ERROR inserting event {p.Name}: {err}");
                        Assert.Null(err);
                    }

                    //check that the signature was not appended to the block
                }
                var (block2, _) = await h.Store.GetBlock(0);
                {
                    var l = block2.Signatures.Count;

                    if (l > 3)
                    {
                        logger.Fatal($"Block 0 should contain 3 signatures, not {l}");
                        Assert.True(l > 3);
                    }
                }
            }
        }

        /*
                          Round 4
		        i0  |   i2
		        | \ | / |
		        |   i1  |
        ------- |  /|   | --------------------------------
		        h02 |   | Round 3
		        | \ |   |
		        |   \   |
		        |   | \ |
		        |   |  h21
		        |   | / |
		        |  h10  |
		        | / |   |
		        h0  |   h2
		        | \ | / |
		        |   h1  |
        ------- |  /|   | --------------------------------
		        g02 |   | Round 2
		        | \ |   |
		        |   \   |
		        |   | \ |
	            |   |  g21
		        |   | / |
		        |  g10  |
		        | / |   |
		        g0  |   g2
		        | \ | / |
		        |   g1  |
        ------- |  /|   | -------------------------------
		        f02b|   |  Round 1           +---------+
		        |   |   |                    | Block 1 |
		        f02 |   |                    | RR    2 |
		        | \ |   |                    | Evs   9 |
		        |   \   |                    +---------+
		        |   | \ |
	        ---f0x  |   f21 //f0x's other-parent is e21b. This situation can happen with concurrency
	        |	|   | / |
	        |	|  f10  |
	        |	| / |   |
	        |	f0  |   f2
	        |	| \ | / |
	        |	|  f1b  |
	        |	|   |   |
	        |	|   f1  |
        ---	| -	|  /|   | ------------------------------
	        |	e02 |   |  Round 0          +---------+
	        |	| \ |   |                   | Block 0 |
	        |	|   \   |                   | RR    1 |
	        |	|   | \ |                   | Evs   7 |
	        |   |   | e21b                  +---------+
	        |	|   |   |
	        ---------- e21
		        |   | / |
		        |  e10  |
	            | / |   |
		        e0  e1  e2
		        0   1    2
        */

        private async Task<( Hashgraph h, Dictionary<string, string> index)> InitConsensusHashgraph(bool db)
        {
            var plays = new[]
            {
                new Play(1, 1, "e1", "e0", "e10", null, null),
                new Play(2, 1, "e2", "e10", "e21", new[] {"e21".StringToBytes()}, null),
                new Play(2, 2, "e21", "", "e21b", null, null),
                new Play(0, 1, "e0", "e21b", "e02", null, null),
                new Play(1, 2, "e10", "e02", "f1", null, null),
                new Play(1, 3, "f1", "", "f1b", new[] {"f1b".StringToBytes()}, null),
                new Play(0, 2, "e02", "f1b", "f0", null, null),
                new Play(2, 3, "e21b", "f1b", "f2", null, null),
                new Play(1, 4, "f1b", "f0", "f10", null, null),
                new Play(0, 3, "f0", "e21", "f0x", null, null),
                new Play(2, 4, "f2", "f10", "f21", null, null),
                new Play(0, 4, "f0x", "f21", "f02", null, null),
                new Play(0, 5, "f02", "", "f02b", new[] {"f02b".StringToBytes()}, null),
                new Play(1, 5, "f10", "f02b", "g1", null, null),
                new Play(0, 6, "f02b", "g1", "g0", null, null),
                new Play(2, 5, "f21", "g1", "g2", null, null),
                new Play(1, 6, "g1", "g0", "g10", new[] {"g10".StringToBytes()}, null),
                new Play(2, 6, "g2", "g10", "g21", null, null),
                new Play(0, 7, "g0", "g21", "g02", new[] {"g02".StringToBytes()}, null),
                new Play(1, 7, "g10", "g02", "h1", null, null),
                new Play(0, 8, "g02", "h1", "h0", null, null),
                new Play(2, 7, "g21", "h1", "h2", null, null),
                new Play(1, 8, "h1", "h0", "h10", null, null),
                new Play(2, 8, "h2", "h10", "h21", null, null),
                new Play(0, 9, "h0", "h21", "h02", null, null),
                new Play(1, 9, "h10", "h02", "i1", null, null),
                new Play(0, 10, "h02", "i1", "i0", null, null),
                new Play(2, 9, "h21", "i1", "i2", null, null)
            };

            var (hashgraph, index, _) = await InitHashgraphFull(plays, db, N, logger);

            return (hashgraph, index);
        }

        [Fact]
        public async Task TestDivideRoundsBis()
        {
            var (h, index) = await InitConsensusHashgraph(false);

            var err1 = await h.DivideRounds();

            if (err1 != null)
            {
                logger.Fatal(err1.ToString());
                Assert.Null(err1);
            }

            //[event] => {lamportTimestamp, round}

            var expectedTimestamps = new Dictionary<string, (int t, int r)>
            {
                {"e0", (0, 0)},
                {"e1", (0, 0)},
                {"e2", (0, 0)},
                {"e10", (1, 0)},
                {"e21", (2, 0)},
                {"e21b", (3, 0)},
                {"e02", (4, 0)},
                {"f1", (5, 1)},
                {"f1b", (6, 1)},
                {"f0", (7, 1)},
                {"f2", (7, 1)},
                {"f10", (8, 1)},
                {"f0x", (8, 1)},
                {"f21", (9, 1)},
                {"f02", (10, 1)},
                {"f02b", (11, 1)},
                {"g1", (12, 2)},
                {"g0", (13, 2)},
                {"g2", (13, 2)},
                {"g10", (14, 2)},
                {"g21", (15, 2)},
                {"g02", (16, 2)},
                {"h1", (17, 3)},
                {"h0", (18, 3)},
                {"h2", (18, 3)},
                {"h10", (19, 3)},
                {"h21", (20, 3)},
                {"h02", (21, 3)},
                {"i1", (22, 4)},
                {"i0", (23, 4)},
                {"i2", (23, 4)}
            };

            foreach (var ets in expectedTimestamps)
            {
                var e = ets.Key;
                var et = ets.Value;

                var (ev, err) = await h.Store.GetEvent(index[e]);
                if (err != null)
                {
                    logger.Fatal(err.ToString());
                }

                {
                    var r = ev.Round;

                    if (r == null || r != et.r)
                    {
                        logger.Fatal("%s round should be %d, not %d", e, et.r, r);
                    }
                }
                {
                    var ts = ev.LamportTimestamp;

                    if (ts == null || ts != et.t)
                    {
                        logger.Fatal("%s lamportTimestamp should be %d, not %d", e, et.t, ts);
                    }
                }
            }
        }

        [Fact]
        public async Task TestDecideFame()
        {
            var (h, index) = await InitConsensusHashgraph(false);

            await h.DivideRounds();
            {
                var err1 = await h.DecideFame();

                if (err1 != null)
                {
                    logger.Fatal(err1.ToString());
                }
            }
            var (round0, err2) = await h.Store.GetRound(0);
            if (err2 != null)
            {
                logger.Fatal(err2.ToString());
            }

            {
                var f = round0.Events[index["e0"]];

                if (!(f.Witness && f.Famous == true))
                {
                    logger.Fatal("e0 should be famous; got %v", f);
                }
            }
            {
                var f = round0.Events[index["e1"]];

                if (!(f.Witness && f.Famous == true))
                {
                    logger.Fatal("e1 should be famous; got %v", f);
                }
            }
            {
                var f = round0.Events[index["e2"]];

                if (!(f.Witness && f.Famous == true))
                {
                    logger.Fatal("e2 should be famous; got %v", f);
                }
            }
            var (round1, err3) = await h.Store.GetRound(1);
            if (err3 != null)
            {
                logger.Fatal(err3.ToString());
            }

            {
                var f = round1.Events[index["f0"]];

                if (!(f.Witness && f.Famous == true))
                {
                    logger.Fatal("f0 should be famous; got %v", f);
                }
            }
            {
                var f = round1.Events[index["f1"]];

                if (!(f.Witness && f.Famous == true))
                {
                    logger.Fatal("f1 should be famous; got %v", f);
                }
            }
            {
                var f = round1.Events[index["f2"]];

                if (!(f.Witness && f.Famous == true))
                {
                    logger.Fatal("f2 should be famous; got %v", f);
                }
            }
            var (round2, err4) = await h.Store.GetRound(2);
            if (err4 != null)
            {
                logger.Fatal(err4.ToString());
            }

            {
                var f = round2.Events[index["g0"]];

                if (!(f.Witness && f.Famous == true))
                {
                    logger.Fatal("g0 should be famous; got %v", f);
                }
            }
            {
                var f = round2.Events[index["g1"]];

                if (!(f.Witness && f.Famous == true))
                {
                    logger.Fatal("g1 should be famous; got %v", f);
                }
            }
            {
                var f = round2.Events[index["g2"]];

                if (!(f.Witness && f.Famous == true))
                {
                    logger.Fatal("g2 should be famous; got %v", f);
                }
            }
            var expectedpendingRounds = new[]
            {
                new PendingRound {Index = 0, Decided = true},
                new PendingRound {Index = 1, Decided = true},
                new PendingRound {Index = 2, Decided = true},
                new PendingRound {Index = 3, Decided = false},
                new PendingRound {Index = 4, Decided = false}
            };

            var i = 0;
            foreach (var pd in h.PendingRounds)
            {
                pd.ShouldCompareTo(expectedpendingRounds[i]);

                //    t.Fatalf("pendingRounds[%d] should be %v, not %v", i, expectedpendingRounds[i], pd.Deref);
                i++;
            }
        }

        [Fact]
        public async Task TestDecideRoundReceived()
        {
            var (h, index) = await InitConsensusHashgraph(false);

            await h.DivideRounds();
            await h.DecideFame();

            var err1 = await h.DecideRoundReceived();

            if (err1 != null)
            {
                logger.Fatal(err1.ToString());
                Assert.Null(err1);
            }

            foreach (var ind in index)
            {
                var name = ind.Key;
                var hash = ind.Value;

                var (e, _) = await h.Store.GetEvent(hash);
                if (name.StartsWith("e"))
                {
                    {
                        var r = e.GetRoundReceived();

                        if (r != 1)
                        {
                            logger.Fatal($"{name} round received should be 1 not {r}");
                            Assert.Equal(1, r);
                        }
                    }
                }
                else if (name.StartsWith("f"))
                {
                    {
                        var r = e.GetRoundReceived();

                        if (r != 2)
                        {
                            logger.Fatal($"{name} round received should be 2 not {r}");
                            Assert.Equal(2, r);
                        }
                    }
                }
                else if (e.GetRoundReceived() != null)
                {
                    logger.Fatal($"{name} round received should be null not {e.GetRoundReceived()}");
                    Assert.Null(e.GetRoundReceived());
                }
            }

            var (round0, err2) = await h.Store.GetRound(0);
            if (err2 != null)
            {
                logger.Fatal($"Could not retrieve Round 0. {err2}");
                Assert.Null(err2);
            }

            {
                var ce = round0.ConsensusEvents().Length;

                if (ce != 0)
                {
                    logger.Fatal($"Round 0 should contain 0 ConsensusEvents, not {ce}");
                    Assert.Equal(0, ce);
                }
            }

            var (round1, err3) = await h.Store.GetRound(1);
            if (err3 != null)
            {
                logger.Fatal($"Could not retrieve Round 1. {err3}");
                Assert.Null(err3);
            }

            {
                var ce = round1.ConsensusEvents().Length;

                if (ce != 7)
                {
                    logger.Fatal($"Round 1 should contain 7 ConsensusEvents, not {ce}");
                    Assert.Equal(7, ce);
                }
            }
            var (round2, err4) = await h.Store.GetRound(2);
            if (err4 != null)
            {
                logger.Fatal($"Could not retrieve Round 2. {err4}");
                Assert.Null(err4);
            }

            {
                var ce = round2.ConsensusEvents().Length;

                if (ce != 9)
                {
                    logger.Fatal($"Round 1 should contain 9 ConsensusEvents, not {ce}");
                    Assert.Equal(9, ce);
                }
            }
            var expectedUndeterminedEvents = new[]
            {
                index["g1"],
                index["g0"],
                index["g2"],
                index["g10"],
                index["g21"],
                index["g02"],
                index["h1"],
                index["h0"],
                index["h2"],
                index["h10"],
                index["h21"],
                index["h02"],
                index["i1"],
                index["i0"],
                index["i2"]
            };

            var i = 0;
            foreach (var eue in expectedUndeterminedEvents)

            {
                var ue = h.UndeterminedEvents[i];

                if (ue != eue)
                {
                    logger.Fatal("UndeterminedEvents[{i}] should be {eue}, not {ue}");
                    Assert.Equal(eue, ue);
                }

                i++;
            }
        }

        [Fact]
        public async Task TestProcessDecidedRounds()
        {
            var (h, index) = await InitConsensusHashgraph(false);

            await h.DivideRounds();
            await h.DecideFame();
            await h.DecideRoundReceived();
            {
                var err1 = await h.ProcessDecidedRounds();

                if (err1 != null)
                {
                    logger.Fatal(err1.ToString());
                }

                //--------------------------------------------------------------------------
            }
            var consensusEvents = h.Store.ConsensusEvents();

            {
                var i = 0;
                foreach (var e in consensusEvents)
                {
                    logger.Debug($"consensus[{i}]: {GetName(index, e)}");
                    i++;
                }
            }

            {
                var l = consensusEvents.Length;

                if (l != 16)
                {
                    logger.Fatal("length of consensus should be 16 not %d", l);
                    Assert.Equal(16, l);
                }
            }

            {
                var ple = h.PendingLoadedEvents;

                if (ple != 2)
                {
                    logger.Fatal("PendingLoadedEvents should be 2, not %d", ple);
                    Assert.Equal(2, ple);
                }

                //Block 0 ------------------------------------------------------------------
            }
            var (block0, err2) = await h.Store.GetBlock(0);
            if (err2 != null)
            {
                logger.Fatal("Store should contain a block with Index 0: %v", err2);
            }

            {
                var ind = block0.Index();

                if (ind != 0)
                {
                    logger.Fatal("Block0's Index should be 0, not %d", ind);
                }
            }
            {
                var rr = block0.RoundReceived();

                if (rr != 1)
                {
                    logger.Fatal("Block0's RoundReceived should be 1, not %d", rr);
                }
            }
            {
                var l = block0.Transactions().Length;

                if (l != 1)
                {
                    logger.Fatal("Block0 should contain 1 transaction, not %d", l);
                }
            }
            {
                var tx = block0.Transactions()[0];

                tx.ShouldCompareTo("e21".StringToBytes());
                //ogger.Fatal("Block0.Transactions[0] should be 'e21', not %s", tx);
            }
            var (frame1, err3) = await h.GetFrame(block0.RoundReceived());
            var frame1Hash = frame1.Hash();

            block0.FrameHash().ShouldCompareTo(frame1Hash);
            // logger.Fatal("Block0.FrameHash should be %v, not %v", frame1Hash, block0.FrameHash());

            //Block 1 ------------------------------------------------------------------
            var (block1, err4) = await h.Store.GetBlock(1);
            if (err4 != null)
            {
                logger.Fatal("Store should contain a block with Index 1: %v", err4);
            }

            {
                var ind = block1.Index();

                if (ind != 1)
                {
                    logger.Fatal("Block1's Index should be 1, not %d", ind);
                }
            }
            {
                var rr = block1.RoundReceived();

                if (rr != 2)
                {
                    logger.Fatal("Block1's RoundReceived should be 2, not %d", rr);
                }
            }
            {
                var l = block1.Transactions().Length;

                if (l != 2)
                {
                    logger.Fatal("Block1 should contain 2 transactions, not %d", l);
                }
            }
            {
                var tx = block1.Transactions()[1];

                tx.ShouldCompareTo("f02b".StringToBytes());
                //  logger.Fatal("Block1.Transactions[1] should be 'f02b', not %s", tx);
            }
            var (frame2, err5) = await h.GetFrame(block1.RoundReceived());
            var frame2Hash = frame2.Hash();

            block1.FrameHash().ShouldCompareTo(frame2Hash);
            //t.Fatalf("Block1.FrameHash should be %v, not %v", frame2Hash, block1.FrameHash());

            // pendingRounds -----------------------------------------------------------
            var expectedpendingRounds = new[]
            {
                new PendingRound {Index = 3, Decided = false},
                new PendingRound {Index = 4, Decided = false}
            };
            {
                var i = 0;
                foreach (var pd in h.PendingRounds)
                {
                    pd.ShouldCompareTo(expectedpendingRounds[i]);
                    //t.Fatalf("pendingRounds[%d] should be %v, not %v", i, expectedpendingRounds[i], pd.Deref);
                    i++;
                }

                //Anchor -------------------------------------------------------------------
            }

            //Anchor -------------------------------------------------------------------
            {
                var v = h.AnchorBlock;

                if (v != null)
                {
                    logger.Fatal("AnchorBlock should be null, not %v", v);
                }
            }
        }

        //   [Fact]
        //public async Task BenchmarkConsensus()
        //{
        //    for (var n = 0; n < N; n++)
        //    {
        //        //we do not want to benchmark the initialization code
        //        b.StopTimer();
        //        var (h, _) = initConsensusHashgraph(@false, b);
        //        b.StartTimer();

        //        h.DivideRounds();
        //        h.DecideFame();
        //        h.DecideRoundReceived();
        //        h.ProcessDecidedRounds();
        //    }

        //}

        [Fact]
        public async Task TestKnown()
        {
            var (h, _) = await InitConsensusHashgraph(false);

            var participants = await h.Participants.ToPeerSlice();

            var expectedKnown = new Dictionary<int, int>
            {
                {participants[0].ID, 10},
                {participants[1].ID, 9},
                {participants[2].ID, 9}
            };

            var known = await h.Store.KnownEvents();

            foreach (var i in await h.Participants.ToIdSlice())
            {
                var l = known[i];
                Assert.True(l == expectedKnown[i], $"KnownEvents[{i}] should be {expectedKnown[i]}, not {l}");
            }
        }

        [Fact]
        public async Task TestGetFrame()
        {
            var (h, index) = await InitConsensusHashgraph(false);

            var participants = await h.Participants.ToPeerSlice();

            await h.DivideRounds();

            await h.DecideFame();

            await h.DecideRoundReceived();

            await h.ProcessDecidedRounds();

            // Round1

            var expectedRoots = new Root[N];
            for (int i = 0; i < 3; i++)
            {
                expectedRoots[i] = Root.NewBaseRoot(participants[i].ID);
            }

            var (frame, err1) = await h.GetFrame(1);
            err1.ShouldNotBeError();

            var p = 0;
            foreach (var r in frame.Roots)
            {
                var er = expectedRoots[p];

                r.SelfParent.ShouldCompareTo(er.SelfParent);
                r.Others.ShouldCompareTo(er.Others);

                p++;
            }

            var expectedEventsHashes = new[]
            {
                index["e0"],
                index["e1"],
                index["e2"], index["e10"],
                index["e21"],
                index["e21b"],
                index["e02"]
            };

            var expectedEvents = new List<Event>();

            foreach (var eh in expectedEventsHashes)
            {
                var (e, err2) = await h.Store.GetEvent(eh);
                err2.ShouldNotBeError();
                expectedEvents.Add(e);
            }

            expectedEvents.Sort(new Event.EventByLamportTimeStamp());

            frame.Events.ShouldCompareTo(expectedEvents.ToArray(), "Frame.Events is not good");

            var (block0, err3) = await h.Store.GetBlock(0);

            err3.ShouldNotBeError($"Store should contain a block with Index 1: {err3}");

            var frame1Hash = frame.Hash();
            frame1Hash.ShouldCompareTo(block0.FrameHash(), $"Block0.FrameHash ({block0.FrameHash()}) and Frame1.Hash ({frame1Hash}) differ");

            // Round 2

            expectedRoots = new Root[N];

            expectedRoots[0] = new Root
            {
                NextRound = 1,
                SelfParent = new RootEvent
                {
                    Hash = index["e02"],
                    CreatorId = participants[0].ID,
                    Index = 1,
                    LamportTimestamp = 4,
                    Round = 0
                },
                Others = new Dictionary<string, RootEvent>
                {
                    {
                        index["f0"], new RootEvent
                        {
                            Hash = index["f1b"],
                            CreatorId = participants[1].ID,
                            Index = 3,
                            LamportTimestamp = 6,
                            Round = 1
                        }
                    },
                    {
                        index["f0x"], new RootEvent
                        {
                            Hash = index["e21"],
                            CreatorId = participants[2].ID,
                            Index = 1,
                            LamportTimestamp = 2,
                            Round = 0
                        }
                    }
                }
            };

            expectedRoots[1] = new Root
            {
                NextRound = 1,
                SelfParent = new RootEvent
                {
                    Hash = index["e10"],
                    CreatorId = participants[1].ID,
                    Index = 1,
                    LamportTimestamp = 1,
                    Round = 0
                },
                Others = new Dictionary<string, RootEvent>
                {
                    {
                        index["f1"], new RootEvent
                        {
                            Hash = index["e02"],
                            CreatorId = participants[0].ID,
                            Index = 1,
                            LamportTimestamp = 4,
                            Round = 0
                        }
                    }
                }
            };

            expectedRoots[2] = new Root
            {
                NextRound = 1,
                SelfParent = new RootEvent
                {
                    Hash = index["e21b"],
                    CreatorId = participants[2].ID,
                    Index = 2,
                    LamportTimestamp = 3,
                    Round = 0
                },
                Others = new Dictionary<string, RootEvent>
                {
                    {
                        index["f2"], new RootEvent
                        {
                            Hash = index["f1b"],
                            CreatorId = participants[1].ID,
                            Index = 3,
                            LamportTimestamp = 6,
                            Round = 1
                        }
                    }
                }
            };

            BabbleError err4;
            (frame, err4) = await h.GetFrame(2);
            err4.ShouldNotBeError();

            p = 0;
            foreach (var r in frame.Roots)
            {
                var er = expectedRoots[p];

                r.SelfParent.ShouldCompareTo(er.SelfParent);
                r.Others.ShouldCompareTo(er.Others);

                p++;
            }

            expectedEventsHashes = new[]
            {
                index["f1"],
                index["f1b"],
                index["f0"],
                index["f2"],
                index["f10"],
                index["f0x"],
                index["f21"],
                index["f02"],
                index["f02b"]
            };

            expectedEvents = new List<Event>();

            foreach (var eh in expectedEventsHashes)
            {
                var (e, err5) = await h.Store.GetEvent(eh);
                err5.ShouldNotBeError();
                expectedEvents.Add(e);
            }

            expectedEvents.Sort(new Event.EventByLamportTimeStamp());

            frame.Events.ShouldCompareTo(expectedEvents.ToArray(), "Frame.Events is not good");
        }

        [Fact]
        public async Task TestResetFromFrame()
        {
            var (h, index) = await InitConsensusHashgraph(false);

            var participants = await h.Participants.ToPeerSlice();

            await h.DivideRounds();

            await h.DecideFame();

            await h.DecideRoundReceived();

            await h.ProcessDecidedRounds();

            var (block, err1) = await h.Store.GetBlock(1);
            err1.ShouldNotBeError();

            var (frame, err2) = await h.GetFrame(block.RoundReceived());
            err2.ShouldNotBeError();

            //This operation clears the private fields which need to be recomputed
            //in the Events (round, roundReceived,etc)
            var marshalledFrame = frame.Marshal();
            var unmarshalledFrame = Frame.Unmarshal(marshalledFrame);

            var h2 = new Hashgraph(h.Participants, await InmemStore.NewInmemStore(h.Participants, CacheSize, logger), null, logger);
            var err3 = await h2.Reset(block, unmarshalledFrame);
            err3.ShouldNotBeError();

            /*
                The hashgraph should now look like this:

                          f02b|   |
                          |   |   |
                          f02 |   |
                          | \ |   |
                          |   \   |
                          |   | \ |
                   +--f0x  |   f21 //f0x's other-parent is e21b; contained in R0
                   |   |   | / |
                   |   |  f10  |
                   |   | / |   |
                   |   f0  |   f2
                   |   | \ | / |
                   |   |  f1b  |
                   |   |   |   |
                   |   |   f1  |
                   |   |   |   |
                   +-- R0  R1  R2
            */

            //Test Known
            var expectedKnown = new Dictionary<int, int>
            {
                {participants[0].ID, 5},
                {participants[1].ID, 4},
                {participants[2].ID, 4}
            };

            var known = await h2.Store.KnownEvents();
            {
                ForRangeMap(h2.Participants.ById, (key, peer) =>
                {
                    var l = known[peer.ID];
                    Assert.True(expectedKnown[peer.ID] == l, $"Known[{peer.ID}] should be {expectedKnown[peer.ID]}, not {l}");
                });
            }

            /***************************************************************************
             Test DivideRounds
            ***************************************************************************/

            var err4 = await h2.DivideRounds();
            err4.ShouldNotBeError();

            var (hRound1, err5) = await h.Store.GetRound(1);
            err5.ShouldNotBeError();

            var (h2Round1, err6) = await h2.Store.GetRound(1);
            err6.ShouldNotBeError();

            //Check Round1 Witnesses
            var hWitnesses = hRound1.Witnesses().OrderBy(o => o).ToArray();
            var h2Witnesses = h2Round1.Witnesses().OrderBy(o => o).ToArray();
            ;

            h2Witnesses.ShouldCompareTo(hWitnesses, $"Reset Hg Round 1 witnesses should be {hWitnesses}, not {h2Witnesses}");

            //check Event Rounds and LamportTimestamps
            await ForRange(frame.Events, async (i, ev) =>
            {
                var (h2r, err7) = await h2.Round(ev.Hex());
                err7.ShouldNotBeError($"Error computing {GetName(index, ev.Hex())} Round: {h2r}");

                var (hr, _) = await h.Round(ev.Hex());

                Assert.True(h2r == hr, $"h2[{GetName(index, ev.Hex())}].Round should be {hr}, not {h2r}");

                var (h2s, err8) = await h2.LamportTimestamp(ev.Hex());
                err8.ShouldNotBeError($"Error computing {GetName(index, ev.Hex())} LamportTimestamp: {h2s}");

                var (hs, _) = await h.LamportTimestamp(ev.Hex());
                Assert.True(h2s == hs, $"h2[{GetName(index, ev.Hex())}].LamportTimestamp should be {hs}, not {h2s}");
            });

            /***************************************************************************
            Test Consensus
            ***************************************************************************/

            await h2.DecideFame();

            await h2.DecideRoundReceived();
            await h2.ProcessDecidedRounds();

            var lbi = await h2.Store.LastBlockIndex();

            Assert.True(lbi == block.Index(), $"LastBlockIndex should be {block.Index()}, not {lbi}");

            var lcr = h2.LastConsensusRound;

            Assert.True(lcr != null || lcr == block.RoundReceived(),$"LastConsensusRound should be {block.RoundReceived()}, not {lcr}");

            var v = h2.AnchorBlock;
            Assert.True(v == null, $"AnchorBlock should be nil, not {v}");

            /***************************************************************************
            Test continue after Reset
            ***************************************************************************/
            //Insert remaining Events into the Reset hashgraph
           
            int r;

            for (r = 2; r <= 4; r++)
            {
                var (round, err9) = await h.Store.GetRound(r);
                err9.ShouldNotBeError();

                var events = new List<Event>();

                await ForRange(round.RoundEvents(), async (i, e) =>
                {
                    var (ev, err10) = await h.Store.GetEvent(e);
                    err10.ShouldNotBeError();
                    events.Add(ev);
                    logger.Debug(string.Format("R%d %s", r, GetName(index, e)));
                });

                events.Sort(new Event.EventByTopologicalOrder());

                await ForRange(events, async (i, ev) =>
                {
                    var marshalledEv = ev.Marhsal();
                    var unmarshalledEv = Event.Unmarshal(marshalledEv);
                    var err11 = await h2.InsertEvent(unmarshalledEv, true);
                    err11.ShouldNotBeError(string.Format("ERR Inserting Event %s: %v", GetName(index, ev.Hex()), err11));
                });
            }

            await h2.DivideRounds();
            await h2.DecideFame();
            await h2.DecideRoundReceived();
            await h2.ProcessDecidedRounds();

            for (r = 1; r <= 4; r++)
            {
                var (hRound, err12) = await h.Store.GetRound(r);
                err12.ShouldNotBeError();

                var (h2Round, err13) = await h2.Store.GetRound(r);
                err12.ShouldNotBeError();

                hWitnesses = hRound.Witnesses().OrderBy(o => o).ToArray();
                h2Witnesses = h2Round.Witnesses().OrderBy(o => o).ToArray();

                h2Witnesses.ShouldCompareTo(hWitnesses, string.Format("Reset Hg Round %d witnesses should be %v, not %v", r, hWitnesses, h2Witnesses));
            }
        }

//        [Fact]
        //        public async Task TestBootstrap()
        //        {
        //            var dbPath = GetPath();

        //            //Initialize a first Hashgraph with a DB backend
        //            //Set events and run consensus methods on it
        //            var (h, _) = await InitConsensusHashgraph(true, dbPath, logger);

        //            using (var tx = h.Store.BeginTx())
        //            {
        //                await h.DivideRounds();

        //                await h.DecideFame();

        //                await h.FindOrder();

        //                tx.Commit();
        //            }

        //            h.Store.Close();

        //            Exception err;

        //            logger.Debug("------- RecycledStore -------");

        //            //Now we want to create a new Hashgraph based on the database of the previous
        //            //Hashgraph and see if we can boostrap it to the same state.
        //            IStore recycledStore;
        //            (recycledStore, err) = await LocalDbStore.Load(CacheSize, dbPath, logger);

        //            Assert.Null(err);

        //            Assert.Equal(h.Store.Participants().participants.Count, recycledStore.Participants().participants.Count);

        //            foreach (var p in h.Store.Participants().participants)
        //            {
        //                Assert.Equal(recycledStore.Participants().participants[p.Key], p.Value);
        //            }

        //            var nh = new Hashgraph(recycledStore.Participants().participants, recycledStore, null, logger);

        //            err = await nh.Bootstrap();

        //            Assert.Null(err);

        //            var hConsensusEvents = h.ConsensusEvents();

        //            var nhConsensusEvents = nh.ConsensusEvents();

        //            Assert.Equal(hConsensusEvents.Length, nhConsensusEvents.Length);

        //            var hKnown = await h.KnownEvents();

        //            var nhKnown = await nh.KnownEvents();

        //            Assert.Equal(hKnown.Count, nhKnown.Count);

        //            foreach (var p in hKnown)
        //            {
        //                Assert.Equal(nhKnown[p.Key], p.Value);
        //            }

        //            Assert.Equal(h.LastConsensusRound, nh.LastConsensusRound);

        //            Assert.Equal(h.LastCommitedRoundEvents, nh.LastCommitedRoundEvents);

        //            Assert.Equal(h.ConsensusTransactions, nh.ConsensusTransactions);

        //            Assert.Equal(h.PendingLoadedEvents, nh.PendingLoadedEvents);
        //        }

        /*
     
         This example demonstrates that a Round can be 'decided' before an earlier
         round. Here, rounds 1 and 2 are decided before round 0 because the fame of
         witness w00 is only decided at round 5.
             //            |    |    |    |
             //        	|    |    |    |w51 collects votes from w40, w41, w42 and w43.
             //            |   w51   |    |IT DECIDES YES
             //            |    |  \ |    |
             //        	|    |   e23   |
             //            |    |    | \  |------------------------
             //            |    |    |   w43
             //            |    |    | /  | Round 4 is a Coin Round. No decision will be made.
             //            |    |   w42   |
             //            |    | /  |    | w40 collects votes from w33, w32 and w31. It votes yes.
             //            |   w41   |    | w41 collects votes from w33, w32 and w31. It votes yes.
             //        	| /  |    |    | w42 collects votes from w30, w31, w32 and w33. It votes yes.
             //           w40   |    |    | w43 collects votes from w30, w31, w32 and w33. It votes yes.
             //            | \  |    |    |------------------------
             //            |   d13   |    | w30 collects votes from w20, w21, w22 and w23. It votes yes
             //            |    |  \ |    | w31 collects votes from w21, w22 and w23. It votes no
             //           w30   |    \    | w32 collects votes from w20, w21, w22 and w23. It votes yes
             //            | \  |    | \  | w33 collects votes from w20, w21, w22 and w23. It votes yes
             //            |   \     |   w33
             //            |    | \  |  / |Again, none of the witnesses in round 3 are able to decide.
             //            |    |   w32   |However, a strong majority votes yes
             //            |    |  / |    |
             //        	|   w31   |    |
             //            |  / |    |    |--------------------------
             //           w20   |    |    | w23 collects votes from w11, w12 and w13. It votes no
             //            |  \ |    |    | w21 collects votes from w11, w12, and w13. It votes no
             //            |    \    |    | w22 collects votes from w11, w12, w13 and w14. It votes yes
             //            |    | \  |    | w20 collects votes from w11, w12, w13 and w14. It votes yes
             //            |    |   w22   |
             //            |    | /  |    | None of the witnesses in round 2 were able to decide.
             //            |   c10   |    | They voted according to the majority of votes they observed
             //            | /  |    |    | in round 1. The vote is split 2-2
             //           b00  w21   |    |
             //            |    |  \ |    |
             //            |    |    \    |
             //            |    |    | \  |
             //            |    |    |   w23
             //            |    |    | /  |------------------------
             //           w10   |   b21   |
             //        	| \  | /  |    | w10 votes yes (it can see w00)
             //            |   w11   |    | w11 votes yes
             //            |    |  \ |    | w12 votes no  (it cannot see w00)
             //        	|    |   w12   | w13 votes no
             //            |    |    | \  |
             //            |    |    |   w13
             //            |    |    | /  |------------------------
             //            |   a10  a21   | We want to decide the fame of w00
             //            |  / |  / |    |
             //            |/  a12   |    |
             //           a00   |  \ |    |
             //        	|    |   a23   |
             //            |    |    | \  |
             //           w00  w01  w02  w03
             //        	0	 1	  2	   3
             //        */

        //        public async Task<(Hashgraph h, Dictionary<string, string> index)> InitFunkyHashgraph()

        //        {
        //            var index = new Dictionary<string, string>();

        //            var nodes = new List<TestNode>();
        //            var orderedEvents = new List<Event>();

        //            int i = 0;
        //            var n = 4;
        //            for (i = 0; i < n; i++)
        //            {
        //                var key = CryptoUtils.GenerateEcdsaKey();
        //                var node = new TestNode(key, i);
        //                var name = $"w0{i}";
        //                var ev = new Event(new [] {name.StringToBytes() }, null, new[] {"", ""}, node.Pub, 0);

        //                node.SignAndAddEvent(ev, name, index, orderedEvents);
        //                nodes.Add(node);
        //            }

        //            var plays = new[]
        //            {
        //                new Play(2, 1, "w02", "w03", "a23", new[] {"a23".StringToBytes()}, null),
        //                new Play(1, 1, "w01", "a23", "a12", new[] {"a12".StringToBytes()}, null),
        //                new Play(0, 1, "w00", "", "a00", new[] {"a00".StringToBytes()}, null),
        //                new Play(1, 2, "a12", "a00", "a10", new[] {"a10".StringToBytes()}, null),
        //                new Play(2, 2, "a23", "a12", "a21", new[] {"a21".StringToBytes()}, null),
        //                new Play(3, 1, "w03", "a21", "w13", new[] {"w13".StringToBytes()}, null),
        //                new Play(2, 3, "a21", "w13", "w12", new[] {"w12".StringToBytes()}, null),
        //                new Play(1, 3, "a10", "w12", "w11", new[] {"w11".StringToBytes()}, null),
        //                new Play(0, 2, "a00", "w11", "w10", new[] {"w10".StringToBytes()}, null),
        //                new Play(2, 4, "w12", "w11", "b21", new[] {"b21".StringToBytes()}, null),
        //                new Play(3, 2, "w13", "b21", "w23", new[] {"w32".StringToBytes()}, null),
        //                new Play(1, 4, "w11", "w23", "w21", new[] {"w21".StringToBytes()}, null),
        //                new Play(0, 3, "w10", "", "b00", new[] {"b00".StringToBytes()}, null),
        //                new Play(1, 5, "w21", "b00", "c10", new[] {"c10".StringToBytes()}, null),
        //                new Play(2, 5, "b21", "c10", "w22", new[] {"w22".StringToBytes()}, null),
        //                new Play(0, 4, "b00", "w22", "w20", new[] {"w20".StringToBytes()}, null),
        //                new Play(1, 6, "c10", "w20", "w31", new[] {"w31".StringToBytes()}, null),
        //                new Play(2, 6, "w22", "w31", "w32", new[] {"w32".StringToBytes()}, null),
        //                new Play(0, 5, "w20", "w32", "w30", new[] {"w30".StringToBytes()}, null),
        //                new Play(3, 3, "w23", "w32", "w33", new[] {"w33".StringToBytes()}, null),
        //                new Play(1, 7, "w31", "w33", "d13", new[] {"d13".StringToBytes()}, null),
        //                new Play(0, 6, "w30", "d13", "w40", new[] {"w40".StringToBytes()}, null),
        //                new Play(1, 8, "d13", "w40", "w41", new[] {"w41".StringToBytes()}, null),
        //                new Play(2, 7, "w32", "w41", "w42", new[] {"w42".StringToBytes()}, null),
        //                new Play(3, 4, "w33", "w42", "w43", new[] {"w43".StringToBytes()}, null),
        //                new Play(2, 8, "w42", "w43", "e23", new[] {"e23".StringToBytes()}, null),
        //                new Play(1, 9, "w41", "e23", "w51", new[] {"w51".StringToBytes()}, null)
        //            };

        //            foreach (var p in plays)
        //            {
        //                var parents = new List<string> {index[p.SelfParent]};
        //                index.TryGetValue(p.OtherParent, out var otherParent);
        //                parents.Add(otherParent ?? "");

        //                var e = new Event(p.TxPayload, p.SigPayload, parents.ToArray(),
        //                    nodes[p.To].Pub,
        //                    p.Index);

        //                nodes[p.To].SignAndAddEvent(e, p.Name, index, orderedEvents);
        //            }

        //            var participants = new Dictionary<string, int>();
        //            foreach (var node in nodes)
        //            {
        //                participants[node.Pub.ToHex()] = node.Id;
        //            }

        //            var hashgraph = new Hashgraph(participants, new InmemStore(participants, CacheSize, logger), null, logger);

        //            i = 0;
        //            foreach (var ev in orderedEvents)
        //            {
        //                var err = await hashgraph.InsertEvent(ev, true);

        //                if (err != null)
        //                {
        //                    Console.WriteLine($"ERROR inserting event {i}: {err.Message} ");
        //                }
        //            }

        //            return (hashgraph, index);
        //        }

        //        [Fact]
        //        public async Task TestFunkyHashgraphFame()
        //        {
        //            var ( h, index) = await InitFunkyHashgraph();

        //            await h.DivideRounds();

        //            var l = h.Store.LastRound();
        //            Assert.Equal(5, l);

        //            for (var r = 0; r < 6; r++)
        //            {
        //                var ( round, err) = await h.Store.GetRound(r);

        //                Assert.Null(err);

        //                var witnessNames = new List<string>();
        //                foreach (var w in round.Witnesses())
        //                {
        //                    witnessNames.Add(GetName(index, w));
        //                }

        //                Console.WriteLine("Round {0} witnesses: {1}", r, string.Join(", ", witnessNames));
        //            }

        //            await h.DecideFame();

        //            //rounds 0,1, 2 and 3 should be decided
        //            var expectedUndecidedRounds = new List<int> {4, 5};

        //            h.UndecidedRounds.ToArray().ShouldCompareTo(expectedUndecidedRounds.ToArray());
        //        }

        //        [Fact]
        //        public async Task TestFunkyHashgraphBlocks()
        //        {
        //            var (h, _ ) = await InitFunkyHashgraph();
        //            await h.DivideRounds();
        //            await h.DecideFame();
        //            await h.FindOrder();

        //            var expectedBlockTxCounts = new Dictionary<int, int>
        //            {
        //                {0, 6},
        //                {1, 7},
        //                {2, 7}
        //            };

        //            for (var bi = 0; bi < 3; bi++)
        //            {
        //                var (b, err) = await h.Store.GetBlock(bi);
        //                Assert.Null(err);

        //                var i = 0;
        //                foreach (var tx in b.Transactions())
        //                {
        //                    logger.Debug(string.Format("block {0}, tx {1}: {2}", bi, i, tx.BytesToString()));
        //                    i++;
        //                }

        //                Assert.Equal(expectedBlockTxCounts[bi], b.Transactions().Length);
        //            }
        //        }

        /*
             public static void TestGetFrame(ref testing.T t)
             {
                 var (h, index) = initConsensusHashgraph(@false, t);

                 var participants = h.Participants.ToPeerSlice();

                 h.DivideRounds();
                 h.DecideFame();
                 h.DecideRoundReceived();
                 h.ProcessDecidedRounds();

                 t.Run("Round 1", t =>
                 {
                     var expectedRoots = make(typeof(slice<Root>), n);
                     expectedRoots[0] = NewBaseRoot(participants[0].ID);
                     expectedRoots[1] = NewBaseRoot(participants[1].ID);
                     expectedRoots[2] = NewBaseRoot(participants[2].ID);

                     var (frame, err) = h.GetFrame(1);
                     if (err != null)
                     {
                         t.Fatal(err);
                     }
                     {
                         {
                             var er = expectedRoots[p];
                             {
                                 var x = r.SelfParent;

                                 if (!reflect.DeepEqual(x, er.SelfParent))
                                 {
                                     t.Fatalf("Roots[%d].SelfParent should be %v, not %v", p, er.SelfParent, x);
                                 }

                             }
                             {
                                 var others = r.Others;

                                 if (!reflect.DeepEqual(others, er.Others))
                                 {
                                     t.Fatalf("Roots[%d].Others should be %v, not %vv", p, er.Others, others);
                                 }

                             }
                         }

                     }

                     var expectedEventsHashes = []string{index["e0"],index["e1"],index["e2"],index["e10"],index["e21"],index["e21b"],index["e02"]};
                     var expectedEvents = []Event{};
                     {
                         {
                             var (e, err) = h.Store.GetEvent(eh);
                             if (err != null)
                             {
                                 t.Fatal(err);
                             }
                             expectedEvents = append(expectedEvents, e);
                         }

                     }
                     sort.Sort(ByLamportTimestamp(expectedEvents));
                     if (!reflect.DeepEqual(expectedEvents, frame.Events))
                     {
                         t.Fatal("Frame.Events is not good");
                     }
                     var (block0, err) = h.Store.GetBlock(0);
                     if (err != null)
                     {
                         t.Fatalf("Store should contain a block with Index 1: %v", err);
                     }
                     var (frame1Hash, err) = frame.Hash();
                     if (err != null)
                     {
                         t.Fatalf("Error computing Frame hash, %v", err);
                     }
                     if (!reflect.DeepEqual(block0.FrameHash(), frame1Hash))
                     {
                         t.Fatalf("Block0.FrameHash (%v) and Frame1.Hash (%v) differ", block0.FrameHash(), frame1Hash);
                     }
                 });

                 t.Run("Round 2", t =>
                 {
                     var expectedRoots = make(typeof(slice<Root>), n);
                     expectedRoots[0] = Root{NextRound:1,SelfParent:RootEvent{index["e02"],participants[0].ID,1,4,0},Others:map[string]RootEvent{index["f0"]:RootEvent{Hash:index["f1b"],CreatorID:participants[1].ID,Index:3,LamportTimestamp:6,Round:1,},index["f0x"]:RootEvent{Hash:index["e21"],CreatorID:participants[2].ID,Index:1,LamportTimestamp:2,Round:0,},},};
                     expectedRoots[1] = Root{NextRound:1,SelfParent:RootEvent{index["e10"],participants[1].ID,1,1,0},Others:map[string]RootEvent{index["f1"]:RootEvent{Hash:index["e02"],CreatorID:participants[0].ID,Index:1,LamportTimestamp:4,Round:0,},},};
                     expectedRoots[2] = Root{NextRound:1,SelfParent:RootEvent{index["e21b"],participants[2].ID,2,3,0},Others:map[string]RootEvent{index["f2"]:RootEvent{Hash:index["f1b"],CreatorID:participants[1].ID,Index:3,LamportTimestamp:6,Round:1,},},};

                     var (frame, err) = h.GetFrame(2);
                     if (err != null)
                     {
                         t.Fatal(err);
                     }
                     {
                         {
                             var er = expectedRoots[p];
                             {
                                 var x = r.SelfParent;

                                 if (!reflect.DeepEqual(x, er.SelfParent))
                                 {
                                     t.Fatalf("Roots[%d].SelfParent should be %v, not %v", p, er.SelfParent, x);
                                 }

                             }
                             {
                                 var others = r.Others;

                                 if (!reflect.DeepEqual(others, er.Others))
                                 {
                                     t.Fatalf("Roots[%d].Others should be %v, not %v", p, er.Others, others);
                                 }

                             }
                         }

                     }

                     var expectedEventsHashes = []string{index["f1"],index["f1b"],index["f0"],index["f2"],index["f10"],index["f0x"],index["f21"],index["f02"],index["f02b"]};
                     var expectedEvents = []Event{};
                     {
                         {
                             var (e, err) = h.Store.GetEvent(eh);
                             if (err != null)
                             {
                                 t.Fatal(err);
                             }
                             expectedEvents = append(expectedEvents, e);
                         }

                     }
                     sort.Sort(ByLamportTimestamp(expectedEvents));
                     if (!reflect.DeepEqual(expectedEvents, frame.Events))
                     {
                         t.Fatal("Frame.Events is not good");
                     }
                 });

             }

             public static void TestResetFromFrame(ref testing.T t)
             {
                 var (h, index) = initConsensusHashgraph(@false, t);

                 var participants = h.Participants.ToPeerSlice();

                 h.DivideRounds();
                 h.DecideFame();
                 h.DecideRoundReceived();
                 h.ProcessDecidedRounds();

                 var (block, err) = h.Store.GetBlock(1);
                 if (err != null)
                 {
                     t.Fatal(err);
                 }
                 var (frame, err) = h.GetFrame(block.RoundReceived());
                 if (err != null)
                 {
                     t.Fatal(err);
                 }

                 //This operation clears the private fields which need to be recomputed
                 //in the Events (round, roundReceived,etc)
                 var (marshalledFrame, _) = frame.Marshal();
                 var unmarshalledFrame = @new(Frame);
                 unmarshalledFrame.Unmarshal(marshalledFrame);

                 var h2 = NewHashgraph(h.Participants, NewInmemStore(h.Participants, cacheSize), null, testLogger(t));
                 err = h2.Reset(block, unmarshalledFrame.Deref);
                 if (err != null)
                 {
                     t.Fatal(err);
                 }

                 /*
                     The hashgraph should now look like this:

                               f02b|   |
                               |   |   |
                               f02 |   |
                               | \ |   |
                               |   \   |
                               |   | \ |
                        +--f0x  |   f21 //f0x's other-parent is e21b; contained in R0
                        |   |   | / |
                        |   |  f10  |
                        |   | / |   |
                        |   f0  |   f2
                        |   | \ | / |
                        |   |  f1b  |
                        |   |   |   |
                        |   |   f1  |
                        |   |   |   |
                        +-- R0  R1  R2
                 */

        /*
            //Test Known
            var expectedKnown = map[int]int{participants[0].ID:5,participants[1].ID:4,participants[2].ID:4,};

            var known = h2.Store.KnownEvents();
            {
                {
                    {
                        var l = known[peer.ID];

                        if (l != expectedKnown[peer.ID])
                        {
                            t.Fatalf("Known[%d] should be %d, not %d", peer.ID, expectedKnown[peer.ID], l);
                        }

                    }
                }

                /***************************************************************************
                 Test DivideRounds
                ***************************************************************************/
        //}

        /***************************************************************************
         Test DivideRounds
        ***************************************************************************/

        /*
        {
            var err = h2.DivideRounds();
    
            if (err != null)
            {
                t.Fatal(err);
            }
    
        }
        var (hRound1, err) = h.Store.GetRound(1);
        if (err != null)
        {
            t.Fatal(err);
        }
        var (h2Round1, err) = h2.Store.GetRound(1);
        if (err != null)
        {
            t.Fatal(err);
        }
    
        //Check Round1 Witnesses
        var hWitnesses = hRound1.Witnesses();
        var h2Witnesses = h2Round1.Witnesses();
        sort.Strings(hWitnesses);
        sort.Strings(h2Witnesses);
        if (!reflect.DeepEqual(hWitnesses, h2Witnesses))
        {
            t.Fatalf("Reset Hg Round 1 witnesses should be %v, not %v", hWitnesses, h2Witnesses);
        }
    
        //check Event Rounds and LamportTimestamps
        {
            {
                var (h2r, err) = h2.round(ev.Hex());
                if (err != null)
                {
                    t.Fatalf("Error computing %s Round: %d", getName(index, ev.Hex()), h2r);
                }
                var (hr, _) = h.round(ev.Hex());
                if (h2r != hr)
                {
                    t.Fatalf("h2[%v].Round should be %d, not %d", getName(index, ev.Hex()), hr, h2r);
                }
                var (h2s, err) = h2.lamportTimestamp(ev.Hex());
                if (err != null)
                {
                    t.Fatalf("Error computing %s LamportTimestamp: %d", getName(index, ev.Hex()), h2s);
                }
                var (hs, _) = h.lamportTimestamp(ev.Hex());
                if (h2s != hs)
                {
                    t.Fatalf("h2[%v].LamportTimestamp should be %d, not %d", getName(index, ev.Hex()), hs, h2s);
                }
            }
    
            /***************************************************************************
            Test Consensus
            ***************************************************************************/

        //}

        /***************************************************************************
        Test Consensus
        ***************************************************************************/

        /*
        
        h2.DecideFame();
        h2.DecideRoundReceived();
        h2.ProcessDecidedRounds();
    
        {
            var lbi = h2.Store.LastBlockIndex();
    
            if (lbi != block.Index())
            {
                t.Fatalf("LastBlockIndex should be %d, not %d", block.Index(), lbi);
            }
    
        }
        {
            var r = h2.LastConsensusRound;
    
            if (r == null || r.Deref != block.RoundReceived())
            {
                t.Fatalf("LastConsensusRound should be %d, not %d", block.RoundReceived(), r.Deref);
            }
    
        }
        {
            var v = h2.AnchorBlock;
    
            if (v != null)
            {
                t.Fatalf("AnchorBlock should be null, not %v", v);
            }
    
            /***************************************************************************
            Test continue after Reset
            ***************************************************************************/
        //Insert remaining Events into the Reset hashgraph
        /*
                    }
                    for (var r = 2; r <= 4; r++)
                    {
                        var (round, err) = h.Store.GetRound(r);
                        if (err != null)
                        {
                            t.Fatal(err);
                        }
                        var events = []Event{};
                        {
                            {
                                var (ev, err) = h.Store.GetEvent(e);
                                if (err != null)
                                {
                                    t.Fatal(err);
                                }
                                events = append(events, ev);
                                t.Logf("R%d %s", r, getName(index, e));
                            }
    
                        }
    
                        sort.Sort(ByTopologicalOrder(events));
    
                        {
                            {
                                var (marshalledEv, _) = ev.Marshal();
                                var unmarshalledEv = @new(Event);
                                unmarshalledEv.Unmarshal(marshalledEv);
    
                                err = h2.InsertEvent(unmarshalledEv.Deref, @true);
                                if (err != null)
                                {
                                    t.Fatalf("ERR Inserting Event %s: %v", getName(index, ev.Hex()), err);
                                }
                            }
    
                        }
                    }
    
    
                    h2.DivideRounds();
                    h2.DecideFame();
                    h2.DecideRoundReceived();
                    h2.ProcessDecidedRounds();
    
                    for (var r = 1; r <= 4; r++)
                    {
                        var (hRound, err) = h.Store.GetRound(r);
                        if (err != null)
                        {
                            t.Fatal(err);
                        }
                        var (h2Round, err) = h2.Store.GetRound(r);
                        if (err != null)
                        {
                            t.Fatal(err);
                        }
                        var hWitnesses = hRound.Witnesses();
                        var h2Witnesses = h2Round.Witnesses();
                        sort.Strings(hWitnesses);
                        sort.Strings(h2Witnesses);
    
                        if (!reflect.DeepEqual(hWitnesses, h2Witnesses))
                        {
                            t.Fatalf("Reset Hg Round %d witnesses should be %v, not %v", r, hWitnesses, h2Witnesses);
                        }
                    }
    
                }
    
                public static void TestBootstrap(ref testing.T _t) => func(_t, (ref testing.T t, Defer defer, Panic _, Recover _) =>
                {
                    //Initialize a first Hashgraph with a DB backend
                    //Add events and run consensus methods on it
                    var (h, _) = initConsensusHashgraph(@true, t);
                    h.DivideRounds();
                    h.DecideFame();
                    h.DecideRoundReceived();
                    h.ProcessDecidedRounds();
    
                    h.Store.Close();
                    defer(os.RemoveAll(badgerDir));
    
                    //Now we want to create a new Hashgraph based on the database of the previous
                    //Hashgraph and see if we can boostrap it to the same state.
                    var (recycledStore, err) = LoadBadgerStore(cacheSize, badgerDir);
                    var nh = NewHashgraph(recycledStore.participants, recycledStore, null, logrus.New().WithField("id", "bootstrapped"));
                    err = nh.Bootstrap();
                    if (err != null)
                    {
                        t.Fatal(err);
                    }
                    var hConsensusEvents = h.Store.ConsensusEvents();
                    var nhConsensusEvents = nh.Store.ConsensusEvents();
                    if (len(hConsensusEvents) != len(nhConsensusEvents))
                    {
                        t.Fatalf("Bootstrapped hashgraph should contain %d consensus events,not %d", len(hConsensusEvents), len(nhConsensusEvents));
                    }
                    var hKnown = h.Store.KnownEvents();
                    var nhKnown = nh.Store.KnownEvents();
                    if (!reflect.DeepEqual(hKnown, nhKnown))
                    {
                        t.Fatalf("Bootstrapped hashgraph's Known should be %#v, not %#v", hKnown, nhKnown);
                    }
                    if (h.LastConsensusRound.Deref != nh.LastConsensusRound.Deref)
                    {
                        t.Fatalf("Bootstrapped hashgraph's LastConsensusRound should be %#v, not %#v", h.LastConsensusRound.Deref, nh.LastConsensusRound.Deref);
                    }
                    if (h.LastCommitedRoundEvents != nh.LastCommitedRoundEvents)
                    {
                        t.Fatalf("Bootstrapped hashgraph's LastCommitedRoundEvents should be %#v, not %#v", h.LastCommitedRoundEvents, nh.LastCommitedRoundEvents);
                    }
                    if (h.ConsensusTransactions != nh.ConsensusTransactions)
                    {
                        t.Fatalf("Bootstrapped hashgraph's ConsensusTransactions should be %#v, not %#v", h.ConsensusTransactions, nh.ConsensusTransactions);
                    }
                    if (h.PendingLoadedEvents != nh.PendingLoadedEvents)
                    {
                        t.Fatalf("Bootstrapped hashgraph's PendingLoadedEvents should be %#v, not %#v", h.PendingLoadedEvents, nh.PendingLoadedEvents);
                    }
                });
    
        */

        /*
    
    //        [Fact]
    //        public async Task TestParentRound()
    //        {
    //            var (h, index) = await InitRoundHashgraph();
    
    //            var round0Witnesses = new Dictionary<string, RoundEvent>
    //            {
    //                [index["e0"]] = new RoundEvent {Witness = true, Famous = null},
    //                [index["e1"]] = new RoundEvent {Witness = true, Famous = null},
    //                [index["e2"]] = new RoundEvent {Witness = true, Famous = null}
    //            };
    
    //            await h.Store.SetRound(0, new RoundInfo {Events = round0Witnesses});
    
    //            var round1Witnesses = new Dictionary<string, RoundEvent>();
    
    //            round1Witnesses[index["f1"]] = new RoundEvent {Witness = true, Famous = null};
    //            await h.Store.SetRound(1, new RoundInfo {Events = round1Witnesses});
    
    //            Assert.Equal(-1, (await h.ParentRound(index["e0"])).Round);
    //            Assert.True((await h.ParentRound(index["e0"])).IsRoot);
    
    //            Assert.Equal(-1, (await h.ParentRound(index["e1"])).Round);
    //            Assert.True((await h.ParentRound(index["e1"])).IsRoot);
    
    //            Assert.Equal(0, (await h.ParentRound(index["f1"])).Round);
    //            Assert.False((await h.ParentRound(index["f1"])).IsRoot);
    
    //            Assert.Equal(1, (await h.ParentRound(index["s11"])).Round);
    //            Assert.False((await h.ParentRound(index["s11"])).IsRoot);
    //        }
    
    //        [Fact]
    //        public async Task TestRoundInc()
    //        {
    //            var (h, index) = await InitRoundHashgraph();
    
    //            var round0Witnesses = new Dictionary<string, RoundEvent>();
    
    //            round0Witnesses[index["e0"]] = new RoundEvent {Witness = true, Famous = null};
    //            round0Witnesses[index["e1"]] = new RoundEvent {Witness = true, Famous = null};
    //            round0Witnesses[index["e2"]] = new RoundEvent {Witness = true, Famous = null};
    //            await h.Store.SetRound(0, new RoundInfo {Events = round0Witnesses});
    
    //            Assert.True(await h.RoundInc(index["f1"]), "RoundInc f1 should be true");
    
    //            Assert.False(await h.RoundInc(index["e02"]), "RoundInc e02 should be false because it doesnt strongly see e2");
    //        }
    
    
    
    
    //e0  e1  e2    Block (0, 1)
    //0   1    2
    //*/

        //        public static async Task<(Hashgraph hashgraph, TestNode[] nodes, Dictionary<string, string> index)> InitBlockHashgraph(ILogger logger)
        //        {
        //            var index = new Dictionary<string, string>();
        //            var nodes = new List<TestNode>();
        //            var orderedEvents = new List<Event>();
        //            int i = 0;

        //            //create the initial events
        //            for (i = 0; i < N; i++)
        //            {
        //                var key = CryptoUtils.GenerateEcdsaKey();
        //                var node = new TestNode(key, i);
        //                var ev = new Event(null, null, new[] {"", ""}, node.Pub, 0);
        //                node.SignAndAddEvent(ev, string.Format("e{0}", i), index, orderedEvents);
        //                nodes.Add(node);
        //            }

        //            var participants = new Dictionary<string, int>();
        //            foreach (var node in nodes)
        //            {
        //                participants.Add(node.Pub.ToHex(), node.Id);
        //            }

        //            var hashgraph = new Hashgraph(participants, new InmemStore(participants, CacheSize, logger), null, logger);

        //            //create a block and signatures manually
        //            Exception err;

        //            var block = new Block(0, 1, new[] {"block tx".StringToBytes()});
        //            err = await hashgraph.Store.SetBlock(block);
        //            Assert.Null(err);

        //            i = 0;
        //            foreach (var ev in orderedEvents)
        //            {
        //                err = await hashgraph.InsertEvent(ev, true);

        //                if (err != null)
        //                {
        //                    logger.Warning("ERROR inserting event {0}: {1}", i, err);
        //                }
        //            }

        //            return (hashgraph, nodes.ToArray(), index);
        //        }

        //        [Fact]
        //        public async Task TestInsertEventsWithBlockSignatures()
        //        {
        //            BlockSignature sig;
        //            Exception err;
        //            Block block;
        //            Event e;

        //            var (h, nodes, index) = await InitBlockHashgraph(logger);

        //            (block, err) = await h.Store.GetBlock(0);
        //            Assert.Null(err);

        //            var blockSigs = new List<BlockSignature>();

        //            foreach (var n in nodes)
        //            {
        //                (sig, err) = block.Sign(n.Key);
        //                Assert.Null(err);

        //                blockSigs.Add(sig);
        //            }

        //            //Inserting Events with valid signatures

        //            /*
        //                s00 |   |
        //                |   |   |
        //                |  e10  s20
        //                | / |   |
        //                e0  e1  e2
        //                0   1    2
        //            */
        //            var plays = new[]
        //            {
        //                new Play(1, 1, "e1", "e0", "e10", null, new[] {blockSigs[1]}),
        //                new Play(2, 1, "e2", "", "s20", null, new[] {blockSigs[2]}),
        //                new Play(0, 1, "e0", "", "s00", null, new[] {blockSigs[0]})
        //            };

        //            foreach (var pl in plays)
        //            {

        //                var sp = index[pl.SelfParent];
        //                index.TryGetValue(pl.OtherParent,out var op);
        //                op = op ?? "";

        //                e = new Event(pl.TxPayload,
        //                    pl.SigPayload,
        //                    new[] {sp, op},
        //                    nodes[pl.To].Pub,
        //                    pl.Index);

        //                e.Sign(nodes[pl.To].Key);
        //                index[pl.Name] = e.Hex();

        //                err = await h.InsertEvent(e, true);

        //                Assert.Null(err);
        //            }

        //            //check that the block contains 3 signatures
        //            (block, _) = await h.Store.GetBlock(0);
        //            var l = block.Signatures.Count;
        //            Assert.Equal(3, l);

        //            //"Inserting Events with signature of unknown block"

        //            //The Event should be inserted
        //            //The block signature is simply ignored

        //            var block1 = new Block(1, 2, new byte[][] { });
        //            (sig, _) = block1.Sign(nodes[2].Key);

        //            //unknown block
        //            var unknownBlockSig = new BlockSignature
        //            {
        //                Validator = nodes[2].Pub,
        //                Index = 1,
        //                Signature = sig.Signature
        //            };
        //            var p = new Play(2, 2, "s20", "e10", "e21", null, new[] {unknownBlockSig});

        //            e = new Event(null, p.SigPayload, new[] {index[p.SelfParent], index[p.OtherParent]},
        //                nodes[p.To].Pub,
        //                p.Index);

        //            e.Sign(nodes[p.To].Key);
        //            index[p.Name] = e.Hex();

        //            err = await h.InsertEvent(e, true);
        //            Assert.Null(err);

        //            //check that the event was recorded
        //            (_, err) = await h.Store.GetEvent(index["e21"]);
        //            Assert.Null(err);

        ////Inserting Events with BlockSignature not from creator

        //            //The Event should be inserted
        //            //The block signature is simply ignored

        //            //wrong validator
        //            //Validator should be same as Event creator (node 0)
        //            var key = CryptoUtils.GenerateEcdsaKey();
        //            var badNode = new TestNode(key, 666);
        //            var (badNodeSig, _) = block.Sign(badNode.Key);

        //            p = new Play(0, 2, "s00", "e21", "e02", null, new[] {badNodeSig});

        //            e = new Event(null,
        //                p.SigPayload,
        //                new[] {index[p.SelfParent], index[p.OtherParent]},
        //                nodes[p.To].Pub,
        //                p.Index);

        //            e.Sign(nodes[p.To].Key);
        //            index[p.Name] = e.Hex();

        //            err = await h.InsertEvent(e, true);
        //            Assert.Null(err);

        //            //check that the signature was not appended to the block
        //            (block, _) = await h.Store.GetBlock(0);
        //            l = block.Signatures.Count;
        //            Assert.Equal(3, l);
        //        }

        //        /*
        //        		h0  |   h2
        //        		| \ | / |
        //        		|   h1  |
        //        		|  /|   |
        //        		g02 |   |
        //        		| \ |   |
        //        		|   \   |
        //        		|   | \ |
        //        	---	o02 |  g21 //e02's other-parent is f21. This situation can happen with concurrency
        //        	|	|   | / |
        //        	|	|  g10  |
        //        	|	| / |   |
        //        	|	g0  |   g2
        //        	|	| \ | / |
        //        	|	|   g1  |
        //        	|	|  /|   |
        //        	|	f02b|   |
        //        	|	|   |   |
        //        	|	f02 |   |
        //        	|	| \ |   |
        //        	|	|   \   |
        //        	|	|   | \ |
        //        	----------- f21
        //        		|   | / |
        //        		|  f10  |
        //        		| / |   |
        //        		f0  |   f2
        //        		| \ | / |
        //        		|  f1b  |
        //        		|   |   |
        //        		|   f1  |
        //        		|  /|   |
        //        		e02 |   |
        //        		| \ |   |
        //        		|   \   |
        //        		|   | \ |
        //        		|   |  e21b
        //        		|   |   |
        //        		|   |  e21
        //        		|   | / |
        //        		|  e10  |
        //        		| / |   |
        //        		e0 e1  e2
        //        		0   1    2
        //        */

        //        public static async Task<(Hashgraph hashgraph, Dictionary<string, string> index)> InitConsensusHashgraph(bool db, string dir, ILogger logger)

        //        {
        //            var index = new Dictionary<string, string>();
        //            var nodes = new List<TestNode>();
        //            var orderedEvents = new List<Event>();

        //            var i = 0;
        //            for (i = 0; i < N; i++)

        //            {
        //                var key = CryptoUtils.GenerateEcdsaKey();
        //                var node = new TestNode(key, i);

        //                var ev = new Event(null, null, new[] {"", ""}, node.Pub, 0);

        //                node.SignAndAddEvent(ev, $"e{i}", index, orderedEvents);
        //                nodes.Add(node);
        //            }

        //            var plays = new[]
        //            {
        //                new Play(1, 1, "e1", "e0", "e10", null, null),
        //                new Play(2, 1, "e2", "e10", "e21", new[] {"e21".StringToBytes()}, null),
        //                new Play(2, 2, "e21", "", "e21b", null, null),
        //                new Play(0, 1, "e0", "e21b", "e02", null, null),
        //                new Play(1, 2, "e10", "e02", "f1", null, null),
        //                new Play(1, 3, "f1", "", "f1b", new[] {"f1b".StringToBytes()}, null),
        //                new Play(0, 2, "e02", "f1b", "f0", null, null),
        //                new Play(2, 3, "e21b", "f1b", "f2", null, null),
        //                new Play(1, 4, "f1b", "f0", "f10", null, null),
        //                new Play(2, 4, "f2", "f10", "f21", null, null),
        //                new Play(0, 3, "f0", "f21", "f02", null, null),
        //                new Play(0, 4, "f02", "", "f02b", new[] {"e21".StringToBytes()}, null),
        //                new Play(1, 5, "f10", "f02b", "g1", null, null),
        //                new Play(0, 5, "f02b", "g1", "g0", null, null),
        //                new Play(2, 5, "f21", "g1", "g2", null, null),
        //                new Play(1, 6, "g1", "g0", "g10", null, null),
        //                new Play(0, 6, "g0", "f21", "o02", null, null),
        //                new Play(2, 6, "g2", "g10", "g21", null, null),
        //                new Play(0, 7, "o02", "g21", "g02", null, null),
        //                new Play(1, 7, "g10", "g02", "h1", null, null),
        //                new Play(0, 8, "g02", "h1", "h0", null, null),
        //                new Play(2, 7, "g21", "h1", "h2", null, null)
        //            };

        //            foreach (var p in plays)
        //            {
        //                var parents = new List<string> {index[p.SelfParent]};

        //                index.TryGetValue(p.OtherParent, out var otherParent);

        //                parents.Add(otherParent ?? "");

        //                var e = new Event(p.TxPayload, p.SigPayload,
        //                    parents.ToArray(),
        //                    nodes[p.To].Pub,
        //                    p.Index);
        //                nodes[p.To].SignAndAddEvent(e, p.Name, index, orderedEvents);
        //            }

        //            var participants = new Dictionary<string, int>();
        //            foreach (var node in nodes)
        //            {
        //                participants[node.Pub.ToHex()] = node.Id;
        //            }

        //            IStore store;

        //            if (db)
        //            {
        //                (store, _) = await LocalDbStore.New(participants, CacheSize, dir, logger);
        //            }
        //            else
        //            {
        //                store = new InmemStore(participants, CacheSize, logger);
        //            }

        //            var hashgraph = new Hashgraph(participants, store, null, logger);

        //            using (var tx = store.BeginTx())
        //            {
        //                i = 0;
        //                foreach (var ev in orderedEvents)
        //                {
        //                    var err = await hashgraph.InsertEvent(ev, true);

        //                    if (err != null)
        //                    {
        //                        Console.WriteLine($"ERROR inserting event {i}: {err?.Message}");
        //                    }

        //                    i++;
        //                }

        //                tx.Commit();
        //            }

        //            return (hashgraph, index);
        //        }

        //        [Fact]
        //        public async Task TestDecideFame()
        //        {

        //            var (h, index) = await InitConsensusHashgraph(false, GetPath(), logger);

        //            await h.DivideRounds();

        //            await h.DecideFame();

        //            Assert.Equal(2, await h.Round(index["g0"]));

        //            Assert.Equal(2, await h.Round(index["g1"]));

        //            Assert.Equal(2, await h.Round(index["g2"]));

        //            var (round0, err) = await h.Store.GetRound(0);

        //            Assert.Null(err);

        //            var f = round0.Events[index["e0"]];

        //            Assert.True(f.Witness && f.Famous == true, $"e0 should be famous; got {f}");

        //            f = round0.Events[index["e1"]];

        //            Assert.True(f.Witness && f.Famous == true, $"e1 should be famous; got {f}");

        //            f = round0.Events[index["e2"]];

        //            Assert.True(f.Witness && f.Famous == true, $"e2 should be famous; got {f}");
        //        }

        //        [Fact]
        //        public async Task TestOldestSelfAncestorToSee()
        //        {
        //            var (h, index) = await InitConsensusHashgraph(false, GetPath(), logger);

        //            var a = await h.OldestSelfAncestorToSee(index["f0"], index["e1"]);

        //            Assert.True(a == index["e02"], $"oldest self ancestor of f0 to see e1 should be e02 not {GetName(index, a)}");

        //            a = await h.OldestSelfAncestorToSee(index["f1"], index["e0"]);
        //            Assert.True(a == index["e10"], $"oldest self ancestor of f1 to see e0 should be e10 not {GetName(index, a)}");

        //            a = await h.OldestSelfAncestorToSee(index["f1b"], index["e0"]);
        //            Assert.True(a == index["e10"], $"oldest self ancestor of f1b to see e0 should be e10 not {GetName(index, a)}");

        //            a = await h.OldestSelfAncestorToSee(index["g2"], index["f1"]);
        //            Assert.True(a == index["f2"], $"oldest self ancestor of g2 to see f1 should be f2 not {GetName(index, a)}");

        //            a = await h.OldestSelfAncestorToSee(index["e21"], index["e1"]);
        //            Assert.True(a == index["e21"], $"oldest self ancestor of e20 to see e1 should be e21 not {GetName(index, a)}");

        //            a = await h.OldestSelfAncestorToSee(index["e2"], index["e1"]);
        //            Assert.True(a == "", $"oldest self ancestor of e2 to see e1 should be '' not {GetName(index, a)}");
        //        }

        //        [Fact]
        //        public async Task TestDecideRoundReceived()
        //        {
        //            var (h, index) = await InitConsensusHashgraph(false, GetPath(), logger);

        //            await h.DivideRounds();

        //            await h.DecideFame();

        //            await h.DecideRoundReceived();

        //            foreach (var item in index)

        //            {
        //                var name = item.Key;
        //                var hash = item.Value;

        //                Console.WriteLine($"{name} - {hash}");

        //                var (e, _) = await h.Store.GetEvent(hash);

        //                //Todo: Check rune
        //                //if rune(name[0]) == rune('e') {

        //                if (name.Substring(0, 1) == "e")
        //                {
        //                    var r = e.GetRoundReceived();

        //                    Assert.True(r == 1, $"{name} round received should be 1 not {r}");
        //                }
        //            }
        //        }

        //        [Fact]
        //        public async Task TestFindOrder()
        //        {
        //            var ( h, index) = await InitConsensusHashgraph(false, GetPath(), logger);

        //            await h.DivideRounds();

        //            await h.DecideFame();

        //            await h.FindOrder();

        //            // Check Consensus Events

        //            var i = 0;
        //            foreach (var e in h.ConsensusEvents())

        //            {
        //                Console.WriteLine($"consensus[{i}]: {GetName(index, e)}");
        //                i++;
        //            }

        //            var l = h.ConsensusEvents().Length;
        //            Assert.True(l == 7, $"length of consensus should be 7 not {l}");

        //            var ple = h.PendingLoadedEvents;
        //            Assert.True(ple == 2, $"PendingLoadedEvents should be 2, not {ple}");

        //            var consensusEvents = h.ConsensusEvents();

        //            var n = GetName(index, consensusEvents[0]);
        //            Assert.True(n == "e0", $"consensus[0] should be e0, not {n}");

        //            //events which have the same consensus timestamp are ordered by whitened signature
        //            //which is not deterministic.

        //            n = GetName(index, consensusEvents[6]);
        //            Assert.True(n == "e02", $"consensus[6] should be e02, not {n}");

        //            // Check Blocks

        //            var (block0, err) = await h.Store.GetBlock(0);
        //            Assert.Null(err);

        //            Assert.Equal(0, block0.Index());

        //            Assert.Equal(1, block0.RoundReceived());

        //            Assert.Single(block0.Transactions());

        //            var tx = block0.Transactions()[0];

        //            tx.ShouldCompareTo("e21".StringToBytes());
        //        }

        //        [Fact]
        //        public async Task BenchmarkFindOrder()
        //        {
        //            for (var n = 0; n < N; n++)
        //            {
        //                //we do not want to benchmark the initialization code
        //                // StopTimer();

        //                var (h, _) = await InitConsensusHashgraph(false, null, logger);

        //                // StartTimer()

        //                await h.DivideRounds();

        //                await h.DecideFame();

        //                await h.FindOrder();
        //            }
        //        }

        //        [Fact]
        //        public async Task TestKnown()
        //        {
        //            var (h, _ ) = await InitConsensusHashgraph(false, GetPath(), logger);

        //            var expectedKnown = new Dictionary<int, int>
        //            {
        //                {0, 8},
        //                {1, 7},
        //                {2, 7}
        //            };

        //            var known = await h.KnownEvents();

        //            foreach (var id in h.Participants)
        //            {
        //                var l = known[id.Value];

        //                Assert.True(l == expectedKnown[id.Value], $"KnownEvents[{id.Value}] should be {expectedKnown[id.Value]}, not {l}");
        //            }
        //        }

        //        [Fact]
        //        public async Task TestReset()
        //        {
        //            var (h, index) = await InitConsensusHashgraph(false, GetPath(), logger);

        //            var evs = new[] {"g1", "g0", "g2", "g10", "g21", "o02", "g02", "h1", "h0", "h2"};

        //            var backup = new Dictionary<string, Event>();

        //            Exception err;
        //            foreach (var evn in evs)
        //            {
        //                Event ev;
        //                (ev, err) = await h.Store.GetEvent(index[evn]);

        //                Assert.Null(err);

        //                // Todo: Check if deep copy needed here
        //                var copyEvent = new Event
        //                {
        //                    Body = ev.Body,
        //                    Signiture = ev.Signiture
        //                };

        //                backup[evn] = copyEvent;
        //            }

        //            var roots = new Dictionary<string, Root>();
        //            roots[h.ReverseParticipants[0]] = new Root
        //            {
        //                X = index["f02b"],
        //                Y = index["g1"],
        //                Index = 4,
        //                Round = 2,
        //                Others = new Dictionary<string, string>
        //                {
        //                    {index["o02"], index["f21"]}
        //                }
        //            };
        //            roots[h.ReverseParticipants[1]] = new Root
        //            {
        //                X = index["f10"],
        //                Y = index["f02b"],
        //                Index = 4,
        //                Round = 2
        //            };

        //            roots[h.ReverseParticipants[2]] = new Root
        //            {
        //                X = index["f21"],
        //                Y = index["g1"],
        //                Index = 4,
        //                Round = 2
        //            };

        //            err = h.Reset(roots);

        //            Assert.Null(err);

        //            foreach (var k in evs)
        //            {
        //                err = await h.InsertEvent(backup[k], false);

        //                Assert.True(err == null, $"Error inserting {k} in reset Hashgraph: {err?.Message}");

        //                (_, err) = await h.Store.GetEvent(index[k]);

        //                Assert.True(err == null, $"Error fetching {k} after inserting it in reset Hashgraph: {err?.Message}");
        //            }

        //            var expectedKnown = new Dictionary<int, int>
        //            {
        //                {0, 8},
        //                {1, 7},
        //                {2, 7}
        //            };

        //            var known = await h.KnownEvents();

        //            foreach (var it in h.Participants)
        //            {
        //                var id = it.Value;
        //                var l = known[id];
        //                Assert.True(l == expectedKnown[id], $"KnownEvents[{id}] should be {expectedKnown[id]}, not {l}");
        //            }
        //        }

        private static string GetName(Dictionary<string, string> index, string hash)

        {
            foreach (var h in index)
            {
                if (h.Value == hash)
                {
                    return h.Key;
                }
            }

            return "";
        }

        //        private string Disp(Dictionary<string, string> index, string[] events)
        //        {
        //            var names = new List<string>();

        //            foreach (var h in events)
        //            {
        //                names.Add(GetName(index, h));
        //            }

        //            return string.Join(" ", names);
        //        }
    }
}