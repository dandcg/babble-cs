using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Dotnatter.Crypto;
using Dotnatter.HashgraphImpl;
using Dotnatter.HashgraphImpl.Model;
using Dotnatter.NodeImpl;
using Dotnatter.Test.Helpers;
using Dotnatter.Util;
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
        public void TestInit()
        {
            var key = CryptoUtils.GenerateEcdsaKey();

            var participants = new Dictionary<string, int> {{CryptoUtils.FromEcdsaPub(key).ToHex(), 0}};
            var core = new Core(0, key, participants, new InmemStore(participants, 10, logger), null, logger);

            var err = core.Init();

            Assert.Null(err);
        }

        private (Core[] cores, CngKey[] privateKey, Dictionary<string, string> index) InitCores(int n)
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
                core.Init();
                cores.Add(core);

                index[$"e{i}"] = core.Head;
            }

            return (cores.ToArray(), participantKeys.ToArray(), index);
        }

        [Fact]
        public void TestInitCores()
        {
            var (cores, privateKey, index) = InitCores(3);
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
        private void InitHashgraph(Core[] cores, CngKey[] keys, Dictionary<string, string> index, int participant)

        {
            Exception err;
            for (var i = 0; i < cores.Length; i++)
            {
                if (i != participant)
                {
                    
                    var evh = index[$"e{i}"];

                    var ( ev, _) = cores[i].GetEvent(evh);

                    err = cores[participant].InsertEvent(ev, true);

                    if (err != null)
                    {
                        output.WriteLine("error inserting {0}: {1} ", GetName(index, ev.Hex()), err);
                    }
                }
            }

            var event01 = new Event(new byte[][] { },
                new[] {index["e0"], index["e1"]}, //e0 and e1
                cores[0].PubKey(), 1);

            err = InsertEvent(cores, keys, index, event01, "e01", participant, 0);
            if (err != null)
            {
                output.WriteLine("error inserting e01: {0}", err);
            }

            var event20 = new Event(new byte[][] { },
                new[] {index["e2"], index["e01"]}, //e2 and e01
                cores[2].PubKey(), 1);

            err = InsertEvent(cores, keys, index, event20, "e20", participant, 2);
            if (err != null)
            {
                output.WriteLine("error inserting e20: {0}", err);
            }

            var event12 = new Event(new byte[][] { },
                new[] {index["e1"], index["e20"]}, //e1 and e20
                cores[1].PubKey(), 1);

            err = InsertEvent(cores, keys, index, event12, "e12", participant, 1);

            if (err != null)
            {
                output.WriteLine("error inserting e12: {err}", err);
            }
        }

        public Exception InsertEvent(Core[] cores, CngKey[] keys, Dictionary<string, string> index, Event ev, string name, int particant, int creator)
        {
            Exception err;
            if (particant == creator)
            {
                err = cores[particant].SignAndInsertSelfEvent(ev);

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

                err = cores[particant].InsertEvent(ev, true);

                if (err != null)
                {
                    return err;
                }

                index[name] = ev.Hex();
            }

            return null;
        }

        [Fact]
        public void TestDiff()
        {
            var (cores, keys, index) = InitCores(3);

            InitHashgraph(cores, keys, index, 0);

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

            var knownBy1 = cores[1].Known();
            var (unknownBy1, err) = cores[0].Diff(knownBy1);

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





























        public string GetName(Dictionary<string, string> index, string hash)
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