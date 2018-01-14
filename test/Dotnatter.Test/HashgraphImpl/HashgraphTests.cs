using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Dotnatter.Common;
using Dotnatter.Crypto;
using Dotnatter.HashgraphImpl;
using Dotnatter.Test.Helpers;
using Dotnatter.Util;
using Grin.Tests.Unit;
using KellermanSoftware.CompareNetObjects;
using Xunit;

namespace Dotnatter.Test.HashgraphImpl
{
    public class HashgraphTests:IClassFixture<LoggingFixture>
    {
        
        private const int CacheSize = 100;

        private const int N = 3;

        private string badgerDir = "test_data/badger";

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

        public class play
        {
            public int To { get; }
            public int Index { get; }
            public string SelfParent { get; }
            public string OtherParent { get; }
            public string Name { get; }
            public byte[][] Payload { get; }

            public play(
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

        public (Hashgraph, Dictionary<string, string>) InitHashgraph()
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
                    play
                    (
                        0,
                        1,
                        "e0",
                        "e1",
                        "e01",
                        new byte[][] { }
                    ),
                new play
                (
                    2,
                    1,
                    "e2",
                    "",
                    "s20",
                    new byte[][] { }
                ),
                new play
                (
                    1,
                    1,
                    "e1",
                    "",
                    "s10",
                    new byte[][] { }
                ),
                new play
                (
                    0,
                    2,
                    "e01",
                    "",
                    "s00",
                    new byte[][] { }
                ),
                new play
                (
                    2,
                    2,
                    "s20",
                    "s00",
                    "e20",
                    new byte[][] { }
                ),
                new play
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

            var store = new InmemStore(participants, CacheSize);

            var h = new Hashgraph(participants, store, null);
            foreach (var ev in orderedEvents)
            {
                h.InitEventCoordinates(ev);

                h.Store.SetEvent(ev);

                h.UpdateAncestorFirstDescendant(ev);
            }

            return (h, index);
        }

        [Fact]
        public void TestInit()
        {
            var (h, index) = InitHashgraph();
        }

        [Fact]
        public void TestAncestor()
        {
            var ( h, index) = InitHashgraph();

            //1 generation

            Assert.True(h.Ancestor(index["e01"], index["e0"]), "e0 should be ancestor of e01");

            Assert.True(h.Ancestor(index["e01"], index["e1"]), "e1 should be ancestor of e01");

            Assert.True(h.Ancestor(index["s00"], index["e01"]), "e01 should be ancestor of s00");

            Assert.True(h.Ancestor(index["s00"], index["e01"]), "e01 should be ancestor of s00");

            Assert.True(h.Ancestor(index["s20"], index["e2"]), "e2 should be ancestor of s20");

            Assert.True(h.Ancestor(index["e20"], index["s00"]), "s00 should be ancestor of e20");

            Assert.True(h.Ancestor(index["e20"], index["s20"]), "s20 should be ancestor of e20");

            Assert.True(h.Ancestor(index["e12"], index["e20"]), "e20 should be ancestor of e12");

            Assert.True(h.Ancestor(index["e12"], index["s10"]), "s10 should be ancestor of e12");

            //2 generations

            Assert.True(h.Ancestor(index["s00"], index["e0"]), "e0 should be ancestor of s00");

            Assert.True(h.Ancestor(index["s00"], index["e1"]), "e1 should be ancestor of s00");

            Assert.True(h.Ancestor(index["e20"], index["e01"]), "e01 should be ancestor of e20");

            Assert.True(h.Ancestor(index["e20"], index["e2"]), "e2 should be ancestor of e20");

            Assert.True(h.Ancestor(index["e12"], index["e1"]), "e1 should be ancestor of e12");

            Assert.True(h.Ancestor(index["e12"], index["s20"]), "s20 should be ancestor of e12");

            // 3 generations

            Assert.True(h.Ancestor(index["e12"], index["e0"]), "e0 should be ancestor of e20");

            Assert.True(h.Ancestor(index["e12"], index["e1"]), "e1 should be ancestor of e20");

            Assert.True(h.Ancestor(index["e12"], index["e2"]), "e2 should be ancestor of e20");

            Assert.True(h.Ancestor(index["e12"], index["e01"]), "e01 should be ancestor of e12");

            Assert.True(h.Ancestor(index["e12"], index["e0"]), "e0 should be ancestor of e12");

            Assert.True(h.Ancestor(index["e12"], index["e1"]), "e1 should be ancestor of e12");

            Assert.True(h.Ancestor(index["e12"], index["e2"]), "e2 should be ancestor of e12");

            //false positive

            Assert.False(h.Ancestor(index["e01"], index["e2"]), "e2 should not be ancestor of e01");

            Assert.False(h.Ancestor(index["s00"], index["e2"]), "e2 should not be ancestor of s00");

            Assert.False(h.Ancestor(index["e0"], ""), "\"\" should not be ancestor of e0");

            Assert.False(h.Ancestor(index["s00"], ""), "\"\" should not be ancestor of s00");

            Assert.False(h.Ancestor(index["e12"], ""), "\"\" should not be ancestor of e12");
        }

        [Fact]
        public void TestSelfAncestor()
        {
            var (h, index) = InitHashgraph();

            // 1 generation

            Assert.True(h.SelfAncestor(index["e01"], index["e0"]), "e0 should be self ancestor of e01");

            Assert.True(h.SelfAncestor(index["s00"], index["e01"]), "e01 should be self ancestor of s00");

            // 1 generation false negatives

            Assert.False(h.SelfAncestor(index["e01"], index["e1"]), "e1 should not be self ancestor of e01");

            Assert.False(h.SelfAncestor(index["e12"], index["e20"]), "e20 should not be self ancestor of e12");

            Assert.False(h.SelfAncestor(index["s20"], ""), "\"\" should not be self ancestor of s20");

            // 2 generation

            Assert.True(h.SelfAncestor(index["e20"], index["e2"]), "e2 should be self ancestor of e20");

            Assert.True(h.SelfAncestor(index["e12"], index["e1"]), "e1 should be self ancestor of e12");

            // 2 generation false negative

            Assert.False(h.SelfAncestor(index["e20"], index["e0"]), "e0 should not be self ancestor of e20");

            Assert.False(h.SelfAncestor(index["e12"], index["e2"]), "e2 should not be self ancestor of e12");

            Assert.False(h.SelfAncestor(index["e20"], index["e01"]), "e01 should not be self ancestor of e20");
        }

        [Fact]
        public void TestSee()
        {
            var (h, index) = InitHashgraph();

            Assert.True(h.See(index["e01"], index["e0"]), "e01 should see e0");

            Assert.True(h.See(index["e01"], index["e1"]), "e01 should see e1");

            Assert.True(h.See(index["e20"], index["e0"]), "e20 should see e0");

            Assert.True(h.See(index["e20"], index["e01"]), "e20 should see e01");

            Assert.True(h.See(index["e12"], index["e01"]), "e12 should see e01");

            Assert.True(h.See(index["e12"], index["e0"]), "e12 should see e0");

            Assert.True(h.See(index["e12"], index["e1"]), "e12 should see e1");

            Assert.True(h.See(index["e12"], index["s20"]), "e12 should see s20");
        }



        [Fact]
        public void TestSigningIssue()
        {

            var key = CryptoUtils.GenerateEcdsaKey();
            
            var node = new Node(key, 1);

            var ev = new Event(new byte[][] { }, new[] { "", "" }, node.Pub, 0);

            ev.Sign(key);

            Console.WriteLine(ev.Hex());
      
            var ev2 = new Event(new byte[][] { }, new[] { "", "" }, node.Pub, 0);

            ev2.Body.Timestamp = ev.Body.Timestamp;

            ev2.Sign(key);
            
            Console.WriteLine(ev2.Hex());
            
            Assert.Equal(ev.Marhsal().ToHex(), ev2.Marhsal().ToHex());
            Assert.Equal(ev.Hex(),ev2.Hex());

            Assert.NotEqual(ev.Signiture(),ev2.Signiture());

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
        public void TestFork()
        {
            var index = new Dictionary<string, string>();

            var nodes = new List<Node>();

            var participants = new Dictionary<string, int>();

            foreach (var node in nodes)
            {
                participants.Add(node.Pub.ToHex(), node.Id);
            }

            var store = new InmemStore(participants, CacheSize);

            var hashgraph = new Hashgraph(participants, store, null);
            

            for (var i = 0; i < N; i++)
            {
             
                var key = CryptoUtils.GenerateEcdsaKey();

                var node = new Node(key, i);

                var ev = new Event(new byte[][] { }, new[] {"", ""}, node.Pub, 0);

                ev.Sign(node.Key);

                index.Add($"e{i}", ev.Hex());
                
                hashgraph.InsertEvent(ev, true);

                nodes.Add(node);
        
            }




            // ---

            //a and e2 need to have different hashes
            
            var eventA = new Event(new byte[][] {"yo".StringToBytes()}, new[] {"", ""}, nodes[2].Pub, 0);
            eventA.Sign(nodes[2].Key);
            index["a"] = eventA.Hex();
            
            // "InsertEvent should return error for 'a'"
            var err  = hashgraph.InsertEvent(eventA, true);
            Assert.NotNull(err);

            //// ---

            var event01 = new Event(new byte[][] { }, new[] { index["e0"], index["a"] }, nodes[0].Pub, 1); //e0 and a
            event01.Sign(nodes[0].Key);
            index["e01"] = event01.Hex();

            // "InsertEvent should return error for e01";
            err = hashgraph.InsertEvent(event01, true);
            Assert.NotNull(err);

            // ---

            var event20 = new Event(new byte[][] { }, new[] { index["e2"], index["e01"] }, nodes[2].Pub, 1); //e2 and e01
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

        private (Hashgraph hashgraph, Dictionary<string, string> index) InitRoundHashgraph()
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
                new play(1, 1, "e1", "e0", "e10", new byte[][] { }),
                new play(2, 1, "e2", "", "s20", new byte[][] { }),
                new play(0, 1, "e0", "", "s00", new byte[][] { }),
                new play(2, 2, "s20", "e10", "e21", new byte[][] { }),
                new play(0, 2, "s00", "e21", "e02", new byte[][] { }),
                new play(1, 2, "e10", "", "s10", new byte[][] { }),
                new play(1, 3, "s10", "e02", "f1", new byte[][] { }),
                new play(1, 4, "f1", "", "s11", new[] {"abc".StringToBytes()})
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

            var hashgraph = new Hashgraph(participants, new InmemStore(participants, CacheSize), null);

            foreach (var ev in orderedEvents)
            {

                hashgraph.InsertEvent(ev, true);

            }
            return (hashgraph, index);
        }

        [Fact]
        public void TestInsertEvent()
        {
            var (h, index) = InitRoundHashgraph();

            var expectedFirstDescendants = new List<EventCoordinates>(N);
            var expectedLastAncestors = new List<EventCoordinates>(N);

            //e0
            var (e0, err) = h.Store.GetEvent(index["e0"]);

            Assert.Null(err);

            Assert.True(e0.Body.GetSelfParentIndex() == -1 &&
                        e0.Body.GetOtherParentCreatorId() == -1 &&
                        e0.Body.GetOtherParentIndex() == -1 &&
                        e0.Body.GetCreatorId() == h.Participants[e0.Creator], "Invalid wire info on e0");

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
            (e21, err) = h.Store.GetEvent(index["e21"]);

            Assert.Null(err);


            Event e10;
            (e10, err) = h.Store.GetEvent(index["e10"]);

            Assert.Null(err);

            Assert.True(e21.Body.GetSelfParentIndex() == 1 &&
                      e21.Body.GetOtherParentCreatorId() == h.Participants[e10.Creator] &&
                      e21.Body.GetOtherParentIndex() == 1 &&
                      e21.Body.GetCreatorId() == h.Participants[e21.Creator]
                   , "Invalid wire info on e21"
                );

           // -------------

            expectedFirstDescendants[0] =new EventCoordinates
            {
                Index = 2,
                Hash = index["e02"],
            };


            expectedFirstDescendants[1]=new EventCoordinates
            {
                Index = 3,
                Hash = index["f1"],
            };

            expectedFirstDescendants[2]=new EventCoordinates
            {
                Index = 2,
                Hash = index["e21"],
            };

            expectedLastAncestors[0]=new EventCoordinates
            {
                Index = 0,
                Hash = index["e0"],
            };

            expectedLastAncestors[1]=new EventCoordinates
            {
                Index = 1,
                Hash = index["e10"],
            };

            expectedLastAncestors[2]=new EventCoordinates
            {
                Index = 2,
                Hash = index["e21"],
            };

            // "e21 firstDescendants not good"
            e21.GetFirstDescendants().ShouldCompareTo(expectedFirstDescendants.ToArray());

            //"e21 lastAncestors not good" 
            e21.GetLastAncestors().ShouldCompareTo(expectedLastAncestors.ToArray());


            //f1
            Event f1;
            (f1, err) = h.Store.GetEvent(index["f1"]);

            Assert.Null(err);
            
            Assert.True(f1.Body.GetSelfParentIndex() == 2 &&
                        f1.Body.GetOtherParentCreatorId() == h.Participants[e0.Creator] &&
                        f1.Body.GetOtherParentIndex() == 2 &&
                        f1.Body.GetCreatorId() == h.Participants[f1.Creator], "Invalid wire info on f1");

            // -------------

            expectedFirstDescendants[0] = new EventCoordinates
            {
                Index = Int32.MaxValue
            };

            expectedFirstDescendants[1] = new EventCoordinates
            {
                Index = 3,
                Hash = index["f1"],
            };

            expectedFirstDescendants[2] = new EventCoordinates
            {
                Index = Int32.MaxValue
            };

            expectedLastAncestors[0] = new EventCoordinates
            {
                Index = 2,
                Hash = index["e02"],
            };

            expectedLastAncestors[1] = new EventCoordinates
            {
                Index = 3,
                Hash = index["f1"],
            };

            expectedLastAncestors[2] = new EventCoordinates
            {
                Index = 2,
                Hash = index["e21"],
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
        public void TestReadWireInfo()
        {
            var (h, index) = InitRoundHashgraph();



            int k = 0;
            foreach (var evh in index)
            {
                //evh.Dump();

                Exception err;
                Event ev;
                (ev, err) = h.Store.GetEvent(evh.Value);

                Assert.Null(err);

                var evWire = ev.ToWire();

                ev.Dump();

                Event evFromWire;
                (evFromWire,err) = h.ReadWireInfo(evWire);
                Assert.Null(err);
           



                //"Error converting %s.Body from light wire"
                evFromWire.Body.ShouldCompareTo(ev.Body);


                evFromWire.Signiture().ShouldCompareTo(ev.Signiture());

                bool ok;
                (ok, err) = ev.Verify();

                Assert.True(ok, $"Error verifying signature for {k} from ligh wire: {err.Message}");

                k++;
            }
        }

        //        [Fact]
        //        public void TestStronglySee(t* testing.T)
        //{
        //    h, index= initRoundHashgraph(t)

        //    if !h.StronglySee(index["e21"], index["e0"]) {
        //        t.Fatal("e21 should strongly see e0")

        //    }

        //    if !h.StronglySee(index["e02"], index["e10"]) {
        //        t.Fatal("e02 should strongly see e10")

        //    }
        //    if !h.StronglySee(index["e02"], index["e0"]) {
        //        t.Fatal("e02 should strongly see e0")

        //    }
        //    if !h.StronglySee(index["e02"], index["e1"]) {
        //        t.Fatal("e02 should strongly see e1")

        //    }

        //    if !h.StronglySee(index["f1"], index["e21"]) {
        //        t.Fatal("f1 should strongly see e21")

        //    }
        //    if !h.StronglySee(index["f1"], index["e10"]) {
        //        t.Fatal("f1 should strongly see e10")

        //    }
        //    if !h.StronglySee(index["f1"], index["e0"]) {
        //        t.Fatal("f1 should strongly see e0")

        //    }
        //    if !h.StronglySee(index["f1"], index["e1"]) {
        //        t.Fatal("f1 should strongly see e1")

        //    }
        //    if !h.StronglySee(index["f1"], index["e2"]) {
        //        t.Fatal("f1 should strongly see e2")

        //    }
        //    if !h.StronglySee(index["s11"], index["e2"]) {
        //        t.Fatal("s11 should strongly see e2")

        //    }

        //    //false negatives
        //    if h.StronglySee(index["e10"], index["e0"]) {
        //        t.Fatal("e12 should not strongly see e2")

        //    }
        //    if h.StronglySee(index["e21"], index["e1"]) {
        //        t.Fatal("e21 should not strongly see e1")

        //    }
        //    if h.StronglySee(index["e21"], index["e2"]) {
        //        t.Fatal("e21 should not strongly see e2")

        //    }
        //    if h.StronglySee(index["e02"], index["e2"]) {
        //        t.Fatal("e02 should not strongly see e2")

        //    }
        //    if h.StronglySee(index["s11"], index["e02"]) {
        //        t.Fatal("s11 should not strongly see e02")

        //    }
        //}

        //        public void TestParentRound(t* testing.T)
        //{
        //    h, index= initRoundHashgraph(t)

        //    round0Witnesses= make(map[string]RoundEvent)

        //    round0Witnesses[index["e0"]] = RoundEvent{ Witness: true, Famous: Undefined}
        //    round0Witnesses[index["e1"]] = RoundEvent{ Witness: true, Famous: Undefined}
        //    round0Witnesses[index["e2"]] = RoundEvent{ Witness: true, Famous: Undefined}
        //    h.Store.SetRound(0, RoundInfo{ Events: round0Witnesses})

        //	round1Witnesses= make(map[string]RoundEvent)

        //    round1Witnesses[index["f1"]] = RoundEvent{ Witness: true, Famous: Undefined}
        //    h.Store.SetRound(1, RoundInfo{ Events: round1Witnesses})

        //	if r = h.ParentRound(index["e0"]).round; r != -1 {
        //        t.Fatalf("e0.ParentRound().round should be -1, not %d", r)

        //    }
        //    if r = h.ParentRound(index["e0"]).isRoot; !r {
        //        t.Fatal("e0.ParentRound().isRoot should be true")

        //    }

        //    if r = h.ParentRound(index["e1"]).round; r != -1 {
        //        t.Fatalf("e1.ParentRound().round should be -1, not %d", r)

        //    }
        //    if r = h.ParentRound(index["e1"]).isRoot; !r {
        //        t.Fatal("e1.ParentRound().isRoot should be true")

        //    }

        //    if r = h.ParentRound(index["f1"]).round; r != 0 {
        //        t.Fatalf("f1.ParentRound().round should be 0, not %d", r)

        //    }
        //    if r = h.ParentRound(index["f1"]).isRoot; r {
        //        t.Fatalf("f1.ParentRound().isRoot should be false")

        //    }

        //    if r = h.ParentRound(index["s11"]).round; r != 1 {
        //        t.Fatalf("s11.ParentRound().round should be 1, not %d", r)

        //    }
        //    if r = h.ParentRound(index["s11"]).isRoot; r {
        //        t.Fatalf("s11.ParentRound().isRoot should be false")

        //    }
        //}

        //        [Fact]
        //        public void TestWitness(t* testing.T)
        //{
        //    h, index= initRoundHashgraph(t)

        //    round0Witnesses= make(map[string]RoundEvent)

        //    round0Witnesses[index["e0"]] = RoundEvent{ Witness: true, Famous: Undefined}
        //    round0Witnesses[index["e1"]] = RoundEvent{ Witness: true, Famous: Undefined}
        //    round0Witnesses[index["e2"]] = RoundEvent{ Witness: true, Famous: Undefined}
        //    h.Store.SetRound(0, RoundInfo{ Events: round0Witnesses})

        //	round1Witnesses= make(map[string]RoundEvent)

        //    round1Witnesses[index["f1"]] = RoundEvent{ Witness: true, Famous: Undefined}
        //    h.Store.SetRound(1, RoundInfo{ Events: round1Witnesses})

        //	if !h.Witness(index["e0"]) {
        //        t.Fatalf("e0 should be witness")

        //    }
        //    if !h.Witness(index["e1"]) {
        //        t.Fatalf("e1 should be witness")

        //    }
        //    if !h.Witness(index["e2"]) {
        //        t.Fatalf("e2 should be witness")

        //    }
        //    if !h.Witness(index["f1"]) {
        //        t.Fatalf("f1 should be witness")

        //    }

        //    if h.Witness(index["e10"]) {
        //        t.Fatalf("e10 should not be witness")

        //    }
        //    if h.Witness(index["e21"]) {
        //        t.Fatalf("e21 should not be witness")

        //    }
        //    if h.Witness(index["e02"]) {
        //        t.Fatalf("e02 should not be witness")

        //    }
        //}

        //        [Fact]
        //        public void TestRoundInc(t* testing.T)
        //{
        //    h, index= initRoundHashgraph(t)

        //    round0Witnesses= make(map[string]RoundEvent)

        //    round0Witnesses[index["e0"]] = RoundEvent{ Witness: true, Famous: Undefined}
        //    round0Witnesses[index["e1"]] = RoundEvent{ Witness: true, Famous: Undefined}
        //    round0Witnesses[index["e2"]] = RoundEvent{ Witness: true, Famous: Undefined}
        //    h.Store.SetRound(0, RoundInfo{ Events: round0Witnesses})

        //	if !h.RoundInc(index["f1"]) {
        //        t.Fatal("RoundInc f1 should be true")

        //    }

        //    if h.RoundInc(index["e02"]) {
        //        t.Fatal("RoundInc e02 should be false because it doesnt strongly see e2")

        //    }
        //}

        //        [Fact]
        //        public void TestRound(t* testing.T)
        //{
        //    h, index= initRoundHashgraph(t)

        //    round0Witnesses= make(map[string]RoundEvent)

        //    round0Witnesses[index["e0"]] = RoundEvent{ Witness: true, Famous: Undefined}
        //    round0Witnesses[index["e1"]] = RoundEvent{ Witness: true, Famous: Undefined}
        //    round0Witnesses[index["e2"]] = RoundEvent{ Witness: true, Famous: Undefined}
        //    h.Store.SetRound(0, RoundInfo{ Events: round0Witnesses})

        //	if r = h.Round(index["f1"]); r != 1 {
        //        t.Fatalf("round of f1 should be 1 not %d", r)

        //    }
        //    if r = h.Round(index["e02"]); r != 0 {
        //        t.Fatalf("round of e02 should be 0 not %d", r)

        //    }

        //}

        //        [Fact]
        //        public void TestRoundDiff(t* testing.T)
        //{
        //    h, index= initRoundHashgraph(t)

        //    round0Witnesses= make(map[string]RoundEvent)

        //    round0Witnesses[index["e0"]] = RoundEvent{ Witness: true, Famous: Undefined}
        //    round0Witnesses[index["e1"]] = RoundEvent{ Witness: true, Famous: Undefined}
        //    round0Witnesses[index["e2"]] = RoundEvent{ Witness: true, Famous: Undefined}
        //    h.Store.SetRound(0, RoundInfo{ Events: round0Witnesses})

        //	if d, err= h.RoundDiff(index["f1"], index["e02"]); d != 1 {
        //        if err != nil {
        //            t.Fatalf("RoundDiff(f1, e02) returned an error: %s", err)

        //        }
        //        t.Fatalf("RoundDiff(f1, e02) should be 1 not %d", d)

        //    }

        //    if d, err= h.RoundDiff(index["e02"], index["f1"]); d != -1 {
        //        if err != nil {
        //            t.Fatalf("RoundDiff(e02, f1) returned an error: %s", err)

        //        }
        //        t.Fatalf("RoundDiff(e02, f1) should be -1 not %d", d)

        //    }
        //    if d, err= h.RoundDiff(index["e02"], index["e21"]); d != 0 {
        //        if err != nil {
        //            t.Fatalf("RoundDiff(e20, e21) returned an error: %s", err)

        //        }
        //        t.Fatalf("RoundDiff(e20, e21) should be 0 not %d", d)

        //    }
        //}

        //        [Fact]
        //        public void TestDivideRounds(t* testing.T)
        //{
        //    h, index= initRoundHashgraph(t)

        //    err= h.DivideRounds()

        //    if err != nil {
        //        t.Fatal(err)

        //    }

        //    if l = h.Store.LastRound(); l != 1 {
        //        t.Fatalf("last round should be 1 not %d", l)

        //    }

        //    round0, err= h.Store.GetRound(0)

        //    if err != nil {
        //        t.Fatal(err)

        //    }
        //    if l = len(round0.Witnesses()); l != 3 {
        //        t.Fatalf("round 0 should have 3 witnesses, not %d", l)

        //    }
        //    if !contains(round0.Witnesses(), index["e0"]) {
        //        t.Fatalf("round 0 witnesses should contain e0")

        //    }
        //    if !contains(round0.Witnesses(), index["e1"]) {
        //        t.Fatalf("round 0 witnesses should contain e1")

        //    }
        //    if !contains(round0.Witnesses(), index["e2"]) {
        //        t.Fatalf("round 0 witnesses should contain e2")

        //    }

        //    round1, err= h.Store.GetRound(1)

        //    if err != nil {
        //        t.Fatal(err)

        //    }
        //    if l = len(round1.Witnesses()); l != 1 {
        //        t.Fatalf("round 1 should have 1 witness, not %d", l)

        //    }
        //    if !contains(round1.Witnesses(), index["f1"]) {
        //        t.Fatalf("round 1 witnesses should contain f1")

        //    }

        //}

        //        [Fact]
        //        public void contains(s[]string, x string) bool {
        //	for _, e = range s
        //{
        //		if e == x
        //    {
        //        return true

        //        }
        //}
        //	return false
        //}

        ///*
        //		h0  |   h2
        //		| \ | / |
        //		|   h1  |
        //		|  /|   |
        //		g02 |   |
        //		| \ |   |
        //		|   \   |
        //		|   | \ |
        //	---	o02 |  g21 //e02's other-parent is f21. This situation can happen with concurrency
        //	|	|   | / |
        //	|	|  g10  |
        //	|	| / |   |
        //	|	g0  |   g2
        //	|	| \ | / |
        //	|	|   g1  |
        //	|	|  /|   |
        //	|	f02b|   |
        //	|	|   |   |
        //	|	f02 |   |
        //	|	| \ |   |
        //	|	|   \   |
        //	|	|   | \ |
        //	----------- f21
        //		|   | / |
        //		|  f10  |
        //		| / |   |
        //		f0  |   f2
        //		| \ | / |
        //		|  f1b  |
        //		|   |   |
        //		|   f1  |
        //		|  /|   |
        //		e02 |   |
        //		| \ |   |
        //		|   \   |
        //		|   | \ |
        //		|   |  e21b
        //		|   |   |
        //		|   |  e21
        //		|   | / |
        //		|  e10  |
        //		| / |   |
        //		e0  e1  e2
        //		0   1    2
        //*/
        //        [Fact]
        //        public void initConsensusHashgraph(db bool, logger* logrus.Logger) (* Hashgraph, map[string]string) {
        //	index = make(map[string]string)

        //    nodes = [] Node{}
        //	orderedEvents = &[] Event{}

        //	for i = 0; i<n; i++ {
        //		key, _ = crypto.GenerateECDSAKey()
        //        node = NewNode(key, i)

        //        event = NewEvent([]
        //[]byte{ }, []string{ "", ""}, node.Pub, 0)
        //		node.signAndAddEvent(event, fmt.Sprintf("e%d", i), index, orderedEvents)
        //		nodes = append(nodes, node)
        //	}

        //plays = [] play{
        //		play{1, 1, "e1", "e0", "e10", [] [] byte{}},
        //		play{2, 1, "e2", "e10", "e21", [] [] byte{[] byte ("e21")}},
        //		play{2, 2, "e21", "", "e21b", [] [] byte{}},
        //		play{0, 1, "e0", "e21b", "e02", [] [] byte{}},
        //		play{1, 2, "e10", "e02", "f1", [] [] byte{}},
        //		play{1, 3, "f1", "", "f1b", [] [] byte{[] byte ("f1b")}},
        //		play{0, 2, "e02", "f1b", "f0", [] [] byte{}},
        //		play{2, 3, "e21b", "f1b", "f2", [] [] byte{}},
        //		play{1, 4, "f1b", "f0", "f10", [] [] byte{}},
        //		play{2, 4, "f2", "f10", "f21", [] [] byte{}},
        //		play{0, 3, "f0", "f21", "f02", [] [] byte{}},
        //		play{0, 4, "f02", "", "f02b", [] [] byte{[] byte ("e21")}},
        //		play{1, 5, "f10", "f02b", "g1", [] [] byte{}},
        //		play{0, 5, "f02b", "g1", "g0", [] [] byte{}},
        //		play{2, 5, "f21", "g1", "g2", [] [] byte{}},
        //		play{1, 6, "g1", "g0", "g10", [] [] byte{}},
        //		play{0, 6, "g0", "f21", "o02", [] [] byte{}},
        //		play{2, 6, "g2", "g10", "g21", [] [] byte{}},
        //		play{0, 7, "o02", "g21", "g02", [] [] byte{}},
        //		play{1, 7, "g10", "g02", "h1", [] [] byte{}},
        //		play{0, 8, "g02", "h1", "h0", [] [] byte{}},
        //		play{2, 7, "g21", "h1", "h2", [] [] byte{}},
        //	}

        //	for _, p = range plays
        //{
        //    e = NewEvent(p.payload,
        //			[]string{ index[p.selfParent], index[p.otherParent]},
        //			nodes [p.to].Pub,
        //			p.index)
        //		nodes [p.to].signAndAddEvent(e, p.name, index, orderedEvents)
        //	}

        //participants = make(map[string]int)
        //	for _, node = range nodes
        //{
        //    participants [node.PubHex] = node.ID
        //}

        //var store Store
        //	if db {
        //		var err error
        //        store, err = NewBadgerStore(participants, cacheSize, badgerDir)
        //		if err != nil {
        //			log.Fatal(err)
        //		}
        //	} else {
        //		store = NewInmemStore(participants, cacheSize)
        //	}

        //	hashgraph = NewHashgraph(participants, store, nil, logger)

        //	for i, ev = range* orderedEvents
        //{
        //		if err = hashgraph.InsertEvent(ev, true); err != nil
        //    {
        //        fmt.Printf("ERROR inserting event %d: %s\n", i, err)

        //        }
        //}

        //	return hashgraph, index
        //}

        //        [Fact]
        //        public voidTestDecideFame(t* testing.T)
        //{
        //    h, index= initConsensusHashgraph(false, common.NewTestLogger(t))

        //    h.DivideRounds()

        //    h.DecideFame()

        //    if r = h.Round(index["g0"]); r != 2 {
        //        t.Fatalf("g0 round should be 2, not %d", r)

        //    }
        //    if r = h.Round(index["g1"]); r != 2 {
        //        t.Fatalf("g1 round should be 2, not %d", r)

        //    }
        //    if r = h.Round(index["g2"]); r != 2 {
        //        t.Fatalf("g2 round should be 2, not %d", r)

        //    }

        //    round0, err= h.Store.GetRound(0)

        //    if err != nil {
        //        t.Fatal(err)

        //    }
        //    if f = round0.Events[index["e0"]]; !(f.Witness && f.Famous == True) {
        //        t.Fatalf("e0 should be famous; got %v", f)

        //    }
        //    if f = round0.Events[index["e1"]]; !(f.Witness && f.Famous == True) {
        //        t.Fatalf("e1 should be famous; got %v", f)

        //    }
        //    if f = round0.Events[index["e2"]]; !(f.Witness && f.Famous == True) {
        //        t.Fatalf("e2 should be famous; got %v", f)

        //    }
        //}

        //        [Fact]
        //        public void TestOldestSelfAncestorToSee(t* testing.T)
        //{
        //    h, index= initConsensusHashgraph(false, common.NewTestLogger(t))

        //    if a = h.OldestSelfAncestorToSee(index["f0"], index["e1"]); a != index["e02"] {
        //        t.Fatalf("oldest self ancestor of f0 to see e1 should be e02 not %s", getName(index, a))

        //    }
        //    if a = h.OldestSelfAncestorToSee(index["f1"], index["e0"]); a != index["e10"] {
        //        t.Fatalf("oldest self ancestor of f1 to see e0 should be e10 not %s", getName(index, a))

        //    }
        //    if a = h.OldestSelfAncestorToSee(index["f1b"], index["e0"]); a != index["e10"] {
        //        t.Fatalf("oldest self ancestor of f1b to see e0 should be e10 not %s", getName(index, a))

        //    }
        //    if a = h.OldestSelfAncestorToSee(index["g2"], index["f1"]); a != index["f2"] {
        //        t.Fatalf("oldest self ancestor of g2 to see f1 should be f2 not %s", getName(index, a))

        //    }
        //    if a = h.OldestSelfAncestorToSee(index["e21"], index["e1"]); a != index["e21"] {
        //        t.Fatalf("oldest self ancestor of e20 to see e1 should be e21 not %s", getName(index, a))

        //    }
        //    if a = h.OldestSelfAncestorToSee(index["e2"], index["e1"]); a != "" {
        //        t.Fatalf("oldest self ancestor of e2 to see e1 should be '' not %s", getName(index, a))

        //    }
        //}

        //        [Fact]
        //        public void TestDecideRoundReceived(t* testing.T)
        //{
        //    h, index= initConsensusHashgraph(false, common.NewTestLogger(t))

        //    h.DivideRounds()

        //    h.DecideFame()

        //    h.DecideRoundReceived()

        //    for name, hash = range index {
        //        e, _= h.Store.GetEvent(hash)

        //        if rune(name[0]) == rune('e') {
        //            if r = *e.roundReceived; r != 1 {
        //                t.Fatalf("%s round received should be 1 not %d", name, r)

        //            }
        //        }
        //    }

        //}

        //[Fact]
        //public void TestFindOrder(t* testing.T)
        //{
        //    h, index= initConsensusHashgraph(false, common.NewTestLogger(t))

        //    h.DivideRounds()

        //    h.DecideFame()

        //    h.FindOrder()

        //    for i, e = range h.ConsensusEvents() {
        //        t.Logf("consensus[%d]: %s\n", i, getName(index, e))

        //    }

        //    if l = len(h.ConsensusEvents()); l != 7 {
        //        t.Fatalf("length of consensus should be 7 not %d", l)

        //    }

        //    if ple = h.PendingLoadedEvents; ple != 2 {
        //        t.Fatalf("PendingLoadedEvents should be 2, not %d", ple)

        //    }

        //    consensusEvents= h.ConsensusEvents()

        //    if n = getName(index, consensusEvents[0]); n != "e0" {
        //        t.Fatalf("consensus[0] should be e0, not %s", n)

        //    }

        //    //events which have the same consensus timestamp are ordered by whitened signature
        //    //which is not deterministic.
        //    if n = getName(index, consensusEvents[6]); n != "e02" {
        //        t.Fatalf("consensus[6] should be e02, not %s", n)

        //    }

        //}

        // [Fact] public void BenchmarkFindOrder(b* testing.B)
        //{
        //    for n = 0; n < b.N; n++ {
        //        //we do not want to benchmark the initialization code
        //        b.StopTimer()

        //        h, _= initConsensusHashgraph(false, common.NewBenchmarkLogger(b))

        //        b.StartTimer()

        //        h.DivideRounds()

        //        h.DecideFame()

        //        h.FindOrder()

        //    }
        //}

        // [Fact] public void TestKnown(t* testing.T)
        //{
        //    h, _= initConsensusHashgraph(false, common.NewTestLogger(t))

        //    expectedKnown= map[int]int{
        //        0: 8,
        //		1: 7,
        //		2: 7,
        //	}

        //    known= h.Known()

        //    for _, id = range h.Participants {
        //        if l = known[id]; l != expectedKnown[id] {
        //            t.Fatalf("Known[%d] should be %d, not %d", id, expectedKnown[id], l)

        //        }
        //    }
        //}

        // [Fact] public void TestReset(t* testing.T)
        //{
        //    h, index= initConsensusHashgraph(false, common.NewTestLogger(t))

        //    evs= []string{ "g1", "g0", "g2", "g10", "g21", "o02", "g02", "h1", "h0", "h2"}

        //    backup= map[string]Event{ }
        //    for _, ev = range evs {
        //		event, err= h.Store.GetEvent(index[ev])

        //        if err != nil {
        //            t.Fatal(err)

        //        }

        //        copyEvent= Event{
        //            Body: event.Body,
        //			R:    event.R,
        //			S:    event.S,
        //		}

        //        backup[ev] = copyEvent

        //    }

        //    roots= map[string]Root{ }
        //    roots[h.ReverseParticipants[0]] = Root{
        //        X: index["f02b"],
        //		Y: index["g1"],
        //		Index: 4,
        //		Round: 2,
        //		Others: map[string]string{
        //            index["o02"]: index["f21"],
        //		},
        //	}
        //    roots[h.ReverseParticipants[1]] = Root{
        //        X: index["f10"],
        //		Y: index["f02b"],
        //		Index: 4,
        //		Round: 2,
        //	}
        //    roots[h.ReverseParticipants[2]] = Root{
        //        X: index["f21"],
        //		Y: index["g1"],
        //		Index: 4,
        //		Round: 2,
        //	}

        //    err= h.Reset(roots)

        //    if err != nil {
        //        t.Fatal(err)

        //    }

        //    for _, k = range evs {
        //        if err = h.InsertEvent(backup[k], false); err != nil {
        //            t.Fatalf("Error inserting %s in reset Hashgraph: %v", k, err)

        //        }
        //        if _, err= h.Store.GetEvent(index[k]); err != nil {
        //            t.Fatalf("Error fetching %s after inserting it in reset Hashgraph: %v", k, err)

        //        }
        //    }

        //    expectedKnown= map[int]int{
        //        0: 8,
        //		1: 7,
        //		2: 7,
        //	}

        //    known= h.Known()

        //    for _, id = range h.Participants {
        //        if l = known[id]; l != expectedKnown[id] {
        //            t.Fatalf("Known[%d] should be %d, not %d", id, expectedKnown[id], l)

        //        }
        //    }
        //}

        // [Fact] public void TestGetFrame(t* testing.T)
        //{
        //    h, index= initConsensusHashgraph(false, common.NewTestLogger(t))

        //    h.DivideRounds()

        //    h.DecideFame()

        //    h.FindOrder()

        //    expectedRoots= map[string]Root{ }
        //    expectedRoots[h.ReverseParticipants[0]] = Root{
        //        X: index["e02"],
        //		Y: index["f1b"],
        //		Index: 1,
        //		Round: 0,
        //		Others: map[string]string{ },
        //	}
        //    expectedRoots[h.ReverseParticipants[1]] = Root{
        //        X: index["e10"],
        //		Y: index["e02"],
        //		Index: 1,
        //		Round: 0,
        //		Others: map[string]string{ },
        //	}
        //    expectedRoots[h.ReverseParticipants[2]] = Root{
        //        X: index["e21b"],
        //		Y: index["f1b"],
        //		Index: 2,
        //		Round: 0,
        //		Others: map[string]string{ },
        //	}

        //    frame, err= h.GetFrame()

        //    if err != nil {
        //        t.Fatal(err)

        //    }

        //    for p, r = range frame.Roots {
        //        er, ok= expectedRoots[p]

        //        if !ok {
        //            t.Fatalf("No Root returned for %s", p)

        //        }
        //        if x = r.X; x != er.X {
        //            t.Fatalf("Roots[%s].X should be %s, not %s", p, er.X, x)

        //        }
        //        if y = r.Y; y != er.Y {
        //            t.Fatalf("Roots[%s].Y should be %s, not %s", p, er.Y, y)

        //        }
        //        if ind = r.Index; ind != er.Index {
        //            t.Fatalf("Roots[%s].Index should be %d, not %d", p, er.Index, ind)

        //        }
        //        if ro = r.Round; ro != er.Round {
        //            t.Fatalf("Roots[%s].Round should be %d, not %d", p, er.Round, ro)

        //        }
        //        if others = r.Others; !reflect.DeepEqual(others, er.Others) {
        //            t.Fatalf("Roots[%s].Others should be %#v, not %#v", p, er.Others, others)

        //        }

        //    }

        //    skip= map[string]int{
        //        h.ReverseParticipants[0]: 1,
        //		h.ReverseParticipants[1]: 1,
        //		h.ReverseParticipants[2]: 2,
        //	}

        //    expectedEvents= []Event{ }
        //    for p, r = range frame.Roots {
        //        ee, err= h.Store.ParticipantEvents(p, skip[p])

        //        if err != nil {
        //            t.Fatal(r)

        //        }
        //        for _, e = range ee {
        //            ev, err= h.Store.GetEvent(e)

        //            if err != nil {
        //                t.Fatal(err)

        //            }
        //            expectedEvents = append(expectedEvents, ev)

        //        }
        //    }
        //    sort.Sort(ByTopologicalOrder(expectedEvents))

        //    if !reflect.DeepEqual(expectedEvents, frame.Events) {
        //        t.Fatal("Frame.Events is not good")

        //    }

        //}

        // [Fact] public void TestResetFromFrame(t* testing.T)
        //{
        //    h, _= initConsensusHashgraph(false, common.NewTestLogger(t))

        //    h.DivideRounds()

        //    h.DecideFame()

        //    h.FindOrder()

        //    frame, err= h.GetFrame()

        //    if err != nil {
        //        t.Fatal(err)

        //    }

        //    err = h.Reset(frame.Roots)

        //    if err != nil {
        //        t.Fatal(err)

        //    }

        //    for _, ev = range frame.Events {
        //        if err = h.InsertEvent(ev, false); err != nil {
        //            t.Fatalf("Error inserting %s in reset Hashgraph: %v", ev.Hex(), err)

        //        }
        //    }

        //    expectedKnown= map[int]int{
        //        0: 8,
        //		1: 7,
        //		2: 7,
        //	}

        //    known= h.Known()

        //    for _, id = range h.Participants {
        //        if l = known[id]; l != expectedKnown[id] {
        //            t.Fatalf("Known[%d] should be %d, not %d", id, expectedKnown[id], l)

        //        }
        //    }

        //    h.DivideRounds()

        //    h.DecideFame()

        //    h.FindOrder()

        //    if r = h.LastConsensusRound; r == nil || *r != 1 {
        //        disp= "nil"

        //        if r != nil {
        //            disp = strconv.Itoa(*r)

        //        }
        //        t.Fatalf("LastConsensusRound should be 1, not %s", disp)

        //    }
        //}

        // [Fact] public void TestBootstrap(t* testing.T)
        //{
        //    logger= common.NewTestLogger(t)

        //    //Initialize a first Hashgraph with a DB backend
        //    //Add events and run consensus methods on it
        //    h, _= initConsensusHashgraph(true, logger)

        //    h.DivideRounds()

        //    h.DecideFame()

        //    h.FindOrder()

        //    h.Store.Close()

        //    defer os.RemoveAll(badgerDir)

        //    //Now we want to create a new Hashgraph based on the database of the previous
        //    //Hashgraph and see if we can boostrap it to the same state.
        //    recycledStore, err= LoadBadgerStore(cacheSize, badgerDir)

        //    nh= NewHashgraph(recycledStore.participants, recycledStore, nil, logger)

        //    err = nh.Bootstrap()

        //    if err != nil {
        //        t.Fatal(err)

        //    }

        //    hConsensusEvents= h.ConsensusEvents()

        //    nhConsensusEvents= nh.ConsensusEvents()

        //    if len(hConsensusEvents) != len(nhConsensusEvents) {
        //        t.Fatalf("Bootstrapped hashgraph should contain %d consensus events,not %d",
        //            len(hConsensusEvents), len(nhConsensusEvents))

        //    }

        //    hKnown= h.Known()

        //    nhKnown= nh.Known()

        //    if !reflect.DeepEqual(hKnown, nhKnown) {
        //        t.Fatalf("Bootstrapped hashgraph's Known should be %#v, not %#v",
        //            hKnown, nhKnown)

        //    }

        //    if *h.LastConsensusRound != *nh.LastConsensusRound {
        //        t.Fatalf("Bootstrapped hashgraph's LastConsensusRound should be %#v, not %#v",
        //            *h.LastConsensusRound, *nh.LastConsensusRound)

        //    }

        //    if h.LastCommitedRoundEvents != nh.LastCommitedRoundEvents {
        //        t.Fatalf("Bootstrapped hashgraph's LastCommitedRoundEvents should be %#v, not %#v",
        //            h.LastCommitedRoundEvents, nh.LastCommitedRoundEvents)

        //    }

        //    if h.ConsensusTransactions != nh.ConsensusTransactions {
        //        t.Fatalf("Bootstrapped hashgraph's ConsensusTransactions should be %#v, not %#v",
        //            h.ConsensusTransactions, nh.ConsensusTransactions)

        //    }

        //    if h.PendingLoadedEvents != nh.PendingLoadedEvents {
        //        t.Fatalf("Bootstrapped hashgraph's PendingLoadedEvents should be %#v, not %#v",
        //            h.PendingLoadedEvents, nh.PendingLoadedEvents)

        //    }
        //}

        ///*
        //    |    |    |    |
        //	|    |    |    |w51 collects votes from w40, w41, w42 and w43.
        //    |   w51   |    |IT DECIDES YES
        //    |    |  \ |    |
        //	|    |   e23   |
        //    |    |    | \  |------------------------
        //    |    |    |   w43
        //    |    |    | /  | Round 4 is a Coin Round. No decision will be made.
        //    |    |   w42   |
        //    |    | /  |    | w40 collects votes from w33, w32 and w31. It votes yes.
        //    |   w41   |    | w41 collects votes from w33, w32 and w31. It votes yes.
        //	| /  |    |    | w42 collects votes from w30, w31, w32 and w33. It votes yes.
        //   w40   |    |    | w43 collects votes from w30, w31, w32 and w33. It votes yes.
        //    | \  |    |    |------------------------
        //    |   d13   |    | w30 collects votes from w20, w21, w22 and w23. It votes yes
        //    |    |  \ |    | w31 collects votes from w21, w22 and w23. It votes no
        //   w30   |    \    | w32 collects votes from w20, w21, w22 and w23. It votes yes
        //    | \  |    | \  | w33 collects votes from w20, w21, w22 and w23. It votes yes
        //    |   \     |   w33
        //    |    | \  |  / |Again, none of the witnesses in round 3 are able to decide.
        //    |    |   w32   |However, a strong majority votes yes
        //    |    |  / |    |
        //	|   w31   |    |
        //    |  / |    |    |--------------------------
        //   w20   |    |    | w23 collects votes from w11, w12 and w13. It votes no
        //    |  \ |    |    | w21 collects votes from w11, w12, and w13. It votes no
        //    |    \    |    | w22 collects votes from w11, w12, w13 and w14. It votes yes
        //    |    | \  |    | w20 collects votes from w11, w12, w13 and w14. It votes yes
        //    |    |   w22   |
        //    |    | /  |    | None of the witnesses in round 2 were able to decide.
        //    |   c10   |    | They voted according to the majority of votes they observed
        //    | /  |    |    | in round 1. The vote is split 2-2
        //   b00  w21   |    |
        //    |    |  \ |    |
        //    |    |    \    |
        //    |    |    | \  |
        //    |    |    |   w23
        //    |    |    | /  |------------------------
        //   w10   |   b21   |
        //	| \  | /  |    | w10 votes yes (it can see w00)
        //    |   w11   |    | w11 votes yes
        //    |    |  \ |    | w12 votes no  (it cannot see w00)
        //	|    |   w12   | w13 votes no
        //    |    |    | \  |
        //    |    |    |   w13
        //    |    |    | /  |------------------------
        //    |   a10  a21   | We want to decide the fame of w00
        //    |  / |  / |    |
        //    |/  a12   |    |
        //   a00   |  \ |    |
        //	|    |   a23   |
        //    |    |    | \  |
        //   w00  w01  w02  w03
        //	0	 1	  2	   3
        //*/

        // [Fact] public void initFunkyHashgraph(logger* logrus.Logger) (* Hashgraph, map[string]string) {
        //	index = make(map[string]string)

        //    nodes = [] Node{}
        //	orderedEvents = &[] Event{}

        //	n = 4
        //	for i = 0; i<n; i++ {
        //		key, _ = crypto.GenerateECDSAKey()
        //        node = NewNode(key, i)

        //        event = NewEvent([]
        //[]byte{ }, []string{ "", ""}, node.Pub, 0)
        //		node.signAndAddEvent(event, fmt.Sprintf("w0%d", i), index, orderedEvents)
        //		nodes = append(nodes, node)
        //	}

        //plays = [] play{
        //		play{2, 1, "w02", "w03", "a23", [] [] byte{}},
        //		play{1, 1, "w01", "a23", "a12", [] [] byte{}},
        //		play{0, 1, "w00", "", "a00", [] [] byte{}},
        //		play{1, 2, "a12", "a00", "a10", [] [] byte{}},
        //		play{2, 2, "a23", "a12", "a21", [] [] byte{}},
        //		play{3, 1, "w03", "a21", "w13", [] [] byte{}},
        //		play{2, 3, "a21", "w13", "w12", [] [] byte{}},
        //		play{1, 3, "a10", "w12", "w11", [] [] byte{}},
        //		play{0, 2, "a00", "w11", "w10", [] [] byte{}},
        //		play{2, 4, "w12", "w11", "b21", [] [] byte{}},
        //		play{3, 2, "w13", "b21", "w23", [] [] byte{}},
        //		play{1, 4, "w11", "w23", "w21", [] [] byte{}},
        //		play{0, 3, "w10", "", "b00", [] [] byte{}},
        //		play{1, 5, "w21", "b00", "c10", [] [] byte{}},
        //		play{2, 5, "b21", "c10", "w22", [] [] byte{}},
        //		play{0, 4, "b00", "w22", "w20", [] [] byte{}},
        //		play{1, 6, "c10", "w20", "w31", [] [] byte{}},
        //		play{2, 6, "w22", "w31", "w32", [] [] byte{}},
        //		play{0, 5, "w20", "w32", "w30", [] [] byte{}},
        //		play{3, 3, "w23", "w32", "w33", [] [] byte{}},
        //		play{1, 7, "w31", "w33", "d13", [] [] byte{}},
        //		play{0, 6, "w30", "d13", "w40", [] [] byte{}},
        //		play{1, 8, "d13", "w40", "w41", [] [] byte{}},
        //		play{2, 7, "w32", "w41", "w42", [] [] byte{}},
        //		play{3, 4, "w33", "w42", "w43", [] [] byte{}},
        //		play{2, 8, "w42", "w43", "e23", [] [] byte{}},
        //		play{1, 9, "w41", "e23", "w51", [] [] byte{}},
        //	}

        //	for _, p = range plays
        //{
        //    e = NewEvent(p.payload,
        //			[]string{ index[p.selfParent], index[p.otherParent]},
        //			nodes [p.to].Pub,
        //			p.index)
        //		nodes [p.to].signAndAddEvent(e, p.name, index, orderedEvents)
        //	}

        //participants = make(map[string]int)
        //	for _, node = range nodes
        //{
        //    participants [node.PubHex] = node.ID
        //}

        //hashgraph = NewHashgraph(participants,
        //    NewInmemStore(participants, cacheSize),
        //    nil, logger)

        //	for i, ev = range* orderedEvents
        //{
        //		if err = hashgraph.InsertEvent(ev, true); err != nil
        //    {
        //        fmt.Printf("ERROR inserting event %d: %s\n", i, err)

        //        }
        //}

        //	return hashgraph, index
        //}

        // [Fact] public void TestFunkyHashgraphFame(t* testing.T)
        //{
        //    h, index= initFunkyHashgraph(common.NewTestLogger(t))

        //    h.DivideRounds()

        //    if l = h.Store.LastRound(); l != 5 {
        //        t.Fatalf("last round should be 5 not %d", l)

        //    }

        //    for r = 0; r < 6; r++ {
        //        round, err= h.Store.GetRound(r)

        //        if err != nil {
        //            t.Fatal(err)

        //        }
        //        witnessNames= []string{ }
        //        for _, w = range round.Witnesses() {
        //            witnessNames = append(witnessNames, getName(index, w))

        //        }
        //        t.Logf("Round %d witnesses: %v", r, witnessNames)

        //    }

        //    h.DecideFame()

        //    //rounds 0,1, 2 and 3 should be decided
        //    expectedUndecidedRounds= []int{ 4, 5}
        //    if !reflect.DeepEqual(expectedUndecidedRounds, h.UndecidedRounds) {
        //        t.Fatalf("UndecidedRounds should be %v, not %v", expectedUndecidedRounds, h.UndecidedRounds)

        //    }
        //}

        // [Fact] public void getName(index map[string]string, hash string) string {
        //	for name, h = range index
        //{
        //		if h == hash
        //    {
        //        return name

        //        }
        //}
        //	return ""
        //}

        // [Fact] public void disp(index map[string]string, events[]string) string {
        //	names = [] string{}
        //	for _, h = range events
        //{
        //    names = append(names, getName(index, h))
        //	}
        //	return fmt.Sprintf("[%s]", strings.Join(names, " "))
        //}
    }
}