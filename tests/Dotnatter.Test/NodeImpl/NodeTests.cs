using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Dotnatter.Crypto;
using Dotnatter.HashgraphImpl.Model;
using Dotnatter.HashgraphImpl.Stores;
using Dotnatter.NetImpl;
using Dotnatter.NetImpl.PeerImpl;
using Dotnatter.NetImpl.TransportImpl;
using Dotnatter.NodeImpl;
using Dotnatter.ProxyImpl;
using Dotnatter.Test.Helpers;
using Dotnatter.Util;
using Serilog;
using Xunit;
using Xunit.Abstractions;

namespace Dotnatter.Test.NodeImpl
{
    public class NodeTests
    {
        private readonly ITestOutputHelper output;
        private readonly ILogger logger;

        public NodeTests(ITestOutputHelper output)
        {
            this.output = output;
            logger = output.SetupLogging().ForContext("SourceContext", "HashGraphTests");
        }

        private const int PortStart = 9990;

        private static (CngKey[] keys, Peer[] peers, Dictionary<string, int> pmap) InitPeers(int n)
        {
            var port = PortStart;
            var keys = new List<CngKey>();
            var peers = new List<Peer>();

            int i = 0;
            for (i = 0; i < n; i++)
            {
                var key = CryptoUtils.GenerateEcdsaKey();
                keys.Add(key);
                peers.Add(new Peer
                (
                    $"127.0.0.1:{port}",
                    CryptoUtils.FromEcdsaPub(key).ToHex()
                ));
                port++;
            }

            peers.Sort((peer, peer1) => string.Compare(peer.PubKeyHex, peer1.PubKeyHex, StringComparison.Ordinal));
            var pmap = new Dictionary<string, int>();

            i = 0;
            foreach (var p in peers)
            {
                pmap[p.PubKeyHex] = i;
                i++;
            }

            return (keys.ToArray(), peers.ToArray(), pmap);
        }

        [Fact]
        public async Task TestProcessSync()
        {
            var (keys, peers, pmap) = InitPeers(2);

            var config = Config.TestConfig();

            //Start two nodes

            var router = new InMemRouter();

            var peer0Trans = await router.Register(peers[0].NetAddr);
            var node0 = new Node(config, pmap[peers[0].PubKeyHex], keys[0], peers, new InmemStore(pmap, config.CacheSize, logger), peer0Trans, new InMemAppProxy(logger), logger);
            await node0.Init(false);

            var node0Task = node0.RunAsync(false);

            var peer1Trans = await router.Register(peers[1].NetAddr);

            var node1 = new Node(config, pmap[peers[1].PubKeyHex], keys[1], peers, new InmemStore(pmap, config.CacheSize, logger), peer1Trans, new InMemAppProxy(logger), logger);
            await node1.Init(false);

            var node1Task = node1.RunAsync(false);

            //Manually prepare SyncRequest and expected SyncResponse

            var node0Known = await node0.Core.Known();

            var node1Known = await node1.Core.Known();

            Exception err;

            Event[] unknown;
            (unknown, err) = await node1.Core.Diff(node0Known);
            Assert.Null(err);

            WireEvent[] unknownWire;
            (unknownWire, err) = node1.Core.ToWire(unknown);
            Assert.Null(err);

            var args = new SyncRequest
            {
                From = node0.LocalAddr,
                Known = node0Known
            };

            var expectedResp = new SyncResponse
            {
                From = node1.LocalAddr,
                Events = unknownWire,
                Known = node1Known
            };

            //Make actual SyncRequest and check SyncResponse

            SyncResponse resp;
            (resp, err) = await peer0Trans.Sync(peers[1].NetAddr, args);
            Assert.Null(err);

            // Verify the response

            resp.ShouldCompareTo(expectedResp);

            Assert.Equal(expectedResp.Events.Length, resp.Events.Length);

            int i = 0;
            foreach (var e in expectedResp.Events)

            {
                var ex = resp.Events[i];
                e.Body.ShouldCompareTo(ex.Body);
                i++;
            }

            resp.Known.ShouldCompareTo(expectedResp.Known);

            // shutdown nodes

            node0.Shutdown();
            node1.Shutdown();
        }

        [Fact]
        public async Task TestProcessEagerSync()
        {
            var (keys, peers, pmap) = InitPeers(2);

            var config = Config.TestConfig();

            //Start two nodes

            var router = new InMemRouter();

            var peer0Trans = await router.Register(peers[0].NetAddr);
            var node0 = new Node(config, pmap[peers[0].PubKeyHex], keys[0], peers, new InmemStore(pmap, config.CacheSize, logger), peer0Trans, new InMemAppProxy(logger), logger);
            await node0.Init(false);

            var node0Task = node0.RunAsync(false);

            var peer1Trans = await router.Register(peers[1].NetAddr);

            var node1 = new Node(config, pmap[peers[1].PubKeyHex], keys[1], peers, new InmemStore(pmap, config.CacheSize, logger), peer1Trans, new InMemAppProxy(logger), logger);
            await node1.Init(false);

            var node1Task = node1.RunAsync(false);

            //Manually prepare EagerSyncRequest and expected EagerSyncResponse

            var node1Known = await node1.Core.Known();

            Event[] unknown;
            Exception err;
            (unknown, err) = await node0.Core.Diff(node1Known);
            Assert.Null(err);

            WireEvent[] unknownWire;
            (unknownWire, err) = node0.Core.ToWire(unknown);
            Assert.Null(err);

            var args = new EagerSyncRequest
            {
                From = node0.LocalAddr,
                Events = unknownWire
            };

            var expectedResp = new EagerSyncResponse
            {
                From = node1.LocalAddr,
                Success = true
            };

            //Make actual EagerSyncRequest and check EagerSyncResponse

            EagerSyncResponse resp;
            (resp, err) = await peer0Trans.EagerSync(peers[1].NetAddr, args);
            Assert.Null(err);

            // Verify the response
            Assert.Equal(expectedResp.Success, resp.Success);

            // shutdown nodes
            node0.Shutdown();
            node1.Shutdown();
        }

        [Fact]
        public async Task TestAddTransaction()
        {
            var (keys, peers, pmap) = InitPeers(2);

            var config = Config.TestConfig();

            //Start two nodes

            var router = new InMemRouter();

            var peer0Trans = await router.Register(peers[0].NetAddr);
            var peer0Proxy = new InMemAppProxy(logger);
            var node0 = new Node(config, pmap[peers[0].PubKeyHex], keys[0], peers, new InmemStore(pmap, config.CacheSize, logger), peer0Trans, peer0Proxy, logger);
            await node0.Init(false);

            var node0Task = node0.RunAsync(false);

            var peer1Trans = await router.Register(peers[1].NetAddr);
            var peer1Proxy = new InMemAppProxy(logger);
            var node1 = new Node(config, pmap[peers[1].PubKeyHex], keys[1], peers, new InmemStore(pmap, config.CacheSize, logger), peer1Trans, peer1Proxy, logger);
            await node1.Init(false);

            var node1Task = node1.RunAsync(false);

            //Submit a Tx to node0

            var message = "Hello World!";
            await peer0Proxy.SubmitTx(message.StringToBytes());

            //simulate a SyncRequest from node0 to node1

            var node0Known = await node0.Core.Known();
            var args = new SyncRequest
            {
                From = node0.LocalAddr,
                Known = node0Known
            };

            Exception err;
            SyncResponse resp;

            (resp, err) = await peer0Trans.Sync(peers[1].NetAddr, args);
            Assert.Null(err);

            err = await node0.Sync(resp.Events);
            Assert.Null(err);

            ////check the Tx was removed from the transactionPool and added to the new Head
            Assert.Empty(node0.Core.TransactionPool);

            var (node0Head, _) = await node0.Core.GetHead();
            Assert.Single(node0Head.Transactions());

            Assert.Equal(message, node0Head.Transactions()[0].BytesToString());

            node0.Shutdown();
            node1.Shutdown();
        }

        private static async Task<(CngKey[] keys, Node[] nodes)> InitNodes(int n, int cacheSize, int syncLimit, string storeType, ILogger logger)
        {
            var (keys, peers, pmap) = InitPeers(n);

            var nodes = new List<Node>();

            var proxies = new List<InMemAppProxy>();

            var router = new InMemRouter();

            for (var i = 0; i < peers.Length; i++)
            {
                var conf = new Config(TimeSpan.FromMilliseconds(5), TimeSpan.FromSeconds(1), cacheSize, syncLimit, storeType, $"test_data/db_{i}");

                var trans = await router.Register(peers[i].NetAddr);

                IStore store = null;
                switch (storeType)
                {
                    case "badger":
                        (store,_) = await LocalDbStore.New(pmap, conf.CacheSize, conf.StorePath, logger);
                        break;
                    case "inmem":
                        store = new InmemStore(pmap, conf.CacheSize, logger);
                        break;
                    default:
                        throw new NotImplementedException();
                }

                var proxy = new InMemAppProxy(logger);
                var node = new Node(conf, pmap[peers[i].PubKeyHex], keys[i], peers,
                    store,
                    trans,
                    proxy, logger);

                var err = await  node.Init(false);

                Assert.Null(err);

                nodes.Add(node);
                proxies.Add(proxy);
            }

            return (keys.ToArray(), nodes.ToArray());
        }

        private static  async Task<Node[]> RecycleNodes(Node[] oldNodes, ILogger logger)
        {
            var newNodes = new List<Node>();
            foreach (var oldNode in oldNodes)
            {
                var newNode = await RecycleNode(oldNode, logger);
                newNodes.Add(newNode);
            }

            return newNodes.ToArray();
        }

        private static async Task<Node> RecycleNode(Node oldNode, ILogger logger)
        {
            var conf = oldNode.Conf;
            var id = oldNode.Id;
            var key = oldNode.Core.Key;
            var peers = oldNode.PeerSelector.Peers();

            IStore store = null;
            if (oldNode.Store is InmemStore)
            {
                store = new InmemStore(oldNode.Store.Participants().participants.Clone(), conf.CacheSize, logger);
            }

            if (oldNode.Store is LocalDbStore)
            {
                //store = new LoadBadgerStore(conf.CacheSize, conf.StorePath);
            }

            Assert.NotNull(store);

            await oldNode.Trans.CloseAsync();

            var trans = await ((InMemRouterTransport) oldNode.Trans).Router.Register(oldNode.LocalAddr);

            var prox = new InMemAppProxy(logger);

            var newNode = new Node(conf, id, key, peers, store, trans, prox, logger);

            var err = newNode.Init(true);
            Assert.Null(err);

            return newNode;
        }

        private static void RunNodes(Node[] nodes, bool gossip)
        {
            foreach (var n in nodes)
            {
                var task = n.RunAsync(gossip);
            }
        }

        private static void ShutdownNodes(Node[] nodes)
        {
            foreach (var n in nodes)
            {
                n.Shutdown();
            }
        }

        private static void DeleteStores(Node[] nodes)
        {
            foreach (var n in nodes)
            {
                var di = new DirectoryInfo(n.Conf.StorePath);

                foreach (var file in di.GetFiles())
                {
                    file.Delete();
                }
            }
        }

        private static Task<(byte[][] txs, Exception err)> GetCommittedTransactions(Node n)
        {
            var inmemAppProxy = n.Proxy as InMemAppProxy;
            Assert.NotNull(inmemAppProxy);
            var res = inmemAppProxy.GetCommittedTransactions();
            return Task.FromResult<(byte[][], Exception)>((res, null));
        }

        [Fact]
        public async Task TestGossip()
        {
            var (keys, nodes) = await InitNodes(4, 1000, 1000, "inmem",logger);

            var err = await Gossip(nodes, 50, true, TimeSpan.FromSeconds(3));
            Assert.Null(err);

            await CheckGossip(nodes, logger);
        }

        [Fact]
        public async Task TestMissingNodeGossip()
        {

            var (keys,nodes) = await InitNodes(4, 1000, 1000, "inmem", logger);
            try
            {
                var err = await Gossip(nodes.Skip(1).ToArray(), 10, true, TimeSpan.FromSeconds(3));
                Assert.Null(err);
                await CheckGossip(nodes.Skip(1).ToArray(),logger);
            }
            finally
            {
                ShutdownNodes(nodes);
            }
            
        }


        [Fact]
        public async Task  TestSyncLimit()
        {

            var ( _, nodes) = await InitNodes(4, 1000, 300, "inmem", logger);

            var err = await Gossip(nodes, 10, false, TimeSpan.FromSeconds(3));
            Assert.Null(err);


            try
            {
                //create fake node[0] known to artificially reach SyncLimit
                var node0Known = await nodes[0].Core.Known();
                int k = 0;
                foreach (var kn in node0Known.ToList())
                {
                    node0Known[k] = 0;
                    k++;
                }

                var args = new SyncRequest
                {
                    From = nodes[0].LocalAddr,
                    Known = node0Known,
                };

                var expectedResp = new SyncResponse
                {
                    From = nodes[1].LocalAddr,
                    SyncLimit = true,
                };

                SyncResponse resp;
                (resp,err) =await nodes[0].Trans.Sync(nodes[1].LocalAddr, args);
                Assert.Null(err);

                // Verify the response

                Assert.Equal(expectedResp.From, resp.From);
                Assert.True(expectedResp.SyncLimit);
                
            }
            finally
            {
                ShutdownNodes(nodes);
            }
            
        }

        [Fact]
        public async Task  TestShutdown()
        {
            var (_, nodes) = await InitNodes(2, 1000, 1000, "inmem", logger);

            RunNodes(nodes, false);

            nodes[0].Shutdown();

            var err = nodes[1].Gossip(nodes[0].LocalAddr);
            Assert.NotNull(err);

            nodes[1].Shutdown();
        }

        [Fact(Skip = "Badger DB alternative not yet implmented!")]
        public async Task TestBootstrapAllNodes() 
        {

            //string path = Directory.GetCurrentDirectory();
            //DirectoryInfo attachments_AR = new DirectoryInfo(mappedPath1));
            //EmptyFolder(attachments_AR);
            //Directory.Delete(mappedPath1);
            //os.RemoveAll("test_data")
            //os.Mkdir("test_data", os.ModeDir|0777)

            //create a first network with BadgerStore and wait till it reaches 10 consensus
            //rounds before shutting it down
            var (_, nodes) =await InitNodes(4, 10000, 1000, "badger", logger);
            var err = await Gossip(nodes, 10, false, TimeSpan.FromSeconds(3));
            Assert.Null(err);

            await CheckGossip(nodes, logger);
            ShutdownNodes(nodes);

            //Now try to recreate a network from the databases created in the first step
            //and advance it to 20 consensus rounds
            var newNodes = await RecycleNodes(nodes, logger);
            err = await Gossip(newNodes, 20, false, TimeSpan.FromSeconds(3));
            Assert.Null(err);

            await CheckGossip(newNodes, logger);
            ShutdownNodes(newNodes);

            //Check that both networks did not have completely different consensus events
            await CheckGossip(new[] {nodes[0], newNodes[0]}, logger);
        }


        private static async Task<Exception> Gossip(Node[] nodes, int target, bool shutdown, TimeSpan timeout)
        {
            RunNodes(nodes, true);

            var err = await BombardAndWait(nodes, target, timeout);
            Assert.Null(err);

            if (shutdown)
            {
                ShutdownNodes(nodes);
            }

            return null;
        }

        private static async Task<Exception> BombardAndWait(Node[] nodes, int target, TimeSpan timeout)
        {
            var cts = new CancellationTokenSource();
            var mrtTask = MakeRandomTransactions(nodes, cts.Token);

            //wait until all nodes have at least 'target' rounds
            var stopper = Task.Delay(timeout, cts.Token);

            async Task Bombard()
            {
                while (!cts.IsCancellationRequested)
                {
                    await Task.Delay(10, cts.Token);

                    var done = true;
                    while (true)
                    {
                        foreach (var n in nodes)
                        {
                            var ce = n.Core.GetLastConsensusRoundIndex();
                            if (ce == null || ce < target) 
                            {
                                done = false;
                                break;
                            }
                        }

                        if (done)
                        {
                            break;
                        }
                    }
                }
            }

            await Task.WhenAny(stopper, Bombard(), mrtTask);
            //cts.Cancel();
            return null;
        }

        private static async Task CheckGossip(Node[] nodes, ILogger logger)
        {
            var consEvents = new Dictionary<int, string[]>();
            var consTransactions = new Dictionary<int, byte[][]>();
            foreach (var n in nodes)
            {
                logger.Debug(n.Id.ToString());

                consEvents[n.Id] = n.Core.GetConsensusEvents();

                var (nodeTxs, err) = await GetCommittedTransactions(n);

                consTransactions[n.Id] = nodeTxs;
            }


            var minE = consEvents.ContainsKey(0)?consEvents[0].Length:0;

            var minT = consTransactions.ContainsKey(0)?consTransactions[0].Length:0;

            for (var k = 1; k < nodes.Length; k++)
            {
                if (consEvents[k].Length < minE)
                {
                    minE = consEvents[k].Length;
                }

                if (consTransactions[k].Length < minT)
                {
                    minT = consTransactions[k].Length;
                }
            }

            var problem = false;

            logger.Debug($"min consensus events: {minE}");

            int i = 0;
            foreach (var e in consEvents.ContainsKey(0)?consEvents[0]:new string[]{}.Take(minE))
            {
                int j = 0;
                foreach (var jn in nodes.Skip(1))
                {
                    var f = consEvents[j][i];
                    if (f != e)
                    {
                        var er = nodes[0].Core.hg.Round(e);

                        var err = nodes[0].Core.hg.RoundReceived(e);

                        var fr = nodes[j].Core.hg.Round(f);

                        var frr = nodes[j].Core.hg.RoundReceived(f);

                        logger.Debug($"nodes[{j}].Consensus[{i}] ({e.Take(6)}, Round {er}, Received {err}) and nodes[0].Consensus[{i}] ({f.Take(6)}, Round {fr}, Received {frr}) are not equal");

                        problem = true;
                    }

                    j++;
                }

                i++;
            }

            Assert.False(problem);

            logger.Debug($"min consensus transactions: {minT}");

            i = 0;
            foreach (var tx in consTransactions.ContainsKey(0)?consTransactions[0]:new byte[][]{}.Take(minT))
            {
                foreach (var k in nodes.Skip(1))
                {
                    var ot = consTransactions[k.Id][i].BytesToString();

                    Assert.True(ot != tx.BytesToString(), $"nodes[{k}].ConsensusTransactions[{i}] should be '{tx.BytesToString()}' not '{ot}'");
                }
            }
        }

        private static async Task MakeRandomTransactions(Node[] nodes, CancellationToken ct)
        {
            var seq = new Dictionary<int, int>();
            while (!ct.IsCancellationRequested)
            {
                var rnd = new Random();
                var n = rnd.Next(0, nodes.Length);

                var node = nodes[n];
                await SubmitTransaction(node, $"node{n} transaction {seq[n]}".StringToBytes());
                seq[n] = seq[n] + 1;

                await Task.Delay(3, ct);
            }
        }

       private static async Task SubmitTransaction(Node n, byte[] tx)
        {
            var prox = n.Proxy as InMemAppProxy;
            Assert.NotNull(prox);

            await prox.SubmitTx(tx);
        }

    //    func BenchmarkGossip(b* testing.B)
    //    {
    //        logger:= common.NewBenchmarkLogger(b)
        
    //for n := 0; n < b.N; n++ {
    //            _, nodes:= initNodes(3, 1000, 1000, "inmem", logger, b)

    //    gossip(nodes, 5, true, 3 * time.Second)

    //}
    //    }
    }
}