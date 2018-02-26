using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Dotnatter.Crypto;
using Dotnatter.HashgraphImpl;
using Dotnatter.HashgraphImpl.Model;
using Dotnatter.HashgraphImpl.Stores;
using Dotnatter.Test.Helpers;
using Dotnatter.Util;
using Serilog;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Dotnatter.Test.HashgraphImpl
{
    public class HashgraphTests

    {
        public HashgraphTests(ITestOutputHelper output)
        {
            this.output = output;
            logger = output.SetupLogging().ForContext("SourceContext", "HashGraphTests");
            dbPath = $"localdb/{Guid.NewGuid():D}";
        }

        private const int CacheSize = 100;

        private const int N = 3;

        private readonly ILogger logger;
        private ITestOutputHelper output;
        private readonly string dbPath;

        public class Node
        {
            public int Id { get; set; }
            public byte[] Pub { get; set; }
            public CngKey Key { get; set; }
            public List<Event> Events { get; set; }

            public Node(CngKey key, int id)
            {
                var pub = CryptoUtils.FromEcdsaPub(key);
                Id = id;
                Key = key;
                Pub = pub;
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

        public class Play
        {
            public int To { get; }
            public int Index { get; }
            public string SelfParent { get; }
            public string OtherParent { get; }
            public string Name { get; }
            public byte[][] Payload { get; }

            public Play(
                int to,
                int index,
                string selfParent,
                string otherParent,
                string name,
                byte[][] payload
            )
            {
                To = to;
                Index = index;
                SelfParent = selfParent;
                OtherParent = otherParent;
                Name = name;
                Payload = payload;
            }
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

        public async Task<(Hashgraph, Dictionary<string, string>)> InitHashgraph()
        {
            var index = new Dictionary<string, string>();

            var nodes = new List<Node>();
            var orderedEvents = new List<Event>();

            for (var i = 0; i < N; i++)
            {
                var key = CryptoUtils.GenerateEcdsaKey();
                var node = new Node(key, i);
                var ev = new Event(new byte[][] { }, new[] {"", ""}, node.Pub, 0);

                node.SignAndAddEvent(ev, $"e{i}", index, orderedEvents);
                nodes.Add(node);
            }

            var plays = new[]
            {
                new
                    Play
                    (
                        0,
                        1,
                        "e0",
                        "e1",
                        "e01",
                        new byte[][] { }
                    ),
                new Play
                (
                    2,
                    1,
                    "e2",
                    "",
                    "s20",
                    new byte[][] { }
                ),
                new Play
                (
                    1,
                    1,
                    "e1",
                    "",
                    "s10",
                    new byte[][] { }
                ),
                new Play
                (
                    0,
                    2,
                    "e01",
                    "",
                    "s00",
                    new byte[][] { }
                ),
                new Play
                (
                    2,
                    2,
                    "s20",
                    "s00",
                    "e20",
                    new byte[][] { }
                ),
                new Play
                (
                    1,
                    2,
                    "s10",
                    "e20",
                    "e12",
                    new byte[][] { }
                )
            };

            foreach (var p in plays)
            {
                var parents = new List<string>();
                parents.Add(index[p.SelfParent]);
                index.TryGetValue(p.OtherParent, out var otherParent);

                parents.Add(otherParent ?? "");

                var e = new Event(p.Payload,
                    parents.ToArray(),
                    nodes[p.To].Pub,
                    p.Index);

                nodes[p.To].SignAndAddEvent(e, p.Name, index, orderedEvents);
            }

            var participants = new Dictionary<string, int>();
            foreach (var node in nodes)
            {
                participants[node.Pub.ToHex()] = node.Id;
            }

            var store = new InmemStore(participants, CacheSize, logger);

            var h = new Hashgraph(participants, store, null, logger);
            foreach (var ev in orderedEvents)
            {
                await h.InitEventCoordinates(ev);

                await h.Store.SetEvent(ev);

                await h.UpdateAncestorFirstDescendant(ev);
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

            //1 generation

            Assert.True(await h.Ancestor(index["e01"], index["e0"]), "e0 should be ancestor of e01");

            Assert.True(await h.Ancestor(index["e01"], index["e1"]), "e1 should be ancestor of e01");

            Assert.True(await h.Ancestor(index["s00"], index["e01"]), "e01 should be ancestor of s00");

            Assert.True(await h.Ancestor(index["s00"], index["e01"]), "e01 should be ancestor of s00");

            Assert.True(await h.Ancestor(index["s20"], index["e2"]), "e2 should be ancestor of s20");

            Assert.True(await h.Ancestor(index["e20"], index["s00"]), "s00 should be ancestor of e20");

            Assert.True(await h.Ancestor(index["e20"], index["s20"]), "s20 should be ancestor of e20");

            Assert.True(await h.Ancestor(index["e12"], index["e20"]), "e20 should be ancestor of e12");

            Assert.True(await h.Ancestor(index["e12"], index["s10"]), "s10 should be ancestor of e12");

            //2 generations

            Assert.True(await h.Ancestor(index["s00"], index["e0"]), "e0 should be ancestor of s00");

            Assert.True(await h.Ancestor(index["s00"], index["e1"]), "e1 should be ancestor of s00");

            Assert.True(await h.Ancestor(index["e20"], index["e01"]), "e01 should be ancestor of e20");

            Assert.True(await h.Ancestor(index["e20"], index["e2"]), "e2 should be ancestor of e20");

            Assert.True(await h.Ancestor(index["e12"], index["e1"]), "e1 should be ancestor of e12");

            Assert.True(await h.Ancestor(index["e12"], index["s20"]), "s20 should be ancestor of e12");

            // 3 generations

            Assert.True(await h.Ancestor(index["e12"], index["e0"]), "e0 should be ancestor of e20");

            Assert.True(await h.Ancestor(index["e12"], index["e1"]), "e1 should be ancestor of e20");

            Assert.True(await h.Ancestor(index["e12"], index["e2"]), "e2 should be ancestor of e20");

            Assert.True(await h.Ancestor(index["e12"], index["e01"]), "e01 should be ancestor of e12");

            Assert.True(await h.Ancestor(index["e12"], index["e0"]), "e0 should be ancestor of e12");

            Assert.True(await h.Ancestor(index["e12"], index["e1"]), "e1 should be ancestor of e12");

            Assert.True(await h.Ancestor(index["e12"], index["e2"]), "e2 should be ancestor of e12");

            //false positive

            Assert.False(await h.Ancestor(index["e01"], index["e2"]), "e2 should not be ancestor of e01");

            Assert.False(await h.Ancestor(index["s00"], index["e2"]), "e2 should not be ancestor of s00");

            Assert.False(await h.Ancestor(index["e0"], ""), "\"\" should not be ancestor of e0");

            Assert.False(await h.Ancestor(index["s00"], ""), "\"\" should not be ancestor of s00");

            Assert.False(await h.Ancestor(index["e12"], ""), "\"\" should not be ancestor of e12");
        }

        [Fact]
        public async Task TestSelfAncestor()
        {
            var (h, index) = await InitHashgraph();

            // 1 generation

            Assert.True(await h.SelfAncestor(index["e01"], index["e0"]), "e0 should be self ancestor of e01");

            Assert.True(await h.SelfAncestor(index["s00"], index["e01"]), "e01 should be self ancestor of s00");

            // 1 generation false negatives

            Assert.False(await h.SelfAncestor(index["e01"], index["e1"]), "e1 should not be self ancestor of e01");

            Assert.False(await h.SelfAncestor(index["e12"], index["e20"]), "e20 should not be self ancestor of e12");

            Assert.False(await h.SelfAncestor(index["s20"], ""), "\"\" should not be self ancestor of s20");

            // 2 generation

            Assert.True(await h.SelfAncestor(index["e20"], index["e2"]), "e2 should be self ancestor of e20");

            Assert.True(await h.SelfAncestor(index["e12"], index["e1"]), "e1 should be self ancestor of e12");

            // 2 generation false negative

            Assert.False(await h.SelfAncestor(index["e20"], index["e0"]), "e0 should not be self ancestor of e20");

            Assert.False(await h.SelfAncestor(index["e12"], index["e2"]), "e2 should not be self ancestor of e12");

            Assert.False(await h.SelfAncestor(index["e20"], index["e01"]), "e01 should not be self ancestor of e20");
        }

        [Fact]
        public async Task TestSee()
        {
            var (h, index) = await InitHashgraph();

            Assert.True(await h.See(index["e01"], index["e0"]), "e01 should see e0");

            Assert.True(await h.See(index["e01"], index["e1"]), "e01 should see e1");

            Assert.True(await h.See(index["e20"], index["e0"]), "e20 should see e0");

            Assert.True(await h.See(index["e20"], index["e01"]), "e20 should see e01");

            Assert.True(await h.See(index["e12"], index["e01"]), "e12 should see e01");

            Assert.True(await h.See(index["e12"], index["e0"]), "e12 should see e0");

            Assert.True(await h.See(index["e12"], index["e1"]), "e12 should see e1");

            Assert.True(await h.See(index["e12"], index["s20"]), "e12 should see s20");
        }

        [Fact]
        public void TestSigningIssue()
        {
            var key = CryptoUtils.GenerateEcdsaKey();

            var node = new Node(key, 1);

            var ev = new Event(new byte[][] { }, new[] {"", ""}, node.Pub, 0);

            ev.Sign(key);

            Console.WriteLine(ev.Hex());

            var ev2 = new Event(new byte[][] { }, new[] {"", ""}, node.Pub, 0);

            ev2.Body.Timestamp = ev.Body.Timestamp;

            ev2.Sign(key);

            Console.WriteLine(ev2.Hex());

            Assert.Equal(ev.Marhsal().ToHex(), ev2.Marhsal().ToHex());
            Assert.Equal(ev.Hex(), ev2.Hex());

            Assert.NotEqual(ev.Signiture, ev2.Signiture);

            ev.ShouldCompareTo(ev2);
        }

        /*
        |    |    e20
        |    |   / |
        |    | /   |
        |    /     |
        |  / |     |
        e01  |     |
        | \  |     |
        |   \|     |
        |    |\    |
        |    |  \  |
        e0   e1 (a)e2
        0    1     2

        Node 2 Forks; events a and e2 are both created by node2, they are not self-parents
        and yet they are both ancestors of event e20
        */
        [Fact]
        public async Task TestFork()
        {
            var index = new Dictionary<string, string>();

            var nodes = new List<Node>();

            var participants = new Dictionary<string, int>();

            foreach (var node in nodes)
            {
                participants.Add(node.Pub.ToHex(), node.Id);
            }

            var store = new InmemStore(participants, CacheSize, logger);

            var hashgraph = new Hashgraph(participants, store, null, logger);

            for (var i = 0; i < N; i++)
            {
                var key = CryptoUtils.GenerateEcdsaKey();

                var node = new Node(key, i);

                var ev = new Event(new byte[][] { }, new[] {"", ""}, node.Pub, 0);

                ev.Sign(node.Key);

                index.Add($"e{i}", ev.Hex());

                await hashgraph.InsertEvent(ev, true);

                nodes.Add(node);
            }

            // ---

            //a and e2 need to have different hashes

            var eventA = new Event(new[] {"yo".StringToBytes()}, new[] {"", ""}, nodes[2].Pub, 0);
            eventA.Sign(nodes[2].Key);
            index["a"] = eventA.Hex();

            // "InsertEvent should return error for 'a'"
            var err = hashgraph.InsertEvent(eventA, true);
            Assert.NotNull(err);

            //// ---

            var event01 = new Event(new byte[][] { }, new[] {index["e0"], index["a"]}, nodes[0].Pub, 1); //e0 and a
            event01.Sign(nodes[0].Key);
            index["e01"] = event01.Hex();

            // "InsertEvent should return error for e01";
            err = hashgraph.InsertEvent(event01, true);
            Assert.NotNull(err);

            // ---

            var event20 = new Event(new byte[][] { }, new[] {index["e2"], index["e01"]}, nodes[2].Pub, 1); //e2 and e01
            event20.Sign(nodes[2].Key);
            index["e20"] = event20.Hex();

            //"InsertEvent should return error for e20"
            err = hashgraph.InsertEvent(event20, true);
            Assert.NotNull(err);
        }

        /*
        |  s11  |
        |   |   |
        |   f1  |
        |  /|   |
        | / s10 |
        |/  |   |
        e02 |   |
        | \ |   |
        |   \   |
        |   | \ |
        s00 |  e21
        |   | / |
        |  e10  s20
        | / |   |
        e0  e1  e2
        0   1    2
        */

        private async Task<(Hashgraph hashgraph, Dictionary<string, string> index)> InitRoundHashgraph()
        {
            var index = new Dictionary<string, string>();

            var nodes = new List<Node>();

            var orderedEvents = new List<Event>();

            for (var i = 0; i < N; i++)
            {
                var key = CryptoUtils.GenerateEcdsaKey();
                var node = new Node(key, i);

                var ev = new Event(new byte[][] { }, new[] {"", ""}, node.Pub, 0);
                node.SignAndAddEvent(ev, $"e{i}", index, orderedEvents);
                nodes.Add(node);
            }

            var plays = new[]
            {
                new Play(1, 1, "e1", "e0", "e10", new byte[][] { }),
                new Play(2, 1, "e2", "", "s20", new byte[][] { }),
                new Play(0, 1, "e0", "", "s00", new byte[][] { }),
                new Play(2, 2, "s20", "e10", "e21", new byte[][] { }),
                new Play(0, 2, "s00", "e21", "e02", new byte[][] { }),
                new Play(1, 2, "e10", "", "s10", new byte[][] { }),
                new Play(1, 3, "s10", "e02", "f1", new byte[][] { }),
                new Play(1, 4, "f1", "", "s11", new[] {"abc".StringToBytes()})
            };

            foreach (var p in plays)
            {
                var parents = new List<string>();

                parents.Add(index[p.SelfParent]);

                index.TryGetValue(p.OtherParent, out var otherParent);

                parents.Add(otherParent ?? "");

                var e = new Event(
                    p.Payload,
                    parents.ToArray(),
                    nodes[p.To].Pub,
                    p.Index);

                nodes[p.To].SignAndAddEvent(e, p.Name, index, orderedEvents);
            }

            var participants = new Dictionary<string, int>();
            foreach (var node in nodes)
            {
                participants.Add(node.Pub.ToHex(), node.Id);
            }

            var hashgraph = new Hashgraph(participants, new InmemStore(participants, CacheSize, logger), null, logger);

            foreach (var ev in orderedEvents)
            {
                await hashgraph.InsertEvent(ev, true);
            }

            return (hashgraph, index);
        }

        [Fact]
        public async Task TestInsertEvent()
        {
            var (h, index) = await InitRoundHashgraph();

            var expectedFirstDescendants = new List<EventCoordinates>(N);
            var expectedLastAncestors = new List<EventCoordinates>(N);

            //e0
            var (e0, err) = await h.Store.GetEvent(index["e0"]);

            Assert.Null(err);

            Assert.True(e0.Body.GetSelfParentIndex() == -1 &&
                        e0.Body.GetOtherParentCreatorId() == -1 &&
                        e0.Body.GetOtherParentIndex() == -1 &&
                        e0.Body.GetCreatorId() == h.Participants[e0.Creator()], "Invalid wire info on e0");

            expectedFirstDescendants.Add(new EventCoordinates
            {
                Index = 0,
                Hash = index["e0"]
            });

            expectedFirstDescendants.Add(new EventCoordinates
            {
                Index = 1,
                Hash = index["e10"]
            });

            expectedFirstDescendants.Add(new EventCoordinates
            {
                Index = 2,
                Hash = index["e21"]
            });

            expectedLastAncestors.Add(new EventCoordinates
            {
                Index = 0,
                Hash = index["e0"]
            });

            expectedLastAncestors.Add(new EventCoordinates
            {
                Index = -1
            });

            expectedLastAncestors.Add(new EventCoordinates
            {
                Index = -1
            });

            e0.GetFirstDescendants().ShouldCompareTo(expectedFirstDescendants.ToArray());
            e0.GetLastAncestors().ShouldCompareTo(expectedLastAncestors.ToArray());

            //e21
            Event e21;
            (e21, err) = await h.Store.GetEvent(index["e21"]);

            Assert.Null(err);

            Event e10;
            (e10, err) = await h.Store.GetEvent(index["e10"]);

            Assert.Null(err);

            Assert.True(e21.Body.GetSelfParentIndex() == 1 &&
                        e21.Body.GetOtherParentCreatorId() == h.Participants[e10.Creator()] &&
                        e21.Body.GetOtherParentIndex() == 1 &&
                        e21.Body.GetCreatorId() == h.Participants[e21.Creator()]
                , "Invalid wire info on e21"
            );

            // -------------

            expectedFirstDescendants[0] = new EventCoordinates
            {
                Index = 2,
                Hash = index["e02"]
            };

            expectedFirstDescendants[1] = new EventCoordinates
            {
                Index = 3,
                Hash = index["f1"]
            };

            expectedFirstDescendants[2] = new EventCoordinates
            {
                Index = 2,
                Hash = index["e21"]
            };

            expectedLastAncestors[0] = new EventCoordinates
            {
                Index = 0,
                Hash = index["e0"]
            };

            expectedLastAncestors[1] = new EventCoordinates
            {
                Index = 1,
                Hash = index["e10"]
            };

            expectedLastAncestors[2] = new EventCoordinates
            {
                Index = 2,
                Hash = index["e21"]
            };

            // "e21 firstDescendants not good"
            e21.GetFirstDescendants().ShouldCompareTo(expectedFirstDescendants.ToArray());

            //"e21 lastAncestors not good" 
            e21.GetLastAncestors().ShouldCompareTo(expectedLastAncestors.ToArray());

            //f1
            Event f1;
            (f1, err) = await h.Store.GetEvent(index["f1"]);

            Assert.Null(err);

            Assert.True(f1.Body.GetSelfParentIndex() == 2 &&
                        f1.Body.GetOtherParentCreatorId() == h.Participants[e0.Creator()] &&
                        f1.Body.GetOtherParentIndex() == 2 &&
                        f1.Body.GetCreatorId() == h.Participants[f1.Creator()], "Invalid wire info on f1");

            // -------------

            expectedFirstDescendants[0] = new EventCoordinates
            {
                Index = int.MaxValue
            };

            expectedFirstDescendants[1] = new EventCoordinates
            {
                Index = 3,
                Hash = index["f1"]
            };

            expectedFirstDescendants[2] = new EventCoordinates
            {
                Index = int.MaxValue
            };

            expectedLastAncestors[0] = new EventCoordinates
            {
                Index = 2,
                Hash = index["e02"]
            };

            expectedLastAncestors[1] = new EventCoordinates
            {
                Index = 3,
                Hash = index["f1"]
            };

            expectedLastAncestors[2] = new EventCoordinates
            {
                Index = 2,
                Hash = index["e21"]
            };

            // "f1 firstDescendants not good"
            f1.GetFirstDescendants().ShouldCompareTo(expectedFirstDescendants.ToArray());

            // "f1 lastAncestors not good"
            f1.GetFirstDescendants().ShouldCompareTo(expectedFirstDescendants.ToArray());

            //Pending loaded Events
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
                //evh.Dump();

                Exception err;
                Event ev;
                (ev, err) = await h.Store.GetEvent(evh.Value);

                Assert.Null(err);

                var evWire = ev.ToWire();

                Event evFromWire;
                (evFromWire, err) = await h.ReadWireInfo(evWire);
                Assert.Null(err);

                //"Error converting %s.Body from light wire"
                //evFromWire.Body.ShouldCompareTo(ev.Body);

                evFromWire.Signiture.ShouldCompareTo(ev.Signiture);

                bool ok;
                (ok, err) = ev.Verify();

                Assert.True(ok, $"Error verifying signature for {k} from light wire: {err?.Message}");

                k++;
            }
        }

        [Fact]
        public async Task TestStronglySee()
        {
            var (h, index) = await InitRoundHashgraph();

            Assert.True(await h.StronglySee(index["e21"], index["e0"]), "e21 should strongly see e0");

            Assert.True(await h.StronglySee(index["e02"], index["e10"]), "e02 should strongly see e10");

            Assert.True(await h.StronglySee(index["e02"], index["e0"]), "e02 should strongly see e0");

            Assert.True(await h.StronglySee(index["e02"], index["e1"]), "e02 should strongly see e1");

            Assert.True(await h.StronglySee(index["f1"], index["e21"]), "f1 should strongly see e21");

            Assert.True(await h.StronglySee(index["f1"], index["e10"]), "f1 should strongly see e10");

            Assert.True(await h.StronglySee(index["f1"], index["e0"]), "f1 should strongly see e0");

            Assert.True(await h.StronglySee(index["f1"], index["e1"]), "f1 should strongly see e1");

            Assert.True(await h.StronglySee(index["f1"], index["e2"]), "f1 should strongly see e2");

            Assert.True(await h.StronglySee(index["s11"], index["e2"]), "s11 should strongly see e2");

            //false negatives
            Assert.False(await h.StronglySee(index["e10"], index["e0"]), "e12 should not strongly see e2");

            Assert.False(await h.StronglySee(index["e21"], index["e1"]), "e21 should not strongly see e1");

            Assert.False(await h.StronglySee(index["e21"], index["e2"]), "e21 should not strongly see e2");

            Assert.False(await h.StronglySee(index["e02"], index["e2"]), "e02 should not strongly see e2");

            Assert.False(await h.StronglySee(index["s11"], index["e02"]), "s11 should not strongly see e02");
        }

        [Fact]
        public async Task TestParentRound()
        {
            var (h, index) = await InitRoundHashgraph();

            var round0Witnesses = new Dictionary<string, RoundEvent>
            {
                [index["e0"]] = new RoundEvent {Witness = true, Famous = null},
                [index["e1"]] = new RoundEvent {Witness = true, Famous = null},
                [index["e2"]] = new RoundEvent {Witness = true, Famous = null}
            };

            await h.Store.SetRound(0, new RoundInfo {Events = round0Witnesses});

            var round1Witnesses = new Dictionary<string, RoundEvent>();

            round1Witnesses[index["f1"]] = new RoundEvent {Witness = true, Famous = null};
            await h.Store.SetRound(1, new RoundInfo {Events = round1Witnesses});

            Assert.Equal(-1, (await h.ParentRound(index["e0"])).Round);
            Assert.True((await h.ParentRound(index["e0"])).IsRoot);

            Assert.Equal(-1, (await h.ParentRound(index["e1"])).Round);
            Assert.True((await h.ParentRound(index["e1"])).IsRoot);

            Assert.Equal(0, (await h.ParentRound(index["f1"])).Round);
            Assert.False((await h.ParentRound(index["f1"])).IsRoot);

            Assert.Equal(1, (await h.ParentRound(index["s11"])).Round);
            Assert.False((await h.ParentRound(index["s11"])).IsRoot);
        }

        [Fact]
        public async Task TestWitness()
        {
            var (h, index) = await InitRoundHashgraph();

            var round0Witnesses = new Dictionary<string, RoundEvent>
            {
                [index["e0"]] = new RoundEvent {Witness = true, Famous = null},
                [index["e1"]] = new RoundEvent {Witness = true, Famous = null},
                [index["e2"]] = new RoundEvent {Witness = true, Famous = null}
            };

            await h.Store.SetRound(0, new RoundInfo {Events = round0Witnesses});

            var round1Witnesses = new Dictionary<string, RoundEvent>
            {
                [index["f1"]] = new RoundEvent {Witness = true, Famous = null}
            };

            await h.Store.SetRound(1, new RoundInfo {Events = round1Witnesses});

            Assert.True(await h.Witness(index["e0"]), "e0 should be witness");

            Assert.True(await h.Witness(index["e1"]), "e1 should be witness");

            Assert.True(await h.Witness(index["e2"]), "e2 should be witness");

            Assert.True(await h.Witness(index["f1"]), "f1 should be witness");

            Assert.False(await h.Witness(index["e10"]), "e10 should not be witness");

            Assert.False(await h.Witness(index["e21"]), "e21 should not be witness");

            Assert.False(await h.Witness(index["e02"]), "e02 should not be witness");
        }

        [Fact]
        public async Task TestRoundInc()
        {
            var (h, index) = await InitRoundHashgraph();

            var round0Witnesses = new Dictionary<string, RoundEvent>();

            round0Witnesses[index["e0"]] = new RoundEvent {Witness = true, Famous = null};
            round0Witnesses[index["e1"]] = new RoundEvent {Witness = true, Famous = null};
            round0Witnesses[index["e2"]] = new RoundEvent {Witness = true, Famous = null};
            await h.Store.SetRound(0, new RoundInfo {Events = round0Witnesses});

            Assert.True(await h.RoundInc(index["f1"]), "RoundInc f1 should be true");

            Assert.False(await h.RoundInc(index["e02"]), "RoundInc e02 should be false because it doesnt strongly see e2");
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

            Assert.Equal(1, await h.Round(index["f1"]));

            Assert.Equal(0, await h.Round(index["e02"]));
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
        public async Task TestDivideRoundsAsync()
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
        }

        /*
        		h0  |   h2
        		| \ | / |
        		|   h1  |
        		|  /|   |
        		g02 |   |
        		| \ |   |
        		|   \   |
        		|   | \ |
        	---	o02 |  g21 //e02's other-parent is f21. This situation can happen with concurrency
        	|	|   | / |
        	|	|  g10  |
        	|	| / |   |
        	|	g0  |   g2
        	|	| \ | / |
        	|	|   g1  |
        	|	|  /|   |
        	|	f02b|   |
        	|	|   |   |
        	|	f02 |   |
        	|	| \ |   |
        	|	|   \   |
        	|	|   | \ |
        	----------- f21
        		|   | / |
        		|  f10  |
        		| / |   |
        		f0  |   f2
        		| \ | / |
        		|  f1b  |
        		|   |   |
        		|   f1  |
        		|  /|   |
        		e02 |   |
        		| \ |   |
        		|   \   |
        		|   | \ |
        		|   |  e21b
        		|   |   |
        		|   |  e21
        		|   | / |
        		|  e10  |
        		| / |   |
        		e0 e1  e2
        		0   1    2
        */

        public static async Task<(Hashgraph hashgraph, Dictionary<string, string> index)> InitConsensusHashgraph(bool db, string dir, ILogger logger)

        {
            var index = new Dictionary<string, string>();
            var nodes = new List<Node>();
            var orderedEvents = new List<Event>();

            var i = 0;
            for (i = 0; i < N; i++)

            {
                var key = CryptoUtils.GenerateEcdsaKey();
                var node = new Node(key, i);

                var ev = new Event(new byte[]
                    [] { }, new[] {"", ""}, node.Pub, 0);

                node.SignAndAddEvent(ev, $"e{i}", index, orderedEvents);
                nodes.Add(node);
            }

            var plays = new[]
            {
                new Play(1, 1, "e1", "e0", "e10", new byte[][] { }),
                new Play(2, 1, "e2", "e10", "e21", new[] {"e21".StringToBytes()}),
                new Play(2, 2, "e21", "", "e21b", new byte[][] { }),
                new Play(0, 1, "e0", "e21b", "e02", new byte[][] { }),
                new Play(1, 2, "e10", "e02", "f1", new byte[][] { }),
                new Play(1, 3, "f1", "", "f1b", new[] {"f1b".StringToBytes()}),
                new Play(0, 2, "e02", "f1b", "f0", new byte[][] { }),
                new Play(2, 3, "e21b", "f1b", "f2", new byte[][] { }),
                new Play(1, 4, "f1b", "f0", "f10", new byte[][] { }),
                new Play(2, 4, "f2", "f10", "f21", new byte[][] { }),
                new Play(0, 3, "f0", "f21", "f02", new byte[][] { }),
                new Play(0, 4, "f02", "", "f02b", new[] {"e21".StringToBytes()}),
                new Play(1, 5, "f10", "f02b", "g1", new byte[][] { }),
                new Play(0, 5, "f02b", "g1", "g0", new byte[][] { }),
                new Play(2, 5, "f21", "g1", "g2", new byte[][] { }),
                new Play(1, 6, "g1", "g0", "g10", new byte[][] { }),
                new Play(0, 6, "g0", "f21", "o02", new byte[][] { }),
                new Play(2, 6, "g2", "g10", "g21", new byte[][] { }),
                new Play(0, 7, "o02", "g21", "g02", new byte[][] { }),
                new Play(1, 7, "g10", "g02", "h1", new byte[][] { }),
                new Play(0, 8, "g02", "h1", "h0", new byte[][] { }),
                new Play(2, 7, "g21", "h1", "h2", new byte[][] { })
            };

            foreach (var p in plays)
            {
                var parents = new List<string> {index[p.SelfParent]};

                index.TryGetValue(p.OtherParent, out var otherParent);

                parents.Add(otherParent ?? "");

                var e = new Event(p.Payload,
                    parents.ToArray(),
                    nodes[p.To].Pub,
                    p.Index);
                nodes[p.To].SignAndAddEvent(e, p.Name, index, orderedEvents);
            }

            var participants = new Dictionary<string, int>();
            foreach (var node in nodes)
            {
                participants[node.Pub.ToHex()] = node.Id;
            }

            IStore store;

            if (db)
            {
                (store, _) = await LocalDbStore.New(participants, CacheSize, dir, logger);
            }
            else
            {
                store = new InmemStore(participants, CacheSize, logger);
            }

            var hashgraph = new Hashgraph(participants, store, null, logger);

            using (var tx = store.BeginTx()) 
            {
            i = 0;
            foreach (var ev in orderedEvents)
            {
                var err = await hashgraph.InsertEvent(ev, true);

                if (err != null)
                {
                    Console.WriteLine($"ERROR inserting event {i}: {err?.Message}");
                }

                i++;
            }
        }

        return (hashgraph, index);
        }

        [Fact]
        public async Task TestDecideFame()
        {
            var (h, index) = await InitConsensusHashgraph(false, dbPath, logger);

            await h.DivideRounds();

            await h.DecideFame();

            Assert.Equal(2, await h.Round(index["g0"]));

            Assert.Equal(2, await h.Round(index["g1"]));

            Assert.Equal(2, await h.Round(index["g2"]));

            var (round0, err) = await h.Store.GetRound(0);

            Assert.Null(err);

            var f = round0.Events[index["e0"]];

            Assert.True(f.Witness && f.Famous == true, $"e0 should be famous; got {f}");

            f = round0.Events[index["e1"]];

            Assert.True(f.Witness && f.Famous == true, $"e1 should be famous; got {f}");

            f = round0.Events[index["e2"]];

            Assert.True(f.Witness && f.Famous == true, $"e2 should be famous; got {f}");
        }

        [Fact]
        public async Task TestOldestSelfAncestorToSee()
        {
            var (h, index) = await InitConsensusHashgraph(false, dbPath, logger);

            var a = await h.OldestSelfAncestorToSee(index["f0"], index["e1"]);

            Assert.True(a == index["e02"], $"oldest self ancestor of f0 to see e1 should be e02 not {GetName(index, a)}");

            a = await h.OldestSelfAncestorToSee(index["f1"], index["e0"]);
            Assert.True(a == index["e10"], $"oldest self ancestor of f1 to see e0 should be e10 not {GetName(index, a)}");

            a = await h.OldestSelfAncestorToSee(index["f1b"], index["e0"]);
            Assert.True(a == index["e10"], $"oldest self ancestor of f1b to see e0 should be e10 not {GetName(index, a)}");

            a = await h.OldestSelfAncestorToSee(index["g2"], index["f1"]);
            Assert.True(a == index["f2"], $"oldest self ancestor of g2 to see f1 should be f2 not {GetName(index, a)}");

            a = await h.OldestSelfAncestorToSee(index["e21"], index["e1"]);
            Assert.True(a == index["e21"], $"oldest self ancestor of e20 to see e1 should be e21 not {GetName(index, a)}");

            a = await h.OldestSelfAncestorToSee(index["e2"], index["e1"]);
            Assert.True(a == "", $"oldest self ancestor of e2 to see e1 should be '' not {GetName(index, a)}");
        }

        [Fact]
        public async Task TestDecideRoundReceived()
        {
            var (h, index) = await InitConsensusHashgraph(false, dbPath, logger);

            await h.DivideRounds();

            await h.DecideFame();

            await h.DecideRoundReceived();

            foreach (var item in index)

            {
                var name = item.Key;
                var hash = item.Value;

                Console.WriteLine($"{name} - {hash}");

                var (e, _) = await h.Store.GetEvent(hash);

                //Todo: Check rune
                //if rune(name[0]) == rune('e') {

                if (name.Substring(0, 1) == "e")
                {
                    var r = e.GetRoundReceived();

                    Assert.True(r == 1, $"{name} round received should be 1 not {r}");
                }
            }
        }

        [Fact]
        public async Task TestFindOrder()
        {
            var ( h, index) = await InitConsensusHashgraph(false, dbPath, logger);

            await h.DivideRounds();

            await h.DecideFame();

            await h.FindOrder();

            var i = 0;
            foreach (var e in h.ConsensusEvents())

            {
                Console.WriteLine($"consensus[{i}]: {GetName(index, e)}");
                i++;
            }

            var l = h.ConsensusEvents().Length;
            Assert.True(l == 7, $"length of consensus should be 7 not {l}");

            var ple = h.PendingLoadedEvents;
            Assert.True(ple == 2, $"PendingLoadedEvents should be 2, not {ple}");

            var consensusEvents = h.ConsensusEvents();

            var n = GetName(index, consensusEvents[0]);
            Assert.True(n == "e0", $"consensus[0] should be e0, not {n}");

            //events which have the same consensus timestamp are ordered by whitened signature
            //which is not deterministic.

            n = GetName(index, consensusEvents[6]);
            Assert.True(n == "e02", $"consensus[6] should be e02, not {n}");
        }

        //[Fact]
        //public void BenchmarkFindOrder(b* testing.B)
        //{
        //    for n = 0; n < b.N; n++ {
        //        //we do not want to benchmark the initialization code
        //        b.StopTimer()

        //        h, _ = initConsensusHashgraph(false, common.NewBenchmarkLogger(b))

        //        b.StartTimer()

        //        h.DivideRounds()

        //        h.DecideFame()

        //        h.FindOrder()

        //    }
        //}

        [Fact]
        public async Task TestKnown()
        {
            var (h, _ ) = await InitConsensusHashgraph(false, dbPath, logger);

            var expectedKnown = new Dictionary<int, int>
            {
                {0, 8},
                {1, 7},
                {2, 7}
            };

            var known = await h.Known();

            foreach (var id in h.Participants)
            {
                var l = known[id.Value];

                Assert.True(l == expectedKnown[id.Value], $"Known[{id.Value}] should be {expectedKnown[id.Value]}, not {l}");
            }
        }

        [Fact]
        public async Task TestReset()
        {
            var (h, index) = await InitConsensusHashgraph(false, dbPath, logger);

            var evs = new[] {"g1", "g0", "g2", "g10", "g21", "o02", "g02", "h1", "h0", "h2"};

            var backup = new Dictionary<string, Event>();

            Exception err;
            foreach (var evn in evs)
            {
                Event ev;
                (ev, err) = await h.Store.GetEvent(index[evn]);

                Assert.Null(err);

                // Todo: Check if deep copy needed here
                var copyEvent = new Event
                {
                    Body = ev.Body,
                    Signiture = ev.Signiture
                };

                backup[evn] = copyEvent;
            }

            var roots = new Dictionary<string, Root>();
            roots[h.ReverseParticipants[0]] = new Root
            {
                X = index["f02b"],
                Y = index["g1"],
                Index = 4,
                Round = 2,
                Others = new Dictionary<string, string>
                {
                    {index["o02"], index["f21"]}
                }
            };
            roots[h.ReverseParticipants[1]] = new Root
            {
                X = index["f10"],
                Y = index["f02b"],
                Index = 4,
                Round = 2
            };

            roots[h.ReverseParticipants[2]] = new Root
            {
                X = index["f21"],
                Y = index["g1"],
                Index = 4,
                Round = 2
            };

            err = h.Reset(roots);

            Assert.Null(err);

            foreach (var k in evs)
            {
                err = await h.InsertEvent(backup[k], false);

                Assert.True(err == null, $"Error inserting {k} in reset Hashgraph: {err?.Message}");

                (_, err) = await h.Store.GetEvent(index[k]);

                Assert.True(err == null, $"Error fetching {k} after inserting it in reset Hashgraph: {err?.Message}");
            }

            var expectedKnown = new Dictionary<int, int>
            {
                {0, 8},
                {1, 7},
                {2, 7}
            };

            var known = await h.Known();

            foreach (var it in h.Participants)
            {
                var id = it.Value;
                var l = known[id];
                Assert.True(l == expectedKnown[id], $"Known[{id}] should be {expectedKnown[id]}, not {l}");
            }
        }

        [Fact]
        public async Task TestGetFrame()
        {
            var (h, index) = await InitConsensusHashgraph(false, dbPath, logger);

            await h.DivideRounds();

            await h.DecideFame();

            await h.FindOrder();

            var expectedRoots = new Dictionary<string, Root>();
            expectedRoots[h.ReverseParticipants[0]] = new Root
            {
                X = index["e02"],
                Y = index["f1b"],
                Index = 1,
                Round = 0,
                Others = new Dictionary<string, string>()
            };
            expectedRoots[h.ReverseParticipants[1]] = new Root
            {
                X = index["e10"],
                Y = index["e02"],
                Index = 1,
                Round = 0,
                Others = new Dictionary<string, string>()
            };
            expectedRoots[h.ReverseParticipants[2]] = new Root
            {
                X = index["e21b"],
                Y = index["f1b"],
                Index = 2,
                Round = 0,
                Others = new Dictionary<string, string>()
            };

            Exception err;
            Frame frame;
            (frame, err) = await h.GetFrame();

            Assert.Null(err);

            foreach (var rs in frame.Roots)
            {
                var p = rs.Key;
                var r = rs.Value;

                var ok = expectedRoots.TryGetValue(p, out var er);

                Assert.True(ok, $"No Root returned for {p}");

                var x = r.X;

                Assert.True(x == er.X, $"Roots[{p}].X should be {er.X}, not {x}");

                var y = r.Y;
                Assert.True(y == er.Y, $"Roots[{p}].Y should be {er.Y}, not {y}");

                var ind = r.Index;
                Assert.True(ind == er.Index, $"Roots[{p}].Index should be {er.Index}, not {ind}");

                var ro = r.Round;
                Assert.True(ro == er.Round, $"Roots[{p}].Round should be {er.Round}, not {ro}");

                var others = r.Others;

                er.Others.ShouldCompareTo(others);
            }

            var skip = new Dictionary<string, int>
            {
                {h.ReverseParticipants[0], 1},

                {h.ReverseParticipants[1], 1},
                {h.ReverseParticipants[2], 2}
            };

            var expectedEvents = new List<Event>();
            foreach (var rs in frame.Roots)
            {
                var p = rs.Key;
                var r = rs.Value;

                string[] ee;
                (ee, err) = await h.Store.ParticipantEvents(p, skip[p]);

                Assert.Null(err);

                foreach (var e in ee)
                {
                    Event ev;
                    (ev, err) = await h.Store.GetEvent(e);

                    Assert.Null(err);

                    expectedEvents.Add(ev);
                }
            }

            expectedEvents.Sort(new Event.EventByTopologicalOrder());

            frame.Events.ShouldCompareTo(expectedEvents.ToArray());
        }

        [Fact]
        public async Task TestResetFromFrame()
        {
            var (h, _) = await InitConsensusHashgraph(false, dbPath, logger);

            await h.DivideRounds();

            await h.DecideFame();

            await h.FindOrder();

            var (frame, err) = await h.GetFrame();

            Assert.Null(err);

            err = h.Reset(frame.Roots);

            Assert.Null(err);

            foreach (var ev in frame.Events)
            {
                err = await h.InsertEvent(ev, false);
                if (err != null)
                {
                    Console.WriteLine($"Error inserting {ev.Hex()} in reset Hashgraph: {err}");
                }

                Assert.Null(err);
            }

            var expectedKnown = new Dictionary<int, int>
            {
                {0, 8},
                {1, 7},
                {2, 7}
            };

            var known = await h.Known();

            foreach (var p in h.Participants)
            {
                var id = p.Value;
                var l = known[id];

                Assert.True(l == expectedKnown[id], $"Known[{id}] should be {expectedKnown[id]}, not {l}");
            }

            await h.DivideRounds();

            await h.DecideFame();

            await h.FindOrder();

            var r = h.LastConsensusRound;
            if (r == null || r != 1)
            {
                var disp = "null";

                if (r != null)
                {
                    disp = r.ToString();
                }

                Assert.True(false, $"LastConsensusRound should be 1, not {disp}");
            }
        }

        [Fact]
        public async Task TestBootstrap()
        {
            //Initialize a first Hashgraph with a DB backend
            //Add events and run consensus methods on it
            var (h, _) = await InitConsensusHashgraph(true, dbPath, logger);

            await h.DivideRounds();

            await h.DecideFame();

            await h.FindOrder();

            h.Store.Close();

            Exception err;
            //Now we want to create a new Hashgraph based on the database of the previous
            //Hashgraph and see if we can boostrap it to the same state.
            IStore recycledStore;
            (recycledStore, err) = await LocalDbStore.Load(CacheSize, dbPath, logger);

            var nh = new Hashgraph(recycledStore.Participants().participants, recycledStore, null, logger);

            err = await nh.Bootstrap();

            Assert.Null(err);

            var hConsensusEvents = h.ConsensusEvents();

            var nhConsensusEvents = nh.ConsensusEvents();

            Assert.Equal(hConsensusEvents.Length, nhConsensusEvents.Length);

            var hKnown = await h.Known();

            var nhKnown = await nh.Known();

            hKnown.ShouldCompareTo(nhKnown);

            Assert.Equal(h.LastConsensusRound, nh.LastConsensusRound);

            Assert.Equal(h.LastCommitedRoundEvents, nh.LastCommitedRoundEvents);

            Assert.Equal(h.ConsensusTransactions, nh.ConsensusTransactions);

            Assert.Equal(h.PendingLoadedEvents, nh.PendingLoadedEvents);
        }

        /*
            |    |    |    |
        	|    |    |    |w51 collects votes from w40, w41, w42 and w43.
            |   w51   |    |IT DECIDES YES
            |    |  \ |    |
        	|    |   e23   |
            |    |    | \  |------------------------
            |    |    |   w43
            |    |    | /  | Round 4 is a Coin Round. No decision will be made.
            |    |   w42   |
            |    | /  |    | w40 collects votes from w33, w32 and w31. It votes yes.
            |   w41   |    | w41 collects votes from w33, w32 and w31. It votes yes.
        	| /  |    |    | w42 collects votes from w30, w31, w32 and w33. It votes yes.
           w40   |    |    | w43 collects votes from w30, w31, w32 and w33. It votes yes.
            | \  |    |    |------------------------
            |   d13   |    | w30 collects votes from w20, w21, w22 and w23. It votes yes
            |    |  \ |    | w31 collects votes from w21, w22 and w23. It votes no
           w30   |    \    | w32 collects votes from w20, w21, w22 and w23. It votes yes
            | \  |    | \  | w33 collects votes from w20, w21, w22 and w23. It votes yes
            |   \     |   w33
            |    | \  |  / |Again, none of the witnesses in round 3 are able to decide.
            |    |   w32   |However, a strong majority votes yes
            |    |  / |    |
        	|   w31   |    |
            |  / |    |    |--------------------------
           w20   |    |    | w23 collects votes from w11, w12 and w13. It votes no
            |  \ |    |    | w21 collects votes from w11, w12, and w13. It votes no
            |    \    |    | w22 collects votes from w11, w12, w13 and w14. It votes yes
            |    | \  |    | w20 collects votes from w11, w12, w13 and w14. It votes yes
            |    |   w22   |
            |    | /  |    | None of the witnesses in round 2 were able to decide.
            |   c10   |    | They voted according to the majority of votes they observed
            | /  |    |    | in round 1. The vote is split 2-2
           b00  w21   |    |
            |    |  \ |    |
            |    |    \    |
            |    |    | \  |
            |    |    |   w23
            |    |    | /  |------------------------
           w10   |   b21   |
        	| \  | /  |    | w10 votes yes (it can see w00)
            |   w11   |    | w11 votes yes
            |    |  \ |    | w12 votes no  (it cannot see w00)
        	|    |   w12   | w13 votes no
            |    |    | \  |
            |    |    |   w13
            |    |    | /  |------------------------
            |   a10  a21   | We want to decide the fame of w00
            |  / |  / |    |
            |/  a12   |    |
           a00   |  \ |    |
        	|    |   a23   |
            |    |    | \  |
           w00  w01  w02  w03
        	0	 1	  2	   3
        */

        public async Task<(Hashgraph h, Dictionary<string, string> index)> InitFunkyHashgraph()

        {
            var index = new Dictionary<string, string>();

            var nodes = new List<Node>();
            var orderedEvents = new List<Event>();

            int i = 0;
            var n = 4;
            for (i = 0; i < n; i++)
            {
                var key = CryptoUtils.GenerateEcdsaKey();
                var node = new Node(key, i);

                var ev = new Event(new byte[][] { }, new[] {"", ""}, node.Pub, 0);

                node.SignAndAddEvent(ev, $"w0{i}", index, orderedEvents);
                nodes.Add(node);
            }

            var plays = new[]
            {
                new Play(2, 1, "w02", "w03", "a23", new byte[][] { }),
                new Play(1, 1, "w01", "a23", "a12", new byte[][] { }),
                new Play(0, 1, "w00", "", "a00", new byte[][] { }),
                new Play(1, 2, "a12", "a00", "a10", new byte[][] { }),
                new Play(2, 2, "a23", "a12", "a21", new byte[][] { }),
                new Play(3, 1, "w03", "a21", "w13", new byte[][] { }),
                new Play(2, 3, "a21", "w13", "w12", new byte[][] { }),
                new Play(1, 3, "a10", "w12", "w11", new byte[][] { }),
                new Play(0, 2, "a00", "w11", "w10", new byte[][] { }),
                new Play(2, 4, "w12", "w11", "b21", new byte[][] { }),
                new Play(3, 2, "w13", "b21", "w23", new byte[][] { }),
                new Play(1, 4, "w11", "w23", "w21", new byte[][] { }),
                new Play(0, 3, "w10", "", "b00", new byte[][] { }),
                new Play(1, 5, "w21", "b00", "c10", new byte[][] { }),
                new Play(2, 5, "b21", "c10", "w22", new byte[][] { }),
                new Play(0, 4, "b00", "w22", "w20", new byte[][] { }),
                new Play(1, 6, "c10", "w20", "w31", new byte[][] { }),
                new Play(2, 6, "w22", "w31", "w32", new byte[][] { }),
                new Play(0, 5, "w20", "w32", "w30", new byte[][] { }),
                new Play(3, 3, "w23", "w32", "w33", new byte[][] { }),
                new Play(1, 7, "w31", "w33", "d13", new byte[][] { }),
                new Play(0, 6, "w30", "d13", "w40", new byte[][] { }),
                new Play(1, 8, "d13", "w40", "w41", new byte[][] { }),
                new Play(2, 7, "w32", "w41", "w42", new byte[][] { }),
                new Play(3, 4, "w33", "w42", "w43", new byte[][] { }),
                new Play(2, 8, "w42", "w43", "e23", new byte[][] { }),
                new Play(1, 9, "w41", "e23", "w51", new byte[][] { })
            };

            foreach (var p in plays)
            {
                var parents = new List<string> {index[p.SelfParent]};
                index.TryGetValue(p.OtherParent, out var otherParent);
                parents.Add(otherParent ?? "");

                var e = new Event(p.Payload, parents.ToArray(),
                    nodes[p.To].Pub,
                    p.Index);

                nodes[p.To].SignAndAddEvent(e, p.Name, index, orderedEvents);
            }

            var participants = new Dictionary<string, int>();
            foreach (var node in nodes)
            {
                participants[node.Pub.ToHex()] = node.Id;
            }

            var hashgraph = new Hashgraph(participants, new InmemStore(participants, CacheSize, logger), null, logger);

            i = 0;
            foreach (var ev in orderedEvents)
            {
                var err = await hashgraph.InsertEvent(ev, true);

                if (err != null)
                {
                    Console.WriteLine($"ERROR inserting event {i}: {err?.Message} ");
                }
            }

            return (hashgraph, index);
        }

        [Fact]
        public async Task TestFunkyHashgraphFame()
        {
            var ( h, index) = await InitFunkyHashgraph();

            await h.DivideRounds();

            var l = h.Store.LastRound();
            Assert.Equal(5, l);

            for (var r = 0; r < 6; r++)
            {
                var ( round, err) = await h.Store.GetRound(r);

                Assert.Null(err);

                var witnessNames = new List<string>();
                foreach (var w in round.Witnesses())
                {
                    witnessNames.Add(GetName(index, w));
                }

                Console.WriteLine("Round {0} witnesses: {1}", r, string.Join(", ", witnessNames));
            }

            await h.DecideFame();

            //rounds 0,1, 2 and 3 should be decided
            var expectedUndecidedRounds = new List<int> {4, 5};

            h.UndecidedRounds.ToArray().ShouldCompareTo(expectedUndecidedRounds.ToArray());
        }

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

        private string Disp(Dictionary<string, string> index, string[] events)
        {
            var names = new List<string>();

            foreach (var h in events)
            {
                names.Add(GetName(index, h));
            }

            return string.Join(" ", names);
        }
    }
}