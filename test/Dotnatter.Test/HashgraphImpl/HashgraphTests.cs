using System.Collections.Generic;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using Dotnatter.Crypto;
using Dotnatter.HashgraphImpl;
using Dotnatter.Util;
using Xunit;

namespace Dotnatter.Test.HashgraphImpl
{
    public class HashgraphTests
    {
        private readonly int cacheSize = 100;

        private readonly int n = 3;

        private string badgerDir = "test_data/badger";

        public class Node
        {
            public int Id { get; set; }
            public byte[] Pub { get; set; }
            public string PubHex { get; set; }
            public CngKey Key { get; set; }
            public List<Event> Events { get; set; }

            public Node(CngKey key, int id)
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

            for (var i = 0; i < n; i++)
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

            foreach (var p in  plays)
            {
                var parents = new List<string>();
                parents.Add(index[p.SelfParent]);
                index.TryGetValue(p.OtherParent, out var otherParent);
    
                    parents.Add(otherParent??"");
            

                var e = new Event(p.Payload,
                    parents.ToArray(),
                    nodes[p.To].Pub,
                    p.Index);

                nodes[p.To].SignAndAddEvent(e, p.Name, index, orderedEvents);
            }

            var participants =new Dictionary<string,int>();
            foreach (var node in nodes)
            {
                participants[node.PubHex] = node.Id;
            }

            var store = new InmemStore(participants, cacheSize);

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




    }
}