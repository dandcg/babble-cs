using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Babble.Core.Crypto;
using Babble.Core.HashgraphImpl.Model;
using Babble.Core.HashgraphImpl.Stores;
using Babble.Core.NodeImpl;
using Babble.Core.Util;
using Dotnatter.Test.Helpers;
using Serilog;
using Xunit;
using Xunit.Abstractions;

namespace Dotnatter.Test.NodeImpl
{
    public class CoreTests
    {
        private readonly ITestOutputHelper output;
        private readonly ILogger logger;

        public CoreTests(ITestOutputHelper output)
        {
            this.output = output;
            logger = output.SetupLogging().ForContext("SourceContext", "HashGraphTests");
        }

        [Fact]
        public async Task TestInit()
        {
            var key = CryptoUtils.GenerateEcdsaKey();

            var participants = new Dictionary<string, int> {{CryptoUtils.FromEcdsaPub(key).ToHex(), 0}};
            var core = new Core(0, key, participants, new InmemStore(participants, 10, logger), null, logger);

            var err = await core.Init();

            Assert.Null(err);
        }

        private async Task<(Core[] cores, CngKey[] privateKey, Dictionary<string, string> index)> InitCores(int n)
        {
            var cacheSize = 1000;

            var cores = new List<Core>();
            var index = new Dictionary<string, string>();

            var participantKeys = new List<CngKey>();
            var participants = new Dictionary<string, int>();
            for (var i = 0; i < n; i++)
            {
                var key = CryptoUtils.GenerateEcdsaKey();
                participantKeys.Add(key);
                participants[CryptoUtils.FromEcdsaPub(key).ToHex()] = i;
            }

            for (var i = 0; i < n; i++)
            {
                var core = new Core(i, participantKeys[i], participants, new InmemStore(participants, cacheSize, logger), null, logger);
                var err=await core.Init();
                Assert.Null(err);

                cores.Add(core);
                
                index[$"e{i}"] = core.Head;
            }

            return (cores.ToArray(), participantKeys.ToArray(), index);
        }

        [Fact]
        public async Task TestInitCores()
        {
            var (cores, privateKey, index) = await InitCores(3);
            Assert.NotEmpty(cores);
            Assert.NotEmpty(privateKey);
            Assert.NotEmpty(index);
        }

        /*
        |  e12  |
        |   | \ |
        |   |   e20
        |   | / |
        |   /   |
        | / |   |
        e01 |   |
        | \ |   |
        e0  e1  e2
        0   1   2
        */
        private async Task InitHashgraph(Core[] cores, CngKey[] keys, Dictionary<string, string> index, int participant)

        {
            Exception err;
            for (var i = 0; i < cores.Length; i++)
            {
                if (i != participant)
                {
                    var evh = index[$"e{i}"];
                    
                    var ( ev, _) = await cores[i].GetEvent(evh);

                    err = await cores[participant].InsertEvent(ev, true);

                    if (err != null)
                    {
                        output.WriteLine("error inserting {0}: {1} ", GetName(index, ev.Hex()), err);
                    }
                }
            }

            var event01 = new Event(new byte[][] { },null,
                new[] {index["e0"], index["e1"]}, //e0 and e1
                cores[0].PubKey(), 1);

            err =await  InsertEvent(cores, keys, index, event01, "e01", participant, 0);
            if (err != null)
            {
                output.WriteLine("error inserting e01: {0}", err);
            }

            var event20 = new Event(new byte[][] { },null,
                new[] {index["e2"], index["e01"]}, //e2 and e01
                cores[2].PubKey(), 1);

            err =await  InsertEvent(cores, keys, index, event20, "e20", participant, 2);
            if (err != null)
            {
                output.WriteLine("error inserting e20: {0}", err);
            }

            var event12 = new Event(new byte[][] { },null,
                new[] {index["e1"], index["e20"]}, //e1 and e20
                cores[1].PubKey(), 1);

            err =await  InsertEvent(cores, keys, index, event12, "e12", participant, 1);

            if (err != null)
            {
                output.WriteLine("error inserting e12: {err}", err);
            }
        }

        public async Task<Exception> InsertEvent(Core[] cores, CngKey[] keys, Dictionary<string, string> index, Event ev, string name, int particant, int creator)
        {
            Exception err;
            if (particant == creator)
            {
                err = await cores[particant].SignAndInsertSelfEvent(ev);

                if (err != null)
                {
                    return err;
                }

                //event is not signed because passed by value
                index[name] = cores[particant].Head;
            }
            else
            {
                ev.Sign(keys[creator]);

                err = await cores[particant].InsertEvent(ev, true);

                if (err != null)
                {
                    return err;
                }

                index[name] = ev.Hex();
            }

            return null;
        }

        [Fact]
        public async Task TestEventDiff()
        {
            var (cores, keys, index) =await  InitCores(3);

            await InitHashgraph(cores, keys, index, 0);

            /*
               P0 knows
        
               |  e12  |
               |   | \ |
               |   |   e20
               |   | / |
               |   /   |
               | / |   |
               e01 |   |        P1 knows
               | \ |   |
               e0  e1  e2       |   e1  |
               0   1   2        0   1   2
            */

            var knownBy1 =await  cores[1].KnownEvents();
            var (unknownBy1, err) =await  cores[0].EventDiff(knownBy1);

            Assert.Null(err);
            var l = unknownBy1.Length;

            Assert.Equal(5, l);

            var expectedOrder = new[] {"e0", "e2", "e01", "e20", "e12"};

            var i = 0;
            foreach (var e in unknownBy1)
            {
                var name = GetName(index, e.Hex());
                Assert.True(name == expectedOrder[i], $"element {i} should be {expectedOrder[i]}, not {name}");
                i++;
            }
        }

        [Fact]
        public async Task TestSync()

        {
            var (cores, _, index) = await InitCores(3);

            /*
               core 0           core 1          core 2
        
               e0  |   |        |   e1  |       |   |   e2
               0   1   2        0   1   2       0   1   2
            */

            //core 1 is going to tell core 0 everything it knows

            var err = await SynchronizeCores(cores, 1, 0, new byte[][] { });

            Assert.Null(err);

            /*
               core 0           core 1          core 2
        
               e01 |   |
               | \ |   |
               e0  e1  |        |   e1  |       |   |   e2
               0   1   2        0   1   2       0   1   2
            */

            var knownBy0 =await  cores[0].KnownEvents();

            var k = knownBy0[cores[0].Id()];
            Assert.False(k != 1, "core 0 should have last-index 1 for core 0, not {k}");

            k = knownBy0[cores[1].Id()];
            Assert.False(k != 0, "core 0 should have last-index 0 for core 1, not {k}");

            k = knownBy0[cores[2].Id()];

            Assert.False(k != -1, "core 0 should have last-index -1 for core 2, not {k}");

            var (core0Head, _ ) =await  cores[0].GetHead();

            Assert.False(core0Head.SelfParent != index["e0"], "core 0 head self-parent should be e0");

            Assert.False(core0Head.OtherParent != index["e1"], "core 0 head other-parent should be e1");

            index["e01"] = core0Head.Hex();

            //core 0 is going to tell core 2 everything it knows
            err = await SynchronizeCores(cores, 0, 2, new byte[][] { });

            Assert.Null(err);

            /*
        
               core 0           core 1          core 2
        
                                                |   |  e20
                                                |   | / |
                                                |   /   |
                                                | / |   |
               e01 |   |                        e01 |   |
               | \ |   |                        | \ |   |
               e0  e1  |        |   e1  |       e0  e1  e2
               0   1   2        0   1   2       0   1   2
            */

            var knownBy2 = await cores[2].KnownEvents();

            k = knownBy2[cores[0].Id()];
            Assert.False(k != 1, "core 2 should have last-index 1 for core 0, not {k}");

            k = knownBy2[cores[1].Id()];
            Assert.False(k != 0, "core 2 should have last-index 0 core 1, not {k}");

            k = knownBy2[cores[2].Id()];
            Assert.False(k != 1, "core 2 should have last-index 1 for core 2, not {k}");

            var (core2Head, _) =await  cores[2].GetHead();

            Assert.Equal(index["e2"], core2Head.SelfParent); // core 2 head self-parent should be e2
            Assert.Equal(index["e01"], core2Head.OtherParent); // core 2 head other-parent should be e01

            index["e20"] = core2Head.Hex();

            //core 2 is going to tell core 1 everything it knows
            err =await  SynchronizeCores(cores, 2, 1, new byte[][] { });

            Assert.Null(err);

            /*
        
               core 0           core 1          core 2
        
                                |  e12  |
                                |   | \ |
                                |   |  e20      |   |  e20
                                |   | / |       |   | / |
                                |   /   |       |   /   |
                                | / |   |       | / |   |
               e01 |   |        e01 |   |       e01 |   |
               | \ |   |        | \ |   |       | \ |   |
               e0  e1  |        e0  e1  e2      e0  e1  e2
               0   1   2        0   1   2       0   1   2
            */

            var knownBy1 = await cores[1].KnownEvents();
            k = knownBy1[cores[0].Id()];

            Assert.False(k != 1, "core 1 should have last-index 1 for core 0, not {k}");

            k = knownBy1[cores[1].Id()];

            Assert.False(k != 1, "core 1 should have last-index 1 for core 1, not {k}");

            k = knownBy1[cores[2].Id()];

            Assert.False(k != 1, "core 1 should have last-index 1 for core 2, not {k}");

            var (core1Head, _) = await cores[1].GetHead();
            Assert.False(core1Head.SelfParent != index["e1"], "core 1 head self-parent should be e1");

            Assert.False(core1Head.OtherParent != index["e20"], "core 1 head other-parent should be e20");

            index["e12"] = core1Head.Hex();
        }

/*
h0  |   h2
| \ | / |
|   h1  |
|  /|   |--------------------
g02 |   | R2
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
|  /|   |--------------------
f02 |   | R1
| \ |   |
|   \   |
|   | \ |
|   |  f21
|   | / |
|  f10  |
| / |   |
f0  |   f2
| \ | / |
|   f1  |
|  /|   |--------------------
e02 |   | R0 Consensus
| \ |   |
|   \   |
|   | \ |
|   |  e21
|   | / |
|  e10  |
| / |   |
e0  e1  e2
0   1    2
*/
        public class Play
        {
            public int From { get; }
            public int To { get; }
            public byte[][] Payload { get; }

            public Play(int from, int to, byte[][] payload)
            {
                From = from;
                To = to;
                Payload = payload;
            }
        }

        private async Task<Core[]> InitConsensusHashgraph()
        {
            var (cores, _, _) =await  InitCores(3);
            var playbook = new[]
            {
                new Play(0, 1, new[] {"e10".StringToBytes()}),
                new Play(1, 2, new[] {"e21".StringToBytes()}),
                new Play(2, 0, new[] {"e02".StringToBytes()}),
                new Play(0, 1, new[] {"f1".StringToBytes()}),
                new Play(1, 0, new[] {"f0".StringToBytes()}),
                new Play(1, 2, new[] {"f2".StringToBytes()}),

                new Play(0, 1, new[] {"f10".StringToBytes()}),
                new Play(1, 2, new[] {"f21".StringToBytes()}),
                new Play(2, 0, new[] {"f02".StringToBytes()}),
                new Play(0, 1, new[] {"g1".StringToBytes()}),
                new Play(1, 0, new[] {"g0".StringToBytes()}),
                new Play(1, 2, new[] {"g2".StringToBytes()}),

                new Play(0, 1, new[] {"g10".StringToBytes()}),
                new Play(1, 2, new[] {"g21".StringToBytes()}),
                new Play(2, 0, new[] {"g02".StringToBytes()}),
                new Play(0, 1, new[] {"h1".StringToBytes()}),
                new Play(1, 0, new[] {"h0".StringToBytes()}),
                new Play(1, 2, new[] {"h2".StringToBytes()})
            };

            foreach (var play in playbook)
            {
                var err = await SyncAndRunConsensus(cores, play.From, play.To, play.Payload);

                Assert.Null(err);
            }

            return cores;
        }

        [Fact]
        public async Task TestConsensus()
        {
            var cores = await InitConsensusHashgraph();

            var l = cores[0].GetConsensusEvents().Length;

            Assert.Equal(6, l); //length of consensus should be 6

            var core0Consensus = cores[0].GetConsensusEvents();
            var core1Consensus = cores[1].GetConsensusEvents();
            var core2Consensus = cores[2].GetConsensusEvents();

            //for (var i = 0; i < l; i++)
            //{
            //    output.WriteLine("{0}: {1}, {2}, {3}", i ,core0Consensus[i],core1Consensus[i],core2Consensus[i] );
            //}

            for (var i = 0; i < l; i++)

            {
                var e = core0Consensus[i];

                Assert.Equal(e, core1Consensus[i]); //core 1 consensus[%d] does not match core 0's
                Assert.Equal(e, core2Consensus[i]); //core 2 consensus[%d] does not match core 0's
            }
        }

        [Fact]
        public async Task TestOverSyncLimit()
        {
            var cores =await  InitConsensusHashgraph();

            var known = new Dictionary<int, int>();

            var syncLimit = 10;

            //positive
            for (var i = 0; i < 3; i++)
            {
                known[i] = 1;
            }

            Assert.True(await cores[0].OverSyncLimit(known, syncLimit), $"OverSyncLimit({known}, {syncLimit}) should return true");

            //negative
            for (var i = 0; i < 3; i++)
            {
                known[i] = 6;
            }

            Assert.False(await cores[0].OverSyncLimit(known, syncLimit), $"OverSyncLimit({known}, {syncLimit}) should return false");

            //edge
            known = new Dictionary<int, int>
            {
                {0, 2},
                {1, 3},
                {2, 3}
            };

            Assert.False(await cores[0].OverSyncLimit(known, syncLimit), $"OverSyncLimit({known}, {syncLimit}) should return false");
        }

        /*

    |   |   |   |-----------------
	|   w31 |   | R3
	|	| \ |   |
    |   |  w32  |
    |   |   | \ |
    |   |   |  w33
    |   |   | / |-----------------
    |   |  g21  | R2
	|   | / |   |
	|   w21 |   |
	|	| \ |   |
    |   |   \   |
    |   |   | \ |
    |   |   |  w23
    |   |   | / |
    |   |  w22  |
	|   | / |   |-----------------
	|  f13  |   | R1
	|	| \ |   | LastConsensusRound for nodes 1, 2 and 3 because it is the last
    |   |   \   | Round that has all its witnesses decided
    |   |   | \ |
	|   |   |  w13
	|   |   | / |
	|   |  w12  |
    |   | / |   |
    |  w11  |   |
	|	| \ |   |-----------------
    |   |   \   | R0
    |   |   | \ |
    |   |   |  e32
    |   |   | / |
    |   |  e21  | All Events in Round 0 are Consensus Events.
    |   | / |   |
    |  e10  |   |
	| / |   |   |
   e0   e1  e2  e3
    0	1	2	3
*/
        private async Task InitFFHashgraph(Core[] cores)
        {
            var playbook = new[]
            {
                new Play(0, 1, new[] {"e10".StringToBytes()}),
                new Play(1, 2, new[] {"e21".StringToBytes()}),
                new Play(2, 3, new[] {"e32".StringToBytes()}),
                new Play(3, 1, new[] {"w11".StringToBytes()}),
                new Play(1, 2, new[] {"w12".StringToBytes()}),
                new Play(2, 3, new[] {"w13".StringToBytes()}),
                new Play(3, 1, new[] {"f13".StringToBytes()}),
                new Play(1, 2, new[] {"w22".StringToBytes()}),
                new Play(2, 3, new[] {"w23".StringToBytes()}),
                new Play(3, 1, new[] {"w21".StringToBytes()}),
                new Play(1, 2, new[] {"g21".StringToBytes()}),
                new Play(2, 3, new[] {"w33".StringToBytes()}),
                new Play(3, 2, new[] {"w32".StringToBytes()}),
                new Play(2, 1, new[] {"w31".StringToBytes()})
            };

            foreach (var play in playbook)
            {
                var err =await  SyncAndRunConsensus(cores, play.From, play.To, play.Payload);

                Assert.Null(err);
            }
        }

        [Fact]
        public async Task TestConsensusFF()
        {
            var (cores, _, _ ) =await  InitCores(4);
            await InitFFHashgraph(cores);

            var r = cores[0].GetLastConsensusRoundIndex();

            if (r != null)
            {
                output.WriteLine($"Cores[0] last consensus Round should be nil, not {r}");
                Assert.Null(r);
            }

            r = cores[1].GetLastConsensusRoundIndex();

            if (r == null || r != 1)
            {
                output.WriteLine($"Cores[1] last consensus Round should be 1, not {r.ToString() ?? "nill"}");
            }

            var l = cores[0].GetConsensusEvents().Length;

            Assert.Equal(0, l);

            l = cores[1].GetConsensusEvents().Length;

            Assert.Equal(7, l);

            var core1Consensus = cores[1].GetConsensusEvents();
            var core2Consensus = cores[2].GetConsensusEvents();
            var core3Consensus = cores[3].GetConsensusEvents();

            for (var i = 0; i < core1Consensus.Length; i++)
            {
                var e = core1Consensus[i];

                Assert.Equal(e, core2Consensus[i]); //Node 2 consensus[%d] does not match Node 1's
                Assert.Equal(e, core3Consensus[i]); //Node 3 consensus[%d] does not match Node 1's
            }
        }

        private async Task<Exception> SynchronizeCores(Core[] cores, int from, int to, byte[][] payload)
        {
            var knownByTo = await cores[to].KnownEvents();
            var ( unknownByTo, err) = await cores[from].EventDiff(knownByTo);
            if (err != null)
            {
                return err;
            }

            WireEvent[] unknownWire;
            ( unknownWire, err) = cores[from].ToWire(unknownByTo);
            if (err != null)
            {
                return err;
            }

            cores[to].AddTransactions(payload);

            //output.WriteLine($"FromId: {from}; To: {to}");
            //output.WriteLine(unknownWire.DumpToString());

            return await  cores[to].Sync(unknownWire);
        }

        private async Task<Exception> SyncAndRunConsensus(Core[] cores, int from, int to, byte[][] payload)
        {
            var err = await SynchronizeCores(cores, from, to, payload);

            if (err != null)
            {
                return err;
            }

            await cores[to].RunConsensus();
            return null;
        }

        private string GetName(Dictionary<string, string> index, string hash)
        {
            foreach (var i in index)
            {
                var name = i.Key;
                var h = i.Value;
                if (h == hash)
                {
                    return name;
                }
            }

            return $"{hash} not found";
        }
    }
}